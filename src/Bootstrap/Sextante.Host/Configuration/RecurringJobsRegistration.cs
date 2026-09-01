using Hangfire;
using Sextante.Modules.Financial.Infrastructure.RecurringRules;
using Sextante.Modules.Identity.Infrastructure.Jobs;

namespace Sextante.Host.Configuration;

internal static class RecurringJobsRegistration
{
    /// <summary>
    /// Registo via <see cref="IRecurringJobManager"/> resolvido do próprio
    /// host — e não pela fachada estática <c>RecurringJob</c>, que escreve
    /// contra o <c>JobStorage.Current</c> global ao processo. Com a fachada
    /// estática, um segundo host no mesmo processo (cada
    /// <c>WebApplicationFactory</c> dos integration tests é um) escrevia
    /// contra a storage do host anterior — já eliminada — e falhava o
    /// arranque com erro de conexão Npgsql.
    /// </summary>
    public static void RegisterRecurringJobs(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();

        // ECB snapshot às 00:30 UTC. Idempotente — re-runs com o mesmo
        // provider apenas atualizam timestamps.
        manager.AddOrUpdate<EcbSnapshotJob>(
            EcbSnapshotJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "30 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Materializer de transações recorrentes às 00:15 UTC. Corre antes do
        // ECB (00:30) para que transações materializadas estejam disponíveis
        // quando o utilizador abrir o sistema de manhã. Job global: faz scan
        // cross-tenant de regras activas e invoca TenantAwareJob<T> para cada
        // tenant (opção (a) do requirements.md).
        RecurringMaterializerGlobalJob.Register(manager);
    }
}
