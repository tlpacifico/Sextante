using System.Reflection;
using NetArchTest.Rules;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Codifica a tabela de dependências do tech-stack §3.1. Falha o build
/// (TreatWarningsAsErrors) se qualquer projeto violar as regras.
/// </summary>
public sealed class ModuleIsolationTests
{
    [Fact]
    public void Domain_DoesNotReferenceApplication()
        => AssertNoDependency(ProjectAssemblies.Identity_Domain, "Sextante.Modules.Identity.Application");

    [Fact]
    public void Domain_DoesNotReferenceInfrastructure()
        => AssertNoDependency(ProjectAssemblies.Identity_Domain, "Sextante.Modules.Identity.Infrastructure");

    [Fact]
    public void Domain_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Identity_Domain, "Sextante.Modules.Identity.Api");

    [Fact]
    public void Domain_DoesNotReferencePublicApi()
        => AssertNoDependency(ProjectAssemblies.Identity_Domain, "Sextante.Modules.Identity.PublicApi");

    [Fact]
    public void Application_DoesNotReferenceInfrastructure()
        => AssertNoDependency(ProjectAssemblies.Identity_Application, "Sextante.Modules.Identity.Infrastructure");

    [Fact]
    public void Application_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Identity_Application, "Sextante.Modules.Identity.Api");

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Identity_Infrastructure, "Sextante.Modules.Identity.Api");

    [Fact]
    public void PublicApi_DoesNotReferenceDomainApplicationInfrastructureApi()
    {
        var forbidden = new[]
        {
            "Sextante.Modules.Identity.Domain",
            "Sextante.Modules.Identity.Application",
            "Sextante.Modules.Identity.Infrastructure",
            "Sextante.Modules.Identity.Api",
        };

        AssertNoDependency(ProjectAssemblies.Identity_PublicApi, forbidden);
    }

    /// <summary>
    /// Regra de ouro do replanning 2026-04-26: <c>Investment</c> não pode
    /// referenciar <c>Financial.{Domain,Application,Infrastructure,Api}</c>.
    /// Phase 1a ainda não tem nenhum desses módulos — passa trivialmente,
    /// mas fica em pé para Phase 4.
    /// </summary>
    [Fact]
    public void Investment_DoesNotReferenceFinancialDomainApplicationInfrastructureApi()
    {
        var investmentAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith(
                "Sextante.Modules.Investment.", StringComparison.Ordinal) == true)
            .ToArray();

        if (investmentAssemblies.Length == 0)
        {
            return;
        }

        var forbidden = new[]
        {
            "Sextante.Modules.Financial.Domain",
            "Sextante.Modules.Financial.Application",
            "Sextante.Modules.Financial.Infrastructure",
            "Sextante.Modules.Financial.Api",
        };

        foreach (var assembly in investmentAssemblies)
        {
            AssertNoDependency(assembly, forbidden);
        }
    }

    private static void AssertNoDependency(Assembly source, string forbiddenAssemblyName)
    {
        AssertNoDependency(source, [forbiddenAssemblyName]);
    }

    private static void AssertNoDependency(Assembly source, string[] forbiddenAssemblyNames)
    {
        var result = Types.InAssembly(source)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenAssemblyNames)
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var failing = result.FailingTypeNames ?? [];
        var summary = string.Join(", ", failing);
        Assert.Fail(
            $"{source.GetName().Name} → não pode depender de [{string.Join(", ", forbiddenAssemblyNames)}]. " +
            $"Tipos ofensivos: {summary}");
    }
}
