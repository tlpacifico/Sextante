# Mission

> O *porquê* do Sextante: visão, audiência, princípios, não-objetivos e métrica de sucesso.

---

## 1. Problema

Um investidor pessoal técnico que opera em **mais de uma moeda** (BRL, EUR, USD) e em **mais de uma jurisdição** (Brasil + Europa) acaba a usar **duas ou três ferramentas mais Excel** para fazer o que devia ser uma coisa só:

- Apps de gestão financeira pessoal (Mobills, Organizze, YNAB) **não integram** gestão de carteira de investimentos com a profundidade necessária para aplicar critérios próprios (Fisher 15 Questions, checklists quantitativas).
- Apps de carteira (Status Invest, Investidor10) **não fazem** controlo orçamental do dia-a-dia.
- Nenhuma faz **multi-moeda nativa** com rastreabilidade do câmbio aplicado em cada transação.

O resultado é que o utilizador-alvo gere as suas finanças em Excel + Mobills + planilha de carteira, com reconciliação manual no fim do mês.

## 2. Audiência

**Single user no MVP**: o autor do projeto (Thacio), que vai dogfood o sistema na sua vida financeira real.

A arquitetura é **multi-tenant desde o dia 1** (User → Tenant → Membership) — não como feature exposta no MVP, mas como **preparação não-bloqueante** para futuras fases (casal/contabilista na Fase 5; SaaS público na Fase 7). Esta é uma decisão de arquitetura, **não** uma promessa de produto.

> O MVP **não é** um produto SaaS. É uma ferramenta pessoal robusta o suficiente para substituir a planilha + Mobills + planilha de carteira.

## 3. Proposta de valor

Um único sistema onde o utilizador:

1. **Controla gastos e receitas** com categorização (manual + por regras), importação de extratos via CSV, recorrentes automáticas e metas mensais por categoria.
2. **Gere a carteira** (Fase 2) com pesos-alvo definidos por **critérios próprios** — não por consenso de mercado — e usa questionários configuráveis (Fisher, checklists quantitativas) que influenciam o peso sugerido.
3. **Recebe sugestões de aporte** (Fase 2) que reequilibram a carteira automaticamente ao longo do tempo.
4. **Lida com várias moedas** sem perder rastreabilidade: cada transação carrega `amount`, `currency`, `exchangeRateToPrimary`, `exchangeRateAt`. Reporting converte; storage não.

## 4. Princípios de produto

Estes princípios são **tie-breakers** quando uma decisão é ambígua. Se uma feature contraria um princípio, ou a feature está mal desenhada, ou o princípio precisa de ser revisto explicitamente em fase de Replanning.

1. **Privacidade por defeito.** Os dados de um tenant nunca tocam noutro. Sem exceções de "feature de comparação social" ou similar. Multi-tenancy é invariante de domínio com defesa em profundidade (Global Query Filters + Row-Level Security).
2. **Multi-moeda nativa, não bolt-on.** Toda transação carrega a sua moeda original e o câmbio aplicado no momento. Reporting converte; storage não. Histórico de câmbio é snapshot, não query on-demand.
3. **Automação sobre manualidade — mas auditável.** Recorrentes, regras de categorização e (Fase 2) sugestões de aporte são automáticos, mas o utilizador vê e pode reverter qualquer decisão automática. Cada decisão automática deixa rasto: que regra aplicou, quando, com que valores.
4. **Self-hosted-friendly.** Como o sistema vive em VPS própria, dependências externas (APIs de câmbio, cotações, SMTP) **têm de ter fallback ou degradação graciosa**. Se o ECB cair, o utilizador pode meter taxa manualmente. Se a Brapi cair, a UI sinaliza "preço desatualizado", não rebenta.
5. **MVP estreito e profundo, não largo e raso.** Antes de adicionar módulo novo (Investimento, Integrações, etc.), o existente tem de estar **sólido** — testado, com edge cases conhecidos e documentados, dogfooded.

## 5. Não-objetivos (explícitos)

O sistema **não** vai tentar fazer:

- Substituir corretora ou banco.
- Fazer execução de ordens.
- Aconselhamento financeiro automatizado ("robo-advisor").
- Open Banking no MVP (avaliar pós-MVP, Fase 3).
- App mobile no MVP (web responsive chega).
- Multi-idioma na UI no MVP (mas i18n estruturado desde o início para evitar refactor).
- Compliance formal documentado (LGPD/GDPR é WIP, não bloqueia o MVP).

## 6. Métrica de sucesso

**MVP é considerado completo quando o utilizador (Thacio) usar o sistema durante 1 mês completo sem voltar a abrir Excel ou outra app financeira.**

É uma métrica binária deliberada. Não há "aceite com ressalvas". Se o sistema não substitui a planilha + Mobills, está incompleto — mesmo que todos os user stories estejam implementados.

Métricas mais granulares (tempo médio para categorizar despesas, % de regras que apanham automaticamente, etc.) são úteis para diagnóstico durante o dogfooding, mas não são parte do **Definition of Done** do MVP.

## 7. Visão de longo prazo

O detalhe está em `roadmap.md`. Em resumo:

- **MVP**: Módulo Financeiro completo + dogfooding 1 mês.
- **Fase 2**: Módulo de Investimento (carteira, cotações, questionários, sugestão de aporte, balanceamento).
- **Fases 3-7**: Integrações (Open Banking, categorização ML, notificações), relatórios fiscais (DARF, Anexo J, IRS), multi-utilizador (UI de convites, roles), mobile, e — só se fizer sentido depois de tudo o resto — plataforma SaaS pública.

Cada fase é um compromisso pequeno. A cada fim de fase há **Replanning** explícito: a Constitution é revisitada, o roadmap é atualizado, e o processo melhora.

---

## Notas para o agente

- A documentação canónica detalhada (15+ ficheiros) vive no Obsidian Vault — ver `README.md` para o caminho.
- Decisões de stack, arquitetura e detalhes técnicos vivem em `tech-stack.md`. **Esta mission é estável e raramente muda**; tech-stack pode evoluir mais.
- Referências aos docs Obsidian usam o formato `Vault: 00 - Visão e Produto.md` para deixar claro que é um documento externo ao repo.
