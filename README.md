# SwissWear

Testing lab for modern web development with .NET 10 and Blazor Server.

Personal project to experiment with architecture patterns, UI components, cloud services, and DevOps practices in the .NET ecosystem.

## Tech Stack

- **.NET 10 (LTS)** — Blazor Server with InteractiveServer rendering
- **MudBlazor 9** — Material Design component library
- **PostgreSQL 17** — Relational database via Entity Framework Core 10
- **Azure Blob Storage** — File storage (Azurite emulator for local development)
- **Valkey 8** — Distributed cache and Pub/Sub (Redis-compatible)
- **Docker Compose** — Local development infrastructure

## Getting Started

```bash
# Start infrastructure (PostgreSQL + Azurite + Valkey)
docker compose up -d

# Apply database migrations
dotnet ef database update --project src/SwissWear.Infrastructure --startup-project src/SwissWear.Web

# Run the app with hot reload
dotnet watch --project src/SwissWear.Web
```

The app will be available at `http://localhost:5201`.

## Project Structure

The solution follows Clean Architecture with three projects:

```
src/
├── SwissWear.Domain/           # Entities, interfaces, shared models (no dependencies)
├── SwissWear.Infrastructure/   # EF Core, Azure SDK, Redis, service implementations
└── SwissWear.Web/              # Blazor components, Program.cs, resources
tests/
└── SwissWear.Tests/            # Unit tests (xUnit + Moq)
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed technical documentation.

## Available Modules

| Module | Description |
|--------|-------------|
| **People** | Full CRUD with photo upload to Azure Blob Storage |
| **Chat** | Real-time messaging (text, images, audio) distributed via Valkey Pub/Sub |

## Localization

English and Spanish, auto-detected from the browser's `Accept-Language` header.

## Docker

```bash
docker build -t swisswear .

docker run -d -p 8080:8080 \
  --network swisswear_default \
  -e "ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=swisswear_dev;Username=postgres;Password=postgres" \
  -e "AzureStorage__ConnectionString=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1" \
  -e "Valkey__ConnectionString=valkey:6379" \
  swisswear
```

## CI/CD

- **CI** (`ci.yml`) — Push/PR to `develop` → build + test
- **Release** (`release.yml`) — Push `v*` tag → test, GitHub Release, Docker Hub

## License

Personal lab project. No license specified.
