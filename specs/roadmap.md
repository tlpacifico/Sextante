# Roadmap

> Sequência de phases pequenas, cada uma implementada com a sua própria feature spec. Documento vivo: revisitar em Replanning entre phases.

---

## Convenções

- Cada **phase** tem **1–3 features** (nano-fases).
- Cada phase corresponde a 1 branch `phase-N-<kebab-name>` e termina com merge para `main`.
- **Definition of Done por phase**: testes a passar (incluindo multi-tenancy desde Phase 1), code review (sub-agent deep review obrigatório nas phases marcadas com 🛡️), changelog atualizado, commit `Mark phase N as complete`.
- **Phases que tocam UI** (1b, 2, 3 (filtro), 4 (wizard), 5 (recorrentes/metas), 6 (polish), 7+) têm de respeitar `tech-stack.md` §19.5 — sanity check responsivo em 375 / 768 / 1280 px é parte do DoD; "só funciona em desktop" é regressão do princípio `mission.md` §4.6.
- Phases pós-MVP estão deliberadamente menos detalhadas — são revisitadas em Replanning antes de arrancar.

---

## MVP — Módulo Financeiro

Critério de saída do MVP: **utilizador usa o sistema 1 mês completo sem reabrir Excel ou outra app financeira** (ver `mission.md` §6).

### Phase 0 — Setup do scaffold

- [x] Solução .NET 10 (`Sextante.slnx`) com `global.json`, `Directory.Build.props`, `Directory.Packages.props`.
- [x] Estrutura de pastas (`src/Bootstrap/Host`, `src/BuildingBlocks/{SharedKernel,Messaging,Infrastructure}`, `src/Modules/{Identity,Financial}`, `src/Web`).
- [x] Projetos Angular (`Sextante.Web`) com build Angular CLI a copiar para `wwwroot` do Host.
- [x] `Dockerfile` multi-stage (Node → .NET SDK → ASP.NET runtime).
- [x] `docker-compose.yml` com `api` + `postgres` + volumes.
- [x] `appsettings.json` + `.env.example`.
- [x] CI básico (GitHub Actions): build, test, format check.

**Saída**: `docker compose up` localmente serve a landing Angular em `http://localhost/` e `http://localhost/api/health` retorna `200`. Primeiro deploy à VPS é diferido para a Phase 6 (pré-dogfooding).

### Phase 1a — Auth backend + Multi-tenancy 🛡️

> Branch: `phase-1a-auth-backend`. Validação por integration tests + curl, **sem UI**.

- [x] Módulo `Identity` (5 projetos) com schema `shared`.
- [x] `AppUser`, `Tenant`, `Membership`, `Roles`.
- [x] `IdentityDbContext` + migration inicial.
- [x] `MapIdentityApi<AppUser>()` em `/api/auth/...`.
- [x] Custom `POST /api/auth/signup` orquestrando User + Tenant + Membership(Owner) atomicamente.
- [x] `TenantAwareClaimsPrincipalFactory` injetando `tenant_id` e `tenant_role` no JWT.
- [x] `ITenantContext` em `Identity.PublicApi`, com fail-loud se claim falta.
- [x] PostgreSQL Row-Level Security setup (role app sem `BYPASSRLS`, role migrations com).
- [x] `DbConnectionInterceptor` para `SET app.current_tenant_id`.
- [x] **Wolverine** registado no Host como mediator in-process + bus inter-módulos (tech-stack §3.5 e §11).
- [x] **Escrever ADR-010** (CQRS via Wolverine) em `docs/adr/` antes de assentar os primeiros handlers.
- [x] Testes de arquitetura (NetArchTest) a validar regras de dependência.
- [x] **Suite de testes de multi-tenancy** (read/write/delete cross-tenant; insert auto-popula `TenantId`; query sem `TenantContext` lança).

**Saída**: dois utilizadores conseguem registar-se, fazer login, e via integration tests / curl nenhum vê dados do outro (mesmo em endpoints que usem SQL raw, graças a RLS). Refresh token cookie/access token in-memory são exercitados via integration tests; UI fica para 1b.

### Phase 1b — Auth UI (Angular) 🛡️

> Branch: `phase-1b-auth-ui`. Constrói em cima da API estável da Phase 1a.

- [x] Upgrade Angular 19 → **Angular 21 LTS** (com bump de TypeScript / CLI / build-angular alinhados).
- [x] Adicionar **PrimeNG**, **PrimeIcons**, **Chart.js** (peer da PrimeNG Chart) e **Tailwind CSS** (+ `postcss`, `autoprefixer`) ao `package.json`. Configurar tema PrimeNG default + Tailwind preflight a coexistir (tech-stack §19).
- [x] **Escrever ADR-011** (Frontend UI stack: PrimeNG + Tailwind + Signals) em `docs/adr/` antes de adicionar dependências.
- [x] Layout shell com PrimeNG: header (tenant ativo + menu user), sidenav placeholder, toast outlet.
- [x] Páginas `/signup`, `/login`, `/forgot-password`, `/reset-password` com Reactive Forms e validação PT-PT.
- [x] `HttpInterceptor` anexa `Authorization: Bearer <access>` e faz auto-refresh em 401 contra `/api/auth/refresh`.
- [x] Refresh token em **httpOnly cookie** (set pelo backend); access token em memória via Signal.
- [x] Auth guard usa Signal de auth state — redireciona para `/login` se vazio.
- [x] E2E manual: signup → login → request protegida → logout → refresh expirado força re-login (com browser real, não só integration tests).

**Saída**: utilizador completa o fluxo de auth inteiro pelo browser; sessão persiste através de refresh do browser via httpOnly cookie. PrimeNG + Tailwind operacionais como base para Phase 2+.

### Phase 2 — Categorias + Contas + Transações manuais + Dashboard mínimo

- [x] Módulo `Financial` (5 projetos) com schema `financial`.
- [x] Entidades: `Account`, `Category` (1 nível, `Kind: {Expense,Income}` enum, ícone+cor), `Transaction` (com `Tags jsonb` preparado).
- [x] CRUD completo de cada (com **soft-delete em todas**).
- [x] Categorias seed criadas no signup (subscriber de `UserRegisteredIntegrationEvent`).
- [x] Não permitir eliminar categoria com transações associadas (arquivar → soft-delete).
- [x] Dashboard mínimo Angular: lista de transações com filtros (período, categoria, conta) + cards de totais + gráfico donut por categoria.
- [x] OpenAPI auto-gen.

**Saída**: utilizador cria categorias, contas e transações manuais; dashboard lista com filtros.

### Phase 3 — Multi-moeda + ECB

- [x] Entidade `Currency` (seed ISO 4217 + crypto base) e `ExchangeRate` em `shared`.
- [x] `Money` value object em `SharedKernel` + EF Core converter para storage `(decimal, varchar(3))`.
- [x] Integração ECB (`ICurrencyProvider` + `EcbCurrencyProvider`).
- [x] Job Hangfire diário (00:30 UTC) que persiste snapshot na `ExchangeRate`.
- [x] Inserção manual de taxa quando provider falha (UI + endpoint).
- [x] Refactor `Transaction` para carregar `ExchangeRateToPrimary` + `ExchangeRateAt`.
- [x] Dashboard com filtro "ver na moeda original" vs "tudo convertido para moeda principal".

**Saída**: registar transação em USD numa conta EUR persiste o câmbio do momento; dashboard mostra totais consolidados na moeda principal do tenant.

### Phase 4 — Importação CSV + Regras de categorização 🛡️

- [ ] Entidades: `ImportProfile` (mapeamento de colunas reutilizável por banco/cartão), `ImportBatch`, `CategorizationRule`.
- [ ] Endpoint upload CSV + parse + pré-visualização.
- [ ] Deduplicação heurística (data + valor + descrição normalizada → "potencial duplicado").
- [ ] Regras de categorização: matching por Contains / Equals / StartsWith.
- [ ] Ordem de prioridade entre regras (primeira a fazer match ganha).
- [ ] Aplicação de regras durante import + endpoint para re-executar regras sobre transações existentes.
- [ ] Audit: transação categorizada por regra X regista qual.
- [ ] UI Angular: wizard de import (upload → mapping → preview → confirmar).

**Saída**: utilizador importa extrato CSV do banco e ≥80% das transações ficam categorizadas automaticamente por regras.

### Phase 5 — Recorrentes + Hangfire + Metas

- [ ] Entidade `RecurringRule` (Daily / Weekly / Monthly / Yearly + start/end date).
- [ ] Job Hangfire que gera transações nas datas previstas, **wrapped em `TenantAwareJob<T>`**.
- [ ] Preview de futuras na UI.
- [ ] Edição de recorrente: opção "aplicar só a futuras" vs "aplicar a todas pendentes".
- [ ] Entidade `Budget` (meta por categoria + período).
- [ ] Dashboard: % consumido, valor restante, projeção de fim de período.
- [ ] Alertas em 80% e 100% (configurável).
- [ ] Suporte a metas em moeda específica.

**Saída**: utilizador define renda mensal e ela aparece automaticamente no dia 1; tem 3 metas configuradas e o dashboard mostra progresso.

### Phase 6 — Polish + Deploy + Dogfooding 🛡️

- [ ] Export CSV das transações.
- [ ] Banner persistente para confirmar email (não bloqueante).
- [ ] Decidir provider SMTP (VPS direto vs SendGrid/Mailgun) com base em testes reais de spam folder.
- [ ] **Provisionar VPS** (provider, sizing, registo DNS A para o domínio público).
- [ ] **Primeiro deploy production** via `docker compose` na VPS; LettuceEncrypt emite certificado Let's Encrypt na 1ª request HTTPS.
- [ ] **Validação live**: `https://<dominio>/` renderiza a landing, `/api/health` retorna `200`, certificado emitido por Let's Encrypt.
- [ ] README de deploy atualizado (refletir provider, domínio e sizing efetivamente escolhidos).
- [ ] Backups automatizados (`pg_dump` por schema, retenção 30 dias).
- [ ] **1 restore de teste** documentado.
- [ ] **Dogfooding 1 mês**: utilizador importa extrato bancário do último mês e categoriza tudo; recorrentes do mês configuradas; pelo menos 3 metas a mostrar progresso; nenhum reabrir de Excel ou outra app financeira durante 1 mês.

**Saída**: critério de Done do MVP cumprido (`mission.md` §6).

---

## Pós-MVP

Detalhe destas phases é tentativo. Cada uma é revisitada em Replanning antes de arrancar.

### Fase 2 — Módulo de Investimento

#### Phase 7 — Modelo de carteira (Sub-fase 2.1)
Módulo `Investment` com `Portfolio`, `AssetClass`, `Asset`, `Operation` (Buy/Sell/Dividend/Interest/Split), cálculo de `Holding` (preço médio, quantidade total). Pesos-alvo hierárquicos.

#### Phase 8 — Cotações (Sub-fase 2.2) 🛡️
ADR-008 (provider de cotações: Brapi BR + Yahoo Finance/Alpha Vantage intl). Job diário de fecho. Cotação intraday on-demand. `AssetPrice` histórico.

#### Phase 9 — Questionários e scoring (Sub-fase 2.3)
`QuestionnaireTemplate` (Fisher 15, checklist quantitativa). Aplicação a `Asset`. `AssetEvaluation` com histórico. Score influencia peso sugerido.

#### Phase 10 — Sugestão de aporte (Sub-fase 2.4)
Algoritmo de distribuição com base em pesos-alvo + scores. UI de revisão e ajuste. Geração de `Operation` rascunho. Integração com `Financial` via `TransactionRequestedIntegrationEvent`.

#### Phase 11 — Balanceamento e dashboards (Sub-fase 2.5)
Heatmap de desvios. Alertas de desvio. Performance (TWR/MWR) por classe e asset.

### Fase 3 — Integrações e automação

#### Phase 12 — Open Banking 🛡️
Pluggy (BR) + GoCardless / GoCardless Bank Account Data API (EU). Importação automática de extratos.

#### Phase 13 — Importação corretoras 🛡️
XP, Avenue, Degiro, Interactive Brokers.

#### Phase 14 — Categorização ML
Modelo simples treinado no histórico do utilizador.

#### Phase 15 — Notificações
Email + push: alertas de meta, recorrentes geradas, cotações.

### Fase 4 — Relatórios fiscais

#### Phase 16 — Brasil
Relatório anual de proventos. DARF sobre vendas com lucro. Carnê-leão.

#### Phase 17 — Portugal
Anexo J (investimentos no estrangeiro). Mapas para IRS.

### Fase 5 — Multi-utilizador

#### Phase 18 — UI de convites + roles 🛡️
Owner / Member / ReadOnly. Convidar membros para tenant. Filtros "minhas transações vs todas" no dashboard. Casos de uso: casal, contabilista.

### Fase 6 — Mobile

#### Phase 19 — App nativa ou PWA
Flutter / .NET MAUI / PWA "real". Foco em entrada rápida (use case: scan recibo). Notificações push.

### Fase 7 — Plataforma SaaS pública

> Só **se** fizer sentido depois de tudo o resto. Critério: o sistema é genuinamente útil a outros e há sinal de procura.

#### Phase 20 — Landing + planos pagos 🛡️
Landing page, Stripe, planos. Suporte a múltiplos tenants em escala. Monitorização e observability completa (OpenTelemetry → Prometheus + Grafana). Compliance formal (GDPR, LGPD).

---

## "Maybe pile" (sem phase atribuída)

- Cenários "what if" (ex.: simular subida de juros).
- Importação de extrato em PDF (OCR).
- Integração com calendário (recorrentes como eventos).
- Exportação para outras ferramentas (YNAB, Mobills).
- API pública para o utilizador automatizar com Zapier / n8n.
- AI assistente para análise das despesas ("onde gastei mais este mês?").

---

## Notas para o agente

- 🛡️ marca phases que **exigem sub-agent deep review** antes do merge (regra dura 3 do `AGENTS.md`).
- Phases pós-MVP estão **propositadamente sem checklist detalhada** — definir feature spec na altura.
- Quando uma phase for marcada como concluída, atualizar este ficheiro **via conversa com o agente**, nunca à mão.
- Ideias spawn durante uma phase (que não cabem nela) → escrever para `backlog/YYYY-MM-DD-<descricao>.md`.
