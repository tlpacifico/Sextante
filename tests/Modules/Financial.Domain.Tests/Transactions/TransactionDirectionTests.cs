using FluentAssertions;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

/// <summary>
/// Phase 6.5 §1.1 — a direção deixa de ser inferida da categoria em tempo
/// de leitura e passa a ser propriedade da transação.
/// </summary>
public sealed class TransactionDirectionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money Eur10 = new(10m, "EUR");

    [Fact]
    public void CreateRegular_with_income_category_is_inflow()
    {
        var tx = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Income,
            Now.AddDays(-1), Eur10, "salário", null, Tenant, now: Now);

        tx.Direction.Should().Be(TransactionDirection.Inflow);
        tx.Kind.Should().Be(TransactionKind.Regular);
        tx.TransferId.Should().BeNull();
        tx.SignedAmount.Should().Be(10m);
    }

    [Fact]
    public void CreateRegular_with_expense_category_is_outflow_with_negative_signed_amount()
    {
        var tx = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense,
            Now.AddDays(-1), Eur10, "supermercado", null, Tenant, now: Now);

        tx.Direction.Should().Be(TransactionDirection.Outflow);
        tx.SignedAmount.Should().Be(-10m);
    }

    [Fact]
    public void CreateRegular_rejects_empty_category()
    {
        var act = () => Transaction.CreateRegular(
            Guid.NewGuid(), Guid.Empty, CategoryKind.Expense,
            Now.AddDays(-1), Eur10, null, null, Tenant, now: Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateUncategorized_has_no_category_and_keeps_given_direction()
    {
        var tx = Transaction.CreateUncategorized(
            Guid.NewGuid(), TransactionDirection.Outflow,
            Now.AddDays(-1), Eur10, "renda", null, Tenant, now: Now);

        tx.CategoryId.Should().BeNull();
        tx.Kind.Should().Be(TransactionKind.Regular);
        tx.Direction.Should().Be(TransactionDirection.Outflow);
    }

    [Fact]
    public void SetCategory_to_opposite_kind_flips_direction()
    {
        var tx = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Expense,
            Now.AddDays(-1), Eur10, null, null, Tenant, now: Now);
        var income = Guid.NewGuid();

        tx.SetCategory(income, CategoryKind.Income);

        tx.CategoryId.Should().Be(income);
        tx.Direction.Should().Be(TransactionDirection.Inflow);
    }

    [Fact]
    public void Update_derives_direction_from_new_category_kind()
    {
        var tx = Transaction.CreateRegular(
            Guid.NewGuid(), Guid.NewGuid(), CategoryKind.Income,
            Now.AddDays(-1), Eur10, null, null, Tenant, now: Now);

        tx.Update(
            tx.AccountId, Guid.NewGuid(), CategoryKind.Expense,
            Now.AddDays(-2), new Money(20m, "EUR"), "corrigido", null, now: Now);

        tx.Direction.Should().Be(TransactionDirection.Outflow);
        tx.SignedAmount.Should().Be(-20m);
    }

    [Fact]
    public void Uncategorized_regular_can_be_categorized()
    {
        var tx = Transaction.CreateUncategorized(
            Guid.NewGuid(), TransactionDirection.Outflow,
            Now.AddDays(-1), Eur10, null, null, Tenant, now: Now);
        var category = Guid.NewGuid();

        tx.SetCategory(category, CategoryKind.Expense);

        tx.CategoryId.Should().Be(category);
        tx.Direction.Should().Be(TransactionDirection.Outflow);
    }

    [Fact]
    public void SetCategory_rejects_empty_category()
    {
        var tx = Transaction.CreateUncategorized(
            Guid.NewGuid(), TransactionDirection.Outflow,
            Now.AddDays(-1), Eur10, null, null, Tenant, now: Now);

        var act = () => tx.SetCategory(Guid.Empty, CategoryKind.Expense);

        act.Should().Throw<ArgumentException>();
    }
}
