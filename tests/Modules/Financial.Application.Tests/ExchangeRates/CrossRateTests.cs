using FluentAssertions;
using Sextante.Modules.Financial.Application.ExchangeRates;

namespace Sextante.Modules.Financial.Application.Tests.ExchangeRates;

public sealed class CrossRateTests
{
    [Fact]
    public void Compute_EUR_to_X_returns_eurToRate()
    {
        var result = CrossRate.Compute("EUR", "USD", eurFromRate: null, eurToRate: 1.10m);

        result.Should().Be(1.10m);
    }

    [Fact]
    public void Compute_X_to_EUR_returns_inverse_of_eurFromRate()
    {
        var result = CrossRate.Compute("USD", "EUR", eurFromRate: 1.10m, eurToRate: null);

        result.Should().BeApproximately(1m / 1.10m, 0.0000001m);
    }

    [Fact]
    public void Compute_X_to_Y_returns_eurTo_over_eurFrom()
    {
        // EUR→USD = 1.10, EUR→BRL = 6.00 ⇒ USD→BRL = 6.00 / 1.10 ≈ 5.4545
        var result = CrossRate.Compute("USD", "BRL", eurFromRate: 1.10m, eurToRate: 6.00m);

        result.Should().BeApproximately(6.00m / 1.10m, 0.0000001m);
    }

    [Fact]
    public void Compute_throws_when_eurToRate_missing_for_EUR_to_X()
    {
        var act = () => CrossRate.Compute("EUR", "USD", eurFromRate: null, eurToRate: null);

        act.Should().Throw<ArgumentException>().WithParameterName("eurToRate");
    }

    [Fact]
    public void Compute_throws_when_eurFromRate_missing_for_X_to_EUR()
    {
        var act = () => CrossRate.Compute("USD", "EUR", eurFromRate: null, eurToRate: null);

        act.Should().Throw<ArgumentException>().WithParameterName("eurFromRate");
    }

    [Fact]
    public void Compute_throws_when_either_rate_missing_for_cross()
    {
        var actNullFrom = () => CrossRate.Compute("USD", "BRL", eurFromRate: null, eurToRate: 6.00m);
        var actNullTo = () => CrossRate.Compute("USD", "BRL", eurFromRate: 1.10m, eurToRate: null);

        actNullFrom.Should().Throw<ArgumentException>();
        actNullTo.Should().Throw<ArgumentException>();
    }
}
