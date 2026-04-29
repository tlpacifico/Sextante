# Validation — Phase 3: Multi-moeda + ECB

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **`Currency` (SharedKernel) e `ExchangeRate` + `EcbSnapshotState`
   (Identity.Domain) criadas com migration `AddCurrencyAndExchangeRate`.**
2. **Schema `shared`** ganha tabelas `currencies`, `exchange_rates`,
   `ecb_snapshot_state` **sem RLS**.
3. **Seed ISO 4217 fiat** carregado em `shared.currencies` via
   migration (≥150 rows ativas).
4. **`ICurrencyProvider` + `EcbCurrencyProvider`** com Polly retry +
   timeout, parser XML + DI typed `HttpClient`.
5. **`EcbSnapshotJob` Hangfire recurring `30 0 * * *` UTC** registado
   no Host. Idempotente. Endpoint admin para forçar execução.
6. **Endpoint `POST /api/admin/exchange-rates`** insere taxa manual
   com upsert; `Source='manual'`. Validação FluentValidation.
7. **Endpoint `GET /api/admin/exchange-rates`** retorna últimos 7
   dias × top-5 currencies (default configurável).
8. **`Account.Currency`** novo campo NOT NULL com migration que
   default-fillou rows existentes pelo tenant primary, FK a
   `shared.currencies`.
9. **`Transaction.ExchangeRateToPrimary` + `ExchangeRateAt`** novas
   colunas NULL allowed; rows pré-Phase-3 mantêm-se NULL.
10. **`IExchangeRateService.ResolveAsync`** implementado com
    cross-rate EUR-base; lança
    `ExchangeRateUnavailableException` em miss.
11. **`CreateTransactionHandler`** integra `IExchangeRateService`;
    devolve ProblemDetails PT-PT em rate miss; `UpdateTransactionHandler`
    mantém ER frozen (não recomputa).
12. **`Account.Create`** valida currency contra allowlist ativa e
    invariant `OpeningBalance.Currency == Currency`.
13. **Dashboard toggle PrimeNG `p-selectButton`** entre `converted`
    e `original` modes funcional. Cards e donut adaptam-se.
14. **Settings UI `/app/settings/general`** muda
    `Tenant.PrimaryCurrency` via `PUT /api/tenants/me`. Toast
    PT-PT explica que histórico mantém ER frozen.
15. **Admin Currency CRUD** `/app/admin/currencies` (System Admin).
    `IsActive=false` é o soft-delete.
16. **Admin Exchange Rates UI** `/app/admin/exchange-rates` com
    tabela, dialog manual, botão snapshot, warning chip de
    `LastError`.
17. **Domain unit tests verdes** (Currency, ExchangeRateSnapshot,
    Transaction com snapshot, Account com currency).
18. **Application unit tests verdes** (`ExchangeRateService`,
    `EcbSnapshotJob` idempotência, `CreateTransactionHandler`
    multi-moeda).
19. **Integration tests verdes**:
    - `MultiCurrencyTests.cs` cobrindo cenário completo
      (snapshot → conta USD → transação $300 → ER persistido →
      dashboard converted matches).
    - Multi-tenancy: tenant A's primary change não afeta tenant B;
      `shared.Currency`/`shared.ExchangeRate` partilhadas (sem RLS);
      `Account.Currency` e `Transaction.Exchange*` isolados por
      tenant.
20. **Architecture tests** verdes:
    - `Module.Financial.Application` referencia `IExchangeRateService`
      e **não** `EcbCurrencyProvider`.
    - `Module.Financial.Domain` referencia `SharedKernel.Currency`
      apenas.
    - `shared.currencies` e `shared.exchange_rates` sem RLS
      (test SQL).
21. **Karma unit tests verdes** (currency-api,
    dashboard.page com toggle, accounts.page, settings-general,
    admin-currencies, admin-exchange-rates).
22. **Suite Phase 1a/1b/2 continua verde** (sem regressão).
23. **Manual browser walkthrough multi-moeda completo** per
    secção abaixo (merge-blocker per AskUserQuestion).
24. **Sanity check responsivo** em 375 / 768 / 1280 px nas
    páginas novas/modificadas (`/app/dashboard`,
    `/app/settings/general`, `/app/admin/currencies`,
    `/app/admin/exchange-rates`, `/app/accounts`).
25. **`specs/roadmap.md` Phase 3 checkboxes ticados** via conversa
    com o agente (AGENTS.md §2 regra 1, nunca à mão).
26. **`CHANGELOG.md`** com nova entrada datada produzida pela skill
    `changelog`.
27. **GitHub Actions CI verde** — build .NET, test .NET, build
    Angular, `npm test` todos passados.
28. **Comment inline em `TenantCurrencyResolver.cs:11`** atualizado
    para refletir a decisão de manter o resolver com responsabilidade
    "primary only".

## How to verify each bullet

1. **Currency / ExchangeRate / EcbSnapshotState.**
   - `git diff main..2026-04-27-phase-3-multi-currency
     src/BuildingBlocks/SharedKernel/Currency.cs
     src/Modules/Identity/.../Domain/Entities/ExchangeRate.cs
     src/Modules/Identity/.../Domain/Entities/EcbSnapshotState.cs`
     mostra os 3 ficheiros novos.
   - `dotnet build -c Release` verde.

2. **Schema sem RLS.**
   - psql:
     ```sql
     SELECT relname, relrowsecurity, relforcerowsecurity
       FROM pg_class
       WHERE relnamespace = (SELECT oid FROM pg_namespace
                             WHERE nspname = 'shared')
         AND relname IN ('currencies', 'exchange_rates',
                          'ecb_snapshot_state');
     -- Esperado: relrowsecurity=false em todas as 3.
     ```

3. **Seed ISO 4217.**
   - psql: `SELECT count(*) FROM shared.currencies WHERE
     is_active = true;` → ≥150.
   - psql: `SELECT code, name, symbol FROM shared.currencies
     WHERE code IN ('EUR', 'USD', 'BRL', 'JPY') ORDER BY code;`
     mostra as 4 com nomes e símbolos corretos.

4. **`EcbCurrencyProvider`.**
   - `dotnet test --filter
     FullyQualifiedName~EcbCurrencyProviderTests` verde
     (cobre parse XML, retry, timeout — usar
     `MockHttpMessageHandler`).
   - Inspeção visual: `Program.cs` regista
     `services.AddHttpClient<EcbCurrencyProvider>(...)` com
     Polly policy.

5. **`EcbSnapshotJob` recurring + manual trigger.**
   - Hangfire dashboard
     (`http://localhost/api/admin/hangfire`) lista
     `ecb-snapshot` em "Recurring Jobs" com cron `30 0 * * *`.
   - `POST /api/admin/exchange-rates/snapshot/run` retorna 202;
     em <5s `shared.exchange_rates` ganha rows novos.
   - `dotnet test --filter
     FullyQualifiedName~EcbSnapshotJobTests` verde.

6. **Manual rate insert.**
   - `dotnet test --filter
     FullyQualifiedName~ManualExchangeRateInsertTests` verde
     (cobre upsert, validation, role check).
   - psql:
     ```sql
     SELECT rate, source FROM shared.exchange_rates
       WHERE rate_date = '<test-date>' AND to_currency = 'USD'
       ORDER BY updated_at DESC LIMIT 1;
     -- Esperado: source='manual' após o admin POST.
     ```

7. **List exchange rates.**
   - `curl -H "Authorization: Bearer <admin>" \
     http://localhost/api/admin/exchange-rates` retorna JSON
     com últimos 7 dias × top-5 currencies (≤35 rows típicos).

8. **`Account.Currency`.**
   - psql:
     ```sql
     \d financial.accounts
     -- Esperado: column currency varchar(3) NOT NULL.
     SELECT count(*) FROM financial.accounts WHERE currency = '';
     -- Esperado: 0 (default-fill aplicado).
     ```
   - `dotnet test --filter
     FullyQualifiedName~AccountCurrencyTests` verde.

9. **`Transaction.ExchangeRateToPrimary` + `ExchangeRateAt`.**
   - psql:
     ```sql
     \d financial.transactions
     -- Esperado: columns exchange_rate_to_primary numeric(20,8)
     --           NULL, exchange_rate_at timestamptz NULL.
     ```

10. **`IExchangeRateService.ResolveAsync`.**
    - `dotnet test --filter
      FullyQualifiedName~ExchangeRateServiceTests` verde
      (cobre `EUR→X`, `X→EUR`, `X→Y` cross-rate, miss → exception).

11. **Handlers integrados.**
    - `dotnet test --filter
      FullyQualifiedName~CreateTransactionHandler_MultiCurrencyTests`
      verde.
    - Endpoint: criar transação com Currency desconhecido para
      a data → 400 ProblemDetails PT-PT (verificar mensagem
      exata).

12. **Account.Create invariants.**
    - `dotnet test --filter
      FullyQualifiedName~AccountCurrencyTests` verde.

13. **Dashboard toggle.**
    - Walkthrough manual passos 8-9 passam.
    - Karma: `dashboard.page.spec.ts` verde
      (`npm test -- --watch=false --browsers=ChromeHeadless`).

14. **Settings primary change.**
    - Walkthrough passo 2 passa.
    - Karma `settings-general.page.spec.ts` verde.
    - psql:
      ```sql
      SELECT primary_currency FROM shared.tenants
        WHERE id = '<test-tenant-id>';
      -- Esperado: 'BRL' após a mudança.
      ```

15. **Currency CRUD admin.**
    - Walkthrough passo (opcional admin) — criar e desativar
      currency de teste.
    - Karma `admin-currencies.page.spec.ts` verde.

16. **Exchange Rates admin UI.**
    - Walkthrough passo 5 passa (botão "Forçar snapshot").
    - Walkthrough passo (opcional) — inserir taxa manual e
      confirmar source='manual'.
    - Karma `admin-exchange-rates.page.spec.ts` verde.

17. **Domain unit tests.**
    - `dotnet test
      tests/Modules/Financial.Domain.Tests/` verde
      (inclui `ExchangeRateSnapshotTests`,
      `Transaction_ExchangeRateSnapshot_Tests`,
      `AccountCurrencyTests`).
    - `dotnet test
      tests/Sextante.SharedKernel.Tests/CurrencyTests.cs` verde.

18. **Application unit tests.**
    - `dotnet test
      tests/Modules/Financial.Application.Tests/` verde.
    - `dotnet test
      tests/Modules/Identity.Application.Tests/` verde
      (`EcbSnapshotJobTests`).

19. **Integration tests.**
    - `dotnet test --filter
      FullyQualifiedName~Sextante.IntegrationTests.Financial.MultiCurrencyTests`
      verde.
    - `dotnet test --filter
      FullyQualifiedName~MultiTenancyTests` verde (cobertura
      Phase-3 estendida — Account.Currency, Transaction.Exchange*,
      Tenant.PrimaryCurrency).

20. **Architecture tests.**
    - `dotnet test --filter
      FullyQualifiedName~MultiCurrencyDependencyTests` verde.
    - `dotnet test --filter
      FullyQualifiedName~SharedSchemaRlsTests` verde
      (test que valida `pg_class.relrowsecurity = false` para
      `currencies`/`exchange_rates`).

21. **Karma unit tests.**
    - `cd src/Web/Sextante.Web && npm test -- --watch=false
      --browsers=ChromeHeadless` verde.

22. **Sem regressão.**
    - `dotnet test -c Release` (suite completa) verde.
    - Suite Phase 1a (Identity), 1b (refresh-cookie),
      2 (Financial CRUD + multi-tenancy) sem falhas novas.

23. **Walkthrough.**
    - Executar todos os passos da secção abaixo num ambiente
      limpo (`docker compose down -v && docker compose up`).

24. **Sanity responsivo.**
    - DevTools → device toolbar → ciclar entre 375 × 667,
      768 × 1024, 1280 × 800. Para cada viewport:
      - `/app/dashboard`: toggle visível e clicável; cards
        empilham 1 coluna em mobile (em `original` mode com
        2-3 currencies, 1 linha por currency); chip de aviso
        em `original` mode legível em mobile.
      - `/app/settings/general`: dropdown ocupa largura
        completa em mobile.
      - `/app/admin/currencies`: tabela com `overflow-x-auto`,
        dialog 95vw em 375 px.
      - `/app/admin/exchange-rates`: idem; warning chip de
        `LastError` legível.
      - `/app/accounts`: dropdown currency cabe em mobile;
        opening balance currency ajusta-se.
    - Sem horizontal scroll do `<body>` em qualquer viewport.

25. **Roadmap.**
    - `git diff main..2026-04-27-phase-3-multi-currency --
      specs/roadmap.md` mostra Phase 3 com `[x]` em cada bullet.

26. **Changelog.**
    - `git diff main..2026-04-27-phase-3-multi-currency --
      CHANGELOG.md` mostra secção nova com data do merge
      sumarizando Phase 3.

27. **CI verde.**
    - GitHub Actions no commit mais recente do
      `2026-04-27-phase-3-multi-currency` mostra todos os jobs
      verdes.

28. **Comment atualizado.**
    - `git diff
      src/Modules/Financial/.../Persistence/TenantCurrencyResolver.cs`
      mostra atualização do comment inline para refletir a
      decisão.

## Manual browser walkthrough — multi-currency

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais por valores de teste.

```bash
# Pré-requisito limpo:
docker compose down -v && docker compose up
# Aplicação em http://localhost/. Migrations correm no startup
# (cria shared.currencies + shared.exchange_rates + financial
# columns novas).
```

### Cenário: tenant primário muda EUR → BRL; transações em USD e BRL

1. **Signup tenant.** Abrir `http://localhost/` → "Criar conta" →
   submeter `email=tester+phase3@example.com`,
   `password=Phase3Password!`, `tenantName=Phase 3`. Esperado:
   toast "Conta criada" → login → land em `/app/dashboard` (vazio).
   Tenant criado com `PrimaryCurrency = 'EUR'` (default).

2. **Mudar primary EUR → BRL.** User menu → Settings →
   `/app/settings/general`. Dropdown "Moeda principal" muda de
   EUR para **BRL**. Submit. Esperado: toast PT-PT *"Moeda
   principal alterada para BRL. Transações antigas mantêm o
   câmbio gravado no momento."* `Tenant.PrimaryCurrency = 'BRL'`
   no DB.

3. **Forçar snapshot ECB.** User menu → Admin → "Taxas de
   câmbio". Botão "Forçar snapshot ECB". Esperado: toast
   "Snapshot iniciado"; em ≤10s a tabela popula com hoje × top-5
   currencies; warning chip de `LastError` ausente.

   - Anotar a row (Date=hoje, To=USD, Rate=R) e (Date=hoje,
     To=BRL, Rate=Rb). **Cross-rate USD→BRL = (1/R) × Rb**.
   - Exemplo (valores ilustrativos): `EUR→USD = 1.10`,
     `EUR→BRL = 6.00` ⇒ `USD→BRL = 6.00/1.10 ≈ 5.4545`.

4. **Criar conta USD.** `/app/accounts` → "Nova conta":
   `Name=USD Wallet`, `Type=Cash`, `Currency=USD`,
   `OpeningBalance=$1000.00`. Toast.

5. **Criar conta BRL.** "Nova conta":
   `Name=BRL Conta`, `Type=Checking`, `Currency=BRL`,
   `OpeningBalance=R$5000.00`. Toast.

6. **Criar transação USD.** `/app/dashboard` → "Nova transação":
   Account=USD Wallet (currency prefilled USD), Category=Salário,
   `Amount=$300.00`, `OccurredAt=hoje`, Description="Freelance
   USD". Submit. Esperado: toast; tabela ganha linha; backend
   persistiu `ExchangeRateToPrimary = ~5.4545` e
   `ExchangeRateAt = now`.

   Verificação opcional via psql:
   ```sql
   SELECT amount, currency, exchange_rate_to_primary,
          exchange_rate_at
     FROM financial.transactions
     WHERE description = 'Freelance USD';
   ```

7. **Criar transação BRL.** "Nova transação": Account=BRL Conta
   (currency prefilled BRL), Category=Alimentação,
   `Amount=R$200.00`, OccurredAt=hoje, Description="Mercado".
   Submit. Esperado: toast; tabela atualiza; backend persistiu
   `ExchangeRateToPrimary = NULL` (BRL == primary).

8. **Dashboard `converted` mode (default).** Esperado:
   - **Cards**:
     - Entrada = R$ (300 × 5.4545) = R$ 1636.36 (≈, dependente
       do snapshot real).
     - Saída = R$ 200.00.
     - Líquido = R$ 1436.36.
   - **Donut por categoria** (despesas): 1 fatia "Alimentação
     R$ 200.00".
   - **Tabela**: 2 linhas, com coluna "Valor" formatada na moeda
     **original** (cada linha mostra `$300.00 USD` e `R$200.00 BRL`).

   **Comparação com hand-calculation table:**

   | Currency | Tipo | Valor original | ER snapshot | Valor convertido (BRL) |
   |----------|------|----------------|-------------|------------------------|
   | USD | Income | $300.00 | 5.4545 | R$ 1636.36 |
   | BRL | Expense | R$ 200.00 | NULL (=1.0) | R$ 200.00 |

   Esperado: cards no UI batem com a coluna "Valor convertido
   (BRL)" da tabela ±0.01.

9. **Dashboard `original` mode.** Toggle → "Original". Esperado:
   - Donut **escondido**; chip de aviso `<p-message
     severity="info">Vista por moeda original — soma multi-moeda
     escondida</p-message>` visível.
   - **2 linhas de cards** (1 por currency):
     - Linha USD: Entrada $300, Saída $0, Líquido $300.
     - Linha BRL: Entrada R$0, Saída R$200, Líquido -R$200.
   - Tabela inalterada.

10. **Inserir taxa manual (admin) — OPCIONAL.** Voltar a
    `/app/admin/exchange-rates`. "Inserir taxa manual":
    Date=ontem, Currency=USD, Rate=1.05. Toast. Tabela mostra
    a row com `source='manual'`. Linha do snapshot ECB para
    USD/ontem é sobreescrita.

11. **Logout + signup tenant B (multi-tenancy).** Logout →
    `/login` → "Criar conta" → `email=tester+phase3-b@example.com`,
    `password=Phase3Password!`, `tenantName=Phase 3 B`. Login.
    Esperado:
    - `/app/dashboard` vazio.
    - `/app/accounts` vazio.
    - Form "Nova conta": dropdown currency mostra **as mesmas
      currencies** que tenant A vê (reference data partilhada).
    - `/app/settings/general` mostra `PrimaryCurrency = EUR`
      (default); a mudança de A para BRL **não** afetou B.
    - Network tab: response de
      `GET /api/financial/transactions` é `[]`.
    - Network tab: response de `GET /api/currencies` lista
      ≥150 currencies, igual ao tenant A.

12. **Inner-loop sanity (psql, opcional).**
    ```sql
    SET app.current_tenant_id = '<tenant-A-uuid>';
    SELECT count(*) FROM financial.transactions;  -- 2
    SELECT primary_currency FROM shared.tenants
      WHERE id = '<tenant-A-uuid>';               -- 'BRL'

    SET app.current_tenant_id = '<tenant-B-uuid>';
    SELECT count(*) FROM financial.transactions;  -- 0
    SELECT primary_currency FROM shared.tenants
      WHERE id = '<tenant-B-uuid>';               -- 'EUR'

    -- Reference data partilhada (sem RLS):
    RESET app.current_tenant_id;
    SELECT count(*) FROM shared.currencies WHERE is_active;  -- ≥150
    SELECT count(*) FROM shared.exchange_rates
      WHERE rate_date = current_date;                         -- ≥30
    ```

## Out of scope for validation

- **Crypto rates** (BTC/ETH). Phase 8.
- **ECB-down end-to-end recovery walkthrough.** Per AskUserQuestion.
  Comportamento coberto por unit test do `EcbSnapshotJob`.
- **Live ECB call em CI nightly.** Per AskUserQuestion.
- **Sub-agent deep review formal.** Phase 3 não 🛡️. Opt-in se
  invocado, output anexado ao PR.
- **Performance / Lighthouse.** Pós-MVP.
- **Stress test com >10k transações multi-moeda.** Pós-MVP.
- **Re-snapshot histórico quando primary muda.** Decisão: rates
  são frozen.
- **Multi-currency reports formais (P&L por currency).** Apenas
  dashboard com toggle.
- **Backfill de Phase 2 transactions com `ER=1.0`.** NULL é
  "1.0 implied" e read-side faz COALESCE.
- **Símbolo display alternativo** (R$ vs BRL etc.).
  `Intl.NumberFormat('pt-PT', { style: 'currency' })` cobre o
  caso normal.
- **MinorUnits aplicado em arithmetic.** Apenas display.
- **Audit responsivo formal** (Lighthouse mobile, screen-reader).
  Sanity check 3-viewport apenas.
- **CSP / security headers tuning.** Phase 6.
- **Backups específicos / restore-de-teste.** Phase 6.
- **Browser cross-version testing.** Chromium-based suficiente.
- **OpenAPI snapshot diffing.** Out (consistente com Phase 2).
- **Currency CRUD admin: testes UX exaustivos** além de criar/editar
  /toggle IsActive — apenas o flow básico está em scope.
- **JOD/KWD/BHD (3 minor units) walkthrough.** Sanity opcional;
  se feito, anexar ao PR como bonus.
