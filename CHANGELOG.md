# Changelog

## 2026-04-26

- Adicionar feature spec da Phase 1a (Auth backend + Multi-tenancy)
- Implementar módulo `Identity` (5 projetos) com `AppUser`, `Tenant`, `Membership` e schema `shared`
- Wolverine 5.x como mediator in-process + bus inter-módulos com outbox PostgreSQL transacional (ADR-010)
- Migrations EF (`Initial` + `EnableRowLevelSecurity`) com lock distribuído via `pg_advisory_lock`
- Row-Level Security em `shared.Memberships` com policy `tenant_isolation` baseada em `current_setting('app.current_tenant_id')`
- Roles `sextante_migrations` (BYPASSRLS) e `sextante_app` (NOBYPASSRLS) bootstrap reutilizado entre testes e produção
- `TenantAwareClaimsPrincipalFactory` injeta `tenant_id` e `tenant_role` no JWT; `ITenantContext` fail-loud
- `MapIdentityApi<AppUser>()` em `/api/auth/*` + custom `POST /api/auth/signup` (User + Tenant + Membership atómicos)
- `NotImplementedEmailSender` stub para endpoints email-dependentes (Phase 1b/Phase 6 substitui)
- Architecture tests (NetArchTest) codificam tabela §3.1 do tech-stack — 9 testes verdes
- Integration tests com Testcontainers Postgres — 6 testes verdes (signup, multi-tenancy, RLS no DB layer)

## 2026-04-25

- Diferir primeiro deploy à VPS para a Phase 6
- Adicionar scaffold Phase 0 e renomear projeto para Sextante
- Introduce SDD foundation
- Mark phase 0 as complete
