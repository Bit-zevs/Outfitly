namespace Outfitly.Domain;

public sealed class Favorite
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid TargetId { get; private set; }
    public Guid? ShareLinkId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    private Favorite() { }
    public Favorite(Guid user, string kind, Guid target, Guid? link, DateTime now)
    {
        Id = Guid.NewGuid(); UserId = Guard.Id(user); Kind = WorkspaceKinds.Validate(kind); TargetId = Guard.Id(target); ShareLinkId = link; CreatedAtUtc = now;
    }
    public void SetLink(Guid? id) => ShareLinkId = id;
}
public static class WorkspaceKinds
{
    public static string Validate(string kind) => kind is "items" or "outfits" ? kind : throw new ArgumentException("Invalid editor kind.");
}
public sealed class CopyReceipt
{
    public Guid UserId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid ResultId { get; private set; }
    private CopyReceipt() { }
    public CopyReceipt(Guid user, Guid operation, Guid source, Guid result)
    { UserId = Guard.Id(user); OperationId = Guard.Id(operation); SourceId = Guard.Id(source); ResultId = Guard.Id(result); }
}
public sealed class EditorDraft
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid? TargetId { get; private set; }
    public string? ActiveSlot { get; private set; }
    public string Data { get; private set; } = null!;
    public string? BaseVersion { get; private set; }
    public Guid Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? PhotoStorageKey { get; private set; }
    public Guid? CompletedId { get; private set; }
    private EditorDraft() { }
    public EditorDraft(Guid id, Guid user, string kind, Guid? target, string data, string? baseline, DateTime now)
    {
        Id = Guard.Id(id); UserId = Guard.Id(user); Kind = WorkspaceKinds.Validate(kind);
        if (target.HasValue) Guard.Id(target.Value);
        TargetId = target; ActiveSlot = kind + ":" + (target?.ToString() ?? "new"); Update(data, baseline, now);
    }
    public void Update(string data, string? baseline, DateTime now)
    {
        if (CompletedId.HasValue) throw new InvalidOperationException("Draft is completed.");
        if (data.Length > 65536 || baseline?.Length > 128) throw new ArgumentException("Draft exceeds allowed size.");
        Data = data; BaseVersion = baseline; Touch(now);
    }
    public void SetPhoto(string? key, DateTime now) { PhotoStorageKey = key; Touch(now); }
    public void Complete(Guid result, DateTime now) { CompletedId = Guard.Id(result); ActiveSlot = null; PhotoStorageKey = null; Touch(now); }
    private void Touch(DateTime now) { UpdatedAtUtc = now; Version = Guid.NewGuid(); }
}
