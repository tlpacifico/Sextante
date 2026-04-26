using JasperFx.Resources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine.Persistence.Durability;

namespace Sextante.IntegrationTests;

/// <summary>
/// Arranca uma Postgres real via Testcontainers, faz bootstrap dos roles
/// (sextante_migrations + sextante_app), e expõe um
/// <see cref="WebApplicationFactory{Program}"/> configurado contra essa BD.
///
/// Reutilizar via <c>IClassFixture&lt;IdentityIntegrationFixture&gt;</c> em
/// classes de teste — uma BD por classe, partilhada entre testes.
/// </summary>
public sealed class IdentityIntegrationFixture : IAsyncLifetime
{
    private const string AppPassword = "sextante_app_pw";
    private const string MigrationsPassword = "sextante_migrations_pw";

    private PostgreSqlContainer? _container;
    private WebApplicationFactory<Program>? _factory;

    public string MigrationConnectionString { get; private set; } = string.Empty;
    public string AppConnectionString { get; private set; } = string.Empty;
    public string SuperuserConnectionString { get; private set; } = string.Empty;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Factory not initialized — InitializeAsync first.");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sextante_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        SuperuserConnectionString = _container.GetConnectionString();
        var dbName = new NpgsqlConnectionStringBuilder(SuperuserConnectionString).Database!;

        await BootstrapRolesAsync(SuperuserConnectionString, dbName);

        MigrationConnectionString = ToConnectionString(_container, "sextante_migrations", MigrationsPassword);
        AppConnectionString = ToConnectionString(_container, "sextante_app", AppPassword);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                builder.UseSetting("ConnectionStrings:Migration", MigrationConnectionString);
                builder.UseSetting("ConnectionStrings:App", AppConnectionString);
                builder.UseSetting("JWT__SIGNING_KEY", "test-signing-key-for-integration-tests");
            });

        // Force factory startup to apply migrations.
        using var _ = _factory.CreateClient();

        // Wolverine não cria sempre o messaging schema na hosted service —
        // forçar via IMessageStoreAdmin.RebuildAsync garante que as tabelas
        // messaging.* existem antes de qualquer teste correr.
        var messageStore = _factory.Services.GetService<IMessageStore>();
        if (messageStore is not null)
        {
            await messageStore.Admin.RebuildAsync();
        }
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task BootstrapRolesAsync(string superuserConnectionString, string databaseName)
    {
        var bootstrapSql = IdentityRoleBootstrap.BuildSql(MigrationsPassword, AppPassword);
        var grantSql = IdentityRoleBootstrap.BuildGrantSql(databaseName);

        await using var conn = new NpgsqlConnection(superuserConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(bootstrapSql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(grantSql, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string ToConnectionString(PostgreSqlContainer container, string username, string password)
    {
        var baseConn = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Username = username,
            Password = password,
        };
        return baseConn.ConnectionString;
    }

    /// <summary>
    /// Connection direta como superuser para testes que precisam de
    /// observar / forçar o estado da BD fora do pipeline da aplicação.
    /// </summary>
    public NpgsqlConnection OpenSuperuserConnection()
    {
        var conn = new NpgsqlConnection(SuperuserConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Connection como sextante_app para validar que o role NOBYPASSRLS
    /// é mesmo bloqueado pela RLS quando o tenant não está definido.
    /// </summary>
    public NpgsqlConnection OpenAppConnection()
    {
        var conn = new NpgsqlConnection(AppConnectionString);
        conn.Open();
        return conn;
    }
}
