using FluentValidation;
using Sextante.Modules.Financial.Application.Features.Transactions;

namespace Sextante.Modules.Financial.Api.Validators;

public sealed class RecategorizeTransactionsValidator : AbstractValidator<RecategorizeTransactionsCommand>
{
    public RecategorizeTransactionsValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty().WithMessage("É necessário selecionar pelo menos uma transação.")
            .Must(ids => ids.Count <= 500).WithMessage("Máximo de 500 transações por pedido.");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Categoria é obrigatória.");
    }
}
