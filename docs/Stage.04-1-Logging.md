# Stage.04-1 - Enriched Logging with Core.Application

## 🎯 Stage Goal

Implement a **reusable LoggingBehavior** in a shared building block (`uSLearn.Core.Application`) that provides:

- Performance tracking by measuring execution times
- Structured logging with request ID and correlation ID
- Automatic detection of slow commands (>500ms)
- Robust exception handling with full context
- Reuse across several microservices

---

## 🏗️ Architectural Decisions

### Why a shared building block?

Logging is a cross-cutting concern shared by every microservice. Implementing it in a building block ensures:

- **Reuse**: a single implementation for Accounts, Identity and future services
- **Consistency**: the same log structure across the solution
- **Maintainability**: changes in one place
- **Testability**: easy to test with mocks

### Abstraction: `IRequestContextAccessor`

**Problem**: `LoggingBehavior` needs the request ID and correlation ID, but these come from HTTP headers (only available in the API layer).

**Solution**: an abstraction that provides the context without depending on HTTP:

```csharp
public interface IRequestContextAccessor
{
    string? RequestId { get; }
    string? CorrelationId { get; }
}
```

**Advantages**:
- ✅ The behavior doesn't care where the context comes from (HTTP, messaging, tests)
- ✅ Separation of concerns: the application layer knows nothing about infrastructure
- ✅ Testable: easy to mock
- ✅ Extensible: future implementations for other contexts (RabbitMQ, gRPC, etc.)

---

## 📦 Implemented Structure

```
src/
├── uSLearn.Core.Application/  ← NEW
│   ├── Abstractions/
│   │   └── IRequestContextAccessor.cs
│   └── Behaviors/
│       └── LoggingBehavior.cs
│
├── Core.Infrastructure/
│   └── Http/  ← NEW
│       └── HttpRequestContextAccessor.cs
│
├── uSLearn.Accounts.API/
│   └── Extensions/Extensions.cs (updated)
│
└── uSLearn.Identity.API/
    └── Extensions/Extensions.cs (updated)
```

> In Stage.04-2 the project is renamed to `Core.Application`, matching the other building blocks.

---

## 🔧 Implemented Components

### 1. `IRequestContextAccessor` (abstraction)

**Location**: `uSLearn.Core.Application/Abstractions/IRequestContextAccessor.cs`

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

### 2. `LoggingBehavior<TRequest, TResponse>` (generic)

**Location**: `uSLearn.Core.Application/Behaviors/LoggingBehavior.cs`

**Features**:
- ✅ Optional (null-safe) injection of `IRequestContextAccessor`
- ✅ Timing with `Stopwatch`
- ✅ Automatic scopes with `BeginScope()`
- ✅ Slow command detection (threshold: 500ms)
- ✅ Exception handling with full context

**Relevant code**:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IRequestContextAccessor? _contextAccessor; // ← Optional
    private const int SlowCommandThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var commandName = GetCommandName(request);
        var requestId = _contextAccessor?.RequestId ?? "N/A";
        var correlationId = _contextAccessor?.CorrelationId ?? "N/A";

        // Scope with structured context
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
                _logger.LogWarning("Command {CommandName} took {ElapsedMilliseconds}ms...", ...);
            }
            else
            {
                _logger.LogInformation("Command {CommandName} handled in {ElapsedMilliseconds}ms", ...);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {CommandName} after {ElapsedMilliseconds}ms", ...);
            throw;
        }
    }
}
```

---

### 3. `HttpRequestContextAccessor` (HTTP implementation)

**Location**: `Core.Infrastructure/Http/HttpRequestContextAccessor.cs`

**Context extracted from HTTP headers**:

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
            // ↑ Falls back to the OpenTelemetry trace or the ASP.NET TraceIdentifier
}
```

---

## ⚙️ DI Registration

### Accounts.API

**File**: `src/uSLearn.Accounts.API/Extensions/Extensions.cs`

```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>)); // ← From Core.Application
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});
```

### Identity.API (ready for future use)

```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

// Once Identity has commands/queries:
// services.AddMediatR(cfg =>
// {
//     cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
// });
```

---

## 🔄 Execution Flow

```
HTTP Request
  Headers: x-requestid, x-correlation-id
      ↓
HttpRequestContextAccessor
  Extracts RequestId and CorrelationId
      ↓
MediatR Pipeline:
  ┌──────────────────────────────────────┐
  │ 1️⃣ LoggingBehavior                   │
  │    ├─ BeginScope (RequestId, CorrelationId)
  │    ├─ Log: "Handling command..."     │
  │    └─ Start Stopwatch                │
  ├──────────────────────────────────────┤
  │ 2️⃣ TransactionBehavior               │
  │    └─ Begin DB Transaction           │
  ├──────────────────────────────────────┤
  │ 3️⃣ CommandHandler (business logic)   │
  ├──────────────────────────────────────┤
  │ ← Response                           │
  ├──────────────────────────────────────┤
  │ 2️⃣ TransactionBehavior (commit)      │
  ├──────────────────────────────────────┤
  │ 1️⃣ LoggingBehavior (exit)            │
  │    ├─ Stop Stopwatch                 │
  │    ├─ Log: "Command handled in Xms"  │
  │    └─ Warning if >500ms              │
  └──────────────────────────────────────┘
```

**Every log inside the pipeline includes**:
- CommandName
- RequestId
- CorrelationId
- ElapsedMilliseconds (in exit logs)

---

## 📊 Structured Log Examples

### Successful command

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

### Slow command (warning)

```json
{
  "level": "Warning",
  "message": "Command CreateOrganizationCommand took 678ms (threshold: 500ms)",
  "ElapsedMilliseconds": 678,
  "ThresholdMs": 500
}
```

### Error with stack trace

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

## 🔧 MediatR 14.x Compatibility

**Notable change**: since MediatR 13, the `RequestHandlerDelegate<TResponse>` delegate accepts a `CancellationToken`:

```csharp
// MediatR 12.x
await next();

// MediatR 14.x
await next(cancellationToken); // ← Pass the CancellationToken through
```

**Benefit**: cancellation propagates correctly through the pipeline (timeouts, client disconnects, etc.).

---

## 🚀 Reuse in New Microservices

To add logging to a new microservice:

1. **Reference** `Core.Application` and `Core.Infrastructure`
2. **Register** in DI:
```csharp
services.AddHttpContextAccessor();
services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();
services.AddMediatR(cfg => cfg.AddOpenBehavior(typeof(LoggingBehavior<,>)));
```

**Three lines of code!** 🚀

---

## 🎓 Key Concepts

### Request ID vs correlation ID

| Concept | Request ID | Correlation ID |
|----------|-----------|----------------|
| **Scope** | A single operation | A whole flow (several requests) |
| **Example** | `CreateOrganizationCommand` | "User Registration Flow" |
| **Use** | Idempotency | Distributed tracing |

**Practical example**:
```
User Registration (Correlation ID: reg-flow-001)
  ├─ Request 1: CreateOrganization (Request ID: guid-001)
  ├─ Request 2: CreateUser (Request ID: guid-002)
  └─ Request 3: SendWelcomeEmail (Request ID: guid-003)
```

### Structured logging

Use named placeholders instead of string interpolation:

```csharp
// ❌ Bad
_logger.LogInformation($"Command {commandName} took {elapsed}ms");

// ✅ Good (queryable in observability tools)
_logger.LogInformation(
    "Command {CommandName} took {ElapsedMs}ms",
    commandName,
    elapsed);
```

---

## 📋 Files Created/Modified

### New

- `src/uSLearn.Core.Application/uSLearn.Core.Application.csproj`
- `src/uSLearn.Core.Application/Abstractions/IRequestContextAccessor.cs`
- `src/uSLearn.Core.Application/Behaviors/LoggingBehavior.cs`
- `src/Core.Infrastructure/Http/HttpRequestContextAccessor.cs`

### Modified

- `src/uSLearn.Accounts.API/Extensions/Extensions.cs`
- `src/uSLearn.Accounts.API/Application/Behaviors/TransactionBehavior.cs` (MediatR 14.x)
- `src/uSLearn.Identity.API/Extensions/Extensions.cs`

### Removed

- `src/uSLearn.Accounts.API/Application/Behaviors/LoggingBehavior.cs` (moved to Core.Application)

---

## ✅ Verification

### Build
```bash
dotnet build
# Build successful
```

### Run
```bash
dotnet run --project src/uSLearn.AppHost
# Application running without errors
```

### Logs in the Aspire dashboard
- Open the dashboard (the URL is printed in the console)
- Go to **Structured Logs**
- Filter by `CommandName`, `RequestId`, `CorrelationId`
- Check that `ElapsedMilliseconds` is present

---

## 🧭 Next Steps

➡️ **Stage.04-2**: Validation with FluentValidation

- Implement `ValidationBehavior<TRequest, TResponse>`
- Create an `AbstractValidator` for each command
- Return structured errors (400 Bad Request)
- Stop invalid commands before they reach the DB

**Pipeline order**:
```
LoggingBehavior → ValidationBehavior → TransactionBehavior → Handler
```

---

## 📚 References

- [High-performance logging in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/high-performance-logging)
- [Structured logging in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging#log-message-template)
- [MediatR pipeline behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
- [Distributed tracing in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing)
