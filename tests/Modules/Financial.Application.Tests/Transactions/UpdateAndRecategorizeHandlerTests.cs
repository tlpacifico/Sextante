using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.Transactions;

public sealed class UpdateAndRecategorizeHandlerTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public async Task Update_changes_fields_and_publishes_event()
    {
        var existing = NewTransaction();
        var repo = new InMemoryRepo(existing);
        var publisher = new StubIntegrationEventPublisher();
        var newAccountId = Guid.NewGuid();
        var newCategoryId = Guid.NewGuid();
        var newOccurredAt = DateTimeOffset.UtcNow.AddDays(-1);

        var command = new UpdateTransactionCommand(
            existing.Id,
            newAccountId,
            newCategoryId,
            newOccurredAt,
            Amount: 250m,
            Description: "atualizada",
            Tags: null);

        var response = await TransactionHandlers.Handle(
            command, repo, new StubTenantContext(Tenant), publisher, CancellationToken.None);

        response.Should().NotBeNull();
        response!.AccountId.Should().Be(newAccountId);
        response.CategoryId.Should().Be(newCategoryId);
        response.Amount.Amount.Should().Be(250m);
        response.OccurredAt.Should().Be(newOccurredAt);

        publisher.Published.Should().ContainSingle(e => e is TransactionUpdatedIntegrationEvent);
        var evt = (TransactionUpdatedIntegrationEvent)publisher.Published.Single();
        evt.TransactionId.Should().Be(existing.Id);
        evt.CategoryId.Should().Be(newCategoryId);
    }

    [Fact]
    public async Task Update_throws_KeyNotFound_when_transaction_does_not_exist()
    {
        var repo = new InMemoryRepo();
        var publisher = new StubIntegrationEventPublisher();

        var command = new UpdateTransactionCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, 50m, null, null);

        var act = () => TransactionHandlers.Handle(
            command, repo, new StubTenantContext(Tenant), publisher, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Recategorize_updates_listed_transactions_and_publishes_events_per_id()
    {
        var t1 = NewTransaction();
        var t2 = NewTransaction();
        var t3 = NewTransaction();
        var repo = new InMemoryRepo(t1, t2, t3);
        var publisher = new StubIntegrationEventPublisher();
        var newCategory = Guid.NewGuid();

        var command = new RecategorizeTransactionsCommand(new[] { t1.Id, t3.Id }, newCategory);

        var result = await TransactionHandlers.Handle(
            command, repo, new StubTenantContext(Tenant), publisher, CancellationToken.None);

        result.UpdatedCount.Should().Be(2);
        t1.CategoryId.Should().Be(newCategory);
        t3.CategoryId.Should().Be(newCategory);
        t2.CategoryId.Should().NotBe(newCategory);

        publisher.Published.Should().HaveCount(2);
        publisher.Published.Should().AllSatisfy(e => e.Should().BeOfType<TransactionUpdatedIntegrationEvent>());
    }

    [Fact]
    public async Task Recategorize_with_unknown_ids_returns_zero_and_publishes_nothing()
    {
        var repo = new InMemoryRepo();
        var publisher = new StubIntegrationEventPublisher();

        var command = new RecategorizeTransactionsCommand(new[] { Guid.NewGuid(), Guid.NewGuid() }, Guid.NewGuid());

        var result = await TransactionHandlers.Handle(
            command, repo, new StubTenantContext(Tenant), publisher, CancellationToken.None);

        result.UpdatedCount.Should().Be(0);
        publisher.Published.Should().BeEmpty();
    }

    private static Transaction NewTransaction()
        => Transaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-2),
            new Money(100m, "EUR"),
            description: "original",
            tags: null,
            tenantId: Tenant);

    [Fact]
    public async Task Archive_publishes_updated_event_so_budgets_recalculate()
    {
        var existing = NewTransaction();
        var repo = new InMemoryRepo(existing);
        var publisher = new StubIntegrationEventPublisher();

        var archived = await TransactionHandlers.Handle(
            new ArchiveTransactionCommand(existing.Id),
            repo, new StubTenantContext(Tenant), publisher, CancellationToken.None);

        archived.Should().BeTrue();
        existing.DeletedAt.Should().NotBeNull();
        publisher.Published.OfType<TransactionUpdatedIntegrationEvent>()
            .Should().ContainSingle(e => e.TransactionId == existing.Id);
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
        public TenantId TenantId { get; }
    }

    private sealed class InMemoryRepo : ITransactionRepository
    {
        private readonly List<Transaction> _items;

        public InMemoryRepo(params Transaction[] items)
        {
            _items = items.ToList();
        }

        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Transaction?>(_items.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<Transaction>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Transaction>>(_items.Where(t => ids.Contains(t.Id)).ToList());

        public Task AddAsync(Transaction t, CancellationToken ct)
        {
            _items.Add(t);
            return Task.CompletedTask;
        }

        public void Update(Transaction t) { /* in-place via shared instances */ }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult(new TransactionPage(_items, null));
        public Task<TransactionTotals> GetConvertedTotalsAsync(TransactionFilter filter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult(new TransactionTotals(
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency)));
        public Task<IReadOnlyList<TransactionTotalsByCurrencyRow>> GetTotalsByCurrencyAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionTotalsByCurrencyRow>>(Array.Empty<TransactionTotalsByCurrencyRow>());
        // Phase 6 — export CSV: os stubs não exercitam este caminho.
        public Task<IReadOnlyList<TransactionExportDataRow>> ListForExportAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionExportDataRow>>(Array.Empty<TransactionExportDataRow>());

        public Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(TransactionFilter filter, CategoryKindFilter kindFilter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionByCategoryRow>>(Array.Empty<TransactionByCategoryRow>());
    }
}
