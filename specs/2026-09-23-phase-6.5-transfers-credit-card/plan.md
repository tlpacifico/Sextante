# Plan — Phase 6.5: Transferências entre contas + cartão de crédito

> Numerado por grupos de tarefa. Phase 🛡️ (schema + RLS + import) →
> **implementação grupo a grupo**, com testes verdes e commit no fim de
> cada grupo antes de arrancar o seguinte (`AGENTS.md` §6). Cada grupo
> é uma fatia vertical (domínio → migration → handlers/endpoints → UI →
> testes).
>
> Abreviaturas: `FIN.` = `src/Modules/Financial/Sextante.Modules.Financial.`,
> `WEB/` = `src/Web/Sextante.Web/src/app/`,
> `TESTS/` = `tests/`.
>
> Decisões referidas como D1…D12 estão em `requirements.md`.

---

## 0. Fundações: ADR + dívida técnica

### 0.1 ADR-014 — Transferências e saldo de conta
`docs/adr/ADR-014-transfers-and-account-balance.md`: par de transações
ligadas (D1), direção explícita (D2), categoria nullable (D3), totais só
`Regular` (D4), saldo on-demand (D5), `OpeningBalanceDate` (D6).
Alternativas rejeitadas: entidade `Transfer` própria; categoria de
sistema "Transferência". Atualizar `tech-stack.md` §17 (tabela de
decisões) e §18 (ADRs) via conversa.

### 0.2 RLS nas tabelas do import
Migration nova `AddMissingRlsToImportTables`, padrão de
`20260502133245_AddBudgetsAndAlerts.cs:122-157`: `ENABLE` + `FORCE ROW
LEVEL SECURITY`, policy `*_tenant_isolation`, sentinel CHECK e `GRANT`
ao `sextante_app` em `financial.categorization_rules`,
`financial.import_profiles`, `financial.import_batches`. `Down`
simétrico. Teste em `TESTS/Sextante.IntegrationTests/MultiTenancy/`
por tabela (leitura cross-tenant vazia; escrita cross-tenant → 42501).

### 0.3 Reaplicar regras sem limite de 100
`FIN.Application/Features/CategorizationRules/CategorizationRuleHandlers.cs:149-203`:
iterar em páginas (ou método de repositório dedicado sem clamp) até
esgotar; publicar `TransactionUpdatedIntegrationEvent` por transação
alterada. Teste com 250 transações.

### 0.4 Totais incluem categorias arquivadas
`FIN.Infrastructure/Persistence/Repositories/TransactionRepository.cs:64-203`:
joins a `Categories` com `IgnoreQueryFilters()` no lado da categoria
(o filtro de tenant mantém-se via RLS + filtro na transação). Teste:
arquivar categoria com transações não altera o summary.

### 0.5 Import: eventos e moeda primária
`FIN.Application/Features/CsvImport/CsvImportHandlers.cs`: publicar
`TransactionCreatedIntegrationEvent` por linha criada, após commit
(padrão do materializer `RecurringTransactionMaterializerHandler.cs:144,179-182`);
substituir `"EUR"` hardcoded (:349) pela moeda primária do tenant.

### 0.6 Arquivar transação / conta
`TransactionHandlers.cs:125-140`: publicar evento de update (ou novo
evento interno) para os orçamentos recalcularem.
`AccountHandlers.cs:62-77`: recusar arquivar conta com transações
ativas (`CountActiveTransactionsAsync`) → ValidationProblem 400 PT-PT
(mesma convenção de `CategoriesEndpoints` para violações de regras de domínio).

---

## 1. `Transaction`: direção, tipo e transferência

### 1.1 Domínio
`FIN.Domain/Transactions/`:
- `TransactionDirection { Inflow = 0, Outflow = 1 }`.
- `TransactionKind { Regular = 0, Transfer = 1, Adjustment = 2 }`.
- `Transaction`: `Direction`, `Kind`, `Guid? TransferId`,
  `Guid? CategoryId` (era `Guid`).
- Factories: `CreateRegular(accountId, categoryId, categoryKind, ...)`
  (direção derivada do tipo da categoria) e `CreateUncategorized(accountId,
  direction, ...)` (transação regular sem categoria). `CreateTransferLeg`
  e `CreateAdjustment` entram nos grupos 3 e 4, com os respetivos testes.
  `Create` atual passa a `CreateRegular`.
- `Update` só para `Regular`; `SetCategory` recusa `Transfer`/
  `Adjustment`. Exceções novas em `FIN.Domain/Common/DomainException.cs`.
- Método `SignedAmount` (`+` Inflow, `−` Outflow) para o cálculo de saldo.

### 1.2 Migration `AddTransactionDirectionAndKind`
- `direction smallint NOT NULL`, `kind smallint NOT NULL DEFAULT 0`,
  `transfer_id uuid NULL`.
- Backfill: `direction` a partir de `categories.kind` (Income → 0,
  Expense → 1), **incluindo categorias arquivadas**; linhas com
  `category_id = '00000000-…'` → `category_id = NULL`, `direction = 1`
  e registo em log da migration (caso legado do materializer).
- `category_id` passa a nullable.
- CHECKs: `kind = 0 OR category_id IS NULL` (só `Regular` tem
  categoria, e pode não ter); `(kind = 1) = (transfer_id IS NOT NULL)`;
  `direction IN (0, 1)`; `kind IN (0, 1, 2)`.
- Índice parcial `(tenant_id, transfer_id) WHERE transfer_id IS NOT NULL`.
- `FinancialDbContext.cs:131-192` atualizado.

### 1.3 Leituras e agregações (D4)
- `TransactionRepository`: `GetConvertedTotalsAsync` (:64),
  `GetTotalsByCurrencyAsync` (:93), `GetByCategoryAsync` (:163) e o
  filtro por tipo `ApplyFilter` (:236) passam a usar `Kind = Regular` +
  `Direction`, sem depender de `Category.Kind`.
- `FIN.Infrastructure/Budgets/BudgetProgressService.cs:49-58`: filtrar
  `Kind = Regular` e `Direction = Outflow`.
- Export (`ListForExportAsync` :122, `TransactionCsvWriter.cs`): coluna
  `Tipo` com `Receita`/`Despesa`/`Transferência`/`Acerto`, nova coluna
  `Conta contraparte` para transferências; left join a categorias.
- `ITransactionRepository.cs:92` `CategoryKindFilter` → acrescentar
  `Transfer` e `Adjustment` ao filtro de tipo da listagem.
- Materializer (`RecurringTransactionMaterializerHandler.cs:113-126`):
  deixa de usar `Guid.Empty`; regra sem categoria → usa a categoria
  "Outros" da direção ou falha explicitamente (decidir na implementação;
  sem sentinel).

### 1.4 Contratos e UI
- `TransactionsContracts.cs`: `TransactionResponse` ganha `Direction`,
  `Kind`, `TransferId`, `CounterpartAccountId`, `CategoryId?`.
- Create/Update de `Regular` recebem `Direction` implícita pela
  categoria (sem mudança de payload para o cliente).
- `WEB/core/api/financial.types.ts`: tipos novos; `TransactionDto`
  com `direction`, `kind`, `transferId`, `counterpartAccountId`.
- `transactions.page.ts:192-193,357-360` e `dashboard.page.ts:309-312,610`:
  sinal/cor pela `direction` (não pela categoria); transferências
  mostradas com ícone e "Conta A → Conta B".

### 1.5 Testes
- Domínio: invariantes das três factories.
- Integração: migration aplicada sobre dados Phase 2–6 preserva
  summary; `Guid.Empty` legado vira `NULL`; CHECKs rejeitam
  combinações inválidas.

---

## 2. Saldo de conta + `OpeningBalanceDate` + saldo inicial negativo

### 2.1 Domínio
`FIN.Domain/Accounts/Account.cs`:
- `DateOnly OpeningBalanceDate` (imutável como o `OpeningBalance`).
- `:57` — `OpeningBalanceNegativeException` só se `Type != CreditCard`.
- `ChangeType` (:78): recusar mudar de `CreditCard` para outro tipo se
  `OpeningBalance < 0`.

### 2.2 Migration `AddAccountOpeningBalanceDate`
- `opening_balance_date date NOT NULL`, backfill `created_at::date`.
- CHECK `opening_balance_amount >= 0 OR type = 3`.

### 2.3 Cálculo
- `IAccountBalanceQuery` (Application) + implementação em
  Infrastructure: `Σ SignedAmount` por conta, `deleted_at IS NULL`,
  `occurred_at::date >= opening_balance_date`, opcionalmente
  `<= at`. Uma query agregada para todas as contas do tenant (lista) e
  uma por conta (detalhe).
- `AccountResponse` ganha `OpeningBalanceDate` e `CurrentBalance`.
- Endpoint `GET /api/financial/accounts/{id}/balance?at=YYYY-MM-DD`.

### 2.4 UI
`WEB/features/financial/pages/accounts.page.ts`:
- Coluna "Saldo atual" (negativo a vermelho; cartão mostra "Dívida").
- Formulário: campo "Data do saldo inicial"; `min(0)` (:165, :227)
  removido quando tipo = Cartão de crédito; texto de ajuda sobre
  imutabilidade do saldo inicial.

### 2.5 Testes
- Saldo = inicial + entradas − saídas, ignorando apagadas e anteriores a
  `OpeningBalanceDate`; `at=` corta no dia; cartão com inicial
  negativo; conta à ordem com inicial negativo → 400.
- Multi-tenancy: saldo de conta de outro tenant → 404.

---

## 3. Transferências

### 3.1 Comandos (Application, `Features/Transfers/`)
- `CreateTransferCommand(FromAccountId, ToAccountId, OccurredAt,
  AmountOut, AmountIn?, Description?)`: contas diferentes, ambas do
  tenant; `AmountIn` obrigatório se as moedas diferem, senão igual a
  `AmountOut`. Cria as duas pernas numa transação de DB com o mesmo
  `TransferId` (Guid v7). Câmbio de cada perna gravado como hoje
  (Phase 3).
- `UpdateTransferCommand(TransferId, ...)`: atualiza as duas pernas.
- `DeleteTransferCommand(TransferId)`: soft-delete das duas.
- `ConvertToTransferCommand(TransactionId, CounterpartAccountId,
  CounterpartTransactionId?)`: converte uma `Regular` em perna; liga a
  uma transação existente (valor compatível, direção oposta, sem
  `TransferId`, convertida também) ou cria a contraperna.
- `ArchiveTransaction`/`UpdateTransaction` sobre uma perna → 400 (ValidationProblem) com
  mensagem a apontar para o endpoint de transferências.

### 3.2 Endpoints
`FIN.Api/Endpoints/TransfersEndpoints.cs` em `/api/financial/transfers`:
`POST`, `PUT /{transferId}`, `DELETE /{transferId}`,
`POST /convert`. Validators FluentValidation em
`FIN.Api/Validators/FinancialValidators.cs`.

### 3.3 UI
- Dialog "Nova transferência" (contas origem/destino, data, valor, valor
  recebido quando as moedas diferem) acessível no ecrã de transações e
  no dashboard.
- Ação "Marcar como transferência" no ecrã de transações (linha e bulk
  de uma linha), com escolha da conta contraparte e sugestão de
  contraperna existente.
- Editar/apagar uma perna abre o fluxo da transferência.

### 3.4 Testes
- Criação atómica (falha numa perna → nenhuma persiste).
- Transferência mesma moeda e moedas diferentes.
- Totais do summary/donut/orçamento **não mudam** após criar
  transferência; saldos das duas contas mudam.
- Converter transação importada: com contraperna existente e sem.
- Multi-tenancy: transferir para conta de outro tenant → 404/400;
  ler/apagar transferência de outro tenant → 404.

---

## 4. Acerto de saldo (reconciliação)

### 4.1 Comando
`ReconcileAccountCommand(AccountId, Date, ActualBalance)`: calcula saldo
à data (grupo 2); diferença ≠ 0 → cria `Adjustment` (`Inflow` se o real
é maior, `Outflow` se menor) com descrição "Acerto de saldo" e data
`Date`. Devolve saldo calculado, real e diferença. Endpoint
`POST /api/financial/accounts/{id}/reconcile`.

### 4.2 UI
Botão "Reconciliar" em cada conta: data + saldo real → pré-visualiza a
diferença antes de confirmar. Acertos aparecem nas transações com tipo
"Acerto" e podem ser apagados (reversão auditável, `mission.md` §4.3).

### 4.3 Testes
Diferença positiva, negativa e zero; acerto fora dos totais; saldo à
data após acerto = saldo real indicado.

---

## 5. Definições e vista do cartão de crédito

### 5.1 Domínio
Owned type `CreditCardSettings` em `Account` (nullable, só
`CreditCard`): `CreditLimit (Money)`, `StatementClosingDay (1–31)`,
`PaymentDueDay (1–31)`, `Guid? PaymentAccountId` (conta do mesmo
tenant, não `CreditCard`). Dia > dias do mês → último dia do mês
(Open question 3).

### 5.2 Migration `AddCreditCardSettings`
Colunas nullable em `financial.accounts`; CHECK: settings só preenchidos
se `type = 3`; limite > 0.

### 5.3 Cálculo do ciclo
`CreditCardStatementCalculator` (Domínio, puro): dado hoje e o dia de
fecho → ciclo corrente e anterior (datas); com as transações → gastos
do ciclo (só `Regular` `Outflow` menos `Regular` `Inflow`),
pagamentos recebidos (pernas `Transfer` `Inflow`), dívida atual,
disponível = limite + saldo (saldo negativo), próximo pagamento =
dívida do ciclo fechado + prestações a vencer (grupo 6).
Endpoint `GET /api/financial/accounts/{id}/credit-card`.

### 5.4 UI
Página/painel "Cartão" (a partir da lista de contas): dívida, limite,
disponível (barra), ciclo corrente (gastos + lista), extrato anterior,
próximo pagamento com data, prestações ativas. Formulário das
definições no dialog de conta quando tipo = Cartão.

### 5.5 Testes
Calculador: fecho a 31 em fevereiro, ciclo a atravessar o ano,
transferências recebidas não contam como gasto. Integração do endpoint
+ multi-tenancy.

---

## 6. Compras em prestações

> Resolver *Open question 1* antes de começar.

### 6.1 Domínio
`FIN.Domain/InstallmentPlans/InstallmentPlan.cs` (`ITenantOwned`,
audit + soft-delete): `AccountId` (CreditCard), `Guid?
PurchaseTransactionId`, `Description`, `TotalAmount (Money)`,
`InstallmentCount`, `InstallmentsAlreadyPaid`, `FirstInstallmentDate`,
`decimal? AnnualRate`. Calendário calculado
(`InstallmentSchedule`): valor por prestação (última absorve
arredondamento), datas mensais, restante em dívida.

### 6.2 Migration `AddInstallmentPlans`
Tabela `financial.installment_plans` com o padrão completo de
`AddBudgetsAndAlerts` (RLS + FORCE + sentinel + grants + CHECKs:
`installment_count BETWEEN 2 AND 120`, `already_paid < count`,
`total_amount > 0`).

### 6.3 Endpoints + UI
CRUD em `/api/financial/installment-plans` (filtro por conta). Na vista
do cartão: lista de planos com "prestação X de N", restante, próximas
datas; criar plano a partir de uma transação de compra ("Isto foi em
prestações") ou manualmente (planos anteriores ao arranque).

### 6.4 Testes
Calendário (arredondamento, planos já a meio), previsão de pagamento do
grupo 5 inclui prestações, multi-tenancy, teste de arquitetura
`InstallmentPlan` implementa `ITenantOwned`.

---

## 7. Transferências na importação CSV

### 7.1 Conta de destino no wizard
`ConfirmImportCommand` / upload recebem `AccountId`; o fallback "primeira
conta" (`CsvImportHandlers.cs:300-314`) desaparece — sem coluna de
conta mapeada, a conta escolhida é obrigatória.
`WEB/features/financial/pages/import-wizard.page.ts`: seletor de conta no
primeiro passo.

### 7.2 Regras com ação de transferência
- `FIN.Domain/CategorizationRules/CategorizationRule.cs`:
  `RuleAction { SetCategory, MarkAsTransfer }`, `Guid? CategoryId`,
  `Guid? TargetAccountId`; invariantes por ação.
- Migration: colunas `action`, `target_account_id`; `category_id`
  nullable; CHECK por ação.
- Engine (`CategorizationRuleEngine.cs:16-58`) devolve a ação;
  `CategorizationMatchResult` ganha `TargetAccountId?`.
- Reaplicar regras: regra de transferência sobre transação existente →
  `ConvertToTransferCommand` (grupo 3).
- UI `categorization-rules.page.ts`: escolher ação; conta alvo quando
  transferência.

### 7.3 Preview e confirm
- Preview (`CsvImportHandlers.cs:21-182`): linha com regra de
  transferência mostra "Transferência → Conta X" e se já existe
  contraperna candidata (D9).
- Confirm (:249-443): cria perna + liga à contraperna existente ou cria
  a contraperna; tudo na mesma transação de DB.
- Linhas com data < `OpeningBalanceDate` → marcadas "anterior ao saldo
  inicial", excluídas por defeito (toggle no preview).

### 7.4 Deduplicação por conta
`FIN.Infrastructure/CsvImport/DuplicateDetector.cs:20-68` e
`IDuplicateDetector`: incluir `AccountId` na chave.

### 7.5 Testes
- Importar extrato da conta com linha "PAGAMENTO CARTAO" + regra →
  transferência criada, cartão recebe perna de entrada.
- Importar a seguir o extrato do cartão com a linha de pagamento →
  liga à perna existente, **sem duplicar**.
- Linhas antes de `OpeningBalanceDate` excluídas.
- Fixture sintética em `TESTS/Sextante.IntegrationTests/Data/Import/`
  com o formato ActivoBank (conta + cartão), **sem dados reais**.

---

## 8. Fecho da phase

### 8.1 Reconciliação com dados reais (manual, local)
Seguir `validation.md` §"Reconciliação real" com os extratos fora do
repo. Registar resultado (sem valores pessoais além dos agregados já
na validation) em `validation.md`.

### 8.2 Testes de arquitetura
`TESTS/Sextante.ArchitectureTests/`: `TransferDependencyTests.cs`,
`InstallmentPlanDependencyTests.cs` no padrão de
`BudgetDependencyTests.cs`.

### 8.3 Responsive audit
`responsive-audit.md` nesta pasta: dialog de transferência, dialog de
reconciliação, vista do cartão, ecrã de contas, wizard com seletor de
conta, página de regras — 375 × 667, 768 × 1024, 1280 × 800
(`AGENTS.md` §2.7).

### 8.4 Deep review + changelog + roadmap
- Sub-agent deep review (schema, RLS, import) antes do merge.
- Skill `changelog`.
- Marcar itens da Phase 6.5 no `roadmap.md` via conversa.
