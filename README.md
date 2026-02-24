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

| Stage | Descripción |
| --- | --- |
| Stage.01 | Creación del proyecto base |
| Stage.02 | Implementación de Eventos de Dominio |
| Stage.03 | Eventos de Integración + Idempotencia |
| Stage.04 | Identity con Duende |
| Stage.05 | WebApp Blazor + Radzen |
| Stage.06 | Webhooks |

Cada etapa tendrá su documentación en:

```
/docs/Stage.XX-<name>.md
```

Ejemplo:

```
docs/Stage.01-project creation.md
```

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