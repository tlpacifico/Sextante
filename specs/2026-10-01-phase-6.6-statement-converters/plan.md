# Plan — Phase 6.6: Conversores de extratos no backend

> Numerado por grupos de tarefa. Phase sem 🛡️ (sem schema, auth, tenancy
> nem integração externa) → implementação "tudo de uma vez" é admissível,
> mas cada grupo fecha com testes verdes e commit (PT-PT) antes do
> seguinte. Testes de integração (`tests/Sextante.IntegrationTests`) só
> correm na **última tarefa de cada grupo** que os tem — durante o grupo
> escrevem-se mas só se corre o projeto unitário.
>
> Abreviaturas: `FIN.` = `src/Modules/Financial/Sextante.Modules.Financial.`,
> `WEB/` = `src/Web/Sextante.Web/src/app/`,
> `TESTS/` = `tests/`, `UT/` = `tests/Modules/Financial.Application.Tests/`
> (já referencia `Financial.Infrastructure`, por isso os conversores testam-se aqui).
>
> Decisões D1…D18 e questões em aberto estão em `requirements.md`.

---

## 0. Fundações: amostras e contrato

### 0.1 Amostras reais e anonimização
Obter com o utilizador os extratos reais e os scripts de referência
(`Documents\Sextante\ferramentas\`): XLSX da conta, PDFs do cartão **nos 2
layouts** (um de ≤ agosto/2026, um de ≥ setembro/2026), JSON da Coverflex.
Anonimizar (comerciantes, nomes, números de conta/cartão; valores trocados
**mantendo saldos encadeados e totais do resumo coerentes**). Guardar em
`UT/StatementConverters/Fixtures/`. **Registar no PR o que os cabeçalhos
trazem** — já inspecionado: ambos trazem o identificador da conta (ver
"Amostras reais" em `requirements.md`); expor só como `SourceAccountHint` (D12).
**Pasta de origem**: `C:/Users/MarloGamer/Documents/Sextante/` (4 XLSX, 9 PDFs
cartão, `coverflex-2026.json`, scripts em `ferramentas/`, CSV esperados em
`extratos/csv/2026/`). **Nunca copiar reais para o repo.** Fabricar as
fixtures com script descartável fora do repo (D15).

### 0.2 Contrato (Application)
`FIN.Application/StatementConversion/`:
- `StatementFormat { ActivoBankAccountXlsx, ActivoBankCardPdf, CoverflexJson }`.
- `IStatementConverter { StatementFormat Format; bool CanHandle(string fileName); StatementConversionResult Convert(Stream, CancellationToken) }`.
- `StatementConversionResult`: `Format`, `IReadOnlyList<StatementRow>`
  (`Date`, `ValueDate`, `Description`, `SignedAmount`, `Balance?`),
  `BalanceBefore?`, `BalanceAfter?`, `PeriodStart/End?`,
  `IReadOnlyList<StatementCheck>` (`Name`, `Passed`, `Expected`, `Actual`).
- `StatementValidationException : FinancialDomainException`
  (`FIN.Domain/Common/DomainException.cs`, mensagem PT-PT com os valores
  esperado/calculado). Exceções para ficheiro ilegível / layout
  desconhecido, também PT-PT.
- `ICanonicalCsvAdapter` (estático/serviço puro): `StatementConversionResult`
  → `(headers, rows, ImportParseSettings)` com
  `Data Lanc.;Data Valor;Descrição;Valor;Saldo`, `dd/MM/yyyy`, vírgula decimal e
  `ColumnMappings` explícitos (Date/Description/Amount) — D2.
- Teste: o adaptador sobre linhas conhecidas produz o mesmo conteúdo que
  `TESTS/Sextante.IntegrationTests/Data/Import/activo-conta-sintetico.csv`.

---

## 1. Conversor XLSX — conta à ordem ActivoBank

### 1.1 Dependência
Adicionar ExcelDataReader (+ `System.Text.Encoding.CodePages` se exigido) a
`Directory.Packages.props` e `FIN.Infrastructure.csproj`; confirmar licença.
Registar no PR a decisão final (ExcelDataReader vs ClosedXML).

### 1.2 `ActivoBankAccountXlsxConverter` (`FIN.Infrastructure/StatementConversion/`)
- Localizar a linha de cabeçalho pelo texto `Data Lanc.` (não por índice
  fixo — o cabeçalho acima tem ~6 linhas variáveis com conta/moeda/período).
- Ler `Data Lanc.`, `Data Valor`, `Descrição`, `Valor`, `Saldo` já tipados
  (data, número); linhas sem data/valor → falha explícita com nº da linha.
- Calcular `BalanceBefore = Saldo₁ − Valor₁`.
- **Verificação "saldo encadeado"**: `Saldoᵢ₋₁ + Valorᵢ == Saldoᵢ` para
  todas as linhas, com tolerância zero (decimal). Falha → exceção com a
  linha, o esperado e o encontrado.
- Ordem: normalizar para cronológica ascendente quando o banco exporta ao
  contrário (detetar pelo encadeamento, não pela data).
- Limites D11 (linhas, tempo).

### 1.3 Testes (`UT/StatementConverters/ActivoBankAccountXlsxConverterTests.cs`)
Fixture real anonimizada; caso feliz (nº de linhas, `BalanceBefore`,
último saldo); falhas: saldo que não encadeia, folha sem cabeçalho
`Data Lanc.`, linha sem valor, ficheiro que não é XLSX, XLSX vazio; dois
movimentos idênticos no mesmo dia ficam ambos (não deduplicar aqui).

---

## 2. Conversor PDF — cartão ActivoBank (2 layouts)

### 2.1 Dependência e extrator
Adicionar `UglyToad.PdfPig` (D5). `PdfStatementTextExtractor`
(`FIN.Infrastructure/StatementConversion/`): PDF → `PdfPageContent`
(linhas de palavras com coordenadas x/y por página). Limite de páginas e
de tempo (D11); PDF protegido/ilegível → erro PT-PT explícito.

### 2.2 Deteção de layout
`CardLayoutDetector`: layout A (≤ ago/2026: datas `AAAA/MM/DD` numa linha)
vs layout B (≥ set/2026: datas `MM/DD`, descrição multi-linha, coluna
"Rede"). Nenhum reconhecido → erro "layout de extrato não suportado"
(nunca adivinhar).

### 2.3 Parsers de layout
`CardLayoutAParser` e `CardLayoutBParser` (puros sobre `PdfPageContent`):
- Extrair movimentos (data, descrição — juntar linhas de continuação no B —,
  valor).
- **Sinal pela coluna do valor** (débito vs crédito), D4.
- Layout B: ano deduzido do período do extrato, com virada de ano (D6).
- Extrair "RESUMO DE MOVIMENTOS": dívida anterior, total de débitos, total
  de créditos, dívida atual ("Saldo em dívida à data do extrato").
- Sinal da conta: compras = saída (negativo), pagamentos recebidos =
  entrada; `BalanceBefore = −dívida anterior`, `BalanceAfter = −dívida atual`.

### 2.3.1 Verificações (`StatementCheck`)
1. Σ débitos extraídos == total de débitos do resumo.
2. Σ créditos extraídos == total de créditos do resumo.
3. `dívida anterior + débitos − créditos == dívida atual`.
Qualquer falha → `StatementValidationException` com os 3 pares
esperado/calculado.

### 2.4 `ActivoBankCardPdfConverter`
Orquestra extrator → detetor → parser → verificações; implementa
`IStatementConverter`. Descrições limpas (espaços, quebras) mas **sem
reescrever o texto do banco** — as regras de categorização dependem dele.

### 2.5 Testes
`UT/StatementConverters/`: parsers de layout sobre `PdfPageContent`
construído a partir de texto fixture (A e B, incluindo descrição
multi-linha, virada de ano, valores com milhares); 1–2 PDFs reais
anonimizados só para o extrator + conversor ponta a ponta; falhas: totais
do resumo que não batem, dívida que não encadeia, layout desconhecido, PDF
corrompido/protegido, extrato sem movimentos.

---

## 3. Conversor JSON — Coverflex

### 3.1 `CoverflexJsonConverter`
Parse com `System.Text.Json` com DTOs **tolerantes** (campos extra
ignorados, campos obrigatórios em falta → erro explícito). Envelope
`movements.list`. Regras (D13):
- Só `status == "confirmed"`; contar `pending` (mostrado no cartão de
  validação, D16) e `cancelled` ignorados.
- `amount.amount` em **cêntimos** → `÷ 100`; sinal por `is_debit`.
- Data = `executed_at` (UTC) convertida para `Europe/Lisbon`; ordenar por
  `executed_at` ascendente.
- Descrição: `description` normalizada (o topup `CVFX… COVERFLEX TOPUP
  ITEMID:<guid>` → `COVERFLEX TOPUP`, como nos CSV atuais).
- Ordenação: D18.
- Saldo por linha = `balance_after` (cêntimos ÷ 100).
- Validação: (a) aritmética por linha `balance_before ∓ valor == balance_after`;
  (b) identidade global `balance_before₁ + Σ valores == balance_after_último`;
  **não** exigir encadeamento linha a linha (quebra em 59/153 pares nos dados
  reais). Moeda ≠ `EUR`/da conta → D14.
Formato desconhecido → erro PT-PT. Limite D11.

### 3.2 Testes
Amostra anonimizada (inclui topup, compras, 1 `pending`, 2 `cancelled`
idênticos e movimento perto da meia-noite UTC); verificar cêntimos→euros,
filtro de `status`, data Lisboa, e que o resultado reproduz o CSV esperado
(`coverflex-2026-*.csv` anonimizado). Falhas: JSON inválido, `list` vazio,
campo obrigatório em falta, montante não inteiro, aritmética por linha que
falha, identidade global que falha, moeda diferente.

---

## 4. Corte de overlap

### 4.1 `StatementOverlapTrimmer` (`FIN.Application/StatementConversion/`)
Dependências: `IAccountBalanceQuery`, `ITransactionRepository` (última data
de movimento da conta). Implementa D7:
- Sem movimentos na conta → nada a cortar.
- Conta com saldo por linha (XLSX e Coverflex — `balance_after`): ancorar em
  `B = saldo no fim de L`; cortar até à **última** linha `Saldo == B` com
  `Data ≤ L`; sem coincidência e com linhas `≤ L` → falha de validação
  explicada.
- Cartão (sem saldo por linha): cortar `Data ≤ L`; verificar `dívida anterior == −saldo no dia
  anterior ao início do período` (`GetBalanceAsync(accountId, periodStart−1)`);
  falha → exceção explicada.
- Resultado: linhas retidas + `RowsTrimmed` + verificações adicionais
  acrescentadas ao `StatementConversionResult` (para o wizard as mostrar).
- Respeitar `OpeningBalanceDate` da conta: linhas anteriores continuam a ser
  tratadas pelo import existente ("excluídas por serem anteriores ao saldo
  inicial") — o trimmer não duplica essa lógica.

### 4.2 Testes (`UT/StatementConverters/StatementOverlapTrimmerTests.cs`)
Com `IAccountBalanceQuery`/repositório falsos: sem overlap; overlap
exato; extrato que começa depois de `L`; **dois movimentos idênticos no
mesmo dia** (90,96 € a 07/09 ×2) cortados corretamente pela âncora de
saldo; saldo que não coincide → falha; cartão com dívida anterior que não
encadeia; conta sem movimentos.

---

## 5. Endpoint e integração com o import

### 5.1 Despacho por formato
`FIN.Api/Endpoints/CsvImportEndpoints.cs` — `POST /upload`: aceitar
`.csv`, `.xlsx`, `.pdf`, `.json` (mensagem de erro PT-PT atualizada);
validar também o **conteúdo** (assinatura `PK`/`%PDF`), não só a extensão.
`.csv` segue o caminho atual sem alterações.

### 5.2 Handler de upload de extratos
`FIN.Application/Features/CsvImport/CsvImportHandlers.cs` (ou handler
irmão `StatementImportHandlers`, preferido para não inflacionar os 743
linhas existentes): selecionar `IStatementConverter` pelo `CanHandle`,
converter, cortar overlap (4.1), adaptar para linhas canónicas (0.2) e
entrar no **mesmo** `ImportPreviewBuilder.BuildAsync` + `ImportBatch`
(`StartParsing`/`SetPreview`) do fluxo CSV. Validação falha → exceção antes
de `batchRepo.AddAsync` (nada gravado, D3).
`ImportParseSettings` do lote = as do adaptador (D2), para
`UpdatePreview`/`Confirm` as reusarem sem mudanças.

### 5.3 Contrato de resposta
`FIN.Application/Features/CsvImport/CsvImportContracts.cs`: adicionar a
`UploadCsvResponse` um campo final com default `null`
(`StatementSummary? Statement = null`: formato, verificações, `RowsTrimmed`,
período, saldos antes/depois) — D8. Atualizar o OpenAPI auto-gerado e
`WEB/core/api/financial.types.ts`.

### 5.4 Perfil/seed
Verificar se `SeedDefaultImportProfilesHandler` precisa de mudança (a
princípio **não**: os formatos convertidos não usam perfil). Nenhuma
migration.

### 5.5 Testes
- `UT`: handler com conversores falsos (conversão → corte → preview; falha
  de validação não chama `batchRepo.AddAsync`).
- `TESTS/Sextante.IntegrationTests/CsvImport/` (**só na última tarefa do
  grupo**): upload de XLSX, PDF (layouts A e B) e JSON → `200` com
  `Statement`; confirm cria as transações esperadas; ficheiro com saldo
  adulterado → `400` PT-PT e **`ImportBatch` não criado**; re-upload do
  mesmo extrato depois do confirm → 0 linhas novas (overlap cortado);
  cross-tenant: batch de A não visível a B (já coberto por RLS da 6.5 — só
  confirmar que o fluxo novo não o contorna).

---

## 6. Wizard Angular

### 6.1 Upload de novos formatos
`WEB/features/financial/pages/import-wizard.page.ts`: `accept` do input e
validação cliente para `.csv,.xlsx,.pdf,.json`; mensagens PT-PT. Tipos em
`WEB/core/api/financial.types.ts` e `financial-api.service.ts`
(`Statement` na resposta).

### 6.2 Fluxo sem mapeamento para formatos convertidos
Se a resposta traz `Statement`, saltar o passo de mapeamento (D9) e ir ao
preview; o `UpdatePreview` não é chamado sem necessidade (as definições
estão gravadas no lote).

### 6.3 Cartão de validação
Componente no passo de preview: formato detetado, período, saldos
antes/depois, verificações ✓/✗ com esperado/calculado, "N linhas cortadas
por já estarem importadas". Estado de erro (400) mostra a mensagem de
validação do backend em destaque, sem preview. Reusar `shared/ui`
(`empty-state`, etc.) do design system.

### 6.4 Responsivo e testes
Sanity check 375×667, 768×1024, 1280×800 (sem overflow do `<body>`,
dialogs ≤ 95vw, touch targets ≥ 44 px) → **`responsive-audit.md`** na pasta
da feature (regra dura `AGENTS.md` §2.7). Specs
`import-wizard.page.spec.ts` e `financial-api.service.spec.ts` estendidos.

---

## 7. Fecho

### 7.1 Docs
README: secção de importação — formatos suportados, como obter cada
ficheiro, o que cada validação garante. `docs/` sem ADR novo (decisões
D1–D11 vivem em `requirements.md`; se a escolha de libs divergir de D5,
registar no PR).

### 7.2 Verificação final
Percorrer `validation.md` bullet a bullet; `dotnet format --verify-no-changes`,
`dotnet test` (unit + arquitetura), build Angular + `ng test`.

### 7.3 Roadmap e changelog
Marcar 6.6 como concluída **via conversa com o agente**; skill `changelog`
antes do merge; commit "Marcar Phase 6.6 como concluída".
