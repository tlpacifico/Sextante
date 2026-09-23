using FluentValidation;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Application.Features.CategorizationRules;
using Sextante.Modules.Financial.Application.Features.ImportProfiles;
using Sextante.Modules.Financial.Application.Features.InstallmentPlans;
using Sextante.Modules.Financial.Application.Features.RecurringRules;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.Modules.Financial.Domain.RecurringRules;

namespace Sextante.Modules.Financial.Api.Validators;

public sealed class CreateCategorizationRuleValidator : AbstractValidator<CreateCategorizationRuleCommand>
{
    public CreateCategorizationRuleValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("O nome da regra é obrigatório.")
            .MaximumLength(CategorizationRule.NameMaxLength)
            .WithMessage($"O nome da regra tem no máximo {CategorizationRule.NameMaxLength} caracteres.");
        RuleFor(c => c.Pattern)
            .NotEmpty().WithMessage("O padrão é obrigatório.")
            .MaximumLength(CategorizationRule.PatternMaxLength)
            .WithMessage($"O padrão tem no máximo {CategorizationRule.PatternMaxLength} caracteres.");
        RuleFor(c => c.MatchType)
            .Must(mt => mt is "Contains" or "Equals" or "StartsWith")
            .WithMessage("MatchType deve ser Contains, Equals ou StartsWith.");
        RuleFor(c => c.CategoryId)
            .NotEmpty().WithMessage("A categoria alvo é obrigatória.");
        RuleFor(c => c.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("A prioridade não pode ser negativa.");
    }
}

public sealed class UpdateCategorizationRuleValidator : AbstractValidator<UpdateCategorizationRuleCommand>
{
    public UpdateCategorizationRuleValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("O nome da regra é obrigatório.")
            .MaximumLength(CategorizationRule.NameMaxLength);
        RuleFor(c => c.Pattern)
            .NotEmpty().WithMessage("O padrão é obrigatório.")
            .MaximumLength(CategorizationRule.PatternMaxLength);
        RuleFor(c => c.MatchType)
            .Must(mt => mt is "Contains" or "Equals" or "StartsWith")
            .WithMessage("MatchType deve ser Contains, Equals ou StartsWith.");
        RuleFor(c => c.CategoryId)
            .NotEmpty().WithMessage("A categoria alvo é obrigatória.");
        RuleFor(c => c.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("A prioridade não pode ser negativa.");
    }
}

public sealed class CreateImportProfileValidator : AbstractValidator<CreateImportProfileCommand>
{
    public CreateImportProfileValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("O nome do perfil é obrigatório.")
            .MaximumLength(128).WithMessage("O nome do perfil tem no máximo 128 caracteres.");
        RuleFor(c => c.ColumnMappings)
            .NotEmpty().WithMessage("Pelo menos um mapeamento de coluna é obrigatório.");
    }
}

public sealed class CreateRecurringRuleValidator : AbstractValidator<CreateRecurringRuleCommand>
{
    public CreateRecurringRuleValidator()
    {
        RuleFor(c => c.Description)
            .NotEmpty().WithMessage("A descrição da regra é obrigatória.")
            .MaximumLength(RecurringRule.DescriptionMaxLength)
            .WithMessage($"A descrição da regra tem no máximo {RecurringRule.DescriptionMaxLength} caracteres.");
        RuleFor(c => c.Amount)
            .GreaterThan(0m).WithMessage("O valor tem de ser maior que zero.");
        RuleFor(c => c.Currency)
            .NotEmpty().WithMessage("A moeda é obrigatória.")
            .Length(3).WithMessage("A moeda deve ter 3 caracteres (ISO 4217).");
        RuleFor(c => c.AccountId)
            .NotEmpty().WithMessage("A conta é obrigatória.");
        RuleFor(c => c.Frequency)
            .Must(f => f is "Daily" or "Weekly" or "Monthly" or "Yearly")
            .WithMessage("Frequência deve ser Daily, Weekly, Monthly ou Yearly.");
        RuleFor(c => c.Interval)
            .GreaterThanOrEqualTo(1).WithMessage("O intervalo tem de ser maior ou igual a 1.");
        RuleFor(c => c.StartDate)
            .NotEmpty().WithMessage("A data de início é obrigatória.");
        RuleFor(c => c)
            .Must(c => c.EndDate is null || c.StartDate <= c.EndDate)
            .WithMessage("A data de início não pode ser superior à data de fim.");
    }
}

public sealed class UpdateRecurringRuleValidator : AbstractValidator<UpdateRecurringRuleCommand>
{
    public UpdateRecurringRuleValidator()
    {
        RuleFor(c => c.Description)
            .NotEmpty().WithMessage("A descrição da regra é obrigatória.")
            .MaximumLength(RecurringRule.DescriptionMaxLength)
            .WithMessage($"A descrição da regra tem no máximo {RecurringRule.DescriptionMaxLength} caracteres.");
        RuleFor(c => c.Amount)
            .GreaterThan(0m).WithMessage("O valor tem de ser maior que zero.");
        RuleFor(c => c.Currency)
            .NotEmpty().WithMessage("A moeda é obrigatória.")
            .Length(3).WithMessage("A moeda deve ter 3 caracteres (ISO 4217).");
        RuleFor(c => c.AccountId)
            .NotEmpty().WithMessage("A conta é obrigatória.");
        RuleFor(c => c.Frequency)
            .Must(f => f is "Daily" or "Weekly" or "Monthly" or "Yearly")
            .WithMessage("Frequência deve ser Daily, Weekly, Monthly ou Yearly.");
        RuleFor(c => c.Interval)
            .GreaterThanOrEqualTo(1).WithMessage("O intervalo tem de ser maior ou igual a 1.");
        RuleFor(c => c.StartDate)
            .NotEmpty().WithMessage("A data de início é obrigatória.");
        RuleFor(c => c)
            .Must(c => c.EndDate is null || c.StartDate <= c.EndDate)
            .WithMessage("A data de início não pode ser superior à data de fim.");
    }
}

public sealed class CreateBudgetValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetValidator()
    {
        RuleFor(c => c.CategoryId)
            .NotEmpty().WithMessage("A categoria é obrigatória.");
        RuleFor(c => c.Year)
            .InclusiveBetween(BudgetPeriod.MinYear, BudgetPeriod.MaxYear)
            .WithMessage($"O ano tem de estar entre {BudgetPeriod.MinYear} e {BudgetPeriod.MaxYear}.");
        RuleFor(c => c.Month)
            .InclusiveBetween(1, 12)
            .WithMessage("O mês tem de estar entre 1 e 12.");
        RuleFor(c => c.LimitAmount)
            .GreaterThan(0m).WithMessage("O limite tem de ser maior que zero.");
        RuleFor(c => c.LimitCurrency)
            .NotEmpty().WithMessage("A moeda é obrigatória.")
            .Length(3).WithMessage("A moeda deve ter 3 caracteres (ISO 4217).");
        RuleFor(c => c.AlertThresholdPercent!.Value)
            .InclusiveBetween(Budget.MinThreshold, Budget.MaxThreshold)
            .WithMessage($"O threshold de alerta tem de estar entre {Budget.MinThreshold} e {Budget.MaxThreshold}%.")
            .When(c => c.AlertThresholdPercent.HasValue);
        RuleFor(c => c.Notes!)
            .MaximumLength(Budget.NotesMaxLength)
            .WithMessage($"As notas têm no máximo {Budget.NotesMaxLength} caracteres.")
            .When(c => c.Notes is not null);
    }
}

public sealed class UpdateBudgetValidator : AbstractValidator<UpdateBudgetCommand>
{
    public UpdateBudgetValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.LimitAmount)
            .GreaterThan(0m).WithMessage("O limite tem de ser maior que zero.");
        RuleFor(c => c.LimitCurrency)
            .NotEmpty().WithMessage("A moeda é obrigatória.")
            .Length(3).WithMessage("A moeda deve ter 3 caracteres (ISO 4217).");
        RuleFor(c => c.AlertThresholdPercent!.Value)
            .InclusiveBetween(Budget.MinThreshold, Budget.MaxThreshold)
            .WithMessage($"O threshold de alerta tem de estar entre {Budget.MinThreshold} e {Budget.MaxThreshold}%.")
            .When(c => c.AlertThresholdPercent.HasValue);
        RuleFor(c => c.Notes!)
            .MaximumLength(Budget.NotesMaxLength)
            .WithMessage($"As notas têm no máximo {Budget.NotesMaxLength} caracteres.")
            .When(c => c.Notes is not null);
    }
}

public sealed class CreateTransferValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferValidator()
    {
        RuleFor(c => c.FromAccountId).NotEmpty().WithMessage("Conta de origem é obrigatória.");
        RuleFor(c => c.ToAccountId).NotEmpty().WithMessage("Conta de destino é obrigatória.");
        RuleFor(c => c.OccurredAt).NotEmpty().WithMessage("Data é obrigatória.");
        RuleFor(c => c.AmountOut).GreaterThan(0m).WithMessage("O valor tem de ser maior que zero.");
        RuleFor(c => c.AmountIn!.Value).GreaterThan(0m).WithMessage("O valor recebido tem de ser maior que zero.")
            .When(c => c.AmountIn.HasValue);
        RuleFor(c => c.Description!).MaximumLength(500).When(c => c.Description is not null);
    }
}

public sealed class UpdateTransferValidator : AbstractValidator<UpdateTransferCommand>
{
    public UpdateTransferValidator()
    {
        RuleFor(c => c.TransferId).NotEmpty();
        RuleFor(c => c.FromAccountId).NotEmpty().WithMessage("Conta de origem é obrigatória.");
        RuleFor(c => c.ToAccountId).NotEmpty().WithMessage("Conta de destino é obrigatória.");
        RuleFor(c => c.OccurredAt).NotEmpty().WithMessage("Data é obrigatória.");
        RuleFor(c => c.AmountOut).GreaterThan(0m).WithMessage("O valor tem de ser maior que zero.");
        RuleFor(c => c.AmountIn!.Value).GreaterThan(0m).WithMessage("O valor recebido tem de ser maior que zero.")
            .When(c => c.AmountIn.HasValue);
        RuleFor(c => c.Description!).MaximumLength(500).When(c => c.Description is not null);
    }
}

public sealed class ConvertToTransferValidator : AbstractValidator<ConvertToTransferCommand>
{
    public ConvertToTransferValidator()
    {
        RuleFor(c => c.TransactionId).NotEmpty();
        RuleFor(c => c.CounterpartAccountId).NotEmpty().WithMessage("Conta contraparte é obrigatória.");
    }
}

public sealed class ReconcileAccountValidator : AbstractValidator<ReconcileAccountCommand>
{
    public ReconcileAccountValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.Date).NotEmpty().WithMessage("Data é obrigatória.");
    }
}

public sealed class CreateInstallmentPlanValidator : AbstractValidator<CreateInstallmentPlanCommand>
{
    public CreateInstallmentPlanValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty().WithMessage("Cartão é obrigatório.");
        RuleFor(c => c.Description).NotEmpty().MaximumLength(InstallmentPlan.DescriptionMaxLength)
            .WithMessage("A descrição do plano é obrigatória (máx. 200 caracteres).");
        RuleFor(c => c.PurchaseDate).NotEmpty().WithMessage("A data da compra é obrigatória.");
        RuleFor(c => c.TotalAmount).GreaterThan(0m).WithMessage("O valor total tem de ser maior que zero.");
        RuleFor(c => c.InstallmentCount).InclusiveBetween(InstallmentPlan.MinInstallments, InstallmentPlan.MaxInstallments)
            .WithMessage("O número de prestações tem de estar entre 2 e 120.");
        RuleFor(c => c.InstallmentsAlreadyPaid).GreaterThanOrEqualTo(0)
            .WithMessage("As prestações já pagas não podem ser negativas.");
        RuleFor(c => c.AnnualRate!.Value).InclusiveBetween(0m, InstallmentPlan.MaxAnnualRate)
            .WithMessage("A TAN tem de estar entre 0 e 100 %.")
            .When(c => c.AnnualRate.HasValue);
    }
}

public sealed class UpdateInstallmentPlanValidator : AbstractValidator<UpdateInstallmentPlanCommand>
{
    public UpdateInstallmentPlanValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Description).NotEmpty().MaximumLength(InstallmentPlan.DescriptionMaxLength)
            .WithMessage("A descrição do plano é obrigatória (máx. 200 caracteres).");
        RuleFor(c => c.PurchaseDate).NotEmpty().WithMessage("A data da compra é obrigatória.");
        RuleFor(c => c.TotalAmount).GreaterThan(0m).WithMessage("O valor total tem de ser maior que zero.");
        RuleFor(c => c.InstallmentCount).InclusiveBetween(InstallmentPlan.MinInstallments, InstallmentPlan.MaxInstallments)
            .WithMessage("O número de prestações tem de estar entre 2 e 120.");
        RuleFor(c => c.InstallmentsAlreadyPaid).GreaterThanOrEqualTo(0)
            .WithMessage("As prestações já pagas não podem ser negativas.");
        RuleFor(c => c.AnnualRate!.Value).InclusiveBetween(0m, InstallmentPlan.MaxAnnualRate)
            .WithMessage("A TAN tem de estar entre 0 e 100 %.")
            .When(c => c.AnnualRate.HasValue);
    }
}
