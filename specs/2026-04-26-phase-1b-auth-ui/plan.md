# Plan — Phase 1b: Auth UI (Angular)

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (tech-stack §X, requirements.md, roadmap.md).
> Phase 1b assenta em cima da API estável da Phase 1a; a única
> extensão backend é a emissão do refresh token como cookie httpOnly.
> Validação inclui Karma unit tests + manual browser walkthrough +
> sub-agent deep review (🛡️ obrigatório).

---

## 1. Frontend dependency upgrade + workspace prep

- 1.1 Bump `src/Web/Sextante.Web/package.json` Angular 19.2.0 →
  **Angular 21 LTS**: `@angular/{core,common,forms,router,
  platform-browser,platform-browser-dynamic,compiler,animations}`,
  `@angular/cli`, `@angular-devkit/build-angular`. Pinar TypeScript
  na versão exigida pelo Angular 21 (≥5.6 conforme matriz oficial).
- 1.2 Adicionar `primeng`, `primeicons`, `chart.js` (peer da
  PrimeNG Chart), `@primeng/themes` (preset Aura). Versões
  compatíveis com Angular 21.
- 1.3 Adicionar `tailwindcss`, `postcss`, `autoprefixer`,
  `tailwindcss-primeui` (interop de cores PrimeNG ↔ Tailwind).
- 1.4 `npx tailwindcss init -p` em `src/Web/Sextante.Web/` para
  scaffold de `tailwind.config.js` + `postcss.config.js`.
  Configurar `content: ['./src/**/*.{html,ts}']` e
  `darkMode: 'class'` (alinha com a estratégia da Aura).
- 1.5 Atualizar `src/Web/Sextante.Web/src/styles.scss` para importar
  Tailwind base/components/utilities + PrimeIcons CSS. Tema Aura é
  injetado via `providePrimeNG` (ver §4), não via CSS estático.
- 1.6 Confirmar pipeline do Host: `dotnet build -c Release` dispara
  o target `BuildAngular` em `src/Bootstrap/Sextante.Host/Sextante.Host.csproj`
  e copia `dist/` para `wwwroot/`. Sanidade: `npm ci && npm run build
  -- --configuration=production` em `src/Web/Sextante.Web/` sem
  warnings de peer-dep.

## 2. ADR-011: Frontend UI stack

- 2.1 Criar `docs/adr/ADR-011-frontend-ui-stack.md` (segundo ADR
  no repo, depois de ADR-010). Secções: Status (Accepted, 2026-04-26),
  Context, Decision, Consequences, Alternatives Considered.
- 2.2 Decision body cobre: Angular 21 LTS, PrimeNG (preset **Aura**),
  PrimeNG Chart para gráficos, Tailwind CSS (`darkMode: 'class'`),
  Reactive Forms, Angular Signals + services para state (sem NgRx),
  Aura dual theme + Tailwind `dark:` para dark mode.
- 2.3 Alternatives discute: presets Lara / Material / Nora
  (rejeitados — Aura é o default moderno do PrimeNG v18+), NgRx
  (rejeitado — tech-stack §19.1 manda Signals), Bootstrap
  (rejeitado — PrimeNG é a UI library escolhida).
- 2.4 Cross-references: tech-stack §19 (secção inteira), §17
  (decisões), memória `replanning_2026_04_26`, mission §4
  (privacidade por defeito — referência de princípio, não muda
  nesta phase).

## 3. Backend: refresh-token httpOnly cookie

- 3.1 Em `src/Bootstrap/Sextante.Host/Program.cs`, anexar middleware
  pós-`MapIdentityApi` (ou usar `BearerTokenOptions.Events.OnSigningIn`
  se o hook expor o `AuthenticationTicket` antes do flush) que
  intercepta as respostas de `/api/auth/login` e `/api/auth/refresh`,
  extrai o `refreshToken` do JSON body, e adiciona
  `Set-Cookie: refresh_token=<value>; HttpOnly; Secure; SameSite=Strict;
  Path=/api/auth/refresh; Max-Age=604800`.
- 3.2 Atributos do cookie:
  - `HttpOnly = true` (sempre).
  - `Secure = true` em `Production`. Em `Development` (`http://localhost`),
    `Secure = false` apenas se `HttpContext.Request.IsHttps == false`,
    senão manter `true`.
  - `SameSite = Strict`.
  - `Path = /api/auth/refresh`.
  - `Max-Age = 604800` (7 dias, alinhado a tech-stack §6).
- 3.3 Em `/api/auth/refresh`, se o body não traz `refreshToken` mas
  o request tem cookie `refresh_token`, injetar o valor do cookie no
  body antes de chegar ao handler do `MapIdentityApi`. Manter
  retro-compatibilidade com clients que ainda enviam pelo body.
- 3.4 Em `/api/auth/logout`, adicionar `Set-Cookie: refresh_token=;
  Max-Age=0; Path=/api/auth/refresh; HttpOnly; Secure; SameSite=Strict`
  para apagar o cookie no browser.
- 3.5 Adicionar `tests/Sextante.IntegrationTests/Auth/RefreshCookieTests.cs`:
  - Login → response tem header `Set-Cookie` com todos os atributos
    requeridos.
  - Refresh sem body mas com cookie → 200 OK + novo par tokens.
  - Refresh com cookie expirado/inválido → 401.
  - Logout → response tem `Set-Cookie` com `Max-Age=0`.
  Reusar `IdentityIntegrationFixture` (Phase 1a) para evitar nova
  spin-up de Postgres container.
- 3.6 **Não** alterar `specs/2026-04-26-phase-1a-auth-backend/validation.md`
  (spec é histórico imutável); se o curl walkthrough da Phase 1a
  partir devido à mudança, adicionar nota em `validation.md` desta
  phase 1b a explicar o desvio.

## 4. Tailwind + PrimeNG Aura theme integration

- 4.1 `tailwind.config.js`: `darkMode: 'class'`,
  `content: ['./src/**/*.{html,ts}']`, plugin `tailwindcss-primeui`
  para expor cores Aura como utilities Tailwind.
- 4.2 `src/Web/Sextante.Web/src/app/app.config.ts` regista
  `providePrimeNG({ theme: { preset: Aura, options: {
    darkModeSelector: '.dark', cssLayer: { name: 'primeng',
    order: 'tailwind-base, primeng, tailwind-utilities' } } } })`.
  `providePrimeNGAnimations()` se necessário (PrimeNG ≥18).
- 4.3 Criar `src/Web/Sextante.Web/README.md` com a regra:
  **"Tailwind para layout/spacing/responsividade; PrimeNG para
  componentes interativos; nunca os dois a estilizar o mesmo elemento"**
  (tech-stack §19.2).

## 5. App shell layout

- 5.1 `app/layout/auth-shell.component.ts` — shell público para
  rotas não autenticadas (`/signup`, `/login`, `/forgot-password`,
  `/reset-password`). Card centrado em fundo Tailwind, outlet do
  `p-toast`. Sem header / sidenav.
- 5.2 `app/layout/app-shell.component.ts` — shell autenticado.
  Header `p-menubar` mostrando `tenantName` (signal de
  `AuthService`), user menu (Terminar sessão + Tema escuro).
  Layout grid Tailwind. `p-sidebar` placeholder colapsado
  ("Phase 2 chega aí"). `p-toast` outlet. `<router-outlet />`
  para rotas filhas.
- 5.3 `app/core/theme.service.ts` — Signal `isDark` persistida em
  `localStorage` (`sextante.theme = 'dark' | 'light'`). Toggle
  adiciona/remove `.dark` no `document.documentElement`. Valor
  inicial: `localStorage` > `prefers-color-scheme: dark` > `light`.
  `effect()` aplica a classe sempre que o signal muda.
- 5.4 Rota placeholder `/app/dashboard` — componente
  `app/features/dashboard/dashboard.placeholder.component.ts`
  com texto "Phase 2 — Dashboard chega aí". Existe apenas para
  o auth guard ter destino. Será substituído na Phase 2.

## 6. Auth core (services + signals + interceptor + guard)

- 6.1 `app/auth/auth.service.ts` — service com private
  `WritableSignal<AuthState | null>`:
  ```ts
  interface AuthState {
    accessToken: string;
    accessTokenExpiresAt: number;  // epoch ms
    userId: string;
    email: string;
    tenantId: string;
    tenantName: string | null;     // resolvido via /manage/info
    tenantRole: 'Owner' | 'Member' | 'ReadOnly';
  }
  ```
  Computed: `isAuthenticated`, `tenantName`, `tenantRole`.
  Métodos: `signup({email, password, tenantName})`,
  `login({email, password})`, `logout()`, `refresh()`,
  `loadProfile()`. JWT decode para popular o signal no login.
  Refresh atualiza apenas `accessToken` + `accessTokenExpiresAt`.
- 6.2 `app/auth/auth.interceptor.ts` — `HttpInterceptorFn` (function
  interceptor, não classe). Anexa `Authorization: Bearer <accessToken>`
  apenas a requests cujo URL começa por `/api/` **e não** por
  `/api/auth/login` ou `/api/auth/signup`. Em 401 não-`/api/auth/*`,
  chama `AuthService.refresh()`; em sucesso, faz retry da request
  original uma vez; em falha, chama `logout()` e propaga o 401.
  **Storm guard**: `AuthService.refresh()` cacheia a Promise
  in-flight e retorna a mesma a chamadores concorrentes.
- 6.3 `app/auth/auth.guard.ts` — `CanActivateFn` que lê
  `authService.isAuthenticated()`. Se `true`, retorna `true`. Se
  `false`, `router.createUrlTree(['/login'], { queryParams: {
  returnUrl: state.url } })`.
- 6.4 `app/auth/i18n/messages.pt-PT.ts` — mapa de error code do
  Identity (`PasswordTooShort`, `DuplicateUserName`, `InvalidEmail`,
  `InvalidCredentials`, etc.) para mensagem PT-PT. Surfaced via
  `MessageService` (PrimeNG Toast).
- 6.5 Configurar `app.config.ts`:
  `provideHttpClient(withInterceptors([authInterceptor]),
  withFetch(), withXsrfConfiguration({ cookieName: 'XSRF-TOKEN',
  headerName: 'X-XSRF-TOKEN' }))`. Garantir que `HttpClient` envia
  `withCredentials: true` para `/api/auth/refresh` (via
  `req.clone({ withCredentials: true })` no interceptor quando a
  URL é o endpoint de refresh).

## 7. Page: `/signup`

- 7.1 `app/auth/pages/signup.page.ts` — Reactive Form com:
  - `email`: `Validators.required`, `Validators.email`.
  - `password`: `Validators.required`, `Validators.minLength(12)`
    (alinhado com `SignupEndpoint.SignupRequest.Password.MinimumLength`
    em `src/Modules/Identity/.../SignupEndpoint.cs`).
  - `tenantName`: `Validators.required`, `Validators.minLength(2)`,
    `Validators.maxLength(200)`.
  Componentes PrimeNG: `p-inputText` para email + tenantName,
  `p-password` (com `feedback="true"`) para password, `p-button`
  para submit.
- 7.2 Submit: chama `AuthService.signup(...)`; em 201 faz
  `router.navigate(['/login'], { queryParams: { signup: 'success' } })`;
  em erro, mensagem PT-PT via Toast.
- 7.3 Validação inline PT-PT (Reactive Forms `markAllAsTouched` no
  submit). PrimeNG `p-message` para erros server-side de campo.
- 7.4 Registar rota em `app/app.routes.ts` debaixo do
  `AuthShellComponent`.

## 8. Page: `/login`

- 8.1 `app/auth/pages/login.page.ts` — Reactive Form com `email`
  + `password` (sem `feedback` no `p-password`, apenas input).
- 8.2 Submit: `AuthService.login(...)`; em sucesso, navegar para
  `returnUrl` se for path interno (começar por `/`), senão
  `/app/dashboard`. Se query param `signup === 'success'` no
  load, mostrar Toast "Conta criada — faça login".
- 8.3 Links no template:
  - "Esqueci-me da palavra-passe" → `/forgot-password`.
  - "Criar conta" → `/signup`.

## 9. Pages: `/forgot-password` + `/reset-password`

- 9.1 `app/auth/pages/forgot-password.page.ts` — form de campo
  único (`email`). Submit chama `POST /api/auth/forgotPassword`
  (via `HttpClient` direto; **não** via `AuthService` para não
  poluir o estado de auth). Sempre mostra Toast "Se o email existir,
  receberá instruções" (anti-enumeração; alinhado com o comportamento
  do `MapIdentityApi` que silencia para emails não confirmados).
  Se o backend stub-throw (path de email confirmado no Phase 1a),
  o interceptor traduz para Toast PT-PT
  "Envio de emails ainda não disponível — Phase 6".
- 9.2 `app/auth/pages/reset-password.page.ts` — lê `email` e `code`
  de `ActivatedRoute.queryParams`. Form: `newPassword`,
  `confirmPassword` (cross-field validator `passwordsMatch`).
  Submit: `POST /api/auth/resetPassword`. Em sucesso → toast
  "Palavra-passe atualizada" + redirect para `/login`.
  Caveat documentado: como o email não é entregue na Phase 1b,
  o teste manual gera o `code` via psql contra `AspNetUserTokens`
  (passo no `validation.md`).

## 10. Karma unit tests para auth core

- 10.1 `app/auth/auth.service.spec.ts` — cobrir:
  - `signup` chama `POST /api/auth/signup` com payload correto.
  - `login` popula o signal com claims decodificadas do JWT
    (mock de access token com `tenant_role=Owner`).
  - `logout` limpa o signal e chama `POST /api/auth/logout`.
  - `refresh` atualiza apenas `accessToken` (outros campos
    inalterados).
  - `loadProfile` chama `GET /api/auth/manage/info` e popula
    `tenantName`.
- 10.2 `app/auth/auth.interceptor.spec.ts` — cobrir:
  - Anexa `Authorization` a requests `/api/financial/...`.
  - **Não** anexa a `/api/auth/login` nem `/api/auth/signup`.
  - Em 401 não-`/api/auth/*`, chama refresh + retry.
  - Refresh storm: 5 requests concorrentes em 401 → 1 chamada a
    refresh.
  - Refresh falha → chama logout + 401 propaga.
- 10.3 `app/auth/auth.guard.spec.ts` — cobrir:
  - `isAuthenticated() === true` → permite navegação.
  - `isAuthenticated() === false` → retorna `UrlTree` para
    `/login?returnUrl=<path>`.
- 10.4 `app/app.component.spec.ts` — atualizar para montar via
  novo shell (ou apagar se ficar redundante).
- 10.5 Atualizar workflow CI (`.github/workflows/`) para incluir
  step de `npm test -- --watch=false --browsers=ChromeHeadless`
  no Angular workspace.

## 11. Manual E2E walkthrough

- 11.1 Documentar no `validation.md` (§ "Manual browser walkthrough")
  o passo-a-passo do golden path: `docker compose up` →
  `npm start` → signup → toast → redirect login → login → land
  `/app/dashboard` → F5 (cookie refresh recupera sessão) →
  logout → `/login`.
- 11.2 Toggle dark mode → confirmar tema Aura escuro renderiza,
  persiste em `localStorage`, sobrevive a F5.
- 11.3 Forgot-password: caminho de email não confirmado (toast
  silencioso de sucesso) e caminho de email confirmado (toast
  "Phase 6"). Reset-password testado com code gerado via psql.

## 12. Sub-agent deep review (🛡️) + close-out

- 12.1 Invocar sub-agent deep review com prompt focado em:
  - Refresh-cookie security: HttpOnly, Secure, SameSite, Path,
    Max-Age, ausência de leak em logs.
  - Interceptor: prevenção de refresh storms, ordem de retry,
    tratamento de erros de rede vs 401.
  - JWT in-memory: nenhum log do `accessToken`, nenhum
    `localStorage`/`sessionStorage`.
  - AuthGuard: bypass paths (deep links, lazy loading, redirects).
  - Dark-mode FOUC: tema aplicado antes do primeiro paint.
  - Acessibilidade: labels, aria, ordem de tab nos formulários
    (sanity check, não audit completo).
  - Cobertura de mensagens PT-PT vs error codes do Identity.
  Output anexado ao PR.
- 12.2 Marcar `specs/roadmap.md` Phase 1b checkboxes como `[x]`
  via conversa com o agente (AGENTS.md §2 regra 1, nunca à mão).
- 12.3 Skill `changelog` para adicionar entrada com data do merge,
  sumário da Phase 1b.
- 12.4 Commit `Mark phase 1b as complete`. PR
  `phase-1b-auth-ui → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, npm test).
  - Sub-agent deep review verde (findings high resolvidos,
    medium em backlog).
  - Aprovação humana.
