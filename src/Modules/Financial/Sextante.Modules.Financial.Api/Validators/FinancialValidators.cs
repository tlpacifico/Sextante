using FluentValidation;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Application.Features.CategorizationRules;
using Sextante.Modules.Financial.Application.Features.ImportProfiles;
using Sextante.Modules.Financial.Application.Features.RecurringRules;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.CategorizationRules;
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
