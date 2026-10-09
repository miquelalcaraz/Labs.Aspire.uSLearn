# Stage.04-2 - Validaciones con FluentValidation

## 🎯 Objetivo de la Etapa

Implementar un **sistema de validación robusto y reutilizable** basado en FluentValidation que proporcione:

- Validación automática de commands/queries con FluentValidation
- Respuestas HTTP estructuradas (400 Bad Request) con errores agrupados
- Manejo global de excepciones con formato RFC 9110
- ValidationBehavior en Core.Application para reutilización en todos los microservicios
- Separación clara entre errores de validación (400) y errores internos (500)

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué FluentValidation?

FluentValidation es la biblioteca estándar de facto para validación declarativa en .NET. Ventajas:

- **Declarativo**: Reglas de validación expresivas y legibles
- **Reutilizable**: Validadores independientes y testeables
- **Integración MediatR**: Se integra naturalmente con pipeline behaviors
- **Extensible**: Fácil crear reglas personalizadas
- **Localizable**: Soporte nativo para mensajes de error multiidioma

### Patrón: Pipeline Behavior + Middleware

Implementamos validación en **dos capas**:

1. **`ValidationBehavior<TRequest, TResponse>`** (MediatR Pipeline)
   - Valida commands/queries antes de llegar al handler
   - Lanza `ValidationException` con errores estructurados
   - Se ejecuta después de LoggingBehavior y antes de TransactionBehavior

2. **`ExceptionHandlingMiddleware`** (ASP.NET Core Pipeline)
   - Captura `ValidationException` → retorna **400 Bad Request**
   - Captura `BadHttpRequestException` (p. ej. falta la cabecera `x-requestid`) → retorna su código, normalmente **400**
   - Captura otras excepciones → retorna **500 Internal Server Error**
   - Formatea respuestas con RFC 9110 (Problem Details)

**Orden del pipeline**:

El endpoint envía un `IdentifiedCommand` que envuelve al `CreateOrganizationCommand`, así que los behaviors se ejecutan **dos veces**:

```
HTTP Request
    ↓
ExceptionHandlingMiddleware ← Captura TODAS las excepciones
    ↓
Endpoint → Mediator.Send(IdentifiedCommand)
    ↓
LoggingBehavior → ValidationBehavior (sin validador, pasa) → TransactionBehavior (ABRE la transacción)
    ↓
IdentifiedCommandHandler → guarda Request ID → Mediator.Send(CreateOrganizationCommand)
    ↓
LoggingBehavior → ValidationBehavior (VALIDA) → TransactionBehavior (ya hay transacción, no abre otra)
    ↓
CreateOrganizationCommandHandler
```

Consecuencia: cuando el comando de negocio es inválido, la transacción **ya está abierta** (la abrió el `IdentifiedCommand`). La `ValidationException` provoca el rollback, incluido el Request ID, por lo que el cliente puede corregir la petición y reenviarla con el mismo `x-requestid`.

### ¿Por qué un Middleware Unificado?

**Problema original**: Teníamos `ValidationExceptionMiddleware` + `UseExceptionHandler()`, pero `UseExceptionHandler` capturaba excepciones **antes** que nuestro middleware.

**Solución**: `ExceptionHandlingMiddleware` unificado que:
- Captura ValidationException → 400 con errores estructurados
- Captura cualquier otra excepción → 500 con detalles solo en Development
- Reemplaza `UseExceptionHandler()` completamente
- Se registra como **primer middleware** en el pipeline

---

## 📦 Estructura Implementada

```
src/
├── Core.Application/   ← renombrado (antes uSLearn.Core.Application)
│   ├── Behaviors/
│   │   └── ValidationBehavior.cs ← NUEVO (genérico, reutilizable)
│   └── Exceptions/
│       └── ValidationException.cs ← NUEVO (excepción estructurada)
│
├── uSLearn.Accounts.API/
│   ├── Application/
│   │   └── Validators/
│   │       └── CreateOrganizationCommandValidator.cs ← NUEVO
│   ├── Infrastructure/
│   │   └── Middleware/
│   │       └── ExceptionHandlingMiddleware.cs ← RENOMBRADO (antes ValidationExceptionMiddleware)
│   ├── Extensions/Extensions.cs (actualizado - registro MediatR behaviors)
│   └── Program.cs (actualizado - registro middleware)
```

---

## 🔧 Componentes Implementados

### 1. `ValidationException` (Custom Exception)

**Ubicación**: `src/Core.Application/Exceptions/ValidationException.cs`

**Características**:
- Hereda de `Exception`
- Contiene `IDictionary<string, string[]> Errors` con errores agrupados por propiedad
- Constructor que recibe `IEnumerable<ValidationFailure>` de FluentValidation
- Agrupa errores automáticamente por `PropertyName`

**Código**:

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

**Ejemplo de estructura de `Errors`**:
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

### 2. `ValidationBehavior<TRequest, TResponse>` (Genérico)

**Ubicación**: `src/Core.Application/Behaviors/ValidationBehavior.cs`

**Características**:
- ✅ Inyecta `IEnumerable<IValidator<TRequest>>` (todos los validadores registrados para el request)
- ✅ Valida en paralelo con `Task.WhenAll`
- ✅ Agrega todos los errores de todos los validadores
- ✅ Lanza `ValidationException` si hay fallos
- ✅ Skip automático si no hay validadores registrados

**Código completo**:

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

**Ventajas**:
- Reutilizable en Accounts.API, Identity.API y futuros microservicios
- No requiere configuración adicional por validator
- Compatible con MediatR 14.x

---

### 3. `CreateOrganizationCommandValidator` (Ejemplo de Uso)

**Ubicación**: `uSLearn.Accounts.API/Application/Validators/CreateOrganizationCommandValidator.cs`

**Características**:
- Hereda de `AbstractValidator<CreateOrganizationCommand>`
- Define reglas con FluentAPI: `RuleFor`, `NotEmpty`, `MaximumLength`, `Matches`, `IsInEnum`, `When`
- Mensajes de error personalizados
- Validación condicional para campos opcionales

**Ejemplo de reglas**:

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

        // ... más reglas
    }
}
```

**Registro automático** (en `Extensions.cs`):
```csharp
services.AddValidatorsFromAssemblyContaining<Program>();
```

Esto auto-descubre y registra **todos los validadores** del assembly con lifetime `Transient`.

---

### 4. `ExceptionHandlingMiddleware` (Global Exception Handler)

**Ubicación**: `uSLearn.Accounts.API/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`

**Características**:
- ✅ Captura `ValidationException` → **400 Bad Request**
- ✅ Captura cualquier otra excepción → **500 Internal Server Error**
- ✅ Formato RFC 9110 (Problem Details) para todas las respuestas
- ✅ Logging apropiado: `LogWarning` para 400, `LogError` para 500
- ✅ Detalles de excepción solo en Development (security)

**Código**:

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
            // Solo en Development
            detail = _environment.IsDevelopment() ? exception.Message : null,
            stackTrace = _environment.IsDevelopment() ? exception.StackTrace : null
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
```

**Registro en `Program.cs`**:
```csharp
var app = builder.Build();

// CRÍTICO: Debe ser el PRIMER middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
// NO usar app.UseExceptionHandler() (reemplazado por nuestro middleware)
```

---

## 📋 Registro de Servicios

**Ubicación**: `uSLearn.Accounts.API/Extensions/Extensions.cs`

```csharp
public static IHostApplicationBuilder AddApplicationServices(this IHostApplicationBuilder builder)
{
    // FluentValidation: auto-discovery de validadores
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // MediatR con behaviors en orden específico
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining<Program>();
        
        // ORDEN CRÍTICO: Logging → Validation → Transaction → Handler
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // 1. Performance tracking
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));    // 2. Validación (lanza excepción)
        cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));   // 3. Transacción DB (si llega aquí)
    });

    // IRequestContextAccessor para LoggingBehavior
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestContextAccessor, HttpRequestContextAccessor>();

    // ... otros servicios
}
```

---

## 🧪 Flujo de Validación (Ejemplo Completo)

### Request Inválido

**HTTP Request**:
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

### Flujo Interno

1. **Endpoint** recibe request → Crea `IdentifiedCommand<CreateOrganizationCommand>` → `Mediator.Send(...)` → `TransactionBehavior` abre la transacción → `IdentifiedCommandHandler` envía el `CreateOrganizationCommand`

2. **LoggingBehavior** → Log: `"Handling command CreateOrganizationCommand with RequestId f61cb746..."`

3. **ValidationBehavior**:
   - Encuentra `CreateOrganizationCommandValidator`
   - Ejecuta validación asíncrona
   - Encuentra errores en 7 propiedades
   - **Lanza `ValidationException`** con errores estructurados

4. **LoggingBehavior** (catch) → Log: `"Error handling command CreateOrganizationCommand after 114ms"`

5. **ExceptionHandlingMiddleware** (catch):
   - Detecta `ValidationException`
   - Log: `"Validation failed for PUT /api/accounts"`
   - Retorna **400 Bad Request**

### HTTP Response (400)

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

### Logs Generados

```
[Information] Handling command CreateOrganizationCommand with RequestId F61CB746-A0CF-4BFF-80C3-D12F18CB6380
[Warning] Validation failed for PUT /api/accounts
[Error] Error handling command CreateOrganizationCommand after 114ms - RequestId: F61CB746-A0CF-4BFF-80C3-D12F18CB6380
```

---

## ✅ Verificación Práctica

### 1. Test con Request Inválido

```bash
curl -X PUT https://localhost:7375/api/accounts?api-version=1.0 \
  -H "x-requestid: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"name":"","legalName":"","taxIdNumber":"","country":"","zipCode":""}'
```

**Resultado esperado**: 400 Bad Request con errores estructurados

### 2. Test con Request Válido

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

**Resultado esperado**: 200 OK

> `organizationType` es obligatorio: si se omite, vale `0`, que no es un valor válido del enum, y la API responde 400 con `"organizationType": ["Invalid organization type"]`.

### 3. Test sin cabecera `x-requestid`

```bash
curl -X PUT https://localhost:7375/api/accounts?api-version=1.0 \
  -H "Content-Type: application/json" \
  -d '{"name":"Acme Corp"}'
```

**Resultado esperado**: 400 Bad Request con `"detail": "Required parameter \"Guid requestId\" was not provided from header."`

### 4. Test de Error Interno (Simular Exception)

Modificar temporalmente el handler para lanzar una excepción:

```csharp
public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    public async Task<bool> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated error");
    }
}
```

**Resultado esperado**: 500 Internal Server Error con formato RFC 9110

---

## 🔄 Reutilización en Otros Microservicios

Para usar validaciones en `Identity.API` o futuros microservicios:

### 1. Instalar FluentValidation

```xml
<PackageReference Include="FluentValidation" Version="12.1.1" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
```

### 2. Crear Validadores

```csharp
// Identity.API/Application/Validators/LoginCommandValidator.cs
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

### 3. Registrar en Extensions.cs

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));      // Core.Application
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));   // Core.Application
    // ... otros behaviors
});
```

### 4. Copiar ExceptionHandlingMiddleware y Registrar en Program.cs

```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();
```

**¡Listo!** Validaciones funcionando con el mismo comportamiento en todos los microservicios.

---

## 📚 Referencias

- [FluentValidation Documentation](https://docs.fluentvalidation.net/)
- [RFC 9110 - HTTP Semantics](https://tools.ietf.org/html/rfc9110)
- [MediatR Pipeline Behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
- [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/)

---

## 🎯 Próximos Pasos

- **Stage.04-3**: Telemetría con OpenTelemetry (métricas, traces, spans)
- **Mejoras opcionales**:
  - Validadores con dependencias (acceso a DB para validaciones complejas)
  - Localización de mensajes de error (multi-idioma)
  - Validaciones asíncronas con reglas personalizadas (`MustAsync`)
  - Rate limiting por endpoint basado en ValidationException count
