using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

public sealed class FinancialDbContextFactory : IDesignTimeDbContextFactory<FinancialDbContext>
{
    public FinancialDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("MIGRATION__CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=sextante;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FinancialDbContext>()
            .UseNpgsql(connection, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", "financial"))
            .Options;

        return new FinancialDbContext(options);
    }
}
