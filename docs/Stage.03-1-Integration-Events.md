# Stage.03-1 - Integration Events

## 🎯 Stage Goal

Implement asynchronous communication between microservices through **integration events**, so that services react to domain changes in a decoupled way, using **RabbitMQ** as the message broker.

---

## 🏗️ Architectural Decisions

### Why integration events?

In a microservices architecture, services must stay autonomous and decoupled. Integration events provide:

- **Asynchronous communication** between services with no direct dependencies
- **Eventual consistency** between bounded contexts
- Independent **scalability** of producers and consumers
- **Resilience** to temporary service failures

### Domain events vs integration events

| Aspect | Domain events | Integration events |
|---------|-------------------|------------------------|
| **Scope** | Within the same service | Across services |
| **Purpose** | Reflect domain changes | Communicate changes to other bounded contexts |
| **Transport** | MediatR (in-process) | RabbitMQ (out-of-process) |
| **Transactionality** | Same DB transaction | Outside the transaction |
| **Format** | Domain events (`INotification`) | Integration events (serializable) |

### Implemented architecture

```
┌─────────────────────────────────────────────────────────┐
│           uSLearn.Accounts.API                          │
│                                                         │
│  ┌──────────────┐        ┌─────────────────┐            │
│  │ Organization │ raises │  Domain Event   │            │
│  │   Entity     │───────>│ (OrganizationCr │            │
│  └──────────────┘        │  eatedDomain)   │            │
│                          └─────────────────┘            │
│                                  │                      │
│                                  ▼                      │
│                    ┌──────────────────────────┐         │
│                    │ DomainEventHandler       │         │
│                    │ - Fetch full aggregate   │         │
│                    │ - Publish to EventBus    │         │
│                    └──────────────────────────┘         │
│                                  │                      │
│                                  ▼                      │
│                    ┌──────────────────────────┐         │
│                    │ Integration Event        │         │
│                    │ (OrganizationCreated)    │         │
│                    └──────────────────────────┘         │
└─────────────────────────────│───────────────────────────┘
                              │
                              ▼
                    ┌──────────────────┐
                    │    RabbitMQ      │
                    │  (Event Bus)     │
                    └──────────────────┘
                              │
                              ▼
┌─────────────────────────────│───────────────────────────┐
│           uSLearn.Identity.API                          │
│                              │                          │
│                              ▼                          │
│                    ┌──────────────────────────┐         │
│                    │ IntegrationEventHandler  │         │
│                    │ - Receive event          │         │
│                    │ - Process in Identity    │         │
│                    └──────────────────────────┘         │
└─────────────────────────────────────────────────────────┘
```

---

## 📦 Implemented Components

### 1. **Building block: EventBus**

Generic abstraction for publishing and subscribing to events.

#### `IEventBus`
```csharp
public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event);
}
```

#### `IntegrationEvent` (base class)
```csharp
public record IntegrationEvent
{
    public Guid Id { get; set; }
    public DateTime CreationDate { get; set; }
}
```

Every integration event inherits from this base class, which guarantees traceability through `Id` and `CreationDate`.

---

### 2. **EventBusRabbitMQ - RabbitMQ implementation**

#### Configuration extension
```csharp
public static IEventBusBuilder AddRabbitMqEventBus(
    this IHostApplicationBuilder builder, 
    string connectionName)
```

**Features:**
- Integration with **Aspire** to discover RabbitMQ
- Configuration through the `EventBus` section in `appsettings.json`
- `IEventBus` registered as a singleton
- Consumption starts automatically through `IHostedService`

---

### 3. **Accounts.API - Event publisher**

#### Domain event
```csharp
public record OrganizationCreatedDomainEvent(
    Guid TenantId,
    Guid OrganizationId,
    string Name,
    string LegalName,
    string TaxNumber,
    string CountryCode) : INotification;
```

Raised in the `Organization` entity's `Create` method.

#### Domain event handler
```csharp
public class OrganizationCreatedDomainEventHandler 
    : INotificationHandler<OrganizationCreatedDomainEvent>
```

**Responsibilities:**
1. Receive the domain event (in-process, through MediatR)
2. Fetch the full aggregate from the repository
3. Turn it into an integration event
4. Publish it on the EventBus (RabbitMQ)

**Key code:**
```csharp
Organization organization = await _organizationRepository.GetAsync(
    notification.OrganizationId);

await _eventBus.PublishAsync(
    new OrganizationCreatedIntegrationEvent(
        organization.TenantId,
        organization.Id,
        organization.Name,
        organization.LegalName,
        organization.TaxNumber,
        organization.Address?.CountryCode!
    ));
```

#### Integration event
```csharp
public record OrganizationCreatedIntegrationEvent(
    Guid TenantId,
    Guid OrganizationId,
    string Name,
    string LegalName,
    string TaxNumber,
    string CountryCode) : IntegrationEvent;
```

#### Registration in Accounts.API
```csharp
builder.AddRabbitMqEventBus("eventbus")
    .AddEventBusSubscriptions();
```

---

### 4. **Identity.API - Event consumer**

#### Integration event handler
```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        _logger.LogInformation(
            "Received integration event for organization created: {OrganizationId} - {Name}", 
            @event.OrganizationId, 
            @event.Name);
        
        await Task.CompletedTask;
    }
}
```

**Current state:** a basic log to confirm the event is received. Business logic arrives in later stages.

#### Subscription in Identity.API
```csharp
private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
{
    eventBus.AddSubscription<OrganizationCreatedIntegrationEvent, 
                             OrganizationCreatedIntegrationEventHandler>();
}
```

---

## 🔄 End-to-End Flow

### Scenario: creating an organization

```
1. API Request → PUT /api/accounts
                     │
                     ▼
2. CreateOrganizationCommandHandler
   - Organization.Create()
                     │
                     ▼
3. Organization Entity
   - AddDomainEvent(OrganizationCreatedDomainEvent)
                     │
                     ▼
4. SaveEntitiesAsync (AccountContext)
   - DispatchDomainEventsAsync (MediatR)
                     │
                     ▼
5. OrganizationCreatedDomainEventHandler
   - GetAsync(organizationId)
   - PublishAsync(IntegrationEvent)
                     │
                     ▼
6. RabbitMQEventBus
   - Serialize event
   - Publish to exchange
                     │
                     ▼
7. RabbitMQ Broker
   - Route to subscribed queues
                     │
                     ▼
8. Identity.API (subscriber)
   - Deserialize event
   - OrganizationCreatedIntegrationEventHandler.Handle()
                     │
                     ▼
9. Log confirmation + future business logic
```

---

## 🔧 Required Configuration

### appsettings.json

Each service uses its own subscription name, which becomes the name of its RabbitMQ queue:

```json
// uSLearn.Accounts.API
{ "EventBus": { "SubscriptionClientName": "Accounts" } }

// uSLearn.Identity.API
{ "EventBus": { "SubscriptionClientName": "Identity" } }
```

`RetryCount` (publish retries with Polly) is optional and defaults to 10.

### Aspire AppHost

```csharp
var rabbitMq = builder.AddRabbitMQ("eventbus")
    .WithLifetime(ContainerLifetime.Persistent);

var identity = builder.AddProject<Projects.uSLearn_Identity_API>("identity")
    .WithReference(rabbitMq).WaitFor(rabbitMq);

var apiService = builder.AddProject<Projects.uSLearn_Accounts_API>("apiservice")
    .WithReference(accountDb).WaitFor(accountDb)
    .WithReference(rabbitMq).WaitFor(rabbitMq);
```

---

## ✅ Implemented Features

- ✅ Generic EventBus abstraction
- ✅ RabbitMQ implementation
- ✅ Integration with Aspire service discovery
- ✅ Domain events → integration events
- ✅ Publisher (Accounts.API)
- ✅ Subscriber (Identity.API)
- ✅ Automatic event serialization/deserialization
- ✅ Logging of published and received events

---

## 🚀 Next Steps

- **Stage.03-2 – Idempotency and transactionality**: outbox (`IntegrationEventLog`) so events are published only after the commit, and command idempotency through `x-requestid`.
- **Stage.03-3 – Idempotent event handlers**: business logic in `Identity.API` and deduplication of received events.
- **Stage.03-4 – Resilient transactions**: atomicity and retries in the integration handlers.

---

## 📝 Technical Notes

### Why does the domain event handler fetch the aggregate?

The domain event already carries the basic data (name, tax number, country), but the handler builds the integration event from the aggregate fetched from the repository. That way the public contract (`OrganizationCreatedIntegrationEvent`) can grow without changing the domain event. Since the aggregate hasn't been saved yet, `GetAsync` returns it from EF Core's `ChangeTracker`.

### ⚠️ Limitation of this stage: publishing before the commit

`DispatchDomainEventsAsync` runs inside `SaveEntitiesAsync`, **before** `SaveChangesAsync`. At this stage the handler publishes straight to RabbitMQ, so the event goes out **before** the organization is saved:

- If `SaveChangesAsync` fails, `Identity.API` has already received an event about an organization that doesn't exist.
- If RabbitMQ is down, the whole organization creation fails.

This is the problem **Stage.03-2** solves with the *transactional outbox* pattern: the event is saved in the same transaction and published only after the commit.

---

## 🎓 DDD Concepts Applied

| Pattern | Implementation |
|--------|----------------|
| **Aggregate root** | `Organization` manages its lifecycle and events |
| **Domain events** | `OrganizationCreatedDomainEvent` reflects a domain change |
| **Integration events** | `OrganizationCreatedIntegrationEvent` communicates across contexts |
| **Event handler** | Domain → integration transformation in a handler |
| **Repository pattern** | `IOrganizationRepository` to access the aggregate |
| **Unit of work** | `AccountContext.SaveEntitiesAsync()` coordinates the save |

---

## 🔍 Verification

### Check that events are published

1. Run the Aspire AppHost
2. Create an organization through the API (`x-requestid` is not required yet at this stage):
```http
PUT https://localhost:7375/api/accounts?api-version=1.0
Content-Type: application/json

{
  "taxIdNumber": "B12345678",
  "taxNumberType": "Cif",
  "name": "ACME Corp",
  "legalName": "ACME Corporation S.L.",
  "street": "Main Street 1",
  "city": "Barcelona",
  "state": "Barcelona",
  "country": "Spain",
  "zipCode": "08001",
  "organizationType": "Company"
}
```

3. Check the `Accounts.API` logs:
```
Publishing integration event for organization: {OrganizationId} - {Name}
```

4. Check the `Identity.API` logs:
```
Received integration event for organization created: {OrganizationId} - {Name}
```

5. In the Aspire dashboard (**Structured logs**), filter by `apiservice` and `identity` to see both messages. The event travels through the `uslearn_event_bus` exchange to the `Identity` queue. (Distributed tracing across both services arrives in Stage.04-3.)

---

**Version:** Stage.03-1  
**Date:** March 2026  
**Stack:** .NET 10, Aspire, RabbitMQ, MediatR, DDD
