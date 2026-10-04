using System.Security.Cryptography;

namespace Outfitly.Domain;

public enum ShareTargetType
{
    WardrobeItem,
    Outfit
}

public sealed class ShareLink
{
    public Guid Id { get; }
    public string Token { get; }
    public ShareTargetType TargetType { get; }
    public Guid TargetId { get; }
    public bool IsActive { get; private set; } = true;

    // Rehydration must restore the persisted token without generating another one.
    private ShareLink()
    {
        Token = null!;
    }

    public ShareLink(Guid id, ShareTargetType targetType, Guid targetId)
    {
        Id = Guard.Id(id);
        TargetId = Guard.Id(targetId);
        if (!Enum.IsDefined(targetType))
            throw new ArgumentOutOfRangeException(nameof(targetType));
        TargetType = targetType;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    public void Deactivate() => IsActive = false;
}
