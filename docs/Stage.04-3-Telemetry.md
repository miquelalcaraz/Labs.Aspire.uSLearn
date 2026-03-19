# Stage.04-3 - Telemetría con OpenTelemetry

## 🎯 Objetivo de la Etapa

Implementar **telemetría completa y observable** con OpenTelemetry que proporcione:

- Distributed tracing con `ActivitySource` personalizado para behaviors, transacciones y eventos
- Métricas personalizadas con `Meter` (counters, histograms) para comandos, validaciones y eventos
- Instrumentación de eventos de dominio e integración con RabbitMQ
- Propagación de trace context en mensajería (distributed tracing end-to-end)
- Compatibilidad con múltiples backends (Azure Monitor, Grafana, Elastic, Jaeger, etc.) sin cambios de código

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué OpenTelemetry?

OpenTelemetry es el **estándar abierto** para observabilidad, respaldado por CNCF (Cloud Native Computing Foundation). Ventajas:

- **Vendor-agnostic**: Compatible con Azure Monitor, Grafana, Datadog, New Relic, Elastic, etc.
- **Un solo SDK**: Unified API para traces, metrics y logs
- **Protocolo estándar (OTLP)**: Cambias de backend sin tocar código
- **Ecosistema maduro**: Instrumentación automática para frameworks populares
- **Futuro-proof**: Adoptado por Microsoft, Google, AWS como estándar

### ¿Por qué `System.Diagnostics.ActivitySource` y `System.Diagnostics.Metrics.Meter`?

Son las **APIs nativas de .NET** para telemetría, parte del BCL (Base Class Library):

- ✅ **No requieren dependencias externas** (parte del runtime)
- ✅ **Performance optimizado** (implementación nativa en .NET)
- ✅ **Integración automática con OpenTelemetry** (conversión a OTLP spans/metrics)
- ✅ **W3C Trace Context standard** (propagación automática de `traceparent`/`tracestate`)
- ✅ **Recomendado por Microsoft** como approach oficial para .NET

**Alternativas descartadas**:
- OpenTelemetry SDK directo (`Tracer.StartActiveSpan`) → Más verboso, `ActivitySource` es la abstracción preferida en .NET
- Logging con correlation IDs manual → Reinventar la rueda, sin jerarquía de spans
- SDKs vendor-specific (Application Insights SDK) → Vendor lock-in, no portable

### Patrón: Telemetría en Building Blocks

**Decisión clave**: Colocar telemetría en los **building blocks reutilizables** (Core.Application, Core.EventBusRabbitMQ) en lugar de en cada microservicio.

**Ventajas**:
- ✅ **Reutilización automática**: Identity.API, futuros microservicios heredan telemetría sin configuración
- ✅ **Consistencia**: Mismas métricas y traces en toda la solución
- ✅ **Single Responsibility**: El building block que ejecuta la lógica es quien la observa
- ✅ **DRY (Don't Repeat Yourself)**: Un solo lugar para mantener

**Ejemplo**: `RabbitMQEventBus.PublishAsync()` registra métricas porque:
- Es el que REALMENTE publica el mensaje
- Se puede llamar directamente sin `AccountIntegrationEventService`
- Todos los microservicios que usen RabbitMQEventBus obtienen telemetría gratis

---

## 📦 Estructura Implementada

```
src/
├── uSLearn.Core.Application/
│   ├── Telemetry/
│   │   └── ApplicationDiagnostics.cs ← NUEVO (ActivitySource, Meter, métricas)
│   └── Behaviors/
│       ├── LoggingBehavior.cs (actualizado con telemetría)
│       ├── ValidationBehavior.cs (actualizado con telemetría)
│       └── (TransactionBehavior en Accounts.API actualizado)
│
├── uSLearn.Core.EventBusRabbitMQ/
│   ├── RabbitMQEventBus.cs (actualizado con telemetría Producer/Consumer)
│   └── Core.EventBusRabbitMQ.csproj (referencia a Core.Application)
│
├── uSLearn.Accounts.API/
│   ├── Application/
│   │   └── Behaviors/
│   │       └── TransactionBehavior.cs (actualizado con telemetría)
│   └── Infrastructure/
│       └── Extensions/
│           └── MediatorExtension.cs (actualizado - métricas domain events)
│
└── uSLearn.ServiceDefaults/
    └── Extensions.cs (actualizado - registro de ActivitySource y Meter personalizados)
```

---

## 🔧 Componentes Implementados

### 1. `ApplicationDiagnostics` (Central Telemetry Hub)

**Ubicación**: `uSLearn.Core.Application/Telemetry/ApplicationDiagnostics.cs`

**Propósito**: Punto único de definición de telemetría personalizada para toda la aplicación.

**Componentes**:

```csharp
public static class ApplicationDiagnostics
{
    public const string SourceName = "uSLearn.Core.Application";
    public const string Version = "1.0.0";

    // ActivitySource para distributed tracing
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    // Meter para métricas personalizadas
    public static readonly Meter Meter = new(SourceName, Version);

    // === Counters (contadores acumulativos) ===
    
    // Total de comandos/queries procesados (tags: command_type, success)
    public static readonly Counter<long> CommandsProcessed;
    
    // Total de validaciones fallidas (tags: command_type, error_count)
    public static readonly Counter<long> ValidationFailures;
    
    // Total de domain events publicados (tags: event_type)
    public static readonly Counter<long> DomainEventsPublished;
    
    // Total de integration events publicados/recibidos (tags: event_type, success)
    public static readonly Counter<long> IntegrationEventsPublished;
    public static readonly Counter<long> IntegrationEventsReceived;

    // === Histograms (distribuciones estadísticas) ===
    
    // Duración de comandos en ms (tags: command_type, success) - para p50, p95, p99
    public static readonly Histogram<double> CommandDuration;
    
    // Duración de validaciones en ms (tags: command_type, validator_count)
    public static readonly Histogram<double> ValidationDuration;
    
    // Duración de transacciones DB en ms (tags: command_type, success)
    public static readonly Histogram<double> TransactionDuration;
    
    // Número de errores de validación por request (tags: command_type)
    public static readonly Histogram<int> ValidationErrorCount;
}
```

**Características**:
- Nombre consistente: `"uSLearn.Core.Application"` (usado en ActivitySource y Meter)
- Métricas semánticas siguiendo OpenTelemetry Semantic Conventions
- Unidades explícitas: `{command}`, `{event}`, `ms`, `{error}`

---

### 2. `LoggingBehavior<TRequest, TResponse>` (Instrumentado)

**Ubicación**: `uSLearn.Core.Application/Behaviors/LoggingBehavior.cs`

**Telemetría añadida**:

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

        // ✅ Métricas de éxito
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
        // ✅ Métricas de fallo
        ApplicationDiagnostics.CommandsProcessed.Add(1, new("command_type", commandName), new("success", "false"));
        ApplicationDiagnostics.CommandDuration.Record(elapsedMs, new("command_type", commandName), new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("error.type", ex.GetType().FullName);

        throw;
    }
}
```

**Tags de Activity**:
- `command.name`: Nombre del comando (e.g., `"CreateOrganizationCommand"`)
- `request.id`: ID único del request (idempotencia)
- `correlation.id`: ID de correlación (distributed tracing)
- `command.success`: `true`/`false`
- `command.duration_ms`: Duración en milisegundos
- `command.slow`: `true` si excede threshold (500ms)
- `error.type`: Tipo de excepción si falla

---

### 3. `ValidationBehavior<TRequest, TResponse>` (Instrumentado)

**Ubicación**: `uSLearn.Core.Application/Behaviors/ValidationBehavior.cs`

**Telemetría añadida**:

```csharp
public async Task<TResponse> Handle(...)
{
    if (!_validators.Any())
        return await next(cancellationToken);

    var commandName = typeof(TRequest).Name;
    var validatorCount = _validators.Count();

    // ✅ Activity de validación
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Validation {commandName}",
        kind: ActivityKind.Internal);

    activity?.SetTag("validation.command", commandName);
    activity?.SetTag("validation.validator_count", validatorCount);

    var stopwatch = Stopwatch.StartNew();
    var validationResults = await Task.WhenAll(...);
    stopwatch.Stop();

    var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

    // ✅ Métrica de duración de validación
    ApplicationDiagnostics.ValidationDuration.Record(elapsedMs,
        new("command_type", commandName),
        new("validator_count", validatorCount));

    if (failures.Any())
    {
        var errorCount = failures.Count;

        // ✅ Métricas de fallo
        ApplicationDiagnostics.ValidationFailures.Add(1,
            new("command_type", commandName),
            new("error_count", errorCount));

        ApplicationDiagnostics.ValidationErrorCount.Record(errorCount,
            new("command_type", commandName));

        activity?.SetTag("validation.failed", true);
        activity?.SetTag("validation.error_count", errorCount);
        activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
        
        // ✅ Event con detalles de errores
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

**Características**:
- Activity anidada dentro del Command Activity (jerarquía visible en traces)
- Histograma de duración para análisis de performance
- Counter de failures para alerting
- ActivityEvent con detalles de errores (evita pollution de tags)

---

### 4. `TransactionBehavior<TRequest, TResponse>` (Instrumentado)

**Ubicación**: `uSLearn.Accounts.API/Application/Behaviors/TransactionBehavior.cs`

**Telemetría añadida**:

```csharp
public async Task<TResponse> Handle(...)
{
    var typeName = request.GetGenericTypeName();

    // ✅ Activity de transacción
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
            await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transaction.TransactionId);
        });

        stopwatch.Stop();
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        // ✅ Métrica de duración de transacción
        ApplicationDiagnostics.TransactionDuration.Record(elapsedMs,
            new("command_type", typeName),
            new("success", "true"));

        activity?.SetTag("transaction.success", true);
        activity?.SetTag("transaction.duration_ms", elapsedMs);

        return response;
    }
    catch (Exception ex)
    {
        // ✅ Métrica de fallo
        ApplicationDiagnostics.TransactionDuration.Record(0,
            new("command_type", typeName),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Tags específicos de transacciones**:
- `transaction.id`: GUID de la transacción DB
- `transaction.nested`: `true` si ya hay transacción activa
- `transaction.duration_ms`: Duración total (incluyendo publicación de eventos)

---

### 5. `RabbitMQEventBus` (Instrumentado - Building Block) ⭐

**Ubicación**: `uSLearn.Core.EventBusRabbitMQ/RabbitMQEventBus.cs`

**Mejora clave**: Telemetría movida de `AccountIntegrationEventService` al building block para **cobertura total** (todas las publicaciones se monitorizan).

#### PublishAsync (Producer)

```csharp
public async Task PublishAsync(IntegrationEvent @event)
{
    var routingKey = @event.GetType().Name;

    // ✅ Activity Producer (distributed tracing)
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Publish {routingKey}",
        kind: ActivityKind.Producer); // ← Producer para mensajería

    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("messaging.destination", ExchangeName);
    activity?.SetTag("messaging.destination_kind", "topic");
    activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
    activity?.SetTag("event.id", @event.Id);
    activity?.SetTag("event.type", routingKey);

    try
    {
        using var channel = await _rabbitMQConnection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: "direct");

        var body = SerializeMessage(@event);
        var properties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };

        // ✅ Inyección de trace context en headers (W3C Trace Context)
        if (activity != null)
        {
            properties.Headers = new Dictionary<string, object?>
            {
                ["traceparent"] = activity.Id, // ← Propagación de trace
                ["tracestate"] = activity.TraceStateString
            };
        }

        await _pipeline.Execute(async () =>
        {
            await channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);
        });

        // ✅ Métrica de publicación exitosa
        ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
            new("event_type", routingKey),
            new("success", "true"));

        activity?.SetTag("messaging.success", true);
    }
    catch (Exception ex)
    {
        // ✅ Métrica de publicación fallida
        ApplicationDiagnostics.IntegrationEventsPublished.Add(1,
            new("event_type", routingKey),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Características clave**:
- `ActivityKind.Producer`: Identifica como productor de mensajes
- **Inyección de trace context** en headers RabbitMQ (`traceparent`, `tracestate`)
- Tags semánticos de mensajería (OpenTelemetry Messaging Semantic Conventions)
- Métricas de success/failure para SLOs

#### ProcessEvent (Consumer)

```csharp
private async Task ProcessEvent(string eventName, string message)
{
    // ✅ Activity Consumer (distributed tracing)
    using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
        name: $"Process {eventName}",
        kind: ActivityKind.Consumer); // ← Consumer para mensajería

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

        // ✅ Métrica de recepción exitosa
        ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
            new("event_type", eventName),
            new("success", "true"));

        activity?.SetTag("messaging.success", true);
    }
    catch (Exception ex)
    {
        // ✅ Métrica de recepción fallida
        ApplicationDiagnostics.IntegrationEventsReceived.Add(1,
            new("event_type", eventName),
            new("success", "false"));

        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Ventajas del distributed tracing en mensajería**:
- Trace completo: `HTTP Request → Command → Transaction → Publish Event → Consume Event → Handler`
- El trace ID se propaga automáticamente via headers
- Visualización end-to-end en Aspire Dashboard / Grafana / Azure Monitor

---

### 6. `MediatorExtension.DispatchDomainEventsAsync` (Instrumentado)

**Ubicación**: `uSLearn.Accounts.API/Infrastructure/Extensions/MediatorExtension.cs`

**Telemetría añadida**:

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

        // ✅ Métrica de domain event publicado
        ApplicationDiagnostics.DomainEventsPublished.Add(1,
            new("event_type", eventType));

        await mediator.Publish(domainEvent);
    }
}
```

**Nota**: Los domain events se publican internamente (no cruzan boundaries), por lo que solo registramos métricas (no activities).

---

### 7. Registro en ServiceDefaults

**Ubicación**: `uSLearn.ServiceDefaults/Extensions.cs`

**Configuración de OpenTelemetry**:

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
                .AddMeter("uSLearn.Core.Application"); // ← NUESTRAS métricas personalizadas ✅
        })
        .WithTracing(tracing =>
        {
            tracing.AddSource(builder.Environment.ApplicationName) // ← Service-specific traces
                .AddSource("uSLearn.Core.Application")             // ← NUESTROS traces personalizados ✅
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
        builder.Services.AddOpenTelemetry().UseOtlpExporter(); // ← OTLP para logs, metrics, traces ✅
    }

    // Opcional: Azure Monitor para features adicionales (Live Metrics, Smart Detection)
    // if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    // {
    //     builder.Services.AddOpenTelemetry().UseAzureMonitor();
    // }

    return builder;
}
```

**Configuración clave**:
- `.AddMeter("uSLearn.Core.Application")`: Exporta NUESTRAS métricas personalizadas
- `.AddSource("uSLearn.Core.Application")`: Exporta NUESTROS traces personalizados
- `.UseOtlpExporter()`: Exporta todo (logs, metrics, traces) a cualquier backend compatible con OTLP

---

## 📊 Métricas y Traces Disponibles

### **Métricas (Metrics)**

| Nombre | Tipo | Descripción | Tags |
|--------|------|-------------|------|
| `application.commands.processed` | Counter | Total de comandos/queries procesados | `command_type`, `success` |
| `application.commands.duration` | Histogram | Duración de comandos en ms (p50, p95, p99) | `command_type`, `success` |
| `application.validation.failures` | Counter | Total de validaciones fallidas | `command_type`, `error_count` |
| `application.validation.duration` | Histogram | Duración de validaciones en ms | `command_type`, `validator_count` |
| `application.validation.error_count` | Histogram | Errores de validación por request | `command_type` |
| `application.transactions.duration` | Histogram | Duración de transacciones DB en ms | `command_type`, `success` |
| `application.domain_events.published` | Counter | Total de domain events publicados | `event_type` |
| `application.integration_events.published` | Counter | Total de integration events publicados | `event_type`, `success` |
| `application.integration_events.received` | Counter | Total de integration events recibidos | `event_type`, `success` |

**Métricas automáticas** (de instrumentación built-in):
- `http.server.request.duration` (ASP.NET Core)
- `http.client.request.duration` (HttpClient)
- `process.runtime.dotnet.gc.*` (GC stats)
- `process.runtime.dotnet.threads.count` (Thread pool)

---

### **Traces (Distributed Tracing)**

Ejemplo de trace completo para `PUT /api/accounts`:

```
Trace ID: 4bf92f3577b3f4f4ae1a0bf7a0d8c7
  │
  ├─ [Server] HTTP PUT /api/accounts (350ms) ← ASP.NET Core (auto)
  │   │
  │   ├─ [Internal] Command IdentifiedCommand<CreateOrganizationCommand,Boolean> (320ms) ← LoggingBehavior
  │   │   │  Tags: command.name, request.id, correlation.id, command.success, command.duration_ms
  │   │   │
  │   │   ├─ [Internal] Validation CreateOrganizationCommand (5ms) ← ValidationBehavior
  │   │   │      Tags: validation.command, validation.validator_count, validation.success
  │   │   │
  │   │   └─ [Internal] Transaction IdentifiedCommand<...> (310ms) ← TransactionBehavior
  │   │       │  Tags: transaction.id, transaction.success, transaction.duration_ms
  │   │       │
  │   │       ├─ [Client] SQL INSERT INTO Organizations (15ms) ← EF Core (auto)
  │   │       │
  │   │       └─ [Producer] Publish OrganizationCreatedIntegrationEvent (25ms) ← RabbitMQEventBus
  │   │              Tags: messaging.system=rabbitmq, event.type, event.id
  │   │              Headers: traceparent, tracestate ← W3C Trace Context
  │
  └─ [Consumer] Process OrganizationCreatedIntegrationEvent (30ms) ← RabbitMQEventBus (otro proceso)
      │  Tags: messaging.system=rabbitmq, event.type, event.handler_count
      │  Parent: Trace ID propagado via headers
      │
      └─ [Internal] EventHandler Logic (25ms)
```

**Characteristics**:
- **Jerarquía automática**: Activities anidadas (parent-child relationships)
- **Propagación de trace**: El mismo Trace ID atraviesa HTTP → Commands → RabbitMQ → Consumers
- **ActivityKind apropiado**: Internal, Server, Client, Producer, Consumer
- **Tags semánticos**: Siguiendo OpenTelemetry Semantic Conventions

---

## 🌐 Configuración para Diferentes Backends

Tu configuración actual usa **OTLP (OpenTelemetry Protocol)**, un estándar abierto compatible con todos los backends principales. **No necesitas cambiar código** para cambiar de backend - solo configuración.

### **Azure Monitor / Application Insights** (Recomendado para Azure)

#### Opción 1: OTLP directo (funciona desde 2023)

**Variables de entorno**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://<region>.monitor.azure.com/v1/traces
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer <access-token>
```

**Tu código actual**: ✅ Ya funciona sin cambios.

---

#### Opción 2: SDK nativo de Azure (features adicionales)

**Descomentar en `Extensions.cs`**:
```csharp
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
       .UseAzureMonitor(); // ← Features: Live Metrics, Smart Detection, Application Map
}
```

**En `appsettings.json` o Azure App Service Configuration**:
```json
{
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=xxx;IngestionEndpoint=https://..."
}
```

**Package necesario**:
```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.3.0" />
```

**Features exclusivas**:
- **Live Metrics**: Telemetría en tiempo real (latency, failures, requests/sec)
- **Smart Detection**: Alertas automáticas de anomalías (picos de errores, degradación de performance)
- **Application Map**: Topología visual de dependencias entre servicios
- **KQL Queries**: Queries avanzadas en Log Analytics

**Cuándo usar**:
- ✅ Production en Azure (integración nativa con Azure Portal)
- ✅ Necesitas features avanzadas de Application Insights
- ❌ Multi-cloud o quieres evitar vendor lock-in → Usa OTLP directo

---

### **Grafana Cloud / Grafana Stack**

**Variables de entorno**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://otlp-gateway-prod-eu-west-0.grafana.net/otlp
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Basic <base64(instance_id:token)>
```

**Tu código actual**: ✅ Ya funciona sin cambios.

**Mapeo automático**:
- Traces → **Grafana Tempo**
- Metrics → **Prometheus** (via Grafana Agent)
- Logs → **Grafana Loki**

**Obtener credenciales**:
1. Grafana Cloud → Stack → OTLP
2. Copiar Instance ID y generar Access Token
3. Base64 encode: `echo -n "instance_id:token" | base64`

**Dashboard recomendado**: [OpenTelemetry APM Dashboard](https://grafana.com/grafana/dashboards/19419)

---

### **Elastic Stack (Kibana + Elasticsearch)**

**Variables de entorno**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://your-elastic-apm.elastic-cloud.com:443
OTEL_EXPORTER_OTLP_HEADERS=Authorization=Bearer <secret-token>
```

**Tu código actual**: ✅ Ya funciona (Elastic APM soporta OTLP desde v8.0).

**Resultado**:
- Traces → **APM UI** en Kibana
- Metrics → **Elasticsearch** (visualizables en Kibana dashboards)
- Logs → **Elasticsearch** (buscables en Kibana Discover)

**Obtener token**:
```sh
# En Elastic Cloud
Observability → APM → Add APM integration → OTLP → Generate secret token
```

---

### **Configuración Híbrida (Múltiples Backends)**

Si quieres enviar telemetría a **múltiples destinos simultáneamente** (e.g., Azure Monitor + Grafana):

#### Deploy OTLP Collector

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

**Deploy en Azure Container Apps**:
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

**Tu aplicación apunta al collector**:
```sh
OTEL_EXPORTER_OTLP_ENDPOINT=https://otel-collector.azurecontainerapps.io:4317
```

**Ventajas**:
- ✅ Enviar a múltiples backends sin cambios en la app
- ✅ Sampling centralizado (reduce costos)
- ✅ Transformaciones de datos (filtrado, enriquecimiento)
- ✅ Cambiar backends sin redesplegar apps

---

## ✅ Verificación Práctica

### 1. **Ejecutar la aplicación**

```sh
dotnet run --project src/uSLearn.AppHost
```

Aspire Dashboard: `http://localhost:15888`

---

### 2. **Generar actividad**

**Request inválido (validación fallida)**:
```sh
curl -X PUT http://localhost:5001/api/accounts \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"name":"","legalName":"","taxIdNumber":"","country":"","zipCode":""}'
```

**Request válido**:
```sh
curl -X PUT http://localhost:5001/api/accounts \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corp",
    "legalName": "Acme Corporation LLC",
    "taxIdNumber": "12-3456789",
    "taxNumberType": "EIN",
    "country": "United States",
    "countryCode": "US",
    "zipCode": "94105",
    "city": "San Francisco",
    "state": "CA",
    "street": "123 Market St",
    "organizationType": "Company"
  }'
```

---

### 3. **Verificar Traces en Aspire Dashboard**

**Navegación**: Dashboard → **Traces** → Filtrar por `uSLearn.Accounts.API`

**Trace esperado**:
```
└─ HTTP PUT /api/accounts (350ms)
    ├─ Command IdentifiedCommand<CreateOrganizationCommand,Boolean> (320ms)
    │   ├─ Validation CreateOrganizationCommand (5ms) ✅
    │   └─ Transaction IdentifiedCommand<...> (310ms)
    │       ├─ SQL INSERT INTO Organizations (15ms)
    │       └─ Publish OrganizationCreatedIntegrationEvent (25ms) ✅
    └─ Process OrganizationCreatedIntegrationEvent (30ms) ✅
        └─ Handler Logic (25ms)
```

**Verificar**:
- ✅ Activities anidadas correctamente
- ✅ Tags visibles (`command.name`, `validation.success`, `messaging.system`, etc.)
- ✅ Durations precisas
- ✅ Trace ID propagado entre Producer → Consumer

---

### 4. **Verificar Métricas en Aspire Dashboard**

**Navegación**: Dashboard → **Metrics** → Seleccionar `uSLearn.Accounts.API`

**Métricas esperadas**:

| Métrica | Valor Esperado |
|---------|----------------|
| `application.commands.processed` (success=true) | 1+ |
| `application.commands.duration` (p95) | < 500ms (ideal) |
| `application.validation.duration` (p50) | < 10ms (ideal) |
| `application.validation.failures` | 1+ (si enviaste request inválido) |
| `application.transactions.duration` (p95) | < 300ms (depende de DB) |
| `application.integration_events.published` | 1+ (si evento publicado) |
| `application.integration_events.received` | 1+ (si evento consumido) |

**Verificar**:
- ✅ Counters incrementan con cada request
- ✅ Histograms muestran distribución (p50, p95, p99)
- ✅ Tags permiten filtrar por `command_type`, `event_type`, `success`

---

### 5. **Verificar Propagación de Trace Context (RabbitMQ)**

**Herramienta**: RabbitMQ Management UI (`http://localhost:15672`)

**Navegación**: Queues → `accounts.organizationcreatedintegrationevent` → Get Messages

**Headers esperados en el mensaje**:
```json
{
  "traceparent": "00-4bf92f3577b3f4f4ae1a0bf7a0d8c7-b7ad6b7169203331-01",
  "tracestate": "..."
}
```

**Verificar**:
- ✅ Header `traceparent` presente (W3C Trace Context)
- ✅ Trace ID coincide con el trace original en Aspire Dashboard

---

## 🔄 Reutilización en Otros Microserviços

Para usar telemetría en `Identity.API` o futuros microservicios:

### 1. **Ya funciona automáticamente** (si usa building blocks)

Si Identity.API usa:
- ✅ `LoggingBehavior`, `ValidationBehavior` → Telemetría automática
- ✅ `RabbitMQEventBus` → Telemetría de eventos automática
- ✅ `ServiceDefaults` → OpenTelemetry configurado

**No necesitas hacer nada adicional.** 🎉

---

### 2. **Para behaviors específicos del servicio**

Si Identity.API tiene un behavior custom (e.g., `AuthenticationBehavior`):

```csharp
public class AuthenticationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var commandName = typeof(TRequest).Name;

        // ✅ Usar ApplicationDiagnostics
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            name: $"Authentication {commandName}",
            kind: ActivityKind.Internal);

        activity?.SetTag("authentication.command", commandName);

        // ... lógica de autenticación ...

        // ✅ Registrar métrica (opcional)
        // ApplicationDiagnostics.Meter.CreateCounter<long>("identity.authentication.attempts")
        //     .Add(1, new("command_type", commandName), new("success", success));

        return await next(cancellationToken);
    }
}
```

**No necesitas crear nuevo ActivitySource** - reusa `ApplicationDiagnostics`.

---

### 3. **Para métricas específicas del dominio**

Si Identity.API necesita métricas específicas (e.g., intentos de login):

**Opción A: Añadir a `ApplicationDiagnostics.cs`** (si es común)
```csharp
// En ApplicationDiagnostics.cs
public static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>(
    name: "identity.login.attempts",
    unit: "{attempt}",
    description: "Total login attempts (tags: success, provider)");
```

**Opción B: Crear `IdentityDiagnostics.cs`** (si es muy específico)
```csharp
// Identity.API/Telemetry/IdentityDiagnostics.cs
public static class IdentityDiagnostics
{
    public const string SourceName = "uSLearn.Identity.API";
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>(
        "identity.login.attempts", "{attempt}", "Login attempts");
}

// En ServiceDefaults/Extensions.cs
.AddMeter("uSLearn.Core.Application")
.AddMeter("uSLearn.Identity.API") // ← Añadir nuevo meter
```

**Recomendación**: Usar Opción A para métricas comunes, Opción B para muy específicas del dominio.

---

## 🎯 Beneficios Logrados

### 1. **Observabilidad Completa (Three Pillars)**
- ✅ **Traces**: Distributed tracing end-to-end (HTTP → Commands → DB → Messaging)
- ✅ **Metrics**: Counters y histograms para SLOs/SLAs
- ✅ **Logs**: Structured logging con correlation IDs (ya implementado en Stage.04-1)

### 2. **Vendor-Agnostic (Portabilidad)**
- ✅ Funciona con Azure Monitor, Grafana, Elastic, Datadog, Jaeger, New Relic, Honeycomb
- ✅ Cambias de backend sin tocar código (solo configuración)
- ✅ Puedes enviar a múltiples backends simultáneamente (via OTLP Collector)

### 3. **Performance Insights**
- ✅ Detectar comandos lentos (threshold: 500ms)
- ✅ Analizar distribución de latencias (p50, p95, p99)
- ✅ Identificar cuellos de botella (validación vs transacción vs DB)

### 4. **Reliability Monitoring**
- ✅ Métricas de success/failure para alerting
- ✅ Tasa de validaciones fallidas
- ✅ Tasa de eventos publicados/recibidos correctamente

### 5. **Troubleshooting Efectivo**
- ✅ Trace completo de un request problemático (en segundos)
- ✅ Jerarquía visual de operaciones (spans anidados)
- ✅ Tags contextuales (command names, event types, error types)
- ✅ Events en activities con detalles adicionales (e.g., errores de validación)

### 6. **Distributed Tracing Real**
- ✅ Propagación de trace context en RabbitMQ (W3C standard)
- ✅ Mismo Trace ID a través de boundaries (HTTP → Messaging → Consumers)
- ✅ Visualización de flujos asincrónicos end-to-end

---

## 📚 Referencias

- [OpenTelemetry .NET Documentation](https://opentelemetry.io/docs/languages/net/)
- [System.Diagnostics.ActivitySource](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
- [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics)
- [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [.NET Aspire Telemetry](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable)

---

## 🎯 Próximos Pasos

### Completados en Stage.04:
- ✅ **Stage.04-1**: Logging enriquecido con structured logging y performance tracking
- ✅ **Stage.04-2**: Validaciones con FluentValidation y manejo de excepciones
- ✅ **Stage.04-3**: Telemetría con OpenTelemetry (traces, metrics, distributed tracing)

### **Stage.05**: Identity con Duende IdentityServer
- Autenticación con OAuth 2.0 / OpenID Connect
- Gestión de usuarios y roles
- Token-based authentication para microservicios
- Integration con Accounts.API

### **Mejoras Opcionales de Telemetría**:
- **Sampler configurables**: Reducir overhead en producción (e.g., 10% de traces)
- **Custom exporters**: Enviar métricas específicas a Prometheus/StatsD
- **Exemplars**: Vincular metrics → traces (clic en métrica → ver trace específico)
- **Baggage**: Propagar metadata custom a través de distributed traces
- **Resource attributes**: Añadir metadata de deployment (version, environment, region)
