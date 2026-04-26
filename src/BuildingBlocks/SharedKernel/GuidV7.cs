namespace Sextante.SharedKernel;

public static class GuidV7
{
    public static Guid NewId() => Guid.CreateVersion7();
}
