using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.ExchangeRates;
using Sextante.Modules.Identity.Infrastructure.Persistence;

namespace Sextante.Modules.Identity.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job que persiste o snapshot diário ECB em
/// <c>shared.exchange_rates</c>. Idempotente: corrida múltipla com o
/// mesmo provider apenas atualiza timestamps e não duplica linhas
/// (chave única <c>(rate_date, from_currency, to_currency)</c>).
/// Não wrapped em <c>TenantAwareJob&lt;T&gt;</c> — rates são globais.
/// </summary>
/// <remarks>
/// Semântica de cancelamento: <see cref="OperationCanceledException"/>
/// é re-throw sem persistir <c>LastError</c> (cancelamento é shutdown,
/// não falha do job). Outras exceptions persistem <c>LastError</c> via
/// <see cref="CancellationToken.None"/> de propósito — o registo da
/// causa deve sobreviver mesmo a um shutdown imediato a meio do catch.
/// </remarks>
public sealed class EcbSnapshotJob
{
    public const string RecurringJobId = "ecb-snapshot";

    private readonly ICurrencyProvider _provider;
    private readonly IdentityDbContext _db;
    private readonly ILogger<EcbSnapshotJob> _logger;

    public EcbSnapshotJob(
        ICurrencyProvider provider,
        IdentityDbContext db,
        ILogger<EcbSnapshotJob> logger)
    {
        _provider = provider;
        _db = db;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var state = await _db.EcbSnapshotStates
            .FirstOrDefaultAsync(s => s.Id == EcbSnapshotState.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new EcbSnapshotState { Id = EcbSnapshotState.SingletonId };
            _db.EcbSnapshotStates.Add(state);
        }

        var startedAt = DateTimeOffset.UtcNow;
        state.LastRunAt = startedAt;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var snapshot = await _provider.FetchLatestAsync(cancellationToken).ConfigureAwait(false);

            await UpsertRatesAsync(snapshot, cancellationToken).ConfigureAwait(false);

            state.LastSuccessAt = DateTimeOffset.UtcNow;
            state.LastError = null;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Snapshot ECB para {Date} aplicado ({Rates} rates).",
                snapshot.RateDate,
                snapshot.Rates.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelamento (shutdown / timeout) não é falha do job.
            // Não persistimos LastError — LastRunAt já regista a tentativa.
            throw;
        }
        catch (Exception ex)
        {
            var truncated = ex.Message.Length > EcbSnapshotState.LastErrorMaxLength
                ? ex.Message[..EcbSnapshotState.LastErrorMaxLength]
                : ex.Message;

            state.LastError = truncated;
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            _logger.LogError(ex, "Snapshot ECB falhou.");
            throw;
        }
    }

    private async Task UpsertRatesAsync(EcbSnapshot snapshot, CancellationToken cancellationToken)
    {
        var toCurrencies = snapshot.Rates.Select(r => r.ToCurrency).ToArray();
        var existing = await _db.ExchangeRates
            .Where(r => r.RateDate == snapshot.RateDate
                && r.FromCurrency == ExchangeRate.EurBase
                && toCurrencies.Contains(r.ToCurrency))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingIndex = existing.ToDictionary(r => r.ToCurrency, StringComparer.Ordinal);

        foreach (var dailyRate in snapshot.Rates)
        {
            if (existingIndex.TryGetValue(dailyRate.ToCurrency, out var existingRow))
            {
                existingRow.UpdateRate(dailyRate.Rate, ExchangeRate.SourceEcb);
            }
            else
            {
                var newRow = ExchangeRate.Create(
                    snapshot.RateDate,
                    ExchangeRate.EurBase,
                    dailyRate.ToCurrency,
                    dailyRate.Rate,
                    ExchangeRate.SourceEcb);
                _db.ExchangeRates.Add(newRow);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
