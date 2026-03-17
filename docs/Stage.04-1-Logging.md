# Stage.04-1 - Logging Enriquecido con Core.Application

## 🎯 Objetivo de la Etapa

Implementar un **LoggingBehavior reutilizable** en un building block compartido (`uSLearn.Core.Application`) que proporcione:

- Performance tracking con medición de tiempos de ejecución
- Structured logging con Request ID y Correlation ID
- Detección automática de comandos lentos (>500ms)
- Manejo robusto de excepciones con contexto completo
- Reutilización en múltiples microservicios

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué un Building Block Compartido?

El logging es una preocupación transversal (cross-cutting concern) común a todos los microservicios. Implementarlo en un building block garantiza:

- **Reutilización**: Un solo código para Accounts, Identity y futuros servicios
- **Consistencia**: Misma estructura de logs en toda la solución
- **Mantenibilidad**: Cambios en un solo lugar
- **Testabilidad**: Fácil de probar con mocks

### Abstracción: `IRequestContextAccessor`

**Problema**: `LoggingBehavior` necesita acceso a Request ID y Correlation ID, pero estos vienen de HTTP headers (disponibles solo en capa API).

**Solución**: Crear una abstracción que permita obtener contexto sin depender de HTTP:

```csharp
public interface IRequestContextAccessor
{
    string? RequestId { get; }
    string? CorrelationId { get; }
}
```

**Ventajas**:
- ✅ Behavior agnóstico del origen del contexto (HTTP, Messaging, Tests)
- ✅ Separation of Concerns: Application Layer no conoce detalles de infraestructura
- ✅ Testeable: Se puede mockear fácilmente
- ✅ Extensible: Futuras implementaciones para otros contextos (RabbitMQ, gRPC, etc.)

---

## 📦 Estructura Implementada

```
src/
├── uSLearn.Core.Application/  ← NUEVO
│   ├── Abstractions/
│   │   └── IRequestContextAccessor.cs
│   └── Behaviors/
│       └── LoggingBehavior.cs
│
├── Core.Infrastructure/
│   └── Http/  ← NUEVO
│       └── HttpRequestContextAccessor.cs
│
├── uSLearn.Accounts.API/
│   └── Extensions/Extensions.cs (actualizado)
│
└── uSLearn.Identity.API/
    └── Extensions/Extensions.cs (actualizado)
```

---

## 🔧 Componentes Implementados

### 1. `IRequestContextAccessor` (Abstracción)

**Ubicación**: `uSLearn.Core.Application/Abstractions/IRequestContextAccessor.cs`

```csharp
namespace uSLearn.Core.Application.Abstractions;

public interface IRequestContextAccessor
{
    /// <summary>
    /// Unique identifier for the current request (idempotency).
    /// </summary>
    string? RequestId { get; }

    /// <summary>
    /// Correlation identifier spanning multiple requests/operations (distributed tracing).
    /// </summary>
    string? CorrelationId { get; }
}
```

---

### 2. `LoggingBehavior<TRequest, TResponse>` (Genérico)

**Ubicación**: `uSLearn.Core.Application/Behaviors/LoggingBehavior.cs`

**Características**:
- ✅ Inyección opcional de `IRequestContextAccessor` (null-safe)
- ✅ Medición de tiempos con `Stopwatch`
- ✅ Scopes automáticos con `BeginScope()`
- ✅ Detección de comandos lentos (threshold: 500ms)
- ✅ Manejo de excepciones con contexto completo

**Código relevante**:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IRequestContextAccessor? _contextAccessor; // ← Opcional
    private const int SlowCommandThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var commandName = GetCommandName(request);
        var requestId = _contextAccessor?.RequestId ?? "N/A";
        var correlationId = _contextAccessor?.CorrelationId ?? "N/A";

        // Scope con contexto estructurado
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CommandName"] = commandName,
            ["RequestId"] = requestId,
            ["CorrelationId"] = correlationId
        });

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            response = await next(cancellationToken); // ← MediatR 14.x
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowCommandThresholdMs)
            {
                _logger.LogWarning("Command {CommandName} took {ElapsedMs}ms...", ...);
            }
            else
            {
                _logger.LogInformation("Command {CommandName} handled in {ElapsedMs}ms", ...);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {CommandName} after {ElapsedMs}ms", ...);
            throw;
        }
    }
}
```

---

### 3. `HttpRequestContextAccessor` (Implementación HTTP)

**Ubicación**: `Core.Infrastructure/Http/HttpRequestContextAccessor.cs`

**Extracción de contexto desde HTTP headers**:

```csharp
public class HttpRequestContextAccessor : IRequestContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public string? RequestId =>
        _httpContextAccessor.HttpContext?
            .Request.Headers.TryGetValue("x-requestid", out var id) == true 
            ? id.ToString() 
            : null;

    public string? CorrelationId =>
        _httpContextAccessor.HttpContext?
            .Request.Headers.TryGetValue("x-correlation-id", out var id) == true 
            ? id.ToString() 
            : Activity.Current?.Id ?? _httpContextAccessor.HttpContext?.TraceIdentifier;
            // ↑ Fallback a OpenTelemetry trace o ASP.NET TraceIdentifier
}
```

---

## ⚙️ Registro en DI

### Accounts.API

**Archivo**: `src/uSLearn.Accounts.API/Extensions/Extensions.cs`

```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>)); // ← Desde Core.Application
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});
```

### Identity.API (preparado para uso futuro)

```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

// Cuando Identity tenga comandos/queries:
// services.AddMediatR(cfg =>
// {
//     cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
// });
```

---

## 🔄 Flujo de Ejecución

```
HTTP Request
  Headers: x-requestid, x-correlation-id
      ↓
HttpRequestContextAccessor
  Extrae RequestId y CorrelationId
      ↓
MediatR Pipeline:
  ┌──────────────────────────────────────┐
  │ 1️⃣ LoggingBehavior                  │
  │    ├─ BeginScope (RequestId, CorrelationId)
  │    ├─ Log: "Handling command..."    │
  │    └─ Start Stopwatch                │
  ├──────────────────────────────────────┤
  │ 2️⃣ TransactionBehavior              │
  │    └─ Begin DB Transaction           │
  ├──────────────────────────────────────┤
  │ 3️⃣ CommandHandler (business logic)  │
  ├──────────────────────────────────────┤
  │ ← Response                           │
  ├──────────────────────────────────────┤
  │ 2️⃣ TransactionBehavior (commit)     │
  ├──────────────────────────────────────┤
  │ 1️⃣ LoggingBehavior (exit)           │
  │    ├─ Stop Stopwatch                 │
  │    ├─ Log: "Command handled in Xms"  │
  │    └─ Warning si >500ms              │
  └──────────────────────────────────────┘
```

**Todos los logs dentro del pipeline incluyen**:
- CommandName
- RequestId
- CorrelationId
- ElapsedMilliseconds (en logs de salida)

---

## 📊 Ejemplos de Logs Estructurados

### Comando Exitoso

```json
{
  "timestamp": "2026-05-12T10:30:45.001Z",
  "level": "Information",
  "message": "Handling command CreateOrganizationCommand with RequestId abc-123",
  "CommandName": "CreateOrganizationCommand",
  "RequestId": "abc-123",
  "CorrelationId": "flow-001"
}

{
  "timestamp": "2026-05-12T10:30:45.235Z",
  "level": "Information",
  "message": "Command CreateOrganizationCommand handled successfully in 234ms",
  "ElapsedMilliseconds": 234
}
```

### Comando Lento (Warning)

```json
{
  "level": "Warning",
  "message": "Command CreateOrganizationCommand took 678ms (threshold: 500ms)",
  "ElapsedMilliseconds": 678,
  "ThresholdMs": 500
}
```

### Error con Stack Trace

```json
{
  "level": "Error",
  "message": "Error handling command CreateOrganizationCommand after 45ms",
  "ElapsedMilliseconds": 45,
  "Exception": {
    "Type": "DbUpdateException",
    "Message": "Duplicate key constraint...",
    "StackTrace": "..."
  }
}
```

---

## 🔧 Compatibilidad con MediatR 14.x

**Cambio importante**: En MediatR 14.0.0, el delegado `RequestHandlerDelegate<TResponse>` cambió:

```csharp
// MediatR 12.x
await next();

// MediatR 14.x
await next(cancellationToken); // ← Ahora requiere CancellationToken
```

**Beneficio**: Propagación correcta de cancelaciones a través del pipeline (timeouts, cliente desconectado, etc.).

---

## 🚀 Reutilización en Nuevos Microservicios

Para agregar logging en un nuevo microservicio:

1. **Referenciar** `uSLearn.Core.Application` y `Core.Infrastructure`
2. **Registrar** en DI:
```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();
services.AddMediatR(cfg => cfg.AddOpenBehavior(typeof(LoggingBehavior<,>)));
```

**¡3 líneas de código!** 🚀

---

## 🎓 Conceptos Clave

### Request ID vs Correlation ID

| Concepto | Request ID | Correlation ID |
|----------|-----------|----------------|
| **Alcance** | Una sola operación | Flujo completo (múltiples requests) |
| **Ejemplo** | `CreateOrganizationCommand` | "User Registration Flow" |
| **Uso** | Idempotencia | Distributed tracing |

**Ejemplo práctico**:
```
User Registration (Correlation ID: reg-flow-001)
  ├─ Request 1: CreateOrganization (Request ID: guid-001)
  ├─ Request 2: CreateUser (Request ID: guid-002)
  └─ Request 3: SendWelcomeEmail (Request ID: guid-003)
```

### Structured Logging

Usar placeholders con nombre en lugar de interpolación:

```csharp
// ❌ Malo
_logger.LogInformation($"Command {commandName} took {elapsed}ms");

// ✅ Bueno (queryable en herramientas de observabilidad)
_logger.LogInformation(
    "Command {CommandName} took {ElapsedMs}ms",
    commandName,
    elapsed);
```

---

## 📋 Archivos Creados/Modificados

### Nuevos

- `src/uSLearn.Core.Application/uSLearn.Core.Application.csproj`
- `src/uSLearn.Core.Application/Abstractions/IRequestContextAccessor.cs`
- `src/uSLearn.Core.Application/Behaviors/LoggingBehavior.cs`
- `src/Core.Infrastructure/Http/HttpRequestContextAccessor.cs`

### Modificados

- `src/uSLearn.Accounts.API/Extensions/Extensions.cs`
- `src/uSLearn.Accounts.API/Application/Behaviors/TransactionBehavior.cs` (MediatR 14.x)
- `src/uSLearn.Identity.API/Extensions/Extensions.cs`

### Eliminados

- `src/uSLearn.Accounts.API/Application/Behaviors/LoggingBehavior.cs` (movido a Core.Application)

---

## ✅ Verificación

### Build
```bash
dotnet build
# Build successful
```

### Ejecución
```bash
dotnet run --project src/uSLearn.AppHost
# Aplicación ejecutándose sin errores
```

### Logs en Aspire Dashboard
- Acceder a: http://localhost:15888
- Navegar a **Structured Logs**
- Filtrar por: `CommandName`, `RequestId`, `CorrelationId`
- Verificar presencia de `ElapsedMilliseconds`

---

## 🧭 Próximos Pasos

➡️ **Stage.04-2**: Validaciones con FluentValidation

- Implementar `ValidationBehavior<TRequest, TResponse>`
- Crear `AbstractValidator` para cada comando
- Retornar errores estructurados (400 Bad Request)
- Prevenir comandos inválidos antes de llegar a la BD

**Orden en el pipeline**:
```
LoggingBehavior → ValidationBehavior → TransactionBehavior → Handler
```

---

## 📚 Referencias

- [High-performance logging in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)
- [Structured logging in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging#log-message-template)
- [MediatR Pipeline Behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
- [Distributed tracing in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing)

---
