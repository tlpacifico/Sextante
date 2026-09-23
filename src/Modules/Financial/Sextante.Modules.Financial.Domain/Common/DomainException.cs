namespace Sextante.Modules.Financial.Domain.Common;

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

public sealed class AccountHasActiveTransactionsException : FinancialDomainException
{
    public AccountHasActiveTransactionsException(int activeCount)
        : base($"Não é possível arquivar uma conta com {activeCount} transações ativas.")
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

public sealed class TransactionNotRegularException : FinancialDomainException
{
    public TransactionNotRegularException()
        : base("Só transações regulares podem ser editadas ou recategorizadas diretamente.")
    {
    }
}

public sealed class TransactionIsTransferLegException : FinancialDomainException
{
    public TransactionIsTransferLegException()
        : base("Esta transação é uma perna de transferência; para editar ou apagar, use o endpoint de transferências (`/api/financial/transfers`).")
    {
    }
}

public sealed class TransactionNotTransferLegException : FinancialDomainException
{
    public TransactionNotTransferLegException()
        : base("Esta operação só é válida para pernas de transferência.")
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

public sealed class AccountTypeChangeInvalidException : FinancialDomainException
{
    public AccountTypeChangeInvalidException()
        : base("Não é possível mudar o tipo de uma conta com saldo inicial negativo para um tipo diferente de Cartão de crédito.")
    {
    }
}

public sealed class OpeningBalanceDateInFutureException : FinancialDomainException
{
    public OpeningBalanceDateInFutureException()
        : base("A data do saldo inicial não pode ser no futuro.")
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

// Categorization rule exceptions
public sealed class CategorizationRuleNameRequiredException : FinancialDomainException
{
    public CategorizationRuleNameRequiredException() : base("O nome da regra é obrigatório.") { }
}
public sealed class CategorizationRuleNameTooLongException : FinancialDomainException
{
    public CategorizationRuleNameTooLongException(int max) : base($"O nome da regra tem no máximo {max} caracteres.") { }
}
public sealed class CategorizationRulePatternRequiredException : FinancialDomainException
{
    public CategorizationRulePatternRequiredException() : base("O padrão da regra é obrigatório.") { }
}
public sealed class CategorizationRulePatternTooLongException : FinancialDomainException
{
    public CategorizationRulePatternTooLongException(int max) : base($"O padrão da regra tem no máximo {max} caracteres.") { }
}
public sealed class CategorizationRuleCategoryRequiredException : FinancialDomainException
{
    public CategorizationRuleCategoryRequiredException() : base("A categoria alvo da regra é obrigatória.") { }
}
public sealed class CategorizationRulePriorityNegativeException : FinancialDomainException
{
    public CategorizationRulePriorityNegativeException() : base("A prioridade da regra não pode ser negativa.") { }
}
public sealed class CategorizationRuleDuplicatePriorityException : FinancialDomainException
{
    public CategorizationRuleDuplicatePriorityException(int priority) : base($"Já existe uma regra com prioridade {priority}. Ajuste as prioridades.") { }
}

// Import profile exceptions
public sealed class ImportProfileNameRequiredException : FinancialDomainException
{
    public ImportProfileNameRequiredException() : base("O nome do perfil é obrigatório.") { }
}
public sealed class ImportProfileNameTooLongException : FinancialDomainException
{
    public ImportProfileNameTooLongException(int max) : base($"O nome do perfil tem no máximo {max} caracteres.") { }
}
public sealed class ImportProfileMappingsRequiredException : FinancialDomainException
{
    public ImportProfileMappingsRequiredException() : base("Pelo menos um mapeamento de coluna é obrigatório.") { }
}
public sealed class ImportProfileDelimiterInvalidException : FinancialDomainException
{
    public ImportProfileDelimiterInvalidException() : base("O delimitador tem de ser um único caractere.") { }
}
public sealed class ImportProfileDecimalSeparatorInvalidException : FinancialDomainException
{
    public ImportProfileDecimalSeparatorInvalidException() : base("O separador decimal tem de ser um único caractere.") { }
}
public sealed class ImportProfileSkipRowsNegativeException : FinancialDomainException
{
    public ImportProfileSkipRowsNegativeException() : base("SkipRows não pode ser negativo.") { }
}

// Import batch exceptions
public sealed class ImportBatchFileNameRequiredException : FinancialDomainException
{
    public ImportBatchFileNameRequiredException() : base("O nome do ficheiro é obrigatório.") { }
}
public sealed class ImportBatchFileNameTooLongException : FinancialDomainException
{
    public ImportBatchFileNameTooLongException(int max) : base($"O nome do ficheiro tem no máximo {max} caracteres.") { }
}

// CSV import exceptions
public sealed class CsvParseException : FinancialDomainException
{
    public CsvParseException(string detail) : base($"Erro ao processar o CSV: {detail}.") { }
}
public sealed class CsvEmptyException : FinancialDomainException
{
    public CsvEmptyException() : base("O ficheiro CSV está vazio ou não contém dados.") { }
}
public sealed class CsvEncodingNotSupportedException : FinancialDomainException
{
    public CsvEncodingNotSupportedException() : base("Encoding do CSV não suportado. Use UTF-8 ou ISO-8859-1.") { }
}
public sealed class CsvDuplicateColumnsException : FinancialDomainException
{
    public CsvDuplicateColumnsException(string column) : base($"CSV contém colunas com nome duplicado: '{column}'.") { }
}
public sealed class CsvParseTimeoutException : FinancialDomainException
{
    public CsvParseTimeoutException() : base("A análise do CSV excedeu o tempo limite de 30 segundos. Use um ficheiro mais pequeno.") { }
}

public sealed class AccountNotFoundForImportException : FinancialDomainException
{
    public AccountNotFoundForImportException(string accountName) : base($"Conta '{accountName}' não encontrada. Crie a conta antes de importar.") { }
}

// Transfer exceptions (Phase 6.5, grupo 3)
public sealed class TransferAccountsMustDifferException : FinancialDomainException
{
    public TransferAccountsMustDifferException()
        : base("A conta de origem e a conta de destino têm de ser diferentes.")
    {
    }
}

public sealed class TransferAmountInRequiredException : FinancialDomainException
{
    public TransferAmountInRequiredException()
        : base("É necessário indicar o valor recebido quando as contas têm moedas diferentes.")
    {
    }
}

public sealed class TransferCounterpartCurrencyMismatchException : FinancialDomainException
{
    public TransferCounterpartCurrencyMismatchException()
        : base("Não é possível criar a contraparte automaticamente entre moedas diferentes; ligue a uma transação existente ou use 'Nova transferência'.")
    {
    }
}

public sealed class TransferCounterpartAmountMismatchException : FinancialDomainException
{
    public TransferCounterpartAmountMismatchException()
        : base("O valor da transação selecionada não corresponde ao valor a converter.")
    {
    }
}

public sealed class TransferCounterpartAlreadyLinkedException : FinancialDomainException
{
    public TransferCounterpartAlreadyLinkedException()
        : base("A transação selecionada já faz parte de outra transferência.")
    {
    }
}

public sealed class TransferCounterpartSameDirectionException : FinancialDomainException
{
    public TransferCounterpartSameDirectionException()
        : base("A transação selecionada tem de ter o sentido oposto (entrada ↔ saída).")
    {
    }
}

public sealed class TransferCounterpartWrongAccountException : FinancialDomainException
{
    public TransferCounterpartWrongAccountException()
        : base("A transação selecionada não pertence à conta indicada.")
    {
    }
}

public sealed class ReconciliationDateInFutureException : FinancialDomainException
{
    public ReconciliationDateInFutureException()
        : base("A data da reconciliação não pode ser no futuro.")
    {
    }
}

public sealed class ReconciliationBeforeOpeningBalanceException : FinancialDomainException
{
    public ReconciliationBeforeOpeningBalanceException()
        : base("A data da reconciliação não pode ser anterior à data do saldo inicial da conta.")
    {
    }
}

public sealed class CreditCardSettingsRequireCreditCardException : FinancialDomainException
{
    public CreditCardSettingsRequireCreditCardException()
        : base("Só contas do tipo cartão de crédito têm definições de cartão.")
    {
    }
}

public sealed class CreditLimitMustBePositiveException : FinancialDomainException
{
    public CreditLimitMustBePositiveException()
        : base("O limite de crédito tem de ser maior que zero.")
    {
    }
}

public sealed class CreditLimitCurrencyMismatchException : FinancialDomainException
{
    public CreditLimitCurrencyMismatchException()
        : base("O limite de crédito tem de estar na moeda do cartão.")
    {
    }
}

public sealed class CreditCardDayOutOfRangeException : FinancialDomainException
{
    public CreditCardDayOutOfRangeException()
        : base("Os dias de fecho e de pagamento têm de estar entre 1 e 31.")
    {
    }
}

public sealed class CreditCardPaymentAccountInvalidException : FinancialDomainException
{
    public CreditCardPaymentAccountInvalidException()
        : base("A conta de pagamento tem de ser outra conta do utilizador que não seja um cartão de crédito.")
    {
    }
}

public sealed class AccountNotCreditCardException : FinancialDomainException
{
    public AccountNotCreditCardException()
        : base("Esta conta não é um cartão de crédito.")
    {
    }
}
