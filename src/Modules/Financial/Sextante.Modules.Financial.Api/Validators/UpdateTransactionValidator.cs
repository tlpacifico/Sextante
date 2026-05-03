using FluentValidation;
using Sextante.Modules.Financial.Application.Features.Transactions;

namespace Sextante.Modules.Financial.Api.Validators;

public sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty().WithMessage("Id da transação é obrigatório.");
        RuleFor(c => c.AccountId)
            .NotEmpty().WithMessage("Conta é obrigatória.");
        RuleFor(c => c.CategoryId)
            .NotEmpty().WithMessage("Categoria é obrigatória.");
        RuleFor(c => c.Amount)
            .GreaterThan(0m).WithMessage("O valor tem de ser maior que zero.");
        RuleFor(c => c.OccurredAt)
            .NotEmpty().WithMessage("Data é obrigatória.");
        RuleFor(c => c.Description!)
            .MaximumLength(500).WithMessage("Descrição tem no máximo 500 caracteres.")
            .When(c => c.Description is not null);
    }
}
