using Outfitly.Domain;

namespace Outfitly.Application;

// actorId must come from the authenticated user, never from a client-supplied owner field.
// The caller commits each completed command once through IUnitOfWork.
public sealed class WardrobeService(IWardrobeRepository repository)
{
    public WardrobeItem CreateItem(Guid actorId, string name, ClothingCategory category,
        string? color = null, string? size = null, string? brand = null,
        string? description = null, string? photoUrl = null)
    {
        var item = new WardrobeItem(Guid.NewGuid(), actorId, name, category,
            color, size, brand, description, photoUrl);
        repository.Add(item);
        return item;
    }

    public Outfit CreateOutfit(Guid actorId, string name, string? description = null)
    {
        var outfit = new Outfit(Guid.NewGuid(), actorId, name, description);
        repository.Add(outfit);
        return outfit;
    }

    public void UpdateItem(Guid actorId, Guid itemId, string name, ClothingCategory category,
        string? color = null, string? size = null, string? brand = null,
        string? description = null, string? photoUrl = null)
    {
        var item = Item(itemId);
        RequireOwner(actorId, item.OwnerId);
        item.Update(name, category, color, size, brand, description, photoUrl);
    }

    public void UpdateOutfit(Guid actorId, Guid outfitId, string name, string? description = null)
    {
        var outfit = Outfit(outfitId);
        RequireOwner(actorId, outfit.OwnerId);
        outfit.Update(name, description);
    }

    public void AddItemToOutfit(Guid actorId, Guid outfitId, Guid itemId)
    {
        var outfit = Outfit(outfitId);
        RequireOwner(actorId, outfit.OwnerId);
        var item = Item(itemId);
        RequireOwner(actorId, item.OwnerId);
        outfit.AddItem(item);
    }

    public void RemoveItemFromOutfit(Guid actorId, Guid outfitId, Guid itemId)
    {
        var outfit = Outfit(outfitId);
        RequireOwner(actorId, outfit.OwnerId);
        outfit.RemoveItem(itemId);
        if (outfit.ItemIds.Count == 0)
            DeactivateLinks(ShareTargetType.Outfit, outfit.Id);
    }

    public void SetPublication(Guid actorId, ShareTargetType targetType, Guid targetId, bool isPublic)
    {
        RequireTargetOwner(actorId, targetType, targetId);
        if (targetType == ShareTargetType.WardrobeItem)
        {
            var item = Item(targetId);
            if (isPublic) item.Publish(); else item.Unpublish();
        }
        else
        {
            var outfit = Outfit(targetId);
            if (isPublic) outfit.Publish(); else outfit.Unpublish();
        }
    }

    public ShareLink CreateShareLink(Guid actorId, ShareTargetType targetType, Guid targetId)
    {
        RequireTargetOwner(actorId, targetType, targetId);
        if (targetType == ShareTargetType.Outfit)
            Outfit(targetId).EnsureCanShare();
        var link = new ShareLink(Guid.NewGuid(), targetType, targetId);
        repository.Add(link);
        return link;
    }

    public void DeactivateShareLink(Guid actorId, Guid linkId)
    {
        var link = repository.GetLinks().SingleOrDefault(link => link.Id == linkId)
            ?? throw new KeyNotFoundException("Share link not found.");
        RequireTargetOwner(actorId, link.TargetType, link.TargetId);
        link.Deactivate();
    }

    public void DeleteItem(Guid actorId, Guid itemId)
    {
        var item = Item(itemId);
        RequireOwner(actorId, item.OwnerId);
        foreach (var outfit in repository.GetOutfits().Where(outfit => outfit.ItemIds.Contains(itemId)))
        {
            outfit.RemoveItem(itemId);
            if (outfit.ItemIds.Count == 0)
                DeactivateLinks(ShareTargetType.Outfit, outfit.Id);
        }
        DeactivateLinks(ShareTargetType.WardrobeItem, itemId);
        repository.Remove(item);
    }

    public void DeleteOutfit(Guid actorId, Guid outfitId)
    {
        var outfit = Outfit(outfitId);
        RequireOwner(actorId, outfit.OwnerId);
        DeactivateLinks(ShareTargetType.Outfit, outfitId);
        repository.Remove(outfit);
    }

    // Views intentionally contain neither share tokens nor mutable domain entities.
    public IReadOnlyCollection<ItemView> GetMyItems(Guid actorId)
    {
        RequireActor(actorId);
        return repository.GetAll().Where(item => item.OwnerId == actorId).Select(View).ToArray();
    }

    public IReadOnlyCollection<OutfitView> GetMyOutfits(Guid actorId)
    {
        RequireActor(actorId);
        return repository.GetOutfits().Where(outfit => outfit.OwnerId == actorId).Select(View).ToArray();
    }

    public ItemView GetItem(Guid actorId, Guid itemId)
    {
        var item = Item(itemId);
        RequireOwner(actorId, item.OwnerId);
        return View(item);
    }

    public OutfitView GetOutfit(Guid actorId, Guid outfitId)
    {
        var outfit = Outfit(outfitId);
        RequireOwner(actorId, outfit.OwnerId);
        return View(outfit);
    }

    public IReadOnlyCollection<ItemView> GetPublicItems() =>
        repository.GetAll().Where(item => item.IsPublic).Select(View).ToArray();

    public IReadOnlyCollection<OutfitView> GetPublicOutfits() =>
        repository.GetOutfits().Where(outfit => outfit.IsPublic).Select(View).ToArray();

    public ItemView? GetSharedItem(string token)
    {
        var link = ActiveLink(token, ShareTargetType.WardrobeItem);
        var item = link is null ? null : repository.GetAll().SingleOrDefault(item => item.Id == link.TargetId);
        return item is null ? null : View(item);
    }

    public OutfitView? GetSharedOutfit(string token)
    {
        var link = ActiveLink(token, ShareTargetType.Outfit);
        var outfit = link is null ? null : repository.GetOutfits().SingleOrDefault(outfit => outfit.Id == link.TargetId);
        return outfit is null || outfit.ItemIds.Count == 0 ? null : View(outfit);
    }

    private ShareLink? ActiveLink(string token, ShareTargetType targetType) =>
        repository.GetLinks().SingleOrDefault(link =>
            link.IsActive && link.TargetType == targetType && link.Token == token);

    private WardrobeItem Item(Guid id) =>
        repository.GetAll().SingleOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException("Wardrobe item not found.");

    private Outfit Outfit(Guid id) =>
        repository.GetOutfits().SingleOrDefault(outfit => outfit.Id == id)
        ?? throw new KeyNotFoundException("Outfit not found.");

    private void RequireTargetOwner(Guid actorId, ShareTargetType targetType, Guid targetId)
    {
        var ownerId = targetType switch
        {
            ShareTargetType.WardrobeItem => Item(targetId).OwnerId,
            ShareTargetType.Outfit => Outfit(targetId).OwnerId,
            _ => throw new ArgumentOutOfRangeException(nameof(targetType))
        };
        RequireOwner(actorId, ownerId);
    }

    private static void RequireOwner(Guid actorId, Guid ownerId)
    {
        if (actorId == Guid.Empty || actorId != ownerId)
            throw new UnauthorizedAccessException("Only the owner can perform this operation.");
    }

    private static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw new UnauthorizedAccessException("An authenticated user is required.");
    }

    private void DeactivateLinks(ShareTargetType type, Guid id)
    {
        foreach (var link in repository.GetLinks().Where(link => link.TargetType == type && link.TargetId == id))
            link.Deactivate();
    }

    private static ItemView View(WardrobeItem item) =>
        new(item.Id, item.OwnerId, item.Name, item.Category, item.Color, item.Size,
            item.Brand, item.Description, item.PhotoUrl, item.IsPublic);

    private OutfitView View(Outfit outfit) =>
        new(outfit.Id, outfit.OwnerId, outfit.Name, outfit.Description, outfit.IsPublic,
            outfit.ItemIds.Select(id => View(Item(id))).ToArray());
}
