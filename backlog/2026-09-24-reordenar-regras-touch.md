# Reordenar regras de categorização em ecrãs táteis

> Encontrado no responsive audit da Phase 6.5 (2026-09-24, grupo 8.3).
> O controlo é da Phase 4 e não foi alterado na 6.5.

## Problema

Na página de regras, a prioridade muda-se com duas setas empilhadas
(`pi-chevron-up` / `pi-chevron-down`) de 12 × 16 px. Num telemóvel
(375 px) estão muito abaixo dos 44 px de touch target
(`tech-stack.md` §19.5) e é fácil tocar na errada ou falhar.

## Proposta

- Trocar as setas por dois botões PrimeNG `icon-only` lado a lado (a
  regra global `pointer: coarse` já os leva a 44 px), ou por um menu de
  ações da linha ("Subir" / "Descer").
- Alternativa: arrastar para reordenar (`p-table` com `pReorderableRow`),
  mantendo os botões para acessibilidade.

## Critério de aceitação

Responsive audit da página de regras sem alvos < 44 px a 375 e 768 px.
