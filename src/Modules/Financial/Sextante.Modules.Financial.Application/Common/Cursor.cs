using System.Text;
using System.Text.Json;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Common;

/// <summary>
/// Cursor opaco base64url para paginação forward-only. Encoded contém o
/// par <c>(occurredAt, id)</c> da última linha da página anterior.
/// Decoded valida a forma do JSON; corrupt cursors devolvem <c>null</c>.
/// </summary>
public static class Cursor
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Encode(TransactionCursor cursor)
    {
        var json = JsonSerializer.Serialize(new CursorPayload
        {
            OccurredAt = cursor.OccurredAt,
            Id = cursor.Id,
        }, Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static TransactionCursor? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            var bytes = Convert.FromBase64String(padded);
            var json = Encoding.UTF8.GetString(bytes);
            var payload = JsonSerializer.Deserialize<CursorPayload>(json, Options);
            if (payload is null || payload.Id == Guid.Empty)
            {
                return null;
            }

            return new TransactionCursor(payload.OccurredAt, payload.Id);
        }
        catch
        {
            return null;
        }
    }

    private sealed class CursorPayload
    {
        public DateTimeOffset OccurredAt { get; set; }
        public Guid Id { get; set; }
    }
}
