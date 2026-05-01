# Plan — Phase 4: Importação CSV + Regras de categorização 🛡️

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (`tech-stack.md` §X, `requirements.md`,
> `roadmap.md`). Phase 4 entrega o wizard de importação CSV com
> mapeamento de colunas reutilizável, deteção de duplicados e regras
> de categorização automática com prioridade (primeira match ganha),
> incluindo audit trail por transação categorizada. É a primeira
> phase 🛡️ — sub-agent deep review é obrigatório antes do merge
> (AGENTS.md §2 regra 3).
>
> Phase 1a/1b/2/3 entregaram: Identidade + Auth, módulo Financial
> completo (Account, Category, Transaction com CRUD + soft-delete),
> multi-moeda + ECB. Phase 4 estende o Transaction com audit de regra
> e adiciona novas entidades: ImportProfile, ImportBatch,
> CategorizationRule. Tudo tenant-owned com soft-delete.
>
> A Saída do roadmap — *"utilizador importa extrato CSV do banco e
> ≥80% das transações ficam categorizadas automaticamente por regras"*
> — é validada com um CSV bancário real (Millennium ou similar) no
> walkthrough manual (merge-blocker).

---

## 1. Schema — novas entidades (Financial module)

### 1.1 `CategorizationRule`
`src/Modules/Financial/.../Domain/CategorizationRules/CategorizationRule.cs`:
- `Id (Guid v7)`, `TenantId (Guid, FK shared.tenants)`.
- `Name (varchar(128))` — ex.: "Supermercado Continente".
- `Pattern (varchar(512))` — texto a procurar.
- `MatchType (enum-as-string: 'Contains' | 'Equals' | 'StartsWith')`.
- `CategoryId (Guid, FK financial.categories)` — categoria alvo.
- `Priority (int NOT NULL)` — ordem de avaliação (menor = maior
  prioridade; primeiro match ganha).
- `IsActive (bool, default true)` — desativar regra sem apagar.
- Audit: `CreatedAt`, `UpdatedAt`, `DeletedAt` (soft-delete), `Version`.
- Invariant: `Pattern` não vazio; `Priority ≥ 0`; `CategoryId` não
  default.
- Implementa `ITenantOwned`.

### 1.2 `ImportProfile`
`src/Modules/Financial/.../Domain/ImportProfiles/ImportProfile.cs`:
- `Id (Guid v7)`, `TenantId`.
- `Name (varchar(128))` — ex.: "Millennium CSV".
- `ColumnMappings (jsonb)` — array de
  `{ csvColumnName, transactionField, defaultValue? }`.
  TransactionField enum: `Date`, `Amount`, `Currency`, `Description`,
  `Account`, `Category`, `CreditDebitIndicator`.
- `Delimiter (varchar(1), default ',')`.
- `HasHeaderRow (bool, default true)`.
- `DateFormat (varchar(32), default 'dd-MM-yyyy')` — formato para
  parsing da coluna Date.
- `DecimalSeparator (varchar(1), default ',')`.
- `SkipRows (int, default 0)` — linhas a saltar no início (metadata).
- Audit + soft-delete + `ITenantOwned`.

### 1.3 `ImportBatch`
`src/Modules/Financial/.../Domain/ImportBatches/ImportBatch.cs`:
- `Id (Guid v7)`, `TenantId`.
- `ImportProfileId (Guid FK, financial.import_profiles)` — nullable
  (pode importar sem perfil, mapeamento manual).
- `FileName (varchar(256))`.
- `Status (enum-as-string:
  'Parsing' | 'PreviewReady' | 'Confirming' | 'Importing' |
  'Completed' | 'Failed')`.
- `TotalRows (int)`, `ImportedRows (int)` — contadores atualizados
  em confirm.
- `DuplicateRows (int)` — linhas marcadas como possível duplicado.
- `ErrorRows (int)` — linhas com erro (parse de data, valor não
  numérico, etc.).
- `ParsedPreview (jsonb)` — snapshot do CSV parseado (linhas
  mapeadas) para preview, limpo pós-confirmação. Tamanho máximo:
  1000 linhas (se CSV maior, guardar primeiras 1000 + `Truncated=true`).
- `CategorizationResult (jsonb)` — pós-import:
  `{ autoCategorized: int, manual: int, uncategorized: int }`.
- Audit + soft-delete + `ITenantOwned`.
- **Sem** FK cross-schema para `shared.tenants` (soft-reference via
  `TenantId` sem FK formal, consistente com invariante §3.2).

### 1.4 Transaction — colunas de audit de regra
Migration `AddCategorizationRuleToTransaction` em Financial:
```sql
ALTER TABLE financial.transactions
  ADD COLUMN categorization_rule_id uuid NULL,
  ADD COLUMN categorized_at timestamptz NULL;
-- FK para categorization_rules (soft: ON DELETE SET NULL
--   quando regra é soft-deleted, para histórico permanecer)
ALTER TABLE financial.transactions
  ADD CONSTRAINT fk_transaction_categorization_rule
    FOREIGN KEY (categorization_rule_id)
    REFERENCES financial.categorization_rules(id)
    ON DELETE SET NULL;
```
- `categorization_rule_id` preenchido apenas quando uma regra automática
  categoriza. Manual fica NULL. Reapply a regra N categorizando
  transação já categorizada → atualiza `categorization_rule_id` e
  `categorized_at`.
- `categorized_at`: timestamp de quando a regra aplicou.

### 1.5 `FinancialDbContext`
- Adicionar `DbSet<CategorizationRule>`, `DbSet<ImportProfile>`,
  `DbSet<ImportBatch>`.
- Global query filters: `WHERE TenantId = @currentTenant AND
  DeletedAt IS NULL` para todas as 3 entidades.
- Configurar `CategorizationRule.Priority` com índice
  `(TenantId, Priority)` para avaliação rápida.
- Configurar `ImportBatch.ParsedPreview` como `jsonb`.
- Configurar `ImportProfile.ColumnMappings` como `jsonb`.

### 1.6 Migration `AddCsvImportEntities` + migration `AddCategorizationRuleToTransaction`
- Cria `financial.import_profiles`, `financial.import_batches`,
  `financial.categorization_rules`, colunas novas em
  `financial.transactions`.
- `MigrationsHistoryTable("__migrations", "financial")`.
- **Sem** seed data (regras são criadas pelo utilizador).

### 1.7 Architecture test
- `Module.Financial.Domain` referencia apenas `SharedKernel`
  (nenhuma referência a `Identity.*` ou `Infrastructure`).
- `CategorizationRule`, `ImportProfile`, `ImportBatch` são
  `ITenantOwned`.

---

## 2. CSV parsing engine

### 2.1 `ICsvParser` + implementação
`src/Modules/Financial/.../Application/CsvImport/ICsvParser.cs`:
- `CsvParseResult Parse(Stream csvStream, CsvParseOptions options,
    CancellationToken ct)`
- `CsvParseOptions(Delimiter, HasHeaderRow, SkipRows,
    MaxPreviewRows = 1000)`
- `CsvParseResult(Headers: IReadOnlyList<string>,
    Rows: IReadOnlyList<IReadOnlyList<string>>,
    TotalRowCount: int, Truncated: bool)`

Implementação em
`src/Modules/Financial/.../Infrastructure/CsvImport/CsvParser.cs`:
- Usar `CsvHelper` (lib .NET madura, sem dependências pesadas).
- Registrar no `Directory.Packages.props`.
- Deteção automática de delimitador se não fornecido: testar
  `,`, `;`, `\t`, `|` nas primeiras 5 linhas, escolher o que produz
  mais colunas consistentes.
- Encoding: UTF-8 default; se BOM inválido, tentar ISO-8859-1
  (fallback para CSVs bancários PT legacy).
- Timeout: 30s de parse.
- Campos quoted: suportar `"campo com, vírgula"`.

### 2.2 Register `ICsvParser` no DI
- Scoped (mesmo lifetime que `DbContext`).
- `services.AddScoped<ICsvParser, CsvParser>()` no módulo
  `Financial.Infrastructure`.

---

## 3. ImportProfile CRUD — API

### 3.1 Comandos e queries (Wolverine in-process)
`src/Modules/Financial/.../Application/ImportProfiles/`:

- `CreateImportProfileCommand(Name, ColumnMappings, Delimiter?,
    HasHeaderRow?, DateFormat?, DecimalSeparator?, SkipRows?)`
  → `CreateImportProfileHandler`.
  Validação FluentValidation: `Name` 1-128 chars; pelo menos 1
  mapping na lista `ColumnMappings`; `TransactionField` não duplicado
  dentro do mesmo profile.
- `UpdateImportProfileCommand(Id, Name, ColumnMappings, ...)`.
- `GetImportProfileQuery` / `GetImportProfilesQuery` / `DeleteImportProfileCommand`.

### 3.2 Endpoints (Minimal API)
`src/Modules/Financial/.../Api/ImportProfiles/ImportProfileEndpoints.cs`:
- `POST /api/financial/import-profiles`
- `PUT /api/financial/import-profiles/{id}`
- `GET /api/financial/import-profiles`
- `GET /api/financial/import-profiles/{id}`
- `DELETE /api/financial/import-profiles/{id}` → soft-delete.
- Todos exigem `[Authorize]` + tenant context.
- Respostas: 200 (lista/individual), 201 (criado), 204 (delete),
  400 (FluentValidation errors, ProblemDetails PT-PT).

---

## 4. ImportProfile CRUD — UI Angular

`src/Web/Sextante.Web/src/app/features/financial/pages/import-profiles/`:
- Página `/app/import-profiles` com PrimeNG `p-table`:
  - Colunas: Name, Delimiter, HasHeader, UpdatedAt, Ações (Edit/Delete).
- Dialog "Novo perfil":
  - Nome (inputText).
  - Column mappings: lista dinâmica `p-table` de linhas
    `[(csvColumnName, transactionField dropdown, defaultValue?)]`.
    Botão "+" adiciona linha, "-" remove.
    TransactionField dropdown populado do enum (Date, Amount,
    Currency, Description, Account, Category, CreditDebitIndicator).
  - Delimiter: dropdown (`,`, `;`, `\t`, `|`), default `;` (PT CSV
    usa `;` frequentemente).
  - HasHeaderRow: `p-checkbox`, default true.
  - DateFormat: dropdown (`dd-MM-yyyy`, `yyyy-MM-dd`, `MM/dd/yyyy`,
    `dd/MM/yyyy`).
  - DecimalSeparator: dropdown (`,`, `.`), default `,`.
  - SkipRows: `p-inputNumber`, default 0.
- Responsive: dialog com `[breakpoints]="{ '960px': '75vw',
  '640px': '95vw' }"`; tabela com `overflow-x-auto`.

---

## 5. CSV upload + preview

### 5.1 Endpoint `POST /api/financial/imports/upload`
- Payload: `multipart/form-data` com campo `file` (.csv).
- Query params: `?importProfileId=<guid>` opcional.
- FluentValidation: ficheiro não vazio; extensão `.csv`; tamanho
  máximo configurável (`appsettings.json`:
  `CsvImport:MaxFileSizeBytes = 5242880` — 5 MB default).
- Handler `UploadCsvCommandHandler`:
  1. Se `importProfileId`, carrega perfil e aplica mapeamento;
     senão, faz deteção automática de colunas.
  2. Parse CSV via `ICsvParser`.
  3. Cria `ImportBatch` com `Status=Parsing`, guarda
     `ParsedPreview` (snapshot JSON de linhas parseadas), guarda
     ficheiro original em disco temporário (`Path.GetTempPath()`).
  4. Atualiza status para `PreviewReady`.
  5. Retorna DTO com: BatchId, Headers, PreviewRows (máx. 1000),
     TotalRowCount, Truncated, SuggestedMappings, DetectedDelimiter,
     DetectedHasHeader.
- **Sugestão automática de mapeamento** (sem perfil):
  - Heurística: coluna com "date"/"data" no header → Date;
    coluna com valores numéricos e header "valor"/"amount"/"montante"
    → Amount; coluna com "descri"/"descriç" → Description.
  - Colunas não mapeadas recebem TransactionField `null` e o wizard
    mostra dropdown.

### 5.2 Endpoint `PUT /api/financial/imports/{batchId}/preview`
Atualiza a preview com mapeamentos revistos:
- Payload: `{ columnMappings: [{ csvColumnName, transactionField,
    defaultValue? }], delimiter?, hasHeaderRow?, dateFormat?,
    decimalSeparator?, skipRows? }`.
- Handler re-parseia o CSV com os novos parâmetros e atualiza
  `ImportBatch.ParsedPreview`.
- Retorna preview atualizada.

---

## 6. Deduplication heuristics

### 6.1 `IDuplicateDetector` + implementação
`src/Modules/Financial/.../Application/CsvImport/IDuplicateDetector.cs`:
- `Task<IReadOnlyList<Guid>> FindPotentialDuplicatesAsync(
    IReadOnlyList<ParsedTransaction> previewRows,
    Guid tenantId, CancellationToken ct)`
- `ParsedTransaction(Date, Amount, Currency, NormalizedDescription)`

Implementação em
`src/Modules/Financial/.../Infrastructure/CsvImport/DuplicateDetector.cs`:
1. **Normalização de descrição**: lowercase + trim + remove acentos +
   colapsar whitespace múltiplo → single space + remove pontuação
   (`. , ; : - _`).
2. Para cada preview row, query:
   ```sql
   SELECT t.id FROM financial.transactions t
   WHERE t.tenant_id = @tenantId
     AND t.occurred_at = @date
     AND t.amount = @amount
     AND t.currency = @currency
     AND t.deleted_at IS NULL
   ```
   + filtro em memória: `NormalizeDescription(t.description) ==
   normalizadaPreview`.
3. Retorna lista de IDs de transações existentes que fazem match.
   Preview marca as linhas com `isDuplicate: true` + coluna a mostrar
   "Possível duplicado — transação #X de Y".
- **Performance**: 1 query por batch de 100 rows (não 1 query por
  row). Para CSV com 1000 linhas → 10 queries. Com índice em
  `(tenant_id, occurred_at, amount)` cada query é <10ms.

### 6.2 Integração no preview
- `UploadCsvCommandHandler` e `UpdatePreviewHandler` chamam
  `IDuplicateDetector` após parse. Preview DTO inclui
  `isDuplicate: bool` e `duplicateTransactionId: Guid?` para cada
  linha.
- Preview UI (§8) mostra badge "Possível duplicado" em linhas
  duplicadas. User pode:
  - Ignorar (default) — linha é excluída do batch de confirmação.
  - Forçar import — user clica toggle "Incluir mesmo assim".
  - Ver transação duplicada — link para `/app/transactions?open=<id>`.

---

## 7. CategorizationRule CRUD — API

### 7.1 Comandos e queries
`src/Modules/Financial/.../Application/CategorizationRules/`:
- `CreateCategorizationRuleCommand(Name, Pattern, MatchType,
    CategoryId, Priority)`
  → `CreateCategorizationRuleHandler`.
  Validação: `Name` 1-128; `Pattern` não vazio, length 1-512;
  `MatchType` enum válido; `CategoryId` Guid válido (categoria do
  tenant, ativa); `Priority ≥ 0`.
- `UpdateCategorizationRuleCommand(Id, Name, Pattern, MatchType,
    CategoryId, Priority, IsActive)`.
- `GetCategorizationRuleQuery` / `GetCategorizationRulesQuery`
  (ordenado por Priority ASC) / `DeleteCategorizationRuleCommand`
  (soft-delete).
- `ReorderCategorizationRulesCommand(RuleIds: Guid[])`:
  Atualiza `Priority` de cada regra para refletir a nova ordem
  (1, 2, 3, ...).

### 7.2 Endpoints
- `POST /api/financial/categorization-rules`
- `PUT /api/financial/categorization-rules/{id}`
- `GET /api/financial/categorization-rules`
- `GET /api/financial/categorization-rules/{id}`
- `DELETE /api/financial/categorization-rules/{id}`
- `PUT /api/financial/categorization-rules/reorder`

---

## 8. CategorizationRule CRUD — UI Angular

`src/Web/Sextante.Web/src/app/features/financial/pages/categorization-rules/`:
- Página `/app/categorization-rules` com PrimeNG `p-table`:
  - Colunas: Priority (número), Name, Pattern, MatchType
    (badge/tag), Category (nome + ícone + cor), IsActive (toggle),
    Ações (Edit/Delete).
  - **Reordering**: drag-and-drop via PrimeNG `p-table`
    `[reorderableColumns]` ou botões ↑↓. Ao salvar ordem → chama
    `PUT .../reorder`.
- Dialog "Nova regra":
  - Nome (inputText), Pattern (inputText), MatchType (dropdown:
    Contains/Equals/StartsWith), Category (dropdown com search via
    `categoryApi.list()`), Priority (auto-sugerido, último + 1,
    editável).
  - Preview ao vivo: input de descrição de teste → mostra "Match?"
    com checkmark ou X (opcional, UX nice-to-have).

---

## 9. Rule application engine

### 9.1 `ICategorizationRuleEngine` + implementação
`src/Modules/Financial/.../Application/CategorizationRules/ICategorizationRuleEngine.cs`:
- `Task<CategorizationResult> ApplyAsync(
    IReadOnlyList<TransactionToCategorize> transactions,
    Guid tenantId, CancellationToken ct)`
- `TransactionToCategorize(TransactionId, Description, CurrentCategoryId?)`
- `CategorizationResult(TransactionId, MatchedRuleId?, Guid NewCategoryId?)`

Implementação em
`src/Modules/Financial/.../Infrastructure/CategorizationRules/CategorizationRuleEngine.cs`:
1. Carrega regras ativas (`IsActive=true`, `DeletedAt IS NULL`)
   ordenadas por `Priority ASC`.
2. Para cada transação:
   - Itera regras por ordem de prioridade.
   - Se `MatchType=Contains` → `description.Contains(pattern,
        StringComparison.OrdinalIgnoreCase)`.
   - Se `Equals` → `description.Equals(pattern,
        StringComparison.OrdinalIgnoreCase)`.
   - Se `StartsWith` → `description.StartsWith(pattern,
        StringComparison.OrdinalIgnoreCase)`.
   - Primeira match → atribui `CategoryId` da regra, regista
     `MatchedRuleId`. **Stop** (não avalia restantes).
3. Retorna `CategorizationResult` por transação.

### 9.2 Integração no import wizard
- Handler `ConfirmImportHandler`:
  1. Carrega `ImportBatch` + preview rows.
  2. Remove linhas marcadas como duplicado (user não confirmou).
  3. Aplica `ICategorizationRuleEngine` às linhas restantes.
  4. Cria `Transaction` para cada linha:
     - `Account`: resolvido do mapeamento (campo Account → lookup
       `financial.accounts` do tenant por nome, ou criar conta
       on-the-fly se não existir e mapeamento tiver flag
       `createAccountIfMissing` — decisão: fora do MVP; falha hard
       com ProblemDetails PT-PT *"Conta '{name}' não encontrada.
       Crie a conta antes de importar."*).
     - Se `CreditDebitIndicator` fornecido: `Debit` → valor negativo,
       `Credit` → valor positivo.
     - Se Currency fornecido: usar; senão, default para
       `account.Currency` (Phase 3).
     - Se currency != tenant primary → resolve exchange rate via
       `IExchangeRateService` (Phase 3). Sem rate → falha a linha
       com erro registado no batch, não aborta todo o batch.
     - Se o rule engine categorizou → `Transaction.CategoryId` =
       `NewCategoryId`, `Transaction.CategorizationRuleId` =
       `MatchedRuleId`, `Transaction.CategorizedAt = now`.
  5. `SaveChangesAsync` — todas as transações numa transação EF.
  6. Atualiza `ImportBatch`: Status=Completed, contadores,
     `CategorizationResult`.
  7. **Em exceção top-level**: Status=Failed, `ErrorRows`, log do
     erro.

### 9.3 Endpoint `POST /api/financial/imports/{batchId}/confirm`
- Payload: `{ includeDuplicates: Guid[] }` — lista de IDs de
  linhas duplicadas que o user quer forçar.
- Handler descrito em §9.2.
- Retorna `ImportResult` DTO com batch status + contadores.

---

## 10. Reapply rules endpoint

### 10.1 `POST /api/financial/categorization-rules/reapply`
- Query params opcionais:
  - `?categoryId=<guid>` — reaplicar apenas a transações de uma
    categoria específica.
  - `?from=YYYY-MM-DD&to=YYYY-MM-DD` — filtrar por período.
  - `?onlyUncategorized=true` — apenas transações sem categoria
    (default false).
- Handler `ReapplyCategorizationRulesHandler`:
  1. Carrega transações do tenant que batem os filtros.
  2. Chama `ICategorizationRuleEngine.ApplyAsync(...)`.
  3. Para cada transação com match:
     - Atualiza `CategoryId`, `CategorizationRuleId`, `CategorizedAt`.
     - **Não** re-atualiza `ExchangeRateToPrimary` (frozen per
       Phase 3).
  4. `SaveChangesAsync`.
  5. Retorna `{ totalProcessed, categorizedCount, unchangedCount }`.

---

## 11. Import wizard — UI Angular

### 11.1 Página wizard
`src/Web/Sextante.Web/src/app/features/financial/pages/import-wizard/`:
Route: `/app/imports/new`.

PrimeNG `p-steps` (ou `p-stepper`):

**Step 1 — Upload:**
- File upload via PrimeNG `p-fileUpload` com `mode="basic"` e
  `accept=".csv"`.
- Tamanho máximo: 5 MB.
- Dropdown opcional "Usar perfil" (lista `importProfileApi.list()`).
- Após upload → navega para Step 2.

**Step 2 — Column mapping:**
- Tabela de preview: primeiras 5 linhas do CSV (para contexto).
- Por cada coluna detetada: dropdown `TransactionField` para
  escolher `Date | Amount | Currency | Description | Account |
  Category | Ignorar`.
- Opções de parsing: Delimiter, HasHeader, DateFormat,
  DecimalSeparator, SkipRows. Herda do perfil se selecionado,
  senão detetado automaticamente.
- Botão "Atualizar preview" → chama `PUT .../{batchId}/preview` →
  refresh da tabela.
- Ao avançar → Step 3.

**Step 3 — Preview + duplicates:**
- Tabela completa (máx. 1000 linhas visíveis) com colunas mapeadas:
  Date, Description, Amount, Currency, Category (se regra aplicada),
  Badge "Possível duplicado" com toggle "Incluir" (desligado por
  default).
- **Categorização em preview**: após parse final, chama rule engine
  para mostrar ao user quantas transações serão categorizadas antes
  de confirmar. Coluna "Categoria sugerida" mostra resultado da regra
  + badge "Auto" vs "Manual".
- Resumo no topo: "X linhas OK, Y possíveis duplicados (Z ignorados),
  W com erro".
- Erros: linhas com data inválida, valor não numérico, conta não
  encontrada. Mostrar badge vermelho + tooltip com mensagem.
- Botão "Confirmar importação" → chamada ao endpoint com
  `includeDuplicates`.

**Step 4 — Resultado:**
- Resumo pós-import: Total imported, Auto-categorized, Manual,
  Errors.
- Link para `/app/categorization-rules` ("Gerir regras").
- Link para `/app/dashboard` ("Ver transações").
- Botão "Nova importação" → volta ao Step 1.

### 11.2 Responsive
- `p-steps` em mobile: labels escondidos, só números (1, 2, 3, 4)
  com `[styleClass]="wizard-steps-mobile"`.
- File upload: full-width em mobile.
- Tabela de preview: `overflow-x-auto`; colunas colapsam em mobile
  (template condicional via `ngTemplate` com breakpoint Tailwind).
- Dialog de "Confirmar importação": `[breakpoints]` adequado.

---

## 12. Tests

### 12.1 Domain unit tests
`tests/Modules/Financial.Domain.Tests/`:
- `CategorizationRules/CategorizationRuleTests.cs`: invariants
  (Pattern não vazio, Priority ≥ 0, CategoryId válido).
- `ImportProfiles/ImportProfileTests.cs`: ColumnMappings valid,
  Delimiter single char, Name required.
- `ImportBatches/ImportBatchTests.cs`: Status transitions válidas,
  contadores não negativos.
- `Transactions/TransactionCategorizationTests.cs`:
  `CategorizationRuleId` e `CategorizedAt` settable via factory;
  nullable por default.

### 12.2 Application unit tests
`tests/Modules/Financial.Application.Tests/`:
- `CsvImport/CsvParserTests.cs`: parse CSV com header; deteção de
  delimitador; encoding UTF-8/ISO-8859-1; quoted fields; CSV vazio
  lança; 1000+ linhas com truncation.
- `CsvImport/DuplicateDetectorTests.cs`: match exato (date+amount+
  normalized description) → retorna ID; descrição com acentos e
  pontuação → normalização produz match; sem match → lista vazia;
  tenant isolation (transação de tenant B não é duplicado para tenant A).
- `CategorizationRules/CategorizationRuleEngineTests.cs`:
  - `Contains` match: "Supermercado Continente Lisboa" com pattern
    "Continente" → true.
  - `Equals` match: "CONTINENTE" com pattern "continente" → true
    (case-insensitive).
  - `StartsWith` match: "PAGAMENTO SERVICO" com pattern "PAGAMENTO"
    → true.
  - First-match-wins: duas regras para a mesma descrição → a com
    menor Priority ganha.
  - Sem match → `CategorizationResult` com `MatchedRuleId=null`.
  - Regra inativa (`IsActive=false`) → ignorada.
  - Regra soft-deleted → ignorada (Global Query Filter).
- `CategorizationRules/ReapplyCategorizationRulesHandlerTests.cs`:
  - Filtro por `categoryId`: só transações da categoria são
    reavaliadas.
  - Filtro por período: `from`/`to` aplicados.
  - `onlyUncategorized=true`: transações já categorizadas não são
    alteradas (mantêm rule actual) ou são re-categorizadas se regra
    mudou? Decisão: **não** altera transações já categorizadas quando
    `onlyUncategorized=true`. Reapply total (sem flag) re-categoriza
    todas (pode mudar categoria de transação já categorizada se
    nova regra tem match e prioridade maior que a anterior —
    reconhecendo que old-rule pode já não existir/estar inativa).
- `ImportProfiles/...Handlers/`: CRUD validações, update mantém
  `TenantId`.

### 12.3 Architecture tests
`tests/Sextante.ArchitectureTests/CsvImportDependencyTests.cs`:
- `Module.Financial.Domain` não referencia `CsvHelper` (fica em
  Infrastructure).
- `Module.Financial.Application` referencia `ICsvParser`,
  `IDuplicateDetector`, `ICategorizationRuleEngine` (abstrações
  próprias); não implementações.
- `CategorizationRule`, `ImportProfile`, `ImportBatch` implementam
  `ITenantOwned`.
- `Module.Financial.*` não referencia `Identity.*` (cross-module
  via `ITenantContext` apenas).

### 12.4 Integration tests
`tests/Sextante.IntegrationTests/Financial/CsvImportTests.cs`:
- **Full pipeline**: criar perfil → upload CSV → preview mostra
  linhas → colunas mapeadas → duplicados detetados → confirmar →
  transações created com `CategorizationRuleId` e `CategorizedAt`.
- **Duplicate detection**: importar CSV 2× → segundo preview marca
  todas as linhas como duplicadas. Forçar include de 1 linha →
  essa linha é criada, restantes ignoradas.
- **Rule application during import**: criar 3 regras com prioridades
  diferentes → upload CSV com descrições que batem → preview mostra
  "Auto" nas linhas → confirm → transações persistidas com
  `CategorizationRuleId` correto.
- **Multi-tenancy**: tenant A faz uploads e cria regras → tenant B
  não vê import profiles / batches / categorization rules de A.
  Transações de A não são duplicados para B.
- **Reapply rules**: criar regras pós-import → chamar
  `POST /api/financial/categorization-rules/reapply` →
  transações uncategorized são categorizadas; transações já
  categorizadas mantêm-se (se `onlyUncategorized=true`).

### 12.5 Karma unit tests
`src/Web/Sextante.Web/src/app/features/financial/`:
- `pages/import-profiles/import-profiles.page.spec.ts`:
  - Lista perfis renderizada; botão "Novo" abre dialog; form
    validação (Name required, pelo menos 1 mapping); dialog
    fecha com sucesso.
- `pages/categorization-rules/categorization-rules.page.spec.ts`:
  - Lista regras por prioridade; drag-drop reorder; dialog criar/editar.
  - IsActive toggle desativa regra.
- `pages/import-wizard/import-wizard.page.spec.ts`:
  - Wizard navigation (steps); file upload; column mapping dropdown;
    preview mostra badges duplicate/auto; confirm chamada.
- `pages/import-batches/import-batches.page.spec.ts`:
  - Histórico de imports: tabela com Status badge, contadores.
- `core/api/import-profile-api.service.spec.ts`:
  - CRUD HTTP calls com HttpTestingController.
- `core/api/categorization-rule-api.service.spec.ts`:
  - CRUD + reorder HTTP calls.
- `core/api/import-api.service.spec.ts`:
  - Upload, update preview, confirm calls.

---

## 13. Walkthrough manual (merge-blocker)

> Reproduzível a partir de `git clone` + `docker compose up`.
> O cenário exato e passos concretos vive em `validation.md`.

Cenário em alto nível:
1. User cria 5-8 categorization rules baseadas no seu extrato
   bancário real (Millennium, ActivoBank, Moey, Revolut, etc.).
2. User cria ImportProfile para o banco com mapeamento de colunas.
3. User faz upload do CSV do último mês.
4. Wizard mostra preview com colunas mapeadas.
5. Duplicados detetados (se CSV já parcialmente importado),
   user decide ignorar.
6. Categorização automática aplicada — ≥80% das transações têm
   categoria sugerida na preview.
7. User confirma importação — transações persistidas com audit trail.
8. Dashboard Phase 3 mostra as novas transações (multi-moeda,
   converted/original).
9. User reaplica regras a transações existentes não categorizadas.
10. User verifica que transações categorizadas por regra mostram
    `CategorizationRuleId` no campo de audit.

---

## 14. Multi-tenancy regression

`tests/Sextante.IntegrationTests/Financial/MultiTenancyTests.cs`:
- ImportProfile, ImportBatch, CategorizationRule — tenant A cria,
  lista; tenant B não vê.
- Regras de tenant A não categorizam transações de tenant B.
- Duplicate detector de tenant A não vê transações de tenant B.
- Reapply rules de tenant A não altera transações de tenant B.

---

## 15. Responsive sanity check

Per `tech-stack.md` §19.5, inspecionar em DevTools 375 / 768 / 1280:
- `/app/import-profiles`: tabela + dialog.
- `/app/categorization-rules`: tabela (pode ser larga com
  drag-and-drop) + dialog.
- `/app/imports/new` (wizard): cada step usável em mobile
  (file upload, dropdown mapping, preview tabela com horizontal
  scroll).
- `/app/imports` (histórico): tabela de batches.
- `/app/dashboard`: import não altera layout (Phase 2/3); novas
  transações aparecem na tabela existente.

---

## 16. Close-out

- 16.1 Skill `changelog` adiciona entrada datada do merge com
  sumário da Phase 4.
- 16.2 Marcar `specs/roadmap.md` Phase 4 checkboxes via conversa
  com o agente (AGENTS.md §2 regra 1, nunca à mão).
- 16.3 Sub-agent deep review **obrigatório** (🛡️). Output anexado
  ao PR. Review cobre:
  - Dedup logic correctness (normalização, tenant isolation).
  - Rule priority execution (first-match-wins, ordering).
  - Multi-tenancy isolation (profiles, batches, rules, transactions).
  - CSV parsing edge cases (encoding, delimiters, quoted fields,
    large files).
  - Exchange rate resolution during import (Phase 3 integration).
- 16.4 Commit `Mark phase 4 as complete`. PR
  `phase-4-csv-import → main`. Merge depois de:
  - CI verde (build .NET, test .NET, build Angular, Karma).
  - Suite Phase 1a/1b/2/3 continua verde.
  - Walkthrough manual com CSV real passa.
  - Sub-agent deep review aprovado.
  - Aprovação humana.
  - ≥80% auto-categorization no walkthrough com regras configuradas.
