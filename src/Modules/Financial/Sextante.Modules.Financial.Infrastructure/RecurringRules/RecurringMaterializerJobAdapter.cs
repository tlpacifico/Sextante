using Sextante.Infrastructure.Jobs;
using Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Infrastructure.RecurringRules;

public sealed class RecurringMaterializerJobAdapter
    : ITenantAwareJobHandler<RecurringMaterializerPayload>
{
    private readonly RecurringTransactionMaterializerHandler _handler;
    private readonly ITenantContext _tenantContext;

    public RecurringMaterializerJobAdapter(
        RecurringTransactionMaterializerHandler handler,
        ITenantContext tenantContext)
    {
        _handler = handler;
        _tenantContext = tenantContext;
    }

    public async Task ExecuteAsync(Guid tenantId, RecurringMaterializerPayload payload, CancellationToken ct)
    {
        await _handler.ExecuteAsync(payload, _tenantContext, ct);
    }
}
