using FluentAssertions;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.TransactionsSpec;

public sealed class Transaction_ExchangeRateSnapshot_Tests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Create_with_snapshot_persists_rate_and_at()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-1);
        var snapshot = new ExchangeRateSnapshot(5.4545m, DateTimeOffset.UtcNow);

        var transaction = Transaction.Create(
            AccountId,
            CategoryId,
            occurredAt,
            new Money(300m, "USD"),
            null, null, Tenant, snapshot);

        transaction.ExchangeRateToPrimary.Should().Be(5.4545m);
        transaction.ExchangeRateAt.Should().Be(snapshot.At);
    }

    [Fact]
    public void Create_without_snapshot_leaves_rate_null()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-1);

        var transaction = Transaction.Create(
            AccountId,
            CategoryId,
            occurredAt,
            new Money(200m, "EUR"),
            null, null, Tenant, exchangeRate: null);

        transaction.ExchangeRateToPrimary.Should().BeNull();
        transaction.ExchangeRateAt.Should().BeNull();
    }

    [Fact]
    public void Update_does_not_recompute_exchange_rate()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var originalSnapshot = new ExchangeRateSnapshot(1.10m, DateTimeOffset.UtcNow.AddDays(-2));

        var transaction = Transaction.Create(
            AccountId,
            CategoryId,
            occurredAt,
            new Money(100m, "USD"),
            null, null, Tenant, originalSnapshot);

        transaction.Update(
            AccountId,
            CategoryId,
            occurredAt,
            new Money(150m, "USD"),
            null, null);

        transaction.Amount.Amount.Should().Be(150m);
        transaction.ExchangeRateToPrimary.Should().Be(1.10m);
        transaction.ExchangeRateAt.Should().Be(originalSnapshot.At);
    }
}
