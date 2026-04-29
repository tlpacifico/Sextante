using FluentAssertions;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class ExchangeRateSnapshotTests
{
    [Fact]
    public void Constructor_rejects_zero_rate()
    {
        var act = () => new ExchangeRateSnapshot(0m, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_rejects_negative_rate()
    {
        var act = () => new ExchangeRateSnapshot(-1m, DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_succeeds_with_positive_rate()
    {
        var at = DateTimeOffset.UtcNow;
        var snapshot = new ExchangeRateSnapshot(5.4545m, at);
        snapshot.Rate.Should().Be(5.4545m);
        snapshot.At.Should().Be(at);
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var at = DateTimeOffset.UtcNow;
        var a = new ExchangeRateSnapshot(1.10m, at);
        var b = new ExchangeRateSnapshot(1.10m, at);
        a.Should().Be(b);
    }
}
