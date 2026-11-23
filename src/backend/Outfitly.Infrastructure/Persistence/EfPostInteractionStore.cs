using Microsoft.EntityFrameworkCore;
using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfPostInteractionStore(OutfitlyDbContext db) : IPostInteractionStore
{
    public IQueryable<PostInteraction> Interactions => db.Set<PostInteraction>();
    public IQueryable<PostInteraction> ReadInteractions => db.Set<PostInteraction>().AsNoTracking();
    public User FindUser(Guid id) => db.Users.AsNoTracking().Single(x => x.Id == id);
    public void Add(PostInteraction interaction) => db.Add(interaction);
    public void Remove(PostInteraction interaction) => db.Remove(interaction);
}
