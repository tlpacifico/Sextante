# Tech Stack

> Stack consolidada, restrições não-negociáveis, padrões obrigatórios e decisões registadas. Acompanha `mission.md` e é referenciado por todas as feature specs.

---

## 1. Stack consolidada

| Camada | Tecnologia | Notas |
|--------|-----------|-------|
| Backend runtime | **.NET 10 LTS** (suporte até nov/2028) | Fixa via `global.json` em `10.0.x` |
| Web framework | ASP.NET Core (Minimal APIs maduras) | |
| ORM | **EF Core 10** | Global Query Filters para multi-tenancy |
| Base de dados | **PostgreSQL 16+** | RLS, JSONB, `SKIP LOCKED`, particionamento |
| Migrations | EF Core Migrations | Lock distribuído via tabela `__migration_lock` no startup |
| Background jobs | **Hangfire + Hangfire.PostgreSql** | Open source, dashboard built-in, schema `hangfire` |
| Mensageria inter-módulos | **Wolverine** com transport PostgreSQL | Outbox transacional built-in, schema `messaging` |
| Auth | **ASP.NET Core Identity + `MapIdentityApi`** | Custom signup que orquestra User+Tenant+Membership atomicamente |
| TLS | **Kestrel + LettuceEncrypt** | Let's Encrypt automático, sem reverse proxy externo |
| Frontend | **Angular 21 LTS + PrimeNG + Tailwind CSS** | Components: PrimeNG (incl. Chart). Layout/utilities: Tailwind. State: Angular Signals. Servido pelo Host .NET via `UseStaticFiles + MapFallbackToFile`. Detalhe em §19. |
| Hosting | **VPS própria via Docker Compose** | 1 container API+Hangfire+Angular, 1 container Postgres |
| Logs | **Serilog → ficheiro** com rotação | Seq opcional pós-MVP. Sem PII em texto claro. |
| Containerização | Docker + Docker Compose | Dockerfile multi-stage (Node→.NET→runtime) |
| Pacote management | Central Package Management (`Directory.Packages.props`) | Evita drift de versões |

---

## 2. Estrutura de solução

Modular Monolith com 5 projetos por módulo + bootstrap + building blocks.

```
Sextante.sln
├── global.json                                     # SDK 10.0.x rollForward latestFeature
├── Directory.Build.props                            # net10.0, Nullable=enable, TreatWarningsAsErrors=true
├── Directory.Packages.props                         # Central Package Management
│
├── src/
│   ├── Bootstrap/
│   │   └── Sextante.Host/                  # ASP.NET Core entry point + wwwroot do Angular
│   │
│   ├── BuildingBlocks/
│   │   ├── Sextante.SharedKernel/          # Money, TenantId, Currency, primitives
│   │   ├── Sextante.Messaging/             # Contratos de mensagens + abstrações Wolverine
│   │   └── Sextante.Infrastructure/        # EF Core base, Hangfire setup, auth, logging
│   │
│   ├── Modules/
│   │   ├── Identity/                                 # 5 projetos: Api / Application / Domain / Infrastructure / PublicApi
│   │   ├── Financial/                                # 5 projetos (MVP)
│   │   └── Investment/                               # 5 projetos (Fase 2)
│   │
│   └── Web/
│       └── Sextante.Web/                    # Angular app (build copia para wwwroot do Host)
│
└── tests/
    ├── Sextante.SharedKernel.Tests/
    ├── Modules/<Module>.Domain.Tests/
    ├── Modules/<Module>.Application.Tests/
    ├── Sextante.ArchitectureTests/          # NetArchTest força regras de isolamento
    └── Sextante.IntegrationTests/            # End-to-end + multi-tenancy obrigatórios
```

> Detalhe completo de `csproj` references e `Directory.Build.props`: `Vault: 02.1 - Arquitetura - Visão Geral.md` secção 3.

---

## 3. Modular Monolith — invariante de arquitetura

### 3.1 Regras de dependência (forçadas via `.csproj` e validadas via NetArchTest no CI)

| De ↓ Para → | Domain | Application | Infrastructure | Api | PublicApi (próprio) | PublicApi (outro módulo) | SharedKernel |
|---|---|---|---|---|---|---|---|
| `M.Domain` | — | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| `M.Application` | ✅ | — | ❌ | ❌ | ✅ | ✅ | ✅ |
| `M.Infrastructure` | ✅ | ✅ | — | ❌ | ✅ | ✅ | ✅ |
| `M.Api` | ❌ | ✅ | ✅ | — | ✅ | ✅ | ✅ |
| `M.PublicApi` | ❌ | ❌ | ❌ | ❌ | — | ❌ | ✅ |

**Regra de ouro**: `Module.Investment.*` **nunca** referencia `Module.Financial.Domain/Application/Infrastructure/Api`. Apenas `Financial.PublicApi` (POCOs).

### 3.2 Comunicação inter-módulos

| Caso | Mecanismo |
|------|-----------|
| Fire-and-forget, "X aconteceu, quem se interessar reage" | **Mensagem** (Wolverine + transport PostgreSQL + Outbox) |
| Precisa de resposta imediata para responder ao user | **HTTP loopback** (`http://localhost`, atravessa pipeline normal) |
| Operação multi-módulo que pode falhar parcialmente | Mensagem + saga explícita |

**Default = mensagem**. HTTP é exceção e tem de ser justificado.

### 3.3 Eventos de integração vs eventos de domínio

- **Eventos de integração** (sufixo `IntegrationEvent`) vivem em `*.PublicApi`. Outros módulos subscrevem.
- **Eventos de domínio** vivem em `*.Domain`. **Não saem** do módulo.

### 3.4 Naming

- Comandos: imperativo (`CreateTransactionCommand`).
- Eventos: passado (`TransactionCreated`, `RecurringRuleTriggered`).
- Eventos de integração: passado + sufixo (`TransactionCreatedIntegrationEvent`).

### 3.5 Despacho in-process de comandos e queries (CQRS)

- **Comandos e queries dentro de um módulo são despachados via Wolverine**
  (mesma infra do messaging inter-módulos descrita em §11). **Sem `MediatR`.**
- Handler discovery por convenção (`[WolverineHandler]` ou descoberta
  automática por nome). Um handler por comando/query.
- Middleware pipeline (logging estruturado, validação, transação, métricas)
  configurada uma única vez no Host e aplicada a todos os handlers.
- **Justificação**: Wolverine (Jeremy Miller) suporta nativamente mediação
  in-process **e** messaging out-of-process com o mesmo modelo mental.
  Empilhar `MediatR` em cima duplica DI, descoberta de handlers e pipeline
  behaviors sem ganho — ver ADR-010 (pendente, escrever na Phase 1a).
- **Application services não são proibidos**: para lógica trivial sem
  pipeline (ex.: leitura simples de configuração) pode-se chamar serviços
  diretamente. O critério é: tem regras transversais (logging, tx,
  validação)? → comando/query via Wolverine. Caso contrário → serviço.

> Detalhe completo: `Vault: 02.3 - Arquitetura - Modular Monolith.md`.

---

## 4. Multi-tenancy — invariante de domínio

**Defesa em profundidade**: Application layer **e** Database layer têm de falhar para haver vazamento.

### 4.1 Application layer
- Toda entidade tenant-owned implementa `ITenantOwned` com `TenantId NOT NULL`.
- `EF Core Global Query Filters` aplicam `WHERE TenantId = @currentTenant` automaticamente.
- `SaveChanges` auto-popula `TenantId` em entidades `EntityState.Added`.

### 4.2 Database layer (PostgreSQL Row-Level Security)
- `ALTER TABLE <t> ENABLE ROW LEVEL SECURITY` em todas as tabelas tenant-owned.
- Policy `USING (tenant_id = current_setting('app.current_tenant_id')::uuid)`.
- `DbConnectionInterceptor` executa `SET app.current_tenant_id = '<guid>'` em cada checkout do connection pool.
- **Role da app não tem `BYPASSRLS`**. Migrations correm com role separado privilegiado.

### 4.3 Modelo
- `User` (em `shared.AspNetUsers`, gerido pelo Identity).
- `Tenant` (em `shared.Tenants`).
- `Membership` (em `shared.Memberships`, com role: Owner / Member / ReadOnly).
- Toda outra entidade tenant-owned tem `TenantId NOT NULL` com FK para `Tenant`.

### 4.4 Fail-loud, nunca fail-silent
- `ITenantContext` lança `UnauthorizedAccessException` se claim `tenant_id` falta numa request autenticada.
- Endpoints públicos (signup, login) marcados `[AllowAnonymous]` e **não** resolvem `ITenantContext`.

### 4.5 Operações cross-tenant (admin)
- `IAdminContext` separado, com role PostgreSQL `BYPASSRLS`, `IgnoreQueryFilters()` no EF Core.
- Acessível **apenas** sob `[Authorize(Roles = "SystemAdmin")]` em controllers em `/api/admin/*`.

### 4.6 Hangfire jobs
- Wrapper `TenantAwareJob<T>` persiste `TenantId` no payload do job.
- No setup do job, reconfigura `ITenantContext` antes do handler correr.
- Sem este wrapper, jobs correm sem tenant context e o `ITenantContext` lança — fail-loud.

### 4.7 Testes obrigatórios (CI desde Sprint 1)
- Tenant A não vê dados de Tenant B (read).
- Tenant A não pode atualizar/eliminar entidade de Tenant B (write).
- Inserts auto-populam `TenantId` mesmo se developer esquecer.
- Query sem `TenantContext` lança exceção.
- Migrations correm com role privilegiado.

> Detalhe completo: `Vault: 02.2 - Arquitetura - Multi-tenancy.md` e `Vault: ADRs/ADR-001 - Multi-tenancy.md`.

---

## 5. Persistência — schema por módulo

| Schema | Módulo / função | Entidades |
|--------|-----------------|-----------|
| `shared` | Identity + cross-cutting | `AspNetUsers`, `AspNetRoles`, `Tenants`, `Memberships`, `Currency`, `ExchangeRate` |
| `financial` | Financial (MVP) | `Account`, `Category`, `Transaction`, `RecurringRule`, `CategorizationRule`, `Budget`, `ImportProfile`, `ImportBatch` |
| `investment` | Investment (Fase 2) | `Portfolio`, `AssetClass`, `Asset`, `Holding`, `Operation`, `QuestionnaireTemplate`, `AssetEvaluation`, `AssetPrice` |
| `messaging` | Wolverine | tabelas de queue, outbox, inbox |
| `hangfire` | Hangfire | tabelas internas do scheduler |

### Princípios

- **Cada módulo tem o seu `DbContext`** com `modelBuilder.HasDefaultSchema("...")`.
- **Migrations independentes por módulo**: `MigrationsHistoryTable("__migrations", "<schema>")`.
- **Sem FKs cross-schema entre módulos de negócio.** Soft references por ID (`OperationOriginAccountId Guid?` sem FK formal).
- **Cross-schema dentro do mesmo módulo está OK** (`financial` referenciar `shared.Currency`).
- Migrations correm no startup do Host com lock distribuído.

> Detalhe completo: `Vault: 03 - Modelo de Dados.md`.

---

## 6. Auth — ASP.NET Core Identity + `MapIdentityApi`

- **Endpoints built-in**: `MapIdentityApi<AppUser>()` regista `register` (não usado), `login`, `refresh`, `confirmEmail`, `resendConfirmationEmail`, `forgotPassword`, `resetPassword`, `manage/info`, `manage/2fa`.
- **Custom signup** (`POST /api/auth/signup`) orquestra User + Tenant + Membership(Owner) **atomicamente** numa única transação. Publica `UserRegisteredIntegrationEvent` que o módulo `Financial` subscreve para fazer seed de categorias default no novo tenant.
- **`TenantAwareClaimsPrincipalFactory`** injeta claims `tenant_id` e `tenant_role` no JWT.
- **`ITenantContext`** (em `Identity.PublicApi`) é resolvido por request a partir dos claims. Outros módulos injetam-no via DI.
- **Tokens**: access token 15 min; refresh token 7 dias com **rotation** (refresh antigo invalidado ao usar).
- **Storage no frontend**: refresh token em **httpOnly cookie**; access token em memória (Authorization header).
- **Confirmação de email**: **não obrigatória no MVP**. Banner persistente até confirmar. Endpoints sensíveis (mudança de password, ações destrutivas) podem exigir email confirmado.

> Detalhe completo: `Vault: 02.4 - Arquitetura - Autenticação.md` e `Vault: ADRs/ADR-002 - Authentication.md`.

---

## 7. Modelo de domínio — invariantes

### 7.1 Money é value object
- `Money(Amount: decimal(20,8), Currency: string ISO 4217)`.
- **Proibido `decimal` solto** em domínios financeiros (Domain, Application).
- Operações aritméticas (`Add`, `Subtract`) lançam se moedas diferem.
- Conversão explícita via `Money.ConvertTo(targetCurrency, exchangeRate)`.

### 7.2 IDs
- **Guid v7** (`Guid.CreateVersion7()` nativo no .NET 10) — sortáveis por tempo, eficientes em índice B-tree.

### 7.3 Timestamps
- **`timestamptz` UTC** sempre na DB.
- Conversão para timezone do utilizador apenas no frontend (Angular).

### 7.4 Soft-delete
- **Todas as entidades de negócio** têm `DeletedAt timestamptz NULL`.
- Filtro global no `DbContext` exclui registos soft-deleted automaticamente.
- Hard-delete reservado para casos excecionais (ex.: GDPR right-to-be-forgotten — pós-MVP).
- Substitui `IsArchived`/flags ad-hoc; uniformiza o modelo.

### 7.5 Audit trail mínimo
- `CreatedAt`, `UpdatedAt`, `DeletedAt`, `Version` (concorrência otimista) em todas as entidades de negócio.

### 7.6 Categorização por regra deixa rasto
- Quando uma regra automática categoriza uma transação, regista qual regra aplicou e quando.

### 7.7 Tags em transações
- Modelo de dados **preparado** desde a primeira migration: coluna `Tags jsonb NOT NULL DEFAULT '[]'` em `financial.Transaction`.
- **UI fica fora do MVP**. Custo zero hoje, evita migration dolorosa quando for adicionada.

> Detalhe completo: `Vault: ADRs/ADR-003 - Money Value Object.md`.

---

## 8. API style

- **Sem versionamento até ser preciso.** Routes em `/api/...` (ex: `/api/auth/login`, `/api/transactions`). Quando houver primeiro consumidor externo ou breaking change, introduzimos versionamento explicitamente (revisitar em Replanning).
- **REST** com JSON.
- **Auth**: JWT Bearer no header `Authorization: Bearer <token>`.
- **Erros**: `ProblemDetails` (RFC 7807) com `traceId` para correlação.
- **OpenAPI**: gerado automaticamente, exposto em `/openapi/v1.json` (mesmo sem `/v1` nas rotas).
- **Mensagens de erro**: PT-PT.

> ADR-007 (Versionamento da API) **fica adiado** — escrever quando for adicionado versionamento.

---

## 9. Multi-moeda

- Toda transação carrega `Amount`, `Currency`, `ExchangeRateToPrimary`, `ExchangeRateAt`.
- **Provider de câmbio principal**: ECB (gratuito, EUR-base, fiável).
- **Snapshot diário** das taxas para tabela `shared.ExchangeRate`. Job Hangfire diário às 00:30 UTC.
- **Fallback**: se o provider falhar, utilizador pode inserir taxa manualmente. UI sinaliza taxa manual.
- **Histórico**: query do snapshot, não chamada online.

---

## 10. Background jobs — Hangfire

- Schema `hangfire`.
- Workers correm **in-process com a API no MVP**. Separar em processo dedicado é refactor trivial pós-MVP.
- **Wrapper `TenantAwareJob<T>` obrigatório** para qualquer job que toque dados de tenant (ver §4.6).
- Dashboard Hangfire em `/api/admin/hangfire`, sob `[Authorize(Roles = "SystemAdmin")]`.

---

## 11. Mensageria — Wolverine

- Schema `messaging`.
- Transport PostgreSQL nativo (via Marten ou ADO direto).
- **Outbox transacional**: mensagens publicadas dentro do `SaveChangesAsync` ficam na mesma transação que os dados de domínio.
- Workers correm in-process com a API.
- `LISTEN/NOTIFY` para wakeup imediato; `SELECT ... FOR UPDATE SKIP LOCKED` para concorrência.
- **Wolverine cobre dois papéis com a mesma infra**:
  1. Mediação in-process de comandos e queries dentro de cada módulo (CQRS) — ver §3.5.
  2. Messaging inter-módulos com outbox transacional (esta secção).
  Mesma descoberta de handlers, mesmo pipeline de middleware. Sem `MediatR` em paralelo.

> Detalhe completo: `Vault: ADRs/ADR-006 - Messaging.md`.

---

## 12. Email

- **Abstração**: `IEmailSender<AppUser>` (interface do Identity) implementada por `SmtpEmailSender` em `Identity.Infrastructure`.
- **Entrega via Hangfire job** (não bloqueia request, retry automático).
- **Provider decidido na Phase 6**: **relay externo em free tier** (Resend ou Mailgun), não SMTP directo da VPS — o email serve apenas confirmação e recuperação de palavra-passe de um utilizador, e não compensa gerir reputação de IP, SPF/DKIM/DMARC e PTR. Continua abstraído atrás de `IEmailSender<AppUser>` + SMTP genérico: trocar de provider (incluindo para Postfix self-hosted) é mudar variáveis no `.env`.
- **Degradação graciosa** (mission §4.4): `SMTP__HOST` vazio é estado válido — a app arranca, o job de entrega loga `Warning` e descarta. O reset de palavra-passe faz-se então pelo CLI `create-admin`.
- Configuração via `.env` (`SMTP__Host`, `SMTP__Port`, `SMTP__Username`, `SMTP__Password`, `SMTP__From`).

---

## 13. Logs e observability

- **Serilog estruturado → ficheiro** com rotação diária (volume `logs/` no Docker).
- **`TenantId` em cada log de request** para correlação. Alerta se mudança inesperada de tenant no mesmo request.
- **Sem PII em texto claro**: filtros configurados em `appsettings.json` para emails, valores, descrições livres.
- **Sentry como error tracker externo** (sentry.io free tier, ADR-012). Fallback graceful: Serilog file sink mantém log local se Sentry inacessível.
- Métricas / OpenTelemetry / Prometheus / Grafana — **pós-MVP** (Fase 7 plataforma SaaS).

---

## 14. Testes

| Tipo | Localização | Quando obrigatório |
|------|-------------|---------------------|
| Domain unit | `<Module>.Domain.Tests` | Para toda regra de negócio |
| Application unit | `<Module>.Application.Tests` | Para handlers com lógica não-trivial |
| Architecture | `Sextante.ArchitectureTests` | Sempre (no CI) — força regras §3.1 |
| Integration | `Sextante.IntegrationTests` | Para CRUD end-to-end + multi-tenancy obrigatório desde Sprint 1 |

- Integration tests usam `WebApplicationFactory` + Testcontainers PostgreSQL para isolamento e velocidade.
- Multi-tenancy: testes da §4.7 são bloqueadores de release.

---

## 15. Deployment

> **Timing**: o stack está pronto a deployar a partir da Phase 0
> (Dockerfile + compose + LettuceEncrypt configurados, runbook no
> README), mas o **primeiro deploy à VPS** é executado em **Phase 6**
> (pré-dogfooding), não em Phase 0. Phase 0 valida o stack apenas
> localmente.

- **Docker Compose** (1 VPS).
- **Containers**: `api` (ASP.NET + Hangfire workers + Angular static via wwwroot) + `postgres`.
- **Volumes**: `pgdata`, `letsencrypt-certs` (LettuceEncrypt), `logs`.
- **TLS automático**: LettuceEncrypt pede certificado a Let's Encrypt na 1ª request e renova sozinho.
- **Backups**: cron + `pg_dump` por schema → diretório com retenção 30 dias. **1 restore de teste obrigatório antes do dogfooding.**
- **Migrations correm no startup do Host** com lock distribuído.
- **CI/CD**: GitHub Actions → build → push de imagem → SSH + `docker compose pull && up -d`.

> Dockerfile multi-stage: `Vault: 02.1 - Arquitetura - Visão Geral.md` secção 8.

---

## 16. Idioma

- **Documentação, comentários de domínio, mensagens de commit, mensagens de erro de API**: PT-PT.
- **Código, identifiers (.NET/Angular), naming convencional**: inglês.
- **i18n estruturado** desde o início (mesmo só com PT-PT no MVP) para evitar refactor.

---

## 17. Decisões registadas (deste passo de Constitution)

| Decisão | Escolha |
|---------|---------|
| Snapshot de câmbio | Diário, tabela `shared.ExchangeRate`, job Hangfire |
| SMTP no MVP | Abstrair atrás de `IEmailSender`; provider concreto decidido antes do dogfooding |
| Soft-delete | Em **todas** as entidades de negócio (uniforme) |
| API versioning | **Sem versionamento** até ser preciso (sem `/v1` nas rotas) |
| Tags em transações | **Modelo preparado** desde a 1ª migration; UI pós-MVP |
| Confirmação de email no MVP | Não obrigatória; banner persistente |
| OFX no MVP | **Fora** (CSV apenas no MVP) |
| Hierarquia de categorias > 1 nível | **Fora** do MVP (máx. 1 nível) |
| Storage do refresh token | httpOnly cookie |
| Storage do access token | Memória + Authorization header |
| Lifetime do access token | 15 min |
| Lifetime do refresh token | 7 dias com rotation |
| CQRS dispatch (in-process) | **Wolverine** (sem `MediatR`); mesma infra do messaging inter-módulos |
| Frontend UI library | **PrimeNG** (componentes) + **Tailwind CSS** (layout/utilities) |
| Frontend state management | **Angular Signals + services** (sem NgRx no MVP) |
| Frontend forms | Reactive Forms |
| Chart library | **PrimeNG Chart** (Chart.js por baixo) |
| Versão Angular | **21 LTS** (upgrade do 19 que entrou no scaffold da Phase 0) |
| Responsive design | **Mobile-first**, breakpoints Tailwind, sanity check em 375 / 768 / 1280 px obrigatório por page (§19.5) |
| Error tracking externo | Sentry SaaS free tier (sentry.io), ADR-012 |

---

## 18. ADRs

### Já escritos (no Vault)
- ADR-001 Multi-tenancy
- ADR-002 Authentication (Identity + MapIdentityApi)
- ADR-003 Money Value Object
- ADR-004 Modular Monolith
- ADR-005 Frontend Hosting
- ADR-006 Messaging (Wolverine + PostgreSQL)

### Escritos no repo (`docs/adr/`)
- ADR-010 CQRS via Wolverine
- ADR-011 Frontend UI stack (PrimeNG + Tailwind + Signals)
- ADR-012 Sentry como error tracker externo

### Pendentes
- **ADR-007 Versionamento da API**: adiado. Escrever quando se introduzir versionamento.
- **ADR-008 Provider de cotações**: para Fase 2. Avaliação comparativa Brapi (BR) + Yahoo Finance/Alpha Vantage (intl) quando começar Sub-fase 2.2.
- **ADR-009 Soft delete vs hard delete**: **resolvido nesta Constitution** (soft-delete uniforme). Escrever ADR formal quando for tocada a primeira feature que elimine entidades (Sprint 2 ou 3) para registar o porquê.

---

## 19. Frontend stack (Angular)

> Esta secção é **invariante** para o MVP. Mudanças (substituir PrimeNG, adicionar NgRx, baixar versão do Angular) só via Replanning.

### 19.1 Stack consolidado

| Camada | Tecnologia | Notas |
|--------|-----------|-------|
| Framework | **Angular 21 LTS** | Standalone components, signal-based reactivity nativa |
| UI components | **PrimeNG** | DataTable, Calendar, MultiSelect, Dropdown, Dialog, Toast, Chart. Tema default. License MIT. |
| Layout / utilities | **Tailwind CSS** | Convive com PrimeNG (preflight ajustado). Layout, spacing, responsividade. |
| Charts | **PrimeNG Chart** (Chart.js) | Suficiente para line / bar / pie / donut do dashboard e budgets. |
| Forms | **Reactive Forms** | Adequado a validação financeira (cross-field, async, custom validators). |
| State management | **Angular Signals + services** | Sem NgRx. Signals nativos cobrem auth, tenant ativo, filtros, paginação. |
| HTTP | `HttpClient` + `HttpInterceptor` | Interceptor anexa `Authorization: Bearer <access>` e faz auto-refresh em 401. |
| i18n | `@angular/localize` | Estrutura PT-PT desde Phase 0; extensível a EN/PT-BR pós-MVP sem refactor. |
| Routing | Angular Router + lazy modules | Auth guard usa Signal de auth state (não Observable). |
| Build | Angular CLI + esbuild | Output copiado para `wwwroot` do Host (.NET) via MSBuild target. |

### 19.2 Princípios

- **Standalone components only**: zero `NgModule` em código novo.
- **Signals primeiro**: `signal()`, `computed()`, `effect()` antes de RxJS. RxJS apenas onde já é idiomático (HTTP, debounce).
- **Reactive Forms primeiro**: nada de Template-driven em formulários financeiros.
- **PrimeNG não é decorativo**: se precisas de DataTable / Calendar / MultiSelect / Chart, usa PrimeNG. Não construir à mão concorrentes.
- **Tailwind não substitui PrimeNG**: usa Tailwind para layout (grid, flex, spacing, responsivo) e cores neutras. Componentes interativos vêm da PrimeNG.
- **Sem CSS frameworks paralelos**: nada de Bootstrap, Bulma, Material em paralelo. PrimeNG + Tailwind é o conjunto.

### 19.3 Decisões deferidas (escrever ADR-011 na Phase 1b)

- Tema PrimeNG concreto (Lara / Aura / outro) — escolher na Phase 1b com base em legibilidade do dashboard.
- Estratégia de dark mode — provavelmente Tailwind `dark:` + PrimeNG dual theme; decidir em Phase 1b.
- Storybook — fora do MVP. Reavaliar quando o número de componentes próprios passar de ~20.

### 19.4 Hosting (lembrete — sem mudança vs ADR-005)

- Build do Angular roda dentro do Dockerfile multi-stage (stage `node:lts-alpine`).
- Output servido pelo Host .NET via `UseStaticFiles + MapFallbackToFile("index.html")`.
- Sem CDN externa, sem reverse proxy. TLS direto pelo Kestrel + LettuceEncrypt (§1).

### 19.5 Responsive design (invariante — concretiza `mission.md` §4.6)

A UI web é **mobile-first responsive**. Mobile não é roadmap futuro — é requisito desde a Phase 1b.

#### Breakpoints alvo (Tailwind defaults)

| Alias | Min-width | Caso típico |
|-------|-----------|-------------|
| (base) | 0 | Mobile portrait (≥360 px alvo de design) |
| `sm` | 640 px | Mobile landscape / phablet |
| `md` | 768 px | Tablet portrait |
| `lg` | 1024 px | Tablet landscape / desktop pequeno |
| `xl` | 1280 px | Desktop |
| `2xl` | 1536 px | Desktop grande (não otimizar abaixo de `xl` por defeito) |

#### Regras

- **Default mobile, depois `md:` para desktop.** Escrever utilities sem prefixo para mobile e adicionar `md:` / `lg:` para densidades maiores. Nunca o inverso (`hidden md:hidden` é antipattern).
- **Grids fluem de 1 coluna para N.** `grid-cols-1 md:grid-cols-2 lg:grid-cols-3` é o padrão para cards e filtros.
- **`p-dialog` tem `breakpoints`.** Diálogos que excedem o viewport em mobile são bug. Padrão: `[breakpoints]="{ '960px': '75vw', '640px': '95vw' }"` mais `[style]="{ width: '32rem' }"`.
- **Tabelas largas têm overflow scroll horizontal.** PrimeNG `p-table` com `[tableStyle]="{ 'min-width': 'Xrem' }"` tem de viver dentro de container `overflow-x-auto`, ou usar `[scrollable]="true" scrollDirection="horizontal"`. Stack-on-mobile (uma linha por célula) é opt-in só para tabelas pequenas.
- **`p-chart` legend responsiva.** Legenda à direita em desktop tira espaço útil em mobile — usar `position: 'bottom'` para viewport `< md` ou recalcular por `window.matchMedia` num signal.
- **Touch targets ≥ 44×44 px.** Botões, links e icon-only buttons em mobile respeitam o mínimo Apple/Google.
- **Tipografia escala por breakpoint.** Headers grandes (`text-2xl`) em desktop podem ser `text-xl` em mobile para evitar wrap feio.
- **Sidebar é overlay em mobile, persistent em desktop.** PrimeNG `p-drawer` com toggle no header é o padrão (Phase 1b já entrega).
- **Viewport meta obrigatório**: `<meta name="viewport" content="width=device-width, initial-scale=1">` em `index.html` (Angular CLI já adiciona — não remover).
- **Sem horizontal scroll na página inteira.** Apenas containers internos podem ter scroll horizontal (tabelas), nunca o `<body>`.

#### Verificação (sanity check, não audit formal)

Cada page entregue tem de ser inspecionada em DevTools com pelo menos três viewports antes do merge:

- 375 × 667 (iPhone SE / mobile pequeno).
- 768 × 1024 (iPad portrait).
- 1280 × 800 (desktop).

Verificar: nenhum overflow horizontal indesejado, nenhum elemento cortado, dialogs cabem, tabelas têm scroll quando preciso, gráficos legíveis. **Não** é audit formal de acessibilidade (esse é Phase 6).

> Detalhe completo (componente a componente): a escrever em `Vault: 04 - Arquitetura - Frontend.md` durante Phase 1b.

---

## Notas para o agente

- **Esta tech-stack é referência única**. Se um documento do Vault contradiz algo aqui, esta versão ganha (a Constitution é o source of truth do repo).
- Restrições marcadas como **invariantes** (multi-tenancy, module isolation, Money VO, soft-delete uniforme) **não** são negociáveis em feature specs. Só podem mudar via Replanning.
- ADRs adicionais escrevem-se em `docs/adr/` no repo (a criar quando o primeiro for redigido).
