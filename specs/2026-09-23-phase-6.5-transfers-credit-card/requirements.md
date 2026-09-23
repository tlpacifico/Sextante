# Requirements — Phase 6.5: Transferências entre contas + cartão de crédito

## Goal

Tornar o Sextante capaz de representar **dinheiro que se move entre
contas do próprio utilizador** e **dívida de cartão de crédito**, de
forma que:

1. o **saldo de cada conta bate ao cêntimo com o banco**, e
2. os **totais de receita/despesa** (dashboard, orçamentos, export)
   contam **só gastos e ganhos reais** — sem dupla contagem do
   pagamento do cartão.

A phase nasceu da primeira tentativa de arrancar o dogfooding
(`mission.md` §6) com dados reais (conta à ordem + cartão de crédito,
agosto–setembro 2026). Três lacunas bloquearam o arranque:

- **Não existe transferência.** Cada `Transaction` tem de ter categoria
  `Income` ou `Expense`; o pagamento do cartão (saída da conta à ordem)
  e as compras do cartão contam ambos como despesa.
- **Não existe saldo atual.** A UI só mostra o saldo inicial; não há
  forma de verificar que o Sextante bate com o banco.
- **Cartão com dívida não é representável.** `Account.Create` rejeita
  saldo inicial negativo; `AccountType.CreditCard` é só um rótulo, sem
  limite, ciclo de extrato ou prestações.

Sem estas três peças o sistema não substitui a planilha — o critério
binário do MVP não é atingível. Por isso esta phase entra **antes** do
dogfooding (que passa da Phase 6 para o fim desta phase no roadmap).

Concretiza `mission.md` §3.1 (controlo de gastos fiável), §4.3
(automação auditável — transferências detetadas por regra deixam
rasto) e §4.2 (multi-moeda nativa — transferência entre contas de
moedas diferentes guarda os dois valores).

Phase 🛡️: toca schema, RLS e import → implementação **grupo a grupo** +
sub-agent deep review obrigatório antes do merge (`AGENTS.md` §2.3 e §6).

## In scope

### A. Transferências entre contas

- Uma transferência é um **par de `Transaction`s ligadas** por
  `TransferId`: perna de saída na conta de origem, perna de entrada na
  conta de destino. Sem categoria.
- `Transaction` passa a ter **direção explícita** (`Inflow` / `Outflow`)
  e **tipo** (`Regular` / `Transfer` / `Adjustment`). Hoje a direção é
  inferida da `Category.Kind` em tempo de leitura; passa a ser coluna.
- Criar, editar e apagar uma transferência atua **sempre nas duas
  pernas** atomicamente. Não é possível apagar/editar uma perna isolada.
- **Converter uma transação existente em transferência** ("marcar como
  transferência"): ligar a uma transação já existente na outra conta ou
  criar a contraperna. Necessário para limpar dados já importados.
- **Multi-moeda**: se as contas têm moedas diferentes, cada perna guarda
  o valor na moeda da sua conta (câmbio implícito = entrada / saída).
- **Exclusão dos totais**: summary, donut por categoria, orçamentos,
  alertas de orçamento e filtros por tipo consideram apenas
  `Kind = Regular`. O export CSV inclui tudo, com coluna `Tipo`
  (`Receita` / `Despesa` / `Transferência` / `Acerto`) e conta
  contraparte.

### B. Saldo atual por conta

- Saldo calculado on-demand:
  `OpeningBalance + Σ Inflow − Σ Outflow` sobre transações não apagadas
  com data ≥ `OpeningBalanceDate`, todos os tipos.
- `OpeningBalanceDate` nova em `Account` (data a que o saldo inicial se
  refere). Contas existentes: backfill com a data de criação.
- Saldo **atual** na lista de contas e **saldo à data** via endpoint
  (`at=`) para reconciliar com um extrato.

### C. Cartão de crédito com dívida + acerto de saldo

- Contas `CreditCard` aceitam **saldo inicial negativo** (dívida) e
  saldo corrente negativo. Restantes tipos mantêm `OpeningBalance ≥ 0`.
- **Acerto de saldo (reconciliação)**: o utilizador indica "o saldo
  real a DD/MM era X"; o sistema cria uma transação `Kind = Adjustment`
  pela diferença (sem categoria, fora dos totais). Diferença zero → não
  cria nada.
- **Definições de cartão** (só `CreditCard`): limite de crédito, dia de
  fecho do extrato, dia de pagamento, conta de pagamento.
- **Vista do cartão**: dívida atual, disponível (`limite − dívida`),
  gastos do ciclo corrente, extrato anterior (ciclo fechado) e próximo
  pagamento previsto.

### D. Compras em prestações

- Entidade `InstallmentPlan` associada a uma conta `CreditCard`: valor
  total, número de prestações, prestações já pagas (para planos
  anteriores ao arranque), data da 1.ª prestação, TAN opcional,
  descrição, transação de compra opcional.
- A **compra entra pelo valor total na data da compra** (a dívida do
  cartão sobe o total — é assim que o banco a reporta). O plano dá o
  **calendário de prestações futuras** na vista do cartão e entra na
  previsão do próximo pagamento.
- Juros, imposto do selo e comissões das prestações entram como
  despesas `Regular` normais (vêm no extrato; categoria sugerida
  "Custos bancários").

### E. Transferências na importação CSV

- **Conta de destino escolhida no wizard** (hoje: coluna mapeada ou
  primeira conta do tenant — inutilizável com mais de uma conta).
- `CategorizationRule` ganha **ação**: `SetCategory` (atual) ou
  `MarkAsTransfer(TargetAccountId)`. Ex.: `VIS PAGAMENTO CARTAO DE
  CREDITO` → transferência Conta à ordem → Cartão.
- No confirm, uma linha marcada como transferência **procura primeiro a
  contraperna já existente** na conta alvo (mesmo valor, direção
  oposta, |Δdata| ≤ 7 dias, ainda não ligada) e liga-a; só cria a
  contraperna se não existir. Importar os dois extratos (conta e
  cartão) não duplica a transferência.
- Linhas com data anterior a `OpeningBalanceDate` da conta são
  sinalizadas no preview e **excluídas por defeito**.
- Deteção de duplicados passa a considerar a conta.
- Direção passa a vir do sinal da linha (já é assim) para a nova coluna
  `direction`; a categoria deixa de decidir a direção.

### 0. Dívida técnica descoberta que esta phase tem de fechar

Levantada no mapeamento do código para esta spec. Entra porque ou viola
invariantes (`AGENTS.md` §3.1) ou torna saldos/totais errados — que é
exatamente o que esta phase promete corrigir.

- **RLS em falta** em `financial.categorization_rules`,
  `financial.import_profiles` e `financial.import_batches` (migration
  `20260430053400_AddCsvImport` não criou policy / FORCE / sentinel
  CHECK). Viola `AGENTS.md` §3.1.
- **Import não publica `TransactionCreatedIntegrationEvent`** → alertas
  de orçamento não disparam para transações importadas.
- **Reaplicar regras só processa 100 transações** (`PageSize` 10000 é
  clamped a 100 em `TransactionRepository.cs:45`) e não publica eventos.
- **Totais perdem transações de categorias arquivadas** (inner join com
  filtro de soft-delete em `TransactionRepository`).
- **Moeda primária `"EUR"` hardcoded** no confirm do import
  (`CsvImportHandlers.cs:349`).
- **Arquivar transação não publica evento**; **arquivar conta** não
  verifica transações ativas (`CountActiveTransactionsAsync` existe e
  não é usado).

## Out of scope

- **Transferências recorrentes** (ex.: poupança mensal automática) —
  `RecurringRule` continua com uma só conta. Backlog.
- **Importação de XLSX e PDF.** O import continua CSV-only; extratos
  XLSX/PDF convertem-se fora da app. XLSX → backlog; PDF/OCR já está na
  "Maybe pile" do roadmap.
- **Espalhar a compra em prestações pelos meses** nos relatórios/
  orçamentos (ver *Open questions*).
- **Cálculo de juros pelo Sextante** — juros vêm do extrato do banco.
- **Transferências para contas de terceiros** (MB WAY a outra pessoa,
  renda) — continuam despesas `Regular`. Transferência é só entre
  contas do próprio tenant.
- Alterações ao `PublicApi` do Financial (ver *Decisions*).
- Faturas/extratos de cartão como entidade persistida — o ciclo é
  calculado a partir das definições do cartão e das transações.

## Decisions

| # | Decisão | Porquê |
|---|---|---|
| D1 | **Transferência = par de `Transaction`s ligadas por `TransferId`**, `Kind = Transfer`, sem categoria. | Escolha do utilizador. Reusa ecrã de transações, filtros, export e cálculo de saldo sem juntar duas fontes; o par é a verdade de cada conta. |
| D2 | **`Direction` explícita na `Transaction`** (`Inflow`/`Outflow`), backfill a partir de `Category.Kind`. Para `Regular`, a direção tem de coincidir com o `Kind` da categoria; recategorizar para categoria de outro tipo inverte a direção. | Transferências e acertos não têm categoria, logo a direção não pode continuar a vir dela. `Amount` continua sempre > 0. |
| D3 | **`CategoryId` passa a nullable**; `NULL` obrigatório para `Transfer`/`Adjustment` (CHECK na DB `kind = 0 OR category_id IS NULL`). `Regular` pode estar **sem categoria** — estado legítimo, com direção própria (recorrentes sem categoria materializam como saída). `Guid.Empty` legado → `NULL` na migration. | Acaba com o sentinel `Guid.Empty` do materializer, que hoje faz linhas desaparecerem dos totais; uma transação sem categoria passa a contar pela sua direção. |
| D4 | **Totais, donut, orçamentos e alertas = só `Kind = Regular`.** Saldo = todos os tipos. | É o objetivo da phase: saldo certo e totais sem dupla contagem. |
| D5 | **Saldo on-demand** (query de agregação), sem coluna materializada. | Volume pessoal (milhares de linhas); evita cache inválida. Revisitar se um dia pesar. |
| D6 | **`OpeningBalanceDate`** em `Account`; transações anteriores não contam para o saldo (continuam nos relatórios). | Permite arrancar a meio do mês (ex.: 28/08) sem descartar histórico importado. |
| D7 | **Saldo inicial negativo só em `CreditCard`.** `OpeningBalance` continua imutável; correções via `Adjustment`. | Dívida é o estado normal de um cartão; nas outras contas um negativo inicial é quase sempre erro. Imutabilidade já é decisão da Phase 2. |
| D8 | **Compra em prestações = despesa pelo total na data da compra**; `InstallmentPlan` é informativo (calendário + previsão de pagamento). | Bate com a dívida reportada pelo banco e com o momento em que o gasto aconteceu. Espalhar pelos meses fica em aberto. |
| D9 | **Matching de contraperna no import**: mesmo valor absoluto, direção oposta, conta alvo, |Δdata| ≤ 7 dias, sem `TransferId`. Primeira candidata por proximidade de data. | Débito na conta e crédito no cartão têm datas de lançamento/valor diferentes (ex.: 11/09 vs 14/09). |
| D10 | **Sem mudança no `Financial.PublicApi`.** Transferências e acertos não têm categoria; o `BudgetAlertDispatchHandler` já ignora `CategoryId` nulo. | `AGENTS.md` §8: PublicApi conservador. |
| D11 | **ADR-014 — Transferências e saldo de conta** escrito antes do grupo 1. | Muda o modelo de domínio da `Transaction` (direção deixa de vir da categoria). |
| D12 | **Contas de cartão em moeda única** (a do cartão). Compras noutra moeda entram já convertidas pelo banco (valor debitado). | É como o extrato chega; evita modelar câmbio do emissor. |

## Context / references

- Roadmap: nova **Phase 6.5** (inserida em replanning, branch
  `replanning`, 2026-09-23), antes do "Dogfooding 1 mês" que sai da
  Phase 6.
- Phases relacionadas: Phase 2 (`Transaction`, `Account`, soft-delete,
  `OpeningBalance` imutável), Phase 3 (multi-moeda, `Money`), Phase 4
  (import CSV, regras, dedup), Phase 5a (materializer com `Guid.Empty`),
  Phase 5b (orçamentos e alertas), Phase 5.5 (inferência de tipo pelo
  sinal no import ActivoBank), Phase 6 (export CSV).
- Caso real que motivou a phase (dados fora do repo, em
  `Documents\Sextante\extratos`): extrato da conta à ordem 01/08–23/09
  (XLSX, 95 linhas, reconcilia ao cêntimo) e extrato do cartão de agosto
  (PDF, 53 movimentos, reconcilia com o total do extrato), incluindo
  pagamentos do cartão a partir da conta à ordem e dois planos de
  prestações.
- Mapeamento de código feito para esta spec: ver `plan.md` (cada grupo
  cita ficheiros e linhas).

## Open questions

1. **Prestações nos relatórios** — manter a compra pelo total na data
   (D8) ou oferecer vista "custo mensal" que distribui a compra pelas
   prestações? Resolver antes do grupo 6. Default: D8, sem distribuição.
2. **Janela do matching (D9)** — 7 dias chega para os casos ActivoBank;
   confirmar com o primeiro import real de cartão.
3. **Dia de fecho > 28** (ex.: 31) em meses curtos — proposta: fecha no
   último dia do mês. Confirmar no grupo 5.
4. **Extrato do cartão em CSV** — o ActivoBank dá o cartão só em PDF?
   Se sim, o dogfooding depende de conversão manual até existir import
   XLSX/PDF.
