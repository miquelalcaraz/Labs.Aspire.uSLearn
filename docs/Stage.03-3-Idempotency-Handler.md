# Stage.03-3 - Idempotency en el Handler

## 🎯 Objetivo

Implementar idempotencia a nivel de handler para garantizar que los eventos de integración no se procesen múltiples veces por el mismo handler, evitando duplicación de datos y comportamientos no deseados en el sistema distribuido.

---

## 🔀 Cambios de estructura en esta etapa

- Los building blocks se renombran con el prefijo `Core.` (`Core.Domain`, `Core.EventBus`, `Core.EventBusRabbitMQ`, `Core.Infrastructure`, `Core.IntegrationEventLogEF`) y sus namespaces pasan a `uSLearn.Core.*`. El `SeedWork` de dominio sale de `Accounts.API` a `Core.Domain` para poder reutilizarse.
- `Identity.API` incorpora su propia base de datos (`identitydb`, esquema `identity`) mediante `IdentityContext`.
- El handler ya crea el **tenant** y el **usuario administrador** de la organización. En esta etapa ambos se guardan en **repositorios en memoria** (singleton): se pierden al reiniciar y no comparten transacción con el registro de idempotencia. Se pasan a EF Core en el Stage.03-4.

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué Idempotencia a Nivel de Handler?

En sistemas distribuidos basados en eventos, es fundamental garantizar que:

- **No se procesen eventos duplicados**: El message broker puede entregar el mismo mensaje múltiples veces (at-least-once delivery)
- **Operaciones sean idempotentes**: Procesar el mismo evento varias veces debe producir el mismo resultado que procesarlo una vez
- **Granularidad por handler**: Diferentes handlers pueden necesitar procesar el mismo evento, por lo que el tracking es por combinación `EventId + HandlerName`

### Arquitectura de la Solución

```
┌─────────────────────────────────────────────────────────────┐
│  Integration Event Handler                                  │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ 1. Recibe Evento                                       │ │
│  │ 2. Verifica si ya fue procesado (IsProcessedAsync)    │ │
│  │ 3. Si duplicado → Skip                                 │ │
│  │ 4. Procesa lógica de negocio                          │ │
│  │ 5. Marca como procesado (MarkAsProcessedAsync)        │ │
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

## 📝 Componentes Implementados

### 1. **ProcessedIntegrationEvent** - Entidad de Tracking

Registra qué eventos han sido procesados por qué handlers:

```csharp
public class ProcessedIntegrationEvent
{
    public Guid EventId { get; set; }          // ID del evento original
    public string HandlerName { get; set; }    // Nombre del handler
    public DateTime ProcessedAt { get; set; }  // Timestamp de procesamiento
    public string? EventType { get; set; }     // Tipo de evento (auditoría)
}
```

**Composite Key**: `EventId + HandlerName` permite que diferentes handlers procesen el mismo evento.

### 2. **IEventIdempotencyService** - Contrato del Servicio

```csharp
public interface IEventIdempotencyService
{
    Task<bool> IsProcessedAsync(Guid eventId, string handlerName);
    Task MarkAsProcessedAsync(Guid eventId, string handlerName, string? eventType = null);
}
```

### 3. **EventIdempotencyService<TContext>** - Implementación

Servicio genérico que funciona con cualquier `DbContext`:

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

### 4. **Configuración en DbContext**

Uso del extension method `UseEventIdempotency()`:

```csharp
public class IdentityContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.UseEventIdempotency();  // ← Registra tabla y configuración
    }
}
```

**Extension Method**:

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

## 🔄 Flujo de Procesamiento Paso a Paso

### Ejemplo: `OrganizationCreatedIntegrationEventHandler`

```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    private readonly IEventIdempotencyService _idempotencyService;

    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        var handlerName = GetType().Name; // "OrganizationCreatedIntegrationEventHandler"

        // ✅ PASO 1: Verificar si ya fue procesado
        if (await _idempotencyService.IsProcessedAsync(@event.Id, handlerName))
        {
            _logger.LogWarning("Event {EventId} already processed. Skipping duplicate.", @event.Id);
            return; // ← Salir sin procesar
        }

        // ✅ PASO 2: Ejecutar lógica de negocio
        var tenant = await CreateTenantAsync(@event);
        var adminUser = await CreateAdminUserAsync(@event, tenant.Id);

        // ✅ PASO 3: Marcar como procesado
        await _idempotencyService.MarkAsProcessedAsync(
            @event.Id, 
            handlerName, 
            @event.GetType().Name);

        _logger.LogInformation("Tenant {TenantId} and Admin User {UserId} created", 
            tenant.Id, adminUser.Id);
    }
}
```

### Escenario de Duplicación

**Sin idempotencia:**
```
Event duplicado → Crea tenant duplicado → Crea admin user duplicado → ❌ Error de constraint o datos duplicados
```

**Con idempotencia:**
```
Event duplicado → IsProcessedAsync() retorna true → Skip procesamiento → ✅ Sin efectos secundarios
```

---

## 🗄️ Esquema de Base de Datos

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

**Índices:**
- **EventId**: Búsquedas rápidas por evento
- **ProcessedAt**: Útil para jobs de limpieza/archivado

---

## ⚙️ Registro de Servicios

### En Identity.API

```csharp
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<IdentityContext>>();
```

### En Otros Microservicios

Cada API que consuma eventos debe registrar el servicio con su propio `DbContext`:

```csharp
// En Accounts.API
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<AccountContext>>();

// En un futuro Courses.API (ejemplo hipotético)
builder.Services.AddScoped<IEventIdempotencyService, EventIdempotencyService<CourseContext>>();
```

---

## ✅ Verificación Práctica

### 1. Migración

La tabla se crea en la migración `Initial` de `IdentityContext` (`src/uSLearn.Identity.API/Infrastructure/Migrations`). No hay que aplicarla a mano: `AddMigration<IdentityContext, IdentityContextSeed>()` migra la base de datos al arrancar el servicio.

```bash
dotnet run --project src/uSLearn.AppHost
```

### 2. Validar Tabla Creada

```sql
SELECT * FROM [identity].ProcessedIntegrationEvents;
```

### 3. Crear una organización

Con un `PUT /api/accounts` (ver Stage.03-2) se publica `OrganizationCreatedIntegrationEvent`, y `Identity.API` lo procesa y lo registra en `ProcessedIntegrationEvents`.

### 4. Simular Evento Duplicado (opcional)

Enviar el mismo evento dos veces desde RabbitMQ o crear dos organizaciones con el mismo event ID:

**Primera vez:**
```
✅ Procesa evento → Crea tenant y admin user → Registra en ProcessedIntegrationEvents
```

**Segunda vez (duplicado):**
```
✅ Detecta evento ya procesado → Skip → Log: "Event already processed. Skipping duplicate."
```

### 5. Verificar Logs

```
[Information] Identity - Processing organization created event: 123e4567-e89b-12d3-a456-426614174000
[Information] Marked event 123e4567-e89b-12d3-a456-426614174000 as processed by OrganizationCreatedIntegrationEventHandler
[Warning] Identity - Event 123e4567-e89b-12d3-a456-426614174000 already processed. Skipping duplicate.
```

---

## 🔍 Consideraciones Importantes

### Transaccionalidad

⚠️ **Problema potencial**: Si `MarkAsProcessedAsync()` se ejecuta en una transacción separada, podría haber race conditions.

**Solución (Stage.03-4)**: Usar `ResilientTransaction` para garantizar que la lógica de negocio y el registro de idempotencia ocurran en la misma transacción.

### Limpieza de Datos Históricos

Con el tiempo, la tabla `ProcessedIntegrationEvents` crecerá. Consideraciones:

- **Archivado**: Mover eventos antiguos (> 90 días) a tabla de históricos
- **Purga**: Eliminar eventos muy antiguos si no son necesarios para auditoría
- **Job programado**: Implementar background job para limpieza automática

### Granularidad de HandlerName

Actualmente usamos `GetType().Name`, lo que significa:

```
"OrganizationCreatedIntegrationEventHandler"
```

Si cambias el nombre de la clase, se tratará como un handler nuevo. Alternativas:

- Usar nombre completo con namespace: `GetType().FullName`
- Usar un identificador estático: `const string HandlerName = "OrganizationCreated.Identity"`

---

## 🚀 Próximos Pasos

### Stage.03-4: Transacciones Resilientes

- Pasar tenant y usuario administrador de los repositorios en memoria a EF Core.
- Usar `ResilientTransaction` para que la lógica de negocio y el registro de idempotencia se confirmen en una única transacción atómica, con reintentos ante fallos transitorios.

---

## 📚 Referencias

- [Idempotent Message Processing](https://microservices.io/patterns/communication-style/idempotent-consumer.html)
- [Event-Driven Architecture Patterns](https://docs.microsoft.com/azure/architecture/patterns/category/messaging)
- [EF Core Composite Keys](https://learn.microsoft.com/ef/core/modeling/keys)
