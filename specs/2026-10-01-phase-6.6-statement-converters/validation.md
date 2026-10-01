# Validation — Phase 6.6: Conversores de extratos no backend

> Cada bullet tem um caminho de verificação concreto. Bullets sem
> caminho de verificação não pertencem aqui.

## Definition of done

Resposta do utilizador à pergunta de validação: **testes unitários com
extratos reais anonimizados** (XLSX, PDF nos 2 layouts, JSON Coverflex) e
casos de falha. Os restantes bullets derivam da saída da phase no roadmap
e das regras duras do `AGENTS.md`.

### Conversores (núcleo da phase)

1. **XLSX da conta converte e valida.** O extrato XLSX anonimizado produz
   as linhas esperadas e o saldo encadeia de ponta a ponta.
2. **XLSX com saldo adulterado falha.** Uma linha com saldo que não encadeia
   → `StatementValidationException` com linha, esperado e encontrado.
3. **PDF do cartão, layout A (≤ ago/2026) converte e valida.** Movimentos
   extraídos; Σ débitos e Σ créditos == "RESUMO DE MOVIMENTOS"; dívida
   anterior + débitos − créditos == dívida atual.
4. **PDF do cartão, layout B (≥ set/2026) converte e valida.** Idem, com
   datas `MM/DD` (ano deduzido do período, virada de ano incluída),
   descrição multi-linha e coluna "Rede".
5. **PDF com totais do resumo que não batem, dívida que não encadeia ou
   layout desconhecido falha** com mensagem PT-PT; PDF corrompido/protegido
   dá erro explícito (nunca 500).
6. **JSON da Coverflex converte.** Amostra anonimizada → linhas esperadas
   (cêntimos→euros, só `confirmed`, data em Lisboa); identidade global e
   aritmética por linha validadas; `pending` ignorados e contados (D16); JSON inválido / campo em falta / moeda
   diferente → erro PT-PT.
7. **Corte de overlap ancorado no saldo.** Reimportar um extrato já
   importado dá 0 linhas novas; extrato parcialmente sobreposto importa só
   as linhas novas; **dois movimentos idênticos no mesmo dia** não são
   confundidos com duplicados.

### Integração com o import

8. **Validação que falha não importa nada.** Upload de ficheiro inválido →
   `400` PT-PT com valores esperado/calculado e **nenhum `ImportBatch`
   criado**.
9. **Fluxo CSV inalterado.** Os testes de integração de import CSV
   existentes passam sem alterações (incl. `example-activo-bank.csv` e as
   fixtures sintéticas de conta e cartão).
10. **Formato canónico.** O adaptador produz CSV equivalente a
    `activo-conta-sintetico.csv` / `activo-cartao-sintetico.csv`.
11. **Wizard aceita os formatos.** Em browser real, upload de XLSX, PDF e
    JSON leva ao preview (sem passo de mapeamento) com o cartão de
    validação; confirmar cria as transações e regras de categorização
    aplicam-se como nos CSV.

### Regras duras do repo

12. **Responsive DoD.** Wizard com os novos formatos passa 375×667,
    768×1024 e 1280×800; achados em `responsive-audit.md` (`AGENTS.md` §2.7).
13. **Sem PII nos logs.** Conversores só logam formato, nº de linhas,
    verificações e duração (D10).
14. **Isolamento de módulo.** Testes de arquitetura (NetArchTest) verdes: as
    libs e o contrato vivem no `Financial`; nada referencia internos de
    outro módulo.
15. **Changelog atualizado** (skill `changelog`) antes do merge; roadmap
    atualizado via conversa com o agente.

## How to verify each bullet

| # | Verificação |
|---|---|
| 1, 2 | `dotnet test tests/Modules/Financial.Application.Tests --filter "FullyQualifiedName~ActivoBankAccountXlsxConverter"` |
| 3, 4, 5 | `dotnet test tests/Modules/Financial.Application.Tests --filter "FullyQualifiedName~ActivoBankCardPdf|FullyQualifiedName~CardLayout"` |
| 6 | `dotnet test tests/Modules/Financial.Application.Tests --filter "FullyQualifiedName~CoverflexJson"` |
| 7 | `dotnet test tests/Modules/Financial.Application.Tests --filter "FullyQualifiedName~StatementOverlapTrimmer"`; e re-upload do mesmo extrato no teste de integração (bullet 8) |
| 8 | `dotnet test tests/Sextante.IntegrationTests --filter "FullyQualifiedName~CsvImport"` (inclui upload XLSX/PDF/JSON, falha de validação sem batch, re-upload → 0 linhas) |
| 9 | Mesmo comando do bullet 8: os testes CSV pré-existentes passam sem edição |
| 10 | Teste unitário do adaptador comparando com o conteúdo das fixtures CSV sintéticas |
| 11 | Manual: `docker compose up`, wizard com um ficheiro de cada formato (amostras anonimizadas), confirmar e ver as transações em `/transactions`; `ng test` para os specs do wizard e do `financial-api.service` |
| 12 | Manual em DevTools nas 3 viewports → `responsive-audit.md` |
| 13 | Revisão do código dos conversores e do handler: nenhum `Log*` com descrição/valor; `grep` por `Description`/`Amount` em chamadas de logging |
| 14 | `dotnet test tests/Sextante.ArchitectureTests` |
| 15 | Skill `changelog`; `git diff` mostra `CHANGELOG.md` e `specs/roadmap.md` atualizados |

Verificação final transversal: `dotnet format --verify-no-changes`,
`dotnet test` da solução (unit + arquitetura; integração na última tarefa
do grupo 5), `ng build` + `ng test` no `Sextante.Web`.

## Out of scope for validation

- **Com extratos reais não anonimizados.** A phase prova-se com amostras
  anonimizadas no repo; a validação com os ficheiros reais do utilizador e
  o saldo bater ao cêntimo com o banco acontece no dogfooding da Fase 1.5,
  não aqui. (Foi proposto, e não escolhido, um bullet de importação manual
  dos ficheiros reais como critério de merge.)
- **Chat/IA**, inferência de conta, ingestão por email, obtenção
  automática do JSON da Coverflex (Phases 6.7/6.8 e extensões futuras).
- **Outros bancos ou layouts** além dos 4 listados; **OCR** de PDFs
  digitalizados.
- **Casos especiais** do processo (planos de prestações, saldo inicial do
  cartão com sinal errado, regra com destino na própria conta).
- **Performance com extratos grandes** além dos limites defensivos (D11).
- **Deploy/produção**: a phase não altera infra; o merge para `main`
  dispara o CD existente (ADR-013).
