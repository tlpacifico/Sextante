using FluentAssertions;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.Transactions;

public sealed class CreateTransactionHandler_MultiCurrencyTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public async Task Same_currency_persists_null_exchange_rate()
    {
        // Tenant primary EUR, account EUR → handler resolves and gets null
        // (rate=1.0 implied) → persisted ER fields are null.
        var account = NewAccount(currency: "EUR");
        var fixture = new HandlerFixture("EUR", new[] { account })
        {
            ExchangeRates = new StubExchangeRateService(returnsNull: true),
        };

        var command = new CreateTransactionCommand(
            account.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Amount: 100m,
            Currency: null,
            Description: null,
            Tags: null);

        var response = await TransactionHandlers.Handle(
            command,
            fixture.Transactions,
            fixture.Accounts,
            fixture.Categories,
            fixture.TenantContext,
            fixture.TenantCurrency,
            fixture.ExchangeRates,
            fixture.CurrencyDirectory,
            fixture.Events,
            CancellationToken.None);

        response.ExchangeRateToPrimary.Should().BeNull();
        response.ExchangeRateAt.Should().BeNull();
        response.Amount.Currency.Should().Be("EUR");
        fixture.ExchangeRates.LastResolveCall.Should()
            .Be((From: "EUR", To: "EUR"));
    }

    [Fact]
    public async Task Account_currency_differs_from_primary_persists_snapshot()
    {
        // Tenant primary EUR, account USD → handler resolves USD→EUR; stub
        // returns rate=0.9, persisted on transaction.
        var account = NewAccount(currency: "USD");
        var rateAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var fixture = new HandlerFixture("EUR", new[] { account })
        {
            ExchangeRates = new StubExchangeRateService(
                returnsNull: false,
                snapshot: new ExchangeRateSnapshot(0.9m, rateAt)),
        };

        var command = new CreateTransactionCommand(
            account.Id,
            Guid.NewGuid(),
            rateAt,
            Amount: 300m,
            Currency: null,
            Description: null,
            Tags: null);

        var response = await TransactionHandlers.Handle(
            command,
            fixture.Transactions,
            fixture.Accounts,
            fixture.Categories,
            fixture.TenantContext,
            fixture.TenantCurrency,
            fixture.ExchangeRates,
            fixture.CurrencyDirectory,
            fixture.Events,
            CancellationToken.None);

        response.ExchangeRateToPrimary.Should().Be(0.9m);
        response.ExchangeRateAt.Should().Be(rateAt);
        response.Amount.Currency.Should().Be("USD");
        fixture.ExchangeRates.LastResolveCall.Should()
            .Be((From: "USD", To: "EUR"));
    }

    [Fact]
    public async Task Override_currency_resolves_against_directory_and_uses_override()
    {
        // Account is USD but command overrides to BRL; tenant primary EUR.
        // Handler resolves BRL→EUR. BRL must be active in directory.
        var account = NewAccount(currency: "USD");
        var fixture = new HandlerFixture("EUR", new[] { account })
        {
            CurrencyDirectory = new StubCurrencyDirectory(active: new[] { "EUR", "USD", "BRL" }),
            ExchangeRates = new StubExchangeRateService(
                returnsNull: false,
                snapshot: new ExchangeRateSnapshot(0.18m, DateTimeOffset.UtcNow)),
        };

        var command = new CreateTransactionCommand(
            account.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Amount: 50m,
            Currency: "brl",
            Description: null,
            Tags: null);

        var response = await TransactionHandlers.Handle(
            command,
            fixture.Transactions,
            fixture.Accounts,
            fixture.Categories,
            fixture.TenantContext,
            fixture.TenantCurrency,
            fixture.ExchangeRates,
            fixture.CurrencyDirectory,
            fixture.Events,
            CancellationToken.None);

        response.Amount.Currency.Should().Be("BRL");
        response.ExchangeRateToPrimary.Should().Be(0.18m);
        fixture.ExchangeRates.LastResolveCall.Should()
            .Be((From: "BRL", To: "EUR"));
    }

    [Fact]
    public async Task Inactive_override_currency_throws_CurrencyNotActiveException()
    {
        var account = NewAccount(currency: "USD");
        var fixture = new HandlerFixture("EUR", new[] { account })
        {
            CurrencyDirectory = new StubCurrencyDirectory(active: new[] { "EUR", "USD" }),
        };

        var command = new CreateTransactionCommand(
            account.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Amount: 10m,
            Currency: "ZZZ",
            Description: null,
            Tags: null);

        var act = () => TransactionHandlers.Handle(
            command,
            fixture.Transactions,
            fixture.Accounts,
            fixture.Categories,
            fixture.TenantContext,
            fixture.TenantCurrency,
            fixture.ExchangeRates,
            fixture.CurrencyDirectory,
            fixture.Events,
            CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyNotActiveException>();
    }

    [Fact]
    public async Task Exchange_rate_unavailable_propagates()
    {
        var account = NewAccount(currency: "USD");
        var rateDate = new DateOnly(2026, 04, 27);
        var fixture = new HandlerFixture("EUR", new[] { account })
        {
            ExchangeRates = new StubExchangeRateService(
                throws: new ExchangeRateUnavailableException("USD", "EUR", rateDate)),
        };

        var command = new CreateTransactionCommand(
            account.Id,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Amount: 100m,
            Currency: null,
            Description: null,
            Tags: null);

        var act = () => TransactionHandlers.Handle(
            command,
            fixture.Transactions,
            fixture.Accounts,
            fixture.Categories,
            fixture.TenantContext,
            fixture.TenantCurrency,
            fixture.ExchangeRates,
            fixture.CurrencyDirectory,
            fixture.Events,
            CancellationToken.None);

        await act.Should().ThrowAsync<ExchangeRateUnavailableException>();
    }

    private static Account NewAccount(string currency)
        => Account.Create(
            name: $"Account {currency}",
            type: AccountType.Checking,
            currency: currency,
            openingBalance: new Money(0m, currency),
            tenantId: Tenant);

    private sealed class HandlerFixture
    {
        public HandlerFixture(string primaryCurrency, IEnumerable<Account> accounts)
        {
            TenantContext = new StubTenantContext(Tenant);
            TenantCurrency = new StubTenantCurrencyResolver(primaryCurrency);
            Accounts = new InMemoryAccountRepository(accounts);
            Transactions = new InMemoryTransactionRepository();
            CurrencyDirectory = new StubCurrencyDirectory(active: new[] { "EUR", "USD", "BRL", "GBP" });
            ExchangeRates = new StubExchangeRateService(returnsNull: true);
            Events = new StubIntegrationEventPublisher();
            Categories = new InMemoryCategoryRepository();
        }

        public InMemoryCategoryRepository Categories { get; }

        public StubTenantContext TenantContext { get; }
        public StubTenantCurrencyResolver TenantCurrency { get; }
        public InMemoryAccountRepository Accounts { get; }
        public InMemoryTransactionRepository Transactions { get; }
        public StubCurrencyDirectory CurrencyDirectory { get; set; }
        public StubExchangeRateService ExchangeRates { get; set; }
        public StubIntegrationEventPublisher Events { get; }
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
        public TenantId TenantId { get; }
    }

    private sealed class StubTenantCurrencyResolver : ITenantCurrencyResolver
    {
        private readonly string _primary;
        public StubTenantCurrencyResolver(string primary) => _primary = primary;
        public Task<string> GetPrimaryCurrencyAsync(CancellationToken cancellationToken)
            => Task.FromResult(_primary);
    }

    private sealed class StubCurrencyDirectory : ICurrencyDirectory
    {
        private readonly HashSet<string> _active;
        public StubCurrencyDirectory(IEnumerable<string> active)
            => _active = new HashSet<string>(active, StringComparer.Ordinal);
        public Task<IReadOnlyList<string>> GetActiveCodesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(_active.OrderBy(c => c).ToList());
        public Task<bool> IsActiveAsync(string code, CancellationToken ct)
            => Task.FromResult(_active.Contains(code));
    }

    private sealed class StubExchangeRateService : IExchangeRateService
    {
        private readonly bool _returnsNull;
        private readonly ExchangeRateSnapshot? _snapshot;
        private readonly Exception? _throws;

        public StubExchangeRateService(bool returnsNull = true, ExchangeRateSnapshot? snapshot = null, Exception? throws = null)
        {
            _returnsNull = returnsNull;
            _snapshot = snapshot;
            _throws = throws;
        }

        public (string From, string To)? LastResolveCall { get; private set; }

        public Task<ExchangeRateSnapshot?> ResolveAsync(string fromCurrency, string toCurrency, DateTimeOffset at, CancellationToken cancellationToken)
        {
            LastResolveCall = (fromCurrency, toCurrency);
            if (_throws is not null)
            {
                throw _throws;
            }

            return Task.FromResult(_returnsNull ? null : _snapshot);
        }
    }

    private sealed class InMemoryAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts;

        public InMemoryAccountRepository(IEnumerable<Account> accounts)
            => _accounts = accounts.ToDictionary(a => a.Id);

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_accounts.TryGetValue(id, out var a) ? a : null);
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Account>>(_accounts.Values.ToList());
        public Task AddAsync(Account account, CancellationToken ct)
        {
            _accounts[account.Id] = account;
            return Task.CompletedTask;
        }
        public void Update(Account account) => _accounts[account.Id] = account;
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<int> CountActiveTransactionsAsync(Guid accountId, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class InMemoryTransactionRepository : ITransactionRepository
    {
        public List<Transaction> Added { get; } = new();

        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Transaction?>(Added.FirstOrDefault(t => t.Id == id));
        public Task<IReadOnlyList<Transaction>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Transaction>>(Added.Where(t => ids.Contains(t.Id)).ToList());
        public Task AddAsync(Transaction transaction, CancellationToken ct)
        {
            Added.Add(transaction);
            return Task.CompletedTask;
        }
        public void Update(Transaction transaction) { /* no-op */ }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult(new TransactionPage(Added, null));
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

        // Phase 6.5 grupo 3 — transferências: os stubs não exercitam este caminho.
        public Task<IReadOnlyList<Transaction>> GetByTransferIdAsync(Guid transferId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Transaction>>(Added.Where(t => t.TransferId == transferId).ToList());
        public Task<IReadOnlyDictionary<Guid, Guid>> GetCounterpartAccountIdsAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());
    }
}
