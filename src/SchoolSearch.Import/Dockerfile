# syntax=docker/dockerfile:1
# Import image: one-shot console app, separate from the Api image. It runs on its own short-lived machine,
# never as a second Api process. Build context is the repository root (the same convention as the Api image).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Restore layer: central package management, SDK pin and project files only.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SchoolSearch.Contracts/SchoolSearch.Contracts.csproj src/SchoolSearch.Contracts/
COPY src/SchoolSearch.Catalog.Data/SchoolSearch.Catalog.Data.csproj src/SchoolSearch.Catalog.Data/
COPY src/SchoolSearch.Import/SchoolSearch.Import.csproj src/SchoolSearch.Import/
RUN dotnet restore src/SchoolSearch.Import/SchoolSearch.Import.csproj

# 2. Source and publish.
COPY src/SchoolSearch.Contracts/ src/SchoolSearch.Contracts/
COPY src/SchoolSearch.Catalog.Data/ src/SchoolSearch.Catalog.Data/
COPY src/SchoolSearch.Import/ src/SchoolSearch.Import/
RUN dotnet publish src/SchoolSearch.Import/SchoolSearch.Import.csproj -c Release -o /app/publish --no-restore

# Runtime image (no SDK). Roll-forward does not cross a major version.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/school-search

# Non-root user shipped with the runtime image.
USER app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SchoolSearch.Import.dll"]
