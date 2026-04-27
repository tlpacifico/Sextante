using FluentAssertions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.AccountsSpec;

public sealed class AccountTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur100 = new(100m, "EUR");

    [Fact]
    public void Create_rejects_empty_name()
    {
        var act = () => Account.Create("", AccountType.Checking, Eur100, Tenant);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_name_over_200_chars()
    {
        var name = new string('a', 201);
        var act = () => Account.Create(name, AccountType.Checking, Eur100, Tenant);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_negative_opening_balance()
    {
        var negative = new Money(-1m, "EUR");
        var act = () => Account.Create("Conta", AccountType.Checking, negative, Tenant);
        act.Should().Throw<OpeningBalanceNegativeException>();
    }

    [Fact]
    public void Create_succeeds_with_valid_inputs()
    {
        var account = Account.Create("Conta", AccountType.Checking, Eur100, Tenant);
        account.Id.Should().NotBeEmpty();
        account.Name.Should().Be("Conta");
        account.OpeningBalance.Should().BeEquivalentTo(Eur100);
        account.TenantId.Should().Be(Tenant);
    }

    [Fact]
    public void Rename_updates_name()
    {
        var account = Account.Create("Conta", AccountType.Checking, Eur100, Tenant);
        account.Rename("Nova");
        account.Name.Should().Be("Nova");
    }

    [Fact]
    public void OpeningBalance_setter_is_not_public()
    {
        // Confirma a invariância via reflection: o setter de OpeningBalance
        // não é public. A criação imutabiliza o saldo inicial.
        var prop = typeof(Account).GetProperty(nameof(Account.OpeningBalance));
        prop.Should().NotBeNull();
        prop!.SetMethod.Should().NotBeNull();
        prop.SetMethod!.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void Archive_sets_DeletedAt()
    {
        var account = Account.Create("Conta", AccountType.Checking, Eur100, Tenant);
        account.Archive();
        account.DeletedAt.Should().NotBeNull();
    }
}
