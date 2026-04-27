using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Accounts;

public sealed record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    Money OpeningBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateAccountCommand(
    string Name,
    AccountType Type,
    decimal OpeningBalanceAmount);

public sealed record UpdateAccountCommand(
    Guid Id,
    string Name,
    AccountType Type);

public sealed record ArchiveAccountCommand(Guid Id);

public sealed record GetAccountByIdQuery(Guid Id);

public sealed record ListAccountsQuery();
