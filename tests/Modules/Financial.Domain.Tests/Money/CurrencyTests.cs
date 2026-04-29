using FluentAssertions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.MoneySpec;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("EUR", true)]
    [InlineData("USD", true)]
    [InlineData("BRL", true)]
    [InlineData("eur", false)]
    [InlineData("EU", false)]
    [InlineData("EURO", false)]
    [InlineData("123", false)]
    public void IsValidCode_matches_iso_4217_format(string code, bool expected)
    {
        Currency.IsValidCode(code).Should().Be(expected);
    }

    [Fact]
    public void Create_succeeds_with_valid_inputs()
    {
        var currency = Currency.Create("USD", "US Dollar", "$", 2);
        currency.Code.Should().Be("USD");
        currency.Name.Should().Be("US Dollar");
        currency.Symbol.Should().Be("$");
        currency.MinorUnits.Should().Be(2);
        currency.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_lowercase_code()
    {
        var act = () => Currency.Create("usd", "US Dollar", "$", 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_name()
    {
        var act = () => Currency.Create("USD", "", "$", 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_negative_minor_units()
    {
        var act = () => Currency.Create("USD", "US Dollar", "$", -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_rejects_minor_units_above_six()
    {
        var act = () => Currency.Create("USD", "US Dollar", "$", 7);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Update_changes_metadata_keeping_code()
    {
        var currency = Currency.Create("USD", "US Dollar", "$", 2);
        currency.Update("Dólar", "$$", 4, false);
        currency.Code.Should().Be("USD");
        currency.Name.Should().Be("Dólar");
        currency.Symbol.Should().Be("$$");
        currency.MinorUnits.Should().Be(4);
        currency.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_and_Deactivate_toggle_flag()
    {
        var currency = Currency.Create("USD", "US Dollar", "$", 2, isActive: false);
        currency.Activate();
        currency.IsActive.Should().BeTrue();
        currency.Deactivate();
        currency.IsActive.Should().BeFalse();
    }
}
