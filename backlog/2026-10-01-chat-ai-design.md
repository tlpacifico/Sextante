# Chat com IA — design

> Resultado do brainstorm de 2026-10-01. Complementa
> `2026-10-01-chat-ai-importacoes.md` (processo atual, passo a passo).
> Estado: **aprovado** (2026-10-01). Entrou no roadmap como Fase 1.5
> (Phases 6.6, 6.7 e 6.8, pós-MVP, depois do dogfooding da Phase 6.5), na
> branch `replanning`.

## 1. Objetivo

O utilizador pede no próprio Sextante ("importa o extrato do cartão de
setembro") e um chat com IA conduz o processo que hoje é feito à mão numa
sessão do Claude Code.

**Sucesso:** o fluxo dos 9 passos corre sem sair do Sextante. Os números batem
certo com o banco, e o utilizador só intervém para decidir (conta, categorias
duvidosas, confirmação final).

**A longo prazo:** a importação é o caso de uso inicial. O chat é uma
plataforma que vai ganhar outras capacidades e, mais tarde, histórico.

## 2. Decisões tomadas

| Tema | Decisão |
|---|---|
| Âmbito da v1 | Só importação de extratos |
| Entrada | Upload do ficheiro no chat. Coverflex automática e leitura do email ficam como extensões futuras |
| Privacidade | Provider cloud, dados completos (descrições e valores). Logs continuam sem PII |
| Números | Vêm sempre dos conversores no backend, nunca do modelo a ler o ficheiro |
| Orquestração | Workflow fixo e determinístico; a IA fala nas pontas (explica falhas, sugere categorias) |
| Escrita | A IA nunca grava. Propõe, o utilizador confirma na UI, o backend executa pelos handlers normais |
| Categorização (passo 9) | Lote com aprovação linha a linha |
| UX | Painel lateral |
| Provider | OpenCode **Go** ($10/mês), endpoint `https://opencode.ai/zen/go/v1/`, compatível com OpenAI e Anthropic |
| Modelo concreto | Decidido na fase 2, com teste comparativo de 2–3 candidatos |
| Histórico | Fase posterior: conversas retomáveis; ficheiros originais **não** são guardados |
| Módulo | `Assistant`, novo, com os 5 projetos habituais |

## 3. Decomposição em fases

1. **Conversores no backend** (sub-projeto próprio). XLSX da conta à ordem, PDF do
   cartão (2 layouts) e JSON da Coverflex, mais validações e corte de overlap, no
   módulo `Financial`. Tem valor por si só: o wizard de importação passa a aceitar
   estes formatos sem chat.
2. **Núcleo do chat + capacidade de importação, sem histórico.** Módulo
   `Assistant`, `IChatModel`, protocolo de propostas, painel lateral e teste de
   modelos.
3. **Histórico.** Schema `assistant`, conversas retomáveis, resumo de contexto.
4. **Segunda capacidade** (a definir; provavelmente consultas só de leitura).

As fases 2 e 3 tocam em integração externa e em DB: deep review com sub-agent
obrigatório e implementação grupo a grupo (`AGENTS.md` §2.3 e §6).

## 4. Arquitetura

### 4.1 Camadas

1. **Núcleo do chat** (`Assistant`): conversa, `IChatModel`, registo de
   capacidades, protocolo propor → confirmar, `conversationId`.
2. **Capacidades:** cada uma declara as suas tools, o seu workflow (se tiver) e o
   que pode ler e escrever. A importação é a primeira. Uma capacidade pode ser
   workflow fixo (importação) ou agente com tools (consultas futuras).
3. **Regra de escrita (constante):** a IA nunca grava.

### 4.2 Isolamento

- `Assistant` fala com outros módulos só via `*.PublicApi` ou HTTP loopback
  (`AGENTS.md` §3.2). Nunca referencia `Financial.Domain`, `.Application`,
  `.Infrastructure` ou `.Api`.
- Contas, transações e importação são expostas pelo `Financial` na sua
  `PublicApi` como DTOs. Verificado por NetArchTest.
- Endpoints do backend por passo do workflow, tenant-aware, a reaproveitar os
  handlers Wolverine existentes.

### 4.3 Estado

- **Fase 2:** estado no cliente (Signals), backend stateless; cada chamada recebe
  o que precisa. Reaproveita o fluxo upload → preview → confirmar do wizard.
  Sem schema novo.
- **Fase 3:** estado passa a ser persistido no servidor (secção 7).
- O `conversationId` existe desde a fase 2. Serve de `x-opencode-session` e,
  depois, de chave da conversa persistida.

## 5. Protocolo de mensagens

Cada mensagem tem papel e **partes tipadas**; a UI renderiza cada tipo à sua
maneira. A forma é a mesma em memória (fase 2) e na DB (fase 3).

- `text`: conversa normal.
- `proposal`: ação proposta, com payload estruturado e estado (`pending`,
  `confirmed`, `rejected`). Exemplos: `select_account`, importar N transações,
  criar N regras.
- `result`: o que o backend fez depois da confirmação.
- `validation`: resultado de uma validação, com os números calculados pelo código.

## 6. Capacidade de importação

### 6.1 Fluxo

1. Upload. O backend deteta o tipo e **sugere** a conta (6.2). O chat mostra a
   sugestão e a lista de contas; **a escolha final é sempre do utilizador**
   (`proposal` `select_account`).
2. Com a conta confirmada, o backend vai buscar o último movimento.
3. Converte e valida (saldo encadeado na conta; totais do "RESUMO DE MOVIMENTOS"
   e dívida anterior encadeada no cartão). Se falhar, **nada é importado**; a IA
   explica a falha citando os valores calculados pelo código.
4. Corta o overlap e gera o preview; `proposal` de importação; o utilizador
   confirma.
5. O backend importa e reconcilia o saldo na data de fecho; `result` com a
   diferença, se houver.
6. A IA sugere categorias em lote; o utilizador aprova linha a linha; o backend
   cria as regras e recategoriza só o aprovado.

Casos especiais já conhecidos (planos de prestações, saldo inicial do cartão com
sinal errado, regra cujo destino é a própria conta) ficam fora da v1 e são
tratados como falha de workflow com explicação, até terem tool própria.

### 6.2 Inferência da conta

Sem identificador explícito: `Account` não tem IBAN nem dígitos do cartão, e os
scripts atuais não leem nenhum identificador do ficheiro.

- **Tipo de ficheiro:** XLSX → conta à ordem, PDF do cartão → cartão, JSON →
  Coverflex.
- **Continuidade de saldo:** o saldo antes do primeiro movimento (XLSX:
  `saldo − valor` da 1.ª linha; cartão: "dívida anterior" do resumo) é comparado
  com o saldo da conta na data correspondente. Match único → pré-selecionada.
  Vários candidatos ou nenhum → lista sem sugestão.
- **Melhoria futura:** campo identificador em `Account` (migration no
  `Financial`), se os ficheiros o trouxerem de forma estável. Ver questões em
  aberto.
- Se o utilizador escolher uma conta que contradiz a inferência, o chat avisa
  antes de importar.

## 7. Histórico (fase 3)

Schema `assistant`, `DbContext` próprio, migrations independentes, RLS.

- `Conversation`: `Id` (Guid v7), `TenantId`, título, capacidade, `CreatedAt`,
  `UpdatedAt`, `DeletedAt`, `Version`.
- `Message`: `Id`, `ConversationId`, papel, partes (JSONB), `CreatedAt`.
- `WorkflowState`: passo atual e dados extraídos por conversa (JSONB). Permite
  retomar uma importação a meio.
- Todas as entidades `ITenantOwned`. Referências a contas e transações guardam só
  o ID, sem FK cross-schema.
- Ficheiros originais **não** se guardam. Apagar uma conversa é definitivo.
- Consumo de tokens registado por conversa, sem conteúdo.
- Contexto enviado ao modelo limitado, com resumo das mensagens antigas, por causa
  da quota do Go.

## 8. Modelo de IA

- `IChatModel`: interface fina, implementação HTTP sobre o endpoint do Go. Trocar
  de modelo é configuração em `appsettings`.
- O Go exige `User-Agent` próprio e `x-opencode-session` estável. O cliente envia
  `User-Agent` do Sextante e o `conversationId`.
- API key como secret em `appsettings.Local.json` ou `.env.local`; nunca commit.
- Usos do modelo na v1: explicar falhas de validação e sugerir categorias.
- Sugestões de categoria são validadas contra a lista real; sugestão inválida é
  descartada, nunca aplicada.
- **Teste de modelos (fase 2):** 2–3 candidatos contra merchants reais (Payshop,
  Lilly, entre outros), medindo qualidade em PT-PT e suporte a tool calling
  (mesmo que a v1 não o use).
- Evitar modelos "free", que costumam ter termos de retenção mais frouxos.

## 9. Privacidade

- Descrições e valores seguem para o provider (decisão tomada para extratos).
- Sem PII nos logs: nem mensagens, nem descrições, nem valores. Só
  `TenantId`, `conversationId`, passo, duração e tokens.
- O Go declara retenção zero na maioria dos modelos, com alguns até 30 dias para
  deteção de abuso, e quase nenhum usado para treino. Confirmar por modelo
  antes de o fixar.
- Cada capacidade futura declara que dados envia ao modelo; capacidades que leiam
  mais dados obrigam a rever isto caso a caso.

## 10. Erros

- **Quota do Go esgotada:** mensagem clara no chat. O workflow determinístico
  continua; sem modelo, o resultado mostra-se em bruto.
- **Resposta inválida do modelo:** descartada e repetida uma vez; depois, mensagem
  de falha, sem aplicar nada.
- **Falha de rede a meio:** na fase 2 recomeça-se (nada foi gravado antes da
  confirmação); na fase 3 retoma-se do último passo confirmado.

## 11. Testes

- **Conversores:** unitários com extratos reais anonimizados e casos de falha
  (saldo que não encadeia, totais que não batem, os dois layouts do cartão).
- **Workflow:** máquina de estados com um `IChatModel` falso.
- **Modelo:** conjunto de merchants reais para comparar candidatos.
- **Arquitetura:** NetArchTest para as regras de módulo.
- **Multi-tenancy** (fase 3): tenant A não vê, não modifica nem apaga conversas do
  tenant B; inserts auto-populam `TenantId`; query sem `TenantContext` lança.
- Testes de integração só correm na última tarefa de cada grupo.

## 12. Questões em aberto

1. Os extratos reais trazem IBAN ou dígitos do cartão no cabeçalho (XLSX acima da
   linha "Data Lanc."; PDF fora do resumo)? Se sim, avaliar o campo identificador.
2. Como se obtém o JSON da Coverflex na v1 (descarregado à mão a partir da API)?
3. Modelo concreto do Go e limite de contexto/resumo na fase 3.
4. Os termos do Go permitem uso como backend de aplicação própria? A página de
   docs não restringe ao CLI, mas exige identificação do cliente; confirmar antes
   da fase 2.
5. Retenção das conversas: guardar para sempre ou com expiração?
