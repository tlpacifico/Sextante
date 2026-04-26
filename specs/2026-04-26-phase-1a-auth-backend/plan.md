# Plan — Phase 1a: Auth backend + Multi-tenancy

> Numerado por grupos de tarefa. Cada sub-tarefa referencia ficheiros
> e decisões concretas (tech-stack §X, requirements.md, roadmap.md).
> Phase 1a é validada **sem UI** — a única superfície externa são os
> endpoints REST + integration tests + curl walkthrough.

---

## 1. Module skeleton (5 projetos Identity + BuildingBlocks)

- 1.1 Criar 5 projetos em `src/Modules/Identity/`:
  - `Sextante.Modules.Identity.Domain/` (zero deps externas)
  - `Sextante.Modules.Identity.Application/`
  - `Sextante.Modules.Identity.Infrastructure/`
  - `Sextante.Modules.Identity.Api/`
  - `Sextante.Modules.Identity.PublicApi/`
  Adicionar todos a `Sextante.slnx`.
- 1.2 References per tabela tech-stack §3.1:
  - `Domain` → `SharedKernel`
  - `Application` → `Domain` + `SharedKernel` + `Identity.PublicApi`
  - `Infrastructure` → `Application` + `Domain` + `SharedKernel` + `Identity.PublicApi` + `Messaging`
  - `Api` → `Application` + `Infrastructure` + `SharedKernel` + `Identity.PublicApi`
  - `PublicApi` → `SharedKernel` only
- 1.3 `BuildingBlocks/SharedKernel`: criar `Sextante.SharedKernel.csproj`
  com `TenantId` value object (`record struct TenantId(Guid Value)`)
  e helper estático `GuidV7.NewId() => Guid.CreateVersion7()`.
  Adicionar à solução.
- 1.4 `BuildingBlocks/Messaging`: criar `Sextante.Messaging.csproj`
  com marker interface `IIntegrationEvent` e abstrações usadas pelos
  contratos publicados em `*.PublicApi`. Adicionar à solução.
- 1.5 `BuildingBlocks/Infrastructure`: deferir — só ganha conteúdo
  quando houver código partilhado entre módulos (provavelmente
  Phase 2). Manter `.gitkeep`.
- 1.6 Atualizar `Directory.Packages.props` com NuGet versions:
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore`,
    `Microsoft.EntityFrameworkCore.Design`,
    `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `Wolverine`, `WolverineFx.PostgreSql`,
    `WolverineFx.EntityFrameworkCore`
  - `Testcontainers.PostgreSql` (test-only)

## 2. Domain layer

- 2.1 `AppUser : IdentityUser<Guid>` em
  `Identity.Domain/Entities/AppUser.cs`. Sem campos custom nesta phase
  (extensões para perfil ficam na Phase 1b ou Phase 6).
- 2.2 `AppRole : IdentityRole<Guid>` em
  `Identity.Domain/Entities/AppRole.cs`. Placeholder; `SystemAdmin`
  (tech-stack §4.5) é populado manualmente; bootstrap automático
  fica fora do scope de 1a.
- 2.3 `Tenant` em `Identity.Domain/Entities/Tenant.cs`: `Id` (Guid v7),
  `Name`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `Version`
  (tech-stack §7.5 audit fields).
- 2.4 `Membership` em `Identity.Domain/Entities/Membership.cs`:
  `Id` (Guid v7), `UserId` (FK `AppUser`), `TenantId` (FK `Tenant`),
  `Role` (enum `MembershipRole { Owner, Member, ReadOnly }`),
  audit fields.
- 2.5 `MembershipRole` enum em
  `Identity.Domain/Enums/MembershipRole.cs`.
- 2.6 `UserRegisteredIntegrationEvent` (record com `UserId`,
  `TenantId`, `Email`, `OccurredAt`) em
  `Identity.PublicApi/Events/UserRegisteredIntegrationEvent.cs`.
  Implementa `IIntegrationEvent` do `Messaging`.
- 2.7 `ITenantContext` em
  `Identity.PublicApi/Abstractions/ITenantContext.cs`: propriedade
  `TenantId TenantId { get; }` com docstring a explicar fail-loud
  quando claim ausente em request autenticada.

## 3. Persistence + RLS (Infrastructure)

- 3.1 `IdentityDbContext : IdentityDbContext<AppUser, AppRole, Guid>`
  em `Identity.Infrastructure/Persistence/IdentityDbContext.cs`.
  `OnModelCreating`: `modelBuilder.HasDefaultSchema("shared")`,
  configurations Fluent API para `Tenant` (tabela `Tenants`) e
  `Membership` (tabela `Memberships`).
- 3.2 `AuditingInterceptor : SaveChangesInterceptor` em
  `Identity.Infrastructure/Persistence/AuditingInterceptor.cs`.
  Popula `CreatedAt` em entidades `EntityState.Added`, `UpdatedAt`
  + incrementa `Version` em entidades `EntityState.Modified`.
  Aplicado a `Tenant` e `Membership`; **não** se aplica a tabelas
  Identity built-in (têm o seu próprio versionamento).
- 3.3 `TenantPopulationInterceptor : SaveChangesInterceptor` que
  popula `TenantId` automaticamente em entidades `ITenantOwned`
  com `EntityState.Added` (tech-stack §4.1). Usa `ITenantContext`
  para resolver o tenant atual. Em 1a só `Membership` é
  tenant-owned; abstração fica pronta para Phase 2+.
- 3.4 EF migration `Initial` cria todas as tabelas `shared.*`
  (Identity built-in + `Tenants` + `Memberships`).
- 3.5 EF migration `EnableRowLevelSecurity` usa
  `MigrationBuilder.Sql(...)` para:
  - `CREATE ROLE sextante_migrations BYPASSRLS;`
  - `CREATE ROLE sextante_app NOBYPASSRLS;`
  - `GRANT` apropriados a cada role nas tabelas `shared.*`.
  - `ALTER TABLE shared."Memberships" ENABLE ROW LEVEL SECURITY;`
  - `CREATE POLICY tenant_isolation ON shared."Memberships"
    USING (tenant_id = current_setting('app.current_tenant_id')::uuid);`
- 3.6 Lock distribuído `__migration_lock` no startup do Host
  (tech-stack §1) — implementação simples via `pg_advisory_lock`
  num `IHostedService` que corre antes do `WebHost`.
- 3.7 Connection strings em `appsettings.json` + `.env.example`:
  - `MIGRATION__CONNECTION_STRING` — conecta como
    `sextante_migrations`. Usada pelo migration runner no startup.
  - `APP__CONNECTION_STRING` — conecta como `sextante_app`. Usada
    pelo `IdentityDbContext` em runtime.

## 4. Tenant context + connection interceptor

- 4.1 `TenantAwareClaimsPrincipalFactory :
  UserClaimsPrincipalFactory<AppUser, AppRole>` em
  `Identity.Infrastructure/Auth/TenantAwareClaimsPrincipalFactory.cs`.
  `GenerateClaimsAsync` carrega memberships do utilizador, ordena
  por priority (`Owner > Member > ReadOnly`) e adiciona claims
  `tenant_id`, `tenant_role` da primeira. Comentário no código
  aponta multi-membership active selection para Phase 18.
- 4.2 `TenantContext : ITenantContext` em
  `Identity.Infrastructure/Auth/TenantContext.cs`. Resolve via
  `IHttpContextAccessor` o claim `tenant_id`. Lança
  `UnauthorizedAccessException("tenant_id claim missing")` se
  ausente em request autenticada (`HttpContext.User.Identity?.IsAuthenticated == true`).
  Endpoints `[AllowAnonymous]` não devem resolver `ITenantContext`.
- 4.3 `TenantConnectionInterceptor : DbConnectionInterceptor` em
  `Identity.Infrastructure/Persistence/TenantConnectionInterceptor.cs`.
  No `ConnectionOpenedAsync`, executa
  `SET app.current_tenant_id = '<guid>'` usando o valor de
  `ITenantContext`. Só corre se o request é autenticado;
  para requests anonymous (signup, login pré-auth), **não** faz `SET`
  e a connection fica sem `app.current_tenant_id` — RLS bloqueia
  qualquer query a tabelas tenant-owned, fail-loud.
- 4.4 Registo DI em
  `Identity.Infrastructure/DependencyInjection.AddIdentityInfrastructure(...)`:
  - `services.AddScoped<ITenantContext, TenantContext>()`
  - `services.AddScoped<TenantConnectionInterceptor>()`
  - `dbContextOptionsBuilder.AddInterceptors<TenantConnectionInterceptor>(...)`
  - `services.AddScoped<TenantPopulationInterceptor>()`
  - `services.AddScoped<AuditingInterceptor>()`
  - Substituir `UserClaimsPrincipalFactory` por
    `TenantAwareClaimsPrincipalFactory`.

## 5. Auth endpoints (mínimos)

- 5.1 `services.AddIdentityCore<AppUser>()` com defaults; **sem**
  override de `IdentityOptions.Password` (decisão em
  requirements.md). Encadear `.AddRoles<AppRole>()`,
  `.AddEntityFrameworkStores<IdentityDbContext>()`,
  `.AddDefaultTokenProviders()`,
  `.AddApiEndpoints()`.
- 5.2 Em `Sextante.Host/Program.cs`,
  `app.MapGroup("/api/auth").MapIdentityApi<AppUser>()`. Endpoints
  email-dependentes (`forgotPassword`, `resetPassword`,
  `confirmEmail`, `resendConfirmationEmail`) ficam mapeados pela
  framework — não suprimir.
- 5.3 `IEmailSender<AppUser>` registado como
  `NotImplementedEmailSender` em
  `Identity.Infrastructure/Email/NotImplementedEmailSender.cs`.
  Cada método lança
  `NotImplementedException("Email sender not configured in Phase 1a; functional in Phase 1b/Phase 6")`.
  Endpoints email-dependentes existem mas falham loudly se invocados
  — alinhado com a escolha "Minimal" em requirements.md.
- 5.4 Custom `POST /api/auth/signup` em
  `Identity.Api/Endpoints/SignupEndpoint.cs`. Recebe
  `{ email: string, password: string, tenantName: string }`.
  Numa única transação (via `IDbContextTransaction` + Wolverine
  outbox integration):
  1. Cria `AppUser` via `UserManager.CreateAsync`.
  2. Cria `Tenant` (Guid v7).
  3. Cria `Membership(Role=Owner)`.
  4. Publica `UserRegisteredIntegrationEvent` via
     `IMessageBus.PublishAsync(...)` — outbox transacional garante
     atomicidade.
  Retorna 201 Created com `{ userId, tenantId }`.
- 5.5 Refresh token: comportamento default do `MapIdentityApi`
  (rotation built-in). Tokens em `shared.AspNetUserTokens`.
  Lifetimes via `services.Configure<IdentityOptions>` ou
  `BearerTokenOptions`: 15 min access, 7 dias refresh
  (tech-stack §6 + §17).
- 5.6 JWT signing key:
  - `JWT__SIGNING_KEY` em `.env.example`.
  - Em `Development`, fallback gera chave persistida em
    `bin/dev-jwt-key.bin` (gitignored, criar entrada no `.gitignore`).
  - Em `Production`, fail-fast no startup se env var ausente.
- 5.7 `ProblemDetails` (RFC 7807) configurado para erros
  (tech-stack §8). Mensagens em PT-PT.

## 6. Wolverine wiring

- 6.1 `Directory.Packages.props` ganha `Wolverine`,
  `WolverineFx.PostgreSql`, `WolverineFx.EntityFrameworkCore`.
- 6.2 Em `Sextante.Host/Program.cs`,
  `builder.Host.UseWolverine(opts => { ... })`:
  - `opts.PersistMessagesWithPostgresql(connectionString, schemaName: "messaging")`
  - `opts.UseEntityFrameworkCoreTransactions()`
  - `opts.Policies.AutoApplyTransactions()`
  - `opts.Discovery.IncludeAssembly(typeof(Identity.Application.AssemblyMarker).Assembly)`
- 6.3 Pipeline middleware em
  `Identity.Application/Middleware/`:
  - `LoggingMiddleware`: enricher Serilog com `tenant_id`,
    `correlation_id`.
  - `TransactionMiddleware`: já fornecido por
    `WolverineFx.EntityFrameworkCore` para handlers que usam
    `IdentityDbContext`.
- 6.4 `Identity.Application` ganha `AssemblyMarker` (classe vazia)
  para descoberta determinística.
- 6.5 Smoke check de outbox: `IMessageBus.PublishAsync(...)` no
  signup grava em `messaging.wolverine_outgoing`. Sem subscriber
  registado, mensagem persiste (validado por integration test em §8).

## 7. Architecture tests (NetArchTest)

- 7.1 Em `Sextante.ArchitectureTests/`, **substituir** o smoke test
  da Phase 0 por testes que codificam tech-stack §3.1.
  Cada teste reporta violações no formato `From → To` com nomes
  dos types ofensivos.
- 7.2 Tests obrigatórios:
  - `Domain_DoesNotReferenceApplication`
  - `Domain_DoesNotReferenceInfrastructure`
  - `Domain_DoesNotReferenceApi`
  - `Domain_DoesNotReferencePublicApi`
  - `Application_DoesNotReferenceInfrastructure`
  - `Application_DoesNotReferenceApi`
  - `Infrastructure_DoesNotReferenceApi`
  - `PublicApi_DoesNotReferenceDomainApplicationInfrastructureApi`
- 7.3 Regra de ouro:
  `Investment_DoesNotReferenceFinancialDomainApplicationInfrastructureApi`
  — passa trivialmente até Investment existir, fica em pé como guard.
- 7.4 Helper `ProjectAssemblies` que carrega todos os assemblies
  por convenção de nome (`Sextante.Modules.<Module>.<Layer>`).

## 8. Multi-tenancy test suite

- 8.1 Criar `tests/Sextante.IntegrationTests/` (xUnit) e adicionar
  à solução. Referências: `Microsoft.AspNetCore.Mvc.Testing`,
  `Testcontainers.PostgreSql`, `xunit`.
- 8.2 `IdentityIntegrationFixture : IAsyncLifetime` que arranca
  `PostgreSqlContainer` (`postgres:16-alpine`), corre migrations
  (incluindo `EnableRowLevelSecurity`), e expõe
  `WebApplicationFactory<Program>` configurado contra esse Postgres.
  Reusable via `IClassFixture<IdentityIntegrationFixture>`.
- 8.3 `MultiTenancy/CrossTenantReadIsolationTests.cs` — Test 1:
  signup A + signup B; consultar memberships como A retorna apenas
  a Membership de A (Global Query Filter cobre).
- 8.4 `MultiTenancy/CrossTenantWriteBlockedTests.cs` — Test 2:
  autenticado como A, tentar update/delete de Membership de B via
  endpoint admin de teste; `SaveChanges` lança ou retorna 0 rows
  affected. Repetir via SQL raw para confirmar que RLS rejeita
  na DB layer (mensagem do Postgres contém `row-level security policy`).
- 8.5 `MultiTenancy/InsertAutoPopulatesTenantIdTests.cs` — Test 3:
  insert de uma `Membership` sem `TenantId` explícito autenticado
  como A; após `SaveChanges`, a row tem `tenant_id` de A
  (`TenantPopulationInterceptor` populou).
- 8.6 `MultiTenancy/QueryWithoutTenantContextThrowsTests.cs` — Test 4:
  resolver `IdentityDbContext` sem autenticar (HttpContext.User
  não autenticado), tentar query a `Memberships` → connection
  interceptor não faz `SET`, RLS rejeita query, exceção propaga.
- 8.7 `MultiTenancy/MigrationsRunAsPrivilegedRoleTests.cs` — Test 5:
  spin up Postgres limpo, aplicar migrations com connection string
  de `sextante_migrations`, depois conectar como `sextante_app` e
  verificar que `pg_has_role('sextante_app', 'sextante_migrations',
  'USAGE')` é false.

## 9. Integration tests (signup → login → cross-tenant)

- 9.1 `Auth/SignupCreatesUserTenantMembershipAtomicallyTests.cs`:
  signup OK → DB tem 1 row em `AspNetUsers`, 1 em `Tenants`,
  1 em `Memberships(Role=Owner)`, 1 mensagem em
  `messaging.wolverine_outgoing` (sem subscriber, fica lá).
- 9.2 `Auth/SignupRollsBackOnFailureTests.cs`: forçar falha no passo 3
  (criar Membership) via interceptor de teste; verificar que
  `AspNetUsers` e `Tenants` ficam vazios (rollback transacional)
  e nenhuma mensagem na outbox.
- 9.3 `Auth/LoginIssuesJwtWithTenantClaimTests.cs`: login pós-signup
  retorna access token; decodificar e verificar claims `sub`,
  `tenant_id`, `tenant_role=Owner`.
- 9.4 `Auth/RefreshTokenRotationTests.cs`: usar refresh token uma
  vez → recebe novo par; tentar usar o refresh token antigo →
  401 Unauthorized.
- 9.5 `Auth/EmailEndpointThrowsInPhase1aTests.cs`: chamar
  `POST /api/auth/forgotPassword` retorna 500 com mensagem de
  `NotImplementedEmailSender`. Documenta a expectativa explícita
  de Phase 1a.

## 10. ADR-010, curl walkthrough, sub-agent review, phase close-out

- 10.1 Criar `docs/adr/ADR-010-cqrs-via-wolverine.md` (a primeira ADR
  no repo — criar diretório se necessário). Secções:
  Status (Accepted, 2026-04-26), Context, Decision, Consequences,
  Alternatives Considered (MediatR + Wolverine; plain Application
  services). Refs cruzadas a tech-stack §3.5 e §11.
- 10.2 Confirmar que `validation.md` § "Manual curl walkthrough"
  está reproduzível: aplicar passo-a-passo num shell com
  `docker compose up` limpo; corrigir comandos se necessário.
- 10.3 Sub-agent deep review (🛡️ obrigatório — roadmap.md):
  invocar `Agent` com prompt focado nos vetores de risco:
  RLS policies (correctness + performance), JWT claims (não vazam
  dados sensíveis), Wolverine outbox (idempotência, retries),
  signup transaction (rollback paths), connection interceptor
  (race conditions). Output anexado ao PR.
- 10.4 Marcar checkboxes da Phase 1a no `specs/roadmap.md` via
  conversa com o agente (AGENTS.md §2 regra 1, nunca à mão).
- 10.5 Skill `changelog` para adicionar entrada `2026-04-26` com
  sumário da Phase 1a.
- 10.6 Commit `Mark phase 1a as complete`. PR
  `phase-1a-auth-backend → main`; merge depois de:
  - CI verde
  - Sub-agent deep review verde
  - Aprovação humana
