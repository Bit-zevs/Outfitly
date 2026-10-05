namespace Outfitly.Application;

public sealed class WardrobeConcurrencyException(Exception innerException)
    : Exception("The wardrobe changed during this operation. Reload and repeat the command.", innerException);
