# Requirements — Phase 6: Polish + Deploy + Dogfooding

## Goal

Fechar o MVP: levar o Sextante da bancada de desenvolvimento para a
VPS própria com HTTPS válido, backups verificados e os últimos dois
buracos funcionais do módulo financeiro tapados (export CSV e envio
real de email), de forma a que o utilizador possa cumprir o critério
binário de `mission.md` §6 — **1 mês completo sem reabrir Excel ou
outra app financeira**.

Phase 6 não adiciona módulos novos. Entrega quatro blocos:

1. **Export CSV** das transações — fecha o último item de MVP scope do
   `README.md` e concretiza a portabilidade dos dados.
2. **Email real** — substitui o `NotImplementedEmailSender` por entrega
   via relay SMTP, resolvendo o TBD deixado em `tech-stack.md` §12
   ("decidir antes do dogfooding") e desbloqueando `/forgot-password`.
3. **Deploy production + backups** — primeiro deploy à VPS (diferido
   desde a Phase 0 por decisão explícita em `tech-stack.md` §15),
   LettuceEncrypt, `pg_dump` por schema com retenção 30 dias, e **1
   restore de teste** — bloqueador declarado em §15 e mitigação do
   risco "VPS down sem backup" do `README.md`.
4. **CD automático** via GitHub Actions — build → push de imagem →
   SSH + `docker compose pull && up -d`.

Esta phase concretiza:

- `mission.md` §4.4 (self-hosted-friendly → degradação graciosa se o
  relay SMTP falhar; o signup nunca rebenta por causa de email),
- `mission.md` §4.6 (UI responsiva → sanity check no domínio real, em
  telemóvel real, não só em DevTools),
- `mission.md` §6 (métrica de sucesso → dogfooding arranca no fim
  desta phase),
- `tech-stack.md` §12 (SmtpEmailSender + entrega via Hangfire job),
- `tech-stack.md` §15 (Docker Compose, LettuceEncrypt, backups,
  restore de teste, CI/CD).

Phase 6 é 🛡️ no roadmap → sub-agent deep review obrigatório antes do
merge.

## In scope

### 1. Export CSV das transações

- Endpoint de export no grupo `/api/financial/transactions`, com os
  **mesmos filtros da UI**.
- **Pré-requisito descoberto**: `transactions.page.ts:285` filtra
  `kind`, `description` e `amountMin/Max` **client-side** sobre a
  página devolvida; `ListTransactionsQuery` só aceita
  `DateFrom/DateTo/CategoryIds/AccountIds/RecurringRuleId`. Um export
  fiel ao que o utilizador vê no ecrã obriga a **empurrar esses três
  filtros para o servidor** antes de escrever o export. Isto corrige
  também um bug latente: hoje os filtros client-side só se aplicam à
  página corrente, não ao conjunto todo.
- Colunas do CSV: data, conta, categoria, tipo, descrição, valor,
  moeda original, câmbio aplicado, valor convertido na moeda primária,
  origem (manual / import / regra / recorrente).
- Formato PT-PT-friendly (separador `;`, decimal `,`) para abrir no
  Excel sem wizard de importação — coerente com os CSV que o próprio
  wizard de import já consome.
- Botão no `transactions.page.ts` reusando `data-table-shell`.

### 2. Email real (SmtpEmailSender + banner)

- `SmtpEmailSender : IEmailSender<AppUser>` em `Identity.Infrastructure`,
  a substituir `NotImplementedEmailSender`.
- **Entrega via Hangfire job** (`tech-stack.md` §12) — não bloqueia o
  request, retry automático.
- Configuração por `.env`: `SMTP__Host`, `SMTP__Port`, `SMTP__Username`,
  `SMTP__Password`, `SMTP__From`.
- **Degradação graciosa**: sem configuração SMTP a app arranca e
  funciona; a tentativa de envio loga `Warning` e não propaga exceção
  para o request do utilizador.
- **Sem PII em log**: o endereço de destino é mascarado pelo
  `PiiScrubbingEnricher` já existente.
- Banner **não bloqueante** de confirmação de email no
  `app-shell.component.ts`, dispensável e com acção "reenviar".
- `/forgot-password` e reset passam a funcionar end-to-end pela UI.

### 3. Deploy production + backups

- DNS `A` record, firewall 80/443, `.env` de produção na VPS.
- `docker compose build && up -d`; LettuceEncrypt emite o certificado
  na primeira request HTTPS.
- Primeiro utilizador via `create-admin` (já entregue na Phase 5.5).
- Script de backup `pg_dump` **por schema** (`shared`, `financial`,
  `hangfire`, `messaging`) com retenção 30 dias, agendado no host.
- **1 restore de teste** para base limpa, documentado num runbook.
- Sanity check responsivo no domínio real, em telemóvel real
  (`mission.md` §4.6).
- `README.md` de deploy actualizado com provider, domínio e sizing
  efectivos.

### 4. CD automático (GitHub Actions)

- Job de deploy no `.github/workflows/` a correr só em `main` verde:
  build da imagem → push para registry → SSH para a VPS →
  `docker compose pull && up -d`.
- Secrets em GitHub Actions (chave SSH, host, registry token).
- **Divergência documentada**: o `README.md` §Deploy afirma "CD
  automático fica fora do MVP". Como a decisão foi revertida, exige
  ADR (convenção do projecto: decisão estrutural → ADR antes do
  código) e actualização do README + `tech-stack.md` §15.

## Out of scope

- **Upload de source maps do Sentry no release** — considerado e
  deixado de fora; a nota da Phase 5.5 fica em aberto para o
  Replanning pós-MVP.
- **Métricas / OpenTelemetry / Prometheus / Grafana** — `tech-stack.md`
  §13 di-lo pós-MVP (Fase 7).
- **Módulo de investimento** e tudo o que `README.md` §"Fora do MVP"
  lista.
- **O mês de dogfooding em si** — não é tarefa de código nem
  bloqueador de merge; é o gate do MVP que arranca *depois* desta
  phase fechar (ver `validation.md` §"Fora do scope de validação").
- **Reverse proxy, multi-domínio, staging environment** — 1 VPS, 1
  domínio, um ambiente.

## Decisions

| # | Decisão | Porquê |
|---|---------|--------|
| D1 | **Relay SMTP externo em free tier** (Resend ou Mailgun) em vez de Postfix na VPS | O email só serve reset de password e confirmação de 1 utilizador; não vale gerir reputação de IP, SPF/DKIM/DMARC e PTR. A dependência externa é aceitável porque tem degradação graciosa (`mission.md` §4.4). |
| D2 | Provider concreto **abstraído atrás de `IEmailSender<AppUser>` + SMTP genérico** | Trocar Resend↔Mailgun↔Postfix passa a ser mudar 5 variáveis no `.env`, sem tocar em código. |
| D3 | **VPS e domínio já existem** — não há tarefas de provisionamento nem de escolha de provider | O utilizador já os tem; o plano assume o alvo e trata só de DNS, firewall, `.env` e deploy. |
| D4 | **CD automático entra no MVP**, revertendo o "fora do MVP" do README | Com deploy manual, cada correcção durante o dogfooding custa uma sessão de SSH; o atrito ia travar as iterações no mês que mais importa. |
| D5 | Filtros `kind` / `description` / `amount` **empurrados para o servidor** antes de escrever o export | Sem isto o export não pode ser fiel ao ecrã, e o filtro client-side actual já está errado para além da página corrente. |
| D6 | CSV de export com `;` e decimal `,` | Abre directamente no Excel em locale PT-PT e é simétrico ao formato que o wizard de import já lê (ex.: `example-activo-bank.csv`). |
| D7 | Validação de merge apoiada em **live check + restore de teste**; export round-trip e email na inbox verificados mas **não bloqueadores** | Os dois primeiros são catastróficos se falharem (sistema inacessível, dados perdidos); os outros dois degradam a experiência sem pôr dados em risco. |

## Context / references

- **Roadmap anchor**: `specs/roadmap.md` → "Phase 6 — Polish + Deploy
  + Dogfooding 🛡️" (última phase do MVP).
- **Phases entregues em que isto assenta**: 5.5 (observability,
  ProblemDetails, design system, CLI `create-admin`), 5a/5b
  (recorrentes + metas), 4 (import CSV), 3 (multi-moeda), 2
  (financial core), 1a/1b (auth + multi-tenancy).
- **Runbook existente**: `README.md` §"Deploy (manual, primeira vez)"
  e §"Provisionamento inicial (admin CLI)" — esta phase executa-o pela
  primeira vez a sério e corrige-o com o que se aprender.
- **Stub a substituir**:
  `src/Modules/Identity/Sextante.Modules.Identity.Infrastructure/Email/NotImplementedEmailSender.cs`.
- **Ponto de partida do export**:
  `src/Modules/Financial/Sextante.Modules.Financial.Api/Endpoints/TransactionsEndpoints.cs`
  + `.../Application/Features/Transactions/TransactionsContracts.cs`.

## Open questions

1. **Resend ou Mailgun?** D1 fixa a categoria, não o fornecedor.
   Decidir na tarefa 2.1, antes de escrever o `SmtpEmailSender`.
2. **Domínio e sizing reais** — necessários para o `.env` de produção
   e para actualizar o README (tarefa 4.7).
3. **Registry da imagem no CD** — GHCR (integrado, free para repo
   privado) vs build directo na VPS (sem registry, mas ocupa CPU/RAM
   da VPS no build). Decidir na tarefa 5.2.
4. **`SignIn.RequireConfirmedEmail` continua `false`?** Com email a
   funcionar passa a ser possível exigir confirmação. Recomendação:
   manter `false` durante o dogfooding (o banner chega) e revisitar no
   Replanning — passar a `true` com 1 utilizador só cria risco de
   auto-lockout.
5. **Retenção dos backups fora da VPS** — 30 dias de `pg_dump` no
   mesmo disco não protege contra perda da VPS. Cópia off-site fica
   fora do scope declarado; registar como candidato a backlog.
