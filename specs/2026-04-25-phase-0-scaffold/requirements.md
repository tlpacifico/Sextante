# Requirements — Phase 0: Setup do scaffold

## Goal

Estabelecer um monorepo .NET 10 + Angular **pronto para deploy** via
Docker Compose (Host + Postgres + LettuceEncrypt configurados, runbook
no README), com gates de CI (build/test/format) em vigor e um endpoint
`/api/health` no-op. É a fundação que a Phase 1 (Auth + multi-tenancy)
vai estender — não tenta resolver nada do que vem depois, apenas
garantir que existe terreno seguro para construir.

A `mission.md` §6 diz que o MVP só está completo quando o utilizador usa
o sistema 1 mês completo sem reabrir Excel — e o dogfooding requer um
sistema **deployed**. Phase 0 entrega o stack pronto a deployar; o
**primeiro deploy à VPS** é responsabilidade da Phase 6 (pré-dogfooding),
para evitar arrastar gestão de DNS, certificados e exposição pública
durante as Phases 1–5 onde a prioridade é iteração local.

## In scope

- Solução `.sln` na raiz + `global.json` + `Directory.Build.props` +
  `Directory.Packages.props`.
- Estrutura de pastas (`src/Bootstrap`, `src/BuildingBlocks`,
  `src/Modules/{Identity,Financial}`, `src/Web`, `tests/`) — pastas vazias
  com `.gitkeep` onde necessário.
- 1 projeto Host (`Sextante.Host`) com `/api/health` no-op,
  Serilog → ficheiro, OpenAPI gen, `UseStaticFiles + MapFallbackToFile`
  para o Angular.
- 1 projeto Angular (`Sextante.Web`) que faz build para
  `wwwroot` do Host, com landing PT-PT mínima.
- 1 projeto `Sextante.ArchitectureTests` (xUnit) com 1 smoke test.
- `Dockerfile` multi-stage (Node → .NET SDK → ASP.NET runtime).
- `docker-compose.yml` com `api` + `postgres` (`postgres:16-alpine`) +
  volumes (`pgdata`, `letsencrypt-certs`, `logs`).
- `.env.example` + `appsettings.json`.
- GitHub Actions CI (`.github/workflows/ci.yml`) com 3 jobs: `build`,
  `test`, `format`.
- LettuceEncrypt configurado no Host, ativo apenas em `Production`
  quando `LETSENCRYPT__EMAIL` e `LETSENCRYPT__DOMAINNAME` estão
  preenchidos (config dormente até Phase 6).
- Secção "Deploy" no `README.md` como **runbook** — passos para o
  primeiro deploy, mas execução real fica para a Phase 6.

## Out of scope

- **Esqueletos de 5-projetos por módulo** (deferido para Phase 1 no caso
  de Identity, Phase 2 no caso de Financial). Pastas dos módulos ficam
  vazias.
- **EF Core, DbContext, migrations, seed.** Toda interação com a BD entra
  na Phase 1.
- **Multi-tenancy infra** (Global Query Filters, Row-Level Security,
  `DbConnectionInterceptor`, `ITenantContext`) — Phase 1 e §3.1 da
  tech-stack.
- **Hangfire** (Phase 3+) e **Wolverine** (Phase 1+ para eventos de
  integração).
- **Identity / `MapIdentityApi` / signup customizado / refresh tokens**
  — Phase 1.
- **Primeiro deploy à VPS** (provisioning, DNS A record, emissão Let's
  Encrypt, validação live HTTPS) — **diferido para a Phase 6**.
  *Why*: `mission.md` §6 acopla "deployed" com "1 mês de dogfooding";
  faz sentido executar ambos no mesmo bloco operacional, e poupa às
  Phases 1–5 a fricção de manter um endpoint público vivo.
  **How to apply**: o stack Docker fica pronto em Phase 0 e validado
  localmente; o `docker compose up` na VPS é a primeira tarefa
  operacional da Phase 6.
- **CD pipeline.** Deploy é manual nesta fase. Documentado, não
  automatizado.
- **Seq / OpenTelemetry / Prometheus / Grafana.** Pós-MVP (tech-stack
  §13).
- **Backups** (`pg_dump` cron + restore de teste) — Phase 6 dogfooding.

## Decisions

- **Host é DB-blind na Phase 0.** `/api/health` devolve `200`
  incondicionalmente, sem ligar ao Postgres. *Why*: mantém Phase 0 mínima
  e desacoplada da Phase 1; o Postgres no compose é capacidade reservada,
  ainda não consumida. **How to apply**: nenhum `DbContext` é referenciado
  no Host até à Phase 1.
- **Pastas dos módulos ficam vazias.** Sem `.csproj` placeholder. *Why*:
  a primeira tarefa concreta da Phase 1 é montar o layout 5-projetos do
  Identity; pré-criar agora seria adivinhação. **How to apply**: só
  `src/Modules/Identity/.gitkeep` e `src/Modules/Financial/.gitkeep`.
- **LettuceEncrypt é o caminho de TLS, sem reverse proxy externo.** *Why*:
  invariante da tech-stack §1. **How to apply**: nada de Nginx/Caddy à
  frente do Kestrel.
- **Postgres `16-alpine` no compose.** *Why*: tech-stack §1 diz
  "PostgreSQL 16+"; 16 é a baseline atual e `alpine` minimiza imagem.
  **How to apply**: pin no `docker-compose.yml`, sem `latest`.
- **.NET SDK pinned via `global.json` com `rollForward: latestFeature`.**
  *Why*: tech-stack §1, evita drift entre dev e CI.
- **CI em `ubuntu-latest` com 3 jobs `build`, `test`, `format`.** *Why*:
  roadmap linha 28 lista explicitamente "build, test, format check" —
  copia esse texto.
- **Idioma**: documentação e mensagens UI em PT-PT; código e identifiers
  em inglês (AGENTS.md §2 regra 5, tech-stack §16).

## Context / references

- `specs/roadmap.md` — secção "Phase 0 — Setup do scaffold" (linhas
  20–31).
- `specs/tech-stack.md` — §1 (stack consolidada), §2 (estrutura de
  solução), §13 (logs Serilog → ficheiro), §15 (deployment Docker
  Compose), §16 (idioma).
- `specs/mission.md` — §6 (critério de sucesso requer sistema deployable
  para dogfooding).
- `AGENTS.md` — §3.2 (regras de module isolation, mesmo que ainda não
  apliquem em Phase 0), §5 (convenção de branch `phase-N-<kebab-name>`),
  §7 (skill `changelog` antes do merge).

## Open questions

- **VPS provider, sizing e DNS** migram para o spec da Phase 6 quando
  ela for arrancada. Não são enforcement da Phase 0.
- **Versão exata do Angular LTS** à data do scaffold — usar a que
  `ng new` resolver no momento; pinning estrito não é necessário em
  Phase 0.
