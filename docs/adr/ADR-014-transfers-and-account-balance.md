# ADR-014 — Transferências entre contas e saldo de conta

**Estado**: Adopted
**Data**: 2026-09-23
**Autores**: Thacio

---

## Context

O primeiro arranque do dogfooding (`mission.md` §6) com dados reais — uma
conta à ordem e um cartão de crédito pago a partir dela — mostrou três
lacunas no modelo do módulo Financial:

1. **Não há transferência.** Cada `Transaction` pertence a uma conta e tem
   obrigatoriamente uma categoria `Income` ou `Expense`. O pagamento
   mensal do cartão sai da conta à ordem como despesa, e as compras do
   cartão também são despesas: o mesmo gasto conta duas vezes nos totais,
   no donut e nos orçamentos.
2. **A direção vem da categoria.** `Amount` é sempre positivo e o sinal
   (entrada/saída) é inferido em tempo de leitura a partir de
   `Category.Kind`. Um movimento sem categoria — como uma transferência ou
   um acerto de saldo — não tem direção. O materializer de recorrentes já
   contorna isto gravando `Guid.Empty` como categoria, e essas linhas
   desaparecem silenciosamente dos totais (inner join com categorias).
3. **Não há saldo.** A UI mostra só o saldo inicial; nada permite
   verificar que o Sextante bate com o banco.

A decisão foi tomada no replanning de 2026-09-23 (Phase 6.5, decisões
D1–D6 em `specs/2026-09-23-phase-6.5-transfers-credit-card/requirements.md`).

---

## Decision

1. **Transferência = par de `Transaction`s ligadas** por um `TransferId`
   (Guid v7) comum: perna de saída na conta de origem, perna de entrada na
   conta de destino. As pernas têm `Kind = Transfer` e **não têm
   categoria**. Criar, editar e apagar atua sempre nas duas pernas, numa
   só transação de base de dados. Entre contas de moedas diferentes, cada
   perna guarda o valor na moeda da sua conta.
2. **Direção explícita.** `Transaction` ganha `Direction`
   (`Inflow` / `Outflow`) e `Kind` (`Regular` / `Transfer` /
   `Adjustment`). `Amount` continua sempre positivo. As linhas existentes
   recebem a direção por backfill a partir de `Category.Kind` (incluindo
   categorias arquivadas). Para `Regular`, a direção tem de coincidir com
   o tipo da categoria.
3. **Categoria nullable com CHECK.** `category_id` passa a aceitar `NULL`;
   a base de dados garante `(kind = Regular) ⇔ (category_id IS NOT NULL)` e
   `(kind = Transfer) ⇔ (transfer_id IS NOT NULL)`. O sentinel
   `Guid.Empty` deixa de existir.
4. **Totais só com `Regular`.** Summary, donut por categoria, orçamentos e
   alertas de orçamento consideram apenas `Kind = Regular`, pela
   `Direction`, sem depender de `Category.Kind`. O export CSV inclui todos
   os tipos, identificados na coluna `Tipo`.
5. **Saldo on-demand.** `saldo = OpeningBalance + Σ Inflow − Σ Outflow`
   sobre transações não apagadas de todos os tipos, com data igual ou
   posterior à nova `Account.OpeningBalanceDate`. Calculado por query de
   agregação, sem coluna materializada.
6. **`Financial.PublicApi` inalterado.** Transferências e acertos publicam
   os eventos de transação com `CategoryId = null`, que o subscriber de
   orçamentos já ignora.

---

## Consequences

**Positivas**

- O saldo de cada conta passa a ser verificável contra o extrato do banco.
- O pagamento do cartão deixa de inflacionar despesas; os gastos reais
  ficam onde aconteceram (as compras no cartão).
- Ecrã de transações, filtros, export e cálculo de saldo continuam a ler
  uma só tabela.
- Acaba o sentinel `Guid.Empty` e o bug de linhas que desaparecem dos
  totais.

**Negativas / custos**

- Todas as agregações de `TransactionRepository` e o
  `BudgetProgressService` têm de ser reescritas para usar `Direction` e
  `Kind`.
- Migration com backfill sobre dados de produção; tem de ser testada
  sobre uma base com dados das Phases 2–6.
- O par de pernas é um invariante que vive no código (handlers de
  transferência) e parcialmente na DB (CHECKs); editar uma perna isolada
  tem de ser recusado explicitamente.
- Saldo on-demand custa uma agregação por pedido — aceitável no volume de
  uso pessoal; revisitar se deixar de o ser.

---

## Alternatives considered

- **Entidade `Transfer` própria** (tabela `financial.transfers` com contas
  de origem/destino, valor e data). Conceptualmente limpa, mas todas as
  listagens de movimentos, o export e o saldo teriam de juntar duas
  fontes. Rejeitada.
- **Categoria de sistema "Transferência"** excluída dos totais. Mínimo de
  código, mas não liga as duas pernas (apagar uma deixa a outra órfã), e
  os totais passariam a depender de excluir uma categoria por convenção.
  Rejeitada.
- **Saldo materializado numa coluna da conta**, atualizado por eventos.
  Rápido de ler, mas sujeito a divergir da soma real após edições,
  arquivos e imports. Rejeitada no MVP (ver *Consequences*).
