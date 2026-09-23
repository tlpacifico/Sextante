using System.Net.Http.Json;
using System.Text;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6 (grupo 1.4) — export CSV das transações. O ficheiro tem de
/// respeitar os filtros activos, abrir no Excel PT-PT (BOM + separador
/// <c>;</c>) e nunca atravessar fronteiras de tenant.
/// </summary>
public sealed class TransactionExportTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TransactionExportTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Export_returns_csv_with_header_and_filtered_rows()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "export");
        var account = await CreateAccount(client);
        var category = await CreateCategory(client);

        await PostTransaction(client, account, category, 10m, "Supermercado Lidl");
        await PostTransaction(client, account, category, 20m, "Combustível");

        var response = await client.GetAsync(
            "/api/financial/transactions/export?descriptionContains=supermercado");

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Export falhou ({(int)response.StatusCode}): {body}");
        }

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain("transacoes");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble(), "o Excel PT-PT precisa do BOM");

        var csv = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(2, "cabeçalho + 1 linha que passa o filtro");
        lines[0].Should().StartWith("Data;Conta;Categoria;Tipo;Descrição;Valor");
        lines[1].Should().Contain("Supermercado Lidl").And.Contain("10,00");
        csv.Should().NotContain("Combustível");
    }

    [Fact]
    public async Task Export_never_includes_rows_from_another_tenant()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "exp-a");
        var accountA = await CreateAccount(clientA);
        var categoryA = await CreateCategory(clientA);
        await PostTransaction(clientA, accountA, categoryA, 42m, "Segredo do tenant A");

        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "exp-b");
        var accountB = await CreateAccount(clientB);
        var categoryB = await CreateCategory(clientB);
        await PostTransaction(clientB, accountB, categoryB, 7m, "Compra do tenant B");

        var csvB = await clientB.GetStringAsync("/api/financial/transactions/export");

        csvB.Should().Contain("Compra do tenant B");
        csvB.Should().NotContain("Segredo do tenant A");
        csvB.Should().NotContain("42,00");
    }

    [Fact]
    public async Task Export_labels_transfer_pair_with_type_and_counterpart_account_name()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "export-xfer");
        var fromId = await CreateAccount(client, "Conta Origem");
        var toId = await CreateAccount(client, "Conta Destino");

        var transferResponse = await client.PostAsJsonAsync("/api/financial/transfers", new
        {
            fromAccountId = fromId,
            toAccountId = toId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amountOut = 75m,
            amountIn = (decimal?)null,
            description = "movimento",
        });
        transferResponse.EnsureSuccessStatusCode();

        var csv = await client.GetStringAsync("/api/financial/transactions/export");
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3, "cabeçalho + as duas pernas");
        lines[0].Should().EndWith("Conta contraparte");

        var outLegLine = lines.Should().ContainSingle(l => l.Contains("Conta Origem;") && l.Contains("Transferência")).Subject;
        outLegLine.Should().EndWith("Conta Destino");

        var inLegLine = lines.Should().ContainSingle(l => l.Contains("Conta Destino;") && l.Contains("Transferência")).Subject;
        inLegLine.Should().EndWith("Conta Origem");
    }

    private static async Task<Guid> CreateAccount(HttpClient client, string name = "Conta Corrente")
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name,
            type = 0,
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateCategory(HttpClient client, int kind = 0, string name = "Alimentação")
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task PostTransaction(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        string description)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-(double)amount),
            amount,
            description,
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdRow(Guid Id);
}
