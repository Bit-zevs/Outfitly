using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfPostInteractionStore(OutfitlyDbContext db) : IPostInteractionStore
{
    public IQueryable<PostInteraction> Interactions => db.Set<PostInteraction>();
    public User FindUser(Guid id) => db.Users.Single(x => x.Id == id);
    public void Add(PostInteraction interaction) => db.Add(interaction);
    public void Remove(PostInteraction interaction) => db.Remove(interaction);
}
