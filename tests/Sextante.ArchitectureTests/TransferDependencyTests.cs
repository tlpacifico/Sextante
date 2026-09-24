using NetArchTest.Rules;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Phase 6.5 — transferências (grupos 3 e 7): o matcher de contrapernas é
/// domínio puro, a query de candidatas vive em Infrastructure por trás de
/// uma interface de Application, e o PublicApi do Financial não muda (D10).
/// Cada teste usa um typeof real — o filtro por nome do NetArchTest
/// passaria sem tipos.
/// </summary>
public sealed class TransferDependencyTests
{
    [Fact]
    public void TransferCounterpartMatcher_IsPureDomain()
    {
        Assert.Equal(ProjectAssemblies.Financial_Domain, typeof(TransferCounterpartMatcher).Assembly);

        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.Transactions")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Wolverine", "Hangfire")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domínio de transações não pode depender de EF Core, Wolverine ou Hangfire");
    }

    [Theory]
    [InlineData("Sextante.Modules.Financial.Application.Features.Transfers")]
    [InlineData("Sextante.Modules.Financial.Application.Features.CsvImport")]
    public void Transfers_Application_DoesNotReferenceInfrastructure(string ns)
    {
        Assert.Equal(ProjectAssemblies.Financial_Application, typeof(ITransferCounterpartQuery).Assembly);

        var result = Types.InAssembly(ProjectAssemblies.Financial_Application)
            .That()
            .ResideInNamespace(ns)
            .ShouldNot()
            .HaveDependencyOnAny("Sextante.Modules.Financial.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, $"{ns} não pode depender de Infrastructure nem de EF Core");
    }

    [Fact]
    public void TransferCounterpartQuery_Implementation_LivesInInfrastructure()
    {
        var implementations = Types.InAssembly(ProjectAssemblies.Financial_Infrastructure)
            .That()
            .ImplementInterface(typeof(ITransferCounterpartQuery))
            .GetTypes()
            .ToList();

        var implementation = Assert.Single(implementations);
        Assert.Equal("Sextante.Modules.Financial.Infrastructure.Transfers", implementation.Namespace);
    }

    [Fact]
    public void Transfers_DoNotChangeFinancialPublicApi()
    {
        var transferTypes = ProjectAssemblies.Financial_PublicApi.GetExportedTypes()
            .Where(t => t.Name.Contains("Transfer", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(transferTypes.Count == 0, $"D10 — PublicApi não expõe transferências: {string.Join(", ", transferTypes)}");
    }
}
