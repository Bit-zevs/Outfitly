namespace Outfitly.Domain;

public sealed class OutfitAssignment
{
    public Guid OwnerId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid OutfitId { get; private set; }
    private OutfitAssignment() { }
    public OutfitAssignment(Guid ownerId, DateOnly date, Guid outfitId)
    {
        OwnerId = Guard.Id(ownerId); Date = date; OutfitId = Guard.Id(outfitId);
    }
}
