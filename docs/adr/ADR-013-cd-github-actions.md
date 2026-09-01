# ADR-013 — CD automático via GitHub Actions

**Estado**: Adopted
**Data**: 2026-09-01
**Autores**: Thacio

---

## Context

O `README.md` §"Deploy (manual, primeira vez)" declarava explicitamente
que **CD automático fica fora do MVP**: o GitHub Actions correria apenas
`build`, `test` e `format`, e o deploy à VPS seria manual (`git pull` +
`docker compose build` + `up -d` por SSH). `tech-stack.md` §15 já
antecipava o fluxo desejado ("GitHub Actions → build → push de imagem →
SSH + `docker compose pull && up -d`"), mas sem compromisso de quando.

A Phase 6 muda o cálculo por uma razão concreta: o critério de Done do MVP
(`mission.md` §6) é **um mês de uso diário real**. Durante esse mês, cada
irritação encontrada no uso gera uma correcção pequena. Com deploy manual,
cada correcção custa uma sessão de SSH — atrito suficiente para as
correcções se acumularem em vez de saírem, o que enviesa exactamente o
mês que mais importa medir.

A decisão foi tomada no kickoff da Phase 6 (decisão D4 em
`specs/2026-08-26-phase-6-polish-deploy/requirements.md`).

---

## Decision

**CD automático entra no MVP**, com o fluxo mínimo que resolve o atrito e
nada mais:

1. Trigger: `push` para `main` (só `main`; branches de phase continuam a
   correr apenas `build`/`test`/`format`).
2. `needs` dos jobs de build e test existentes — um `main` vermelho nunca
   chega à VPS.
3. Build da imagem a partir do `Dockerfile` multi-stage existente e push
   para registry, com tag `sha-<short>` **e** `latest`.
4. Deploy por SSH: `docker compose pull api && docker compose up -d api`
   no directório do repo na VPS.
5. Smoke check pós-deploy: `curl -fsS https://<dominio>/api/health`. Se
   não responder 200, o job fica vermelho — um deploy que passa
   silenciosamente para um container que não arranca é pior que um deploy
   falhado.
6. `concurrency` a serializar deploys, para dois merges próximos não se
   atropelarem.

Fora de scope deliberadamente: ambiente de staging, blue-green, migrations
como passo separado (correm no startup do Host com lock distribuído),
rollback automático.

---

## Consequences

**Positivas**

- Uma correcção durante o dogfooding chega a produção com um merge.
- A imagem passa a ser um artefacto versionado por SHA: saber o que está a
  correr deixa de depender de `git log` na VPS.
- O smoke check transforma "o deploy correu" em "a app responde".

**Negativas e mitigações**

- **Secrets de produção no GitHub** (chave SSH, host, token do registry).
  Mitigação: chave SSH dedicada ao deploy, sem passphrase, com acesso
  restrito ao utilizador e directório do deploy; rotação documentada no
  README.
- **Um push mal revisto chega a produção.** Mitigação: o gatilho é `main`,
  e `main` só recebe merges de branches de phase revistas (regra dura 3 do
  `AGENTS.md` para phases 🛡️). Não há protecção técnica adicional — num
  repo de um autor, seria cerimónia sem ganho.
- **Rollback é manual**: `docker compose` aponta a uma tag `sha-<short>`
  anterior e `up -d`. Aceitável para um utilizador; o caminho fica
  documentado no README mas **não é exercitado** na Phase 6
  (`validation.md` §"Fora do scope de validação").

---

## Alternatives considered

- **Manter deploy manual (status quo).** Zero secrets e zero superfície
  nova, mas mantém o atrito que motivou a decisão. Rejeitado pelo peso do
  mês de dogfooding.
- **Watchtower** (auto-pull de imagens novas na VPS). Elimina a chave SSH
  no GitHub, mas dá o controlo do momento do deploy a um daemon e não
  permite smoke check pós-deploy nem rollback deliberado. Rejeitado.
- **Runner self-hosted na VPS.** Dispensa SSH e registry, mas põe um
  agente com acesso ao repo dentro da máquina que serve os dados
  financeiros — mais superfície de ataque, contra `mission.md` §4.1.
  Rejeitado.
- **Build na própria VPS** (`git pull && docker compose build` disparado
  por SSH, sem registry). Poupa o registry, mas o build Angular + .NET
  consome CPU/RAM da máquina que está a servir a aplicação, e um build
  falhado deixa o container antigo a correr com código novo no disco.
  Rejeitado; a escolha entre GHCR e este caminho fica registada como
  resolvida a favor do registry.
