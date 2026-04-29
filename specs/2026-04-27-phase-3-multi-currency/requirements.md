# Requirements — Phase 3: Multi-moeda + ECB

## Goal

Entregar a primeira capacidade real de multi-moeda do Sextante.
Phase 2 deixou o `Money` value object pronto em SharedKernel mas
nunca foi exercitado com moedas diferentes da primary do tenant —
o invariant Phase-2 "`Amount.Currency` == tenant primary" garantia
isso. Phase 3 levanta essa restrição e introduz o stack que torna
multi-moeda seguro:

- `Currency` (ISO 4217 fiat seedada por migration) e `ExchangeRate`
  (snapshot diário) em schema `shared`.
- Provider ECB + job Hangfire diário às 00:30 UTC que persiste o
  snapshot.
- Inserção manual de taxa quando o ECB falha (defesa em profundidade
  do princípio `mission.md` §4 invariante 4 — degradação graciosa).
- `Account.Currency` (cada conta tem a sua moeda fixa).
- `Transaction.ExchangeRateToPrimary` + `ExchangeRateAt` capturados
  em insert-time — congelados, não recomputados em reads.
- Dashboard com toggle "moeda original" vs "convertido para moeda
  principal".
- UI Settings para mudar `Tenant.PrimaryCurrency`.
- UI admin para CRUD de Currency.

Esta phase concretiza a proposta de valor 4 da mission
(`mission.md` §3 — "lida com várias moedas sem perder
rastreabilidade") e o princípio §4.2 ("multi-moeda nativa, não
bolt-on; storage carrega a moeda original; reporting converte"), e
cobre os 7 bullets do `roadmap.md` § Phase 3 mais 2 opt-ins
(Settings UI + Currency CRUD admin) escolhidos via AskUserQuestion.

A Saída do roadmap — *"registar transação em USD numa conta EUR
persiste o câmbio do momento; dashboard mostra totais consolidados
na moeda principal do tenant"* — é validada no walkthrough manual
(merge-blocker per AskUserQuestion).

## In scope

- **`Currency` em `SharedKernel`** com `Code (varchar 3 PK)`, `Name`,
  `Symbol`, `MinorUnits (int)`, `IsActive (bool)`, audit. **Sem
  `DeletedAt`**: soft-delete de reference data via `IsActive=false`.
  Seed ISO 4217 fiat (≈170 rows) em migration. **Sem crypto**
  (Phase 8).
- **`ExchangeRate` em `Identity.Domain`** com `Id (Guid v7)`,
  `RateDate (date UTC)`, `FromCurrency = 'EUR'`,
  `ToCurrency`, `Rate (decimal(20,8))`,
  `Source ('ECB' | 'manual')`, audit, unique
  `(RateDate, FromCurrency, ToCurrency)`. **Sem RLS** (reference
  data partilhada).
- **`EcbSnapshotState`** single-row em `shared` com
  `LastRunAt`, `LastSuccessAt`, `LastError`.
- **`ICurrencyProvider`** abstrato em `Sextante.Infrastructure` +
  `EcbCurrencyProvider` que parseia
  `eurofxref-daily.xml` da ECB. Polly retry 3× / backoff 2s /
  timeout 10s.
- **`EcbSnapshotJob`** Hangfire recurring `30 0 * * *` UTC.
  Idempotente. Regista failure em `EcbSnapshotState.LastError`.
  **Não** wrapped em `TenantAwareJob<T>` (rates são globais).
- **Endpoint admin `POST /api/admin/exchange-rates`** para inserir
  taxa manual (upsert; manual sobreescreve ECB).
- **Endpoint admin `POST /api/admin/exchange-rates/snapshot/run`**
  para forçar snapshot ECB (usado pelo walkthrough).
- **Página admin `/app/admin/exchange-rates`** com tabela últimos
  7 dias × top-5 currencies, dialog "Inserir taxa manual", botão
  "Forçar snapshot ECB", warning chip de `LastError`.
- **`Account.Currency`** novo campo (`varchar(3) NOT NULL`).
  Migration `AddAccountCurrency` faz default-fill por tenant
  primary. Form Reactive inclui dropdown currency. Invariant
  `OpeningBalance.Currency == Account.Currency`.
- **`Transaction.ExchangeRateToPrimary` + `ExchangeRateAt`**
  (NULL allowed). Migration `AddExchangeRateToTransaction`. Sem
  backfill de Phase 2 rows (per opt-in OUT — NULL = "1.0
  implied").
- **Domain VO `ExchangeRateSnapshot(Rate, At)`**. Domain remove o
  invariant Phase-2 "`Amount.Currency` == tenant primary"; a
  restrição passa para Application com o snapshot enrichment.
- **`IExchangeRateService.ResolveAsync(from, to, at, ct)`** em
  Application; resolve direta (`EUR→X`), inversa (`X→EUR`) ou
  cross-rate via EUR-base (`X→Y`). Lança
  `ExchangeRateUnavailableException` em miss.
- **`CreateTransactionHandler`** chama `IExchangeRateService` antes
  de criar a Transaction. ProblemDetails PT-PT *"Não há taxa de
  câmbio disponível para {from} → {to} em {date}. Insira
  manualmente em /app/admin/exchange-rates."*
- **`UpdateTransactionHandler`** **não** re-resolve rate (frozen).
- **Dashboard toggle PrimeNG `p-selectButton`**:
  - `converted` (default): cards Entrada/Saída/Líquido em primary,
    donut por categoria — render Phase 2 inalterado em comportamento
    visual.
  - `original`: N×3 cards por currency; donut **escondido** com
    chip de aviso *"Vista por moeda original — soma multi-moeda
    escondida"*.
- **`TransactionSummaryQuery` e `TransactionsByCategoryQuery`** ganham
  `viewMode` parameter; em `converted` somam usando
  `COALESCE(ExchangeRateToPrimary, 1.0)`.
- **Settings UI** `/app/settings/general` muda
  `Tenant.PrimaryCurrency`. Endpoint `PUT /api/tenants/me`. Não
  re-snapshot transações antigas (frozen).
- **Currency CRUD admin** `/app/admin/currencies` (System Admin role).
  Endpoints `GET /api/admin/currencies`, `POST`, `PUT`. `DELETE`
  via `IsActive=false`. Endpoint público autenticado
  `GET /api/currencies` (lookup ativas para forms).
- **Walkthrough manual multi-moeda** documentado em
  `validation.md` (merge-blocker per AskUserQuestion).
- **Multi-tenancy regression** estendida ao módulo Identity
  (`shared.Currency`/`shared.ExchangeRate` partilhados sem RLS;
  primary currency change isolada por tenant) e ao módulo
  Financial (`Account.Currency`, `Transaction.Exchange*`).
- **Architecture tests** novos:
  - `Module.Financial.Application` referencia
    `IExchangeRateService` (próprio); **não** `EcbCurrencyProvider`.
  - `Module.Financial.Domain` referencia `SharedKernel.Currency`
    apenas (sem `Identity.Domain.ExchangeRate`).
- **Domain unit tests** (Currency, ExchangeRateSnapshot,
  Transaction com snapshot, Account com currency).
- **Application unit tests** (`IExchangeRateService` resolução
  direta/inversa/cross-rate; `EcbSnapshotJob` idempotência;
  `CreateTransactionHandler` multi-moeda).
- **Integration tests** stubbed-provider em
  `MultiCurrencyTests.cs` cobrindo o cenário completo.
- **Karma unit tests** para currency-api, dashboard toggle,
  accounts page com dropdown currency, settings-general,
  admin-currencies, admin-exchange-rates.
- **Responsive sanity check** per `tech-stack.md` §19.5 nas
  páginas novas/modificadas (375 / 768 / 1280 px).
- **`ITenantCurrencyResolver` shape**: continua responsável apenas
  por `GetPrimaryCurrencyAsync`. Comment inline em
  `TenantCurrencyResolver.cs:11` atualizado para refletir que
  rate resolution vive em `IExchangeRateService` (não no
  resolver).
- **OpenAPI auto-gen** estendido (sem snapshot checked-in,
  consistente com Phase 2).

## Out of scope

- **Crypto rates (BTC, ETH, etc.)**. Provider de cotações cripto
  fica para Phase 8 (ADR-008 pendente). Schema preparado para
  expandir (Currency.Code aceita 3 chars; ECB rates ficam em
  `Source='ECB'`, crypto pode ficar `Source='<provider>'`
  posteriormente).
- **Backfill de transações pré-Phase-3 com `ExchangeRateToPrimary
  = 1.0`**. Linhas existentes ficam NULL e o read-side trata-as
  como `COALESCE(..., 1.0)`. Documentado abaixo em "Decisions".
- **Intraday rates / on-demand fetch**. Apenas snapshot diário
  às 00:30 UTC. Se um user inserir transação USD às 12:00 com
  primary BRL, lookup é da rate de hoje (00:30 UTC snapshot).
  Histórico anterior usa snapshot do dia da transação.
- **Re-snapshot de transações antigas quando Tenant.PrimaryCurrency
  muda**. Rates são frozen em insert. Mudança de primary leva a
  que o read-side recompute via cross-rate (EUR-base é canónico,
  então qualquer primary é derivável do mesmo `shared.ExchangeRate`).
  Histórico fica intacto.
- **ECB-down recovery integration test**. Per AskUserQuestion. O
  comportamento é coberto por unit test do `EcbSnapshotJob`
  (provider lança → `LastError` preenchido) e pela existência do
  endpoint manual fallback. Sem cenário end-to-end no walkthrough.
- **Live ECB call em CI nightly**. Per AskUserQuestion. Stub é
  suficiente; contract drift detetado em primeiro run real
  pós-merge.
- **Sub-agent deep review formal**. Per AskUserQuestion (Phase 3
  não 🛡️). Opt-in livre; output anexado ao PR se invocado.
- **Currency.IsActive=false em runtime: hide nas listas de
  Account/Transaction forms** mas **não** soft-archive das
  contas/transações que já usam a moeda. Histórico mantém-se
  legível.
- **Multi-currency reports (P&L, cash-flow segregados por
  currency)**. Apenas dashboard com toggle. Reports formais
  pós-MVP.
- **Tenant primary currency change com rollback transacional**.
  É uma mudança simples ao Tenant; sem migração de dados; sem
  necessidade de saga.
- **Symbol display localization** (ex.: BRL como "R$" vs "BRL")
  além do que `Intl.NumberFormat('pt-PT', { style: 'currency',
  currency })` já faz. Symbol em `shared.Currency` é informativo
  (admin UI), não usado para formatting.
- **`MinorUnits` em arithmetic**. Storage continua
  `decimal(20,8)`; `MinorUnits` afeta apenas display rounding em
  componentes Angular que escolham aplicá-lo. Domain não conhece
  `MinorUnits`.
- **Hangfire dashboard restrição extra** (já está em
  `[Authorize(Roles="SystemAdmin")]` desde Phase 1a).
- **Migration que substitua `OpeningBalance` para preservar
  histórico de saldo cross-currency**. Account.OpeningBalance
  fica na Account.Currency; sem conversão.
- **Performance benchmark do snapshot job** ou da query
  cross-rate em ≥10k transações. Sanity-check apenas.
- **Audit responsivo formal** (Lighthouse mobile, screen-reader).
  Sanity check em 3 viewports apenas.
- **CSP / security headers tweaks**. Phase 6.
- **Backups específicos para `shared.exchange_rates`**. Já incluído
  em `pg_dump` por schema (Phase 6).

## Decisions

- **Rate model: EUR-base canonical.** *Why*: ECB publica
  EUR-base nativamente; uma só linha por (`Date, ToCurrency`)
  cobre todo o fan-out de tenants com primaries diferentes via
  cross-rate. Per AskUserQuestion. **How to apply**:
  `shared.ExchangeRate.FromCurrency = 'EUR'` sempre. Conversão
  `X→Y` em runtime: `(1 / EUR→X) × EUR→Y`. Casos especiais:
  `from=EUR` → `EUR→to`; `to=EUR` → `1 / EUR→from`.

- **`Currency` vive em `SharedKernel`, não em `Identity.Domain`.**
  *Why*: `Currency` é primitiva de domínio referenciada por dois
  módulos (Identity para `Tenant.PrimaryCurrency`, Financial para
  `Account.Currency`/`Transaction.Amount.Currency`). Em
  `SharedKernel` é referenciável sem violar §3.1. **How to
  apply**: `src/BuildingBlocks/SharedKernel/Currency.cs`. Tabela
  `shared.currencies` é gerida pelo Identity (porque o schema
  `shared` é Identity-owned), mas o tipo de domínio é
  cross-module.

- **`ExchangeRate` vive em `Identity.Domain`.** *Why*: tabela
  `shared.exchange_rates` é gerida pela mesma migration runner
  que `shared.tenants`/`shared.currencies` (`IdentityDbContext`).
  Não há razão para o tipo migrar para SharedKernel — Financial
  acede via `IExchangeRateService` (abstração) ou via raw query
  ao `shared.exchange_rates` (no `Infrastructure`, OK por §3.1
  porque Financial.Infrastructure pode tocar `shared` directamente
  via SQL).

- **`Transaction.ExchangeRateToPrimary` NULL é "1.0 implied".**
  *Why*: rows pré-Phase-3 não têm rate gravado. Backfill é OUT
  (per AskUserQuestion). Read-side faz
  `COALESCE(ExchangeRateToPrimary, 1.0)`. **How to apply**:
  migration `AddExchangeRateToTransaction` adiciona colunas como
  NULL allowed. Handler de criação só preenche se
  `Amount.Currency != tenantPrimary`. Dashboard query usa
  `COALESCE` em todos os SUM.

- **Rate é frozen em insert; tenant primary change não
  re-snapshot.** *Why*: o ER capturado é "qual era o câmbio
  no momento" — propriedade histórica imutável. Recompute em
  reads é via EUR-base canónico (que é estável; ECB não republica
  histórico). **How to apply**: `UpdateTransactionHandler` não
  toca em ER; documentar em
  `Transaction_ExchangeRateSnapshot_Tests.cs`. Settings handler
  (mudança de primary) não dispara re-snapshot — read-side
  recalcula via cross-rate de EUR-base do dia da transação para
  o novo primary.

- **`Currency.IsActive=false` substitui `DeletedAt` para reference
  data.** *Why*: soft-delete uniforme (`tech-stack.md` §7.4)
  aplica-se a entidades de negócio. Currency é reference data
  cross-tenant (não é "do tenant"); fazer soft-delete via flag
  é mais idiomático e mantém a row consultável para histórico.
  **How to apply**: `Currency` não implementa `IAuditable`
  com `DeletedAt`; tem audit (`CreatedAt`/`UpdatedAt`/`Version`)
  e `IsActive`. CRUD admin alterna o flag.

- **Dashboard `original` mode esconde o donut.** *Why*: somar EUR
  + USD + BRL num donut por categoria é matematicamente sem
  sentido — o utilizador vê "30 + 50" sem saber a moeda. Cards
  separados por currency são honestos sobre a fragmentação;
  donut só faria sentido se houvesse uma "currency virtual de
  total" (= primary), que é exatamente o que o `converted` mode
  cobre. **How to apply**: em `original`, render `<p-message
  severity="info">Vista por moeda original — soma multi-moeda
  escondida</p-message>` no lugar do donut.

- **`EcbSnapshotJob` não é `TenantAwareJob<T>`.** *Why*:
  `tech-stack.md` §10 prescreve o wrapper para jobs que tocam
  dados de tenant. Snapshots ECB são globais — vivem em
  `shared.exchange_rates` sem RLS. **How to apply**: registo
  Hangfire normal sem o wrapper.

- **`shared.Currency` e `shared.ExchangeRate` sem RLS.** *Why*:
  reference data partilhada cross-tenant (todos os tenants veem
  a mesma rate USD→EUR de hoje). Aplicar RLS aqui forçaria
  N cópias por tenant sem benefício. **How to apply**: migration
  cria as tabelas sem `ENABLE ROW LEVEL SECURITY`. Architecture
  test confirma.

- **Account.Currency é fixo após criação.** *Why*: mudar a
  currency da conta retroativamente quebra coerência com
  `OpeningBalance` e com transações já criadas. Mesmo princípio
  que `OpeningBalance` (Phase 2). **How to apply**: factory
  `Account.Create` aceita `currency`; sem método `ChangeCurrency`.
  Form de "Editar conta" tem o campo currency read-only após
  criação.

- **Manual rate insertion é admin-only (System Admin role).**
  *Why*: rate manual é fallback operacional em edge case (ECB
  down, gap em fim-de-semana antes do snapshot da segunda).
  Não é fluxo de utilizador final. **How to apply**: endpoint
  com `Authorize(Roles="SystemAdmin")`. Nada na UI do tenant
  comum aponta para `/app/admin/exchange-rates`.

- **Páginas Phase 3 são mobile-first.** *Why*: `mission.md` §4.6
  e `tech-stack.md` §19.5 declaram responsive como invariante.
  Phase 3 introduz 3 páginas novas e modifica o dashboard;
  retrofit posterior é mais caro. **How to apply**: utilities
  Tailwind sem prefixo + `md:` para densidades maiores; dialogs
  com `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`;
  tabelas com `overflow-x-auto`; toggle do dashboard
  (`p-selectButton`) ocupa linha completa em mobile, alinha à
  direita em desktop.

- **`ITenantCurrencyResolver` mantém-se "primary only".** *Why*:
  o comment em `TenantCurrencyResolver.cs:11` antecipava
  *"Phase 3 substitui por uma variante que aceita override por
  transação"*, mas durante o design do plan ficou claro que
  rate resolution é responsabilidade do `IExchangeRateService`
  (com cache, dependências distintas, error handling próprio).
  Misturar no resolver complicaria o lifetime scoping do cache.
  **How to apply**: atualizar comment para apontar para
  `IExchangeRateService`; manter API do resolver inalterada.

## Context / references

- `specs/roadmap.md` — secção "Phase 3 — Multi-moeda + ECB".
- `specs/mission.md` — §3 (proposta de valor 4: multi-moeda
  nativa), §4 princípio 2 (multi-moeda nativa, não bolt-on),
  §4 princípio 4 (self-hosted-friendly: fallback / degradação
  graciosa), §6 (definition of done MVP).
- `specs/tech-stack.md` — §1 (stack — Hangfire, ECB), §3 (regras
  de dependência), §3.5 (Wolverine — não usado no job ECB),
  §4 (multi-tenancy — `shared.exchange_rates` sem RLS),
  §5 (schema `shared`), §7.1 (Money VO — sem mudanças),
  §9 (multi-moeda — esta phase é a implementação),
  §10 (Hangfire wrappers — `EcbSnapshotJob` não é
  `TenantAwareJob<T>`), §17 (decisões — snapshot diário, ECB
  fallback manual).
- `specs/2026-04-26-phase-2-financial-core/` — entregou
  `Money` VO em SharedKernel, `Tenant.PrimaryCurrency`,
  `Account.OpeningBalance: Money`, `Transaction.Amount: Money`,
  `TenantCurrencyResolver` (comment inline antecipando esta
  phase). Phase 3 reusa todos; **não** modifica specs anteriores.
- Memória do agente — `memory/replanning_2026_04_26.md`
  (Wolverine + PrimeNG/Tailwind/Signals + Angular 21 LTS),
  `memory/project_name.md` (Sextante).
- ADR-008 (provider de cotações) — Phase 8. ECB fiat-only nesta
  phase **não** preempta crypto/equities; ADR-008 ficará para
  decidir Brapi (BR) + Yahoo Finance/Alpha Vantage (intl).
- `src/BuildingBlocks/SharedKernel/Money.cs` — VO completo;
  reusar sem mudanças.
- `src/Modules/Identity/.../Domain/Entities/Tenant.cs:15` —
  `PrimaryCurrency` já existente desde Phase 2.
- `src/Modules/Financial/.../Persistence/TenantCurrencyResolver.cs:11`
  — comment a atualizar (decisão registada).
- `src/Modules/Financial/.../Domain/Transactions/Transaction.cs`
  — extender factory + Update path.
- `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml`
  — feed XML da ECB (gratuito, sem API key, atualizado por dia
  útil às 16:00 CET).

## Open questions

- **Tenant primary change com transações em moedas que não eram
  primary**: dashboard `converted` recomputa usando cross-rate
  EUR-base do dia da transação para o novo primary — confirmação
  na implementação que isto produz números intuitivos para o
  utilizador (ex.: tenant tinha primary EUR, fez transação USD,
  muda para BRL → cards convertidos passam de EUR para BRL via
  cross-rate). Se o utilizador estranhar, considerar fixar a
  moeda de display do dashboard à `Tenant.PrimaryCurrency` no
  momento da query (já é assim) e exibir a primary anterior em
  hover/tooltip.
- **Manual rate vs snapshot ECB do mesmo dia**: decisão é que
  manual sobreescreve ECB (último upsert ganha). Se ECB rodar
  às 00:30 UTC e admin inserir manual às 11:00, a manual fica
  até ao próximo snapshot. Confirmar em walkthrough que o
  comportamento corresponde à expectativa.
- **`EcbSnapshotState.LastError` em produção** (visível na admin
  UI): formatar com timestamp e ofuscar PII (nada deve haver,
  mas defensivamente). Decidir em implementação se mostrar a
  stack trace completa ou só `ex.Message`.
- **Lookup performance**: `IExchangeRateService.ResolveAsync`
  faz 1-2 queries por transação criada. Para insert em massa
  (Phase 4 — Importação CSV) considerar cache scoped à
  ImportBatch. Phase 3 não otimiza — apenas correctness.
- **Lista de currencies "top-5 default"** para a UI admin
  (`appsettings.json` → `ExchangeRates:DefaultDisplayCurrencies`):
  proposta inicial `["USD", "GBP", "BRL", "JPY", "CHF"]`.
  Confirmar.
- **`Currency.MinorUnits`** — display rounding aplicado por
  componente Angular (`p-inputNumber` `maxFractionDigits`)
  ou central no `money` pipe? Decidir em implementação;
  preferência é central no pipe.
- **JOD / KWD / BHD** (3 minor units) — `Intl.NumberFormat` em
  pt-PT lida bem? Sanity check em walkthrough opcional para uma
  delas se houver tempo; senão deferir.
- **`shared.exchange_rates` sem RLS**: confirmar via integration
  test que tenant role (sem BYPASSRLS) consegue `SELECT` na
  tabela mesmo sem `app.current_tenant_id` setado. Se PostgreSQL
  rejeitar (porque o role app não tem privilégios para tabelas
  em que RLS está desativado), considerar `GRANT SELECT ON
  shared.exchange_rates TO app_role;` na migration.
