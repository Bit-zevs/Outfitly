using Microsoft.AspNetCore.Identity;
using Outfitly.Application.Authentication;

namespace Outfitly.Infrastructure.Authentication;

public sealed class AccountPasswordHasher : IAccountPasswordHasher
{
    private readonly PasswordHasher<object> hasher = new();
    private readonly object subject = new();
    private readonly string dummyHash;

    public AccountPasswordHasher() => dummyHash = hasher.HashPassword(subject, Guid.NewGuid().ToString());
    public string Hash(string password) => hasher.HashPassword(subject, password);

    public PasswordCheck Verify(string? hash, string password) =>
        hasher.VerifyHashedPassword(subject, hash ?? dummyHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.RehashNeeded,
            _ => PasswordCheck.Failed
        };
}
