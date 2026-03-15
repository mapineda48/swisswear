# Architecture

Technical overview of the SwissWear project.

## Rendering Model

The app uses **Blazor Server** with **InteractiveServer** rendering mode set globally in `Routes.razor`. This means all pages maintain a persistent SignalR connection to the server. There is no WebAssembly or static SSR — all interactivity happens server-side.

```
Browser <--SignalR--> ASP.NET Core Server <--EF Core--> PostgreSQL
                                          <--SDK------> Azure Blob Storage
                                          <--Client----> Valkey (Cache + Pub/Sub)
```

MudBlazor requires interactive rendering for its components (dialogs, snackbars, date pickers, etc.), so the `@rendermode InteractiveServer` directive is applied at the router level rather than per-page.

## Project Layout

```
src/SwissWear.Web/
├── Program.cs                  # Composition root (DI, middleware pipeline)
├── Components/
│   ├── App.razor               # HTML document shell (head, scripts, styles)
│   ├── Routes.razor            # Router with global InteractiveServer mode
│   ├── _Imports.razor          # Global usings for all components
│   ├── Layout/
│   │   ├── MainLayout.razor    # MudBlazor layout (AppBar, Drawer, providers)
│   │   ├── NavMenu.razor       # Navigation links
│   │   ├── ChatWidget.razor    # Floating chat widget (real-time messaging)
│   │   └── Chat/               # Chat sub-components
│   │       ├── ChatInputBar.razor
│   │       ├── ChatMessageBubble.razor
│   │       ├── ChatAudioPlayer.razor
│   │       ├── ChatImageGrid.razor
│   │       └── ChatLightbox.razor
│   └── Pages/
│       ├── Home.razor          # Landing page with project info
│       ├── People.razor        # People list with CRUD operations
│       └── PersonDialog.razor  # Create/Edit dialog for a person
├── Data/
│   ├── AppDbContext.cs         # EF Core DbContext
│   ├── Person.cs               # Person entity
│   └── ChatMessageLog.cs       # Chat audit log entity
├── Services/
│   ├── IStorageService.cs      # File storage abstraction
│   ├── AzureBlobStorageService.cs  # Azure Blob Storage implementation
│   ├── ICacheService.cs        # Distributed cache abstraction (key-value, hash, list)
│   ├── ValkeyCacheService.cs   # Valkey/Redis cache implementation
│   ├── IPubSubService.cs       # Pub/Sub messaging abstraction
│   ├── ValkeyPubSubService.cs  # Valkey/Redis Pub/Sub implementation
│   ├── IChatService.cs         # Chat operations contract
│   ├── ChatService.cs          # Chat logic (uses ICacheService + IPubSubService)
│   ├── IChatAuditService.cs    # Chat audit logging contract
│   ├── ChatAuditService.cs     # Persists chat messages to database
│   └── PersonService.cs        # People business logic
├── Resources/
│   ├── AppStrings.cs           # Marker class for IStringLocalizer
│   ├── AppStrings.resx         # English strings (default/fallback)
│   └── AppStrings.es.resx      # Spanish strings
└── Migrations/                 # EF Core migrations (auto-generated)
```

## Dependency Injection

Services are registered in `Program.cs`:

| Service | Lifetime | Purpose |
|---------|----------|---------|
| `AddLocalization()` | - | Enables `IStringLocalizer<T>` |
| `AddMudServices()` | - | MudBlazor component services |
| `AddDbContext<AppDbContext>` | Scoped | EF Core with Npgsql (PostgreSQL) |
| `IStorageService` → `AzureBlobStorageService` | Singleton | Azure Blob Storage SDK client |
| `IConnectionMultiplexer` | Singleton | Valkey/Redis connection pool (StackExchange.Redis) |
| `ICacheService` → `ValkeyCacheService` | Singleton | Distributed cache (key-value, hash, list) |
| `IPubSubService` → `ValkeyPubSubService` | Singleton | Cross-pod event messaging |
| `PersonService` | Scoped | CRUD operations for People |
| `IChatAuditService` → `ChatAuditService` | Singleton | Persists chat messages to PostgreSQL |
| `IChatService` → `ChatService` | Singleton | Real-time chat with distributed state |

## Stateless Design

The application is designed to be fully stateless for horizontal scaling in Kubernetes (AKS). All shared state is externalized:

| State | Storage | Mechanism |
|-------|---------|-----------|
| People data | PostgreSQL | EF Core |
| Photos / Media | Azure Blob Storage | `IStorageService` |
| Chat messages | Valkey List | `ICacheService.ListPushAsync` / `ListRangeAsync` |
| Active users | Valkey Hash | `ICacheService.HashSetAsync` / `HashGetAllAsync` |
| Typing indicators | Valkey Keys with TTL | `ICacheService.SetAsync` (5s expiry) |
| Cross-pod events | Valkey Pub/Sub | `IPubSubService.PublishAsync` / `Subscribe` |
| Chat audit logs | PostgreSQL | `IChatAuditService` |

No in-memory state is shared between requests or pods.

## Database

PostgreSQL via Entity Framework Core 10 with Npgsql provider.

- **Connection string** is configured per environment in `appsettings.{Environment}.json`
- **Migrations** are managed with `dotnet ef` CLI

### People

```
People
├── Id (PK, auto-increment)
├── FirstName (required, max 100)
├── LastName (required, max 100)
├── Email (unique, nullable, max 200)
├── Phone (nullable, max 20)
├── BirthDate (nullable, DateOnly)
├── PhotoUrl (nullable, max 500)
└── CreatedAt (DateTime, UTC)
```

### ChatMessageLog

```
ChatMessageLogs
├── Id (PK, auto-increment)
├── UserName (required, max 100)
├── MessageType (enum: Text, Image, Audio)
├── MessageText (nullable)
├── MediaUrls (nullable, JSON array)
├── SenderIp (required, max 45)
├── UserAgent (nullable, max 500)
└── SentAt (DateTime, UTC)
```

## Storage

The `IStorageService` interface abstracts file operations:

```csharp
Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct);
Task<Stream> DownloadAsync(string fileName, CancellationToken ct);
Task DeleteAsync(string fileName, CancellationToken ct);
Task<Uri> GetUriAsync(string fileName, CancellationToken ct);
```

- **Development**: Connects to Azurite via `UseDevelopmentStorage=true`
- **Production**: Same SDK, different connection string pointing to a real Azure Storage account

## Cache and Pub/Sub

Two separate abstractions keep concerns clean:

**`ICacheService`** — Data storage operations:
- Key-value: `SetAsync<T>`, `GetAsync<T>`, `RemoveAsync`, `ExistsAsync`
- Hash: `HashSetAsync`, `HashGetAsync`, `HashGetAllAsync`, `HashRemoveAsync`
- List: `ListPushAsync`, `ListRangeAsync`, `ListTrimAsync`

**`IPubSubService`** — Event messaging:
- `PublishAsync(channel, message)`
- `Subscribe(channel, handler)`
- `UnsubscribeAll()`

Both are implemented by Valkey (Redis-compatible) via StackExchange.Redis. The separation allows replacing the messaging backend (e.g., RabbitMQ, Azure Service Bus) without affecting the cache layer.

## Chat System

The chat widget is a floating component (`ChatWidget.razor`) present on every page via `MainLayout.razor`.

**Message flow (multi-pod):**
1. User sends message on Pod A → `ChatService.SendMessageAsync()`
2. Message is stored in Valkey List (`chat:messages`) via `ICacheService`
3. Message is published to `chat:events:message` channel via `IPubSubService`
4. All pods (including A) receive the Pub/Sub event → fire local C# `OnMessageReceived` event
5. Blazor components subscribed to the event update their UI via `InvokeAsync(StateHasChanged)`
6. Audit service persists the message to PostgreSQL asynchronously

**Supported message types:** Text, Images (with gallery and lightbox), Audio (with player)

## Localization

The app uses `IStringLocalizer<AppStrings>` with .resx resource files.

1. `UseRequestLocalization` middleware reads the `Accept-Language` HTTP header
2. Sets `CultureInfo.CurrentUICulture` based on supported cultures (`en`, `es`)
3. `IStringLocalizer` resolves the correct .resx file for the current culture
4. Falls back to `AppStrings.resx` (English) if no match

## Docker

Multi-stage Dockerfile with three stages:

```
Stage 1 (restore)  → sdk:10.0    → Copy .csproj files, dotnet restore
Stage 2 (build)    → from restore → Copy source, dotnet publish -c Release
Stage 3 (runtime)  → aspnet:10.0  → Copy published output, ENTRYPOINT
```

### docker-compose.yml (local development)

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| `postgres` | `postgres:17-alpine` | 5432 | Relational database |
| `azurite` | `mcr.microsoft.com/azure-storage/azurite` | 10000-10002 | Blob/Queue/Table storage emulator |
| `valkey` | `valkey/valkey:8-alpine` | 6379 | Distributed cache and Pub/Sub |

## CI/CD

Two GitHub Actions workflows:

### CI (`ci.yml`)
- **Trigger**: Push or PR to `develop`
- **Steps**: Restore → Build → Test (with PostgreSQL service container)

### Release (`release.yml`)
- **Trigger**: Push tag `v*`
- **Jobs**:
  1. `build-and-test` — Same validation as CI
  2. `publish` — Creates GitHub Release with compiled binaries and auto-generated release notes
  3. `docker` — Builds multi-stage image, pushes to Docker Hub with version tag and `latest`

## Development Workflow

```
develop (default branch)
  └── feature branches → PR to develop → CI runs
  └── git tag v1.0.0 → push tag → Release pipeline
```

Local development uses `docker compose` for PostgreSQL, Azurite, and Valkey, with `dotnet watch` for hot reload.
