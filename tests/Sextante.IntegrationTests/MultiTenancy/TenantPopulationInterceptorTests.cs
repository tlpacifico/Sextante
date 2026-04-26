using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Auth;
using Sextante.SharedKernel;

namespace Sextante.IntegrationTests.MultiTenancy;

/// <summary>
/// Plan §8.5 — entidades <see cref="ITenantOwned"/> sem TenantId explícito
/// devem receber o tenant da request corrente via
/// <c>TenantPopulationInterceptor</c>. Sem teste, o interceptor podia
/// estar dead code (signup hand-passa TenantId, então não exercita o
/// caminho de auto-população).
/// </summary>
public sealed class TenantPopulationInterceptorTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TenantPopulationInterceptorTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Membership_added_without_TenantId_is_populated_from_TenantContext()
    {
        // Setup: signup user A (cria membership(A, tA, Owner)). Depois,
        // criamos um segundo AppUser e adicionamos uma Membership para
        // ele com tenantId=Guid.Empty (default). O interceptor tem de
        // popular com o tenant da request corrente (tA).
        var client = _fixture.Factory.CreateClient();
        var emailA = $"pop-owner-{Guid.NewGuid():N}@example.com";
        var signupA = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = emailA,
            password = "Password123!extra",
            tenantName = "Tenant Pop",
        });
        signupA.EnsureSuccessStatusCode();
        var signupBody = await signupA.Content.ReadFromJsonAsync<SignupBody>();
        var ownerUserId = signupBody!.UserId;
        var tenantA = signupBody.TenantId;

        // Cria user2 via UserManager (sem signup → sem tenant).
        Guid memberUserId;
        using (var setupScope = _fixture.Factory.Services.CreateScope())
        {
            var userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user2 = new AppUser
            {
                Id = GuidV7.NewId(),
                UserName = $"member-{Guid.NewGuid():N}@example.com",
                Email = $"member-{Guid.NewGuid():N}@example.com",
            };
            // Quando criar este user, o UserManager corre saves contra a DB
            // — precisa de um GUC válido. AspNetUsers não é tenant-owned
            // (não tem coluna tenant_id), portanto não passa pela RLS
            // policy; basta o sentinel ou qualquer GUC.
            (await userManager.CreateAsync(user2, "Password123!extra")).Succeeded.Should().BeTrue();
            memberUserId = user2.Id;
        }

        // Agora: simula request autenticada como owner A; resolve
        // IdentityDbContext; adiciona Membership(member, TenantId=default).
        // Interceptor deve popular TenantId = tA.
        var accessor = _fixture.Factory.Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = BuildAuthenticatedContext(ownerUserId, tenantA);

        try
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var membership = new Membership
            {
                Id = GuidV7.NewId(),
                UserId = memberUserId,
                // TenantId = default → valor interno Guid.Empty. O contrato do
                // tipo torna-o `required` para forçar inicialização explícita,
                // mas para exercitar o interceptor passamos sentinel-default.
                // Se o interceptor não correr, o INSERT falha pelo CHECK
                // constraint CK_Memberships_TenantId_NotSentinel — sintoma
                // diferente do esperado e fácil de diagnosticar.
                TenantId = default,
                Role = MembershipRole.Member,
            };
            ctx.Memberships.Add(membership);
            await ctx.SaveChangesAsync();

            membership.TenantId.Value.Should().Be(tenantA,
                "TenantPopulationInterceptor deve ter copiado tenant da request.");

            // Confirma diretamente na DB.
            await using var super = _fixture.OpenSuperuserConnection();
            await using var cmd = super.CreateCommand();
            cmd.CommandText = "SELECT tenant_id FROM shared.\"Memberships\" WHERE \"Id\" = @id";
            cmd.Parameters.AddWithValue("id", membership.Id);
            var persistedTenant = (Guid)(await cmd.ExecuteScalarAsync())!;
            persistedTenant.Should().Be(tenantA);
        }
        finally
        {
            accessor.HttpContext = null;
        }
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

    private sealed record SignupBody(Guid UserId, Guid TenantId);
}
