namespace Outfitly.Domain;

public sealed class User
{
    public Guid Id { get; }
    public string DisplayName { get; private set; }
    public string Username { get; private set; }

    public User(Guid id, string displayName, string? username = null)
    {
        Id = Guard.Id(id);
        DisplayName = Guard.Name(displayName);
        Username = username is null ? Usernames.FromId(id) : Usernames.Normalize(username);
    }

    public void Rename(string displayName) => DisplayName = Guard.Name(displayName);
    public void ChangeUsername(string username) => Username = Usernames.Normalize(username);
}
