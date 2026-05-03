# ADR-012 — Sentry como error tracker externo

**Estado**: Adopted
**Data**: 2026-05-03
**Autores**: Thacio

---

## Context

`tech-stack.md` §13 diferiu métricas / OpenTelemetry / Prometheus para a
Phase 7 (plataforma SaaS). No entanto, o dogfooding pré-deploy (Phase 6)
precisa de error tracking externo para:

- Apanhar exceções 5xx que escapam ao log em ficheiro (Serilog file sink
  depende de acesso SSH ao VPS para consulta).
- Correlacionar erros de frontend (JS) com backend no mesmo dashboard.
- Alertar por email/notificação para erros críticos (diferentemente de
  grep manual em `logs/sextante-*.log`).

Phase 5.5 concretiza a integração. Phase 6 adiciona release management
automático via CI (`sentry-cli releases new $GITHUB_SHA`) no job de
deploy.

---

## Decision

**Sentry SaaS (sentry.io) free tier**, com dois projectos separados por
surface:

| Projecto | Surface | DSN env var |
|----------|---------|-------------|
| `sextante-api` | Backend .NET (`Sextante.Host`) | `SENTRY__DSN` |
| `sextante-web` | Frontend Angular (`Sextante.Web`) | `SENTRY_DSN_WEB` (compilada no build) |

A versão self-hosted do Sentry é reavaliada na Phase 7, se o volume de
erros justificar o overhead de RAM adicional no VPS.

---

## Consequences

### Positivas
- **Observability externa sem VPS overhead**: se o container API
  reiniciar, os eventos da janela antes do crash estão no Sentry, não
  dependem do file sink local.
- **Frontend + backend no mesmo dashboard**: stack trace do JS com
  source maps publicados no release.
- **Free tier (5k errors/mês) sobra** para 1 utilizador (MVP: Thacio).
- **Scrubbing de PII alinhado aos filtros Serilog** (mesma lista de
  propriedades sensíveis — `PiiProperties`).

### Negativas
- **Dependência externa em SaaS**: se o Sentry estiver inacessível, a
  app continua a funcionar (`fallback graceful` — DSN ausente → skip
  da inicialização, Serilog file sink mantém log local). `mission.md`
  §4.4 (self-hosted-friendly).
- **Custo pós-MVP**: quando o volume de erros exceder 5k/mês, ou o
  projecto precisa de upgrade para plano pago. Reavaliar em Phase 7.
- **Source maps públicos**: Angular CI publica source maps no release
  do Sentry — expõe código frontend. Apesar de todo o frontend ser
  estático e público de qualquer forma (Sextante.Web é SPA servida no
  Host), é trade-off aceite para stack traces legíveis.

---

## Alternatives considered

### Self-hosted Sentry (Docker no VPS)
- **Rejeitada porque**: consome RAM (~1 GB) no VPS partilhado com
  Postgres + API + Hangfire + Angular. O VPS do MVP é pequeno (1-2 GB
  RAM). Self-hosted é reavaliado na Phase 7.

### Seq (Datalust) — self-hosted
- **Rejeitada porque**: não captura erros de frontend (source maps
  JavaScript). Backend-only não cobre o need case de debug em produção.

### Apenas Serilog file sink (sem error tracking externo)
- **Rejeitada porque**: dogfooding sem alertas externos significa que
  erros 5xx só são descobertos quando o utilizador reporta — não
  cumpre o critério "substitui Excel/Mobills" do `mission.md` §6.

---

## Referências

- `specs/tech-stack.md` §13 — Logs e observability (actualizado nesta
  Phase 5.5).
- ADR-010 — CQRS via Wolverine.
- ADR-011 — Frontend UI stack (PrimeNG + Tailwind + Signals).
