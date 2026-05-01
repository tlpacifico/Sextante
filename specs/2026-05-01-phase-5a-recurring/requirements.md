# Requirements — Phase 5a: Transações recorrentes + Hangfire

## Goal

Entregar transações recorrentes (renda, salário, subscrições, prestações)
que aparecem automaticamente na data prevista, com job Hangfire idempotente
wrapped em `TenantAwareJob<T>`. Phase 2 entregou `Transaction` manual;
Phase 3 entregou multi-moeda + Hangfire setup (ECB job); Phase 4 entregou
import + categorização. Phase 5a junta Hangfire + Transaction:
`RecurringRule` materializa `Transaction` na data prevista, com audit trail
(`recurring_rule_id` na Transaction) e UI para preview das próximas
ocorrências.

Esta phase concretiza o princípio de produto §4.3 da mission (*"automação
sobre manualidade — mas auditável"*) e cobre os 4 primeiros bullets do
`roadmap.md` § Phase 5. **Phase 5 foi dividida em 5a (recurrings) e 5b
(budgets)** durante kickoff via AskUserQuestion: o user scoped down para
recurring + Hangfire generation only; Budget + alertas + edit semantics
ficaram fora desta phase.

## In scope

- **`RecurringRule` entity** com `Frequency` enum (Daily / Weekly /
  Monthly / Yearly), `Interval` (int ≥1 — "every N days/weeks/...").
  Campos: `StartDate`, `EndDate?` (nullable, open-ended), `NextOccurrence`
  computed/stored, `Description`, `Amount` (Money VO), `Account` FK,
  `Category?` FK, `Tags?` (jsonb, consistente com Transaction Phase 2).
  Soft-delete + audit + `ITenantOwned`.
- **CRUD completo** — endpoints REST + UI Angular (lista, criar, editar,
  apagar, ativar/desativar via `IsActive`).
- **Hangfire recurring job** `RecurringTransactionMaterializerJob`
  agendado diariamente (00:15 UTC, antes do ECB job das 00:30 para evitar
  contention; ambos são idempotentes) que materializa `Transaction` para
  cada `RecurringRule` com `NextOccurrence ≤ today`.
- **`TenantAwareJob<T>` wrapper** — primeira vez que o pattern é usado
  (Phase 3 ECB job é cross-tenant, sem wrapper). Wrapper lê
  `TenantId` do payload do job e configura `ITenantContext` antes do
  handler. Sem wrapper → `ITenantContext` lança (fail-loud, tech-stack
  §4.4).
- **Idempotent generation** — lookup por `(RecurringRuleId, OccurrenceDate)`
  antes de insert. Hangfire retry / restart / re-run manual nunca duplica.
  Mirror do pattern do `EcbSnapshotJob` (Phase 3).
- **Audit na Transaction** — coluna nova `recurring_rule_id (uuid NULL,
  FK financial.recurring_rules ON DELETE SET NULL)`. Preenchida quando
  Transaction é materializada por job; `NULL` em transações manuais e em
  transações importadas via CSV (Phase 4 não toca esta coluna).
- **`NextOccurrence` calculation** — domain logic em `RecurringRule`
  (avança após cada materialização; respeita `EndDate`; pára quando
  `NextOccurrence > EndDate`). Determinístico, testável.
- **UI: lista de recurrings** (`/app/recurrings`) com PrimeNG `p-table` —
  Description, Amount, Frequency, NextOccurrence, IsActive, Ações.
- **UI: dialog criar/editar** — Description, Amount + Currency (default
  account currency), Frequency dropdown, Interval (default 1), StartDate,
  EndDate?, Account, Category?, Tags?.
- **UI: preview das próximas N ocorrências** — botão "Ver próximas" no
  row → dialog mostra próximas 10 occurrences calculadas (não
  materializadas — preview puro). Quando uma ocorrência futura coincide
  com `NextOccurrence`, mostra badge "próxima a materializar".
- **UI: histórico de transações geradas por regra** — link "Ver
  transações" no row → filtro pré-aplicado em `/app/dashboard` por
  `recurringRuleId=<id>` (parâmetro de query novo no endpoint
  `/api/financial/transactions`).
- **Domain unit tests** — `RecurringRule` invariants (Frequency válida,
  Interval ≥1, StartDate ≤ EndDate, NextOccurrence advance per
  Frequency).
- **Application unit tests** — `RecurringTransactionMaterializerHandler`
  (idempotência, EndDate boundary, multi-rule batch); `TenantAwareJob<T>`
  wrapper (TenantId from payload, fail-loud sem TenantId).
- **Architecture tests** — `RecurringRule` implementa `ITenantOwned`;
  Domain não referencia Hangfire; `TenantAwareJob<T>` vive em
  `Sextante.Infrastructure` (BuildingBlock).
- **Integration tests** — pipeline completo (criar regra → avançar
  clock → job invocado → Transaction materializada uma única vez);
  multi-tenancy (regra de tenant A não materializa em tenant B);
  idempotência (job invocado 2× sem duplicar); EndDate respeitado.
- **Karma unit tests** — recurring-rules page, dialog, preview-occurrences
  dialog, API service.
- **Manual walkthrough** — criar regra de "Renda mensal €500" no dia
  corrente; verificar que transação aparece no Dashboard automaticamente
  no próximo run do job (forçar via Hangfire dashboard
  `/api/admin/hangfire`).
- **Responsive sanity check** — per `tech-stack.md` §19.5 em 375/768/1280
  px.

## Out of scope

- **Budget entity / metas / alertas 80%/100%** — Phase 5b. Esta phase
  apenas materializa transações, não compara contra budgets.
- **Edit recurring com semântica "aplicar só a futuras" vs "todas
  pendentes"** — deferido. Phase 5a permite editar a regra (afeta
  ocorrências futuras a partir de `NextOccurrence`); ocorrências já
  materializadas ficam imutáveis (são `Transaction` regulares,
  editáveis individualmente via Phase 2 CRUD). Sem opção de "reaplicar
  alteração a transações já geradas". Critério de Phase 5b ou backlog
  separado se vier a ser necessário.
- **Notificações de geração** (email / push). Phase 15.
- **Recurrings com schedule complexo** — sem CRON expressions, sem
  "primeiro dia útil do mês", sem "todas as quintas-feiras exceto
  feriados". Apenas `Frequency × Interval × StartDate`.
- **Suspender/retomar regras** — só `IsActive` toggle (fica mantida
  como entity, materialização pára enquanto inactive). Sem "pausar até
  data X".
- **Materialização proativa em UI** — botão "Materializar agora"
  (forçar geração imediata sem esperar pelo job) está OUT. Job daily é
  o único caminho. Hangfire dashboard permite trigger manual ao
  SystemAdmin para troubleshooting.
- **Backfill de regras criadas com `StartDate` no passado** — se user
  cria regra com `StartDate = 2026-01-01` em `2026-05-01`, o job
  materializa apenas a próxima ocorrência futura, não as 4 ocorrências
  do passado. Avisar no UI com hint visual ("StartDate no passado:
  apenas a próxima ocorrência será materializada"). Sem opção de
  backfill automático.
- **Recurrings em moeda ≠ tenant primary currency** — suportado (a
  rule guarda Money VO com Currency); o job resolve `ExchangeRateToPrimary`
  via `IExchangeRateService` (Phase 3) no momento da materialização,
  exatamente como uma transação manual. Mas sem UI especial — dropdown
  de Currency simples.
- **Audit detalhado de execuções** — `RecurringRuleExecutionLog` ou
  similar. Sem entity dedicada. `Transaction.recurring_rule_id` +
  `CreatedAt` da Transaction é audit suficiente.
- **Preview retroativa** — preview só mostra ocorrências futuras, não
  passadas.

## Decisions

- **Hangfire job daily às 00:15 UTC.** *Why*: Antes do ECB job (00:30
  UTC, Phase 3) para que recurrings em data corrente já existam quando
  o user abre o sistema de manhã. Ambos os jobs são idempotentes,
  sem contention real. **How to apply**:
  `RecurringInfo("recurring-transaction-materializer", "15 0 * * *",
  TimeZoneInfo.Utc)` em `Sextante.Host/Program.cs`, pattern análogo
  ao `EcbSnapshotJob`.

- **`TenantAwareJob<T>` wrapper obrigatório.** *Why*: Tech-stack §4.6.
  ECB job (Phase 3) corre cross-tenant (reference data); recurring job
  toca `financial.transactions` que é tenant-owned com RLS. Sem
  wrapper, `ITenantContext` lança no handler — fail-loud. **How to
  apply**: criar `Sextante.Infrastructure/Jobs/TenantAwareJob.cs`
  genérico que:
  1. Recebe `(Guid TenantId, T Payload)` no enqueue.
  2. No execute, configura `ITenantContext.SetCurrent(tenantId)` num
     scope do DI.
  3. Resolve handler `IRecurringJobHandler<T>` e invoca.
  4. Limpa o context no dispose.
  Job recurring agenda 1 entry por tenant (não global): scan dos
  tenants ativos no startup → enqueue de `RecurringInfo` por tenant.

- **Idempotente via `(RecurringRuleId, OccurrenceDate)` lookup.** *Why*:
  Hangfire retries on failure. Job pode correr 2× para a mesma regra
  no mesmo dia (manual trigger + recurring schedule, ou retry após
  falha parcial). Sem idempotência → Transaction duplicada, audit
  trail confuso. **How to apply**: antes de insert, query
  `SELECT 1 FROM financial.transactions WHERE recurring_rule_id=@id
  AND date_trunc('day', occurred_at) = @occurrenceDate AND deleted_at
  IS NULL`. Se existe, skip. Logar "já materializada" sem error.

- **`NextOccurrence` é column persistida + calculada.** *Why*: Permite
  query directa "regras a materializar hoje" sem CPU walk de toda a
  rule. Trade-off: precisa de update após cada materialização. **How
  to apply**: no domain, método `RecurringRule.AdvanceNextOccurrence()`
  chamado pelo handler após criar Transaction. Index
  `(tenant_id, next_occurrence)` para o query do job.

- **`EndDate` boundary é exclusivo no infinito.** *Why*: User cria
  regra "renda mensal de 2026-01-01 a 2026-12-31". Última materialização
  esperada: `2026-12-01` (ou `2026-12-31` se for daily). `EndDate=null`
  é open-ended (nunca pára). **How to apply**: ao calcular
  `NextOccurrence`, se resultado > `EndDate`, set
  `NextOccurrence=null` e regra fica em estado "completed" (UI mostra
  badge cinza "Concluída"; toggle `IsActive` sem efeito).

- **Soft-delete com FK `ON DELETE SET NULL`.** *Why*: Tech-stack §7.4
  exige soft-delete uniforme. Apagar uma regra não pode partir o
  histórico de transações materializadas. **How to apply**:
  `RecurringRule.DeletedAt` + Global Query Filter; FK
  `transactions.recurring_rule_id` com `ON DELETE SET NULL` (mesma
  pattern de `categorization_rule_id` em Phase 4).

- **Multi-currency: rate snapshot no momento da materialização.** *Why*:
  Phase 3 invariante — cada Transaction carrega `ExchangeRateToPrimary`
  + `ExchangeRateAt` frozen. Job materializa Transaction como se fosse
  manual: chama `IExchangeRateService.ResolveAsync` com a data da
  ocorrência. **How to apply**: no handler, após resolver Account e
  Currency, chamar `ResolveAsync(rule.Amount.Currency, primary,
  occurrenceDate, ct)` antes de criar Transaction.

- **`StartDate` no passado: só próxima ocorrência futura.** *Why*: User
  pode criar regra para algo que já estava a acontecer (registo
  retrospetivo). Backfill automático de meses passados criaria
  Transactions inesperadas e seria difícil reverter. **How to apply**:
  ao criar regra, se `StartDate < today`, calcular `NextOccurrence`
  como o **primeiro slot futuro** baseado em
  `Frequency × Interval × StartDate` (ou seja, avança o cursor até
  passar `today`). UI mostra hint "Esta regra começou em
  {StartDate}; primeira materialização será em {NextOccurrence}".

- **Preview retorna 10 ocorrências.** *Why*: Visibility útil sem ser
  assustadoramente longo para regras Daily com Interval=1. **How to
  apply**: `GetUpcomingOccurrencesQuery(ruleId, count=10)` calcula
  in-memory chamando `AdvanceNextOccurrence` 10× sobre uma cópia da
  regra (sem persistir). Respeita `EndDate` (retorna menos se a regra
  termina antes).

- **UI mobile-first.** *Why*: Mission §4.6, tech-stack §19.5. Lista de
  recurrings + dialogs + preview de ocorrências têm de funcionar em 375
  px. **How to apply**: tabela com `overflow-x-auto`; dialog com
  `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`; preview
  dialog idem.

## Context / references

- `specs/roadmap.md` — secção "Phase 5 — Recorrentes + Hangfire +
  Metas". Phase 5a entrega os primeiros 4 bullets (RecurringRule +
  Hangfire + TenantAwareJob + preview). Bullets 5–8 (Budget) ficam para
  Phase 5b.
- `specs/mission.md` — §3 (proposta de valor 1: recorrentes
  automáticas), §4 princípio 3 (automação auditável), §4 princípio 4
  (degradação graciosa: se ECB falhar, recurring em moeda ≠ primary
  falha apenas a linha), §4 princípio 6 (responsive).
- `specs/tech-stack.md` — §1 (Hangfire), §3 (regras de dependência),
  §4.6 (`TenantAwareJob<T>` obrigatório — primeira invocação prática),
  §5 (schema `financial`), §7.4 (soft-delete uniforme), §7.6
  (audit de origem automática), §10 (Hangfire workers in-process,
  dashboard SystemAdmin), §19.5 (responsive).
- `specs/2026-04-26-phase-2-financial-core/` — entregou
  `Transaction` (estrutura base, `Tags jsonb` preparado), `Account`,
  `Category` que Phase 5a referencia. Money VO + audit columns.
- `specs/2026-04-27-phase-3-multi-currency/` — entregou Hangfire setup
  (Postgres storage, schema `hangfire`, dashboard, `EcbSnapshotJob`
  como template), `IExchangeRateService.ResolveAsync`,
  `Account.Currency`. Phase 5a usa estes 3.
- `specs/2026-04-29-phase-4-csv-import/` — entregou
  `Transaction.categorization_rule_id` pattern (FK ON DELETE SET NULL).
  Phase 5a aplica o mesmo pattern com `recurring_rule_id`.
- `src/Modules/Financial/.../Domain/Transactions/Transaction.cs` —
  entidade a estender com `recurring_rule_id` column.
- `src/Modules/Financial/.../Application/ExchangeRates/IExchangeRateService.cs`
  — Phase 3.
- `src/BuildingBlocks/SharedKernel/Money.cs` — VO usado para Amount.
- `src/Bootstrap/Sextante.Host/Hangfire/EcbSnapshotJob.cs` — template
  para o job recurring (recurring registration, idempotency pattern,
  fail-loud + truncated error log).
- ADR-006 Messaging — Wolverine vs Hangfire boundary: Hangfire para
  scheduled / recurring; Wolverine para event-driven entre módulos.
- ADR-009 Soft delete — pendente; Phase 5a aplica soft-delete uniforme
  consistente com Phases 1a–4.

## Open questions

- **Job per-tenant vs global scan.** Duas implementações possíveis:
  (a) recurring registration global, handler faz `SELECT DISTINCT
  tenant_id FROM financial.recurring_rules WHERE next_occurrence <=
  today`, itera tenants, configura `ITenantContext` e processa cada;
  (b) recurring registration por tenant (1 entry por tenant em
  Hangfire). (a) é mais simples para 1 tenant (MVP), mas precisa de
  `BYPASSRLS` ou `IAdminContext` para o scan inicial — viola tech-stack
  §4.6 ("sem wrapper, lança"). (b) precisa de hook em signup
  (`UserRegisteredIntegrationEvent` subscriber) para registar a entry
  Hangfire por novo tenant. **Preferência: (a) com `IAdminContext`
  apenas para o scan top-level**; cada job per-tenant materializa em
  contexto correto. Decidir na implementação; documentar escolha em
  comentário.
- **`Frequency=Monthly` em meses curtos.** Regra com `StartDate=
  2026-01-31`, Frequency=Monthly. Próxima ocorrência: `2026-02-28` ou
  `2026-03-03` (rollover) ou skip Fevereiro? **Preferência: clamp ao
  último dia do mês** (`2026-02-28`, `2026-03-31`, `2026-04-30`,
  ...). Determinístico e intuitivo. Documentar em
  `RecurringRuleTests`.
- **Concorrência: 2 workers Hangfire materializando a mesma regra.**
  Atualmente 1 worker in-process (tech-stack §10). Mas se escalar
  pós-MVP para múltiplos workers, idempotência via lookup
  `(RecurringRuleId, OccurrenceDate)` previne duplicate, mas race
  condition entre `SELECT` e `INSERT` é possível. **Preferência: unique
  index parcial** `CREATE UNIQUE INDEX ON financial.transactions
  (recurring_rule_id, date_trunc('day', occurred_at)) WHERE
  recurring_rule_id IS NOT NULL` — falha hard em race; handler captura
  e ignora (já existe). Decidir: aplicar agora (defesa em profundidade)
  ou adiar até multi-worker.
- **Edição de regra com `NextOccurrence` no passado.** User edita
  regra antiga inactiva e muda `StartDate` para o futuro. Devemos
  recalcular `NextOccurrence` automaticamente? **Preferência: sim**,
  no `UpdateRecurringRuleHandler`, recalcular `NextOccurrence` baseado
  no novo `StartDate` se a edição tocou `StartDate`/`Frequency`/
  `Interval`. Sempre. Documentar.
- **Tags em recurrings.** Phase 2 preparou `Transaction.Tags jsonb`
  mas UI ficou OUT do MVP. Recurring deve carregar Tags para
  propagar à Transaction materializada? **Preferência: sim** —
  coluna `RecurringRule.Tags jsonb DEFAULT '[]'` é trivial e o handler
  copia para a Transaction. UI sem editor de tags por enquanto
  (consistente com Phase 2). Decidir se vale a pena ou se adia para
  quando UI de tags chegar.
- **Pagar à frente:** edge case onde user clica "materializar agora"
  via Hangfire dashboard a meio do dia. Idempotência protege contra
  duplicate da ocorrência atual. Mas e se user enqueue uma 2ª vez
  na mesma execução (race interno do Hangfire)? Hangfire serializa
  jobs com mesmo ID por default; aceita sem ação adicional.
