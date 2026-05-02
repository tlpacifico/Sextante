using FluentAssertions;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.Budgets;

public sealed class BudgetAlertTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid BudgetId = GuidV7.NewId();
    private static readonly Money Spent400 = new(400m, "EUR");

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Create_rejects_threshold_out_of_range(int threshold)
    {
        var act = () => BudgetAlert.Create(Tenant, BudgetId, threshold, Spent400);
        act.Should().Throw<BudgetAlertThresholdInvalidException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(100)]
    public void Create_accepts_threshold_within_range(int threshold)
    {
        var alert = BudgetAlert.Create(Tenant, BudgetId, threshold, Spent400);
        alert.Threshold.Should().Be(threshold);
    }

    [Fact]
    public void Create_rejects_negative_spent()
    {
        var act = () => BudgetAlert.Create(Tenant, BudgetId, 80, new Money(-1m, "EUR"));
        act.Should().Throw<BudgetAlertSpentNegativeException>();
    }

    [Fact]
    public void Create_initializes_with_default_state()
    {
        var alert = BudgetAlert.Create(Tenant, BudgetId, 80, Spent400);
        alert.Acknowledged.Should().BeFalse();
        alert.AcknowledgedAt.Should().BeNull();
        alert.TriggeredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Acknowledge_sets_acknowledged_and_timestamp()
    {
        var alert = BudgetAlert.Create(Tenant, BudgetId, 80, Spent400);
        alert.Acknowledge();
        alert.Acknowledged.Should().BeTrue();
        alert.AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public void Acknowledge_is_idempotent()
    {
        var alert = BudgetAlert.Create(Tenant, BudgetId, 80, Spent400);
        alert.Acknowledge();
        var firstAck = alert.AcknowledgedAt;

        alert.Acknowledge();
        alert.AcknowledgedAt.Should().Be(firstAck);
    }
}
