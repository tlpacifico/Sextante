using FluentAssertions;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Tests.Common;

public sealed class CursorTests
{
    [Fact]
    public void Roundtrip_preserves_values()
    {
        var cursor = new TransactionCursor(DateTimeOffset.UtcNow, Guid.NewGuid());
        var encoded = Cursor.Encode(cursor);
        var decoded = Cursor.Decode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Id.Should().Be(cursor.Id);
        decoded.OccurredAt.Should().BeCloseTo(cursor.OccurredAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Decode_returns_null_for_garbage()
    {
        Cursor.Decode("not-a-cursor").Should().BeNull();
    }

    [Fact]
    public void Decode_returns_null_for_empty()
    {
        Cursor.Decode(null).Should().BeNull();
        Cursor.Decode("").Should().BeNull();
    }
}
