# ADR-010 — CQRS via Wolverine

- **Status**: Accepted
- **Data**: 2026-04-26
- **Phase**: 1a (Auth backend + Multi-tenancy)
- **Refs cruzadas**: `specs/tech-stack.md` §3.5, §11; `specs/replanning_2026_04_26.md`

## Contexto

A Phase 0 deixou o Host operacional sem qualquer biblioteca de mediação ou
mensageria. A Phase 1a precisa de:

1. **Mediação in-process** — o endpoint custom `/api/auth/signup` orquestra
   `User + Tenant + Membership` numa transação e tem de despachar comandos /
   eventos entre camadas sem acoplamento direto.
2. **Bus inter-módulos** — o `UserRegisteredIntegrationEvent` é publicado em
   Phase 1a para o módulo `Financial` (Phase 2) consumir e semear categorias
   default. A entrega tem de ser durável e transacional (outbox).
3. **Outbox transacional partilhado com EF Core** — qualquer mensagem
   publicada num handler tem de fazer parte da mesma transação que escreve
   em `IdentityDbContext`.

A questão é: usamos um **mediador** (MediatR) + **bus** (MassTransit/NServiceBus/
Wolverine), ou um único framework que cobre ambos?

## Decisão

**Adoptar Wolverine (`WolverineFx` 5.x) como mediador in-process e bus
inter-módulos simultaneamente.**

Wiring no `Sextante.Host/Program.cs`:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(migrationConnection, schemaName: "messaging");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
    opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Identity.Application.AssemblyMarker).Assembly);
});
```

- Schema `messaging` em PostgreSQL (`messaging.wolverine_outgoing`,
  `messaging.wolverine_incoming`, `messaging.wolverine_node_assignments`, ...).
- Transactional outbox integrado com `IdentityDbContext` via
  `WolverineFx.EntityFrameworkCore`.
- Discovery por assembly: cada módulo aponta para o seu
  `<Module>.Application.AssemblyMarker`.

## Alternativas consideradas

### A. MediatR (in-process) + Wolverine (bus)

- **Prós**: MediatR é a baseline familiar; muito material online; pequeno.
- **Contras**:
  - Duas configurações DI, dois conjuntos de middleware, dois conceitos
    paralelos para o utilizador (`IRequest<T>` vs Wolverine handler).
  - MediatR não partilha outbox com Wolverine — para invariar atomicidade
    entre handler in-process e mensagem inter-módulo é preciso código
    de cola não trivial.
  - Custo cognitivo desproporcionado para um modular monolith de
    single-developer (mission §2).
- **Veredicto**: rejeitado.

### B. Plain Application services (sem mediador)

- **Prós**: zero dependências; máxima clareza para handlers triviais.
- **Contras**:
  - Sem middleware uniforme (logging, transações, validação) — cada handler
    duplica a setup.
  - Eventos inter-módulos ainda precisariam de um bus à parte — então a
    decisão volta a A vs Wolverine só.
  - Quando vier o módulo `Investment` em Phase 4 e os jobs Hangfire em
    Phase 3, o lock-in em "fazer tudo à mão" torna-se uma dívida explícita.
- **Veredicto**: rejeitado.

### C. MassTransit (bus) + plain services (in-process)

- **Prós**: MassTransit é o bus mais maduro do ecossistema .NET; suporte a
  PostgreSQL persistence e EF Core outbox.
- **Contras**:
  - Pesado para mediação in-process (não foi desenhado para isso).
  - Setup mais verboso que Wolverine para o caso `local + remote`.
  - Wolverine ganha em DX para o cenário "monolith + outbox + EF": menos
    attributes, descoberta convencional, menos cerimônia.
- **Veredicto**: viável, mas para Sextante (single-dev, monolith) Wolverine
  ganha em produtividade. Reavaliável se passarmos a múltiplos serviços
  em pós-MVP.

## Consequências

### Positivas

- **Uma única abstração** para mediação + messaging. Os handlers parecem-se
  iguais, sejam invocados localmente ou via outbox.
- **Outbox transacional grátis** — `IDbContextOutbox<T>` ou
  `Policies.AutoApplyTransactions()` enrolla o handler numa tx que cobre
  EF + outgoing messages.
- **Testabilidade** — IMessageBus injetável, handlers são funções estáticas,
  cobertura via Wolverine's TestSupport (`InvokeMessageAndWaitAsync`).
- **Schema próprio** — `messaging.*` separa Wolverine internals de qualquer
  schema de negócio.

### Negativas / mitigações

- **Curva de aprendizagem específica** — convenções de descoberta,
  middleware via `Policies`, sources via `IConfigureWolverineExtension`.
  Mitigação: ADR + leitura conjunta dos docs no replanning.
- **Versão 5.x recente** — risco de regressões. Mitigação: pinning em
  `Directory.Packages.props`; bump deliberado por phase.
- **Schema `messaging` partilhado por todos os módulos** — futuro
  upgrade quebra todos ao mesmo tempo. Aceitável: é a infraestrutura
  partilhada do bus, não do domínio.

## Pendentes

- **Bootstrap do role `sextante_migrations`**: tem de existir antes
  de qualquer migration ou inicialização Wolverine. Phase 1a faz isso
  no `IdentityIntegrationFixture` (testes) e via init script de docker
  compose (produção). Decisão final em ADR de DB roles a futuro.
- **Subscriber do `UserRegisteredIntegrationEvent`** — Phase 2 (módulo
  Financial faz seed de categorias).
