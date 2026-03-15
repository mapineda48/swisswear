# SwissWear

Testing lab for modern web development with .NET 10 and Blazor Server.

This is a personal project to experiment with architecture patterns, UI components, cloud services, and DevOps practices in the .NET ecosystem.

## Tech Stack

- **.NET 10 (LTS)** — Blazor Server with InteractiveServer rendering
- **MudBlazor 9** — Material Design component library
- **PostgreSQL 17** — Relational database via Entity Framework Core 10
- **Azure Blob Storage** — File storage (Azurite emulator for local development)
- **Valkey 8** — Distributed cache and Pub/Sub (Redis-compatible alternative)
- **Docker Compose** — Local development infrastructure

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose
- [VS Code](https://code.visualstudio.com/) (recommended) or Visual Studio 2026

## Getting Started

```bash
# Start infrastructure (PostgreSQL + Azurite + Valkey)
docker compose up -d

# Apply database migrations
dotnet ef database update --project src/SwissWear.Web

# Run the app with hot reload
dotnet watch --project src/SwissWear.Web
```

The app will be available at `http://localhost:5201`.

## Project Structure

```
.
├── src/SwissWear.Web/          # Blazor Server application
│   ├── Components/             # Razor components (pages, layout, chat)
│   ├── Data/                   # EF Core DbContext and entities
│   ├── Services/               # Business logic, storage, cache, pub/sub
│   └── Resources/              # Localization (.resx files)
├── tests/SwissWear.Tests/      # Unit tests (xUnit + bUnit + Moq)
├── docker-compose.yml          # PostgreSQL + Azurite + Valkey for local dev
├── Dockerfile                  # Multi-stage build for production
└── .github/workflows/          # CI/CD pipelines
```

## Available Modules

| Module | Description |
|--------|-------------|
| **People** | Full CRUD with photo upload to Azure Blob Storage |
| **Chat** | Real-time messaging with text, images, and audio. Distributed via Valkey Pub/Sub for multi-pod deployments |

## Architecture Highlights

- **Stateless design** — All shared state is externalized to PostgreSQL, Azure Blob Storage, and Valkey, making the app ready for horizontal scaling in Kubernetes (AKS)
- **Service abstractions** — `ICacheService`, `IPubSubService`, and `IStorageService` decouple business logic from infrastructure
- **Chat system** — Uses Valkey for distributed message storage (List), user presence (Hash), typing indicators (Key with TTL), and cross-pod event propagation (Pub/Sub)

## Localization

The app supports English and Spanish. The language is automatically detected from the browser's `Accept-Language` header. English is the default fallback.

Resource files are located in `src/SwissWear.Web/Resources/`:
- `AppStrings.resx` — English (default)
- `AppStrings.es.resx` — Spanish

## Docker

```bash
# Build the image
docker build -t swisswear .

# Run (connect to existing docker-compose services)
docker run -d -p 8080:8080 \
  --network swisswear_default \
  -e "ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=swisswear_dev;Username=postgres;Password=postgres" \
  -e "AzureStorage__ConnectionString=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1" \
  -e "Valkey__ConnectionString=valkey:6379" \
  swisswear
```

## CI/CD

- **CI** (`ci.yml`) — Runs on every push/PR to `develop`. Builds, tests with PostgreSQL service container.
- **Release** (`release.yml`) — Triggered by `v*` tags. Runs tests, creates a GitHub Release with binaries, and publishes the Docker image to Docker Hub.

```bash
# Create a release
git tag v1.0.0
git push origin v1.0.0
```

Requires Docker Hub secrets: `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`.

## License

This is a personal lab project. No license specified.
