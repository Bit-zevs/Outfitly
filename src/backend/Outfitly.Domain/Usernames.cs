using System.Numerics;

namespace Outfitly.Domain;

public static class Usernames
{
    public static string Normalize(string username)
    {
        var value = username?.Trim().TrimStart('@').ToLowerInvariant();
        if (value is null || value.Length is < 3 or > 30 || value[0] is < 'a' or > 'z' ||
            value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) ||
            username!.Trim().StartsWith("@@", StringComparison.Ordinal))
            throw new ArgumentException("Username must contain 3–30 Latin letters, digits or underscores and start with a letter.");
        return value;
    }

    // Fixed-width base32 of the full UUID, using its canonical hexadecimal order.
    public static string FromId(Guid id)
    {
        var number = BigInteger.Parse("0" + id.ToString("N"), System.Globalization.NumberStyles.HexNumber);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var chars = new char[26];
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            chars[i] = alphabet[(int)(number & 31)];
            number >>= 5;
        }
        return "u_" + new string(chars);
    }
}
