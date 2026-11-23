using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;

namespace Outfitly.Tests.Infrastructure;

public sealed class AccountPersistenceTests
{
    [Fact]
    public async Task Login_rehashes_an_old_password_and_preserves_significant_spaces()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string password = "  fifteen plus characters  ";
        var oldHasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }));
        var oldHash = oldHasher.HashPassword(new object(), password);
        await using (var write = database.Open())
        {
            var service = new AccountService(new EfAccountStore(write), new AccountPasswordHasher(), TimeProvider.System);
            var registered = await service.RegisterAsync("alice@example.com", "Alice");
            await write.SaveChangesAsync();
            await service.ConfirmEmailAsync(registered.Confirmation.Token, password, "alice");
            (await write.Accounts.SingleAsync()).PasswordHash = oldHash;
            await write.SaveChangesAsync();
        }
        await using (var login = database.Open())
        {
            var service = new AccountService(new EfAccountStore(login), new AccountPasswordHasher(), TimeProvider.System);
            Assert.Null(await service.LoginAsync("alice@example.com", password.Trim()));
            Assert.NotNull(await service.LoginAsync("alice@example.com", password));
            await login.SaveChangesAsync();
        }
        await using var read = database.Open();
        var hash = (await read.Accounts.SingleAsync()).PasswordHash;
        Assert.NotEqual(oldHash, hash);
        Assert.Equal(PasswordCheck.Success, new AccountPasswordHasher().Verify(hash, password));
        Assert.Single(await read.LoginSessions.ToListAsync());
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task Password_length_boundaries_are_enforced(int length, bool accepted)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var write = database.Open())
        {
            var service = new AccountService(new EfAccountStore(write), new AccountPasswordHasher(), TimeProvider.System);
            var registered = await service.RegisterAsync("boundary@example.com", "Boundary");
            await write.SaveChangesAsync();
            var password = new string('a', length);
            if (accepted)
            {
                await service.ConfirmEmailAsync(registered.Confirmation.Token, password, "boundary");
                await write.SaveChangesAsync();
            }
            else
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.ConfirmEmailAsync(registered.Confirmation.Token, password, "boundary"));
        }
        await using var read = database.Open();
        var account = await read.Accounts.SingleAsync();
        Assert.Equal(accepted, account.EmailConfirmed);
        Assert.Equal(!accepted, string.IsNullOrEmpty(account.PasswordHash));
    }
}
