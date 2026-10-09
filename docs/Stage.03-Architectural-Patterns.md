# Stage.03 - Architectural Patterns in Depth

> 📖 **Companion to:** [`Stage.03-2-Idempotency.md`](Stage.03-2-Idempotency.md)  
> This document goes deeper into the architectural patterns, with diagrams, code samples and detailed analysis.

## 📚 Index of Implemented Patterns

| Pattern | Problem it solves | Section |
|--------|----------------------|---------|
| **Transactional Outbox** | Inconsistency between the DB and published events | [See](#1️⃣-transactional-outbox-pattern) |
| **Idempotent Consumer** | Duplicate command processing | [See](#2️⃣-idempotent-consumer-pattern) |
| **Unit of Work** | Non-atomic operations | [See](#3️⃣-unit-of-work-pattern) |
| **Pipeline Behavior** | Duplicated cross-cutting concerns | [See](#4️⃣-pipeline-behavior-pattern) |
| **Eventual Consistency** | Complex distributed transactions | [See](#5️⃣-eventual-consistency-pattern) |

---

## 1️⃣ Transactional Outbox Pattern

### 🎯 Problem

In a distributed system with integration events there is a critical race condition:

```
❌ PROBLEMATIC SCENARIO:

1. Command creates Organization in the DB
2. Event published to RabbitMQ          ← Published BEFORE the commit
3. DB commits
4. ❌ Commit fails (deadlock, constraint violation, etc.)

Result: the event was sent but the entity was never created
        Other services receive data from an operation that failed
```

### ✅ Solution

Store events in a table **within the same transaction** as the domain entities:

```
✅ CORRECT FLOW WITH OUTBOX:

┌─────────────────────────────────────────┐
│  BEGIN TRANSACTION                      │
├─────────────────────────────────────────┤
│                                         │
│  1. INSERT INTO Organization (...)      │
│                                         │
│  2. INSERT INTO IntegrationEventLog     │
│     - EventId: guid                     │
│     - TransactionId: tx-guid            │
│     - State: NotPublished               │
│     - Content: JSON serialized event    │
│                                         │
│  3. COMMIT                              │ ← Both operations are atomic
│                                         │
└─────────────────────────────────────────┘

4. After a successful commit:
   - Retrieve events by TransactionId
   - Publish to RabbitMQ
   - Mark as Published
```

### 🔧 Implementation in the Code

**AccountIntegrationEventService.cs:**

```csharp
// Phase 1: Save the event (inside the transaction)
public async Task AddAndSaveEventAsync(IntegrationEvent evt)
{
    // GetCurrentTransaction() makes sure the active transaction is used
    await _eventLogService.SaveEventAsync(evt, _accountContext.GetCurrentTransaction());
}

// Phase 2: Publish events (after the commit)
public async Task PublishEventsThroughEventBusAsync(Guid transactionId)
{
    // Only the events of this specific transaction
    var pendingLogEvents = await _eventLogService
        .RetrieveEventLogsPendingToPublishAsync(transactionId);

    foreach (var logEvt in pendingLogEvents)
    {
        await _eventLogService.MarkEventAsInProgressAsync(logEvt.EventId);
        await _eventBus.PublishAsync(logEvt.IntegrationEvent);
        await _eventLogService.MarkEventAsPublishedAsync(logEvt.EventId);
    }
}
```

### 📊 Event States

```
┌─────────────────┐
│  NotPublished   │ ← Event saved in the DB
└────────┬────────┘
         │
         │ PublishEventsThroughEventBusAsync()
         ↓
┌─────────────────┐
│   InProgress    │ ← Publishing in progress
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
    ↓         ↓
┌─────────┐  ┌───────────────┐
│Published│  │PublishedFailed│
└─────────┘  └───────────────┘
```

### ⚡ Benefits

- ✅ **Atomicity**: domain + events are persisted together, or not at all
- ✅ **Consistency**: DB state = stored events
- ✅ **Durability**: events survive process failures
- ⚠️ **At-least-once delivery (pending)**: failed events stay as `PublishedFailed`. Guaranteeing delivery would require a background process that republishes them; it is not implemented yet

---

## 2️⃣ Idempotent Consumer Pattern

### 🎯 Problem

Clients may send the same command several times:

```
❌ PROBLEMATIC SCENARIO:

Client → CreateOrganizationCommand
           ↓
         API processing... ⏳ (takes 5 seconds)
           ↓
Client timeout (3s) → Retries ❌
           ↓
         API creates Organization #1 ✅
           ↓
         API creates Organization #2 ❌ (DUPLICATE)

Result: 2 organizations created for 1 request
```

### ✅ Solution

The client generates a **unique request ID** and sends it with every command:

```
✅ FLOW WITH IDEMPOTENCY:

Client generates: RequestId = "a1b2c3d4..."

Attempt 1:
  → IdentifiedCommand(CreateOrganizationCommand, "a1b2c3d4")
  → RequestManager.ExistAsync("a1b2c3d4") → FALSE
  → Saves the request ID in the DB
  → Processes the command
  → Organization created ✅

Attempt 2 (retry after timeout):
  → IdentifiedCommand(CreateOrganizationCommand, "a1b2c3d4")
  → RequestManager.ExistAsync("a1b2c3d4") → TRUE ✅
  → Returns CreateResultForDuplicateRequest()
  → Does NOT process the command
  → Returns success without creating a duplicate ✅
```

### 🔧 Implementation in the Code

**IdentifiedCommandHandler.cs:**

```csharp
public async Task<R> Handle(IdentifiedCommand<T, R> message, CancellationToken cancellationToken)
{
    // 1. Check whether it was already processed
    var alreadyExists = await _requestManager.ExistAsync(message.Id);
    
    if (alreadyExists)
    {
        return CreateResultForDuplicateRequest(); // ← Idempotency
    }

    // 2. Register the request ID (marks it as being processed)
    await _requestManager.CreateRequestForCommandAsync<T>(message.Id);

    // 3. Process the command
    var result = await _mediator.Send(message.Command, cancellationToken);

    return result;
}
```

**ClientRequestConfiguration.cs:**

```csharp
public void Configure(EntityTypeBuilder<ClientRequest> requestConfiguration)
{
    requestConfiguration.ToTable("requests");
    requestConfiguration.HasKey(r => r.Id);
    requestConfiguration.HasIndex(r => r.Id).IsUnique(); // ← Last line of defense
}
```

### 📊 Requests Table

```sql
SELECT * FROM [account].[requests];

Id                                    | Name                         | Time
--------------------------------------|------------------------------|--------------------
a1b2c3d4-5678-90ab-cdef-123456789abc  | CreateOrganizationCommand   | 2026-03-06 10:30:15
b2c3d4e5-6789-01bc-def0-234567890bcd  | CreateOrganizationCommand   | 2026-03-06 10:31:42
```

### ⚠️ Production Considerations

**Problem: the requests table grows forever**

Solutions:
1. **TTL (time to live)**: periodic cleanup of old requests (proposal, not implemented)
   ```csharp
   // Background job (sketch)
   var cutoffDate = DateTime.UtcNow.AddDays(-30);
   await _context.Set<ClientRequest>()
       .Where(r => r.Time < cutoffDate)
       .ExecuteDeleteAsync();
   ```

2. **Partitioning**: partition the table by date for better performance

3. **Cached response**: store the result next to the request ID (proposal)
   ```csharp
   public class ClientRequest
   {
       public Guid Id { get; set; }
       public string Name { get; set; }
       public DateTime Time { get; set; }
       public string Result { get; set; } // ← Result as JSON
   }
   ```

### ⚡ Benefits

- ✅ **One execution per request ID**: the command runs only once
- ✅ **No duplicates**: several retries → same result
- ✅ **Client responsibility**: the client controls uniqueness
- ✅ **Traceability**: history of processed requests

---

## 3️⃣ Unit of Work Pattern

### 🎯 Problem

Several DB operations must be atomic:

```
❌ WITHOUT UNIT OF WORK:

public async Task Handle(CreateOrganizationCommand request)
{
    // Operation 1
    var org = new Organization(...);
    await _context.Organizations.AddAsync(org);
    await _context.SaveChangesAsync(); // ← Commit 1

    // Operation 2
    var contact = new OrganizationContact(...);
    await _context.OrganizationContacts.AddAsync(contact);
    await _context.SaveChangesAsync(); // ← Commit 2 ❌ May fail

    // Operation 3
    await _eventLogService.SaveEventAsync(...);
    await _context.SaveChangesAsync(); // ← Commit 3 ❌ May fail

    // Result: Organization created but Contact and Event not
}
```

### ✅ Solution

TransactionBehavior wraps ALL the processing in one transaction:

```
✅ WITH UNIT OF WORK (TransactionBehavior):

┌──────────────────────────────────────────┐
│  BEGIN TRANSACTION                       │
├──────────────────────────────────────────┤
│                                          │
│  CommandHandler runs:                    │
│    ├─ Organizations.Add(org)             │
│    ├─ OrganizationContacts.Add(contact)  │
│    └─ EventLog.Add(event)                │
│                                          │
│  COMMIT                                  │ ← ALL or NOTHING
│                                          │
└──────────────────────────────────────────┘
```

### 🔧 Implementation in the Code

**TransactionBehavior.cs:**

```csharp
public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, ...)
{
    // Avoid nested transactions
    if (_dbContext.HasActiveTransaction)
    {
        return await next();
    }

    // Use an ExecutionStrategy for resilience
    var strategy = _dbContext.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
        Guid transactionId;

        // Begin transaction
        await using var transaction = await _dbContext.BeginTransactionAsync();
        
        _logger.LogInformation("Begin transaction {TransactionId}", transaction.TransactionId);

        // Run the handler (may change several aggregates)
        var response = await next();

        // Commit ALL operations
        await _dbContext.CommitTransactionAsync(transaction);
        transactionId = transaction.TransactionId;

        // Publish events ONLY if the commit succeeded
        await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

        return response;
    });
}
```

### 📊 ExecutionStrategy

Retries automatically on transient failures **as long as the `DbContext` has `EnableRetryOnFailure`**. Without that option, `CreateExecutionStrategy()` returns a strategy that never retries. It isn't enabled at this stage yet: it arrives with Stage.03-4 in Identity and after Stage.04 in Accounts.

```
Attempt 1: BEGIN → Operations → COMMIT → ❌ Deadlock
           ↓ Wait 1s
Attempt 2: BEGIN → Operations → COMMIT → ❌ Timeout
           ↓ Wait 2s
Attempt 3: BEGIN → Operations → COMMIT → ✅ Success
```

### ⚡ Benefits

- ✅ **Atomicity**: all operations or none
- ✅ **Isolation**: changes aren't visible until the commit
- ✅ **Consistency**: the DB state is always valid
- ✅ **Resilience**: ready for retries on transient failures (requires `EnableRetryOnFailure`)
- ✅ **Centralization**: transaction logic outside the handlers

---

## 4️⃣ Pipeline Behavior Pattern

### 🎯 Problem

Cross-cutting concerns duplicated in every handler:

```
❌ WITHOUT PIPELINE BEHAVIORS:

public class CreateOrganizationHandler
{
    public async Task<bool> Handle(CreateOrganizationCommand request)
    {
        // Logging (duplicated in every handler)
        _logger.LogInformation("Handling {Command}", nameof(CreateOrganizationCommand));

        // Transaction (duplicated in every handler)
        await using var transaction = await _dbContext.BeginTransactionAsync();

        try
        {
            // Business logic
            var org = new Organization(...);
            await _repository.AddAsync(org);

            // Commit (duplicated in every handler)
            await _dbContext.CommitTransactionAsync(transaction);

            // Logging (duplicated in every handler)
            _logger.LogInformation("Command handled successfully");

            return true;
        }
        catch (Exception ex)
        {
            // Error handling (duplicated in every handler)
            _logger.LogError(ex, "Error handling command");
            throw;
        }
    }
}
```

### ✅ Solution

Behaviors intercept commands and add cross-cutting functionality:

```
✅ WITH PIPELINE BEHAVIORS:

Request
  │
  ↓
┌─────────────────────┐
│ LoggingBehavior     │ → Log entry
├─────────────────────┤
│ TransactionBehavior │ → BEGIN TRANSACTION
├─────────────────────┤
│ CommandHandler      │ → Business logic (only this)
├─────────────────────┤
│ TransactionBehavior │ → COMMIT + publish events
├─────────────────────┤
│ LoggingBehavior     │ → Log exit
└─────────────────────┘
  │
  ↓
Response
```

### 🔧 Implementation in the Code

**Program.cs / Extensions.cs:**

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    
    // Order matters: behaviors run in registration order
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // ← First
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));   // ← Second
});
```

**Simplified handler:**

```csharp
public class CreateOrganizationHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    public async Task<bool> Handle(CreateOrganizationCommand request, CancellationToken ct)
    {
        // Business logic only
        var org = new Organization(request.Name, request.TaxNumber, ...);
        await _repository.AddAsync(org);
        
        return true;
    }
    // ← No logging, no transactions, no try/catch
}
```

### 📊 Chain of Responsibility

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next, // ← Next in the chain
        CancellationToken cancellationToken
    );
}
```

Each behavior decides whether to:
1. Run logic before `next()`
2. Call `next()` or not (it can short-circuit the chain)
3. Run logic after `next()`

### ⚡ Benefits

- ✅ **DRY**: cross-cutting logic in one place
- ✅ **Separation of concerns**: handlers only contain business logic
- ✅ **Composability**: easy to add/remove behaviors
- ✅ **Testability**: behaviors and handlers are tested independently
- ✅ **Controlled order**: predictable execution

---

## 5️⃣ Eventual Consistency Pattern

### 🎯 Problem

Distributed transactions are slow, complex and fragile:

```
❌ DISTRIBUTED TRANSACTION (2PC):

Coordinator:
  ├─ Phase 1: PREPARE
  │   ├─ Accounts Service → Can commit? ✅
  │   ├─ Identity Service → Can commit? ✅
  │   └─ Billing Service → Can commit? ❌ TIMEOUT
  │
  └─ Phase 2: ROLLBACK (one failed)
      ├─ Accounts Service → Rollback
      └─ Identity Service → Rollback

Problems:
- Long-held locks across several DBs
- One failing service blocks all of them
- Complexity of the 2PC protocol
```

### ✅ Solution

Accept **temporary inconsistency** and converge to consistency through events.

> Illustrative example: `Billing Service` and `TenantCreatedIntegrationEvent` don't exist in the project; they show how consistency would chain across several services.

```
✅ EVENTUAL CONSISTENCY:

1. Accounts Service:
   ├─ Create Organization (COMMIT) ✅
   └─ Publish OrganizationCreatedIntegrationEvent

2. Identity Service (receives the event):
   ├─ Create Tenant
   └─ Publish TenantCreatedIntegrationEvent
   
   ⏳ Delay: 200ms

3. Billing Service (receives the event):
   ├─ Create Customer
   └─ Activate subscription
   
   ⏳ Delay: 500ms

Result:
T+0ms:   Organization created ✅, Tenant ❌, Customer ❌
T+200ms: Organization created ✅, Tenant created ✅, Customer ❌
T+500ms: Organization created ✅, Tenant created ✅, Customer created ✅
         ↑ EVENTUAL CONSISTENCY REACHED
```

### 🔧 Implementation in the Code

**OrganizationCreatedDomainEventHandler.cs:**

```csharp
public async Task Handle(OrganizationCreatedDomainEvent notification, CancellationToken ct)
{
    var organization = await _organizationRepository.GetAsync(notification.OrganizationId);
    
    // Create the integration event
    var integrationEvent = new OrganizationCreatedIntegrationEvent(
        organization.TenantId,
        organization.Id,
        organization.Name,
        organization.LegalName,
        organization.TaxNumber,
        organization.Address?.CountryCode!
    );
    
    // Save to the outbox (same transaction)
    await _accountIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
    
    // Publishing happens after the commit (TransactionBehavior)
}
```

**Identity.API (consumer):**

At this stage the handler only logs that the event was received. Creating the tenant and the admin user arrives in Stage.03-3 (and becomes atomic in 03-4):

```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        // Runs asynchronously, after the commit in Accounts
        _logger.LogInformation("Received integration event for organization created: {OrganizationId} - {Name}",
            @event.OrganizationId, @event.Name);
        await Task.CompletedTask;
    }
}
```

### 📊 Consistency Timeline

```
Service A (Accounts)      Service B (Identity)     Service C (Billing)
     │                            │                         │
     │ CreateOrganization         │                         │
     ├─────────────────► ✅       │                         │
     │                            │                         │
     │ Publish Event              │                         │
     ├────────────────────────────►                         │
     │                            │                         │
     │                     CreateTenant                     │
     │                            ├─────────────────► ✅    │
     │                            │                         │
     │                     Publish Event                    │
     │                            ├─────────────────────────►
     │                            │                         │
     │                            │                 CreateCustomer
     │                            │                         ├───► ✅
     │                            │                         │
     ▼                            ▼                         ▼
  CONSISTENT                  CONSISTENT                CONSISTENT
```

### ⚠️ Challenges

1. **Idempotency**: events may be received more than once → Stage.03-3
2. **Ordering**: events may arrive out of order → versioning
3. **Compensation**: what if a step fails? → Saga pattern
4. **Monitoring**: how do we know the system converged? → distributed tracing

### ⚡ Benefits

- ✅ **Scalability**: services process events asynchronously
- ✅ **Availability**: one service being down doesn't affect the others
- ✅ **Autonomy**: each service has its own DB
- ✅ **Simplicity**: avoids the complexity of distributed transactions

---

## 📊 Comparative Summary

| Pattern | Guarantees | Protects against | Implementation |
|--------|-----------|----------------|----------------|
| **Outbox** | DB ↔ events consistency | Events sent without a DB commit | `IntegrationEventLog` + `TransactionBehavior` |
| **Idempotency** | One execution per request ID | Duplicate commands | `IdentifiedCommand` + `RequestManager` |
| **Unit of Work** | Atomic operations | Partial commits | `TransactionBehavior` with `ExecutionStrategy` |
| **Pipeline Behavior** | Separation of concerns | Duplicated code | MediatR behaviors |
| **Eventual Consistency** | Eventual consistency | Distributed transactions | Integration events |

---

## 🔗 How the Patterns Fit Together

```
┌─────────────────────────────────────────────────────────────┐
│                    PIPELINE BEHAVIOR                        │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              UNIT OF WORK (Transaction)                │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │         IDEMPOTENCY CHECK                        │  │ │
│  │  │  ┌────────────────────────────────────────────┐  │  │ │
│  │  │  │       COMMAND HANDLER                      │  │  │ │
│  │  │  │         ↓                                  │  │  │ │
│  │  │  │    Domain Events                           │  │  │ │
│  │  │  │         ↓                                  │  │  │ │
│  │  │  │  OUTBOX PATTERN                            │  │  │ │
│  │  │  │  (Save Integration Events)                 │  │  │ │
│  │  │  └────────────────────────────────────────────┘  │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │  COMMIT ✅                                             │ │
│  │  Publish Events → EVENTUAL CONSISTENCY                 │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 🎓 Key Lessons

1. **Outbox before events**: always store events before publishing them
2. **Idempotency starts with the client**: the client generates the request ID
3. **Local transactions**: avoid distributed transactions
4. **Behaviors for cross-cutting concerns**: keep handlers clean
5. **Accept temporary inconsistency**: it's the price of scalability

---

## 📖 References

- [Transactional Outbox - Microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [Idempotent Receiver - Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)
- [Unit of Work - Martin Fowler](https://martinfowler.com/eaaCatalog/unitOfWork.html)
- [Chain of Responsibility](https://refactoring.guru/design-patterns/chain-of-responsibility)
- [Eventually Consistent - Werner Vogels](https://www.allthingsdistributed.com/2008/12/eventually_consistent.html)
