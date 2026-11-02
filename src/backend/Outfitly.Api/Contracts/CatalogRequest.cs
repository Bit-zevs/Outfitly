using Outfitly.Application;
using Outfitly.Domain;

namespace Outfitly.Api.Contracts;

public static class CatalogRequest
{
    public static ItemCatalogQuery Parse(IQueryCollection query, bool personal = false)
    {
        string? Value(string name)
        {
            if (!query.TryGetValue(name, out var value)) return null;
            if (value.Count != 1) throw new ArgumentException($"Repeated {name}.");
            return value[0];
        }
        int Number(string name, int fallback) => Value(name) is { } value
            ? int.TryParse(value, out var number) ? number : throw new ArgumentException($"Invalid {name}.")
            : fallback;
        bool Flag(string name) => Value(name) is { } raw ? bool.TryParse(raw, out var flag) ? flag : throw new ArgumentException($"Invalid {name}.") : false;
        ClothingCategory? category = null;
        if (Value("category") is { } raw)
        {
            if (!Enum.GetNames<ClothingCategory>().Any(name => name.Equals(raw, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Invalid category.");
            category = Enum.Parse<ClothingCategory>(raw, true);
        }
        return new(Value("search"), category, Value("color"), Value("sort") ?? "nameAsc", Number("page", 1), Number("pageSize", 20),
            personal ? Value("brand") : null, personal ? Value("size") : null, personal ? Value("publication") ?? "all" : "all",
            personal && Flag("colorMissing"), personal && Flag("brandMissing"), personal && Flag("sizeMissing"));
    }
}
