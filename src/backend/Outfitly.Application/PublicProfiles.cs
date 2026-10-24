using Outfitly.Domain;

namespace Outfitly.Application;

public sealed record PublicUser(Guid Id, string DisplayName, string Username);
public sealed record PublicPage<T>(IReadOnlyCollection<T> Items, int Total, int Page, int PageSize);

public interface IPublicProfileQuery
{
    Task<PublicPage<PublicUser>> SearchAsync(string prefix, int page, int pageSize, CancellationToken cancellationToken);
    Task<PublicUser?> FindAsync(string username, CancellationToken cancellationToken);
    Task<PublicPage<ItemView>> ItemsAsync(Guid ownerId, int page, int pageSize, CancellationToken cancellationToken);
    Task<PublicPage<OutfitView>> OutfitsAsync(Guid? ownerId, int page, int pageSize, CancellationToken cancellationToken);
    Task<ItemView?> ItemAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<OutfitView?> OutfitAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OutfitView>> AllOutfitsAsync(CancellationToken cancellationToken);
}

public sealed class PublicProfiles(IPublicProfileQuery query)
{
    public static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Invalid page or pageSize (1–100).");
    }

    public Task<PublicPage<PublicUser>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var prefix = (search ?? "").Trim();
        if (prefix.StartsWith('@')) prefix = prefix[1..];
        prefix = prefix.ToLowerInvariant();
        if (prefix.Length > 30 || prefix.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("Invalid username search.");
        return prefix.Length == 0 ? Task.FromResult(new PublicPage<PublicUser>([], 0, page, pageSize))
            : query.SearchAsync(prefix, page, pageSize, ct);
    }

    public async Task<PublicUser> FindAsync(string username, CancellationToken ct) =>
        await query.FindAsync(Usernames.Normalize(username), ct) ?? throw new KeyNotFoundException();

    public async Task<PublicPage<ItemView>> ItemsAsync(string username, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        return await query.ItemsAsync((await FindAsync(username, ct)).Id, page, pageSize, ct);
    }

    public async Task<PublicPage<OutfitView>> OutfitsAsync(string username, int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        return await query.OutfitsAsync((await FindAsync(username, ct)).Id, page, pageSize, ct);
    }

    public async Task<ItemView> ItemAsync(string username, Guid id, CancellationToken ct) =>
        await query.ItemAsync((await FindAsync(username, ct)).Id, id, ct) ?? throw new KeyNotFoundException();

    public async Task<OutfitView> OutfitAsync(string username, Guid id, CancellationToken ct) =>
        await query.OutfitAsync((await FindAsync(username, ct)).Id, id, ct) ?? throw new KeyNotFoundException();

    public Task<IReadOnlyCollection<OutfitView>> AllOutfitsAsync(CancellationToken ct) => query.AllOutfitsAsync(ct);
}
