# Stage.01 - Creación del Proyecto Base

## 🎯 Objetivo de la Etapa

Crear la estructura base del proyecto utilizando **.NET Aspire** como orquestador, estableciendo los cimientos para una arquitectura de microservicios con:

- Un servicio API básico
- Una aplicación web Blazor
- Configuración compartida mediante Service Defaults
- Infraestructura de caché con Redis
- Health checks configurados
- Service Discovery integrado

---

## 🏗️ Decisiones Arquitectónicas

### ¿Por qué .NET Aspire?

.NET Aspire proporciona desde el inicio:

- **Orquestación local** sin necesidad de Docker Compose o Kubernetes
- **Service Discovery** automático entre servicios
- **Observabilidad** integrada (logs, métricas, traces)
- **Dashboard** unificado para monitoreo
- **Configuración centralizada** mediante Service Defaults

### Estructura Inicial

Se ha optado por una estructura simple y escalable:

```
src/
 ├── Aspire.uSLearn.AppHost/          → Orquestador Aspire
 ├── Aspire.uSLearn.ServiceDefaults/  → Configuración compartida
 ├── Aspire.uSLearn.ApiService/       → API backend inicial
 └── Aspire.uSLearn.Web/              → Frontend Blazor
```

### Componentes Clave

#### 1. **AppHost (Orquestador)**

El `AppHost` es el punto de entrada que orquesta todos los servicios:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var apiService = builder.AddProject<Projects.Aspire_uSLearn_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Aspire_uSLearn_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
```

**Características configuradas:**

- ✅ Redis como caché distribuido
- ✅ Health checks en `/health` para ambos servicios
- ✅ Dependencias explícitas (`WaitFor`) para arranque ordenado
- ✅ Service Discovery mediante referencias
- ✅ Endpoints externos para el frontend

#### 2. **ServiceDefaults**

Proyecto compartido que encapsula configuración común:

- Health checks
- Service Discovery
- OpenTelemetry (tracing, metrics, logs)
- Resilience patterns

Todos los servicios referencian este proyecto mediante:

```csharp
builder.AddServiceDefaults();
```

#### 3. **ApiService**

API minimalista con:

- Endpoint de ejemplo `/weatherforecast`
- OpenAPI configurado (Swagger en desarrollo)
- Health checks integrados
- Service Defaults aplicados

```csharp
// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Configuración de OpenAPI
builder.Services.AddOpenApi();
```

#### 4. **Web (Blazor)**

Aplicación Blazor con:

- Render mode: **Interactive Server**
- Output caching mediante Redis
- HttpClient configurado con Service Discovery
- Páginas de ejemplo: Home, Counter, Weather

```csharp
// Blazor configuration
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Output cache con Redis
builder.AddRedisOutputCache("cache");

// HttpClient con Service Discovery
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

**Componentes Blazor incluidos:**

- `App.razor` → Raíz de la aplicación
- `Routes.razor` → Enrutamiento
- `MainLayout.razor` → Layout principal
- `NavMenu.razor` → Navegación
- Páginas: `Home.razor`, `Counter.razor`, `Weather.razor`

---

## 📦 Estructura Creada

```
Aspire.uSLearn/
│
├── src/
│   ├── Aspire.uSLearn.AppHost/
│   │   ├── AppHost.cs                          → Orquestador principal
│   │   └── Aspire.uSLearn.AppHost.csproj
│   │
│   ├── Aspire.uSLearn.ServiceDefaults/
│   │   └── Aspire.uSLearn.ServiceDefaults.csproj
│   │
│   ├── Aspire.uSLearn.ApiService/
│   │   ├── Program.cs                          → API minimalista
│   │   └── Aspire.uSLearn.ApiService.csproj
│   │
│   └── Aspire.uSLearn.Web/
│       ├── Program.cs                          → Configuración Blazor
│       ├── WeatherApiClient.cs                 → Cliente HTTP
│       ├── Components/
│       │   ├── App.razor
│       │   ├── Routes.razor
│       │   ├── Layout/
│       │   │   ├── MainLayout.razor
│       │   │   └── NavMenu.razor
│       │   └── Pages/
│       │       ├── Home.razor
│       │       ├── Counter.razor
│       │       └── Weather.razor
│       └── Aspire.uSLearn.Web.csproj
│
├── docs/
│   └── Stage.01-project creation.md           → Este documento
│
└── README.md                                   → Documentación general
```

---

## 🔧 Cómo se Creó

### 1. Crear la solución con Aspire

```bash
dotnet new aspire-starter -n Aspire.uSLearn
```

Este comando genera automáticamente:
- AppHost
- ServiceDefaults
- ApiService (con template minimalista)
- Web (Blazor con Interactive Server)

### 2. Configurar Redis

Redis se añade directamente en el `AppHost.cs`:

```csharp
var cache = builder.AddRedis("cache");
```

Aspire se encarga de:
- Levantar el contenedor Redis
- Configurar la conexión
- Inyectar la configuración en los servicios que lo referencian

### 3. Configurar Health Checks

```csharp
.WithHttpHealthCheck("/health")
```

Los health checks permiten a Aspire:
- Validar que los servicios están operativos
- Mostrar el estado en el Dashboard
- Gestionar dependencias de inicio

---

## ✅ Verificación

Para ejecutar el proyecto:

```bash
dotnet run --project src/Aspire.uSLearn.AppHost
```

### Comprobaciones:

1. **Dashboard de Aspire** se abre automáticamente en el navegador
2. Se visualizan todos los servicios:
   - ✅ `cache` (Redis)
   - ✅ `apiservice`
   - ✅ `webfrontend`
3. Los health checks aparecen en verde
4. La aplicación Blazor es accesible y funcional
5. El endpoint `/weatherforecast` del API devuelve datos

### Endpoints:

- **Dashboard Aspire**: `http://localhost:15XXX` (puerto dinámico)
- **Web Frontend**: `https://localhost:7XXX`
- **API Service**: `https://localhost:7XXX` (descubierto automáticamente)

---

## 🧪 Código Relevante

### Service Discovery en acción

En `WeatherApiClient.cs`, el cliente HTTP usa el nombre lógico del servicio:

```csharp
client.BaseAddress = new("https+http://apiservice");
```

Aspire resuelve `apiservice` automáticamente sin necesidad de configurar URLs manualmente.

### Output Caching con Redis

En `Program.cs` del proyecto Web:

```csharp
builder.AddRedisOutputCache("cache");
```

El nombre `"cache"` corresponde al definido en el AppHost, estableciendo la conexión automáticamente.

---

## 🔮 Consideraciones Futuras

Esta etapa inicial establece los fundamentos. En etapas posteriores se añadirán:

- **Stage.02**: Eventos de dominio dentro de los servicios
- **Stage.03**: Comunicación entre servicios mediante eventos de integración
- **Stage.04**: Autenticación con Duende IdentityServer
- **Stage.05**: Mejoras en el frontend Blazor con Radzen
- **Stage.06**: Sistema de webhooks

### Preparación para Microservicios

Aunque actualmente solo hay un `ApiService`, la estructura permite añadir fácilmente nuevos servicios:

```
src/Services/
 ├── Accounts/
 ├── Identity/
 └── ...
```

Cada servicio será independiente y se comunicará mediante:
- HTTP (síncronas)
- Eventos de integración (asíncronas)

### Preparación para BuildingBlocks

En futuras etapas se crearán componentes reutilizables:

```
src/BuildingBlocks/
 ├── EventBus/         → Abstracción de mensajería
 ├── SharedKernel/     → Elementos compartidos (Value Objects, etc.)
 └── Infrastructure/   → Utilidades comunes
```

---

## 📚 Recursos Adicionales

- [Documentación oficial de .NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Blazor Documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Service Discovery in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)

---

## 🧩 Próximos Pasos

➡️ **Stage.02**: Implementación de Eventos de Dominio

En la siguiente etapa se introducirán:
- Domain Events dentro de los agregados
- MediatR para dispatching de eventos
- Event handlers
- Patrón Repository

---

## 📝 Resumen

En esta etapa inicial se ha logrado:

✅ Proyecto base con .NET Aspire configurado  
✅ Blazor WebApp con Interactive Server  
✅ API Service minimalista  
✅ Redis para output caching  
✅ Health checks configurados  
✅ Service Discovery funcional  
✅ Dashboard de observabilidad operativo  

**Resultado**: Una aplicación distribuida funcional lista para evolucionar incrementalmente hacia una arquitectura de microservicios completa.