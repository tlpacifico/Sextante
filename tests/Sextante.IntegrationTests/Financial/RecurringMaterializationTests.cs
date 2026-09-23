using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sextante.Infrastructure.Jobs;
using Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;

namespace Sextante.IntegrationTests.Financial;

public sealed class RecurringMaterializationTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public RecurringMaterializationTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Materializer_creates_transaction_for_due_daily_rule()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mat");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateCategoryAsync(client);

        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Daily coffee",
            amount = 3.50m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = new[] { "fixa" },
        });
        ruleResponse.EnsureSuccessStatusCode();
        var rule = await ruleResponse.Content.ReadFromJsonAsync<RuleIdentity>();

        // Invocar o materializer via TenantAwareJob.
        await InvokeMaterializerAsync(tenantId, today);

        // Verificar que a transacção foi criada com RecurringRuleId.
        var page = await client.GetFromJsonAsync<TxPage>("/api/financial/transactions?pageSize=50");
        page!.Items.Should().HaveCount(1);
        page.Items[0].RecurringRuleId.Should().Be(rule!.Id);
        page.Items[0].Description.Should().Be("Daily coffee");
    }

    [Fact]
    public async Task Rule_on_archived_income_category_still_materializes_as_income()
    {
        // Revisão do grupo 1 (Important 1): arquivar a categoria de uma
        // recorrente não pode transformar uma receita numa despesa.
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mat-arch");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accountId = await CreateAccountAsync(client);
        var categoryResponse = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name = "Salário",
            kind = 1,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        categoryResponse.EnsureSuccessStatusCode();
        var incomeCategory = (await categoryResponse.Content.ReadFromJsonAsync<RuleIdentity>())!.Id;

        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Vencimento",
            amount = 1000m,
            currency = "EUR",
            accountId,
            categoryId = incomeCategory,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();

        await using (var super = _fixture.OpenSuperuserConnection())
        await using (var archive = new Npgsql.NpgsqlCommand(
            "UPDATE financial.categories SET deleted_at = now() WHERE id = @id", super))
        {
            archive.Parameters.AddWithValue("id", incomeCategory);
            await archive.ExecuteNonQueryAsync();
        }

        await InvokeMaterializerAsync(tenantId, today);

        var range = "dateFrom=" + Uri.EscapeDataString(today.AddDays(-1).ToDateTime(TimeOnly.MinValue).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(1).ToString("O"));
        var summary = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/financial/transactions/summary?{range}");
        summary.GetProperty("income").GetProperty("amount").GetDecimal().Should().Be(1000m);
        summary.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Materializer_is_idempotent_skips_already_materialized_occurrence()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "idem");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateCategoryAsync(client);

        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Idempotent rule",
            amount = 10m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();

        // Primeira invocação: cria a transacção.
        await InvokeMaterializerAsync(tenantId, today);

        // Segunda invocação: mesma data — o HasMaterializedAsync deve
        // interceptar e pular a ocorrência já materializada.
        await InvokeMaterializerAsync(tenantId, today);

        var page = await client.GetFromJsonAsync<TxPage>("/api/financial/transactions?pageSize=50");
        page!.Items.Should().HaveCount(1, "segunda execução não deve duplicar a transacção");
    }

    [Fact]
    public async Task Rule_completes_when_end_date_is_reached_by_materialization()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "comp");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateCategoryAsync(client);

        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Single occurrence only",
            amount = 50m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = today, // 1 ocorrência apenas
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();
        var rule = await ruleResponse.Content.ReadFromJsonAsync<RuleIdentity>();

        await InvokeMaterializerAsync(tenantId, today);

        // Depois da materialização, a regra deve estar completed (NextOccurrence = null).
        var getRule = await client.GetFromJsonAsync<RuleDetail>($"/api/financial/recurring-rules/{rule!.Id}");
        getRule!.NextOccurrence.Should().BeNull("endDate foi atingido — regra completed");
    }

    [Fact]
    public async Task Multi_tenant_isolation_tenant_A_cannot_see_tenant_B_transactions()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var (clientA, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "iso-a");
        var (clientB, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "iso-b");

        var accountA = await CreateAccountAsync(clientA);
        var categoryA = await CreateCategoryAsync(clientA);

        var accountB = await CreateAccountAsync(clientB);
        var categoryB = await CreateCategoryAsync(clientB);

        // Criar regra no tenant A.
        var ruleAResponse = await clientA.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Rule A",
            amount = 20m,
            currency = "EUR",
            accountId = accountA,
            categoryId = categoryA,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleAResponse.EnsureSuccessStatusCode();

        // Criar regra no tenant B.
        var ruleBResponse = await clientB.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Rule B",
            amount = 30m,
            currency = "EUR",
            accountId = accountB,
            categoryId = categoryB,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleBResponse.EnsureSuccessStatusCode();

        // Materializar apenas o tenant A.
        await InvokeMaterializerAsync(tenantA, today);

        // Tenant A vê a sua transacção.
        var pageA = await clientA.GetFromJsonAsync<TxPage>("/api/financial/transactions?pageSize=50");
        pageA!.Items.Should().HaveCount(1);
        pageA.Items[0].Description.Should().Be("Rule A");

        // Tenant B não vê transacções — a materialização foi só para A.
        var pageB = await clientB.GetFromJsonAsync<TxPage>("/api/financial/transactions?pageSize=50");
        pageB!.Items.Should().BeEmpty("tenant B não foi materializado");

        // Barreira RLS: tenant B não consegue ver a row de A mesmo com raw SQL.
        await using var appConn = _fixture.OpenAppConnection();
        await ExecuteAsync(appConn,
            "SELECT set_config('app.current_tenant_id', @tid, false)",
            ("tid", tenantB.ToString()));

        await using var countCmd = new NpgsqlCommand(
            "SELECT count(*) FROM financial.transactions WHERE tenant_id = @ta",
            appConn);
        countCmd.Parameters.AddWithValue("ta", tenantA);
        var visibleToB = (long)(await countCmd.ExecuteScalarAsync())!;
        visibleToB.Should().Be(0, "RLS esconde a transaction de A do tenant B");
    }

    [Fact]
    public async Task Multi_currency_rule_is_skipped_when_exchange_rate_unavailable()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "nofx");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accountId = await CreateAccountAsync(client, "EUR");
        var categoryId = await CreateCategoryAsync(client);

        // Criar regra com currency JPY. A primary do tenant é EUR.
        // shared.exchange_rates é reference data global (sem tenant_id);
        // usamos JPY (em vez de USD) para evitar colisão com seeds de
        // outros testes nesta mesma class fixture (e.g.
        // Multi_currency_rule_materializes_with_exchange_rate_when_rate_is_seeded
        // semeia EUR→USD). Sem snapshot ECB corrido para JPY, o
        // materializer deve pular por falta de taxa.
        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "JPY subscription",
            amount = 1000m,
            currency = "JPY",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();

        await InvokeMaterializerAsync(tenantId, today);

        // Nenhuma transacção deve ter sido criada — SkippedNoRate.
        var page = await client.GetFromJsonAsync<TxPage>("/api/financial/transactions?pageSize=50");
        page!.Items.Should().BeEmpty("sem taxa de câmbio JPY→EUR — ocorrência foi saltada");
    }

    [Fact]
    public async Task Multi_currency_rule_materializes_with_exchange_rate_when_rate_is_seeded()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "fxok");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var accountId = await CreateAccountAsync(client, "EUR");
        var categoryId = await CreateCategoryAsync(client);

        // Semear rate USD→EUR no shared.exchange_rates via superuser.
        await using var superConn = _fixture.OpenSuperuserConnection();
        await using var seedCmd = new NpgsqlCommand(
            """
            INSERT INTO shared."exchange_rates"
              ("id", "rate_date", "from_currency", "to_currency", "rate", "source",
               "created_at", "updated_at", "version")
            VALUES
              (@id, @rateDate, 'EUR', 'USD', @rate, 'manual',
               now(), now(), 1)
            ON CONFLICT ("rate_date", "from_currency", "to_currency") DO NOTHING
            """,
            superConn);
        seedCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        seedCmd.Parameters.AddWithValue("rateDate", today);
        seedCmd.Parameters.AddWithValue("rate", 1.09m);
        await seedCmd.ExecuteNonQueryAsync();

        var ruleResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "USD with rate",
            amount = 10m,
            currency = "USD",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();
        var rule = await ruleResponse.Content.ReadFromJsonAsync<RuleIdentity>();

        await InvokeMaterializerAsync(tenantId, today);

        // Verificar via HTTP API que a transacção foi criada com ExchangeRateToPrimary.
        // Convenção do projeto (ExchangeRateSnapshot): Rate é o multiplicador
        // de currency origem → tenant primary. ECB cota EUR→X; aqui resolvemos
        // USD→EUR, logo a rate efetiva é 1/1.09 ≈ 0.91743119 (ver
        // CrossRate.Compute quando to == EUR).
        var page = await client.GetFromJsonAsync<TxPageWithRate>("/api/financial/transactions?pageSize=50");
        page!.Items.Should().HaveCount(1);
        var tx = page.Items[0];
        tx.RecurringRuleId.Should().Be(rule!.Id);
        tx.Amount.Currency.Should().Be("USD");
        tx.ExchangeRateToPrimary.Should().BeApproximately(1m / 1.09m, 0.0000001m);
    }

    [Fact]
    public async Task RLS_prevents_cross_tenant_read_of_recurring_rules()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Tenant A cria a regra.
        var (clientA, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-a");
        var accountA = await CreateAccountAsync(clientA);
        var categoryA = await CreateCategoryAsync(clientA);

        var ruleResponse = await clientA.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Rule tenant A",
            amount = 10m,
            currency = "EUR",
            accountId = accountA,
            categoryId = categoryA,
            frequency = "Daily",
            interval = 1,
            startDate = today,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        ruleResponse.EnsureSuccessStatusCode();

        // Tenant B — signup apenas, sem criar regras.
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-b");

        // Sanity: a regra existe (visível como superuser).
        await using var super = _fixture.OpenSuperuserConnection();
        await using var superRead = super.CreateCommand();
        superRead.CommandText =
            "SELECT count(*) FROM financial.recurring_rules WHERE tenant_id = @a";
        superRead.Parameters.AddWithValue("a", tenantA);
        var superCount = (long)(await superRead.ExecuteScalarAsync())!;
        superCount.Should().Be(1, "superuser vê a regra de A confirmando que existe");

        // Como sextante_app + GUC=tenantB: SELECT vê 0 regras do tenant A.
        await using var appConn = _fixture.OpenAppConnection();
        await ExecuteAsync(appConn,
            "SELECT set_config('app.current_tenant_id', @tid, false)",
            ("tid", tenantB.ToString()));

        await using var read = appConn.CreateCommand();
        read.CommandText =
            "SELECT count(*) FROM financial.recurring_rules WHERE tenant_id = @a";
        read.Parameters.AddWithValue("a", tenantA);
        var count = (long)(await read.ExecuteScalarAsync())!;
        count.Should().Be(0,
            "RLS USING clause torna a regra de A invisível para B mesmo com WHERE explícito");
    }

    private async Task InvokeMaterializerAsync(Guid tenantId, DateOnly runDate)
    {
        var scopeFactory = _fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TenantAwareJob<RecurringMaterializerPayload>>();
        await job.RunAsync(tenantId, new RecurringMaterializerPayload(runDate), CancellationToken.None);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string currency = "EUR")
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta Teste",
            type = 0,
            currency,
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, int kind = 0)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name = "Despesa Teste",
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection conn,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    // -- DTOs de deserialização --

    private sealed record IdRow(Guid Id);

    private sealed record RuleIdentity(Guid Id);

    private sealed record RuleDetail(Guid Id, DateOnly? NextOccurrence);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record TxPage(IReadOnlyList<TxRow> Items);

    private sealed record TxRow(
        Guid Id,
        Guid? RecurringRuleId,
        string? Description);

    private sealed record TxPageWithRate(IReadOnlyList<TxWithRate> Items);

    private sealed record TxWithRate(
        Guid Id,
        Guid? RecurringRuleId,
        MoneyValue Amount,
        decimal? ExchangeRateToPrimary);

}
