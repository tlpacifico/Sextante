using FluentAssertions;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class TransactionTransferTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur10 = new(10m, "EUR");

    [Fact]
    public void CreateTransferLeg_has_no_category_and_the_given_direction()
    {
        var accountId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var leg = Transaction.CreateTransferLeg(
            accountId, transferId, TransactionDirection.Outflow, now,
            Eur10, "Transferência", Tenant);

        leg.Kind.Should().Be(TransactionKind.Transfer);
        leg.CategoryId.Should().BeNull();
        leg.Direction.Should().Be(TransactionDirection.Outflow);
        leg.TransferId.Should().Be(transferId);
        leg.AccountId.Should().Be(accountId);
        leg.Amount.Should().BeEquivalentTo(Eur10);
        leg.Description.Should().Be("Transferência");
        leg.Tags.Should().BeEmpty();
    }

    [Fact]
    public void CreateTransferLeg_rejects_empty_transfer_id()
    {
        var act = () => Transaction.CreateTransferLeg(
            Guid.NewGuid(), Guid.Empty, TransactionDirection.Outflow, DateTimeOffset.UtcNow,
            Eur10, null, Tenant);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("transferId")
            .WithMessage("*TransferId obrigatório*");
    }

    [Fact]
    public void UpdateTransferLeg_on_regular_transaction_throws()
    {
        var regular = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense, DateTimeOffset.UtcNow,
            Eur10, null, null, Tenant);

        var act = () => regular.UpdateTransferLeg(
            Guid.NewGuid(), DateTimeOffset.UtcNow, Eur10, "new desc");

        act.Should().Throw<TransactionNotTransferLegException>();
    }

    [Fact]
    public void UpdateTransferLeg_changes_account_amount_date_description_only()
    {
        var transferId = Guid.NewGuid();
        var leg = Transaction.CreateTransferLeg(
            Guid.NewGuid(), transferId, TransactionDirection.Outflow, DateTimeOffset.UtcNow,
            Eur10, "old desc", Tenant);

        var originalDirection = leg.Direction;
        var originalCategoryId = leg.CategoryId;
        var originalKind = leg.Kind;
        var originalTransferId = leg.TransferId;

        var newAccountId = Guid.NewGuid();
        var newDate = DateTimeOffset.UtcNow.AddDays(-1);
        var newAmount = new Money(20m, "EUR");
        var newDescription = "new desc";

        leg.UpdateTransferLeg(newAccountId, newDate, newAmount, newDescription);

        leg.AccountId.Should().Be(newAccountId);
        leg.OccurredAt.Should().Be(newDate);
        leg.Amount.Should().BeEquivalentTo(newAmount);
        leg.Description.Should().Be(newDescription);

        leg.Direction.Should().Be(originalDirection);
        leg.CategoryId.Should().Be(originalCategoryId);
        leg.Kind.Should().Be(originalKind);
        leg.TransferId.Should().Be(originalTransferId);
    }

    [Fact]
    public void ArchiveTransferLeg_soft_deletes_a_transfer_leg()
    {
        var leg = Transaction.CreateTransferLeg(
            Guid.NewGuid(), Guid.NewGuid(), TransactionDirection.Inflow, DateTimeOffset.UtcNow,
            Eur10, null, Tenant);

        leg.DeletedAt.Should().BeNull();

        leg.ArchiveTransferLeg();

        leg.DeletedAt.Should().NotBeNull();
        leg.DeletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ArchiveTransferLeg_on_regular_transaction_throws()
    {
        var regular = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense, DateTimeOffset.UtcNow,
            Eur10, null, null, Tenant);

        var act = () => regular.ArchiveTransferLeg();

        act.Should().Throw<TransactionNotTransferLegException>();
    }

    [Fact]
    public void Archive_on_transfer_leg_throws_TransactionIsTransferLegException()
    {
        var leg = Transaction.CreateTransferLeg(
            Guid.NewGuid(), Guid.NewGuid(), TransactionDirection.Outflow, DateTimeOffset.UtcNow,
            Eur10, null, Tenant);

        var act = () => leg.Archive();

        act.Should().Throw<TransactionIsTransferLegException>();
    }

    [Fact]
    public void Archive_on_regular_transaction_still_works()
    {
        var regular = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense, DateTimeOffset.UtcNow,
            Eur10, null, null, Tenant);

        regular.DeletedAt.Should().BeNull();

        regular.Archive();

        regular.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ConvertToTransferLeg_on_regular_transaction_clears_category_and_sets_kind()
    {
        var regular = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense, DateTimeOffset.UtcNow,
            Eur10, "desc", null, Tenant);

        var originalDirection = regular.Direction;
        var originalAmount = regular.Amount;
        var originalAccountId = regular.AccountId;
        var originalOccurredAt = regular.OccurredAt;

        var transferId = Guid.NewGuid();
        regular.ConvertToTransferLeg(transferId);

        regular.Kind.Should().Be(TransactionKind.Transfer);
        regular.CategoryId.Should().BeNull();
        regular.TransferId.Should().Be(transferId);
        regular.CategorizationRuleId.Should().BeNull();
        regular.CategorizedAt.Should().BeNull();

        regular.Direction.Should().Be(originalDirection);
        regular.Amount.Should().BeEquivalentTo(originalAmount);
        regular.AccountId.Should().Be(originalAccountId);
        regular.OccurredAt.Should().Be(originalOccurredAt);
    }

    [Fact]
    public void ConvertToTransferLeg_on_already_transfer_leg_throws()
    {
        var leg = Transaction.CreateTransferLeg(
            Guid.NewGuid(), Guid.NewGuid(), TransactionDirection.Outflow, DateTimeOffset.UtcNow,
            Eur10, null, Tenant);

        var act = () => leg.ConvertToTransferLeg(Guid.NewGuid());

        act.Should().Throw<TransactionNotRegularException>();
    }

    [Fact]
    public void New_transactions_are_not_statement_confirmed_until_marked()
    {
        // Revisão da phase (C1) — só uma perna que nenhum extrato da própria
        // conta confirmou pode ser "já registada" por uma linha importada.
        var leg = Transaction.CreateTransferLeg(
            Guid.NewGuid(), Guid.NewGuid(), TransactionDirection.Inflow, DateTimeOffset.UtcNow.AddDays(-1),
            Eur10, "Transferência", Tenant);

        leg.StatementConfirmed.Should().BeFalse();

        leg.MarkStatementConfirmed();

        leg.StatementConfirmed.Should().BeTrue();
    }
}
