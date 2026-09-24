# Roadmap

> Sequência de phases pequenas, cada uma implementada com a sua própria feature spec. Documento vivo: revisitar em Replanning entre phases.

---

## Convenções

- Cada **phase** tem **1–3 features** (nano-fases).
- Cada phase corresponde a 1 branch `phase-N-<kebab-name>` e termina com merge para `main`.
- **Definition of Done por phase**: testes a passar (incluindo multi-tenancy desde Phase 1), code review (sub-agent deep review obrigatório nas phases marcadas com 🛡️), changelog atualizado, commit `Mark phase N as complete`.
- **Phases que tocam UI** (1b, 2, 3 (filtro), 4 (wizard), 5 (recorrentes/metas), 5.5 (design system), 6 (polish), 7+) têm de respeitar `tech-stack.md` §19.5 — sanity check responsivo em 375 / 768 / 1280 px é parte do DoD; "só funciona em desktop" é regressão do princípio `mission.md` §4.6.
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

- [x] Entidades: `ImportProfile` (mapeamento de colunas reutilizável por banco/cartão), `ImportBatch`, `CategorizationRule`.
- [x] Endpoint upload CSV + parse + pré-visualização.
- [x] Deduplicação heurística (data + valor + descrição normalizada → "potencial duplicado").
- [x] Regras de categorização: matching por Contains / Equals / StartsWith.
- [x] Ordem de prioridade entre regras (primeira a fazer match ganha).
- [x] Aplicação de regras durante import + endpoint para re-executar regras sobre transações existentes.
- [x] Audit: transação categorizada por regra X regista qual.
- [x] UI Angular: wizard de import (upload → mapping → preview → confirmar).

**Saída**: utilizador importa extrato CSV do banco e ≥80% das transações ficam categorizadas automaticamente por regras.

### Phase 5 — Recorrentes + Hangfire + Metas

> Dividida durante kickoff em **Phase 5a (recurrings + Hangfire)** e
> **Phase 5b (Budget + alertas)**. Phase 5a fechada em 2026-05-02.
> Phase 5b fechada em 2026-05-02.

- [x] **(5a)** Entidade `RecurringRule` (Daily / Weekly / Monthly / Yearly + start/end date).
- [x] **(5a)** Job Hangfire que gera transações nas datas previstas, **wrapped em `TenantAwareJob<T>`**.
- [x] **(5a)** Preview de futuras na UI.
- [x] **(5a)** Edição de recorrente afeta só ocorrências futuras (a partir de `NextOccurrence`); ocorrências já materializadas ficam imutáveis e editáveis individualmente via Phase 2 CRUD. Semântica "aplicar a todas pendentes" deferida (backlog se vier a ser necessário).
- [x] **(5b)** Entidade `Budget` (meta por categoria + período).
- [x] **(5b)** Dashboard: % consumido, valor restante, projeção de fim de período.
- [x] **(5b)** Alertas em 80% e 100% (configurável).
- [x] **(5b)** Suporte a metas em moeda específica.

**Saída**: utilizador define renda mensal e ela aparece automaticamente no dia 1; tem 3 metas configuradas e o dashboard mostra progresso.

### Phase 5.5 — Refinement: observability, middlewares e design system 🛡️

> Branch: `phase-5.5-refinement`. Hardening estrutural antes do deploy +
> bugs prioritários e UX crítica encontrados no dogfooding inicial. Fecha
> lacunas entre `tech-stack.md` e implementação corrente das Phases 1a–5.

#### Backend — observability e middlewares

- [x] **Filtros PII no Serilog** em `appsettings.json` (mascarar emails, valores monetários, descrições livres em texto). Tech-stack §13.
- [x] **Correlation/traceId** propagado via `Activity.Current.TraceId` e injetado em todos os logs de request via `LogContext.PushProperty`.
- [x] **`TenantId` enricher** automático em todos os logs de request autenticada. Tech-stack §13 + AGENTS.md §4.
- [x] **Alerta de mudança inesperada de tenant** no mesmo request (log `Warning` ou `Error` se `ITenantContext.TenantId` mudar entre middleware e handler). Tech-stack §13.
- [x] **`IExceptionHandler` global** com `ProblemDetails` (RFC 7807) preenchendo `type`, `title`, `status`, `detail`, `instance`, `traceId`. Mensagens PT-PT. Tech-stack §8 + §16.
- [x] **Security headers middleware** (`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`) e CORS explícito (mesmo que same-origin no MVP, registar a política).
- [x] **Wolverine validation policy** centralizada via FluentValidation (handler que valide o command antes de despachar; falha → `ValidationException` → ProblemDetails 400). Tech-stack §3.5.
- [x] **Wolverine metrics policy** mínima: contador de invocações + duração por handler, exposta via `ILogger` no momento (sem Prometheus — fica para Phase 7). Tech-stack §3.5.

#### Sentry — error tracking externo

- [x] **Criar projeto/conta Sentry** (free tier, projetos separados para `sextante-api` e `sextante-web`).
- [x] **Escrever ADR-012 — Sentry como error tracker externo** em `docs/adr/`, justificando a divergência face a "Métricas/OTel pós-MVP" do tech-stack §13. Documentar PII scrubbing, sample rate, retenção.
- [x] Atualizar `specs/tech-stack.md` §13 e tabela §17 a refletir Sentry como decisão MVP.
- [x] **Backend**: integrar `Sentry.AspNetCore` + `Sentry.Serilog` em `Sextante.Host`. DSN via `.env` (`SENTRY__DSN`). Scrubbing de PII alinhado aos filtros Serilog. Tag `tenant_id` em cada evento.
- [x] **Frontend**: integrar `@sentry/angular` em `Sextante.Web`. DSN via build env. Source maps publicados no release. Privacy mode (mascarar inputs financeiros).
- [x] **Fallback graceful**: se Sentry estiver inacessível ou DSN ausente, app continua a funcionar e Serilog file sink mantém o registo local. Mission §4.4 (self-hosted-friendly).

#### Frontend — design system e responsividade

- [x] **Design tokens consolidados**: paleta, espaçamento, tipografia em `src/styles/tokens.css` (CSS custom properties) consumidas via Tailwind theme extend e PrimeNG CSS variables.
- [x] **Componentes shared** extraídos para `src/app/shared/ui/`: `page-header`, `empty-state`, `confirm-dialog`, `data-table-shell`, `form-field`. Substituir duplicação ad-hoc nas pages existentes.
- [x] **Tema PrimeNG Aura customizado** (cores primárias/secundárias do Sextante, dark mode opcional). Documentar em `Vault: 04 - Arquitetura - Frontend.md`.
- [x] **Sanity check responsivo retroactivo**: percorrer todas as pages das Phases 1b–5 (login, signup, dashboard, accounts, categories, transactions, recurring rules, budgets, import wizard) em DevTools nas viewports 375 × 667, 768 × 1024, 1280 × 800. Corrigir overflow horizontal, dialogs cortados, gráficos ilegíveis, touch targets < 44 px. Tech-stack §19.5 + mission §4.6.
- [x] **Checklist DoD responsivo** adicionado a `AGENTS.md` (ou `tech-stack.md` §19.5) como passo obrigatório no merge de qualquer phase futura que toque UI.

#### Bugs prioritários e UX crítica

- [x] **Bug de importação — inferir tipo pelo sinal do valor**: ao importar `tests/Sextante.IntegrationTests/Data/Import/example-activo-bank.csv` (Activo Bank, formato `Data Lanc.;Data Valor;Descrição;Valor;Saldo` com `;` e decimal `,`), todas as linhas são classificadas como despesa. Corrigir o parser/staging para inferir o tipo a partir do sinal de `Valor`: `> 0` → receita, `< 0` → despesa (guardar `Amount` em valor absoluto + `TransactionType`). Adicionar caso de teste de integração com este CSV cobrindo o mix receita/despesa.
- [x] **Ecrã dedicado de transações com filtros e edição inline**: a lista actual no dashboard só permite apagar (ícone caixote) — não há forma de editar data, conta, categoria, descrição ou valor de uma transação já existente. Criar página `/transactions` (rota dedicada) com: (1) tabela paginada com todas as transações do tenant; (2) filtros por intervalo de datas, conta, categoria, tipo (receita/despesa), texto livre na descrição, intervalo de valor; (3) ordenação por qualquer coluna; (4) edição via dialog (PrimeNG `Dialog`) ou inline (`p-table` editing mode) — todos os campos editáveis excepto `Id`/`TenantId`; (5) bulk actions (recategorizar várias transações de uma vez, útil pós-importação); (6) reusa o mesmo `data-table-shell` do design system. Sanity check responsivo 375/768/1280 px.
- [x] **Lembrar utilizador no login**: persistir email no `localStorage` (preencher automaticamente no próximo acesso) e checkbox opcional "Manter-me ligado" que estende a duração do refresh token. Hoje o utilizador tem de digitar email + palavra-passe a cada sessão.
- [x] **Persistir sessão entre reloads (F5)**: hoje o refresh do browser perde o estado em memória e o `AuthGuard` redireciona para `/login`. Persistir tokens (access + refresh) em `localStorage`/`sessionStorage` com rehidratação no bootstrap do Angular e refresh silencioso se o access token estiver expirado mas o refresh ainda for válido. Sessão só termina em logout explícito ou expiração total do refresh token.
- [x] **Provisionamento de utilizador administrador via script/CLI**: hoje não existe forma de criar um utilizador com privilégios elevados sem manipular o Postgres directamente. Adicionar um comando CLI ao `Sextante.Host` (ex.: `dotnet run --project src/Bootstrap/Sextante.Host -- create-admin --email <e> --password <p>`) que: (1) cria `User` + `Tenant` + `Membership` com `Role = Admin`; (2) marca o email como já confirmado; (3) é idempotente (se já existir, faz reset da palavra-passe ou erro explícito). Documentar no README (secção "Provisionamento inicial") e usar este caminho para o primeiro acesso à VPS no Phase 6. Preparação para a UI de gestão de roles (Phase 18) — o role `Admin` já fica modelado.

**Saída**: (1) qualquer erro 4xx/5xx do backend chega ao frontend como `ProblemDetails` com `traceId` rastreável em logs Serilog **e** no Sentry; (2) logs de produção não contêm emails nem valores em texto claro; (3) todas as pages do MVP passam sanity check em 3 viewports sem regressões; (4) ADR-012 escrito e tech-stack atualizado a refletir Sentry como integração MVP; (5) importação de extrato Activo Bank classifica receitas/despesas correctamente; (6) utilizador edita transações existentes através de ecrã dedicado com filtros; (7) sessão persiste entre reloads do browser e termina apenas em logout; (8) comando CLI cria utilizador admin sem acesso directo à base de dados.

### Phase 6 — Polish + Deploy + Dogfooding 🛡️

> Branch: `phase-6-polish-deploy`. Spec em
> `specs/2026-08-26-phase-6-polish-deploy/`. CD automático entrou no
> scope em kickoff (reverte o "fora do MVP" do README — ADR-013).

- [x] Export CSV das transações.
- [x] Banner persistente para confirmar email (não bloqueante).
- [x] Decidir provider SMTP: relay externo em free tier (Resend/Mailgun), atrás de SMTP genérico. Teste real de spam folder fica para a tarefa 2.1 (credenciais) — a decisão de arquitectura está fechada.
- [~] **Provisionar VPS**: resolvido por reutilização — a VPS já existe e corre o `oui-system` e o `binance-bot`; o Sextante entra na porta `8090` (loopback) com Postgres em container próprio. Falta **registar o domínio** e criar o registo DNS A para o IP da VPS.
- [ ] **Primeiro deploy production** via `docker compose` na VPS. O TLS é do **Caddy** (systemd, partilhado), não do LettuceEncrypt — vhost em `deploy/Caddyfile.sextante`, a aplicar quando o domínio existir.
- [ ] **Validação live**: `https://<dominio>/` renderiza a landing, `/api/health` retorna `200`, certificado emitido por Let's Encrypt. (Antes do domínio: `curl http://127.0.0.1:8090/api/health` na VPS.)
- [x] README de deploy atualizado (VPS partilhada, Caddy, tabela de GitHub Secrets, rollback, recuperação manual).
- [~] Backups automatizados (`pg_dump` por schema, retenção 30 dias): script `infra/backup/backup.sh` escrito e verificado localmente; unidades `sextante-backup.{service,timer}` (03:00 UTC) escritas; falta instalá-las na VPS.
- [ ] **1 restore de teste** documentado (com dumps reais da VPS; o ensaio local de 2026-09-01 está registado no runbook mas não substitui este).
- [~] **CD automático** via GitHub Actions (build → push GHCR → SSH `docker compose pull && up -d` + smoke check) + **ADR-013**: workflow `.github/workflows/deploy.yml` escrito; falta criar os Secrets, pôr o `deployuser` no grupo `docker` e correr o primeiro deploy.
- [→] ~~Dogfooding 1 mês~~ — movido para o fim da Phase 6.5 (replanning 2026-09-23).

**Saída**: Sextante em produção na VPS, com backups verificados e CD automático.

### Phase 6.5 — Transferências entre contas + cartão de crédito 🛡️

> Inserida em replanning (2026-09-23, branch `replanning`). Spec em
> `specs/2026-09-23-phase-6.5-transfers-credit-card/`. Motivo: o
> primeiro arranque do dogfooding com dados reais (conta à ordem +
> cartão) mostrou que o sistema não representa transferências, não
> mostra saldo atual e não aceita dívida de cartão — o pagamento do
> cartão contava duas vezes como despesa e nenhum saldo era
> verificável contra o banco.

- [x] **ADR-014** (transferências e saldo de conta) + dívida técnica descoberta: RLS em falta em `categorization_rules` / `import_profiles` / `import_batches`, reaplicar regras limitado a 100, import sem eventos, totais a perder categorias arquivadas.
- [x] `Transaction` com direção explícita (`Inflow`/`Outflow`) e tipo (`Regular`/`Transfer`/`Adjustment`); categoria nullable; totais, donut e orçamentos só com `Regular`.
- [x] Saldo atual por conta (e saldo à data) + `OpeningBalanceDate`; saldo inicial negativo em cartões de crédito.
- [x] Transferências entre contas (par de transações ligadas), incluindo multi-moeda e "marcar como transferência".
- [x] Acerto de saldo (reconciliação com o banco) fora dos totais.
- [x] Definições e vista do cartão: limite, dia de fecho, dia de pagamento, ciclo corrente, próximo pagamento.
- [x] Compras em prestações (`InstallmentPlan`) com calendário e previsão de pagamento.
- [x] Import: conta escolhida no wizard; regras com ação "transferência para conta X"; ligação à contraperna já existente (sem duplicar); linhas anteriores ao saldo inicial excluídas; dedup por conta.
- [ ] **Dogfooding 1 mês**: utilizador importa extrato bancário do último mês e categoriza tudo; recorrentes do mês configuradas; pelo menos 3 metas a mostrar progresso; nenhum reabrir de Excel ou outra app financeira durante 1 mês. _Arranque previsto após o merge da Phase 6.5 (2026-09-24); falta fechar o acerto do cartão com o saldo real (validation 5)._

**Saída**: saldos da conta à ordem e do cartão batem ao cêntimo com o banco; pagamento do cartão não aparece como despesa; critério de Done do MVP cumprido (`mission.md` §6).

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
