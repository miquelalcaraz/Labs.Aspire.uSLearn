# Stage.04-3 - Telemetry with OpenTelemetry

## 🎯 Stage Goal

Implement **complete, observable telemetry** with OpenTelemetry that provides:

- Distributed tracing with a custom `ActivitySource` for behaviors, transactions and events
- Custom metrics with a `Meter` (counters, histograms) for commands, validation and events
- Instrumentation of domain events and of integration events over RabbitMQ
- Trace context propagation through messaging (end-to-end distributed tracing)
- Support for several backends (Azure Monitor, Grafana, Elastic, Jaeger, etc.) without code changes

---

## 🏗️ Architectural Decisions

### Why OpenTelemetry?

OpenTelemetry is the **open standard** for observability, backed by the CNCF (Cloud Native Computing Foundation). Advantages:

- **Vendor-agnostic**: works with Azure Monitor, Grafana, Datadog, New Relic, Elastic, etc.
- **A single SDK**: one API for traces, metrics and logs
- **Standard protocol (OTLP)**: switch backends without touching code
- **Mature ecosystem**: automatic instrumentation for popular frameworks
- **Future-proof**: adopted as the standard by Microsoft, Google and AWS

### Why `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter`?

They are the **native .NET telemetry APIs**, part of the BCL (Base Class Library):

- ✅ **No external dependencies** (part of the runtime)
- ✅ **Optimized performance** (native .NET implementation)
- ✅ **Automatic integration with OpenTelemetry** (converted to OTLP spans/metrics)
- ✅ **W3C Trace Context standard** (`traceparent`/`tracestate`)
- ✅ **Recommended by Microsoft** as the official approach for .NET

**Discarded alternatives**:
- The OpenTelemetry SDK directly (`Tracer.StartActiveSpan`) → more verbose; `ActivitySource` is the preferred abstraction in .NET
- Manual logging with correlation IDs → reinventing the wheel, with no span hierarchy
- Vendor-specific SDKs (Application Insights SDK) → vendor lock-in, not portable

### Pattern: telemetry in the building blocks

**Key decision**: put telemetry in the **reusable building blocks** (Core.Application, Core.EventBusRabbitMQ) instead of in each microservice.

**Advantages**:
- ✅ **Automatic reuse**: Identity.API and future microservices inherit the telemetry with no configuration
- ✅ **Consistency**: the same metrics and traces across the solution
- ✅ **Single responsibility**: the building block that runs the logic is the one that observes it
- ✅ **DRY (Don't Repeat Yourself)**: one place to maintain

**Example**: `RabbitMQEventBus.PublishAsync()` records metrics because:
- It is the component that ACTUALLY publishes the message
- It can be called directly, without `AccountIntegrationEventService`
- Every microservice that uses `RabbitMQEventBus` gets the telemetry for free

---

## 📦 Implemented Structure

```
src/
├── Core.Application/
│   ├── Telemetry/
│   │   └── ApplicationDiagnostics.cs ← NEW (ActivitySource, Meter, metrics)
│   └── Behaviors/
│       ├── LoggingBehavior.cs (telemetry added)
│       └── ValidationBehavior.cs (telemetry added)
│
├── Core.EventBusRabbitMQ/
│   ├── RabbitMQEventBus.cs (producer/consumer telemetry and trace context propagation)
│   └── Core.EventBusRabbitMQ.csproj (references Core.Application)
│
├── uSLearn.Accounts.API/
│   ├── Application/
│   │   └── Behaviors/
│   │       └── TransactionBehavior.cs (telemetry added)
│   └── Infrastructure/
│       └── Extensions/
│           └── MediatorExtension.cs (updated - domain event metrics)
│
└── uSLearn.ServiceDefaults/
    └── Extensions.cs (updated - registers the custom ActivitySource and Meter)
```

---

## 🔧 Implemented Components

### 1. `ApplicationDiagnostics` (central telemetry hub)

**Location**: `Core.Application/Telemetry/ApplicationDiagnostics.cs`

**Purpose**: the single place where the application's custom telemetry is defined.

**Components**:

```csharp
public static class ApplicationDiagnostics
{
    public const string SourceName = "uSLearn.Core.Application";
    public const string Version = "1.0.0";

    // ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    // Meter for custom metrics
    public static readonly Meter Meter = new(SourceName, Version);

    // === Counters (cumulative) ===
    
    // Total commands/queries processed (tags: command_type, success)
    public static readonly Counter<long> CommandsProcessed;
    
    // Total failed validations (tags: command_type, error_count)
    public static readonly Counter<long> ValidationFailures;
    
    // Total domain events published (tags: event_type)
    public static readonly Counter<long> DomainEventsPublished;
    
    // Total integration events published/received (tags: event_type, success)
    public static readonly Counter<long> IntegrationEventsPublished;
    public static readonly Counter<long> IntegrationEventsReceived;

    // === Histograms (statistical distributions) ===
    
    // Command duration in ms (tags: command_type, success) - for p50, p95, p99
    public static readonly Histogram<double> CommandDuration;
    
    // Validation duration in ms (tags: command_type, validator_count)
    public static readonly Histogram<double> ValidationDuration;
    
    // DB transaction duration in ms (tags: command_type, success)
    public static readonly Histogram<double> TransactionDuration;
    
    // Number of validation errors per request (tags: command_type)
    public static readonly Histogram<int> ValidationErrorCount;
}
```

**Characteristics**:
- Consistent name: `"uSLearn.Core.Application"` (used by both the ActivitySource and the Meter)
- Semantic metrics following the OpenTelemetry semantic conventions
- Explicit units: `{command}`, `{event}`, `ms`, `{error}`

---

### 2. `LoggingBehavior<TRequest, TResponse>` (instrumented)

**Location**: `Core.Application/Behaviors/LoggingBehavior.cs`

**Added telemetry**:

```csharp
public async Task<TResponse> Handle(...)
{
    var commandName = GetCommandName(request);

    // ✅ Distributed tracing activity
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Command {commandName}",
        kind: ActivityKind.Internal);

    activity?.SetTag("command.name", commandName);
    activity?.SetTag("request.id", requestId);
    activity?.SetTag("correlation.id", correlationId);

    var stopwatch = Stopwatch.StartNew();

    try
    {
        response = await next(cancellationToken);
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;

        // ✅ Success metrics
        ApplicationDiagnostics.CommandsProcessed.Add(1, 
            new("command_type", commandName),
            new("success", "true"));

        ApplicationDiagnostics.CommandDuration.Record(elapsedMs,
            new("command_type", commandName),
            new("success", "true"));

        activity?.SetTag("command.success", true);
        activity?.SetTag("command.duration_ms", elapsedMs);
        activity?.SetTag("command.slow", elapsedMs > SlowCommandThresholdMs);

        return response;
    }
    catch (Exception ex)
    {
        // ✅ Failure metrics
        ApplicationDiagnostics.CommandsProcessed.Add(1, new("command_type", commandName), new("success", "false"));
        ApplicationDiagnostics.CommandDuration.Record(elapsedMs, new("command_type", commandName), new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("error.type", ex.GetType().FullName);

        throw;
    }
}
```

**Activity tags**:
- `command.name`: command name (e.g. `"CreateOrganizationCommand"`)
- `request.id`: unique request ID (idempotency)
- `correlation.id`: correlation ID (distributed tracing)
- `command.success`: `true`/`false`
- `command.duration_ms`: duration in milliseconds
- `command.slow`: `true` if it exceeds the threshold (500ms)
- `error.type`: exception type on failure

---

### 3. `ValidationBehavior<TRequest, TResponse>` (instrumented)

**Location**: `Core.Application/Behaviors/ValidationBehavior.cs`

**Added telemetry**:

```csharp
public async Task<TResponse> Handle(...)
{
    if (!_validators.Any())
        return await next(cancellationToken);

    var commandName = typeof(TRequest).Name;
    var validatorCount = _validators.Count();

    // ✅ Validation activity
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Validation {commandName}",
        kind: ActivityKind.Internal);

    activity?.SetTag("validation.command", commandName);
    activity?.SetTag("validation.validator_count", validatorCount);

    var stopwatch = Stopwatch.StartNew();
    var validationResults = await Task.WhenAll(...);
    stopwatch.Stop();

    var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

    // ✅ Validation duration metric
    ApplicationDiagnostics.ValidationDuration.Record(elapsedMs,
        new("command_type", commandName),
        new("validator_count", validatorCount));

    if (failures.Any())
    {
        var errorCount = failures.Count;

        // ✅ Failure metrics
        ApplicationDiagnostics.ValidationFailures.Add(1,
            new("command_type", commandName),
            new("error_count", errorCount));

        ApplicationDiagnostics.ValidationErrorCount.Record(errorCount,
            new("command_type", commandName));

        activity?.SetTag("validation.failed", true);
        activity?.SetTag("validation.error_count", errorCount);
        activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
        
        // ✅ Event with the error details
        activity?.AddEvent(new ActivityEvent("ValidationFailed", 
            tags: new ActivityTagsCollection {
                { "errors", string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}")) }
            }));

        throw new Exceptions.ValidationException(failures);
    }

    activity?.SetTag("validation.success", true);
    return await next(cancellationToken);
}
```

**Characteristics**:
- Activity nested inside the command activity (hierarchy visible in traces)
- No activity when the request has no validators (e.g. the `IdentifiedCommand` wrapper)
- Duration histogram for performance analysis
- Failure counter for alerting
- `ActivityEvent` with the error details (avoids polluting tags)

---

### 4. `TransactionBehavior<TRequest, TResponse>` (instrumented)

**Location**: `uSLearn.Accounts.API/Application/Behaviors/TransactionBehavior.cs`

**Added telemetry** (simplified; the real behavior also resets the transaction and the `ChangeTracker` between retries, and records the attempt number in the `transaction.attempt` tag):

```csharp
public async Task<TResponse> Handle(...)
{
    var typeName = request.GetGenericTypeName();

    // ✅ Transaction activity
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Transaction {typeName}",
        kind: ActivityKind.Internal);

    activity?.SetTag("transaction.command", typeName);

    try
    {
        if (_dbContext.HasActiveTransaction)
        {
            activity?.SetTag("transaction.nested", true);
            return await next(cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.BeginTransactionAsync();
            activity?.SetTag("transaction.id", transaction.TransactionId);

            response = await next(cancellationToken);
            await _dbContext.CommitTransactionAsync(transaction);
        });

        // Publish outside the execution strategy: a transient failure here must not re-run the committed command
        await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

        stopwatch.Stop();
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        // ✅ Transaction duration metric
        ApplicationDiagnostics.TransactionDuration.Record(elapsedMs,
            new("command_type", typeName),
            new("success", "true"));

        activity?.SetTag("transaction.success", true);
        activity?.SetTag("transaction.duration_ms", elapsedMs);

        return response;
    }
    catch (Exception ex)
    {
        // ✅ Failure metric
        ApplicationDiagnostics.TransactionDuration.Record(0,
            new("command_type", typeName),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Transaction-specific tags**:
- `transaction.id`: GUID of the DB transaction
- `transaction.attempt`: attempt number within the execution strategy
- `transaction.nested`: `true` if a transaction was already active
- `transaction.duration_ms`: total duration (including event publishing)

---

### 5. `RabbitMQEventBus` (instrumented building block) ⭐

**Location**: `Core.EventBusRabbitMQ/RabbitMQEventBus.cs`

**Key improvement**: telemetry lives in the building block rather than in `AccountIntegrationEventService`, so **every** publish is monitored.

#### PublishAsync (producer)

```csharp
public async Task PublishAsync(IntegrationEvent @event)
{
    var routingKey = @event.GetType().Name;

    // ✅ Producer activity (distributed tracing)
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Publish {routingKey}",
        kind: ActivityKind.Producer); // ← Producer for messaging

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.destination", ExchangeName);
    activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
    activity?.SetTag("event.id", @event.Id);

    try
    {
        using var channel = await _rabbitMQConnection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: "direct");

        var body = SerializeMessage(@event);

        // ExecuteAsync (not Execute) so that asynchronous publish failures are retried too
        await _pipeline.ExecuteAsync(async _ =>
        {
            var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };

            // ✅ Inject the trace context into the headers (W3C traceparent/tracestate)
            InjectTraceContext(activity ?? Activity.Current, properties);

            await channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);
        });

        // ✅ Successful publish metric
        ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
            new("event_type", routingKey),
            new("success", "true"));

        activity?.SetTag("messaging.success", true);
    }
    catch (Exception ex)
    {
        // ✅ Failed publish metric
        ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
            new("event_type", routingKey),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Key characteristics**:
- `ActivityKind.Producer`: identifies the activity as a message producer
- **Trace context injection** into the RabbitMQ headers (`traceparent`, `tracestate`) with .NET's `DistributedContextPropagator`, no extra dependencies:

```csharp
internal static void InjectTraceContext(Activity? activity, BasicProperties properties)
{
    DistributedContextPropagator.Current.Inject(activity, properties, static (carrier, key, value) =>
    {
        var props = (BasicProperties)carrier!;
        props.Headers ??= new Dictionary<string, object?>();
        props.Headers[key] = value;
    });
}
```
- Semantic messaging tags (OpenTelemetry messaging semantic conventions)
- Success/failure metrics for SLOs

#### ProcessEvent (consumer)

```csharp
private async Task ProcessEvent(string eventName, string message, IReadOnlyBasicProperties properties)
{
    // ✅ Consumer activity, child of the producer's trace (context read from the headers)
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        $"Process {eventName}",
        ActivityKind.Consumer,
        ExtractTraceContext(properties)); // ← RabbitMQ delivers header values as byte[]

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.source", ExchangeName);
    activity?.SetTag("messaging.rabbitmq.routing_key", eventName);
    activity?.SetTag("event.type", eventName);

    try
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
        {
            activity?.SetTag("event.resolved", false);
            return;
        }

        activity?.SetTag("event.resolved", true);

        var integrationEvent = DeserializeMessage(message, eventType);
        activity?.SetTag("event.id", integrationEvent.Id);

        var handlerCount = 0;
        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
        {
            handlerCount++;
            await handler.Handle(integrationEvent);
        }

        activity?.SetTag("event.handler_count", handlerCount);

        // ✅ Successful receive metric
        ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
            new("event_type", eventName),
            new("success", "true"));

        activity?.SetTag("messaging.success", true);
    }
    catch (Exception ex)
    {
        // ✅ Failed receive metric
        ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
            new("event_type", eventName),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Benefits of distributed tracing over messaging**:
- Full trace: `HTTP request → command → transaction → publish event → consume event → handler`
- The trace ID travels in the message headers
- End-to-end view in the Aspire dashboard / Grafana / Azure Monitor

---

### 6. `MediatorExtension.DispatchDomainEventsAsync` (instrumented)

**Location**: `uSLearn.Accounts.API/Infrastructure/Extensions/MediatorExtension.cs`

**Added telemetry**:

```csharp
public static async Task DispatchDomainEventsAsync(this IMediator mediator, AccountContext ctx)
{
    var domainEntities = ctx.ChangeTracker
        .Entries<Entity>()
        .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any());

    var domainEvents = domainEntities.SelectMany(x => x.Entity.DomainEvents).ToList();
    domainEntities.ToList().ForEach(entity => entity.Entity.ClearDomainEvents());

    foreach (var domainEvent in domainEvents)
    {
        var eventType = domainEvent.GetType().Name;

        // ✅ Domain event published metric
        ApplicationDiagnostics.DomainEventsPublished.Add(1,
            new("event_type", eventType));

        await mediator.Publish(domainEvent);
    }
}
```

**Note**: domain events stay in-process (they don't cross boundaries), so only metrics are recorded (no activities).

---

### 7. Registration in ServiceDefaults

**Location**: `uSLearn.ServiceDefaults/Extensions.cs`

**OpenTelemetry configuration**:

```csharp
public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) 
    where TBuilder : IHostApplicationBuilder
{
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
    });

    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation()     // ← HTTP server metrics
                .AddHttpClientInstrumentation()        // ← HTTP client metrics
                .AddRuntimeInstrumentation()           // ← .NET runtime metrics (GC, threads, etc.)
                .AddMeter("uSLearn.Core.Application"); // ← OUR custom metrics ✅
        })
        .WithTracing(tracing =>
        {
            tracing.AddSource(builder.Environment.ApplicationName) // ← Service-specific traces
                .AddSource("uSLearn.Core.Application")             // ← OUR custom traces ✅
                .AddAspNetCoreInstrumentation(tracing =>
                    tracing.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                )
                .AddHttpClientInstrumentation(); // ← HTTP client tracing
        });

    builder.AddOpenTelemetryExporters();

    return builder;
}

private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) 
    where TBuilder : IHostApplicationBuilder
{
    var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    if (useOtlpExporter)
    {
        builder.Services.AddOpenTelemetry().UseOtlpExporter(); // ← OTLP for logs, metrics, traces ✅
    }

    // Optional: Azure Monitor for extra features (Live Metrics, Smart Detection)
    // if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    // {
    //     builder.Services.AddOpenTelemetry().UseAzureMonitor();
    // }

    return builder;
}
```

**Key configuration**:
- `.AddMeter("uSLearn.Core.Application")`: exports OUR custom metrics
- `.AddSource("uSLearn.Core.Application")`: exports OUR custom traces
- `.UseOtlpExporter()`: exports everything (logs, metrics, traces) to any OTLP-compatible backend

---

## 📊 Available Metrics and Traces

### **Metrics**

| Name | Type | Description | Tags |
|--------|------|-------------|------|
| `application.commands.processed` | Counter | Total commands/queries processed | `command_type`, `success` |
| `application.commands.duration` | Histogram | Command duration in ms (p50, p95, p99) | `command_type`, `success` |
| `application.validation.failures` | Counter | Total failed validations | `command_type`, `error_count` |
| `application.validation.duration` | Histogram | Validation duration in ms | `command_type`, `validator_count` |
| `application.validation.error_count` | Histogram | Validation errors per request | `command_type` |
| `application.transactions.duration` | Histogram | DB transaction duration in ms | `command_type`, `success` |
| `application.domain_events.published` | Counter | Total domain events published | `event_type` |
| `application.integration_events.published` | Counter | Total integration events published | `event_type`, `success` |
| `application.integration_events.received` | Counter | Total integration events received | `event_type`, `success` |

**Automatic metrics** (built-in instrumentation):
- `http.server.request.duration` (ASP.NET Core)
- `http.client.request.duration` (HttpClient)
- .NET runtime metrics (GC, thread pool, etc.)

---

### **Traces (distributed tracing)**

Expected structure of the trace for `PUT /api/accounts`. The endpoint sends an `IdentifiedCommand` that wraps the business command, so the behaviors appear twice:

```
HTTP PUT /api/accounts                                       ← ASP.NET Core (auto)        [apiservice]
└─ Command IdentifiedCommand<CreateOrganizationCommand, ...>  ← LoggingBehavior
   └─ Transaction IdentifiedCommand<...>                      ← TransactionBehavior (opens the transaction)
      ├─ Command CreateOrganizationCommand                     ← LoggingBehavior
      │  ├─ Validation CreateOrganizationCommand               ← ValidationBehavior
      │  └─ Transaction CreateOrganizationCommand              ← TransactionBehavior (transaction.nested = true)
      └─ Publish OrganizationCreatedIntegrationEvent           ← RabbitMQEventBus (Producer)
         │  Headers: traceparent, tracestate                   ← W3C Trace Context
         └─ Process OrganizationCreatedIntegrationEvent        ← RabbitMQEventBus (Consumer)   [identity]
```

**Characteristics**:
- **Automatic hierarchy**: nested activities (parent-child relationships)
- **Trace propagation**: the same trace ID crosses HTTP → commands → RabbitMQ → consumers
- **Appropriate ActivityKind**: Internal, Server, Producer, Consumer
- **Semantic tags**: following the OpenTelemetry semantic conventions
- **No SQL spans**: EF Core/SqlClient instrumentation isn't enabled (see optional improvements)

---

## 🌐 Configuration for Different Backends

The current setup uses **OTLP (OpenTelemetry Protocol)**, an open standard supported by all major backends. **Switching backends requires no code changes**, only configuration.

### **Azure Monitor / Application Insights**

Use the Azure Monitor OpenTelemetry distro (or route OTLP through an OpenTelemetry Collector with the `azuremonitor` exporter, see the hybrid setup below).

**Uncomment in `Extensions.cs`**:
```csharp
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
       .UseAzureMonitor(); // ← Features: Live Metrics, Smart Detection, Application Map
}
```

**In `appsettings.json` or the Azure App Service configuration**:
```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=xxx;IngestionEndpoint=https://..."
}
```

**Required package**:
```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" />
```

**Exclusive features**:
- **Live Metrics**: real-time telemetry (latency, failures, requests/sec)
- **Smart Detection**: automatic anomaly alerts (error spikes, performance degradation)
- **Application Map**: visual topology of dependencies between services
- **KQL queries**: advanced queries in Log Analytics

**When to use it**:
- ✅ Production on Azure (native integration with the Azure portal)
- ✅ You need the advanced Application Insights features
- ❌ Multi-cloud, or you want to avoid vendor lock-in → use plain OTLP

---

### **Grafana Cloud / Grafana stack**

**Environment variables**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-prod-eu-west-0.grafana.net/otlp
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic <base64(instance_id:token)>
```

**Current code**: ✅ works without changes.

**Automatic mapping**:
- Traces → **Grafana Tempo**
- Metrics → **Prometheus / Mimir**
- Logs → **Grafana Loki**

**Getting credentials**:
1. Grafana Cloud → Stack → OTLP
2. Copy the instance ID and generate an access token
3. Base64-encode them: `echo -n "instance_id:token" | base64`

**Recommended dashboard**: [OpenTelemetry APM Dashboard](https://grafana.com/grafana/dashboards/19419)

---

### **Elastic Stack (Kibana + Elasticsearch)**

**Environment variables**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://your-elastic-apm.elastic-cloud.com:443
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer <secret-token>
```

**Current code**: ✅ works (Elastic APM supports OTLP since v8.0).

**Result**:
- Traces → **APM UI** in Kibana
- Metrics → **Elasticsearch** (visualized in Kibana dashboards)
- Logs → **Elasticsearch** (searchable in Kibana Discover)

**Getting a token**:
```sh
# In Elastic Cloud
Observability → APM → Add APM integration → OTLP → Generate secret token
```

---

### **Hybrid setup (several backends)**

To send telemetry to **several destinations at once** (e.g. Azure Monitor + Grafana):

#### Deploy an OTLP Collector

**Collector config** (`otel-collector-config.yaml`):
```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

processors:
  batch:
    timeout: 10s

exporters:
  # Azure Monitor
  azuremonitor:
    connection_string: "${APPLICATIONINSIGHTS_CONNECTION_STRING}"
  
  # Grafana Cloud
  otlp/grafana:
    endpoint: "otlp-gateway-prod-eu-west-0.grafana.net:443"
    headers:
      authorization: "Basic ${GRAFANA_TOKEN}"
  
  # Jaeger (local)
  otlp/jaeger:
    endpoint: "jaeger:4317"
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuremonitor, otlp/grafana, otlp/jaeger]
    
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuremonitor, otlp/grafana]
    
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [azuremonitor, otlp/grafana]
```

**Deploy on Azure Container Apps**:
```sh
az containerapp create \
  --name otel-collector \
  --resource-group rg-uslearn \
  --environment env-uslearn \
  --image otel/opentelemetry-collector-contrib:latest \
  --target-port 4317 \
  --ingress external \
  --env-vars \
    APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=xxx..." \
    GRAFANA_TOKEN="<base64-token>"
```

**Point the application at the collector**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://otel-collector.azurecontainerapps.io:4317
```

**Advantages**:
- ✅ Send to several backends without changing the app
- ✅ Centralized sampling (lower costs)
- ✅ Data transformations (filtering, enrichment)
- ✅ Switch backends without redeploying the apps

---

## ✅ Practical Verification

### 1. **Run the application**

```sh
dotnet run --project src/uSLearn.AppHost
```

The Aspire dashboard URL is printed in the console.

---

### 2. **Generate activity**

**Invalid request (validation fails)**:
```sh
curl -X PUT "https://localhost:7375/api/accounts?api-version=1.0" \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"name":"","legalName":"","taxIdNumber":"","country":"","zipCode":""}'
```

**Valid request**:
```sh
curl -X PUT "https://localhost:7375/api/accounts?api-version=1.0" \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corp",
    "legalName": "Acme Corporation LLC",
    "taxIdNumber": "12-3456789",
    "taxNumberType": "Ein",
    "country": "United States",
    "zipCode": "94105",
    "city": "San Francisco",
    "state": "CA",
    "street": "123 Market St",
    "organizationType": "Company"
  }'
```

---

### 3. **Check the traces in the Aspire dashboard**

**Navigation**: dashboard → **Traces** → filter by `apiservice`

**Check**:
- ✅ Activities nested as in the [expected structure](#traces-distributed-tracing)
- ✅ Tags visible (`command.name`, `validation.success`, `messaging.system`, etc.)
- ✅ Accurate durations
- ✅ The same trace ID in the producer (`apiservice`) and the consumer (`identity`)

---

### 4. **Check the metrics in the Aspire dashboard**

**Navigation**: dashboard → **Metrics** → select `apiservice`

**Expected metrics**:

| Metric | Expected value |
|---------|----------------|
| `application.commands.processed` (success=true) | 1+ |
| `application.commands.duration` (p95) | < 500ms (ideally) |
| `application.validation.duration` (p50) | < 10ms (ideally) |
| `application.validation.failures` | 1+ (if you sent the invalid request) |
| `application.transactions.duration` (p95) | < 300ms (depends on the DB) |
| `application.integration_events.published` | 1+ (once an event is published) |
| `application.integration_events.received` | 1+ (in `identity`, once the event is consumed) |

**Check**:
- ✅ Counters increase with each request
- ✅ Histograms show the distribution (p50, p95, p99)
- ✅ Tags allow filtering by `command_type`, `event_type`, `success`

---

### 5. **Check trace context propagation (RabbitMQ)**

**Tool**: Aspire dashboard → **Traces**

**Action**: create an organization (`PUT /api/accounts`) and open the request's trace.

**Check**:
- ✅ A single trace contains `PUT /api/accounts` (apiservice) → `Publish OrganizationCreatedIntegrationEvent` → `Process OrganizationCreatedIntegrationEvent` (identity)
- ✅ The `Process ...` span is a child of the `Publish ...` span: the `traceparent` travelled in the message headers

> The AppHost doesn't enable the RabbitMQ management plugin; to inspect the messages, add `.WithManagementPlugin()` to `AddRabbitMQ("eventbus")`.

---

## 🔄 Reuse in Other Microservices

To use telemetry in `Identity.API` or future microservices:

### 1. **It already works** (when using the building blocks)

If Identity.API uses:
- ✅ `LoggingBehavior`, `ValidationBehavior` → automatic telemetry
- ✅ `RabbitMQEventBus` → automatic event telemetry
- ✅ `ServiceDefaults` → OpenTelemetry already configured

**Nothing else to do.** 🎉

---

### 2. **Service-specific behaviors**

If Identity.API has a custom behavior (e.g. a hypothetical `AuthenticationBehavior`):

```csharp
public class AuthenticationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var commandName = typeof(TRequest).Name;

        // ✅ Use ApplicationDiagnostics
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            name: $"Authentication {commandName}",
            kind: ActivityKind.Internal);

        activity?.SetTag("authentication.command", commandName);

        // ... authentication logic ...

        // ✅ Record a metric (optional)
        // ApplicationDiagnostics.Meter.CreateCounter<long>("identity.authentication.attempts")
        //     .Add(1, new("command_type", commandName), new("success", success));

        return await next(cancellationToken);
    }
}
```

**No need to create a new ActivitySource**: reuse `ApplicationDiagnostics`.

---

### 3. **Domain-specific metrics**

If Identity.API needs specific metrics (e.g. login attempts):

**Option A: add them to `ApplicationDiagnostics.cs`** (if they are shared)
```csharp
// In ApplicationDiagnostics.cs
public static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>(
    name: "identity.login.attempts",
    unit: "{attempt}",
    description: "Total login attempts (tags: success, provider)");
```

**Option B: create `IdentityDiagnostics.cs`** (if they are very specific)
```csharp
// Identity.API/Telemetry/IdentityDiagnostics.cs
public static class IdentityDiagnostics
{
    public const string SourceName = "uSLearn.Identity.API";
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>(
        "identity.login.attempts", "{attempt}", "Login attempts");
}

// In ServiceDefaults/Extensions.cs
.AddMeter("uSLearn.Core.Application")
.AddMeter("uSLearn.Identity.API") // ← Add the new meter
```

**Recommendation**: option A for shared metrics, option B for very domain-specific ones.

---

## 🎯 Benefits

### 1. **Full observability (three pillars)**
- ✅ **Traces**: end-to-end distributed tracing (HTTP → commands → messaging)
- ✅ **Metrics**: counters and histograms for SLOs/SLAs
- ✅ **Logs**: structured logging with correlation IDs (from Stage.04-1)

### 2. **Vendor-agnostic (portability)**
- ✅ Works with Azure Monitor, Grafana, Elastic, Datadog, Jaeger, New Relic, Honeycomb
- ✅ Switch backends without touching code (configuration only)
- ✅ Send to several backends at once (through an OTLP Collector)

### 3. **Performance insights**
- ✅ Detect slow commands (threshold: 500ms)
- ✅ Analyze the latency distribution (p50, p95, p99)
- ✅ Find bottlenecks (validation vs transaction vs messaging)

### 4. **Reliability monitoring**
- ✅ Success/failure metrics for alerting
- ✅ Failed validation rate
- ✅ Rate of events published/received successfully

### 5. **Effective troubleshooting**
- ✅ The full trace of a problematic request in seconds
- ✅ Visual hierarchy of operations (nested spans)
- ✅ Contextual tags (command names, event types, error types)
- ✅ Activity events with extra details (e.g. validation errors)

### 6. **Real distributed tracing**
- ✅ Trace context propagated through RabbitMQ (W3C standard)
- ✅ The same trace ID across boundaries (HTTP → messaging → consumers)
- ✅ End-to-end view of asynchronous flows

---

## 📚 References

- [OpenTelemetry .NET documentation](https://opentelemetry.io/docs/languages/net/)
- [System.Diagnostics.ActivitySource](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
- [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics)
- [OpenTelemetry semantic conventions](https://opentelemetry.io/docs/specs/semconv/)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [.NET Aspire telemetry](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable)

---

## 🎯 Next Steps

### Completed in Stage.04:
- ✅ **Stage.04-1**: Enriched logging with structured logging and performance tracking
- ✅ **Stage.04-2**: Validation with FluentValidation and exception handling
- ✅ **Stage.04-3**: Telemetry with OpenTelemetry (traces, metrics, distributed tracing)

### **Stage.05**: Identity with Duende IdentityServer
- Authentication with OAuth 2.0 / OpenID Connect
- User and role management
- Token-based authentication for the microservices
- Integration with Accounts.API

### **Optional telemetry improvements**:
- **SQL spans**: enable EF Core / SqlClient instrumentation to see database calls in the traces
- **Configurable samplers**: reduce overhead in production (e.g. 10% of traces)
- **Custom exporters**: send specific metrics to Prometheus/StatsD
- **Exemplars**: link metrics → traces (click a metric → see a specific trace)
- **Baggage**: propagate custom metadata through distributed traces
- **Resource attributes**: add deployment metadata (version, environment, region)
