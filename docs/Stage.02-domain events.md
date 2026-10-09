# Stage.02 - Implementación de Eventos de Dominio

## 🎯 Objetivo de la Etapa

Implementar **Domain Events** dentro del microservicio de cuentas (Accounts) siguiendo los principios de **Domain-Driven Design (DDD)**, estableciendo las bases para una arquitectura rica en dominio con:

- Un agregado `Organization` con lógica de negocio encapsulada
- Eventos de dominio que representan cambios significativos en el estado del dominio
- Patrón MediatR para despachar eventos dentro del mismo bounded context
- Separación clara entre capas (Domain, Application, Infrastructure, API)
- Entity Framework Core con implementación del patrón Repository y Unit of Work
- CQRS básico con separación entre Commands y Queries

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué Domain Events?

Los **Domain Events** son fundamentales en una arquitectura DDD porque:

- **Desacoplan** la lógica de negocio de los efectos secundarios
- Permiten **extensibilidad** sin modificar el agregado
- Hacen **explícitos** los cambios importantes en el dominio
- Facilitan la **auditoría** y el tracking de cambios
- Son la base para eventos de integración (Stage.03)

### Diferencia entre Domain Events e Integration Events

| Aspecto | Domain Events | Integration Events |
|---------|--------------|-------------------|
| **Alcance** | Mismo bounded context | Entre bounded contexts |
| **Transacción** | Dentro de la misma transacción DB | Transacciones distribuidas |
| **Infraestructura** | MediatR (in-process) | Message broker (RabbitMQ, Azure Service Bus) |
| **Propósito** | Mantener consistencia interna | Comunicación entre servicios |

En esta etapa implementamos **Domain Events**. Los **Integration Events** se añadirán en Stage.03.

### Estructura del Microservicio Accounts

Se ha optado por una **arquitectura en capas** inspirada en DDD:

```
uSLearn.Accounts.API/
├── Domain/                          → Lógica de negocio pura
│   ├── SeedWork/                    → Building blocks DDD
│   ├── OrganizationAggregate/       → Agregado Organization
│   ├── Events/                      → Domain Events
│   └── Exceptions/                  → Excepciones de dominio
├── Application/                     → Casos de uso (Commands/Queries)
│   ├── Commands/                    → Command handlers (CQRS)
│   ├── Queries/                     → Query handlers (CQRS)
│   └── Behaviors/                   → Behaviors de MediatR
├── Infrastructure/                  → Persistencia y detalles técnicos
│   ├── EntityConfigurations/        → EF Core configurations
│   ├── Repositories/                → Implementaciones de repositorios
│   ├── Migrations/                  → Migraciones de EF Core
│   ├── Seed/                        → Datos iniciales
│   └── Extensions/                  → Extensiones y utilidades
├── Api/                             → Endpoints HTTP
└── Extensions/                      → Configuración de servicios
```

### Principios DDD Aplicados

1. **Aggregate Root**: `Organization` es el agregado raíz que controla el acceso a `OrganizationContact`
2. **Value Objects**: `Address` representa un concepto de dominio sin identidad
3. **Domain Events**: `OrganizationCreatedDomainEvent` notifica la creación de organizaciones
4. **Ubiquitous Language**: Tipos como `OrganizationType`, `TaxNumberType` expresan el lenguaje del negocio
5. **Invariantes**: La lógica de validación está en el agregado (ej: `TaxNumberType` no puede ser `Unknown`)

---

## 📦 Componentes Implementados

### 1. **Domain Layer (Capa de Dominio)**

#### Entity Base Class

La clase base `Entity` proporciona:
- Identidad mediante `Guid Id`
- Gestión de eventos de dominio mediante una colección interna
- Métodos para añadir, eliminar y limpiar eventos
- Implementación de igualdad basada en identidad

```csharp
public abstract class Entity
{
    private List<INotification>? _domainEvents;
    public IReadOnlyCollection<INotification>? DomainEvents => _domainEvents?.AsReadOnly();

    public void AddDomainEvent(INotification eventItem)
    {
        _domainEvents = _domainEvents ?? new List<INotification>();
        _domainEvents.Add(eventItem);
    }

    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }
}
```

**Características clave:**
- Los eventos se almacenan como `INotification` (interfaz de MediatR)
- Colección inmutable expuesta mediante `IReadOnlyCollection`
- Los eventos se despachan en `SaveChanges` antes del commit

#### Organization Aggregate Root

El agregado `Organization` implementa:

```csharp
public class Organization : Entity, IAggregateRoot
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string LegalName { get; private set; }
    public Address? Address { get; private set; }
    public string TaxNumber { get; private set; }
    public TaxNumberType TaxNumberType { get; private set; }
    public OrganizationType OrganizationType { get; private set; }
    
    private readonly List<OrganizationContact> _organizationContacts;
    public IReadOnlyCollection<OrganizationContact> OrganizationContacts => _organizationContacts.AsReadOnly();

    public static Organization Create(...)
    {
        var organization = new Organization()
        {
            TenantId = Guid.NewGuid(),
            Name = name.Trim(),
            // ... inicialización
        };
        
        organization.AddOrganizationCreatedDomainEvent();
        return organization;
    }

    private void AddOrganizationCreatedDomainEvent()
    {
        var organizationCreatedDomainEvent = new OrganizationCreatedDomainEvent(
            TenantId, Id, Name, LegalName, TaxNumber, Address?.CountryCode!);
        this.AddDomainEvent(organizationCreatedDomainEvent);
    }
}
```

**Decisiones de diseño:**
- ✅ Constructor privado: solo se puede crear mediante `Create()` (factory method)
- ✅ Setters privados: inmutabilidad después de creación
- ✅ Validación en el factory: `TaxNumberType` no puede ser `Unknown`
- ✅ Evento de dominio añadido automáticamente en la creación
- ✅ Colección privada de contactos con propiedad read-only pública

#### OrganizationCreatedDomainEvent

```csharp
public record OrganizationCreatedDomainEvent(
    Guid TenantId,
    Guid OrganizationId,
    string Name,
    string LegalName,
    string TaxNumber,
    string CountryCode) : INotification;
```

**Características:**
- Implementado como `record` (inmutabilidad por defecto en C# 10+)
- Implementa `INotification` de MediatR
- Contiene solo datos relevantes del evento (no la entidad completa)

#### Value Objects

**Address** encapsula dirección como concepto sin identidad:

```csharp
public class Address : ValueObject
{
    public string Country { get; private set; }
    public string CountryCode { get; private set; }
    public string State { get; private set; }
    public string ZipCode { get; private set; }
    public string City { get; private set; }
    public string Street { get; private set; }

    public static Address Create(...)
    {
        return new Address
        {
            Country = country,
            CountryCode = countryCode.ToUpperInvariant(),
            // ...
        };
    }
}
```

---

### 2. **Application Layer (Capa de Aplicación)**

#### Commands (CQRS - Write Side)

**CreateOrganizationCommand**:
```csharp
public record CreateOrganizationCommand(
    string TaxIdNumber,
    string Name,
    string LegalName,
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode,
    OrganizationType OrganizationType,
    TaxNumberType TaxNumberType) : IRequest<bool>;
```

**CreateOrganizationCommandHandler**:
```csharp
public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    private readonly IOrganizationRepository _organizationRepository;

    public async Task<bool> Handle(CreateOrganizationCommand message, CancellationToken cancellationToken)
    {
        var address = Address.Create(...);
        var organization = Organization.Create(...);
        
        _organizationRepository.Add(organization);
        
        // El evento se despachará automáticamente en SaveEntitiesAsync
        return await _organizationRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }
}
```

**Flujo de ejecución:**
1. API recibe la petición HTTP
2. Se crea un `CreateOrganizationCommand`
3. MediatR enruta al `CommandHandler`
4. El handler crea el agregado `Organization`
5. El agregado añade `OrganizationCreatedDomainEvent` internamente
6. El repositorio guarda la entidad
7. En `SaveChanges`, los eventos de dominio se despachan **antes del commit**
8. Los handlers de eventos se ejecutan en la misma transacción

#### Queries (CQRS - Read Side)

**IOrganizationQueries** con implementación de solo lectura sobre EF Core (`OrganizationQueries`):

```csharp
public interface IOrganizationQueries
{
    Task<Organization?> GetOrganizationAsync(Guid id);
    Task<IEnumerable<OrganizationSummary>> GetAllOrganizationsAsync();
}
```

**Separación CQRS:**
- **Commands** modifican estado → pasan por el agregado → repositorio
- **Queries** solo leen → pueden acceder directamente a la DB (optimización)

#### MediatR Pipeline Behaviors

**LoggingBehavior** para logging cross-cutting:

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, ...)
    {
        _logger.LogInformation("Handling command {CommandName} ({@Command})", 
            request.GetGenericTypeName(), request);
        
        var response = await next();
        
        _logger.LogInformation("Command {CommandName} handled - response: {@Response}", 
            request.GetGenericTypeName(), response);

        return response;
    }
}
```

---

### 3. **Infrastructure Layer (Capa de Infraestructura)**

#### AccountContext (DbContext con Domain Events)

```csharp
public class AccountContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Despachar eventos de dominio ANTES del commit
        await _mediator.DispatchDomainEventsAsync(this);

        // 2. Guardar cambios en la base de datos
        await base.SaveChangesAsync(cancellationToken);

        return true;
    }
}
```

**Decisión arquitectónica crítica:**
Los eventos se despachan **ANTES** de `SaveChangesAsync()` para que:
- Los handlers de eventos se ejecuten en la **misma transacción**
- Si un handler falla, se hace rollback de todo
- Consistencia transaccional garantizada

#### MediatorExtension (Despacho de Eventos)

```csharp
public static async Task DispatchDomainEventsAsync(this IMediator mediator, AccountContext ctx)
{
    var domainEntities = ctx.ChangeTracker
        .Entries<Entity>()
        .Where(x => x.Entity.DomainEvents != null && x.Entity.DomainEvents.Any());

    var domainEvents = domainEntities
        .SelectMany(x => x.Entity.DomainEvents)
        .ToList();

    domainEntities.ToList()
        .ForEach(entity => entity.Entity.ClearDomainEvents());

    foreach (var domainEvent in domainEvents)
        await mediator.Publish(domainEvent);
}
```

**Proceso:**
1. Buscar todas las entidades con eventos de dominio
2. Extraer todos los eventos
3. Limpiar los eventos de las entidades
4. Publicar cada evento mediante MediatR (`Publish` permite múltiples handlers)

#### Repository Pattern

**IOrganizationRepository**:
```csharp
public interface IOrganizationRepository : IRepository<Organization>
{
    Organization Add(Organization organization);
    void Update(Organization organization);
}
```

**OrganizationRepository**:
```csharp
public class OrganizationRepository : IOrganizationRepository
{
    private readonly AccountContext _context;
    public IUnitOfWork UnitOfWork => _context;

    public Organization Add(Organization organization)
    {
        return _context.Organizations.Add(organization).Entity;
    }
}
```

**IUnitOfWork**:
```csharp
public interface IUnitOfWork : IDisposable
{
    Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default);
}
```

#### Entity Framework Migrations

**Migración inicial creada:**
```bash
dotnet ef migrations add Initial -c AccountContext
```

Resultado: esquema `account` con tablas:
- `account.Organization`
- `account.OrganizationContact`

#### Database Seeding

**AccountContextSeed** inicializa datos de prueba:
```csharp
public class AccountContextSeed : IDbSeeder<AccountContext>
{
    public async Task SeedAsync(AccountContext context)
    {
        if (!context.Organizations.Any())
        {
            // Crear organizaciones de ejemplo
        }
    }
}
```

---

### 4. **API Layer (Capa de API)**

#### Minimal APIs con Versionado

**AccountsApi.cs**:
```csharp
public static RouteGroupBuilder MapAccountsApiV1(this IEndpointRouteBuilder app)
{
    var api = app.MapGroup("api/accounts").HasApiVersion(1.0);
    
    api.MapPut("/", CreateOrganizationAsync);
    api.MapGet("/{id:guid}", GetOrganizationByIdAsync);
    api.MapGet("/", GetAllOrganizationsAsync);
    
    return api;
}
```

**Endpoints implementados:**
- `PUT /api/accounts` → Crear organización (Command)
- `GET /api/accounts/{id}` → Obtener organización por ID (Query)
- `GET /api/accounts` → Listar todas las organizaciones (Query)

#### Dependency Injection

**AccountsServices** encapsula dependencias:
```csharp
public class AccountsServices
{
    public IMediator Mediator { get; }
    public IOrganizationQueries Queries { get; }

    public AccountsServices(IMediator mediator, IOrganizationQueries queries)
    {
        Mediator = mediator;
        Queries = queries;
    }
}
```

**Uso en endpoints:**
```csharp
public static async Task<Results<Ok, BadRequest<string>>> CreateOrganizationAsync(
    CreateOrganizationRequest request,
    [AsParameters] AccountsServices services)
{
    var command = new CreateOrganizationCommand(...);
    var result = await services.Mediator.Send(command);
    
    return result ? TypedResults.Ok() : TypedResults.BadRequest("Failed to create organization");
}
```

---

## 🔧 Configuración y Registro de Servicios

### Extensions.cs

```csharp
public static void AddApplicationServices(this IHostApplicationBuilder builder)
{
    // DbContext con SQL Server
    builder.Services.AddDbContext<AccountContext>(options =>
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(Program).Assembly.FullName);
        });
    });

    // Migraciones automáticas en desarrollo
    builder.Services.AddMigration<AccountContext, AccountContextSeed>();

    // MediatR con behaviors
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    });

    // Repositorios y queries
    builder.Services.AddScoped<IOrganizationQueries, OrganizationQueries>();
    builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
}
```

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Aspire Service Defaults
builder.AddServiceDefaults();

// Servicios de la aplicación
builder.AddApplicationServices();

// API Versioning
var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

// Mapear endpoints
var accounts = app.NewVersionedApi("accounts");
accounts.MapAccountsApiV1();

app.MapDefaultEndpoints();
app.Run();
```

---

## 📊 Diagrama de Flujo de Domain Events

```
┌─────────────┐
│ HTTP Request│
└──────┬──────┘
       │
       ▼
┌──────────────────────┐
│  API Endpoint        │
│  (Minimal API)       │
└──────┬───────────────┘
       │
       │ CreateOrganizationCommand
       ▼
┌──────────────────────┐
│  MediatR Pipeline    │
│  (LoggingBehavior)   │
└──────┬───────────────┘
       │
       ▼
┌─────────────────────────────┐
│ CreateOrganizationHandler   │
│ 1. Create Organization      │
│ 2. Add to Repository        │
│ 3. SaveEntitiesAsync()      │
└──────┬──────────────────────┘
       │
       ▼
┌─────────────────────────────┐
│ AccountContext              │
│ (DbContext)                 │
└──────┬──────────────────────┘
       │
       ▼
┌─────────────────────────────┐
│ DispatchDomainEventsAsync   │
│ 1. Extract domain events    │
│ 2. Clear from entities      │
│ 3. Publish via MediatR      │
└──────┬──────────────────────┘
       │
       ▼
┌─────────────────────────────┐
│ Event Handlers              │
│ (OrganizationCreated...)    │
│ - Logging                   │
│ - Notifications             │
│ - Business logic            │
└──────┬──────────────────────┘
       │
       ▼
┌─────────────────────────────┐
│ SaveChangesAsync()          │
│ - Commit transaction        │
│ - Persist to SQL Server     │
└─────────────────────────────┘
```

---

## ✅ Verificación

### Ejecutar el proyecto

```bash
dotnet run --project src/uSLearn.AppHost
```

### Comprobaciones:

1. ✅ Dashboard de Aspire muestra `apiservice` corriendo
2. ✅ Base de datos SQL Server conectada
3. ✅ Migraciones aplicadas automáticamente
4. ✅ OpenAPI + interfaz Scalar disponibles en desarrollo (`/scalar/v1`)
5. ✅ Endpoints de la API funcionando

### Pruebas de API

**Crear organización:**
```bash
PUT https://localhost:7xxx/api/accounts
Content-Type: application/json

{
  "taxIdNumber": "B12345678",
  "taxNumberType": 1,
  "name": "Acme Corp",
  "legalName": "Acme Corporation S.L.",
  "street": "Main Street 123",
  "city": "Madrid",
  "state": "Madrid",
  "country": "Spain",
  "zipCode": "28001",
  "organizationType": 1
}
```

**Respuesta esperada:**
- Status: `200 OK`
- Logs: Evento `OrganizationCreatedDomainEvent` despachado
- Base de datos: Registro creado en `account.Organization`

**Obtener organización:**
```bash
GET https://localhost:7xxx/api/accounts/{id}
```

**Listar organizaciones:**
```bash
GET https://localhost:7xxx/api/accounts
```

---

## 🧪 Código Relevante

### Flujo Completo de Creación

1. **API recibe petición**
```csharp
// AccountsApi.cs
api.MapPut("/", CreateOrganizationAsync);
```

2. **Se crea el comando**
```csharp
var command = new CreateOrganizationCommand(
    request.TaxIdNumber,
    request.Name,
    // ...
);
```

3. **MediatR ejecuta el handler**
```csharp
var result = await services.Mediator.Send(command);
```

4. **Handler crea el agregado**
```csharp
var organization = Organization.Create(name, legalName, address, ...);
_organizationRepository.Add(organization);
```

5. **Agregado añade evento de dominio**
```csharp
private void AddOrganizationCreatedDomainEvent()
{
    var domainEvent = new OrganizationCreatedDomainEvent(TenantId, Id, Name, ...);
    this.AddDomainEvent(domainEvent);
}
```

6. **SaveEntitiesAsync despacha eventos**
```csharp
public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
{
    // Eventos se despachan ANTES del commit
    await _mediator.DispatchDomainEventsAsync(this);
    
    // Luego se persiste en la DB
    await base.SaveChangesAsync(cancellationToken);
    
    return true;
}
```

---

## 🔮 Consideraciones Futuras

### Stage.03: Integration Events + Idempotency

En la próxima etapa se añadirá:

1. **Integration Events**
   - Publicar `OrganizationCreatedIntegrationEvent` a un message broker
   - Otros servicios podrán suscribirse a estos eventos
   - Comunicación asíncrona entre bounded contexts

2. **Outbox Pattern**
   - Tabla `IntegrationEventLog` para garantizar entrega
   - Publicación de eventos mediante un proceso background
   - Consistencia eventual entre servicios

3. **Idempotent Event Handlers**
   - Evitar procesamiento duplicado de eventos
   - Tabla de `ProcessedEvents` para tracking
   - Garantías at-least-once delivery

### Extensibilidad de Domain Events

Actualmente no hay handlers de `OrganizationCreatedDomainEvent`, pero se pueden añadir fácilmente:

```csharp
public class OrganizationCreatedDomainEventHandler 
    : INotificationHandler<OrganizationCreatedDomainEvent>
{
    public async Task Handle(OrganizationCreatedDomainEvent notification, ...)
    {
        // Crear usuario admin por defecto
        // Enviar email de bienvenida
        // Inicializar configuración
        // ...
    }
}
```

MediatR automáticamente ejecutará todos los handlers registrados.

### Preparación para Microservicios

Esta estructura está lista para:
- Añadir nuevos agregados (User, Subscription, etc.)
- Crear nuevos microservicios con la misma estructura
- Comunicarse mediante Integration Events
- Aplicar Event Sourcing si es necesario

---

## 📚 Recursos Adicionales

- [Domain Events Pattern - Microsoft](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)
- [MediatR Documentation](https://github.com/jbogard/MediatR)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [CQRS Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)
- [Repository Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)

---

## 🧩 Próximos Pasos

➡️ **Stage.03**: Eventos de Integración + Idempotencia

En la siguiente etapa se introducirán:
- Integration Events para comunicación entre servicios
- Message broker (RabbitMQ/Azure Service Bus)
- Outbox pattern para consistencia eventual
- Idempotent consumers
- Abstracción `EventBus` como building block compartido

---

## 📝 Resumen

En esta etapa se ha logrado:

✅ Microservicio Accounts con arquitectura DDD  
✅ Domain Events implementados  
✅ MediatR para despacho in-process  
✅ Entity Framework Core con migraciones  
✅ Patrón Repository + Unit of Work  
✅ CQRS básico (Commands/Queries separados)  
✅ Minimal APIs con versionado  
✅ Logging pipeline con MediatR behaviors  
✅ Agregado Organization con validaciones  
✅ Value Objects (Address)  
✅ Eventos despachados en la misma transacción  

**Resultado**: Un microservicio funcional con arquitectura rica en dominio, listo para evolucionar hacia comunicación distribuida mediante Integration Events en la siguiente etapa.
