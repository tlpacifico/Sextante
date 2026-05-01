# Plan — Phase 5a: Transações recorrentes + Hangfire

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 5a entrega `RecurringRule` (entidade + CRUD +
> UI), Hangfire recurring job idempotente que materializa
> `Transaction` com audit trail, e o wrapper `TenantAwareJob<T>` —
> primeira invocação prática do pattern descrito em tech-stack §4.6.
>
> Phases anteriores entregaram:
> - **Phase 2**: `Transaction`, `Account`, `Category`, Money VO,
>   audit columns base, soft-delete uniforme.
> - **Phase 3**: Hangfire setup (Postgres, dashboard, `EcbSnapshotJob`
>   como template para job idempotente), `IExchangeRateService.ResolveAsync`,
>   `Account.Currency`.
> - **Phase 4**: pattern de FK `ON DELETE SET NULL` para audit
>   (`categorization_rule_id`); Phase 5a replica com
>   `recurring_rule_id`.
>
> Phase 5b (Budget + alertas) é spec separada após 5a fechar.

---

## 1. Schema — `RecurringRule` + Transaction extension

### 1.1 `RecurringRule` entity
`src/Modules/Financial/.../Domain/RecurringRules/RecurringRule.cs`:
- `Id (Guid v7)`, `TenantId (Guid)`.
- `Description (varchar(256))` — e.g. "Renda mensal apartamento".
- `Amount (Money)` — VO, persistido como `(amount numeric(20,8), currency varchar(3))`.
- `AccountId (Guid, FK financial.accounts)` — onde a transação aterra.
- `CategoryId (Guid?, FK financial.categories ON DELETE SET NULL)`
  — opcional; se NULL, transação fica uncategorized e regras de
  Phase 4 podem apanhá-la (não automático: o materializer não
  invoca rule engine — Phase 4 reapply endpoint faz isso).
- `Frequency (enum-as-string: 'Daily' | 'Weekly' | 'Monthly' | 'Yearly')`.
- `Interval (int NOT NULL DEFAULT 1)` — "every N units".
- `StartDate (date)` — primeira ocorrência teórica.
- `EndDate (date?)` — nullable; open-ended se NULL.
- `NextOccurrence (date?)` — próxima a materializar; NULL quando
  regra atingiu `EndDate` (estado "completed").
- `IsActive (bool, default true)` — toggle on/off sem apagar.
- `Tags (jsonb, default '[]')` — propagadas à Transaction
  materializada (consistente com Phase 2 Transaction.Tags).
- Audit: `CreatedAt`, `UpdatedAt`, `DeletedAt`, `Version`.
- Implementa `ITenantOwned`.
- Invariants:
  - `Description` 1–256 chars.
  - `Amount.Amount > 0` (sinal aplicado em materialização: Expense
    category → negativo, Income category → positivo, sem categoria
    → assume Income/positivo; documentar).
  - `Interval ≥ 1`.
  - `StartDate ≤ EndDate` (se EndDate não-NULL).
  - Se editar `StartDate`/`Frequency`/`Interval`, recalcular
    `NextOccurrence` (handler).

### 1.2 Domain method `RecurringRule.AdvanceNextOccurrence()`
- Avança `NextOccurrence` para o próximo slot baseado em
  `Frequency × Interval`:
  - Daily: `NextOccurrence + Interval days`.
  - Weekly: `NextOccurrence + (Interval × 7) days`.
  - Monthly: `NextOccurrence + Interval months`, **clamp ao último
    dia do mês** se o dia original não existe (e.g.
    `2026-01-31 + 1 month = 2026-02-28`). Documentar.
  - Yearly: `NextOccurrence + Interval years`, idem clamp para
    29 Fev.
- Se novo valor > `EndDate` (não-NULL), set `NextOccurrence = null`.
- Método retorna `bool isCompleted` (true quando set para null).
- Determinístico e puro — testável sem clock.

### 1.3 Domain method `RecurringRule.GetUpcomingOccurrences(int count)`
- Retorna `IReadOnlyList<DateOnly>` das próximas `count` ocorrências
  (in-memory, sem persistência).
- Implementação: clona estado interno, chama
  `AdvanceNextOccurrence` em loop até `count` ou completion.
- Usado pelo endpoint de preview.

### 1.4 Migration `AddRecurringRulesAndAudit` em Financial
- Cria `financial.recurring_rules` com colunas + RLS:
  ```sql
  ALTER TABLE financial.recurring_rules ENABLE ROW LEVEL SECURITY;
  ALTER TABLE financial.recurring_rules FORCE ROW LEVEL SECURITY;
  CREATE POLICY tenant_isolation ON financial.recurring_rules
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid);
  ALTER TABLE financial.recurring_rules
    ADD CONSTRAINT chk_tenant_not_sentinel
    CHECK (tenant_id <> '<sentinel-uuid>');
  ```
- Adiciona `recurring_rule_id uuid NULL` + FK em `financial.transactions`:
  ```sql
  ALTER TABLE financial.transactions
    ADD COLUMN recurring_rule_id uuid NULL;
  ALTER TABLE financial.transactions
    ADD CONSTRAINT fk_transaction_recurring_rule
    FOREIGN KEY (recurring_rule_id)
    REFERENCES financial.recurring_rules(id)
    ON DELETE SET NULL;
  ```
- Index para o query do job:
  `CREATE INDEX ix_recurring_rules_tenant_next ON financial.recurring_rules
   (tenant_id, next_occurrence) WHERE next_occurrence IS NOT NULL
   AND is_active = true AND deleted_at IS NULL;`
- Index para audit lookup (Dashboard filter):
  `CREATE INDEX ix_transactions_recurring_rule ON
   financial.transactions(recurring_rule_id) WHERE recurring_rule_id
   IS NOT NULL;`
- **Decisão pendente** (ver requirements §Open questions): unique
  partial index em `(recurring_rule_id, date_trunc('day',
  occurred_at))` para defesa-em-profundidade contra race em
  multi-worker. Aplicar agora (cheap insurance) ou adiar.
  **Recomendação para implementação**: aplicar agora.

### 1.5 `FinancialDbContext`
- Adicionar `DbSet<RecurringRule>`.
- Global Query Filter:
  `WHERE TenantId = @currentTenant AND DeletedAt IS NULL`.
- Configurar `Money` VO via `OwnsOne` (mesmo pattern Phase 2).
- Configurar `Tags` como `jsonb`.
- `Transaction` configuration: adicionar
  `RecurringRuleId` como nullable Guid + FK (sem navigation
  property obrigatória — soft reference).

### 1.6 Architecture test
`tests/Sextante.ArchitectureTests/RecurringDependencyTests.cs`:
- `RecurringRule` em `Module.Financial.Domain` referencia apenas
  SharedKernel (Money, ITenantOwned).
- `Module.Financial.Domain` não referencia Hangfire.
- `RecurringRule` implementa `ITenantOwned`.
- Job handler vive em `Module.Financial.Application` ou
  `Module.Financial.Infrastructure` (decidir — recomendação:
  Application para a lógica, Infrastructure para o registo
  Hangfire/wrapper).

---

## 2. `TenantAwareJob<T>` wrapper

### 2.1 Wrapper genérico em BuildingBlocks
`src/BuildingBlocks/Sextante.Infrastructure/Jobs/TenantAwareJob.cs`:
```csharp
public interface ITenantAwareJobHandler<T>
{
    Task ExecuteAsync(T payload, CancellationToken ct);
}

public sealed class TenantAwareJob<T>
{
    public async Task RunAsync(Guid tenantId, T payload,
        CancellationToken ct, IServiceScopeFactory scopeFactory,
        ITenantContextSetter setter, ILogger logger)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        setter.SetCurrent(tenantId);
        try
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ITenantAwareJobHandler<T>>();
            await handler.ExecuteAsync(payload, ct);
        }
        finally
        {
            setter.Clear();
        }
    }
}
```

### 2.2 `ITenantContextSetter` em Identity.PublicApi
- `Identity.PublicApi` ganha contrato `ITenantContextSetter`:
  - `void SetCurrent(Guid tenantId);`
  - `void Clear();`
- Implementação em `Identity.Infrastructure` modifica o
  `TenantContext` interno para aceitar override programático
  (caminho normal continua a ler dos claims).
- **Justificação**: Hangfire jobs não têm `HttpContext` — claims
  não estão disponíveis. Setter é o único caminho seguro;
  fail-loud se chamado de dentro de uma request HTTP autenticada
  (logger warning + ignore? ou throw? **Recomendação**: throw
  em Development, warn em Production — defensivo).

### 2.3 Application unit tests
`tests/Modules/Financial.Application.Tests/Jobs/TenantAwareJobTests.cs`:
- `RunAsync` configura `ITenantContext` antes do handler;
  handler vê o tenant correto.
- `RunAsync` limpa o context no finally (mesmo em exceção).
- `RunAsync` sem `tenantId` válido (Guid.Empty) → throw com erro
  PT-PT.
- Handler exception propaga para Hangfire (retry policy aplicada).

---

## 3. RecurringRule CRUD — API

### 3.1 Comandos e queries (Wolverine in-process)
`src/Modules/Financial/.../Application/RecurringRules/`:

- `CreateRecurringRuleCommand(Description, Amount, Currency,
    AccountId, CategoryId?, Frequency, Interval, StartDate, EndDate?,
    Tags?)`
  → `CreateRecurringRuleHandler`.
  - FluentValidation `CreateRecurringRuleValidator`:
    `Description` 1–256, `Amount > 0`, `Interval ≥ 1`,
    `StartDate ≤ EndDate` (when not null), `Frequency` enum válido,
    `Currency` em allowlist (Phase 3 `ICurrencyDirectory.IsActiveAsync`),
    `AccountId` existe e pertence ao tenant.
  - Handler resolve `Account` para validar tenant ownership
    (RLS dá-nos isso de borla, mas validação pré-DB melhora UX).
  - Calcula `NextOccurrence` inicial:
    - Se `StartDate ≥ today`: `NextOccurrence = StartDate`.
    - Senão: avança da `StartDate` em saltos de
      `Frequency × Interval` até passar `today`; primeiro futuro =
      `NextOccurrence`.
  - Persistência via `RecurringRuleRepository.SaveChangesAsync`
    (UoW por repo, Phase 2 pattern).

- `UpdateRecurringRuleCommand(Id, Description, Amount, Currency,
    AccountId, CategoryId?, Frequency, Interval, StartDate, EndDate?,
    IsActive, Tags?)`
  → `UpdateRecurringRuleHandler`.
  - Recalcula `NextOccurrence` se `StartDate` / `Frequency` /
    `Interval` / `EndDate` mudaram.
  - Não toca em transações já materializadas (decisão phase 5a:
    edição afeta só ocorrências futuras).

- `GetRecurringRuleQuery(Id)` → `RecurringRuleDto`.
- `GetRecurringRulesQuery` → ordenado por `NextOccurrence ASC NULLS
   LAST, Description ASC`.
- `DeleteRecurringRuleCommand(Id)` → soft-delete.
- `GetUpcomingOccurrencesQuery(RuleId, Count=10)` → in-memory call
  a `RecurringRule.GetUpcomingOccurrences(count)`. Retorna
  `IReadOnlyList<DateOnly>`.

### 3.2 Endpoints (Minimal API)
`src/Modules/Financial/.../Api/RecurringRules/RecurringRuleEndpoints.cs`:
- `POST /api/financial/recurring-rules` → 201.
- `PUT /api/financial/recurring-rules/{id}` → 200.
- `GET /api/financial/recurring-rules` → 200 lista.
- `GET /api/financial/recurring-rules/{id}` → 200 individual.
- `GET /api/financial/recurring-rules/{id}/upcoming?count=10`
   → 200 com array de datas.
- `DELETE /api/financial/recurring-rules/{id}` → 204 (soft-delete).
- Todos: `[Authorize]` + tenant context fail-loud.
- Erros: `ProblemDetails` PT-PT (FluentValidation).

### 3.3 Endpoint Phase 2 estendido
- `GET /api/financial/transactions?recurringRuleId=<guid>` —
  filtro novo, opcional. Aplicado no
  `GetTransactionsQueryHandler` (Phase 2). Para o link
  "Ver transações" da UI.

---

## 4. Job — `RecurringTransactionMaterializerJob`

### 4.1 Handler
`src/Modules/Financial/.../Application/RecurringRules/Materialization/`:

`RecurringTransactionMaterializerHandler : ITenantAwareJobHandler<RecurringMaterializerPayload>`:
- `RecurringMaterializerPayload` é `record (DateOnly RunDate)`.
  RunDate default = `DateOnly.FromDateTime(DateTime.UtcNow)`.
- Algoritmo:
  1. `_repo.GetActiveRulesDueAsync(RunDate, ct)` —
     `WHERE next_occurrence <= @runDate AND is_active = true
      AND next_occurrence IS NOT NULL`.
     RLS aplicada (já estamos em scope do tenant via wrapper).
  2. Para cada `rule`:
     - Loop interno (uma regra pode ter ocorrências passadas se o
       job não correu em N dias — improvável mas defensivo):
       enquanto `rule.NextOccurrence <= RunDate`:
       - Idempotência: `_repo.HasMaterializedAsync(rule.Id,
         rule.NextOccurrence, ct)` →  se `true`, log debug + skip
         para evitar duplicate.
       - Se `false`:
         - Resolver `account = _accountRepo.GetById(rule.AccountId)`.
         - Resolver `exchangeRate = _exchangeRateService.ResolveAsync(
             rule.Amount.Currency, _tenantPrimary, rule.NextOccurrence,
             ct)`. Se lança `ExchangeRateUnavailableException`:
           catch + log error + skip esta ocorrência (não bloqueia
           outras regras). Avança `NextOccurrence` mesmo assim?
           **Decisão**: **não avança** se rate falha — user precisa
           de inserir rate manual; próximo run tenta novamente.
           Documentar em comentário.
         - Determinar sinal: se `category.Kind == Expense` →
           `amount = -|rule.Amount.Amount|`; se `Income` ou
           `category` é null → `amount = +|rule.Amount.Amount|`.
         - `Transaction.Create(...)` com:
           `OccurredAt = rule.NextOccurrence + 00:00 UTC`,
           `Amount = new Money(amount, rule.Amount.Currency)`,
           `AccountId = rule.AccountId`,
           `CategoryId = rule.CategoryId`,
           `Description = rule.Description`,
           `Tags = rule.Tags`,
           `RecurringRuleId = rule.Id`,
           `ExchangeRateToPrimary = exchangeRate?.Rate`,
           `ExchangeRateAt = exchangeRate?.At`.
         - Persistir Transaction.
         - `rule.AdvanceNextOccurrence()`.
     - Persistir mudança em `rule` (NextOccurrence atualizado /
       potencialmente null).
  3. `SaveChangesAsync` no fim (1 Tx por regra ou batch — escolher;
     batch é mais eficiente, isolation aceitável para job offline).
  4. Log estruturado: `{ tenantId, totalRules, materialized,
     skippedDueRate, skippedDueDuplicate, completedRules }`.
- **Não invoca** `CategorizationRuleEngine` da Phase 4 — recurring
  já tem categoria explícita. Se user quiser que regras Phase 4
  sobreescrevam, usa o reapply endpoint manualmente.

### 4.2 Repositório
`src/Modules/Financial/.../Infrastructure/RecurringRules/RecurringRuleRepository.cs`:
- `GetActiveRulesDueAsync(DateOnly runDate, CancellationToken ct)`.
- `HasMaterializedAsync(Guid ruleId, DateOnly occurrenceDate,
    CancellationToken ct)` —
  `SELECT EXISTS(SELECT 1 FROM financial.transactions
   WHERE recurring_rule_id = @id
     AND date_trunc('day', occurred_at) = @date::timestamp
     AND deleted_at IS NULL)`.

### 4.3 Job registration (Hangfire) com scan per-tenant
`src/Bootstrap/Sextante.Host/Hangfire/RecurringJobsRegistration.cs`:
- Hosted service que corre no startup:
  - Lê `IAdminContext` (Phase 3 / 4 — `BYPASSRLS`) para
    `SELECT id FROM shared.tenants WHERE deleted_at IS NULL`.
  - Para cada `tenantId`, regista
    `RecurringJob.AddOrUpdate(
       recurringJobId: $"recurring-materializer-{tenantId}",
       () => _enqueuer.Enqueue(tenantId,
         new RecurringMaterializerPayload(...)),
       cronExpression: "15 0 * * *",
       options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc })`.
- Subscriber em `UserRegisteredIntegrationEvent` (Phase 1a /
  Phase 2 pattern) regista a entry Hangfire para o novo tenant.
  Reutiliza o seed handler ou cria novo handler dedicado em
  Wolverine.
- **Alternativa simplificada**: 1 recurring job global → handler
  faz scan de tenants (precisa de `IAdminContext` no scan top
  + `TenantAwareJob<T>` per-tenant inner). Documentar a escolha
  em comentário de bloco no `RecurringJobsRegistration`. Ver
  `requirements.md > Open questions`.

### 4.4 Hangfire dashboard trigger manual
- `/api/admin/hangfire` (Phase 3) já permite ao SystemAdmin fazer
  trigger manual. Sem endpoint adicional na Phase 5a.

---

## 5. RecurringRule CRUD — UI Angular

### 5.1 Página `recurring-rules.page.ts`
`src/Web/Sextante.Web/src/app/features/financial/pages/recurring-rules/recurring-rules.page.ts`:
- Route: `/app/recurrings`. Lazy-loaded sob `authGuard`.
- PrimeNG `p-table`:
  - Colunas: Description, Amount + Currency (formatado via
    `MoneyPipe` de Phase 2), Frequency (badge), Interval,
    NextOccurrence (data formatada PT-PT), IsActive (toggle),
    Ações (Editar / Apagar / Ver próximas / Ver transações).
  - Estado "completed" (NextOccurrence=null) → linha cinza com
    badge "Concluída".
- Botão "Nova regra" → abre `RecurringRuleDialog`.
- Toast PT-PT em sucesso ("Regra criada", "Regra atualizada",
  "Regra apagada").
- Empty state: "Sem regras recorrentes. Cria a primeira para
  automatizar transações."

### 5.2 Dialog `recurring-rule.dialog.ts`
- Form fields:
  - Description (inputText).
  - Amount (`p-inputNumber` com mode currency, ligado a Currency).
  - Currency (`p-select`, populated por `currencyApi.list()`).
  - Account (`p-select` com Phase 2 `accountApi.list()`).
  - Category (`p-select` opcional, Phase 2 `categoryApi.list()`).
  - Frequency (`p-select`: Diária / Semanal / Mensal / Anual).
  - Interval (`p-inputNumber`, min 1, default 1).
  - StartDate (`p-datepicker` PT-PT).
  - EndDate (`p-datepicker` PT-PT, opcional).
  - Tags (chip input, opcional — propaga à Transaction).
- Hint visual quando `StartDate < today`:
  "Esta regra começou em {data}; primeira materialização será em
  {NextOccurrence preview}."
- `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`.

### 5.3 Dialog `upcoming-occurrences.dialog.ts`
- Trigger: ação "Ver próximas" no row.
- Chama `GET /api/financial/recurring-rules/{id}/upcoming?count=10`.
- Lista 10 datas formatadas PT-PT.
- Badge "próxima a materializar" no primeiro item se
  `NextOccurrence == today` ou `<today`.
- Mensagem se retornar < 10 datas: "Regra termina em
  {EndDate} — {N} ocorrências futuras."

### 5.4 API service
`src/Web/Sextante.Web/src/app/features/financial/core/api/recurring-rule-api.service.ts`:
- Métodos: `list()`, `get(id)`, `create(input)`, `update(id, input)`,
  `delete(id)`, `upcoming(id, count?)`.
- Tipos em `financial.types.ts`: `RecurringRuleDto`,
  `CreateRecurringRuleInput`, `UpdateRecurringRuleInput`,
  `FrequencyValue`, `FREQUENCY_LABELS` (PT-PT).

### 5.5 Navegação
- `app-shell.component.ts` ganha link "Recorrentes" no menu drawer.
- `app.routes.ts`: route `recurrings` lazy-loaded sob `authGuard`.

### 5.6 Filtro "Ver transações" no Dashboard
- `dashboard.page.ts` (Phase 2/3) ganha leitura do query param
  `?recurringRuleId=<guid>`. Quando presente, filtra a tabela.
- `transaction-api.service.ts` ganha parâmetro
  `recurringRuleId?: string` em `list()`.

---

## 6. Tests

### 6.1 Domain unit tests
`tests/Modules/Financial.Domain.Tests/RecurringRules/`:
- `RecurringRuleTests.cs`:
  - Description 1–256 enforced (lança fora dos limites).
  - Interval ≥ 1 enforced.
  - StartDate ≤ EndDate enforced.
  - Frequency enum válido.
  - `AdvanceNextOccurrence` Daily / Weekly / Monthly / Yearly
    com Interval=1 e Interval=N.
  - `AdvanceNextOccurrence` clamp em mês curto (`2026-01-31 +
    1 month → 2026-02-28`).
  - `AdvanceNextOccurrence` clamp em ano bissexto
    (`2024-02-29 + 1 year → 2025-02-28`).
  - `AdvanceNextOccurrence` set para null quando passa EndDate.
  - `GetUpcomingOccurrences(count)` retorna até `count` datas.
  - `GetUpcomingOccurrences` retorna menos quando rule termina.
  - `GetUpcomingOccurrences` é puro (não muta o estado da regra).
  - Soft-delete + Archive comportam-se idempotente.

### 6.2 Application unit tests
`tests/Modules/Financial.Application.Tests/`:
- `RecurringRules/CreateRecurringRuleHandlerTests.cs`:
  - StartDate futuro → NextOccurrence = StartDate.
  - StartDate passado → NextOccurrence avança até futuro.
  - Currency inactive → erro.
  - Account de outro tenant → 404 (RLS).
- `RecurringRules/UpdateRecurringRuleHandlerTests.cs`:
  - Mudar StartDate recalcula NextOccurrence.
  - Mudar Description não recalcula NextOccurrence.
  - Mudar Frequency recalcula NextOccurrence baseado no novo
    pattern + StartDate atual.
- `RecurringRules/Materialization/RecurringTransactionMaterializerHandlerTests.cs`:
  - 1 regra Daily, NextOccurrence=today → materializa 1 Transaction
    e avança NextOccurrence em 1 dia.
  - 1 regra com NextOccurrence=ontem (job não correu ontem) →
    materializa 2 Transactions (ontem + hoje), avança 2× a
    NextOccurrence.
  - Idempotência: 2× a chamada com mesma RunDate → segunda chamada
    é no-op (skipDueDuplicate++).
  - EndDate boundary: regra com EndDate=today → materializa hoje,
    NextOccurrence set para null.
  - ExchangeRateUnavailableException → skip (não avança), próximo
    run tenta novamente; outras regras continuam.
  - Multi-rule batch: 5 regras due → 5 Transactions criadas numa
    transação EF.
  - Categoria Expense → amount negativo.
  - Categoria Income → amount positivo.
  - Categoria null → amount positivo (default Income).
- `Jobs/TenantAwareJobTests.cs` (cobre wrapper, ver §2.3).

### 6.3 Architecture tests
`tests/Sextante.ArchitectureTests/RecurringDependencyTests.cs`:
- `RecurringRule` em Domain, sem dependência em Hangfire / EF.
- `RecurringRule` implementa `ITenantOwned`.
- Job handler vive em Application; registo Hangfire em Bootstrap.
- `Sextante.Infrastructure.Jobs.TenantAwareJob<>` é genérico e
  usado por pelo menos 1 handler.

### 6.4 Integration tests
`tests/Sextante.IntegrationTests/Financial/RecurringMaterializationTests.cs`:
- **Pipeline completo**: criar Account + Category + RecurringRule
  com `StartDate=today`, Frequency=Monthly → enqueue manual do
  job (via `IBackgroundJobClient`) → aguardar conclusão → assert
  Transaction criada com `RecurringRuleId`, sinal correto, audit
  columns preenchidas.
- **Idempotência**: enqueue 2× → assert exatamente 1 Transaction
  (não 2). Logger captou skip.
- **EndDate**: regra com EndDate=today, Monthly → materializa hoje
  → NextOccurrence=null → próximo run não cria nada.
- **Multi-tenancy**: Tenant A cria regra; job per-tenant para
  tenant B não materializa em A. Assert RLS via
  `SqlState=42501` se tentado cross-tenant via raw SQL.
- **TenantAwareJob<T> wrapper**: enqueue sem TenantId no payload
  → throw + Hangfire marca failed.
- **Multi-currency**: regra em USD, tenant primary EUR, ECB rate
  no snapshot → Transaction criada com `ExchangeRateToPrimary`
  e `ExchangeRateAt` preenchidos.
- **ECB miss**: regra em moeda exótica sem rate → Transaction NÃO
  criada; rule.NextOccurrence inalterado; próximo run depois de
  inserir rate manual cria a Transaction.
- **Filter on Dashboard**:
  `GET /api/financial/transactions?recurringRuleId=<id>` retorna
  só as Transactions geradas por essa regra.

### 6.5 Karma unit tests
`src/Web/Sextante.Web/src/app/features/financial/`:
- `pages/recurring-rules/recurring-rules.page.spec.ts`:
  - Lista carrega, ordenação, toggle IsActive, ações abrem dialogs.
- `pages/recurring-rules/recurring-rule.dialog.spec.ts`:
  - Form validação (Description, Amount > 0, Interval ≥ 1,
    StartDate ≤ EndDate); submit chama API; hint visual quando
    StartDate < today.
- `pages/recurring-rules/upcoming-occurrences.dialog.spec.ts`:
  - Lista N datas formatadas PT-PT; badge "próxima" no primeiro
    se today; mensagem "regra termina em" quando < N retornadas.
- `core/api/recurring-rule-api.service.spec.ts`:
  - HTTP calls de CRUD + upcoming + delete via
    `HttpTestingController`.

---

## 7. Walkthrough manual (merge-blocker)

> Reproduzível a partir de `git clone` + `docker compose up`.
> Cenário em alto nível; passos concretos vivem em
> `validation.md`.

1. Signup novo tenant.
2. Criar Account "Conta corrente EUR" + Category "Habitação"
   (Expense) + Category "Salário" (Income).
3. Criar RecurringRule "Renda" (Mensal, EUR -500, StartDate=hoje)
   e "Salário" (Mensal, EUR +1500, StartDate=dia 25 deste mês).
4. Trigger manual do job via Hangfire dashboard
   `/api/admin/hangfire` ou aguardar 00:15 UTC do próximo dia.
5. Verificar no Dashboard que aparece a Transaction "Renda" com
   sinal negativo, badge ou indicador "Gerada por regra: Renda"
   (UI Phase 5a — TBD nível de visibilidade — pelo menos no
   detalhe da Transaction).
6. Verificar via psql que `recurring_rule_id` está preenchido.
7. Editar a regra "Renda" (mudar valor para -550) → confirma
   que a Transaction de ontem (já materializada) **não muda**;
   próxima ocorrência ficará em -550.
8. Verificar preview "Ver próximas" — mostra 10 datas mensais
   futuras.
9. Apagar a regra → soft-delete; Transaction histórica mantém-se
   (FK ON DELETE SET NULL — `recurring_rule_id` continua a
   apontar mas a regra não aparece em GET).

---

## 8. Multi-tenancy regression

`tests/Sextante.IntegrationTests/Financial/MultiTenancyTests.cs`
estendido com:
- `RecurringRule` — Tenant A cria, lista, edita, apaga; Tenant B
  não vê nem afeta.
- Job per-tenant: tenant B não materializa em tenant A (cross-tenant
  scan bloqueado pela RLS, ou pelo wrapper a configurar
  `ITenantContext` para o tenant correto).
- Endpoint
  `GET /api/financial/transactions?recurringRuleId=<id-de-A>`
  chamado por tenant B → 404 ou lista vazia (não vê a regra de A).

---

## 9. Responsive sanity check

Per `tech-stack.md` §19.5, inspecionar em DevTools 375 / 768 / 1280:
- `/app/recurrings`: tabela com `overflow-x-auto`; toggle
  IsActive funcional em mobile (touch target ≥ 44px); ações
  collapsam em menu kebab em mobile?
- Dialog "Nova regra": form fica usável em 375 px (inputs
  full-width, datepickers funcionais em touch).
- Dialog "Ver próximas": lista cabe sem scroll horizontal.
- Dashboard com filtro `?recurringRuleId=<id>` aplicado: layout
  Phase 2/3 mantém-se sem regressão.

---

## 10. Close-out

- 10.1 Skill `changelog` adiciona entrada datada do merge com
  sumário da Phase 5a.
- 10.2 Marcar `specs/roadmap.md` Phase 5 bullets 1–4 (recurring +
  Hangfire + preview) via conversa com o agente. Bullets 5–8
  (Budget) ficam para Phase 5b — anotar na linha do roadmap.
- 10.3 Phase 5 não é 🛡️ no roadmap, mas pode beneficiar de
  sub-agent review focado em (a) idempotência do job,
  (b) multi-tenancy do wrapper, (c) Money / exchange rate
  resolution. **Decidir em PR**: opcional, não merge-blocker
  (regra dura aplica-se só a phases 🛡️).
- 10.4 Commit `Mark phase 5a as complete`. PR
  `phase-5a-recurring → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, Karma).
  - Suite Phase 1a/1b/2/3/4 continua verde.
  - Walkthrough manual passa (renda + salário materializam
    automaticamente; idempotência verificada).
  - Aprovação humana.
