using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.CsvImport;

/// <summary>
/// Phase 6.5 grupo 7 (R2) — resolução de uma linha de transferência do
/// import: InvalidTarget, AlreadyRecorded, CurrencyMismatch, LinkExisting,
/// CreateCounterpart, por esta ordem.
/// </summary>
public sealed class ImportTransferResolverTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateOnly Date = new(2026, 9, 11);

    private readonly Account _checking = Account.Create("Conta", AccountType.Checking, "EUR", new Money(0m, "EUR"), Tenant);
    private readonly Account _card = Account.Create("Cartão", AccountType.CreditCard, "EUR", new Money(0m, "EUR"), Tenant);

    private Task<ImportTransferResolution> ResolveAsync(
        Account? target, StubTransferCounterpartQuery query, HashSet<Guid>? claimed = null)
        => ImportTransferResolver.ResolveAsync(
            _checking, target, TransactionDirection.Outflow, 450m, "EUR", Date,
            query, claimed ?? new HashSet<Guid>(), CancellationToken.None);

    [Fact]
    public async Task Missing_target_is_invalid()
    {
        (await ResolveAsync(null, new StubTransferCounterpartQuery())).Status
            .Should().Be(ImportTransferStatus.InvalidTarget);
    }

    [Fact]
    public async Task Target_equal_to_account_is_invalid()
    {
        (await ResolveAsync(_checking, new StubTransferCounterpartQuery())).Status
            .Should().Be(ImportTransferStatus.InvalidTarget);
    }

    [Fact]
    public async Task Existing_leg_with_counterpart_in_target_is_already_recorded()
    {
        var leg = new TransferCandidate(Guid.NewGuid(), Date.AddDays(3), _card.Id);
        var query = new StubTransferCounterpartQuery().With(_checking.Id, TransactionKind.Transfer, leg);

        var resolution = await ResolveAsync(_card, query);

        resolution.Status.Should().Be(ImportTransferStatus.AlreadyRecorded);
        resolution.TransactionId.Should().Be(leg.TransactionId);
    }

    [Fact]
    public async Task Existing_leg_with_counterpart_elsewhere_is_ignored()
    {
        var leg = new TransferCandidate(Guid.NewGuid(), Date, Guid.NewGuid());
        var query = new StubTransferCounterpartQuery().With(_checking.Id, TransactionKind.Transfer, leg);

        (await ResolveAsync(_card, query)).Status.Should().Be(ImportTransferStatus.CreateCounterpart);
    }

    [Fact]
    public async Task Target_in_other_currency_is_currency_mismatch()
    {
        var usdCard = Account.Create("Cartão USD", AccountType.CreditCard, "USD", new Money(0m, "USD"), Tenant);

        (await ResolveAsync(usdCard, new StubTransferCounterpartQuery())).Status
            .Should().Be(ImportTransferStatus.CurrencyMismatch);
    }

    [Fact]
    public async Task Regular_candidate_in_target_is_linked()
    {
        var candidate = new TransferCandidate(Guid.NewGuid(), Date.AddDays(3), null);
        var query = new StubTransferCounterpartQuery().With(_card.Id, TransactionKind.Regular, candidate);
        var claimed = new HashSet<Guid>();

        var resolution = await ResolveAsync(_card, query, claimed);

        resolution.Status.Should().Be(ImportTransferStatus.LinkExisting);
        resolution.TransactionId.Should().Be(candidate.TransactionId);
        claimed.Should().Contain(candidate.TransactionId);
    }

    [Fact]
    public async Task Nothing_found_creates_counterpart()
    {
        var resolution = await ResolveAsync(_card, new StubTransferCounterpartQuery());

        resolution.Status.Should().Be(ImportTransferStatus.CreateCounterpart);
        resolution.TransactionId.Should().BeNull();
    }

    [Fact]
    public async Task Single_candidate_is_claimed_by_the_first_row_only()
    {
        var candidate = new TransferCandidate(Guid.NewGuid(), Date.AddDays(1), null);
        var query = new StubTransferCounterpartQuery().With(_card.Id, TransactionKind.Regular, candidate);
        var claimed = new HashSet<Guid>();

        var first = await ResolveAsync(_card, query, claimed);
        var second = await ResolveAsync(_card, query, claimed);

        first.Status.Should().Be(ImportTransferStatus.LinkExisting);
        second.Status.Should().Be(ImportTransferStatus.CreateCounterpart);
    }
}
