# Plan — Phase 0: Setup do scaffold

> Task groups numbered. Cada sub-task referencia ficheiros concretos do repo
> (tech-stack §1–§16, AGENTS.md §3). Sem código nesta fase do SDD — o código
> é produzido pela implementação contra este `plan.md`.

---

## 1. Solution scaffolding (.NET)

- 1.1 Criar `Sextante.sln` na raiz do repo.
- 1.2 `global.json` com SDK `10.0.x` e `rollForward: latestFeature`
  (tech-stack §1).
- 1.3 `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`,
  `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`,
  `LangVersion=latest`.
- 1.4 `Directory.Packages.props` com `ManagePackageVersionsCentrally=true`.
  Lista de `PackageVersion` arranca vazia e cresce conforme se adicionam
  pacotes.
- 1.5 Estrutura de pastas (com `.gitkeep` onde precisar):
  - `src/Bootstrap/`
  - `src/BuildingBlocks/`
  - `src/Modules/Identity/`
  - `src/Modules/Financial/`
  - `src/Web/`
  - `tests/`

## 2. Host project (ASP.NET Core)

- 2.1 Criar `src/Bootstrap/Sextante.Host/` como ASP.NET Core
  Minimal API. Adicionar à solução.
- 2.2 Endpoint `GET /api/health` devolve `200` com `{ status: "ok", at:
  <utc> }`. **Sem** chamada à BD.
- 2.3 `UseStaticFiles()` + `MapFallbackToFile("index.html")` para servir
  o Angular a partir de `wwwroot` (tech-stack §1, ADR-005).
- 2.4 Serilog → ficheiro com rotação diária em `logs/` (tech-stack §13).
  Sem PII em texto claro (filtros mesmo que vazios neste momento).
- 2.5 OpenAPI auto-gen exposto em `/openapi/v1.json` (tech-stack §8).

## 3. Angular project

- 3.1 `ng new Sextante.Web` em `src/Web/Sextante.Web/`
  (Angular LTS disponível à data do scaffold).
- 3.2 `angular.json` com output em
  `../../Bootstrap/Sextante.Host/wwwroot`.
- 3.3 Substituir o landing component default por uma página PT-PT mínima:
  título "Sextante" + subtítulo "Phase 0 — It works" + version
  stamp lido de `package.json`.
- 3.4 MSBuild target em `Sextante.Host.csproj` que executa
  `npm ci` + `ng build --configuration production` antes de
  `dotnet publish` (e em `dotnet build -c Release`).
- 3.5 Estrutura i18n stubbed (locale único PT-PT, mas estrutura pronta —
  tech-stack §16).

## 4. Architecture tests skeleton

- 4.1 Criar `tests/Sextante.ArchitectureTests/` (xUnit). Adicionar
  à solução.
- 4.2 Referenciar `NetArchTest.Rules` via `Directory.Packages.props`.
- 4.3 Um único smoke test que carrega o assembly do Host e afirma que existe
  — prova que o pipeline de testes funciona em CI. Regras de dependência
  reais (§3.1 da tech-stack) entram na Phase 1.

## 5. Docker

- 5.1 `Dockerfile` multi-stage na raiz:
  - stage 1 `node:lts-alpine` → `npm ci` + `ng build --configuration
    production`
  - stage 2 `mcr.microsoft.com/dotnet/sdk:10.0` → `dotnet publish -c
    Release`
  - stage 3 `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime), copia
    publish output, expõe `80` e `443`.
- 5.2 `docker-compose.yml` com:
  - serviço `api` (build a partir do `Dockerfile`, ports `80:80` e
    `443:443`)
  - serviço `postgres` (`postgres:16-alpine`, sem ports expostos para
    fora — só rede interna)
  - volumes `pgdata`, `letsencrypt-certs`, `logs`
  - rede `internal`
- 5.3 `.env.example` com placeholders:
  - `POSTGRES_PASSWORD=`
  - `ASPNETCORE_URLS=http://+:80;https://+:443`
  - `LETSENCRYPT__EMAIL=`
  - `LETSENCRYPT__DOMAINNAMES=`
- 5.4 `appsettings.json` (committed) com defaults inertes;
  `appsettings.Development.json` para overrides locais (já no `.gitignore`,
  linha 100).

## 6. TLS via LettuceEncrypt

- 6.1 Adicionar `LettuceEncrypt` ao `Directory.Packages.props` e referenciar
  no Host.
- 6.2 `services.AddLettuceEncrypt()` lê domínios + email da configuração
  (`LETSENCRYPT__DOMAINNAMES`, `LETSENCRYPT__EMAIL`).
- 6.3 Persistir estado do cert em `/var/letsencrypt-certs` (volume montado
  em §5.2).
- 6.4 Em `Development`, fallback para Kestrel dev cert. LettuceEncrypt
  ativo apenas em `Production`.

## 7. CI — GitHub Actions

- 7.1 `.github/workflows/ci.yml`:
  - triggers: `push` em `main` e em `phase-*`; `pull_request` para `main`.
  - runner: `ubuntu-latest`.
- 7.2 Job `build`:
  - `actions/checkout@v4`
  - `actions/setup-dotnet@v4` (10.0.x)
  - `actions/setup-node@v4` (LTS) com cache `npm`
  - `dotnet restore`
  - `dotnet build -c Release --no-restore` (corre `ng build` via target
    de §3.4)
- 7.3 Job `test`: `dotnet test -c Release --no-build` — corre apenas o
  smoke test de `Sextante.ArchitectureTests`.
- 7.4 Job `format`: `dotnet format --verify-no-changes`.
- 7.5 Cache para `~/.nuget/packages` e `node_modules`.

## 8. Phase close-out

- 8.1 Atualizar `specs/roadmap.md`: marcar checkboxes da Phase 0 como
  concluídos via conversa com o agente (AGENTS.md §2 rule 1 — nunca
  editar `specs/*` à mão).
- 8.2 Correr a skill `changelog` para adicionar entrada `2026-04-25`
  com sumário dos commits da Phase 0.
- 8.3 Commit `Mark phase 0 as complete` (AGENTS.md §5).
- 8.4 Abrir PR `phase-0-scaffold` → `main`; merge depois de CI verde.

> **Nota**: o primeiro deploy à VPS — provisioning, DNS, emissão de cert
> Let's Encrypt, validação live HTTPS — é responsabilidade da **Phase 6**
> (pré-dogfooding), não da Phase 0. Toda a infra de deploy fica pronta
> aqui (Dockerfile, compose, LettuceEncrypt em config, runbook no
> README), mas o comando `docker compose up` na VPS só corre quando
> chegarmos à Phase 6. Justificação em `requirements.md` *Out of scope*.
