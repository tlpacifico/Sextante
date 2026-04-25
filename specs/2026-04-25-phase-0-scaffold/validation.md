# Validation — Phase 0: Setup do scaffold

> Cada bullet de Definition of Done tem um caminho de verificação concreto.
> Bullets sem caminho de verificação não pertencem aqui — são wishlist.

## Definition of done

1. **Local docker compose funcional.** `docker compose up` numa máquina
   de dev serve a landing Angular em `http://localhost/` e
   `/api/health` devolve `200`.
2. **GitHub Actions verde no branch.** Os 3 jobs (`build`, `test`,
   `format`) passam no `phase-0-scaffold`.
3. **Architecture test smoke test existe e passa.**
   `tests/Sextante.ArchitectureTests/` corre 1 teste no CI e
   passa.
4. **`specs/roadmap.md` Phase 0** com todos os checkboxes ticados.
5. **`CHANGELOG.md`** com entrada `2026-04-25` produzida pela skill
   `changelog`.

## How to verify each bullet

1. **Local docker compose.**
   - `docker compose up --build` numa máquina limpa (sem state prévio).
   - `curl -fsS http://localhost/api/health` devolve `{ "status":
     "ok", "at": "..." }` com HTTP 200.
   - Browser em `http://localhost/` mostra a landing PT-PT do Angular
     com `<title>Sextante</title>`.
2. **GitHub Actions verde.**
   - Push do branch `phase-0-scaffold`.
   - Página de Actions no repo mostra check verde no commit mais
     recente, com os 3 jobs concluídos.
3. **Architecture test smoke test.**
   - Localmente: `dotnet test -c Release` mostra `Passed: 1, Failed: 0`
     em `Sextante.ArchitectureTests`.
   - CI: o job `test` tem o mesmo output e está verde.
4. **Roadmap atualizado.**
   - `git diff main..phase-0-scaffold -- specs/roadmap.md` mostra os
     checkboxes da Phase 0 marcados como `[x]`.
   - As alterações foram feitas via conversa com o agente, não à mão
     (AGENTS.md §2 regra 1).
5. **Changelog.**
   - `git diff main..phase-0-scaffold -- CHANGELOG.md` mostra nova
     secção sob `## 2026-04-25` com sumário da Phase 0.

## Out of scope for validation

- **Live VPS URL sobre HTTPS** — validado em Phase 6 (primeiro deploy +
  cert Let's Encrypt + dogfooding).
- Queries à BD, EF Core, migrations, suite de multi-tenancy isolation —
  validados na Phase 1.
- Fluxos de auth (login, signup, refresh, logout) — Phase 1.
- CD pipeline (deploy automático em push) — fora do MVP.
- SMTP / envio de email — Phase 1+ quando Identity entrar.
- Performance, monitorização, alerting — pós-MVP (tech-stack §13).
- Backups e restore de teste — Phase 6 (dogfooding).
