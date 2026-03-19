## 🧭 Overview

Este repositorio contiene una **aplicación de referencia basada en Microservicios**, diseñada como laboratorio práctico para explorar patrones modernos de desarrollo con el ecosistema .NET.

El proyecto evoluciona en **etapas incrementales**, incorporando progresivamente capacidades arquitectónicas clave como:

- Eventos de dominio
- Integración entre servicios
- Idempotencia
- Autenticación y autorización
- Frontend moderno con Blazor
- Webhooks y extensibilidad

El objetivo principal es servir como:

- 📚 guía educativa
- 🧪 sandbox técnico
- 🧱 base reusable para proyectos reales

---

## 🏗️ Stack Tecnológico

### Backend

- ASP.NET Core
- Arquitectura basada en Microservicios
- Domain Driven Design (DDD) (lightweight)
- Eventos de dominio e integración
- Messaging / Event-driven communication
- Idempotent consumers

### Orquestación y Observabilidad

- .NET Aspire
- Service Discovery
- Distributed tracing
- Health checks
- Centralized configuration

### Frontend

- Blazor WebApp
- Componentes UI con Radzen

### Seguridad

- Duende IdentityServer (Identity & Access Management)

---

## 🎯 Objetivos Arquitectónicos

Este proyecto está construido siguiendo los siguientes principios:

- Separación clara entre dominios
- Independencia de servicios
- Comunicación desacoplada mediante eventos
- Observabilidad desde el inicio
- Evolución incremental sin “big bang architecture”

---

## 🧩 Estructura General (High Level)

```
src/
 ├── AppHost/           → Orquestación Aspire
 ├── ServiceDefaults/   → Configuración compartida
 ├── Services/
 │    ├── Accounts/
 │    ├── Identity/
 │    └── ...
 ├── Web/
 │    └── BlazorApp/
 └── BuildingBlocks/
      ├── EventBus/
      ├── SharedKernel/
      └── Infrastructure/
```

---

## 🪜 Roadmap por Etapas

El desarrollo se divide en fases documentadas individualmente:

| Stage | Descripción | Estado |
| --- | --- | --- |
| **Stage.01** | Creación del proyecto base | ✅ Completado |
| **Stage.02** | Implementación de Eventos de Dominio | ✅ Completado |
| **Stage.03** | **Eventos de Integración + Idempotencia** | ✅ Completado |
| ↳ Stage.03-1 | Eventos de Integración con RabbitMQ | ✅ Completado |
| ↳ Stage.03-2 | Idempotencia y Transaccionalidad (Comandos) | ✅ Completado |
| ↳ Stage.03-3 | Idempotencia en Event Handlers | ✅ Completado |
| ↳ Stage.03-4 | Transacciones Resilientes | ✅ Completado |
| **Stage.04** | **Observabilidad y Validaciones** | 🚧 En progreso |
| ↳ Stage.04-1 | Logging Enriquecido | ✅ Completado |
| ↳ Stage.04-2 | Validaciones con FluentValidation | 📋 Planificado |
| ↳ Stage.04-3 | Telemetría con OpenTelemetry | 📋 Planificado |
| **Stage.05** | Identity con Duende IdentityServer | 📋 Planificado |
| **Stage.06** | WebApp Blazor + Radzen | 📋 Planificado |
| **Stage.07** | Webhooks y extensibilidad | 📋 Planificado |

### 📚 Documentación Disponible

Cada etapa tiene su documentación detallada en:

```
/docs/Stage.XX-N-<name>.md
```

**Documentos principales:**
- [`docs/Stage.01-project creation.md`](docs/Stage.01-project%20creation.md) - Setup inicial con .NET Aspire
- [`docs/Stage.02-domain events.md`](docs/Stage.02-domain%20events.md) - Eventos de dominio con MediatR
- [`docs/Stage.03-1-Eventos de Integracion.md`](docs/Stage.03-1-Eventos%20de%20Integracion.md) - Eventos de integración con RabbitMQ
- [`docs/Stage.03-2-Idempotencia.md`](docs/Stage.03-2-Idempotencia.md) - Idempotencia en comandos
- [`docs/Stage.03-3-Idempotency-Handler.md`](docs/Stage.03-3-Idempotency-Handler.md) - Idempotencia en handlers
- [`docs/Stage.03-4-Transacciones-Resilientes.md`](docs/Stage.03-4-Transacciones-Resilientes.md) - Transacciones resilientes en handlers de integración
- [`docs/Stage.03-Patrones-Arquitectonicos.md`](docs/Stage.03-Patrones-Arquitectonicos.md) - Guía detallada de patrones arquitectónicos
- [`docs/Stage.04-1-Logging.md`](docs/Stage.04-1-Logging.md) - Logging enriquecido con structured logging
- [`docs/Stage.04-2-Validations.md`](docs/Stage.04-2-Validations.md) - Validaciones con FluentValidation y manejo de excepciones
- [`docs/Stage.04-3-Telemetry.md`](docs/Stage.04-3-Telemetry.md) - Telemetría con OpenTelemetry (traces, metrics, distributed tracing)

---

## 🚀 Cómo ejecutar el proyecto

### Requisitos

- .NET SDK (versión recomendada: latest LTS)
- Docker (opcional pero recomendado)
- IDE compatible (Visual Studio / Rider / VS Code)

### Ejecutar con Aspire

```
dotnet run--project src/AppHost
```

Aspire iniciará:

- Microservicios
- Dependencias
- Dashboard de observabilidad

---

## 🧠 Conceptos Arquitectónicos Clave

Este repositorio prioriza el aprendizaje práctico de:

- Domain Events vs Integration Events
- Eventual Consistency
- Idempotent processing
- Vertical Slice Architecture
- Contract-first integration
- Boundary context isolation

---

## 📖 Documentación

Toda la evolución del proyecto está documentada paso a paso en:

```
/docs
```

Cada documento incluye:

- Objetivo de la etapa
- Decisiones arquitectónicas
- Estructura añadida
- Código relevante
- Consideraciones futuras

---

## ⚠️ Estado del Proyecto

Proyecto en construcción incremental.

No pretende representar una arquitectura definitiva, sino una **referencia evolutiva** mostrando trade-offs reales.

---

## 🤝 Contribuciones

Este repositorio está pensado como referencia personal y educativa, pero cualquier sugerencia o mejora es bienvenida.

---

## 🧩 Próximos pasos

➡️ Ver: `Stage.01-project creation.md`

---