namespace Outfitly.Application;

internal static class PublicOutfitItem
{
    internal static void Require(IWardrobeRepository wardrobe, Guid? outfitId, Guid itemId, bool changing)
    {
        if (outfitId is not { } id) throw new KeyNotFoundException();
        var outfit = wardrobe.GetOutfits().SingleOrDefault(x => x.Id == id);
        if (outfit is null || !outfit.IsPublic || !outfit.ItemIds.Contains(itemId))
            throw new KeyNotFoundException();
        if (changing) wardrobe.MarkOutfitChanged(outfit);
    }
}
