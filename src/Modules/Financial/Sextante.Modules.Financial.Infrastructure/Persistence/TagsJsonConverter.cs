using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

/// <summary>
/// Converte <c>List&lt;string&gt;</c> de tags para <c>jsonb</c> e vice-versa.
/// Default DB é <c>'[]'</c>; null colapsa para lista vazia.
/// </summary>
public sealed class TagsJsonConverter : ValueConverter<List<string>, string>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public TagsJsonConverter()
        : base(
            list => Serialize(list),
            json => Deserialize(json))
    {
    }

    private static string Serialize(List<string> list)
        => JsonSerializer.Serialize(list ?? new List<string>(), Options);

    private static List<string> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        return JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>();
    }
}
