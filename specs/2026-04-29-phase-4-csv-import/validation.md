# Validation — Phase 4: Importação CSV + Regras de categorização 🛡️

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui —
> são wishlist.

## Definition of done

1. **`CategorizationRule`, `ImportProfile`, `ImportBatch` criadas**
   no módulo Financial com migration completa.
2. **Transaction estendida** com `categorization_rule_id` (FK) e
   `categorized_at`.
3. **CSV parsing engine** (`ICsvParser` + `CsvParser`) funcional
   com CsvHelper — deteção de delimitador, header, encoding, quoted
   fields, truncation a 1000 linhas.
4. **ImportProfile CRUD completo** — endpoints REST + UI Angular.
5. **CSV upload + preview endpoint** com sugestão automática de
   mapeamento + preview parseada.
6. **Duplicate detection** marca linhas como "Possível duplicado"
   no preview; user pode ignorar ou forçar.
7. **CategorizationRule CRUD completo** — endpoints REST
   + UI Angular com drag-drop reorder.
8. **CategorizationRuleEngine** aplica regras ordenadas por
   prioridade (first match wins), com suporte a Contains/Equals/
   StartsWith case-insensitive.
9. **Rule application during import** — preview wizard Step 3
   mostra categorias sugeridas com badge "Auto"; confirm persiste
   `CategorizationRuleId` e `CategorizedAt` nas transações.
10. **Reapply rules endpoint** funcional — re-categoriza transações
    existentes; filtros opcionais (categoryId, período,
    onlyUncategorized).
11. **Import wizard Angular** 4 passos funcional (upload → mapping →
    preview + duplicates → confirmar resultado).
12. **Domain unit tests verdes** — CategorizationRule,
    ImportProfile, ImportBatch, Transaction audit columns.
13. **Application unit tests verdes** — CsvParser, DuplicateDetector,
    CategorizationRuleEngine, ReapplyCategorizationRulesHandler.
14. **Architecture tests verdes** — dependências, ITenantOwned,
    sem referências indevidas.
15. **Integration tests verdes** — pipeline completo, duplicates,
    rule application, reapply, multi-tenancy.
16. **Karma unit tests verdes** — import-profiles,
    categorization-rules, import-wizard, import-batches pages
    + API services.
17. **Suite Phase 1a/1b/2/3 continua verde** (sem regressão).
18. **Manual walkthrough com CSV real** — ≥80% auto-categorização
    com regras configuradas (merge-blocker).
19. **Sanity check responsivo** em 375 / 768 / 1280 px nas páginas
    novas/modificadas.
20. **Sub-agent deep review** aprovado e output anexado ao PR.
21. **`specs/roadmap.md` Phase 4 checkboxes ticados** via conversa
    com o agente.
22. **`CHANGELOG.md`** com entrada datada Phase 4.
23. **GitHub Actions CI verde** — build .NET, test .NET, build
    Angular, Karma.

## How to verify each bullet

1. **Novas entidades + migration.**
   - `git diff main..phase-4-csv-import -- src/Modules/Financial/`
     mostra 3 entidades novas + migration SQL.
   - `dotnet build -c Release` verde.
   - psql: `\d financial.categorization_rules`, `\d
     financial.import_profiles`, `\d financial.import_batches`
     mostram as 3 tabelas.

2. **Transaction estendida.**
   - psql:
     ```sql
     \d financial.transactions
     -- Esperado: columns categorization_rule_id uuid,
     --           categorized_at timestamptz.
     SELECT tc.constraint_name
       FROM information_schema.table_constraints tc
       WHERE tc.table_schema = 'financial'
         AND tc.table_name = 'transactions'
         AND tc.constraint_name LIKE '%categorization_rule%';
     -- Esperado: 1 row (FK exists).
     ```

3. **CSV parsing engine.**
   - `dotnet test --filter FullyQualifiedName~CsvParserTests` verde.
   - Cobrir: CSV com header, sem header, delimitador `;`, `,`,
     encoding UTF-8, encoding ISO-8859-1 com ç/ã, campos quoted,
     CSV vazio → erro, 1000+ linhas truncadas.

4. **ImportProfile CRUD.**
   - `curl -H "Authorization: Bearer <token>" \
     http://localhost/api/financial/import-profiles` → 200 [ ].
   - Criar perfil via POST → 201; GET /{id} → 200 com dados;
     PUT atualiza; DELETE → soft-delete (GET não retorna).
   - Angular: navegar para `/app/import-profiles` → tabela
     renderiza; "Novo perfil" abre dialog; criar, editar, apagar.

5. **CSV upload + preview.**
   - `curl -X POST http://localhost/api/financial/imports/upload \
     -H "Authorization: Bearer <token>" \
     -F "file=@test.csv"` → 200 com `{ batchId, headers,
     previewRows, totalRowCount, detectedDelimiter }`.
   - Preview rows têm valores corretos mapeados.
   - `PUT /api/financial/imports/{batchId}/preview` com
     mapeamentos custom → preview atualizada.

6. **Duplicate detection.**
   - `dotnet test --filter FullyQualifiedName~DuplicateDetectorTests`
     verde.
   - Cenário: importar CSV → criar transações → importar mesmo CSV
     novamente → preview mostra todas as linhas com
     `isDuplicate: true`.
   - UI: badge "Possível duplicado" visível; toggle "Incluir"
     desligado por default.

7. **CategorizationRule CRUD.**
   - `curl http://localhost/api/financial/categorization-rules` →
     200.
   - Criar regra → 201; GET /{id} → 200; PUT atualiza Priority;
     DELETE → soft-delete.
   - Reorder: `PUT .../reorder` com array de IDs → Priority
     atualizado na DB.
   - Angular: `/app/categorization-rules` → tabela ordenada por
     Priority; drag-drop funcional; dialog criar/editar; toggle
     IsActive.

8. **CategorizationRuleEngine.**
   - `dotnet test --filter
     FullyQualifiedName~CategorizationRuleEngineTests` verde.
   - Cobrir: Contains match, Equals match, StartsWith match,
     case-insensitive, first-match-wins, regra inativa ignorada,
     regra soft-deleted ignorada, sem match → MatchedRuleId null.

9. **Rule application during import.**
   - Integration test `CsvImportTests` confirma:
     - Criar 3 regras com diferentes priorities.
     - Upload CSV com descrições que batem cada regra.
     - Preview Step 3 mostra categorias sugeridas.
     - Confirm → transações persistidas com
       `CategorizationRuleId` correto e `CategorizedAt` preenchido.
   - Walkthrough manual: ver transação no dashboard → campo "Categorizada
     por regra" mostra nome da regra (psql: `SELECT
     categorization_rule_id FROM financial.transactions WHERE ...`).

10. **Reapply rules endpoint.**
    - Criar 3 transações uncategorized via import (sem regras).
    - Criar 3 regras.
    - `POST /api/financial/categorization-rules/reapply` → 200
      `{ totalProcessed: 3, categorizedCount: 3, unchangedCount: 0 }`.
    - psql: `SELECT categorization_rule_id FROM
      financial.transactions` → 3 non-null values.
    - With `onlyUncategorized=true`: criar nova transação já
      categorizada → reapply não a altera.
    - With filters: `?categoryId=X` só processa transações da
      categoria X; `?from=2026-01-01&to=2026-01-31` filtro por
      período.

11. **Import wizard Angular.**
    - Walkthrough manual: navegar para `/app/imports/new` →
      Step 1 (upload), Step 2 (mapping), Step 3 (preview),
      Step 4 (resultado). Navegação entre steps funcional
      (anterior/próximo).
    - Karma: `import-wizard.page.spec.ts` → todos os steps
      renderizam; file upload aceita .csv; column mapping
      dropdown funcional; preview mostra badges; confirm
      mostra resultado.

12. **Domain unit tests.**
    - `dotnet test tests/Modules/Financial.Domain.Tests/` verde.
    - Inclui `CategorizationRuleTests`, `ImportProfileTests`,
      `ImportBatchTests`, `TransactionCategorizationTests`.

13. **Application unit tests.**
    - `dotnet test tests/Modules/Financial.Application.Tests/` verde.
    - Inclui `CsvParserTests`, `DuplicateDetectorTests`,
      `CategorizationRuleEngineTests`,
      `ReapplyCategorizationRulesHandlerTests`.

14. **Architecture tests.**
    - `dotnet test --filter
      FullyQualifiedName~CsvImportDependencyTests` verde.
    - Confirma: Domain não referencia CsvHelper; todas as novas
      entidades implementam `ITenantOwned`.

15. **Integration tests.**
    - `dotnet test --filter
      FullyQualifiedName~CsvImportTests` verde.
    - Pipeline completo + duplicates + rule application + reapply.
    - `dotnet test --filter FullyQualifiedName~MultiTenancyTests`
      verde (estendida com Phase 4 entities).

16. **Karma unit tests.**
    - `cd src/Web/Sextante.Web && npm test -- --watch=false
      --browsers=ChromeHeadless` verde.
    - Inclui import-profiles, categorization-rules, import-wizard,
      import-batches specs.

17. **Sem regressão.**
    - `dotnet test -c Release` (suite completa) verde.
    - Suite Phase 1a (Identity/auth), 1b (UI auth), 2 (Financial
      CRUD + multi-tenancy), 3 (Multi-moeda + ECB) sem falhas novas.

18. **Walkthrough com CSV real (merge-blocker).**
    - Executar todos os passos da secção abaixo num ambiente limpo
      (`docker compose down -v && docker compose up`).
    - **Critério quantitativo**: ≥80% das transações do CSV
      categorizadas automaticamente por regras.
    - Se <80%, regras devem ser refinadas e walkthrough re-executado
      até atingir ≥80%, ou justificar que o CSV tem descrições
      inconsistentes que regras simples não podem cobrir (ex.:
      "COMPRA 1234" com código variável). Nesse caso, documentar %.

19. **Sanity responsivo.**
    - DevTools → device toolbar → ciclar entre 375 × 667,
      768 × 1024, 1280 × 800. Para cada viewport:
      - `/app/import-profiles`: tabela com overflow-x-auto
        (se necessário); dialog 95vw em 375 px.
      - `/app/categorization-rules`: idem; drag-drop funcional em
        desktop.
      - `/app/imports/new`: cada step do wizard usável;
        `p-steps` com labels reduzidos/ocultos em mobile;
        file upload full-width; preview tabela com scroll
        horizontal.
      - `/app/imports`: tabela de histórico de batches com
        overflow-x-auto.
    - Sem horizontal scroll do `<body>` em qualquer viewport.

20. **Sub-agent deep review.**
    - Output do sub-agent anexado ao PR (markdown ou screenshot).
    - Review cobre: dedup logic, rule priority execution,
      multi-tenancy isolation, CSV parsing edge cases, exchange
      rate integration no import.
    - Todos os findings resolvidos antes do merge.

21. **Roadmap.**
    - `git diff main..phase-4-csv-import -- specs/roadmap.md`
      mostra Phase 4 com `[x]` em cada bullet.

22. **Changelog.**
    - `git diff main..phase-4-csv-import -- CHANGELOG.md` mostra
      secção nova com data do merge sumarizando Phase 4.

23. **CI verde.**
    - GitHub Actions no commit mais recente do `phase-4-csv-import`
      mostra todos os jobs verdes (build .NET, test .NET, build
      Angular, Karma).

---

## Manual walkthrough — CSV import com regras de categorização

> Reproduzível a partir de `git clone` + `docker compose up`.
> Substituir credenciais e caminho do CSV por valores reais.
> **O CSV tem de ser um extrato bancário real** (Millennium,
> ActivoBank, Moey, ou similar) para validar o critério ≥80%.

### Pré-requisitos

```bash
docker compose down -v && docker compose up
# Migrations correm no startup.
```

### Cenário: importar extrato real com regras configuradas

1. **Signup.** Browser → `http://localhost/` → "Criar conta" →
   `email=tester+phase4@example.com`, `password=Phase4Password!`,
   `tenantName=Phase 4`. Tenant criado com `PrimaryCurrency = 'EUR'`
   (default).

2. **Criar categorias.** Menu → Categorias → "Nova categoria":
   - `Name=Supermercado`, `Kind=Expense`, cor verde (opcional).
   - `Name=Restaurante`, `Kind=Expense`.
   - `Name=Salário`, `Kind=Income`.
   - `Name=Transporte`, `Kind=Expense`.
   - `Name=Saúde`, `Kind=Expense`.
   - `Name=Utilidades`, `Kind=Expense`.
   - `Name=Subscrições`, `Kind=Expense`.
   - (Adicionar mais conforme o conteúdo do CSV.)

3. **Criar conta(s).** Menu → Contas → "Nova conta":
   - `Name=Millennium`, `Type=Checking`, `Currency=EUR`,
     `OpeningBalance=€0.00`.
   - Se o CSV tiver transações de outra conta/banco, criar também.

4. **Criar regras de categorização.** Menu → Regras → criar as
   regras baseadas no conteúdo do CSV:
   - Exemplo (ajustar ao CSV real):
     - `Name=Continente`, Pattern=`CONTINENTE`, MatchType=`Contains`,
       Category=Supermercado, Priority=1.
     - `Name=Pingo Doce`, Pattern=`PINGO DOCE`, MatchType=`Contains`,
       Category=Supermercado, Priority=2.
     - `Name=Lidl`, Pattern=`LIDL`, MatchType=`Contains`,
       Category=Supermercado, Priority=3.
     - `Name=Uber`, Pattern=`UBER`, MatchType=`StartsWith`,
       Category=Transporte, Priority=4.
     - `Name=Netflix`, Pattern=`NETFLIX`, MatchType=`Contains`,
       Category=Subscrições, Priority=5.
     - `Name=Salário`, Pattern=`SALARIO`, MatchType=`Contains`,
       Category=Salário, Priority=6.
     - ... (criar regras suficientes para cobrir padrões recorrentes
       no CSV).

5. **Criar ImportProfile.** Menu → Perfis de Importação → "Novo":
   - `Name=Millennium CSV`.
   - Column mappings (ajustar ao CSV real):
     - Coluna "Data" → `Date`.
     - Coluna "Descrição" → `Description`.
     - Coluna "Valor" → `Amount`.
     - Coluna "Saldo" → `Ignorar`.
   - `Delimiter=;` (ou `,` conforme CSV).
   - `HasHeaderRow=true`.
   - `DateFormat=dd-MM-yyyy`.
   - `DecimalSeparator=,`.
   - Salvar.

6. **Wizard de import — Step 1 (Upload).** Menu → Importar → Nova
   importação:
   - Selecionar perfil "Millennium CSV" no dropdown.
   - Upload do ficheiro CSV → aguardar parsing.

7. **Step 2 (Column mapping).** Preview mostra primeiras 5 linhas
   do CSV e colunas mapeadas conforme perfil. Ajustar se necessário.
   "Atualizar preview" se mudar parâmetros. Próximo.

8. **Step 3 (Preview + duplicates + categorização).** Esperado:
   - Tabela mostra todas as linhas com colunas mapeadas.
   - **Duplicados**: se é a primeira importação, nenhum. Se CSV já
     parcialmente importado antes, linhas duplicadas mostram badge
     "Possível duplicado" com toggle para incluir.
   - **Categorização**: coluna "Categoria sugerida" mostra resultados
     do engine. Badge "Auto" nas linhas categorizadas, "Manual" nas
     não categorizadas.
   - **Resumo no topo**: "X linhas OK, Y categorizadas automaticamente
     (Z%), W com erro, V possíveis duplicados".

   **Verificação do critério ≥80%:**
   - Calcular: `categorizedCount / (totalRows - errorRows - duplicatesIgnored)`.
   - Se <80%: voltar ao passo 4, criar mais regras, re-upload.
   - Se ≥80%: prosseguir.

9. **Confirmar.** Botão "Confirmar importação" → aguardar. Esperado:
   toast PT-PT "Importação concluída. X transações importadas,
   Y categorizadas automaticamente (Z%)."

10. **Step 4 (Resultado).** Tela mostra resumo: total imported,
    auto-categorized count, errors. Links para Dashboard e Regras.

11. **Verificar transações.** Menu → Dashboard:
    - Tabela de transações mostra as novas linhas com valores,
      categorias, e contas corretas.
    - Cards de Entrada/Saída/Líquido atualizados.
    - Donut por categoria (despesas) mostra fatias correspondentes
      às categorias atribuídas.

12. **Verificar audit de regra.** psql:
    ```sql
    SELECT t.description, t.category_id, t.categorization_rule_id,
           t.categorized_at, cr.name AS rule_name
      FROM financial.transactions t
      LEFT JOIN financial.categorization_rules cr
        ON t.categorization_rule_id = cr.id
      WHERE t.tenant_id = '<tenant-uuid>'
      ORDER BY t.occurred_at DESC;
    ```
    Esperado: transações categorizadas por regra têm
    `categorization_rule_id` não-nulo e `rule_name` preenchido;
    transações manuais têm ambos NULL.

13. **Reapply rules.** Criar 2-3 novas regras que cubram transações
    que ficaram uncategorized no passo 8:
    - `POST /api/financial/categorization-rules/reapply
      ?onlyUncategorized=true` → verificar que transações
      uncategorized foram categorizadas.
    - No Dashboard, verificar que categorias foram atualizadas.

14. **Reimport (duplicate detection).** Upload do mesmo CSV novamente:
    - Wizard Step 3: **todas** as linhas marcadas como "Possível
      duplicado".
    - Ignorar todos (default) → confirmar → 0 transações novas.
    - Ou forçar 1-2 linhas → confirmar → essas são criadas como
      novas transações (não substituem).

15. **Multi-tenancy.** Logout → novo signup com outro email:
    - `/app/import-profiles` vazio.
    - `/app/categorization-rules` vazio.
    - `/app/imports` vazio.
    - Dashboard vazio.
    - Tentar `GET /api/financial/import-profiles` só retorna os do
      tenant B (vazios), não vê os de A.
    - Criar regra em B — não afeta transações de A.
    - Reapply rules de B não altera transações de A.

### Hand-calculation table (verificação de categorização)

| # | Descrição CSV | Regra (Pattern) | MatchType | Categoria esperada | Auto? |
|---|---------------|-----------------|-----------|---------------------|-------|
| 1 | COMPRA CONTINENTE 1234 | CONTINENTE | Contains | Supermercado | Sim |
| 2 | UBER TRIP | UBER | StartsWith | Transporte | Sim |
| 3 | NETFLIX.COM | NETFLIX | Contains | Subscrições | Sim |
| ... | ... | ... | ... | ... | ... |

Após o walkthrough, a tabela acima deve bater com as transações no
Dashboard. Contar % de "Sim" na coluna "Auto": deve ser ≥80%.

---

## Out of scope for validation

- **Open Banking / API import** (Phase 12).
- **PDF/OCR** — apenas CSV.
- **Batch undo/rollback** — apenas delete/edit individual.
- **OFX/QIF** — apenas CSV.
- **Criação automática de contas** — falha hard se conta não existe.
- **Regex match type** — apenas Contains/Equals/StartsWith.
- **Machine learning** (Phase 14).
- **Performance benchmark com >10k linhas** — alvo é CSV mensal
  (~200 linhas).
- **Background import com polling** — import síncrono.
- **Currency auto-detection** — coluna Currency mapeada ou default
  à Account.
- **Backfill de `categorization_rule_id`** em transações Phase 2/3 —
  ficam NULL.
- **Lighthouse / acessibilidade formal** — pós-MVP.
- **Browser cross-version testing** — Chromium-based suficiente.
- **OpenAPI snapshot diffing** — out (consistente com Phase 2/3).
- **Multiple CSV file import** — um ficheiro de cada vez.
- **CategorizationRule com múltiplos patterns** — 1 pattern por
  regra.
- **Real-time rule preview no dialog** — opcional nice-to-have.
