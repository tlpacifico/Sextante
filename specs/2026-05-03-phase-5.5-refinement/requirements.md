# Requirements — Phase 5.5: Refinement (observability, middlewares, design system, dogfooding bugs)

## Goal

Fechar a lacuna entre `tech-stack.md` e a implementação corrente das
Phases 1a–5b **antes** do primeiro deploy à VPS (Phase 6) e antes do
critério MVP de 1 mês de dogfooding (`mission.md` §6). Phase 5.5 não
adiciona módulos novos: hardeniza o que já existe (logs sem PII,
error handling RFC 7807, security headers, Wolverine pipeline,
métricas mínimas), introduz error tracking externo (Sentry) com ADR-012
formalizado, consolida o design system (tokens + componentes shared
+ tema Aura + responsive sanity retroativo), e fecha bugs prioritários
+ UX crítica encontrados no dogfooding inicial (importação Activo Bank,
ecrã dedicado de transações com edição, "lembrar utilizador" no login,
persistência de sessão entre reloads, provisionamento de admin via CLI).

Esta phase concretiza:
- `mission.md` §4.1 (privacidade por defeito → sem PII em logs),
- §4.4 (self-hosted-friendly → fallback graceful do Sentry),
- §4.6 (UI responsiva → sanity retroactivo nas pages 1b–5b),
- `tech-stack.md` §3.5 (pipeline Wolverine + validação),
- §8 (ProblemDetails RFC 7807),
- §13 (Serilog estruturado + TenantId enricher + alerta de tenant
  switch),
- §19.5 (responsive 375/768/1280 px obrigatório por page).

Phase 5.5 é 🛡️ no roadmap → sub-agent deep review obrigatório antes
do merge.

## In scope

### Backend — observability e middlewares

- **Filtros PII no Serilog** (`appsettings.json` + `appsettings.Production.json`):
  - Mascarar emails (`***@***`), valores monetários (em descrições
    livres), descrições de transação (truncar / hash).
  - Implementação via `Destructure.ByTransforming<T>` ou
    `Enrich.WithDynamicProperty` filtrando propriedades por nome
    (`email`, `password`, `description`, `notes`, `amount`).
  - Tech-stack §13.

- **Correlation / traceId propagado**:
  - Injetado em todos os logs de request via
    `LogContext.PushProperty("TraceId", Activity.Current?.TraceId)`.
  - Mesmo `traceId` aparece no `ProblemDetails` (RFC 7807 §3.1
    extension `traceId`) e no evento Sentry.

- **`TenantId` enricher automático**:
  - Middleware que faz `LogContext.PushProperty("TenantId",
    tenantContext.TenantId)` em requests autenticadas.
  - Tech-stack §13 + AGENTS.md §4.

- **Alerta de mudança inesperada de tenant no mesmo request**:
  - Snapshot do `TenantId` no início do request (após auth);
    comparação no fim do pipeline.
  - Se diferente → log `Error` com `TraceId`, `TenantIdInitial`,
    `TenantIdFinal`, `UserId`, `Path`. Não bloqueia o request
    (já é too late) mas dispara Sentry warning.
  - Tech-stack §13.

- **`IExceptionHandler` global** (.NET 10 nativo):
  - Singleton `GlobalExceptionHandler : IExceptionHandler` em
    `BuildingBlocks/Sextante.Infrastructure/ErrorHandling/`.
  - Mapeia exceções conhecidas → `ProblemDetails`:
    - `ValidationException` (FluentValidation) → 400.
    - `UnauthorizedAccessException` → 401 (tenant claim missing).
    - `KeyNotFoundException` / `EntityNotFoundException` → 404.
    - `UniqueConstraintException` (custom) → 409.
    - `ExchangeRateUnavailableException` (Phase 3) → 422.
    - `Exception` (catch-all) → 500.
  - Preenche `type`, `title`, `status`, `detail`, `instance`,
    `traceId`. Mensagens PT-PT.
  - Tech-stack §8 + §16.

- **Migração de todos os endpoints existentes para ProblemDetails**:
  - Auth (`/api/auth/signup`, login responses), Identity manage,
    Financial endpoints (Accounts, Categories, Transactions, Budgets,
    RecurringRules, ImportProfiles, ImportBatches,
    CategorizationRules), Admin endpoints (Hangfire, exchange rates).
  - Substituir `BadRequest(string)` / `NotFound()` ad-hoc por
    `ProblemDetails`. Decisão **locked**.

- **Security headers middleware**:
  - `X-Content-Type-Options: nosniff`.
  - `X-Frame-Options: DENY`.
  - `Referrer-Policy: strict-origin-when-cross-origin`.
  - `Permissions-Policy: camera=(), microphone=(), geolocation=()`.
  - `Strict-Transport-Security` em produção (max-age=31536000;
    includeSubDomains).
  - CORS explícito mesmo same-origin (registar política para
    documentação).

- **Wolverine validation policy**:
  - `FluentValidationPolicy` registada em `WolverineOptions` que
    procura `IValidator<TCommand>` no DI antes do handler correr.
  - Falha → `ValidationException` → ProblemDetails 400 via
    `IExceptionHandler`.
  - Removeção de chamadas manuais a validators dentro de handlers
    existentes (Phases 2/3/4/5a/5b).

- **Wolverine metrics policy mínima**:
  - Middleware Wolverine que conta invocações + duração por handler.
  - Exposto via `ILogger.LogInformation` no momento do close
    (estruturado: `{ HandlerType, DurationMs, Success }`).
  - Sem Prometheus (Phase 7).

### Sentry — error tracking externo

- **Conta + projetos Sentry** (free tier sentry.io):
  - 2 projetos: `sextante-api`, `sextante-web`.
  - DSNs persistidos em `.env.example` (placeholder) e `.env`
    (real — gitignored).

- **ADR-012 — Sentry como error tracker externo** em
  `docs/adr/ADR-012-sentry-error-tracking.md`:
  - Justifica divergência face a "Métricas/OTel pós-MVP" do
    tech-stack §13.
  - Documenta: free tier, DSN config via env, sample rate (100%
    erros / 10% performance), retenção (30 dias free tier),
    PII scrubbing alinhado a Serilog filters, fallback graceful.
  - Atualiza `tech-stack.md` §13 e tabela §17 a refletir Sentry
    como decisão MVP.

- **Backend** (`Sentry.AspNetCore` + `Sentry.Serilog`):
  - Integrado em `Program.cs` do Host.
  - DSN via `.env` (`SENTRY__DSN`).
  - `BeforeSend` callback aplica scrubbing de PII alinhado aos
    filtros Serilog (mesma lista de propriedades).
  - Tag `tenant_id` em cada evento (extraído de
    `ITenantContext`).
  - Sample rate configurável via env
    (`SENTRY__TRACES_SAMPLE_RATE`).

- **Frontend** (`@sentry/angular`):
  - Integrado em `app.config.ts`.
  - DSN via build env (`SENTRY_DSN_WEB` injetado por Angular CLI
    config).
  - Source maps publicados no release (Angular CLI sourceMap=true
    em produção + script `sentry-cli releases files upload`).
  - Privacy mode: mascarar inputs financeiros (`<input
    type="number">` e campos `Amount`, `Limit`).
  - `replaysSessionSampleRate=0` no MVP (custo).

- **Fallback graceful**:
  - Se DSN ausente ou Sentry inacessível → app continua, Serilog
    file sink mantém log local.
  - Mission §4.4 (self-hosted-friendly).

### Frontend — design system e responsividade

- **Design tokens consolidados**:
  - `src/Web/Sextante.Web/src/styles/tokens.css` com CSS custom
    properties: paleta (primary, secondary, neutrals, semantic
    success/warning/danger), espaçamento (escala 4 px), tipografia
    (font-family, sizes, weights, line-heights).
  - Consumidos via `tailwind.config.js` (`theme.extend.colors` /
    `spacing`) e PrimeNG CSS variables (`--p-primary-*`).

- **Componentes shared extraídos** para
  `src/Web/Sextante.Web/src/app/shared/ui/`:
  - `page-header` (título + breadcrumb + actions slot).
  - `empty-state` (ícone + título + descrição + CTA).
  - `confirm-dialog` (substitui ad-hoc dialogs de delete).
  - `data-table-shell` (PrimeNG `p-table` wrapper com paginação,
    overflow horizontal mobile, empty state, loading skeleton).
  - `form-field` (label + control + error message PT-PT).
  - Substituem duplicação ad-hoc nas pages das Phases 1b–5b.

- **Tema PrimeNG Aura customizado**:
  - Cores primárias/secundárias do Sextante (definidas nos
    design tokens).
  - Dark mode opcional via Tailwind `dark:` + PrimeNG
    `.p-dark` toggle.
  - Documentado em `Vault: 04 - Arquitetura - Frontend.md`.

- **Sanity check responsivo retroactivo**:
  - Pages das Phases 1b–5b inspecionadas em DevTools 375 × 667,
    768 × 1024, 1280 × 800: `/login`, `/signup`,
    `/forgot-password`, `/reset-password`, `/app/dashboard`,
    `/app/accounts`, `/app/categories`, `/app/transactions`
    (a criar abaixo), `/app/recurring-rules`, `/app/budgets`,
    `/app/import-profiles`, `/app/import-batches`,
    `/app/categorization-rules`, `/app/import-wizard`.
  - Corrigir overflow horizontal, dialogs cortados, gráficos
    ilegíveis, touch targets < 44 px.

- **Checklist DoD responsivo** adicionado a `AGENTS.md`:
  - Bullet "Responsive sanity check em 375/768/1280 px" como
    requisito merge para qualquer phase futura que toque UI.

### Bugs prioritários e UX crítica

- **Bug Activo Bank — inferir tipo pelo sinal**:
  - `tests/Sextante.IntegrationTests/Data/Import/example-activo-bank.csv`
    formato `Data Lanc.;Data Valor;Descrição;Valor;Saldo` com
    `;` separator e `,` decimal.
  - Hoje todas as linhas → despesa.
  - Corrigir o parser/staging em
    `src/Modules/Financial/Sextante.Modules.Financial.Application/Features/CsvImport/`:
    `Valor > 0` → `TransactionType.Income`; `Valor < 0` →
    `TransactionType.Expense`.
  - Persistir `Amount` em valor absoluto + `TransactionType`
    correto.
  - Adicionar caso de teste em
    `tests/Sextante.IntegrationTests/Financial/CsvImportRealFileTests.cs`
    (ou ficheiro novo `ActivoBankCsvImportTests.cs`) que importa
    o CSV completo e assert mix receita/despesa correto.

- **Ecrã dedicado de transações `/app/transactions`**:
  - Rota lazy-loaded sob `authGuard` em `app.routes.ts`.
  - Standalone component
    `src/Web/Sextante.Web/src/app/features/financial/pages/transactions.page.ts`.
  - Tabela paginada (PrimeNG `p-table` server-side via
    `data-table-shell`):
    - Colunas: Data (sortable), Conta, Categoria, Descrição,
      Valor, Tipo, Ações.
    - Cor por tipo (verde income / vermelho expense).
  - Filtros (Reactive Forms, debounced 300 ms):
    - Intervalo de datas (`p-datepicker range`).
    - Conta (`p-multiselect`).
    - Categoria (`p-multiselect`).
    - Tipo (Income / Expense `p-selectButton`).
    - Texto livre na descrição (`p-inputText`).
    - Intervalo de valor (min/max `p-inputNumber`).
  - Ordenação por qualquer coluna (`sortField`, `sortOrder`).
  - **Edição via dialog** (PrimeNG `Dialog`) com Reactive Form:
    - Campos: Data, Conta, Categoria, Descrição, Valor, Currency,
      Tipo, Notes (todos editáveis exceto `Id`/`TenantId`).
    - PUT para `/api/financial/transactions/{id}` (endpoint a
      adicionar se não existir; verificar Phase 2).
  - **Bulk actions**:
    - Checkbox por linha + checkbox header.
    - Ação "Recategorizar selecionadas" → dialog escolhe nova
      categoria → PATCH em batch.
  - Reusa `data-table-shell` do design system.
  - Sanity check responsivo 375/768/1280 px (filtros colapsam
    em accordion mobile, tabela com overflow horizontal, ações
    em menu kebab em mobile).

- **Backend — endpoint de update de transação**:
  - Verificar se `PUT /api/financial/transactions/{id}` existe
    (Phase 2). Se não, adicionar:
    - `UpdateTransactionCommand` em
      `src/Modules/Financial/.../Application/Features/Transactions/`.
    - Handler valida: tenant fail-loud, account/category
      pertencem ao tenant, currency válida, `OccurredAt` em
      range razoável.
    - Publica `TransactionUpdatedIntegrationEvent` (Phase 2 já
      define) → `BudgetAlertDispatchHandler` recalcula.
  - Endpoint de bulk recategorize:
    - `PATCH /api/financial/transactions/recategorize` body
      `{ ids: [...], categoryId: "..." }` → 200 com count.
    - Publica events em loop (Wolverine batches via outbox).

- **Lembrar utilizador no login**:
  - Persistir `email` em `localStorage` (key
    `sextante.last-login-email`) após signup/login bem sucedido.
  - Pre-fill no `/login` no próximo acesso.
  - Checkbox "Manter-me ligado" (default false) — quando
    selecionado, backend emite refresh token com lifetime
    estendido (configurável; default 30 dias vs 7 dias normal,
    `appsettings.json`).
  - Backend: `/api/auth/login` aceita campo opcional
    `extendedSession: bool` no body; emite refresh token com
    lifetime = `Auth:RefreshTokenLifetimeExtended` em vez do
    default `Auth:RefreshTokenLifetime`.

- **Persistir sessão entre reloads (F5)** — **decisão locked: localStorage**:
  - Access + refresh tokens persistidos em
    `localStorage` (keys `sextante.access`, `sextante.refresh`).
  - Rehidratação no bootstrap do Angular (`appInitializer` factory).
  - Refresh silencioso em bootstrap se access token expirado mas
    refresh válido.
  - Logout limpa ambas as keys + httpOnly cookie via
    `/api/auth/logout`.
  - Sessão termina apenas em logout explícito ou expiração total
    do refresh token.
  - **Trade-off documentado em ADR**: localStorage é vulnerável
    a XSS exfiltration. Aceite porque (a) `mission.md` §2 — MVP
    é single-user; (b) CSP do Angular + Tailwind + PrimeNG
    suficiente; (c) httpOnly cookie continua a ser fonte
    canónica para o backend (rotation + invalidation server-side).

- **Provisionamento de admin via CLI** — **decisão locked: idempotente com reset**:
  - Comando `dotnet run --project src/Bootstrap/Sextante.Host -- create-admin --email <e> --password <p> [--tenant-name <t>]`.
  - Reusa `IServiceScopeFactory` para obter `UserManager`,
    `IIdentityDbContext`, `Wolverine MessageBus`.
  - Lógica:
    1. Procura `User` por email.
    2. Se existe: rotaciona password + garante role `Admin` +
       garante `EmailConfirmed=true` (idempotente — sem erro,
       log info "admin password rotated for {email}").
    3. Se não existe: cria User + Tenant (default name =
       `Admin Tenant` ou flag `--tenant-name`) +
       Membership(Owner) + role Admin + `EmailConfirmed=true`.
       Publica `UserRegisteredIntegrationEvent` para seed de
       categorias.
  - Documentado no README secção "Provisionamento inicial".
  - Roda fora do servidor HTTP (`Host.CreateApplicationBuilder`
    + parsing de args via `System.CommandLine` ou switch
    manual; `args[0] == "create-admin"` → CLI mode, senão
    pipeline normal).
  - Testes:
    - Integration test que invoca o CLI in-process e verifica
      User + Tenant + Membership + role Admin criados, e
      seed de categorias do `Financial` triggered.
    - Idempotência: invocar 2× → password rotated, sem erro.

## Out of scope

- **Open Banking** (Phase 12).
- **OpenTelemetry / Prometheus / Grafana** (Phase 7).
- **Self-hosted Sentry** — locked: SaaS free tier.
- **Email / push notifications de alertas** (Phase 15).
- **i18n EN / PT-BR** (estrutura PT-PT only no MVP).
- **Lighthouse / a11y formal** — `tech-stack.md` §19.5 nota que é
  Phase 6.
- **Storybook** — fora do MVP.
- **Roles UI completa** (Owner/Member/ReadOnly invites) — Phase 18.
  Phase 5.5 só modela `Admin` para o CLI.
- **Refresh token revocation list / device management** — Phase
  pós-MVP.
- **CSV de saída (export)** — Phase 6.
- **Performance benchmarks formais** — Phase 7.

## Decisions

- **Scope = todos os 4 sub-blocos numa só phase**. *Why*: a-coerent
  narrativa "pre-deploy hardening + dogfooding fixes" justifica o
  branch único. Quebrar em 5.5a/5.5b duplicaria PR overhead sem
  ganho — todos os bullets já estavam no roadmap. *How to apply*:
  branch `phase-5.5-refinement` carrega 4 grupos de tarefas no
  `plan.md`.

- **Admin CLI é idempotente — re-execução rotaciona password**.
  *Why*: VPS recovery scenarios (admin password lost ou compromised)
  precisam de um caminho não-destrutivo. Erro em "user already
  exists" obrigaria a manipular Postgres directamente, derrotando
  o propósito. Trade-off: leak da CLI invocation rotaciona prod
  admin password — mitigado por VPS-only access (sem porta SSH
  pública para utilizadores não-admin). *How to apply*:
  `CreateAdminCommandHandler` faz `UserManager.FindByEmailAsync`;
  se found → `RemovePasswordAsync` + `AddPasswordAsync` + ensure
  role; se not found → cria fluxo completo.

- **ProblemDetails é migração full — todos os endpoints existentes**.
  *Why*: superfície inconsistente entre módulos (Identity vs
  Financial) causa fragilidade no frontend (`error-translator.ts`
  precisa de N branches). Padronizar agora é mais barato que durante
  Phase 6. *How to apply*: substituir `BadRequest(...)`,
  `NotFound()`, `Conflict()` ad-hoc por `Results.Problem(...)`
  ou deixar `IExceptionHandler` apanhar. Endpoints CSV import (que
  têm `ImportError` próprio) mantêm shape mas devolvem dentro de
  `ProblemDetails.extensions`.

- **Sentry SaaS free tier (sentry.io)**. *Why*: dogfooding-grade
  observability sem VPS overhead. Self-hosted Sentry custa mais
  RAM que o Postgres no MVP. Free tier (5k errors/mês) sobra para
  1 user. *How to apply*: ADR-012 documenta. Self-hosted permanece
  open option para Phase 7.

- **Session persistence em localStorage** (não sessionStorage).
  *Why*: F5 e fechar/reabrir browser têm de preservar sessão
  (UX dogfooding). sessionStorage perde-se ao fechar a tab. XSS
  risk aceite porque `mission.md` §2 é single-user MVP. *How to
  apply*: `auth.service.ts` write/read em `localStorage`;
  `appInitializer` rehidrata; CSP no `index.html` mitiga
  inline-script XSS.

- **Merge gate = sub-agent deep review + tests + responsive sanity
  + Sentry live + walkthrough**. *Why*: Phase 5.5 é 🛡️ no
  roadmap; é a última phase antes do deploy production em Phase 6.
  Lighthouse a11y é Phase 6 — não duplicar. Dogfooding window
  pertence à Phase 6. *How to apply*: `validation.md` §DoD lista
  bullets concretos; PR description referencia review approval +
  CI green + Sentry inbox screenshot.

- **`TenantId` enricher é middleware — não Serilog enricher
  estático**. *Why*: `LogContext.PushProperty` precisa de scope
  per-request. Serilog enricher estático lê de `IHttpContextAccessor`
  mas falha em background jobs. Middleware claro + `TenantAwareJob<T>`
  já existente cobre os 2 cenários. *How to apply*: middleware
  registado **depois** de `UseAuthentication` em `Program.cs`.

- **Wolverine metrics são logger-based no MVP**. *Why*: Prometheus
  é Phase 7. Logger structured logs com `{HandlerType, DurationMs,
  Success}` permitem grep + Seq se for útil pós-deploy. *How to
  apply*: `WolverineMetricsPolicy` aplica `Logger` middleware com
  `Stopwatch` start/stop.

- **Design tokens em CSS custom properties, não SCSS variables**.
  *Why*: Tailwind v4 + PrimeNG Aura ambos suportam CSS variables
  nativamente. SCSS adiciona build step sem ROI. Dark mode toggle
  é `[data-theme=dark]` em `<html>` flipping vars. *How to apply*:
  `src/styles/tokens.css` import-first em `styles.css`.

- **Bug Activo Bank é fix no parser, não regra de categorização**.
  *Why*: o sinal do `Valor` no CSV é metadata canónica do banco
  (sempre presente). Regras de categorização são UX layer e não
  devem suportar inferência de tipo. *How to apply*: parser
  detecta o sinal antes de aplicar profile mapping; `Amount` =
  `Math.Abs(parsed)`, `TransactionType` = `parsed > 0 ? Income :
  Expense`.

- **Bulk recategorize é endpoint dedicado (não loop client-side)**.
  *Why*: 100 PATCH requests do client são O(N) round-trips +
  N events em vez de 1. Endpoint dedicado garante 1 transação +
  1 batch de events. *How to apply*: `PATCH
  /api/financial/transactions/recategorize` body
  `{ ids, categoryId }`.

- **"Manter-me ligado" estende refresh token apenas — não muda
  access token**. *Why*: access token de 15 min é trade-off de
  segurança (leaked → 15 min de expor). Estender access token
  derrota o propósito. Estender refresh token (default 7 → 30
  dias) suficiente para "não digitar password todos os dias".
  *How to apply*: `Auth:RefreshTokenLifetime` (7 dias) e
  `Auth:RefreshTokenLifetimeExtended` (30 dias) em
  `appsettings.json`. `LoginCommand` aceita flag.

## Context / references

- `specs/roadmap.md` § Phase 5.5 — Refinement. Phase 5.5 fecha 4
  blocos: backend observability, Sentry, frontend design system,
  bugs/UX.
- `specs/mission.md` §2 (single-user MVP — justifica
  localStorage), §4.1 (privacidade — PII filters), §4.4
  (self-hosted-friendly — Sentry fallback), §4.6 (responsive),
  §6 (MVP done = 1 mês dogfooding).
- `specs/tech-stack.md` §3.5 (Wolverine pipeline), §8 (RFC 7807),
  §13 (Serilog + TenantId + tenant switch alert), §17 (decisões
  registadas — atualizar com Sentry), §19.5 (responsive
  invariante).
- `specs/2026-04-26-phase-1a-auth-backend/` — entregou
  `ITenantContext`, `TenantAwareClaimsPrincipalFactory`, RLS
  setup, `DbConnectionInterceptor`. Phase 5.5 adiciona enricher
  + alert no topo.
- `specs/2026-04-26-phase-1b-auth-ui/` — entregou Auth pages
  Reactive Forms + interceptor. Phase 5.5 adiciona localStorage
  persistence + remember email + extended session.
- `specs/2026-04-26-phase-2-financial-core/` — entregou Transaction
  CRUD + events. Phase 5.5 garante endpoint update + bulk
  recategorize + cria página dedicada.
- `specs/2026-04-29-phase-4-csv-import/` — entregou parser CSV.
  Phase 5.5 corrige inferência de tipo Activo Bank.
- `specs/2026-05-02-phase-5b-budgets/` — entregou
  `BudgetAlertDispatchHandler`. Phase 5.5 garante que
  `TransactionUpdatedIntegrationEvent` continua a triggar
  recálculo após edição via novo dialog.
- `docs/adr/ADR-010-cqrs-via-wolverine.md`,
  `docs/adr/ADR-011-frontend-ui-stack.md` — ADRs existentes.
  ADR-012 segue o mesmo formato.
- `tests/Sextante.IntegrationTests/Data/Import/example-activo-bank.csv` —
  fixture do bug.
- `src/Bootstrap/Sextante.Host/Program.cs` — entry point para
  CLI mode + Sentry + middlewares + `IExceptionHandler`.
- `src/BuildingBlocks/Sextante.Infrastructure/` — destino para
  `GlobalExceptionHandler`, `SecurityHeadersMiddleware`,
  `TenantIdEnricherMiddleware`, `TenantSwitchDetectorMiddleware`.
- `src/Web/Sextante.Web/src/styles/` — destino para `tokens.css`.
- `src/Web/Sextante.Web/src/app/shared/ui/` — destino para
  componentes shared (criar pasta).

## Open questions

- **Email scrubbing — full mask vs partial?**: hoje a decisão é
  `***@***`. Útil ter `t***@***.pt` para diagnóstico? Decisão
  para implementação: full mask (`[email]`) mais defensivo,
  alinha com PII princípio §4.1.
- **ProblemDetails `type` URI** — usar `urn:sextante:errors:<code>`
  ou URLs `https://docs.sextante.app/errors/<code>`? Decisão para
  implementação: `urn:sextante:errors:<code>` (sem dependência de
  domínio público no MVP).
- **Tema Aura cores concretas** — definir paleta exacta no
  kickoff de implementação (não no scaffold da spec). Vault tem
  preview palette possível.
- **Sentry release management** — auto via CI ou manual? Decisão
  para implementação: auto via GitHub Actions (`sentry-cli
  releases new $GITHUB_SHA`) no job de deploy Phase 6 — Phase 5.5
  só configura DSN local.
- **Bulk recategorize — limite de items por request?** Decisão
  para implementação: 500 items hard cap (proteção DoS); UI
  agrupa em batches se selection > 500 (improvável no MVP).
- **CLI `--tenant-name` default** — `Admin Tenant` ou prompt
  interactivo? Locked: default literal `Admin Tenant`; flag
  override.
- **Edição inline vs dialog na transactions page** — locked
  dialog (mais simples, suporta validação cross-field, mobile
  ergonomic). Inline (`p-table editing mode`) fica para Phase
  6 ou backlog.
- **Soft hold de 1 semana de dogfooding** — não selecionado
  (gate standard). Se durante dogfooding aparecerem regressões
  em Phase 5.5 features, fica como bug fix em Phase 6.
