using Outfitly.Application;

namespace Outfitly.Api.Contracts;

public static class WardrobeResponses
{
    public static object Item(ItemView item, string photoRoute) => new
    {
        item.Id, item.OwnerId, item.OwnerUsername, item.Name, item.Category, item.Color, item.Size, item.Brand,
        item.Description, PhotoUrl = item.HasUploadedPhoto ? photoRoute : item.PhotoUrl, item.IsPublic
    };

    public static object Outfit(OutfitView outfit, Func<Guid, string> photoRoute) => new
    {
        outfit.Id, outfit.OwnerId, outfit.OwnerUsername, outfit.Name, outfit.Description, outfit.IsPublic,
        Items = outfit.Items.Select(item => Item(item, photoRoute(item.Id))).ToArray()
    };

    public static object Page(ItemPage page, bool personal) => new
    {
        Items = page.Items.Select(item => Item(item, personal ? $"/api/items/{item.Id}/photo" : $"/api/wardrobe/items/{item.Id}/photo")).ToArray(),
        page.Total, page.Page, page.PageSize
    };

    public static string? ExternalPhotoInput(string? url, Guid id, HttpRequest request, ItemView? current = null)
    {
        var candidate = url?.Trim();
        if (candidate == $"/api/items/{id}/photo" && current?.HasUploadedPhoto == true) return null;
        var origin = new Uri($"{request.Scheme}://{request.Host}");
        var isInternal = Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(origin.Scheme, StringComparison.OrdinalIgnoreCase) &&
            uri.IdnHost.Equals(origin.IdnHost, StringComparison.OrdinalIgnoreCase) && uri.Port == origin.Port &&
            uri.AbsolutePath.StartsWith($"{request.PathBase}/api/", StringComparison.OrdinalIgnoreCase);
        if (candidate is not null && (candidate.StartsWith('/') || isInternal))
            throw new ArgumentException("Use the upload endpoint to assign an internal photo.");
        return url;
    }
}
