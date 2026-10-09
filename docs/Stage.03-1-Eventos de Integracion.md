# Stage.03-1 - Eventos de Integración

## 🎯 Objetivo de la Etapa

Implementar la comunicación asíncrona entre microservicios mediante **eventos de integración**, permitiendo que diferentes servicios reaccionen a cambios en el dominio de manera desacoplada utilizando **RabbitMQ** como message broker.

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué Eventos de Integración?

En una arquitectura de microservicios, los servicios deben mantener su autonomía y estar desacoplados. Los eventos de integración permiten:

- **Comunicación asíncrona** entre servicios sin dependencias directas
- **Eventual consistency** entre bounded contexts
- **Escalabilidad** independiente de servicios productores y consumidores
- **Resiliencia** ante fallos temporales de servicios

### Diferencia entre Eventos de Dominio e Integración

| Aspecto | Eventos de Dominio | Eventos de Integración |
|---------|-------------------|------------------------|
| **Ámbito** | Dentro del mismo servicio | Entre servicios diferentes |
| **Propósito** | Reflejar cambios en el dominio | Comunicar cambios a otros bounded contexts |
| **Transporte** | MediatR (in-process) | RabbitMQ (out-of-process) |
| **Transaccionalidad** | Misma transacción DB | Fuera de la transacción |
| **Formato** | Domain Events (INotification) | Integration Events (serializable) |

### Arquitectura Implementada

```
┌─────────────────────────────────────────────────────────┐
│           uSLearn.Accounts.API                          │
│                                                         │
│  ┌──────────────┐        ┌─────────────────┐          │
│  │ Organization │ raises │  Domain Event   │          │
│  │   Entity     │───────>│ (OrganizationCr │          │
│  └──────────────┘        │  eatedDomain)   │          │
│                          └─────────────────┘          │
│                                  │                      │
│                                  ▼                      │
│                    ┌──────────────────────────┐        │
│                    │ DomainEventHandler       │        │
│                    │ - Fetch full aggregate   │        │
│                    │ - Publish to EventBus    │        │
│                    └──────────────────────────┘        │
│                                  │                      │
│                                  ▼                      │
│                    ┌──────────────────────────┐        │
│                    │ Integration Event        │        │
│                    │ (OrganizationCreated)    │        │
│                    └──────────────────────────┘        │
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
│                              │                           │
│                              ▼                           │
│                    ┌──────────────────────────┐         │
│                    │ IntegrationEventHandler  │         │
│                    │ - Receive event          │         │
│                    │ - Process in Identity    │         │
│                    └──────────────────────────┘         │
└─────────────────────────────────────────────────────────┘
```

---

## 📦 Componentes Implementados

### 1. **Building Block: EventBus**

Abstracción genérica para publicación y suscripción de eventos.

#### `IEventBus`
```csharp
public interface IEventBus
{
    Task PublishAsync(IntegrationEvent @event);
}
```

#### `IntegrationEvent` (Base Class)
```csharp
public record IntegrationEvent
{
    public Guid Id { get; set; }
    public DateTime CreationDate { get; set; }
}
```

Todas las clases de eventos de integración heredan de esta clase base, garantizando trazabilidad mediante `Id` y `CreationDate`.

---

### 2. **EventBusRabbitMQ - Implementación con RabbitMQ**

#### Extensión de configuración
```csharp
public static IEventBusBuilder AddRabbitMqEventBus(
    this IHostApplicationBuilder builder, 
    string connectionName)
```

**Funcionalidades:**
- Integración con **Aspire** para Service Discovery de RabbitMQ
- Configuración mediante sección `EventBus` en `appsettings.json`
- Registro de `IEventBus` como singleton
- Auto-inicio del consumo mediante `IHostedService`

---

### 3. **Accounts.API - Publicador de Eventos**

#### Evento de Dominio
```csharp
public record OrganizationCreatedDomainEvent(
    Guid TenantId,
    Guid OrganizationId,
    string Name,
    string LegalName,
    string TaxNumber,
    string CountryCode) : INotification;
```

Se dispara en el método `Create` de la entidad `Organization`.

#### Domain Event Handler
```csharp
public class OrganizationCreatedDomainEventHandler 
    : INotificationHandler<OrganizationCreatedDomainEvent>
```

**Responsabilidades:**
1. Recibir el evento de dominio (in-process via MediatR)
2. Recuperar el agregado completo desde el repositorio
3. Transformar en evento de integración
4. Publicar en el EventBus (RabbitMQ)

**Código clave:**
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

#### Integration Event
```csharp
public record OrganizationCreatedIntegrationEvent(
    Guid TenantId,
    Guid OrganizationId,
    string Name,
    string LegalName,
    string TaxNumber,
    string CountryCode) : IntegrationEvent;
```

#### Registro en Accounts.API
```csharp
builder.AddRabbitMqEventBus("eventbus")
    .AddEventBusSubscriptions();
```

---

### 4. **Identity.API - Consumidor de Eventos**

#### Integration Event Handler
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

**Estado actual:** Log básico para validar recepción. En futuras etapas se implementará lógica de negocio.

#### Suscripción en Identity.API
```csharp
private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
{
    eventBus.AddSubscription<OrganizationCreatedIntegrationEvent, 
                             OrganizationCreatedIntegrationEventHandler>();
}
```

---

## 🔄 Flujo Completo

### Caso: Creación de una Organización

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

## 🔧 Configuración Requerida

### appsettings.json

Cada servicio usa su propio nombre de suscripción, que se convierte en el nombre de su cola en RabbitMQ:

```json
// uSLearn.Accounts.API
{ "EventBus": { "SubscriptionClientName": "Accounts" } }

// uSLearn.Identity.API
{ "EventBus": { "SubscriptionClientName": "Identity" } }
```

`RetryCount` (reintentos de publicación con Polly) es opcional; por defecto vale 10.

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

## ✅ Características Implementadas

- ✅ Abstracción genérica de EventBus
- ✅ Implementación con RabbitMQ
- ✅ Integración con Aspire Service Discovery
- ✅ Transformación de Domain Events → Integration Events
- ✅ Publisher (Accounts.API)
- ✅ Subscriber (Identity.API)
- ✅ Serialización/Deserialización automática de eventos
- ✅ Logging de eventos publicados y recibidos

---

## 🚀 Próximos Pasos

- **Stage.03-2 – Idempotencia y transaccionalidad**: outbox (`IntegrationEventLog`) para publicar solo después del commit e idempotencia de comandos con `x-requestid`.
- **Stage.03-3 – Idempotencia en event handlers**: lógica de negocio en `Identity.API` y deduplicación de eventos recibidos.
- **Stage.03-4 – Transacciones resilientes**: atomicidad y reintentos en los handlers de integración.

---

## 📝 Notas Técnicas

### ¿Por qué se recupera el agregado en el DomainEventHandler?

El evento de dominio ya lleva los datos básicos (nombre, NIF, país), pero el handler construye el evento de integración a partir del agregado recuperado del repositorio. Así el contrato público (`OrganizationCreatedIntegrationEvent`) se puede ampliar sin cambiar el evento de dominio. Como el agregado aún no se ha guardado, `GetAsync` lo obtiene del `ChangeTracker` de EF Core.

### ⚠️ Limitación de esta etapa: publicación antes del commit

`DispatchDomainEventsAsync` se ejecuta dentro de `SaveEntitiesAsync`, **antes** de `SaveChangesAsync`. En esta etapa el handler publica directamente en RabbitMQ, así que el evento sale **antes** de que la organización se haya guardado:

- Si `SaveChangesAsync` falla, `Identity.API` ya ha recibido un evento de una organización que no existe.
- Si RabbitMQ no está disponible, falla toda la creación de la organización.

Es el problema que resuelve el **Stage.03-2** con el patrón *Transactional Outbox*: el evento se guarda en la misma transacción y se publica solo tras el commit.

---

## 🎓 Conceptos DDD Aplicados

| Patrón | Implementación |
|--------|----------------|
| **Aggregate Root** | `Organization` gestiona su ciclo de vida y eventos |
| **Domain Events** | `OrganizationCreatedDomainEvent` refleja cambio en dominio |
| **Integration Events** | `OrganizationCreatedIntegrationEvent` comunica entre contextos |
| **Event Handler** | Transformación domain → integration mediante handler |
| **Repository Pattern** | `IOrganizationRepository` para acceso a agregado |
| **Unit of Work** | `AccountContext.SaveEntitiesAsync()` coordina transacción |

---

## 🔍 Verificación

### Comprobar publicación de eventos

1. Ejecutar Aspire AppHost
2. Crear organización mediante API (en esta etapa todavía no se exige `x-requestid`):
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

3. Revisar logs de `Accounts.API`:
```
Publishing integration event for organization: {OrganizationId} - {Name}
```

4. Revisar logs de `Identity.API`:
```
Received integration event for organization created: {OrganizationId} - {Name}
```

5. En el dashboard de Aspire (**Structured logs**), filtrar por `apiservice` e `identity` para ver ambos mensajes. El evento viaja por el exchange `uslearn_event_bus` hasta la cola `Identity`. (La traza distribuida entre ambos servicios llega en el Stage.04-3.)

---

**Autor:** Sistema uSLearn  
**Versión:** Stage.03-1  
**Fecha:** Marzo 2026  
**Stack:** .NET 10, Aspire, RabbitMQ, MediatR, DDD
