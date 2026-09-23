# Transferências recorrentes

> Ideia surgida no replanning da Phase 6.5 (2026-09-23). Fora do scope
> dessa phase por decisão explícita.

## Problema

`RecurringRule` só tem uma conta, por isso não consegue gerar
transferências automáticas (ex.: poupança mensal da conta à ordem para
uma conta poupança, reforço mensal de uma conta de investimento).

## Proposta

- `RecurringRule` ganha `Kind` (`Regular` / `Transfer`) e
  `Guid? TargetAccountId`.
- O materializer, para `Transfer`, usa o `CreateTransferCommand` da
  Phase 6.5 (duas pernas ligadas) em vez de criar uma transação.

## Dependências

Phase 6.5 (modelo de transferência).
