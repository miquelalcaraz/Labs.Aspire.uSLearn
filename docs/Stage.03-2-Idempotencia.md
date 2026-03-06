# Stage.03-2 - Idempotencia y Transaccionalidad

> 💡 **Lectura complementaria:** Para un análisis detallado de los patrones arquitectónicos con diagramas y ejemplos, consulta [`Stage.03-Patrones-Arquitectonicos.md`](Stage.03-Patrones-Arquitectonicos.md)

## 🎯 Objetivo de la Etapa

Implementar **idempotencia** en el procesamiento de comandos y asegurar la **consistencia transaccional** entre el almacenamiento de eventos de integración y las operaciones de dominio, garantizando:

- Comandos procesados una sola vez (prevención de duplicados)
- Eventos de integración publicados dentro de transacciones de base de datos
- Consistencia entre el estado del dominio y la tabla de eventos
- Sistema resiliente ante reintentos y fallos de red

---

## 🎨 Patrones Arquitectónicos Implementados

> Esta sección presenta un resumen de los patrones. Para explicaciones detalladas, problemáticas específicas y diagramas de flujo, ver [`Stage.03-Patrones-Arquitectonicos.md`](Stage.03-Patrones-Arquitectonicos.md)

### 1. **Transactional Outbox Pattern**

**Problemática:**
- Los eventos de integración podrían publicarse a RabbitMQ antes de que la transacción de BD haga commit
- Si la transacción falla después de publicar, otros servicios recibirían eventos de operaciones que nunca ocurrieron
- Inconsistencia entre el estado del dominio y los mensajes publicados

**Solución:**
Guardar los eventos en una tabla (`IntegrationEventLog`) **dentro de la misma transacción** que las entidades de dominio. Después del commit exitoso, publicarlos al message broker.

**Implementación:**
```csharp
// 1. Guardar evento en la transacción
await _accountIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
// ← Evento guardado en IntegrationEventLog (mismo transaction)

// 2. Commit de la transacción
await _dbContext.CommitTransactionAsync(transaction);

// 3. Publicar eventos solo si commit fue exitoso
await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);
```

**Garantías:**
- ✅ **Atomicidad**: Dominio + Eventos se guardan o fallan juntos
- ✅ **Consistencia**: Estado de BD siempre refleja eventos guardados
- ✅ **At-least-once delivery**: Los eventos eventualmente se publican

---

### 2. **Idempotent Consumer/Processor Pattern**

**Problemática:**
- Clientes pueden enviar el mismo comando múltiples veces (timeouts, reintentos, errores de red)
- Sin protección, se crearían registros duplicados o se ejecutarían operaciones múltiples veces
- Efectos secundarios indeseables (cobros duplicados, emails repetidos, etc.)

**Solución:**
Cada comando incluye un **Request ID único** generado por el cliente. El servidor verifica si ese ID ya fue procesado antes de ejecutar la lógica de negocio.

**Implementación:**
```csharp
// Cliente genera un ID único
var requestId = Guid.NewGuid();
var command = new IdentifiedCommand<CreateOrganizationCommand, bool>(
    new CreateOrganizationCommand(...), 
    requestId
);

// IdentifiedCommandHandler verifica duplicados
var alreadyExists = await _requestManager.ExistAsync(message.Id);
if (alreadyExists)
{
    return CreateResultForDuplicateRequest(); // ← Retorna sin procesar
}

// Registra el Request ID
await _requestManager.CreateRequestForCommandAsync<T>(message.Id);

// Procesa el comando
var result = await _mediator.Send(message.Command, cancellationToken);
```

**Garantías:**
- ✅ **Exactly-once processing**: El comando se ejecuta una sola vez
- ✅ **Idempotencia**: Múltiples llamadas con mismo ID → mismo resultado
- ✅ **Sin side-effects duplicados**: Operaciones costosas no se repiten

---

### 3. **Unit of Work Pattern (implícito)**

**Problemática:**
- Múltiples operaciones de BD necesitan ejecutarse como una unidad atómica
- Necesidad de coordinar commit/rollback de todas las operaciones
- Gestión manual de transacciones es propensa a errores

**Solución:**
`TransactionBehavior` actúa como Unit of Work, envolviendo toda la ejecución del comando en una transacción de base de datos.

**Implementación:**
```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, ...)
    {
        // Iniciar transacción
        await using var transaction = await _dbContext.BeginTransactionAsync();

        // Ejecutar comando (puede modificar múltiples agregados)
        var response = await next();

        // Commit de todos los cambios
        await _dbContext.CommitTransactionAsync(transaction);

        // Publicar eventos solo si commit fue exitoso
        await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

        return response;
    }
}
```

**Garantías:**
- ✅ **Atomicidad**: Todas las operaciones o ninguna
- ✅ **Gestión centralizada**: Lógica transaccional fuera del handler
- ✅ **Resilience**: Usa `ExecutionStrategy` para reintentos automáticos

---

### 4. **Pipeline Behavior Pattern**

**Problemática:**
- Cross-cutting concerns (logging, transacciones, validación) dispersos en cada handler
- Duplicación de código transversal
- Difícil mantener consistencia en aspectos comunes

**Solución:**
MediatR Pipeline Behaviors permiten interceptar comandos y agregar funcionalidad transversal de forma declarativa.

**Implementación:**
```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));        // ← 1º: Log entrada/salida
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));    // ← 2º: Wrapper transaccional
});
```

**Flujo de ejecución:**
```
Request → LoggingBehavior → TransactionBehavior → CommandHandler → Response
```

**Garantías:**
- ✅ **Separation of Concerns**: Handlers solo contienen lógica de negocio
- ✅ **Reusabilidad**: Behaviors aplicables a todos los comandos
- ✅ **Orden controlado**: Pipeline ejecuta behaviors en orden de registro

---

### 5. **Eventual Consistency Pattern**

**Problemática:**
- Operaciones distribuidas no pueden ser atómicas (teorema CAP)
- Transacciones distribuidas (2PC) son lentas y complejas
- Necesidad de mantener consistencia entre servicios

**Solución:**
Aceptar que el sistema estará **temporalmente inconsistente** hasta que los eventos de integración se publiquen y procesen en otros servicios.

**Implementación:**
```
1. Comando procesa → Organization creada en BD
2. Evento guardado en IntegrationEventLog (misma transacción)
3. Commit de transacción
4. Evento publicado a RabbitMQ
5. Otros servicios procesan evento (eventual consistency)
```

**Garantías:**
- ✅ **Consistencia eventual**: Sistema converge a estado consistente
- ✅ **Desacoplamiento**: Servicios no necesitan disponibilidad síncrona
- ✅ **Escalabilidad**: Procesamiento asíncrono de eventos

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué Idempotencia?

En sistemas distribuidos, las solicitudes duplicadas son inevitables debido a:

- Reintentos de clientes ante timeouts
- Balanceadores de carga repitiendo peticiones
- Failures en el procesamiento
- Network glitches

La idempotencia garantiza que **procesar el mismo comando varias veces produce el mismo resultado** que procesarlo una sola vez.

### ¿Por qué Transaction Behavior?

Para evitar **inconsistencias** entre:
- El estado persistido del dominio (agregados)
- Los eventos de integración guardados

Ambas operaciones deben ocurrir en la **misma transacción de base de datos**, o ninguna debe persistirse.

---

## 🧩 Componentes Implementados

### 1. **IntegrationEventLogEF** (Building Block)

Proyecto compartido que proporciona infraestructura para **almacenar eventos de integración** dentro de transacciones de EF Core.

**Estructura:**

```
src/IntegrationEventLogEF/
 ├── IntegrationEventLogEntry.cs           → Entidad que representa un evento pendiente
 ├── EventStateEnum.cs                     → Estados del evento (NotPublished, InProgress, Published, PublishedFailed)
 ├── IntegrationLogExtensions.cs           → Extension para configurar tabla en DbContext
 ├── Services/
 │    ├── IIntegrationEventLogService.cs
 │    └── IntegrationEventLogService.cs    → Servicio para guardar/recuperar eventos
 └── Utilities/
      └── ResilientTransaction.cs          → Wrapper para transacciones resilientes
```

**IntegrationEventLogEntry.cs:**

```csharp
public class IntegrationEventLogEntry
{
    public Guid EventId { get; set; }
    public string EventTypeName { get; set; }
    public EventState State { get; set; }
    public int TimesSent { get; set; }
    public DateTime CreationTime { get; set; }
    public string Content { get; set; }  // JSON serializado
    public Guid TransactionId { get; set; }
    
    [NotMapped]
    public IntegrationEvent IntegrationEvent { get; set; }
}
```

**Integración en `AccountContext`:**

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("account");
    modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
    modelBuilder.ApplyConfiguration(new OrganizationContactConfiguration());
    modelBuilder.ApplyConfiguration(new ClientRequestConfiguration());
    modelBuilder.UseIntegrationEventLogs();  // ← Agrega tabla IntegrationEventLog
}
```

---

### 2. **AccountIntegrationEventService**

Servicio que **coordina** el almacenamiento y publicación de eventos de integración.

**Responsabilidades:**

1. **Guardar eventos en la transacción del dominio** (`AddAndSaveEventAsync`)
2. **Publicar eventos pendientes después de commit** (`PublishEventsThroughEventBusAsync`)

**Implementación:**

```csharp
public class AccountIntegrationEventService : IAccountIntegrationEventService
{
    private readonly IEventBus _eventBus;
    private readonly AccountContext _accountContext;
    private readonly IIntegrationEventLogService _eventLogService;
    private readonly ILogger<AccountIntegrationEventService> _logger;

    public async Task AddAndSaveEventAsync(IntegrationEvent evt)
    {
        _logger.LogInformation("Enqueuing integration event {IntegrationEventId}", evt.Id);
        
        // Guarda el evento en la MISMA transacción que el dominio
        await _eventLogService.SaveEventAsync(evt, _accountContext.GetCurrentTransaction());
    }

    public async Task PublishEventsThroughEventBusAsync(Guid transactionId)
    {
        var pendingLogEvents = await _eventLogService
            .RetrieveEventLogsPendingToPublishAsync(transactionId);

        foreach (var logEvt in pendingLogEvents)
        {
            try
            {
                await _eventLogService.MarkEventAsInProgressAsync(logEvt.EventId);
                await _eventBus.PublishAsync(logEvt.IntegrationEvent);
                await _eventLogService.MarkEventAsPublishedAsync(logEvt.EventId);
            }
            catch (Exception ex)
            {
                await _eventLogService.MarkEventAsFailedAsync(logEvt.EventId);
            }
        }
    }
}
```

**Flujo:**
1. Domain event handler llama a `AddAndSaveEventAsync`
2. Evento se guarda en tabla `IntegrationEventLog` con estado `NotPublished`
3. Después del commit, `TransactionBehavior` llama a `PublishEventsThroughEventBusAsync`
4. Eventos se marcan como `InProgress`, se publican, y se marcan como `Published`

---

### 3. **TransactionBehavior** (MediatR Pipeline)

Behavior que **envuelve comandos en transacciones** y asegura que los eventos de integración se publiquen solo si la transacción fue exitosa.

**Implementación:**

```csharp
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> 
    where TRequest : IRequest<TResponse>
{
    private readonly AccountContext _dbContext;
    private readonly IAccountIntegrationEventService _accountIntegrationEventService;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        if (_dbContext.HasActiveTransaction)
        {
            return await next();  // Ya hay transacción activa, no crear otra
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            Guid transactionId;

            await using var transaction = await _dbContext.BeginTransactionAsync();
            
            _logger.LogInformation("Begin transaction {TransactionId}", transaction.TransactionId);

            var response = await next();  // Ejecuta el handler

            _logger.LogInformation("Commit transaction {TransactionId}", transaction.TransactionId);

            await _dbContext.CommitTransactionAsync(transaction);
            transactionId = transaction.TransactionId;

            // Publica eventos DESPUÉS del commit
            await _accountIntegrationEventService.PublishEventsThroughEventBusAsync(transactionId);

            return response;
        });
    }
}
```

**Características:**

- ✅ Usa `ExecutionStrategy` para resilience (reintentos en caso de fallos transitorios)
- ✅ Eventos de integración se publican **solo si el commit es exitoso**
- ✅ Logging estructurado con `TransactionId`
- ✅ No crea transacciones anidadas

**Registro en DI:**

```csharp
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));  // ← Se ejecuta después de LoggingBehavior
});
```

---

### 4. **Idempotencia con IdentifiedCommand**

Wrapper genérico para comandos que incluye un **Request ID** único.

**IdentifiedCommand:**

```csharp
public class IdentifiedCommand<T, R> : IRequest<R>
    where T : IRequest<R>
{
    public T Command { get; }
    public Guid Id { get; }  // Request ID para detección de duplicados
    
    public IdentifiedCommand(T command, Guid id)
    {
        Command = command;
        Id = id;
    }
}
```

**IdentifiedCommandHandler:**

```csharp
public abstract class IdentifiedCommandHandler<T, R> : IRequestHandler<IdentifiedCommand<T, R>, R>
    where T : IRequest<R>
{
    private readonly IMediator _mediator;
    private readonly IRequestManager _requestManager;
    private readonly ILogger<IdentifiedCommandHandler<T, R>> _logger;

    public async Task<R> Handle(IdentifiedCommand<T, R> message, CancellationToken cancellationToken)
    {
        var alreadyExists = await _requestManager.ExistAsync(message.Id);
        
        if (alreadyExists)
        {
            return CreateResultForDuplicateRequest();  // Comando ya procesado
        }

        // Registra el Request ID en la base de datos
        await _requestManager.CreateRequestForCommandAsync<T>(message.Id);

        // Envía el comando real al handler
        var result = await _mediator.Send(message.Command, cancellationToken);

        return result;
    }

    protected abstract R CreateResultForDuplicateRequest();
}
```

**CreateOrganizationIdentifiedCommandHandler:**

```csharp
public class CreateOrganizationIdentifiedCommandHandler 
    : IdentifiedCommandHandler<CreateOrganizationCommand, bool>
{
    public CreateOrganizationIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CreateOrganizationCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true;  // Retorna éxito para requests duplicados
    }
}
```

---

### 5. **RequestManager e Infraestructura de Idempotencia**

**ClientRequest (entidad):**

```csharp
public class ClientRequest
{
    public Guid Id { get; set; }       // Request ID
    public string Name { get; set; }   // Tipo de comando
    public DateTime Time { get; set; } // Timestamp
}
```

**ClientRequestConfiguration:**

```csharp
public class ClientRequestConfiguration : IEntityTypeConfiguration<ClientRequest>
{
    public void Configure(EntityTypeBuilder<ClientRequest> requestConfiguration)
    {
        requestConfiguration.ToTable("requests");
        requestConfiguration.HasKey(r => r.Id);
        requestConfiguration.HasIndex(r => r.Id).IsUnique();  // ← Garantiza unicidad
    }
}
```

**RequestManager:**

```csharp
public class RequestManager : IRequestManager
{
    private readonly AccountContext _context;

    public async Task<bool> ExistAsync(Guid id)
    {
        var request = await _context.FindAsync<ClientRequest>(id);
        return request != null;
    }

    public async Task CreateRequestForCommandAsync<T>(Guid id)
    {
        var exists = await ExistAsync(id);

        if (exists)
            throw new AccountDomainException($"Request with {id} already exists");

        var request = new ClientRequest()
        {
            Id = id,
            Name = typeof(T).Name,
            Time = DateTime.UtcNow
        };

        _context.Add(request);
        await _context.SaveChangesAsync();
    }
}
```

**Nota:** En producción, manejar `DbUpdateException` para violaciones de constraint único.

---

### 6. **Migración de Base de Datos**

Se creó la migración `Add_Account_EventLog` que agrega:

**Tablas:**

1. **IntegrationEventLog:** Almacena eventos de integración pendientes de publicar
2. **requests:** Almacena Request IDs para idempotencia

```sql
CREATE TABLE [account].[IntegrationEventLog] (
    EventId         UNIQUEIDENTIFIER PRIMARY KEY,
    EventTypeName   NVARCHAR(MAX),
    State           INT,
    TimesSent       INT,
    CreationTime    DATETIME2,
    Content         NVARCHAR(MAX),
    TransactionId   UNIQUEIDENTIFIER
);

CREATE TABLE [account].[requests] (
    Id    UNIQUEIDENTIFIER PRIMARY KEY,
    Name  NVARCHAR(MAX),
    Time  DATETIME2
);

CREATE UNIQUE INDEX IX_requests_Id ON [account].[requests] (Id);
```

---

## 📐 Flujo Completo

### Procesamiento de Comando con Idempotencia

```
1. Cliente envía IdentifiedCommand<CreateOrganizationCommand>
   ↓
2. IdentifiedCommandHandler verifica si Request ID existe
   ↓
3a. Si existe → Retorna CreateResultForDuplicateRequest() (idempotencia)
   ↓
3b. Si no existe → Guarda Request ID y continúa
   ↓
4. TransactionBehavior inicia transacción
   ↓
5. CreateOrganizationCommandHandler ejecuta lógica de negocio
   ↓
6. Agrega Organization a la base de datos
   ↓
7. Dispara OrganizationCreatedDomainEvent
   ↓
8. DomainEventHandler llama a AddAndSaveEventAsync
   ↓
9. IntegrationEvent se guarda en IntegrationEventLog (mismo transaction)
   ↓
10. TransactionBehavior hace COMMIT
   ↓
11. PublishEventsThroughEventBusAsync publica eventos a RabbitMQ
   ↓
12. Eventos se marcan como Published
```

---

## 🔧 Configuración de Dependencias

**Extensions.cs:**

```csharp
// DbContext con configuración de eventos
services.AddDbContext<AccountContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// Servicios de integración
services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<AccountContext>>();
services.AddTransient<IAccountIntegrationEventService, AccountIntegrationEventService>();

// MediatR con behaviors
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining(typeof(Program));
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
});

// Idempotencia
services.AddScoped<IRequestManager, RequestManager>();
```

**Referencia de proyecto:**

```xml
<ItemGroup>
    <ProjectReference Include="..\IntegrationEventLogEF\IntegrationEventLogEF.csproj" />
</ItemGroup>
```

---

## 🎯 Cambios en Domain Event Handler

**Antes (Stage.03-1):**

```csharp
public class OrganizationCreatedDomainEventHandler
{
    private readonly IEventBus _eventBus;

    public async Task Handle(OrganizationCreatedDomainEvent notification)
    {
        var organization = await _organizationRepository.GetAsync(notification.OrganizationId);
        
        // Publicación directa (fuera de transacción)
        await _eventBus.PublishAsync(new OrganizationCreatedIntegrationEvent(...));
    }
}
```

**Después (Stage.03-2):**

```csharp
public class OrganizationCreatedDomainEventHandler
{
    private readonly IAccountIntegrationEventService _accountIntegrationEventService;

    public async Task Handle(OrganizationCreatedDomainEvent notification)
    {
        var organization = await _organizationRepository.GetAsync(notification.OrganizationId);
        
        var integrationEvent = new OrganizationCreatedIntegrationEvent(...);
        
        // Guardado transaccional
        await _accountIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
    }
}
```

---

## ✅ Verificación Práctica

### 1. **Verificar Idempotencia**

```csharp
// Enviar el mismo comando dos veces con el mismo Request ID
var requestId = Guid.NewGuid();

await mediator.Send(new IdentifiedCommand<CreateOrganizationCommand, bool>(
    new CreateOrganizationCommand(...), requestId));

// Segunda llamada con el mismo ID
await mediator.Send(new IdentifiedCommand<CreateOrganizationCommand, bool>(
    new CreateOrganizationCommand(...), requestId));

// Resultado: Solo se crea una organización
```

### 2. **Verificar Tabla de Eventos**

```sql
SELECT * FROM [account].[IntegrationEventLog];
-- Verificar que eventos tienen TransactionId correcto

SELECT * FROM [account].[requests];
-- Verificar que Request IDs están registrados
```

### 3. **Verificar Logs**

```
Begin transaction a1b2c3d4...
Enqueuing integration event 1234-5678...
Commit transaction a1b2c3d4...
Publishing integration event: 1234-5678
```

---

## 🚀 Próximos Pasos

### Stage.03-3 - Idempotencia en Event Handlers

Actualmente, la idempotencia solo está implementada en el **procesamiento de comandos** (capa de aplicación). El siguiente paso es implementar idempotencia en el **consumo de eventos de integración**:

**Problemática:**
- Un evento de integración puede recibirse múltiples veces (at-least-once delivery)
- Los event handlers podrían procesar el mismo evento varias veces
- Necesidad de garantizar procesamiento idempotente de eventos

**Solución planificada:**
- Almacenar Event IDs procesados en una tabla de deduplicación
- Verificar duplicados antes de procesar eventos
- Transactional processing de eventos con Outbox pattern

### Etapas Posteriores

Con idempotencia completa (comandos + eventos), el sistema estará preparado para:

- **Stage.04:** Implementar Duende IdentityServer para autenticación y autorización
- **Background workers** para reintentar eventos fallidos
- **Políticas de retry** más sofisticadas con Polly
- **Distributed transactions** con Saga pattern
- **Compensating transactions** para rollback distribuido

---

## 📖 Conceptos Clave Aplicados

| Concepto | Implementación |
|----------|----------------|
| **Idempotencia** | `IdentifiedCommand` + `RequestManager` |
| **Transaccionalidad** | `TransactionBehavior` con `ExecutionStrategy` |
| **Outbox Pattern** | `IntegrationEventLog` guardado en la misma transacción |
| **Eventual Consistency** | Publicación de eventos después del commit |
| **Pipeline Behavior** | MediatR behaviors para cross-cutting concerns |

---

## 🧩 Referencias

- [Idempotent message processing](https://docs.particular.net/nservicebus/messaging/messages-events-commands#command-messages)
- [Outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [EF Core Execution Strategies](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
- [MediatR Pipeline Behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
