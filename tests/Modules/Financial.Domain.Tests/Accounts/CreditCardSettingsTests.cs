using FluentAssertions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.AccountsSpec;

public sealed class CreditCardSettingsTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Limit = new(2000m, "EUR");

    private static Account NewCard(decimal openingBalance = -500m)
        => Account.Create("Cartão", AccountType.CreditCard, "EUR", new Money(openingBalance, "EUR"), Tenant);

    [Fact]
    public void ConfigureCreditCard_sets_all_fields()
    {
        var card = NewCard();
        var paymentAccountId = Guid.NewGuid();

        card.ConfigureCreditCard(Limit, 20, 10, paymentAccountId);

        card.CreditCard.Should().NotBeNull();
        card.CreditCard!.CreditLimit.Should().Be(Limit);
        card.CreditCard.StatementClosingDay.Should().Be(20);
        card.CreditCard.PaymentDueDay.Should().Be(10);
        card.CreditCard.PaymentAccountId.Should().Be(paymentAccountId);
    }

    [Fact]
    public void ConfigureCreditCard_on_checking_account_throws()
    {
        var checking = Account.Create("Conta", AccountType.Checking, "EUR", new Money(0m, "EUR"), Tenant);

        var act = () => checking.ConfigureCreditCard(Limit, 20, 10, null);

        act.Should().Throw<CreditCardSettingsRequireCreditCardException>();
    }

    [Fact]
    public void ConfigureCreditCard_rejects_other_currency()
    {
        var act = () => NewCard().ConfigureCreditCard(new Money(2000m, "USD"), 20, 10, null);

        act.Should().Throw<CreditLimitCurrencyMismatchException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfigureCreditCard_rejects_non_positive_limit(decimal limit)
    {
        var act = () => NewCard().ConfigureCreditCard(new Money(limit, "EUR"), 20, 10, null);

        act.Should().Throw<CreditLimitMustBePositiveException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(32, 10)]
    [InlineData(20, 0)]
    [InlineData(20, 32)]
    public void ConfigureCreditCard_rejects_days_out_of_range(int closingDay, int dueDay)
    {
        var act = () => NewCard().ConfigureCreditCard(Limit, closingDay, dueDay, null);

        act.Should().Throw<CreditCardDayOutOfRangeException>();
    }

    [Fact]
    public void ConfigureCreditCard_rejects_itself_as_payment_account()
    {
        var card = NewCard();

        var act = () => card.ConfigureCreditCard(Limit, 20, 10, card.Id);

        act.Should().Throw<CreditCardPaymentAccountInvalidException>();
    }

    [Fact]
    public void ConfigureCreditCard_twice_replaces_settings()
    {
        var card = NewCard();
        card.ConfigureCreditCard(Limit, 20, 10, Guid.NewGuid());

        card.ConfigureCreditCard(new Money(3000m, "EUR"), 5, 25, null);

        card.CreditCard!.CreditLimit.Amount.Should().Be(3000m);
        card.CreditCard.StatementClosingDay.Should().Be(5);
        card.CreditCard.PaymentDueDay.Should().Be(25);
        card.CreditCard.PaymentAccountId.Should().BeNull();
    }

    [Fact]
    public void ChangeType_away_from_credit_card_clears_settings()
    {
        var card = NewCard(openingBalance: 0m);
        card.ConfigureCreditCard(Limit, 20, 10, null);

        card.ChangeType(AccountType.Checking);

        card.CreditCard.Should().BeNull();
    }

    [Fact]
    public void ChangeType_to_credit_card_keeps_settings_empty()
    {
        var card = NewCard();
        card.ConfigureCreditCard(Limit, 20, 10, null);

        card.ChangeType(AccountType.CreditCard);

        card.CreditCard.Should().NotBeNull();
    }

    [Fact]
    public void RemoveCreditCardSettings_clears()
    {
        var card = NewCard();
        card.ConfigureCreditCard(Limit, 20, 10, null);

        card.RemoveCreditCardSettings();

        card.CreditCard.Should().BeNull();
    }
}
