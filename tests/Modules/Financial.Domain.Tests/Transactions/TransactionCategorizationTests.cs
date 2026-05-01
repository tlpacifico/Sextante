using FluentAssertions;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class TransactionCategorizationTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur10 = new(10m, "EUR");

    [Fact]
    public void Newly_created_transaction_has_no_categorization_audit()
    {
        var tx = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            Eur10, "café", null, Tenant);

        tx.CategorizationRuleId.Should().BeNull();
        tx.CategorizedAt.Should().BeNull();
    }

    [Fact]
    public void MarkCategorizedByRule_records_rule_id_and_timestamp()
    {
        var tx = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            Eur10, "café", null, Tenant);

        var ruleId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;
        tx.MarkCategorizedByRule(ruleId);
        var after = DateTimeOffset.UtcNow;

        tx.CategorizationRuleId.Should().Be(ruleId);
        tx.CategorizedAt.Should().NotBeNull();
        tx.CategorizedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void SetCategory_replaces_category_without_clearing_rule_audit()
    {
        var tx = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            Eur10, "café", null, Tenant);
        var ruleId = Guid.NewGuid();
        tx.MarkCategorizedByRule(ruleId);

        var newCategory = Guid.NewGuid();
        tx.SetCategory(newCategory);

        tx.CategoryId.Should().Be(newCategory);
        tx.CategorizationRuleId.Should().Be(ruleId);
        tx.CategorizedAt.Should().NotBeNull();
    }

    [Fact]
    public void Update_does_not_alter_categorization_audit()
    {
        var tx = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            Eur10, "café", null, Tenant);
        var ruleId = Guid.NewGuid();
        tx.MarkCategorizedByRule(ruleId);
        var capturedAt = tx.CategorizedAt;

        tx.Update(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-2),
            new Money(20m, "EUR"), "outro", null);

        tx.CategorizationRuleId.Should().Be(ruleId);
        tx.CategorizedAt.Should().Be(capturedAt);
    }
}
