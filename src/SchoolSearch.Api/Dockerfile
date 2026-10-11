# syntax=docker/dockerfile:1
# Build context is the repository root (fly.toml: [build] dockerfile = "src/SchoolSearch.Api/Dockerfile", context = ".").

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Restore layer: central package management, SDK pin and project files only.
#    A source-only change does not invalidate this layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SchoolSearch.ServiceDefaults/SchoolSearch.ServiceDefaults.csproj src/SchoolSearch.ServiceDefaults/
COPY src/SchoolSearch.Catalog.Data/SchoolSearch.Catalog.Data.csproj src/SchoolSearch.Catalog.Data/
COPY src/SchoolSearch.Api/SchoolSearch.Api.csproj src/SchoolSearch.Api/
RUN dotnet restore src/SchoolSearch.Api/SchoolSearch.Api.csproj

# 2. Source and publish.
COPY src/SchoolSearch.ServiceDefaults/ src/SchoolSearch.ServiceDefaults/
COPY src/SchoolSearch.Catalog.Data/ src/SchoolSearch.Catalog.Data/
COPY src/SchoolSearch.Api/ src/SchoolSearch.Api/
RUN dotnet publish src/SchoolSearch.Api/SchoolSearch.Api.csproj -c Release -o /app/publish --no-restore

# Runtime major version equals the TFM major (net10.0). Roll-forward does not cross a major.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/school-search

# Non-root user shipped with the aspnet image.
USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SchoolSearch.Api.dll"]
