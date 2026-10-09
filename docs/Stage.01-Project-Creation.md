# Stage.01 - Base Project Creation

## 🎯 Stage Goal

Create the base structure of the project using **.NET Aspire** as the orchestrator, laying the foundations for a microservices architecture with:

- A basic API service
- A Blazor web application
- Shared configuration through Service Defaults
- Redis cache infrastructure
- Configured health checks
- Built-in service discovery

---

## 🏗️ Architectural Decisions

### Why .NET Aspire?

From day one, .NET Aspire provides:

- **Local orchestration** without Docker Compose or Kubernetes
- Automatic **service discovery** between services
- Built-in **observability** (logs, metrics, traces)
- A unified **dashboard** for monitoring
- **Centralized configuration** through Service Defaults

### Initial Structure

A simple, scalable structure was chosen:

```
src/
 ├── Aspire.uSLearn.AppHost/          → Aspire orchestrator
 ├── Aspire.uSLearn.ServiceDefaults/  → Shared configuration
 ├── Aspire.uSLearn.ApiService/       → Initial backend API
 └── Aspire.uSLearn.Web/              → Blazor frontend
```

### Key Components

#### 1. **AppHost (Orchestrator)**

The `AppHost` is the entry point that orchestrates all services:

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

**Configured features:**

- ✅ Redis as a distributed cache
- ✅ Health checks at `/health` for both services
- ✅ Explicit dependencies (`WaitFor`) for an ordered startup
- ✅ Service discovery through references
- ✅ External endpoints for the frontend

#### 2. **ServiceDefaults**

Shared project that encapsulates common configuration:

- Health checks
- Service discovery
- OpenTelemetry (tracing, metrics, logs)
- Resilience patterns

Every service references it with:

```csharp
builder.AddServiceDefaults();
```

#### 3. **ApiService**

Minimal API with:

- Sample `/weatherforecast` endpoint
- OpenAPI document published in development (`/openapi/v1.json`)
- Built-in health checks
- Service Defaults applied

```csharp
// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// OpenAPI configuration
builder.Services.AddOpenApi();
```

#### 4. **Web (Blazor)**

Blazor application with:

- Render mode: **Interactive Server**
- Output caching backed by Redis
- HttpClient configured with service discovery
- Sample pages: Home, Counter, Weather

```csharp
// Blazor configuration
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Output cache with Redis
builder.AddRedisOutputCache("cache");

// HttpClient with service discovery
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});
```

**Included Blazor components:**

- `App.razor` → Application root
- `Routes.razor` → Routing
- `MainLayout.razor` → Main layout
- `NavMenu.razor` → Navigation
- Pages: `Home.razor`, `Counter.razor`, `Weather.razor`

---

## 📦 Created Structure

```
Aspire.uSLearn/
│
├── src/
│   ├── Aspire.uSLearn.AppHost/
│   │   ├── AppHost.cs                          → Main orchestrator
│   │   └── Aspire.uSLearn.AppHost.csproj
│   │
│   ├── Aspire.uSLearn.ServiceDefaults/
│   │   └── Aspire.uSLearn.ServiceDefaults.csproj
│   │
│   ├── Aspire.uSLearn.ApiService/
│   │   ├── Program.cs                          → Minimal API
│   │   └── Aspire.uSLearn.ApiService.csproj
│   │
│   └── Aspire.uSLearn.Web/
│       ├── Program.cs                          → Blazor configuration
│       ├── WeatherApiClient.cs                 → HTTP client
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
│   └── Stage.01-Project-Creation.md            → This document
│
└── README.md                                   → General documentation
```

---

## 🔧 How It Was Created

### 1. Create the solution with Aspire

```bash
dotnet new aspire-starter -n Aspire.uSLearn
```

This command generates:
- AppHost
- ServiceDefaults
- ApiService (minimal API template)
- Web (Blazor with Interactive Server)

### 2. Configure Redis

Redis is added directly in `AppHost.cs`:

```csharp
var cache = builder.AddRedis("cache");
```

Aspire takes care of:
- Starting the Redis container
- Configuring the connection
- Injecting the configuration into the services that reference it

### 3. Configure Health Checks

```csharp
.WithHttpHealthCheck("/health")
```

Health checks let Aspire:
- Validate that services are up
- Show their status in the dashboard
- Manage startup dependencies

---

## ✅ Verification

Run the project:

```bash
dotnet run --project src/Aspire.uSLearn.AppHost
```

### Checks:

1. The **Aspire dashboard** opens in the browser
2. All services are listed:
   - ✅ `cache` (Redis)
   - ✅ `apiservice`
   - ✅ `webfrontend`
3. Health checks are green
4. The Blazor application is reachable and working
5. The API's `/weatherforecast` endpoint returns data

### Endpoints:

- **Aspire dashboard**: `http://localhost:15XXX` (dynamic port)
- **Web frontend**: `https://localhost:7XXX`
- **API service**: `https://localhost:7XXX` (discovered automatically)

---

## 🧪 Relevant Code

### Service discovery in action

In `WeatherApiClient.cs`, the HTTP client uses the logical service name:

```csharp
client.BaseAddress = new("https+http://apiservice");
```

Aspire resolves `apiservice` automatically, with no URLs to configure by hand.

### Output caching with Redis

In the Web project's `Program.cs`:

```csharp
builder.AddRedisOutputCache("cache");
```

The name `"cache"` matches the one defined in the AppHost, so the connection is set up automatically.

---

## 🔮 Future Considerations

This first stage lays the foundations. Later stages add:

- **Stage.02**: Domain events inside the services
- **Stage.03**: Service-to-service communication with integration events, plus idempotency
- **Stage.04**: Observability and validation
- **Stage.05**: Identity with Duende IdentityServer
- **Stage.06**: Blazor web app + Radzen
- **Stage.07**: Webhooks and extensibility

### Getting ready for microservices

Although there is only one `ApiService` for now, the structure makes it easy to add services. In later stages `ApiService` becomes `uSLearn.Accounts.API` and `uSLearn.Identity.API` is added. Each service is independent and communicates through:
- HTTP (synchronous)
- Integration events (asynchronous)

### Getting ready for building blocks

Reusable components are extracted into `Core.*` projects as they are needed (`Core.Domain`, `Core.EventBus`, `Core.EventBusRabbitMQ`, `Core.IntegrationEventLogEF`, `Core.Application`, `Core.Infrastructure`), following eShop's *building blocks* idea.

---

## 📚 Additional Resources

- [Official .NET Aspire documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Blazor documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Service discovery in Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)

---

## 🧩 Next Steps

➡️ **Stage.02**: Domain events

The next stage introduces:
- Domain events inside aggregates
- MediatR for event dispatching
- Event handlers
- The Repository pattern

---

## 📝 Summary

This first stage delivers:

✅ Base project with .NET Aspire  
✅ Blazor web app with Interactive Server  
✅ Minimal API service  
✅ Redis for output caching  
✅ Health checks  
✅ Working service discovery  
✅ Observability dashboard  

**Result**: a working distributed application, ready to evolve step by step into a full microservices architecture.
