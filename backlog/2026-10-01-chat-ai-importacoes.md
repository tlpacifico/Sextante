# Chat com IA para importar extratos

> Ideia surgida no dogfooding da Phase 6.5 (2026-10-01). Hoje o processo é
> feito à mão numa sessão do Claude Code, fora do Sextante. O objetivo é o
> utilizador pedir no próprio sistema ("importa o extrato do cartão de
> setembro") e um chat com IA conduzir o processo.

## Processo atual (como é feito hoje, passo a passo)

### 1. Obter o ficheiro

| Fonte | Formato | Como chega |
|---|---|---|
| Conta à ordem ActivoBank | XLSX `mov<conta>-<data>.xlsx` | Download manual no homebanking |
| Cartão de crédito ActivoBank | PDF `EXT AUTONOMO CARTAO (DOC 2026000NN).pdf` | Email mensal (Gmail); quando falta, download na app do banco |
| Subsídio de alimentação (Coverflex) | JSON da API | `curl` com o bearer token da sessão web |

### 2. Identificar a conta de destino

Pelo tipo de ficheiro: XLSX → *Activo Bank Deb*; PDF do cartão →
*Activo Bank Cred*; Coverflex → *Subsídio alimentação*.

### 3. Saber até onde já está importado

Último movimento por conta (consulta à DB) e último lote de import.
Define a data a partir da qual se exporta / se aproveita o ficheiro.

### 4. Converter para CSV (perfil ActivoBank)

Scripts fora do repo (`Documents\Sextante\ferramentas\`):

- `converter-conta-activobank.py` — XLSX → CSV.
- `converter-cartao-activobank.py` — PDF → CSV com `pypdf`. Dois layouts
  conhecidos: até agosto/2026 (datas `AAAA/MM/DD` numa linha) e desde
  setembro/2026 (datas `MM/DD` sem ano, descrição em várias linhas, coluna
  "Rede"). Débito/crédito decidido pela coluna do valor (modo layout).
- Coverflex — conversão ad hoc do JSON.

Saída: `Data Lanc.;Data Valor;Descrição;Valor;Saldo`, `dd/MM/yyyy`,
decimal vírgula, sinal do ponto de vista da conta.

### 5. Validar antes de importar (controlos que param o processo)

- **Conta**: o saldo encadeia linha a linha (saldo anterior + valor = saldo).
- **Cartão**: soma de débitos e créditos = "RESUMO DE MOVIMENTOS" do PDF;
  dívida anterior encadeia com o extrato do mês anterior.

### 6. Cortar a sobreposição

Retirar linhas já importadas (até ao último saldo conhecido). Não confiar
só no dedup: movimentos idênticos no mesmo dia (ex.: duas transferências
de 90,96 € a 07/09) parecem duplicados entre si.

### 7. Importar no wizard

Conta → upload → mapeamento (auto-detetado) → preview → confirmar. No
preview verificar:

- pagamento do cartão aparece como **"já registada"** (transferência
  criada pelo lado da conta à ordem);
- duplicados e linhas antes da data de abertura;
- regras de categorização aplicadas.

### 8. Reconciliar depois de importar

Saldo da conta na data de fecho = saldo do banco (conta: último saldo do
XLSX; cartão: −dívida atual do extrato; Coverflex: saldo da app).

### 9. Categorizar o que ficou sem regra

- Listar comerciantes novos (caíram em "Outros").
- Sugerir categoria (e categorias novas quando faz sentido).
- **Perguntar ao utilizador** os que não se deduzem (ex.: Payshop, Lilly).
- Criar regras "Contém" + recategorizar as transações afetadas
  (categoria tem de ter o tipo `1 - direction`).

### 10. Casos especiais que surgiram

- Compras em prestações (Apple, Booking) → criar plano de prestações
  ligado à compra.
- Saldo inicial de cartão com sinal errado → corrigir.
- Regra cujo destino é a própria conta (pagamento do cartão) → ignorada.

## Pontos de decisão humana

Escolha de categoria para comerciantes ambíguos; criação de categorias;
planos de prestações; confirmar o import depois de ver o resumo.

## Implicações para o chat

- Os **conversores têm de passar para o backend** como parsers
  determinísticos com testes (PDF cartão em 2 layouts, XLSX conta,
  Coverflex). A IA orquestra; não lê valores do PDF "à mão".
- Ferramentas (tools) que a IA precisa: listar contas e último movimento;
  carregar ficheiro e obter preview; confirmar lote; saldo numa data;
  listar/criar categorias e regras; recategorizar em massa; criar plano de
  prestações.
- Toda a escrita passa pelos mesmos handlers da API (tenant, RLS, audit),
  com confirmação explícita do utilizador antes de gravar.
- Liga-se à ideia de ingestão por email (ler os extratos do Gmail
  automaticamente).

## Em aberto

Provider/modelo e custo, onde vive o chat (página própria ou painel),
guardar histórico de conversas, PII enviada ao modelo (descrições e
valores — ver regra de logs sem PII), e se é uma nova phase no roadmap
(replanning).
