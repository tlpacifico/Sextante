namespace Sextante.SharedKernel;

public interface ITenantOwned
{
    TenantId TenantId { get; }
}
