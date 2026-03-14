# Stage 1: Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY global.json .
COPY SwissWear.slnx .
COPY src/SwissWear.Web/SwissWear.Web.csproj src/SwissWear.Web/
COPY tests/SwissWear.Tests/SwissWear.Tests.csproj tests/SwissWear.Tests/
RUN dotnet restore SwissWear.slnx

# Stage 2: Build & Publish
FROM restore AS build
COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/SwissWear.Web -c Release -o /app --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SwissWear.Web.dll"]
