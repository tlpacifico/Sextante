# Importação de extratos em XLSX

> Ideia surgida no replanning da Phase 6.5 (2026-09-23), no primeiro
> arranque do dogfooding com dados reais.

## Problema

O banco do utilizador (ActivoBank) exporta o histórico da conta à ordem
em **XLSX** (cabeçalho de 6 linhas com número de conta, moeda e
período; depois `Data Lanc.`, `Data Valor`, `Descrição`, `Valor`,
`Saldo`). O extrato do cartão de crédito vem só em **PDF**. O import
do Sextante é CSV-only, por isso o utilizador tem de converter à mão
antes de cada import — atrito que ameaça o dogfooding.

## Proposta

- Aceitar `.xlsx` no upload do wizard: ler a primeira folha e tratá-la
  como uma tabela, reutilizando `ImportProfile` (`SkipRows`,
  mapeamento de colunas). Datas e números chegam já tipados,
  dispensando `DateFormat`/`DecimalSeparator`.
- Biblioteca candidata: ClosedXML ou ExcelDataReader (avaliar licença e
  peso).
- PDF (extrato do cartão) continua na "Maybe pile" do roadmap
  (importação de PDF/OCR).

## Dependências

Nenhuma. Pode entrar logo a seguir à Phase 6.5 ou dentro dela, se o
dogfooding o exigir.
