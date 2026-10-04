namespace Outfitly.Domain;

public sealed class User
{
    public Guid Id { get; }
    public string DisplayName { get; private set; }

    public User(Guid id, string displayName)
    {
        Id = Guard.Id(id);
        DisplayName = Guard.Name(displayName);
    }

    public void Rename(string displayName) => DisplayName = Guard.Name(displayName);
}
