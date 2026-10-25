using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfSocialProfileStore(OutfitlyDbContext db) : ISocialProfileStore
{
    private IQueryable<User> Users => db.Users.Where(u => db.Accounts.Any(a => a.UserId == u.Id && a.EmailConfirmed));
    private IQueryable<UserFollow> Follows => db.Set<UserFollow>();
    public Task<PublicProfile?> ProfileAsync(string username, Guid? actor, CancellationToken ct) => Users.AsNoTracking()
        .Where(u => u.Username == username).Select(u => new PublicProfile(u.Id, u.DisplayName, u.Username,
            u.AvatarStorageKey == null ? null : "/api/users/" + u.Id + "/avatar", u.Bio,
            Follows.Count(f => f.FollowedId == u.Id && Users.Any(p => p.Id == f.FollowerId)),
            Follows.Count(f => f.FollowerId == u.Id && Users.Any(p => p.Id == f.FollowedId)),
            actor.HasValue && Follows.Any(f => f.FollowerId == actor && f.FollowedId == u.Id), actor.HasValue && actor != u.Id))
        .SingleOrDefaultAsync(ct);
    public async Task<PublicPage<PublicUser>> ConnectionsAsync(Guid id, bool followers, int page, CancellationToken ct)
    {
        var users = Users.AsNoTracking().Where(u => Follows.Any(f => followers
            ? f.FollowedId == id && f.FollowerId == u.Id : f.FollowerId == id && f.FollowedId == u.Id));
        var total = await users.CountAsync(ct);
        var items = await users.OrderBy(u => u.Username).ThenBy(u => u.Id).Skip((page - 1) * 20).Take(20)
            .Select(u => new PublicUser(u.Id, u.DisplayName, u.Username, u.AvatarStorageKey == null ? null : "/api/users/" + u.Id + "/avatar")).ToArrayAsync(ct);
        return new(items, total, page, 20);
    }
    public Task<bool> ConfirmedAsync(Guid id, CancellationToken ct) => Users.AnyAsync(u => u.Id == id, ct);
    public Task<UserFollow?> FollowAsync(Guid actor, Guid target, CancellationToken ct) => db.Set<UserFollow>().SingleOrDefaultAsync(f => f.FollowerId == actor && f.FollowedId == target, ct);
    public Task<string?> AvatarKeyAsync(Guid id, CancellationToken ct) => Users.Where(u => u.Id == id).Select(u => u.AvatarStorageKey).SingleOrDefaultAsync(ct);
    public void Add(UserFollow follow) => db.Add(follow);
    public void Remove(UserFollow follow) => db.Remove(follow);
}
