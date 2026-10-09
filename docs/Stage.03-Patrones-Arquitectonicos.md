# Stage.03 - Patrones Arquitectónicos Detallados

> 📖 **Documento complementario de:** [`Stage.03-2-Idempotencia.md`](Stage.03-2-Idempotencia.md)  
> Este documento profundiza en los patrones arquitectónicos con diagramas, ejemplos de código y análisis detallado.

## 📚 Índice de Patrones Implementados

| Patrón | Problema que resuelve | Sección |
|--------|----------------------|---------|
| **Transactional Outbox** | Inconsistencia entre BD y eventos publicados | [Ver](#1️⃣-transactional-outbox-pattern) |
| **Idempotent Consumer** | Procesamiento duplicado de comandos | [Ver](#2️⃣-idempotent-consumer-pattern) |
| **Unit of Work** | Operaciones no atómicas | [Ver](#3️⃣-unit-of-work-pattern) |
| **Pipeline Behavior** | Cross-cutting concerns duplicados | [Ver](#4️⃣-pipeline-behavior-pattern) |
| **Eventual Consistency** | Transacciones distribuidas complejas | [Ver](#5️⃣-eventual-consistency-pattern) |

---

## 1️⃣ Transactional Outbox Pattern

### 🎯 Problema

En un sistema distribuido con eventos de integración, existe una race condition crítica:

```
❌ ESCENARIO PROBLEMÁTICO:

1. Comando crea Organization en BD
2. Evento publicado a RabbitMQ          ← Publicado ANTES del commit
3. BD hace commit
4. ❌ Commit falla (deadlock, constraint violation, etc.)

Resultado: Evento enviado pero la entidad nunca fue creada
           Otros servicios reciben datos de una operación que falló
```

### ✅ Solución

Guardar eventos en una tabla **dentro de la misma transacción** que las entidades de dominio:

```
✅ FLUJO CORRECTO CON OUTBOX:

┌─────────────────────────────────────────┐
│  BEGIN TRANSACTION                      │
├─────────────────────────────────────────┤
│                                         │
│  1. INSERT INTO Organizations (...)     │
│                                         │
│  2. INSERT INTO IntegrationEventLog     │
│     - EventId: guid                     │
│     - TransactionId: tx-guid            │
│     - State: NotPublished               │
│     - Content: JSON serialized event    │
│                                         │
│  3. COMMIT                              │ ← Ambas operaciones atómicas
│                                         │
└─────────────────────────────────────────┘

4. Después del commit exitoso:
   - Recuperar eventos con TransactionId
   - Publicar a RabbitMQ
   - Marcar como Published
```

### 🔧 Implementación en el Código

**AccountIntegrationEventService.cs:**

```csharp
// Fase 1: Guardar evento (dentro de transacción)
public async Task AddAndSaveEventAsync(IntegrationEvent evt)
{
    // GetCurrentTransaction() asegura que use la transacción activa
    await _eventLogService.SaveEventAsync(evt, _accountContext.GetCurrentTransaction());
}

// Fase 2: Publicar eventos (después del commit)
public async Task PublishEventsThroughEventBusAsync(Guid transactionId)
{
    // Solo eventos de esta transacción específica
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

### 📊 Estados del Evento

```
┌─────────────────┐
│  NotPublished   │ ← Evento guardado en BD
└────────┬────────┘
         │
         │ PublishEventsThroughEventBusAsync()
         ↓
┌─────────────────┐
│   InProgress    │ ← Publicación en curso
└────────┬────────┘
         │
    ┌────┴────┐
    │         │
    ↓         ↓
┌───────┐  ┌──────────────┐
│Published│  │PublishedFailed│
└────────┘  └──────────────┘
```

### ⚡ Beneficios

- ✅ **Atomicidad**: Dominio + Eventos se persisten juntos o ninguno
- ✅ **Consistencia**: Estado de BD = Eventos guardados
- ✅ **Durabilidad**: Eventos sobreviven a fallos de proceso
- ⚠️ **At-least-once delivery (pendiente)**: los eventos que fallan quedan como `PublishedFailed`. Para garantizar la entrega haría falta un proceso en segundo plano que los republique; todavía no está implementado

---

## 2️⃣ Idempotent Consumer Pattern

### 🎯 Problema

Los clientes pueden enviar el mismo comando múltiples veces:

```
❌ ESCENARIO PROBLEMÁTICO:

Cliente → CreateOrganizationCommand
           ↓
         API procesa... ⏳ (demora 5 segundos)
           ↓
Cliente timeout (3s) → Reintenta ❌
           ↓
         API crea Organization #1 ✅
           ↓
         API crea Organization #2 ❌ (DUPLICADO)

Resultado: 2 organizaciones creadas para 1 solicitud
```

### ✅ Solución

El cliente genera un **Request ID único** y lo envía con cada comando:

```
✅ FLUJO CON IDEMPOTENCIA:

Cliente genera: RequestId = "a1b2c3d4..."

Intento 1:
  → IdentifiedCommand(CreateOrganizationCommand, "a1b2c3d4")
  → RequestManager.ExistAsync("a1b2c3d4") → FALSE
  → Guarda Request ID en BD
  → Procesa comando
  → Organization creada ✅

Intento 2 (reintento por timeout):
  → IdentifiedCommand(CreateOrganizationCommand, "a1b2c3d4")
  → RequestManager.ExistAsync("a1b2c3d4") → TRUE ✅
  → Retorna CreateResultForDuplicateRequest()
  → NO procesa comando
  → Retorna éxito sin crear duplicado ✅
```

### 🔧 Implementación en el Código

**IdentifiedCommandHandler.cs:**

```csharp
public async Task<R> Handle(IdentifiedCommand<T, R> message, CancellationToken cancellationToken)
{
    // 1. Verificar si ya fue procesado
    var alreadyExists = await _requestManager.ExistAsync(message.Id);
    
    if (alreadyExists)
    {
        _logger.LogInformation("Request {RequestId} already processed, returning cached result", message.Id);
        return CreateResultForDuplicateRequest(); // ← Idempotencia
    }

    // 2. Registrar Request ID (marca como procesándose)
    await _requestManager.CreateRequestForCommandAsync<T>(message.Id);

    // 3. Procesar comando
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
    requestConfiguration.HasIndex(r => r.Id).IsUnique(); // ← Última línea de defensa
}
```

### 📊 Tabla de Requests

```sql
SELECT * FROM [account].[requests];

Id                                    | Name                         | Time
--------------------------------------|------------------------------|--------------------
a1b2c3d4-5678-90ab-cdef-123456789abc  | CreateOrganizationCommand   | 2026-03-06 10:30:15
b2c3d4e5-6789-01bc-def0-234567890bcd  | CreateOrganizationCommand   | 2026-03-06 10:31:42
```

### ⚠️ Consideraciones de Producción

**Problema: Tabla requests crece indefinidamente**

Soluciones:
1. **TTL (Time To Live)**: Limpieza periódica de requests antiguos (propuesta, no implementada)
   ```csharp
   // Background job (sketch)
   var cutoffDate = DateTime.UtcNow.AddDays(-30);
   await _context.Set<ClientRequest>()
       .Where(r => r.Time < cutoffDate)
       .ExecuteDeleteAsync();
   ```

2. **Partitioning**: Particionar tabla por fecha para mejor performance

3. **Respuesta cacheada**: Almacenar el resultado junto al Request ID
   ```csharp
   public class ClientRequest
   {
       public Guid Id { get; set; }
       public string Name { get; set; }
       public DateTime Time { get; set; }
       public string Result { get; set; } // ← JSON del resultado
   }
   ```

### ⚡ Beneficios

- ✅ **Exactly-once processing**: Comando se ejecuta solo una vez
- ✅ **Sin duplicados**: Múltiples reintentos → mismo resultado
- ✅ **Responsabilidad del cliente**: Cliente controla la unicidad
- ✅ **Trazabilidad**: Historial de requests procesados

---

## 3️⃣ Unit of Work Pattern

### 🎯 Problema

Múltiples operaciones de BD necesitan ser atómicas:

```
❌ SIN UNIT OF WORK:

public async Task Handle(CreateOrganizationCommand request)
{
    // Operación 1
    var org = new Organization(...);
    await _context.Organizations.AddAsync(org);
    await _context.SaveChangesAsync(); // ← Commit 1

    // Operación 2
    var contact = new OrganizationContact(...);
    await _context.OrganizationContacts.AddAsync(contact);
    await _context.SaveChangesAsync(); // ← Commit 2 ❌ Puede fallar

    // Operación 3
    await _eventLogService.SaveEventAsync(...);
    await _context.SaveChangesAsync(); // ← Commit 3 ❌ Puede fallar

    // Resultado: Organization creada pero Contact y Event no
}
```

### ✅ Solución

TransactionBehavior envuelve TODO el procesamiento en una transacción:

```
✅ CON UNIT OF WORK (TransactionBehavior):

┌──────────────────────────────────────────┐
│  BEGIN TRANSACTION                       │
├──────────────────────────────────────────┤
│                                          │
│  CommandHandler ejecuta:                 │
│    ├─ Organizations.Add(org)             │
│    ├─ OrganizationContacts.Add(contact)  │
│    └─ EventLog.Add(event)                │
│                                          │
│  COMMIT                                  │ ← TODO o NADA
│                                          │
└──────────────────────────────────────────┘
```

### 🔧 Implementación en el Código

**TransactionBehavior.cs:**

```csharp
public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, ...)
{
    // Evitar transacciones anidadas
    if (_dbContext.HasActiveTransaction)
    {
        return await next();
    }

    // Usar ExecutionStrategy para resilience
    var strategy = _dbContext.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
        Guid transactionId;

        // Iniciar transacción
        await using var transaction = await _dbContext.BeginTransactionAsync();
        
        _logger.LogInformation("Begin transaction {TransactionId}", transaction.TransactionId);

        // Ejecutar handler (puede modificar múltiples agregados)
        var response = await next();

        // Commit de TODAS las operaciones
        await _dbContext.CommitTransactionAsync(transaction);
        transactionId = transaction.TransactionId;

        // Publicar eventos SOLO si commit fue exitoso
        await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

        return response;
    });
}
```

### 📊 ExecutionStrategy

Maneja reintentos automáticos ante fallos transitorios **siempre que el `DbContext` tenga `EnableRetryOnFailure`**. Sin esa opción, `CreateExecutionStrategy()` devuelve una estrategia que no reintenta. En este stage aún no está activado: llega con el Stage.03-4 en Identity y después del Stage.04 en Accounts.

```
Intento 1: BEGIN → Operaciones → COMMIT → ❌ Deadlock
           ↓ Espera 1s
Intento 2: BEGIN → Operaciones → COMMIT → ❌ Timeout
           ↓ Espera 2s
Intento 3: BEGIN → Operaciones → COMMIT → ✅ Success
```

### ⚡ Beneficios

- ✅ **Atomicidad**: Todas las operaciones o ninguna
- ✅ **Isolation**: Cambios no visibles hasta commit
- ✅ **Consistency**: Estado de BD siempre válido
- ✅ **Resilience**: Preparado para reintentos ante fallos transitorios (requiere `EnableRetryOnFailure`)
- ✅ **Centralización**: Lógica transaccional fuera de handlers

---

## 4️⃣ Pipeline Behavior Pattern

### 🎯 Problema

Cross-cutting concerns duplicados en cada handler:

```
❌ SIN PIPELINE BEHAVIORS:

public class CreateOrganizationHandler
{
    public async Task<bool> Handle(CreateOrganizationCommand request)
    {
        // Logging (duplicado en todos los handlers)
        _logger.LogInformation("Handling {Command}", nameof(CreateOrganizationCommand));

        // Transacción (duplicado en todos los handlers)
        await using var transaction = await _dbContext.BeginTransactionAsync();

        try
        {
            // Lógica de negocio
            var org = new Organization(...);
            await _repository.AddAsync(org);

            // Commit (duplicado en todos los handlers)
            await _dbContext.CommitTransactionAsync(transaction);

            // Logging (duplicado en todos los handlers)
            _logger.LogInformation("Command handled successfully");

            return true;
        }
        catch (Exception ex)
        {
            // Error handling (duplicado en todos los handlers)
            _logger.LogError(ex, "Error handling command");
            throw;
        }
    }
}
```

### ✅ Solución

Behaviors interceptan comandos y agregan funcionalidad transversal:

```
✅ CON PIPELINE BEHAVIORS:

Request
  │
  ↓
┌─────────────────────┐
│ LoggingBehavior     │ → Log entrada
├─────────────────────┤
│ TransactionBehavior │ → BEGIN TRANSACTION
├─────────────────────┤
│ CommandHandler      │ → Lógica de negocio (solo esto)
├─────────────────────┤
│ TransactionBehavior │ → COMMIT + Publish events
├─────────────────────┤
│ LoggingBehavior     │ → Log salida
└─────────────────────┘
  │
  ↓
Response
```

### 🔧 Implementación en el Código

**Program.cs / Extensions.cs:**

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    
    // Orden importa: se ejecutan en orden de registro
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));       // ← Primero
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));   // ← Segundo
});
```

**Handler simplificado:**

```csharp
public class CreateOrganizationHandler : IRequestHandler<CreateOrganizationCommand, bool>
{
    public async Task<bool> Handle(CreateOrganizationCommand request, CancellationToken ct)
    {
        // Solo lógica de negocio
        var org = new Organization(request.Name, request.TaxNumber, ...);
        await _repository.AddAsync(org);
        
        return true;
    }
    // ← Sin logging, sin transacciones, sin try/catch
}
```

### 📊 Cadena de Responsabilidad

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next, // ← Siguiente en la cadena
        CancellationToken cancellationToken
    );
}
```

Cada behavior decide si:
1. Ejecutar lógica antes de `next()`
2. Llamar o no a `next()` (puede cortar la cadena)
3. Ejecutar lógica después de `next()`

### ⚡ Beneficios

- ✅ **DRY**: Lógica transversal en un solo lugar
- ✅ **Separation of Concerns**: Handlers solo contienen lógica de negocio
- ✅ **Composabilidad**: Fácil agregar/quitar behaviors
- ✅ **Testabilidad**: Behaviors y handlers se testean independientemente
- ✅ **Orden controlado**: Ejecución predecible

---

## 5️⃣ Eventual Consistency Pattern

### 🎯 Problema

Transacciones distribuidas son lentas, complejas y frágiles:

```
❌ TRANSACCIÓN DISTRIBUIDA (2PC):

Coordinator:
  ├─ Phase 1: PREPARE
  │   ├─ Accounts Service → Can commit? ✅
  │   ├─ Identity Service → Can commit? ✅
  │   └─ Billing Service → Can commit? ❌ TIMEOUT
  │
  └─ Phase 2: ROLLBACK (uno falló)
      ├─ Accounts Service → Rollback
      └─ Identity Service → Rollback

Problemas:
- Locks prolongados en múltiples BDs
- Fallo de un servicio bloquea a todos
- Complejidad del protocolo 2PC
```

### ✅ Solución

Aceptar **inconsistencia temporal** y converger a consistencia mediante eventos.

> Ejemplo ilustrativo: `Billing Service` y `TenantCreatedIntegrationEvent` no existen en el proyecto; sirven para mostrar cómo se encadenaría la consistencia entre varios servicios.

```
✅ EVENTUAL CONSISTENCY:

1. Accounts Service:
   ├─ Crear Organization (COMMIT) ✅
   └─ Publicar OrganizationCreatedIntegrationEvent

2. Identity Service (recibe evento):
   ├─ Crear Tenant
   └─ Publicar TenantCreatedIntegrationEvent
   
   ⏳ Delay: 200ms

3. Billing Service (recibe evento):
   ├─ Crear Customer
   └─ Activar suscripción
   
   ⏳ Delay: 500ms

Resultado:
T+0ms:   Organization creada ✅, Tenant ❌, Customer ❌
T+200ms: Organization creada ✅, Tenant creado ✅, Customer ❌
T+500ms: Organization creada ✅, Tenant creado ✅, Customer creado ✅
         ↑ CONSISTENCIA EVENTUAL ALCANZADA
```

### 🔧 Implementación en el Código

**OrganizationCreatedDomainEventHandler.cs:**

```csharp
public async Task Handle(OrganizationCreatedDomainEvent notification, CancellationToken ct)
{
    var organization = await _organizationRepository.GetAsync(notification.OrganizationId);
    
    // Crear evento de integración
    var integrationEvent = new OrganizationCreatedIntegrationEvent(
        organization.TenantId,
        organization.Id,
        organization.Name,
        organization.LegalName,
        organization.TaxNumber,
        organization.Address?.CountryCode!
    );
    
    // Guardar en Outbox (misma transacción)
    await _accountIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
    
    // Publicación ocurre después del commit (TransactionBehavior)
}
```

**Identity.API (consumidor):**

En este stage el handler solo registra la recepción del evento. La creación del tenant y del usuario administrador llega en el Stage.03-3 (y se hace atómica en el 03-4):

```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        // Se ejecuta de forma asíncrona, después del commit en Accounts
        _logger.LogInformation("Received integration event for organization created: {OrganizationId} - {Name}",
            @event.OrganizationId, @event.Name);
        await Task.CompletedTask;
    }
}
```

### 📊 Timeline de Consistencia

```
Servicio A (Accounts)     Servicio B (Identity)    Servicio C (Billing)
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

### ⚠️ Desafíos

1. **Idempotencia**: Eventos pueden recibirse múltiples veces → Stage.03-3
2. **Orden**: Eventos pueden llegar desordenados → Versioning
3. **Compensación**: ¿Qué pasa si un paso falla? → Saga pattern
4. **Monitoring**: ¿Cómo saber si el sistema convergió? → Distributed tracing

### ⚡ Beneficios

- ✅ **Escalabilidad**: Servicios procesan eventos asincrónicamente
- ✅ **Disponibilidad**: Un servicio caído no afecta a otros
- ✅ **Autonomía**: Cada servicio tiene su propia BD
- ✅ **Simplicity**: Evita complejidad de transacciones distribuidas

---

## 📊 Resumen Comparativo

| Patrón | Garantiza | Protege Contra | Implementación |
|--------|-----------|----------------|----------------|
| **Outbox** | Consistencia BD ↔ Eventos | Eventos enviados sin BD commit | `IntegrationEventLog` + `TransactionBehavior` |
| **Idempotency** | Exactly-once processing | Comandos duplicados | `IdentifiedCommand` + `RequestManager` |
| **Unit of Work** | Atomicidad de operaciones | Commits parciales | `TransactionBehavior` con `ExecutionStrategy` |
| **Pipeline Behavior** | Separation of concerns | Código duplicado | MediatR behaviors |
| **Eventual Consistency** | Consistencia eventual | Transacciones distribuidas | Integration events |

---

## 🔗 Cómo se Relacionan los Patrones

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
│  │  COMMIT ✅                                              │ │
│  │  Publish Events → EVENTUAL CONSISTENCY                 │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 🎓 Lecciones Clave

1. **Outbox antes que eventos**: Siempre guardar eventos antes de publicar
2. **Idempotencia es responsabilidad del cliente**: El cliente genera Request ID
3. **Transacciones locales**: Evitar transacciones distribuidas
4. **Behaviors para cross-cutting**: Mantener handlers limpios
5. **Aceptar inconsistencia temporal**: Es el precio de la escalabilidad

---

## 📖 Referencias

- [Transactional Outbox - Microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [Idempotent Consumer - Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)
- [Unit of Work - Martin Fowler](https://martinfowler.com/eaaCatalog/unitOfWork.html)
- [Chain of Responsibility - Gang of Four](https://refactoring.guru/design-patterns/chain-of-responsibility)
- [Eventual Consistency - Werner Vogels](https://www.allthingsdistributed.com/2008/12/eventually_consistent.html)
