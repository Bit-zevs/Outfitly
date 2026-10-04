namespace Outfitly.Domain;

internal static class Guard
{
    internal static Guid Id(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identifier must not be empty.", nameof(value));
        return value;
    }

    internal static string Name(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    internal static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? PhotoUrl(string? value)
    {
        var url = Optional(value);
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new ArgumentException("Photo URL must be an absolute HTTP or HTTPS URL.", nameof(value));
        return url;
    }
}
