# Responsive audit — Phase 6.6

> Regra dura `AGENTS.md` §2.7 / `tech-stack.md` §19.5. Âmbito: o que a 6.6 mudou na UI —
> wizard de importação (`import-wizard.page.ts`) e o novo cartão de validação do extrato
> (`statement-validation-card.component.ts`).

## Método (e o que **não** foi feito)

Medição automática em Chrome headless (Karma): o componente é renderizado dentro de um
contentor com 375, 768 e 1280 px de largura (com 16 px de padding, como o conteúdo da
página) e mede-se o `scrollWidth` do contentor e os elementos cuja borda direita passa a
do contentor (fora das tabelas com `overflow-x-auto`). Estados medidos: passo 1 com a
mensagem de erro de validação do backend (texto longo) e passo 3 de um extrato de cartão com
cartão de validação (3 verificações, uma falhada, notas de linhas cortadas e pendentes) e 6 linhas.

**Não substitui** o sanity check em DevTools com emulação de dispositivo (os media queries
usam a janela real do Karma, não a largura do contentor, e o tema PrimeNG não está carregado
no Karma, pelo que alturas de botões e toques não são medidos aqui). Fica por fazer, no
dogfooding da Fase 1.5, o passe manual a 375 × 667, 768 × 1024 e 1280 × 800 com um ficheiro de
cada formato.

## Resultados

| Estado | 375 px | 768 px | 1280 px |
|---|---|---|---|
| Passo 1 + erro de validação (texto longo) | ✅ sem overflow | ✅ sem overflow | ✅ sem overflow |
| Passo 3 + cartão de validação + 6 linhas | ✅ sem overflow | ✅ sem overflow | ✅ sem overflow |

- `scrollWidth == clientWidth` do contentor nas 3 larguras e nos 2 estados; nenhum elemento
  fora das tabelas passa a borda do contentor.
- Cartão de validação: a 375 px as métricas (período, saldo antes, saldo depois) ficam numa
  coluna (`grid-cols-1`), a partir de 640 px em 3 colunas; as verificações quebram linha
  (`break-words`) em vez de alargar o cartão.
- A mensagem de erro do upload tem `role="alert"` e quebra linha (`break-words`).
- A tabela de pré-visualização continua dentro de `overflow-x-auto` (inalterada).

## Alvos táteis

A 6.6 não acrescenta botões nem campos novos: o cartão de validação é só leitura e o seletor de
ficheiro é a zona de largar existente. Os botões "Voltar", "Confirmar importação" e
"Analisar ficheiro" e o `p-select` da conta são os de antes e herdam a regra global
`@media (pointer: coarse)` da 6.5 (`min-height`/`min-width` de 44 px em `styles.scss`).
O seletor de perfil CSV esconde-se quando o ficheiro é XLSX/PDF/JSON (menos um campo).

## Achados

Nenhum que exija correção na 6.6.
