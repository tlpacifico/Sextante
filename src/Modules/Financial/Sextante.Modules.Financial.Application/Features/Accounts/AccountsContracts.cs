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
    Money CurrentBalance,
    CreditCardSettingsResponse? CreditCard = null);

/// <summary>
/// Definições do cartão (Phase 6.5 grupo 5). O limite vem na moeda da conta.
/// </summary>
public sealed record CreditCardSettingsInput(
    decimal CreditLimit,
    int StatementClosingDay,
    int PaymentDueDay,
    Guid? PaymentAccountId);

public sealed record CreditCardSettingsResponse(
    Money CreditLimit,
    int StatementClosingDay,
    int PaymentDueDay,
    Guid? PaymentAccountId);

public sealed record CreateAccountCommand(
    string Name,
    AccountType Type,
    string? Currency,
    decimal OpeningBalanceAmount,
    DateOnly? OpeningBalanceDate = null,
    CreditCardSettingsInput? CreditCard = null);

/// <summary>
/// PUT substitui as definições do cartão: <see cref="CreditCard"/> <c>null</c>
/// num cartão remove-as.
/// </summary>
public sealed record UpdateAccountCommand(
    Guid Id,
    string Name,
    AccountType Type,
    CreditCardSettingsInput? CreditCard = null);

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

public sealed record GetCreditCardViewQuery(Guid AccountId);

public sealed record CreditCardCycleResponse(
    DateOnly Start,
    DateOnly End,
    DateOnly PaymentDueDate,
    Money Spent,
    Money PaymentsReceived);

/// <summary>
/// Vista do cartão (Phase 6.5 grupo 5). Sem definições, só
/// <see cref="CurrentBalance"/> e <see cref="CurrentDebt"/> vêm preenchidos.
/// <see cref="PaymentAccountId"/> é <c>null</c> se a conta de pagamento já
/// não existir.
/// </summary>
public sealed record CreditCardViewResponse(
    Guid AccountId,
    Money CurrentBalance,
    Money CurrentDebt,
    CreditCardSettingsResponse? Settings,
    Money? Available,
    CreditCardCycleResponse? CurrentCycle,
    CreditCardCycleResponse? PreviousCycle,
    Money? PreviousClosingDebt,
    DateOnly? NextPaymentDueDate,
    Money? NextPaymentAmount,
    Guid? PaymentAccountId);
