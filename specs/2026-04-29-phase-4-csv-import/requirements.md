# Requirements — Phase 4: Importação CSV + Regras de categorização 🛡️

## Goal

Entregar a capacidade de importar extratos bancários via CSV com um
wizard completo e regras de categorização automática que atinjam ≥80%
de cobertura em extrato real. Phase 2 entregou o CRUD manual de
transações e categorias; Phase 3 entregou multi-moeda. Phase 4 junta
as duas: o utilizador deixa de precisar de registar transações uma a
uma — carrega o CSV do banco, mapeia colunas (uma vez, reutilizando o
perfil), o sistema deteta duplicados, aplica regras de categorização
baseadas em padrões de descrição (Contains / Equals / StartsWith), e
confirma. Cada transação categorizada por regra carrega o rasto de
audit — que regra aplicou e quando.

Esta phase concretiza o princípio de produto §4.3 da mission
(*"automação sobre manualidade — mas auditável"*) e cobre os 8
bullets do `roadmap.md` § Phase 4, com 3 explicitamente marcados IN e
3 explicitamente marcados OUT via AskUserQuestion.

## In scope

- **CSV upload + parse + preview** com deteção automática de
  delimitador, header, encoding (UTF-8 + ISO-8859-1 fallback).
  Parsing via `CsvHelper`. Preview limitado a 1000 linhas com
  `Truncated=true` para CSVs maiores.
- **ImportProfile CRUD** — mapeamento de colunas reutilizável por
  banco/cartão. Cada perfil guarda column mappings, delimiter,
  hasHeaderRow, dateFormat, decimalSeparator, skipRows como
  configuração. CRUD completo com UI Angular.
- **ImportBatch** — entidade de tracking de cada importação:
  ficheiro, status, contadores, snapshot de preview (jsonb),
  resultado de categorização.
- **Duplicate detection** — heurística por data + valor + moeda +
  descrição normalizada (lowercase, trim, remove acentos, colapsar
  whitespace, remover pontuação). Preview marca linhas como
  "Possível duplicado"; user decide ignorar ou forçar import.
- **CategorizationRule CRUD** — nome, pattern, match type
  (Contains/Equals/StartsWith), category alvo, priority (int).
  CRUD completo + reordering via drag-drop na UI.
- **Rule application engine** — aplica regras ordenadas por
  prioridade: primeira match ganha. Case-insensitive. Regras
  inativas ou soft-deleted são ignoradas. Aplica durante import
  e via endpoint de reapply.
- **Reapply rules endpoint** — `POST /api/financial/categorization-rules/reapply`
  re-executa regras sobre transações existentes. Filtros:
  categoryId, período, onlyUncategorized. Marca `CategorizationRuleId`
  e `CategorizedAt` nas transações afetadas.
- **Transaction audit de regra** — novas colunas
  `categorization_rule_id` (FK nullable → `financial.categorization_rules`,
  ON DELETE SET NULL) e `categorized_at` (timestamptz). Preenchidas
  quando regra aplica; NULL quando categorização é manual.
- **Import wizard Angular** — 4 passos (upload → mapping → preview
  + duplicates → confirmar resultado) com PrimeNG `p-steps`.
- **Categorization rule preview** — no Step 3 do wizard, rule engine
  sugere categorias e mostra badge "Auto" vs "Manual" antes de
  confirmar. User pode ver % de auto-categorização antes de confirmar.
- **Domain unit tests** — CategorizationRule, ImportProfile,
  ImportBatch invariants; Transaction audit columns.
- **Application unit tests** — CsvParser (encoding, delimiters,
  truncation); DuplicateDetector (normalização, tenant isolation);
  CategorizationRuleEngine (match types, first-match-wins,
  inactive/soft-deleted rules ignored); Reapply handler.
- **Architecture tests** — Domain não referencia infraestrutura
  (CsvHelper) nem Identity; todas as novas entidades implementam
  `ITenantOwned`.
- **Integration tests** — pipeline completo (upload → parse →
  preview → dedup → categorize → confirm); duplicate detection
  idempotente; rule application durante import; reapply rules;
  multi-tenancy total (profiles, batches, rules, transactions).
- **Karma unit tests** — import-profiles, categorization-rules,
  import-wizard, import-batches pages + API services.
- **Multi-tenancy regression** — estendida a todas as novas
  entidades: tenant A não vê/modifica/apaga dados de tenant B.
- **Responsive sanity check** — per `tech-stack.md` §19.5 em
  375/768/1280 px nas páginas novas/modificadas.
- **Sub-agent deep review obrigatório** (🛡️) — output anexado ao
  PR.
- **Walkthrough manual com CSV real** (merge-blocker) — ≥80%
  auto-categorização num extrato bancário real com regras
  configuradas.

## Out of scope

- **Automatic bank-specific API import** (Open Banking, Pluggy,
  GoCardless). Phase 12.
- **PDF/OCR import** — apenas CSV. PDF parsing está na "Maybe pile".
- **Batch undo/rollback** — transações importadas podem ser
  eliminadas/editadas individualmente via CRUD Phase 2, mas não há
  "desfazer importação inteira" com um clique.
- **OFX/QIF import** — apenas CSV no MVP (tech-stack §17).
- **Criação automática de Account on-the-fly durante import**.
  Se a conta referenciada no CSV não existe, a linha falha com erro
  PT-PT claro, e o batch continua (não aborta).
- **Múltiplas regras por transação** — apenas a primeira match
  aplica. Sem composição de regras ("se contém X E contém Y").
- **Regex / Expressões regulares** como match type — apenas Contains,
  Equals, StartsWith. Regex pode ser adicionado pós-MVP se útil.
- **Machine learning / categorização inteligente** — apenas regras
  manuais. Phase 14 (Categorização ML) é pós-MVP.
- **Importação multi-file / batch de CSVs** — um ficheiro de cada
  vez.
- **Conversão automática de encoding não suportado** — UTF-8 e
  ISO-8859-1 são suportados. Outros encodings produzem erro
  informativo.
- **CategorizationRule com múltiplos patterns** — 1 pattern por
  regra.
- **Preview de regras em tempo real enquanto se digita** — preview
  de match no dialog de criar/editar regra com descrição de teste
  é opcional (nice-to-have UX, não merge-blocker).
- **Import em background com polling** — import síncrono no request.
  Para CSVs >1000 linhas, considerar chunking em Phase futura.
- **Currency detection automática no CSV** — se CSV não tem coluna
  Currency, default à currency da Account. Sem deteção automática
  baseada em símbolos (R$, €, $).
- **Backfill de `categorization_rule_id` para transações Phase 2/3**.
  Transações antigas ficam com `categorization_rule_id=NULL`. Sem
  migration de backfill.
- **Performance benchmark com >10k transações** — Pós-MVP. CSV de
  ~200 linhas (uso típico mensal) é o alvo.
- **Hangfire jobs para import** — import síncrono. Sem background
  job para CSV parsing.

## Decisions

- **First match wins priority.** *Why*: Roadmap especifica "primeira
  a fazer match ganha". Simples, determinístico, fácil de debuggar.
  User controla ordem via drag-drop na UI. **How to apply**:
  `CategorizationRuleEngine` carrega regras por `Priority ASC`, itera
  e faz `break` ao primeiro match.

- **Soft-delete on all new entities.** *Why*: `tech-stack.md` §7.4
  exige soft-delete uniforme em todas as entidades de negócio.
  ImportProfile e CategorizationRule podem ser apagados sem perder
  histórico de importbatches/transações que os referenciam (FK ON
  DELETE SET NULL). **How to apply**: `CategorizationRule.DeletedAt`,
  `ImportProfile.DeletedAt`, `ImportBatch.DeletedAt` + Global Query
  Filter + `ON DELETE SET NULL` nas FKs relevantes.

- **Sub-agent deep review obrigatório.** *Why*: AGENTS.md §2 regra 3
  — phases 🛡️. Phase 4 toca DB schema (migrations), multi-tenancy
  (novas entidades tenant-owned), e integração externa (parsing de
  CSV com encoding detection — contract com `CsvHelper`). **How to
  apply**: invocar sub-agent antes do merge; output anexado ao PR;
  review cobre dedup logic, rule priority, multi-tenancy isolation,
  CSV parsing edge cases, exchange rate integration.

- **Rule audit trail mandatory.** *Why*: Mission §4.3 — "cada decisão
  automática deixa rasto: que regra aplicou, quando, com que
  valores". Sem audit, user não consegue auditar por que uma
  transação foi categorizada como X. **How to apply**: colunas
  `categorization_rule_id` + `categorized_at` em Transaction; FK
  `ON DELETE SET NULL` para preservar histórico quando regra é
  apagada; engine regista ambos no `CategorizationResult`.

- **CsvHelper como lib de parsing.** *Why*: lib .NET madura (3.5k+
  stars), suporta deteção de delimitador, encoding, quoted fields,
  streaming para ficheiros grandes. Sem dependências externas
  pesadas. Licença Apache 2.0 / MS-PL dual (compatível). **How to
  apply**: adicionar `CsvHelper` ao `Directory.Packages.props`;
  referenciar no `Financial.Infrastructure.csproj`; encapsular atrás
  de `ICsvParser` para testes.

- **Encoding fallback UTF-8 → ISO-8859-1.** *Why*: Bancos PT
  (Millennium, ActivoBank) exportam CSVs em ISO-8859-1 com
  caracteres acentuados (€, ç, ã). UTF-8 é o default moderno
  (Revolut, Moey). Deteção tenta UTF-8 primeiro; se BOM inválido
  ou caracteres de escape, fallback para ISO-8859-1 com warning
  log. **How to apply**: `CsvParser` testa ambos em caso de falha
  no primeiro; loga encoding usado para troubleshooting.

- **Preview limit: 1000 rows.** *Why*: CSVs bancários mensais são
  tipicamente 50-300 linhas. 1000 cobre qualquer banco no caso
  comum e evita payloads JSON gigantes. Acima de 1000, preview é
  truncado e UI mostra "Preview truncado — a mostrar 1000 de X
  linhas". **How to apply**: `CsvParserOptions.MaxPreviewRows =
  1000`; `ImportBatch.ParsedPreview` truncado; coluna
  `TotalRowCount` real.

- **Reapply rules: comportamento com `onlyUncategorized`.** *Why*:
  User pode querer re-executar regras após criar novas regras, mas
  sem perder categorizações manuais. **How to apply**: quando
  `onlyUncategorized=true` (default), apenas transações com
  `category_id IS NULL` são processadas. Quando false, todas as
  transações no scope são re-categorizadas (regra pode sobreescrever
  categorização manual — útil quando user confia nas regras).

- **Account lookup no import: fail-loud.** *Why*: Criar contas
  automaticamente do CSV pode criar lixo (nomes diferentes do
  mesmo banco: "Millennium", "Millennium BCP", "MBCP"). User deve
  criar a conta primeiro para ter controlo sobre nome, currency,
  tipo. **How to apply**: se `TransactionField.Account` mapeado e
  nome não encontrado em `financial.accounts` do tenant, erro
  PT-PT *"Conta '{name}' não encontrada. Crie a conta antes de
  importar."*. Linha vai para `ErrorRows`; batch continua.

- **Currency default na importação.** *Why*: Nem todos os CSVs
  incluem coluna de moeda (especialmente bancos PT que operam só
  em EUR). **How to apply**: se coluna Currency mapeada, usar valor
  do CSV (validado contra active currencies). Senão, default à
  `account.Currency`. Resolver exchange rate via Phase 3
  `IExchangeRateService` quando necessário.

- **Páginas Phase 4 são mobile-first.** *Why*: `mission.md` §4.6 e
  `tech-stack.md` §19.5 declaram responsive como invariante.
  Wizard de import em mobile é use case real (user pode querer
  importar extrato no telemóvel). **How to apply**: Tailwind
  utilities sem prefixo + `md:` para densidades maiores; dialogs
  com `[breakpoints]`; tabelas com `overflow-x-auto`; `p-steps`
  com labels escondidos em mobile.

## Context / references

- `specs/roadmap.md` — secção "Phase 4 — Importação CSV + Regras
  de categorização 🛡️".
- `specs/mission.md` — §3 (proposta de valor 1: controlo de gastos
  com categorização manual + por regras + importação), §4 princípio 3
  (automação sobre manualidade — mas auditável), §4 princípio 6
  (UI responsiva por defeito), §6 (definition of done MVP).
- `specs/tech-stack.md` — §1 (stack — CSV via CsvHelper, Hangfire
  wrapper não necessário para jobs síncronos), §3 (regras de
  dependência — módulo Financial não referencia Identity), §4
  (multi-tenancy — todas as novas entidades tenant-owned), §5
  (schema `financial`), §7.4 (soft-delete uniforme), §7.6
  (categorização por regra deixa rasto), §17 (decisões — OFX no MVP
  FORA, categorização por regra auditável), §19.5 (responsive
  design).
- `specs/2026-04-27-phase-3-multi-currency/` — entregou
  `IExchangeRateService`, `Account.Currency`, multi-moeda.
  Phase 4 integra exchange rate resolution no import.
- `specs/2026-04-26-phase-2-financial-core/` — entregou Account,
  Category, Transaction CRUD com soft-delete, audit trail.
  Phase 4 estende Transaction com `CategorizationRuleId` e
  `CategorizedAt`.
- `src/Modules/Financial/.../Domain/Transactions/Transaction.cs` —
  entidade base a estender com colunas de audit de regra.
- `src/Modules/Financial/.../Domain/Accounts/Account.cs` — lookup
  por nome no import.
- `src/Modules/Financial/.../Domain/Categories/Category.cs` —
  target de categorização nas regras.
- `src/Modules/Financial/.../Application/ExchangeRates/IExchangeRateService.cs`
  — Phase 3; usado no import quando currency != tenant primary.
- `src/BuildingBlocks/SharedKernel/Money.cs` — VO, usado para
  parsing de Amount do CSV.
- CsvHelper docs: `https://github.com/JoshClose/CsvHelper`
- ECB eurofxref: `https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml`
  (Phase 3 já integrado).
- ADR-009 (soft delete vs hard delete): pendente. Phase 4 aplica
  soft-delete em todas as entidades novas. ADR formal é posterior.
- ADR-010 (CQRS via Wolverine): já escrito na Phase 1a.

## Open questions

- **Regras com mesmo Priority**: comportamento indefinido. Prevenir
  via validação no endpoint `POST` (rejeitar duplicate Priority) ou
  permitir e definir tie-breaker (ordem alfabética? createdAt?).
  Decidir na implementação — preferência: **rejeitar duplicate
  Priority** com erro PT-PT *"Já existe uma regra com prioridade
  {priority}. Ajuste as prioridades."*.
- **ImportBatch.ParsedPreview**: guardar o snapshot completo das
  1000 linhas em JSONB na DB ou em ficheiro temporário no disco?
  Prós DB: transacional, consistente, sem gestão de ficheiros
  temporários. Prós disco: não polui a DB com dados temporários.
  Decisão preliminar: **JSONB na DB** (simples, transacional). Se
  performance for problema, rever.
- **CSV com múltiplas moedas na mesma coluna**: edge case raro.
  Se coluna Amount tem "$1,000.00" e "R$500,00" no mesmo CSV, a
  coluna Currency mapeada resolve. Se não, falha na linha (erro de
  parse do valor com símbolo).
- **Reapply rules em transações já categorizadas por regra**:
  quando `onlyUncategorized=false` e uma transação foi categorizada
  pela Regra A (priority 1), e agora existe Regra B (novo priority 0)
  que também faz match, a regra B ganha. Isto pode ser surpreendente
  para o user se a regra A era preferida. Documentar no UI: tooltip
  "A reaplicação reavalia todas as transações com as regras atuais;
  categorizações manuais são mantidas (se onlyUncategorized=true)."
- **Formato de data no CSV**: bancos usam formatos variados.
  Deteção automática baseada em heurística (tentar `dd-MM-yyyy`,
  `yyyy-MM-dd`, `dd/MM/yyyy`, `MM/dd/yyyy`) ou exigir que user
  configure no perfil? Decisão: **user configura no perfil**;
  sem deteção automática para evitar parsing incorreto (01/02/2026
  pode ser 1 fev ou 2 jan).
- **Descrição com acentos**: normalização para dedup: remover acentos
  é seguro (acento não muda semântica: "Supermercado" =
  "Supermercado"). Mas pattern matching de regras: Contains
  case-insensitive já cobre Ç com C, etc.? `StringComparison.OrdinalIgnoreCase`
  em .NET **não** iguala 'ç' com 'c'. Decidir: manter exact matching
  (user escreve pattern como aparece no CSV) ou normalizar acentos
  também nas regras. Preferência: **manter exact matching** — user
  copia-pega a descrição do CSV que quer categorizar; alterar imitando
  normalização pode ser confuso.
- **Decimal separator**: bancos PT usam `,` para decimais e ` ` ou
  `.` para milhares. CSV pode ter `1 234,56` ou `1.234,56`.
  `CsvHelper` lida com config cultural. Default `pt-PT`
  (`CultureInfo.GetCultureInfo("pt-PT")`). User pode override no
  perfil.
- **Colunas com nomes duplicados no CSV header**: CsvParser deve
  throw com erro claro "CSV contém colunas com nome duplicado: X".
- **Workflow de importação parcial**: se user fez upload e fechou
  o browser no Step 3, o `ImportBatch` fica em `PreviewReady`.
  UI `/app/imports` lista batches com status e botão "Continuar"
  para retomar. Decisão: implementar — simples e melhora UX; batch
  guarda estado suficiente para retomar.
