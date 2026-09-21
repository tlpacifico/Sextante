# Sextante

> Sistema de **gestão financeira pessoal + carteira de investimentos**, hosted em VPS própria.
> Cada um navega por critérios próprios — daí o nome.
> Stack: **.NET 10 + PostgreSQL 16 + Angular**, **Modular Monolith**, multi-tenant.

Este `README.md` é o **input de stakeholders** para a Constitution SDD (`specs/mission.md`, `specs/tech-stack.md`, `specs/roadmap.md`). A documentação canónica detalhada vive no Obsidian Vault — ver secção *Referências canónicas* no fim.

---

## Sobre

Plataforma para investidor pessoal técnico (Brasil/Portugal) que hoje precisa de **duas ou três ferramentas + Excel** para fazer o que devia ser uma coisa só: controlar despesas/receitas do dia-a-dia **e** gerir carteira de investimentos com critérios próprios (não com o consenso de mercado), em **multi-moeda nativa** e com rastreabilidade completa do câmbio aplicado.

O MVP foca **só no módulo financeiro**. O módulo de investimento é Fase 2 e é onde está a verdadeira diferenciação face ao Mobills, Organizze, YNAB, Status Invest, Investidor10, etc.

---

## Stakeholders & input

### Thacio — dono do produto e utilizador primário (single stakeholder no MVP)

- **Persona**: investidor pessoal técnico, opera em **BRL, EUR, USD**, ativos em mais de uma jurisdição (Brasil + Europa), tem rendimento estável, investe regularmente, aplica critérios próprios de seleção (ex.: Fisher 15 Questions, checklists quantitativas).
- **Hoje usa**: Excel + 2 apps (uma de gestão financeira, outra de carteira). Quer reduzir a um sistema só.
- **Skills técnicos**: confortável com importações de CSV, definição de regras, uso de stack .NET. Vai ele próprio implementar.
- **Hosting**: VPS própria (decidido). Não quer dependência de SaaS externo para os dados financeiros.
- **Validação do MVP**: o sistema é considerado "Done" quando o Thacio o usar **1 mês completo** sem reabrir Excel ou outra app financeira.

> Nota: este projeto começa como **single-tenant pessoal**, mas é arquitetado **multi-tenant desde o dia 1** (User → Tenant → Membership) para abrir caminho a uso por casal/contabilista (Fase 5) e eventualmente como SaaS público (Fase 7) sem refactor estrutural.

---

## Princípios de produto (tie-breakers em decisões ambíguas)

1. **Privacidade por defeito.** Os dados de um tenant nunca tocam noutro. Sem exceções.
2. **Multi-moeda nativa, não bolt-on.** Toda transação carrega moeda original e câmbio aplicado no momento. Storage em moeda original; conversão só no reporting.
3. **Automação sobre manualidade — mas auditável.** Recorrentes, regras de categorização e sugestões de aporte são automáticos, mas o utilizador vê e pode reverter qualquer decisão automática.
4. **Self-hosted-friendly.** Dependências externas (APIs de cotações, FX, SMTP) têm fallback ou degradação graciosa.
5. **MVP estreito e profundo, não largo e raso.** Antes de adicionar módulo novo, o existente tem de estar sólido.

---

## Não-objetivos (explícitos)

O sistema **não** tenta ser:

- Substituto de corretora ou banco.
- Executor de ordens.
- Aconselhamento financeiro automatizado ("robo-advisor").
- Cliente Open Banking no MVP (avaliar pós-MVP).
- App mobile no MVP (web responsive chega).

---

## MVP scope (resumo)

**Dentro do MVP — módulo Financeiro apenas:**

- Auth + signup/login/refresh/reset com ASP.NET Core Identity + `MapIdentityApi`.
- Multi-tenancy completo (User → Tenant → Membership), modelado mesmo sem UI de convites.
- CRUD de **categorias** (1 nível, com seed no signup, ícone+cor+isExpense/isIncome, "arquivar" em vez de eliminar).
- CRUD de **contas** e CRUD manual de **transações**.
- **Importação CSV** com `ImportProfile` reutilizável por banco/cartão + deduplicação (heurística data+valor+descrição normalizada) + pré-visualização.
- **Regras de categorização** (Contains/Equals/StartsWith) com ordem de prioridade e re-execução sobre transações existentes.
- **Recorrentes** (Daily/Weekly/Monthly/Yearly) com Hangfire, preview de futuras, edição "só futuras vs todas pendentes".
- **Metas** mensais por categoria com alertas em 80% e 100%.
- **Multi-moeda** com câmbio do ECB + fallback manual.
- **Dashboard mensal**: total in/out, top 5 categorias, evolução 12 meses, filtros por moeda/categoria/período.
- **Export CSV** das transações.

**Fora do MVP** (mesmo que pareça "fácil de adicionar"):

- Módulo de investimento.
- Open Banking / PSD2 / Pluggy / Belvo.
- Categorização ML.
- UI de convidar membros para tenant.
- App mobile.
- Notificações push/SMS, relatórios fiscais, dark mode (a menos que venha de graça da lib Angular), OFX/QIF, hierarquia de categorias > 1 nível, tags livres, anexos a transações, histórico de edições visível.

**Definition of Done para o MVP completo:**

1. Todas as user stories acima implementadas.
2. Deployed na VPS atrás de Kestrel+LettuceEncrypt com HTTPS.
3. Backup automatizado a correr e **pelo menos 1 restore de teste** já feito.
4. Thacio importou o extrato bancário do último mês e categorizou tudo.
5. Recorrentes (renda, salário, etc.) configuradas e a gerar.
6. Pelo menos 3 metas a mostrar progresso no dashboard.
7. README de deploy atualizado.

---

## Restrições técnicas duras

Estas decisões já estão tomadas e validadas em ADRs (ver Vault). A Constitution `tech-stack.md` deve incorporá-las como **constraints**, não revisitá-las.

| Camada | Decisão | Origem |
|--------|---------|--------|
| Backend runtime | **.NET 10 LTS** (ASP.NET Core, suporte até nov/2028) | `02.1` |
| ORM | **EF Core 10** com Global Query Filters para multi-tenancy | `02.1` |
| Base de dados | **PostgreSQL 16+** — RLS, JSONB, `SKIP LOCKED`, particionamento | `02.1`, `02.2` |
| Frontend | **Angular (LTS)** servido pelo próprio Host .NET (`UseStaticFiles + MapFallbackToFile`) | `02.1`, ADR-005 |
| TLS | **Kestrel + LettuceEncrypt** (Let's Encrypt automático, sem reverse proxy externo) | `02.1` |
| Background jobs | **Hangfire + Hangfire.PostgreSql** (open source, gratuito, dashboard built-in) | `02.1` |
| Mensageria entre módulos | **Wolverine** com transport PostgreSQL (Outbox transacional built-in) | `02.3`, ADR-006 |
| Auth | **ASP.NET Core Identity + `MapIdentityApi`** + custom signup que orquestra User+Tenant+Membership atomicamente + custom `ClaimsPrincipalFactory` que injeta `tenant_id` no token | `02.4`, ADR-002 |
| Hosting | **VPS própria via Docker Compose** (1 container API+Hangfire+Angular, 1 container Postgres, volumes para `pgdata`/`letsencrypt-certs`/`logs`) | `02.1` |
| Logs | **Serilog → ficheiro** com rotação | `02.1` |
| Migrations | EF Core Migrations com lock distribuído via tabela `__migration_lock` no startup do Host | `02.1` |
| Idioma da docs/mensagens | **PT-PT** | Convenção Marlo |

### Invariantes não-negociáveis

1. **Tenant isolation é invariante de domínio.** Discriminator column `TenantId NOT NULL` em todas as tabelas tenant-owned + RLS PostgreSQL como **segunda barreira** (defesa em profundidade). Nenhum query bypassa o filtro. Code review obrigatório para SQL raw. Testes de integração de multi-tenancy desde o Sprint 1.
2. **Module isolation é invariante de arquitetura.** Modular Monolith com 5 projetos por módulo (`Api`, `Application`, `Domain`, `Infrastructure`, `PublicApi`). Módulo A **nunca** referencia código interno de Módulo B — só `*.PublicApi`. Comunicação via **Wolverine messages** (preferido) ou **HTTP loopback** (quando precisa de resposta imediata). Validação via `NetArchTest` no CI.
3. **Schema PostgreSQL por módulo.** `shared` (Identity + Currency/ExchangeRate), `financial`, `investment`, `messaging` (Wolverine), `hangfire`. Cada `DbContext` com `HasDefaultSchema(...)`. Sem FKs cross-schema entre módulos de negócio (soft references por ID).
4. **Money é value object.** `Money(Amount: decimal(20,8), Currency: string ISO 4217)`. Proibido `decimal` solto em domínios financeiros.
5. **IDs são GUIDs v7** (`Guid.CreateVersion7()` nativo no .NET 10). Sortáveis por tempo.
6. **UTC sempre na DB.** Conversão para timezone do utilizador apenas no frontend.
7. **Sem PII em log de texto claro.** Logs estruturados Serilog com filtros.
8. **Defesa em profundidade no Hangfire**: wrapper `TenantAwareJob<T>` que persiste o `TenantId` no payload do job e reconfigura `ITenantContext` no setup do job.

---

## Decisões em aberto (a resolver na conversa de Constitution)

Ver requisitos funcionais e arquitetura para o contexto. A Constitution deve **decidir** estes pontos (ou registá-los explicitamente como pendentes com critério de quando resolver).

### Funcional
- **Tags livres** em transações além de categorias? (Recomendação Vault: sim, pós-MVP.)
- **OFX além de CSV** no MVP? (Recomendação Vault: CSV apenas no MVP.)
- **Hierarquia de categorias > 1 nível** alguma vez? (Hoje: máx. 1 nível.)

### Multi-moeda
- **Provider de câmbio principal**: ECB confirmado. Fallback manual confirmado. Falta validar se há um 2º provider on-line antes do fallback manual.
- **Snapshot diário** das taxas de câmbio (tabela `ExchangeRate` em `shared`) **ou** query on-demand? Recomendação Vault: snapshot diário.

### Operacional
- ~~**Confirmação de email obrigatória** no MVP?~~ **Resolvido (Phase 6)**: não obrigatória (`RequireConfirmedEmail = false`); banner não bloqueante no app-shell, dispensável por sessão.
- ~~**SMTP**: direto da VPS ou serviço externo?~~ **Resolvido (Phase 6)**: relay externo em free tier (Resend / Mailgun), atrás de `IEmailSender` + SMTP genérico; entrega em job Hangfire com degradação graciosa.
- **Lifetime do access token**: 15 min sugerido; refresh 7d com rotation. Confirmar.
- **Storage do refresh token no frontend**: httpOnly cookie (refresh) + Authorization header (access)?

### Arquitetura
- **Soft-delete vs hard-delete** sistémico (ADR-009 ainda por escrever). Transações são soft-delete por requisito; resto?
- **Versionamento da API** (ADR-007 ainda por escrever): URL versioning `/api/v1/...` é o atual default; confirmar.
- **Provider de cotações** para Fase 2 (ADR-008): Brapi (BR) + Yahoo Finance/Alpha Vantage (intl) — avaliar comparativamente quando chegar a hora.

---

## Roadmap de alto nível

| Fase | Objetivo |
|------|----------|
| **MVP** | Módulo Financeiro completo (este `README`) — Sprints 0 a 6 |
| Fase 2 | Módulo de Investimento (carteira + cotações + questionários + sugestão de aporte + balanceamento) |
| Fase 3 | Integrações: Open Banking (Pluggy/GoCardless), import automático corretoras, categorização ML, notificações |
| Fase 4 | Relatórios fiscais (DARF, Anexo J, mapas IRS) |
| Fase 5 | Multi-utilizador (UI de convites, roles granulares, casos de uso casal/contabilista) |
| Fase 6 | Mobile (Flutter / .NET MAUI / PWA real) |
| Fase 7 | Plataforma SaaS pública (landing, planos pagos, Stripe, observability completa, compliance formal) |

**Sprints sugeridos para o MVP** (marcos, não datas):

| Sprint | Entrega |
|--------|---------|
| 0 | Setup repo, solução .NET 10, Angular, Docker Compose, CI básico, deploy VPS dummy |
| 1 | Auth + Multi-tenancy + testes de isolamento |
| 2 | Categorias + Contas + Transações manuais (CRUD) + Dashboard mínimo |
| 3 | Multi-moeda + ECB integration + ExchangeRate snapshots |
| 4 | Importação CSV + ImportProfile + deduplicação + Regras de categorização |
| 5 | Recorrentes + Hangfire setup + Metas |
| 6 | Polish, fixing, dogfooding 1 mês, ajustes |

---

## Riscos do MVP

| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| Multi-tenancy mal implementado | Catastrófico (vazamento de dados) | RLS + testes de integração obrigatórios desde Sprint 1 |
| Câmbio incorreto em transações antigas | Alto (números errados) | Snapshot no momento + testes |
| Hangfire jobs sem contexto de tenant | Médio (geração errada) | Wrapper `TenantAwareJob` desde início |
| Scope creep para investimento | Alto (atraso) | MVP scope é fonte de verdade |
| VPS down sem backup | Catastrófico | Backup automatizado + 1 restore de teste antes do dogfooding |

---

## Desenvolvimento local

Pré-requisitos:

- .NET 10 SDK (`dotnet --list-sdks` deve listar `10.0.x`).
- Node LTS (≥ 22) e npm.
- Docker + Docker Compose v2.

Setup inicial:

```bash
# .NET
dotnet restore Sextante.slnx

# Angular
cd src/Web/Sextante.Web
npm install
cd -

# Build full release (Angular + .NET) e tests
dotnet build Sextante.slnx -c Release
dotnet test  Sextante.slnx -c Release --no-build
```

Para iterar localmente sem rebuild Angular cada vez, usar:

```bash
# .NET com Angular skip
dotnet run --project src/Bootstrap/Sextante.Host -p:SkipAngularBuild=true

# Em paralelo, Angular dev server (proxy futuro pode ser adicionado em Phase 1)
cd src/Web/Sextante.Web && npm start
```

`docker compose up --build` corre o sistema inteiro localmente: Angular é construído na stage 1, o Host publica em Release na stage 2, e o container final escuta em `http://localhost:80` e `https://localhost:443` (sem cert válido localmente; `http://localhost/api/health` deve devolver `200`).

---

## Provisionamento inicial (admin CLI)

Depois de a aplicação arrancar pela primeira vez, a base de dados está vazia — sem utilizadores. O comando `create-admin` cria o primeiro utilizador administrador sem precisar de aceder ao Postgres directamente:

```bash
docker compose exec api dotnet Sextante.Host.dll create-admin \
  --email admin@exemplo.pt --password 'PalavraForte1!' \
  --tenant-name 'Sextante Admin'
```

- **`--email`** (obrigatório): email do administrador.
- **`--password`** (obrigatório): palavra-passe inicial.
- **`--tenant-name`** (opcional, default `Admin Tenant`): nome do tenant.

O comando é **idempotente**: re-executar com o mesmo email rotaciona a palavra-passe, garante a role `Admin` e `EmailConfirmed=true`, sem criar duplicados nem erro.

Após execução, o utilizador pode fazer login em `/login` e aceder ao sistema. As categorias padrão são automaticamente criadas para o novo tenant.

---

## Deploy (produção)

> Deploys são **automáticos**: push em `main` verde → imagem para o GHCR →
> SSH para a VPS → `docker compose pull && up -d` → smoke check a
> `/api/health`. Workflow em `.github/workflows/deploy.yml`, decisão em
> `docs/adr/ADR-013-cd-github-actions.md`. Também corre à mão em
> **Actions → Deploy Sextante → Run workflow** (com opção de saltar testes
> para hotfixes).
>
> Esta secção documenta a **VPS partilhada** e o setup que se faz uma vez.

### 1. A VPS

O Sextante não tem VPS própria: partilha a que já corre outras duas apps.

| App | Porta | TLS |
|---|---|---|
| `oui-system` | `8080` | Caddy (systemd), bloco `:80` catch-all |
| `binance-bot` | `3000` | nenhum (acesso directo) |
| **`sextante`** | **`8090`, só em `127.0.0.1`** | **Caddy, vhost nomeado** |

Cada app vive em `/opt/<app>` com `docker-compose.yml` + `.env`. O
PostgreSQL nativo da VPS (`:5432`) serve as outras duas — **o Sextante não
lhe toca**: usa container próprio, sem publicar porta. É deliberado: o
`infra/postgres/01-bootstrap-roles.sh` cria os roles `sextante_migrations`
(BYPASSRLS) e `sextante_app` (**sem** BYPASSRLS), e essa separação é o
invariante de multi-tenancy do `AGENTS.md` §3.1 — não é coisa para se fazer
à mão num cluster partilhado.

### 2. TLS via Caddy (não LettuceEncrypt)

O `LettuceEncrypt` **não é usado em produção**: quem detém as portas 80/443
é o Caddy do systemd. As variáveis `LETSENCRYPT__*` ficam vazias e o
`KestrelTlsSetup` larga o bind HTTPS sozinho, deixando a API a escutar só
HTTP na `8080` interna. O `UseForwardedHeaders` (`Program.cs`) lê o
`X-Forwarded-Proto` do Caddy, o que mantém `Secure=true` nos cookies do
refresh token.

O bloco a acrescentar a `/etc/caddy/Caddyfile` está em
`deploy/Caddyfile.sextante`, com o raciocínio e as precauções. A edição é
aditiva — o `:80` catch-all do oui-system fica intacto, porque o Caddy
resolve por especificidade.

### 3. Setup inicial da VPS (uma vez)

```bash
# 3.1 — o utilizador de deploy precisa de Docker sem sudo
sudo usermod -aG docker deployuser     # relogin depois disto

# 3.2 — directório da app (o resto é copiado pelo workflow)
sudo mkdir -p /opt/sextante && sudo chown deployuser:deployuser /opt/sextante

# 3.3 — timer de backup, depois do primeiro deploy ter copiado o infra/
sudo cp /opt/sextante/infra/backup/sextante-backup.* /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now sextante-backup.timer
```

Não é preciso `docker login` manual: o job de deploy autentica-se no GHCR
com o `GITHUB_TOKEN` efémero do próprio workflow e faz `logout` no fim — a
imagem fica privada e nenhum PAT de longa duração assenta na VPS.

### 4. GitHub Secrets

Toda a configuração de produção vem dos **Secrets do repositório**
(Settings → Secrets and variables → Actions). O `.env` em
`/opt/sextante/.env` é **reescrito a cada deploy** a partir deles — editá-lo
à mão na VPS não sobrevive ao push seguinte.

| Secret | Uso | Como gerar |
|---|---|---|
| `VPS_HOST` / `VPS_USER` / `VPS_SSH_KEY` | Acesso SSH | `VPS_USER=deployuser`; chave dedicada ao deploy, sem passphrase |
| `POSTGRES_PASSWORD` | Superuser do container Postgres | `openssl rand -base64 32` |
| `SEXTANTE_MIGRATIONS_PASSWORD` | Role BYPASSRLS (migrations) | `openssl rand -base64 32` |
| `SEXTANTE_APP_PASSWORD` | Role sem BYPASSRLS (runtime) | `openssl rand -base64 32` |
| `JWT__SIGNING_KEY` | Assinatura dos tokens | `openssl rand -base64 64` |
| `SENTRY__DSN` | Error tracking (vazio desliga) | Sentry → `sextante-api` → Client Keys |
| `SMTP__HOST` / `__PORT` / `__USERNAME` / `__PASSWORD` / `__FROM` | Email transaccional (vazio desliga) | Relay (Resend/Mailgun) |

> ⚠️ As passwords dos roles Postgres são consumidas em **dois** sítios: pelo
> `01-bootstrap-roles.sh` na primeira boot do volume, e pelas connection
> strings da API. Rodá-las depois do volume existir exige `ALTER ROLE`
> manual — ver `docs/runbooks/restore.md`.

### 4.1 Email transaccional (SMTP)

Confirmação de email e recuperação de palavra-passe são entregues por um
**relay SMTP externo** em free tier (Resend ou Mailgun) — decisão da Phase 6:
não vale gerir reputação de IP, SPF/DKIM/DMARC e PTR para o email de um
utilizador.

- Criar conta no relay, **verificar o domínio** (registos SPF + DKIM no DNS)
  e gerar credenciais SMTP. É o **mesmo domínio** do vhost do Caddy.
- **`SMTP__HOST` vazio é um estado válido**: a app arranca e funciona, os
  emails são descartados com log `Warning`. Nesse modo, o reset de palavra-passe
  faz-se pelo CLI `create-admin` (idempotente, rotaciona a palavra-passe).
- A entrega corre em job Hangfire com retry — um relay em baixo nunca faz
  falhar um signup nem um pedido de reset.

### 5. Primeiro utilizador

```bash
cd /opt/sextante
docker compose exec api dotnet Sextante.Host.dll create-admin \
  --email <email> --password <password> --tenant-name <nome>
```

Idempotente. Confirma depois que as categorias seed foram criadas (o
subscriber de `UserRegisteredIntegrationEvent`).

### 6. Verificação live

```bash
# Na VPS — a API só escuta em loopback, por isso é daqui que se testa
curl -fsS http://127.0.0.1:8090/api/health       # {"status":"ok",...}
cd /opt/sextante && docker compose ps

# Depois de existir o vhost do Caddy + registo DNS A
curl -I https://<dominio>/api/health             # → 200
openssl s_client -connect <dominio>:443 -servername <dominio> </dev/null 2>/dev/null \
  | openssl x509 -noout -issuer                  # → issuer Let's Encrypt
```

O dashboard do Hangfire está em `/api/admin/hangfire` atrás do
`HangfireSystemAdminFilter` — acessível só a `SystemAdmin`, nunca a
anónimos.

### 7. Backups

O `infra/backup/backup.sh` faz `pg_dump` por schema (`shared`, `financial`,
`hangfire`, `messaging`) para `/var/backups/sextante`, com retenção de 30
dias. Falha ruidosamente (exit code 1) se algum dump falhar ou vier
suspeitosamente pequeno — um backup silenciosamente vazio é pior que
nenhum.

Corre por **systemd timer às 03:00 UTC** (`infra/backup/sextante-backup.timer`).
UTC explícito, e não cron: a VPS está em `Europe/Berlin` e o DST faria a
hora oscilar duas vezes por ano.

```bash
sudo systemctl start sextante-backup.service   # correr já, uma vez
journalctl -u sextante-backup.service -n 30
systemctl list-timers sextante-backup.timer
```

Agendamento, restore completo, restore selectivo e o procedimento de
**restore de teste**: `docs/runbooks/restore.md`. `tech-stack.md` §15 exige
pelo menos um restore de teste executado antes do dogfooding.

### 8. Operações comuns

```bash
cd /opt/sextante
docker compose ps                        # estado
docker compose logs -f api               # tail live
docker compose up -d --no-deps api       # reiniciar só a API
docker compose exec postgres psql -U postgres -d sextante
```

**Rollback**: cada deploy fixa a imagem pela tag do SHA curto no `.env`
(`SEXTANTE_IMAGE`). Para voltar atrás, editar essa linha para o SHA anterior
e `docker compose up -d api` — ou re-correr o workflow a partir do commit
bom, que é o caminho preferido (deixa rasto no Actions).

### 9. Recuperação manual (quando o CD não está disponível)

```bash
cd /opt/sextante
echo "<GHCR_PAT>" | docker login ghcr.io -u <user> --password-stdin
docker compose pull && docker compose up -d
docker logout ghcr.io
```

---

## Convenções

- **Idioma**: PT-PT na docs e mensagens; código e identifiers em inglês.
- **IDs**: GUID v7.
- **Branches**: `phase-N-<kebab-name>` para features; `replanning` dedicada para updates de Constitution; `mvp`/`demo` para milestones.
- **Mensagens de commit**: imperativo curto em PT-PT-friendly. Título canónico SDD em EN só nos arranques (ex.: `Introduce SDD foundation`).
- **Specs**: nunca editar à mão — sempre via conversa com o agente (regra dura 1 do cheat-sheet Marlo).

---

## Referências canónicas

A documentação detalhada (~15 ficheiros, ADRs, ERD, fluxos, API contracts) vive no Obsidian Vault e **não está duplicada neste repo** para evitar drift:

```
C:\Users\MarloGamer\Documents\Obsidian Vault\Projectos\Sistema Financeiro\
├── 00 - Visão e Produto.md            ← visão, persona, princípios
├── 01.1 - Requisitos Funcionais - Módulo Financeiro.md     ← user stories MVP
├── 01.2 - Requisitos Funcionais - Módulo Investimento.md   ← Fase 2
├── 02.1 - Arquitetura - Visão Geral.md                     ← stack + estrutura .sln
├── 02.2 - Arquitetura - Multi-tenancy.md      ⭐ crítico
├── 02.3 - Arquitetura - Modular Monolith.md   ⭐ crítico
├── 02.4 - Arquitetura - Autenticação.md
├── 03 - Modelo de Dados.md
├── 04 - Fluxos de Utilizador.md
├── 05 - MVP Scope.md
├── 06 - Roadmap.md
├── 07 - API Contracts.md
└── ADRs/
    ├── ADR-001 - Multi-tenancy.md
    ├── ADR-002 - Authentication.md
    ├── ADR-003 - Money Value Object.md
    ├── ADR-004 - Modular Monolith.md
    ├── ADR-005 - Frontend Hosting.md
    └── ADR-006 - Messaging.md
```

ADRs ainda por escrever: 007 (versionamento da API), 008 (provider de cotações), 009 (soft delete vs hard delete).

Metodologia SDD canónica: `C:\Users\MarloGamer\Documents\Obsidian Vault\Projectos\Spec Driven Development\` (Playbook Greenfield Marlo, cheat-sheet, regras duras).
