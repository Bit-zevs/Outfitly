using Outfitly.Application;
using SkiaSharp;
using System.Buffers.Binary;

namespace Outfitly.Infrastructure.Photos;

public sealed class SkiaPhotoProcessor : IPhotoProcessor, IAvatarProcessor
{
    public ProcessedPhoto Process(byte[] content, string contentType)
    {
        contentType = contentType.ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png")) throw new UnsupportedPhotoException();
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new ArgumentException("Invalid image content.");
        var expected = contentType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        if (codec.EncodedFormat != expected) throw new UnsupportedPhotoException();
        if (expected == SKEncodedImageFormat.Png) ValidatePngChunks(content);
        var info = codec.Info;
        if (info.Width is < 1 or > 8192 || info.Height is < 1 or > 8192 || (long)info.Width * info.Height > 25_000_000)
            throw new ArgumentException("Image exceeds 8192 pixels per side or 25 megapixels.");
        if (codec.FrameCount > 1) throw new ArgumentException("Animated photos are not supported.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException("Incomplete or invalid image content.");

        // Apply EXIF orientation before encoding; do not copy EXIF or other input metadata.
        var origin = codec.EncodedOrigin;
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var oriented = new SKBitmap(swapsAxes ? info.Height : info.Width, swapsAxes ? info.Width : info.Height);
        using (var canvas = new SKCanvas(oriented))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(info.Width, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(info.Width, info.Height); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, info.Height); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(info.Height, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(info.Height, info.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, info.Width); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions());
        }
        using var image = SKImage.FromBitmap(oriented);
        using var encoded = image.Encode(expected, 90);
        return new(encoded.ToArray(), expected == SKEncodedImageFormat.Png ? ".png" : ".jpg");
    }

    public ProcessedPhoto ProcessAvatar(byte[] content, string contentType, AvatarCrop crop)
    {
        var normalized = Process(content, contentType);
        using var bitmap = SKBitmap.Decode(normalized.Content);
        if (crop.X < 0 || crop.Y < 0 || crop.Size <= 0 || (long)crop.X + crop.Size > bitmap.Width || (long)crop.Y + crop.Size > bitmap.Height)
            throw new ArgumentException("Crop must be a square within the oriented image.");
        using var result = new SKBitmap(512, 512);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, new SKRect(crop.X, crop.Y, crop.X + crop.Size, crop.Y + crop.Size),
                new SKRect(0, 0, 512, 512), new SKSamplingOptions(SKFilterMode.Linear));
        }
        using var image = SKImage.FromBitmap(result);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(encoded.ToArray(), ".png");
    }

    private static void ValidatePngChunks(byte[] content)
    {
        // Some PNG decoders ignore APNG ancillary chunks and report one frame.
        // Inspect chunk boundaries rather than searching pixel data for a marker.
        var offset = 8;
        while (offset <= content.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(offset, 4));
            if (length > (uint)(content.Length - offset - 12)) throw new ArgumentException("Incomplete PNG chunk.");
            var type = content.AsSpan(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8)) throw new ArgumentException("Animated photos are not supported.");
            if (type.SequenceEqual("IEND"u8)) return;
            offset += (int)length + 12;
        }
        throw new ArgumentException("PNG end chunk is missing.");
    }
}
