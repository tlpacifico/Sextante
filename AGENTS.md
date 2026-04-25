# Agent rules — Sextante

> Regras globais para qualquer agent (Claude Code, Cursor, Copilot, etc.) que trabalhe neste repo. Lê este ficheiro **e** `specs/mission.md` + `specs/tech-stack.md` + `specs/roadmap.md` antes de qualquer trabalho.

---

## 1. Workflow SDD

- **Sempre ler primeiro**: `specs/mission.md`, `specs/tech-stack.md`, `specs/roadmap.md`. Em features começadas, ler também `specs/<YYYY-MM-DD-feature>/plan.md`, `requirements.md`, `validation.md`.
- **Em dúvida → `AskUserQuestion`**, não adivinhar. Especialmente decisões de produto, UX, ou trade-offs de arquitetura não cobertos nos specs.
- **Cada feature segue o ciclo**: Plan → Implement → Validate.
- **`/clear` é obrigatório antes de cada feature nova.** O contexto da feature anterior contamina raciocínio.

---

## 2. Regras duras (não negociáveis)

1. **Nunca editar `specs/*` à mão.** Mudanças à Constitution ou a feature specs são feitas **via conversa com o agente**, para manter `mission` / `tech-stack` / `roadmap` / `feature specs` em sync.
2. **Nunca usar IDE move/rename** em código gerado pelo agente sem pedir ao agente para atualizar specs.
3. **Sub-agent deep review obrigatório** em features que tocam: DB schema/migrations, Auth, Multi-tenancy, integrações externas (FX, cotações, SMTP), Hangfire jobs.
4. **Changelog skill antes do merge.** Sempre.
5. **Idioma**:
   - Documentação, comentários de domínio, mensagens de commit: **PT-PT**.
   - Código, identifiers, naming convencional .NET/Angular: **inglês**.
6. **Secrets**: nunca commit. Usar `.env.local` (já no `.gitignore`) ou `appsettings.Local.json`.

---

## 3. Invariantes técnicos do projeto

Estas são propriedades **sempre verdadeiras** do sistema. Qualquer código que as viole é bug, mesmo que compile e passe testes.

### 3.1 Multi-tenancy

- Toda entidade tenant-owned tem `TenantId NOT NULL` com FK e índice.
- `EF Core Global Query Filters` aplicam `WHERE TenantId = @currentTenant` automaticamente em todas as queries das entidades que implementam `ITenantOwned`.
- **PostgreSQL Row-Level Security é a segunda barreira.** O role da app **não** tem `BYPASSRLS`. Mesmo se a aplicação tiver bug, a DB recusa retornar linhas de outro tenant.
- Operações cross-tenant (admin) usam um `IAdminContext` separado com role `BYPASSRLS` e endpoints sob `[Authorize(Roles = "SystemAdmin")]` em `/api/admin/*`.
- **Sem fail-silent**: se falta `TenantContext` numa request autenticada, lançar exceção, **não** retornar tudo nem string vazia.
- **Hangfire jobs** usam o wrapper `TenantAwareJob<T>` que persiste `TenantId` no payload e reconfigura `ITenantContext` no setup do job.
- Testes de integração de multi-tenancy correm no CI desde o Sprint 1: tenant A não vê / não modifica / não elimina dados de tenant B; inserts auto-populam `TenantId`; query sem `TenantContext` lança.

### 3.2 Module isolation (Modular Monolith)

- **5 projetos por módulo**: `<Module>.Api`, `<Module>.Application`, `<Module>.Domain`, `<Module>.Infrastructure`, `<Module>.PublicApi`.
- **Regras de dependência forçadas em `.csproj`**:
  - `*.Domain` → não depende de nada de outros módulos. Pode usar `SharedKernel`.
  - `*.Application` → depende do próprio `Domain` + `SharedKernel` + `Messaging` (contratos) + `*.PublicApi` próprio + `*.PublicApi` de outros módulos.
  - `*.Infrastructure` → depende do próprio `Application` + EF Core / Hangfire / Wolverine.
  - `*.Api` → depende do próprio `Application` + `Infrastructure`.
  - `*.PublicApi` → **só** POCOs (DTOs e records de eventos). Não referencia `Domain` nem `Infrastructure`.
- **Comunicação inter-módulos**:
  - **Mensagem (Wolverine + transport PostgreSQL)** — preferida, fire-and-forget, com Outbox transacional.
  - **HTTP loopback** — quando precisa de resposta imediata. Reusa pipeline (auth, tenant context).
- **Proibido**: `Module.X` referenciar `Module.Y.Domain`, `Module.Y.Application`, `Module.Y.Infrastructure` ou `Module.Y.Api`.
- **Validação automática**: testes de arquitetura com **NetArchTest** correm no CI e falham o build se as regras forem violadas.

### 3.3 Persistência

- **Cada módulo no seu schema PostgreSQL**: `shared`, `financial`, `investment`, `messaging` (Wolverine), `hangfire`.
- **Cada módulo tem o seu `DbContext`** com `modelBuilder.HasDefaultSchema("...")`.
- **Migrations independentes por módulo** (`MigrationsHistoryTable("__migrations", "<schema>")`).
- **Sem FKs cross-schema entre módulos de negócio.** Se `Investment` precisa referenciar uma `Account` do `Financial`, guarda **apenas o ID** sem FK formal (soft reference).
- Migrations correm no startup do Host com lock distribuído via tabela `__migration_lock` (evita race quando há mais de uma instância).

### 3.4 Modelo de domínio

- **Money é value object**: `Money(Amount: decimal(20,8), Currency: string ISO 4217)`. **Proibido `decimal` solto** em domínios financeiros.
- **IDs**: `Guid` v7 (`Guid.CreateVersion7()` nativo no .NET 10) — sortáveis por tempo, eficientes em índice.
- **Timestamps**: `timestamptz` UTC. Conversão para timezone do utilizador apenas no frontend.
- **Soft delete via `DeletedAt`** quando aplicável (transações é requisito; resto a confirmar quando ADR-009 for escrito).
- **Audit trail mínimo**: `CreatedAt`, `UpdatedAt`, `Version` (concorrência otimista) em todas as entidades de negócio.

### 3.5 Auth

- **ASP.NET Core Identity + `MapIdentityApi`** para endpoints standard (`/auth/login`, `/auth/refresh`, `/auth/forgotPassword`, etc.).
- **Custom signup** (`POST /api/auth/signup`) que orquestra User + Tenant + Membership **atomicamente** numa transação.
- **`TenantAwareClaimsPrincipalFactory`** injeta `tenant_id` e `tenant_role` no JWT.
- **`ITenantContext`** (em `Identity.PublicApi`) é resolvido por request a partir dos claims. Lança `UnauthorizedAccessException` se `tenant_id` falta — **fail-loud**.
- **Outros módulos não conhecem `Identity`** — só usam `ITenantContext` injetado via DI.
- Endpoints públicos (signup, login) marcados `[AllowAnonymous]` e **não** resolvem `ITenantContext`.

---

## 4. Logging e observability

- **Serilog estruturado → ficheiro** com rotação (Seq opcional pós-MVP).
- **Sem PII em texto claro** nos logs (emails, valores, descrições de transação livres). Filtros configurados em `appsettings.json`.
- **`TenantId` em cada log de request** para correlação. Alerta se mudança inesperada de tenant no mesmo request.

---

## 5. Branches e commits

- **Feature**: `phase-N-<kebab-name>` (ex.: `phase-1-auth-multi-tenancy`).
- **Replanning**: `replanning` (branch dedicada — isolar Constitution updates).
- **MVP/demo**: `mvp` ou `demo`.
- **Mensagens de commit**: imperativo curto em PT-PT-friendly. Títulos canónicos SDD em EN só nos arranques (ex.: `Introduce SDD foundation`, `Mark phase N as complete`).

---

## 6. Decision tree rápido (quando algo aparece a meio de uma feature)

| Situação | O que fazer |
|---|---|
| Ajuste pequeno à Constitution (ex.: check-off de roadmap) | Mesma branch da feature |
| Update grande à Constitution (ex.: mudar tech stack) | Branch `replanning` separada |
| Update pequeno ao product spec (ex.: viewport meta) | Durante replanning |
| Update grande ao product spec (ex.: novo módulo de auth) | Nova phase no roadmap |
| Ideia spawn durante feature (não no roadmap ainda) | Escrever para `backlog/YYYY-MM-DD-<descricao>.md` |
| Feature toca DB / auth / integração externa | Implementação **grupo a grupo**, não tudo de uma vez. + sub-agent deep review obrigatório |
| Feature standard | Implementação tudo de uma vez |

---

## 7. Skills úteis (referência rápida)

- `feature-spec` — cria o esqueleto de uma feature spec (`plan.md`, `requirements.md`, `validation.md`) a partir da próxima phase do `roadmap.md`.
- `changelog` — atualiza `CHANGELOG.md` com base nos commits da branch.
- `review` — code review automático de uma branch.

---

## 8. Anti-patterns a evitar

- ❌ "É só uma chamada direta entre módulos, é mais rápido." Não. Mantém a fronteira limpa.
- ❌ Schema partilhado por dois módulos de negócio.
- ❌ Eventos com payloads enormes (carregar IDs, não estados).
- ❌ Subscribers que reagem a evento e fazem cascata de 5 outros (modela como saga explícita).
- ❌ `PublicApi` que cresce sem critério (ser conservador — mudança quebra outros módulos).
- ❌ Implementar sem spec clara. Se o prompt de implementação precisa de mais do que "implement the task groups", a spec está incompleta — corrige a spec primeiro.
- ❌ Saltar `/clear` entre features.
- ❌ Editar `specs/*` à mão para "poupar tempo".

---

## 9. Referências canónicas

- **Visão de produto e arquitetura**: ver `README.md` (input de stakeholders) e secção *Referências canónicas* lá para o Obsidian Vault.
- **Metodologia SDD**: `C:\Users\MarloGamer\Documents\Obsidian Vault\Projectos\Spec Driven Development\` (Playbook Greenfield Marlo, cheat-sheet, regras duras).
