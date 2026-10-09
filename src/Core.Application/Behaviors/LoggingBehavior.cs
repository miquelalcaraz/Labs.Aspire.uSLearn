using System.Diagnostics;

using MediatR;

using Microsoft.Extensions.Logging;

using uSLearn.Core.Application.Abstractions;
using uSLearn.Core.Application.Telemetry;

namespace uSLearn.Core.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs command/query execution with performance tracking.
/// Works with any request context (HTTP, messaging, background jobs) via IRequestContextAccessor.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IRequestContextAccessor? _contextAccessor;
    private const int SlowCommandThresholdMs = 500;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        IRequestContextAccessor? contextAccessor = null)
    {
        _logger = logger;
        _contextAccessor = contextAccessor;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var commandName = GetCommandName(request);
        var requestId = _contextAccessor?.RequestId ?? "N/A";
        var correlationId = _contextAccessor?.CorrelationId ?? "N/A";

        // Start distributed tracing activity
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            name: $"Command {commandName}",
            kind: ActivityKind.Internal);

        activity?.SetTag("command.name", commandName);
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("correlation.id", correlationId);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CommandName"] = commandName,
            ["RequestId"] = requestId,
            ["CorrelationId"] = correlationId
        });

        _logger.LogInformation(
            "Handling command {CommandName} with RequestId {RequestId}",
            commandName,
            requestId);

        var stopwatch = Stopwatch.StartNew();
        TResponse response;
        var success = false;

        try
        {
            response = await next(cancellationToken);
            stopwatch.Stop();
            success = true;

            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Record metrics
            ApplicationDiagnostics.CommandsProcessed.Add(1, 
                new KeyValuePair<string, object?>("command_type", commandName),
                new KeyValuePair<string, object?>("success", "true"));

            ApplicationDiagnostics.CommandDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("command_type", commandName),
                new KeyValuePair<string, object?>("success", "true"));

            activity?.SetTag("command.success", true);
            activity?.SetTag("command.duration_ms", elapsedMs);

            if (elapsedMs > SlowCommandThresholdMs)
            {
                activity?.SetTag("command.slow", true);
                _logger.LogWarning(
                    "Command {CommandName} took {ElapsedMilliseconds}ms (threshold: {ThresholdMs}ms) - RequestId: {RequestId}",
                    commandName,
                    elapsedMs,
                    SlowCommandThresholdMs,
                    requestId);
            }
            else
            {
                _logger.LogInformation(
                    "Command {CommandName} handled successfully in {ElapsedMilliseconds}ms - RequestId: {RequestId}",
                    commandName,
                    elapsedMs,
                    requestId);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            success = false;

            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // Record failure metrics
            ApplicationDiagnostics.CommandsProcessed.Add(1,
                new KeyValuePair<string, object?>("command_type", commandName),
                new KeyValuePair<string, object?>("success", "false"));

            ApplicationDiagnostics.CommandDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("command_type", commandName),
                new KeyValuePair<string, object?>("success", "false"));

            activity?.SetTag("command.success", false);
            activity?.SetTag("command.duration_ms", elapsedMs);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(
                ex,
                "Error handling command {CommandName} after {ElapsedMilliseconds}ms - RequestId: {RequestId}",
                commandName,
                elapsedMs,
                requestId);

            throw;
        }
    }

    private static string GetCommandName(TRequest request)
    {
        var type = request.GetType();
        
        if (type.IsGenericType)
        {
            var genericArgs = string.Join(",", type.GetGenericArguments().Select(t => t.Name));
            var typeName = type.Name[..type.Name.IndexOf('`')];
            return $"{typeName}<{genericArgs}>";
        }
        
        return type.Name;
    }
}
