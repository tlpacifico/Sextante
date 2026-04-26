using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Design-time factory usada por <c>dotnet ef migrations</c>. Não é
/// invocada em runtime — DI fornece <see cref="IdentityDbContext"/>
/// configurado contra Postgres real.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("MIGRATION__CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=sextante;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connection, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", "shared"))
            .Options;

        return new IdentityDbContext(options);
    }
}
