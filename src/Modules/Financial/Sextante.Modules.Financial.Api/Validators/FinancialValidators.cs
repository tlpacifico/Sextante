using FluentValidation;
using Sextante.Modules.Financial.Application.Features.CategorizationRules;
using Sextante.Modules.Financial.Application.Features.ImportProfiles;
using Sextante.Modules.Financial.Domain.CategorizationRules;

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
