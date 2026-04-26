# Changelog

## 2026-04-26

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
