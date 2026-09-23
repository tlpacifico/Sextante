using FluentAssertions;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class TransferCounterpartMatcherTests
{
    private static readonly DateOnly Date = new(2026, 9, 11);
    private static readonly IReadOnlySet<Guid> None = new HashSet<Guid>();

    private static TransferCandidate At(int deltaDays, Guid? id = null)
        => new(id ?? Guid.NewGuid(), Date.AddDays(deltaDays), null);

    [Fact]
    public void Picks_closest_date_within_window()
    {
        var far = At(3);
        var near = At(-1);

        TransferCounterpartMatcher.Pick([far, near], Date, None).Should().Be(near);
    }

    [Fact]
    public void Ignores_candidates_outside_window()
    {
        TransferCounterpartMatcher.Pick([At(8), At(-8)], Date, None).Should().BeNull();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-7)]
    public void Accepts_candidates_exactly_at_window_edge(int delta)
    {
        var edge = At(delta);

        TransferCounterpartMatcher.Pick([edge], Date, None).Should().Be(edge);
    }

    [Fact]
    public void Ignores_claimed_candidates()
    {
        var closest = At(0);
        var next = At(2);

        TransferCounterpartMatcher.Pick([closest, next], Date, new HashSet<Guid> { closest.TransactionId })
            .Should().Be(next);
    }

    [Fact]
    public void Tie_breaks_by_earlier_date()
    {
        var before = At(-2);
        var after = At(2);

        TransferCounterpartMatcher.Pick([after, before], Date, None).Should().Be(before);
    }

    [Fact]
    public void Same_date_tie_breaks_by_lower_id()
    {
        var low = At(1, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var high = At(1, Guid.Parse("00000000-0000-0000-0000-000000000002"));

        TransferCounterpartMatcher.Pick([high, low], Date, None).Should().Be(low);
    }

    [Fact]
    public void Empty_returns_null()
    {
        TransferCounterpartMatcher.Pick([], Date, None).Should().BeNull();
    }
}
