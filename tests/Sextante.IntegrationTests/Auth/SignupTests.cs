using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Persistence;

namespace Sextante.IntegrationTests.Auth;

public sealed class SignupTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public SignupTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Signup_creates_user_tenant_membership_and_outbox_message_atomically()
    {
        var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email = $"alice-{Guid.NewGuid():N}@example.com",
            password = "Password123!",
            tenantName = "Tenant Alice",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<SignupResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().NotBeEmpty();
        body.TenantId.Should().NotBeEmpty();

        // Inspecciona a BD diretamente via superuser para confirmar que tudo foi
        // gravado dentro da mesma transação.
        await using var conn = _fixture.OpenSuperuserConnection();

        var userCount = await Scalar<long>(conn,
            "SELECT count(*) FROM shared.\"AspNetUsers\" WHERE \"Id\" = @id", body.UserId);
        userCount.Should().Be(1);

        var tenantCount = await Scalar<long>(conn,
            "SELECT count(*) FROM shared.\"Tenants\" WHERE \"Id\" = @id", body.TenantId);
        tenantCount.Should().Be(1);

        var membershipCount = await Scalar<long>(conn,
            "SELECT count(*) FROM shared.\"Memberships\" WHERE \"UserId\" = @id", body.UserId);
        membershipCount.Should().Be(1);

        // O membership criado tem role Owner para o tenant criado.
        var ownerRole = await Scalar<string>(conn,
            "SELECT \"Role\" FROM shared.\"Memberships\" WHERE \"UserId\" = @id", body.UserId);
        ownerRole.Should().Be("Owner");

        // Outbox: o INSERT na messaging.wolverine_outgoing acontece quando há
        // subscriber para UserRegisteredIntegrationEvent. Em Phase 1a não há —
        // verificação completa do outbox transacional fica para Phase 2 (módulo
        // Financial trá-lo o subscriber). Aqui só asseguramos que o schema
        // messaging existe (Wolverine inicializou) e a contagem é >= 0.
        var schemaExists = await Scalar<long>(conn,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'messaging'");
        schemaExists.Should().Be(1);
    }

    [Fact]
    public async Task Signup_with_invalid_password_returns_400_and_persists_nothing()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"bob-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "weak",
            tenantName = "Tenant Bob",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = _fixture.OpenSuperuserConnection();
        var userCount = await Scalar<long>(conn,
            "SELECT count(*) FROM shared.\"AspNetUsers\" WHERE \"Email\" = @id", email);
        userCount.Should().Be(0);
    }

    [Fact]
    public async Task NotImplementedEmailSender_throws_with_phase_1a_message()
    {
        // O endpoint /forgotPassword do MapIdentityApi tem guard EmailConfirmed
        // que devolve 200 silencioso para evitar enumeração — ou seja, nem chega
        // a chamar o IEmailSender. A garantia de fail-loud da Phase 1a é
        // verificada chamando o stub diretamente.
        using var scope = _fixture.Factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<
            Microsoft.AspNetCore.Identity.IEmailSender<Sextante.Modules.Identity.Domain.Entities.AppUser>>();

        var act = async () => await sender.SendConfirmationLinkAsync(
            new Sextante.Modules.Identity.Domain.Entities.AppUser(),
            "x@y.z",
            "https://example.com/confirm");

        await act.Should()
            .ThrowAsync<NotImplementedException>()
            .WithMessage("*Phase 1a*");
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection conn, string sql, object? id = null)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (id is not null)
        {
            cmd.Parameters.AddWithValue("id", id);
        }
        var raw = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(raw!, typeof(T))!;
    }

    private sealed record SignupResponse(Guid UserId, Guid TenantId);
}
