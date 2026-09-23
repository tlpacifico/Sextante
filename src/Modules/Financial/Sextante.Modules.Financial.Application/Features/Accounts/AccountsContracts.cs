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
