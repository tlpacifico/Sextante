using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sextante.Infrastructure.Jobs;
using Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;
using Sextante.Modules.Financial.Infrastructure.Persistence;

namespace Sextante.Modules.Financial.Infrastructure.RecurringRules;

/// <summary>
/// Job global que faz scan cross-tenant via connection BYPASSRLS
/// e invoca <c>TenantAwareJob</c> para cada tenant.
/// Desenho: 1 recurring job global → handler faz scan de
/// tenants (opção (a) do requirements.md).
/// </summary>
public sealed class RecurringMaterializerGlobalJob
{
    private readonly FinancialMigrationConnectionString _migrationConnection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringMaterializerGlobalJob> _logger;

    public const string RecurringJobId = "recurring-materializer-global";

    public RecurringMaterializerGlobalJob(
        FinancialMigrationConnectionString migrationConnection,
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringMaterializerGlobalJob> logger)
    {
        _migrationConnection = migrationConnection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Recebe o <see cref="IRecurringJobManager"/> do host em vez de usar a
    /// fachada estática <c>RecurringJob</c> (que escreve contra o
    /// <c>JobStorage.Current</c> global ao processo).
    /// </summary>
    public static void Register(IRecurringJobManager manager)
    {
        manager.AddOrUpdate<RecurringMaterializerGlobalJob>(
            RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "15 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var runDate = DateOnly.FromDateTime(DateTime.UtcNow);
        _logger.LogInformation("RecurringMaterializerGlobalJob iniciado para {RunDate}", runDate);

        // Scan cross-tenant via connection com BYPASSRLS (role migration).
        // A query toca financial.recurring_rules que tem RLS com
        // tenant_id = current_setting('app.current_tenant_id')::uuid.
        // A connection de migração corre com BYPASSRLS — o scan vê todos
        // os tenants.
        var tenantIds = new List<Guid>();

        await using (var conn = new NpgsqlConnection(_migrationConnection.Value))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT tenant_id
                  FROM financial.recurring_rules
                 WHERE deleted_at IS NULL
                   AND next_occurrence IS NOT NULL
                   AND next_occurrence <= @runDate
                   AND is_active = true
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "runDate";
            p.Value = runDate;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                tenantIds.Add(reader.GetGuid(0));
            }
        }

        if (tenantIds.Count == 0)
        {
            _logger.LogInformation("Nenhum tenant com regras a materializar para {RunDate}", runDate);
            return;
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Cada tenant corre no seu próprio scope para evitar
                // contaminação de DbContext entre materializações.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tenantAwareJob = scope.ServiceProvider
                    .GetRequiredService<TenantAwareJob<RecurringMaterializerPayload>>();

                var payload = new RecurringMaterializerPayload(runDate);
                await tenantAwareJob.RunAsync(tenantId, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Erro ao materializar regras para tenant {TenantId}",
                    tenantId);
            }
        }

        _logger.LogInformation(
            "RecurringMaterializerGlobalJob concluído: {Count} tenants processados",
            tenantIds.Count);
    }
}
