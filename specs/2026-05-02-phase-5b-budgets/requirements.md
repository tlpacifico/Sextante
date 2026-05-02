# Requirements — Phase 5b: Budgets + alertas + metas em moeda específica

## Goal

Entregar **metas orçamentais por categoria** com período mensal (calendar
month), dashboard com progresso (% consumido, valor restante, projeção
de fim de período), alertas in-app em 80% e 100%, e suporte nativo a
metas em moeda diferente da primary do tenant. Phase 5a entregou
recurrings + Hangfire wrapper + audit `recurring_rule_id`. Phase 5b
fecha os bullets 5–8 do `roadmap.md` § Phase 5 e completa o requisito
de produto (`mission.md` §3.1: *"metas mensais por categoria"*) que
falta para o critério MVP de 1 mês de dogfooding (`mission.md` §6).

Esta phase concretiza o princípio §4.3 da mission (*"automação sobre
manualidade — mas auditável"*) — alertas são automáticos mas o user vê
exatamente quanto e quando — e os princípios §4.2 e §4.4 (multi-moeda
nativa + degradação graciosa quando o ECB falha).

## In scope

- **`Budget` entity** em `Module.Financial.Domain.Budgets`:
  - `Id (Guid v7)`, `TenantId`, `CategoryId (FK financial.categories)`.
  - `Period (BudgetPeriod VO)` — `(int Year, int Month)` representando
    um mês civil; método `Period.Contains(DateOnly)` true se a data
    cai entre dia 1 e último dia do mês.
  - `Limit (Money)` — Money VO Phase 2; `Limit.Amount > 0`.
  - `AlertThresholdPercent (int, default 80)` — percentagem do limit
    a partir da qual o primeiro alerta dispara. Range `[1, 99]`. O
    threshold de 100% é sempre disparado (hardcoded), independente
    deste valor.
  - `Notes (varchar(500)?)` — opcional.
  - Audit + soft-delete uniformes (`tech-stack.md` §7.4, §7.5).
  - Implementa `ITenantOwned`.
- **Constraint de unicidade** — 1 Budget ativo por
  `(tenant_id, category_id, year, month)`. Soft-deleted budgets não
  contam para a constraint (consistente com Phase 4 ImportProfile).
- **CRUD completo** — endpoints REST + UI Angular (lista por mês,
  criar, editar, apagar). Sem "duplicar para próximo mês" no MVP
  (manual recreation; backlog se vier a ser pedido).
- **Cálculo on-demand** de `BudgetProgress`:
  - Query agregadora sobre `financial.transactions` filtrada por
    `category_id`, `occurred_at >= period.Start AND <= period.End`,
    `deleted_at IS NULL`.
  - **Conversão de moeda**: cada transaction tem
    `Amount + ExchangeRateToPrimary` (Phase 3). Se Budget está em
    moeda diferente da Transaction, converter via ECB rate snapshot
    do dia da transaction (`ExchangeRateAt`). Soma feita em
    Budget.Limit.Currency.
  - **Sinal**: budget é meta de **gasto** por categoria (Phase 2
    decidiu Category.Kind ∈ {Expense, Income}; Budget só faz sentido
    para Expense — ver Open questions). Soma `|amount|` de
    transactions onde Category é Expense; ignora Income da soma
    (ainda que rare).
  - Output: `{ Spent: Money, Remaining: Money, PercentUsed: decimal,
    ProjectedEndOfPeriod: Money? }`.
  - **Projeção**: `(Spent / DaysElapsed) * DaysInMonth` quando
    `DaysElapsed >= 5` (suficiente para tendência); senão `null`.
- **`BudgetAlert` entity** (audit de alertas disparados):
  - `Id (Guid v7)`, `TenantId`, `BudgetId (FK)`, `Threshold (int)` —
    80, 100, ou outro custom; `TriggeredAt (timestamptz UTC)`,
    `SpentAtTrigger (Money)`, `Acknowledged (bool, default false)`,
    `AcknowledgedAt (timestamptz?)`.
  - **Idempotência**: unique index `(tenant_id, budget_id, threshold)`
    where `deleted_at IS NULL`. Garante 1 alerta por budget+threshold
    por período (period é implícito porque budget é per-month e
    alerts vivem com o budget — quando user cria budget para mês
    seguinte, é nova entity, novos alerts).
- **Dispatch de alertas** — Wolverine handler subscreve
  `TransactionCreatedIntegrationEvent` e
  `TransactionUpdatedIntegrationEvent` (Phase 2):
  - Recalcula `BudgetProgress` para o(s) Budget(s) afetado(s)
    (categoria + mês da transaction).
  - Se `PercentUsed >= AlertThresholdPercent` e não existe alerta
    para esse threshold → cria `BudgetAlert(threshold=AlertThreshold)`.
  - Se `PercentUsed >= 100` e não existe alerta para 100 → cria
    `BudgetAlert(threshold=100)`.
  - Idempotente: insert protegido pelo unique index;
    `UniqueConstraintViolation` é capturado e logado como debug.
- **Endpoint `GET /api/financial/budgets/alerts/active`** — retorna
  alertas não-`Acknowledged` para o tenant atual, ordenados por
  `TriggeredAt DESC`. Usado pelo banner do Dashboard.
- **Endpoint `POST /api/financial/budgets/alerts/{id}/acknowledge`** —
  marca alerta como reconhecido (`Acknowledged = true`,
  `AcknowledgedAt = now`).
- **Endpoint `GET /api/financial/budgets?year=YYYY&month=MM`** — lista
  budgets do mês com progresso embutido (default = mês corrente).
- **Endpoint `GET /api/financial/budgets/{id}/progress`** —
  `BudgetProgress` standalone (mesmo cálculo, mas para 1 budget).
- **UI Página `/app/budgets`**:
  - Mês picker (`p-datepicker` mode="month"). Default = mês corrente.
  - PrimeNG `p-table` com Categoria, Limit (Money), Spent, Remaining,
    %, Projeção (badge cinza se < 5 dias), Threshold, Ações.
  - Linhas em estado "alerta" (≥80%) com tinta amarela; ≥100% com
    vermelha. Toast + banner persistente (apenas 1 banner agregando
    N alertas, com link "Ver" → expande lista).
  - Botão "Novo orçamento" abre dialog (Categoria, Limit + Currency,
    Threshold opcional).
  - Empty state: "Sem orçamentos para {mês}. Cria o primeiro para
    começar a controlar gastos por categoria."
- **Dashboard (Phase 2/3) ganha `BudgetProgressCard`**:
  - Topo do dashboard, abaixo dos cards de totais existentes.
  - Mostra **até 4** budgets do mês corrente com maior `PercentUsed`
    (incluindo os que excederam 100%). Link "Ver todos"
    → `/app/budgets`.
  - Banner persistente acima do dashboard agrupando alertas activos
    (consume `/budgets/alerts/active`); botão "Reconhecer" marca todos
    como `Acknowledged`.
- **Multi-tenancy via RLS + Global Query Filter** (mesmo pattern Phases
  1a–5a). `Budget` e `BudgetAlert` têm RLS + FORCE + sentinel CHECK.
- **Domain unit tests** — `BudgetTests` (Limit > 0, threshold range,
  Period invariants); `BudgetPeriodTests` (Contains, Start, End,
  DaysInMonth, leap year February); `BudgetProgressCalculator` (mix
  de moedas, soft-deleted excluídas, conversão ECB).
- **Application unit tests** —
  `Create/Update/Delete BudgetHandler`, `BudgetProgressQueryHandler`,
  `BudgetAlertDispatchHandler` (idempotência, 80→100 transição,
  threshold custom, transaction de Income ignorada).
- **Architecture tests** — `Budget` em Domain sem Hangfire/EF/
  Wolverine; implementa `ITenantOwned`; alert dispatch em Application.
- **Integration tests** — pipeline criar Budget → criar Transaction
  → alerta dispara; multi-tenancy (alerts de A invisíveis a B);
  multi-currency (Budget em USD, Transaction em EUR → progresso
  correto via ECB); idempotência de alerts.
- **Karma unit tests** — `budgets.page.ts`, dialog,
  `budget-progress-card.component.ts`, `budget-alerts-banner.component.ts`,
  `budget-api.service.ts`.
- **Manual walkthrough** — Cenários 1–3 da `validation.md` (criar
  budget €500/Habitação/Maio → registar transações → alertas 80%
  e 100% disparam → reconhecer; multi-currency com Budget em USD;
  multi-tenancy isolation).
- **Responsive sanity check** — `tech-stack.md` §19.5 em
  375 / 768 / 1280 px nas páginas novas/modificadas.
- **`CHANGELOG.md`** com entrada datada da Phase 5b.

## Out of scope

- **Períodos não-mensais** (semanal, trimestral, anual). Decisão
  locked via AskUserQuestion: monthly only no MVP. Yearly e weekly
  ficam para backlog se houver pedido durante dogfooding.
- **Orçamento total mensal agregado** (sum de todos os budgets).
  Calculável trivialmente no frontend; sem entity dedicada para
  evitar duplicate source of truth.
- **Hierarquia de budgets** (parent/child por sub-categoria). Phase 2
  decidiu categorias 1 nível; budgets seguem o mesmo.
- **Email / push notifications** dos alertas. Phase 15 (Notificações).
  Phase 5b: in-app toast + banner persistente apenas.
- **Materialização de progresso** (tabela cache atualizada em background).
  Cálculo on-demand é suficiente para 10–30 budgets/tenant; trade-off
  documentado em Decisions. Re-avaliar pós-dogfooding se latência for
  problema (improvável: filtro `(tenant_id, category_id, occurred_at)`
  já existe da Phase 2).
- **Carry-over** (sobra/excesso do mês passado afeta o seguinte).
  Cada Budget é independente do anterior; user re-cria mensalmente.
  Backlog se pedido.
- **Auto-criação do próximo mês** (job que clona budgets do mês
  passado). Manual no MVP; backlog se for tedioso durante dogfooding.
- **Budgets para Income categories** ("ganhar pelo menos €X de
  freelance este mês"). Conceptualmente coerente mas adia complexidade
  (sinal invertido, alertas de "abaixo de X%"). Backlog.
- **Edição retroativa com semântica explícita** — editar `Limit` a
  meio do mês ajusta o cálculo *imediatamente* (próxima query usa o
  novo limit). Alertas já emitidos para o threshold antigo permanecem
  (audit). Sem "regenerar alertas" ou "rollback do alerta antigo".
  Documentado como expected behavior, não como feature.
- **Notificações de transação acima do limit antes de criar**
  (warning preemptivo no form de Transaction). Backlog UX —
  cabe em Phase de polish.
- **Sub-agent deep review** — Phase 5b não é 🛡️ no roadmap (Phase 6
  é a próxima 🛡️). Review opcional decidida no PR; não é
  merge-blocker.

## Decisions

- **Período = mês civil.** *Why*: locked via AskUserQuestion;
  alinha com extratos bancários, recorrentes mensais (Phase 5a) e
  o use case primário do user (controlo de despesas mensais).
  Suportar weekly/quarterly/yearly multiplica complexidade do
  `BudgetPeriod` VO + dashboard sem ROI claro no MVP. **How to
  apply**: `BudgetPeriod` é record `(int Year, int Month)` com
  validação `Year ∈ [2000, 2100]`, `Month ∈ [1, 12]`. Conversões
  para `DateOnly` via `Start = new DateOnly(Year, Month, 1)` e
  `End = Start.AddMonths(1).AddDays(-1)`.

- **Cálculo on-demand (sem cache).** *Why*: Não selecionado
  explicitamente pelo user mas é a opção recomendada. MVP target
  são 10–30 budgets por tenant; query agregadora sobre transactions
  já indexadas em `(tenant_id, category_id, occurred_at)` (Phase 2)
  é O(centenas-poucos-milhares de rows). Cache materializada
  introduz drift, job de refresh, complexity de invalidação;
  trade-off não vale para o volume MVP. **How to apply**:
  `BudgetProgressQueryHandler` faz `SUM(amount * COALESCE(rate, 1))
  GROUP BY budget_id` num único round-trip. Re-avaliar pós-dogfooding
  se latência > 200ms na page Budgets.

- **Alertas in-app (toast + banner persistente).** *Why*: Não
  selecionado explicitamente mas alinha com a opção recomendada e
  com `tech-stack.md` §13 (notificações pós-MVP, Phase 15).
  Disparar email/push agora exige decisão de provider SMTP que
  está adiada para Phase 6 (`tech-stack.md` §12). **How to apply**:
  Banner agregado em `app-shell.component.ts` consome
  `/budgets/alerts/active`. Toast disparado no client quando
  `BudgetAlertCreatedIntegrationEvent` chega via push (deferido —
  Phase 15) **ou** quando próximo poll do banner detecta alerta
  novo. **MVP**: polling a cada 60s no banner é suficiente
  (low-frequency event).

- **Granularidade: 1 Budget por (categoria, mês).** *Why*: Não
  selecionado explicitamente mas é a opção recomendada e é
  invariante natural — o user pensa "renda este mês: €500", não
  "renda parte 1: €300, renda parte 2: €200". Unique index
  `(tenant_id, category_id, year, month)` enforced no DB.
  **How to apply**: `CreateBudgetHandler` valida via query antes
  de insert (UX melhor que esperar `UniqueConstraintViolation`);
  unique index parcial é defesa em profundidade.

- **Threshold default 80%, customizable [1, 99].** *Why*: Roadmap
  bullet diz "80% e 100% (configurável)". 100% é hardcoded (sempre
  alerta — é o evento crítico); 80% é o customizable. User pode
  baixar para 50% se preferir aviso antecipado, ou subir até 99%
  se prefere zero-aviso preventivo. **How to apply**: campo
  `AlertThresholdPercent (int, default 80)` no Budget; range check
  no domain ctor. UI tem dropdown com presets (50/70/80/90)
  + "Custom".

- **`BudgetAlert` entity persistida (não computed).** *Why*:
  Idempotência: dispatch handler corre N vezes (Wolverine retries,
  multi-Transaction batches em CSV import Phase 4); precisamos de
  persistência stable para "já alertei este threshold". Audit
  trail: user pode ver quando o alerta disparou, com que valor.
  **How to apply**: unique index `(tenant_id, budget_id, threshold)`
  WHERE `deleted_at IS NULL`. `Acknowledged` separado de
  `DeletedAt` (acknowledge ≠ apagar — histórico mantém-se).

- **Dispatch via `TransactionCreated/Updated IntegrationEvent`.**
  *Why*: Phase 2 já publica esses events. Wolverine handler
  in-process subscreve, recalcula budget(s) afetado(s), insere
  alerta se threshold cruzado. Fire-and-forget consistente com
  princípio §3.2 do tech-stack ("default = mensagem"). **How to
  apply**: `BudgetAlertDispatchHandler` em
  `Module.Financial.Application.Features.Budgets.Alerts`. Recalcula
  apenas budgets para `(transaction.CategoryId, period that contains
  transaction.OccurredAt)`. Skip se transaction não tem
  CategoryId ou Category.Kind != Expense.

- **Conversão de moeda a custo da query, não do storage.** *Why*:
  `tech-stack.md` §9: "reporting converte; storage não". Cada
  Transaction guarda Money + ExchangeRateToPrimary (Phase 3).
  Budget pode estar em moeda diferente; converter na query é
  consistente com Phase 3 dashboard. **How to apply**:
  - Se `Budget.Currency == Transaction.Currency`: soma directa.
  - Se `Budget.Currency == TenantPrimary`: usa
    `Transaction.Amount * ExchangeRateToPrimary`.
  - Se `Budget.Currency` ≠ ambos: converte via
    `Transaction.Amount * ExchangeRateToPrimary * (1 / RateAt(BudgetCurrency, primary, Transaction.OccurredAt))`.
    Lookup do ECB snapshot via `IExchangeRateService.ResolveAsync`
    (Phase 3) na data da Transaction.
  - Se ECB miss para qualquer leg: skip a transaction da soma e
    log warning. `BudgetProgress.HasIncomplete` flag → UI mostra
    badge "Cálculo parcial" com tooltip explicativo. **Não** falha
    o request inteiro (degradação graciosa, princípio §4.4).

- **Soft-delete uniforme.** *Why*: tech-stack §7.4. Apagar Budget
  preserva alerts históricos via FK ON DELETE SET NULL? **Não**:
  alerts não fazem sentido sem o budget — usar ON DELETE CASCADE
  para alerts. Quando user soft-deleta o budget, alerts ficam
  soft-deleted juntos via cascade aplicado no handler (não no DB,
  para preservar audit; ver How to apply). **How to apply**: no
  `DeleteBudgetHandler`, soft-delete o budget e os seus alerts
  numa Tx (`SaveChangesAsync` único). FK ON DELETE CASCADE no DB
  só dispara em hard-delete (não acontece no MVP).

- **Limit > 0.** *Why*: Budget de €0 não faz sentido (um
  "não-budget"). User pode toggle archive/inactive em vez. **How
  to apply**: `Money.Amount > 0` enforced no `Budget` ctor;
  FluentValidation no `CreateBudgetCommand`.

- **UI mobile-first.** *Why*: `mission.md` §4.6, `tech-stack.md`
  §19.5. Lista de Budgets + dialog + BudgetProgressCard +
  banner têm de funcionar em 375 px. **How to apply**: `p-table`
  com `overflow-x-auto`; dialog com
  `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`; banner
  com truncate + "..." quando N alerts > 3 em mobile.

## Context / references

- `specs/roadmap.md` — secção "Phase 5 — Recorrentes + Hangfire +
  Metas". Phase 5b fecha bullets 5–8: Budget entity, dashboard
  com % consumido + projeção, alertas 80%/100% configurável, metas
  em moeda específica.
- `specs/mission.md` — §3.1 (proposta de valor: "metas mensais por
  categoria"), §4.2 (multi-moeda nativa), §4.3 (automação
  auditável: alertas automáticos mas history visível), §4.4
  (degradação graciosa: ECB miss não bloqueia), §4.6 (responsive),
  §6 (MVP done = 1 mês de dogfooding com metas a mostrar progresso).
- `specs/tech-stack.md` — §3.5 (CQRS via Wolverine), §4
  (multi-tenancy invariante: RLS + Global Query Filter), §5
  (schema `financial`), §7 (Money VO, audit, soft-delete), §9
  (reporting converte; storage não), §11 (Wolverine
  inter-module — dispatch handler), §13 (logs sem PII), §19.5
  (responsive).
- `specs/2026-04-26-phase-2-financial-core/` — entregou
  `Transaction.CategoryId`, `Transaction.OccurredAt`, Money VO,
  audit columns, soft-delete; `TransactionCreated/Updated
  IntegrationEvent`. Phase 5b consome estes eventos.
- `specs/2026-04-27-phase-3-multi-currency/` — entregou
  `IExchangeRateService.ResolveAsync` + ECB snapshot table.
  Phase 5b usa para conversão. Confirmado pattern de
  "graceful skip on miss" que Phase 5b reaplica.
- `specs/2026-04-29-phase-4-csv-import/` — entregou
  `Transaction.categorization_rule_id` audit pattern; Phase 5b
  segue o mesmo style para `BudgetAlert.budget_id` (FK preserva
  audit).
- `specs/2026-05-01-phase-5a-recurring/` — entregou
  `RecurringRule` + materialização gera Transactions com `OccurredAt`
  no dia da ocorrência. Budget recalcula automaticamente quando
  estas Transactions são criadas (publish do `TransactionCreated
  IntegrationEvent` no flow do materializer Phase 5a).
- `src/Modules/Financial/.../Domain/Transactions/Transaction.cs` —
  fonte para query de progresso.
- `src/Modules/Financial/.../Domain/Categories/Category.cs` —
  `Kind` enum determina se Budget aplica (só Expense no MVP).
- `src/Modules/Financial/.../Application/ExchangeRates/IExchangeRateService.cs`
  — Phase 3.
- `src/BuildingBlocks/SharedKernel/Money.cs` — VO usado para
  `Budget.Limit` e `Spent` / `Remaining`.
- `src/BuildingBlocks/SharedKernel/Sextante.SharedKernel/ITenantOwned.cs`
  — invariante.

## Open questions

- **Budgets para Income categories.** Decidi OUT em §Out of scope,
  mas se durante dogfooding o user quiser controlar receita
  mínima ("freelance: €200/mês mínimo"), backlog. Sinal e direção
  de alerta inverteriam (alert quando `< threshold` em vez de
  `>= threshold`).
- **Auto-criação do próximo mês.** Manual no MVP. Se durante
  dogfooding o user achar tedioso recriar 5–10 budgets todo dia 1,
  Phase 6 ou backlog adiciona "Duplicar para próximo mês" (ação
  na lista) e/ou job Hangfire que clona automaticamente no dia 1
  (`TenantAwareJob<T>` Phase 5a).
- **Frequência do polling do banner.** 60s default; configurável
  via setting? Provavelmente over-engineering para MVP.
- **Categoria sem budget mas com gasto significativo.** Banner
  só mostra alertas de budgets existentes. Se user gasta €500 em
  "Lazer" sem budget, dashboard não avisa. Phase de polish ou
  dogfooding feedback decidirá se há "discovery prompt"
  ("Categoria X teve gasto significativo este mês — criar
  budget?").
- **Alertas em moeda do Budget vs primary.** Banner mostra
  "€450 de €500 (90%)". Se Budget está em USD e primary em EUR,
  qual valor o banner mostra? **Decisão para implementação**:
  banner mostra na **moeda do Budget** (era a intenção do user
  ao criar). Tooltip mostra equivalente na primary.
- **Phase 5b é gateway para Phase 6 dogfooding.** Roadmap Phase 6
  exige "pelo menos 3 metas a mostrar progresso" como critério de
  done MVP. Esta phase tem de fechar antes do dogfooding começar.
- **Wolverine dispatch ordering.** Se 5 transactions são criadas
  num CSV import (Phase 4), Wolverine processa em paralelo? Se
  sim, race condition entre 2 dispatches do mesmo budget pode
  inserir o mesmo alerta 2× — unique index protege, mas debugar
  exception confunde. **Decisão para implementação**: handler
  apanha `UniqueConstraintViolation` e log debug ("alert already
  triggered concurrently"); não é error.
- **CSV import Phase 4 + alertas em batch.** Importar 100
  transactions de uma vez pode disparar dezenas de alertas em
  cascata. UX em mobile fica confusa. **Decisão para
  implementação**: dispatch handler é fire-and-forget; banner faz
  poll periódico (não push instantâneo) e agrega; user vê 1 banner
  com "5 orçamentos atingiram threshold" em vez de 5 toasts.
