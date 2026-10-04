using Outfitly.Domain;

namespace Outfitly.Application;

public sealed record ItemView(Guid Id, Guid OwnerId, string Name, ClothingCategory Category,
    string? Color, string? Size, string? Brand, string? Description, string? PhotoUrl, bool IsPublic);

public sealed record OutfitView(Guid Id, Guid OwnerId, string Name, string? Description,
    bool IsPublic, IReadOnlyCollection<ItemView> Items);
