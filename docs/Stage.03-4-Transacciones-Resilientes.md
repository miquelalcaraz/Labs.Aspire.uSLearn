# Stage.03-4 - Transacciones Resilientes

## 🎯 Objetivo

Implementar transacciones resilientes que garanticen **atomicidad** y **consistencia** en operaciones complejas del handler, asegurando que la creación de entidades y el registro de idempotencia ocurran dentro de una única transacción con capacidad de retry automático ante fallos transitorios.

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué Transacciones Resilientes?

En el Stage 03-3 implementamos idempotencia, pero las operaciones no estaban agrupadas en una transacción:

**❌ Problema Anterior (Stage 03-3):**
```csharp
// Operaciones separadas (sin transacción explícita)
var tenant = await CreateTenantAsync(@event);           // SaveChanges() #1
var adminUser = await CreateAdminUserAsync(@event);     // SaveChanges() #2
await MarkAsProcessedAsync(@event.Id, handlerName);     // SaveChanges() #3
```

**Riesgos:**
1. **Inconsistencia parcial**: Si falla `CreateAdminUserAsync`, el Tenant ya fue persistido
2. **Idempotencia incompleta**: Si falla `MarkAsProcessedAsync`, el evento puede procesarse dos veces
3. **Sin retry strategy**: No hay manejo automático de fallos transitorios (timeouts, deadlocks)

**✅ Solución (Stage 03-4):**
```csharp
await ResilientTransaction.New(_context).ExecuteAsync(async () =>
{
    var tenant = await CreateTenantAsync(@event);
    var adminUser = await CreateAdminUserAsync(@event, tenant.Id);
    await MarkAsProcessedAsync(@event.Id, handlerName);
    // Todas las operaciones se confirman juntas o ninguna persiste
});
```

**Ventajas:**
- ✅ **Atomicidad**: Todo o nada (commit o rollback)
- ✅ **Consistency**: Estado siempre consistente
- ✅ **Retry automático**: EF Core `ExecutionStrategy` maneja fallos transitorios
- ✅ **Rollback explícito**: En caso de error, revierte todos los cambios

---

## 📊 Arquitectura de la Solución

```
┌──────────────────────────────────────────────────────────────────┐
│  OrganizationCreatedIntegrationEventHandler                      │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ 1. IsProcessedAsync() - Fuera de TX                       │  │
│  │    └─ Si duplicado → Skip                                 │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │ 2. ResilientTransaction.ExecuteAsync()                    │  │
│  │    ┌────────────────────────────────────────────────────┐ │  │
│  │    │ ExecutionStrategy.ExecuteAsync()                   │ │  │
│  │    │  ┌──────────────────────────────────────────────┐  │ │  │
│  │    │  │ BeginTransactionAsync()                      │  │ │  │
│  │    │  ├──────────────────────────────────────────────┤  │ │  │
│  │    │  │ CreateTenantAsync()                          │  │ │  │
│  │    │  │   → context.SaveChangesAsync()               │  │ │  │
│  │    │  ├──────────────────────────────────────────────┤  │ │  │
│  │    │  │ CreateAdminUserAsync()                       │  │ │  │
│  │    │  │   → context.SaveChangesAsync()               │  │ │  │
│  │    │  ├──────────────────────────────────────────────┤  │ │  │
│  │    │  │ MarkAsProcessedAsync()                       │  │ │  │
│  │    │  │   → context.SaveChangesAsync()               │  │ │  │
│  │    │  ├──────────────────────────────────────────────┤  │ │  │
│  │    │  │ CommitAsync() ✅                             │  │ │  │
│  │    │  └──────────────────────────────────────────────┘  │ │  │
│  │    │     │                                               │ │  │
│  │    │     ▼ (Si Exception)                                │ │  │
│  │    │  RollbackAsync() ❌ → Retry si transient error     │ │  │
│  │    └────────────────────────────────────────────────────┘ │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 📝 Componentes Implementados

### 1. **ResilientTransaction (Mejorado)**

#### **Versión Básica (ExecuteAsync sin retorno)**

```csharp
public async Task ExecuteAsync(Func<Task> action)
{
    var strategy = _context.Database.CreateExecutionStrategy();
    
    await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await action();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    });
}
```

**Uso:**
```csharp
await ResilientTransaction.New(_context).ExecuteAsync(async () =>
{
    await CreateTenantAsync(@event);
    await CreateAdminUserAsync(@event, tenantId);
    await MarkAsProcessedAsync(eventId, handlerName);
});
```

---

#### **Versión con Retorno (ExecuteAsync<T>)**

```csharp
public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
{
    var strategy = _context.Database.CreateExecutionStrategy();
    
    return await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var result = await action();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    });
}
```

**Uso:**
```csharp
var tenant = await ResilientTransaction.New(_context).ExecuteAsync(async () =>
{
    var t = await CreateTenantAsync(@event);
    await MarkAsProcessedAsync(eventId, handlerName);
    return t;
});
```

---

#### **Versión con Callback (Para Outbox Pattern)**

```csharp
public async Task ExecuteAsync(Func<Task> action, Func<Task> onCommitted)
{
    var strategy = _context.Database.CreateExecutionStrategy();
    
    await strategy.ExecuteAsync(async () =>
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await action();
            await transaction.CommitAsync();
            
            // Ejecutar callback solo después de commit exitoso
            await onCommitted();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    });
}
```

**Uso (Stage 03-5 - Outbox Pattern):**
```csharp
await ResilientTransaction.New(_context).ExecuteAsync(
    action: async () =>
    {
        await CreateTenantAsync(@event);
        await SaveEventToOutbox(@event);
    },
    onCommitted: async () =>
    {
        await _eventBus.PublishAsync(@event);
    }
);
```

---

### 2. **Repositorios EF Core**

Reemplazamos los repositorios InMemory por implementaciones basadas en Entity Framework Core.

#### **TenantRepository.cs**

```csharp
public class TenantRepository : ITenantRepository
{
    private readonly IdentityContext _context;

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return tenant;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id) =>
        await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Tenant?> GetByOrganizationIdAsync(Guid organizationId) =>
        await _context.Tenants.FirstOrDefaultAsync(t => t.OrganizationId == organizationId);
}
```

#### **UserRepository.cs**

```csharp
public class UserRepository : IUserRepository
{
    private readonly IdentityContext _context;

    public async Task<AdminUser> CreateAsync(AdminUser user)
    {
        _context.AdminUsers.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<AdminUser?> GetByIdAsync(Guid id) =>
        await _context.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<IEnumerable<AdminUser>> GetByTenantIdAsync(Guid tenantId) =>
        await _context.AdminUsers.Where(u => u.TenantId == tenantId).ToListAsync();
}
```

**⚠️ Nota importante:**
- Cambiamos de `Singleton` a `Scoped` lifetime (necesario para EF Core DbContext)
- Los repositorios ahora participan de la misma transacción del `IdentityContext`

---

### 3. **IdentityContext (Actualizado)**

Configuración de entidades con EF Core:

```csharp
public class IdentityContext : DbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.UseEventIdempotency(); // Stage 03-3
        
        ConfigureTenant(modelBuilder);
        ConfigureAdminUser(modelBuilder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(builder =>
        {
            builder.ToTable("Tenants");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
            builder.Property(t => t.OrganizationId).IsRequired();
            builder.HasIndex(t => t.OrganizationId).IsUnique(); // 1 Tenant por Organization
        });
    }

    private static void ConfigureAdminUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(builder =>
        {
            builder.ToTable("AdminUsers");
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
            builder.Property(u => u.TenantId).IsRequired();
            builder.HasIndex(u => u.TenantId);
            builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique(); // Email único por tenant
        });
    }
}
```

---

### 4. **Handler Refactorizado**

```csharp
public class OrganizationCreatedIntegrationEventHandler 
    : IIntegrationEventHandler<OrganizationCreatedIntegrationEvent>
{
    private readonly IdentityContext _context;
    // ... otros servicios

    public async Task Handle(OrganizationCreatedIntegrationEvent @event)
    {
        var handlerName = GetType().Name;

        // ✅ PASO 1: Verificar idempotencia (fuera de transacción)
        if (await _idempotencyService.IsProcessedAsync(@event.Id, handlerName))
        {
            _logger.LogWarning("Event {EventId} already processed. Skipping.", @event.Id);
            return;
        }

        // ✅ PASO 2: Ejecutar toda la lógica en transacción resiliente
        await ResilientTransaction.New(_context).ExecuteAsync(async () =>
        {
            _logger.LogInformation("Starting resilient transaction for event {EventId}", @event.Id);

            var tenant = await CreateTenantAsync(@event);
            var adminUser = await CreateAdminUserAsync(@event, tenant.Id);
            
            // Marcar como procesado dentro de la misma transacción
            await _idempotencyService.MarkAsProcessedAsync(
                @event.Id, 
                handlerName, 
                @event.GetType().Name);

            _logger.LogInformation(
                "Tenant {TenantId} and Admin User {UserId} created", 
                tenant.Id, adminUser.Id);
        });

        _logger.LogInformation("Successfully completed resilient transaction for event {EventId}", @event.Id);
    }
}
```

---

### 5. **Registro de Servicios (DI)**

**❌ ANTES (InMemory - Singleton):**
```csharp
services.AddSingleton<ITenantRepository, InMemoryTenantRepository>();
services.AddSingleton<IUserRepository, InMemoryUserRepository>();
```

**✅ AHORA (EF Core - Scoped):**
```csharp
services.AddScoped<ITenantRepository, TenantRepository>();
services.AddScoped<IUserRepository, UserRepository>();
```

**Razón del cambio:**
- `DbContext` en EF Core es **Scoped** por defecto
- Los repositorios que lo usan también deben ser **Scoped**
- Permite compartir la misma transacción entre múltiples operaciones

---

## 🔄 Flujo de Ejecución Completo

### Escenario 1: Procesamiento Exitoso

```
1. Event llega a Handler
2. IsProcessedAsync() → false (no duplicado)
3. ResilientTransaction.ExecuteAsync() inicia
   ├─ ExecutionStrategy inicia retry logic
   ├─ BeginTransactionAsync()
   ├─ CreateTenantAsync()
   │   └─ INSERT INTO Tenants → pending
   ├─ CreateAdminUserAsync()
   │   └─ INSERT INTO AdminUsers → pending
   ├─ MarkAsProcessedAsync()
   │   └─ INSERT INTO ProcessedIntegrationEvents → pending
   └─ CommitAsync() ✅
       └─ Todas las inserciones se persisten atómicamente
4. Log: "Successfully completed resilient transaction"
```

---

### Escenario 2: Fallo en CreateAdminUserAsync

```
1. Event llega a Handler
2. IsProcessedAsync() → false
3. ResilientTransaction.ExecuteAsync() inicia
   ├─ BeginTransactionAsync()
   ├─ CreateTenantAsync()
   │   └─ INSERT INTO Tenants → pending
   ├─ CreateAdminUserAsync()
   │   └─ ❌ EXCEPTION: Duplicate email constraint
   ├─ CATCH block ejecuta
   └─ RollbackAsync() ❌
       └─ Tenant NO persiste en DB
       └─ ProcessedIntegrationEvent NO se crea
4. Exception se re-lanza
5. Message broker reintenta el evento
6. Handler procesa nuevamente (idempotencia sigue siendo false)
```

---

### Escenario 3: Fallo Transitorio (Timeout de Red)

```
1. Event llega a Handler
2. IsProcessedAsync() → false
3. ResilientTransaction.ExecuteAsync() inicia
   ├─ ExecutionStrategy.ExecuteAsync() detecta error transitorio
   ├─ BeginTransactionAsync()
   ├─ CreateTenantAsync()
   │   └─ ❌ SqlException: Timeout
   ├─ RollbackAsync()
   ├─ ExecutionStrategy RETRY #1 (automático)
   ├─ BeginTransactionAsync()
   ├─ CreateTenantAsync()
   │   └─ ✅ INSERT INTO Tenants → pending
   ├─ CreateAdminUserAsync()
   │   └─ ✅ INSERT INTO AdminUsers → pending
   ├─ MarkAsProcessedAsync()
   │   └─ ✅ INSERT INTO ProcessedIntegrationEvents → pending
   └─ CommitAsync() ✅
4. Procesamiento exitoso después de retry
```

---

## 🗄️ Esquema de Base de Datos

### Migración a Crear

```bash
dotnet ef migrations add AddTenantAndAdminUserTables --context IdentityContext --project src/uSLearn.Identity.API
```

### SQL Generado

```sql
-- Tenants
CREATE TABLE identity.Tenants (
    Id uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    CONSTRAINT PK_Tenants PRIMARY KEY (Id)
);

CREATE UNIQUE INDEX IX_Tenants_OrganizationId ON identity.Tenants (OrganizationId);

-- AdminUsers
CREATE TABLE identity.AdminUsers (
    Id uniqueidentifier NOT NULL,
    TenantId uniqueidentifier NOT NULL,
    Email nvarchar(255) NOT NULL,
    TemporaryPassword nvarchar(100) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    CONSTRAINT PK_AdminUsers PRIMARY KEY (Id)
);

CREATE INDEX IX_AdminUsers_TenantId ON identity.AdminUsers (TenantId);
CREATE UNIQUE INDEX IX_AdminUsers_TenantId_Email ON identity.AdminUsers (TenantId, Email);
```

---

## ✅ Verificación Práctica

### 1. Crear y Aplicar Migración

```bash
# Crear migración
dotnet ef migrations add AddTenantAndAdminUserTables --context IdentityContext --project src/uSLearn.Identity.API

# Aplicar migración
dotnet ef database update --context IdentityContext --project src/uSLearn.Identity.API
```

### 2. Validar Tablas Creadas

```sql
SELECT * FROM identity.Tenants;
SELECT * FROM identity.AdminUsers;
SELECT * FROM identity.ProcessedIntegrationEvents;
```

### 3. Probar Transacción Exitosa

**Enviar evento desde Accounts.API:**
```bash
# Crear organización → Publica OrganizationCreatedIntegrationEvent
POST http://localhost:5000/api/v1/organizations
{
  "name": "ACME Corp",
  "contactEmail": "contact@acme.com"
}
```

**Verificar en Identity DB:**
```sql
-- Debe haber 1 Tenant
SELECT * FROM identity.Tenants WHERE Name = 'ACME Corp';

-- Debe haber 1 AdminUser
SELECT * FROM identity.AdminUsers WHERE Email = 'admin@acmecorp.com';

-- Debe estar marcado como procesado
SELECT * FROM identity.ProcessedIntegrationEvents 
WHERE EventType = 'OrganizationCreatedIntegrationEvent';
```

### 4. Simular Fallo y Rollback

**Modificar temporalmente el handler para forzar un error:**

```csharp
private async Task<AdminUser> CreateAdminUserAsync(OrganizationCreatedIntegrationEvent @event, Guid tenantId)
{
    // ❌ Forzar excepción para probar rollback
    throw new InvalidOperationException("Test rollback");
    
    var adminUser = new AdminUser { ... };
    return await _userRepository.CreateAsync(adminUser);
}
```

**Resultado esperado:**
```sql
-- NO debe haber Tenant (rollback completo)
SELECT COUNT(*) FROM identity.Tenants; -- 0

-- NO debe haber AdminUser
SELECT COUNT(*) FROM identity.AdminUsers; -- 0

-- NO debe estar marcado como procesado
SELECT COUNT(*) FROM identity.ProcessedIntegrationEvents; -- 0
```

**Logs esperados:**
```
[Information] Starting resilient transaction for event {EventId}
[Error] Failed to process event: Test rollback
[Information] Transaction rolled back
```

---

## 🔍 Comparación: Antes vs. Ahora

| Aspecto | Stage 03-3 (Idempotencia) | Stage 03-4 (Transacciones Resilientes) |
|---------|---------------------------|----------------------------------------|
| **Atomicidad** | ❌ Operaciones separadas | ✅ Todo en una transacción |
| **Consistencia** | ⚠️ Posible estado parcial | ✅ Estado siempre consistente |
| **Idempotencia** | ✅ Verificación funciona | ✅ Marcado dentro de TX |
| **Retry automático** | ❌ Manual | ✅ EF Core ExecutionStrategy |
| **Rollback** | ❌ Sin rollback | ✅ Rollback automático en error |
| **Persistencia** | ❌ InMemory (volátil) | ✅ SQL Server (persistente) |
| **Escalabilidad** | ❌ No escala | ✅ Soporta múltiples instancias |

---

## ⚠️ Consideraciones Importantes

### 1. Verificación de Idempotencia Fuera de Transacción

```csharp
// ✅ CORRECTO: Verificar fuera de transacción
if (await _idempotencyService.IsProcessedAsync(@event.Id, handlerName))
    return;

await ResilientTransaction.New(_context).ExecuteAsync(async () => { ... });
```

**Razón:**
- Evita locks innecesarios en eventos duplicados
- Si verificáramos dentro de la transacción, cada duplicado bloquearía recursos

### 2. ExecutionStrategy y Transacciones Explícitas

⚠️ **Error común:**
```csharp
// ❌ INCORRECTO: No usar BeginTransaction con ExecutionStrategy por defecto
using var transaction = await _context.Database.BeginTransactionAsync();
await action();
await transaction.CommitAsync();
```

✅ **Correcto (nuestra implementación):**
```csharp
var strategy = _context.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    using var transaction = await _context.Database.BeginTransactionAsync();
    await action();
    await transaction.CommitAsync();
});
```

**Documentación:**
- [EF Core Connection Resiliency](https://docs.microsoft.com/ef/core/miscellaneous/connection-resiliency)

### 3. Timeout de Transacciones Largas

Para operaciones muy largas, configurar timeout:

```csharp
services.AddDbContext<IdentityContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
        
        sqlOptions.CommandTimeout(60); // 60 segundos timeout
    });
});
```

### 4. Isolación de Transacciones

Por defecto, EF Core usa `ReadCommitted`. Para operaciones críticas:

```csharp
await strategy.ExecuteAsync(async () =>
{
    await using var transaction = await _context.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable); // Máxima consistencia
    
    await action();
    await transaction.CommitAsync();
});
```

---

## 🚀 Próximos Pasos

### Stage 03-5: Outbox Pattern

Implementar patrón Outbox para garantizar que los eventos se publiquen de manera confiable:

1. **Guardar evento en tabla Outbox** dentro de la misma transacción
2. **Background worker** lee eventos pendientes y los publica
3. **Garantiza at-least-once delivery** incluso si el broker falla

**Estructura:**
```csharp
await ResilientTransaction.New(_context).ExecuteAsync(
    action: async () =>
    {
        await CreateTenantAsync(@event);
        await SaveToOutboxAsync(new TenantCreatedEvent(tenant.Id));
    },
    onCommitted: async () =>
    {
        await _outboxPublisher.ProcessPendingEventsAsync();
    }
);
```

---

## 📚 Referencias

- [EF Core Transactions](https://learn.microsoft.com/ef/core/saving/transactions)
- [Connection Resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency)
- [Execution Strategies](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency#execution-strategies)
- [Microservices Patterns: Saga](https://microservices.io/patterns/data/saga.html)
