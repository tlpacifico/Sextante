using JasperFx;
using JasperFx.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sextante.Infrastructure.Wolverine;
using Sextante.Modules.Identity.Application.Middleware;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace Sextante.Host.Configuration;

internal static class WolverineSetup
{
    public static void AddSextanteWolverine(this WebApplicationBuilder builder)
    {
        builder.Host.UseWolverine(opts =>
        {
            var migrationConnection = builder.Configuration.GetConnectionString("Migration")
                ?? builder.Configuration["MIGRATION__CONNECTION_STRING"]
                ?? throw new InvalidOperationException(
                    "Connection string 'Migration' não configurada para Wolverine.");

            opts.PersistMessagesWithPostgresql(migrationConnection, schemaName: "messaging");
            opts.UseEntityFrameworkCoreTransactions();

            // AutoApplyTransactions corre por default em qualquer chain sem
            // [Transactional]/[NonTransactional]. Com FinancialDbContext +
            // IdentityDbContext registados, o EFCorePersistenceFrameProvider
            // não consegue desambiguar — cada handler Wolverine declara
            // [NonTransactional] e chama repository.SaveChangesAsync
            // explicitamente (UoW por repositório). Quando um handler precisar
            // de outbox transacional, troca para [Transactional] e injecta o
            // DbContext concreto como parâmetro do Handle.
            opts.Policies.UseDurableLocalQueues();
            opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // Wolverine cria/actualiza o schema messaging.* no startup — JasperFx
            // hosted service corre antes de o servidor aceitar HTTP.
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            opts.Services.AddResourceSetupOnStartup();

            opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Identity.Application.AssemblyMarker).Assembly);
            opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Financial.Application.AssemblyMarker).Assembly);

            // Convenção plural: aceitar `*Handlers` (vertical slices agrupam vários
            // handlers numa única classe estática). Wolverine default é `*Handler`.
            opts.Discovery.CustomizeHandlerDiscovery(x =>
            {
                x.Includes.WithNameSuffix("Handlers");
            });

            // TenantSettingMiddleware corre antes de TenantLoggingMiddleware:
            // configura ITenantContextSetter para subscribers fora de HTTP
            // (Wolverine outbox em background scope) — ver tech-stack §4.4.
            opts.Policies.AddMiddleware<TenantSettingMiddleware>();
            opts.Policies.AddMiddleware<TenantLoggingMiddleware>();

            // Phase 5.5 — FluentValidation centralizada + métricas mínimas.
            opts.Policies.AddMiddleware<FluentValidationMiddleware>();
            opts.Policies.AddMiddleware<MetricsMiddleware>();
        });
    }
}
