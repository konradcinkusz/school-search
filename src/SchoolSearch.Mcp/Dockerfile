# syntax=docker/dockerfile:1
# Build context is the repository root (flyio/mcp.fly.toml: [build] dockerfile = "../src/SchoolSearch.Mcp/Dockerfile", context = "..").

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Restore layer: central package management, SDK pin and project files only.
#    A source-only change does not invalidate this layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SchoolSearch.ServiceDefaults/SchoolSearch.ServiceDefaults.csproj src/SchoolSearch.ServiceDefaults/
COPY src/SchoolSearch.Contracts/SchoolSearch.Contracts.csproj src/SchoolSearch.Contracts/
COPY src/SchoolSearch.Mcp/SchoolSearch.Mcp.csproj src/SchoolSearch.Mcp/
RUN dotnet restore src/SchoolSearch.Mcp/SchoolSearch.Mcp.csproj

# 2. Source and publish.
COPY src/SchoolSearch.ServiceDefaults/ src/SchoolSearch.ServiceDefaults/
COPY src/SchoolSearch.Contracts/ src/SchoolSearch.Contracts/
COPY src/SchoolSearch.Mcp/ src/SchoolSearch.Mcp/
RUN dotnet publish src/SchoolSearch.Mcp/SchoolSearch.Mcp.csproj -c Release -o /app/publish --no-restore

# Runtime major version equals the TFM major (net10.0). Roll-forward does not cross a major.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/school-search

# Non-root user shipped with the aspnet image.
USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SchoolSearch.Mcp.dll"]
