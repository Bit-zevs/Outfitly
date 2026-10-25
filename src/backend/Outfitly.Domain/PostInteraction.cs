namespace Outfitly.Domain;

public sealed class PostInteraction
{
    public Guid Id { get; private set; }
    public Guid? ItemId { get; private set; }
    public Guid? OutfitId { get; private set; }
    public Guid UserId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string? Text { get; private set; }
    public Guid? ParentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    private PostInteraction() { }
    public PostInteraction(Guid target, bool outfit, Guid user, string kind, string? text, Guid? parent, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); UserId = Guard.Id(user);
        if (outfit) OutfitId = Guard.Id(target); else ItemId = Guard.Id(target);
        if (kind is not ("view" or "like" or "comment")) throw new ArgumentException("Invalid interaction kind.");
        Kind = kind; ParentId = parent; CreatedAt = now;
        if (kind == "comment") Text = ValidateText(text);
        else if (text is not null || parent is not null) throw new ArgumentException("Invalid interaction.");
    }
    public void Edit(string text, DateTimeOffset now)
    {
        if (Kind != "comment" || IsDeleted) throw new ArgumentException("Comment is deleted.");
        Text = ValidateText(text); UpdatedAt = now;
    }
    public void Delete() { Text = null; IsDeleted = true; }
    private static string ValidateText(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 2000) throw new ArgumentException("Comment must contain 1–2000 characters.");
        return value;
    }
}
