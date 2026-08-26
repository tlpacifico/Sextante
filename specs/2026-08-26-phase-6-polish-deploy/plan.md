# Plan — Phase 6: Polish + Deploy + Dogfooding

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros e
> decisões concretas (`requirements.md` D1–D7, `tech-stack.md` §X,
> `roadmap.md`). Phase 6 entrega 5 blocos: (1) export CSV, (2) email
> real + banner, (3) backups + restore de teste, (4) primeiro deploy à
> VPS, (5) CD via GitHub Actions.
>
> **Ordem recomendada**: grupos 1 e 2 são código e podem correr
> enquanto o utilizador prepara DNS/firewall; o grupo 3 tem de estar
> pronto **antes** de haver dados reais na VPS; o grupo 5 só depois de
> o grupo 4 ter validado um deploy manual (não se automatiza um caminho
> que nunca correu à mão).
>
> Phase 6 é 🛡️ no roadmap → sub-agent deep review obrigatório (grupo 6).

---

## 1. Export CSV das transações

### 1.1 Empurrar os filtros client-side para o servidor (D5)
Hoje `src/Web/Sextante.Web/src/app/features/financial/pages/transactions/transactions.page.ts:285`
("Filter locally by kind and description for simplicity") filtra
`kind`, `description` e `amountMin/amountMax` **em memória, só sobre a
página devolvida**. Antes do export:
- Estender `ListTransactionsQuery` em
  `src/Modules/Financial/Sextante.Modules.Financial.Application/Features/Transactions/TransactionsContracts.cs`
  com `TransactionKind? Kind`, `string? DescriptionContains`,
  `decimal? AmountMin`, `decimal? AmountMax`.
- Aplicar no handler correspondente em `TransactionHandlers.cs`
  (`ILIKE` para descrição via EF `EF.Functions.ILike`, comparação sobre
  o `Amount` do value object `Money`).
- Actualizar o binding do `MapGet("")` em
  `src/Modules/Financial/Sextante.Modules.Financial.Api/Endpoints/TransactionsEndpoints.cs`.
- Remover o filtro local do `transactions.page.ts`, passando os
  parâmetros ao serviço; manter a UI igual.
- Teste de integração: filtro por descrição + intervalo de valor
  atravessa mais que uma página (prova que já não é client-side).

### 1.2 `ExportTransactionsQuery` + handler
- Novo contrato em `TransactionsContracts.cs`: mesmos filtros de
  `ListTransactionsQuery`, sem paginação.
- Handler em `TransactionHandlers.cs` devolve as linhas já projectadas
  (join a conta e categoria, `ExchangeRateToPrimary` incluído) —
  streaming/`AsAsyncEnumerable` para não materializar o histórico todo
  em memória.
- Multi-tenancy: nada de SQL raw; o Global Query Filter + RLS aplicam-se
  como em qualquer query (`tech-stack.md` §4).

### 1.3 Serializador CSV
- `TransactionCsvWriter` em `Financial.Infrastructure` (ou
  `Financial.Application` se não precisar de dependências de infra).
- Formato D6: separador `;`, decimal `,`, datas `yyyy-MM-dd`, UTF-8
  **com BOM** (sem BOM o Excel estraga acentos em PT-PT).
- Colunas: `Data;Conta;Categoria;Tipo;Descrição;Valor;Moeda;Câmbio;ValorConvertido;Origem`.
- Escaping: descrições com `;`, `"` ou newline entre aspas duplas.

### 1.4 Endpoint `GET /api/financial/transactions/export`
- No `MapGroup` existente de `TransactionsEndpoints.cs`, com
  `RequireAuthorization()`.
- `Content-Type: text/csv; charset=utf-8` e
  `Content-Disposition: attachment; filename="transacoes-<yyyyMMdd>.csv"`.
- Erros em `ProblemDetails` (herda do `GlobalExceptionHandler` da 5.5).

### 1.5 Botão de export na UI
- No `transactions.page.ts`, botão no header do `data-table-shell`
  (`src/app/shared/ui/data-table-shell`), com os filtros activos.
- Download via `Blob` + `URL.createObjectURL`, com `Authorization`
  header (o access token vive em memória, não em cookie — não dá para
  usar `<a href>` directo).
- Estado de loading + toast de erro; empty state se 0 linhas.
- Sanity responsivo 375 / 768 / 1280 px (`tech-stack.md` §19.5).

### 1.6 Testes
- Integração: export com filtros devolve as mesmas linhas que o list
  equivalente; cross-tenant não vaza (tenant B não vê linhas de A).
- Unit: `TransactionCsvWriter` — escaping, decimal `,`, BOM, transação
  em moeda estrangeira com câmbio.

---

## 2. Email real (SmtpEmailSender + banner)

### 2.1 Escolher e provisionar o relay (manual, decide open question 1)
- Criar conta no relay escolhido (Resend ou Mailgun, D1), verificar o
  domínio (SPF + DKIM no DNS), gerar credenciais SMTP.
- Preencher `SMTP__*` no `.env` da VPS; placeholders no `.env.example`.

### 2.2 `SmtpEmailSender`
- Novo `src/Modules/Identity/Sextante.Modules.Identity.Infrastructure/Email/SmtpEmailSender.cs`
  implementando `IEmailSender<AppUser>` (MailKit — adicionar ao
  `Directory.Packages.props`, versão central).
- `SmtpOptions` com validação `ValidateOnStart` **opcional**: config
  ausente não impede o arranque (D1 / `mission.md` §4.4).
- Templates PT-PT (`tech-stack.md` §16) para confirmação e reset, em
  texto + HTML simples.
- Substituir o registo de `NotImplementedEmailSender` em
  `Identity.Infrastructure/DependencyInjection.cs:88`; **apagar o stub**
  (o comentário do próprio ficheiro aponta a Phase 6 como o momento).

### 2.3 Entrega via Hangfire job (`tech-stack.md` §12)
- `SendEmailJob` em `Identity.Infrastructure`, enfileirado pelo
  `SmtpEmailSender` (o `IEmailSender` só enfileira; o job entrega).
- Retry automático do Hangfire; não é tenant-scoped (email de reset
  precede o contexto de tenant) — **não** usar `TenantAwareJob<T>`, e
  documentar o porquê em comentário para não parecer lapso face a
  `tech-stack.md` §4.6.
- Log estruturado do resultado, com o destino mascarado pelo
  `PiiScrubbingEnricher` existente.

### 2.4 Degradação graciosa
- Sem `SMTP__Host` configurado: job loga `Warning` "email não
  configurado" e termina sem excepção.
- Relay em baixo: excepção fica dentro do job (retry), nunca chega ao
  request do utilizador — `/forgot-password` responde sempre 200
  (também não revela se o email existe).
- Teste: sem configuração SMTP, `POST /api/auth/forgotPassword` → 200 e
  a app continua de pé.

### 2.5 `.env.example` + compose
- Acrescentar bloco `# --- SMTP (email transaccional) ---` com
  `SMTP__HOST`, `SMTP__PORT`, `SMTP__USERNAME`, `SMTP__PASSWORD`,
  `SMTP__FROM` ao `.env.example`.
- Passthrough no `docker-compose.yml` no serviço `api` (sem `:?`
  obrigatório — a ausência tem de ser tolerada).

### 2.6 Banner de confirmação de email (não bloqueante)
- No `src/Web/Sextante.Web/src/app/layout/app-shell.component.ts`,
  banner acima do conteúdo quando `emailConfirmed === false`.
- `GET /api/auth/me` (já existe, Phase 1b) tem de expor
  `emailConfirmed` — verificar e acrescentar se faltar.
- Acção "Reenviar email" → `POST /api/auth/resendConfirmationEmail`
  (endpoint do `MapIdentityApi`, hoje a rebentar no stub) + toast.
- Dispensável por sessão (`sessionStorage`), nunca bloqueia navegação.
- Sanity responsivo 375 / 768 / 1280 px.

### 2.7 Reactivar os fluxos de email na UI
- Confirmar que `/forgot-password` e `/reset-password` (Phase 1b) fazem
  o round-trip com links reais.
- Manter `SignIn.RequireConfirmedEmail = false`
  (`Identity.Infrastructure/DependencyInjection.cs:76`) — ver
  `requirements.md` open question 4.

---

## 3. Backups + restore de teste

> Este grupo tem de fechar **antes** de existirem dados reais na VPS.

### 3.1 Script de backup
- `infra/backup/backup.sh`: `pg_dump` **por schema** (`shared`,
  `financial`, `hangfire`, `messaging`) para
  `/var/backups/sextante/<schema>-<yyyyMMdd-HHmm>.dump` (formato
  custom `-Fc`), via `docker compose exec -T postgres`.
- Retenção 30 dias (`find -mtime +30 -delete`), com log do que apagou.
- Falha ruidosa: exit code ≠ 0 e mensagem em stderr se algum dump vier
  vazio ou o `pg_dump` falhar (backup silenciosamente vazio é pior que
  nenhum).

### 3.2 Agendamento no host
- Timer diário (cron ou systemd timer) fora do horário do job ECB
  (00:30 UTC, `RecurringJobsRegistration.cs`) — sugestão 03:00 UTC.
- Documentar a instalação do timer no README de deploy.

### 3.3 Runbook de restore
- `docs/runbooks/restore.md`: passos para restaurar um schema para uma
  base limpa (`createdb` temporária → `pg_restore` → verificação),
  incluindo a ordem dos schemas e a nota de que os roles
  `sextante_app` / `sextante_migrations` têm de existir no destino
  (`infra/postgres/01-bootstrap-roles.sh`).

### 3.4 Executar o restore de teste (bloqueador, `tech-stack.md` §15)
- Restaurar o dump para uma base limpa, apontar uma instância local a
  essa base, fazer login e confirmar transações/categorias/metas.
- Registar output e data no runbook (§"Restore de teste executado").

---

## 4. Primeiro deploy à VPS

### 4.1 DNS + firewall (manual, D3)
- Registo `A` do domínio → IP público da VPS.
- Portas 80/tcp e 443/tcp abertas (80 é necessária para o HTTP-01
  challenge do LettuceEncrypt).

### 4.2 `.env` de produção na VPS
- `cp .env.example .env` e preencher: `POSTGRES_PASSWORD`,
  `SEXTANTE_MIGRATIONS_PASSWORD`, `SEXTANTE_APP_PASSWORD`,
  `JWT__SIGNING_KEY` (`openssl rand -base64 64`),
  `LETSENCRYPT__EMAIL`, `LETSENCRYPT__DOMAINNAME`, `SENTRY__DSN`,
  `SMTP__*` (grupo 2).
- Confirmar `ASPNETCORE_ENVIRONMENT=Production`.

### 4.3 Build e arranque
- `docker compose build && docker compose up -d`;
  `docker compose logs -f api` até as migrations correrem (lock
  distribuído via `pg_advisory_lock`).

### 4.4 Primeiro utilizador
- `docker compose exec api dotnet Sextante.Host.dll create-admin
  --email <e> --password <p> --tenant-name <t>` (Phase 5.5, idempotente).
- Confirmar que as categorias seed foram criadas (subscriber de
  `UserRegisteredIntegrationEvent`).

### 4.5 Validação live
- `curl -I https://<dominio>/api/health` → 200.
- Landing Angular renderiza em `https://<dominio>/`.
- `openssl s_client ... | openssl x509 -noout -issuer` → Let's Encrypt.
- Hangfire dashboard acessível só a autenticado (verificar que não está
  exposto publicamente).
- Forçar um erro 500 e confirmar que chega ao Sentry com `traceId`
  correlacionável no log do ficheiro (fecha o ciclo da Phase 5.5).

### 4.6 Sanity responsivo real (`mission.md` §4.6)
- Percorrer no **telemóvel real**, no domínio real: login, dashboard,
  `/transactions` (incluindo o export do grupo 1), wizard de import,
  recorrentes, metas.
- Registar defeitos encontrados; corrigir os bloqueantes nesta phase,
  os cosméticos vão para backlog.

### 4.7 Actualizar o README de deploy
- Provider, sizing e domínio efectivos; passos que na prática
  divergiram do runbook; instalação do timer de backup (3.2);
  referência ao runbook de restore (3.3).

---

## 5. CD automático (GitHub Actions)

> Só depois de 4.5 verde — não se automatiza um caminho que nunca
> correu à mão.

### 5.1 ADR-013 — CD automático no MVP (escrever **antes** do código)
- `docs/adr/ADR-013-cd-github-actions.md`, mesmo formato de ADR-010/011/012.
- Context: `README.md` §Deploy declarava CD fora do MVP; o dogfooding
  de 1 mês torna o deploy manual atrito real (D4).
- Decision: workflow de deploy em `main` verde, imagem em registry,
  SSH + `docker compose pull && up -d`.
- Consequences: secrets de produção no GitHub; um push mal revisto
  chega a produção (mitigação: só `main`, e `main` só recebe merges
  revistos); rollback = re-deploy da tag anterior.
- Alternatives: deploy manual (status quo), watchtower (auto-pull sem
  controlo), runner self-hosted na VPS (mais superfície).

### 5.2 Build + push da imagem (decide open question 3)
- Novo job no `.github/workflows/` (ou workflow `deploy.yml` separado,
  `needs: [build, test]` do `ci.yml` existente).
- `docker/build-push-action` → GHCR (`ghcr.io/<owner>/sextante-api`),
  tags `sha-<short>` + `latest`.
- Reutilizar o `Dockerfile` multi-stage existente.

### 5.3 Job de deploy por SSH
- `appleboy/ssh-action` (ou `ssh` puro): `docker compose pull api &&
  docker compose up -d api` no directório do repo na VPS.
- Guardas: só em `push` para `main`, `concurrency` a serializar deploys,
  smoke check pós-deploy (`curl -fsS https://<dominio>/api/health`) que
  falha o job se não responder 200.

### 5.4 Secrets
- `SSH_HOST`, `SSH_USER`, `SSH_KEY` (chave dedicada ao deploy, sem
  passphrase, com acesso restrito), `GHCR_TOKEN` se necessário.
- Documentar no README quais são e como rodá-los; **nunca** no repo.

### 5.5 Alinhar `docker-compose.yml` com imagem de registry
- O serviço `api` hoje faz `build: .` com `image: sextante-api:latest`.
  Para `docker compose pull` funcionar, a imagem tem de apontar ao
  registry — parametrizar via `IMAGE=${SEXTANTE_IMAGE:-sextante-api:latest}`
  mantendo o `build:` para uso local.

### 5.6 Actualizar documentação afectada
- `README.md` §Deploy: remover "CD automático fica fora do MVP",
  descrever o fluxo real.
- `specs/tech-stack.md` §15: bullet CI/CD passa de intenção a
  implementado, com link ao ADR-013; §18 marca ADR-013 como escrito.

---

## 6. Fecho da phase

### 6.1 Changelog
- Entrada com data de hoje em `CHANGELOG.md`, um bullet por commit
  (skill `changelog`).

### 6.2 Sub-agent deep review 🛡️
- Obrigatório antes do merge (regra dura 3 do `AGENTS.md`). Foco:
  multi-tenancy no export (query nova sobre dados de todos os tenants),
  secrets no workflow de CD, e o caminho de degradação do email.

### 6.3 Marcar a phase como completa
- Actualizar `specs/roadmap.md` (checkboxes da Phase 6) **via conversa**,
  commit `Mark phase 6 as complete`, merge para `main`.

### 6.4 Arrancar o dogfooding (gate do MVP, não tarefa)
- Importar o extrato do último mês, categorizar tudo, configurar
  recorrentes e ≥ 3 metas. A partir daí conta o mês de `mission.md` §6.
- Defeitos e irritações encontrados → `backlog/YYYY-MM-DD-<descricao>.md`
  (pasta ainda não existe; criar no primeiro registo).
