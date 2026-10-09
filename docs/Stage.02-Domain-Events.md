# Stage.02 - Domain Events

## 🎯 Stage Goal

Implement **domain events** inside the accounts microservice (Accounts) following **Domain-Driven Design (DDD)** principles, laying the groundwork for a rich-domain architecture with:

- An `Organization` aggregate with encapsulated business logic
- Domain events that represent meaningful state changes in the domain
- MediatR to dispatch events within the same bounded context
- Clear separation between layers (Domain, Application, Infrastructure, API)
- Entity Framework Core implementing the Repository and Unit of Work patterns
- Basic CQRS, separating commands from queries

---

## 🏗️ Architectural Decisions

### Why domain events?

**Domain events** are central to a DDD architecture because they:

- **Decouple** business logic from side effects
- Enable **extensibility** without touching the aggregate
- Make important domain changes **explicit**
- Help with **auditing** and change tracking
- Are the foundation for integration events (Stage.03)

### Domain events vs integration events

| Aspect | Domain events | Integration events |
|--------|--------------|-------------------|
| **Scope** | Same bounded context | Across bounded contexts |
| **Transaction** | Same DB transaction | Distributed (eventual consistency) |
| **Infrastructure** | MediatR (in-process) | Message broker (RabbitMQ, Azure Service Bus) |
| **Purpose** | Keep internal consistency | Communication between services |

This stage implements **domain events**. **Integration events** arrive in Stage.03.

### Accounts microservice structure

A **layered architecture** inspired by DDD:

```
uSLearn.Accounts.API/
├── Domain/                          → Pure business logic
│   ├── SeedWork/                    → DDD building blocks
│   ├── OrganizationAggregate/       → Organization aggregate
│   ├── Events/                      → Domain events
│   └── Exceptions/                  → Domain exceptions
├── Application/                     → Use cases (commands/queries)
│   ├── Commands/                    → Command handlers (CQRS)
│   ├── Queries/                     → Query handlers (CQRS)
│   └── Behaviors/                   → MediatR behaviors
├── Infrastructure/                  → Persistence and technical details
│   ├── EntityConfigurations/        → EF Core configurations
│   ├── Repositories/                → Repository implementations
│   ├── Migrations/                  → EF Core migrations
│   ├── Seed/                        → Seed data
│   └── Extensions/                  → Extensions and utilities
├── Api/                             → HTTP endpoints
└── Extensions/                      → Service registration
```

### DDD principles applied

1. **Aggregate root**: `Organization` is the aggregate root that controls access to `OrganizationContact`
2. **Value objects**: `Address` represents a domain concept with no identity
3. **Domain events**: `OrganizationCreatedDomainEvent` signals that an organization was created
4. **Ubiquitous language**: types such as `OrganizationType` and `TaxNumberType` express the business language
5. **Invariants**: validation lives in the aggregate (e.g. `TaxNumberType` cannot be `Unknown`)

---

## 📦 Implemented Components

### 1. **Domain Layer**

#### Entity base class

The `Entity` base class provides:
- Identity through a `Guid Id`
- Domain event management through an internal collection
- Methods to add, remove and clear events
- Identity-based equality

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

**Key points:**
- Events are stored as `INotification` (MediatR interface)
- The collection is exposed read-only through `IReadOnlyCollection`
- Events are dispatched in `SaveChanges`, before the commit

#### Organization aggregate root

The `Organization` aggregate:

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
            // ... initialization
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

**Design decisions:**
- ✅ Private constructor: instances are only created through `Create()` (factory method)
- ✅ Private setters: immutable after creation
- ✅ Validation in the factory: `TaxNumberType` cannot be `Unknown`
- ✅ The domain event is added automatically on creation
- ✅ Private contacts collection with a read-only public property

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

**Characteristics:**
- Implemented as a `record` (immutable by default)
- Implements MediatR's `INotification`
- Carries only the data relevant to the event (not the whole entity)

#### Value objects

**Address** encapsulates an address as a concept without identity:

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

### 2. **Application Layer**

#### Commands (CQRS write side)

**CreateOrganizationCommand** (simplified; the real one is a `[DataContract]` record that also carries `CountryCode`, `Language` and `LanguageCode`):
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
        
        // The event is dispatched automatically in SaveEntitiesAsync
        return await _organizationRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }
}
```

**Execution flow:**
1. The API receives the HTTP request
2. A `CreateOrganizationCommand` is created
3. MediatR routes it to the command handler
4. The handler creates the `Organization` aggregate
5. The aggregate adds `OrganizationCreatedDomainEvent` internally
6. The repository adds the entity
7. In `SaveEntitiesAsync`, domain events are dispatched **before the commit**
8. Event handlers run within the same unit of work

#### Queries (CQRS read side)

**IOrganizationQueries**, implemented read-only on top of EF Core (`OrganizationQueries`):

```csharp
public interface IOrganizationQueries
{
    Task<Organization?> GetOrganizationAsync(Guid id);
    Task<IEnumerable<OrganizationSummary>> GetAllOrganizationsAsync();
}
```

**CQRS separation:**
- **Commands** change state → go through the aggregate → repository
- **Queries** only read → can access the database directly (optimization)

#### MediatR pipeline behaviors

**LoggingBehavior** for cross-cutting logging:

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

### 3. **Infrastructure Layer**

#### AccountContext (DbContext with domain events)

```csharp
public class AccountContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Dispatch domain events BEFORE the commit
        await _mediator.DispatchDomainEventsAsync(this);

        // 2. Save changes to the database
        await base.SaveChangesAsync(cancellationToken);

        return true;
    }
}
```

**Key architectural decision:**
Events are dispatched **BEFORE** `SaveChangesAsync()` so that:
- Event handlers' changes made through the same `DbContext` are saved together
- If a handler fails, nothing is saved
- Transactional consistency is guaranteed

#### MediatorExtension (event dispatching)

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

**Process:**
1. Find every entity with domain events
2. Extract all events
3. Clear the events from the entities
4. Publish each event through MediatR (`Publish` supports multiple handlers)

#### Repository pattern

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

#### Entity Framework migrations

**Initial migration:**
```bash
dotnet ef migrations add Initial -c AccountContext
```

Result: `account` schema with the tables:
- `account.Organization`
- `account.OrganizationContact`

#### Database seeding

**AccountContextSeed** seeds test data:
```csharp
public class AccountContextSeed : IDbSeeder<AccountContext>
{
    public async Task SeedAsync(AccountContext context)
    {
        if (!context.Organizations.Any())
        {
            // Create sample organizations
        }
    }
}
```

---

### 4. **API Layer**

#### Versioned minimal APIs

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

**Endpoints:**
- `PUT /api/accounts` → Create organization (command)
- `GET /api/accounts/{id}` → Get organization by ID (query)
- `GET /api/accounts` → List all organizations (query)

#### Dependency injection

**AccountsServices** groups the dependencies:
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

**Usage in endpoints:**
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

## 🔧 Service Configuration and Registration

### Extensions.cs

```csharp
public static void AddApplicationServices(this IHostApplicationBuilder builder)
{
    // DbContext with SQL Server
    builder.Services.AddDbContext<AccountContext>(options =>
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(Program).Assembly.FullName);
        });
    });

    // Automatic migrations at startup
    builder.Services.AddMigration<AccountContext, AccountContextSeed>();

    // MediatR with behaviors
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    });

    // Repositories and queries
    builder.Services.AddScoped<IOrganizationQueries, OrganizationQueries>();
    builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
}
```

### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults
builder.AddServiceDefaults();

// Application services
builder.AddApplicationServices();

// API versioning
var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

// Map endpoints
var accounts = app.NewVersionedApi("accounts");
accounts.MapAccountsApiV1();

app.MapDefaultEndpoints();
app.Run();
```

---

## 📊 Domain Events Flow

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

## ✅ Verification

### Run the project

```bash
dotnet run --project src/uSLearn.AppHost
```

### Checks:

1. ✅ The Aspire dashboard shows `apiservice` running
2. ✅ SQL Server database connected
3. ✅ Migrations applied automatically
4. ✅ OpenAPI + Scalar UI available in development (`/scalar/v1`)
5. ✅ API endpoints working

### API tests

**Create organization:**
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

**Expected response:**
- Status: `200 OK`
- Logs: `OrganizationCreatedDomainEvent` dispatched
- Database: row created in `account.Organization`

**Get organization:**
```bash
GET https://localhost:7xxx/api/accounts/{id}
```

**List organizations:**
```bash
GET https://localhost:7xxx/api/accounts
```

---

## 🧪 Relevant Code

### End-to-end creation flow

1. **The API receives the request**
```csharp
// AccountsApi.cs
api.MapPut("/", CreateOrganizationAsync);
```

2. **The command is created**
```csharp
var command = new CreateOrganizationCommand(
    request.TaxIdNumber,
    request.Name,
    // ...
);
```

3. **MediatR runs the handler**
```csharp
var result = await services.Mediator.Send(command);
```

4. **The handler creates the aggregate**
```csharp
var organization = Organization.Create(name, legalName, address, ...);
_organizationRepository.Add(organization);
```

5. **The aggregate adds the domain event**
```csharp
private void AddOrganizationCreatedDomainEvent()
{
    var domainEvent = new OrganizationCreatedDomainEvent(TenantId, Id, Name, ...);
    this.AddDomainEvent(domainEvent);
}
```

6. **SaveEntitiesAsync dispatches the events**
```csharp
public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
{
    // Events are dispatched BEFORE the commit
    await _mediator.DispatchDomainEventsAsync(this);
    
    // Then everything is persisted
    await base.SaveChangesAsync(cancellationToken);
    
    return true;
}
```

---

## 🔮 Future Considerations

### Stage.03: Integration events + idempotency

The next stage adds:

1. **Integration events**
   - Publish `OrganizationCreatedIntegrationEvent` to a message broker
   - Other services can subscribe to these events
   - Asynchronous communication between bounded contexts

2. **Outbox pattern**
   - `IntegrationEventLog` table to make publishing reliable
   - Events published only after the commit
   - Eventual consistency between services

3. **Idempotent event handlers**
   - Avoid processing the same event twice
   - A processed-events table for tracking
   - At-least-once delivery semantics

### Extending domain events

There are no `OrganizationCreatedDomainEvent` handlers yet, but they are easy to add:

```csharp
public class OrganizationCreatedDomainEventHandler 
    : INotificationHandler<OrganizationCreatedDomainEvent>
{
    public async Task Handle(OrganizationCreatedDomainEvent notification, ...)
    {
        // Create a default admin user
        // Send a welcome email
        // Initialize configuration
        // ...
    }
}
```

MediatR runs every registered handler automatically.

### Getting ready for microservices

This structure is ready to:
- Add new aggregates (User, Subscription, etc.)
- Create new microservices with the same structure
- Communicate through integration events
- Apply event sourcing if needed

---

## 📚 Additional Resources

- [Domain events pattern - Microsoft](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)
- [MediatR](https://github.com/jbogard/MediatR)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [CQRS pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs)
- [Repository pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)

---

## 🧩 Next Steps

➡️ **Stage.03**: Integration events + idempotency

The next stage introduces:
- Integration events for service-to-service communication
- A message broker (RabbitMQ)
- The outbox pattern for eventual consistency
- Idempotent consumers
- An `EventBus` abstraction as a shared building block

---

## 📝 Summary

This stage delivers:

✅ Accounts microservice with a DDD architecture  
✅ Domain events  
✅ MediatR for in-process dispatching  
✅ Entity Framework Core with migrations  
✅ Repository + Unit of Work patterns  
✅ Basic CQRS (separate commands and queries)  
✅ Versioned minimal APIs  
✅ Logging pipeline with MediatR behaviors  
✅ Organization aggregate with validation  
✅ Value objects (Address)  
✅ Events dispatched within the same unit of work  

**Result**: a working microservice with a rich domain model, ready to evolve into distributed communication through integration events in the next stage.
