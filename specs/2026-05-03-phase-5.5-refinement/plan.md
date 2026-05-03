# Plan — Phase 5.5: Refinement

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 5.5 entrega 4 blocos: (1) backend observability
> + middlewares + ProblemDetails + Wolverine pipeline; (2) Sentry +
> ADR-012 + tech-stack update; (3) frontend design system + tokens +
> shared UI + tema Aura + responsive sanity retroactivo; (4) bugs
> prioritários e UX crítica (Activo Bank parser, /transactions page,
> remember email, session F5, admin CLI).
>
> Phase 5.5 é 🛡️ no roadmap → sub-agent deep review obrigatório.

---

## 1. ADR-012 + tech-stack update (escrever **antes** de tocar código)

### 1.1 Criar `docs/adr/ADR-012-sentry-error-tracking.md`
Mesmo formato de ADR-010 / ADR-011. Secções: Context, Decision,
Consequences, Alternatives considered. Conteúdo:
- Context: tech-stack §13 originalmente diferiu OTel/Prom para Phase
  7. Dogfooding pré-deploy precisa de error tracking externo para
  apanhar exceções 5xx que escapam ao file sink.
- Decision: Sentry SaaS (sentry.io) free tier, 2 projetos
  (`sextante-api`, `sextante-web`).
- Consequences: dependência externa em SaaS (mitigado por fallback
  graceful), free tier 5k errors/mês suficiente para MVP, source
  maps publicados no release (CI Phase 6).
- Alternatives: self-hosted Sentry (RAM overhead), Seq (sem source
  maps frontend), apenas Serilog file (sem alertas).

### 1.2 Atualizar `specs/tech-stack.md`
- §13: adicionar bullet "Sentry como error tracker externo
  (sentry.io free tier, ADR-012). Fallback graceful: Serilog file
  sink mantém log local se Sentry inacessível."
- §17 tabela: nova linha
  `| Error tracking externo | Sentry SaaS free tier (sentry.io) |`.
- §18 ADRs: marcar ADR-012 como escrito.

### 1.3 Criar conta + projetos Sentry
- Manual step: criar org, criar projetos `sextante-api` (.NET) e
  `sextante-web` (Angular). Copiar DSNs.
- Persistir DSNs em `.env.example` (placeholder
  `<change-me-sentry-dsn-api>`) e `.env` (real, gitignored).

---

## 2. Backend — observability e middlewares

### 2.1 Filtros PII no Serilog
- `src/Bootstrap/Sextante.Host/appsettings.json` + `appsettings.Production.json`:
  - `Serilog.Filter` block com `ByExcluding` em propriedades de
    risco.
  - Implementar `PiiScrubbingEnricher : ILogEventEnricher` em
    `src/BuildingBlocks/Sextante.Infrastructure/Logging/PiiScrubbingEnricher.cs`:
    - Lista de propriedades sensíveis configurável via
      `Logging:PiiProperties` (`email`, `password`, `description`,
      `notes`, `amount`, `limitAmount`).
    - Substitui valor por `[redacted]` ou `[email]` conforme tipo.
- Registar em `Program.cs`:
  ```csharp
  builder.Host.UseSerilog((ctx, cfg) => cfg
      .ReadFrom.Configuration(ctx.Configuration)
      .Enrich.With<PiiScrubbingEnricher>()
      .Enrich.With<TenantIdEnricher>()
      .Enrich.FromLogContext());
  ```
- Tech-stack §13.

### 2.2 Correlation / TraceId enricher
- `src/BuildingBlocks/Sextante.Infrastructure/Logging/TraceIdMiddleware.cs`:
  - Usa `Activity.Current?.TraceId.ToString()` como TraceId.
  - `using (LogContext.PushProperty("TraceId", traceId)) { await
      _next(ctx); }`.
- Registar antes de `UseAuthentication`.

### 2.3 TenantId enricher (middleware)
- `src/BuildingBlocks/Sextante.Infrastructure/Logging/TenantIdEnricherMiddleware.cs`:
  - Resolve `ITenantContext` (scoped).
  - Se autenticado → `LogContext.PushProperty("TenantId",
    ctx.TenantId)`.
  - Skip se `[AllowAnonymous]` (não tem claim).
- Registar **depois** de `UseAuthentication`/`UseAuthorization`,
  antes de `MapEndpoints`.

### 2.4 Tenant-switch detector
- `src/BuildingBlocks/Sextante.Infrastructure/Logging/TenantSwitchDetectorMiddleware.cs`:
  - Snapshot do `tenantContext.TenantId` no início.
  - Após `await _next(ctx)`: re-resolve `ITenantContext` (scoped)
    e compara. Se diferente:
    ```csharp
    _logger.LogError(
        "Tenant switched mid-request: {TenantInitial} → {TenantFinal}, user={UserId}, path={Path}",
        initial, final, userId, path);
    SentrySdk.CaptureMessage(...);
    ```
  - Não bloqueia (logging-only); resposta já foi enviada.
- Registar como wrapper imediatamente depois do TenantId enricher.

### 2.5 GlobalExceptionHandler (`IExceptionHandler` .NET 10)
- `src/BuildingBlocks/Sextante.Infrastructure/ErrorHandling/GlobalExceptionHandler.cs`:
  - Implementa `IExceptionHandler.TryHandleAsync`.
  - Switch sobre tipos:
    - `FluentValidation.ValidationException` → 400, errors mapped
      em `ProblemDetails.Errors` extension.
    - `UnauthorizedAccessException` → 401.
    - `EntityNotFoundException` (custom em SharedKernel) → 404.
    - `Sextante.Modules.Financial.Domain.UniqueConstraintException`
      → 409.
    - `ExchangeRateUnavailableException` (Phase 3) → 422.
    - default → 500 com generic message PT-PT.
  - Preenche:
    ```csharp
    var problem = new ProblemDetails {
        Type = $"urn:sextante:errors:{code}",
        Title = titlePtPt,
        Status = statusCode,
        Detail = ex.Message,
        Instance = ctx.Request.Path
    };
    problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString();
    ```
  - Captura em Sentry para 5xx (não para 4xx).
- Registar `services.AddExceptionHandler<GlobalExceptionHandler>();`
  + `services.AddProblemDetails();` em `Program.cs`.
- Pipeline: `app.UseExceptionHandler();` no início (depois de
  TraceId middleware).

### 2.6 Migrar endpoints existentes para ProblemDetails
- Inspecionar e ajustar:
  - `src/Modules/Identity/Sextante.Modules.Identity.Api/AuthEndpoints.cs`
    (signup custom, etc.).
  - `src/Modules/Financial/Sextante.Modules.Financial.Api/Accounts/AccountsEndpoints.cs`.
  - `.../Categories/CategoriesEndpoints.cs`.
  - `.../Transactions/TransactionsEndpoints.cs`.
  - `.../Budgets/BudgetEndpoints.cs`.
  - `.../RecurringRules/RecurringRulesEndpoints.cs`.
  - `.../ImportProfiles/...`, `.../ImportBatches/...`,
    `.../CategorizationRules/...`.
- Substituir `Results.BadRequest("...")` → `Results.Problem(...)`
  ou deixar `IExceptionHandler` apanhar exceptions de domínio.
- CSV import endpoints (que retornam `ImportError[]`): manter
  shape mas envolver em `ProblemDetails.Extensions["importErrors"]`
  quando 4xx.

### 2.7 Security headers middleware
- `src/BuildingBlocks/Sextante.Infrastructure/Security/SecurityHeadersMiddleware.cs`:
  - Headers fixos: `X-Content-Type-Options`, `X-Frame-Options`,
    `Referrer-Policy`, `Permissions-Policy`.
  - HSTS apenas se `app.Environment.IsProduction()`.
- Registar **antes** de `UseStaticFiles` em `Program.cs`.
- CORS explícito: `services.AddCors(o => o.AddDefaultPolicy(p => p
  .WithOrigins("https://<vps-domain>", "http://localhost")
  .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));`
  Domain configurável via `Cors:AllowedOrigins[]` em settings.

### 2.8 Wolverine validation policy
- `src/BuildingBlocks/Sextante.Infrastructure/Wolverine/FluentValidationPolicy.cs`:
  ```csharp
  public class FluentValidationPolicy : IChainPolicy {
      public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IContainer container) {
          foreach (var chain in chains) {
              var msgType = chain.MessageType;
              var validatorType = typeof(IValidator<>).MakeGenericType(msgType);
              if (container.Model.HasRegistrationFor(validatorType)) {
                  chain.Middleware.Insert(0, new ValidationFrame(msgType, validatorType));
              }
          }
      }
  }
  ```
- `ValidationFrame` chama validator e lança `ValidationException`
  se falha.
- Registar em `Program.cs`:
  ```csharp
  builder.Host.UseWolverine(opts => {
      opts.Policies.Add<FluentValidationPolicy>();
      opts.Policies.Add<MetricsPolicy>();
  });
  ```
- Remover chamadas manuais a validators dentro de handlers
  existentes (Phases 2/3/4/5a/5b) — grep
  `await _validator.ValidateAsync` ou `IValidator<TCommand>` em
  ctors → eliminar.

### 2.9 Wolverine metrics policy mínima
- `src/BuildingBlocks/Sextante.Infrastructure/Wolverine/MetricsPolicy.cs`:
  - `IChainPolicy` que aplica `MetricsFrame` (Stopwatch.Start/Stop)
    em todos os chains.
  - Log estruturado:
    `_logger.LogInformation("Wolverine handler {HandlerType} completed in {DurationMs}ms (success={Success})", ...)`.
- Sem Prometheus (Phase 7).

### 2.10 Backend integration tests para middlewares
- `tests/Sextante.IntegrationTests/Infrastructure/`:
  - `ProblemDetailsTests.cs`:
    - 400 com `ValidationException` → body com `type`, `title`,
      `status`, `detail`, `traceId`, `errors`.
    - 404 → mesma estrutura.
    - 500 (forced via test endpoint) → genéric PT-PT, `traceId`
      presente, sem stack trace exposto em produção.
  - `SecurityHeadersTests.cs`:
    - GET `/api/health` → headers presentes.
    - HSTS apenas em produção.
  - `PiiScrubbingTests.cs` (xUnit + Serilog memory sink):
    - Log com `email=foo@bar.com` → output contém `[email]` ou
      `[redacted]`, não a string original.
  - `TenantSwitchDetectorTests.cs`:
    - Endpoint malicioso que mude `ITenantContext` (via
      `IServiceScope` direct) → log Error capturado.

---

## 3. Sentry — backend + frontend integration

### 3.1 Backend — `Sentry.AspNetCore` + `Sentry.Serilog`
- Adicionar a `Directory.Packages.props`:
  ```xml
  <PackageVersion Include="Sentry.AspNetCore" Version="<latest>" />
  <PackageVersion Include="Sentry.Serilog" Version="<latest>" />
  ```
- `Sextante.Host.csproj` → `<PackageReference Include="Sentry.AspNetCore" />`.
- `Program.cs`:
  ```csharp
  builder.WebHost.UseSentry(o => {
      o.Dsn = builder.Configuration["Sentry:Dsn"];
      o.TracesSampleRate = builder.Configuration.GetValue<double>("Sentry:TracesSampleRate", 0.1);
      o.SendDefaultPii = false;
      o.BeforeSend = evt => SentryPiiScrubber.Scrub(evt);
      o.Environment = builder.Environment.EnvironmentName;
  });
  ```
- `SentryPiiScrubber` em `BuildingBlocks/Sextante.Infrastructure/Sentry/`:
  - Reusa lista de `PiiProperties` do Serilog enricher (DRY).
  - Faz scrub em `evt.Extra`, `evt.Tags`, `evt.Message`,
    `evt.Request.Data`.
- Tag `tenant_id`: enricher / `SentryEventProcessor` que lê
  `ITenantContext` do scope e adiciona
  `evt.Tags["tenant_id"] = ctx.TenantId.ToString()`.
- Settings: `appsettings.json` adiciona block `Sentry:` com `Dsn`
  vazio (override por env `SENTRY__DSN`).

### 3.2 Backend — fallback graceful
- Se `Sentry:Dsn` ausente ou vazio → `UseSentry` é skip
  (validação no `Program.cs`).
- Test: arrancar app sem `SENTRY__DSN` → endpoints respondem
  normalmente, no log "Sentry disabled (DSN missing)".

### 3.3 Frontend — `@sentry/angular`
- `src/Web/Sextante.Web/package.json` → `@sentry/angular`.
- `src/Web/Sextante.Web/src/environments/environment.ts` +
  `environment.production.ts`: campo `sentryDsn` (placeholder /
  injetado por build env).
- `src/Web/Sextante.Web/src/main.ts` ou `app.config.ts`:
  ```ts
  if (environment.sentryDsn) {
    Sentry.init({
      dsn: environment.sentryDsn,
      environment: environment.production ? 'production' : 'development',
      tracesSampleRate: 0.1,
      replaysSessionSampleRate: 0,
      beforeSend(event) {
        // scrub Amount/Limit inputs from breadcrumbs
        return scrubFinancialFields(event);
      }
    });
  }
  ```
- `Sentry.TraceService` no providers do `app.config.ts`.
- ErrorHandler: `{ provide: ErrorHandler, useValue: Sentry.createErrorHandler() }`.
- TraceService: `{ provide: TraceService, deps: [Router] }`.
- Privacy: tag `class="sentry-mask"` em campos `Amount`, `Limit`,
  `Notes`, `Description` na transactions/budgets/recurring forms.

### 3.4 Frontend — source maps
- `angular.json` → `production` config: `sourceMap.scripts: true`,
  `sourceMap.hidden: true` (não expostos no JS final).
- Script em `package.json`:
  `"sentry:upload-sourcemaps": "sentry-cli sourcemaps upload --release=$RELEASE dist/Sextante.Web/browser"`.
- Documentar em README — execução em CI Phase 6.

### 3.5 Frontend — fallback graceful
- Se `environment.sentryDsn` vazio → `Sentry.init` skipped, app
  funciona normalmente. Test em Karma com `environment` mock.

### 3.6 Sentry forced-event smoke test
- Backend: endpoint `/api/admin/sentry-test` (apenas
  `app.Environment.IsDevelopment()` ou `[Authorize(Roles="SystemAdmin")]`)
  que faz `throw new Exception("Sentry test")`.
- Frontend: botão hidden em `/app/dashboard?debug=sentry` que
  faz `throw`.
- Walkthrough manual confirma evento aparece no inbox Sentry.

---

## 4. Frontend — design system

### 4.1 Design tokens
- Criar `src/Web/Sextante.Web/src/styles/tokens.css`:
  ```css
  :root {
    --color-primary-50: ...;
    --color-primary-500: ...;
    --color-primary-700: ...;
    --color-success: ...;
    --color-warning: ...;
    --color-danger: ...;
    --color-neutral-0: #fff;
    --color-neutral-900: #0f172a;

    --space-1: 0.25rem;
    --space-2: 0.5rem;
    /* … escala 4 px */

    --font-sans: 'Inter', system-ui, sans-serif;
    --font-size-xs: 0.75rem;
    --font-size-sm: 0.875rem;
    --font-size-base: 1rem;
    /* … */

    --radius-sm: 4px;
    --radius-md: 8px;
    --radius-lg: 12px;
  }

  [data-theme='dark'] {
    --color-neutral-0: #0f172a;
    --color-neutral-900: #fff;
    /* dark overrides */
  }
  ```
- Import no topo de `src/styles.css` antes de Tailwind / PrimeNG.

### 4.2 Tailwind theme extend
- `src/Web/Sextante.Web/tailwind.config.js`:
  ```js
  theme: {
    extend: {
      colors: {
        primary: {
          50: 'var(--color-primary-50)',
          500: 'var(--color-primary-500)',
          /* … */
        },
        success: 'var(--color-success)',
        warning: 'var(--color-warning)',
        danger: 'var(--color-danger)',
      },
      fontFamily: { sans: ['var(--font-sans)'] },
    }
  }
  ```

### 4.3 PrimeNG Aura customizado
- Substituir tema atual por Aura preset.
- `app.config.ts` ou `styles.css`:
  ```ts
  import Aura from '@primeng/themes/aura';
  // …
  providePrimeNG({
    theme: { preset: definePreset(Aura, {
      semantic: {
        primary: { 50: 'var(--color-primary-50)', /* … */ }
      }
    }) }
  })
  ```
- Dark mode toggle: `theme.service.ts` (já existe) flipa
  `[data-theme]` em `<html>`.

### 4.4 Componentes shared
Criar `src/Web/Sextante.Web/src/app/shared/ui/`:
- `page-header/page-header.component.ts`:
  - Inputs: `title: string`, `description?: string`,
    `breadcrumb?: { label: string, route?: string }[]`.
  - Slot `<ng-content select="[actions]">`.
  - Standalone, signal-driven.
- `empty-state/empty-state.component.ts`:
  - Inputs: `icon: string` (PrimeIcons class), `title: string`,
    `description: string`, `ctaLabel?: string`, `ctaRoute?: string`.
- `confirm-dialog/confirm-dialog.component.ts`:
  - Wrapper PrimeNG `p-confirmDialog` com strings PT-PT default.
  - Inputs: `header`, `message`, `severity` (`danger`/`warning`).
- `data-table-shell/data-table-shell.component.ts`:
  - Wrapper PrimeNG `p-table` com:
    - `loading` signal.
    - `empty` signal + `emptyTitle`/`emptyDescription` inputs.
    - `[scrollable]="true"` em mobile (matchMedia).
    - Footer com paginator.
    - `<ng-content select="[columns]">` para definir colunas.
- `form-field/form-field.component.ts`:
  - Wrapper de `<label> + content + error message`.
  - Input: `label: string`, `errorKey?: string`,
    `control: AbstractControl`.
  - Resolve mensagem PT-PT por errorKey via dictionary
    (`required`, `email`, `min`, `max`, `pattern`, etc.).

### 4.5 Refactor pages existentes para usar componentes shared
- Pages a tocar (substituir duplicação ad-hoc):
  - `auth/pages/login.page.ts`, `signup.page.ts`,
    `forgot-password.page.ts`, `reset-password.page.ts` —
    `form-field` + `page-header` (signup/login podem manter
    layout dedicado).
  - `features/financial/pages/accounts.page.ts`,
    `categories.page.ts`, `recurring-rules.page.ts`,
    `budgets.page.ts`, `categorization-rules.page.ts`,
    `import-profiles.page.ts`, `import-batches.page.ts` —
    `page-header` + `data-table-shell` + `empty-state`.
  - `features/dashboard/dashboard.page.ts` — `page-header`.
  - `features/financial/pages/import-wizard.page.ts` —
    `page-header`.
- Substituir `ConfirmService` ad-hoc por `confirm-dialog`.

### 4.6 Sanity check responsivo retroactivo
Para cada page abaixo, abrir em DevTools 375 × 667, 768 × 1024,
1280 × 800. Corrigir overflow horizontal, dialogs cortados,
gráficos ilegíveis, touch targets < 44 px, tipografia que parte:
- `/login`, `/signup`, `/forgot-password`, `/reset-password`.
- `/app/dashboard` (cards + chart donut + budget cards strip +
  banner).
- `/app/accounts`, `/app/categories`,
  `/app/recurring-rules`, `/app/budgets`.
- `/app/transactions` (criada em §6 abaixo — novo).
- `/app/categorization-rules`, `/app/import-profiles`,
  `/app/import-batches`, `/app/import-wizard`.
- Dialogs: `budget.dialog`, `recurring-rule.dialog`,
  `upcoming-occurrences.dialog`, `confirm-dialog`, novo
  `transaction-edit.dialog` (§6.3).
- Documentar achados em `specs/2026-05-03-phase-5.5-refinement/responsive-audit.md`
  (page → viewport → issue → fix).

### 4.7 Atualizar AGENTS.md com checklist DoD responsivo
- `AGENTS.md` (raiz) ou `tech-stack.md` §19.5: bullet
  "Sanity check responsivo 375/768/1280 px é DoD merge para
  qualquer phase futura que toque UI". Já está em §19.5 — reforçar
  em AGENTS.md como rule dura.

---

## 5. Backend — endpoints novos / atualizados

### 5.1 `PUT /api/financial/transactions/{id}` (verificar / criar)
- Verificar Phase 2 já entrega; se não, criar:
  - `src/Modules/Financial/.../Application/Features/Transactions/UpdateTransactionCommand.cs`:
    - Fields: `Id`, `AccountId`, `CategoryId`, `OccurredAt`,
      `Amount`, `Currency`, `TransactionType`, `Description?`,
      `Notes?`, `Tags?`.
  - `UpdateTransactionHandler`:
    - Tenant fail-loud.
    - Valida Account/Category pertencem ao tenant.
    - Aplica `Transaction.Update(...)` (criar se não existir).
    - Publica `TransactionUpdatedIntegrationEvent` (Phase 2 já
      definido).
  - `UpdateTransactionValidator` FluentValidation.
  - Endpoint em `TransactionsEndpoints.cs`.

### 5.2 `PATCH /api/financial/transactions/recategorize`
- `RecategorizeTransactionsCommand(Guid[] Ids, Guid CategoryId)`:
  - Hard cap 500 ids (validator).
  - Handler:
    - Carrega transactions filtradas por `tenant_id` + `Ids`.
    - Atualiza `CategoryId` em batch + audit (`UpdatedAt`).
    - Publica 1 `TransactionsRecategorizedIntegrationEvent` com
      `(Ids, OldCategoryIds, NewCategoryId)` ou N
      `TransactionUpdatedIntegrationEvent` (mais simples — reusa
      Phase 5b dispatch). Decisão para implementação: N events,
      delivered no outbox numa Tx → Wolverine batches.
- Endpoint:
  - `app.MapPatch("/api/financial/transactions/recategorize", ...)`
    `[Authorize]`.
  - 200 com `{ updatedCount: int }`.

### 5.3 Auth — extended session refresh
- `appsettings.json`: `Auth:RefreshTokenLifetime: "7.00:00:00"`,
  `Auth:RefreshTokenLifetimeExtended: "30.00:00:00"`.
- `LoginCommand` (verificar Phase 1a; estender):
  - Aceita `extendedSession: bool = false`.
  - Emite refresh token com lifetime correspondente.
- Endpoint `/api/auth/login` aceita campo no body.
- Frontend `auth.service.ts` envia flag.

### 5.4 Auth — `/api/auth/logout`
- Verificar Phase 1a se já existe; se não, criar:
  - Invalida refresh token server-side (DELETE da row em
    `shared.refresh_tokens` ou Identity equivalent).
  - Limpa httpOnly cookie via `Response.Cookies.Delete`.
- Frontend chama em `auth.service.logout()`.

### 5.5 Backend — CLI `create-admin`
- `src/Bootstrap/Sextante.Host/Cli/CreateAdminCommand.cs`:
  ```csharp
  public sealed class CreateAdminCommand {
      public static async Task<int> RunAsync(string[] args, IHost host) {
          var email = ParseFlag(args, "--email");
          var password = ParseFlag(args, "--password");
          var tenantName = ParseFlag(args, "--tenant-name") ?? "Admin Tenant";

          using var scope = host.Services.CreateScope();
          var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
          var dbCtx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
          var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

          var existing = await users.FindByEmailAsync(email);
          if (existing is not null) {
              await users.RemovePasswordAsync(existing);
              await users.AddPasswordAsync(existing, password);
              if (!await users.IsInRoleAsync(existing, "Admin")) {
                  await users.AddToRoleAsync(existing, "Admin");
              }
              if (!existing.EmailConfirmed) {
                  existing.EmailConfirmed = true;
                  await users.UpdateAsync(existing);
              }
              Console.WriteLine($"Admin {email} updated (password rotated, role ensured).");
              return 0;
          }

          // Create new
          var user = new AppUser { Email = email, UserName = email, EmailConfirmed = true };
          await users.CreateAsync(user, password);
          await users.AddToRoleAsync(user, "Admin");
          var tenant = Tenant.Create(tenantName);
          dbCtx.Tenants.Add(tenant);
          dbCtx.Memberships.Add(Membership.Create(user.Id, tenant.Id, MembershipRole.Owner));
          await dbCtx.SaveChangesAsync();
          await bus.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, tenant.Id, user.Email));
          Console.WriteLine($"Admin {email} created with tenant {tenantName}.");
          return 0;
      }
  }
  ```
- `Program.cs`:
  ```csharp
  if (args.Length > 0 && args[0] == "create-admin") {
      var host = builder.Build();
      return await CreateAdminCommand.RunAsync(args, host);
  }
  // … senão pipeline normal
  ```
- README → secção "Provisionamento inicial" com exemplo:
  ```bash
  docker compose run --rm api dotnet Sextante.Host.dll create-admin \
    --email admin@example.com --password '<strong>'
  ```

### 5.6 Backend — testes
- `tests/Sextante.IntegrationTests/Auth/`:
  - `ExtendedSessionTests.cs`: login com flag → refresh token
    cookie tem `Max-Age` ≈ 30 dias; sem flag → ≈ 7 dias.
  - `LogoutTests.cs`: POST logout → refresh token invalidado;
    GET protegido subsequente → 401.
- `tests/Sextante.IntegrationTests/Auth/CreateAdminCliTests.cs`:
  - Run CLI in-process com email novo → User + Tenant +
    Membership + role Admin + EmailConfirmed=true criados; seed
    de categorias triggered.
  - Run CLI 2× com mesmo email → password rotated, sem erro,
    sem duplicate User/Tenant.
- `tests/Sextante.IntegrationTests/Financial/`:
  - `UpdateTransactionTests.cs`: PUT atualiza, publica event,
    Budget alert recalcula.
  - `RecategorizeTransactionsTests.cs`: PATCH 5 ids → 200,
    transactions atualizadas, 5 events publicados, idempotente.
  - `ActivoBankCsvImportTests.cs`: importa
    `Data/Import/example-activo-bank.csv` → mix Income/Expense
    correto, `Amount` em valor absoluto, `TransactionType`
    coerente com sinal original.

---

## 6. CSV import — fix Activo Bank

### 6.1 Localizar parser
- `src/Modules/Financial/.../Application/Features/CsvImport/`:
  - `CsvImportContracts.cs`, `CsvImportHandlers.cs` (já existem).
  - Identificar onde mapeia `Valor` → `Amount` + `TransactionType`.

### 6.2 Inferência por sinal
- Onde hoje força `TransactionType.Expense` (assumindo CSV de
  débito), substituir por:
  ```csharp
  var rawValue = ParseDecimalPtPt(row[mapping.AmountColumnIndex]);
  var type = rawValue >= 0 ? TransactionType.Income : TransactionType.Expense;
  var amount = Math.Abs(rawValue);
  ```
- Manter compat com profiles existentes (Phase 4) — adicionar
  flag opcional ao `ImportProfile` (`InferTypeFromSign: bool =
  true`) com default true. Profiles antigos continuam a funcionar
  (sinal positivo → Income; se CSV original só tem positivos
  expenses, profile pode setar `false` e ter coluna explícita
  de tipo).

### 6.3 Activo Bank profile pré-canned
- Migration de seed (ou data source code):
  - `ImportProfile` global "Activo Bank PT" com:
    - Separator `;`, decimal `,`, header row 1, date format
      `dd-MM-yyyy`, columns map: `Data Lanc.→OccurredAt`,
      `Descrição→Description`, `Valor→Amount`,
      `InferTypeFromSign=true`.
  - Visível a todos os tenants (read-only) — feature útil para
    onboarding em Phase 6.

### 6.4 Testes
- `tests/Sextante.IntegrationTests/Financial/ActivoBankCsvImportTests.cs`:
  - Setup: tenant + Account EUR + categorias dummy.
  - Carrega profile "Activo Bank PT" (seed).
  - POST upload `example-activo-bank.csv`.
  - Confirm preview retorna mix Income/Expense baseado em sinal.
  - Confirm transactions criadas têm `TransactionType` correto e
    `Amount > 0`.
- Karma: `import-wizard.page.spec.ts` verifica preview mostra
  ícones diferentes por tipo.

---

## 7. Frontend — `/app/transactions` page + edição

### 7.1 Route
- `src/Web/Sextante.Web/src/app/app.routes.ts`: nova route
  `transactions` lazy-loaded sob `authGuard`.
- `app-shell.component.ts`: link "Transações" no menu drawer
  (entre "Dashboard" e "Recorrentes").

### 7.2 Page
- `src/Web/Sextante.Web/src/app/features/financial/pages/transactions.page.ts`:
  - Standalone, signal-driven.
  - Form de filtros (Reactive):
    - `dateRange: FormControl<[Date, Date] | null>`.
    - `accountIds: FormControl<string[]>`.
    - `categoryIds: FormControl<string[]>`.
    - `type: FormControl<'Income' | 'Expense' | null>`.
    - `description: FormControl<string>`.
    - `amountMin / amountMax: FormControl<number | null>`.
  - `valueChanges` debounce 300 ms → `transactionsApi.list({...})`.
  - `data-table-shell` com colunas:
    - Checkbox bulk.
    - Data (sortable).
    - Conta.
    - Categoria (com cor + ícone).
    - Descrição.
    - Valor (Money formatado, cor por tipo).
    - Tipo (badge).
    - Ações (`p-menu` com Editar / Apagar).
  - Pagination server-side: `lazy=true`, `onLazyLoad`.
  - Bulk action toolbar: "Recategorizar selecionadas" (N) →
    abre `recategorize.dialog`.
  - Empty state via `empty-state` shared.

### 7.3 Dialog edição
- `src/Web/Sextante.Web/src/app/features/financial/pages/transaction-edit.dialog.ts`:
  - Reactive Form com mesmos campos do create, pre-fill com
    `[transaction]` input.
  - Submit → `transactionsApi.update(id, payload)` → toast PT-PT
    + close + reload tabela.
  - `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"`.

### 7.4 Dialog bulk recategorize
- `src/Web/Sextante.Web/src/app/features/financial/pages/recategorize.dialog.ts`:
  - Inputs: `transactionIds: string[]`.
  - `p-select` de categorias (filtradas por Kind das transactions
    selecionadas? — no MVP mostra todas; user é responsável).
  - Submit → `transactionsApi.recategorize(ids, categoryId)` →
    toast `{N} transações recategorizadas` + reload.

### 7.5 API service
- `src/Web/Sextante.Web/src/app/features/financial/api/transactions-api.service.ts`
  (extend Phase 2):
  - `list(filter): Observable<Page<TransactionDto>>`.
  - `update(id, payload): Observable<TransactionDto>`.
  - `recategorize(ids, categoryId): Observable<{ updatedCount: number }>`.

### 7.6 Karma tests
- `transactions.page.spec.ts`: filtros disparam list, paginação,
  cor por tipo, bulk selection enabled/disabled.
- `transaction-edit.dialog.spec.ts`: form valida, submit chama
  API.
- `recategorize.dialog.spec.ts`: submit batch chama 1 vez API.
- `transactions-api.service.spec.ts`: HTTP via
  `HttpTestingController`.

---

## 8. Frontend — auth UX (remember + session F5)

### 8.1 `auth.service.ts` — localStorage persistence
- Locked: localStorage (não sessionStorage).
- Keys:
  - `sextante.access` (access token).
  - `sextante.refresh` (refresh token — backup; fonte canónica
    é httpOnly cookie).
  - `sextante.last-login-email`.
  - `sextante.extended-session` (`'true'` / `'false'`).
- `login(email, password, extendedSession)`:
  - POST `/api/auth/login` body `{ email, password,
    extendedSession }`.
  - Salvar tokens em localStorage + email + flag.
- `logout()`:
  - POST `/api/auth/logout`.
  - Limpar `sextante.access`, `sextante.refresh`. Manter
    `sextante.last-login-email` (UX: pre-fill no próximo login).
- `refreshSilent()`:
  - Lê `sextante.refresh`; POST `/api/auth/refresh`; substitui
    `sextante.access`.

### 8.2 `appInitializer` rehidratação
- `src/Web/Sextante.Web/src/app/app.config.ts`:
  ```ts
  provideAppInitializer(() => {
    const auth = inject(AuthService);
    return auth.rehydrateFromStorage();
  })
  ```
- `auth.service.rehydrateFromStorage()`:
  - Se `sextante.access` válido → set signal.
  - Se expirado mas `sextante.refresh` presente → silent refresh.
  - Se ambos falham → limpa storage, signal vazio (guard
    redireciona).

### 8.3 `login.page.ts` — pre-fill + checkbox
- No `ngOnInit`: lê `sextante.last-login-email` e pre-fill no
  campo email.
- Adiciona `<p-checkbox>` "Manter-me ligado" ligado a
  `extendedSession: FormControl<boolean>(false)`.
- Submit envia flag.

### 8.4 Karma tests
- `auth.service.spec.ts`:
  - `login` salva tokens + email + flag em localStorage mock.
  - `logout` limpa access/refresh; mantém email.
  - `rehydrateFromStorage` valid → signal populado.
  - `rehydrateFromStorage` expired access + valid refresh →
    silent refresh.
  - `rehydrateFromStorage` ambos invalid → signal vazio,
    storage limpa.
- `login.page.spec.ts`:
  - Pre-fill email do storage.
  - Checkbox enviado no payload.
- `auth.guard.spec.ts`: rehidrata signal antes do guard verificar
  (já garantido por `appInitializer`).

---

## 9. Tests — multi-tenancy regression

### 9.1 Sem regressão em phases anteriores
- Suite Phases 1a/1b/2/3/4/5a/5b deve continuar verde.
- Atenção em particular:
  - Phase 1a `MultiTenancyTests` — `TenantSwitchDetector` não
    deve falsificar (test que valida que o middleware NÃO loga
    error em tenant válido).
  - Phase 5b `BudgetWorkflowTests` — `TransactionUpdatedIntegrationEvent`
    triggered pelo PUT do dialog tem de re-disparar
    `BudgetAlertDispatchHandler` (já garantido — verificar).

### 9.2 Tests novos para isolation
- `RecategorizeTransactionsTests`: tenant A tenta recategorizar
  IDs do tenant B → 0 atualizados (RLS isola), test não 500.
- `UpdateTransactionTests`: tenant A tenta PUT em ID do tenant B
  → 404.

---

## 10. Documentation + close-out

### 10.1 README
- Secção "Provisionamento inicial" com instruções `create-admin`.
- Secção "Configuração de Sentry" — DSNs em `.env`, fallback
  graceful documentado.

### 10.2 Vault doc (Obsidian)
- Atualizar `Vault: 04 - Arquitetura - Frontend.md`:
  - Tema Aura customizado, dark mode, design tokens.
  - Componentes shared (page-header, empty-state, etc.).
  - `data-table-shell` pattern.
- Atualizar `Vault: 02.4 - Arquitetura - Autenticação.md`:
  - Extended session, "Manter-me ligado".
  - LocalStorage rehydrate + trade-off XSS.

### 10.3 Changelog
- Skill `changelog` adiciona entrada datada com sumário Phase 5.5.

### 10.4 Roadmap
- Marcar todos os bullets de Phase 5.5 com `[x]` em
  `specs/roadmap.md` via conversa com o agente após merge.

### 10.5 Sub-agent deep review (🛡️ obrigatório)
- Fokus areas:
  - **PII scrubbing** — verificar que `description`, `amount`,
    `email` não aparecem em logs reais (Serilog) **nem** em
    eventos Sentry capturados.
  - **TenantSwitchDetector** — assert que cobre o flow
    realístico (não apenas test fabricado).
  - **ProblemDetails migration** — todos os endpoints retornam
    shape consistente (sem leak de stack trace em produção).
  - **localStorage XSS** — CSP do `index.html` adequado;
    docs/adr/ADR-013 (opcional, se review pedir formalização)
    documenta trade-off.
  - **Admin CLI idempotência** — race condition em concurrent
    invocations? Não esperado em ops normais; documentar.
  - **Sentry frontend privacy** — assert que campos `Amount`,
    `Limit`, `Description`, `Notes` não aparecem em breadcrumbs
    nem replays.
  - **Activo Bank parser** — não regressa profiles existentes
    (Phase 4 testes mantêm verde).

### 10.6 Manual walkthrough (merge-blocker)
Cenários documentados em `validation.md`. Resumo:
1. Backend live com Sentry DSN configurado → forçar 500 →
   evento aparece em Sentry com `tenant_id` tag.
2. Frontend live com Sentry DSN → forçar erro JS → evento
   aparece sem PII de inputs financeiros.
3. Admin CLI primeira execução → User criado + categorias
   seeded. Re-execução → password rotated, sem erro.
4. Importação CSV Activo Bank → mix Income/Expense correto.
5. Editar transação no novo `/app/transactions` → Budget
   recalcula.
6. Bulk recategorize 10 transações → 1 toast, 10 events,
   Budget recalcula (se aplicável).
7. Login com "Manter-me ligado" → fechar browser → reabrir →
   sessão persiste, sem re-login.
8. F5 em qualquer página autenticada → não redirect para
   login.
9. Logout → redirect para login → email pre-filled.
10. Mobile audit: percorrer todas as pages em 375 px.

### 10.7 Commit + PR
- `git commit -am "Mark phase 5.5 as complete"` após merge dos
  últimos checkpoints.
- PR `phase-5.5-refinement → main`.
- Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, Karma).
  - Sentry inbox screenshot em PR description (1 backend + 1
    frontend evento test).
  - Sub-agent deep review approved.
  - Walkthrough manual passa.
  - Aprovação humana.
- Phase 6 (Polish + Deploy + Dogfooding) é o próximo passo.
