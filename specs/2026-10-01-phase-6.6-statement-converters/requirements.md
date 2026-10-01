# Requirements — Phase 6.6: Conversores de extratos no backend

> Primeira phase da Fase 1.5 (importação assistida por IA). Roadmap:
> `specs/roadmap.md` → "Fase 1.5 / Phase 6.6". Design de origem:
> `backlog/2026-10-01-chat-ai-design.md` (§3, fase 1) e processo atual em
> `backlog/2026-10-01-chat-ai-importacoes.md`.

## Goal

Passar para o backend, como **parsers determinísticos e testados**, os
conversores que hoje correm à mão em scripts Python fora do repo
(`Documents\Sextante\ferramentas\`): XLSX da conta à ordem ActivoBank, PDF do
cartão de crédito ActivoBank (2 layouts) e JSON da Coverflex. O wizard de
importação passa a aceitar estes ficheiros diretamente, sem chat e sem
converter para CSV à mão.

Serve o princípio de produto "automação sobre manualidade — mas auditável"
(`mission.md` §4.3): cada extrato é validado contra os números do próprio
banco (saldo encadeado, "RESUMO DE MOVIMENTOS", dívida anterior) e, se a
validação falhar, **não se importa nada**. Tem valor por si só (remove o
atrito que ameaça o dogfooding) e é a base que a Phase 6.7 (chat com IA) vai
orquestrar: a IA nunca lê valores do ficheiro, só usa o que estes conversores
calculam.

## In scope

Roadmap tal como está:

- Conversor **XLSX** da conta à ordem (ActivoBank), com validação de saldo
  encadeado linha a linha.
- Conversor **PDF** do cartão de crédito (ActivoBank), com os **2 layouts**
  (até agosto/2026: datas `AAAA/MM/DD` numa linha; desde setembro/2026: datas
  `MM/DD` sem ano, descrição em várias linhas, coluna "Rede"), validação dos
  totais do "RESUMO DE MOVIMENTOS" e encadeamento da dívida anterior.
- Conversor **JSON** da Coverflex.
- **Corte de overlap** com os movimentos já importados na conta.
- **Wizard de importação** aceita estes formatos diretamente (sem chat).
- **Testes unitários** com extratos reais anonimizados e casos de falha.

## Out of scope

- Chat, módulo `Assistant`, `IChatModel`, qualquer chamada a modelos (Phase 6.7).
- Inferência da conta a partir do ficheiro e campo identificador em `Account`
  (Phase 6.7 — design §6.2); aqui a conta continua a ser escolhida no wizard.
- Obtenção automática dos ficheiros (email/Gmail, API Coverflex com bearer
  token): o utilizador continua a descarregar o ficheiro à mão.
- Casos especiais do passo 10 do processo (planos de prestações, saldo inicial
  do cartão com sinal errado, regra com destino na própria conta).
- Categorização em lote com sugestões; as regras de categorização existentes
  continuam a aplicar-se no preview/confirm como hoje.
- Outros bancos/layouts além dos 4 acima (XLSX conta, PDF cartão ×2, JSON
  Coverflex). Layout desconhecido → erro explícito, nunca adivinhar.
- OCR / PDFs digitalizados (continua na "Maybe pile").
- Alterações de schema: **sem migration** nesta phase.

## Decisions

| # | Decisão | Porquê |
|---|---|---|
| D1 | Conversores **dentro do `Financial`**: contrato `IStatementConverter` em `Financial.Application`, implementações em `Financial.Infrastructure` (onde vivem as libs), registadas por DI como `ICsvParser`. Sem projeto/módulo novo. | Resposta do utilizador. Respeita `AGENTS.md` §3.2 (módulo isolado, 5 projetos); só o `Financial` precisa deles e a 6.7 chega lá via `PublicApi`/HTTP. |
| D2 | O conversor devolve um **resultado tipado** (`StatementConversionResult`: linhas com data, valor com sinal, descrição, saldo opcional; saldo antes/depois; período; lista de verificações com esperado/calculado) e um adaptador transforma-o nas **linhas canónicas do import** (`Data Lanc.;Data Valor;Descrição;Valor;Saldo`, `dd/MM/yyyy`, decimal vírgula). O lote grava essas linhas + `ImportParseSettings` pré-definidas (mapeamentos explícitos). | Reutiliza `ImportPreviewBuilder`, dedup, regras, transferências e confirm **sem os alterar** (ficam string-based). Os metadados (saldos, resumo) ficam no resultado para validar e mostrar, não no lote. |
| D3 | Validação **tudo-ou-nada**: qualquer verificação falhada lança `StatementValidationException` (→ `FinancialDomainException`, 400 PT-PT com os valores esperado/calculado) **antes** de criar o `ImportBatch`. | Requisito do roadmap ("uma validação que falha não importa nada") e do design §6.1.3. |
| D4 | **Separar extração de layout**: o PDF passa por um extrator de texto (PdfPig, palavras com posição) e um parser de layout puro sobre um modelo (`PdfPageContent`). O parser de cada layout testa-se com texto fixture; 1–2 PDFs reais anonimizados cobrem só o extrator. | Os 2 layouts mudam de formato com o banco; testar sobre modelo evita fixtures binárias frágeis. Débito/crédito decide-se pela **coluna** do valor (posição x), como o modo layout do `pypdf` nos scripts. |
| D5 | Libs candidatas: **ExcelDataReader** (XLSX) e **UglyToad.PdfPig** (PDF), via `Directory.Packages.props`. Licenças (MIT / Apache-2.0) e peso a confirmar na tarefa 1.1; alternativa XLSX: ClosedXML. | Leves, sem dependência nativa (a imagem Docker é `aspnet` runtime). CsvHelper continua só para CSV. |
| D6 | **Datas sem ano** (layout do cartão desde setembro/2026, `MM/DD`): o ano deduz-se do período do extrato (data do extrato e "dívida anterior"), tratando a virada de ano (movimento de dezembro num extrato de janeiro). | Os scripts atuais já o fazem; sem isto o import tem datas erradas. |
| D7 | **Corte de overlap ancorado no saldo**, nunca só por data nem só por dedup: com `L` = data do último movimento da conta e `B` = saldo da conta no fim de `L` (`IAccountBalanceQuery.GetBalanceAsync`), cortam-se as linhas até à última linha com `Saldo == B` e `Data ≤ L`. Se existem linhas com `Data ≤ L` mas nenhum saldo coincide → falha de validação explicada (extrato e conta divergem). Se todas as linhas são posteriores a `L` → nada a cortar. Cartão/Coverflex (sem saldo por linha): corte por `Data ≤ L` + verificação de que a dívida anterior bate com o saldo da conta no dia anterior ao início do período. | O dedup heurístico não distingue dois movimentos idênticos no mesmo dia (ex.: 2 transferências de 90,96 € a 07/09) — `backlog/2026-10-01-chat-ai-importacoes.md` §6. Âncora no saldo é determinística. |
| D8 | O endpoint `POST /api/financial/imports/upload` passa a despachar por **extensão** (`.csv`, `.xlsx`, `.pdf`, `.json`); o fluxo CSV mantém-se byte a byte. Limite de 5 MB mantém-se. Resposta ganha um campo opcional `Statement` (formato, verificações, linhas cortadas) com default `null`, para JSON de lotes/clientes antigos continuar a deserializar. | Menor superfície de mudança; compat com `UploadCsvResponse` existente (padrão do grupo 7 da 6.5, R9). |
| D9 | No wizard, os formatos convertidos **saltam o passo de mapeamento** (as definições vêm do lote) e mostram um cartão de validação (verificações ✓/✗, linhas cortadas). Responsivo 375/768/1280. | Evita o utilizador mapear colunas que já são conhecidas; torna visível o que o conversor garantiu. |
| D10 | Sem PII nos logs (`AGENTS.md` §4): os conversores logam só formato, nº de linhas, resultado das verificações e duração — nunca descrições nem valores. Mensagens de erro ao utilizador (PT-PT) podem citar valores, porque vão na resposta, não nos logs. | Regra dura de logging. |
| D11 | Timeout/limites defensivos no parse (XLSX/PDF são entrada não confiável): limite de linhas, de páginas e de tempo (30 s, como o `CsvParser`). | Evita ficheiros patológicos a bloquear a API. |
| D12 | O resultado do conversor expõe `SourceAccountHint?` (texto do identificador de conta/cartão lido do cabeçalho), **sem o usar nem persistir** nesta phase; nunca vai para logs nem para a resposta da API. | Evita reabrir os parsers na 6.7 (inferência de conta, design §6.2) sem antecipar decisões de schema. |
| D13 | **Coverflex**: montantes em cêntimos ÷ 100; só `status = confirmed`; data em `Europe/Lisbon`; validação = aritmética por linha + identidade global (não encadeamento linha a linha — ver "Amostras reais"). | Comportamento observado no JSON real; o encadeamento entre linhas não é fiável nesta fonte. |
| D14 | Cabeçalho `Moeda:` do extrato ≠ moeda da conta de destino → falha de validação PT-PT. | Sem coluna de moeda no formato canónico; converter em silêncio corromperia saldos. |
| D15 | **Anonimização**: fixtures fabricadas por script descartável **fora do repo** (comerciantes genéricos, valores perturbados com saldos e totais do resumo coerentes, números de conta/cartão e nomes removidos); fixtures sintéticas resultantes aceites para commit. | Confirmado pelo utilizador; dados reais nunca entram no repo. |
| D16 | **Coverflex `pending`**: ignorados na importação, mas contados e mostrados no cartão de validação ("N movimentos pendentes não importados"). `cancelled` ignorados em silêncio (só contados no resumo). | Confirmado pelo utilizador; pendentes entram quando confirmarem. |
| D17 | **Corte de overlap**: Coverflex ancora no `balance_after` do último movimento importado (como o XLSX, via D7); cartão corta por `Data ≤ L` + verificação da dívida anterior. | Confirmado pelo utilizador; a Coverflex tem saldo por linha, o cartão não. |
| D18 | **Ordem Coverflex**: ordenar por `executed_at` ascendente (único nas amostras); empate → ordem de chegada no ficheiro invertida (a API devolve do mais recente). | Confirmado pelo utilizador; a ordem de contabilização não coincide com a de execução. |

## Context / references

- Roadmap: Fase 1.5 → Phase 6.6 (inserida em replanning 2026-10-01). Não tem 🛡️:
  não toca em DB schema, auth, multi-tenancy nem integrações externas.
  Implementação "tudo de uma vez" é admissível (`AGENTS.md` §6, "feature
  standard"), mas o plan está em grupos com commit no fim de cada.
- Código existente a reutilizar sem alterar a semântica:
  `Financial.Application/Features/CsvImport/{CsvImportHandlers,ImportPreviewBuilder,ImportRowParser,ImportTransferResolver}.cs`,
  `Financial.Infrastructure/CsvImport/CsvParser.cs`,
  `Financial.Api/Endpoints/CsvImportEndpoints.cs`,
  `Financial.Application/Features/Accounts/IAccountBalanceQuery.cs`,
  perfil seed `SeedDefaultImportProfilesHandler`.
- Fixtures CSV sintéticas já existentes em
  `tests/Sextante.IntegrationTests/Data/Import/` (`activo-conta-sintetico.csv`,
  `activo-cartao-sintetico.csv`) — o formato canónico do adaptador (D2) tem de
  produzir exatamente este CSV.
- Backlog relacionado: `backlog/2026-09-23-importacao-xlsx.md` (esta phase
  absorve-o: XLSX no wizard) e `backlog/2026-10-01-chat-ai-design.md` §12
  questões 1 e 2 (IBAN/dígitos do cartão nos ficheiros; obtenção do JSON
  Coverflex) — esta phase deve **registar** o que os ficheiros reais trazem no
  cabeçalho para responder à questão 1, sem agir sobre isso.
- Regras duras aplicáveis: `AGENTS.md` §2.5 (idioma), §2.7 (Responsive DoD,
  `responsive-audit.md`), §4 (logs), changelog antes do merge.
- Dívida a evitar: não duplicar a lógica de parse de datas/valores —
  reutilizar `ImportRowParser.TryParseDate` e o `CsvColumnResolver` via linhas
  canónicas (D2).

## Amostras reais (inspecionadas em 2026-10-01)

Fonte: `C:\Users\MarloGamer\Documents\Sextante\` (fora do repo — **nunca
copiar ficheiros reais para o repo**, só versões anonimizadas). Contém
`ferramentas/converter-conta-activobank.py` e
`ferramentas/converter-cartao-activobank.py` (referência de comportamento),
`extratos/` com 4 XLSX da conta (`mov45557352614-*.xlsx`), 9 PDFs do cartão
(`EXT  AUTONOMO CARTAO (DOC 2026000NN).pdf`, `Extracto_24-09-2026_*.pdf`,
`extrato cartao de credito.pdf`; abrangem os 2 layouts) e
`coverflex-2026.json` (157 movimentos, jan–set/2026), mais os CSV já
convertidos em `extratos/csv/2026/` (úteis como **resultado esperado** nos
testes, depois de anonimizados).

### Coverflex JSON (resolve a questão 2)

Resposta da API: `{ "movements": { "list": [...], total_pages, total_results,
current_page, results_per_page, has_older } }`. O ficheiro de 2026 vem numa
só página (`total_pages = 1`) com `has_older: true`.

- **Montantes em cêntimos inteiros** (`amount.amount = 189` → 1,89 €; um
  carregamento mensal = `21120` → 211,20 €). O conversor divide por 100.
- Sinal em `is_debit` (`true` = saída). Tipos vistos: `purchase` (148, débito)
  e `transfer` (9, `COVERFLEX TOPUP …`, crédito).
- **`status`**: `confirmed` (154), `cancelled` (2, sem saldos) e `pending`
  (1, sem saldos). Só `confirmed` se importa — pendentes entram quando
  confirmarem; cancelados nunca.
- `balance_before`/`balance_after` existem só nos confirmados e a aritmética
  **por linha** fecha sempre (`antes ∓ valor = depois`, 0 falhas em 154).
  Mas o encadeamento **entre linhas** ordenadas por `executed_at` quebra em
  59 de 153 pares (a ordem de contabilização não é a de execução). O que
  fecha é a **identidade global**: `balance_before` da 1.ª + Σ(valores com
  sinal) = `balance_after` da última (25 + Σ = 311 € no ficheiro real).
  Logo a validação Coverflex é: aritmética por linha **e** identidade global,
  não saldo encadeado linha a linha.
- `executed_at` está em **UTC** (ex.: `2026-09-01T22:19:39Z`). A data do
  movimento é a data em `Europe/Lisbon` (22:19Z de 1/set já é 23:19 locais; a
  partir das 23:00Z de verão passa ao dia seguinte). O script atual gerou
  `06/01/2026` para `2026-01-06T09:53Z` — confirmar nos testes que a regra
  Lisboa reproduz os CSV existentes.
- Descrição: `description` (o topup traz `CVFX… COVERFLEX TOPUP ITEMID:<guid>`
  — o CSV atual usa só `COVERFLEX TOPUP`; o conversor normaliza o ruído
  variável para as regras de categorização/dedup serem estáveis). Todas as
  linhas são `EUR` e da bolsa `meals`.
- Dois movimentos `cancelled` idênticos no mesmo minuto (23/07) confirmam
  que **filtrar por `status` é obrigatório**, não opcional.

### Cabeçalhos dos ficheiros (resolve a questão 4)

Ambos trazem o **identificador da conta**, por isso o campo identificador em
`Account` (design §12.1) é viável na 6.7:

- XLSX: 1.ª linha da folha (acima de `Moeda:`/`Tipo:`/`Data de:`/`Data até:`
  e da linha de cabeçalho `Data Lanc.`) é o texto com o número da conta.
- PDF do cartão: "RESUMO DA CONTA" traz `Conta Cartão: <dígitos>` e o nome do
  extrato (`EXTRATO VISA CLASSIC ACTIVOBANK N. …`). Os 2 layouts partilham
  este bloco; o layout B muda a tabela de movimentos, não o resumo.

O conversor **não usa nem guarda** estes identificadores nesta phase (out of
scope), mas o resultado pode expô-los como campo opcional
(`StatementConversionResult.SourceAccountHint`) para a 6.7 não ter de
reabrir os parsers. Decisão em D12.

### Moeda (resolve a questão 5)

Coverflex: tudo `EUR`. Cartão e XLSX: cabeçalho `Moeda: EUR`. Sem linhas em
moeda estrangeira nas amostras → assume-se a moeda do cabeçalho do extrato =
moeda da conta; se o cabeçalho divergir da moeda da conta escolhida no
wizard, **falha de validação** (nunca converter em silêncio).

## Open questions

Nenhuma em aberto. As 4 questões anteriores foram resolvidas com o
utilizador em 2026-10-01 (D15–D18).
