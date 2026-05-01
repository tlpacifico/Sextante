using NetArchTest.Rules;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.SharedKernel;

namespace Sextante.ArchitectureTests;

public sealed class RecurringDependencyTests
{
    [Fact]
    public void RecurringRule_Implements_ITenantOwned()
    {
        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.RecurringRules")
            .And()
            .HaveName("RecurringRule")
            .Should()
            .ImplementInterface(typeof(ITenantOwned))
            .GetResult();

        Assert.True(result.IsSuccessful, result.FailingTypeNames is not null
            ? string.Join(", ", result.FailingTypeNames)
            : "RecurringRule deve implementar ITenantOwned");
    }

    [Fact]
    public void Domain_DoesNotReferenceHangfire()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Hangfire.Core");

    [Fact]
    public void Domain_DoesNotReferenceWolverine()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Wolverine");

    [Fact]
    public void Application_DoesNotReferenceHangfire()
        => AssertNoDependency(ProjectAssemblies.Financial_Application, "Hangfire.Core");

    [Fact]
    public void TenantAwareJob_LivesInBuildingBlocks()
    {
        var result = Types.InAssembly(ProjectAssemblies.Sextante_Infrastructure)
            .That()
            .ResideInNamespace("Sextante.Infrastructure.Jobs")
            .And()
            .HaveName("TenantAwareJob`1")
            .Should()
            .BePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, "TenantAwareJob deve ser público em Sextante.Infrastructure");
    }

    private static void AssertNoDependency(System.Reflection.Assembly source, string forbiddenAssemblyName)
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
