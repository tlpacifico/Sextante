# Validation — Phase 5b: Budgets + alertas + metas em moeda específica

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **`Budget` entity criada** no módulo Financial com migration
   completa (RLS + FORCE + sentinel CHECK + unique index parcial
   `(tenant_id, category_id, year, month)`).
2. **`BudgetAlert` entity criada** com migration (RLS + FORCE +
   unique index parcial `(tenant_id, budget_id, threshold)`).
3. **`BudgetPeriod` value object** (Year, Month) com invariants e
   métodos `Start`, `End`, `Contains`, `DaysInMonth`, `Next`.
4. **`BudgetProgressCalculator`** puro / determinístico calcula
   `Spent`, `Remaining`, `PercentUsed`, `ProjectedEndOfPeriod`,
   `HasIncompleteRates`.
5. **`BudgetProgressService`** (Application) executa query
   agregadora com conversão multi-moeda via
   `IExchangeRateService.ResolveAsync` (Phase 3) e cache em-memória
   por `(currency, occurredAt)`.
6. **CRUD completo de Budget** — endpoints REST + UI Angular
   (lista por mês, criar, editar `Limit`/`Threshold`/`Notes`, apagar
   soft).
7. **Endpoint `GET /api/financial/budgets?year=&month=`** retorna
   lista com progress embutido.
8. **Endpoint `GET /api/financial/budgets/{id}/progress`** retorna
   progress standalone.
9. **Endpoint `GET /api/financial/budgets/alerts/active`** retorna
   alerts não-acknowledged.
10. **Endpoint `POST /api/financial/budgets/alerts/{id}/acknowledge`**
    marca como reconhecido (idempotente).
11. **`BudgetAlertDispatchHandler`** subscreve
    `TransactionCreatedIntegrationEvent` +
    `TransactionUpdatedIntegrationEvent` (Phase 2), recalcula
    progress e insere alerts em 80% (configurável) e 100%.
12. **Idempotência via unique index** `(tenant_id, budget_id,
    threshold)` — 2× event para mesma transaction = 0 alerts novos.
13. **Multi-currency suportado** — Budget em moeda diferente da
    primary do tenant calcula spent corretamente (conversão dupla
    quando necessário) e degrada graciosamente em ECB miss
    (`HasIncompleteRates=true`, sem 500).
14. **UI página `/app/budgets`** funcional: month picker, tabela
    com cores por threshold (verde / amarelo ≥80 / vermelho ≥100),
    dialog criar/editar, ações apagar.
15. **`BudgetProgressCard`** componente reutilizável no Dashboard
    (top 4) e na página `/app/budgets`.
16. **`BudgetAlertsBanner`** persistente em rotas autenticadas com
    polling 60s; agrega N alerts; botão "Reconhecer" em batch.
17. **Domain unit tests verdes** — Budget, BudgetPeriod, BudgetAlert,
    BudgetProgressCalculator (mix de moedas, soft-deleted,
    projeção, leap year).
18. **Application unit tests verdes** —
    Create/Update/Delete BudgetHandler, BudgetProgressService,
    BudgetAlertDispatchHandler (idempotência, threshold transition,
    Income skip, no-budget skip, custom threshold).
19. **Architecture tests verdes** — `Budget`/`BudgetAlert` em Domain
    sem Hangfire/EF/Wolverine; ambos implementam `ITenantOwned`;
    dispatch em Application.
20. **Integration tests verdes** — pipeline completo (criar Budget
    → POST transactions → alerts disparam → acknowledge);
    multi-tenancy (Budget e Alert isolados);
    multi-currency (Budget USD com tenant primary EUR);
    ECB miss (HasIncompleteRates flag);
    Phase 5a integration (recurring → budget alert);
    Phase 4 integration (CSV batch → banner agregado).
21. **Karma unit tests verdes** — budgets.page, dialog,
    progress-card, alerts-banner, API service.
22. **Suite Phases 1a/1b/2/3/4/5a continua verde** (sem regressão).
23. **Manual walkthrough** — Cenários 1–4 passam num ambiente
    limpo (`docker compose down -v && docker compose up`).
24. **Sanity check responsivo** em 375 / 768 / 1280 px nas páginas
    novas/modificadas.
25. **`specs/roadmap.md` Phase 5 bullets 5–8 ticados** via conversa
    com o agente.
26. **`CHANGELOG.md`** com entrada datada da Phase 5b.
27. **GitHub Actions CI verde** — build .NET, test .NET, build
    Angular, Karma.

## How to verify each bullet

1. **`Budget` + migration.**
   - `dotnet build -c Release` verde.
   - psql:
     ```sql
     \d financial.budgets
     -- Esperado: id, tenant_id, category_id, period_year, period_month,
     --           limit_amount, limit_currency, alert_threshold_percent,
     --           notes, audit columns.
     SELECT polname FROM pg_policies
       WHERE schemaname='financial' AND tablename='budgets';
     -- Esperado: 1 row (tenant_isolation).
     SELECT relrowsecurity, relforcerowsecurity
       FROM pg_class
       WHERE relnamespace='financial'::regnamespace
         AND relname='budgets';
     -- Esperado: t, t.
     SELECT indexdef FROM pg_indexes
       WHERE schemaname='financial' AND tablename='budgets'
         AND indexname='uq_budgets_tenant_category_period';
     -- Esperado: UNIQUE INDEX ... WHERE deleted_at IS NULL.
     ```

2. **`BudgetAlert` + migration.**
   - psql:
     ```sql
     \d financial.budget_alerts
     -- Esperado: id, tenant_id, budget_id, threshold,
     --           triggered_at, spent_at_trigger_amount/currency,
     --           acknowledged, acknowledged_at, audit.
     SELECT indexdef FROM pg_indexes
       WHERE schemaname='financial'
         AND indexname='uq_budget_alerts_budget_threshold';
     -- Esperado: UNIQUE INDEX ... WHERE deleted_at IS NULL.
     ```

3. **`BudgetPeriod` VO.**
   - `dotnet test --filter
     FullyQualifiedName~BudgetPeriodTests` verde.
   - Cobre: invariants Year/Month, Start/End/DaysInMonth/Contains,
     leap year February, transição Dezembro → Janeiro via `Next`.

4. **`BudgetProgressCalculator` puro.**
   - `dotnet test --filter
     FullyQualifiedName~BudgetProgressCalculatorTests` verde.
   - Cobre: percent/remaining em < / = / > limit; projection só
     quando daysElapsed >= 5; HasIncompleteRates flag; empty
     contributions.

5. **`BudgetProgressService` query.**
   - Integration test cria 5 transactions em moedas mistas;
     chama `progress(budgetId)`; assert `Spent` correto, `PercentUsed`
     correto.
   - SQL EXPLAIN ANALYZE confirma uso do índice
     `ix_transactions_tenant_category_occurred` (Phase 2). Sem
     full table scan.

6. **CRUD Budget.**
   - `curl -H "Authorization: Bearer <token>"
     "http://localhost/api/financial/budgets?year=2026&month=5"`
     → 200 [].
   - POST → 201 com payload completo. GET /{id} → 200. PUT atualiza
     (200) com Limit/Threshold/Notes. DELETE → 204; subsequente GET
     → não retorna.
   - Angular: navegar `/app/budgets` → tabela renderiza;
     "Novo orçamento" abre dialog; criar/editar/apagar funcionam.

7. **Lista por mês.**
   - GET com `?year=2026&month=4` retorna budgets de Abril; sem
     query params → mês corrente.
   - Angular: mudar mês picker → tabela atualiza.

8. **Progress endpoint.**
   - `curl http://localhost/api/financial/budgets/{id}/progress`
     → 200 com `{ spentAmount, remainingAmount, percentUsed,
        projectedAmount?, hasIncompleteRates }`.

9. **Active alerts endpoint.**
   - `curl http://localhost/api/financial/budgets/alerts/active`
     → 200 lista ordenada por TriggeredAt DESC. Acknowledged=false
     apenas.

10. **Acknowledge endpoint.**
    - `curl -X POST http://localhost/api/financial/budgets/alerts/{id}/acknowledge`
      → 204.
    - Re-call do mesmo endpoint → 204 (idempotente).
    - GET active → alert não aparece.

11. **Dispatch handler.**
    - `dotnet test --filter
      FullyQualifiedName~BudgetAlertDispatchHandlerTests` verde.
    - Cenários: cruza 80, cruza 100 sem duplicar 80, custom
      threshold, Income skip, no-budget skip, idempotência.

12. **Idempotência alert.**
    - Integration test: re-publish do
      `TransactionCreatedIntegrationEvent` para mesma Transaction
      → `Σ alerts` constante.
    - psql: `SELECT count(*) FROM financial.budget_alerts
        WHERE budget_id=@id AND threshold=80;` → 1.

13. **Multi-currency.**
    - Integration test §6.3 do plan.md verde.
    - Manual: walkthrough cenário 3 (Budget USD, transaction EUR)
      mostra spent convertido corretamente.

14. **UI página.**
    - Navegar `/app/budgets`. Mês picker default = mês corrente.
    - Tabela mostra cores: verde < 80, amarelo 80–99, vermelho ≥
      100. Inspeção visual confirma.
    - Dialog abre, valida campos, submete.

15. **`BudgetProgressCard`.**
    - Dashboard mostra "Orçamentos do mês" com até 4 cards.
    - Cada card mostra cor por percent, valor formatado em moeda
      do Budget, badge "Projeção" se aplicável.

16. **Banner.**
    - Após registar transação que cruza threshold, esperar até
      60s; banner aparece com count.
    - Acknowledge → banner desaparece (até nova transação cruzar
      novo threshold).

17. **Domain unit tests.**
    - `dotnet test tests/Modules/Financial.Domain.Tests/` verde
      (incl. Budgets/).

18. **Application unit tests.**
    - `dotnet test tests/Modules/Financial.Application.Tests/`
      verde (incl. Features/Budgets/).

19. **Architecture tests.**
    - `dotnet test --filter
      FullyQualifiedName~BudgetDependencyTests` verde.

20. **Integration tests.**
    - `dotnet test --filter
      FullyQualifiedName~BudgetWorkflowTests` verde.
    - `dotnet test --filter
      FullyQualifiedName~MultiTenancyTests` verde (estendido).

21. **Karma unit tests.**
    - `cd src/Web/Sextante.Web && npm test -- --watch=false
      --browsers=ChromeHeadless` verde.

22. **Sem regressão.**
    - `dotnet test -c Release` (suite completa) verde.
    - Phases 1a–5a continuam OK.

23. **Manual walkthrough (merge-blocker).**
    - Executar Cenários 1–4 num ambiente limpo (instruções abaixo).
    - Critérios:
      - Budget €500 + 4×€100 transactions → banner amarelo (80%).
      - Mais €150 → banner vermelho (100%).
      - Acknowledge → banner desaparece.
      - Edit Limit → progress recalcula; alerts antigos não removem.
      - Multi-currency Budget USD com transaction EUR funciona.
      - Tenant B não vê dados de A.

24. **Sanity responsivo.**
    - DevTools → device toolbar → ciclar 375 × 667, 768 × 1024,
      1280 × 800.
    - `/app/budgets`: tabela com overflow horizontal; ações
      condensadas em menu kebab em < 768.
    - Dialog: inputs full-width em 375; datepicker funcional em
      touch.
    - `BudgetProgressCard`: layout `grid-cols-1 md:grid-cols-2
      lg:grid-cols-4`.
    - Banner: truncate em mobile.
    - Sem horizontal scroll do `<body>`.

25. **Roadmap.**
    - `git diff main..phase-5b-budgets -- specs/roadmap.md` mostra
      Phase 5 bullets 5–8 com `[x]`.

26. **Changelog.**
    - `git diff main..phase-5b-budgets -- CHANGELOG.md` mostra
      secção nova com data do merge.

27. **CI verde.**
    - GitHub Actions no commit mais recente do `phase-5b-budgets`
      mostra todos os jobs verdes.

---

## Manual walkthrough — Budgets + alertas

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais por valores reais.

### Pré-requisitos

```bash
docker compose down -v && docker compose up
# Migrations correm no startup; ECB job snapshot já tem rates atuais.
```

### Cenário 1: Budget €500/Habitação cruza 80% e 100%

1. **Signup.** Browser → `http://localhost/` → "Criar conta" →
   `email=tester+phase5b@example.com`, `password=Phase5bPass!`,
   `tenantName=Phase 5b`.

2. **Criar Account.** Menu → Contas → "Nova conta":
   - `Name=Conta corrente`, `Type=Checking`, `Currency=EUR`,
     `OpeningBalance=€2000.00`.

3. **Criar Categories.** Menu → Categorias → "Nova":
   - `Name=Habitação`, `Kind=Expense`, ícone home, cor azul.
   - `Name=Subscrições`, `Kind=Expense`, ícone laptop, cor roxa.

4. **Criar Budget Habitação.** Menu → Orçamentos → "Novo":
   - `Categoria=Habitação`, `Mês/Ano=mês corrente`,
     `Limite=500`, `Currency=EUR`, `Threshold=80`,
     `Notes=Renda + condomínio`.
   - Salvar. Verificar na tabela: % consumido = 0%, Restante =
     €500, sem badge de projeção (DaysElapsed < 5 esperado se
     hoje for início do mês).

5. **Registar transação 1.** Menu → Dashboard → "Nova transação":
   - `Account=Conta corrente`, `Category=Habitação`,
     `Amount=-100`, `Currency=EUR`, `Description=Renda parte 1`,
     `Date=hoje`.
   - Verificar: nenhum banner aparece (20% < 80%).

6. **Registar mais 3 transações de €100.**
   - Após cada uma, verificar Dashboard → `BudgetProgressCard`
     atualiza: 20% → 40% → 60% → 80%.
   - Aguardar até 60s após a 4ª (€400 total = 80%) → **banner
     amarelo aparece** "1 orçamento próximo do limite".
   - Verificar via psql:
     ```sql
     SELECT id, threshold, triggered_at, spent_at_trigger_amount,
            spent_at_trigger_currency
       FROM financial.budget_alerts
       WHERE deleted_at IS NULL;
     ```
     - Esperado: 1 row, threshold=80, spent_at_trigger ≈ €400.

7. **Acknowledge.** Banner → "Reconhecer".
   - Banner desaparece. psql confirma `acknowledged=true`,
     `acknowledged_at` preenchido.

8. **Cruzar 100%.** Registar transação €150 (total €550 = 110%).
   - Aguardar até 60s → **banner vermelho** "1 orçamento atingiu
     o limite".
   - psql: 2 alerts (80 acknowledged, 100 unacknowledged).

9. **Editar Budget.** Menu → Orçamentos → editar Habitação →
   `Limite=700` → salvar.
   - Tabela mostra agora `€550 / €700 = 78.6%` (volta ao verde).
   - **Alert 100 antigo permanece** (audit, não rolled back).
     Banner mostra alert 100 enquanto não-acknowledged.
   - psql: `SELECT count(*) FROM financial.budget_alerts
        WHERE acknowledged=false;` → 1 (alert 100).

10. **Acknowledge alert 100.** Banner → "Reconhecer". Banner
    desaparece.

11. **Apagar Budget.** Menu → Orçamentos → ação "Apagar".
    - Soft-delete. psql:
      ```sql
      SELECT id, deleted_at FROM financial.budgets
        WHERE category_id=<habitação-id>;
      -- deleted_at NOT NULL.
      SELECT id, deleted_at FROM financial.budget_alerts
        WHERE budget_id=<budget-id>;
      -- Ambos com deleted_at NOT NULL (cascata aplicada no handler).
      ```

### Cenário 2: Multi-currency (Budget USD, tenant primary EUR)

1. (continuação do tenant Phase 5b) Confirmar que ECB snapshot
   tem rate EUR↔USD (psql `SELECT * FROM shared.exchange_rates
   WHERE date_trunc('day', at)::date = current_date;`).

2. **Criar Budget USD.** Orçamentos → "Novo":
   - `Categoria=Subscrições`, `Limite=200`, `Currency=USD`,
     `Threshold=80`.

3. **Registar transação EUR.** Dashboard → "Nova":
   - `Account=Conta corrente`, `Category=Subscrições`,
     `Amount=-100`, `Currency=EUR`, `Date=hoje`.

4. **Verificar progress USD.** Orçamentos → linha Subscrições.
   - Spent ≈ $108–$112 (depende do rate; ~ EUR 100 / 0.92 USD/EUR).
   - PercentUsed ≈ 54%.
   - Tooltip mostra equivalente EUR.

5. **Verificar via psql.**
   ```sql
   SELECT amount_amount, amount_currency,
          exchange_rate_to_primary, exchange_rate_at
     FROM financial.transactions
     WHERE category_id=<subscriçõ es-id>;
   ```
   - amount_currency='EUR', exchange_rate_to_primary preenchido
     se primary != EUR (skip se primary == EUR).

6. **Empurrar até 80%.** Registar transação EUR €60 (total ≈
   $172 = 86%). Banner amarelo aparece após poll.

### Cenário 3: ECB miss

> Pré-requisito: criar tenant primary BRL ou usar setting de tenant
> que mude primary. Se não suportado no MVP, simular criando
> regra em moeda exótica e removendo manualmente o ECB snapshot.

1. Apagar manualmente o snapshot ECB para uma moeda exótica:
   ```sql
   DELETE FROM shared.exchange_rates
     WHERE base_currency='EUR' AND quote_currency='XAF';
   ```

2. Criar Budget XAF / Categoria nova → POST transaction XAF.

3. GET `/budgets/{id}/progress` → JSON com
   `hasIncompleteRates=true`, `spent` reflete só transactions
   com rate disponível.

4. UI: linha mostra badge cinza "Cálculo parcial" com tooltip
   "1 transação ignorada por falta de câmbio".

5. Inserir rate manual via Phase 3 endpoint
   `POST /api/admin/exchange-rates`:
   ```json
   { "from": "XAF", "to": "EUR", "rate": 0.0015, "at": "2026-05-02" }
   ```

6. Recarregar página → `hasIncompleteRates=false`, spent
   atualizado.

### Cenário 4: Multi-tenancy

1. Logout. Signup novo tenant
   `tester+phase5b-b@example.com`.

2. `/app/budgets` vazio.

3. `curl -H "Authorization: Bearer <tokenB>"
    http://localhost/api/financial/budgets/<budget-id-de-A>`
   → 404 (RLS).

4. Banner não mostra alerts de A.

5. psql como role `sextante_app` (NOBYPASSRLS):
   ```sql
   SET app.current_tenant_id = '<tenantB-uuid>';
   SELECT * FROM financial.budgets;
   -- Esperado: 0 rows.
   SELECT * FROM financial.budget_alerts;
   -- Esperado: 0 rows.
   ```

### Cenário 5 (opcional): integração Phase 5a (recurring)

1. Criar RecurringRule mensal "Renda" €500 / Habitação /
   StartDate=hoje.

2. Criar Budget €500 / Habitação / mês corrente.

3. Trigger materializer Phase 5a (Hangfire dashboard).

4. Esperado: Transaction criada → publish do event →
   `BudgetAlertDispatchHandler` corre → Budget está a 100%
   imediatamente → 2 alerts (80 + 100) inseridos numa cascata.

5. Banner agrega: "1 orçamento atingiu o limite".

### Cenário 6 (opcional): integração Phase 4 (CSV import)

1. Criar Budget €1000 / Restaurantes.

2. Importar CSV com 5 transactions de €300 em "Restaurantes".

3. Após import, banner mostra "1 orçamento atingiu o limite"
   (não 5 banners — agregação).

4. psql: 2 alerts (80 + 100) — não 5×2 = 10.

---

## Out of scope for validation

- **Períodos não-mensais** (semanal/trimestral/anual) — locked
  monthly-only via AskUserQuestion.
- **Email / push notifications** — Phase 15.
- **Carry-over de mês passado** — backlog.
- **Auto-criação do próximo mês** — backlog.
- **Budgets para Income** — backlog.
- **Cache materializada de progress** — re-avaliar pós-dogfooding.
- **Alertas com sinal invertido** ("abaixo de X%") — backlog
  com Income budgets.
- **Performance benchmark** — alvo MVP é 10–30 budgets / tenant;
  sem benchmark formal.
- **Lighthouse / acessibilidade formal** — Phase 6.
- **Browser cross-version** — Chromium-based suficiente.
- **Sub-agent deep review** — Phase 5b não é 🛡️; opcional no PR.
