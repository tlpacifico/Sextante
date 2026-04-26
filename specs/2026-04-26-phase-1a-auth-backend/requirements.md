# Requirements — Phase 1a: Auth backend + Multi-tenancy

## Goal

Entregar o módulo `Identity` (5 projetos) operacional, com signup
customizado que orquestra `User + Tenant + Membership` atomicamente,
`login`/`refresh`/`logout` via `MapIdentityApi`, claim `tenant_id`
injetada no JWT, e multi-tenancy com defesa em profundidade
(Global Query Filters + Row-Level Security + role separation).
Wolverine fica registado no Host como **mediator in-process e bus
inter-módulos** simultaneamente (tech-stack §3.5 e §11). Phase 1a
é validada **sem UI** — a única superfície externa são os endpoints
REST + integration tests + curl walkthrough. UI Angular vem na Phase 1b.

`tech-stack.md` §4 marca multi-tenancy como invariante; Phase 1a é
onde esse invariante deixa de ser teoria e passa a ser código testado.
`mission.md` §4 princípio 1 ("Privacidade por defeito") só fica
garantido depois desta phase.

## In scope

- Módulo `Identity` em `src/Modules/Identity/` com 5 projetos
  (`Domain`, `Application`, `Infrastructure`, `Api`, `PublicApi`)
  e references conforme tech-stack §3.1.
- `BuildingBlocks/SharedKernel` populado com `TenantId` value object
  e `GuidV7` helper (`Guid.CreateVersion7()` nativo do .NET 10).
- `BuildingBlocks/Messaging` com abstrações Wolverine
  (`IIntegrationEvent` marker).
- Entidades `AppUser`, `Tenant`, `Membership`, `AppRole` com schema
  `shared`, audit fields (`CreatedAt`, `UpdatedAt`, `DeletedAt`,
  `Version`) em `Tenant` e `Membership`.
- `IdentityDbContext` herdando de `IdentityDbContext<AppUser, AppRole, Guid>`
  com schema default `shared`.
- Migration inicial `Initial` + migration `EnableRowLevelSecurity`.
- `MapIdentityApi<AppUser>()` em `/api/auth/...` — endpoints
  **funcionais**: custom `signup`, `login`, `refresh`, `logout`,
  `manage/info`.
- Endpoints email-dependentes (`forgotPassword`, `resetPassword`,
  `confirmEmail`, `resendConfirmationEmail`) **expostos pela framework
  mas não funcionais** — `IEmailSender<AppUser>` é stub
  `NotImplementedEmailSender` que lança. Implementação real em Phase 1b
  ou Phase 6.
- `TenantAwareClaimsPrincipalFactory` injetando `tenant_id` e
  `tenant_role` no JWT (escolhe a Membership Owner por defeito).
- `ITenantContext` em `Identity.PublicApi`, fail-loud quando claim
  ausente.
- Postgres RLS setup: roles `sextante_migrations` (com `BYPASSRLS`)
  e `sextante_app` (sem `BYPASSRLS`), policy `tenant_isolation`,
  criados via migration `EnableRowLevelSecurity`.
- `DbConnectionInterceptor` que executa `SET app.current_tenant_id`
  em cada checkout do pool.
- Wolverine registado no Host como mediator in-process + bus
  inter-módulos (storage PostgreSQL, schema `messaging`, outbox
  transacional integrada com `IdentityDbContext`).
- `UserRegisteredIntegrationEvent` publicada na transação do signup
  (sem subscriber em 1a — fica na outbox até Phase 2).
- `Sextante.ArchitectureTests` com regras NetArchTest que codificam
  a tabela tech-stack §3.1 (incluindo a "regra de ouro": Investment
  não pode referenciar Financial.{Domain,Application,Infrastructure,Api}).
- `Sextante.IntegrationTests` com Testcontainers PostgreSQL e a
  suite multi-tenancy completa (§4.7).
- ADR-010 (CQRS via Wolverine) escrito em `docs/adr/`.
- Manual curl walkthrough em `validation.md`.

## Out of scope

- **UI Angular de auth.** Phase 1b. Nada de páginas signup/login,
  nada de PrimeNG/Tailwind nesta phase.
- **`forgotPassword` / `resetPassword` / `confirmEmail` funcionais.**
  Endpoints mapeados pela framework mas o stub `IEmailSender` lança.
  Implementação real em Phase 1b ou Phase 6 (decisão SMTP-vs-SaaS
  per tech-stack §12).
- **SMTP / SendGrid / Mailgun integration.** Diferido a pré-dogfooding
  (Phase 6) per tech-stack §12.
- **Subscriber do `UserRegisteredIntegrationEvent`.** Phase 2
  (módulo Financial faz seed de categorias default ao receber).
- **`SystemAdmin` bootstrap.** Não criar admin automático em 1a;
  criação manual (insert via psql) documentada em README, decisão
  formal em Phase 6 quando `/api/admin/*` ficar relevante.
- **Convites / multi-membership UI / role switching.** Phase 18.
- **2FA, Passkeys, OAuth providers.** Pós-MVP.
- **Rate limiting / brute force protection.** Phase 6 polish.
- **Email templates / design.** Phase 1b ou Phase 6.
- **Production JWT signing key procurement / rotation.** Phase 6
  (pré-deploy).
- **Hangfire.** Phase 3 (job de snapshot ECB).
- **`Money` value object em SharedKernel.** Phase 3 (multi-moeda).
- **Módulo `Financial` (Account, Category, Transaction).** Phase 2.

## Decisions

- **CQRS via Wolverine, sem `MediatR`.** *Why*: tech-stack §3.5;
  Wolverine cobre mediação in-process + messaging com a mesma infra.
  **How to apply**: registar Wolverine no Host com handler discovery
  em `Identity.Application`; **não** adicionar `MediatR` ao
  `Directory.Packages.props`.

- **Endpoint surface minimal.** Custom `signup` + MapIdentityApi
  para `login`/`refresh`/`logout`/`manage/info` ficam funcionais;
  endpoints email-dependentes ficam mapeados pela framework mas
  com `IEmailSender` stub. *Why*: limitar superfície de review da
  Phase 1a; emails dependem de provider concreto que tech-stack §12
  difere a Phase 6. **How to apply**:
  `services.AddSingleton<IEmailSender<AppUser>, NotImplementedEmailSender>()`.

- **Publicar `UserRegisteredIntegrationEvent` no signup mesmo sem
  subscriber.** *Why*: validar plumbing Wolverine outbox end-to-end
  agora; quando Phase 2 adicionar Financial subscriber, a infra já
  está provada — o evento sai naturalmente da outbox sem qualquer
  alteração no Identity. **How to apply**: chamar
  `messageBus.PublishAsync(...)` dentro da transação do signup;
  integration test verifica que a mensagem aparece em
  `messaging.wolverine_outgoing` e nunca é entregue (sem subscriber).

- **JWT signing key via env var.** *Why*: separa concerns dev/prod,
  evita check-in de secrets, mantém a phase deployable.
  **How to apply**: `JWT__SIGNING_KEY` em `.env.example`. Em
  Development, key gerada e persistida em `bin/dev-jwt-key.bin`
  (gitignored); fallback automático se env var ausente em
  `Development`. Em `Production`, fail-fast se env var ausente.

- **Identity default password policy.** *Why*: pré-otimização de
  policy stricter sem feedback de uso real é arbitrária; defaults
  ASP.NET Core Identity (≥8 chars, dígito + minúscula + maiúscula
  + não-alfanumérico) são razoáveis para o MVP. **How to apply**:
  não override `IdentityOptions.Password` em 1a.

- **Role separation via EF migration `EnableRowLevelSecurity`.**
  *Why*: tech-stack §4.2 obriga separação de roles; o **mecanismo**
  (SQL script vs migration vs Docker init) ficou em aberto no
  replanning de 2026-04-26. EF migration é a opção escolhida porque
  já temos lock distribuído de migration na Phase 0 e o histórico
  de migrations dá versionamento natural. **How to apply**: a
  migration usa `MigrationBuilder.Sql(...)` para criar roles +
  policies; documentado no Vault e referenciado em ADR-010.

- **`IdentityDbContext` com schema default `shared`.** *Why*: alinha
  com tech-stack §5 (schema `shared` para Identity + cross-cutting)
  e facilita upgrade futuro (Phase 18 multi-membership).
  **How to apply**: `modelBuilder.HasDefaultSchema("shared")` no
  `OnModelCreating`.

- **Multi-membership: signup cria 1 Membership(Owner); claims
  escolhem Owner por defeito.** *Why*: mission §2 single-user MVP;
  multi-membership UI e selection ativa de tenant é Phase 18.
  **How to apply**: `TenantAwareClaimsPrincipalFactory` ordena
  memberships por role priority (Owner > Member > ReadOnly) e usa
  a primeira; comentário no código aponta para Phase 18.

## Context / references

- `specs/roadmap.md` — secção "Phase 1a — Auth backend + Multi-tenancy".
- `specs/tech-stack.md` — §3 (Modular Monolith), §3.5 (CQRS via
  Wolverine), §4 (Multi-tenancy), §5 (Persistência), §6 (Auth),
  §11 (Wolverine), §17 (decisões), §18 (ADR-010 pendente).
- `specs/mission.md` — §2 (audiência single-user MVP), §4 princípio 1
  (privacidade por defeito), §4 princípio 4 (self-hosted-friendly:
  degradação graciosa do email integration).
- `specs/2026-04-25-phase-0-scaffold/` — Phase 0 entregou Host vazio
  + Postgres no compose; Phase 1a é a primeira phase a tocar a BD.
- Memória do agente: `replanning_2026_04_26.md` — decisão Wolverine-
  for-both e split de Phase 1.

## Open questions

- **Mecanismo final para criar roles `sextante_app` /
  `sextante_migrations`.** Phase 1a usa EF migration; reavaliar para
  Docker init script se houver fricção em testes (Testcontainers
  precisa dos roles antes da app arrancar). Decisão final fica em
  ADR-010 ou ADR de DB roles a futuro.
- **`SystemAdmin` role bootstrap.** Quem cria o primeiro admin?
  Migration? Comando CLI? Documentação manual? Phase 6 quando o tema
  admin (Hangfire dashboard, `/api/admin/*`) ficar relevante.
- **Refresh token revocation list.** Se o utilizador faz logout num
  device, o refresh token deve ficar inválido. Identity built-in
  faz isso? Confirmar comportamento durante implementação; se não
  fizer, abrir backlog item.
- **Rate limiting em `/api/auth/signup`.** Sem rate limiting, signup
  é DoS-able. Mission §2 diz single-user MVP, mas tornar `/signup`
  invite-only ou adicionar rate limiting básico pode ser tema da
  Phase 1b ou Phase 6.
