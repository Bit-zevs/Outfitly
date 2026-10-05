using System.Text.Json;
using System.Text.Json.Serialization;
using Outfitly.Domain;

namespace Outfitly.Api.Contracts;

public sealed class ClothingCategoryJsonConverter : JsonConverter<ClothingCategory>
{
    public override ClothingCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            foreach (var category in Enum.GetValues<ClothingCategory>())
                if (string.Equals(name, category.ToString(), StringComparison.OrdinalIgnoreCase))
                    return category;
        }
        throw new JsonException("Category must be a single clothing category name.");
    }

    public override void Write(Utf8JsonWriter writer, ClothingCategory value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
