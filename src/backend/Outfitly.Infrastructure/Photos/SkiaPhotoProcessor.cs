using Outfitly.Application;
using SkiaSharp;
using System.Buffers.Binary;

namespace Outfitly.Infrastructure.Photos;

public sealed class SkiaPhotoProcessor : IPhotoProcessor, IAvatarProcessor
{
    private const int MaxOutputSide = 2048;
    private static readonly SemaphoreSlim ProcessingSlots = new(2);

    public ProcessedPhoto Process(byte[] content, string contentType) =>
        ProcessLimited(content, contentType, null);

    public ProcessedPhoto ProcessAvatar(byte[] content, string contentType, AvatarCrop crop) =>
        ProcessLimited(content, contentType, crop);

    private static ProcessedPhoto ProcessLimited(byte[] content, string contentType, AvatarCrop? crop)
    {
        if (!ProcessingSlots.Wait(0)) throw new PhotoProcessingBusyException();
        try { return ProcessImage(content, contentType, crop); }
        finally { ProcessingSlots.Release(); }
    }

    private static ProcessedPhoto ProcessImage(byte[] content, string contentType, AvatarCrop? crop)
    {
        contentType = contentType.ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp")) throw new UnsupportedPhotoException();
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new ArgumentException("Invalid image content.");
        var expected = contentType == "image/png" ? SKEncodedImageFormat.Png : contentType == "image/webp" ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg;
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
        var width = swapsAxes ? info.Height : info.Width;
        var height = swapsAxes ? info.Width : info.Height;
        if (crop is { } area && (area.X < 0 || area.Y < 0 || area.Size <= 0 ||
            (long)area.X + area.Size > width || (long)area.Y + area.Size > height))
            throw new ArgumentException("Crop must be a square within the oriented image.");
        var scale = Math.Min(1d, (double)MaxOutputSide / Math.Max(width, height));
        var targetWidth = crop is null ? Math.Max(1, (int)Math.Round(width * scale)) : 512;
        var targetHeight = crop is null ? Math.Max(1, (int)Math.Round(height * scale)) : 512;
        using var oriented = new SKBitmap(targetWidth, targetHeight);
        using (var canvas = new SKCanvas(oriented))
        {
            if (crop is { } square)
            {
                canvas.Scale(512f / square.Size);
                canvas.Translate(-square.X, -square.Y);
            }
            else
                canvas.Scale((float)targetWidth / width, (float)targetHeight / height);
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
            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        }
        var format = crop is not null || expected == SKEncodedImageFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var image = SKImage.FromBitmap(oriented);
        using var encoded = image.Encode(format, 90);
        if (encoded is null) throw new ArgumentException("Unable to encode image.");
        if (encoded.Size > PhotoService.MaxBytes) throw new PhotoTooLargeException();
        return new(encoded.ToArray(), format == SKEncodedImageFormat.Png ? ".png" : ".jpg");
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
