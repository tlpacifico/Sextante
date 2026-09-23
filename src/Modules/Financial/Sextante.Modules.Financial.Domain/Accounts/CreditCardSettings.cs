using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Accounts;

/// <summary>
/// Definições de um cartão de crédito (Phase 6.5 grupo 5): limite, dia de
/// fecho do extrato, dia de pagamento e conta de onde sai o pagamento.
/// Imutável — <see cref="Account.ConfigureCreditCard"/> substitui-as inteiras.
/// Dias 1–31; num mês sem esse dia conta o dia 1 do mês seguinte
/// (<see cref="CreditCardCalendar"/>).
/// </summary>
public sealed class CreditCardSettings
{
    public const int MinDay = 1;
    public const int MaxDay = 31;

    private CreditCardSettings()
    {
        CreditLimitCurrency = string.Empty;
    }

    internal CreditCardSettings(Money creditLimit, int statementClosingDay, int paymentDueDay, Guid? paymentAccountId)
    {
        CreditLimitAmount = creditLimit.Amount;
        CreditLimitCurrency = creditLimit.Currency;
        StatementClosingDay = statementClosingDay;
        PaymentDueDay = paymentDueDay;
        PaymentAccountId = paymentAccountId;
    }

    public decimal CreditLimitAmount { get; private set; }
    public string CreditLimitCurrency { get; private set; }
    public int StatementClosingDay { get; private set; }
    public int PaymentDueDay { get; private set; }

    /// <summary>
    /// Soft reference a outra conta do tenant (não cartão), sem FK — validada
    /// na Application.
    /// </summary>
    public Guid? PaymentAccountId { get; private set; }

    public Money CreditLimit => new(CreditLimitAmount, CreditLimitCurrency);
}
