using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Auth;

namespace Sextante.IntegrationTests.MultiTenancy;

/// <summary>
/// Regressão: connections devolvidas ao pool com <c>app.current_tenant_id</c>
/// definido não devem permitir um request anonymous subsequente ler dados
/// do tenant anterior. O <c>TenantConnectionInterceptor</c> tem de
/// reescrever o GUC em cada checkout (sentinel para anonymous) e fazer
/// reset no close.
/// </summary>
public sealed class PoolConnectionLeakTests : IClassFixture<IdentityIntegrationFixture>
{
    private static readonly Guid AnonymousSentinel =
        new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly IdentityIntegrationFixture _fixture;

    public PoolConnectionLeakTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Anonymous_sentinel_filters_all_rows_at_RLS_layer()
    {
        // Invariante de schema: WITH/USING policies dependem estritamente
        // de current_setting('app.current_tenant_id') — o sentinel anonymous
        // não bate com nenhum tenant_id real (CHECK constraint garante).
        await using var conn = _fixture.OpenAppConnection();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Seed via superuser (bypassa RLS).
        await using (var super = _fixture.OpenSuperuserConnection())
        {
            await using var seed = super.CreateCommand();
            seed.CommandText = """
                INSERT INTO shared."AspNetUsers"
                    ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                     "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                     "LockoutEnabled", "AccessFailedCount")
                VALUES
                    (@u1, 'pool-a@x.com', 'POOL-A@X.COM', 'pool-a@x.com', 'POOL-A@X.COM', false, false, false, false, 0),
                    (@u2, 'pool-b@x.com', 'POOL-B@X.COM', 'pool-b@x.com', 'POOL-B@X.COM', false, false, false, false, 0);
                INSERT INTO shared."Tenants"("Id", "Name", "CreatedAt", "UpdatedAt", "Version")
                VALUES (@a, 'Pool-A', now(), now(), 1), (@b, 'Pool-B', now(), now(), 1);
                INSERT INTO shared."Memberships"("Id", "UserId", tenant_id, "Role", "CreatedAt", "UpdatedAt", "Version")
                VALUES
                    (@m1, @u1, @a, 'Owner', now(), now(), 1),
                    (@m2, @u2, @b, 'Owner', now(), now(), 1);
                """;
            seed.Parameters.AddWithValue("a", tenantA);
            seed.Parameters.AddWithValue("b", tenantB);
            seed.Parameters.AddWithValue("m1", Guid.NewGuid());
            seed.Parameters.AddWithValue("m2", Guid.NewGuid());
            seed.Parameters.AddWithValue("u1", userA);
            seed.Parameters.AddWithValue("u2", userB);
            await seed.ExecuteNonQueryAsync();
        }

        // GUC=tenantA → vê só A.
        await SetGuc(conn, tenantA);
        (await CountMemberships(conn)).Should().Be(1, "RLS filtra para tenant A.");

        // GUC=tenantB → vê só B.
        await SetGuc(conn, tenantB);
        (await CountMemberships(conn)).Should().Be(1, "GUC novo, vê só B.");

        // GUC=sentinel → vê zero rows.
        await SetGuc(conn, AnonymousSentinel);
        (await CountMemberships(conn)).Should().Be(0, "sentinel anonymous oculta tudo.");
    }

    [Fact]
    public async Task Interceptor_does_not_leak_tenant_between_consecutive_DI_scopes()
    {
        // Setup: signup duas tenants via HTTP (cada uma fica com a sua row
        // em Memberships). Em seguida, manipulamos o IHttpContextAccessor
        // singleton da factory (AsyncLocal flows com este flow async),
        // forçando o pipeline a ver "autenticado como A" → "anonymous"
        // → "autenticado como B" sem fazer requests reais. Cada operação
        // resolve um IdentityDbContext novo via DI scope, exercitando
        // o TenantConnectionInterceptor de fim a fim.

        var client = _fixture.Factory.CreateClient();
        var (userA, tenantA) = await Signup(client, "scope-A");
        var (userB, tenantB) = await Signup(client, "scope-B");

        var accessor = _fixture.Factory.Services.GetRequiredService<IHttpContextAccessor>();

        // 1. Como A: vê 1 membership (a sua).
        accessor.HttpContext = BuildAuthenticatedContext(userA, tenantA);
        var seenAsA = await CountMembershipsViaContext();
        seenAsA.Should().Be(1, "interceptor deve setar GUC=tenantA → RLS mostra só a row de A.");

        // 2. Anonymous (HttpContext=null): interceptor escreve sentinel,
        //    RLS oculta tudo. Se o interceptor não fosse invocado por
        //    causa de pool reuse, veria a row de A (leak).
        accessor.HttpContext = null;
        var seenAnon = await CountMembershipsViaContext();
        seenAnon.Should().Be(0, "anonymous → sentinel → RLS oculta tudo (sem leak).");

        // 3. Como B: vê 1 membership (a de B), não a de A.
        accessor.HttpContext = BuildAuthenticatedContext(userB, tenantB);
        var seenAsB = await CountMembershipsViaContext();
        seenAsB.Should().Be(1, "swap de tenant entre scopes não traz state do anterior.");
    }

    private async Task<int> CountMembershipsViaContext()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // IgnoreQueryFilters para isolar o efeito da RLS (filtro EF é
        // redundante quando RLS está ativo, mas no scope anonymous o
        // filtro lança porque tenta resolver TenantContext.TenantId).
        return await ctx.Memberships.IgnoreQueryFilters().CountAsync();
    }

    private static HttpContext BuildAuthenticatedContext(Guid userId, Guid tenantId)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(IdentityClaimTypes.TenantId, tenantId.ToString()),
                new Claim(IdentityClaimTypes.TenantRole, "Owner"),
            },
            authenticationType: "TestBearer");

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
    }

    private async Task<(Guid UserId, Guid TenantId)> Signup(HttpClient client, string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!extra",
            tenantName = $"Tenant {label}",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignupBody>();
        return (body!.UserId, body.TenantId);
    }

    private static async Task SetGuc(NpgsqlConnection conn, Guid value)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @t, false)";
        cmd.Parameters.AddWithValue("t", value.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountMemberships(NpgsqlConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM shared.\"Memberships\"";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record SignupBody(Guid UserId, Guid TenantId);
}
