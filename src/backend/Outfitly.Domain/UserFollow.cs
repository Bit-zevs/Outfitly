namespace Outfitly.Domain;

public sealed class UserFollow
{
    public Guid FollowerId { get; private set; }
    public Guid FollowedId { get; private set; }
    private UserFollow() { }
    public UserFollow(Guid followerId, Guid followedId)
    {
        FollowerId = Guard.Id(followerId); FollowedId = Guard.Id(followedId);
        if (FollowerId == FollowedId) throw new ArgumentException("Cannot follow yourself.");
    }
}
