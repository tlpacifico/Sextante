# Importação de extratos (XLSX, PDF, JSON)

> Phase 6.6. Além de CSV, o wizard de importação (`/app/imports/new`) aceita os
> extratos tal como o banco os entrega. O backend lê-os, **valida-os contra os
> números do próprio banco** e só então os põe no mesmo preview e lote do CSV.
> Se uma validação falha, **não se importa nada** (nem sequer fica um lote).

## Formatos suportados

| Ficheiro | Origem | Conta de destino típica | Como obter |
|---|---|---|---|
| `.xlsx` | Histórico da conta à ordem ActivoBank | Conta à ordem | Exportação dos movimentos em Excel, no homebanking |
| `.pdf` | Extrato mensal do cartão de crédito ActivoBank (2 layouts: até agosto/2026 e desde setembro/2026) | Cartão de crédito | Extrato mensal em PDF, no homebanking |
| `.json` | Movimentos da Coverflex (`movements.list`) | Conta Coverflex | Resposta JSON dos movimentos da Coverflex, gravada em ficheiro |
| `.csv` | Qualquer banco | Qualquer | Perfil de importação + mapeamento de colunas (fluxo anterior, inalterado) |

Outros bancos ou layouts dão erro explícito ("layout de extrato não suportado"):
nunca se adivinha. PDFs digitalizados (sem texto) não são suportados.

## O que cada validação garante

- **XLSX da conta** — o saldo encadeia linha a linha (`saldo anterior + valor = saldo`,
  tolerância zero). Uma linha que não encadeia é recusada com o movimento, o saldo
  esperado e o encontrado.
- **PDF do cartão** — o total de débitos e o total de créditos extraídos batem com o
  "RESUMO DE MOVIMENTOS" do extrato, e `dívida anterior + débitos − créditos = dívida atual`.
  Débito ou crédito decide-se pela coluna onde o valor aparece, não pelo texto.
- **JSON da Coverflex** — só movimentos `confirmed` (os `pending` são contados e mostrados,
  mas não se importam; os `cancelled` ignoram-se). A aritmética de cada movimento
  (`saldo antes ± valor = saldo depois`) e a identidade global
  (`saldo inicial + Σ movimentos = saldo final`) têm de bater. A data é a de Lisboa, não a UTC.
- **Moeda** — a moeda do extrato tem de ser a da conta escolhida.

## Overlap com o que já foi importado

Importar de novo um extrato, ou um que se sobrepõe a outro, não duplica movimentos:

- Contas com saldo por linha (XLSX, Coverflex): corta-se até à última linha cujo saldo é o
  saldo atual da conta na data do último movimento. Isto distingue dois movimentos idênticos
  no mesmo dia, que o detetor de duplicados por data/valor/descrição não consegue.
  Se nenhuma linha do extrato tem esse saldo, o extrato e a conta divergem e a importação
  é recusada com a explicação.
- Cartão: se o extrato é inteiramente posterior ao último movimento, a dívida anterior tem
  de bater com o saldo do cartão no dia anterior ao período; se já toca o que existe,
  cortam-se as linhas com data até ao último movimento.

O cartão de validação do wizard mostra o formato, o período, os saldos, cada verificação com o
valor esperado e o calculado, e quantas linhas se cortaram.

## Notas técnicas

- Conversores em `Financial.Infrastructure/StatementConversion/`, contrato em
  `Financial.Application/StatementConversion/`. Libs: ExcelDataReader (XLSX, MIT) e PdfPig
  (PDF, Apache-2.0, package id `PdfPig`).
- O resultado do conversor passa por um adaptador para linhas canónicas
  (`Data Lanc.;Data Valor;Descrição;Valor;Saldo`, `dd/MM/yyyy`, decimal vírgula), por isso
  regras de categorização, transferências e confirm são os do CSV.
- Entrada não confiável: limite de 5 MB, assinatura do ficheiro validada, no máximo 30 páginas
  (PDF) / 20 000 linhas, 30 s por conversão. Os logs só têm formato, nº de linhas, resultado
  das verificações e duração — nunca descrições nem valores.
- Os testes usam extratos sintéticos gerados em memória (nunca ficheiros reais no repositório).
