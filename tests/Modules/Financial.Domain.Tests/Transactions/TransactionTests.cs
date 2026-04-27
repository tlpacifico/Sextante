using FluentAssertions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class TransactionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur10 = new(10m, "EUR");

    [Fact]
    public void Create_rejects_zero_amount()
    {
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new Money(0m, "EUR"), null, null, Tenant);
        act.Should().Throw<TransactionAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_negative_amount()
    {
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new Money(-1m, "EUR"), null, null, Tenant);
        act.Should().Throw<TransactionAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_future_occurredAt()
    {
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), future,
            Eur10, null, null, Tenant);
        act.Should().Throw<TransactionInFutureException>();
    }

    [Fact]
    public void Create_rejects_duplicate_tags()
    {
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Eur10, null, new[] { "x", "x" }, Tenant);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_more_than_10_tags()
    {
        var tags = Enumerable.Range(0, 11).Select(i => $"t{i}").ToArray();
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Eur10, null, tags, Tenant);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_tag_over_50_chars()
    {
        var bigTag = new string('x', 51);
        var act = () => Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Eur10, null, new[] { bigTag }, Tenant);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_succeeds_with_valid_inputs()
    {
        var t = Transaction.Create(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            Eur10, "Mercado", new[] { "alimentação" }, Tenant);
        t.Amount.Should().BeEquivalentTo(Eur10);
        t.Description.Should().Be("Mercado");
        t.Tags.Should().ContainSingle().Which.Should().Be("alimentação");
    }
}
