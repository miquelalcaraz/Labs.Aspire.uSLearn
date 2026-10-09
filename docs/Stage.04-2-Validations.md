# Stage.04-2 - Validation with FluentValidation

## 🎯 Stage Goal

Implement a **robust, reusable validation system** based on FluentValidation that provides:

- Automatic validation of commands/queries with FluentValidation
- Structured HTTP responses (400 Bad Request) with grouped errors
- Global exception handling in RFC 9110 format
- A `ValidationBehavior` in Core.Application, reusable by every microservice
- A clear split between validation errors (400) and internal errors (500)

---

## 🏗️ Architectural Decisions

### Why FluentValidation?

FluentValidation is the de facto standard library for declarative validation in .NET. Advantages:

- **Declarative**: expressive, readable validation rules
- **Reusable**: independent, testable validators
- **MediatR integration**: fits naturally into pipeline behaviors
- **Extensible**: custom rules are easy to write
- **Localizable**: built-in support for multi-language error messages

### Pattern: pipeline behavior + middleware

Validation is implemented in **two layers**:

1. **`ValidationBehavior<TRequest, TResponse>`** (MediatR pipeline)
   - Validates commands/queries before they reach the handler
   - Throws `ValidationException` with structured errors
   - Runs after `LoggingBehavior` and before `TransactionBehavior`

2. **`ExceptionHandlingMiddleware`** (ASP.NET Core pipeline)
   - Catches `ValidationException` → returns **400 Bad Request**
   - Catches `BadHttpRequestException` (e.g. missing `x-requestid` header) → returns its status code, usually **400**
   - Catches any other exception → returns **500 Internal Server Error**
   - Formats responses as RFC 9110 problem details

**Pipeline order**:

The endpoint sends an `IdentifiedCommand` that wraps the `CreateOrganizationCommand`, so the behaviors run **twice**:

```
HTTP Request
    ↓
ExceptionHandlingMiddleware ← Catches ALL exceptions
    ↓
Endpoint → Mediator.Send(IdentifiedCommand)
    ↓
LoggingBehavior → ValidationBehavior (no validator, passes) → TransactionBehavior (OPENS the transaction)
    ↓
IdentifiedCommandHandler → saves request ID → Mediator.Send(CreateOrganizationCommand)
    ↓
LoggingBehavior → ValidationBehavior (VALIDATES) → TransactionBehavior (transaction already open, doesn't open another)
    ↓
CreateOrganizationCommandHandler
```

Consequence: when the business command is invalid, the transaction is **already open** (the `IdentifiedCommand` opened it). The `ValidationException` triggers the rollback, including the request ID, so the client can fix the request and resend it with the same `x-requestid`.

### Why a single middleware?

**Original problem**: we had `ValidationExceptionMiddleware` + `UseExceptionHandler()`, but `UseExceptionHandler` caught exceptions **before** our middleware.

**Solution**: a single `ExceptionHandlingMiddleware` that:
- Catches `ValidationException` → 400 with structured errors
- Catches any other exception → 500, with details only in Development
- Completely replaces `UseExceptionHandler()`
- Is registered as the **first middleware** in the pipeline

---

## 📦 Implemented Structure

```
src/
├── Core.Application/   ← renamed (was uSLearn.Core.Application)
│   ├── Behaviors/
│   │   └── ValidationBehavior.cs ← NEW (generic, reusable)
│   └── Exceptions/
│       └── ValidationException.cs ← NEW (structured exception)
│
├── uSLearn.Accounts.API/
│   ├── Application/
│   │   └── Validators/
│   │       └── CreateOrganizationCommandValidator.cs ← NEW
│   ├── Infrastructure/
│   │   └── Middleware/
│   │       └── ExceptionHandlingMiddleware.cs ← RENAMED (was ValidationExceptionMiddleware)
│   ├── Extensions/Extensions.cs (updated - MediatR behaviors registration)
│   └── Program.cs (updated - middleware registration)
```

---

## 🔧 Implemented Components

### 1. `ValidationException` (custom exception)

**Location**: `src/Core.Application/Exceptions/ValidationException.cs`

**Characteristics**:
- Inherits from `Exception`
- Holds an `IDictionary<string, string[]> Errors` with errors grouped by property
- Constructor that takes FluentValidation's `IEnumerable<ValidationFailure>`
- Groups errors automatically by `PropertyName`

**Code**:

```csharp
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(
                failureGroup => failureGroup.Key, 
                failureGroup => failureGroup.ToArray()
            );
    }
}
```

**Example `Errors` structure** (keys are serialized in camelCase):
```json
{
  "name": ["Organization name is required"],
  "taxIdNumber": [
    "Tax ID number is required",
    "Tax ID number can only contain alphanumeric characters and hyphens"
  ],
  "zipCode": ["Zip code is required"]
}
```

---

### 2. `ValidationBehavior<TRequest, TResponse>` (generic)

**Location**: `src/Core.Application/Behaviors/ValidationBehavior.cs`

**Characteristics**:
- ✅ Injects `IEnumerable<IValidator<TRequest>>` (every validator registered for the request)
- ✅ Validates in parallel with `Task.WhenAll`
- ✅ Aggregates the errors of all validators
- ✅ Throws `ValidationException` if anything fails
- ✅ Skips automatically when no validators are registered

**Full code**:

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            // No validators registered for this request type, skip validation
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .Where(r => r.Errors.Any())
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Any())
        {
            throw new Exceptions.ValidationException(failures);
        }

        return await next(cancellationToken);
    }
}
```

**Advantages**:
- Reusable in Accounts.API, Identity.API and future microservices
- No extra configuration per validator
- Compatible with MediatR 14.x

---

### 3. `CreateOrganizationCommandValidator` (usage example)

**Location**: `uSLearn.Accounts.API/Application/Validators/CreateOrganizationCommandValidator.cs`

**Characteristics**:
- Inherits from `AbstractValidator<CreateOrganizationCommand>`
- Defines rules with the fluent API: `RuleFor`, `NotEmpty`, `MaximumLength`, `Matches`, `IsInEnum`, `When`
- Custom error messages
- Conditional validation for optional fields

**Sample rules**:

```csharp
public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        // Required fields
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required")
            .MaximumLength(200).WithMessage("Organization name cannot exceed 200 characters");

        RuleFor(x => x.LegalName)
            .NotEmpty().WithMessage("Legal name is required")
            .MaximumLength(200).WithMessage("Legal name cannot exceed 200 characters");

        RuleFor(x => x.TaxIdNumber)
            .NotEmpty().WithMessage("Tax ID number is required")
            .Matches(@"^[a-zA-Z0-9\-]+$")
            .WithMessage("Tax ID number can only contain alphanumeric characters and hyphens");

        // Enum validations
        RuleFor(x => x.TaxNumberType)
            .NotEqual(TaxNumberType.Unknown).WithMessage("Tax number type must be specified")
            .IsInEnum().WithMessage("Invalid tax number type");

        // Optional fields with conditional validation
        When(x => !string.IsNullOrWhiteSpace(x.Street), () =>
        {
            RuleFor(x => x.Street)
                .MaximumLength(500).WithMessage("Street cannot exceed 500 characters");
        });

        // ... more rules
    }
}
```

**Automatic registration** (in `Extensions.cs`):
```csharp
services.AddValidatorsFromAssemblyContaining<Program>();
```

This discovers and registers **every validator** in the assembly with a `Transient` lifetime.

---

### 4. `ExceptionHandlingMiddleware` (global exception handler)

**Location**: `uSLearn.Accounts.API/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`

**Characteristics**:
- ✅ Catches `ValidationException` → **400 Bad Request**
- ✅ Catches `BadHttpRequestException` → its own status code (usually **400**)
- ✅ Catches any other exception → **500 Internal Server Error**
- ✅ RFC 9110 problem details format for every response
- ✅ Appropriate logging: `LogWarning` for 400, `LogError` for 500
- ✅ Exception details only in Development (security)

**Code**:

```csharp
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed for {RequestMethod} {RequestPath}", 
                context.Request.Method, context.Request.Path);
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (BadHttpRequestException ex)
        {
            // e.g. missing x-requestid header: keep its status code (400) instead of a 500
            await HandleBadRequestExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestMethod} {RequestPath}: {ExceptionType}", 
                context.Request.Method, context.Request.Path, ex.GetType().FullName);
            await HandleGenericExceptionAsync(context, ex);
        }
    }

    private static async Task HandleValidationExceptionAsync(HttpContext context, ValidationException exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = 400,
            errors = exception.Errors,
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private async Task HandleGenericExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An error occurred while processing your request.",
            status = 500,
            traceId = context.TraceIdentifier,
            // Only in Development
            detail = _environment.IsDevelopment() ? exception.Message : null,
            stackTrace = _environment.IsDevelopment() ? exception.StackTrace : null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
```

**Registration in `Program.cs`**:
```csharp
var app = builder.Build();

// CRITICAL: must be the FIRST middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
// Do NOT use app.UseExceptionHandler() (replaced by our middleware)
```

---

## 📋 Service Registration

**Location**: `uSLearn.Accounts.API/Extensions/Extensions.cs`

```csharp
public static IHostApplicationBuilder AddApplicationServices(this IHostApplicationBuilder builder)
{
    // FluentValidation: validator auto-discovery
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // MediatR with behaviors in a specific order
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<Program>();
        
        // CRITICAL ORDER: Logging → Validation → Transaction → Handler
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // 1. Performance tracking
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 2. Validation (throws)
        cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));   // 3. DB transaction (if it gets here)
    });

    // IRequestContextAccessor for LoggingBehavior
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

    // ... other services
}
```

---

## 🧪 Validation Flow (Full Example)

### Invalid request

**HTTP request**:
```http
PUT /api/accounts?api-version=1.0 HTTP/1.1
x-requestid: f61cb746-a0cf-4bff-80c3-d12f18cb6380
Content-Type: application/json

{
  "name": "",
  "legalName": "",
  "taxIdNumber": "",
  "country": "",
  "zipCode": ""
}
```

### Internal flow

1. **Endpoint** receives the request → creates `IdentifiedCommand<CreateOrganizationCommand>` → `Mediator.Send(...)` → `TransactionBehavior` opens the transaction → `IdentifiedCommandHandler` sends the `CreateOrganizationCommand`

2. **LoggingBehavior** → log: `"Handling command CreateOrganizationCommand with RequestId f61cb746..."`

3. **ValidationBehavior**:
   - Finds `CreateOrganizationCommandValidator`
   - Runs the validation asynchronously
   - Finds errors in 7 properties
   - **Throws `ValidationException`** with structured errors

4. **LoggingBehavior** (catch) → log: `"Error handling command CreateOrganizationCommand after 114ms"`

5. **ExceptionHandlingMiddleware** (catch):
   - Detects `ValidationException`
   - Log: `"Validation failed for PUT /api/accounts"`
   - Returns **400 Bad Request**

### HTTP response (400)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "name": ["Organization name is required"],
    "legalName": ["Legal name is required"],
    "taxIdNumber": [
      "Tax ID number is required",
      "Tax ID number can only contain alphanumeric characters and hyphens"
    ],
    "taxNumberType": ["Tax number type must be specified"],
    "country": ["Country is required"],
    "zipCode": ["Zip code is required"],
    "organizationType": ["Invalid organization type"]
  },
  "traceId": "0HNP641GLKE5H:00000001"
}
```

### Generated logs

```
[Information] Handling command CreateOrganizationCommand with RequestId F61CB746-A0CF-4BFF-80C3-D12F18CB6380
[Warning] Validation failed for PUT /api/accounts
[Error] Error handling command CreateOrganizationCommand after 114ms - RequestId: F61CB746-A0CF-4BFF-80C3-D12F18CB6380
```

---

## ✅ Practical Verification

### 1. Invalid request

```bash
curl -X PUT https://localhost:7375/api/accounts?api-version=1.0 \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"name":"","legalName":"","taxIdNumber":"","country":"","zipCode":""}'
```

**Expected result**: 400 Bad Request with structured errors

### 2. Valid request

```bash
curl -X PUT https://localhost:7375/api/accounts?api-version=1.0 \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corp",
    "legalName": "Acme Corporation LLC",
    "taxIdNumber": "12-3456789",
    "taxNumberType": "Ein",
    "organizationType": "Company",
    "country": "United States",
    "zipCode": "94105",
    "city": "San Francisco",
    "state": "CA",
    "street": "123 Market St"
  }'
```

**Expected result**: 200 OK

> `organizationType` is required: if omitted it defaults to `0`, which isn't a valid enum value, and the API returns 400 with `"organizationType": ["Invalid organization type"]`.

### 3. Request without the `x-requestid` header

```bash
curl -X PUT https://localhost:7375/api/accounts?api-version=1.0 \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme Corp"}'
```

**Expected result**: 400 Bad Request with `"detail": "Required parameter \"Guid requestId\" was not provided from header."`

### 4. Internal error (simulated exception)

Temporarily change the handler to throw an exception:

```csharp
public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    public async Task<bool> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated error");
    }
}
```

**Expected result**: 500 Internal Server Error in RFC 9110 format

---

## 🔄 Reuse in Other Microservices

To add validation to `Identity.API` or future microservices:

### 1. Install FluentValidation

```xml
<PackageReference Include="FluentValidation" Version="12.1.1" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
```

### 2. Create validators

```csharp
// Identity.API/Application/Validators/LoginCommandValidator.cs (hypothetical example)
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");
    }
}
```

### 3. Register in Extensions.cs

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));      // Core.Application
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));   // Core.Application
    // ... other behaviors
});
```

### 4. Copy ExceptionHandlingMiddleware and register it in Program.cs

```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();
```

**Done!** Validation behaves the same way in every microservice.

---

## 📚 References

- [FluentValidation documentation](https://docs.fluentvalidation.net/)
- [RFC 9110 - HTTP Semantics](https://tools.ietf.org/html/rfc9110)
- [MediatR pipeline behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
- [ASP.NET Core middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/)

---

## 🎯 Next Steps

- **Stage.04-3**: Telemetry with OpenTelemetry (metrics, traces, spans)
- **Optional improvements**:
  - Validators with dependencies (DB access for complex validations)
  - Localized error messages (multi-language)
  - Async validation with custom rules (`MustAsync`)
  - Per-endpoint rate limiting based on the number of validation failures
