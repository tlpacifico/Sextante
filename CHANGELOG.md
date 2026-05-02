# Changelog

## 2026-05-02

- Phase 5b — Budgets + alertas + metas em moeda específica. Fecha bullets 5–8 da Phase 5 do roadmap; gateway para Phase 6 (dogfooding) que exige ≥3 metas com progresso
- Entidades novas no schema `financial`: `Budget` (CategoryId FK obrigatória + `BudgetPeriod` VO `(int Year, int Month)` mapeado para `period_year`/`period_month` + `Limit` Money VO `(numeric(20,8), varchar(3))` + `AlertThresholdPercent` int default 80 com range `[1, 99]` + `Notes` varchar(500)?; soft-delete + audit + `ITenantOwned`); `BudgetAlert` (BudgetId FK `ON DELETE CASCADE`, Threshold int `[1, 100]`, TriggeredAt timestamptz, `SpentAtTrigger` Money VO, Acknowledged bool + AcknowledgedAt? — `Acknowledge()` é idempotente; soft-delete + audit + `ITenantOwned`)
- `BudgetPeriod` value object é `record` heap-allocated (alinha com Money OwnsOne pattern; `record struct` ficaria sem ctor parameterless e complica EF) com `Year` em `[2000, 2100]`, `Month` em `[1, 12]`, propriedades derivadas `Start`/`End`/`DaysInMonth`/`Contains(date)`, `Next()` para iteração futura e `FromDate(DateOnly)` factory
- `BudgetProgressCalculator` puro/determinístico devolve `BudgetProgress(Limit, Spent, Remaining, PercentUsed, ProjectedEndOfPeriod?, HasIncompleteRates)`; projeção `(spent / daysElapsed) * DaysInMonth` quando `daysElapsed >= 5` e período inclui hoje, senão null; `HasIncompleteRates=true` quando ≥1 contribution tem `RateMissing` (degradação graciosa em ECB miss — princípio §4.4 da mission)
- Migration `AddBudgetsAndAlerts`: ambas as tabelas com RLS habilitado + FORCE + policies `budgets_tenant_isolation` / `budget_alerts_tenant_isolation` por `app.current_tenant_id` + sentinel `CHECK` (`<> 'ffffffff-...'`) + grants ao `sextante_app`; CHECKs DB em `period_year/period_month/limit_amount/alert_threshold_percent/threshold` (defesa em camadas — Domain já valida); índice unique partial `(tenant_id, category_id, period_year, period_month) WHERE deleted_at IS NULL` em `budgets` (1 budget por categoria+mês — Phase 5b §Decisions); índice unique partial `(tenant_id, budget_id, threshold) WHERE deleted_at IS NULL` em `budget_alerts` (idempotência do dispatch handler); índice parcial `(tenant_id, triggered_at DESC) WHERE acknowledged=false AND deleted_at IS NULL` para o banner; índice parcial `(tenant_id, period_year, period_month) WHERE deleted_at IS NULL` para listagem
- **Integration events scaffolding** (gap fix, não estava em Phase 2): `Sextante.Modules.Financial.PublicApi/Events/TransactionCreatedIntegrationEvent.cs` + `TransactionUpdatedIntegrationEvent.cs` carregam só IDs + `Money` fields + `OccurredAt` (sem PII — sem description, sem tags); `TransactionHandlers.Handle(Create…)` e `Handle(Update…)` publicam pós-`SaveChangesAsync`; `RecurringTransactionMaterializerHandler` enfileira eventos durante o loop e publica em batch após `SaveChangesAsync`. Phase 15 (notificações) reutiliza estes eventos sem reesculpir nada
- `IIntegrationEventPublisher` thin abstraction em `Application.Common` (impl `WolverineIntegrationEventPublisher` em Infrastructure delega a `IMessageBus.PublishAsync`) — mantém Application limpo de Wolverine, simplifica unit tests (stub trivial), permite trocar transporte sem tocar nos handlers
- `BudgetAlertDispatchHandlers` Wolverine subscriber de `TransactionCreated/UpdatedIntegrationEvent`: skip se categoria null/`Guid.Empty`, busca Budget para `(category, BudgetPeriod.FromDate(occurredAt))`, recalcula via `IBudgetProgressService`, emite alerta para `AlertThresholdPercent` configurável (default 80) e sempre 100% se cruzados; idempotência via `ExistsAsync(budgetId, threshold)` + unique index DB; race condition entre dispatchers paralelos catch via `BudgetAlertDuplicateException` no `BudgetAlertRepository.SaveChangesAsync` (Postgres SQLSTATE 23505) e log debug
- `IBudgetProgressService` (interface em Application, impl em `Sextante.Modules.Financial.Infrastructure.Budgets.BudgetProgressService`): query agregadora SQL sobre `financial.transactions` filtrada por categoria + period range; conversão multi-moeda em 3 casos — (1) tx.currency == budget.currency soma directa, (2) budget.currency == primary usa `tx.exchange_rate_to_primary` gravado, (3) double-leg via `IExchangeRateService.ResolveAsync(primary, budget.currency, occurredAt)` com cache local `(currency, day)` para evitar N+1; `ExchangeRateUnavailableException` marca contribution como `RateMissing=true` em vez de bloquear request inteiro (§Decisions: "graceful skip on miss")
- Endpoints REST novos em `/api/financial/budgets`: GET `?year=&month=` (default mês corrente do server), GET `{id}`, GET `{id}/progress`, POST → 201, PUT `{id}` (apenas Limit/Threshold/Notes — Period e CategoryId imutáveis), DELETE → 204 (soft-delete em cascata para `BudgetAlert` no handler, não no DB); GET `alerts/active` (lista não-acknowledged ordenada por TriggeredAt DESC), POST `alerts/{id}/acknowledge` → 204; todos `[Authorize]` com fail-loud do tenant context, `ProblemDetails` PT-PT
- FluentValidation `CreateBudgetValidator` / `UpdateBudgetValidator`: Year `[2000, 2100]`, Month `[1, 12]`, Limit > 0, Currency 3 chars, Threshold `[1, 99]` quando provided, Notes ≤ 500 chars; validação de moeda activa via `ICurrencyDirectory.IsActiveAsync` no handler; categoria deve ser `Kind=Expense` (handler levanta `BudgetCategoryMustBeExpenseException`)
- `DeleteBudgetHandler` soft-deleta budget + alerts associados numa Tx única (cascade aplicado no handler em vez de no DB para preservar audit em hard-delete acidental — Phase 5b §Decisions); `UpdateBudgetHandler` chama `Budget.UpdateLimit/UpdateThreshold/UpdateNotes` (sem setters públicos no domain)
- Frontend Angular: 4 componentes + página + dialog em `src/Web/Sextante.Web/src/app/features/financial/pages/`
  - `budgets.page.ts` lazy-loaded em `/app/budgets` sob `authGuard`: `p-datepicker view="month" dateFormat="mm/yy"` para month picker default mês corrente; PrimeNG `p-table` com Categoria / Limite / Gasto / Restante / `p-tag` colorido por % (verde < threshold, amarelo ≥ threshold mas < 100, vermelho ≥ 100) / Projeção / Threshold / ações (Editar / Apagar); empty state PT-PT; `ConfirmationService` para delete; uso de Signals
  - `budget.dialog.ts`: Reactive Form com Categoria (`p-select` filtrado a `Kind=Expense`), Mês/Ano (`p-datepicker view="month"` — desabilitado em modo edit, Period imutável após criar com hint PT-PT), Limit (`p-inputNumber currency`), Currency (`p-select`), Threshold preset dropdown (50/70/80/90/Custom) com `p-inputNumber [1, 99]` quando custom, Notes (`pTextarea` 500 chars max); breakpoints `{ '960px': '75vw', '640px': '95vw' }`
  - `components/budget-progress-card.component.ts`: standalone signal-driven com `input.required<BudgetDto>()` + `input<string?>('categoryName')`; `p-progressbar` + `p-tag` colorido por threshold; badge "Cálculo parcial" com tooltip quando `HasIncompleteRates`; badge "Projeção: €X" quando ProjectedAmount não-null; reutilizado pelo Dashboard (top 4 budgets do mês ordenados por % DESC) e pela página
  - `components/budget-alerts-banner.component.ts`: standalone, mounted em `app-shell.component.ts` acima do `<router-outlet>`; gated em `auth.isAuthenticated()`; `setInterval(60_000)` registado **fora do NgZone** (caso contrário bloqueia `whenStable()` nos Karma tests); polling de `/budgets/alerts/active`, count agregado com mensagem PT-PT diferenciando "próximo do limite" vs "atingiu o limite", botão "Ver" expande lista (até 5 + "+N mais"), botão "Reconhecer" acknowledge em batch via `Promise.all`
- `FinancialApiService` ganha `listBudgets(year?, month?)`, `getBudget(id)`, `getBudgetProgress(id)`, `createBudget`, `updateBudget`, `archiveBudget`, `listActiveBudgetAlerts()`, `acknowledgeBudgetAlert(id)`; `financial.types.ts` com `BudgetDto`, `BudgetProgressDto`, `BudgetAlertDto`, `CreateBudgetRequest`, `UpdateBudgetRequest`
- `app-shell.component.ts` adiciona link "Orçamentos" (icon `pi-chart-bar`) no menu drawer entre "Recorrentes" e "Regras"; rota `budgets` lazy-loaded em `app.routes.ts` sob `authGuard`; banner mounted no shell visível em todas as rotas autenticadas
- `dashboard.page.ts` ganha section "Orçamentos do mês" com até 4 `BudgetProgressCard`s ordenados por `percentUsed DESC` quando há budgets; link "Ver todos" → `/app/budgets`
- Domain unit tests: `BudgetTests` (15), `BudgetPeriodTests` (12), `BudgetAlertTests` (8), `BudgetProgressCalculatorTests` (9) — invariants, idempotência do `Acknowledge`, projeção edge cases (< 5 dias null, span igual ao período), `HasIncompleteRates` flag, leap year February, transição Dezembro→Janeiro do `Next()`
- Application unit tests: `BudgetHandlerTests` (12) cobre Create/Update/Archive/List/Get/AcknowledgeAlert/ListActiveAlerts + Income rejection + duplicate rejection + currency uppercase normalization + cascade soft-delete; `BudgetAlertDispatchHandlersTests` (10) cobre below-threshold/cross-80/cross-100/idempotência/custom threshold/null categoryId/empty Guid/Update event
- Architecture tests novos (`BudgetDependencyTests`, 7): `Budget`/`BudgetAlert` implementam `ITenantOwned`; `Budgets/` namespace de Domain não referencia Hangfire/Wolverine/EFCore; `BudgetAlertDispatchHandlers` vive em `Application.Features.Budgets.Alerts`; `BudgetProgressService` impl vive em `Infrastructure.Budgets`
- Integration tests novos: `BudgetsCrudTests` (6 — full CRUD lifecycle, Income rejection, duplicate rejection, alerts endpoints, current-month default); `BudgetWorkflowTests` (3 — pipeline 80→100 + acknowledge, idempotência cross-event, below-threshold no-op); `FinancialMultiTenancyTests` estendido com 2 tests Budget (cross-tenant 404, acknowledge cross-tenant 404)
- Karma tests: `financial-api.service.spec.ts` ganha 8 testes de Budget endpoints; `budget-alerts-banner.component.spec.ts` (5 — fetch + count, critical msg, hidden when empty, acknowledge batch); `budget-progress-card.component.spec.ts` (6 — color severity por percent, badge HasIncompleteRates, projection rendering, categoryName input). Total: 91 Karma tests verdes
- 409 testes não-integration verdes (172 Domain + 106 Application + 40 Architecture + 91 Karma); integration tests requerem Docker (Testcontainers PostgreSQL 16) — validados no CI
- Pacotes novos: nenhum
- Mark phase 5b (Budgets) as complete
- Marcar Phase 5a como completa no roadmap
- Resolver findings da validação da Phase 5a

## 2026-05-02

- Phase 5a — Transações recorrentes + Hangfire (`RecurringRule` + materializer + `TenantAwareJob<T>` wrapper). Cobre os 4 primeiros bullets do roadmap §Phase 5; bullets 5–8 (Budget + alertas) ficam para Phase 5b
- Entidade nova no schema `financial`: `RecurringRule` (Description varchar(256), Amount Money VO `(numeric(20,8), varchar(3))`, Account FK obrigatória, Category FK opcional `ON DELETE SET NULL`, Frequency enum-as-string `Daily`/`Weekly`/`Monthly`/`Yearly`, Interval int ≥1 default 1, StartDate, EndDate? (open-ended), NextOccurrence? (persistido + advançado pelo handler), IsActive bool default true, Tags jsonb default `[]`; soft-delete + audit + `ITenantOwned`)
- Domain methods em `RecurringRule`: `AdvanceNextOccurrence()` puro/determinístico — Daily += Interval days; Weekly += Interval×7 days; Monthly += Interval months **com clamp ao último dia do mês** (`2026-01-31 + 1 month → 2026-02-28`); Yearly idem com clamp em 29-Fev (`2024-02-29 + 1 year → 2025-02-28`); `NextOccurrence` set para null quando passa `EndDate`. `GetUpcomingOccurrences(int count)` é puro (clona estado interno, não muta a regra) — usado pelo endpoint `/upcoming`
- `Transaction` estendida com `recurring_rule_id (uuid NULL)` + FK `ON DELETE SET NULL` (mesmo padrão de `categorization_rule_id` da Phase 4); `Transaction.Create` aceita `recurringRuleId` opcional; `ITransactionRepository.TransactionFilter` ganha `RecurringRuleId?` para o filtro Dashboard
- Migration `AddRecurringRules` cria a tabela com RLS habilitado + FORCE + policy `recurring_rules_tenant_isolation` por `app.current_tenant_id` + sentinel `CHECK`; índice parcial `(tenant_id, next_occurrence)` filtrado a `next_occurrence IS NOT NULL AND is_active AND deleted_at IS NULL` para o scan do job; índice em `transactions(recurring_rule_id) WHERE recurring_rule_id IS NOT NULL` para o filtro Dashboard; índice **unique partial** `(recurring_rule_id, date_trunc('day', occurred_at AT TIME ZONE 'UTC'))` (envolvido com `AT TIME ZONE 'UTC'` para tornar a expressão IMMUTABLE — `date_trunc(text, timestamptz)` é STABLE e Postgres rejeita-a em índices) — defesa em profundidade contra race em multi-worker
- `TenantAwareJob<T>` wrapper genérico em `src/BuildingBlocks/Sextante.Infrastructure/Jobs/` — primeira invocação prática do pattern descrito em tech-stack §4.6: cria scope DI per-tenant, chama `ITenantContextSetter.SetCurrent(tenantId)`, resolve `ITenantAwareJobHandler<T>` e invoca, faz `Clear()` no `finally` (mesmo em exceção); throw em `Guid.Empty`
- `ITenantContextSetter` em `Identity.PublicApi.Abstractions` permite override programático de `ITenantContext` para jobs sem `HttpContext`. Implementação em `Identity.Infrastructure.Auth.TenantContext` falha com `InvalidOperationException` se chamado dentro de uma request HTTP autenticada (defesa contra acidente de uso)
- Endpoints REST novos em `/api/financial/recurring-rules`: POST (create) → 201; PUT `{id}` (update — recalcula `NextOccurrence` se `StartDate`/`Frequency`/`Interval`/`EndDate` mudaram); GET `""` (lista ordenada por `NextOccurrence ASC NULLS LAST, Description ASC`); GET `{id}`; GET `{id}/upcoming?count=10` (in-memory via `GetUpcomingOccurrences`, max 50); DELETE `{id}` (soft-delete via `Archive()`); todos `[Authorize]` com fail-loud do tenant context, ProblemDetails PT-PT
- `GET /api/financial/transactions` ganha parâmetro opcional `recurringRuleId` para o filtro "Ver transações" do Dashboard
- FluentValidation `CreateRecurringRuleValidator` / `UpdateRecurringRuleValidator`: Description 1–256, Amount > 0, Interval ≥1, `StartDate ≤ EndDate`, Frequency em allowlist, Currency 3 chars (validação de moeda activa via `ICurrencyDirectory.IsActiveAsync` no handler); Account ownership validada no handler (RLS dá-nos isso de borla, mas pré-DB melhora UX)
- `RecurringTransactionMaterializerHandler` (`ITenantAwareJobHandler<RecurringMaterializerPayload>`): query `GetActiveRulesDueAsync(today)`, loop por regra, loop interno enquanto `NextOccurrence ≤ runDate`, idempotência via `HasMaterializedAsync(ruleId, occurrenceDate)` (skip + log debug se já materializado), resolve `IExchangeRateService.ResolveAsync(rule.Currency, primary, occurrenceDate, ct)` quando moedas diferem, **catch `ExchangeRateUnavailableException` → skip sem avançar `NextOccurrence`** (próximo run tenta novamente após inserção manual da rate), `Transaction.Create` com `OccurredAt = occurrenceDate 00:00 UTC` + `RecurringRuleId` + `Tags` propagadas + snapshot da rate; logs estruturados com `tenantId`/`totalRules`/`materialized`/`skippedDuplicate`/`skippedNoRate`/`completed`. **Não invoca** `CategorizationRuleEngine` da Phase 4 — recurring tem categoria explícita
- `RecurringMaterializerGlobalJob` (Hangfire recurring) cron `15 0 * * *` UTC — 1 entry global (opção (a) das open questions): scan cross-tenant via `migrationConnectionString` (BYPASSRLS) de `SELECT DISTINCT tenant_id FROM financial.recurring_rules WHERE deleted_at IS NULL AND next_occurrence IS NOT NULL AND next_occurrence <= today AND is_active`; itera tenants, cada um em scope DI próprio via `TenantAwareJob<T>.RunAsync(tenantId, payload)`. Cron escolhido para preceder o ECB job (00:30) — ambos idempotentes, sem contention real
- Frontend Angular: 3 ficheiros novos em `src/Web/Sextante.Web/src/app/features/financial/pages/`
  - `recurring-rules.page.ts` lazy-loaded em `/app/recurrings` sob `authGuard`: PrimeNG `p-table` com Description / Amount (formatado via `MoneyPipe`) / Frequency badge + Interval / NextOccurrence PT-PT / IsActive toggle / Ações (Editar / Apagar / Ver próximas); estado completed (NextOccurrence=null) com linha cinza + badge "Concluída"; toast PT-PT em sucesso; empty state PT-PT; uso de Signals (`rules()`, `dialogRuleId()`, `editingRuleId()`, `upcomingRule()`)
  - `recurring-rule.dialog.ts`: Reactive Form com Description / Amount (`p-inputNumber` currency) / Currency (`p-select`) / Account (`p-select`) / Category opcional / Frequency (`p-select` PT-PT) / Interval min 1 / StartDate / EndDate opcional / Tags chip; hint visual "esta regra começou em {data}; primeira materialização será em {NextOccurrence preview}" quando StartDate < today; breakpoints `{ '960px': '75vw', '640px': '95vw' }`
  - `upcoming-occurrences.dialog.ts`: chama `GET /upcoming?count=10`, lista 10 datas formatadas PT-PT, badge "Próxima" no primeiro item, mensagem "regra termina em {EndDate}" quando retornadas < N
- `FinancialApiService` ganha métodos `listRecurringRules`, `getRecurringRule`, `createRecurringRule`, `updateRecurringRule`, `archiveRecurringRule`, `getUpcomingOccurrences(id, count?)`; `financial.types.ts` com `RecurringRuleDto`, `CreateRecurringRuleRequest`, `UpdateRecurringRuleRequest`, `FrequencyValue`, `FREQUENCY_LABELS` PT-PT; `TransactionFilter` ganha `recurringRuleId?: string | null`
- `app-shell.component.ts` adiciona link "Recorrentes" no menu drawer; rota `recurrings` lazy-loaded em `app.routes.ts` sob `authGuard`
- Domain unit tests: `RecurringRuleTests` (26 testes) — invariants (Description 1–256, Amount > 0, Interval ≥1, StartDate ≤ EndDate, Frequency válida), `AdvanceNextOccurrence` Daily/Weekly/Monthly/Yearly × Interval=1 e N + clamp em mês curto + clamp em ano bissexto + null quando passa EndDate, `GetUpcomingOccurrences` (count, truncação em EndDate, pureza)
- Application unit tests: `RecurringRuleHandlerTests` (Create/Update/Archive/List/Get/Upcoming + currency invalid + account not found + frequency invalid), `RecurringTransactionMaterializerHandlerTests` (idempotência, EndDate boundary, multi-rule, sinal `Math.Abs`, exchange rate resolution, ECB miss skip, tags propagation, sem categoria → `Guid.Empty`), `TenantAwareJobTests` (sets tenant + handler invoke, Clear no finally em exception, Guid.Empty throw)
- Architecture tests novos (`RecurringDependencyTests`): `RecurringRule` implementa `ITenantOwned`; Domain não referencia Hangfire nem Wolverine; Application não referencia Hangfire; `TenantAwareJob<>` vive em `Sextante.Infrastructure` (BuildingBlock)
- Integration tests: `RecurringRulesCrudTests` (lifecycle CRUD completo, upcoming, 404 em rule inexistente, 400 em account inexistente); `RecurringMaterializationTests` (pipeline completo, idempotência, EndDate, multi-tenancy + RLS cross-tenant, multi-currency com ECB rate, ECB miss skip)
- `RouterLink` import não usado em `recurring-rules.page.ts:35` (warning Angular NG8113 — não-blocker)
- Pacotes novos: nenhum — Hangfire / FluentValidation / PrimeNG já estavam desde Phases 1b/2/3

## 2026-05-01

- Phase 4 — Importação CSV + Regras de categorização (🛡️)
- Entidades novas no schema `financial`: `CategorizationRule` (Name varchar(128), Pattern varchar(512), MatchType enum-as-string `Contains`/`Equals`/`StartsWith`, CategoryId, Priority int ≥0, IsActive; soft-delete + audit), `ImportProfile` (Delimiter char(1), HasHeaderRow, DateFormat, DecimalSeparator char(1), SkipRows ≥0, ColumnMappings jsonb com `{csvColumnName, transactionField, defaultValue?}`; `TransactionField` enum: Date/Amount/Currency/Description/Account/Category/CreditDebitIndicator), `ImportBatch` (Status state machine `Parsing` → `PreviewReady` → `Confirming` → `Importing` → `Completed`/`Failed`; ParsedPreviewJson + CategorizationResultJson em jsonb; PreviewTruncated flag; FileName/total/imported/duplicate/error counters)
- `Transaction` estendida com `CategorizationRuleId (uuid NULL)` + `CategorizedAt (timestamptz NULL)`; mutators `MarkCategorizedByRule(ruleId)` e `SetCategory(categoryId)`; FK para `categorization_rules(id)` com `ON DELETE SET NULL`
- Migration `AddCsvImport` cria as 3 tabelas, adiciona as 2 colunas a `transactions`, índice `(tenant_id, priority)` em `categorization_rules` e índice em `transactions(categorization_rule_id)`; FK preserva audit mesmo após archive da regra
- `ICsvParser` + `CsvParser` (CsvHelper 33.0.1) com auto-detect de delimitador (`,` / `;` / `\t` / `|` por variance scoring sobre ≤5 linhas), strict UTF-8 com fallback transparente para ISO-8859-1, timeout 30s via `CancellationTokenSource`, truncation a `MaxPreviewRows` (default 1000) com flag `truncated`, deteção de colunas duplicadas case-insensitive
- Excepções CSV PT-PT: `CsvEmptyException`, `CsvParseTimeoutException`, `CsvDuplicateColumnsException`, `CsvEncodingNotSupportedException` — mapeadas para `ValidationProblem` 400 nos endpoints
- `IDuplicateDetector` + `DuplicateDetector`: matching heurístico `(Date + Amount + Currency + descrição normalizada)`; normalização aplica lowercase + remove diacríticos (FormD) + colapsa pontuação `.,;:_-` em espaços + reduz whitespace; query batched a 100 linhas para limitar tamanho do `WHERE IN`; devolve `IReadOnlyList<DuplicateMatch>(rowIndex, existingTransactionId)` para a UI marcar
- `ICategorizationRuleEngine` + `CategorizationRuleEngine`: regras ordenadas por `Priority ASC` (menor = maior prioridade), first-match-wins; comparações `OrdinalIgnoreCase` para Contains/Equals/StartsWith; descrição vazia/null → no match (não consome regra)
- Endpoints REST novos:
  - `POST /api/financial/imports/upload` (multipart, DisableAntiforgery; 5 MB max; só `.csv`; valida no handler, devolve `UploadCsvResponse` com batch + preview + duplicates + auto-categorization suggestions)
  - `PUT /api/financial/imports/{batchId}/preview` (re-parse com mapping editado pelo utilizador)
  - `POST /api/financial/imports/{batchId}/confirm` (persiste transações; `ConfirmImportResponse` com totals)
  - `GET /api/financial/imports` (lista batches do tenant)
  - `GET/POST/PUT/DELETE /api/financial/categorization-rules` + `PUT /reorder` (drag-drop) + `POST /reapply?categoryId&from&to&onlyUncategorized` (default `onlyUncategorized=true`; devolve counts re-categorizados)
  - `GET/POST/PUT/DELETE /api/financial/import-profiles`
- FluentValidation `CreateCategorizationRuleValidator` / `UpdateCategorizationRuleValidator` / `CreateImportProfileValidator` com mensagens PT-PT (limites de length, MatchType allowlist, Priority não-negativa, ColumnMappings não-vazia)
- `SeedDefaultImportProfilesHandler` (`[NonTransactional]` Wolverine subscriber de `UserRegisteredIntegrationEvent`) cria perfil "ActivoBank / Millennium CSV" no signup (delimiter `;`, header sim, `dd/MM/yyyy`, decimal `,`, mappings Data Valor → Date / Descrição → Description / Valor → Amount) — utilizador importa extrato desde o primeiro dia sem configurar mapeamentos
- Frontend Angular: 4 páginas novas em `features/financial/pages/`
  - `import-wizard.page.ts` — 4 steps (upload → mapping editável → preview com badges "Possível duplicado" / "Auto-categorizada" → confirmar) usando `p-steps`, `p-table`, `p-select`, `p-checkbox`, `p-tag`
  - `import-batches.page.ts` — histórico de batches com status badges
  - `import-profiles.page.ts` — CRUD de perfis com editor de mapping
  - `categorization-rules.page.ts` — CRUD com drag-drop reorder + endpoint reapply triggers UI
- `FinancialApiService` ganha métodos `uploadCsv`, `updatePreview`, `confirmImport`, `listBatches`, CRUD de rules/profiles, `reorderRules`, `reapplyRules`; `financial.types.ts` com `UploadCsvResponse`, `PreviewRow`, `ColumnMappingInput`, `ConfirmImportResponse`, `TRANSACTION_FIELD_LABELS` (PT-PT)
- `app-shell.component.ts` adiciona links de navegação para Importar / Histórico / Perfis / Regras; rotas lazy-loaded em `app.routes.ts` sob `authGuard`
- Domain unit tests: `CategorizationRuleTests` (length limits, MatchType, Priority ≥0, Archive), `ImportProfileTests` (Delimiter char(1), DecimalSeparator char(1), SkipRows ≥0, ColumnMappings não-vazia), `ImportBatchTests` (state machine + completion stats), `TransactionCategorizationTests` (`MarkCategorizedByRule` + `SetCategory` + audit columns persistem)
- Application unit tests: `CsvParserTests` (delimiters, encoding fallback, truncation, timeout, duplicate columns), `DuplicateDetectorTests` (normalização + heurística), `CategorizationRuleEngineTests` (first-match-wins por prioridade, case-insensitive, descrição vazia), `ReapplyCategorizationRulesHandlerTests` (filtros categoryId/from/to/onlyUncategorized)
- Architecture test `CsvImportDependencyTests` valida que Domain/Application/Infrastructure das novas features respeitam regras de dependência (sem leakage de Infra para Domain, sem referências indevidas)
- Integration test `CsvImportRealFileTests` exercita pipeline completo (upload → preview → mapping → duplicates → confirm → re-categorize) com fixture CSV em `tests/Sextante.IntegrationTests/Data/`
- Pacote novo: `CsvHelper` 33.0.1; `FluentValidation` adicionada à `Sextante.Modules.Financial.Api.csproj` para registo dos validators
- Walkthrough manual com extrato bancário real (ActivoBank) confirmou ≥80% das transações auto-categorizadas — *Saída* do roadmap §Phase 4 cumprida
- Mark phase 4 as complete

## 2026-04-29

- Mark phase 3 as complete

## 2026-04-28

- Phase 3 — Multi-moeda + ECB (backend)
- Entidades novas no schema `shared`: `Currency` (em SharedKernel; ISO 4217 fiat ≈155 rows seedadas; soft-delete via `IsActive=false` em vez de `DeletedAt`), `ExchangeRate` (em Identity.Domain; EUR-base canónico; chave única `(rate_date, from_currency, to_currency)`), `EcbSnapshotState` (singleton com `LastRunAt` / `LastSuccessAt` / `LastError`)
- Migration `AddCurrencyAndExchangeRate` cria as 3 tabelas, faz seed da lista ISO 4217 (`Iso4217Currencies.cs`) e inicializa o singleton; `DISABLE ROW LEVEL SECURITY` nas 3 (reference data partilhada cross-tenant)
- `IVersioned` extraído de `IAuditable` para entidades com audit sem soft-delete (Currency, ExchangeRate); `AuditingInterceptor` populando `CreatedAt`/`UpdatedAt`/`Version` em ambas as variantes
- `ICurrencyProvider` + `EcbCurrencyProvider` (typed `HttpClient`, parser XML do feed `eurofxref-daily.xml`, `EcbProviderException` fail-loud); resilience via `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`: retry 3× / backoff 2s exponencial / per-attempt 10s)
- `EcbSnapshotJob` Hangfire recurring `30 0 * * *` UTC; idempotente (lookup por `(rate_date, from, to)` antes de upsert); falha → `LastError` (truncado a 2000 chars) + re-throw para Hangfire marcar como failed
- Hangfire registado com Postgres storage (`messaging`/`shared` schemas separados); dashboard em `/api/admin/hangfire` restrito a `SystemAdmin` via `HangfireSystemAdminFilter`
- `IExchangeRateService.ResolveAsync(from, to, at, ct)` em Financial.Application com cross-rate EUR-base: `from==to` → null (1.0 implied); `from==EUR` → direta; `to==EUR` → inversa; nenhum é EUR → `(1/EUR→from) × EUR→to`. Fallback para a rate mais recente até à data quando o dia exato não tem snapshot (fim-de-semana / feriados)
- `ExchangeRateUnavailableException` PT-PT em miss; mapeado para `ProblemDetails` 400 nos endpoints REST
- Domain: VO `ExchangeRateSnapshot(Rate, At)` (Phase 3); `Transaction.ExchangeRateToPrimary` + `ExchangeRateAt` (NULL allowed; `1.0` implied via COALESCE no read-side); rate é frozen — `Transaction.Update` não recomputa
- Domain: `Account.Currency varchar(3) NOT NULL` (FK a `shared.currencies(code)`); `Account.Create` exige `Currency == OpeningBalance.Currency` (`AccountCurrencyMismatchException`); `Currency` setter privado (fixo após criação)
- `CreateTransactionHandler` e `CreateAccountHandler` validam currency contra `ICurrencyDirectory.IsActiveAsync` (allowlist ativa em `shared.currencies`); `CreateTransactionHandler` chama `IExchangeRateService.ResolveAsync(amount.Currency, tenantPrimary, occurredAt)` antes de criar a `Transaction`
- Migration `AddAccountCurrencyAndExchangeRate` em Financial: adiciona `currency` (default-fill por tenant primary; depois drop default + FK constraint a `shared.currencies`); adiciona `exchange_rate_to_primary numeric(20,8) NULL` + `exchange_rate_at timestamptz NULL`
- Dashboard backend: `TransactionSummaryQuery` e `TransactionsByCategoryQuery` ganham parâmetro `viewMode` ∈ `{converted, original}` (default `converted`); `converted` soma usando `COALESCE(ExchangeRateToPrimary, 1.0)` e devolve `Money` em tenant primary; `original` agrupa por `Amount.Currency` e devolve `IReadOnlyList<CurrencyTotals>`
- Endpoints admin Identity novos:
  - `GET /api/currencies` (público autenticado, lista as ativas)
  - `GET /api/admin/currencies` / `POST` / `PUT {code}` (SystemAdmin)
  - `GET /api/admin/exchange-rates?from&to&currencies` (default últimos 7 dias × top-5 configuráveis em `ExchangeRates:DefaultDisplayCurrencies`)
  - `GET /api/admin/exchange-rates/state` (surface do `EcbSnapshotState` para a UI)
  - `POST /api/admin/exchange-rates` (manual upsert; FluentValidation `ManualExchangeRateValidator`; `Source='manual'` sobreescreve ECB)
  - `POST /api/admin/exchange-rates/snapshot/run` (enqueue do `EcbSnapshotJob` via `IBackgroundJobClient`)
  - `GET /api/tenants/me` + `PUT /api/tenants/me { primaryCurrency }` (mudar primary do tenant ativo; histórico fica frozen)
- `TenantCurrencyResolver`: comment inline atualizado para apontar para `IExchangeRateService` (decisão Phase 3 — resolver continua "primary only"; rate resolution vive no service)
- Domain unit tests novos: `CurrencyTests` (regex ISO 4217, MinorUnits 0..6), `ExchangeRateSnapshotTests` (rate>0, equality), `Transaction_ExchangeRateSnapshot_Tests` (Create persiste rate+at; Update não recomputa; null persiste null), `AccountCurrencyTests` (mismatch + invalid code rejected; Currency setter privado)
- Pacotes novos: `Hangfire.AspNetCore`, `Hangfire.PostgreSql`, `Microsoft.Extensions.Http.Resilience`, `FluentValidation`, `FluentValidation.AspNetCore`

## 2026-04-27

- Phase 2 — Categorias + Contas + Transações manuais + Dashboard mínimo
- Módulo `Financial` (5 projetos: Domain/Application/Infrastructure/Api/PublicApi) sob `src/Modules/Financial/` com schema `financial`
- Entidades `Account` (`AccountType` enum: Checking/Savings/Cash/CreditCard, `OpeningBalance` imutável), `Category` (`CategoryKind` enum: Expense/Income, ícone PrimeIcon allowlist + cor hex `^#[0-9A-Fa-f]{6}$`), `Transaction` (`Tags` jsonb default `[]`, ≤10 tags / 50 chars cada, `OccurredAt` ≤ now)
- `Money` value object (record class) em `Sextante.SharedKernel` — `decimal` solto proibido em Domain (tech-stack §7.1); persistido via EF Core `OwnsOne`
- Coluna `PrimaryCurrency varchar(3) NOT NULL DEFAULT 'EUR'` adicionada a `shared.Tenants` (migration `AddTenantPrimaryCurrency`)
- `ITenantCurrencyResolver` em `Identity.PublicApi` resolve a moeda primária do tenant ativo; usado pelos handlers de Account/Transaction para garantir `Amount.Currency = tenant primary` (Phase 3 abre multi-moeda)
- Migrations EF (`InitialFinancial` + `EnableFinancialRowLevelSecurity`) — RLS + FORCE em `accounts`, `categories`, `transactions` com policy `tenant_isolation` USING+WITH CHECK; CHECK constraint `tenant_id <> sentinel` (defesa em profundidade)
- `FinancialMigrationRunner` hosted service (advisory lock distinto de Identity); `FinancialDbContext` com Global Query Filter por TenantId + soft-delete (`DeletedAt is null`)
- `AccountRepository` / `CategoryRepository` / `TransactionRepository` (UoW por repo: `SaveChangesAsync` interno em vez de Wolverine `AutoApplyTransactions`, que falha com múltiplos DbContexts)
- Wolverine handlers (vertical slices) com convenção `*Handlers` plural + `CustomizeHandlerDiscovery`; queries cursor-based opaque base64 (`OccurredAt DESC, Id DESC`); endpoints REST `/api/financial/{accounts,categories,transactions}` + `/transactions/summary` + `/transactions/by-category`
- Subscriber `UserRegisteredIntegrationEvent` cria 11 categorias seed (7 Expense + 4 Income); usa `CategoryRepository.SeedAsync` que faz override transacional do GUC `app.current_tenant_id` (handler Wolverine corre fora de HTTP scope)
- Frontend Angular: `FinancialApiService` (HttpClient), `FinancialStore` (Signals + computed: `expenseCategories`, `incomeCategories`, `hasMoreTransactions`), `MoneyPipe` (`Intl.NumberFormat('pt-PT', currency)`)
- Páginas `/app/accounts` (PrimeNG `p-table` + `p-dialog` + `p-select`), `/app/categories` (chip de cor + dropdown de ícone + erro PT-PT em archive bloqueado), `/app/dashboard` (filtros período/categorias/contas, 3 cards de totais, `p-chart type="doughnut"` por categoria com toggle Despesas/Receitas, tabela com paginação "Carregar mais")
- Navegação: `AppShellComponent` drawer com links Dashboard/Contas/Categorias; `app.routes.ts` lazy-loaded sob `authGuard`
- 25 Domain unit tests (Money currency mismatch, Account OpeningBalance imutável + ≥0, Category icon allowlist + colorHex regex + EnsureCanArchive, Transaction Amount > 0 + OccurredAt ≤ now + 10/50 tags)
- 6 Application unit tests (Cursor encode/decode roundtrip, DefaultCategories 11 entries com ícones na allowlist)
- 7 Architecture tests do módulo Financial (Domain sem EF nem Identity, Application só via Identity.PublicApi, PublicApi sem outras layers)
- 7 Integration tests Financial (CRUD por entidade, archive de categoria com transações ativas → 400 PT, summary, list cursor, multi-tenancy A/B, anonymous → 401, seed-on-signup com 11 categorias)
- 28 Karma unit tests (FinancialApiService, MoneyPipe PT-PT, FinancialStore loadTransactions reset/append + computed filters)
- PrimeNG 21 nomes atualizados — `Dropdown` → `Select` (`p-select`), `Calendar` → `DatePicker` (`p-datepicker`)
- `app.UseExceptionHandler()` mantido em produção; `app.UseDeveloperExceptionPage()` em Development para diagnostics em testes
- Wolverine: removido `AutoApplyTransactions` (incompatível com múltiplos DbContexts) — handlers usam UoW por repositório
- Mark phase 2 as complete
- Adicionar responsive design como invariante do produto

## 2026-04-26

- Phase 1b — Auth UI completa (Angular)
- Upgrade Angular 19 → Angular 21 LTS (TypeScript ~5.9, zone.js ~0.16)
- PrimeNG 21 (preset Aura, dark mode via `darkModeSelector: '.dark'`), PrimeIcons, Chart.js (peer da PrimeNG Chart)
- Tailwind CSS 3.4 + tailwindcss-primeui (regra: Tailwind para layout, PrimeNG para componentes — `Sextante.Web/README.md`)
- ADR-011 — Frontend UI stack (PrimeNG + Tailwind + Signals; alternativas Material / Bootstrap / NgRx rejeitadas)
- `AuthService` com `WritableSignal<AuthState>` privado, computed `isAuthenticated`/`tenantName`/`tenantRole`, signup/login/logout/refresh/loadProfile, storm guard via Promise in-flight cacheada
- `authInterceptor` (HttpInterceptorFn) anexa `Authorization: Bearer ...` a `/api/*`, ignora endpoints marcados com `SKIP_AUTH`, refresh + retry em 401, força logout em refresh-failure
- `authGuard` (`CanActivateFn`) baseado em signal, redirect para `/login?returnUrl=<path>`
- Páginas `/signup`, `/login`, `/forgot-password`, `/reset-password` com Reactive Forms, validação PT-PT, `error-translator` para Identity error codes
- `AuthShellComponent` (card centrado + toast outlet) e `AppShellComponent` (`p-menubar` com tenant + theme toggle + user menu, `p-drawer` placeholder, toast outlet)
- `ThemeService` (signal `isDark`, `localStorage('sextante.theme')`, fallback `prefers-color-scheme`); script inline em `index.html` aplica `.dark` antes do primeiro paint para evitar FOUC
- Backend: middleware `RefreshTokenCookieMiddleware` em `Sextante.Host` emite `Set-Cookie: refresh_token=...; HttpOnly; Secure; SameSite=Strict; Path=/api/auth/refresh; Max-Age=604800` em `/login` + `/refresh`, aceita o cookie como fallback ao body em `/refresh`, e limpa o cookie em `/logout`
- Backend: novos endpoints `POST /api/auth/logout` (204, requer auth) e `GET /api/auth/me` (perfil + tenant claims; necessário porque o bearer ticket é opaque, não JWT)
- 4 novos integration tests (`RefreshCookieTests`) — atributos do cookie no login, refresh com cookie + sem body, refresh com cookie inválido → 401, logout emite delete-cookie
- 16 Karma unit tests (`auth.service.spec`, `auth.interceptor.spec`, `auth.guard.spec`) cobrindo signup/login/logout/refresh/storm-guard/guard-redirect
- CI: novo step `npm test -- --watch=false --browsers=ChromeHeadless` no job de testes
- `@primeng/themes` (deprecado) substituído por `@primeuix/themes` (open question da Phase 1b resolvida)
- Adicionar feature spec da Phase 1a (Auth backend + Multi-tenancy)
- Implementar módulo `Identity` (5 projetos) com `AppUser`, `Tenant`, `Membership` e schema `shared`
- Wolverine 5.x como mediator in-process + bus inter-módulos com outbox PostgreSQL transacional (ADR-010)
- Migrations EF (`Initial` + `EnableRowLevelSecurity` + `HardenTenantSentinelGuard`) com lock distribuído via `pg_advisory_lock`
- Row-Level Security em `shared.Memberships` e `shared.Tenants` com policies baseadas em `current_setting('app.current_tenant_id')`; sentinel anonymous é UUID estruturalmente impossível com CHECK constraint a proibi-lo como `tenant_id` real
- Roles `sextante_migrations` (BYPASSRLS) e `sextante_app` (NOBYPASSRLS) bootstrap via `infra/postgres/01-bootstrap-roles.sh`, alimentado por env vars obrigatórias (sem defaults `changeme_*`)
- `TenantAwareClaimsPrincipalFactory` injeta `tenant_id` e `tenant_role` no JWT; `ITenantContext` fail-loud
- `MapIdentityApi<AppUser>()` em `/api/auth/*` com `/register` desativado (404) — `POST /api/auth/signup` é o único path de criação atómica de User + Tenant + Membership
- Identity defaults endurecidos: password ≥ 12 chars, lockout 5 fails / 15 min, email único; rate limiter fixed-window 30 req/IP/min em `/api/auth/*`
- `NotImplementedEmailSender` stub para endpoints email-dependentes (Phase 1b/Phase 6 substitui)
- `TenantConnectionInterceptor` reescreve GUC em cada checkout do pool e faz reset no close (defesa em profundidade contra leak entre tenants)
- Architecture tests (NetArchTest) codificam tabela §3.1 do tech-stack — 9 testes verdes
- Integration tests com Testcontainers Postgres — cobrem signup, multi-tenancy (incluindo assert do SqlState `42501` da RLS policy), regressão pool leak via WebApplicationFactory, login → JWT com `tenant_id` claim, e auto-população de `TenantId` pelo interceptor
- Sub-agent deep review verde (round 1 fechou findings HIGH RLS-em-Tenants, pool leak, claims via privileged conn, ordem de save no signup; round 2 endureceu critical/high adicionais — ver sub-agent log)
- Mark phase 1a as complete
- Implementar Phase 1a — Auth backend + Multi-tenancy
- Fechar findings HIGH do sub-agent review
- Endurecer Phase 1a — round 2 sub-agent review

### Pendentes para Phase 2 (registados em ADR-010 §"Pendentes")

- Refactorar signup para handler Wolverine com `Policies.AutoApplyTransactions`: hoje `bus.PublishAsync` corre **pós-commit** (at-most-once); o outbox transacional só é exercido quando o módulo Financial introduz o primeiro subscriber.
- Substituir a connection BYPASSRLS direta em `TenantAwareClaimsPrincipalFactory` por `IMembershipReader` exposto em `PublicApi`, atrás de gate tipado.

## 2026-04-25

- Diferir primeiro deploy à VPS para a Phase 6
- Adicionar scaffold Phase 0 e renomear projeto para Sextante
- Introduce SDD foundation
- Mark phase 0 as complete
