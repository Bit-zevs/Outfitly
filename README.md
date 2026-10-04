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
- `Directory.Packages.props` contains shared NuGet package versions; Infrastructure imports it and declares its EF/Npgsql versions in a layer-local `Directory.Packages.props`.
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

The API currently uses an in-memory repository and does not provide
authentication or HTTP write/share endpoints. Infrastructure also contains an EF Core
PostgreSQL implementation and migrations; connecting it to HTTP use cases requires
a scoped service and one save per completed use case. See
[Infrastructure setup](src/backend/Outfitly.Infrastructure/README.md). Future endpoints
must obtain the actor identifier from authenticated identity. A persistent
repository must save affected objects and links in one transaction; the current
repository tracks changes by reference and is intended for this initial skeleton.

## Regression checks

Backend tests use xUnit and are discovered by `dotnet test`:

```shell
dotnet test Outfitly.sln
```

Tests are grouped under `tests/backend/Outfitly.Tests/Domain`, `Application`, and
`Infrastructure`. They cover domain boundaries, authorization, publication/sharing,
deletion, persistence and transaction rollback. Each test has independent data;
database assertions use a fresh context. SQLite checks run without an external server.
Actual PostgreSQL migration/constraint tests require `OUTFITLY_TEST_CONNECTION_STRING`
pointing to a dedicated test database. Without it, these tests are explicitly skipped.
Each PostgreSQL test creates and deletes its own randomly named schema; the test user
must have permission to create schemas. A configured but unreachable database fails
the tests rather than falling back to SQLite. See [test strategy](tests/backend/Outfitly.Tests/README.md).

Review regression tests also exercise an in-flight repository read overlapping
with a write on another thread. The overlap is deterministic, without sleeps or
stress-loop timing. This checks read safety, not full transaction isolation.

Run the development server's HTTP boundary tests with:

```shell
cd src/frontend
npm test
```

These tests start the actual server against temporary files, use port 5173
(stop any existing development server first), and clean up after completion.
They check query parameters and attempts to read files outside the frontend root.
Tests added for review findings assert the desired behavior and may fail until
the corresponding implementation defect is fixed. No production fixes are
included in this test-only change.

`OwnerId` disclosure is not tested as a forbidden behavior: the public DTO
contract currently includes it, and a privacy policy must first decide whether
that identifier should be omitted.
