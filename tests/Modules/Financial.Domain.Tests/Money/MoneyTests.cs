using FluentAssertions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.MoneySpec;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_rejects_lowercase_currency()
    {
        var act = () => new Money(1m, "eur");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_rejects_currency_with_wrong_length()
    {
        var act = () => new Money(1m, "EU");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_throws_when_currencies_differ()
    {
        var eur = new Money(10m, "EUR");
        var usd = new Money(10m, "USD");

        var act = () => eur.Add(usd);
        act.Should().Throw<MoneyCurrencyMismatchException>();
    }

    [Fact]
    public void Add_returns_sum_when_currencies_match()
    {
        var a = new Money(10m, "EUR");
        var b = new Money(2.5m, "EUR");

        a.Add(b).Should().BeEquivalentTo(new Money(12.5m, "EUR"));
    }

    [Fact]
    public void Multiply_scales_amount()
    {
        var m = new Money(10m, "EUR").Multiply(1.5m);
        m.Amount.Should().Be(15m);
        m.Currency.Should().Be("EUR");
    }

    [Fact]
    public void ConvertTo_uses_target_currency()
    {
        var m = new Money(10m, "EUR").ConvertTo("USD", 1.1m);
        m.Amount.Should().Be(11m);
        m.Currency.Should().Be("USD");
    }
}
