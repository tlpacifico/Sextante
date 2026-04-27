# Validation — Phase 2: Categorias + Contas + Transações manuais + Dashboard mínimo

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **Módulo Financial scaffolded** com 5 projetos
   (`Domain`/`Application`/`Infrastructure`/`Api`/`PublicApi`) sob
   `src/Modules/Financial/`, registado em `Sextante.slnx` e
   `Sextante.Host`.
2. **Architecture tests verdes** cobrindo o módulo Financial.
3. **Schema `financial`** criado com migrations independentes
   e RLS ativo nas três tabelas (`accounts`, `categories`,
   `transactions`).
4. **CRUD completo via REST** para Account, Category, Transaction
   com soft-delete.
5. **Subscriber `UserRegisteredIntegrationEvent`** cria 11 categorias
   seed (7 Expense + 4 Income) no signup.
6. **Arquivar Category com transações ativas falha** com erro
   PT-PT explícito.
7. **`Money` VO ativo** em todo o domínio Financial; sem `decimal`
   solto em `Account`/`Transaction`.
8. **Dashboard `/app/dashboard` funcional**: filtros + tabela com
   paginação cursor + cards de totais + gráfico (PrimeNG Chart
   donut por categoria de despesa, toggle para receita).
9. **Páginas CRUD `/app/accounts` e `/app/categories`** completas.
10. **Domain unit tests verdes** (Money / Account / Category /
    Transaction invariants).
11. **Karma unit tests verdes** (FinancialApiService, money pipe,
    financial.store, dashboard.page, accounts.page).
12. **Integration tests verdes**:
    - CRUD por entidade.
    - Multi-tenancy estendida (per tech-stack §4.7) — tenant A
      não vê / não modifica / não arquiva dados do tenant B;
      insert auto-popula TenantId; query sem `TenantContext`
      lança.
    - Seed de categorias no signup.
13. **Suite Phase 1a/1b continua verde** (multi-tenancy do Identity,
    refresh-cookie, auth interceptor).
14. **Migration adicionada a `shared.Tenants`** com coluna
    `PrimaryCurrency varchar(3) NOT NULL DEFAULT 'EUR'`.
15. **Manual browser walkthrough completo** per "Manual browser
    walkthrough" abaixo.
16. **`specs/roadmap.md` Phase 2 checkboxes ticados** via conversa
    com o agente (AGENTS.md §2 regra 1, nunca à mão).
17. **`CHANGELOG.md`** com nova entrada datada produzida pela skill
    `changelog`, sumarizando Phase 2.
18. **GitHub Actions CI verde** — build .NET, test .NET, build
    Angular, `npm test` todos passados.
19. **Sanity check responsivo** per `tech-stack.md` §19.5 e
    `mission.md` §4.6: dashboard, accounts e categories renderizam
    corretamente em viewports 375 × 667 (iPhone SE), 768 × 1024
    (iPad portrait) e 1280 × 800 (desktop) — sem overflow horizontal
    do `<body>`, dialogs cabem (95vw em mobile), tabelas com scroll
    horizontal interno em mobile, legenda do gráfico donut alterna
    entre right e bottom conforme breakpoint.

## How to verify each bullet

1. **Módulo scaffolded.**
   - `ls src/Modules/Financial/` lista os 5 projetos.
   - `dotnet sln Sextante.slnx list` inclui-os.
   - `dotnet build -c Release` verde.

2. **Architecture tests.**
   - `dotnet test --filter
     FullyQualifiedName~ArchitectureTests` verde.
   - Inspeção: existe `FinancialDependencyTests.cs` cobrindo as
     regras de §3.1 + a regra "Module.Financial não referencia
     Module.Identity exceto via PublicApi".

3. **Schema + RLS.**
   - Após `docker compose up`, conectar via psql:
     ```sql
     \dn  -- lista schemas, deve mostrar 'financial'
     \dt financial.*  -- accounts, categories, transactions
     SELECT relrowsecurity, relforcerowsecurity
       FROM pg_class
       WHERE relname IN ('accounts', 'categories', 'transactions')
         AND relnamespace = (SELECT oid FROM pg_namespace
                              WHERE nspname = 'financial');
     -- Esperado: ambos colunas TRUE para todas as 3 tabelas.
     ```
   - `SELECT polname, polcmd FROM pg_policies WHERE schemaname =
     'financial';` mostra `accounts_tenant_isolation`,
     `categories_tenant_isolation`,
     `transactions_tenant_isolation`.

4. **CRUD REST.**
   - `dotnet test --filter
     FullyQualifiedName~Financial.Integration.AccountsCrud` verde.
   - Idem `CategoriesCrud`, `TransactionsCrud`.
   - Inspeção do `/openapi/v1.json` em runtime
     (`http://localhost/openapi/v1.json`) lista os 13+ endpoints
     `/api/financial/...`.

5. **Seed de categorias.**
   - `dotnet test --filter
     FullyQualifiedName~SeedCategoriesOnSignupTests` verde.
   - Walkthrough manual passo 3: após signup, `/app/categories`
     mostra exatamente 11 categorias com nomes e Kinds esperados.

6. **Archive Category bloqueado.**
   - `dotnet test --filter
     FullyQualifiedName~ArchiveCategoryHandlerTests` verde.
   - Walkthrough manual passo 8: arquivar "Salário" com 1
     transação retorna toast PT-PT exato:
     "Não é possível arquivar uma categoria com transações
     ativas".

7. **Money VO sem `decimal` solto.**
   - `dotnet test --filter FullyQualifiedName~MoneyTests` verde
     (currency mismatch lança; conversões corretas).
   - Inspeção (architecture test):
     `tests/Sextante.ArchitectureTests/MoneyDisciplineTests.cs`
     verifica que nenhuma propriedade pública em
     `Module.Financial.Domain` é `decimal` (apenas `Money`).

8. **Dashboard.**
   - Walkthrough manual passos 4-7 passam.
   - Karma: `dashboard.page.spec.ts` verde
     (`npm test -- --watch=false --browsers=ChromeHeadless` em
     `src/Web/Sextante.Web/`).
   - DevTools: cards atualizam em ≤200ms ao mudar filtro
     (sanity, não benchmark).

9. **Páginas CRUD.**
   - Walkthrough manual passos 4 e 8 passam.
   - Karma: `accounts.page.spec.ts` e
     `categories.page.spec.ts` verdes.

10. **Domain unit tests.**
    - `dotnet test
      tests/Modules/Financial.Domain.Tests/` verde.
    - Cobertura inclui: Money currency mismatch, Account
      OpeningBalance imutável + ≥0, Category icon allowlist +
      colorHex regex + EnsureCanArchive, Transaction Amount > 0
      + OccurredAt ≤ now + Tags 10/50.

11. **Karma unit tests.**
    - `cd src/Web/Sextante.Web && npm test -- --watch=false
      --browsers=ChromeHeadless` verde.
    - Files cobertos: `financial-api.service.spec.ts`,
      `money.pipe.spec.ts`, `financial.store.spec.ts`,
      `dashboard.page.spec.ts`, `accounts.page.spec.ts`,
      `categories.page.spec.ts`.

12. **Integration tests.**
    - `dotnet test --filter
      FullyQualifiedName~Sextante.IntegrationTests.Financial`
      verde (todos os arquivos sob
      `tests/Sextante.IntegrationTests/Financial/`).
    - Multi-tenancy: `MultiTenancyTests.cs` cobre os 5 cenários
      (read/update/archive/insert auto-populate/sem context)
      explicitamente para Account, Category, Transaction.

13. **Phase 1a/1b regressão zero.**
    - `dotnet test -c Release --filter
      FullyQualifiedName~Identity` verde.
    - `dotnet test --filter
      FullyQualifiedName~RefreshCookieTests` verde
      (Phase 1b auth UI integration).
    - `dotnet test --filter
      FullyQualifiedName~MultiTenancyTests` (Identity-side)
      verde — RLS de `shared.*` não regredido.

14. **Migration `PrimaryCurrency`.**
    - `git diff main..phase-2-financial-core
      src/Modules/Identity/.../Persistence/Migrations/`
      mostra migration nova adicionando coluna.
    - psql: `\d shared."Tenants"` mostra coluna
      `PrimaryCurrency character varying(3) not null default
      'EUR'::character varying`.

15. **Manual walkthrough.**
    - Executar todos os passos da secção abaixo num ambiente
      limpo (`docker compose down -v && docker compose up`).

16. **Roadmap.**
    - `git diff main..phase-2-financial-core --
      specs/roadmap.md` mostra Phase 2 com `[x]` em cada bullet,
      alterado via conversa com o agente (não à mão).

17. **Changelog.**
    - `git diff main..phase-2-financial-core -- CHANGELOG.md`
      mostra secção nova com data do merge sumarizando Phase 2.

18. **CI verde.**
    - GitHub Actions no commit mais recente do
      `phase-2-financial-core` mostra todos os jobs verdes.

19. **Sanity check responsivo.**
    - DevTools → device toolbar → ciclar entre `iPhone SE`
      (375 × 667), `iPad` (768 × 1024) e responsive desktop
      (1280 × 800).
    - Para cada viewport, navegar `/app/dashboard`,
      `/app/accounts`, `/app/categories` (após login):
      - Nenhum scroll horizontal no `<body>`.
      - `p-dialog` ("Nova transação", "Nova conta", "Nova
        categoria") cabem dentro do viewport com margens; em
        375 px ocupam 95vw.
      - Tabelas largas mostram scroll horizontal **dentro** da
        secção de tabela (não scroll de página inteira).
      - Filtros do dashboard (período / categorias / contas)
        empilham 1 coluna em mobile, 3 em desktop. Cards de
        totais idem.
      - Gráfico donut: legenda à direita em desktop (≥ 768 px),
        em baixo em mobile.
      - Botão "Nova ___" sempre visível e clicável (touch
        target ≥ 44 px).

## Manual browser walkthrough

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais por valores de teste.

```bash
# Pré-requisito limpo:
docker compose down -v && docker compose up
# Aplicação na http://localhost/. Migrations correm no startup
# do Host (já criou o schema 'financial' + RLS).
```

1. **Signup tenant A.** Abrir `http://localhost/` → redirect
   para `/login` → "Criar conta" → submeter
   `email=tester+phase2@example.com`, `password=Phase2Password!`,
   `tenantName=Phase 2 A`. Esperado: toast "Conta criada" →
   redirect `/login` → login com mesmas credenciais → land em
   `/app/dashboard` (vazio: cards 0, gráfico vazio, lista
   vazia).

2. **Verificar categorias seed.** Navegar para `/app/categories`.
   Esperado: 11 linhas. Filtrar por Kind=Expense → 7 linhas
   (`Alimentação`, `Transporte`, `Saúde`, `Lazer`, `Casa`,
   `Educação`, `Outros`). Filtrar por Kind=Income → 4 linhas
   (`Salário`, `Freelance`, `Investimentos`, `Outros`). Cada
   categoria mostra ícone PrimeIcon + chip de cor.

3. **Criar contas.** Navegar para `/app/accounts`. "Nova conta".
   Submeter:
   - `Name=Conta Principal`, `Type=Checking`,
     `OpeningBalance=1000.00 EUR`. Toast "Conta criada".
   - "Nova conta" outra vez: `Name=Carteira`, `Type=Cash`,
     `OpeningBalance=50.00 EUR`. Toast "Conta criada".

   Tabela mostra 2 linhas. Edit "Conta Principal" → confirma
   que campo `OpeningBalance` é read-only (per requirements
   decisão de imutabilidade). Cancelar.

4. **Criar transações.** Voltar a `/app/dashboard`. "Nova
   transação". Criar 5 entradas (data = hoje em todas):
   - `Conta Principal` / `Alimentação` / `30 EUR` / "Mercado".
   - `Conta Principal` / `Transporte` / `20 EUR` / "Combustível".
   - `Conta Principal` / `Casa` / `500 EUR` / "Renda".
   - `Conta Principal` / `Salário` / `2000 EUR` / "Mensal".
   - `Carteira` / `Freelance` / `300 EUR` / "Ad-hoc".

   Esperado: toast por cada criação; tabela do dashboard
   atualiza após cada submit; cards refletem o novo total.

5. **Verificar dashboard final.** Sem filtros (período = mês
   corrente):
   - **Cards**: Entrada=2300 €, Saída=550 €, Líquido=1750 €
     (formatado PT-PT com `€` à direita).
   - **Gráfico**: donut com 3 fatias (Alimentação 30,
     Transporte 20, Casa 500), legenda lateral com valores
     formatados.
   - **Tabela**: 5 linhas ordenadas por data desc, com
     coluna "Categoria" mostrando chip com ícone+cor; coluna
     "Valor" color-coded (vermelho expense, verde income).

6. **Filtrar.** Aplicar filtro `Conta = Carteira`. Esperado:
   tabela mostra só "Freelance 300"; cards: Entrada=300 €,
   Saída=0 €, Líquido=300 €; gráfico donut vazio (não há
   despesas com este filtro).

7. **Toggle gráfico Despesas → Receitas.** Limpar filtro de
   conta. Botão Despesas/Receitas → escolher Receitas.
   Esperado: gráfico donut com 2 fatias (Salário 2000,
   Freelance 300).

8. **Tentar arquivar categoria com transação.** Em
   `/app/categories`, arquivar "Salário". Esperado: toast
   PT-PT "Não é possível arquivar uma categoria com
   transações ativas". Linha continua na tabela.

9. **Arquivar transação.** Em `/app/dashboard`, arquivar a
   transação "Freelance". Esperado: linha desaparece da
   tabela; cards recalculam (Entrada=2000, Líquido=1450);
   gráfico Receitas atualiza (só Salário).

10. **Persistência.** F5. Esperado: todas as ações
    persistidas; estado idêntico ao passo 9.

11. **Logout + signup tenant B (multi-tenancy UI).** User
    menu → Terminar sessão → `/login`. "Criar conta" →
    `email=tester+phase2-b@example.com`,
    `password=Phase2Password!`, `tenantName=Phase 2 B`.
    Login. Esperado:
    - `/app/dashboard` vazio (sem transações, sem contas).
    - `/app/accounts` vazio.
    - `/app/categories` mostra 11 categorias seed
      **independentes** das de tenant A (mesmo IDs?, mesmos
      nomes — visualmente são "iguais", mas no DB são
      registos diferentes; nenhum dado de tenant A
      atravessa).
    - Console + Network tab: nenhuma resposta da API contém
      IDs ou nomes de transações/contas do tenant A.

12. **Inner-loop dev (sanity check, opcional).** Em psql:
    ```sql
    SET app.current_tenant_id = '<tenant-A-uuid>';
    SELECT count(*) FROM financial.transactions;  -- esperado: 4
    SET app.current_tenant_id = '<tenant-B-uuid>';
    SELECT count(*) FROM financial.transactions;  -- esperado: 0
    RESET app.current_tenant_id;
    SELECT count(*) FROM financial.transactions;  -- ERROR: RLS
    ```

## Out of scope for validation

- **ECB / multi-moeda real / câmbio aplicado por transação.**
  Phase 3.
- **Performance / Lighthouse scores.** Pós-MVP. Sanity manual
  apenas (≤200ms para mudança de filtro a aplicar; ≤500ms para
  carregar 50 transações).
- **OpenAPI snapshot diffing.** Out per requirements.md.
  Documento gerado em runtime e inspecionado visualmente
  durante implementação; sem checked-in artifact.
- **Sub-agent deep review formal.** Out per requirements.md.
  Opt-in; se invocado, output anexado ao PR — mas não bloqueia
  merge.
- **Playwright/Cypress E2E.** Manual walkthrough é o artefacto
  desta phase.
- **Audit de acessibilidade (axe-core, screen-reader
  walkthrough).** Sanity check (labels, aria, ordem de tab)
  apenas; audit pós-MVP.
- **CSP headers / security headers tuning.** Phase 6.
- **Rate limiting específico para `/api/financial/*`.** Phase 6.
  Rate limiting global do Phase 1a continua em vigor.
- **Live VPS deploy + cert Let's Encrypt.** Phase 6.
- **Penetration testing / security audit formal.** Pós-MVP.
- **Browser cross-version testing (Safari, Firefox, Edge).**
  Chromium-based suficiente; revisitar em Phase 6.
- **Audit responsivo formal** (Lighthouse mobile, screen-reader
  walkthrough mobile, device físicos). O baseline responsivo
  (mobile-first, breakpoints `tech-stack.md` §19.5, sanity check
  em 3 viewports) é IN scope per DoD #19; audit formal pós-MVP.
- **UI de restore de soft-deleted.** Out per requirements.md.
  Restore manual via psql se inner-loop dev exigir.
- **Backups / restore-de-teste do schema `financial`.** Phase 6.
- **Stress test com >10k transações por tenant.** Pós-MVP.
- **Seed de moedas além de EUR.** Phase 3 introduz tabela
  `Currency` completa.
