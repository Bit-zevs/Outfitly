namespace Outfitly.Tests.Infrastructure;

public sealed class PostgreSqlConcurrencyTests
{
    [PostgreSqlFact]
    public async Task Concurrent_membership_and_item_deletions_preserve_publication_and_revocation()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var deleteItems in new[] { false, true })
        foreach (var reverse in new[] { false, true })
            await OutfitConcurrencyScenarios.Removals(database.Open, deleteItems, reverse);
    }

    [PostgreSqlFact]
    public async Task Link_creation_cannot_escape_last_member_revocation_in_either_commit_order()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var linkWins in new[] { false, true })
            await OutfitConcurrencyScenarios.CreateLink(database.Open, linkWins);
    }

    [PostgreSqlFact]
    public async Task Publication_cannot_make_an_empty_outfit_public_in_either_commit_order()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var publicationWins in new[] { false, true })
            await OutfitConcurrencyScenarios.Publish(database.Open, publicationWins);
    }

    [PostgreSqlFact]
    public async Task Outfit_deletion_cannot_leave_a_racing_link_active_in_either_commit_order()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        foreach (var linkWins in new[] { false, true })
            await OutfitConcurrencyScenarios.DeleteOutfit(database.Open, linkWins);
    }
}
