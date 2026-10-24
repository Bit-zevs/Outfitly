using Outfitly.Domain;

namespace Outfitly.Tests.Domain;

public sealed class UsernameTests
{
    [Theory]
    [InlineData(" @Anna_23 ", "anna_23")]
    [InlineData("ABC", "abc")]
    [InlineData("a12345678901234567890123456789", "a12345678901234567890123456789")]
    public void User_normalizes_username_independently_of_display_name(string input, string expected)
    {
        var user = new User(Guid.NewGuid(), "Анна", input);
        Assert.Equal(expected, user.Username);
        user.ChangeUsername(" @OTHER ");
        Assert.Equal("other", user.Username);
        Assert.Equal("Анна", user.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("1anna")]
    [InlineData("_anna")]
    [InlineData("анна")]
    [InlineData("anna.name")]
    [InlineData("anna name")]
    [InlineData("@@anna")]
    [InlineData("a1234567890123456789012345678901")]
    public void Invalid_usernames_are_rejected(string? input) =>
        Assert.Throws<ArgumentException>(() => Usernames.Normalize(input!));

    [Fact]
    public void Automatic_usernames_encode_the_entire_id()
    {
        Assert.Equal("u_aaaaaaaaaaaaaaaaaaaaaaaaaa", Usernames.FromId(Guid.Empty));
        Assert.Equal("u_h7777777777777777777777777", Usernames.FromId(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        var id = Guid.NewGuid();
        var username = Usernames.FromId(id);
        Assert.Equal(28, username.Length);
        Assert.Equal(username, Usernames.Normalize(username));
        Assert.Equal(username, new User(id, "Legacy").Username);
    }
}
