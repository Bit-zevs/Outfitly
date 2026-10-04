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

## Domain model

- `User` owns wardrobe items and outfits; authentication is outside the domain.
- `WardrobeItem` stores its owner, name, category and optional characteristics.
- `Outfit` stores references to its owner's items without duplicates.
- `ShareLink` grants read access to one item or outfit using a random 256-bit token.

Items and outfits start private. Publication and share links are independent.
Empty outfits can be saved but cannot be published or shared.
Public/shared outfits include their private items in context without publishing
those items separately.

`WardrobeService` in Application checks ownership and referenced objects, handles
publication and links, and coordinates deletion. Deleting an item removes it from
all outfits; empty outfits become private and their links are deactivated.
Deleting an item or outfit deactivates all links targeting that object.
Removing the last item from an outfit has the same effect on outfit visibility.
Disabled links stay disabled even if the outfit is filled again.

Required names are trimmed and cannot be blank. Identifiers cannot be empty.
Optional blank characteristics become null; photo URLs, when provided, must use
absolute HTTP or HTTPS addresses. Updating characteristics replaces all optional
values, so omitted values clear the previous values.

Application throws `UnauthorizedAccessException` for a foreign owner,
`KeyNotFoundException` for a missing object, and `InvalidOperationException`
for forbidden domain transitions. Public/shared reads return snapshots without
share tokens; invalid, disabled or deleted share targets return null.
The existing `GET /api/wardrobe` returns only public items.

The implementation currently uses an in-memory repository and does not provide
authentication, durable storage, or HTTP write/share endpoints. Future endpoints
must obtain the actor identifier from authenticated identity. A persistent
repository must save affected objects and links in one transaction; the current
repository tracks changes by reference and is intended for this initial skeleton.

## Regression checks

The dependency-free executable test project covers domain and Application rules
with the in-memory repository. Run it explicitly (it is not a test-framework
project discovered by `dotnet test`):

```shell
dotnet run --project tests/backend/Outfitly.Tests/Outfitly.Tests.csproj
```

It reports each scenario and exits with a nonzero status if a check fails.
