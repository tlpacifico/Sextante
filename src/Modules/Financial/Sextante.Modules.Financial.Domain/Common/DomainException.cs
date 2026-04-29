namespace Sextante.Modules.Financial.Domain.Common;

/// <summary>
/// Base para exceções de invariant do Domain. Handlers da Application
/// mapeiam para <c>ValidationProblemDetails</c> PT-PT.
/// </summary>
public abstract class FinancialDomainException : InvalidOperationException
{
    protected FinancialDomainException(string message) : base(message)
    {
    }
}

public sealed class CategoryHasActiveTransactionsException : FinancialDomainException
{
    public CategoryHasActiveTransactionsException(int activeCount)
        : base($"Não é possível arquivar uma categoria com {activeCount} transações ativas.")
    {
        ActiveCount = activeCount;
    }

    public int ActiveCount { get; }
}

public sealed class TransactionAmountMustBePositiveException : FinancialDomainException
{
    public TransactionAmountMustBePositiveException()
        : base("O valor da transação tem de ser maior que zero.")
    {
    }
}

public sealed class TransactionInFutureException : FinancialDomainException
{
    public TransactionInFutureException()
        : base("A data da transação não pode ser no futuro.")
    {
    }
}

public sealed class InvalidIconNameException : FinancialDomainException
{
    public InvalidIconNameException(string iconName)
        : base($"Ícone '{iconName}' não está na allowlist de PrimeIcons.")
    {
    }
}

public sealed class InvalidColorHexException : FinancialDomainException
{
    public InvalidColorHexException(string colorHex)
        : base($"Cor '{colorHex}' inválida — esperado formato '#RRGGBB'.")
    {
    }
}

public sealed class OpeningBalanceNegativeException : FinancialDomainException
{
    public OpeningBalanceNegativeException()
        : base("O saldo inicial não pode ser negativo.")
    {
    }
}

public sealed class AccountCurrencyMismatchException : FinancialDomainException
{
    public AccountCurrencyMismatchException(string accountCurrency, string openingBalanceCurrency)
        : base($"Moeda do saldo inicial ('{openingBalanceCurrency}') tem de coincidir com a moeda da conta ('{accountCurrency}').")
    {
        AccountCurrency = accountCurrency;
        OpeningBalanceCurrency = openingBalanceCurrency;
    }

    public string AccountCurrency { get; }
    public string OpeningBalanceCurrency { get; }
}

public sealed class CurrencyNotActiveException : FinancialDomainException
{
    public CurrencyNotActiveException(string code)
        : base($"Moeda '{code}' não está ativa.")
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ExchangeRateUnavailableException : FinancialDomainException
{
    public ExchangeRateUnavailableException(string from, string to, DateOnly date)
        : base($"Não há taxa de câmbio disponível para {from} → {to} em {date:yyyy-MM-dd}. Insira manualmente em /app/admin/exchange-rates.")
    {
        From = from;
        To = to;
        Date = date;
    }

    public string From { get; }
    public string To { get; }
    public DateOnly Date { get; }
}
