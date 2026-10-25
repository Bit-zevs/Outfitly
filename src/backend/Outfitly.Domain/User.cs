namespace Outfitly.Domain;

public sealed class User
{
    public Guid Id { get; }
    public string DisplayName { get; private set; }
    public string? Bio { get; private set; }
    public string? AvatarStorageKey { get; private set; }
    public string Username { get; private set; }

    public User(Guid id, string displayName, string? username = null)
    {
        Id = Guard.Id(id);
        DisplayName = Guard.Name(displayName);
        Username = username is null ? Usernames.FromId(id) : Usernames.Normalize(username);
    }

    public void SetBio(string? bio)
    {
        var value = bio?.Trim();
        if (value?.Length > 500) throw new ArgumentException("Bio must not exceed 500 characters.");
        Bio = string.IsNullOrEmpty(value) ? null : value;
    }
    public void SetAvatar(string? key)
    {
        if (key is not null) ArgumentException.ThrowIfNullOrWhiteSpace(key);
        AvatarStorageKey = key;
    }
    public void Rename(string displayName) => DisplayName = Guard.Name(displayName);
    public void ChangeUsername(string username) => Username = Usernames.Normalize(username);
}
