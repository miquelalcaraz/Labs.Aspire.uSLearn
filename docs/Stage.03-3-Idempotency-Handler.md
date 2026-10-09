# Stage.03-3 - Idempotent Event Handlers

## 🎯 Goal

Implement idempotency at the handler level so that integration events are never processed more than once by the same handler, avoiding duplicate data and unwanted behavior in the distributed system.

---

## 🔀 Structural Changes in This Stage

- The building blocks are renamed with the `Core.` prefix (`Core.Domain`, `Core.EventBus`, `Core.EventBusRabbitMQ`, `Core.Infrastructure`, `Core.IntegrationEventLogEF`) and their namespaces move to `uSLearn.Core.*`. The domain `SeedWork` moves out of `Accounts.API` into `Core.Domain` so it can be reused.
- `Identity.API` gets its own database (`identitydb`, `identity` schema) through `IdentityContext`.
- The handler now creates the organization's **tenant** and **admin user**. At this stage both are stored in **in-memory repositories** (singletons): they are lost on restart and don't share a transaction with the idempotency record. They move to EF Core in Stage.03-4.

---

## 🏗️ Architectural Decisions

### Why idempotency at the handler level?

In event-driven distributed systems it is essential that:

- **Duplicate events are not processed**: the message broker may deliver the same message more than once (at-least-once delivery)
- **Operations are idempotent**: processing the same event several times has the same result as processing it once
- **Granularity is per handler**: different handlers may need to process the same event, so tracking is per `EventId + HandlerName` combination

### Solution architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Integration Event Handler                                  │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 1. Receive event                                       │ │
│  │ 2. Check whether it was processed (IsProcessedAsync)   │ │
│  │ 3. If duplicate → skip                                 │ │
│  │ 4. Run business logic                                  │ │
│  │ 5. Mark as processed (MarkAsProcessedAsync)            │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
         ┌─────────────────────────────────┐
         │  EventIdempotencyService        │
         │  ┌───────────────────────────┐  │
         │  │ IsProcessedAsync()        │  │
         │  │ MarkAsProcessedAsync()    │  │
         │  └───────────────────────────┘  │
         └─────────────────────────────────┘
                           │
                           ▼
         ┌─────────────────────────────────┐
         │  ProcessedIntegrationEvents     │
         │  ┌───────────────────────────┐  │
         │  │ EventId (PK)              │  │
         │  │ HandlerName (PK)          │  │
         │  │ ProcessedAt               │  │
         │  │ EventType                 │  │
         │  └───────────────────────────┘  │
         └─────────────────────────────────┘
```

---

## 📝 Implemented Components

### 1. **ProcessedIntegrationEvent** - tracking entity

Records which events have been processed by which handlers:

```csharp
public class ProcessedIntegrationEvent
{
    public Guid EventId { get; set; }          // ID of the original event
    public string HandlerName { get; set; }    // Handler name
    public DateTime ProcessedAt { get; set; }  // Processing timestamp
    public string? EventType { get; set; }     // Event type (auditing)
}
```

**Composite key**: `EventId + HandlerName` lets different handlers process the same event.

### 2. **IEventIdempotencyService** - service contract

```csharp
public interface IEventIdempotencyService
{
    Task<bool> IsProcessedAsync(Guid eventId, string handlerName);
    Task MarkAsProcessedAsync(Guid eventId, string handlerName, string? eventType = null);
}
```

### 3. **EventIdempotencyService<TContext>** - implementation

Generic service that works with any `DbContext`:

```csharp
public class EventIdempotencyService<TContext> : IEventIdempotencyService 
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly ILogger<IEventIdempotencyService> _logger;

    public async Task<bool> IsProcessedAsync(Guid eventId, string handlerName)
    {
        return await _context.Set<ProcessedIntegrationEvent>()
            .AnyAsync(e => e.EventId == eventId && e.HandlerName == handlerName);
    }

    public async Task MarkAsProcessedAsync(Guid eventId, string handlerName, string? eventType = null)
    {
        var processedEvent = new ProcessedIntegrationEvent
        {
            EventId = eventId,
            HandlerName = handlerName,
            ProcessedAt = DateTime.UtcNow,
            EventType = eventType
        };
        
        _context.Set<ProcessedIntegrationEvent>().Add(processedEvent);
        await _context.SaveChangesAsync();
    }
}
```

### 4. **DbContext configuration**

Through the `UseEventIdempotency()` extension method:

```csharp
public class IdentityContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.UseEventIdempotency();  // ← Registers the table and its configuration
    }
}
```

**Extension method**:

```csharp
public static void UseEventIdempotency(this ModelBuilder builder)
{
    builder.Entity<ProcessedIntegrationEvent>(builder =>
    {
        builder.ToTable("ProcessedIntegrationEvents");
        builder.HasKey(e => new { e.EventId, e.HandlerName }); // Composite key
        builder.Property(e => e.HandlerName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.ProcessedAt).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(255);
        builder.HasIndex(e => e.EventId);
        builder.HasIndex(e => e.ProcessedAt);
    });
}
```

---

## 🔄 Step-by-Step Processing Flow

### Example: `OrganizationCreatedIntegrationEventHandler`

```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    private readonly IEventIdempotencyService _idempotencyService;

    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        var handlerName = GetType().Name; // "OrganizationCreatedIntegrationEventHandler"

        // ✅ STEP 1: Check whether it was already processed
        if (await _idempotencyService.IsProcessedAsync(@event.Id, handlerName))
        {
            _logger.LogWarning("Event {EventId} already processed. Skipping duplicate.", @event.Id);
            return; // ← Exit without processing
        }

        // ✅ STEP 2: Run business logic
        var tenant = await CreateTenantAsync(@event);
        var adminUser = await CreateAdminUserAsync(@event, tenant.Id);

        // ✅ STEP 3: Mark as processed
        await _idempotencyService.MarkAsProcessedAsync(
            @event.Id, 
            handlerName, 
            @event.GetType().Name);

        _logger.LogInformation("Tenant {TenantId} and Admin User {UserId} created", 
            tenant.Id, adminUser.Id);
    }
}
```

### Duplicate scenario

**Without idempotency:**
```
Duplicate event → duplicate tenant → duplicate admin user → ❌ constraint error or duplicate data
```

**With idempotency:**
```
Duplicate event → IsProcessedAsync() returns true → processing skipped → ✅ no side effects
```

---

## 🗄️ Database Schema

```sql
CREATE TABLE identity.ProcessedIntegrationEvents (
    EventId uniqueidentifier NOT NULL,
    HandlerName nvarchar(255) NOT NULL,
    ProcessedAt datetime2 NOT NULL,
    EventType nvarchar(255) NULL,
    CONSTRAINT PK_ProcessedIntegrationEvents PRIMARY KEY (EventId, HandlerName)
);

CREATE INDEX IX_ProcessedIntegrationEvents_EventId ON identity.ProcessedIntegrationEvents (EventId);
CREATE INDEX IX_ProcessedIntegrationEvents_ProcessedAt ON identity.ProcessedIntegrationEvents (ProcessedAt);
```

**Indexes:**
- **EventId**: fast lookups by event
- **ProcessedAt**: useful for cleanup/archiving jobs

---

## ⚙️ Service Registration

### In Identity.API

```csharp
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<IdentityContext>>();
```

### In other microservices

Every API that consumes events registers the service with its own `DbContext`:

```csharp
// In Accounts.API
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<AccountContext>>();

// In a future Courses.API (hypothetical example)
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<CourseContext>>();
```

---

## ✅ Practical Verification

### 1. Migration

The table is created by `IdentityContext`'s `Initial` migration (`src/uSLearn.Identity.API/Infrastructure/Migrations`). There's no need to apply it by hand: `AddMigration<IdentityContext, IdentityContextSeed>()` migrates the database when the service starts.

```bash
dotnet run --project src/uSLearn.AppHost
```

### 2. Check the table

```sql
SELECT * FROM [identity].ProcessedIntegrationEvents;
```

### 3. Create an organization

A `PUT /api/accounts` (see Stage.03-2) publishes `OrganizationCreatedIntegrationEvent`; `Identity.API` processes it and records it in `ProcessedIntegrationEvents`.

### 4. Simulate a duplicate event (optional)

Deliver the same event twice from RabbitMQ:

**First time:**
```
✅ Process event → create tenant and admin user → record in ProcessedIntegrationEvents
```

**Second time (duplicate):**
```
✅ Event already processed → skip → log: "Event already processed. Skipping duplicate."
```

### 5. Check the logs

```
[Information] Identity - Processing organization created event: 123e4567-e89b-12d3-a456-426614174000
[Information] Marked event 123e4567-e89b-12d3-a456-426614174000 as processed by OrganizationCreatedIntegrationEventHandler
[Warning] Identity - Event 123e4567-e89b-12d3-a456-426614174000 already processed. Skipping duplicate.
```

---

## 🔍 Important Considerations

### Transactionality

⚠️ **Potential problem**: `MarkAsProcessedAsync()` runs in a separate save, so the business logic and the idempotency record are not atomic, and race conditions are possible.

**Solution (Stage.03-4)**: use `ResilientTransaction` so the business logic and the idempotency record are committed in the same transaction.

### Cleaning up historical data

The `ProcessedIntegrationEvents` table grows over time. Options:

- **Archiving**: move old events (> 90 days) to a history table
- **Purging**: delete very old events if they aren't needed for auditing
- **Scheduled job**: a background job for automatic cleanup

### HandlerName granularity

We currently use `GetType().Name`, which means:

```
"OrganizationCreatedIntegrationEventHandler"
```

If the class is renamed, it is treated as a new handler. Alternatives:

- Use the full name with namespace: `GetType().FullName`
- Use a static identifier: `const string HandlerName = "OrganizationCreated.Identity"`

---

## 🚀 Next Steps

### Stage.03-4: Resilient transactions

- Move the tenant and admin user from the in-memory repositories to EF Core.
- Use `ResilientTransaction` so the business logic and the idempotency record are committed in a single atomic transaction, with retries on transient failures.

---

## 📚 References

- [Idempotent consumer](https://microservices.io/patterns/communication-style/idempotent-consumer.html)
- [Messaging patterns](https://learn.microsoft.com/azure/architecture/patterns/category/messaging)
- [EF Core composite keys](https://learn.microsoft.com/ef/core/modeling/keys)
