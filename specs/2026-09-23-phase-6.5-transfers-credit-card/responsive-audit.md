# Responsive audit — Phase 6.5: Transferências entre contas + cartão de crédito

> Regra dura 7 do `AGENTS.md` e `tech-stack.md` §19.5. Feito em
> **2026-09-24** (grupo 8.3), contra a stack local (`docker compose up
> -d --build`), com dados sintéticos (fixtures do grupo 7, cartão com
> definições, um plano de prestações, uma transferência e um acerto).

## Método

Script Playwright (Chromium) fora do repo:
- login pela UI;
- cada ecrã ou dialog da tabela abaixo em 375 × 667 e 768 × 1024 (com `hasTouch` + `isMobile`, ou seja, `pointer: coarse`) e em 1280 × 800 (rato).

Em cada caso mede:
- **Overflow do `<body>`:** `document.documentElement.scrollWidth > innerWidth`.
- **Elementos fora da viewport:** não conta os que estão dentro de contentores com scroll horizontal próprio (`overflow-x-auto`, tabelas PrimeNG).
- **Dialogs:** largura / viewport, e scroll horizontal dentro de `.p-dialog-content`.
- **Touch targets:** `button`, `a[href]`, `[role=button]`, `p-select`, input da checkbox, inputs, com altura ou largura < 44 px. Só a 375 e 768.
- **Gráficos:** `canvas`/`svg` com largura 0 ou a sair da viewport.

Os screenshots ficaram fora do repo.

## Resultado final

| Ecrã / dialog | 375 × 667 | 768 × 1024 | 1280 × 800 |
|---|---|---|---|
| Dashboard | ✅ | ✅ | ✅ |
| Contas (lista com saldo, ações por conta) | ✅ | ✅ | ✅ |
| Dialog de conta — definições do cartão | ✅ ¹ | ✅ ¹ | ✅ |
| Dialog de reconciliação | ✅ | ✅ | ✅ |
| Página do cartão (dívida, ciclo, prestações) | ✅ | ✅ | ✅ |
| Dialog do plano de prestações | ✅ | ✅ | ✅ |
| Transações (linhas de transferência e acerto, filtros) | ✅ ¹ | ✅ ¹ | ✅ |
| Dialog "Nova transferência" | ✅ ¹ | ✅ ¹ | ✅ |
| Dialog "Marcar como transferência" | ✅ ¹ | ✅ ¹ | ✅ |
| Import — passo 1 (conta de destino) | ✅ ¹ | ✅ ¹ | ✅ |
| Import — passo 3 (transferências, antes do saldo inicial) | ✅ | ✅ | ✅ |
| Regras (lista com ação) | ⚠️ ² | ⚠️ ² | ✅ |
| Dialog de regra com ação "transferência" | ✅ ¹ | ✅ ¹ | ✅ |

Nenhum ecrã tem overflow horizontal do `<body>` em nenhuma viewport. Todos os dialogs cabem (≤ 95vw a 375 px) sem scroll horizontal interno.

¹ O script ainda assinala o ícone interno do `p-select` (`.p-select-dropdown`, 44 × 42). É um falso positivo: o alvo tátil é o `p-select` inteiro, com 44 px.
² As setas de reordenar regras (12 × 16 px) são anteriores a esta phase (Phase 4) e não foram alteradas. Ficam em `backlog/2026-09-24-reordenar-regras-touch.md`.

## Achados corrigidos

Commit `UI: correções do responsive audit da Phase 6.5`.

| Achado | Onde | Correção |
|---|---|---|
| Scroll horizontal de 74 px dentro do dialog a 375 px (e 12 px a 1280 px no plano de prestações) | Dialog de conta (dia de fecho / dia de pagamento) e dialog do plano de prestações (n.º de prestações / já pagas) | O `p-inputNumber` tinha largura intrínseca maior do que a coluna da grelha de 2 colunas: `styleClass="w-full"`, `inputStyleClass="w-full min-w-0"` e `min-w-0` nas colunas. |
| Filtro "Tipo" com rótulos cortados ("Toc Desp Rec Transf Ace") a 375 e 1280 px | Transações | O filtro ganhou "Transferências" e "Acertos" nesta phase. Passa a ocupar 2 colunas a partir de `md` e os botões quebram de linha (`.sxt-selectbutton-wrap`). No PrimeNG 20 o `styleClass` do `p-selectbutton` vai para cada botão, por isso a classe fica no próprio elemento. |
| Campos "Valor mín./máx." a invadir a coluna ao lado | Transações | `min-w-0` nas colunas e `inputStyleClass="w-full min-w-0"`. |
| Touch targets < 44 px em ecrãs táteis: botões e campos PrimeNG com 42 px, botões de ícone com 32–40 px, paginação com 40 px, checkboxes com 20 px, ícones do topo com 36 px | Todos os ecrãs | Regra global `@media (pointer: coarse)` em `styles.scss`: `min-height`/`min-width` de 44 px em botões, toggles, selects, inputs, datepicker e paginação. Nas checkboxes cresce só a área clicável (o input invisível), mantendo o desenho de 20 px. Os ícones do topo (`.icon-btn`) passam a 2,75 rem em ecrã tátil. Com rato nada muda. |

## Achados aceites

- **Tabelas largas** (transações, detalhe do ciclo do cartão): ficam dentro de contentores com scroll horizontal próprio (`overflow-x-auto`), o padrão do projeto. O `<body>` não transborda.
- **Setas de reordenar regras (12 × 16 px):** anteriores à phase; ver o backlog acima.
