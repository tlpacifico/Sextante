using NetArchTest.Rules;
using Sextante.SharedKernel;

namespace Sextante.ArchitectureTests;

public sealed class BudgetDependencyTests
{
    [Fact]
    public void Budget_Implements_ITenantOwned()
    {
        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.Budgets")
            .And()
            .HaveName("Budget")
            .Should()
            .ImplementInterface(typeof(ITenantOwned))
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Budget deve implementar ITenantOwned");
    }

    [Fact]
    public void BudgetAlert_Implements_ITenantOwned()
    {
        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.Budgets")
            .And()
            .HaveName("BudgetAlert")
            .Should()
            .ImplementInterface(typeof(ITenantOwned))
            .GetResult();

        Assert.True(result.IsSuccessful,
            "BudgetAlert deve implementar ITenantOwned");
    }

    [Fact]
    public void Budgets_Domain_DoesNotReferenceHangfire()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Hangfire.Core");

    [Fact]
    public void Budgets_Domain_DoesNotReferenceWolverine()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Wolverine");

    [Fact]
    public void Budgets_Domain_DoesNotReferenceEntityFrameworkCore()
        => AssertNoDependency(ProjectAssemblies.Financial_Domain, "Microsoft.EntityFrameworkCore");

    [Fact]
    public void BudgetAlertDispatchHandlers_LivesInApplication()
    {
        var result = Types.InAssembly(ProjectAssemblies.Financial_Application)
            .That()
            .HaveName("BudgetAlertDispatchHandlers")
            .Should()
            .ResideInNamespace("Sextante.Modules.Financial.Application.Features.Budgets.Alerts")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "BudgetAlertDispatchHandlers deve estar em Application.Features.Budgets.Alerts");
    }

    [Fact]
    public void BudgetProgressService_Impl_LivesInInfrastructure()
    {
        // O contract IBudgetProgressService está em Application; a impl
        // (que faz EF Core queries) tem de estar em Infrastructure.
        var result = Types.InAssembly(ProjectAssemblies.Financial_Infrastructure)
            .That()
            .HaveName("BudgetProgressService")
            .Should()
            .ResideInNamespace("Sextante.Modules.Financial.Infrastructure.Budgets")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "BudgetProgressService impl deve estar em Infrastructure.Budgets");
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
        Assert.Fail(
            $"{source.GetName().Name} → não pode depender de [{forbiddenAssemblyName}]. " +
            $"Tipos ofensivos: {string.Join(", ", failing)}");
    }
}
