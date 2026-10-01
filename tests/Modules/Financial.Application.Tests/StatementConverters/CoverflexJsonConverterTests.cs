using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.StatementConversion;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

public sealed class CoverflexJsonConverterTests
{
    private readonly CoverflexJsonConverter _converter = new(NullLogger<CoverflexJsonConverter>.Instance);

    private StatementConversionResult Convert(string json) => _converter.Convert(new MemoryStream(Encoding.UTF8.GetBytes(json)), CancellationToken.None);

    private static JsonObject Movement(
        string executedAt, string description, long cents, bool isDebit, long? before, long? after,
        string status = "confirmed", string currency = "EUR", string type = "purchase")
        => new()
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["status"] = status,
            ["type"] = type,
            ["description"] = description,
            ["amount"] = new JsonObject { ["currency"] = currency, ["amount"] = cents },
            ["is_debit"] = isDebit,
            ["executed_at"] = executedAt,
            ["merchant_name"] = null,
            ["balance_before"] = before is null ? null : new JsonObject { ["currency"] = currency, ["amount"] = before },
            ["balance_after"] = after is null ? null : new JsonObject { ["currency"] = currency, ["amount"] = after },
            ["is_transfer_adjustment"] = false,
            ["pocket"] = new JsonObject { ["type"] = "meals" },   // campo extra: tem de ser ignorado
        };

    private static string Envelope(params JsonObject[] list)
        => new JsonObject
        {
            ["movements"] = new JsonObject
            {
                ["list"] = new JsonArray(list.Select(m => (JsonNode?)m).ToArray()),
                ["total_pages"] = 1,
                ["has_older"] = true,
            },
        }.ToJsonString();

    /// <summary>
    /// A API devolve do mais recente para o mais antigo. Saldo inicial 0,25 €:
    /// topup 211,20 → compra 9,37 → compra 132,47, mais 1 pendente e 2 cancelados.
    /// </summary>
    private static string SampleJson() => Envelope(
        Movement("2026-09-26T07:55:17.609000Z", "COMPRA PENDENTE EXEMPLO", 189, true, null, null, status: "pending"),
        Movement("2026-07-23T17:39:40.390000Z", "COMPRA CANCELADA EXEMPLO", 130, true, null, null, status: "cancelled"),
        Movement("2026-07-23T17:39:01.238000Z", "COMPRA CANCELADA EXEMPLO", 130, true, null, null, status: "cancelled"),
        Movement("2026-01-07T09:00:00Z", "COMPRA SUPERMERCADO EXEMPLO", 13247, true, 20208, 6961),
        Movement("2026-01-06T22:30:00Z", "COMPRA PADARIA EXEMPLO", 937, true, 21145, 20208),
        Movement("2026-01-06T09:53:29.506000Z", "CVFX79X5V490YH6K COVERFLEX TOPUP ITEMID:13AE6BC8-F42A-434E-AD9D-189DD6FB1DB1", 21120, false, 25, 21145, type: "transfer"));

    [Fact]
    public void Converts_cents_to_euros_keeping_only_confirmed_movements_in_execution_order()
    {
        var result = Convert(SampleJson());

        result.Format.Should().Be(StatementFormat.CoverflexJson);
        result.Rows.Select(r => (r.Description, r.SignedAmount, r.Balance)).Should().Equal(
            ("COVERFLEX TOPUP", 211.20m, 211.45m),
            ("COMPRA PADARIA EXEMPLO", -9.37m, 202.08m),
            ("COMPRA SUPERMERCADO EXEMPLO", -132.47m, 69.61m));
        result.BalanceBefore.Should().Be(0.25m);
        result.BalanceAfter.Should().Be(69.61m);
        result.Currency.Should().Be("EUR");
        result.HasRowBalances.Should().BeTrue();
        result.PendingIgnored.Should().Be(1);
        result.CancelledIgnored.Should().Be(2);
        result.Checks.Should().HaveCount(2).And.OnlyContain(c => c.Passed);
    }

    [Fact]
    public void Date_is_the_lisbon_date_not_the_utc_date()
    {
        var result = Convert(SampleJson());

        // 22:30Z de 6 de janeiro é 22:30 em Lisboa (inverno): continua dia 6.
        result.Rows[1].Date.Should().Be(new DateOnly(2026, 1, 6));
        result.Rows[1].ValueDate.Should().Be(result.Rows[1].Date);
    }

    [Theory]
    [InlineData("2026-09-01T22:19:39Z", 2026, 9, 1)]   // verão (UTC+1): 23:19 locais, mesmo dia
    [InlineData("2026-09-01T23:30:00Z", 2026, 9, 2)]   // verão: 00:30 locais, dia seguinte
    [InlineData("2026-01-06T23:30:00Z", 2026, 1, 6)]   // inverno (UTC+0): mesmo dia
    [InlineData("2026-03-29T00:59:59Z", 2026, 3, 29)]  // antes da mudança para a hora de verão
    [InlineData("2026-03-29T23:30:00Z", 2026, 3, 30)]  // depois: UTC+1
    [InlineData("2026-10-25T23:30:00Z", 2026, 10, 25)] // depois do fim do verão: UTC+0
    [InlineData("2026-10-24T23:30:00Z", 2026, 10, 25)] // antes: UTC+1
    public void Lisbon_date_follows_the_eu_summer_time_rule(string utc, int year, int month, int day)
        => CoverflexJsonConverter.LisbonDate(DateTimeOffset.Parse(utc)).Should().Be(new DateOnly(year, month, day));

    [Fact]
    public void Movement_near_midnight_utc_in_summer_lands_on_the_next_lisbon_day()
    {
        var json = Envelope(Movement("2026-07-10T23:15:00Z", "COMPRA EXEMPLO", 100, true, 1000, 900));

        Convert(json).Rows[0].Date.Should().Be(new DateOnly(2026, 7, 11));
    }

    [Fact]
    public void Topup_noise_is_normalized_so_rules_and_dedup_are_stable()
    {
        CoverflexJsonConverter.NormalizeDescription("CVFX79X5V490YH6K COVERFLEX TOPUP ITEMID:13AE6BC8-F42A")
            .Should().Be("COVERFLEX TOPUP");
        CoverflexJsonConverter.NormalizeDescription("COVERFLEX TOPUP").Should().Be("COVERFLEX TOPUP");
        CoverflexJsonConverter.NormalizeDescription("COMPRA   PINGO  DOCE; \"X\"").Should().Be("COMPRA PINGO DOCE, 'X'");
    }

    [Fact]
    public void Same_instant_keeps_the_reverse_of_file_order()
    {
        // Mesmo executed_at: a API lista do mais recente; a contabilização foi B depois de A.
        var json = Envelope(
            Movement("2026-02-01T10:00:00Z", "B SEGUNDO", 200, true, 800, 600),
            Movement("2026-02-01T10:00:00Z", "A PRIMEIRO", 200, true, 1000, 800));

        var result = Convert(json);

        result.Rows.Select(r => r.Description).Should().Equal("A PRIMEIRO", "B SEGUNDO");
        result.BalanceBefore.Should().Be(10.00m);
        result.BalanceAfter.Should().Be(6.00m);
    }

    [Fact]
    public void Execution_order_that_differs_from_the_balance_chain_still_validates()
    {
        // Contabilizado: Y (10,00→6,00) depois X (6,00→7,50); executado: X antes de Y.
        var json = Envelope(
            Movement("2026-02-02T10:00:00Z", "Y EXECUTADO DEPOIS", 400, true, 1000, 600),
            Movement("2026-02-02T09:00:00Z", "X EXECUTADO ANTES", 150, false, 600, 750));

        var result = Convert(json);

        result.Rows.Select(r => r.Description).Should().Equal("X EXECUTADO ANTES", "Y EXECUTADO DEPOIS");
        result.BalanceBefore.Should().Be(10.00m);
        result.BalanceAfter.Should().Be(7.50m);
    }

    // ---- Falhas ---------------------------------------------------------------------------

    [Fact]
    public void Invalid_json_is_unreadable()
        => FluentActions.Invoking(() => Convert("{ isto não é json"))
            .Should().Throw<StatementUnreadableException>();

    [Fact]
    public void Json_without_the_movements_envelope_is_not_supported()
        => FluentActions.Invoking(() => Convert("""{ "transactions": [] }"""))
            .Should().Throw<StatementLayoutNotSupportedException>().WithMessage("*movements.list*");

    [Fact]
    public void Empty_list_is_rejected()
        => FluentActions.Invoking(() => Convert(Envelope()))
            .Should().Throw<StatementValidationException>().WithMessage("*não tem movimentos*");

    [Fact]
    public void Only_pending_and_cancelled_is_rejected()
        => FluentActions.Invoking(() => Convert(Envelope(
                Movement("2026-09-26T07:55:17Z", "PENDENTE", 189, true, null, null, status: "pending"))))
            .Should().Throw<StatementValidationException>().WithMessage("*movimentos confirmados*");

    [Fact]
    public void Missing_required_field_names_the_movement_and_the_field()
    {
        var node = Movement("2026-02-01T10:00:00Z", "SEM VALOR", 100, true, 1000, 900);
        node.Remove("amount");

        FluentActions.Invoking(() => Convert(Envelope(node)))
            .Should().Throw<StatementValidationException>().WithMessage("Movimento 1*amount*");
    }

    [Fact]
    public void Confirmed_movement_without_balances_is_rejected()
        => FluentActions.Invoking(() => Convert(Envelope(
                Movement("2026-02-01T10:00:00Z", "SEM SALDOS", 100, true, null, null))))
            .Should().Throw<StatementValidationException>().WithMessage("Movimento 1*balance_before*");

    [Fact]
    public void Non_integer_cents_are_rejected()
    {
        var node = Movement("2026-02-01T10:00:00Z", "MEIO CENTIMO", 100, true, 1000, 900);
        node["amount"]!["amount"] = 100.5;

        FluentActions.Invoking(() => Convert(Envelope(node)))
            .Should().Throw<StatementValidationException>().WithMessage("*inteiro de cêntimos*");
    }

    [Fact]
    public void Per_line_arithmetic_that_fails_is_reported_with_expected_and_found()
        => FluentActions.Invoking(() => Convert(Envelope(
                Movement("2026-02-01T10:00:00Z", "ARITMETICA ERRADA", 100, true, 1000, 950))))
            .Should().Throw<StatementValidationException>().WithMessage("*esperado 9,00*encontrado 9,50*");

    [Fact]
    public void Missing_movement_breaks_the_global_identity()
    {
        // 10,00 → 9,00 e 7,00 → 6,00: falta o movimento 9,00 → 7,00.
        var json = Envelope(
            Movement("2026-02-02T10:00:00Z", "SEGUNDO", 100, true, 700, 600),
            Movement("2026-02-01T10:00:00Z", "PRIMEIRO", 100, true, 1000, 900));

        FluentActions.Invoking(() => Convert(json))
            .Should().Throw<StatementValidationException>().WithMessage("*faltam ou sobram movimentos*");
    }

    [Fact]
    public void Different_currencies_are_rejected()
        => FluentActions.Invoking(() => Convert(Envelope(
                Movement("2026-02-01T10:00:00Z", "EM EUROS", 100, true, 1000, 900),
                Movement("2026-02-02T10:00:00Z", "EM DOLARES", 100, true, 900, 800, currency: "USD"))))
            .Should().Throw<StatementValidationException>().WithMessage("*várias moedas*");

    [Fact]
    public void Unknown_status_is_rejected_instead_of_silently_dropped()
        => FluentActions.Invoking(() => Convert(Envelope(
                Movement("2026-02-01T10:00:00Z", "ESTADO NOVO", 100, true, 1000, 900, status: "reversed"))))
            .Should().Throw<StatementValidationException>().WithMessage("*estado desconhecido 'reversed'*");

    [Theory]
    [InlineData("coverflex.json", true)]
    [InlineData("COVERFLEX.JSON", true)]
    [InlineData("coverflex.csv", false)]
    public void Handles_only_json_files(string fileName, bool expected)
        => _converter.CanHandle(fileName).Should().Be(expected);

    [Fact]
    public void Converted_rows_adapt_to_the_canonical_csv()
    {
        var csv = CanonicalCsvAdapter.Adapt(Convert(SampleJson()));

        csv.Rows[0].Should().Equal("06/01/2026", "06/01/2026", "COVERFLEX TOPUP", "211,20", "211,45");
    }
}
