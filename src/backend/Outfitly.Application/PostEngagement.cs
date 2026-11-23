using Outfitly.Domain;
namespace Outfitly.Application;

public interface IPostInteractionStore
{
    IQueryable<PostInteraction> Interactions { get; }
    IQueryable<PostInteraction> ReadInteractions { get; }
    User FindUser(Guid id);
    void Add(PostInteraction interaction);
    void Remove(PostInteraction interaction);
}
public sealed record EngagementView(int Views, int Likes, int Comments, bool IsLiked, bool CanInteract);
public sealed record CommentView(Guid Id, PublicUser Author, string? Text, Guid? ParentId,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, bool IsDeleted, bool CanEdit, bool CanDelete);

public sealed class PostEngagement(IWardrobeRepository wardrobe, IPostInteractionStore store, TimeProvider time)
{
    private Guid Access(Guid id, bool outfit, Guid? actor, string? token, bool changing, Guid? publicOutfitId)
    {
        Guid owner; bool published;
        var publicOnly = actor is null && token is null && (outfit || publicOutfitId is null);
        Outfit? trackedOutfit = null;
        if (outfit)
        {
            if (changing) trackedOutfit = wardrobe.FindOutfitForChange(id) ?? throw new KeyNotFoundException();
            var target = trackedOutfit ?? wardrobe.ReadOutfit(id, publicOnly) ?? throw new KeyNotFoundException();
            owner = target.OwnerId; published = target.IsPublic;
        }
        else
        {
            var target = wardrobe.ReadItem(id, publicOnly) ?? throw new KeyNotFoundException();
            owner = target.OwnerId; published = target.IsPublic;
        }
        var type = outfit ? ShareTargetType.Outfit : ShareTargetType.WardrobeItem;
        if (!published && owner != actor && (token is null || wardrobe.FindActiveLink(token, type)?.TargetId != id))
        {
            if (outfit) throw new KeyNotFoundException();
            PublicOutfitItem.Require(wardrobe, publicOutfitId, id, changing);
        }
        if (trackedOutfit is not null) wardrobe.MarkOutfitChanged(trackedOutfit);
        return owner;
    }
    private IQueryable<PostInteraction> Rows(Guid id, bool outfit, bool readOnly = false) =>
        (readOnly ? store.ReadInteractions : store.Interactions).Where(x => outfit ? x.OutfitId == id : x.ItemId == id);
    public EngagementView Read(Guid id, bool outfit, Guid? actor, string? token, Guid? publicOutfitId = null)
    {
        Access(id, outfit, actor, token, false, publicOutfitId);
        var rows = Rows(id, outfit, readOnly: true);
        return new(rows.Count(x => x.Kind == "view"), rows.Count(x => x.Kind == "like"),
            rows.Count(x => x.Kind == "comment" && !x.IsDeleted), actor.HasValue && rows.Any(x => x.Kind == "like" && x.UserId == actor), actor.HasValue);
    }
    public void React(Guid id, bool outfit, Guid actor, string? token, string kind, bool remove = false, Guid? publicOutfitId = null)
    {
        var owner = Access(id, outfit, actor, token, true, publicOutfitId);
        if (kind == "view" && owner == actor) return;
        var existing = Rows(id, outfit).SingleOrDefault(x => x.Kind == kind && x.UserId == actor);
        if (remove) { if (existing is not null) store.Remove(existing); }
        else if (existing is null) store.Add(new(id, outfit, actor, kind, null, null, time.GetUtcNow()));
    }
    public PublicPage<CommentView> Comments(Guid id, bool outfit, Guid? actor, string? token, Guid? parent, int page, Guid? publicOutfitId = null)
    {
        var owner = Access(id, outfit, actor, token, false, publicOutfitId);
        PublicProfiles.ValidatePage(page, 20);
        var rows = Rows(id, outfit, readOnly: true).Where(x => x.Kind == "comment" && x.ParentId == parent);
        var total = rows.Count();
        var result = rows.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToArray()
            .Select(x => View(x, actor, owner)).ToArray();
        return new(result, total, page, 20);
    }
    private CommentView View(PostInteraction x, Guid? actor, Guid owner)
    {
        var user = store.FindUser(x.UserId);
        return new(x.Id, new(user.Id, user.DisplayName, user.Username, user.AvatarStorageKey == null ? null : $"/api/users/{user.Id}/avatar"), x.Text, x.ParentId, x.CreatedAt, x.UpdatedAt,
            x.IsDeleted, !x.IsDeleted && actor == x.UserId, !x.IsDeleted && (actor == x.UserId || actor == owner));
    }
    public Guid AddComment(Guid id, bool outfit, Guid actor, string? token, string text, Guid? parent, Guid? publicOutfitId = null)
    {
        Access(id, outfit, actor, token, true, publicOutfitId);
        if (parent.HasValue && !Rows(id, outfit).Any(x => x.Id == parent && x.Kind == "comment" && x.ParentId == null && !x.IsDeleted))
            throw new ArgumentException("Invalid reply target.");
        var comment = new PostInteraction(id, outfit, actor, "comment", text, parent, time.GetUtcNow());
        store.Add(comment); return comment.Id;
    }
    public void ChangeComment(Guid id, bool outfit, Guid actor, string? token, Guid commentId, string? text, bool delete, Guid? publicOutfitId = null)
    {
        var owner = Access(id, outfit, actor, token, true, publicOutfitId);
        var comment = Rows(id, outfit).SingleOrDefault(x => x.Id == commentId && x.Kind == "comment") ?? throw new KeyNotFoundException();
        if (comment.UserId != actor && !(delete && owner == actor)) throw new UnauthorizedAccessException();
        if (delete) comment.Delete(); else comment.Edit(text!, time.GetUtcNow());
    }
}
