# Plan — Phase 3: Multi-moeda + ECB

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 3 é o primeiro sistema real multi-moeda — o
> `Money` VO ficou em SharedKernel desde Phase 2, mas nada exercitava
> moedas diferentes da primary do tenant. Aqui isso muda: USD numa
> conta EUR persiste o câmbio do momento. Validação inclui Domain
> unit tests + Karma + integration tests CRUD/multi-tenancy +
> walkthrough multi-moeda manual (merge-blocker per AskUserQuestion).
> Phase 3 **não** está marcada 🛡️ no roadmap, logo sub-agent deep
> review é opcional.

---

## 1. Schema `shared` — entidades Currency e ExchangeRate

- 1.1 `Currency` em `src/BuildingBlocks/SharedKernel/Currency.cs`:
  - `Code (string, ISO 4217 — varchar(3) PK)`,
    `Name (string)`, `Symbol (string)`,
    `MinorUnits (int)` (display rounding, default 2),
    `IsActive (bool)`, audit (`CreatedAt`/`UpdatedAt`/`Version`).
  - **Sem `DeletedAt`**: soft-delete de reference data faz-se via
    `IsActive=false` (decisão registada em `requirements.md`).
  - SharedKernel (não Identity.Domain) para que Financial referencie
    sem violar §3.1.
- 1.2 `ExchangeRate` em
  `src/Modules/Identity/Sextante.Modules.Identity.Domain/Entities/ExchangeRate.cs`:
  - `Id (Guid v7)`, `RateDate (date, UTC)`,
    `FromCurrency (varchar(3))` — sempre `'EUR'` no MVP per opt-in,
    `ToCurrency (varchar(3) FK shared.Currencies)`,
    `Rate (decimal(20,8))`,
    `Source (varchar(16))` — enum-as-string `'ECB' | 'manual'`,
    audit.
  - Unique constraint `(RateDate, FromCurrency, ToCurrency)`.
  - **Sem RLS** (reference data partilhada cross-tenant).
- 1.3 `EcbSnapshotState` (single-row table em `shared`) com
  `LastRunAt`, `LastSuccessAt`, `LastError (varchar(2000) NULL)`.
  Surface para a UI Settings (passo 3.3).
- 1.4 `IdentityDbContext` regista DbSets para `Currency`,
  `ExchangeRate`, `EcbSnapshotState`. **Não** adicionar Global Query
  Filter de tenant a estas entidades.
- 1.5 Migration `AddCurrencyAndExchangeRate` no módulo Identity:
  - Cria as 3 tabelas no schema `shared`.
  - Seed ISO 4217 fiat (≈170 rows) via `migrationBuilder.InsertData`.
    Lista canónica em
    `src/Modules/Identity/.../Migrations/Data/Iso4217Currencies.cs`
    (pode ser gerada de
    `https://www.currency-iso.org/dam/downloads/lists/list_one.xml`
    e checked-in; sem dependência runtime).
  - **Não** seedar crypto (per opt-in OUT — Phase 8).
  - Adicionar `INSERT` initial em `EcbSnapshotState`.
- 1.6 Architecture test: `Module.Financial.*` referencia
  `SharedKernel.Currency`; **não** referencia
  `Identity.Domain.ExchangeRate` (lookup vai pela
  `IExchangeRateService` em §5).

## 2. ECB integration — provider abstrato + implementação

- 2.1 `ICurrencyProvider` em
  `src/BuildingBlocks/Sextante.Infrastructure/ExchangeRates/ICurrencyProvider.cs`:
  - `Task<EcbSnapshot> FetchLatestAsync(CancellationToken ct)`
  - `EcbSnapshot(DateOnly RateDate,
      IReadOnlyList<EcbDailyRate> Rates)`
  - `EcbDailyRate(string ToCurrency, decimal Rate)` (FromCurrency
    sempre `EUR` no MVP).
- 2.2 `EcbCurrencyProvider` em
  `src/BuildingBlocks/Sextante.Infrastructure/ExchangeRates/EcbCurrencyProvider.cs`:
  - Typed `HttpClient` registado em `AddHttpClient<EcbCurrencyProvider>`
    com base URL
    `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml`.
  - Polly retry: 3 tentativas, 2s backoff exponencial, total timeout
    10s (`HandleTransientHttpError` + `WaitAndRetryAsync`).
  - Parse XML (root `gesmes:Envelope` → `Cube` → `Cube time="..."` →
    `Cube currency="USD" rate="1.0856"`). Mapper isolado em
    classe interna estática para facilitar test.
- 2.3 Fail-loud na falha: parser exception → `EcbProviderException`
  com mensagem PT-PT. Job em §3 captura e regista em
  `EcbSnapshotState.LastError`.

## 3. Hangfire daily snapshot job

- 3.1 `EcbSnapshotJob` em
  `src/Modules/Identity/Sextante.Modules.Identity.Infrastructure/Jobs/EcbSnapshotJob.cs`:
  - Construtor injeta `ICurrencyProvider`, `IdentityDbContext`,
    `ILogger<EcbSnapshotJob>`.
  - Método `RunAsync(CancellationToken ct)`:
    1. Atualiza `EcbSnapshotState.LastRunAt = now`.
    2. Chama provider.
    3. Para cada `EcbDailyRate`, upsert em `shared.ExchangeRate`
       (`ON CONFLICT DO UPDATE` via raw SQL ou EF
       `FromSqlInterpolated`; manter idempotente).
    4. Atualiza `LastSuccessAt = now`, `LastError = null`.
    5. Em exception: `LastError = ex.Message` (truncar a 2000),
       re-throw para Hangfire marcar a job como failed.
  - **Sem** `TenantAwareJob<T>` — rates são globais (tech-stack §10
    prescreve o wrapper para jobs que tocam tenant-data).
- 3.2 Registo em `Program.cs`:
  - `RecurringJob.AddOrUpdate<EcbSnapshotJob>("ecb-snapshot",
    job => job.RunAsync(CancellationToken.None),
    "30 0 * * *", TimeZoneInfo.Utc);` (cron 00:30 UTC).
- 3.3 Endpoint admin `POST /api/admin/exchange-rates/snapshot/run`
  para forçar execução imediata (usado pelo walkthrough manual em
  §12). Encole via `BackgroundJob.Enqueue<EcbSnapshotJob>(...)`.
- 3.4 Test idempotência: correr `RunAsync` duas vezes consecutivas
  com o mesmo provider → segundo run apenas updates timestamps,
  não cria duplicates.

## 4. Manual rate insertion + admin UI

- 4.1 Endpoint `POST /api/admin/exchange-rates`
  (`Authorize(Roles = "SystemAdmin")`):
  - Payload `{ rateDate (YYYY-MM-DD), toCurrency (ISO),
    rate (decimal) }`.
  - FluentValidation: `rateDate ≤ today`, `toCurrency ∈ active
    currencies`, `rate > 0`.
  - Upsert com `Source = 'manual'` (mesma unique constraint que ECB
    sobreescreve manual; manual sobreescreve ECB).
  - Toast PT-PT *"Taxa manual registada para {toCurrency} em
    {date}."*
- 4.2 Endpoint `GET /api/admin/exchange-rates`:
  - Query `?from=YYYY-MM-DD&to=YYYY-MM-DD&currencies=USD,BRL`.
  - Default: últimos 7 dias × top-5 currencies (configurável em
    `appsettings.json` `ExchangeRates:DefaultDisplayCurrencies`).
- 4.3 Página Angular `/app/admin/exchange-rates`:
  - PrimeNG `p-table` com colunas Date, Currency, Rate, Source.
  - Botão "Forçar snapshot ECB" → chama §3.3 → toast com resultado.
  - Botão "Inserir taxa manual" → `p-dialog` com form Reactive
    (calendar para date, dropdown currency, inputNumber rate).
  - Surface `EcbSnapshotState.LastError` em `p-message severity=warn`
    quando preenchido.

## 5. Refactor Transaction para snapshot de câmbio

- 5.1 Migration `AddExchangeRateToTransaction` em Financial:
  - `ALTER TABLE financial.transactions
      ADD COLUMN exchange_rate_to_primary numeric(20,8) NULL,
      ADD COLUMN exchange_rate_at timestamptz NULL;`
  - Sem backfill (per opt-in OUT). Linhas pré-Phase-3 ficam NULL e
    são interpretadas como `1.0` em queries.
- 5.2 Domain — extender
  `src/Modules/Financial/.../Domain/Transactions/Transaction.cs`:
  - Novo VO interno `ExchangeRateSnapshot(decimal Rate,
    DateTimeOffset At)` em
    `Domain/Transactions/ExchangeRateSnapshot.cs`.
  - Adicionar propriedades `ExchangeRateToPrimary (decimal?)` e
    `ExchangeRateAt (DateTimeOffset?)` (private set).
  - Atualizar `Create(...)` e `Update(...)`: aceitar parâmetro
    `ExchangeRateSnapshot? snapshot`. Persistir Rate+At quando não
    nulo. **Remover** o invariant Phase-2 *"`Amount.Currency` ==
    tenant primary"* — agora é responsabilidade da Application
    layer enriquecer.
- 5.3 Application — `IExchangeRateService` em
  `src/Modules/Financial/.../Application/ExchangeRates/IExchangeRateService.cs`:
  - `Task<ExchangeRateSnapshot?> ResolveAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset at,
        CancellationToken ct)`
  - Implementação em
    `src/Modules/Financial/.../Infrastructure/ExchangeRates/ExchangeRateService.cs`:
    1. Se `from == to`, retorna `null` (signal "rate = 1.0 implied").
    2. Caso contrário, lookup em `shared.ExchangeRate` para
       `RateDate = DateOnly.FromDateTime(at.UtcDateTime)`.
    3. Cross-rate via EUR-base:
       `from→to = (1 / EUR→from) × EUR→to`.
       Casos: `from=EUR` → `EUR→to`; `to=EUR` → `1 / EUR→from`;
       neither → cross-rate.
    4. Se algum lookup falha → throw
       `ExchangeRateUnavailableException(from, to, date)`.
- 5.4 `CreateTransactionHandler` (Application):
  - Resolve `tenantPrimary` via `ITenantCurrencyResolver`.
  - Resolve `account.Currency` (campo novo §6).
  - `Money amount` recebido com `Currency = account.Currency`
    por default (o frontend prefilla; se vier outra coisa, falha
    invariant em §6).
  - Chama `IExchangeRateService.ResolveAsync(amount.Currency,
    tenantPrimary, occurredAt, ct)`.
  - Em `ExchangeRateUnavailableException` → retorna
    `ProblemDetails` PT-PT *"Não há taxa de câmbio disponível para
    {from} → {to} em {date}. Insira manualmente em
    /app/admin/exchange-rates."*
- 5.5 `UpdateTransactionHandler`:
  - **Não** re-resolve rate em update — rate fica congelado em
    insert. Documentado como decisão.

## 6. Account.Currency override

- 6.1 Migration `AddAccountCurrency` em Financial:
  - `ALTER TABLE financial.accounts
      ADD COLUMN currency varchar(3) NOT NULL DEFAULT '';`
  - Em script de Up: `UPDATE financial.accounts a
      SET currency = (SELECT primary_currency FROM
        shared.tenants t WHERE t.id = a.tenant_id);`
  - Drop default (pós-update) para forçar fornecimento explícito
    em inserts futuros.
  - Add FK constraint a `shared.currencies(code)`.
- 6.2 Domain — `Account.cs`:
  - Adicionar `Currency (string)` private set.
  - Atualizar factory `Create(...)`: aceitar `currency` parâmetro;
    validar contra allowlist (passada por DI ao handler, não
    embutida no Domain — Domain só valida regex ISO 4217).
  - Invariant: `OpeningBalance.Currency == Currency`.
- 6.3 `CreateAccountHandler`:
  - Resolve allowlist via `ICurrencyDirectory.GetActiveCodesAsync()`
    (interface fina em `SharedKernel`).
  - Falha PT-PT se currency inativa.
- 6.4 Frontend — `accounts.page.ts`:
  - Form: dropdown currency (lookup via
    `currencyApi.listActive()`), prefill com `tenant.primaryCurrency`.
  - `OpeningBalance` `p-inputNumber` lê `mode=currency`
    + `currency=<selectedCurrency>`.
- 6.5 Frontend — formulário "Nova transação":
  - Quando user escolhe Account, prefill `amount.currency =
    account.currency`. User pode manualmente mudar (ex.: USD em
    Conta EUR), o que aciona o snapshot de câmbio em §5.

## 7. ITenantCurrencyResolver — atualização de shape

- 7.1 Atualizar interface em
  `src/Modules/Identity/.../PublicApi/Abstractions/ITenantCurrencyResolver.cs`:
  - Manter `GetPrimaryCurrencyAsync(ct)`.
  - Aplicar mudança que o comentário inline já antecipava em
    `src/Modules/Financial/.../Persistence/TenantCurrencyResolver.cs:11`:
    a abstração do resolver passa a ser apenas "primary currency
    do tenant". Resolução de exchange rate sai daqui e vai para
    `IExchangeRateService` (§5.3).
- 7.2 Atualizar comment inline para refletir a decisão (remover
  *"Phase 3 substitui por uma variante que aceita override por
  transação"* e substituir por *"Phase 3+ — resolver continua
  responsável apenas pela primary currency; rate resolution vive
  em IExchangeRateService."*).

## 8. Dashboard toggle: original vs converted

- 8.1 `dashboard.page.ts` adiciona toggle PrimeNG `p-selectButton`
  com opções `[{ label: 'Convertido', value: 'converted' },
  { label: 'Original', value: 'original' }]`. Default `'converted'`.
  Estado em `WritableSignal<'original' | 'converted'>`.
- 8.2 Backend — extender `TransactionSummaryQuery` e
  `TransactionsByCategoryQuery`:
  - Param `viewMode: 'original' | 'converted'`.
  - `converted`: somar `Amount.Amount * COALESCE(
      ExchangeRateToPrimary, 1.0)` por Kind/Category. Resultado em
    tenant primary.
  - `original`: agrupar por `Amount.Currency` e devolver lista
    `[(Currency, IncomeTotal, ExpenseTotal, Net)]`.
- 8.3 Frontend — render condicional:
  - `converted` mode: cards Entrada/Saída/Líquido com `money`
    pipe na tenant primary; donut por categoria igual ao Phase 2.
  - `original` mode: render N×3 cards (1 linha por currency com
    Entrada/Saída/Líquido); donut **escondido** + chip de aviso
    PrimeNG `<p-message severity="info">Vista por moeda original —
    soma multi-moeda escondida</p-message>` (rationale em
    `requirements.md`: somar moedas diferentes num donut é
    misleading).
- 8.4 Karma test
  `dashboard.page.spec.ts` cobre toggle:
  - Default render mostra cards convertidos.
  - Toggle para `original` esconde donut, mostra warning chip,
    cards por currency.

## 9. Settings UI — mudar primary currency (opt-in IN)

- 9.1 Página `/app/settings/general` (route nova) com Reactive Form:
  - Campo `primaryCurrency` `p-dropdown` populado via
    `currencyApi.listActive()`. Valor inicial =
    `tenantSettings.primaryCurrency`.
  - Submit chama `PUT /api/tenants/me`.
- 9.2 Endpoint `PUT /api/tenants/me` em Identity:
  - Payload `{ primaryCurrency }`.
  - Validate active currency.
  - Update `shared.Tenants.PrimaryCurrency` para tenant ativo
    (`ITenantContext.TenantId`).
  - **Não** re-snapshot historic transactions (decisão registada).
- 9.3 Toast PT-PT *"Moeda principal alterada para {code}.
  Transações antigas mantêm o câmbio gravado no momento."*
- 9.4 Após change, frontend invalida `tenantSettings` signal e
  refresca dashboard. Cards convertidos passam a usar a nova
  primary; transações antigas continuam com o ER que foi gravado
  no insert (lookup contra ECB rate from insert date para o novo
  primary; documentado open question).

## 10. Currency CRUD admin UI (opt-in IN)

- 10.1 Página `/app/admin/currencies` (System Admin role) com
  PrimeNG `p-table`:
  - Colunas: Code, Name, Symbol, MinorUnits, IsActive (toggle).
  - "Nova moeda" abre `p-dialog` com Code (regex ISO 4217),
    Name, Symbol, MinorUnits (default 2), IsActive (default true).
  - "Editar moeda" reusa o dialog (Code read-only depois de
    criada).
- 10.2 Endpoints em Identity:
  - `GET /api/admin/currencies` (lista todas, ativa+inativa).
  - `GET /api/currencies` (público autenticado, retorna apenas
    `IsActive=true` — lookup para forms de Account/Transaction).
  - `POST /api/admin/currencies`, `PUT /api/admin/currencies/{code}`.
  - **Sem `DELETE`** — soft-delete via `IsActive=false`.
- 10.3 Validação: Code 3 maiúsculas; MinorUnits 0..6;
  Code único.

## 11. Tests — Domain unit + Application unit + Architecture + Karma

> Suite Phase 1a/1b/2 continua verde. Phase 3 acrescenta cobertura.

- 11.1 Domain unit em
  `tests/Modules/Financial.Domain.Tests/`:
  - `Transactions/Transaction_ExchangeRateSnapshot_Tests.cs`:
    `Create` com snapshot persiste Rate+At; sem snapshot persiste
    NULL; `Update` não toca em ER (frozen).
  - `Transactions/ExchangeRateSnapshotTests.cs`: VO equality,
    Rate > 0 invariant.
  - `Accounts/AccountCurrencyTests.cs`:
    `Create` rejeita `OpeningBalance.Currency != Currency`.
- 11.2 Domain unit em
  `tests/Sextante.SharedKernel.Tests/CurrencyTests.cs`:
  - Code regex (3 letras maiúsculas).
  - MinorUnits ≥ 0.
- 11.3 Application unit em
  `tests/Modules/Financial.Application.Tests/`:
  - `ExchangeRates/ExchangeRateServiceTests.cs`:
    `ResolveAsync(EUR, USD, ...)` lê linha direta;
    `ResolveAsync(USD, BRL, ...)` calcula cross-rate;
    `ResolveAsync(X, Y, ...)` sem rates lança
    `ExchangeRateUnavailableException`.
  - `Transactions/CreateTransactionHandler_MultiCurrencyTests.cs`:
    USD em conta USD numa tenant EUR persiste ER; BRL em conta BRL
    numa tenant BRL persiste NULL; sem rate disponível retorna
    ProblemDetails PT-PT.
  - `Jobs/EcbSnapshotJobTests.cs` (em Identity.Application.Tests):
    Idempotência (segundo run não duplica). Falha do provider
    grava `LastError`. Sucesso atualiza `LastSuccessAt`.
- 11.4 Architecture
  `tests/Sextante.ArchitectureTests/MultiCurrencyDependencyTests.cs`:
  - `Module.Financial.Application` referencia
    `IExchangeRateService` (próprio); **não** referencia
    `EcbCurrencyProvider` diretamente.
  - `Module.Financial.Domain` referencia `SharedKernel.Currency`
    (string Code apenas, não ExchangeRate).
  - `Module.Financial.*` **não** referencia
    `Module.Identity.Domain.ExchangeRate` (cross-module via
    `IExchangeRateService` / lookup direto à `shared.ExchangeRate`
    pela `Infrastructure` é OK, mas Domain/Application não).
- 11.5 Integration em
  `tests/Sextante.IntegrationTests/Financial/MultiCurrencyTests.cs`:
  - Stub `ICurrencyProvider` com snapshot fixo
    (EUR→USD=1.10, EUR→BRL=6.00, EUR→GBP=0.85).
  - Snapshot job inserta 3 rows em `shared.ExchangeRate`.
  - Tenant com primary=BRL cria conta USD → cria transação $300 →
    persiste `ExchangeRateToPrimary = 6.00 / 1.10 ≈ 5.4545`.
  - Dashboard converted: $300 USD na conta = R$ 1636.36 (±0.01).
  - Dashboard original: card USD com 300 in / 0 out.
  - Mudança de primary BRL→EUR via Settings: dashboard converted
    recalcula usando ER stored × cross-rate to EUR no read-side.
  - Multi-tenancy: tenant B vê os mesmos rates de
    `shared.ExchangeRate` (reference data); tenant B **não** vê
    transações de tenant A.
- 11.6 Karma em `src/Web/Sextante.Web/`:
  - `core/api/currency-api.service.spec.ts` (lookup currencies).
  - `features/dashboard/dashboard.page.spec.ts` (toggle behavior,
    legenda do donut alternada — herdado de Phase 2 — continua
    verde).
  - `features/financial/pages/accounts.page.spec.ts` (currency
    dropdown + opening balance currency match).
  - `features/settings/settings-general.page.spec.ts`.
  - `features/admin/currencies.page.spec.ts`.
  - `features/admin/exchange-rates.page.spec.ts`.
- 11.7 `npm test -- --watch=false --browsers=ChromeHeadless` verde
  local e CI.

## 12. Walkthrough manual — cenário multi-moeda (merge-blocker)

> Reproduzível a partir de `git clone` + `docker compose up`.
> O cenário exato vive em `validation.md`. Em alto nível:
> tenant primary muda EUR→BRL; criadas contas USD e BRL;
> rates ECB triggerados via admin; transações em USD e BRL;
> dashboard converted/original alternados; tabela de
> hand-calculation comparada com cards renderizados.

- 12.1 Documentar passos exatos em `validation.md` § "Manual
  browser walkthrough — multi-currency".
- 12.2 Capturar screenshots em DevTools de 375 / 768 / 1280 px
  para cada nova página (settings general / admin currencies /
  admin exchange-rates / dashboard com toggle) e arquivá-los em
  `specs/2026-04-27-phase-3-multi-currency/screenshots/` (opcional;
  se feito, referenciar em validation.md).

## 13. Multi-tenancy regression

- 13.1 Estender
  `tests/Sextante.IntegrationTests/Financial/MultiTenancyTests.cs`:
  - Account.Currency: tenant A lista as suas contas com currency
    correto; tenant B não vê.
  - Transaction.ExchangeRateToPrimary: tenant A vê os seus ER;
    tenant B não consegue ler nem update (404 não 403).
- 13.2 Confirmar em integration test que `shared.Currency` e
  `shared.ExchangeRate` **não** têm RLS — query como tenant A e
  tenant B retorna o mesmo conjunto de currencies/rates.
- 13.3 Mudança de `Tenant.PrimaryCurrency` por tenant A não afeta
  `Tenant.PrimaryCurrency` de tenant B.

## 14. Responsive sanity check

- 14.1 Verificar em DevTools (375 / 768 / 1280 px) cada página
  nova ou modificada:
  - `/app/dashboard` (toggle + cards split em mobile).
  - `/app/settings/general`.
  - `/app/admin/currencies`.
  - `/app/admin/exchange-rates`.
  - `/app/accounts` (dropdown currency cabe em mobile).
- 14.2 Aplicar regras tech-stack §19.5: dialogs com `breakpoints`,
  tabelas com `overflow-x-auto`, grids `grid-cols-1
  md:grid-cols-N`. Sem horizontal scroll do `<body>`.

## 15. Close-out (Phase 3 não 🛡️)

- 15.1 Skill `changelog` adiciona entrada datada do merge com
  sumário da Phase 3.
- 15.2 Marcar `specs/roadmap.md` Phase 3 checkboxes via conversa
  com o agente (AGENTS.md §2 regra 1, nunca à mão).
- 15.3 Commit `Mark phase 3 as complete`. PR
  `2026-04-27-phase-3-multi-currency → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, npm test).
  - Suite Phase 1a/1b/2 continua verde.
  - Aprovação humana.
- 15.4 Sem sub-agent deep review formal (Phase 3 não 🛡️).
  Opt-in possível focado em snapshot job idempotency + cross-rate
  math + multi-tenancy de `shared.*`; se invocado, output anexado
  ao PR.
