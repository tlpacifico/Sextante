using Sextante.Host;

namespace Sextante.ArchitectureTests;

public sealed class HostAssemblySmokeTests
{
    [Fact]
    public void HostAssembly_can_be_loaded()
    {
        var assembly = typeof(HostAssemblyMarker).Assembly;

        Assert.NotNull(assembly);
        Assert.Equal("Sextante.Host", assembly.GetName().Name);
        Assert.NotEmpty(assembly.GetTypes());
    }
}
