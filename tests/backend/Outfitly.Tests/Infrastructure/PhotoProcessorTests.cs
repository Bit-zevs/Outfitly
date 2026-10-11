using Outfitly.Infrastructure.Photos;
using SkiaSharp;

namespace Outfitly.Tests.Infrastructure;

public sealed class PhotoProcessorTests
{
    [Fact]
    public void Png_animation_control_is_rejected_even_when_the_decoder_ignores_animation()
    {
        using var bitmap = new SKBitmap(2, 3);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        // Insert an APNG animation control chunk after IHDR (2 frames, no loop limit).
        byte[] chunk = [0, 0, 0, 8, 97, 99, 84, 76, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0];
        uint crc = 0xffffffff;
        foreach (var value in chunk.AsSpan(4, 12))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(16), ~crc);
        var input = png[..33].Concat(chunk).Concat(png[33..]).ToArray();
        var error = Assert.Throws<ArgumentException>(() => new SkiaPhotoProcessor().Process(input, "image/png"));
        Assert.Contains("Animated", error.Message);
    }

    [Fact]
    public void Jpeg_orientation_is_applied_and_original_EXIF_is_removed()
    {
        using var bitmap = new SKBitmap(2, 3);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();
        // Independent EXIF fixture: little-endian TIFF with Orientation=6 (90 degrees).
        byte[] exif = [0xFF, 0xE1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var input = jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray();
        var result = new SkiaPhotoProcessor().Process(input, "image/jpeg");
        using var decoded = SKBitmap.Decode(result.Content);
        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.DoesNotContain("Exif", System.Text.Encoding.Latin1.GetString(result.Content));
    }
}
