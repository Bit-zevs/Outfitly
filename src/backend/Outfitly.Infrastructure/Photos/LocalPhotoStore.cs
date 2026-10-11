using Microsoft.Extensions.Logging;
using Outfitly.Application;

namespace Outfitly.Infrastructure.Photos;

public sealed class LocalPhotoStore(string root, ILogger<LocalPhotoStore> logger) : IPhotoStore
{
    private string PathFor(string key)
    {
        var extension = Path.GetExtension(key);
        if (extension is not (".jpg" or ".png") || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(key), "N", out _)
            || Path.GetFileName(key) != key)
            throw new ArgumentException("Invalid photo key.");
        return Path.Combine(root, key);
    }

    public async Task<string> WriteAsync(byte[] content, string extension, CancellationToken cancellationToken)
    {
        var key = $"{Guid.NewGuid():N}{extension}";
        var path = PathFor(key);
        Directory.CreateDirectory(root);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            await file.WriteAsync(content, cancellationToken);
        }
        catch
        {
            await DeleteBestEffortAsync(key);
            throw;
        }
        return key;
    }

    public Task<StoredPhoto?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Stream file = new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<StoredPhoto?>(new(file, key.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg"));
        }
        catch (FileNotFoundException) { return Task.FromResult<StoredPhoto?>(null); }
        catch (DirectoryNotFoundException) { return Task.FromResult<StoredPhoto?>(null); }
    }

    public Task DeleteBestEffortAsync(string key)
    {
        try { File.Delete(PathFor(key)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { logger.LogWarning(exception, "Could not remove unused photo {PhotoKey}", key); }
        return Task.CompletedTask;
    }
}
