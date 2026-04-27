# Plan — Phase 2: Categorias + Contas + Transações manuais + Dashboard mínimo

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 2 é o primeiro módulo de domínio próprio
> (`Financial`) — tudo o que aqui se decide vira precedente para
> Phases 3+. Validação inclui Domain unit tests + Karma unit tests
> + integration tests CRUD/multi-tenancy + manual browser walkthrough.
> Phase 2 **não** está marcada 🛡️ no roadmap, logo sub-agent deep
> review é opcional.

---

## 1. Scaffold do módulo Financial

- 1.1 Criar 5 projetos sob `src/Modules/Financial/` espelhando a
  estrutura de `src/Modules/Identity/`:
  - `Sextante.Modules.Financial.Domain/`
  - `Sextante.Modules.Financial.Application/`
  - `Sextante.Modules.Financial.Infrastructure/`
  - `Sextante.Modules.Financial.Api/`
  - `Sextante.Modules.Financial.PublicApi/`
  Todos `net10.0` via `Directory.Build.props`. Referências entre
  projetos seguindo a tabela de tech-stack §3.1 (Domain → SharedKernel
  apenas; Application → Domain + SharedKernel + own PublicApi;
  Infrastructure → Application + Domain + SharedKernel + own PublicApi;
  Api → Application + Infrastructure + own PublicApi + SharedKernel;
  PublicApi → SharedKernel apenas).
- 1.2 Adicionar os 5 projetos a `Sextante.slnx` (na secção
  `<Folder Name="/src/Modules/Financial/">`).
- 1.3 Atualizar `Directory.Packages.props` com pacotes que ainda
  não estão centralmente declarados (ex.: `FluentValidation`,
  `Wolverine.FluentValidation` se ainda não estiver). Nenhum
  pacote novo de Wolverine — herdar do que Phase 1a já registou.
- 1.4 Criar projeto de testes `tests/Modules/Financial.Domain.Tests/`
  e `tests/Modules/Financial.Application.Tests/` com referências para
  os respetivos projetos. Adicionar à `Sextante.slnx`.
- 1.5 Atualizar `tests/Sextante.ArchitectureTests/` para validar as
  regras de §3.1 sobre os novos projetos. Acrescentar fact: "Module
  Financial não referencia Module Identity exceto via PublicApi".
- 1.6 Sanidade: `dotnet build -c Release` verde com novos projetos
  vazios (apenas marker classes); `dotnet test
  --filter FullyQualifiedName~ArchitectureTests` verde.

## 2. Domain — entidades, Money e invariants

- 2.1 Validar `Sextante.SharedKernel/Money.cs` (tech-stack §7.1).
  Se não existir, criar como `record struct Money(decimal Amount,
  string Currency)` com:
  - `Add`/`Subtract` lançam `MoneyCurrencyMismatchException` se
    `Currency` diferem.
  - `Multiply(decimal factor)` / `Divide(decimal divisor)`.
  - `ConvertTo(string targetCurrency, decimal exchangeRate)` retorna
    novo `Money`.
  - Validação no construtor: `Currency` ISO 4217 (3 chars maiúsculas).
  - JSON converter para serialização `{ "amount": 12.34,
    "currency": "EUR" }`.
- 2.2 `Sextante.Modules.Financial.Domain/Accounts/Account.cs`:
  - Propriedades: `Id (Guid v7)`, `TenantId (Guid)`, `Name (string)`,
    `Type (AccountType enum)`, `OpeningBalance (Money)`,
    `CreatedAt/UpdatedAt/DeletedAt (timestamptz)`,
    `Version (uint)` para concorrência otimista.
  - `AccountType`: `Checking`, `Savings`, `Cash`, `CreditCard`.
  - Construtor `private` + factory `Create(name, type,
    openingBalance, tenantId)` que valida `Name` não vazio
    (≤200 chars) e `OpeningBalance.Amount >= 0` (CreditCard pode
    ter saldo inicial negativo? — não no MVP; documentar como open
    question em `requirements.md`).
  - Método `Rename(string newName)` valida e atualiza
    `UpdatedAt`/`Version`.
  - Método `Archive()` valida e set `DeletedAt = DateTime.UtcNow`.
  - **Invariant**: `OpeningBalance` é imutável depois de criada
    (sem setter público; sem método `ChangeOpeningBalance`).
- 2.3 `Sextante.Modules.Financial.Domain/Categories/Category.cs`:
  - Propriedades: `Id`, `TenantId`, `Name`, `Kind (CategoryKind enum)`,
    `IconName (string)`, `ColorHex (string)`, audit + soft-delete.
  - `CategoryKind`: `Expense`, `Income`.
  - Validação: `Name` 1-100 chars, `IconName` ∈ allowlist
    (PrimeIcons ex.: `pi-shopping-cart`, `pi-car`, `pi-money-bill`,
    etc.), `ColorHex` regex `^#[0-9A-Fa-f]{6}$`.
  - `Archive()` exige zero `Transaction` ativas associadas (regra
    aplicada na Application layer com check explícito; Domain
    expõe `EnsureCanArchive(int activeTransactionCount)` que lança
    `CategoryHasActiveTransactionsException` se > 0).
- 2.4 `Sextante.Modules.Financial.Domain/Transactions/Transaction.cs`:
  - Propriedades: `Id`, `TenantId`, `AccountId`, `CategoryId`,
    `OccurredAt (timestamptz)`, `Amount (Money)`, `Description
    (string?)`, `Tags (IReadOnlyList<string>)` — mapped to `jsonb`,
    audit + soft-delete.
  - Construtor factory que valida: `Amount.Amount > 0`
    (sinal positivo sempre; o `Kind` da Category determina se é
    despesa ou receita), `OccurredAt` não no futuro (configurável,
    default true), `Description` ≤ 500 chars, `Tags` distintos e
    cada uma ≤ 50 chars (até 10 tags).
  - **Invariant**: `Amount.Currency` igual ao `Currency` primary
    do tenant (Phase 2 não suporta multi-moeda real; check no
    handler usando `ITenantContext` + `ITenantCurrencyResolver`
    a registar — ver §4).
- 2.5 `Sextante.Modules.Financial.Domain/Common/`:
  - `ITenantOwned` (per tech-stack §4.1), `IFinancialAggregate`
    marker para `NetArchTest`.
  - `MoneyCurrencyMismatchException`,
    `CategoryHasActiveTransactionsException`, etc.

## 3. Infrastructure — DbContext, schema, RLS, EF converters

- 3.1 `Sextante.Modules.Financial.Infrastructure/Persistence/
  FinancialDbContext.cs`:
  - `OnModelCreating`: `modelBuilder.HasDefaultSchema("financial")`.
  - `MigrationsHistoryTable("__migrations", "financial")` (per
    tech-stack §5).
  - Global Query Filter por TenantId em `Account`, `Category`,
    `Transaction` (per §4.1). Soft-delete filter
    (`e.DeletedAt == null`).
  - Auto-população de `TenantId` em `SaveChangesAsync` para
    entidades `Added` (per §4.1).
  - Audit: `CreatedAt`/`UpdatedAt` setados automaticamente.
- 3.2 EF Core converters:
  - `Money` → coluna `(numeric(20,8) Amount, varchar(3) Currency)`
    via `OwnsOne` ou `ValueConverter`. Decisão: `OwnsOne` para
    preservar query-ability dos sub-campos.
  - `IReadOnlyList<string>` → `jsonb` para `Tags` (default
    `'[]'::jsonb`).
- 3.3 Migrations:
  - `dotnet ef migrations add InitialFinancial -p
    src/Modules/Financial/.../Infrastructure -s
    src/Bootstrap/Sextante.Host -o Persistence/Migrations` em
    schema `financial`.
  - SQL adicional na migration para RLS (per §4.2):
    - `ALTER TABLE financial.accounts ENABLE ROW LEVEL SECURITY;`
    - `CREATE POLICY accounts_tenant_isolation ON financial.accounts
      USING (tenant_id = current_setting('app.current_tenant_id')::uuid);`
    - Repetir para `categories` e `transactions`.
    - `ALTER TABLE financial.<t> FORCE ROW LEVEL SECURITY;` (cobre
      o role da app que não tem `BYPASSRLS`).
- 3.4 `DbConnectionInterceptor` para `SET app.current_tenant_id`
  já existe em `Sextante.Infrastructure` (Phase 1a). Confirmar que
  é registado no DI scope do `FinancialDbContext` (mesmo connection
  pool, mesmo interceptor — não duplicar).
- 3.5 Repositórios mínimos: `IAccountRepository`,
  `ICategoryRepository`, `ITransactionRepository` em `Domain` com
  implementações em `Infrastructure`. EF Core direto via
  `DbContext` é OK; repositórios servem para handlers não
  conhecerem `DbContext` diretamente.
- 3.6 `ITenantCurrencyResolver` (em `Identity.PublicApi` ou
  `SharedKernel`): retorna o `Currency` primary do tenant atual.
  Para Phase 2, lookup contra `shared.Tenants.PrimaryCurrency`
  (campo a adicionar via migration **a Identity**, não Financial,
  com default `'EUR'`). Decisão registada em `requirements.md`.

## 4. Application — Wolverine handlers + queries + validators

- 4.1 Estrutura por feature (vertical slice) sob
  `Sextante.Modules.Financial.Application/Features/`:
  - `Accounts/{Create,Update,Archive,GetById,List}/`
  - `Categories/{Create,Update,Archive,GetById,List}/`
  - `Transactions/{Create,Update,Archive,GetById,List,Summary,
    ByCategory}/`
  Cada folder com `*Command.cs`/`*Query.cs`, `*Handler.cs`,
  `*Validator.cs` (FluentValidation), `*Result.cs` (DTO de saída).
- 4.2 Handlers via Wolverine (per tech-stack §3.5). Pipeline já
  configurado na Phase 1a (logging, transação, validação) — apenas
  registar os handlers via descoberta automática do assembly.
- 4.3 `ListTransactionsQuery`:
  - Filtros: `DateFrom`, `DateTo`, `CategoryIds`, `AccountIds`.
  - Paginação cursor-based: cursor opaco base64 contendo
    `(OccurredAt, Id)` do último item da página anterior.
    `Cursor.Encode/Decode` em `Application/Common/`.
  - Ordenação: `OccurredAt DESC, Id DESC`.
  - Page size default 50, max 100.
- 4.4 `TransactionSummaryQuery`: dados os mesmos filtros, retorna
  `(IncomeTotal: Money, ExpenseTotal: Money, Net: Money)`.
  Implementação: `JOIN` com Category para somar por `Kind`.
- 4.5 `TransactionsByCategoryQuery`: agregado para o gráfico do
  dashboard. Retorna `[(CategoryId, CategoryName, IconName,
  ColorHex, Total: Money)]` para `Kind = Expense` por default
  (podendo ser `Income` por flag).
- 4.6 `ArchiveCategoryHandler`: chama
  `categoryRepository.CountActiveTransactionsAsync(categoryId)` e
  passa o resultado para `category.EnsureCanArchive(count)`. Se
  lança, handler converte para `ProblemDetails` PT-PT
  "Não é possível arquivar uma categoria com transações ativas".
- 4.7 Subscriber do `UserRegisteredIntegrationEvent`:
  - `Sextante.Modules.Financial.Application/Integration/
    SeedDefaultCategoriesHandler.cs`.
  - Resolve `ITenantContext` para o `TenantId` recém-criado (no
    contexto do evento, o tenant já está ativo).
  - Cria categorias seed (lista no `requirements.md`):
    - Expense: `Alimentação`, `Transporte`, `Saúde`, `Lazer`,
      `Casa`, `Educação`, `Outros`.
    - Income: `Salário`, `Freelance`, `Investimentos`, `Outros`.
    Cada uma com `IconName` (PrimeIcon) e `ColorHex` pré-definidos
    em `Application/Common/DefaultCategories.cs`.

## 5. Api — REST endpoints

- 5.1 `Sextante.Modules.Financial.Api/Endpoints/`:
  - `AccountsEndpoints.cs` → `MapGroup("/api/financial/accounts")`
    com `MapPost("")` (create), `MapGet("{id:guid}")`,
    `MapPut("{id:guid}")` (update), `MapDelete("{id:guid}")`
    (archive — soft-delete), `MapGet("")` (list).
  - `CategoriesEndpoints.cs` similar, `/api/financial/categories`.
  - `TransactionsEndpoints.cs` em `/api/financial/transactions`
    + sub-endpoints `MapGet("summary")` e `MapGet("by-category")`.
  - Todos com `[Authorize]` (default; `ITenantContext` lança se
    sem claim).
- 5.2 Cada endpoint despacha via Wolverine
  (`InvokeAsync<TResult>(command)`). Erros mapeados para
  `ProblemDetails` PT-PT por exception filter já configurado na
  Phase 1a.
- 5.3 Registo do módulo no Host:
  - `src/Bootstrap/Sextante.Host/Program.cs` adiciona
    `builder.Services.AddFinancialModule()` (extensão em
    `Financial.Infrastructure/DependencyInjection.cs`).
  - `app.MapFinancialEndpoints()` (extensão em
    `Financial.Api/DependencyInjection.cs`).
- 5.4 OpenAPI: tech-stack §8 manda auto-gen em `/openapi/v1.json`.
  Phase 2 não checked-in snapshot (excluded per AskUserQuestion);
  apenas verificar em runtime que o documento gera sem erros.

## 6. Frontend — services + state (Angular Signals)

- 6.1 `src/Web/Sextante.Web/src/app/core/api/financial-api.service.ts`:
  - Injetável (`providedIn: 'root'`).
  - Métodos: `listAccounts()`, `createAccount(req)`,
    `updateAccount(id, req)`, `archiveAccount(id)`,
    `listCategories()`, `createCategory(req)`,
    `updateCategory(id, req)`, `archiveCategory(id)`,
    `listTransactions(filter, cursor?)`, `createTransaction(req)`,
    `updateTransaction(id, req)`, `archiveTransaction(id)`,
    `getTransactionSummary(filter)`,
    `getTransactionsByCategory(filter)`.
  - DTOs em
    `src/Web/Sextante.Web/src/app/core/api/financial.types.ts`
    (TypeScript interfaces espelhando os DTOs C#: Money é
    `{ amount: number; currency: string }`).
- 6.2 `src/Web/Sextante.Web/src/app/features/financial/state/
  financial.store.ts`:
  - `accounts: Signal<AccountDto[]>`, `categories:
    Signal<CategoryDto[]>`, `transactions: Signal<TransactionDto[]>`,
    `transactionsCursor: Signal<string | null>`,
    `transactionFilter: WritableSignal<TransactionFilter>`.
  - Computed: `activeAccounts`, `expenseCategories`,
    `incomeCategories`.
  - Métodos `loadAccounts()`, `loadCategories()`,
    `loadTransactions(reset = true)` que reusam o
    `FinancialApiService`.
- 6.3 `authInterceptor` da Phase 1b já anexa `Authorization`;
  nenhuma alteração necessária.
- 6.4 Formatação Money PT-PT:
  - `src/Web/Sextante.Web/src/app/core/format/money.pipe.ts`:
    `@Pipe({ name: 'money', standalone: true })`. Recebe `Money`
    (`{ amount, currency }`) e retorna string formatada
    (`Intl.NumberFormat('pt-PT', { style: 'currency', currency })`).

## 7. Frontend — páginas CRUD `/app/accounts` e `/app/categories`

- 7.1 `app/features/financial/pages/accounts.page.ts`:
  - PrimeNG `p-table` com colunas Name, Type (badge), OpeningBalance
    (formatado via `money` pipe), CreatedAt, ações.
  - Botão "Nova conta" abre `p-dialog` com form Reactive:
    `name (required, 1-200)`, `type (p-dropdown enum)`,
    `openingBalance (p-inputNumber, mode=currency,
    currency=tenantCurrency)`.
  - Edit reusa o mesmo dialog com pre-fill (apenas `name` e
    `type`; `openingBalance` é read-only depois de criado per
    §2.2 invariant).
  - Archive: confirmação via PrimeNG `confirmDialog`. Toast PT-PT
    de sucesso/erro.
- 7.2 `app/features/financial/pages/categories.page.ts`:
  - `p-table` com colunas Name, Kind (badge expense/income),
    Icon (render PrimeIcon), Color (chip), ações.
  - "Nova categoria" abre `p-dialog` com form: `name (required)`,
    `kind (p-dropdown {Expense, Income})`,
    `iconName (p-select com lista de PrimeIcons curados em
    constante `app/features/financial/common/category-icons.ts`)`,
    `colorHex (p-colorPicker)`.
  - Archive falha com toast PT-PT
    "Não é possível arquivar uma categoria com transações ativas"
    quando o backend retorna 400.
- 7.3 Rotas registadas em `app.routes.ts` debaixo do
  `AppShellComponent` e do `authGuard` (per Phase 1b):
  - `{ path: 'app/accounts', loadComponent: () =>
      import('./features/financial/pages/accounts.page').then(m =>
      m.AccountsPage) }`
  - mesmo para `app/categories`.
- 7.4 Adicionar links de navegação no `AppShellComponent`
  (`app/layout/app-shell.component.ts`) via PrimeNG `p-menubar`
  ou `p-sidebar` (a sidebar placeholder da Phase 1b passa a
  funcional).

## 8. Frontend — `/app/dashboard`

- 8.1 Substituir `app/features/dashboard/dashboard.placeholder.
  component.ts` por `app/features/dashboard/dashboard.page.ts`.
- 8.2 Layout (Tailwind grid):
  - **Linha 1 — Filtros**: PrimeNG `p-calendar selectionMode=
    "range"` (período), `p-multiSelect` para categorias,
    `p-multiSelect` para contas. Default: mês corrente.
  - **Linha 2 — Cards de totais**: 3 cards Tailwind com PrimeNG
    `p-card`: "Entrada", "Saída", "Líquido" (formatados via
    `money` pipe). Atualizam reactivamente via `effect()` quando
    `transactionFilter` muda.
  - **Linha 3 — Gráfico**: PrimeNG `p-chart type="doughnut"`
    com dataset `transactionsByCategory()` (signal computed
    no store). Legenda lateral com total por categoria.
    Default: `Kind = Expense`. Toggle (button group)
    Despesas/Receitas controla um signal local.
  - **Linha 4 — Tabela**: `p-table` com paginação cursor
    (PrimeNG paginator custom: botão "Carregar mais" porque
    cursor é forward-only). Colunas: Data, Conta, Categoria
    (chip com icon+color), Descrição, Valor (color-coded por
    Kind).
- 8.3 Botão "Nova transação" no header da página abre `p-dialog`
  com form: `account (p-dropdown)`, `category (p-dropdown
  filtrado por Kind)`, `occurredAt (p-calendar)`, `amount
  (p-inputNumber currency)`, `description (p-inputText)`,
  `tags` (deferido — sem UI per tech-stack §7.7).
- 8.4 Toast PT-PT em sucesso/erro. Após criar transação, store
  invalida e re-fetch da lista + summary + by-category.
- 8.5 Apagar `app/features/dashboard/dashboard.placeholder.
  component.ts` (e atualizar `app.routes.ts` para apontar
  para o novo componente).

## 9. Tests — Domain unit

> Suite Phase 1a continua verde. Phase 2 acrescenta testes
> Domain.

- 9.1 `tests/Modules/Financial.Domain.Tests/Money/MoneyTests.cs`:
  - `Add` lança `MoneyCurrencyMismatchException` se moedas diferem.
  - `Multiply`, `ConvertTo` retornam novo Money correto.
  - Construtor lança se `Currency` não tem 3 chars.
- 9.2 `Accounts/AccountTests.cs`:
  - `Create` valida nome ≤200, OpeningBalance ≥0 (exceto se
    requirements decidir CreditCard pode negativo).
  - `Rename` atualiza `UpdatedAt` e `Version`.
  - `OpeningBalance` é imutável (sem setter público — teste
    de reflection que confirma).
  - `Archive` set `DeletedAt`.
- 9.3 `Categories/CategoryTests.cs`:
  - `Create` valida `IconName` na allowlist e `ColorHex` regex.
  - `EnsureCanArchive(0)` não lança.
  - `EnsureCanArchive(1)` lança
    `CategoryHasActiveTransactionsException`.
- 9.4 `Transactions/TransactionTests.cs`:
  - `Create` rejeita `Amount.Amount <= 0`.
  - `Create` rejeita `OccurredAt > now`.
  - `Tags` distintos (rejeita duplicados).
  - Limite 10 tags / 50 chars cada.
- 9.5 `xunit.runner.json` herdado do projeto Phase 1a; sem
  config nova.

## 10. Tests — Application unit + Integration + Architecture

- 10.1 `tests/Modules/Financial.Application.Tests/`:
  - `Accounts/CreateAccountHandlerTests.cs` (mock repo + tenant
    context; verifica TenantId auto-popular).
  - `Categories/ArchiveCategoryHandlerTests.cs` (count=0 → ok;
    count=3 → exception convertida a ProblemDetails).
  - `Transactions/ListTransactionsHandlerTests.cs` (cursor
    encoding/decoding; filtros aplicados).
  - `Transactions/SummaryHandlerTests.cs`.
- 10.2 `tests/Sextante.IntegrationTests/Financial/`:
  - `AccountsCrudTests.cs` — full CRUD via `WebApplicationFactory`
    + Testcontainers Postgres (já existe na Phase 1a).
  - `CategoriesCrudTests.cs`.
  - `TransactionsCrudTests.cs`.
  - `MultiTenancyTests.cs` — testes obrigatórios per tech-stack
    §4.7 aplicados ao módulo Financial:
    - Tenant A não vê Account/Category/Transaction de Tenant B.
    - Tenant A não pode update/archive de Tenant B (404 não
      403, para evitar enumeração).
    - Insert auto-popula TenantId mesmo se o handler esquecer.
    - Query sem `TenantContext` lança.
    - Migrations correm com role privilegiado (already covered
      Phase 1a; estender filtro para incluir `financial.*`).
  - `SeedCategoriesOnSignupTests.cs` — após `signup`, GET
    `/api/financial/categories` retorna as 11 categorias seed.
- 10.3 `tests/Sextante.ArchitectureTests/FinancialDependencyTests.cs`:
  - `Module.Financial.Domain` não referencia EF Core nem
    `Module.Identity.*`.
  - `Module.Financial.Application` referencia `Identity.PublicApi`
    (para `ITenantContext`) mas não outros projetos do Identity.
  - `Module.Financial.Api` não referencia
    `Module.Financial.Domain` diretamente (per §3.1 — Api → via
    Application).
- 10.4 Karma unit tests em `src/Web/Sextante.Web/`:
  - `core/api/financial-api.service.spec.ts` — cada método chama
    a URL correta com payload correto.
  - `core/format/money.pipe.spec.ts` — formatação PT-PT com
    EUR, BRL, USD.
  - `features/financial/state/financial.store.spec.ts` —
    `loadTransactions(reset=true)` reseta cursor;
    `loadTransactions(reset=false)` apenda página.
  - `features/dashboard/dashboard.page.spec.ts` — filtros
    atualizam summary; toggle expense/income altera dataset
    do gráfico.
  - `features/financial/pages/accounts.page.spec.ts` — abrir
    dialog, submeter, refresh.
- 10.5 `npm test -- --watch=false --browsers=ChromeHeadless`
  passa local e em CI.

## 11. Manual browser walkthrough

- 11.1 Documentar em `validation.md` § "Manual browser walkthrough":
  - `docker compose up` → http://localhost/login.
  - Signup tester+phase2@example.com → land `/app/dashboard`.
  - Confirmar 11 categorias seed em `/app/categories`
    (7 Expense + 4 Income).
  - Criar 2 contas (Checking "Conta Principal" 1000.00 EUR;
    Cash "Carteira" 50.00 EUR).
  - Criar 5 transações: 3 expense (Alimentação 30, Transporte
    20, Casa 500), 2 income (Salário 2000, Freelance 300).
  - Dashboard mostra cards (Entrada=2300, Saída=550,
    Líquido=1750), gráfico donut por categoria de despesa,
    tabela com 5 transações ordenadas por data desc.
  - Aplicar filtro: período = só hoje. Cards e gráfico
    atualizam.
  - Toggle gráfico Despesas → Receitas. Dataset muda.
  - Tentar arquivar a categoria "Salário" (que tem 1 transação)
    → toast PT-PT "Não é possível arquivar uma categoria com
    transações ativas".
  - Arquivar transação Freelance → desaparece da lista; cards
    + gráfico recalculados.
  - F5 → tudo persiste.
- 11.2 Walkthrough multi-tenant (sanity check UI-side):
  - Logout. Signup tester+phase2-b@example.com.
  - Land em `/app/dashboard` vazio (sem transações). Categorias
    seed presentes mas as do tenant A invisíveis.

## 12. Close-out (Phase 2 não 🛡️)

- 12.1 (Opt-in) Considerar invocar sub-agent deep review focado
  em multi-tenancy do módulo Financial (primeiro stress test
  real de RLS + Global Query Filter combinados em writes
  intensivos). Se invocado, output anexado ao PR.
- 12.2 Marcar `specs/roadmap.md` Phase 2 checkboxes como `[x]`
  via conversa com o agente (AGENTS.md §2 regra 1, nunca à
  mão).
- 12.3 Skill `changelog` para adicionar entrada datada do merge
  com sumário da Phase 2.
- 12.4 Commit `Mark phase 2 as complete`. PR
  `phase-2-financial-core → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, npm test).
  - Suite Phase 1a continua verde.
  - Aprovação humana.
