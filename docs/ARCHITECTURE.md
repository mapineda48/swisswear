# Architecture

Technical overview of the SwissWear project.

## Rendering Model

The app uses **Blazor Server** with **InteractiveServer** rendering mode set globally in `Routes.razor`. This means all pages maintain a persistent SignalR connection to the server. There is no WebAssembly or static SSR — all interactivity happens server-side.

```
Browser <--SignalR--> ASP.NET Core Server <--EF Core--> PostgreSQL
                                          <--SDK------> Azure Blob Storage
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
│   │   └── NavMenu.razor       # Navigation links
│   └── Pages/
│       ├── Home.razor          # Landing page with project info
│       ├── People.razor        # People list with CRUD operations
│       └── PersonDialog.razor  # Create/Edit dialog for a person
├── Data/
│   ├── AppDbContext.cs         # EF Core DbContext
│   └── Person.cs               # Person entity
├── Services/
│   ├── IStorageService.cs      # Storage abstraction
│   ├── AzureBlobStorageService.cs  # Azure Blob Storage implementation
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
| `PersonService` | Scoped | CRUD operations for People |

## Database

PostgreSQL via Entity Framework Core 10 with Npgsql provider.

- **Connection string** is configured per environment in `appsettings.{Environment}.json`
- **Migrations** are managed with `dotnet ef` CLI
- The `Person` entity has a unique index on `Email` (filtered, allows nulls)

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

## Storage

The `IStorageService` interface abstracts file operations:

```csharp
Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct);
Task<Stream> DownloadAsync(string fileName, CancellationToken ct);
Task DeleteAsync(string fileName, CancellationToken ct);
Task<Uri> GetUriAsync(string fileName, CancellationToken ct);
```

A single implementation (`AzureBlobStorageService`) works for both environments:

- **Development**: Connects to Azurite via `UseDevelopmentStorage=true`. The container is created with `PublicAccessType.Blob` so uploaded images are directly accessible by URL.
- **Production**: Same SDK, different connection string pointing to a real Azure Storage account.

Photos are stored with the path pattern `people/{guid}.{extension}` and the full URL is persisted in the `Person.PhotoUrl` column.

## Localization

The app uses `IStringLocalizer<AppStrings>` with .resx resource files.

**How it works:**

1. `UseRequestLocalization` middleware reads the `Accept-Language` HTTP header
2. Sets `CultureInfo.CurrentUICulture` based on supported cultures (`en`, `es`)
3. `IStringLocalizer` resolves the correct .resx file for the current culture
4. Falls back to `AppStrings.resx` (English) if no match

The marker class `AppStrings` lives in namespace `SwissWear.Web`, which makes the resource manager look for embedded resources named `SwissWear.Web.AppStrings.resources` — matching the default convention for .resx files at `Resources/AppStrings.resx`.

All UI strings are referenced by key: `@L["People_Title"]` in Razor or `L["Key"].Value` when a `string` is needed.

## Docker

Multi-stage Dockerfile with three stages:

```
Stage 1 (restore)  → sdk:10.0    → Copy .csproj files, dotnet restore
Stage 2 (build)    → from restore → Copy source, dotnet publish -c Release
Stage 3 (runtime)  → aspnet:10.0  → Copy published output, ENTRYPOINT
```

The final image (~368MB) contains only the ASP.NET runtime and the compiled application. No SDK, compiler, or source code is included.

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

The `publish` and `docker` jobs run in parallel after tests pass.

## Development Workflow

```
develop (default branch)
  └── feature branches → PR to develop → CI runs
  └── git tag v1.0.0 → push tag → Release pipeline
```

Local development uses `docker compose` for PostgreSQL and Azurite, with `dotnet watch` for hot reload. VS Code is the primary editor with C# Dev Kit, and the solution file (`.slnx`) ensures compatibility with Visual Studio 2026.
