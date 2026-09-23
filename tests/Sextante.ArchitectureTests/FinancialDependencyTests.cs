using System.Reflection;
using NetArchTest.Rules;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Codifica a tabela de dependências de tech-stack §3.1 aplicada ao
/// módulo Financial (Phase 2). Em conjunto com <see cref="ModuleIsolationTests"/>,
/// previne regressão de modular monolith.
/// </summary>
public sealed class FinancialDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceApplication()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Sextante.Modules.Financial.Application");

    [Fact]
    public void Domain_DoesNotReferenceInfrastructure()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Sextante.Modules.Financial.Infrastructure");

    [Fact]
    public void Domain_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Sextante.Modules.Financial.Api");

    [Fact]
    public void Domain_DoesNotReferenceEntityFrameworkCore()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Microsoft.EntityFrameworkCore");

    [Fact]
    public void Domain_DoesNotReferenceIdentityModule()
    {
        var forbidden = new[]
        {
            "Sextante.Modules.Identity.Domain",
            "Sextante.Modules.Identity.Application",
            "Sextante.Modules.Identity.Infrastructure",
            "Sextante.Modules.Identity.Api",
            "Sextante.Modules.Identity.PublicApi",
        };
        AssertNoDependency(ProjectAssemblies.Financial_Domain, forbidden);
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructure()
        => AssertNoDependency(ProjectAssemblies.Financial_Application, "Sextante.Modules.Financial.Infrastructure");

    /// <summary>
    /// Achado da revisão final do grupo 3 (transferências): TransferHandlers
    /// tinha passado a referenciar o BuildingBlock partilhado
    /// Sextante.Infrastructure (por EntityNotFoundException), o que as regras
    /// acima não apanham — só cobrem o *.Infrastructure do próprio módulo.
    /// </summary>
    [Fact]
    public void Application_DoesNotReferenceSharedInfrastructureBuildingBlock()
        => AssertNoDependency(ProjectAssemblies.Financial_Application, "Sextante.Infrastructure");

    [Fact]
    public void Application_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Financial_Application, "Sextante.Modules.Financial.Api");

    [Fact]
    public void Application_OnlyTouchesIdentity_via_PublicApi()
    {
        // Application pode referenciar Identity.PublicApi (ITenantContext etc.)
        // mas nada mais do Identity.
        var forbidden = new[]
        {
            "Sextante.Modules.Identity.Domain",
            "Sextante.Modules.Identity.Application",
            "Sextante.Modules.Identity.Infrastructure",
            "Sextante.Modules.Identity.Api",
        };
        AssertNoDependency(ProjectAssemblies.Financial_Application, forbidden);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
        => AssertNoDependency(ProjectAssemblies.Financial_Infrastructure, "Sextante.Modules.Financial.Api");

    [Fact]
    public void PublicApi_DoesNotReferenceOtherFinancialLayers()
    {
        var forbidden = new[]
        {
            "Sextante.Modules.Financial.Domain",
            "Sextante.Modules.Financial.Application",
            "Sextante.Modules.Financial.Infrastructure",
            "Sextante.Modules.Financial.Api",
        };
        AssertNoDependency(ProjectAssemblies.Financial_PublicApi, forbidden);
    }

    private static void AssertNoDependency(Assembly source, string forbiddenAssemblyName)
        => AssertNoDependency(source, new[] { forbiddenAssemblyName });

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
