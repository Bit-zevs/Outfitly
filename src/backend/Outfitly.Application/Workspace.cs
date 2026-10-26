using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Outfitly.Domain;
namespace Outfitly.Application;

public static class ResourceVersion
{
    private static string Hash(object data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    public static string For(WardrobeItem x) => Hash(new { x.Id, x.Name, x.Category, x.Color, x.Size, x.Brand, x.Description, x.PhotoUrl, x.PhotoStorageKey, x.IsPublic });
    public static string For(Outfit x) => Hash(new { x.Id, x.Name, x.Description, x.IsPublic, Items = x.ItemIds.Order().ToArray() });
}
public sealed record DraftContent(string? Name = null, string? Description = null, ClothingCategory? Category = null,
    string? Color = null, string? Size = null, string? Brand = null, string? PhotoUrl = null,
    string PhotoMode = "file", bool RemovePhoto = false, IReadOnlyList<Guid>? ItemIds = null);
public sealed record DraftInput(string Kind, Guid? TargetId, Guid? Version, string? BaseVersion, DraftContent Content);
public sealed record DraftView(Guid Id, string Kind, Guid? TargetId, Guid Version, string? BaseVersion,
    DateTime UpdatedAtUtc, DraftContent Content, string? PhotoUrl, Guid? CompletedId);
public sealed record FavoriteView(Guid TargetId, string Kind, bool Available, string? Name, string? PhotoUrl, string? Route);
public interface IWorkspaceStore
{
    IQueryable<Favorite> Favorites { get; }
    IQueryable<EditorDraft> Drafts { get; }
    IQueryable<CopyReceipt> Copies { get; }
    void Add(Favorite x);
    void Add(EditorDraft x);
    void Add(CopyReceipt x);
    void Remove(Favorite x);
    void Remove(EditorDraft x);
}
public sealed class WorkspaceService(IWorkspaceStore store, IWardrobeRepository wardrobe, WardrobeService commands,
    IPhotoStore files, IPhotoProcessor processor, PhotoChanges changes, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static void Actor(Guid actor) { if (actor == Guid.Empty) throw new UnauthorizedAccessException(); }
    private (Guid Owner, bool Public)? Target(string kind, Guid id)
    {
        WorkspaceKinds.Validate(kind);
        if (kind == "items") { var item = wardrobe.GetAll().SingleOrDefault(x => x.Id == id); return item is null ? null : (item.OwnerId, item.IsPublic); }
        var outfit = wardrobe.GetOutfits().SingleOrDefault(x => x.Id == id); return outfit is null ? null : (outfit.OwnerId, outfit.IsPublic);
    }
    private ShareLink? Link(string kind, Guid id, string? token = null, Guid? linkId = null) => wardrobe.GetLinks().SingleOrDefault(x =>
        x.IsActive && x.TargetId == id && x.TargetType == (kind == "items" ? ShareTargetType.WardrobeItem : ShareTargetType.Outfit) &&
        (token != null && x.Token == token || linkId != null && x.Id == linkId));
    private bool Accessible(Guid actor, string kind, Guid id, string? token = null, Guid? linkId = null)
    {
        var target = Target(kind, id);
        return target.HasValue && (target.Value.Owner == actor || target.Value.Public || Link(kind, id, token, linkId) != null);
    }
    private void RequireAccess(Guid actor, string kind, Guid id, string? token)
    { Actor(actor); if (!Accessible(actor, kind, id, token)) throw new KeyNotFoundException(); }
    private void Mark(string kind, Guid id)
    { if (kind == "outfits" && wardrobe.GetOutfits().SingleOrDefault(x => x.Id == id) is { } outfit) wardrobe.MarkOutfitChanged(outfit); }
    public bool IsFavorite(Guid actor, string kind, Guid id, string? token)
    { RequireAccess(actor, kind, id, token); return store.Favorites.Any(x => x.UserId == actor && x.Kind == kind && x.TargetId == id); }
    public void Favorite(Guid actor, string kind, Guid id, string? token, bool remove)
    {
        Actor(actor); WorkspaceKinds.Validate(kind);
        var existing = store.Favorites.SingleOrDefault(x => x.UserId == actor && x.Kind == kind && x.TargetId == id);
        if (remove) { if (existing != null) { store.Remove(existing); Mark(kind, id); } return; }
        RequireAccess(actor, kind, id, token); Mark(kind, id);
        var link = Link(kind, id, token);
        if (existing == null) store.Add(new Favorite(actor, kind, id, link?.Id, Now));
        else if (link != null) existing.SetLink(link.Id);
    }
    public PublicPage<FavoriteView> Favorites(Guid actor, string kind, int page)
    {
        Actor(actor); WorkspaceKinds.Validate(kind); PublicProfiles.ValidatePage(page, 20);
        var rows = store.Favorites.Where(x => x.UserId == actor && x.Kind == kind);
        var total = rows.Count();
        var result = rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToArray().Select(x => FavoriteView(actor, x)).ToArray();
        return new(result, total, page, 20);
    }
    private FavoriteView FavoriteView(Guid actor, Favorite f)
    {
        if (!Accessible(actor, f.Kind, f.TargetId, linkId: f.ShareLinkId)) return new(f.TargetId, f.Kind, false, null, null, null);
        var target = Target(f.Kind, f.TargetId)!.Value;
        var own = target.Owner == actor; var link = Link(f.Kind, f.TargetId, linkId: f.ShareLinkId);
        var route = own ? $"/{f.Kind}/{f.TargetId}" : target.Public ? $"/public/{f.Kind}/{f.TargetId}" : $"/shared/{f.Kind}/{link!.Token}";
        var prefix = own ? $"/api/items/" : target.Public ? "/api/wardrobe/items/" : $"/api/shared/items/";
        if (f.Kind == "items")
        {
            var item = wardrobe.GetAll().Single(x => x.Id == f.TargetId);
            var url = item.PhotoStorageKey == null ? item.PhotoUrl : own || target.Public ? prefix + item.Id + "/photo" : prefix + link!.Token + "/photo";
            return new(f.TargetId, f.Kind, true, item.Name, url, route);
        }
        var outfit = wardrobe.GetOutfits().Single(x => x.Id == f.TargetId);
        var first = wardrobe.GetAll().FirstOrDefault(x => outfit.ItemIds.Contains(x.Id));
        string? photo = first?.PhotoUrl;
        if (first?.PhotoStorageKey != null) photo = own ? $"/api/items/{first.Id}/photo" : target.Public ? $"/api/wardrobe/outfits/{outfit.Id}/items/{first.Id}/photo" : $"/api/shared/outfits/{link!.Token}/items/{first.Id}/photo";
        return new(f.TargetId, f.Kind, true, outfit.Name, photo, route);
    }
    public async Task<Guid> CopyAsync(Guid actor, Guid source, Guid operation, string? token, CancellationToken ct)
    {
        Actor(actor); if (operation == Guid.Empty) throw new ArgumentException("Operation id is required.");
        var previous = store.Copies.SingleOrDefault(x => x.UserId == actor && x.OperationId == operation);
        if (previous != null) { if (previous.SourceId != source) throw new ArgumentException("Operation belongs to another source."); return previous.ResultId; }
        RequireAccess(actor, "outfits", source, token);
        var original = wardrobe.GetOutfits().Single(x => x.Id == source); wardrobe.MarkOutfitChanged(original);
        var copy = commands.CreateOutfit(actor, original.Name + " (копия)", original.Description);
        foreach (var item in wardrobe.GetAll().Where(x => original.ItemIds.Contains(x.Id)).ToArray())
        {
            var cloned = commands.CreateItem(actor, item.Name, item.Category, item.Color, item.Size, item.Brand, item.Description, item.PhotoUrl);
            if (item.PhotoStorageKey != null)
            {
                var photo = await files.ReadAsync(item.PhotoStorageKey, ct) ?? throw Conflict();
                await using var stream = photo.Content; using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct);
                var key = await files.WriteAsync(buffer.ToArray(), photo.ContentType == "image/png" ? ".png" : ".jpg", ct); changes.Created(key); cloned.SetUploadedPhoto(key);
            }
            copy.AddItem(cloned);
        }
        store.Add(new CopyReceipt(actor, operation, source, copy.Id)); return copy.Id;
    }
    private static WardrobeConcurrencyException Conflict() => new(new InvalidOperationException("Resource changed; reload before saving."));
    private EditorDraft Owned(Guid actor, Guid id)
    { Actor(actor); return store.Drafts.SingleOrDefault(x => x.Id == id && x.UserId == actor) ?? throw new KeyNotFoundException(); }
    private static DraftContent Content(EditorDraft draft) => JsonSerializer.Deserialize<DraftContent>(draft.Data)!;
    private static DraftView View(EditorDraft d) => new(d.Id, d.Kind, d.TargetId, d.Version, d.BaseVersion, d.UpdatedAtUtc, Content(d), d.PhotoStorageKey == null ? null : $"/api/drafts/{d.Id}/photo", d.CompletedId);
    private static void Version(EditorDraft d, Guid? version) { if (d.Version != version) throw Conflict(); }
    private void OwnTarget(Guid actor, string kind, Guid? id)
    { if (id.HasValue && Target(kind, id.Value)?.Owner != actor) throw new KeyNotFoundException(); }
    public DraftView? Active(Guid actor, string kind, Guid? target)
    {
        Actor(actor); WorkspaceKinds.Validate(kind); var slot = kind + ":" + (target?.ToString() ?? "new");
        var d = store.Drafts.SingleOrDefault(x => x.UserId == actor && x.ActiveSlot == slot); return d == null ? null : View(d);
    }
    public DraftView Read(Guid actor, Guid id) => View(Owned(actor, id));
    public PublicPage<DraftView> Drafts(Guid actor, int page)
    {
        Actor(actor); PublicProfiles.ValidatePage(page, 20); var rows = store.Drafts.Where(x => x.UserId == actor && x.ActiveSlot != null);
        return new(rows.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).ToArray().Select(View).ToArray(), rows.Count(), page, 20);
    }
    public DraftView Update(Guid actor, Guid id, DraftInput input)
    {
        Actor(actor); WorkspaceKinds.Validate(input.Kind); OwnTarget(actor, input.Kind, input.TargetId);
        if (input.Content is null || input.Content.PhotoMode is not ("file" or "url") || input.Content.Category.HasValue && !Enum.IsDefined(input.Content.Category.Value) || input.Content.ItemIds?.Count > 1000) throw new ArgumentException("Invalid draft fields.");
        var json = JsonSerializer.Serialize(input.Content);
        var draft = store.Drafts.SingleOrDefault(x => x.Id == id);
        if (draft == null)
        {
            if (input.Version.HasValue) throw Conflict();
            var slot = input.Kind + ":" + (input.TargetId?.ToString() ?? "new");
            if (store.Drafts.Any(x => x.UserId == actor && x.ActiveSlot == slot)) throw Conflict();
            draft = new EditorDraft(id, actor, input.Kind, input.TargetId, json, input.BaseVersion, Now); store.Add(draft);
        }
        else { if (draft.UserId != actor) throw new KeyNotFoundException(); Version(draft, input.Version); if (draft.Kind != input.Kind || draft.TargetId != input.TargetId) throw new ArgumentException("Cannot change draft target."); draft.Update(json, input.BaseVersion, Now); }
        return View(draft);
    }
    public void Delete(Guid actor, Guid id, Guid version)
    {
        var d = Owned(actor, id); if (d.CompletedId.HasValue) return; Version(d, version); changes.RemoveAfterCommit(d.PhotoStorageKey); store.Remove(d);
    }
    public string? Photo(Guid actor, Guid id) => Owned(actor, id).PhotoStorageKey;
    public async Task<DraftView> UploadAsync(Guid actor, Guid id, Guid version, Stream stream, string type, CancellationToken ct)
    {
        var d = Owned(actor, id); Version(d, version); if (d.CompletedId.HasValue) throw Conflict();
        if (d.Kind != "items") throw new ArgumentException("Only item drafts have photos.");
        using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0) { if (buffer.Length + read > PhotoService.MaxBytes) throw new PhotoTooLargeException(); buffer.Write(chunk, 0, read); }
        if (buffer.Length == 0) throw new ArgumentException("Empty photo.");
        var photo = processor.Process(buffer.ToArray(), type); var key = await files.WriteAsync(photo.Content, photo.Extension, ct);
        changes.Created(key); changes.RemoveAfterCommit(d.PhotoStorageKey); d.SetPhoto(key, Now); return View(d);
    }
    public DraftView RemovePhoto(Guid actor, Guid id, Guid version)
    { var d = Owned(actor, id); Version(d, version); if (d.CompletedId.HasValue) throw Conflict(); changes.RemoveAfterCommit(d.PhotoStorageKey); d.SetPhoto(null, Now); return View(d); }
    public Guid Save(Guid actor, Guid id, Guid version)
    {
        var d = Owned(actor, id); if (d.CompletedId.HasValue) return d.CompletedId.Value; Version(d, version);
        OwnTarget(actor, d.Kind, d.TargetId); var content = Content(d);
        if (d.TargetId is { } target)
        {
            var actual = d.Kind == "items" ? ResourceVersion.For(wardrobe.GetAll().Single(x => x.Id == target)) : ResourceVersion.For(wardrobe.GetOutfits().Single(x => x.Id == target));
            if (actual != d.BaseVersion) throw Conflict();
        }
        Guid result;
        if (d.Kind == "items")
        {
            if (content.Category == null) throw new ArgumentException("Category is required.");
            var url = content.PhotoMode == "url" && !string.IsNullOrWhiteSpace(content.PhotoUrl) ? content.PhotoUrl.Trim() : null;
            WardrobeItem item;
            if (d.TargetId is { } itemId) { item = wardrobe.GetAll().Single(x => x.Id == itemId); commands.UpdateItem(actor, itemId, content.Name!, content.Category.Value, content.Color, content.Size, content.Brand, content.Description, url); }
            else item = commands.CreateItem(actor, content.Name!, content.Category.Value, content.Color, content.Size, content.Brand, content.Description, url);
            if (content.PhotoMode == "file" && !content.RemovePhoto && d.PhotoStorageKey != null)
            { changes.RemoveAfterCommit(item.PhotoStorageKey); item.SetUploadedPhoto(d.PhotoStorageKey); }
            else if (content.PhotoMode == "url" && string.IsNullOrEmpty(url) || content.PhotoMode == "file" && content.RemovePhoto) { changes.RemoveAfterCommit(item.PhotoStorageKey); item.RemovePhoto(); }
            if (content.PhotoMode != "file" || content.RemovePhoto) changes.RemoveAfterCommit(d.PhotoStorageKey);
            result = item.Id;
        }
        else
        {
            Outfit outfit;
            if (d.TargetId is { } outfitId) { outfit = wardrobe.GetOutfits().Single(x => x.Id == outfitId); commands.UpdateOutfit(actor, outfitId, content.Name!, content.Description); }
            else outfit = commands.CreateOutfit(actor, content.Name!, content.Description);
            var desired = (content.ItemIds ?? []).Distinct().ToArray();
            foreach (var itemId in desired.Except(outfit.ItemIds).ToArray()) commands.AddItemToOutfit(actor, outfit.Id, itemId);
            foreach (var itemId in outfit.ItemIds.Except(desired).ToArray()) commands.RemoveItemFromOutfit(actor, outfit.Id, itemId);
            changes.RemoveAfterCommit(d.PhotoStorageKey); result = outfit.Id;
        }
        d.Complete(result, Now); return result;
    }
}
