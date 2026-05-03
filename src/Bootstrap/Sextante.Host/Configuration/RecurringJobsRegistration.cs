using Hangfire;
using Sextante.Modules.Financial.Infrastructure.RecurringRules;
using Sextante.Modules.Identity.Infrastructure.Jobs;

namespace Sextante.Host.Configuration;

internal static class RecurringJobsRegistration
{
    public static void RegisterRecurringJobs()
    {
        // ECB snapshot às 00:30 UTC. Idempotente — re-runs com o mesmo
        // provider apenas atualizam timestamps.
        RecurringJob.AddOrUpdate<EcbSnapshotJob>(
            EcbSnapshotJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            "30 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        // Materializer de transações recorrentes às 00:15 UTC. Corre antes do
        // ECB (00:30) para que transações materializadas estejam disponíveis
        // quando o utilizador abrir o sistema de manhã. Job global: faz scan
        // cross-tenant de regras activas e invoca TenantAwareJob<T> para cada
        // tenant (opção (a) do requirements.md).
        RecurringMaterializerGlobalJob.Register();
    }
}
