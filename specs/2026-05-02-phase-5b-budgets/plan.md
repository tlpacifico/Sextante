# Plan — Phase 5b: Budgets + alertas + metas em moeda específica

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 5b entrega `Budget` + `BudgetAlert`
> (entidades + CRUD + UI), cálculo de progresso on-demand com
> conversão multi-moeda via ECB, e dispatch automático de alertas em
> 80% (configurável) e 100% via Wolverine subscriber dos
> `TransactionCreated/Updated IntegrationEvent` de Phase 2.
>
> Phases anteriores entregaram:
> - **Phase 2**: `Transaction`, `Category` com `Kind` enum, Money
>   VO, audit columns base, soft-delete uniforme,
>   `TransactionCreated/Updated IntegrationEvent`.
> - **Phase 3**: `IExchangeRateService.ResolveAsync` + ECB snapshot
>   table; pattern "graceful skip on miss".
> - **Phase 4**: pattern de FK preservando audit;
>   `categorization_rule_id` style.
> - **Phase 5a**: `RecurringRule` materialização publica
>   `TransactionCreated IntegrationEvent` — Budget recalcula
>   automaticamente quando recorrentes geram transactions.
>
> Phase 6 (Polish + Deploy + Dogfooding) é a próxima e exige
> "pelo menos 3 metas a mostrar progresso" no critério de done MVP.

---

## 1. Schema — `Budget` + `BudgetAlert` + Period VO

### 1.1 `BudgetPeriod` value object
`src/Modules/Financial/Sextante.Modules.Financial.Domain/Budgets/BudgetPeriod.cs`:
- `record BudgetPeriod(int Year, int Month)`.
- Validação no ctor: `Year ∈ [2000, 2100]`, `Month ∈ [1, 12]`.
- Métodos:
  - `DateOnly Start => new DateOnly(Year, Month, 1)`.
  - `DateOnly End => Start.AddMonths(1).AddDays(-1)`.
  - `int DaysInMonth => DateTime.DaysInMonth(Year, Month)`.
  - `bool Contains(DateOnly date) => date >= Start && date <= End`.
  - `BudgetPeriod Next() => new(...)` — útil para "duplicar"
    futuro (não no MVP, pero free since the VO is here).
  - `static BudgetPeriod FromDate(DateOnly date) => new(date.Year,
     date.Month)`.
- Determinístico, puro, testável sem clock.

### 1.2 `Budget` entity
`src/Modules/Financial/Sextante.Modules.Financial.Domain/Budgets/Budget.cs`:
- Fields:
  - `Id (Guid v7)`, `TenantId (Guid)`.
  - `CategoryId (Guid, FK financial.categories)`.
  - `Period (BudgetPeriod VO)` — persistido como `(Year int, Month int)`.
  - `Limit (Money VO)` — `(amount numeric(20,8), currency varchar(3))`.
  - `AlertThresholdPercent (int, default 80)`.
  - `Notes (varchar(500)?)`.
  - Audit: `CreatedAt`, `UpdatedAt`, `DeletedAt`, `Version`.
- Implementa `ITenantOwned`.
- Invariants no ctor + setters:
  - `Limit.Amount > 0`.
  - `AlertThresholdPercent ∈ [1, 99]`.
  - `Notes` ≤ 500 chars.
- Factory: `Budget.Create(tenantId, categoryId, period, limit,
    threshold, notes?)` — Guid v7 nativo.
- Update method: `Budget.UpdateLimit(newLimit)`,
  `Budget.UpdateThreshold(int)`, `Budget.UpdateNotes(string?)`. Sem
  setters públicos.

### 1.3 `BudgetAlert` entity
`src/Modules/Financial/Sextante.Modules.Financial.Domain/Budgets/BudgetAlert.cs`:
- Fields:
  - `Id (Guid v7)`, `TenantId (Guid)`.
  - `BudgetId (Guid, FK financial.budgets)`.
  - `Threshold (int)` — 80 (ou custom), ou 100.
  - `TriggeredAt (DateTimeOffset UTC)`.
  - `SpentAtTrigger (Money VO)` — snapshot do valor no momento.
  - `Acknowledged (bool, default false)`.
  - `AcknowledgedAt (DateTimeOffset?)`.
  - Audit: `CreatedAt`, `UpdatedAt`, `DeletedAt`.
- Implementa `ITenantOwned`.
- Methods:
  - `BudgetAlert.Create(tenantId, budgetId, threshold,
      spentAtTrigger)` — `TriggeredAt = UtcNow`.
  - `Acknowledge()` — set `Acknowledged = true`,
    `AcknowledgedAt = UtcNow`. Idempotente (segunda chamada no-op).
- Invariants: `Threshold ∈ [1, 100]`, `SpentAtTrigger.Amount >= 0`.

### 1.4 Domain `IBudgetRepository` + `IBudgetAlertRepository`
`src/Modules/Financial/Sextante.Modules.Financial.Domain/Budgets/`:
- `IBudgetRepository`:
  - `GetByIdAsync(Guid id, CancellationToken ct)`.
  - `GetActiveAsync(int year, int month, CancellationToken ct)` —
    filtrado por tenant via RLS + soft-delete via filter.
  - `GetForCategoryAsync(Guid categoryId, BudgetPeriod period,
      CancellationToken ct)` — usado pelo dispatch handler.
  - `AddAsync`, `Update`, `Remove` (soft).
  - `SaveChangesAsync`.
- `IBudgetAlertRepository`:
  - `ExistsAsync(Guid budgetId, int threshold, CancellationToken ct)`.
  - `GetActiveAsync(CancellationToken ct)` — `Acknowledged=false`,
    ordenado por `TriggeredAt DESC`.
  - `AddAsync`, `SaveChangesAsync`.

### 1.5 Migration `AddBudgetsAndAlerts` em Financial
`src/Modules/Financial/.../Infrastructure/Migrations/`:
- Cria `financial.budgets`:
  ```sql
  CREATE TABLE financial.budgets (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    category_id uuid NOT NULL REFERENCES financial.categories(id),
    period_year int NOT NULL CHECK (period_year BETWEEN 2000 AND 2100),
    period_month int NOT NULL CHECK (period_month BETWEEN 1 AND 12),
    limit_amount numeric(20,8) NOT NULL CHECK (limit_amount > 0),
    limit_currency varchar(3) NOT NULL,
    alert_threshold_percent int NOT NULL DEFAULT 80
      CHECK (alert_threshold_percent BETWEEN 1 AND 99),
    notes varchar(500) NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NULL,
    deleted_at timestamptz NULL,
    version int NOT NULL DEFAULT 0,
    CONSTRAINT chk_tenant_not_sentinel CHECK (tenant_id <> '<sentinel>')
  );

  ALTER TABLE financial.budgets ENABLE ROW LEVEL SECURITY;
  ALTER TABLE financial.budgets FORCE ROW LEVEL SECURITY;
  CREATE POLICY tenant_isolation ON financial.budgets
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);

  CREATE UNIQUE INDEX uq_budgets_tenant_category_period
    ON financial.budgets(tenant_id, category_id, period_year, period_month)
    WHERE deleted_at IS NULL;

  CREATE INDEX ix_budgets_tenant_period
    ON financial.budgets(tenant_id, period_year, period_month)
    WHERE deleted_at IS NULL;
  ```
- Cria `financial.budget_alerts`:
  ```sql
  CREATE TABLE financial.budget_alerts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    budget_id uuid NOT NULL REFERENCES financial.budgets(id) ON DELETE CASCADE,
    threshold int NOT NULL CHECK (threshold BETWEEN 1 AND 100),
    triggered_at timestamptz NOT NULL DEFAULT NOW(),
    spent_at_trigger_amount numeric(20,8) NOT NULL,
    spent_at_trigger_currency varchar(3) NOT NULL,
    acknowledged boolean NOT NULL DEFAULT false,
    acknowledged_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT NOW(),
    updated_at timestamptz NULL,
    deleted_at timestamptz NULL,
    CONSTRAINT chk_tenant_not_sentinel CHECK (tenant_id <> '<sentinel>')
  );

  ALTER TABLE financial.budget_alerts ENABLE ROW LEVEL SECURITY;
  ALTER TABLE financial.budget_alerts FORCE ROW LEVEL SECURITY;
  CREATE POLICY tenant_isolation ON financial.budget_alerts
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);

  CREATE UNIQUE INDEX uq_budget_alerts_budget_threshold
    ON financial.budget_alerts(tenant_id, budget_id, threshold)
    WHERE deleted_at IS NULL;

  CREATE INDEX ix_budget_alerts_tenant_active
    ON financial.budget_alerts(tenant_id, triggered_at DESC)
    WHERE acknowledged = false AND deleted_at IS NULL;
  ```

### 1.6 `FinancialDbContext`
- Adicionar `DbSet<Budget>` + `DbSet<BudgetAlert>`.
- Global Query Filter:
  `WHERE TenantId = @currentTenant AND DeletedAt IS NULL`.
- Configurar `Limit` e `SpentAtTrigger` Money VOs via `OwnsOne`
  (mesmo pattern Phase 2 / 5a).
- Configurar `Period` VO via `OwnsOne` mapeando para
  `period_year` + `period_month`.
- `BudgetAlert.Budget` navigation (FK only, no cascade load by
  default).

### 1.7 Architecture test
`tests/Sextante.ArchitectureTests/BudgetDependencyTests.cs`:
- `Budget` em `Module.Financial.Domain.Budgets` referencia apenas
  SharedKernel (Money, ITenantOwned).
- `Module.Financial.Domain` não referencia Hangfire / EF / Wolverine.
- `Budget` e `BudgetAlert` implementam `ITenantOwned`.
- Dispatch handler vive em
  `Module.Financial.Application.Features.Budgets.Alerts`.

---

## 2. Domain — `BudgetProgressCalculator`

### 2.1 Calculator service
`src/Modules/Financial/.../Domain/Budgets/BudgetProgressCalculator.cs`:
- Pure domain function (não tem DI; recebe inputs):
  ```csharp
  public static class BudgetProgressCalculator
  {
      public static BudgetProgress Calculate(
          Budget budget,
          IReadOnlyCollection<TransactionContribution> contributions,
          DateOnly today);
  }

  public sealed record TransactionContribution(
      decimal AmountInBudgetCurrency,
      bool RateMissing,
      DateOnly OccurredAt);

  public sealed record BudgetProgress(
      Money Limit,
      Money Spent,
      Money Remaining,
      decimal PercentUsed,
      Money? ProjectedEndOfPeriod,
      bool HasIncompleteRates);
  ```
- Algoritmo:
  1. `spent = Σ contributions.AmountInBudgetCurrency` (já
     convertido na query — calculator não conhece ECB).
  2. `remaining = limit - spent` (pode ser negativo se overspent).
  3. `percent = (spent / limit) * 100`.
  4. `daysElapsed = (today - period.Start).Days + 1`, clamped a
     `[1, period.DaysInMonth]`.
  5. Projeção: se `daysElapsed >= 5`:
     `projected = (spent / daysElapsed) * period.DaysInMonth`.
     Senão `null`.
  6. `hasIncomplete = contributions.Any(c => c.RateMissing)`.
- Determinístico. Testes cobrem edge cases sem DB.

### 2.2 Domain unit tests
`tests/Modules/Financial.Domain.Tests/Budgets/`:
- `BudgetTests.cs`:
  - `Limit.Amount > 0` enforced.
  - `AlertThresholdPercent ∈ [1, 99]` enforced.
  - `UpdateLimit` recalcula `UpdatedAt`, `Version++`.
  - Soft-delete + audit columns.
- `BudgetPeriodTests.cs`:
  - `Year` / `Month` invariants.
  - `Start` / `End` correctos para Janeiro, Dezembro, Fevereiro
    ano normal, Fevereiro ano bissexto.
  - `Contains(date)` — boundary days.
  - `DaysInMonth` para 28/29/30/31.
  - `Next()` salta de Dezembro para Janeiro do ano seguinte.
- `BudgetProgressCalculatorTests.cs`:
  - Spent < limit → percent < 100, remaining positivo, no
    incomplete.
  - Spent == limit → percent = 100, remaining = 0.
  - Spent > limit → percent > 100, remaining negativo.
  - DaysElapsed < 5 → projection null.
  - DaysElapsed = 10, spent = 100 → projection ≈
    (100/10)*30 = 300 (Janeiro).
  - One contribution with `RateMissing` → `HasIncompleteRates=true`,
    spent ignora essa contribution.
  - Empty contributions → spent=0, percent=0, projection=0 ou null
    conforme dia.
- `BudgetAlertTests.cs`:
  - `Threshold ∈ [1, 100]` enforced.
  - `Acknowledge()` é idempotente.

---

## 3. Application — comandos, queries, alert dispatch

### 3.1 Comandos e queries (Wolverine in-process)
`src/Modules/Financial/Sextante.Modules.Financial.Application/Features/Budgets/`:

- `BudgetsContracts.cs` — DTOs e records (mesmo style de
  Phase 5a `RecurringRulesContracts.cs`):
  - `CreateBudgetCommand(CategoryId, Year, Month, LimitAmount,
      LimitCurrency, AlertThresholdPercent?, Notes?)`.
  - `UpdateBudgetCommand(Id, LimitAmount, LimitCurrency,
      AlertThresholdPercent?, Notes?)`.
  - `DeleteBudgetCommand(Id)`.
  - `GetBudgetsQuery(Year?, Month?)` — default = mês corrente.
  - `GetBudgetProgressQuery(Id)`.
  - `GetActiveAlertsQuery`.
  - `AcknowledgeAlertCommand(AlertId)`.
  - DTOs:
    - `BudgetDto(Id, CategoryId, CategoryName, Year, Month,
        LimitAmount, LimitCurrency, AlertThresholdPercent, Notes,
        Progress: BudgetProgressDto)`.
    - `BudgetProgressDto(SpentAmount, RemainingAmount,
        PercentUsed, ProjectedAmount?, HasIncompleteRates)`.
    - `BudgetAlertDto(Id, BudgetId, CategoryName, Threshold,
        TriggeredAt, SpentAtTriggerAmount, SpentAtTriggerCurrency,
        Acknowledged, AcknowledgedAt?)`.

- `BudgetHandlers.cs`:
  - `CreateBudgetHandler` — valida via `IBudgetRepository.GetForCategoryAsync`
    (UX cleaner que esperar UniqueConstraintViolation), valida
    `Category.Kind == Expense` (Phase 2), valida currency está em
    Phase 3 directory, persiste.
  - `UpdateBudgetHandler` — só `Limit` / `Threshold` / `Notes`
    (Period e Category são imutáveis após criar; `requirements.md`
    > Decisions). Chama `Budget.UpdateLimit/UpdateThreshold/UpdateNotes`.
  - `DeleteBudgetHandler` — soft-delete; também soft-deleta os
    `BudgetAlert` associados (loop, mesma Tx).
  - `GetBudgetsQueryHandler` — chama `IBudgetRepository.GetActiveAsync`,
    e para cada Budget chama `BudgetProgressService.CalculateAsync`
    (próxima sub-tarefa). Retorna `IReadOnlyList<BudgetDto>`.
  - `GetBudgetProgressQueryHandler` — para 1 budget.
  - `GetActiveAlertsQueryHandler` — chama `GetActiveAsync`, joina
    com Budget para `CategoryName`, retorna lista.
  - `AcknowledgeAlertHandler` — chama `BudgetAlert.Acknowledge()`,
    salva.

- FluentValidation `CreateBudgetValidator` /
  `UpdateBudgetValidator`:
  - `LimitAmount > 0`.
  - `LimitCurrency` em allowlist (Phase 3 `ICurrencyDirectory.IsActiveAsync`).
  - `AlertThresholdPercent ∈ [1, 99]` (default 80 se null).
  - `Year ∈ [2000, 2100]`, `Month ∈ [1, 12]`.
  - `Notes` ≤ 500 chars.
  - `CategoryId` existe e pertence ao tenant (validação via repo).

### 3.2 `BudgetProgressService` (em Application)
`src/Modules/Financial/.../Application/Features/Budgets/BudgetProgressService.cs`:
- Encapsula a query de progresso para reuso entre `GetBudgets...`,
  `GetBudgetProgress...` e `BudgetAlertDispatchHandler`.
- Método:
  ```csharp
  Task<BudgetProgress> CalculateAsync(
      Budget budget,
      DateOnly? asOfDate = null,
      CancellationToken ct = default);
  ```
- Algoritmo:
  1. Query SQL agregadora sobre `financial.transactions`:
     ```sql
     SELECT
       t.amount_amount,
       t.amount_currency,
       t.exchange_rate_to_primary,
       t.exchange_rate_at,
       t.occurred_at::date AS occurred_date
     FROM financial.transactions t
     WHERE t.tenant_id = current_setting('app.current_tenant_id')::uuid
       AND t.category_id = @categoryId
       AND t.occurred_at::date >= @periodStart
       AND t.occurred_at::date <= @periodEnd
       AND t.deleted_at IS NULL
     ```
  2. Para cada row, converter para `Budget.Limit.Currency`:
     - Se `txCurrency == budgetCurrency`: contribution = `amount`.
     - Se `budgetCurrency == tenantPrimary`:
       contribution = `amount * exchange_rate_to_primary`.
     - Se ambos diferem: precisa de 2 conversões. Resolver
       `IExchangeRateService.ResolveAsync(budgetCurrency, primary,
        occurredAt, ct)` para obter rate inversa; converter.
       Cache em-memória (dict por `(currency, occurredAt)`) durante
       a vida da query para evitar N+1.
     - Se qualquer leg falha (`ExchangeRateUnavailableException`):
       marcar `RateMissing=true`, contribution=0.
  3. Construir `IReadOnlyList<TransactionContribution>` e chamar
     `BudgetProgressCalculator.Calculate(budget, contributions,
      asOfDate ?? today)`.
- **Note**: `category.Kind == Income` não tem Budget no MVP; para
  estrita robustez o service pode validar no início, mas confio
  no `CreateBudgetHandler`.

### 3.3 Alert dispatch handler
`src/Modules/Financial/.../Application/Features/Budgets/Alerts/BudgetAlertDispatchHandler.cs`:
- Wolverine subscriber para `TransactionCreatedIntegrationEvent`
  e `TransactionUpdatedIntegrationEvent` (Phase 2).
- **Eventos consumidos** (não publica novos no MVP; Phase 15
  pode adicionar `BudgetAlertCreatedIntegrationEvent` para email/push).
- Algoritmo:
  1. Se `event.CategoryId == null` ou `event.Category.Kind == Income`:
     skip.
  2. `period = BudgetPeriod.FromDate(event.OccurredAt)`.
  3. `budget = _budgetRepo.GetForCategoryAsync(event.CategoryId,
      period, ct)`. Se null: skip.
  4. `progress = _progressService.CalculateAsync(budget,
      asOfDate: event.OccurredAt, ct)`.
  5. **Threshold customizable**:
     ```csharp
     if (progress.PercentUsed >= budget.AlertThresholdPercent
         && !await _alertRepo.ExistsAsync(budget.Id,
             budget.AlertThresholdPercent, ct))
     {
         await _alertRepo.AddAsync(BudgetAlert.Create(
             tenantId, budget.Id, budget.AlertThresholdPercent,
             progress.Spent), ct);
     }
     ```
  6. **Threshold 100% (hardcoded)**:
     ```csharp
     if (progress.PercentUsed >= 100m
         && !await _alertRepo.ExistsAsync(budget.Id, 100, ct))
     {
         await _alertRepo.AddAsync(BudgetAlert.Create(
             tenantId, budget.Id, 100, progress.Spent), ct);
     }
     ```
  7. `SaveChangesAsync`. Se `UniqueConstraintViolation`
     (race condition entre 2 dispatches paralelos), catch + log
     debug; não throw.
- Logger estruturado:
  `{ tenantId, budgetId, percentUsed, alertedThresholds: [80, 100] }`.

### 3.4 Application unit tests
`tests/Modules/Financial.Application.Tests/Budgets/`:
- `CreateBudgetHandlerTests.cs`:
  - Happy path → Budget criado, audit columns preenchidas.
  - Duplicate `(category, year, month)` → erro PT-PT.
  - `Category.Kind == Income` → erro PT-PT ("Budgets só
    suportam categorias de despesa.").
  - Currency inválida → erro.
  - `LimitAmount <= 0` → validation error.
- `UpdateBudgetHandlerTests.cs`:
  - Mudar `Limit` → `UpdatedAt` muda, `Period` imutável (test
    confirma que payload com Period diferente é ignorado/erro).
- `DeleteBudgetHandlerTests.cs`:
  - Soft-delete → Budget invisible em GetBudgets.
  - Alerts associados ficam soft-deleted juntos.
- `BudgetProgressServiceTests.cs`:
  - 0 transactions → progress.Spent=0, percent=0.
  - 1 transaction same currency → contribution direta.
  - 1 transaction primary currency, budget USD → resolve rate,
    contribution convertida.
  - 1 transaction com `RateMissing` → `HasIncompleteRates=true`.
  - Soft-deleted transactions excluídas.
  - Transactions fora do period excluídas.
- `BudgetAlertDispatchHandlerTests.cs`:
  - Transaction abaixo de threshold → 0 alerts.
  - Transaction cruza threshold 80 → 1 alert (80) inserido.
  - Transaction cruza threshold 100 (já 80 emitido) → 1 alert
    (100) inserido; alert 80 NÃO duplica.
  - Re-emit do event para mesma transaction → 0 alerts novos
    (idempotência via unique index).
  - Transaction com `Category.Kind=Income` → 0 alerts.
  - Transaction com `CategoryId=null` → 0 alerts.
  - Transaction sem Budget para a categoria → 0 alerts.
  - Threshold custom (50) → alert dispara em 50, não em 80.

---

## 4. API — endpoints

### 4.1 Endpoints (Minimal API)
`src/Modules/Financial/Sextante.Modules.Financial.Api/Budgets/BudgetEndpoints.cs`:
- `POST /api/financial/budgets` → 201.
- `GET /api/financial/budgets?year=YYYY&month=MM` → 200 lista.
  Default = mês corrente do client (passar via query param).
- `GET /api/financial/budgets/{id}` → 200.
- `GET /api/financial/budgets/{id}/progress` → 200.
- `PUT /api/financial/budgets/{id}` → 200.
- `DELETE /api/financial/budgets/{id}` → 204.
- `GET /api/financial/budgets/alerts/active` → 200 lista.
- `POST /api/financial/budgets/alerts/{id}/acknowledge` → 204.
- Todos: `[Authorize]` + tenant fail-loud.
- Erros: `ProblemDetails` PT-PT (FluentValidation).

### 4.2 Module wiring
- Registar `IBudgetRepository`, `IBudgetAlertRepository`,
  `BudgetProgressService` no `AddFinancialModule` (Module.Api).
- Registar handler `BudgetAlertDispatchHandler` (Wolverine
  auto-discovery por convenção).
- OpenAPI: schema gerado automaticamente.

---

## 5. UI Angular

### 5.1 Página `budgets.page.ts`
`src/Web/Sextante.Web/src/app/features/financial/pages/budgets.page.ts`:
- Route: `/app/budgets`. Lazy-loaded sob `authGuard`.
- Standalone component, signals.
- Mês picker (`p-datepicker mode="month"`). Default = mês corrente.
  Mudar mês refaz GET com `?year=&month=`.
- PrimeNG `p-table`:
  - Colunas: Categoria (com cor + ícone, Phase 2), Limite (Money
    formatado), Gasto, Restante, % consumido (badge colorido:
    verde <80, amarelo 80–99, vermelho ≥100), Projeção (badge cinza
    se incompleto ou < 5 dias), Threshold, Ações
    (Editar / Apagar / Ver alertas).
  - `[scrollable]="true" scrollDirection="horizontal"` para mobile.
  - `[tableStyle]="{'min-width': '50rem'}"`.
- Botão "Novo orçamento" → `BudgetDialog`.
- Toast PT-PT em sucesso ("Orçamento criado", "atualizado",
  "apagado").
- Empty state: "Sem orçamentos para {mês}. Cria o primeiro para
  começar a controlar gastos."

### 5.2 Dialog `budget.dialog.ts`
`src/Web/Sextante.Web/src/app/features/financial/pages/budget.dialog.ts`:
- Form fields:
  - Categoria (`p-select`, populado por `categoryApi.list()` filtrado
    a `Kind=Expense`).
  - Mês/Ano (`p-datepicker mode="month"`) — só edição é
    desabilitada (Period imutável após criar).
  - Limite (`p-inputNumber mode="currency"`) ligado a Currency.
  - Currency (`p-select`, `currencyApi.list()`).
  - Alert threshold (`p-select` com presets [50, 70, 80, 90] +
    "Custom" → `p-inputNumber [1, 99]`). Default 80.
  - Notes (`textarea`, opcional, max 500).
- Botões: "Salvar", "Cancelar". Validação inline.
- `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`.

### 5.3 Componente `budget-progress-card.component.ts`
`src/Web/Sextante.Web/src/app/features/financial/components/budget-progress-card.component.ts`:
- Recebe `[budget]: BudgetDto` como input signal.
- Renderiza:
  - Categoria (cor + ícone + nome).
  - Progress bar (PrimeNG `p-progressbar`) com cor variando por %
    (verde / amarelo / vermelho).
  - "€450 de €500 (90%)" — moeda do Budget.
  - Tooltip se `LimitCurrency != tenantPrimary` mostra equivalente
    na primary.
  - Badge "Cálculo parcial" se `progress.HasIncompleteRates=true`.
  - Badge "Projeção: €600" se `progress.ProjectedAmount` não-null.
- Usado pelo Dashboard (top 4) e pelo `budgets.page.ts` (linha
  da tabela).

### 5.4 Componente `budget-alerts-banner.component.ts`
`src/Web/Sextante.Web/src/app/shared/components/budget-alerts-banner/budget-alerts-banner.component.ts`:
- Standalone signal-driven.
- Usa `effect()` + `setInterval` (60s) para chamar
  `budgetApi.activeAlerts()`.
- Renderiza:
  - Banner persistente acima do conteúdo principal se
    `alerts.length > 0`.
  - Texto: `{N} orçamento(s) atingiram o limite` (`alerts >= 100`)
    ou `{N} orçamento(s) próximo do limite` (caso contrário).
    Mensagens PT-PT.
  - Botão "Ver" → expande lista (até 5; "+N mais" se overflow).
  - Botão "Reconhecer" → acknowledge em batch (loop).
- Mobile: em < 768 px, banner truncate com "..." e ícone para
  expandir; em ≥ md, lista 1ª linha completa.
- Visível apenas em rotas autenticadas (consume signal de
  auth state Phase 1b).

### 5.5 Dashboard ganha `BudgetProgressCard` strip
`src/Web/Sextante.Web/src/app/features/financial/pages/dashboard/dashboard.page.ts`:
- Acrescenta sección "Orçamentos do mês" abaixo dos cards de
  totais (Phase 2/3).
- Carrega `budgetApi.list({ year: currentYear, month: currentMonth })`.
- Mostra até 4 budgets ordenados por `PercentUsed DESC` (incluindo
  > 100). Link "Ver todos" → `/app/budgets`.
- Se 0 budgets: card cinza "Sem orçamentos este mês — criar."
  com link.

### 5.6 API service
`src/Web/Sextante.Web/src/app/features/financial/core/api/budget-api.service.ts`:
- Métodos:
  - `list(year?: number, month?: number): Observable<BudgetDto[]>`.
  - `get(id: string): Observable<BudgetDto>`.
  - `progress(id: string): Observable<BudgetProgressDto>`.
  - `create(input): Observable<BudgetDto>`.
  - `update(id, input): Observable<BudgetDto>`.
  - `delete(id): Observable<void>`.
  - `activeAlerts(): Observable<BudgetAlertDto[]>`.
  - `acknowledgeAlert(id): Observable<void>`.
- Tipos em `financial.types.ts`:
  - `BudgetDto`, `CreateBudgetInput`, `UpdateBudgetInput`,
    `BudgetProgressDto`, `BudgetAlertDto`.

### 5.7 Navegação
- `app-shell.component.ts` ganha link "Orçamentos" no menu drawer
  (depois de "Recorrentes" Phase 5a).
- `app.routes.ts`: route `budgets` lazy-loaded sob `authGuard`.
- Banner alertas montado no shell, visível em todas as rotas
  autenticadas.

---

## 6. Tests — Integration

### 6.1 Pipeline completo
`tests/Sextante.IntegrationTests/Financial/BudgetWorkflowTests.cs`:
- **Cria Budget e regista transactions até cruzar 80%**:
  - Setup: tenant + Account EUR + Category "Habitação" (Expense) +
    Budget €500 / Habitação / mês corrente.
  - POST 4 transactions de €100 (total €400 = 80%).
  - Aguardar dispatch Wolverine (ou usar in-process synchronous
    config nos tests — Phase 5a precedent).
  - GET `/budgets/alerts/active` → 1 alerta (threshold=80, spent=400).
- **Cruza 100%**:
  - POST mais 1 transaction de €150 (total €550 = 110%).
  - GET `/budgets/alerts/active` → 2 alertas (80 + 100).
- **Acknowledge**:
  - POST `/alerts/{id}/acknowledge` em ambos.
  - GET `/active` → vazio.

### 6.2 Idempotência
- Re-publish do `TransactionCreatedIntegrationEvent` para a mesma
  Transaction → 0 alerts novos.
- Update de uma Transaction (Phase 2) que não muda valor → 0
  alerts novos.
- Update que reduz valor de €120 para €50 (faz cair abaixo de
  100%) → 100% alert permanece (audit, não rolled back).

### 6.3 Multi-currency
- Tenant primary = EUR.
- Budget USD $500 / Subscrições (Expense) / mês corrente.
- Account = EUR; Transaction USD $200 num dia com ECB rate
  1.10 USD/EUR.
- Progress: spent = $200 (já em USD); percent = 40%.
- Adicionar Transaction EUR €450 noutro dia com rate 1.10:
  contribution = €450 * (1/1.10) USD≈ $409. Spent = $609 = 122%.
- 2 alertas disparam (80 + 100).

### 6.4 ECB miss
- Tenant primary = BRL; Budget XOF / Categoria exótica.
- Sem rate XOF → BRL no snapshot.
- POST transaction XOF.
- GET `/budgets/{id}/progress` → `HasIncompleteRates=true`,
  spent ignorado para essa Transaction.
- Inserir rate manual via Phase 3 endpoint.
- GET de novo → `HasIncompleteRates=false`, spent atualizado.

### 6.5 Multi-tenancy
`tests/Sextante.IntegrationTests/Financial/MultiTenancyTests.cs`
estendido:
- Budget de tenant A → tenant B GET retorna 404 / vazio.
- Alert de tenant A → tenant B `activeAlerts` vazio.
- Acknowledge endpoint chamado por tenant B com alert id de A
  → 404.
- Raw SQL com role `sextante_app` (NOBYPASSRLS):
  `SET app.current_tenant_id = '<B>'; SELECT * FROM financial.budgets;`
  → 0 rows quando A tem dados.

### 6.6 Phase 5a integration (recurring → budget)
- Criar RecurringRule mensal Renda €500/Habitação.
- Trigger materializer Phase 5a (Hangfire dashboard ou direct
  enqueue).
- Transaction criada → publish do
  `TransactionCreatedIntegrationEvent` →
  `BudgetAlertDispatchHandler` corre → 100% alert se Budget
  €500 já existe.

### 6.7 Phase 4 integration (CSV import → batch alerts)
- Criar Budget €1000 / Restaurantes.
- Importar CSV com 5 transactions de €300 (total €1500).
- Após import, GET `/active` → 2 alertas (80 + 100). Banner
  agrega — não há "5 banners".

---

## 7. Tests — Karma (frontend)

`src/Web/Sextante.Web/src/app/features/financial/`:

- `pages/budgets.page.spec.ts`:
  - Lista carrega para mês corrente.
  - Mudar mês → re-fetch.
  - Toggle threshold filter (se UI tiver).
  - Empty state.
  - Linhas com cor por threshold.
- `pages/budget.dialog.spec.ts`:
  - Validação form (Limit > 0, threshold range, currency obrigatória).
  - Mês desabilitado em modo edição.
  - Submit chama API.
- `components/budget-progress-card.component.spec.ts`:
  - Cor varia por percent.
  - Tooltip de moeda diferente da primary.
  - Badge "Cálculo parcial" quando `HasIncompleteRates`.
  - Badge "Projeção" só quando não-null.
- `shared/components/budget-alerts-banner.component.spec.ts`:
  - Polling cada 60s.
  - Mostra count agregado.
  - Acknowledge em batch chama N vezes API.
  - Mobile truncate.
- `core/api/budget-api.service.spec.ts`:
  - HTTP calls de CRUD + progress + alerts via
    `HttpTestingController`.

---

## 8. Walkthrough manual (merge-blocker)

> Reproduzível a partir de `git clone` + `docker compose up`.
> Detalhe completo em `validation.md`. Resumo:
>
> 1. Signup novo tenant.
> 2. Criar Account + Categories ("Habitação" Expense,
>    "Subscrições" Expense).
> 3. Criar Budget €500 / Habitação / mês corrente / threshold 80.
> 4. Registar transactions até €400 → banner amarelo aparece.
> 5. Acknowledge banner → desaparece.
> 6. Registar mais €150 → banner vermelho aparece (alert 100).
> 7. Editar Budget → subir limit para €700 → progress recalcula
>    para 78%; alerts existentes permanecem (audit).
> 8. Apagar Budget → soft-delete; alerts ficam soft-deleted juntos.
> 9. Cenário multi-currency: Budget USD $200 / Subscrições;
>    transaction EUR €100 com rate 1.10 → contribution USD ≈ $110.
> 10. Cenário multi-tenant: signup tenant B → não vê nada de A.

---

## 9. Multi-tenancy regression

`tests/Sextante.IntegrationTests/Financial/MultiTenancyTests.cs`
estendido (§6.5 acima): Budget e BudgetAlert.

Architecture test em §1.7 verifica `ITenantOwned` em ambas as
entidades.

---

## 10. Responsive sanity check

Per `tech-stack.md` §19.5, em DevTools 375 / 768 / 1280 px:
- `/app/budgets`: tabela com `overflow-x-auto`; ações collapsam
  em menu kebab em mobile; toggle threshold funcional em touch.
- Dialog "Novo orçamento": form full-width em 375; datepicker e
  selects funcionais em touch.
- `BudgetProgressCard` no Dashboard: 1 coluna em mobile, 2 em
  tablet, 4 em desktop (`grid-cols-1 md:grid-cols-2 lg:grid-cols-4`).
- Banner agregador: truncate + "..." em < 768; lista expandível
  via tap.

---

## 11. Close-out

- 11.1 Skill `changelog` adiciona entrada datada do merge com
  sumário da Phase 5b.
- 11.2 Marcar `specs/roadmap.md` Phase 5 bullets 5–8 (Budget +
  alertas + multi-currency budget) via conversa com o agente.
  Phase 5 fica completamente fechada.
- 11.3 Phase 5 não é 🛡️ no roadmap, mas pode beneficiar de
  sub-agent review focado em (a) idempotência do alert dispatch,
  (b) multi-tenancy do banner / endpoints, (c) Money / exchange
  rate na agregação, (d) RLS + unique index combo. **Decidir em
  PR**: opcional, não merge-blocker.
- 11.4 Commit `Mark phase 5b as complete`. PR
  `phase-5b-budgets → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, Karma).
  - Suite Phases 1a–5a continua verde.
  - Walkthrough manual passa (alertas 80% e 100% disparam,
    multi-currency funciona, multi-tenant isola).
  - Aprovação humana.
- 11.5 Phase 6 (Polish + Deploy + Dogfooding) é o próximo passo
  e depende deste fecho — critério MVP §6 exige metas a
  funcionar.
