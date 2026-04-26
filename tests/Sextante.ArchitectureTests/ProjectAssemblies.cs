using System.Reflection;

namespace Sextante.ArchitectureTests;

/// <summary>
/// Helper que carrega assemblies por convenção de nome
/// (<c>Sextante.Modules.&lt;Module&gt;.&lt;Layer&gt;</c>).
/// </summary>
internal static class ProjectAssemblies
{
    public static Assembly Identity_Domain { get; } =
        typeof(Sextante.Modules.Identity.Domain.AssemblyMarker).Assembly;

    public static Assembly Identity_Application { get; } =
        typeof(Sextante.Modules.Identity.Application.AssemblyMarker).Assembly;

    public static Assembly Identity_Infrastructure { get; } =
        typeof(Sextante.Modules.Identity.Infrastructure.AssemblyMarker).Assembly;

    public static Assembly Identity_Api { get; } =
        typeof(Sextante.Modules.Identity.Api.AssemblyMarker).Assembly;

    public static Assembly Identity_PublicApi { get; } =
        typeof(Sextante.Modules.Identity.PublicApi.AssemblyMarker).Assembly;
}
