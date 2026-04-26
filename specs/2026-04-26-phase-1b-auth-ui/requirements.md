# Requirements — Phase 1b: Auth UI (Angular)

## Goal

Entregar a UI Angular de auth em cima da API estável da Phase 1a:
quatro páginas (`/signup`, `/login`, `/forgot-password`,
`/reset-password`), shell autenticado com PrimeNG Aura + Tailwind +
toggle de dark mode, `HttpInterceptor` com auto-refresh, auth guard
suportado por Angular Signals, e refresh token persistido em cookie
httpOnly emitido pelo backend. Depois desta phase, o princípio 1 da
mission ("Privacidade por defeito") fica acessível a partir de um
browser real, não apenas integration tests. PrimeNG + Tailwind tornam-se
a base operacional para os dashboards das phases 2+.

`tech-stack.md` §19 marca esta stack como invariante; Phase 1b é
onde essa stack deixa de ser plano e passa a ser código.
`roadmap.md` § "Phase 1b" enumera os 8 bullets que esta phase entrega
(plus a extensão backend de cookie wiring decidida via clarifying
question).

## In scope

- **Upgrade Angular 19.2.0 → Angular 21 LTS** em `src/Web/Sextante.Web/`:
  `@angular/{core,common,forms,router,platform-browser,
  platform-browser-dynamic,compiler,animations}`, `@angular/cli`,
  `@angular-devkit/build-angular`, TypeScript pin.
- **PrimeNG (`@primeng/themes` preset Aura)**, **PrimeIcons**,
  **Chart.js** (peer da PrimeNG Chart), **Tailwind CSS** + `postcss` +
  `autoprefixer` + `tailwindcss-primeui` adicionados ao `package.json`.
- **ADR-011** (`docs/adr/ADR-011-frontend-ui-stack.md`) committed com
  Status / Context / Decision / Consequences / Alternatives Considered.
- **`AuthShellComponent`** (centred card sobre fundo Tailwind, toast
  outlet, sem header/sidenav) para rotas não autenticadas.
- **`AppShellComponent`** (PrimeNG Menubar com tenant + user menu +
  toggle de dark mode, sidebar placeholder, toast outlet, router-outlet)
  para rotas autenticadas.
- **`ThemeService`** (signal `isDark`) com persistência em
  `localStorage` e fallback para `prefers-color-scheme`.
- **Páginas** `/signup`, `/login`, `/forgot-password`, `/reset-password`
  com Reactive Forms e validação PT-PT.
- **`AuthService`** com `WritableSignal<AuthState>`, JWT decode,
  computed `isAuthenticated` / `tenantName` / `tenantRole`.
- **`authInterceptor`** (HttpInterceptorFn) que anexa
  `Authorization: Bearer <accessToken>` a requests `/api/...`
  (excepto `/api/auth/login` e `/api/auth/signup`), faz auto-refresh
  em 401, e previne refresh storms via Promise in-flight cacheada.
- **`authGuard`** (`CanActivateFn`) baseado no signal
  `isAuthenticated`, com redirect para `/login?returnUrl=<path>`.
- **Backend extension**: middleware pós-`MapIdentityApi` em
  `src/Bootstrap/Sextante.Host/Program.cs` que emite o refresh token
  como `Set-Cookie: refresh_token=<v>; HttpOnly; Secure;
  SameSite=Strict; Path=/api/auth/refresh; Max-Age=604800` em
  `/api/auth/login` e `/api/auth/refresh`. `/api/auth/refresh` aceita
  o cookie quando o body não traz refresh token. `/api/auth/logout`
  limpa o cookie.
- **Integration test** `tests/Sextante.IntegrationTests/Auth/RefreshCookieTests.cs`
  para o fluxo do cookie (login → cookie set → refresh sem body →
  logout → cookie cleared).
- **Karma/Jasmine unit tests** para `AuthService`, `authInterceptor`,
  `authGuard`.
- **Manual browser walkthrough** documentado em `validation.md`,
  reproduzível a partir de `git clone` + `docker compose up`.
- **Rota placeholder** `/app/dashboard` (componente "Phase 2 chega aí")
  para o auth guard ter destino — será substituída na Phase 2.

## Out of scope

- **SMTP / email delivery real.** Tech-stack §12 difere a escolha do
  provider para Phase 6 pré-dogfooding. `IEmailSender` continua a ser
  o `NotImplementedEmailSender` da Phase 1a. Página de
  forgot-password chega ao stub; o interceptor traduz a falha numa
  toast PT-PT "Envio de emails ainda não disponível — Phase 6".
- **Subscriber do `UserRegisteredIntegrationEvent`.** Phase 2
  (módulo Financial faz seed de categorias default).
- **Multi-membership UI / tenant switcher.** Phase 18.
- **Hangfire dashboard / `/api/admin/*`.** Phase 6.
- **2FA / Passkeys / OAuth / social login.** Pós-MVP.
- **Rate limiting login-específico / brute-force protection.**
  Phase 6 (Phase 1a já limita 30 req/IP/min no route group).
- **Playwright / Cypress E2E.** Manual walkthrough é o artefacto
  escolhido para Phase 1b (revisitar em Phase 6 se a regressão crescer).
- **Accessibility audit (axe-core, screen-reader walkthrough).**
  Sub-agent review faz sanity check; audit formal fica para Phase 6.
- **Storybook.** Tech-stack §19.3 difere até ~20 componentes próprios.
- **Integração completa de PrimeNG Chart.** Importado como peer dep
  mas nenhum gráfico é renderizado até Phase 2.
- **i18n EN / PT-BR.** `@angular/localize` em pt-PT já existe;
  outros locales pós-MVP per mission §5.
- **CSP headers / security headers tuning.** Phase 6.
- **Tweaks de CI para além de `npm test`.** Caching e reporting
  ficam diferidos.

## Decisions

- **Preset PrimeNG = Aura, com dark mode IN scope.** *Why*: Aura é
  o preset moderno recomendado do PrimeNG (substituiu Lara como
  default em v18+); resolver dark mode agora evita retrofit quando
  Phase 2 introduzir o dashboard. Tech-stack §19.3 marca explicitamente
  estas decisões para Phase 1b. **How to apply**:
  `providePrimeNG({ theme: { preset: Aura, options: {
  darkModeSelector: '.dark' } } })` em `app.config.ts`. Tailwind
  `darkMode: 'class'`. `ThemeService` toggle adiciona/remove `.dark`
  no `document.documentElement`, com persistência em `localStorage`.

- **Refresh token via cookie httpOnly, emitido pelo backend.**
  *Why*: roadmap §1b manda armazenar o refresh em cookie httpOnly;
  Phase 1a usa bearer header sem qualquer cookie. Sem extensão
  backend, o frontend não consegue cumprir o roadmap. Pôr o refresh
  em JS derrota a proteção contra XSS. **How to apply**: middleware
  pós-`MapIdentityApi` extrai o `refreshToken` do response body e
  adiciona `Set-Cookie: refresh_token=...; HttpOnly; Secure;
  SameSite=Strict; Path=/api/auth/refresh; Max-Age=604800`.
  `/api/auth/refresh` aceita o cookie como fallback ao body.
  Angular `HttpClient` usa `withCredentials: true` no interceptor
  quando a URL é `/api/auth/refresh`.

- **Forgot/reset password pages construídas, mas dependem do stub
  de email.** *Why*: roadmap §1b lista explicitamente estas páginas;
  tech-stack §12 difere SMTP para Phase 6. Construir a UI agora e
  surfacing graceful do stub é mais barato que retrofit em Phase 6.
  **How to apply**: interceptor traduz o erro do
  `NotImplementedEmailSender` numa toast PT-PT
  "Envio de emails ainda não disponível — Phase 6". Reset-password
  manual usa código gerado via psql contra `AspNetUserTokens`.

- **State management = Angular Signals + services. Sem NgRx.**
  *Why*: tech-stack §19.1 e §19.2 mandam Signals + services no MVP.
  **How to apply**: `AuthService` expõe `WritableSignal<AuthState>`
  privado e signals computados (`isAuthenticated`, `tenantName`,
  `tenantRole`). Components consomem via `inject(AuthService)` e
  leitura direta dos signals em templates.

- **Refresh storm prevention via Promise in-flight cacheada.**
  *Why*: 5 calls paralelas em 401 não podem disparar 5 chamadas a
  `/api/auth/refresh`. **How to apply**: `AuthService.refresh()`
  cacheia a Promise pendente; chamadores concorrentes recebem a
  mesma; cleared em `then`/`catch`.

- **Standalone components only, Reactive Forms only.**
  *Why*: tech-stack §19.2 manda ambos. **How to apply**: zero
  `NgModule` em código novo; zero `FormsModule`/`ngModel` em
  formulários financeiros.

- **Sub-agent deep review (🛡️) é merge blocker.** *Why*: roadmap
  marca Phase 1b com 🛡️. **How to apply**: invocar review com o
  prompt enumerado em `plan.md` §12.1; findings high bloqueiam,
  medium viram backlog.

- **Karma unit tests para o auth core; sem framework E2E ainda.**
  *Why*: DoD escolhido foi "manual walkthrough + Karma + sub-agent".
  Adicionar Playwright/Cypress agora é infra pesada (runner novo,
  headless browser em CI, Testcontainers Postgres) para uma única
  feature flow. Revisitar em Phase 6 se a regressão crescer.
  **How to apply**: spec files para `AuthService`, `authInterceptor`,
  `authGuard` em `app/auth/*.spec.ts`; `npm test -- --watch=false`
  em CI.

- **Cookie path = `/api/auth/refresh` (mais estreito possível).**
  *Why*: minimiza superfície de ataque (só o endpoint que precisa
  do cookie o recebe). **How to apply**: cookie emitido com
  `Path=/api/auth/refresh`; logout emite delete-cookie no mesmo path.
  Se durante a implementação o logout precisar de um path mais largo
  (ex.: `/api/auth`) por questão de browser, registar em "Open
  questions" + ADR follow-up.

## Context / references

- `specs/roadmap.md` — secção "Phase 1b — Auth UI (Angular)".
- `specs/tech-stack.md` — §6 (Auth: 15min access / 7day refresh /
  cookie storage / header para access), §19 (frontend secção inteira),
  §17 (decisões), §18 (ADR-011 pendente).
- `specs/mission.md` — §4 princípio 1 (privacidade por defeito —
  só fica plenamente entregue quando um browser real prova),
  princípio 4 (degradação graciosa — cobre o stub de email).
- `specs/2026-04-26-phase-1a-auth-backend/` — Phase 1a entregou a
  superfície de API que esta phase consome; o curl walkthrough
  do `validation.md` desse spec é o contrato.
- Memória do agente — `replanning_2026_04_26.md` (Wolverine,
  PrimeNG+Tailwind+Signals, Angular 21, split de Phase 1).
- `src/Bootstrap/Sextante.Host/Program.cs` (registo de
  `MapIdentityApi`, ponto onde Phase 1b enxerta o cookie wiring).
- `src/Modules/Identity/Sextante.Modules.Identity.Api/Endpoints/SignupEndpoint.cs`
  (contrato do signup que o form Angular segue: email, password
  min 12, tenantName min 2 max 200).

## Open questions

- **Path exato de import do preset Aura**: `@primeng/themes` em v18+
  expõe `import Aura from '@primeng/themes/aura';`. Confirmar
  durante implementação que a versão escolhida não mudou para
  `@primeuix/themes`.
- **Cookie path em logout**: se o browser (algumas versões) recusar
  apagar um cookie cujo path não bate exatamente com o request
  current, pode ser preciso emitir delete em `Path=/api/auth/refresh`
  **e** o `Path` dos requests de logout. Decidir no integration test.
- **`SkipAngularBuild` MSBuild flag** durante inner-loop dev:
  documentar em `README.md` do `Sextante.Web` o fluxo `npm start`
  + `dotnet run` com CORS apenas em Development, OU usar o caminho
  do `wwwroot` com `dotnet watch`. Decidir no início da
  implementação.
- **Reset-password manual flow**: validar que o token gerado via
  psql é compatível com o `DataProtectionTokenProvider` configurado
  pelo Identity (depende de chave estável entre psql-side e
  app-side; em dev a key vive em `bin/dev-jwt-key.bin` per Phase 1a).
  Se incompatível, alternativa é expor um endpoint dev-only que
  gere o token; preferimos não, mas é fallback.
- **Storage da `tenantName`**: a claim `tenant_id` está no JWT;
  `tenantName` requer `GET /api/auth/manage/info`. Decidir se o
  signal esperar pela chamada `loadProfile()` ou se incluir
  `tenant_name` numa claim adicional do JWT (Phase 1a não inclui;
  mudaria a `TenantAwareClaimsPrincipalFactory`). Phase 1b assume
  `loadProfile()` para evitar mexer Phase 1a; revisitar se latência
  for percebida.
