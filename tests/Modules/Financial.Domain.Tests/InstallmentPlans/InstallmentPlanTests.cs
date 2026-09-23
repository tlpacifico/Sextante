using System.Globalization;
using FluentAssertions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.InstallmentPlansSpec;

public sealed class InstallmentPlanTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid AccountId = Guid.NewGuid();

    private static DateOnly D(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static InstallmentPlan Plan(
        decimal total = 1200m,
        int count = 12,
        int alreadyPaid = 0,
        string first = "2026-04-15",
        string description = "Portátil",
        decimal? annualRate = null,
        string currency = "EUR")
        => InstallmentPlan.Create(
            Tenant, AccountId, null, description, new Money(total, currency),
            count, alreadyPaid, D(first), annualRate);

    [Fact]
    public void Create_stores_fields()
    {
        var purchaseId = Guid.NewGuid();

        var plan = InstallmentPlan.Create(
            Tenant, AccountId, purchaseId, "  Portátil  ", new Money(1200m, "EUR"), 12, 2, D("2026-04-15"), 5.5m);

        plan.Id.Should().NotBeEmpty();
        plan.TenantId.Should().Be(Tenant);
        plan.AccountId.Should().Be(AccountId);
        plan.PurchaseTransactionId.Should().Be(purchaseId);
        plan.Description.Should().Be("Portátil");
        plan.TotalAmount.Should().Be(new Money(1200m, "EUR"));
        plan.InstallmentCount.Should().Be(12);
        plan.InstallmentsAlreadyPaid.Should().Be(2);
        plan.FirstInstallmentDate.Should().Be(D("2026-04-15"));
        plan.AnnualRate.Should().Be(5.5m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Description_is_required(string description)
    {
        var act = () => Plan(description: description);

        act.Should().Throw<InstallmentPlanDescriptionRequiredException>();
    }

    [Fact]
    public void Description_max_length()
    {
        var act = () => Plan(description: new string('x', InstallmentPlan.DescriptionMaxLength + 1));

        act.Should().Throw<InstallmentPlanDescriptionRequiredException>();
    }

    [Fact]
    public void Total_must_be_positive()
    {
        var act = () => Plan(total: 0m);

        act.Should().Throw<InstallmentPlanTotalMustBePositiveException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(121)]
    public void Count_between_2_and_120(int count)
    {
        var act = () => Plan(count: count);

        act.Should().Throw<InstallmentPlanCountOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void Already_paid_between_0_and_count_minus_1(int alreadyPaid)
    {
        var act = () => Plan(alreadyPaid: alreadyPaid);

        act.Should().Throw<InstallmentPlanAlreadyPaidOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Annual_rate_between_0_and_100(double rate)
    {
        var act = () => Plan(annualRate: (decimal)rate);

        act.Should().Throw<InstallmentPlanAnnualRateOutOfRangeException>();
    }

    [Fact]
    public void Update_rejects_other_currency()
    {
        var plan = Plan();

        var act = () => plan.Update(null, "Portátil", new Money(1200m, "USD"), 12, 0, D("2026-04-15"), null);

        act.Should().Throw<InstallmentPlanCurrencyMismatchException>();
    }

    [Fact]
    public void Update_changes_fields()
    {
        var plan = Plan();
        var purchaseId = Guid.NewGuid();

        plan.Update(purchaseId, "Telemóvel", new Money(600m, "EUR"), 6, 1, D("2026-05-01"), 3m);

        plan.PurchaseTransactionId.Should().Be(purchaseId);
        plan.Description.Should().Be("Telemóvel");
        plan.TotalAmount.Amount.Should().Be(600m);
        plan.InstallmentCount.Should().Be(6);
        plan.InstallmentsAlreadyPaid.Should().Be(1);
        plan.FirstInstallmentDate.Should().Be(D("2026-05-01"));
        plan.AnnualRate.Should().Be(3m);
        plan.AccountId.Should().Be(AccountId);
    }

    [Fact]
    public void Archive_sets_deleted_at()
    {
        var plan = Plan();

        plan.Archive();

        plan.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Plan_in_the_middle_counts_past_dates()
    {
        // 12 × 100 a partir de 15/04; hoje 23/09 → 6 datas passadas.
        var plan = Plan(alreadyPaid: 3);
        var today = D("2026-09-23");

        plan.InstallmentsPaidOrDue(today).Should().Be(6);
        plan.UnbilledAfter(today).Should().Be(600m);
        plan.NextInstallment(today)!.Number.Should().Be(7);
        plan.NextInstallment(today)!.Date.Should().Be(D("2026-10-15"));
    }

    [Fact]
    public void Early_payoff_counts_already_paid_over_dates()
    {
        var plan = Plan(alreadyPaid: 8);
        var today = D("2026-09-23");

        plan.InstallmentsPaidOrDue(today).Should().Be(8);
        plan.UnbilledAfter(today).Should().Be(400m);
        plan.NextInstallment(today)!.Number.Should().Be(9);
    }

    [Fact]
    public void Before_the_first_installment_everything_is_unbilled()
    {
        var plan = Plan();

        plan.UnbilledAfter(D("2026-04-14")).Should().Be(1200m);
        plan.InstallmentsPaidOrDue(D("2026-04-14")).Should().Be(0);
    }

    [Fact]
    public void After_the_last_installment_the_plan_is_done()
    {
        var plan = Plan();
        var afterEnd = D("2027-03-16");

        plan.InstallmentsPaidOrDue(afterEnd).Should().Be(12);
        plan.UnbilledAfter(afterEnd).Should().Be(0m);
        plan.NextInstallment(afterEnd).Should().BeNull();
    }

    [Fact]
    public void IsPaid_uses_already_paid_or_date()
    {
        var plan = Plan(alreadyPaid: 8);
        var schedule = plan.Schedule();
        var today = D("2026-09-23");

        plan.IsPaid(schedule[7], today).Should().BeTrue();   // n.º 8, paga antecipadamente
        plan.IsPaid(schedule[5], today).Should().BeTrue();   // n.º 6, data passada
        plan.IsPaid(schedule[8], today).Should().BeFalse();  // n.º 9
    }
}
