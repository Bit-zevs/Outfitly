using Outfitly.Domain;
namespace Outfitly.Application;
public interface ISupportAdminAccess { bool IsAdmin(Guid actor); }
public interface ISupportStore
{
    Task<SupportTicket?> FindAsync(Guid id, CancellationToken token);
    Task<SupportPage> ListAsync(Guid? owner, string? category, string? status, int page, CancellationToken token);
    void Add(SupportTicket ticket);
}
public sealed record SupportSummary(Guid Id, string Subject, string Category, string Status, DateTime UpdatedAtUtc);
public sealed record SupportPage(IReadOnlyCollection<SupportSummary> Items, int Total, int Page, int PageSize = 20);
public sealed record SupportAttachmentView(Guid Id, string Url);
public sealed record SupportMessageView(Guid Id, Guid AuthorId, bool IsAdmin, string Text, DateTime CreatedAtUtc, IReadOnlyCollection<SupportAttachmentView> Attachments);
public sealed record SupportTicketView(Guid Id, Guid OwnerId, string Subject, string Category, string Status, Guid Revision, DateTime CreatedAtUtc, IReadOnlyCollection<SupportMessageView> Messages);
public sealed record SupportUpload(byte[] Content, string ContentType);
public sealed class SupportService(ISupportStore store, ISupportAdminAccess admins, IPhotoStore photos, IPhotoProcessor processor, PhotoChanges changes, TimeProvider time)
{
    public bool IsAdmin(Guid actor) => actor != Guid.Empty && admins.IsAdmin(actor);
    public void RequireAdmin(Guid actor) { RequireActor(actor); if (!IsAdmin(actor)) throw new UnauthorizedAccessException(); }
    private static void RequireActor(Guid actor) { if (actor == Guid.Empty) throw new UnauthorizedAccessException(); }
    public Task<SupportPage> ListAsync(Guid actor, bool admin, string? category, string? status, int page, CancellationToken token)
    {
        RequireActor(actor); if (admin) RequireAdmin(actor);
        if (page < 1 || page > int.MaxValue / 20) throw new ArgumentException("Invalid page.");
        if (category is not null && category is not ("feedback" or "suggestion" or "bug" or "problem")) throw new ArgumentException("Invalid category.");
        if (status is not null && status is not ("new" or "inProgress" or "closed")) throw new ArgumentException("Invalid status.");
        return store.ListAsync(admin ? null : actor, category, status, page, token);
    }
    private async Task<SupportTicket> Access(Guid actor, Guid id, bool admin, CancellationToken token)
    {
        RequireActor(actor); if (admin) RequireAdmin(actor);
        var ticket = await store.FindAsync(id, token) ?? throw new KeyNotFoundException();
        if (!admin && ticket.OwnerId != actor) throw new KeyNotFoundException();
        return ticket;
    }
    public async Task<SupportTicketView> ReadAsync(Guid actor, Guid id, bool admin, CancellationToken token) => View(await Access(actor, id, admin, token));
    public async Task<SupportTicketView> CreateAsync(Guid actor, string category, string subject, string text, IReadOnlyCollection<SupportUpload> uploads, CancellationToken token)
    {
        RequireActor(actor);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Description is required.");
        var ticket = new SupportTicket(actor, category, subject, time.GetUtcNow().UtcDateTime);
        ticket.Reply(actor, false, text, await Uploads(uploads, token), time.GetUtcNow().UtcDateTime);
        store.Add(ticket); return View(ticket);
    }
    public async Task ReplyAsync(Guid actor, Guid id, bool admin, Guid revision, string text, IReadOnlyCollection<SupportUpload> uploads, CancellationToken token)
    {
        var ticket = await Access(actor, id, admin, token); CheckRevision(ticket, revision);
        if (ticket.Status == "closed") throw new WardrobeConcurrencyException(new InvalidOperationException("Support ticket changed."));
        ticket.Reply(actor, admin, text, await Uploads(uploads, token), time.GetUtcNow().UtcDateTime);
    }
    public async Task SetStatusAsync(Guid actor, Guid id, Guid revision, string status, CancellationToken token)
    {
        var ticket = await Access(actor, id, true, token); CheckRevision(ticket, revision); ticket.SetStatus(status, time.GetUtcNow().UtcDateTime);
    }
    private static void CheckRevision(SupportTicket ticket, Guid revision) { if (ticket.Revision != revision) throw new WardrobeConcurrencyException(new InvalidOperationException("Support ticket changed.")); }
    public async Task<StoredPhoto?> AttachmentAsync(Guid actor, Guid id, Guid attachmentId, CancellationToken token)
    {
        var ticket = await Access(actor, id, IsAdmin(actor), token);
        var attachment = ticket.Messages.SelectMany(x => x.Attachments).SingleOrDefault(x => x.Id == attachmentId) ?? throw new KeyNotFoundException();
        return await photos.ReadAsync(attachment.StorageKey, token);
    }
    private async Task<IReadOnlyCollection<SupportAttachment>> Uploads(IReadOnlyCollection<SupportUpload> uploads, CancellationToken token)
    {
        if (uploads.Count > 3) throw new ArgumentException("At most three images.");
        var result = new List<SupportAttachment>();
        foreach (var upload in uploads)
        {
            if (upload.Content.Length > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            if (upload.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) throw new UnsupportedPhotoException();
            var image = processor.Process(upload.Content, upload.ContentType);
            var key = await photos.WriteAsync(image.Content, image.Extension, token); changes.Created(key); result.Add(new(key));
        }
        return result;
    }
    private static SupportTicketView View(SupportTicket ticket) => new(ticket.Id, ticket.OwnerId, ticket.Subject, ticket.Category, ticket.Status, ticket.Revision, ticket.CreatedAtUtc,
        ticket.Messages.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => new SupportMessageView(x.Id, x.AuthorId, x.IsAdmin, x.Text, x.CreatedAtUtc,
            x.Attachments.Select(a => new SupportAttachmentView(a.Id, $"/api/support/tickets/{ticket.Id}/attachments/{a.Id}")).ToArray())).ToArray());
}

