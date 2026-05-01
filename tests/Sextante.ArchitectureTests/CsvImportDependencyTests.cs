using System.Reflection;
using NetArchTest.Rules;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.SharedKernel;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Phase 4 layering: Domain knows nothing of CsvHelper; Application
/// depends on its own CSV/duplicate/rule abstractions but not the
/// implementations; new aggregates are tenant-owned. Phase 4 does not
/// open new cross-module dependencies.
/// </summary>
public sealed class CsvImportDependencyTests
{
    [Fact]
    public void Domain_does_not_reference_CsvHelper()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "CsvHelper");

    [Fact]
    public void Application_does_not_reference_CsvHelper()
        => AssertNoDependency(ProjectAssemblies.Financial_Application, "CsvHelper");

    [Fact]
    public void CategorizationRule_implements_ITenantOwned()
        => typeof(CategorizationRule)
            .GetInterfaces()
            .Should_Contain(typeof(ITenantOwned), nameof(CategorizationRule));

    [Fact]
    public void ImportProfile_implements_ITenantOwned()
        => typeof(ImportProfile)
            .GetInterfaces()
            .Should_Contain(typeof(ITenantOwned), nameof(ImportProfile));

    [Fact]
    public void ImportBatch_implements_ITenantOwned()
        => typeof(ImportBatch)
            .GetInterfaces()
            .Should_Contain(typeof(ITenantOwned), nameof(ImportBatch));

    [Fact]
    public void Application_csvimport_abstractions_live_in_Application()
    {
        // ICsvParser, IDuplicateDetector, ICategorizationRuleEngine are
        // declared in Application — Infrastructure provides the impl
        // and depends on Application, not the other way round.
        var icsvParser = ProjectAssemblies.Financial_Application
            .GetType("Sextante.Modules.Financial.Application.CsvImport.ICsvParser");
        var iDuplicateDetector = ProjectAssemblies.Financial_Application
            .GetType("Sextante.Modules.Financial.Application.CsvImport.IDuplicateDetector");
        var iRuleEngine = ProjectAssemblies.Financial_Application
            .GetType("Sextante.Modules.Financial.Application.CategorizationRules.ICategorizationRuleEngine");

        Assert.NotNull(icsvParser);
        Assert.NotNull(iDuplicateDetector);
        Assert.NotNull(iRuleEngine);
    }

    private static void AssertNoDependency(Assembly source, string forbiddenAssemblyName)
    {
        var result = Types.InAssembly(source)
            .ShouldNot()
            .HaveDependencyOn(forbiddenAssemblyName)
            .GetResult();
        if (result.IsSuccessful) return;

        var failing = string.Join(", ", result.FailingTypeNames ?? []);
        Assert.Fail(
            $"{source.GetName().Name} não pode depender de '{forbiddenAssemblyName}'. " +
            $"Tipos ofensivos: {failing}");
    }
}

internal static class TypeAssertionExtensions
{
    public static void Should_Contain(this Type[] interfaces, Type expected, string typeName)
    {
        if (!interfaces.Contains(expected))
            Assert.Fail($"{typeName} deve implementar {expected.Name}.");
    }
}
