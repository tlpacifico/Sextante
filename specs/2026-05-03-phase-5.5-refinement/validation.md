# Validation — Phase 5.5: Refinement

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

### ADR + tech-stack

1. **ADR-012 escrito** em `docs/adr/ADR-012-sentry-error-tracking.md`
   com Context / Decision / Consequences / Alternatives.
2. **`specs/tech-stack.md` §13 e tabela §17 atualizados** a refletir
   Sentry como decisão MVP.

### Backend — observability

3. **Filtros PII no Serilog** mascaram `email`, `password`,
   `description`, `notes`, `amount`, `limitAmount` em logs estruturados.
4. **TraceId enricher** — todos os logs de request carregam `TraceId`
   correlacionável com `Activity.Current.TraceId`.
5. **TenantId enricher** — todos os logs de request autenticada
   carregam `TenantId`.
6. **TenantSwitchDetector** — log `Error` + Sentry warning quando
   `ITenantContext.TenantId` muda mid-request.
7. **GlobalExceptionHandler** mapeia exceções conhecidas → ProblemDetails
   RFC 7807 com `type`, `title`, `status`, `detail`, `instance`,
   `traceId`, mensagens PT-PT.
8. **Migração full ProblemDetails** — todos os endpoints (Auth +
   Identity + Financial + Admin) retornam `ProblemDetails` em 4xx/5xx.
9. **SecurityHeaders middleware** injecta `X-Content-Type-Options`,
   `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`,
   `HSTS` (prod). CORS explicit policy registada.
10. **Wolverine FluentValidationPolicy** valida command antes do
    handler; falha → `ValidationException` → ProblemDetails 400.
11. **Wolverine MetricsPolicy** loga `{HandlerType, DurationMs,
    Success}` por handler invocation.

### Sentry

12. **Backend Sentry integration** — `Sentry.AspNetCore` +
    `Sentry.Serilog` registados, DSN via `.env`, `BeforeSend` aplica
    PII scrubber, tag `tenant_id` em cada evento.
13. **Frontend Sentry integration** — `@sentry/angular` integrado,
    DSN via build env, source maps configurados, mascarado em
    inputs financeiros.
14. **Fallback graceful** — DSN ausente → app continua, file sink
    Serilog mantém log.
15. **Smoke test** — forçar 500 backend e erro JS frontend → ambos
    aparecem no inbox Sentry, sem PII vazada.

### Frontend — design system

16. **Design tokens consolidados** em `src/styles/tokens.css` (CSS
    custom properties) + Tailwind theme extend + PrimeNG Aura
    customizado.
17. **Componentes shared** extraídos: `page-header`, `empty-state`,
    `confirm-dialog`, `data-table-shell`, `form-field`. Usados em
    pages refactoradas.
18. **Tema Aura customizado** com cores Sextante e dark mode toggle
    funcional via `[data-theme]`.
19. **Sanity check responsivo retroactivo** em todas as pages das
    Phases 1b–5b nas viewports 375 / 768 / 1280 px. Sem overflow
    horizontal do `<body>`. Achados documentados em
    `responsive-audit.md`.
20. **AGENTS.md** ou tech-stack §19.5 com checklist DoD responsivo
    como rule dura para phases futuras.

### Bugs prioritários e UX crítica

21. **CSV Activo Bank** — parser infere tipo pelo sinal de `Valor`;
    teste de integração com `example-activo-bank.csv` mostra mix
    Income/Expense correto.
22. **Página `/app/transactions`** — tabela paginada server-side com
    filtros (data, conta, categoria, tipo, descrição, valor),
    ordenação por coluna, edição via dialog, bulk recategorize,
    `data-table-shell` reused.
23. **Endpoint PUT transação** — atualiza com tenant fail-loud,
    publica `TransactionUpdatedIntegrationEvent`, `BudgetAlertDispatchHandler`
    recalcula.
24. **Endpoint PATCH recategorize** — bulk até 500 ids numa Tx,
    publica events, idempotente.
25. **Login pre-fill email** — `localStorage.sextante.last-login-email`
    persistido em login bem-sucedido, lido no `ngOnInit` da
    `login.page.ts`.
26. **Checkbox "Manter-me ligado"** — extended session emite refresh
    token com lifetime 30 dias (vs 7 dias default). Endpoint aceita
    `extendedSession`.
27. **Persistência F5** — `appInitializer` rehidrata signal de auth
    a partir de localStorage; refresh silencioso se access token
    expirado mas refresh válido. Logout limpa storage.
28. **CLI `create-admin`** — primeira execução cria User + Tenant +
    Membership(Owner) + role Admin + EmailConfirmed + categorias
    seeded. Re-execução com mesmo email rotaciona password,
    idempotente. Documentado no README.

### Tests

29. **Backend integration tests verdes** — ProblemDetails, Security
    headers, PII scrubbing, TenantSwitchDetector, Update transaction,
    Recategorize transactions, Activo Bank CSV import, Extended
    session, Logout, Create admin CLI.
30. **Backend unit tests verdes** — `GlobalExceptionHandler`,
    `FluentValidationPolicy`, `MetricsPolicy`, parser CSV signal
    inference.
31. **Karma frontend tests verdes** — `auth.service` rehydrate,
    `login.page` pre-fill + checkbox, `transactions.page` filtros,
    `transaction-edit.dialog`, `recategorize.dialog`, components
    shared.
32. **Architecture tests verdes** — middlewares e ExceptionHandler
    em `BuildingBlocks/Sextante.Infrastructure`; CLI em
    `Bootstrap/Sextante.Host/Cli`; sem dependência circular.
33. **Suite Phases 1a/1b/2/3/4/5a/5b verde** (sem regressão).

### Merge gate (🛡️)

34. **Sub-agent deep review approved** — focus em PII, tenant
    switch, ProblemDetails migration, localStorage trade-off,
    Sentry privacy, Activo Bank parser regression.
35. **Manual walkthrough completo** — Cenários 1–10 abaixo passam
    em ambiente limpo.
36. **CI verde** — GitHub Actions build .NET, test .NET, build
    Angular, Karma.
37. **Sentry inbox screenshot** anexado ao PR (1 evento backend +
    1 evento frontend de smoke test).
38. **`specs/roadmap.md` Phase 5.5 bullets ticados** via conversa
    com o agente.
39. **`CHANGELOG.md`** com entrada datada da Phase 5.5.

## How to verify each bullet

1. **ADR-012.**
   - `cat docs/adr/ADR-012-sentry-error-tracking.md` mostra
     secções esperadas.
   - Skill review apanha qualquer secção em falta.

2. **Tech-stack updates.**
   - `git diff main..phase-5.5-refinement -- specs/tech-stack.md`
     mostra §13 + §17 + §18 atualizados.

3. **PII scrubbing Serilog.**
   - Test integration `PiiScrubbingTests` verde.
   - Manual: provocar log com email/password → grep do log file
     não retorna a string original.

4. **TraceId enricher.**
   - GET request, inspecionar log file: cada linha do request
     tem mesmo `TraceId`.
   - Header `traceparent` da request gerou esse TraceId.

5. **TenantId enricher.**
   - Login → inspecionar log file → request autenticada tem
     `TenantId={guid}`.
   - Anonymous request (`/api/health`) não tem `TenantId`.

6. **TenantSwitchDetector.**
   - Test integration que injeta switch malicioso → log Error
     + Sentry capture.
   - Test em fluxo normal (single-tenant request) → 0 logs Error
     deste detector.

7. **GlobalExceptionHandler.**
   - Test `ProblemDetailsTests` verde.
   - Manual: 400 `curl POST /api/financial/budgets` body inválido
     → JSON com `type=urn:sextante:errors:validation`,
     `traceId`, `errors[]`.

8. **ProblemDetails migration full.**
   - Grep do código por `BadRequest(string)`,
     `NotFound(string)`, `Conflict(string)` → 0 hits (todos
     migrados).
   - Test integration por endpoint pega shape consistente.

9. **Security headers.**
   - `curl -I http://localhost/api/health` em produção →
     headers presentes.
   - Test `SecurityHeadersTests` verde.

10. **Wolverine FluentValidationPolicy.**
    - Test unit valida que `Validate` é chamado antes do handler.
    - Manual: comand inválido → 400 sem chegar ao handler.

11. **Wolverine MetricsPolicy.**
    - Inspecionar log file durante request → linha
      `Wolverine handler X completed in Yms`.

12. **Backend Sentry.**
    - Definir `SENTRY__DSN` válido no `.env` → `dotnet run` →
      forçar 500 via `/api/admin/sentry-test` → evento aparece
      no inbox Sentry com `tenant_id` tag.
    - Inspecionar evento: campo `email` mascarado.

13. **Frontend Sentry.**
    - `npm run build:prod` com `SENTRY_DSN_WEB` definido.
    - Browser → trigger erro JS via debug button →
      evento aparece no inbox `sextante-web` com source maps.
    - Inputs `Amount`, `Limit` não aparecem em breadcrumbs.

14. **Fallback graceful.**
    - Test integration arranca app sem `SENTRY__DSN` → app
      responde, log "Sentry disabled (DSN missing)".
    - Frontend sem `sentryDsn` → `Sentry.init` skipped, app boots.

15. **Smoke test Sentry.**
    - Anexar screenshot ao PR (`/api/admin/sentry-test` evento
      + frontend debug event).

16. **Design tokens.**
    - `cat src/Web/Sextante.Web/src/styles/tokens.css` mostra
      paleta + spacing + typography.
    - Inspecionar Tailwind config: `theme.extend.colors.primary`
      referencia `var(--color-primary-*)`.

17. **Componentes shared.**
    - `ls src/Web/Sextante.Web/src/app/shared/ui/` mostra 5
      pastas: `page-header`, `empty-state`, `confirm-dialog`,
      `data-table-shell`, `form-field`.
    - Grep `<sxt-page-header>` em pages → ≥ 5 hits.

18. **Tema Aura.**
    - Inspecionar `app.config.ts` → `definePreset(Aura, ...)`.
    - Toggle dark mode no header → `[data-theme=dark]` em
      `<html>`, cores invertem.

19. **Responsive audit retroactivo.**
    - `cat specs/2026-05-03-phase-5.5-refinement/responsive-audit.md`
      lista cada page × viewport × issue × fix.
    - DevTools manual em cada page das Phases 1b–5b → sem
      overflow horizontal, dialogs cabem, gráficos legíveis.

20. **AGENTS.md DoD responsivo.**
    - `git diff main..phase-5.5-refinement -- AGENTS.md` mostra
      novo bullet.

21. **Activo Bank fix.**
    - `dotnet test --filter
      FullyQualifiedName~ActivoBankCsvImportTests` verde.
    - Manual: importar `example-activo-bank.csv` no UI →
      preview mostra mix de Income/Expense alinhado com sinal
      do `Valor`.

22. **Página `/app/transactions`.**
    - `npm run start` → navegar `/app/transactions` →
      tabela carrega, filtros funcionam (testar cada um),
      ordenação muda dados, paginação muda dados.
    - Edição: clicar Editar → dialog abre com pre-fill →
      submit → toast PT-PT → tabela atualiza.
    - Bulk: selecionar 3 linhas → toolbar mostra "3
      selecionadas" → Recategorizar → escolher cat → toast
      `3 transações recategorizadas` → tabela atualiza.

23. **PUT endpoint.**
    - `curl -X PUT http://localhost/api/financial/transactions/{id}`
      com body completo → 200 com novo dto.
    - psql confirma `updated_at` mudou.
    - `BudgetAlertDispatchHandler` test (re-utilizar Phase 5b
      patterns) confirma recálculo.

24. **PATCH recategorize.**
    - `curl -X PATCH http://localhost/api/financial/transactions/recategorize
      -d '{"ids":["..."], "categoryId":"..."}'` → 200 com
      `updatedCount`.
    - Limit: ids.length > 500 → 400 com ProblemDetails
      `validation`.
    - Re-call mesmo payload → 200 com mesmo updatedCount
      (idempotente).

25. **Pre-fill email.**
    - Login bem-sucedido. Logout. Recarregar `/login` →
      campo email pre-filled.
    - Karma `login.page.spec.ts` verde para esse cenário.

26. **Extended session.**
    - Login com checkbox marcado → inspecionar `Set-Cookie`
      do refresh: `Max-Age` ≈ 30 dias (2592000 s).
    - Sem checkbox: `Max-Age` ≈ 7 dias.
    - Test integration `ExtendedSessionTests` verde.

27. **Persistência F5.**
    - Login → F5 em `/app/dashboard` → não redireciona para
      login.
    - Inspecionar Application tab → localStorage tem
      `sextante.access`, `sextante.refresh`,
      `sextante.last-login-email`.
    - Fechar tab → reabrir → sessão persiste (até refresh
      token expirar).
    - Logout → localStorage `access`/`refresh` limpos;
      `last-login-email` mantido.

28. **CLI create-admin.**
    - `docker compose run --rm api dotnet Sextante.Host.dll
      create-admin --email admin@x.pt --password 'Strong1!'`
      → exit 0 + log "Admin admin@x.pt created with tenant
      Admin Tenant".
    - psql: `User`, `Tenant`, `Membership(Owner)`,
      `AspNetUserRoles` (Admin) presentes; categorias seed
      criadas.
    - Re-executar mesma comand → exit 0 + log "password rotated,
      role ensured". psql: 1 User, 1 Tenant (não duplicado),
      `EmailConfirmed=true`.
    - Test `CreateAdminCliTests` verde.

29. **Backend integration tests.**
    - `dotnet test tests/Sextante.IntegrationTests` verde
      (incl. novos arquivos §3.7, §5.6, §6.4 do plan).

30. **Backend unit tests.**
    - `dotnet test` em projetos `*.Tests` verde.

31. **Karma frontend tests.**
    - `cd src/Web/Sextante.Web && npm test --
      --watch=false --browsers=ChromeHeadless` verde.

32. **Architecture tests.**
    - `dotnet test --filter
      FullyQualifiedName~ArchitectureTests` verde.

33. **Sem regressão.**
    - `dotnet test -c Release` (suite completa) verde.
    - Phases 1a–5b continuam OK.

34. **Sub-agent deep review.**
    - PR description tem checklist de focus areas (§10.5
      do plan) marcado pelo reviewer.
    - Comments resolvidos antes do merge.

35. **Manual walkthrough.**
    - Executar Cenários 1–10 abaixo num ambiente limpo
      (`docker compose down -v && docker compose up`).

36. **CI.**
    - GitHub Actions no commit mais recente do
      `phase-5.5-refinement` → todos os jobs verdes.

37. **Sentry screenshots.**
    - PR description anexa imagens dos eventos.

38. **Roadmap.**
    - `git diff main..phase-5.5-refinement -- specs/roadmap.md`
      mostra Phase 5.5 bullets com `[x]`.

39. **Changelog.**
    - `git diff main..phase-5.5-refinement -- CHANGELOG.md`
      mostra secção nova com data do merge.

---

## Manual walkthrough — Phase 5.5

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais por valores reais.

### Pré-requisitos

```bash
docker compose down -v && docker compose up -d
# Migrations correm no startup; aguardar healthcheck.
```

Definir DSNs Sentry válidos em `.env` antes de subir.

### Cenário 1: Admin CLI bootstrap

```bash
docker compose exec api dotnet Sextante.Host.dll create-admin \
  --email admin@sextante.local --password 'Phase55Pass!' \
  --tenant-name 'Sextante Admin'
```

Esperado:
- Exit 0.
- Log: `Admin admin@sextante.local created with tenant Sextante Admin.`.
- psql:
  ```sql
  SELECT u.email, u.email_confirmed, t.name, m.role,
         array_agg(r.name) AS roles
    FROM shared."AspNetUsers" u
    JOIN shared."Memberships" m ON m.user_id = u.id
    JOIN shared."Tenants" t ON t.id = m.tenant_id
    JOIN shared."AspNetUserRoles" ur ON ur.user_id = u.id
    JOIN shared."AspNetRoles" r ON r.id = ur.role_id
    GROUP BY u.email, u.email_confirmed, t.name, m.role;
  ```
  Esperado: 1 row, `email_confirmed=true`, `role='Owner'`,
  `roles={Admin}`.
- Categorias seed presentes em `financial.categories` para o
  novo tenant.

Re-executar a mesma comand → log `password rotated, role
ensured`, sem erro, psql sem duplicates.

### Cenário 2: Login + extended session + F5

1. Browser → `/login` → pre-fill email vazio (primeira vez).
2. Login com `admin@sextante.local`, password, **checkbox
   "Manter-me ligado" marcado**.
3. Inspecionar Application → localStorage:
   - `sextante.access`, `sextante.refresh`,
     `sextante.last-login-email=admin@sextante.local`,
     `sextante.extended-session=true`.
   - Cookies: refresh token httpOnly com `Max-Age` ≈ 2592000.
4. F5 em `/app/dashboard` → não redirect para login.
5. Fechar browser. Reabrir. Navegar para `/app/dashboard` →
   sessão persiste.
6. Logout. localStorage `access`/`refresh` limpos;
   `last-login-email` mantido. Re-load `/login` → email
   pre-filled.

### Cenário 3: ProblemDetails consistency

1. `curl -X POST http://localhost/api/financial/accounts \
    -H "Authorization: Bearer <token>" \
    -d '{}'`
   → 400 com:
   ```json
   {
     "type": "urn:sextante:errors:validation",
     "title": "Erros de validação",
     "status": 400,
     "detail": "...",
     "instance": "/api/financial/accounts",
     "traceId": "...",
     "errors": { "Name": ["Nome é obrigatório."] }
   }
   ```
2. `curl http://localhost/api/financial/transactions/{nonexistent-id}`
   → 404 com `type=urn:sextante:errors:not-found`,
   `traceId` presente.
3. Forçar 500 via `/api/admin/sentry-test` → 500 com
   `type=urn:sextante:errors:internal`, sem stack trace exposto.

### Cenário 4: Sentry smoke test

1. Backend: `curl http://localhost/api/admin/sentry-test
   -H "Authorization: Bearer <admin-token>"`.
2. Esperar ≤ 30 s.
3. Sentry inbox `sextante-api` → 1 evento "Sentry test".
4. Inspecionar evento:
   - Tag `tenant_id` presente.
   - `breadcrumbs` não contêm `email`, `password`,
     `description`.
5. Frontend: navegar `/app/dashboard?debug=sentry` → trigger.
6. Sentry inbox `sextante-web` → 1 evento JS, source map
   resolvido (linhas em TS, não em minified JS).
7. `breadcrumbs` não contêm `Amount`, `Limit`, `Description`.

### Cenário 5: TenantSwitchDetector

1. Test endpoint malicioso (apenas em `Development`) que
   injeta troca de tenant mid-request:
   `curl /api/admin/tenant-switch-test`.
2. Inspecionar log file: linha `Tenant switched mid-request:
   {A} → {B}`.
3. Sentry inbox: 1 evento Warning.
4. Em fluxo normal de request autenticada: 0 logs deste
   detector.

### Cenário 6: Activo Bank CSV import

1. Criar Account EUR.
2. Importar `tests/Sextante.IntegrationTests/Data/Import/example-activo-bank.csv`.
3. No preview wizard:
   - Linhas com `Valor` negativo → tipo Expense (vermelho).
   - Linhas com `Valor` positivo → tipo Income (verde).
   - Todos os `Amount` em valor absoluto.
4. Confirmar import → transactions criadas com tipos
   correctos. psql verifica.

### Cenário 7: Página /app/transactions + edição

1. Navegar `/app/transactions`.
2. Filtros: definir intervalo de datas → tabela atualiza.
3. Ordenar por Valor desc → topo mostra maior gasto.
4. Editar uma transação → dialog abre, mudar categoria →
   submit → toast PT-PT, tabela atualiza.
5. Confirmar via psql que `updated_at` da transação mudou
   e Budget alert recalcula (se aplicável).
6. Selecionar 5 transações (checkbox) → toolbar mostra
   "5 selecionadas" → "Recategorizar selecionadas" →
   escolher categoria → toast `5 transações recategorizadas`.
7. psql: `category_id` das 5 transações é a nova.

### Cenário 8: Tema Aura + dark mode

1. Inspecionar `/app/dashboard` em modo claro → cores
   primárias do design tokens.
2. Toggle dark mode no header → `[data-theme=dark]` em
   `<html>` → cores invertem (background escuro, texto
   claro).
3. Toggle de volta → modo claro.
4. PrimeNG components (botões, tabelas, dialogs) seguem o
   tema.

### Cenário 9: Responsive sanity check

DevTools → Device toolbar → ciclar 375 × 667, 768 × 1024,
1280 × 800 em cada page:
- `/login`, `/signup`, `/forgot-password`, `/reset-password`.
- `/app/dashboard`, `/app/accounts`, `/app/categories`,
  `/app/transactions`, `/app/recurring-rules`,
  `/app/budgets`, `/app/categorization-rules`,
  `/app/import-profiles`, `/app/import-batches`,
  `/app/import-wizard`.
- Dialogs (budget, recurring-rule, transaction-edit,
  recategorize, confirm).

Verificar:
- Sem overflow horizontal do `<body>`.
- Tabelas com overflow horizontal interno.
- Dialogs cabem (≤ 95vw em < 640 px).
- Touch targets ≥ 44 × 44 px.
- Tipografia escala (text-2xl em desktop, text-xl em mobile).
- Charts: legenda em bottom em mobile.

Findings vão para `responsive-audit.md`. Fixes commitados antes
do merge.

### Cenário 10: Multi-tenancy regression

1. Signup tenant B.
2. Tenant B `/app/transactions` → vazio (não vê de A).
3. `curl PUT http://localhost/api/financial/transactions/<id-de-A>`
   com token de B → 404.
4. `curl PATCH http://localhost/api/financial/transactions/recategorize
   -d '{"ids":["<id-de-A>"],"categoryId":"<cat-B>"}'`
   com token de B → `updatedCount=0`.
5. psql como role `sextante_app` (NOBYPASSRLS):
   ```sql
   SET app.current_tenant_id = '<tenantB-uuid>';
   SELECT count(*) FROM financial.transactions
     WHERE id = '<id-de-A>';
   -- Esperado: 0.
   ```

### Cenário 11 (opcional): Sentry sem DSN

1. `unset SENTRY__DSN` em `.env`.
2. `docker compose restart api`.
3. App responde normalmente (`/api/health` 200).
4. Log: `Sentry disabled (DSN missing)`.
5. Forçar 500 → log Error em ficheiro, mas inbox Sentry vazio
   (esperado).

---

## Out of scope for validation

- **OpenTelemetry / Prometheus / Grafana** — Phase 7.
- **Self-hosted Sentry** — locked SaaS.
- **Email / push notifications de alertas** — Phase 15.
- **Lighthouse / acessibilidade formal** — Phase 6.
- **Refresh token revocation list / device management** —
  pós-MVP.
- **CSV export** — Phase 6.
- **Performance benchmark formal** — Phase 7.
- **Edição inline da transactions table** (vs dialog) — Phase
  6 ou backlog.
- **CSP hardening completo** (script-src, style-src strict) —
  Phase 6 (depende de tema final + nonces).
- **Sentry release management auto via CI** — Phase 6 (job de
  deploy).
- **Storybook** — fora do MVP.
- **Roles UI** (invites Owner/Member/ReadOnly) — Phase 18.
- **Browser cross-version** — Chromium-based suficiente.
