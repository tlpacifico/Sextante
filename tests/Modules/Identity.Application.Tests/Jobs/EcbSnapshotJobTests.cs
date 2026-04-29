using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.ExchangeRates;
using Sextante.Modules.Identity.Infrastructure.Jobs;
using Sextante.Modules.Identity.Infrastructure.Persistence;

namespace Sextante.Modules.Identity.Application.Tests.Jobs;

public sealed class EcbSnapshotJobTests
{
    [Fact]
    public async Task RunAsync_persists_rates_and_updates_state_on_success()
    {
        var db = NewDb();
        var snapshot = new EcbSnapshot(
            new DateOnly(2026, 04, 28),
            new[]
            {
                new EcbDailyRate("USD", 1.10m),
                new EcbDailyRate("BRL", 6.00m),
            });
        var provider = new StubProvider(snapshot);
        var job = new EcbSnapshotJob(provider, db, NullLogger<EcbSnapshotJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var rates = await db.ExchangeRates.ToListAsync();
        rates.Should().HaveCount(2);
        rates.Select(r => r.ToCurrency).Should().BeEquivalentTo(new[] { "USD", "BRL" });
        rates.Should().AllSatisfy(r => r.Source.Should().Be(ExchangeRate.SourceEcb));

        var state = await db.EcbSnapshotStates.SingleAsync();
        state.LastError.Should().BeNull();
        state.LastSuccessAt.Should().NotBeNull();
        state.LastRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_is_idempotent_when_called_twice()
    {
        var db = NewDb();
        var snapshot = new EcbSnapshot(
            new DateOnly(2026, 04, 28),
            new[]
            {
                new EcbDailyRate("USD", 1.10m),
                new EcbDailyRate("BRL", 6.00m),
            });
        var provider = new StubProvider(snapshot);
        var job = new EcbSnapshotJob(provider, db, NullLogger<EcbSnapshotJob>.Instance);

        await job.RunAsync(CancellationToken.None);
        var firstSuccess = (await db.EcbSnapshotStates.SingleAsync()).LastSuccessAt;

        // Update snapshot to slightly different rates — second run should
        // update existing rows in place, not create duplicates.
        provider.Snapshot = new EcbSnapshot(
            new DateOnly(2026, 04, 28),
            new[]
            {
                new EcbDailyRate("USD", 1.11m),
                new EcbDailyRate("BRL", 6.05m),
            });
        await job.RunAsync(CancellationToken.None);

        var rates = await db.ExchangeRates.ToListAsync();
        rates.Should().HaveCount(2);
        rates.Single(r => r.ToCurrency == "USD").Rate.Should().Be(1.11m);
        rates.Single(r => r.ToCurrency == "BRL").Rate.Should().Be(6.05m);

        var state = await db.EcbSnapshotStates.SingleAsync();
        state.LastSuccessAt.Should().NotBeNull();
        state.LastSuccessAt!.Value.Should().BeOnOrAfter(firstSuccess!.Value);
    }

    [Fact]
    public async Task RunAsync_records_LastError_when_provider_fails()
    {
        var db = NewDb();
        var provider = new StubProvider(throws: new EcbProviderException("ECB unreachable"));
        var job = new EcbSnapshotJob(provider, db, NullLogger<EcbSnapshotJob>.Instance);

        var act = () => job.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<EcbProviderException>();

        var state = await db.EcbSnapshotStates.SingleAsync();
        state.LastError.Should().Be("ECB unreachable");
        state.LastSuccessAt.Should().BeNull();
        state.LastRunAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_truncates_long_error_messages()
    {
        var db = NewDb();
        var longMessage = new string('x', EcbSnapshotState.LastErrorMaxLength + 100);
        var provider = new StubProvider(throws: new EcbProviderException(longMessage));
        var job = new EcbSnapshotJob(provider, db, NullLogger<EcbSnapshotJob>.Instance);

        var act = () => job.RunAsync(CancellationToken.None);
        await act.Should().ThrowAsync<EcbProviderException>();

        var state = await db.EcbSnapshotStates.SingleAsync();
        state.LastError.Should().HaveLength(EcbSnapshotState.LastErrorMaxLength);
    }

    [Fact]
    public async Task RunAsync_does_not_persist_LastError_on_cancellation()
    {
        var db = NewDb();
        // Pre-seed state with a known prior error so we can verify it is
        // NOT overwritten by cancellation.
        const string priorError = "previous failure";
        db.EcbSnapshotStates.Add(new EcbSnapshotState
        {
            Id = EcbSnapshotState.SingletonId,
            LastError = priorError,
        });
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource();
        var provider = new StubProvider(onFetch: ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new EcbSnapshot(new DateOnly(2026, 04, 28), Array.Empty<EcbDailyRate>()));
        });
        var job = new EcbSnapshotJob(provider, db, NullLogger<EcbSnapshotJob>.Instance);

        var act = () => job.RunAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Reload state from DB to defeat tracking caching.
        db.ChangeTracker.Clear();
        var state = await db.EcbSnapshotStates.SingleAsync();
        state.LastError.Should().Be(priorError, "cancellation should not overwrite prior LastError");
    }

    private static IdentityDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase($"ecb-snapshot-{Guid.NewGuid()}")
            .Options;
        return new IdentityDbContext(options);
    }

    private sealed class StubProvider : ICurrencyProvider
    {
        private readonly Exception? _throws;
        private readonly Func<CancellationToken, Task<EcbSnapshot>>? _onFetch;

        public StubProvider(EcbSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public StubProvider(Exception throws)
        {
            _throws = throws;
        }

        public StubProvider(Func<CancellationToken, Task<EcbSnapshot>> onFetch)
        {
            _onFetch = onFetch;
        }

        public EcbSnapshot? Snapshot { get; set; }

        public Task<EcbSnapshot> FetchLatestAsync(CancellationToken cancellationToken)
        {
            if (_throws is not null)
            {
                throw _throws;
            }

            if (_onFetch is not null)
            {
                return _onFetch(cancellationToken);
            }

            return Task.FromResult(Snapshot!);
        }
    }
}
