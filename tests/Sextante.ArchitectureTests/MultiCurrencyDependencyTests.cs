using System.Reflection;
using NetArchTest.Rules;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Codifica decisões Phase 3 sobre cross-module references de
/// multi-moeda:
/// <list type="bullet">
/// <item><c>Module.Financial.Application</c> usa
/// <c>IExchangeRateService</c> (próprio) — concretização
/// (<c>EcbCurrencyProvider</c>) vive em <c>Identity.Infrastructure</c>
/// e não pode ser referenciada directamente.</item>
/// <item><c>Module.Financial.Domain</c> referencia
/// <c>SharedKernel.Currency</c> mas não <c>Identity.Domain.ExchangeRate</c>.</item>
/// </list>
/// </summary>
public sealed class MultiCurrencyDependencyTests
{
    [Fact]
    public void Financial_Application_DoesNotReference_Identity_Infrastructure()
        => AssertNoDependency(
            ProjectAssemblies.Financial_Application,
            "Sextante.Modules.Identity.Infrastructure");

    [Fact]
    public void Financial_Domain_DoesNotReference_Identity_Domain()
        => AssertNoDependency(
            ProjectAssemblies.Financial_Domain,
            "Sextante.Modules.Identity.Domain");

    [Fact]
    public void Financial_Domain_References_SharedKernel()
    {
        // Sanity: a regra positiva é que Currency vive em SharedKernel —
        // se este teste falhar é porque alguém a moveu para outro
        // módulo. Não exige uso explícito de Currency, apenas que o
        // assembly esteja referenciado.
        var refs = ProjectAssemblies.Financial_Domain.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Sextante.SharedKernel", refs);
    }

    private static void AssertNoDependency(Assembly source, string forbiddenAssemblyName)
    {
        var result = Types.InAssembly(source)
            .ShouldNot()
            .HaveDependencyOn(forbiddenAssemblyName)
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var failing = result.FailingTypeNames ?? [];
        var summary = string.Join(", ", failing);
        Assert.Fail(
            $"{source.GetName().Name} → não pode depender de [{forbiddenAssemblyName}]. " +
            $"Tipos ofensivos: {summary}");
    }
}
