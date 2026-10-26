using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Outfitly.Application;
using Outfitly.Domain;
namespace Outfitly.Infrastructure.Persistence;
public sealed class EfWorkspaceStore(OutfitlyDbContext db) : IWorkspaceStore
{
    public IQueryable<Favorite> Favorites => db.Set<Favorite>();
    public IQueryable<EditorDraft> Drafts => db.Set<EditorDraft>();
    public IQueryable<CopyReceipt> Copies => db.Set<CopyReceipt>();
    public void Add(Favorite x) => db.Add(x);
    public void Add(EditorDraft x) => db.Add(x);
    public void Add(CopyReceipt x) => db.Add(x);
    public void Remove(Favorite x) => db.Remove(x);
    public void Remove(EditorDraft x) => db.Remove(x);
}
internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Favorite>, IEntityTypeConfiguration<EditorDraft>, IEntityTypeConfiguration<CopyReceipt>
{
    public void Configure(EntityTypeBuilder<Favorite> b)
    {
        b.ToTable("Favorites", t => t.HasCheckConstraint("CK_Favorites_Kind", "\"Kind\" IN ('items', 'outfits')")); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.UserId, x.Kind, x.TargetId }).IsUnique(); b.HasIndex(x => new { x.UserId, x.Kind, x.CreatedAtUtc, x.Id });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<EditorDraft> b)
    {
        b.ToTable("EditorDrafts", t => t.HasCheckConstraint("CK_EditorDrafts_Kind", "\"Kind\" IN ('items', 'outfits')")); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Version).IsConcurrencyToken(); b.Property(x => x.Data).HasMaxLength(65536); b.Property(x => x.BaseVersion).HasMaxLength(128);
        b.HasIndex(x => new { x.UserId, x.ActiveSlot }).IsUnique(); b.HasIndex(x => new { x.UserId, x.UpdatedAtUtc, x.Id });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<CopyReceipt> b)
    {
        b.ToTable("CopyReceipts"); b.HasKey(x => new { x.UserId, x.OperationId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
