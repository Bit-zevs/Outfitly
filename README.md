# Outfitly

Outfitly is a coursework application for managing a personal wardrobe. Users will be able to add clothes by category, record their characteristics, combine clothes into outfits, and share clothes and outfits.

## Structure

```text
src/
  backend/
    Outfitly.Domain/          # Entities, value objects, domain rules
    Outfitly.Application/     # Use cases, ports, DTOs
    Outfitly.Infrastructure/  # Persistence and external integrations
    Outfitly.Api/             # HTTP entry point and composition root
  frontend/                   # Framework-free client application
```

The dependency direction for the backend is inward:

```text
Api -> Application <- Infrastructure
              |
              v
            Domain
```

`Domain` must not depend on other application layers. `Application` may depend only on `Domain`. `Infrastructure` implements interfaces declared by `Application`. `Api` wires the application together and owns transport-specific concerns.

## Shared configuration

- `Directory.Build.props` contains common .NET compiler settings.
- `Directory.Packages.props` is the single source of NuGet package versions.
- Each future `.csproj` selects only the packages needed by that layer using `PackageReference` without `Version`.
- `global.json` pins the .NET SDK used by the solution.

## Build

Build the complete .NET backend:

```shell
dotnet build Outfitly.sln
```

Each backend layer can also be built independently by passing its `.csproj` to `dotnet build`.

Build the frontend (no third-party packages are required):

```shell
cd src/frontend
npm run build
```

The frontend build is written to `src/frontend/dist`.
