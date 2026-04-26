namespace Sextante.SharedKernel;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(GuidV7.NewId());

    public override string ToString() => Value.ToString();
}
