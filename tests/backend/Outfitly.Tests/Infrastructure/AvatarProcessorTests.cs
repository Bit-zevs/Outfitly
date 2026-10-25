using Outfitly.Application;
using Outfitly.Infrastructure.Photos;
using SkiaSharp;
namespace Outfitly.Tests.Infrastructure;
public sealed class AvatarProcessorTests
{
    [Fact]
    public void Crop_uses_oriented_coordinates_and_reencodes_without_EXIF()
    {
        using var bitmap = new SKBitmap(20, 30); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();
        byte[] exif = [0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var input = jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray();
        // x=20 is valid only after the original 20x30 image becomes 30x20.
        var result = new SkiaPhotoProcessor().ProcessAvatar(input, "image/jpeg", new(20, 0, 10));
        using var output = SKBitmap.Decode(result.Content);
        Assert.Equal(512, output.Width); Assert.Equal(512, output.Height);
        Assert.True(output.GetPixel(256, 256).Blue > 240);
        Assert.DoesNotContain("Exif", System.Text.Encoding.Latin1.GetString(result.Content));
        Assert.Throws<ArgumentException>(() => new SkiaPhotoProcessor().ProcessAvatar(input, "image/jpeg", new(0, 20, 10)));
    }
    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 21)]
    [InlineData(int.MaxValue, 0, int.MaxValue)]
    public void Invalid_crop_is_rejected(int x, int y, int size)
    {
        using var bitmap = new SKBitmap(20, 20); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        Assert.Throws<ArgumentException>(() => new SkiaPhotoProcessor().ProcessAvatar(encoded.ToArray(), "image/png", new(x, y, size)));
    }
}
