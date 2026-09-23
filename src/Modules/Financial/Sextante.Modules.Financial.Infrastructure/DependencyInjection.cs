using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Infrastructure.Jobs;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Application.Features.Budgets.Alerts;
using Sextante.Modules.Financial.Application.Features.RecurringRules;
using Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;
using Sextante.Modules.Financial.Infrastructure.Accounts;
using Sextante.Modules.Financial.Infrastructure.Budgets;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Financial.Infrastructure.CsvImport;
using Sextante.Modules.Financial.Infrastructure.CategorizationRules;
using Sextante.Modules.Financial.Infrastructure.ExchangeRates;
using Sextante.Modules.Financial.Infrastructure.Messaging;
using Sextante.Modules.Financial.Infrastructure.Persistence;
using Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;
using Sextante.Modules.Financial.Infrastructure.RecurringRules;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinancialModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var migrationConnection = configuration.GetConnectionString("Migration")
            ?? configuration["MIGRATION__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'Migration' não configurada (ConnectionStrings:Migration ou MIGRATION__CONNECTION_STRING).");

        var appConnection = configuration.GetConnectionString("App")
            ?? configuration["APP__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'App' não configurada (ConnectionStrings:App ou APP__CONNECTION_STRING).");

        services.AddSingleton(new FinancialMigrationConnectionString(migrationConnection));

        services.AddScoped<FinancialAuditingInterceptor>();
        services.AddScoped<FinancialTenantPopulationInterceptor>();

        services.AddDbContext<FinancialDbContext>((sp, options) =>
        {
            options.UseNpgsql(appConnection, npgsql =>
                    npgsql.MigrationsHistoryTable("__migrations", "financial"))
                .AddInterceptors(
                    sp.GetRequiredService<TenantConnectionInterceptor>(),
                    sp.GetRequiredService<FinancialTenantPopulationInterceptor>(),
                    sp.GetRequiredService<FinancialAuditingInterceptor>());
        });

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountBalanceQuery, AccountBalanceQuery>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IRecurringRuleRepository, RecurringRuleRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IBudgetAlertRepository, BudgetAlertRepository>();
        services.AddScoped<IBudgetProgressService, BudgetProgressService>();
        services.AddScoped<BudgetAlertDispatchHandlers>();

        services.AddScoped<ITenantCurrencyResolver, TenantCurrencyResolver>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IIntegrationEventPublisher, WolverineIntegrationEventPublisher>();

        services.AddScoped<ICategorizationRuleRepository, CategorizationRuleRepository>();
        services.AddScoped<IImportProfileRepository, ImportProfileRepository>();
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddScoped<ICsvParser, CsvParser>();
        services.AddScoped<IDuplicateDetector, DuplicateDetector>();
        services.AddScoped<ICategorizationRuleEngine, CategorizationRuleEngine>();

        services.AddScoped<RecurringTransactionMaterializerHandler>();
        services.AddScoped(typeof(TenantAwareJob<>));
        services.AddScoped<
            ITenantAwareJobHandler<RecurringMaterializerPayload>,
            RecurringMaterializerJobAdapter>();
        services.AddScoped<RecurringMaterializerGlobalJob>();

        services.AddHostedService<FinancialMigrationRunner>();

        return services;
    }
}
