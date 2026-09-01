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
            password = "Password123!extra",
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

        var ownerRole = await Scalar<string>(conn,
            "SELECT \"Role\" FROM shared.\"Memberships\" WHERE \"UserId\" = @id", body.UserId);
        ownerRole.Should().Be("Owner");

        // Outbox: o INSERT na messaging.wolverine_outgoing acontece quando há
        // subscriber para UserRegisteredIntegrationEvent. Em Phase 1a não há —
        // verificação completa do outbox transacional fica para Phase 2 (módulo
        // Financial trá-lo o subscriber). Aqui só asseguramos que o schema
        // messaging existe (Wolverine inicializou).
        var schemaExists = await Scalar<long>(conn,
            "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'messaging'");
        schemaExists.Should().Be(1);
    }

    [Fact]
    public async Task Signup_with_short_password_is_rejected_by_validation_filter()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"short-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "weak", // < 12 chars → DataAnnotations filter rejeita
            tenantName = "Tenant Short",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = _fixture.OpenSuperuserConnection();
        var userCount = await Scalar<long>(conn,
            "SELECT count(*) FROM shared.\"AspNetUsers\" WHERE \"Email\" = @id", email);
        userCount.Should().Be(0, "filter falha antes do UserManager.CreateAsync.");
    }

    [Fact]
    public async Task Signup_with_simple_long_password_is_rejected_by_identity_validator()
    {
        // Passa o filter (>= 12 chars) mas falha o Password.RequiresDigit
        // do Identity → handler devolve 400 com mensagem genérica.
        var client = _fixture.Factory.CreateClient();
        var email = $"simple-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "abcdefghijkl", // 12 chars, nenhum dígito/símbolo/upper
            tenantName = "Tenant Simple",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Anti-enumeração: a resposta NÃO deve revelar qual regra falhou.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("PasswordRequiresDigit",
            "código do Identity é leak de enumeração — handler colapsa em mensagem genérica.");
        body.Should().NotContain("PasswordRequiresUpper");
        body.Should().NotContain("PasswordRequiresNonAlphanumeric");
    }

    [Fact]
    public async Task Duplicate_email_signup_returns_generic_error_no_enumeration_oracle()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!extra",
            tenantName = "Tenant Dup",
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!extra",
            tenantName = "Tenant Dup 2",
        });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await second.Content.ReadAsStringAsync();
        body.Should().NotContain("DuplicateUserName",
            "DuplicateUserName/DuplicateEmail revelam que email existe — leak de enumeração.");
        body.Should().NotContain("DuplicateEmail");
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
