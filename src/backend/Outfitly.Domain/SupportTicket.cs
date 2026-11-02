namespace Outfitly.Domain;

public sealed class SupportTicket
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Status { get; private set; } = "new";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid Revision { get; private set; }
    public List<SupportMessage> Messages { get; private set; } = [];
    private SupportTicket() { }
    public SupportTicket(Guid owner, string category, string subject, DateTime now)
    {
        Id = Guid.NewGuid(); OwnerId = Guard.Id(owner);
        if (category is not ("feedback" or "suggestion" or "bug" or "problem")) throw new ArgumentException("Invalid support category.");
        Category = category; Subject = subject.Trim();
        if (Subject.Length is < 1 or > 150) throw new ArgumentException("Subject must contain 1–150 characters.");
        CreatedAtUtc = UpdatedAtUtc = now; Revision = Guid.NewGuid();
    }
    public void Reply(Guid author, bool admin, string text, IReadOnlyCollection<SupportAttachment> attachments, DateTime now)
    {
        if (Status == "closed") throw new InvalidOperationException("Ticket is closed.");
        Messages.Add(new SupportMessage(Id, author, admin, text, attachments, now));
        if (admin && Status == "new") Status = "inProgress";
        Touch(now);
    }
    public void SetStatus(string status, DateTime now)
    {
        if (status is not ("new" or "inProgress" or "closed")) throw new ArgumentException("Invalid support status.");
        if (Status == "closed" && status == "new") throw new ArgumentException("Reopen into inProgress.");
        Status = status; Touch(now);
    }
    private void Touch(DateTime now) { UpdatedAtUtc = now; Revision = Guid.NewGuid(); }
}
public sealed class SupportMessage
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid AuthorId { get; private set; }
    public bool IsAdmin { get; private set; }
    public string Text { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public List<SupportAttachment> Attachments { get; private set; } = [];
    private SupportMessage() { }
    public SupportMessage(Guid ticket, Guid author, bool admin, string text, IReadOnlyCollection<SupportAttachment> attachments, DateTime now)
    {
        Text = text.Trim();
        if (Text.Length > 5000 || Text.Length == 0 && attachments.Count == 0 || attachments.Count > 3) throw new ArgumentException("Invalid support message.");
        Id = Guid.NewGuid(); TicketId = Guard.Id(ticket); AuthorId = Guard.Id(author); IsAdmin = admin; CreatedAtUtc = now;
        Attachments = attachments.ToList();
    }
}
public sealed class SupportAttachment
{
    public Guid Id { get; private set; }
    public string StorageKey { get; private set; } = null!;
    private SupportAttachment() { }
    public SupportAttachment(string key) { ArgumentException.ThrowIfNullOrWhiteSpace(key); Id = Guid.NewGuid(); StorageKey = key; }
}
