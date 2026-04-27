using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

/// <summary>
/// Equivalente do <c>MigrationRunner</c> do Identity, mas para o
/// schema <c>financial</c>. Usa um advisory lock distinto para não
/// colidir com o lock do Identity.
/// </summary>
public sealed class FinancialMigrationRunner : IHostedService
{
    private const long AdvisoryLockKey = 8423718242L;

    private readonly ILogger<FinancialMigrationRunner> _logger;
    private readonly string _migrationConnectionString;

    public FinancialMigrationRunner(
        ILogger<FinancialMigrationRunner> logger,
        FinancialMigrationConnectionString migrationConnectionString)
    {
        _logger = logger;
        _migrationConnectionString = migrationConnectionString.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var lockConnection = new NpgsqlConnection(_migrationConnectionString);
        await lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var lockCmd = lockConnection.CreateCommand())
        {
            lockCmd.CommandText = "SELECT pg_advisory_lock(@key)";
            var p = lockCmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = AdvisoryLockKey;
            lockCmd.Parameters.Add(p);
            await lockCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var options = new DbContextOptionsBuilder<FinancialDbContext>()
                .UseNpgsql(_migrationConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__migrations", "financial"))
                .Options;

            await using var ctx = new FinancialDbContext(options);

            _logger.LogInformation(
                "A aplicar migrations Financial ({Connection}).",
                MaskConnection(_migrationConnectionString));
            await ctx.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Migrations Financial aplicadas com sucesso.");
        }
        finally
        {
            await using var unlockCmd = lockConnection.CreateCommand();
            unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = unlockCmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = AdvisoryLockKey;
            unlockCmd.Parameters.Add(p);
            await unlockCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string MaskConnection(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Password = "***";
        return builder.ConnectionString;
    }
}

public sealed record FinancialMigrationConnectionString(string Value);
