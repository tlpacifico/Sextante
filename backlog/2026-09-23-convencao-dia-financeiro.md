# Convenção única de "dia financeiro" (data local vs instante UTC)

> Achado da revisão final do grupo 4 da Phase 6.5 (acerto de saldo),
> 2026-09-23. Pré-existente; fora do scope do grupo 4.

## Problema

Há três convenções diferentes para "a transação aconteceu no dia D":

- **Transações manuais** (`transaction-edit.dialog.ts`, `transfer.dialog.ts`):
  a data escolhida no datepicker é enviada com `toISOString()`, ou seja,
  meia-noite **local** em UTC. Em Lisboa no verão, "21/09" fica guardado
  como `2026-09-20T23:00Z`.
- **Importação CSV** (`CsvImportHandlers.cs`): meia-noite **UTC** do dia D.
- **Acertos de saldo** (grupo 4): meio-dia UTC do dia D.

O saldo à data (`AccountBalanceQuery.GetBalanceAsync`, `at = D`) corta em
`< D+1 00:00Z`. Por isso uma transação **manual** datada de D+1 em UTC+1
entra no saldo "a D".

**Cenário:** o extrato diz que o saldo no fim de 20/09 era 1 000 €. Há uma
despesa manual de 50 € datada de 21/09. O servidor calcula 950 € "a
20/09", e a reconciliação cria um acerto de +50 €. A partir daí o saldo
atual fica 50 € acima do real.

Pelo mesmo motivo, a listagem (`date:'dd/MM/yyyy'` em hora local) e os
filtros de intervalo de datas podem mostrar as linhas importadas e as
manuais em dias diferentes, conforme o fuso.

### Agravado no grupo 5 (vista do cartão)

Revisão profunda do grupo 5: o mesmo desvio passa a **mover dinheiro entre
extratos**, e já não só um saldo à data. Exemplo: fecho a 15, em Lisboa no
verão. Uma compra manual datada de 16/09 fica guardada como
`2026-09-15T23:00Z`. Entra no extrato já fechado ("Dívida no fecho" e
"Gastos" do extrato anterior sobem) e aumenta o próximo pagamento
previsto. Também não aparece na lista de movimentos do ciclo corrente, que
começa a `16/09T00:00Z`. Ficheiros envolvidos: `CreditCardActivityQuery.cs`,
`AccountHandlers.cs` (`GetCreditCardViewQuery`) e `credit-card.page.ts`.

**Prioridade: resolver antes de usar dados reais do cartão** com compras
introduzidas à mão.

## Proposta

- Escolher uma convenção e documentá-la (candidato: ADR-014 ou um ADR
  novo). Por exemplo: as datas escolhidas pelo utilizador guardam-se sempre
  como meio-dia UTC da data local, que é estável em qualquer fuso até ±11h.
  O corte do saldo e os filtros usam a mesma convenção.
- Migration de backfill das transações manuais existentes, se a convenção
  mudar.
- Teste de integração que fixe a convenção: uma transação manual em
  `D 23:00Z` não entra no saldo a D.

## Dependências

- Deve ficar resolvido antes da reconciliação com dados reais
  (`validation.md` §"Reconciliação real", grupo 8.1) se houver linhas
  introduzidas à mão.
