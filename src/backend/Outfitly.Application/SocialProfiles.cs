using Outfitly.Domain;
using Outfitly.Application.Authentication;
namespace Outfitly.Application;

public sealed record PublicProfile(Guid Id, string DisplayName, string Username, string? AvatarUrl,
    string? Bio, int FollowersCount, int FollowingCount, bool IsFollowing, bool CanFollow);
public interface ISocialProfileStore
{
    Task<PublicProfile?> ProfileAsync(string username, Guid? actor, CancellationToken ct);
    Task<PublicPage<PublicUser>> ConnectionsAsync(Guid id, bool followers, int page, CancellationToken ct);
    Task<bool> ConfirmedAsync(Guid id, CancellationToken ct);
    Task<UserFollow?> FollowAsync(Guid actor, Guid target, CancellationToken ct);
    Task<string?> AvatarKeyAsync(Guid id, CancellationToken ct);
    void Add(UserFollow follow);
    void Remove(UserFollow follow);
}
public sealed class SocialProfiles(ISocialProfileStore store)
{
    public async Task<PublicProfile> ProfileAsync(string username, Guid? actor, CancellationToken ct) =>
        await store.ProfileAsync(Usernames.Normalize(username), actor, ct) ?? throw new KeyNotFoundException();
    public async Task<PublicPage<PublicUser>> ConnectionsAsync(string username, bool followers, int page, CancellationToken ct)
    {
        PublicProfiles.ValidatePage(page, 20);
        var profile = await ProfileAsync(username, null, ct);
        return await store.ConnectionsAsync(profile.Id, followers, page, ct);
    }
    public async Task FollowAsync(Guid actor, string username, bool remove, CancellationToken ct)
    {
        if (!await store.ConfirmedAsync(actor, ct)) throw new UnauthorizedAccessException();
        var target = await ProfileAsync(username, actor, ct);
        if (actor == target.Id) throw new ArgumentException("Cannot follow yourself.");
        var follow = await store.FollowAsync(actor, target.Id, ct);
        if (remove) { if (follow is not null) store.Remove(follow); }
        else if (follow is null) store.Add(new UserFollow(actor, target.Id));
    }
    public Task<string?> AvatarKeyAsync(Guid id, CancellationToken ct) => store.AvatarKeyAsync(id, ct);
}

public sealed record AvatarCrop(int X, int Y, int Size);
public interface IAvatarProcessor
{
    ProcessedPhoto ProcessAvatar(byte[] content, string contentType, AvatarCrop crop);
}
public sealed class AvatarService(IAccountStore accounts, IPhotoStore store, IAvatarProcessor processor, PhotoChanges changes)
{
    public async Task UploadAsync(Guid actor, Stream stream, string contentType, AvatarCrop crop, CancellationToken ct)
    {
        var user = await accounts.FindUserAsync(actor, ct) ?? throw new KeyNotFoundException();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int count;
        while ((count = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > PhotoService.MaxBytes) throw new PhotoTooLargeException();
            buffer.Write(chunk, 0, count);
        }
        if (buffer.Length == 0) throw new ArgumentException("Image must not be empty.");
        var photo = processor.ProcessAvatar(buffer.ToArray(), contentType, crop);
        var key = await store.WriteAsync(photo.Content, photo.Extension, ct);
        changes.Created(key); changes.RemoveAfterCommit(user.AvatarStorageKey); user.SetAvatar(key);
    }
    public async Task DeleteAsync(Guid actor, CancellationToken ct)
    {
        var user = await accounts.FindUserAsync(actor, ct) ?? throw new KeyNotFoundException();
        changes.RemoveAfterCommit(user.AvatarStorageKey); user.SetAvatar(null);
    }
}
