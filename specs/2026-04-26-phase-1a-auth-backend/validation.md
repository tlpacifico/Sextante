# Validation — Phase 1a: Auth backend + Multi-tenancy

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **Multi-tenancy test suite (§4.7) passing.** Os 5 testes da suite
   verdes em CI: cross-tenant read isolado, cross-tenant write/delete
   bloqueado, insert auto-popula `TenantId`, query sem `TenantContext`
   lança, migrations correm como role privilegiado.
2. **NetArchTest enforces module isolation (§3.1).** Testes falham o
   build se qualquer projeto violar a tabela de dependências, incluindo
   a "regra de ouro" de Investment não referenciar
   `Financial.{Domain,Application,Infrastructure,Api}`.
3. **Integration tests via Testcontainers PostgreSQL.** Suite completa
   com Postgres real cobre signup → login → request protegido →
   refresh → logout → tentativa cross-tenant rejeitada (Global Query
   Filter rejeita em alto nível **e** RLS rejeita SQL raw).
4. **ADR-010 (CQRS via Wolverine) committed** em
   `docs/adr/ADR-010-cqrs-via-wolverine.md` com Context, Decision,
   Consequences, Alternatives.
5. **Manual curl walkthrough documentado** em §3 deste ficheiro e
   reproduzível a partir de um `docker compose up` limpo.
6. **Sub-agent deep review (🛡️) verde** com output anexado ao PR
   `phase-1a-auth-backend → main`.
7. **GitHub Actions CI verde** no branch (build + test + format).
8. **`specs/roadmap.md` Phase 1a checkboxes ticados** via conversa
   com o agente.
9. **`CHANGELOG.md`** com entrada `2026-04-26` produzida pela skill
   `changelog`.

## How to verify each bullet

1. **Multi-tenancy test suite.**
   - `dotnet test -c Release --filter FullyQualifiedName~MultiTenancy`
     mostra os 5 testes passados.
   - Inspeccionar o log do teste de cross-tenant SQL raw para
     confirmar que a rejeição vem da DB (mensagem do Postgres contém
     `row-level security policy`), não só do EF Core Global Query
     Filter.
2. **NetArchTest.**
   - `dotnet test -c Release --filter FullyQualifiedName~Architecture`
     mostra todos os testes verdes.
   - Sanity check manual: introduzir reference inválida temporária
     (ex.: `Identity.Domain` referenciar `Identity.Infrastructure`)
     e correr — teste deve falhar com nome do projeto ofensivo.
     Reverter antes de commit.
3. **Integration tests com Testcontainers.**
   - `dotnet test -c Release --filter FullyQualifiedName~Integration`
     arranca container Postgres em cada run, aplica migrations,
     corre suite.
   - Tempo total ≤ 60s em runner standard (warning se passar).
4. **ADR-010.**
   - `git diff main..phase-1a-auth-backend -- docs/adr/` mostra
     `ADR-010-cqrs-via-wolverine.md` com secções Context / Decision
     / Consequences / Alternatives.
   - Alternatives discute explicitamente MediatR + Wolverine e
     plain Application services, com razões para rejeitar cada uma.
5. **Manual curl walkthrough.** Ver §3 abaixo. Cada comando é
   reproduzível em sequência a partir de `docker compose up` limpo.
6. **Sub-agent deep review.**
   - PR `phase-1a-auth-backend → main` no GitHub tem comentário com
     output do agent.
   - Áreas obrigatoriamente cobertas: RLS policies (correctness +
     performance), JWT claims (não vazam dados sensíveis ou enumeram
     utilizadores), Wolverine outbox (idempotência, comportamento de
     retry, ordem de mensagens), signup transaction (rollback paths
     em cada um dos 4 passos), connection interceptor (race
     conditions, requests anonymous vs autenticadas).
   - Findings de severidade alta bloqueiam o merge; severidade média
     viram backlog items.
7. **GitHub Actions verde.**
   - Página de Actions no repo mostra check verde no commit mais
     recente do branch, com `build`, `test` e `format` todos passados.
8. **Roadmap atualizado.**
   - `git diff main..phase-1a-auth-backend -- specs/roadmap.md`
     mostra os checkboxes da Phase 1a marcados como `[x]`.
   - Alterações via conversa com o agente
     (AGENTS.md §2 regra 1).
9. **Changelog.**
   - `git diff main..phase-1a-auth-backend -- CHANGELOG.md` mostra
     nova secção sob `## 2026-04-26` com sumário da Phase 1a.

## 3. Manual curl walkthrough

> Reproduzível a partir de um clone fresh + `docker compose up`.
> Substituir `<EMAIL_A>`, `<EMAIL_B>`, `<PASSWORD>` por valores de
> teste; copiar tokens das respostas anteriores quando aparecem
> placeholders `<ACCESS_TOKEN_A>`, `<REFRESH_TOKEN_A>`, etc.

```bash
# 1. Signup do User A — cria User + Tenant + Membership(Owner)
#    atomicamente e publica UserRegisteredIntegrationEvent na outbox.
curl -fsS -X POST http://localhost/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"<EMAIL_A>","password":"<PASSWORD>","tenantName":"Tenant A"}'
# → 201 Created. Body: { "userId": "...", "tenantId": "..." }

# 2. Login do User A
curl -fsS -X POST http://localhost/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<EMAIL_A>","password":"<PASSWORD>"}'
# → 200 OK. Body: { "accessToken": "...", "refreshToken": "..." }
# Decodificar accessToken (jwt.io) e confirmar claims:
#   - sub      = userId
#   - tenant_id
#   - tenant_role = "Owner"
#   - exp ≈ agora + 15min

# 3. Signup do User B (em paralelo; tenant separado)
curl -fsS -X POST http://localhost/api/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"<EMAIL_B>","password":"<PASSWORD>","tenantName":"Tenant B"}'

# 4. Login do User B
curl -fsS -X POST http://localhost/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<EMAIL_B>","password":"<PASSWORD>"}'

# 5. Manage info do User A (autenticado) — só vê o próprio email + tenant.
curl -fsS http://localhost/api/auth/manage/info \
  -H "Authorization: Bearer <ACCESS_TOKEN_A>"
# → 200 OK. email == <EMAIL_A>.

# 6. Refresh token rotation
curl -fsS -X POST http://localhost/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH_TOKEN_A>"}'
# → 200 OK. Novo par accessToken/refreshToken; o anterior fica inválido.

# 7. Reuso do refresh token antigo é rejeitado
curl -i -X POST http://localhost/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<OLD_REFRESH_TOKEN_A>"}'
# → 401 Unauthorized.

# 8. Endpoint email-dependente é rejeitado loudly (Phase 1a stub)
curl -i -X POST http://localhost/api/auth/forgotPassword \
  -H "Content-Type: application/json" \
  -d '{"email":"<EMAIL_A>"}'
# → 500 Internal Server Error. Log do Host inclui
#   "NotImplementedEmailSender". Phase 1b/Phase 6 vai concretizar.

# 9. Logout
curl -fsS -X POST http://localhost/api/auth/logout \
  -H "Authorization: Bearer <ACCESS_TOKEN_A>"
# → 200 OK. Próximo refresh com qualquer token de A é 401.

# 10. Validar outbox: User A criado → mensagem na outbox sem subscriber.
docker compose exec postgres psql -U sextante_migrations -d sextante \
  -c 'SELECT count(*) FROM messaging.wolverine_outgoing;'
# → 2 (uma por signup; ficam até Phase 2 conectar o Financial subscriber).
```

## Out of scope for validation

- **UI flows** (browser-based signup/login, formulários, error states).
  Phase 1b.
- **SMTP / email delivery** (entrega real, anti-spam, templates).
  Phase 1b ou Phase 6.
- **Performance / load tests** (10k users, 50 tenants concurrent).
  Pós-MVP.
- **Penetration testing / formal security audit.** Pós-MVP.
- **Hangfire / background jobs.** Phase 3.
- **Live VPS deploy + HTTPS cert Let's Encrypt.** Phase 6.
- **CD pipeline auto-deploy on merge.** Pós-MVP.
- **Backup / restore drills.** Phase 6 (pré-dogfooding).
- **Provider real de email** (decidir SMTP vs SendGrid vs Mailgun).
  Phase 6 per tech-stack §12.
- **Multi-membership UX** (mudar tenant ativo, convidar utilizadores).
  Phase 18.
- **2FA, Passkeys, OAuth.** Pós-MVP.
- **Rate limiting, brute-force protection.** Phase 6 polish.
