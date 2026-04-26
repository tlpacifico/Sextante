using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Aplica migrations ao arrancar o Host, com lock distribuído em
/// <c>pg_advisory_lock</c> para evitar race quando há múltiplas
/// instâncias (tech-stack §1).
/// </summary>
/// <remarks>
/// O <see cref="IdentityDbContext"/> registado em DI usa a connection
/// <c>sextante_app</c> (runtime, NOBYPASSRLS). Migrations precisam de
/// privilégios de DDL — daí construir aqui um DbContext separado
/// configurado com <see cref="IdentityMigrationConnectionString"/>
/// (<c>sextante_migrations</c>, BYPASSRLS).
/// </remarks>
public sealed class MigrationRunner : IHostedService
{
    private const long AdvisoryLockKey = 8423718237L;

    private readonly ILogger<MigrationRunner> _logger;
    private readonly string _migrationConnectionString;

    public MigrationRunner(
        ILogger<MigrationRunner> logger,
        IdentityMigrationConnectionString migrationConnectionString)
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
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(_migrationConnectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__migrations", "shared"))
                .Options;

            await using var ctx = new IdentityDbContext(options);

            _logger.LogInformation("A aplicar migrations Identity ({Connection}).", MaskConnection(_migrationConnectionString));
            await ctx.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Migrations Identity aplicadas com sucesso.");
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

/// <summary>
/// Wrapper tipado da connection string usada pelo migration runner
/// (separa <c>sextante_migrations</c> da connection runtime <c>sextante_app</c>).
/// </summary>
public sealed record IdentityMigrationConnectionString(string Value);
