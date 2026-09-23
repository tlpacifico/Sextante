using NetArchTest.Rules;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.SharedKernel;

namespace Sextante.ArchitectureTests;

public sealed class InstallmentPlanDependencyTests
{
    [Fact]
    public void InstallmentPlan_Implements_ITenantOwned()
    {
        // typeof garante que o teste falha se o tipo desaparecer (o filtro por
        // nome do NetArchTest passaria sem tipos).
        Assert.True(typeof(ITenantOwned).IsAssignableFrom(typeof(InstallmentPlan)),
            "InstallmentPlan deve implementar ITenantOwned");

        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.InstallmentPlans")
            .And()
            .HaveName("InstallmentPlan")
            .Should()
            .ImplementInterface(typeof(ITenantOwned))
            .GetResult();

        Assert.True(result.IsSuccessful, "InstallmentPlan deve implementar ITenantOwned");
    }

    [Fact]
    public void InstallmentPlans_Domain_DoesNotReferenceEntityFrameworkCore()
    {
        var result = Types.InAssembly(ProjectAssemblies.Financial_Domain)
            .That()
            .ResideInNamespace("Sextante.Modules.Financial.Domain.InstallmentPlans")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domínio de prestações não pode depender de EF Core");
    }
}
