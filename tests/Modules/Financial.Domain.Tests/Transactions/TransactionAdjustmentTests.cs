using FluentAssertions;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class TransactionAdjustmentTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur15 = new(15m, "EUR");

    [Fact]
    public void CreateAdjustment_has_adjustment_kind_no_category_no_transfer_id()
    {
        var accountId = Guid.NewGuid();

        var adjustment = Transaction.CreateAdjustment(
            accountId, TransactionDirection.Inflow, DateTimeOffset.UtcNow,
            Eur15, "Acerto de saldo", Tenant);

        adjustment.Kind.Should().Be(TransactionKind.Adjustment);
        adjustment.CategoryId.Should().BeNull();
        adjustment.TransferId.Should().BeNull();
        adjustment.Tags.Should().BeEmpty();
        adjustment.Direction.Should().Be(TransactionDirection.Inflow);
        adjustment.AccountId.Should().Be(accountId);
        adjustment.Amount.Should().BeEquivalentTo(Eur15);
        adjustment.Description.Should().Be("Acerto de saldo");
    }

    [Fact]
    public void CreateAdjustment_rejects_non_positive_amount()
    {
        var act = () => Transaction.CreateAdjustment(
            Guid.NewGuid(), TransactionDirection.Outflow, DateTimeOffset.UtcNow,
            new Money(0m, "EUR"), null, Tenant);

        act.Should().Throw<TransactionAmountMustBePositiveException>();
    }

    [Fact]
    public void CreateAdjustment_rejects_future_date()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        var act = () => Transaction.CreateAdjustment(
            Guid.NewGuid(), TransactionDirection.Outflow, now.AddHours(1),
            Eur15, null, Tenant, now: now);

        act.Should().Throw<TransactionInFutureException>();
    }

    [Fact]
    public void Adjustment_can_be_archived()
    {
        var adjustment = Transaction.CreateAdjustment(
            Guid.NewGuid(), TransactionDirection.Outflow, DateTimeOffset.UtcNow, Eur15, null, Tenant);

        adjustment.Archive();

        adjustment.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Adjustment_cannot_be_updated_or_recategorized()
    {
        var adjustment = Transaction.CreateAdjustment(
            Guid.NewGuid(), TransactionDirection.Outflow, DateTimeOffset.UtcNow, Eur15, null, Tenant);

        var update = () => adjustment.Update(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense, DateTimeOffset.UtcNow,
            Eur15, null, null);
        var recategorize = () => adjustment.SetCategory(Guid.NewGuid(), CategoryKind.Expense);

        update.Should().Throw<TransactionNotRegularException>();
        recategorize.Should().Throw<TransactionNotRegularException>();
    }

    [Theory]
    [InlineData(TransactionDirection.Outflow, 150, -150)]
    [InlineData(TransactionDirection.Inflow, 100, 100)]
    public void Adjustment_signed_amount_follows_direction(TransactionDirection direction, decimal amount, decimal expected)
    {
        var adjustment = Transaction.CreateAdjustment(
            Guid.NewGuid(), direction, DateTimeOffset.UtcNow, new Money(amount, "EUR"), null, Tenant);

        adjustment.SignedAmount.Should().Be(expected);
    }
}
