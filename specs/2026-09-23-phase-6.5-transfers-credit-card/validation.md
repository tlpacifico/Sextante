# Validation — Phase 6.5: Transferências entre contas + cartão de crédito

> Cada bullet de Definition of Done tem um caminho de verificação
> concreto. Bullets sem caminho de verificação não pertencem aqui.

## Definition of done

### Reconciliação com dados reais

1. **Conta à ordem bate com o banco.** Conta criada com saldo inicial
   588,81 € a 28/08/2026, extrato importado desde 28/08 → saldo atual
   **137,82 €** (último saldo do extrato, 22/09/2026).
2. **Conta à ordem desde 01/08 também bate.** Variante com saldo inicial
   1 589,87 € a 01/08 e extrato completo → **137,82 €**.
3. **Cartão bate com o extrato de agosto.** Cartão com saldo inicial
   −2 500,00 € a 01/08, movimentos de agosto importados (53 linhas) e o
   pagamento de 12/08 como transferência → dívida **−2 409,17 €** a
   31/08 (= "Saldo em dívida à data do extrato atual").
4. **Pagamento de setembro abate a dívida.** Transferência de
   1 135,24 € a 11/09 → dívida do cartão, sem compras de setembro,
   **−1 273,93 €** (= montante em prestações do extrato).
5. **Acerto reconcilia o resto.** Com o saldo real do cartão à data
   (dívida = limite − disponível indicado pelo banco), a reconciliação
   cria um único `Adjustment` pela diferença e o saldo passa a coincidir.

### Totais sem dupla contagem

6. **Dashboard de setembro** não inclui os 1 135,24 € do pagamento do
   cartão em despesas nem em receitas; o donut não tem fatia de
   transferência.
7. **Orçamentos e alertas** ignoram transferências e acertos.
8. **Export CSV** inclui transferências e acertos com `Tipo` correto e
   conta contraparte.

### Funcional

9. Transferência criada, editada e apagada atua sempre nas duas pernas.
10. Transferência entre contas de moedas diferentes guarda os dois
    valores.
11. "Marcar como transferência" liga a uma transação existente na
    outra conta ou cria a contraperna.
12. Regra `MarkAsTransfer` transforma linhas importadas em
    transferências; importar o segundo extrato **não duplica** a
    transferência (liga à perna existente).
13. Linhas anteriores a `OpeningBalanceDate` são excluídas por defeito
    no import.
14. Vista do cartão mostra dívida, limite, disponível, ciclo corrente,
    extrato anterior, próximo pagamento e prestações ativas.
15. Planos de prestações (incluindo planos a meio) geram o calendário
    certo e entram na previsão do próximo pagamento.

### Dívida técnica (grupo 0)

16. RLS + FORCE + sentinel em `categorization_rules`, `import_profiles`,
    `import_batches`.
17. Reaplicar regras processa todas as transações (> 100).
18. Import publica `TransactionCreatedIntegrationEvent`; alertas de
    orçamento disparam para transações importadas.
19. Totais incluem transações de categorias arquivadas.
20. Arquivar conta com transações ativas → 400 (ValidationProblem PT-PT).

### Qualidade

21. Testes de integração dos endpoints novos, incluindo multi-tenancy.
22. Testes de arquitetura para `Transfers` e `InstallmentPlans`.
23. `responsive-audit.md` preenchido (regra dura 7 do `AGENTS.md`).
24. ADR-014 escrito; `tech-stack.md` §17/§18 atualizados.
25. Sub-agent deep review feito e achados resolvidos ou registados.

## How to verify each bullet

| # | Verificação |
|---|---|
| 1–5 | **Reconciliação real** (manual, stack local `docker compose up -d --build`, conta nova): extratos em `C:\Users\MarloGamer\Documents\Sextante\extratos\` convertidos para CSV fora do repo; importar; comparar `GET /api/financial/accounts` (`currentBalance`) e `GET /api/financial/accounts/{id}/balance?at=2026-08-31` com os valores acima. Registar resultado neste ficheiro (secção *Resultados*). |
| 6 | `GET /api/financial/transactions/summary?from=2026-09-01&to=2026-09-30` antes e depois de criar a transferência → `expense`/`income` iguais. Verificação visual no dashboard. |
| 7 | Teste de integração em `BudgetWorkflowTests`: transferência e acerto não alteram `BudgetProgress` nem criam `BudgetAlert`. |
| 8 | Teste em `TransactionExportTests`: colunas `Tipo` e `Conta contraparte`. |
| 9–11 | Testes de integração `TransfersCrudTests` (novo). |
| 12–13 | Testes `CsvImportTransferTests` (novo) com fixture sintética ActivoBank conta + cartão. |
| 14–15 | Testes unitários de `CreditCardStatementCalculator` e `InstallmentSchedule`; teste de integração do endpoint `/credit-card`; verificação visual. |
| 16 | Testes em `TESTS/Sextante.IntegrationTests/MultiTenancy/` por tabela (leitura vazia, escrita 42501, sentinel rejeitado). |
| 17 | Teste de integração com 250 transações + reapply. |
| 18 | Teste: import em categoria com orçamento a 80 % → `BudgetAlert` criado. |
| 19 | Teste: arquivar categoria não muda o summary. |
| 20 | Teste em `AccountsCrudTests`. |
| 21 | `dotnet test` verde no CI; `FinancialMultiTenancyTests` com casos de transferência, reconciliação, cartão e prestações. |
| 22 | `dotnet test tests/Sextante.ArchitectureTests`. |
| 23 | Ficheiro `responsive-audit.md` com as 3 viewports por ecrã novo/alterado. |
| 24 | Ficheiros presentes; diff de `tech-stack.md`. |
| 25 | Relatório do deep review referido no PR. |

## Out of scope for validation

- Transferências recorrentes, importação XLSX/PDF, distribuição de
  prestações pelos meses nos relatórios (fora de scope, ver
  `requirements.md`).
- Validação em produção (VPS) — o dogfooding arranca depois desta phase
  e é validado no item "Dogfooding 1 mês" do roadmap.
- Performance do saldo on-demand com volumes além do uso pessoal.

## Resultados

Reconciliação com os extratos reais feita em **2026-09-24** (grupo 8.1):
- stack local `docker compose up -d --build` no commit do grupo 8;
- tenants descartáveis na DB local, um por variante e por ordem de import;
- conversão fora do repo (`Documents\Sextante\extratos\csv`), sem dados reais no repo.

**Conversão (controlo antes de importar)**
- **Conta à ordem (XLSX):** 95 movimentos. O saldo encadeado bate linha a linha: 1 589,87 € antes do 1.º movimento, 588,81 € no fim de 27/08 e 137,82 € no fim.
- **Cartão (PDF, agosto):** 54 movimentos (53 débitos + 1 pagamento). Débitos 746,30 € e créditos 837,13 €, iguais ao "Resumo de movimentos" do extrato; −2 500,00 − 746,30 + 837,13 = −2 409,17 €. O texto do PDF não separa as colunas Débito/Crédito: o sinal vem do descritivo (pagamento = crédito), validado pelos totais do resumo.

**Regras:** só uma, "PAGAMENTO CARTAO" → transferência para o cartão (o exemplo da spec). Restantes linhas com a categoria do fallback.

| # | Resultado | Obtido |
|---|---|---|
| 1 | ✅ | Variante 28/08 (588,81 €): saldo atual da conta **137,82 €**; 57 linhas anteriores ao saldo inicial excluídas por defeito. |
| 2 | ✅ | Variante 01/08 (1 589,87 €): saldo atual da conta **137,82 €**. |
| 3 | ✅ | Cartão −2 500,00 € a 01/08: saldo a 31/08 **−2 409,17 €**, nas duas variantes e nas duas ordens de import. |
| 4 | ✅ | Com o pagamento de 1 135,24 € (11/09, data valor 14/09) como transferência: dívida atual **−1 273,93 €**. |
| 5 | ⏳ | Pendente: o saldo real do cartão à data não está nos ficheiros. Mecânica verificada com um valor sintético (−1 300,00 € a 23/09): um só `Adjustment` de −26,07 €, o saldo à data fica igual ao indicado e os totais não mudam. Fechar no arranque do dogfooding com o valor da app do banco. |
| 6 | ✅ | Setembro: despesa 409,47 € e receita 797,92 €, sem os 1 135,24 € em nenhum dos lados. |
| 7 | ✅ | Orçamento de setembro na categoria das despesas: gasto 409,47 € = soma das despesas regulares, sem o pagamento do cartão. |
| 8 | ✅ | Export: tipos `Despesa`/`Receita`/`Transferência`; as 4 linhas `Transferência` (2 pagamentos × 2 pernas) têm `Conta contraparte`. |

**Pernas:** na variante 01/08, 4 pernas (2 pagamentos), com qualquer ordem de import, sem nenhuma receita regular no cartão.
- **Conta primeiro:** a linha do cartão fica "já registada".
- **Cartão primeiro:** a linha da conta liga-se à entrada do cartão.

**Divergência encontrada e corrigida (R4):** no extrato do cartão o pagamento aparece como ">PAGAMENTO CARTAO DE CREDITO" e casa com a regra da conta, cujo alvo é o próprio cartão. Era importado como **receita normal**: na variante 01/08 o cartão ficava errado em exatamente 837,13 €.
- **Correção:** uma regra cujo alvo é a conta da linha não se aplica a essa linha, que segue o caminho das linhas sem regra e reconhece a perna já registada.
- **Teste:** `CsvImportTransferEdgeTests.Rule_targeting_the_imported_account_still_recognises_the_recorded_leg`.

**Limitação conhecida (variante 28/08):** o pagamento de 12/08 do cartão não tem contraperna, porque o lado da conta é anterior ao saldo inicial. Fica como receita regular do cartão em agosto. Os saldos batem; os totais de agosto contam-no como receita. Para o limpar: "Marcar como transferência" com contraperna na conta à ordem; a perna criada, anterior a 28/08, não mexe no saldo da conta.

**Open questions**
- **2 (janela de 7 dias):** pagamento de agosto com Δ 0 dias entre a data valor da conta (12/08) e a do cartão (12/08). A data de movimento do cartão (01/08) é a do extrato, não a do débito, e por isso o perfil usa `Data Valor`. O pagamento de setembro fica por confirmar com o extrato do cartão de setembro. 7 dias chegam para o caso observado.
- **4 (extrato do cartão em CSV):** o ActivoBank dá o cartão só em PDF. O PDF tem texto extraível (sem OCR), mas a conversão é feita fora da app com um script. Até existir import de PDF (Maybe pile), o dogfooding do cartão depende dessa conversão mensal.
