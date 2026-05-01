# Validation — Phase 5a: Transações recorrentes + Hangfire

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **`RecurringRule` entity criada** no módulo Financial com migration
   completa (RLS + FORCE + sentinel CHECK + índices).
2. **`Transaction` estendida** com `recurring_rule_id` (FK nullable, ON
   DELETE SET NULL) + index para audit lookup.
3. **Domain methods** `AdvanceNextOccurrence` e
   `GetUpcomingOccurrences` implementados, puros, determinísticos.
4. **`TenantAwareJob<T>` wrapper** em
   `Sextante.Infrastructure.Jobs` — primeira invocação prática do
   pattern descrito em tech-stack §4.6.
5. **`ITenantContextSetter`** em `Identity.PublicApi` permite override
   programático de `ITenantContext` (sem HttpContext).
6. **CRUD completo de RecurringRule** — endpoints REST + UI Angular
   (lista, criar, editar, apagar, toggle IsActive).
7. **Endpoint `GET /upcoming?count=N`** retorna próximas ocorrências
   sem persistir.
8. **Endpoint `GET /transactions?recurringRuleId=<id>`** filtra a
   lista de transações por regra de origem.
9. **`RecurringTransactionMaterializerHandler`** materializa
   `Transaction` para regras com `NextOccurrence ≤ today`, idempotente
   via lookup `(RecurringRuleId, OccurrenceDate)`.
10. **Hangfire recurring job registado** (cron `15 0 * * *` UTC) per-tenant
    no startup + subscriber `UserRegisteredIntegrationEvent` para novos
    tenants.
11. **Multi-currency suportado** — handler resolve
    `IExchangeRateService.ResolveAsync` na data da ocorrência;
    `ExchangeRateUnavailableException` skip da ocorrência sem avançar
    NextOccurrence.
12. **Domain unit tests verdes** — RecurringRule invariants, Advance,
    GetUpcoming, edge cases (month clamp, leap year, EndDate boundary).
13. **Application unit tests verdes** —
    Create/Update/Delete handlers, Materializer handler (idempotência,
    EndDate, multi-rule, sinal Expense/Income, ECB miss),
    `TenantAwareJob<T>` wrapper.
14. **Architecture tests verdes** — `RecurringRule` em Domain sem
    dependência em Hangfire/EF; implementa `ITenantOwned`; wrapper
    `TenantAwareJob<>` vive em BuildingBlocks.
15. **Integration tests verdes** — pipeline completo, idempotência,
    EndDate, multi-tenancy (incl. wrapper enforcement),
    multi-currency com ECB rate, ECB miss boundary.
16. **Karma unit tests verdes** — recurring-rules page, dialog,
    upcoming-occurrences dialog, API service.
17. **Suite Phase 1a/1b/2/3/4 continua verde** (sem regressão).
18. **Manual walkthrough** — renda mensal + salário mensal
    materializam automaticamente após trigger do job; audit em
    `recurring_rule_id` confirmado em psql.
19. **Sanity check responsivo** em 375 / 768 / 1280 px nas páginas
    novas/modificadas.
20. **`specs/roadmap.md` Phase 5 bullets 1–4 ticados** via conversa
    com o agente; bullets 5–8 anotados como Phase 5b.
21. **`CHANGELOG.md`** com entrada datada da Phase 5a.
22. **GitHub Actions CI verde** — build .NET, test .NET, build Angular,
    Karma.

## How to verify each bullet

1. **`RecurringRule` + migration.**
   - `dotnet build -c Release` verde.
   - psql:
     ```sql
     \d financial.recurring_rules
     -- Esperado: colunas description, amount_amount, amount_currency,
     --           account_id, category_id, frequency, interval,
     --           start_date, end_date, next_occurrence, is_active,
     --           tags, audit columns (tenant_id, created_at, ...).
     SELECT polname FROM pg_policies
       WHERE schemaname='financial' AND tablename='recurring_rules';
     -- Esperado: 1 row (tenant_isolation).
     SELECT relrowsecurity, relforcerowsecurity
       FROM pg_class
       WHERE relnamespace='financial'::regnamespace
         AND relname='recurring_rules';
     -- Esperado: t, t.
     ```

2. **`Transaction` estendida.**
   - psql:
     ```sql
     \d financial.transactions
     -- Esperado: column recurring_rule_id uuid.
     SELECT constraint_name FROM information_schema.table_constraints
       WHERE table_schema='financial' AND table_name='transactions'
         AND constraint_name LIKE '%recurring%';
     -- Esperado: 1 row (FK).
     ```

3. **Domain methods.**
   - `dotnet test --filter
     FullyQualifiedName~RecurringRuleTests` verde.
   - Cobre: Daily/Weekly/Monthly/Yearly × Interval=1 e N; clamp em
     `2026-01-31 + 1 month → 2026-02-28`; clamp `2024-02-29 +
     1 year → 2025-02-28`; null quando passa EndDate;
     `GetUpcomingOccurrences` puro.

4. **`TenantAwareJob<T>` wrapper.**
   - `dotnet test --filter
     FullyQualifiedName~TenantAwareJobTests` verde.
   - Confirma: configura ITenantContext; limpa no finally
     (incl. exceção); throw em Guid.Empty; handler resolvido via
     ServiceScope.

5. **`ITenantContextSetter`.**
   - psql / unit test confirma: `SetCurrent(tenantId)` afeta
     `current_setting('app.current_tenant_id')` no
     `DbConnectionInterceptor` durante o scope. `Clear()` reset.

6. **CRUD RecurringRule.**
   - `curl -H "Authorization: Bearer <token>"
     http://localhost/api/financial/recurring-rules` → 200 [].
   - POST → 201 com payload completo. GET /{id} → 200. PUT atualiza
     (200) com NextOccurrence recalculado se StartDate mudou.
     DELETE → 204; subsequente GET → não retorna.
   - Angular: navegar `/app/recurrings` → tabela renderiza;
     "Nova regra" abre dialog; criar/editar/apagar funcionam;
     toggle IsActive persiste.

7. **Upcoming endpoint.**
   - `curl http://localhost/api/financial/recurring-rules/{id}/upcoming?count=5`
     → 200 com array de 5 datas ISO YYYY-MM-DD.
   - Regra com EndDate próximo → retorna < 5 quando rule termina.
   - UI: dialog "Ver próximas" mostra as datas formatadas PT-PT
     (`p-datepicker` formato local).

8. **Filter recurringRuleId.**
   - `curl http://localhost/api/financial/transactions?recurringRuleId=<id>`
     → 200 com apenas Transactions geradas pela regra.
   - Dashboard com `?recurringRuleId=<id>` na URL aplica o filtro
     visualmente.

9. **Materializer handler.**
   - `dotnet test --filter
     FullyQualifiedName~RecurringTransactionMaterializerHandlerTests`
     verde.
   - Cobertura específica:
     - Idempotência: 2× call → 1 Transaction.
     - EndDate: NextOccurrence=null após última.
     - ECB miss: skip sem avançar.
     - Multi-rule: 5 regras due → 5 Transactions.
     - Sinal: Expense → -|amount|, Income → +|amount|.

10. **Hangfire registration.**
    - Hangfire dashboard (`/api/admin/hangfire/recurring`) mostra
      entries `recurring-materializer-{tenantId}` com cron
      `15 0 * * *` UTC.
    - Após signup novo tenant, dashboard ganha entry para o novo
      tenant (subscriber `UserRegisteredIntegrationEvent`).
    - Trigger manual via dashboard → job corre + materializa.

11. **Multi-currency.**
    - Integration test cria regra USD em tenant EUR primary →
      ECB rate snapshot existe → Transaction criada com
      `ExchangeRateToPrimary` e `ExchangeRateAt` populados.
    - Sem snapshot para a moeda → handler skip + log; rule
      NextOccurrence inalterado; manual rate insert (Phase 3
      endpoint) → próximo run materializa.

12. **Domain unit tests.**
    - `dotnet test tests/Modules/Financial.Domain.Tests/` verde,
      incluindo novos `RecurringRuleTests`.

13. **Application unit tests.**
    - `dotnet test tests/Modules/Financial.Application.Tests/` verde,
      incluindo novos `Create*/Update*/Delete*RecurringRuleHandler`,
      `RecurringTransactionMaterializerHandlerTests`,
      `TenantAwareJobTests`.

14. **Architecture tests.**
    - `dotnet test --filter
      FullyQualifiedName~RecurringDependencyTests` verde.

15. **Integration tests.**
    - `dotnet test --filter
      FullyQualifiedName~RecurringMaterializationTests` verde.
    - `dotnet test --filter
      FullyQualifiedName~MultiTenancyTests` verde (estendida com
      RecurringRule).

16. **Karma unit tests.**
    - `cd src/Web/Sextante.Web && npm test -- --watch=false
      --browsers=ChromeHeadless` verde.

17. **Sem regressão.**
    - `dotnet test -c Release` (suite completa) verde.
    - Phases 1a–4 continuam OK.

18. **Manual walkthrough (merge-blocker).**
    - Executar passos da secção abaixo num ambiente limpo
      (`docker compose down -v && docker compose up`).
    - Critérios:
      - Renda mensal materializada automaticamente após trigger
        manual do job.
      - Re-trigger imediato → 0 Transactions novas (idempotência
        verificada).
      - psql confirma `recurring_rule_id` populado e
        `categorization_rule_id` NULL (recurring não invoca rule
        engine).

19. **Sanity responsivo.**
    - DevTools → device toolbar → ciclar 375 × 667, 768 × 1024,
      1280 × 800.
    - `/app/recurrings`: tabela com overflow horizontal se
      necessário; toggle IsActive com touch target ≥ 44px;
      ações condensadas em menu kebab em < 768.
    - Dialog "Nova regra": inputs full-width em mobile;
      datepicker funcional em touch.
    - Dialog "Ver próximas": lista cabe sem horizontal scroll.
    - Sem horizontal scroll do `<body>` em qualquer viewport.

20. **Roadmap.**
    - `git diff main..phase-5a-recurring -- specs/roadmap.md` mostra
      Phase 5 bullets 1–4 com `[x]`; bullets 5–8 anotados com
      pointer "(Phase 5b)" ou similar.

21. **Changelog.**
    - `git diff main..phase-5a-recurring -- CHANGELOG.md` mostra
      secção nova com data do merge sumarizando Phase 5a.

22. **CI verde.**
    - GitHub Actions no commit mais recente do `phase-5a-recurring`
      mostra todos os jobs verdes.

---

## Manual walkthrough — Recurring rules + materialização

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais por valores reais.

### Pré-requisitos

```bash
docker compose down -v && docker compose up
# Migrations correm no startup; Hangfire dashboard fica disponível.
```

### Cenário 1: Renda mensal + salário mensal materializam automaticamente

1. **Signup.** Browser → `http://localhost/` → "Criar conta" →
   `email=tester+phase5a@example.com`, `password=Phase5aPass!`,
   `tenantName=Phase 5a`.

2. **Criar Account.** Menu → Contas → "Nova conta":
   - `Name=Conta corrente`, `Type=Checking`, `Currency=EUR`,
     `OpeningBalance=€2000.00`.

3. **Criar Categories.** Menu → Categorias → "Nova":
   - `Name=Habitação`, `Kind=Expense`, ícone home, cor azul.
   - `Name=Salário`, `Kind=Income`, ícone briefcase, cor verde.

4. **Criar RecurringRule "Renda".** Menu → Recorrentes → "Nova":
   - `Description=Renda apartamento`,
   - `Amount=500`, `Currency=EUR`,
   - `Account=Conta corrente`, `Category=Habitação`,
   - `Frequency=Mensal`, `Interval=1`,
   - `StartDate=hoje`, `EndDate=` (vazio).
   - Salvar. Verificar na lista: `NextOccurrence=hoje`.

5. **Criar RecurringRule "Salário".** Menu → Recorrentes → "Nova":
   - `Description=Salário`,
   - `Amount=1500`, `Currency=EUR`,
   - `Account=Conta corrente`, `Category=Salário`,
   - `Frequency=Mensal`, `Interval=1`,
   - `StartDate=dia 25 deste mês`.
   - Salvar.

6. **Trigger manual do job.** Como SystemAdmin (criar via seed
   ou via psql update direto na role), navegar
   `/api/admin/hangfire/recurring` → encontrar
   `recurring-materializer-{tenantId}` → "Trigger now".
   Aguardar conclusão (estado "Succeeded").

7. **Verificar Transaction "Renda".** Menu → Dashboard:
   - Tabela mostra "Renda apartamento", €-500.00, hoje,
     categoria "Habitação".
   - Detalhe da Transaction (clicar): mostra origem
     "Gerada por regra: Renda apartamento" (link para
     `/app/recurrings`).

8. **Verificar audit em psql.**
   ```sql
   SELECT t.description, t.amount_amount, t.amount_currency,
          t.recurring_rule_id, t.categorization_rule_id,
          rr.description AS rule_name
     FROM financial.transactions t
     LEFT JOIN financial.recurring_rules rr
       ON t.recurring_rule_id = rr.id
     WHERE t.tenant_id = '<tenant-uuid>'
     ORDER BY t.occurred_at DESC;
   ```
   - Esperado: 1 row para "Renda apartamento" com
     `recurring_rule_id` preenchido e `categorization_rule_id`
     NULL. Salário só aparece se hoje ≥ dia 25.

9. **Idempotência: re-trigger imediato.** Hangfire dashboard →
   "Trigger now" do mesmo job.
   - Esperado: estado "Succeeded" mas Dashboard NÃO ganha
     duplicado. psql `SELECT count(*)` em transactions de
     `recurring_rule_id=<renda-id>` → 1.
   - Logs mostram entry `skipDueDuplicate++` (ou equivalente
     log estruturado).

10. **Preview "Ver próximas".** Menu → Recorrentes → ação
    "Ver próximas" na regra Renda.
    - Esperado: 10 datas mensais consecutivas, primeira é
      `next_occurrence` (que avançou após o materialize).

11. **Editar regra: subir renda.** Editar regra Renda →
    `Amount=550` → salvar.
    - Esperado: Transaction de hoje (já materializada) **não muda**
      (€500). Próximo materialize criará Transaction com €550.
    - Verificar via psql: `amount_amount` da Transaction antiga
      ainda 500.

12. **Apagar regra.** Apagar Renda na UI.
    - Esperado: regra desaparece da lista. psql:
      `recurring_rules.deleted_at IS NOT NULL` (soft-delete).
      `transactions.recurring_rule_id` continua a apontar para
      o GUID (FK preserva audit; GET endpoint não retorna a
      regra mas o Dashboard continua a mostrar a Transaction).

### Cenário 2: Multi-currency com ECB rate

1. (continuação do tenant acima ou novo) Criar regra:
   - `Description=Subscrição internacional`,
   - `Amount=10`, `Currency=USD`,
   - `Account=Conta corrente` (EUR), `Category=Subscrições`
     (criar antes se necessário, `Kind=Expense`),
   - `Frequency=Mensal`, `StartDate=hoje`.

2. Trigger manual do job.

3. Verificar via psql:
   ```sql
   SELECT description, amount_amount, amount_currency,
          exchange_rate_to_primary, exchange_rate_at
     FROM financial.transactions
     WHERE recurring_rule_id IN (
       SELECT id FROM financial.recurring_rules
        WHERE description='Subscrição internacional'
     );
   ```
   - `amount_currency='USD'`, `exchange_rate_to_primary` ≠ NULL,
     `exchange_rate_at` = hoje (ECB snapshot do dia).

### Cenário 3: ECB miss

1. Pré-requisito: tenant primary ≠ EUR e moeda exótica sem rate
   (ex.: tenant primary `BRL`, regra em `XOF`).

2. Trigger job. Esperado:
   - Transaction NÃO é criada para a regra XOF.
   - Logger emite warning.
   - `rule.NextOccurrence` permanece (não avançado).
   - Outras regras continuam a materializar normalmente.

3. Inserir rate manual via Phase 3 endpoint
   `POST /api/admin/exchange-rates`:
   - `from=XOF, to=EUR, rate=..., at=hoje`.

4. Re-trigger job. Esperado: Transaction XOF criada agora.

### Cenário 4: Multi-tenancy

1. Logout. Signup novo tenant
   `tester+phase5a-b@example.com`.

2. `/app/recurrings` vazio.

3. `curl -H "Authorization: Bearer <tokenB>"
   http://localhost/api/financial/recurring-rules/<rule-id-de-A>`
   → 404 (RLS).

4. Trigger manual do job de tenant B → 0 Transactions
   materializadas para tenant A (cross-tenant isolation).

5. psql como role `sextante_app` (NOBYPASSRLS):
   ```sql
   SET app.current_tenant_id = '<tenantB-uuid>';
   SELECT * FROM financial.recurring_rules;
   -- Esperado: 0 rows (não vê regras de A).
   ```

---

## Out of scope for validation

- **Budget entity / metas / alertas** — Phase 5b.
- **Edit semantics "future-only" vs "all-pending"** — Phase 5a
  apenas aplica edição a futuras; não há validação de "regenerar
  pendentes".
- **Notificações (email/push) ao gerar Transaction** — Phase 15.
- **CRON expressions / schedules complexos** — apenas Frequency
  × Interval × StartDate.
- **Backfill automático de StartDate passado** — só próxima
  ocorrência futura (validar via hint UI; não validar via teste
  de "criar 4 transactions retroativas").
- **Materialização proativa "Materializar agora" via UI** — só
  Hangfire dashboard (SystemAdmin).
- **Audit log dedicado de execuções** — `Transaction.recurring_rule_id`
  + `CreatedAt` é audit suficiente.
- **Performance benchmark com N regras** — alvo MVP é 10–30
  regras por tenant; sem benchmark formal.
- **Lighthouse / acessibilidade formal** — pós-MVP.
- **Browser cross-version** — Chromium-based suficiente.
- **Sub-agent deep review obrigatório** — Phase 5 não é 🛡️;
  review opcional decidida no PR.
