# Changelog

## 2026-04-26

- Phase 1b — Auth UI completa (Angular)
- Upgrade Angular 19 → Angular 21 LTS (TypeScript ~5.9, zone.js ~0.16)
- PrimeNG 21 (preset Aura, dark mode via `darkModeSelector: '.dark'`), PrimeIcons, Chart.js (peer da PrimeNG Chart)
- Tailwind CSS 3.4 + tailwindcss-primeui (regra: Tailwind para layout, PrimeNG para componentes — `Sextante.Web/README.md`)
- ADR-011 — Frontend UI stack (PrimeNG + Tailwind + Signals; alternativas Material / Bootstrap / NgRx rejeitadas)
- `AuthService` com `WritableSignal<AuthState>` privado, computed `isAuthenticated`/`tenantName`/`tenantRole`, signup/login/logout/refresh/loadProfile, storm guard via Promise in-flight cacheada
- `authInterceptor` (HttpInterceptorFn) anexa `Authorization: Bearer ...` a `/api/*`, ignora endpoints marcados com `SKIP_AUTH`, refresh + retry em 401, força logout em refresh-failure
- `authGuard` (`CanActivateFn`) baseado em signal, redirect para `/login?returnUrl=<path>`
- Páginas `/signup`, `/login`, `/forgot-password`, `/reset-password` com Reactive Forms, validação PT-PT, `error-translator` para Identity error codes
- `AuthShellComponent` (card centrado + toast outlet) e `AppShellComponent` (`p-menubar` com tenant + theme toggle + user menu, `p-drawer` placeholder, toast outlet)
- `ThemeService` (signal `isDark`, `localStorage('sextante.theme')`, fallback `prefers-color-scheme`); script inline em `index.html` aplica `.dark` antes do primeiro paint para evitar FOUC
- Backend: middleware `RefreshTokenCookieMiddleware` em `Sextante.Host` emite `Set-Cookie: refresh_token=...; HttpOnly; Secure; SameSite=Strict; Path=/api/auth/refresh; Max-Age=604800` em `/login` + `/refresh`, aceita o cookie como fallback ao body em `/refresh`, e limpa o cookie em `/logout`
- Backend: novos endpoints `POST /api/auth/logout` (204, requer auth) e `GET /api/auth/me` (perfil + tenant claims; necessário porque o bearer ticket é opaque, não JWT)
- 4 novos integration tests (`RefreshCookieTests`) — atributos do cookie no login, refresh com cookie + sem body, refresh com cookie inválido → 401, logout emite delete-cookie
- 16 Karma unit tests (`auth.service.spec`, `auth.interceptor.spec`, `auth.guard.spec`) cobrindo signup/login/logout/refresh/storm-guard/guard-redirect
- CI: novo step `npm test -- --watch=false --browsers=ChromeHeadless` no job de testes
- `@primeng/themes` (deprecado) substituído por `@primeuix/themes` (open question da Phase 1b resolvida)
- Adicionar feature spec da Phase 1a (Auth backend + Multi-tenancy)
- Implementar módulo `Identity` (5 projetos) com `AppUser`, `Tenant`, `Membership` e schema `shared`
- Wolverine 5.x como mediator in-process + bus inter-módulos com outbox PostgreSQL transacional (ADR-010)
- Migrations EF (`Initial` + `EnableRowLevelSecurity` + `HardenTenantSentinelGuard`) com lock distribuído via `pg_advisory_lock`
- Row-Level Security em `shared.Memberships` e `shared.Tenants` com policies baseadas em `current_setting('app.current_tenant_id')`; sentinel anonymous é UUID estruturalmente impossível com CHECK constraint a proibi-lo como `tenant_id` real
- Roles `sextante_migrations` (BYPASSRLS) e `sextante_app` (NOBYPASSRLS) bootstrap via `infra/postgres/01-bootstrap-roles.sh`, alimentado por env vars obrigatórias (sem defaults `changeme_*`)
- `TenantAwareClaimsPrincipalFactory` injeta `tenant_id` e `tenant_role` no JWT; `ITenantContext` fail-loud
- `MapIdentityApi<AppUser>()` em `/api/auth/*` com `/register` desativado (404) — `POST /api/auth/signup` é o único path de criação atómica de User + Tenant + Membership
- Identity defaults endurecidos: password ≥ 12 chars, lockout 5 fails / 15 min, email único; rate limiter fixed-window 30 req/IP/min em `/api/auth/*`
- `NotImplementedEmailSender` stub para endpoints email-dependentes (Phase 1b/Phase 6 substitui)
- `TenantConnectionInterceptor` reescreve GUC em cada checkout do pool e faz reset no close (defesa em profundidade contra leak entre tenants)
- Architecture tests (NetArchTest) codificam tabela §3.1 do tech-stack — 9 testes verdes
- Integration tests com Testcontainers Postgres — cobrem signup, multi-tenancy (incluindo assert do SqlState `42501` da RLS policy), regressão pool leak via WebApplicationFactory, login → JWT com `tenant_id` claim, e auto-população de `TenantId` pelo interceptor
- Sub-agent deep review verde (round 1 fechou findings HIGH RLS-em-Tenants, pool leak, claims via privileged conn, ordem de save no signup; round 2 endureceu critical/high adicionais — ver sub-agent log)
- Mark phase 1a as complete
- Implementar Phase 1a — Auth backend + Multi-tenancy
- Fechar findings HIGH do sub-agent review
- Endurecer Phase 1a — round 2 sub-agent review

### Pendentes para Phase 2 (registados em ADR-010 §"Pendentes")

- Refactorar signup para handler Wolverine com `Policies.AutoApplyTransactions`: hoje `bus.PublishAsync` corre **pós-commit** (at-most-once); o outbox transacional só é exercido quando o módulo Financial introduz o primeiro subscriber.
- Substituir a connection BYPASSRLS direta em `TenantAwareClaimsPrincipalFactory` por `IMembershipReader` exposto em `PublicApi`, atrás de gate tipado.

## 2026-04-25

- Diferir primeiro deploy à VPS para a Phase 6
- Adicionar scaffold Phase 0 e renomear projeto para Sextante
- Introduce SDD foundation
- Mark phase 0 as complete
