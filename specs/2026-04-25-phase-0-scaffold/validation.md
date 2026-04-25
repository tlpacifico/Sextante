# Validation — Phase 0: Setup do scaffold

> Cada bullet de Definition of Done tem um caminho de verificação concreto.
> Bullets sem caminho de verificação não pertencem aqui — são wishlist.

## Definition of done

1. **Local docker compose funcional.** `docker compose up` numa máquina
   de dev serve a página Angular em `http://localhost:<port>/` e
   `/api/health` devolve `200`.
2. **GitHub Actions verde no branch.** Os 3 jobs (`build`, `test`,
   `format`) passam no `phase-0-scaffold`.
3. **Architecture test smoke test existe e passa.**
   `tests/Sextante.ArchitectureTests/` corre 1 teste no CI e
   passa.
4. **Live VPS URL acessível sobre HTTPS.** Página Angular renderiza,
   `/api/health` devolve `200`, certificado emitido por Let's Encrypt.
5. **`specs/roadmap.md` Phase 0** com todos os checkboxes ticados.
6. **`CHANGELOG.md`** com entrada `2026-04-25` produzida pela skill
   `changelog`.

## How to verify each bullet

1. **Local docker compose.**
   - `docker compose up --build` numa máquina limpa (sem state prévio).
   - `curl -fsS http://localhost:8080/api/health` devolve `{ "status":
     "ok", "at": "..." }` com HTTP 200.
   - Browser em `http://localhost:8080/` mostra a landing PT-PT do
     Angular.
2. **GitHub Actions verde.**
   - Push do branch `phase-0-scaffold`.
   - Página de Actions no repo mostra check verde no commit mais
     recente, com os 3 jobs concluídos.
3. **Architecture test smoke test.**
   - Localmente: `dotnet test -c Release` mostra `Passed: 1, Failed: 0`
     em `Sextante.ArchitectureTests`.
   - CI: o job `test` tem o mesmo output e está verde.
4. **Live VPS URL.**
   - Browser em `https://<domain>/` renderiza a landing.
   - `curl -I https://<domain>/api/health` devolve `HTTP/2 200`.
   - `openssl s_client -connect <domain>:443 -servername <domain>
     </dev/null 2>/dev/null | openssl x509 -noout -issuer` mostra
     `issuer=C=US, O=Let's Encrypt, CN=...` (intermediate atual da LE).
5. **Roadmap atualizado.**
   - `git diff main..phase-0-scaffold -- specs/roadmap.md` mostra os
     checkboxes da Phase 0 marcados como `[x]`.
   - As alterações foram feitas via conversa com o agente, não à mão
     (AGENTS.md §2 regra 1).
6. **Changelog.**
   - `git diff main..phase-0-scaffold -- CHANGELOG.md` mostra nova
     secção sob `## 2026-04-25` com sumário da Phase 0.

## Out of scope for validation

- Queries à BD, EF Core, migrations, suite de multi-tenancy isolation —
  validados na Phase 1.
- Fluxos de auth (login, signup, refresh, logout) — Phase 1.
- CD pipeline (deploy automático em push) — fora do MVP.
- SMTP / envio de email — Phase 1+ quando Identity entrar.
- Performance, monitorização, alerting — pós-MVP (tech-stack §13).
- Backups e restore de teste — Phase 6 (dogfooding).
