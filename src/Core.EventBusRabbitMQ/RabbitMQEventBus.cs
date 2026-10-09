
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;
using Polly.Retry;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

using uSLearn.Core.Application.Telemetry;
using uSLearn.Core.EventBus.Abstractions;
using uSLearn.Core.EventBus.Events;

namespace uSLearn.Core.EventBusRabbitMQ;

public sealed class RabbitMQEventBus(
    ILogger<RabbitMQEventBus> logger,
    IServiceProvider serviceProvider,
    IOptions<EventBusOptions> options,
    IOptions<EventBusSubscriptionInfo> subscriptionOptions) : IEventBus, IDisposable, IHostedService
{
    private const string ExchangeName = "uslearn_event_bus";

    private readonly ResiliencePipeline _pipeline = CreateResiliencePipeline(options.Value.RetryCount);

    private readonly string _queueName = options.Value.SubscriptionClientName;
    private readonly EventBusSubscriptionInfo _subscriptionInfo = subscriptionOptions.Value;
    private IConnection _rabbitMQConnection;

    private IChannel _consumerChannel;

    public async Task PublishAsync(IntegrationEvent @event)
    {
        var routingKey = @event.GetType().Name;

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            name: $"Publish {routingKey}",
            kind: ActivityKind.Producer);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", ExchangeName);
        activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
        activity?.SetTag("event.id", @event.Id);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id, routingKey);
        }

        using var channel = (await _rabbitMQConnection?.CreateChannelAsync()) ?? throw new InvalidOperationException("RabbitMQ connection is not open");

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);
        }

        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: "direct");

        var body = SerializeMessage(@event);

        try
        {
            // ExecuteAsync (not Execute) so that asynchronous publish failures are retried too
            await _pipeline.ExecuteAsync(async _ =>
            {
                var properties = new BasicProperties()
                {
                    DeliveryMode = DeliveryModes.Persistent
                };

                InjectTraceContext(activity ?? Activity.Current, properties);

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);
                }

                await channel.BasicPublishAsync(
                    exchange: ExchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body);
            });

            ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
                new KeyValuePair<string, object?>("event_type", routingKey),
                new KeyValuePair<string, object?>("success", "true"));

            activity?.SetTag("messaging.success", true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish event to RabbitMQ after {RetryCount} retries: {EventId}", options.Value.RetryCount, @event.Id);

            ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
                new KeyValuePair<string, object?>("event_type", routingKey),
                new KeyValuePair<string, object?>("success", "false"));

            activity?.SetTag("messaging.success", false);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }



    public void Dispose()
    {
        _consumerChannel?.Dispose();
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs eventArgs)
    {

        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.Span);

        try
        {
            await ProcessEvent(eventName, message, eventArgs.BasicProperties);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error Processing message \"{Message}\"", message);

        }

        // Even on exception we take the message off the queue.
        // in a REAL WORLD app this should be handled with a Dead Letter Exchange (DLX). 
        // For more information see: https://www.rabbitmq.com/dlx.html
        await _consumerChannel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false);
    }

    private async Task ProcessEvent(string eventName, string message, IReadOnlyBasicProperties properties)
    {
        // Start distributed tracing activity (Consumer) as a child of the producer's trace, if propagated
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            $"Process {eventName}",
            ActivityKind.Consumer,
            ExtractTraceContext(properties));

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.source", ExchangeName);
        activity?.SetTag("messaging.rabbitmq.routing_key", eventName);
        activity?.SetTag("event.type", eventName);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);
        }

        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
            {
                logger.LogWarning("Unable to resolve event type for event name {EventName}", eventName);
                activity?.SetTag("event.resolved", false);
                return;
            }

            activity?.SetTag("event.resolved", true);

            // Deserialize the event
            var integrationEvent = DeserializeMessage(message, eventType);
            activity?.SetTag("event.id", integrationEvent.Id);

            // REVIEW: This could be done in parallel

            var handlerCount = 0;
            // Get all the handlers using the event type as the key
            foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
            {
                handlerCount++;
                await handler.Handle(integrationEvent);
            }

            activity?.SetTag("event.handler_count", handlerCount);

            // Record successful processing metric
            ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
                new KeyValuePair<string, object?>("event_type", eventName),
                new KeyValuePair<string, object?>("success", "true"));

            activity?.SetTag("messaging.success", true);

            logger.LogInformation("Successfully processed event {EventType} with {HandlerCount} handler(s)", eventName, handlerCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing event {EventType}", eventName);

            // Record failed processing metric
            ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
                new KeyValuePair<string, object?>("event_type", eventName),
                new KeyValuePair<string, object?>("success", "false"));

            activity?.SetTag("messaging.success", false);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            throw;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    private IntegrationEvent DeserializeMessage(string message, Type eventType)
    {
        return JsonSerializer.Deserialize(message, eventType, _subscriptionInfo.JsonSerializerOptions) as IntegrationEvent;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed .NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    private byte[] SerializeMessage(IntegrationEvent @event)
    {
        return JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), _subscriptionInfo.JsonSerializerOptions);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Messaging is async so we don't need to wait for it to complete.
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                logger.LogInformation("Starting RabbitMQ connection on a background thread");

                _rabbitMQConnection = serviceProvider.GetRequiredService<IConnection>();
                if (!_rabbitMQConnection.IsOpen)
                {
                    return;
                }

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Creating RabbitMQ consumer channel");
                }

                _consumerChannel = await _rabbitMQConnection.CreateChannelAsync();

                _consumerChannel.CallbackExceptionAsync += (sender, ea) =>
                {
                    logger.LogWarning(ea.Exception, "Error with RabbitMQ consumer channel");
                    return Task.CompletedTask;
                };

                await _consumerChannel.ExchangeDeclareAsync(
                    exchange: ExchangeName,
                    type: "direct");

                await _consumerChannel.QueueDeclareAsync(
                    queue: _queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Starting RabbitMQ basic consume");
                }

                var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

                consumer.ReceivedAsync += OnMessageReceived;

                await _consumerChannel.BasicConsumeAsync(
                    queue: _queueName,
                    autoAck: false,
                    consumer: consumer);

                foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
                {
                    await _consumerChannel.QueueBindAsync(
                        queue: _queueName,
                        exchange: ExchangeName,
                        routingKey: eventName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting RabbitMQ connection");
            }
        },
        TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // Writes the W3C trace context (traceparent/tracestate) into the message headers
    internal static void InjectTraceContext(Activity? activity, BasicProperties properties)
    {
        DistributedContextPropagator.Current.Inject(activity, properties, static (carrier, key, value) =>
        {
            var props = (BasicProperties)carrier!;
            props.Headers ??= new Dictionary<string, object?>();
            props.Headers[key] = value;
        });
    }

    // Reads the W3C trace context from the message headers (RabbitMQ delivers header values as byte[])
    internal static ActivityContext ExtractTraceContext(IReadOnlyBasicProperties properties)
    {
        DistributedContextPropagator.Current.ExtractTraceIdAndState(properties.Headers,
            static (object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) =>
            {
                fieldValues = null;
                fieldValue = null;

                if (carrier is IDictionary<string, object?> headers && headers.TryGetValue(fieldName, out var raw))
                {
                    fieldValue = raw is byte[] bytes ? Encoding.UTF8.GetString(bytes) : raw?.ToString();
                }
            },
            out var traceParent,
            out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context) ? context : default;
    }

    private static ResiliencePipeline CreateResiliencePipeline(int retryCount)
    {
        // See https://www.pollydocs.org/strategies/retry.html
        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<BrokerUnreachableException>().Handle<SocketException>(),
            MaxRetryAttempts = retryCount,
            DelayGenerator = (context) => ValueTask.FromResult(GenerateDelay(context.AttemptNumber))
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retryOptions)
            .Build();

        static TimeSpan? GenerateDelay(int attempt)
        {
            return TimeSpan.FromSeconds(Math.Pow(2, attempt));
        }
    }
}
