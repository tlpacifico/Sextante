using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Accounts;

public sealed record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    string Currency,
    Money OpeningBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateOnly OpeningBalanceDate,
    Money CurrentBalance);

public sealed record CreateAccountCommand(
    string Name,
    AccountType Type,
    string? Currency,
    decimal OpeningBalanceAmount,
    DateOnly? OpeningBalanceDate = null);

public sealed record UpdateAccountCommand(
    Guid Id,
    string Name,
    AccountType Type);

public sealed record ArchiveAccountCommand(Guid Id);

public sealed record GetAccountByIdQuery(Guid Id);

public sealed record ListAccountsQuery();

public sealed record GetAccountBalanceQuery(Guid AccountId, DateOnly? At);

public sealed record AccountBalanceResponse(Guid AccountId, DateOnly? At, Money Balance);

/// <summary>
/// Acerto de saldo (Phase 6.5 grupo 4): "o saldo real a <see cref="Date"/>
/// era <see cref="ActualBalance"/>" — na moeda da conta, qualquer sinal
/// (dívida de cartão é negativa).
/// </summary>
public sealed record ReconcileAccountCommand(Guid AccountId, DateOnly Date, decimal ActualBalance);

/// <summary>
/// <see cref="Difference"/> = real − calculado (com sinal).
/// <see cref="Adjustment"/> é <c>null</c> quando não há diferença.
/// </summary>
public sealed record ReconcileAccountResponse(
    Guid AccountId,
    DateOnly Date,
    Money CalculatedBalance,
    Money ActualBalance,
    Money Difference,
    TransactionResponse? Adjustment);
