using System.Text;
using FluentAssertions;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

/// <summary>
/// Phase 6.6 (5.2/5.5) — conversão → corte de overlap → preview → lote; e que uma
/// validação que falha não cria nenhum lote (D3).
/// </summary>
public sealed class StatementUploadServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private sealed class FakeConverter(Func<StatementConversionResult> convert, string extension = ".xlsx") : IStatementConverter
    {
        public StatementFormat Format => StatementFormat.ActivoBankAccountXlsx;
        public bool CanHandle(string fileName) => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        public StatementConversionResult Convert(Stream stream, CancellationToken ct) => convert();
    }

    private sealed class RecordingBatchRepository : IImportBatchRepository
    {
        public List<ImportBatch> Added { get; } = [];
        public Task<ImportBatch?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Added.FirstOrDefault(b => b.Id == id));
        public Task<IReadOnlyList<ImportBatch>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ImportBatch>>(Added);
        public Task AddAsync(ImportBatch batch, CancellationToken ct) { Added.Add(batch); return Task.CompletedTask; }
        public void Update(ImportBatch batch) { }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class NoRules : ICategorizationRuleEngine
    {
        public Task<IReadOnlyList<CategorizationMatchResult>> ApplyAsync(
            IReadOnlyList<TransactionToCategorize> transactions, TenantId tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CategorizationMatchResult>>(
                transactions.Select(t => new CategorizationMatchResult(t.TransactionId, null, null)).ToList());
    }

    private sealed class FixedTenant : ITenantContext
    {
        public TenantId TenantId => Tenant;
    }

    private readonly Account _account = Account.Create(
        "Conta à ordem", AccountType.Checking, "EUR", new Money(0m, "EUR"), Tenant, new DateOnly(2026, 1, 1));

    private readonly RecordingBatchRepository _batches = new();

    private static StatementRow Row(string date, string description, decimal amount, decimal balance)
    {
        var d = DateOnly.ParseExact(date, "yyyy-MM-dd");
        return new StatementRow(d, d, description, amount, balance);
    }

    private static StatementConversionResult Statement(string currency = "EUR") => new(
        StatementFormat.ActivoBankAccountXlsx,
        [
            Row("2026-09-02", "TRF. VENCIMENTO", 1500.00m, 1500.00m),
            Row("2026-09-03", "COMPRA PADARIA", -3.20m, 1496.80m),
        ],
        0m, 1496.80m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3),
        [new StatementCheck("Saldo encadeado linha a linha", true, "1496,80", "1496,80")],
        currency);

    private Task<UploadCsvResponse> Upload(
        IStatementConverter converter, FakeAccountLedger? ledger = null, string fileName = "extrato.xlsx", Guid? accountId = null)
    {
        ledger ??= new FakeAccountLedger(0m);
        return StatementUploadService.UploadAsync(
            new MemoryStream("x"u8.ToArray()), fileName, accountId ?? _account.Id,
            [converter], new StatementOverlapTrimmer(ledger, ledger),
            _batches, new StubAccountRepository(_account), new InMemoryCategoryRepository(),
            new NoRules(), new StubTransferCounterpartQuery(),
            new FixedTenant(), CancellationToken.None);
    }

    [Fact]
    public async Task Converted_statement_becomes_a_preview_batch_with_the_statement_summary()
    {
        var response = await Upload(new FakeConverter(() => Statement()));

        response.TotalRowCount.Should().Be(2);
        response.Truncated.Should().BeFalse();
        response.Headers.Should().Equal("Data Lanc.", "Data Valor", "Descrição", "Valor", "Saldo");
        response.PreviewRows.Should().HaveCount(2).And.OnlyContain(r => r.Error == null && !r.IsDuplicate);
        response.PreviewRows[0].Values.Should().Equal("02/09/2026", "02/09/2026", "TRF. VENCIMENTO", "1500,00", "1500,00");
        response.Statement.Should().NotBeNull();
        response.Statement!.Format.Should().Be("ActivoBankAccountXlsx");
        response.Statement.PeriodStart.Should().Be("2026-09-01");
        response.Statement.BalanceAfter.Should().Be(1496.80m);
        response.Statement.Checks.Should().ContainSingle().Which.Passed.Should().BeTrue();
        response.Statement.RowsTrimmed.Should().Be(0);

        var batch = _batches.Added.Should().ContainSingle().Subject;
        batch.Id.Should().Be(response.BatchId);
        batch.AccountId.Should().Be(_account.Id);
        batch.Status.Should().Be(ImportBatchStatus.PreviewReady);
        batch.TotalRows.Should().Be(2);
    }

    [Fact]
    public async Task Batch_stores_the_canonical_settings_so_confirm_reads_the_rows_without_mapping()
    {
        var response = await Upload(new FakeConverter(() => Statement()));

        var batch = _batches.Added.Single();
        var wrapper = System.Text.Json.JsonSerializer.Deserialize<PreviewWrapper>(batch.ParsedPreviewJson)!;
        wrapper.Settings!.DateFormat.Should().Be("dd/MM/yyyy");
        wrapper.Settings.DecimalSeparator.Should().Be(",");
        wrapper.Settings.ColumnMappings.Should().Contain(m => m.CsvColumnName == "Valor" && m.TransactionField == "Amount");
        response.PreviewRows.Should().OnlyContain(r => r.AccountId == _account.Id);
    }

    [Fact]
    public async Task Failed_validation_creates_no_batch()
    {
        var converter = new FakeConverter(() => throw new StatementValidationException("O saldo não encadeia no movimento 4."));

        var act = () => Upload(converter);

        await act.Should().ThrowAsync<StatementValidationException>().WithMessage("*movimento 4*");
        _batches.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Unexpected_converter_error_is_a_domain_error_never_a_crash_and_creates_no_batch()
    {
        var converter = new FakeConverter(() => throw new IndexOutOfRangeException("detalhe interno"));

        var act = () => Upload(converter);

        (await act.Should().ThrowAsync<StatementUnreadableException>()).Which.Message.Should().NotContain("detalhe interno");
        _batches.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Statement_currency_different_from_the_account_currency_is_refused()
    {
        var act = () => Upload(new FakeConverter(() => Statement(currency: "USD")));

        await act.Should().ThrowAsync<StatementCurrencyMismatchException>().WithMessage("*USD*EUR*");
        _batches.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Overlap_with_the_account_is_trimmed_before_the_preview()
    {
        var ledger = new FakeAccountLedger(0m, (new DateOnly(2026, 9, 2), 1500.00m));

        var response = await Upload(new FakeConverter(() => Statement()), ledger);

        response.PreviewRows.Should().ContainSingle().Which.Values[2].Should().Be("COMPRA PADARIA");
        response.TotalRowCount.Should().Be(1);
        response.Statement!.RowsTrimmed.Should().Be(1);
    }

    [Fact]
    public async Task Reuploading_an_imported_statement_gives_zero_new_rows()
    {
        var ledger = new FakeAccountLedger(0m, (new DateOnly(2026, 9, 2), 1500.00m), (new DateOnly(2026, 9, 3), -3.20m));

        var response = await Upload(new FakeConverter(() => Statement()), ledger);

        response.PreviewRows.Should().BeEmpty();
        response.TotalRowCount.Should().Be(0);
        response.Statement!.RowsTrimmed.Should().Be(2);
    }

    [Fact]
    public async Task Missing_account_is_refused()
    {
        var act = () => Upload(new FakeConverter(() => Statement()), accountId: Guid.NewGuid());

        await act.Should().ThrowAsync<ImportAccountRequiredException>();
    }

    [Fact]
    public async Task File_type_without_a_converter_is_refused()
    {
        var act = () => Upload(new FakeConverter(() => Statement()), fileName: "extrato.json");

        await act.Should().ThrowAsync<StatementUnreadableException>().WithMessage("*não suportado*");
    }
}

public sealed class StatementFileSnifferTests
{
    [Theory]
    [InlineData("extrato.xlsx", true)]
    [InlineData("EXTRATO.PDF", true)]
    [InlineData("extrato.json", true)]
    [InlineData("extrato.csv", false)]
    [InlineData("extrato.txt", false)]
    public void Recognises_statement_extensions(string fileName, bool expected)
        => StatementFileSniffer.IsStatementFile(fileName).Should().Be(expected);

    [Fact]
    public void Content_must_match_the_extension()
    {
        StatementFileSniffer.ContentMatchesExtension("a.xlsx", "PK\u0003\u0004rest"u8).Should().BeTrue();
        StatementFileSniffer.ContentMatchesExtension("a.xlsx", "%PDF-1.7"u8).Should().BeFalse();
        StatementFileSniffer.ContentMatchesExtension("a.pdf", "%PDF-1.7"u8).Should().BeTrue();
        StatementFileSniffer.ContentMatchesExtension("a.pdf", "PK\u0003\u0004"u8).Should().BeFalse();
        StatementFileSniffer.ContentMatchesExtension("a.json", "  \n{ \"movements\": {}}"u8).Should().BeTrue();
        StatementFileSniffer.ContentMatchesExtension("a.json", "<html>"u8).Should().BeFalse();
        StatementFileSniffer.ContentMatchesExtension("a.json", [0xEF, 0xBB, 0xBF, (byte)'{']).Should().BeTrue();
        StatementFileSniffer.ContentMatchesExtension("a.xlsx", ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }
}
