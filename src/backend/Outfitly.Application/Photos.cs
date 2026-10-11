namespace Outfitly.Application;

public interface IPhotoStore
{
    Task<string> WriteAsync(byte[] content, string extension, CancellationToken cancellationToken);
    Task<StoredPhoto?> ReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteBestEffortAsync(string key);
}

public interface IPhotoProcessor
{
    ProcessedPhoto Process(byte[] content, string contentType);
}

public sealed record ProcessedPhoto(byte[] Content, string Extension);
public sealed record StoredPhoto(Stream Content, string ContentType);
public sealed class UnsupportedPhotoException : Exception;
public sealed class PhotoTooLargeException : Exception;
public sealed record ShareLinkView(Guid Id, string Token);

// Scoped alongside the database unit of work. File removal never precedes commit.
public sealed class PhotoChanges(IPhotoStore store)
{
    private readonly HashSet<string> created = [];
    private readonly HashSet<string> replaced = [];
    public void Created(string key) => created.Add(key);
    public void RemoveAfterCommit(string? key) { if (key is not null) replaced.Add(key); }

    public async Task CompleteAsync(bool committed)
    {
        foreach (var key in committed ? replaced : created)
            await store.DeleteBestEffortAsync(key);
        created.Clear();
        replaced.Clear();
    }
}

public sealed class PhotoService(WardrobeService wardrobe, IPhotoStore store,
    IPhotoProcessor processor, PhotoChanges changes)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public async Task UploadAsync(Guid actor, Guid id, Stream content, string contentType,
        CancellationToken cancellationToken)
    {
        var item = wardrobe.OwnedItem(actor, id);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw new PhotoTooLargeException();
            buffer.Write(chunk, 0, count);
        }
        if (buffer.Length == 0) throw new ArgumentException("Photo must not be empty.");
        var photo = processor.Process(buffer.ToArray(), contentType);
        var key = await store.WriteAsync(photo.Content, photo.Extension, cancellationToken);
        changes.Created(key);
        changes.RemoveAfterCommit(item.PhotoStorageKey);
        item.SetUploadedPhoto(key);
    }

    public void Delete(Guid actor, Guid id)
    {
        var item = wardrobe.OwnedItem(actor, id);
        changes.RemoveAfterCommit(item.PhotoStorageKey);
        item.RemovePhoto();
    }
}
