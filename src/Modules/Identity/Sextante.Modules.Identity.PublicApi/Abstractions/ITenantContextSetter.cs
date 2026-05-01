using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.PublicApi.Abstractions;

public interface ITenantContextSetter
{
    void SetCurrent(Guid tenantId);
    void Clear();
}
