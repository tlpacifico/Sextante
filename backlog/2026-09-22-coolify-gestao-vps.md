# Coolify para gerir a VPS partilhada

> Ideia surgida durante a Phase 6 (deploy). Adiada para **depois do MVP**
> por decisão explícita: primeiro pôr o Sextante em produção pelo caminho
> já escrito e arrancar o dogfooding; migração de plataforma depois.

## Problema

A VPS corre 3 apps (`oui-system`, `binance-bot`, `sextante`), cada uma com
o seu pipeline montado à mão:

- um `deploy.yml` por repo, com SSH + scp + `.env` gerado por script próprio;
- chave SSH e GitHub Secrets repetidos por repo;
- Caddyfile do systemd editado à mão para cada vhost;
- backups agendados por app (systemd timer no Sextante);
- logs e estado só por SSH.

Já há sinais de deriva silenciosa, detectados na Phase 6: o container
`oui-caddy` esteve 5 semanas em crash-loop (colisão na porta 2019 com o
Caddy do systemd) e o `oui-system` aparece `unhealthy` porque o healthcheck
usa `curl`, que a imagem `dotnet/aspnet` não traz, contra um `/health` que
não existe. Ninguém deu por nada. Cada app nova multiplica o problema.

## Proposta

[Coolify](https://coolify.io) — PaaS self-hosted, open source. Deploy por
push a partir do GitHub, variáveis de ambiente numa UI, TLS automático por
domínio, logs, rollback e backups agendados de bases geridas.

## Custos conhecidos

- **Cutover do proxy**: o Coolify usa Traefik nas portas 80/443, hoje do
  Caddy do systemd. Há alguns minutos de indisponibilidade para
  `oui-system` e `binance-bot` na troca. É o passo delicado.
- **Recursos**: cerca de 0,5–1 GB de RAM (a VPS tem 7,8 GB, com 6,6 GB livres
  à data).
- **Constitution**: substitui a estratégia de deploy do ADR-013 e de
  `tech-stack.md` §15. Pelo `AGENTS.md` §6, faz-se numa branch `replanning`
  e não dentro de uma phase de feature.
- **Sextante**: o `deploy/docker-compose.yml` é reutilizável tal como está (o
  Coolify faz deploy de compose). O `deploy.yml` ficaria só com os testes. O
  Postgres continua dentro do compose por causa do bootstrap dos roles RLS
  (`AGENTS.md` §3.1), por isso o timer de backup mantém-se.

## Sequência sugerida (da app com menos risco para a com mais)

1. Instalar o Coolify na VPS.
2. Migrar o **Sextante** (é o mais recente e tem o compose mais limpo).
3. Migrar o **binance-bot**.
4. Migrar o **oui-system** por último, junto com a troca Caddy → Traefik.

## Alternativas consideradas

- **Dokploy**: mesma ideia, mais leve, comunidade mais pequena.
- **Kamal**: só CLI, configuração por repo; não resolve a parte de gestão
  manual, que é o que pesa.
- **Status quo normalizado**: um repo `infra` com o Caddyfile e scripts
  partilhados. Reduz a deriva, mas mantém tudo manual.
