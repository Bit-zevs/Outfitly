using System.Text.Json.Serialization;
using Outfitly.Domain;

namespace Outfitly.Api.Contracts;

public sealed record ItemRequest(
    [property: JsonRequired] string Name,
    [property: JsonRequired] ClothingCategory Category,
    string? Color = null, string? Size = null, string? Brand = null,
    string? Description = null, string? PhotoUrl = null);

public sealed record OutfitRequest([property: JsonRequired] string Name, string? Description = null);
public sealed record PublicationRequest([property: JsonRequired] bool IsPublic);
public sealed record ShareLinkResponse(Guid Id, string Token, string Url);
