# 🧭 Resumen del Stage.03 - Eventos de Integración e Idempotencia

## 📍 Estado Actual del Proyecto

**Branch activo:** `core/stage-03.2`

**Último commit:** Stage.03-1-Integration-Events

**Documentación:**
- ✅ Stage.01 - Creación del proyecto base
- ✅ Stage.03-1 - Eventos de Integración (committedá)
- ✅ Stage.03-2 - Idempotencia y Transaccionalidad (pendiente de commit)

---

## 🎯 Objetivos del Stage.03

El Stage.03 se divide en tres pasos:

### Stage.03-1 - Eventos de Integración ✅ COMPLETADO
- Implementación de EventBus con RabbitMQ
- Domain Events vs Integration Events
- Publicación de `OrganizationCreatedIntegrationEvent`
- Infraestructura básica de messaging

### Stage.03-2 - Idempotencia y Transaccionalidad ⏸️ PENDIENTE COMMIT
- **TransactionBehavior** para garantizar consistencia transaccional
- **AccountIntegrationEventService** para gestión de eventos dentro de transacciones
- **IdentifiedCommand pattern** para idempotencia en comandos
- **RequestManager** para detectar comandos duplicados
- **IntegrationEventLogEF** (building block compartido)
- **Outbox Pattern** para publicación transaccional de eventos
- Migración de BD con tablas `IntegrationEventLog` y `requests`

### Stage.03-3 - Idempotencia en Event Handlers 📋 PLANIFICADO
- Implementación de idempotencia en consumidores de eventos de integración
- Tabla de deduplicación para Event IDs procesados
- Procesamiento transaccional de eventos entrantes
- Garantía de exactly-once processing en event handlers

---

## 📦 Cambios Pendientes de Commitear

### Archivos Modificados

1. **src/uSLearn.Accounts.API/Application/Commands/CreateOrganizationCommandHandler.cs**
   - Agregado `CreateOrganizationIdentifiedCommandHandler` para idempotencia

2. **src/uSLearn.Accounts.API/Application/DomainEventHandlers/OrganizationCreatedDomainEventHandler.cs**
   - Cambio de `IEventBus` a `IAccountIntegrationEventService`
   - Eventos ahora se guardan transaccionalmente

3. **src/uSLearn.Accounts.API/Extensions/Extensions.cs**
   - Registro de `IIntegrationEventLogService`
   - Registro de `IAccountIntegrationEventService`
   - Registro de `TransactionBehavior` en MediatR
   - Registro de `IRequestManager`

4. **src/uSLearn.Accounts.API/Infrastructure/AccountContext.cs**
   - Configuración de `ClientRequestConfiguration`
   - Integración de `IntegrationEventLog` con `UseIntegrationEventLogs()`

5. **src/uSLearn.Accounts.API/Infrastructure/Migrations/AccountContextModelSnapshot.cs**
   - Snapshot actualizado con nuevas tablas

6. **src/uSLearn.Accounts.API/Application/IntegrationEvents/Events/OrganizationCreatedIntegrationEvent.cs**
   - Ajustes menores (si los hay)

7. **src/uSLearn.Accounts.API/buildschema.bat**
   - Script actualizado para generación de esquema

8. **src/uSLearn.Accounts.API/uSLearn.Accounts.API.csproj**
   - Referencia a `IntegrationEventLogEF.csproj`

9. **uSLearn.slnx**
   - Inclusión del proyecto `IntegrationEventLogEF`

### Archivos Nuevos (Untracked)

#### Building Block: IntegrationEventLogEF
```
src/IntegrationEventLogEF/
 ├── IntegrationEventLogEF.csproj
 ├── IntegrationEventLogEntry.cs
 ├── EventStateEnum.cs
 ├── IntegrationLogExtensions.cs
 ├── Services/
 │    ├── IIntegrationEventLogService.cs
 │    └── IntegrationEventLogService.cs
 └── Utilities/
      └── ResilientTransaction.cs
```

#### Application Layer
```
src/uSLearn.Accounts.API/Application/
 ├── Behaviors/
 │    └── TransactionBehavior.cs
 ├── Commands/
 │    ├── IdentifiedCommand.cs
 │    └── IdentifiedCommandHandler.cs
 └── IntegrationEvents/
      ├── AccountIntegrationEventService.cs
      └── IAccountIntegrationEventService.cs
```

#### Infrastructure Layer
```
src/uSLearn.Accounts.API/Infrastructure/
 ├── EntityConfigurations/
 │    └── ClientRequestConfiguration.cs
 ├── Idempotency/
 │    ├── ClientRequest.cs
 │    ├── IRequestManager.cs
 │    └── RequestManager.cs
 └── Migrations/
      ├── 20260306073915_Add_Account_EventLog.cs
      └── 20260306073915_Add_Account_EventLog.Designer.cs
```

---

## 🔄 Flujo Completo Implementado

```
Cliente → IdentifiedCommand<CreateOrganizationCommand>
  ↓
IdentifiedCommandHandler
  ├─ Verifica Request ID duplicado
  ├─ Si duplicado → Retorna resultado sin procesar
  └─ Si nuevo → Guarda Request ID y continúa
  ↓
TransactionBehavior (inicia transacción)
  ↓
CreateOrganizationCommandHandler
  ├─ Crea organización
  └─ Dispara OrganizationCreatedDomainEvent
  ↓
OrganizationCreatedDomainEventHandler
  └─ Llama a AddAndSaveEventAsync()
      └─ Guarda evento en IntegrationEventLog (misma transacción)
  ↓
TransactionBehavior (commit)
  ↓
PublishEventsThroughEventBusAsync()
  ├─ Recupera eventos pendientes por TransactionId
  ├─ Marca como InProgress
  ├─ Publica a RabbitMQ
  └─ Marca como Published
```

---

## ✅ Checklist para Continuar

### Antes de Commitear

- [ ] Ejecutar build para verificar compilación
  ```bash
  dotnet build
  ```

- [ ] Verificar que las migraciones están generadas correctamente
  ```bash
  dotnet ef migrations list --project src/uSLearn.Accounts.API
  ```

- [ ] Ejecutar tests (si existen)

- [ ] Revisar que no queden TODOs o comentarios temporales

### Commit Sugerido

```bash
git add .
git commit -m "feat(stage-03.2): Implement idempotency and transaction behavior

- Add IntegrationEventLogEF building block
- Implement TransactionBehavior for transactional consistency
- Add IdentifiedCommand pattern for idempotency
- Create RequestManager for duplicate detection
- Update domain event handlers to use AccountIntegrationEventService
- Add database migration for IntegrationEventLog and requests tables"
```

### Después del Commit

- [ ] Crear tag para el stage
  ```bash
  git tag Stage.03-2-Idempotency
  git push origin core/stage-03.2 --tags
  ```

- [ ] Actualizar README.md con referencia a Stage.03-2

---

## 📖 Documentación Creada

### Documentos del Stage.03-2

1. **docs/Stage.03-2-Idempotencia.md** (Principal)
   - Objetivo de la etapa
   - Patrones arquitectónicos implementados (resumen)
   - Decisiones arquitectónicas
   - Componentes implementados
   - Flujo completo
   - Configuración
   - Verificación práctica
   - Próximos pasos (Stage.03-3)

2. **docs/Stage.03-Patrones-Arquitectonicos.md** (Complementario)
   - Transactional Outbox Pattern (detallado)
   - Idempotent Consumer Pattern (detallado)
   - Unit of Work Pattern (detallado)
   - Pipeline Behavior Pattern (detallado)
   - Eventual Consistency Pattern (detallado)
   - Diagramas y ejemplos de código
   - Comparativa de patrones
   - Lecciones aprendidas

3. **docs/RESUMEN-Stage.03.md** (Este documento)
   - Resumen ejecutivo
   - Checklist de trabajo
   - Estado del proyecto

**Recomendación de lectura:**
1. Leer `Stage.03-2-Idempotencia.md` para entender la implementación
2. Leer `Stage.03-Patrones-Arquitectonicos.md` para profundizar en los patrones
3. Usar `RESUMEN-Stage.03.md` como referencia rápida

---

## 🚀 Próximos Stages Planificados

| Stage | Descripción | Estado |
|-------|-------------|--------|
| Stage.03-3 | Idempotencia en Event Handlers | 📋 Planificado |
| Stage.04 | Identity con Duende IdentityServer | 📋 Planificado |
| Stage.05 | WebApp Blazor + Radzen | 📋 Planificado |
| Stage.06 | Webhooks | 📋 Planificado |

---

## 🧩 Conceptos Implementados en Stage.03

### Stage.03-1 ✅
- ✅ EventBus abstraction
- ✅ RabbitMQ integration
- ✅ Domain Events
- ✅ Integration Events
- ✅ Domain Event Handlers

### Stage.03-2 ✅
- ✅ Idempotency pattern (comandos)
- ✅ Outbox pattern (IntegrationEventLog)
- ✅ Transaction Behavior
- ✅ Request deduplication
- ✅ Transactional messaging
- ✅ MediatR pipeline behaviors
- ✅ Unit of Work pattern
- ✅ Eventual consistency

### Stage.03-3 📋 (Planificado)
- ⏳ Idempotency pattern (event handlers)
- ⏳ Event deduplication
- ⏳ Exactly-once processing
- ⏳ Transactional event consumption

---

## 🔍 Archivos Clave a Revisar

### Para entender Idempotencia:
1. `src/uSLearn.Accounts.API/Application/Commands/IdentifiedCommand.cs`
2. `src/uSLearn.Accounts.API/Application/Commands/IdentifiedCommandHandler.cs`
3. `src/uSLearn.Accounts.API/Infrastructure/Idempotency/RequestManager.cs`

### Para entender Transaccionalidad:
1. `src/uSLearn.Accounts.API/Application/Behaviors/TransactionBehavior.cs`
2. `src/uSLearn.Accounts.API/Application/IntegrationEvents/AccountIntegrationEventService.cs`
3. `src/IntegrationEventLogEF/Services/IntegrationEventLogService.cs`

### Para entender el Building Block:
1. `src/IntegrationEventLogEF/IntegrationEventLogEntry.cs`
2. `src/IntegrationEventLogEF/IntegrationLogExtensions.cs`

---

## 💡 Notas Importantes

1. **IntegrationEventLogEF** es un **building block reutilizable** que puede usarse en otros microservicios

2. El **TransactionBehavior** se ejecuta DESPUÉS del **LoggingBehavior** en el pipeline de MediatR

3. Los **eventos de integración NO se publican** si la transacción falla

4. El **RequestManager** usa un índice único en la BD como última línea de defensa contra duplicados

5. En producción, manejar `DbUpdateException` para violaciones de constraint único en `requests` table

---

## 🎓 Lecciones Aprendidas

### Idempotencia
- El cliente debe generar un Request ID único y enviarlo con cada comando
- El Request ID debe persistirse ANTES de procesar el comando
- Si el comando falla, el Request ID queda registrado, evitando reintentos automáticos
- Considerar TTL para limpiar Request IDs antiguos

### Transaccionalidad
- Usar `ExecutionStrategy` para resilience ante fallos transitorios
- No publicar eventos antes del commit de la transacción
- Guardar eventos en la misma transacción que las entidades de dominio
- Manejar fallos en publicación con estados (InProgress, Published, Failed)

### Outbox Pattern
- Permite publicar eventos con garantía "at least once"
- Separa la responsabilidad de guardar vs. publicar
- Requiere proceso background para reintentar eventos fallidos
- Considerar partitioning por TransactionId para performance

---

## 📚 Referencias Técnicas

- [Idempotent Consumers](https://microservices.io/patterns/communication-style/idempotent-consumer.html)
- [Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html)
- [MediatR Behaviors](https://github.com/jbogard/MediatR/wiki/Behaviors)
- [EF Core Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
- [EF Core Execution Strategies](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)
