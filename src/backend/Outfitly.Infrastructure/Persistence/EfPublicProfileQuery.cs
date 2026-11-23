using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Infrastructure.Persistence;

public sealed class EfPublicProfileQuery(OutfitlyDbContext context) : IPublicProfileQuery, IOutfitCatalogQuery
{
    private IQueryable<User> Users => context.Users.AsNoTracking().Where(user =>
        context.Accounts.Any(account => account.UserId == user.Id && account.EmailConfirmed));

    public async Task<PublicPage<PublicUser>> SearchAsync(string prefix, int page, int pageSize, CancellationToken ct)
    {
        var users = Users.Where(user => user.Username.StartsWith(prefix));
        var total = await users.CountAsync(ct);
        var result = await users.OrderByDescending(user => user.Username == prefix).ThenBy(user => user.Username)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(user => new PublicUser(user.Id, user.DisplayName, user.Username, user.AvatarStorageKey == null ? null : "/api/users/" + user.Id + "/avatar")).ToArrayAsync(ct);
        return new(result, total, page, pageSize);
    }

    public Task<PublicUser?> FindAsync(string username, CancellationToken ct) => Users.Where(user => user.Username == username)
        .Select(user => new PublicUser(user.Id, user.DisplayName, user.Username, user.AvatarStorageKey == null ? null : "/api/users/" + user.Id + "/avatar")).SingleOrDefaultAsync(ct);

    private IQueryable<WardrobeItem> Items(Guid ownerId) => context.WardrobeItems.AsNoTracking()
        .Where(item => item.OwnerId == ownerId && item.IsPublic && Users.Any(user => user.Id == item.OwnerId));

    private IQueryable<ItemView> ItemViews(IQueryable<WardrobeItem> items) => items.Select(item =>
        new ItemView(item.Id, item.OwnerId, item.Name, item.Category, item.Color, item.Size, item.Brand,
            item.Description, item.PhotoUrl, item.IsPublic, item.PhotoStorageKey != null,
            context.Users.Where(user => user.Id == item.OwnerId).Select(user => user.Username).Single()));

    public async Task<PublicPage<ItemView>> ItemsAsync(Guid ownerId, int page, int pageSize, CancellationToken ct)
    {
        var items = Items(ownerId);
        var total = await items.CountAsync(ct);
        var result = await ItemViews(items.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToArrayAsync(ct);
        return new(result, total, page, pageSize);
    }

    public Task<ItemView?> ItemAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        ItemViews(Items(ownerId).Where(item => item.Id == id)).SingleOrDefaultAsync(ct);

    private IQueryable<Outfit> Outfits(Guid? ownerId) => context.Outfits.AsNoTracking()
        .Where(outfit => outfit.IsPublic && (ownerId == null || outfit.OwnerId == ownerId));

    public async Task<PublicPage<OutfitView>> OutfitsAsync(Guid? ownerId, int page, int pageSize, CancellationToken ct)
    {
        var outfits = Outfits(ownerId);
        var total = await outfits.CountAsync(ct);
        var result = await outfits.OrderBy(outfit => outfit.Name).ThenBy(outfit => outfit.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(await ViewsAsync(result, ct), total, page, pageSize);
    }

    public async Task<OutfitView?> OutfitAsync(Guid? ownerId, Guid id, CancellationToken ct)
    {
        var outfit = await Outfits(ownerId).SingleOrDefaultAsync(outfit => outfit.Id == id, ct);
        return outfit is null ? null : (await ViewsAsync([outfit], ct)).Single();
    }

    public Task<PublicPage<OutfitView>> AllOutfitsAsync(ItemCatalogQuery filter, CancellationToken ct) => CatalogAsync(null, filter, ct);

    public Task<PublicPage<OutfitView>> MineAsync(Guid owner, ItemCatalogQuery filter, CancellationToken ct) => CatalogAsync(owner, filter, ct);

    private async Task<PublicPage<OutfitView>> CatalogAsync(Guid? owner, ItemCatalogQuery filter, CancellationToken ct)
    {
        filter = filter.Normalize();
        var outfits = owner is { } ownerId ? context.Outfits.AsNoTracking().Where(outfit => outfit.OwnerId == ownerId) : Outfits(null);
        if (filter.Search is { } search)
        {
            if (context.Database.IsNpgsql())
            {
                var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                outfits = outfits.Where(outfit => EF.Functions.ILike(outfit.Name, pattern, "\\"));
            }
            else { var term = search.ToLowerInvariant(); outfits = outfits.Where(outfit => outfit.Name.ToLower().Contains(term)); }
        }
        var total = await outfits.CountAsync(ct);
        var ordered = filter.Sort switch
        {
            "nameDesc" => outfits.OrderByDescending(outfit => outfit.Name),
            "newest" => outfits.OrderByDescending(outfit => outfit.CreatedAtUtc.HasValue).ThenByDescending(outfit => outfit.CreatedAtUtc),
            "likes" => outfits.OrderByDescending(outfit => context.Set<PostInteraction>().Count(reaction => reaction.OutfitId == outfit.Id && reaction.Kind == "like")),
            "views" => outfits.OrderByDescending(outfit => context.Set<PostInteraction>().Count(reaction => reaction.OutfitId == outfit.Id && reaction.Kind == "view")),
            _ => outfits.OrderBy(outfit => outfit.Name)
        };
        var selected = await ordered.ThenBy(outfit => outfit.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToArrayAsync(ct);
        return new(await ViewsAsync(selected, ct), total, filter.Page, filter.PageSize);
    }

    private async Task<IReadOnlyCollection<OutfitView>> ViewsAsync(Outfit[] outfits, CancellationToken ct)
    {
        var ownerIds = outfits.Select(outfit => outfit.OwnerId).Distinct().ToArray();
        var owners = await context.Users.AsNoTracking().Where(user => ownerIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Username, ct);
        var ids = outfits.SelectMany(outfit => outfit.ItemIds).Distinct().ToArray();
        var items = await context.WardrobeItems.AsNoTracking().Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
        return outfits.Select(outfit => new OutfitView(outfit.Id, outfit.OwnerId, outfit.Name, outfit.Description,
            outfit.IsPublic, outfit.ItemIds.Select(id => items[id]).Select(item => new ItemView(item.Id, item.OwnerId,
                item.Name, item.Category, item.Color, item.Size, item.Brand, item.Description, item.PhotoUrl,
                item.IsPublic, item.PhotoStorageKey != null, owners[item.OwnerId])).ToArray(), owners[outfit.OwnerId])).ToArray();
    }
}
