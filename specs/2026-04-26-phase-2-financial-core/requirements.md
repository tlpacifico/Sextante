# Requirements — Phase 2: Categorias + Contas + Transações manuais + Dashboard mínimo

## Goal

Entregar o primeiro módulo de domínio próprio (`Financial`) e a
primeira UI funcional do produto: o utilizador cria contas,
categorias e transações manuais, e vê o estado financeiro num
dashboard mínimo (lista filtrada + cards de totais + gráfico
por categoria). É o primeiro stress test real do invariante de
multi-tenancy (`mission.md` §4 princípio 1) com escrita intensiva
de dados de domínio — tenant A e tenant B convivem na mesma
base sem nunca verem dados um do outro, garantido por Global
Query Filters **e** Row-Level Security (defesa em profundidade
per `tech-stack.md` §4).

Esta phase também concretiza a proposta de valor 1 da mission
(`mission.md` §3): "controla gastos e receitas com categorização
manual". Importação CSV, regras automáticas, recorrentes,
metas e multi-moeda real são phases posteriores; Phase 2 é o
piso fundacional sobre o qual tudo isso assenta.

`roadmap.md` § "Phase 2" enumera 7 bullets que esta phase entrega
(módulo + entidades + CRUD + soft-delete + categorias seed + regra
de archive + dashboard mínimo + OpenAPI auto-gen), mais 2 extras
escolhidos via clarifying question (cards de totais + gráfico).

## In scope

- **Módulo `Financial`** com 5 projetos
  (`Domain`/`Application`/`Infrastructure`/`Api`/`PublicApi`) sob
  `src/Modules/Financial/`, registado em `Sextante.slnx` e
  `Sextante.Host`.
- **Schema `financial`** no Postgres com tabelas `accounts`,
  `categories`, `transactions`. Migrations independentes do
  schema `shared` (Identity).
- **Entidades de domínio**:
  - `Account` (Id, TenantId, Name, `Type: AccountType
    {Checking, Savings, Cash, CreditCard}`, `OpeningBalance:
    Money`, audit + soft-delete).
  - `Category` (Id, TenantId, Name, `Kind: CategoryKind
    {Expense, Income}`, IconName ∈ allowlist PrimeIcons,
    ColorHex regex `^#[0-9A-Fa-f]{6}$`, audit + soft-delete).
  - `Transaction` (Id, TenantId, AccountId, CategoryId,
    OccurredAt, `Amount: Money`, Description, `Tags: jsonb`,
    audit + soft-delete).
- **`Money` value object** ativo em todo o domínio (per
  tech-stack §7.1 — `decimal` solto é proibido).
- **CRUD completo via REST** sob `/api/financial/{accounts,
  categories,transactions}` + `transactions/summary` e
  `transactions/by-category`.
- **Soft-delete em todas** as entidades (per tech-stack §7.4)
  com filtro global no `DbContext`.
- **Subscriber do `UserRegisteredIntegrationEvent`** (publicado
  pela Phase 1a) que cria as 11 categorias seed no signup
  (7 expense + 4 income).
- **Arquivar Category com transações ativas falha** com erro
  PT-PT (regra de domínio).
- **Multi-tenancy testes obrigatórios** (per tech-stack §4.7)
  estendidos ao módulo Financial.
- **Defesa em profundidade**: Global Query Filter **e** RLS
  policies em `accounts`, `categories`, `transactions`.
- **`ITenantCurrencyResolver`** em `Identity.PublicApi` ou
  `SharedKernel`, retornando `Currency` primary do tenant.
  Migration nova em Identity adiciona `PrimaryCurrency varchar(3)
  NOT NULL DEFAULT 'EUR'` em `shared.Tenants`.
- **Dashboard Angular `/app/dashboard`** substitui o placeholder
  da Phase 1b. Inclui:
  - Filtros: período (range datas), categoria multi-select,
    conta multi-select.
  - Tabela de transações com paginação cursor-based (PrimeNG
    `p-table` + botão "Carregar mais").
  - **Cards de totais** (Entrada / Saída / Líquido) atualizam
    reactivamente com filtros — IN scope per AskUserQuestion.
  - **Gráfico** PrimeNG `p-chart type="doughnut"` por categoria
    de despesa (toggle para receita) — IN scope per
    AskUserQuestion.
  - Botão "Nova transação" abre dialog.
- **Páginas CRUD** `/app/accounts` e `/app/categories` (PrimeNG
  DataTable + dialog de criar/editar/arquivar).
- **`FinancialApiService`** Angular (HttpClient) + `financial.store`
  com Signals.
- **`money` pipe** PT-PT (`Intl.NumberFormat`).
- **Tags column preparada no Transaction** (jsonb default `[]`)
  per tech-stack §7.7. Sem UI nesta phase.
- **Domain unit tests** (Money / Account / Category / Transaction
  invariants) — IN scope per AskUserQuestion.
- **Karma unit tests** (FinancialApiService, money pipe,
  financial.store, dashboard.page, accounts.page) — IN scope
  per AskUserQuestion.
- **Integration tests** CRUD por entidade + multi-tenancy estendida
  + seed-on-signup.
- **Architecture tests** estendidos para o módulo Financial.
- **Manual browser walkthrough** documentado em `validation.md`,
  reproduzível a partir de `git clone` + `docker compose up`.
- **Suite Phase 1a continua verde** (multi-tenancy, Identity,
  refresh cookie da Phase 1b).
- **OpenAPI auto-gerado** (mas **não** snapshot checked-in;
  ver Out of scope).

## Out of scope

- **UI de arquivados / restore.** Soft-delete vive só no DB +
  filtro global; sem vista para listar/restaurar — out per
  AskUserQuestion. Restore via psql se necessário no
  inner-loop dev.
- **Edição em linha (inline edit) na tabela do dashboard.**
  Edit via dialog apenas — out per AskUserQuestion.
- **Multi-moeda real / ECB / Currency picker.** Phase 3.
  Phase 2 assume `Currency` = primary do tenant para todas
  as transações (validado no handler).
- **Regras de categorização automática.** Phase 4.
- **Recorrentes (`RecurringRule`) + metas (`Budget`).** Phase 5.
- **Importação CSV / `ImportProfile` / `ImportBatch`.** Phase 4.
- **Hangfire jobs em Phase 2.** Nenhum job background é
  necessário (sem snapshots de câmbio, sem recurrents). O
  módulo `hangfire` continua presente per Phase 1a mas
  Financial não usa.
- **OpenAPI snapshot checked-in** (`/openapi/v1.json` em
  `docs/openapi/`). Out per AskUserQuestion. Documento gerado
  em runtime; snapshot/diffing fica para uma phase pós-MVP.
- **Sub-agent deep review formal.** Out per AskUserQuestion
  (Phase 2 não está marcada 🛡️ no roadmap). Opt-in pode ser
  considerado para o stress test multi-tenant; se feito, output
  anexado ao PR como bonus.
- **Tags UI** (input, filtro, autocompletion). Coluna preparada,
  UI fica fora do MVP per tech-stack §7.7.
- **Hierarquia de categorias > 1 nível.** Excluída per
  tech-stack §17 ("Hierarquia de categorias > 1 nível: Fora do
  MVP").
- **Conversão de transações entre contas (Transfer).** Não
  modelada nesta phase (decisão A em vez de C — Kind enum só
  Expense/Income). Pode entrar em Phase 5 se necessário.
- **Dark mode tweaks específicos para gráficos.** Aura preset
  + Tailwind `dark:` herdados de Phase 1b cobrem layout; cores
  custom da PrimeNG Chart pós-MVP.
- **Performance / Lighthouse / paginação otimizada para >100k
  transações.** MVP é dogfood pessoal — assumir <10k
  transações por tenant. Revisitar em Phase 6 se dogfooding
  expor lentidão.
- **Accessibility audit formal.** Sanity check apenas (labels +
  aria + ordem de tab); audit pós-MVP.
- **CSP headers / security headers tuning.** Phase 6.
- **Playwright/Cypress E2E.** Manual walkthrough é o artefacto
  desta phase. Revisitar em Phase 6 se a regressão crescer.

## Decisions

- **Account `Type` enum (não string livre).** *Why*: enum dá
  `switch` exhaustivos no Domain e força allowlist; extensível
  via migration mais tarde sem regressão. Decisão A da
  AskUserQuestion. **How to apply**: `enum AccountType
  { Checking, Savings, Cash, CreditCard }` em
  `Sextante.Modules.Financial.Domain/Accounts/`. EF Core grava
  como `int` (não string) por default — confirmar no
  `AccountConfiguration`.

- **Account tem `OpeningBalance: Money`, imutável depois de
  criada.** *Why*: derivar de uma "transação especial inicial"
  acopla artificialmente Account a Transaction; um campo
  dedicado é mais limpo, mais barato de testar, e evita lógica
  condicional de "ignorar a transação 0" em queries. Imutável
  porque mudar OpeningBalance retroativamente quebra coerência
  com transações já criadas. **How to apply**:
  `Account.OpeningBalance` é private setter no factory; sem
  método público que altere. Para "ajustes" pós-criação,
  utilizador cria uma `Transaction` de ajuste.

- **Category `Kind` enum (`{Expense, Income}`) em vez de
  `IsExpense`/`IsIncome` bools separados (per roadmap literal).**
  *Why*: dois bools podem combinar em estados inválidos
  (ambos true / ambos false). Enum é exclusivo por construção.
  Phase 2 não modela `Transfer` (decisão A da AskUserQuestion;
  Transfer fica para Phase 5+). **How to apply**:
  `enum CategoryKind { Expense, Income }`. Soma de despesas vs
  receitas no `TransactionSummaryQuery` filtra por `Kind` da
  Category associada.

- **Money VO ativo desde já com `Currency = tenant primary`.**
  *Why*: tech-stack §7.1 proíbe `decimal` solto em Domain.
  Mesmo que multi-moeda real só venha em Phase 3, o Domain
  tem de ser preparado para não exigir refactor depois.
  **How to apply**: factory de `Transaction` valida que
  `Amount.Currency == tenantPrimaryCurrency` resolvido via
  `ITenantCurrencyResolver`. `shared.Tenants` recebe coluna
  `PrimaryCurrency varchar(3) NOT NULL DEFAULT 'EUR'` via
  migration nova em Identity (ADR-following: Phase 2 tem direito
  a evoluir o schema `shared` se necessário, desde que com
  migration explícita). Phase 3 substitui esta validação por
  uma que aceita qualquer `Currency` válido + grava
  `ExchangeRateToPrimary`.

- **Sem hard-delete; arquivar Category com transações ativas
  falha.** *Why*: roadmap explicitamente "Não permitir eliminar
  categoria com transações associadas (arquivar → soft-delete)".
  Hard-delete viola tech-stack §7.4 (soft-delete uniforme).
  **How to apply**: `Category.EnsureCanArchive(int
  activeTransactionCount)` no Domain lança
  `CategoryHasActiveTransactionsException` se > 0. Handler
  conta ativas (DeletedAt NULL) e chama. Erro mapeia para
  `ProblemDetails` PT-PT.

- **Paginação cursor-based, opaque base64.** *Why*: offset não
  é estável para listas que mudam entre páginas (transação nova
  empurra tudo); cursor é estável e tem custo `O(log n)` em
  index `(OccurredAt DESC, Id DESC)`. Opaque base64 evita o
  client construir cursores artisanais. **How to apply**:
  `Cursor.Encode((occurredAt, id))` retorna `base64url(JSON)`;
  `Cursor.Decode(string)` valida e retorna o tuple. Page size
  default 50, max 100. Na UI, paginação é forward-only (botão
  "Carregar mais"); back-navigation refaz a query do início.

- **State management = Angular Signals + services (sem NgRx).**
  *Why*: tech-stack §19.1 manda Signals + services no MVP.
  Decisão herdada de Phase 1b. **How to apply**:
  `financial.store.ts` expõe `WritableSignal`s e `computed`s;
  components consomem via `inject(FinancialStore)`.

- **Categorias seed default**: 7 Expense (`Alimentação`,
  `Transporte`, `Saúde`, `Lazer`, `Casa`, `Educação`,
  `Outros`) + 4 Income (`Salário`, `Freelance`,
  `Investimentos`, `Outros`). *Why*: cobertura mínima do
  use case de dogfooding (mission §6) sem inflar a lista.
  Lista é canonical em
  `Sextante.Modules.Financial.Application/Common/
  DefaultCategories.cs`. **How to apply**: subscriber do
  `UserRegisteredIntegrationEvent` itera a lista e cria
  `Category` por entrada (ícones PrimeIcon + cor hex
  predefinidos por sensibilidade visual).

- **Dashboard inclui cards de totais + gráfico, mas sem inline
  edit nem UI de arquivados.** *Why*: AskUserQuestion. Cards e
  gráfico são "high-value, low-cost" no estado atual (PrimeNG
  Chart já foi peer dep desde Phase 1b); inline edit e
  arquivados duplicariam UI sem ganho proporcional para o
  dogfooding.

- **Domain unit tests + Karma unit tests são merge-blockers
  além do baseline.** *Why*: AskUserQuestion. Domain é onde
  vivem invariants críticos (Money currency mismatch,
  Category archive); Karma é onde vivem as integrações
  Angular ↔ API. Application unit tests são opt-in
  (cobertos quando lógica não-trivial — não bloqueante).

- **Sub-agent deep review é opt-in, não merge-blocker.**
  *Why*: roadmap não marca Phase 2 com 🛡️.
  AskUserQuestion confirmou. **How to apply**: se invocado,
  prompt focado em multi-tenancy estendida (Global Query Filter
  + RLS combinados em writes), output anexado ao PR.

- **Money no DB persistido como `OwnsOne` (composto Amount +
  Currency).** *Why*: preserva query-ability dos sub-campos
  (`WHERE amount > 100 AND currency = 'EUR'`) sem precisar de
  parser. **How to apply**: no `OnModelCreating`,
  `builder.OwnsOne(t => t.Amount, m => { m.Property(p => p.Amount)
  .HasColumnName("amount").HasPrecision(20, 8); m.Property(p =>
  p.Currency).HasColumnName("currency").HasMaxLength(3); })`.

- **OpeningBalance ≥ 0 (CreditCard inclusive).** *Why*: MVP
  simplifica — saldo inicial negativo em CreditCard pode ser
  modelado via transação inicial de ajuste. Decisão revisitável
  em Replanning se dogfooding mostrar fricção. **How to apply**:
  factory `Account.Create` lança se `openingBalance.Amount < 0`.
  CreditCard tem o mesmo gating; documentado como open question
  abaixo.

## Context / references

- `specs/roadmap.md` — secção "Phase 2 — Categorias + Contas +
  Transações manuais + Dashboard mínimo".
- `specs/mission.md` — §3 (proposta de valor 1: controlo de
  gastos com categorização), §4 princípio 1 (privacidade por
  defeito), §4 princípio 5 (MVP estreito e profundo), §6
  (definition of done MVP).
- `specs/tech-stack.md` — §3 (regras de dependência por módulo),
  §3.5 (Wolverine como mediator), §4 (multi-tenancy + RLS),
  §4.7 (testes obrigatórios), §5 (schema `financial`), §7
  (Money VO, IDs Guid v7, soft-delete uniforme, Tags jsonb),
  §8 (API style + ProblemDetails PT-PT), §11 (Wolverine +
  Outbox), §17 (decisões — sem hierarquia >1 nível, soft-delete
  uniforme), §18 (ADR-010 já escrito na Phase 1a; ADR-011 já
  escrito na Phase 1b).
- `specs/2026-04-26-phase-1a-auth-backend/` — entregou
  multi-tenancy backbone (RLS + Global Query Filter + tests),
  `UserRegisteredIntegrationEvent`, Wolverine pipeline. Phase 2
  consome a infra; **não** modifica nada deste spec
  (specs são histórico imutável; se alguma sub-fase 1a não
  cobrir um caso, regista-se em "Open questions" desta phase 2).
- `specs/2026-04-26-phase-1b-auth-ui/` — entregou shell Angular,
  `authInterceptor`, `authGuard`, PrimeNG Aura + Tailwind.
  Phase 2 reusa tudo sem modificar.
- Memória do agente —
  `memory/replanning_2026_04_26.md` (Wolverine, PrimeNG +
  Tailwind + Signals, sem MediatR, Angular 21).
  `memory/project_name.md` (canonical Sextante).
- `src/Modules/Identity/Sextante.Modules.Identity.PublicApi/
  Events/UserRegisteredIntegrationEvent.cs` — contrato do
  evento que o subscriber Financial vai consumir (Phase 2 lê,
  não modifica).
- `src/Bootstrap/Sextante.Host/Program.cs` — onde Phase 2
  enxerta `AddFinancialModule()` + `MapFinancialEndpoints()`
  + adiciona `FinancialDbContext` ao migration runner.

## Open questions

- **CreditCard com OpeningBalance negativa**: decisão atual é
  `OpeningBalance.Amount >= 0` para todos os tipos. Se durante
  dogfooding for evidente que faz falta saldo inicial negativo
  para CreditCard (limite usado já no momento de signup),
  registar em backlog para Replanning. Workaround imediato:
  criar a CreditCard com OpeningBalance = 0 e adicionar uma
  transação de ajuste.
- **Lista exata de PrimeIcons na allowlist de Category**:
  curar 20-30 ícones em `app/features/financial/common/
  category-icons.ts` (e mirror em
  `Application/Common/AllowedCategoryIcons.cs` para
  validação Domain). Lista exata fica para a implementação,
  com sanity check visual.
- **Tipo de gráfico default do dashboard** (donut por
  categoria vs bar por mês): donut por categoria é o default
  per AskUserQuestion ("gráfico simples"); bar por mês fica
  para Phase 5+ quando recorrentes adicionarem séries
  temporais com sentido. Confirmar no início da implementação.
- **`PrimaryCurrency` na tabela `shared.Tenants`** vs em
  configuração separada: opta-se por coluna na tabela
  (mais simples, atómico com signup). Migration adiciona com
  default `'EUR'`; Phase 3 introduz UI de mudança.
- **Cursor encoding** (opaco base64 vs claro
  `(occurredAt|id)`): opta-se por opaco para evitar clientes
  artisanais; documentar formato em comentário no
  `Cursor.cs` para debug.
- **EF Core `OwnsOne` para Money e RLS**: confirmar que
  Global Query Filter + `OwnsOne` + RLS coexistem sem
  surpresa de geração SQL (testar na primeira migration).
- **Allowlist de PrimeIcons sincronizada Domain ↔ Frontend**:
  manualmente espelhada agora; potencial geração automática
  via source generator pós-MVP se a lista crescer.
- **Restore de soft-deleted via psql**: documentar no
  `validation.md` como inner-loop dev quando UI de restore
  fica out of scope. Não é parte do walkthrough manual.
