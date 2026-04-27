# Validation — Phase 1b: Auth UI (Angular)

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **Angular 21 LTS upgrade completo.**
   `src/Web/Sextante.Web/package.json` tem `@angular/core@^21.x`
   (e restante família alinhada); `npm ci && npm run build --
   --configuration=production` em `src/Web/Sextante.Web/` succeeds
   sem warnings de peer-dep.
2. **PrimeNG (Aura) + Tailwind operacionais.** O bundle de produção
   renderiza o auth shell com tema Aura aplicado e utilities Tailwind
   ativas. Toggle de dark mode funciona em browser real e persiste
   em `localStorage`.
3. **Quatro páginas de auth alcançáveis e funcionais.** `/signup`,
   `/login`, `/forgot-password`, `/reset-password` renderizam,
   validam (PT-PT), submetem ao backend. Golden path manual
   (signup → login → rota guardada → refresh do browser → logout)
   passa.
4. **Refresh-token httpOnly cookie wired.** `RefreshCookieTests.cs`
   verde; teste manual confirma `document.cookie` no DevTools
   **não** contém `refresh_token` (HttpOnly esconde-o); F5 em
   `/app/dashboard` mantém sessão viva (cookie alimenta o refresh).
5. **Karma unit tests verdes.** `npm test -- --watch=false
   --browsers=ChromeHeadless` em `src/Web/Sextante.Web/` passa para
   `auth.service.spec.ts`, `auth.interceptor.spec.ts`,
   `auth.guard.spec.ts`.
6. **Suite multi-tenancy da Phase 1a continua verde.**
   `dotnet test -c Release --filter FullyQualifiedName~MultiTenancy`
   inalterada. As mudanças de cookie do Phase 1b não devem regressar
   nenhum teste de tenant isolation.
7. **ADR-011 committed** em
   `docs/adr/ADR-011-frontend-ui-stack.md` com Context / Decision /
   Consequences / Alternatives Considered.
8. **Sub-agent deep review (🛡️) verde** com output anexado ao PR
   `phase-1b-auth-ui → main`. Findings de severidade alta resolvidos
   antes do merge; medium viram backlog.
9. **GitHub Actions CI verde** com build .NET, test .NET, build
   Angular, e `npm test` todos passados.
10. **`specs/roadmap.md` Phase 1b checkboxes ticados** via conversa
    com o agente (AGENTS.md §2 regra 1).
11. **`CHANGELOG.md`** com nova entrada datada produzida pela skill
    `changelog`, sumarizando Phase 1b.
12. **Sanity check responsivo** per `tech-stack.md` §19.5 e
    `mission.md` §4.6: ambos os shells e as 4 páginas auth
    renderizam corretamente em viewports 375 × 667 (iPhone SE),
    768 × 1024 (iPad portrait) e 1280 × 800 (desktop) — sem
    overflow horizontal de página, sem elementos cortados, sidebar
    em modo overlay em mobile.

## How to verify each bullet

1. **Angular 21.**
   - `cat src/Web/Sextante.Web/package.json | jq '.dependencies' |
     grep -E '"@angular/(core|common|forms|router|platform-browser)"'`
     mostra `^21.x` em todos.
   - `cd src/Web/Sextante.Web && npx ng version` mostra Angular CLI
     21.x.
   - `npm ci` termina sem `peer dep WARN`.
2. **PrimeNG + Tailwind.**
   - Browser em `http://localhost/login`: inputs renderizados como
     componentes Aura (border arredondado, focus ring); espaçamento
     vem de classes `p-4`, `gap-2`, etc.
   - Toggle dark mode no header → body ganha class `.dark`,
     background flips, Aura aplica palette escura. F5 → tema
     persistido (Application → Local Storage tem `sextante.theme`).
3. **Quatro páginas.**
   - Walkthrough manual (§ "Manual browser walkthrough") completa
     todos os 12 passos com os outcomes esperados.
4. **Cookie httpOnly.**
   - `dotnet test --filter FullyQualifiedName~RefreshCookie` verde.
   - DevTools → Application → Cookies → `localhost`: existe
     `refresh_token` com flag `HttpOnly ✓`, `Secure ✓`,
     `SameSite=Strict`, `Path=/api/auth/refresh`.
   - Console: `document.cookie` não inclui `refresh_token`.
   - F5 em `/app/dashboard` → continua na rota, sem re-login.
5. **Karma.**
   - `cd src/Web/Sextante.Web && npm test -- --watch=false
     --browsers=ChromeHeadless --code-coverage` passa.
   - CI repete o comando.
6. **Multi-tenancy regressão zero.**
   - `dotnet test -c Release --filter FullyQualifiedName~MultiTenancy`
     mostra os 5 testes da Phase 1a verdes; tempo total ≤ 60s.
7. **ADR-011.**
   - `git diff main..phase-1b-auth-ui -- docs/adr/` mostra
     `ADR-011-frontend-ui-stack.md` com as 4 secções.
   - Alternatives discute Lara, Material, Nora, NgRx, Bootstrap
     com motivos para rejeitar cada um.
8. **Sub-agent review.**
   - PR no GitHub tem comentário com output do agent.
   - Áreas obrigatoriamente cobertas: refresh-cookie security
     (HttpOnly, Secure, SameSite, Path, Max-Age, ausência de log
     leaks), interceptor (storm prevention, retry order, network
     error vs 401), JWT in-memory (sem logging do access token, sem
     localStorage/sessionStorage), AuthGuard (deep links, lazy
     loading, redirects), dark-mode FOUC, acessibilidade básica
     (labels + aria + ordem de tab nos forms), cobertura PT-PT vs
     error codes Identity.
   - Findings high resolvidos no branch antes do merge; medium em
     backlog.
9. **CI verde.**
   - GitHub Actions no commit mais recente do `phase-1b-auth-ui`
     mostra todos os jobs verdes.
10. **Roadmap.**
    - `git diff main..phase-1b-auth-ui -- specs/roadmap.md` mostra
      Phase 1b com `[x]` em cada bullet, alterado via conversa com
      o agente.
11. **Changelog.**
    - `git diff main..phase-1b-auth-ui -- CHANGELOG.md` mostra
      secção nova com data do merge sumarizando Phase 1b.

12. **Sanity check responsivo.**
    - DevTools → device toolbar → ciclar entre `iPhone SE`
      (375 × 667), `iPad` (768 × 1024) e responsive desktop
      (1280 × 800).
    - Para cada viewport, navegar `/login`, `/signup`,
      `/forgot-password`, `/reset-password`, `/app/dashboard`
      (após login):
      - Nenhum scroll horizontal no `<body>` (apenas, se
        existir, dentro de containers internos).
      - Card auth (`AuthShell`) cabe inteiro em 375 px com
        margens; botões com touch target ≥ 44 px.
      - `AppShell` em mobile: botão hamburger visível, email do
        user escondido (`hidden md:inline`), tenant chip visível.
        `p-drawer` abre como overlay (não desloca conteúdo).
      - Toggle dark mode acessível em todos os viewports
        (do menu user em desktop, ou do botão dedicado mobile).

## Manual browser walkthrough

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir `<EMAIL_A>`, `<PASSWORD>`, `<TENANT_NAME>` por valores
> de teste. Para passos com psql, usar credenciais de
> `sextante_migrations`.

```bash
# Pré-requisito: docker compose up (api + postgres) — landing está
# em http://localhost/. Se a Angular dev server for usada para
# inner loop, npm start em src/Web/Sextante.Web/ + dotnet run em
# src/Bootstrap/Sextante.Host (ver README.md do Sextante.Web).
```

1. Abrir `http://localhost/` → auth guard envia para `/login`
   (rota raiz é guardada e não há sessão).
2. Clicar em **"Criar conta"** → navegação para `/signup`.
3. Submeter form vazio → validações PT-PT inline em cada campo
   (email, password, tenantName).
4. Submeter `email=tester+a@example.com`, `password=PasswordSegura1!`
   (≥12 chars), `tenantName=Test Tenant A`. Esperado: toast
   "Conta criada" + redirect para `/login?signup=success`.
5. Em `/login`, submeter as mesmas credenciais. Esperado: land em
   `/app/dashboard` (placeholder "Phase 2 chega aí").
   Network tab: response de `/api/auth/login` traz header
   `Set-Cookie: refresh_token=...; HttpOnly; Secure;
   SameSite=Strict; Path=/api/auth/refresh; Max-Age=604800`.
   Console: `document.cookie` **não** mostra `refresh_token`.
6. **F5 (browser refresh)** → continua em `/app/dashboard`,
   continua autenticado (interceptor usou o cookie via
   `/api/auth/refresh` para obter novo access token).
7. Abrir user menu no header → **"Tema escuro"**. Página flips
   para tema Aura escuro. F5 → tema persiste (Application →
   Local Storage → `sextante.theme = 'dark'`).
8. User menu → **"Terminar sessão"** → redirect para `/login`.
   Application → Cookies: `refresh_token` removido (Set-Cookie
   com `Max-Age=0` foi enviado).
9. Tentar navegar diretamente para `/app/dashboard` →
   redirect para `/login?returnUrl=%2Fapp%2Fdashboard`. Login →
   land em `/app/dashboard` (returnUrl honrado).
10. **Forgot-password (email não confirmado).** Em `/login`,
    clicar **"Esqueci-me da palavra-passe"** → submeter
    `tester+a@example.com`. Esperado: toast silencioso de sucesso
    "Se o email existir, receberá instruções" (anti-enumeração;
    `MapIdentityApi` no-op porque o email não está confirmado;
    o stub `NotImplementedEmailSender` não é invocado).
11. **Forgot-password (email confirmado).** Setup manual:
    ```bash
    docker compose exec postgres psql -U sextante_migrations \
      -d sextante \
      -c "UPDATE shared.\"AspNetUsers\" SET \"EmailConfirmed\" = true \
          WHERE \"Email\" = 'tester+a@example.com';"
    ```
    Repetir passo 10. Esperado: backend invoca `IEmailSender`,
    stub lança, interceptor surfaces toast PT-PT
    "Envio de emails ainda não disponível — Phase 6".
    Confirma que a página está wired mas o entrega de email é
    Phase 6.
12. **Reset-password manual.** Gerar code via psql:
    ```bash
    # Identity usa DataProtectionTokenProvider; o code não é o valor
    # raw da AspNetUserTokens. Em vez disso, usar a API:
    docker compose exec api dotnet run --project tools/MintResetToken \
      --email tester+a@example.com
    # Output: "code=<URL-encoded token>"
    # OU: chamar /api/auth/forgotPassword num user com email confirmado
    # (passo 11) e ler o code dos logs do Serilog (filtrar por
    # "ResetPassword token generated").
    ```
    Construir URL `http://localhost/reset-password?email=...&code=<code>`,
    abrir, submeter `newPassword=NovaPassword2024!` +
    `confirmPassword=...`. Esperado: toast "Palavra-passe atualizada"
    + redirect para `/login`. Login com nova password → succeeds.

## Out of scope for validation

- **Email delivery / spam-folder testing.** Phase 6 (decisão SMTP).
- **E2E automatizado em browser real (Playwright/Cypress).**
  Manual walkthrough é o artefacto da Phase 1b.
- **Audit de acessibilidade (axe-core, screen-reader walkthrough).**
  Sub-agent review faz sanity check; audit formal em Phase 6.
- **Performance / Lighthouse scores.** Pós-MVP.
- **CSP headers / security headers tuning.** Phase 6.
- **Live VPS deploy + cert Let's Encrypt.** Phase 6.
- **Multi-tenant browser session isolation** (dois users em duas
  tabs simultaneamente). Coberto pela Phase 1a no API layer; UI-layer
  test fica para Phase 18 quando multi-membership UI chegar.
- **Audit responsivo formal** (Lighthouse mobile score, screen-reader
  walkthrough mobile, testes em device físicos). O baseline responsivo
  (mobile-first, breakpoints `tech-stack.md` §19.5, sanity check em 3
  viewports) é IN scope desta phase per DoD #12; audit formal
  pós-MVP.
- **Penetration testing / security audit formal.** Pós-MVP.
- **Browser cross-version testing** (Safari, Firefox, Edge).
  Chromium-based suficiente para Phase 1b; revisitar em Phase 6.
