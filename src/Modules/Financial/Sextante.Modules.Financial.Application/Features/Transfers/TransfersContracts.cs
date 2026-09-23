using Sextante.Modules.Financial.Application.Features.Transactions;

namespace Sextante.Modules.Financial.Application.Features.Transfers;

public sealed record CreateTransferCommand(
    Guid FromAccountId,
    Guid ToAccountId,
    DateTimeOffset OccurredAt,
    decimal AmountOut,
    decimal? AmountIn,
    string? Description);

public sealed record UpdateTransferCommand(
    Guid TransferId,
    Guid FromAccountId,
    Guid ToAccountId,
    DateTimeOffset OccurredAt,
    decimal AmountOut,
    decimal? AmountIn,
    string? Description);

public sealed record DeleteTransferCommand(Guid TransferId);

public sealed record GetTransferByIdQuery(Guid TransferId);

public sealed record ConvertToTransferCommand(
    Guid TransactionId,
    Guid CounterpartAccountId,
    Guid? CounterpartTransactionId);

/// <summary>
/// Reaproveita o <see cref="TransactionResponse"/> de Features/Transactions
/// para cada perna — sem duplicar o shape de leitura de uma transação.
/// </summary>
public sealed record TransferResponse(
    Guid TransferId,
    TransactionResponse OutLeg,
    TransactionResponse InLeg);
