# Runbook — restore de backup do Sextante

> Cobre o caminho inverso do `infra/backup/backup.sh`: pegar nos dumps por
> schema e recolocá-los numa base limpa. `tech-stack.md` §15 exige **pelo
> menos 1 restore de teste executado** antes do dogfooding — a última
> secção deste ficheiro registra-o.

## Onde isto corre

Na VPS, o Sextante vive em `/opt/sextante` — e **o repo não está lá
clonado**. O que existe é o que o job de deploy copia (ver
`.github/workflows/deploy.yml`):

```
/opt/sextante/
├── docker-compose.yml          # copiado de deploy/docker-compose.yml
├── .env                        # reescrito a cada deploy a partir dos GitHub Secrets
└── infra/
    ├── postgres/01-bootstrap-roles.sh
    └── backup/backup.sh + sextante-backup.{service,timer}
```

Todos os comandos abaixo assumem `cd /opt/sextante`.

> ⚠️ O `.env` é **regenerado a cada deploy**. Se um restore exigir
> passwords diferentes das que estão nos Secrets, actualiza os Secrets no
> GitHub — mudar o ficheiro na VPS dura até ao próximo push.

## Variáveis usadas nos comandos

O superuser do Postgres **não é necessariamente `postgres`** — vem do
`.env`. Definir uma vez por sessão de shell:

```bash
SU=$(grep -E '^POSTGRES_USER=' .env | cut -d= -f2)
DB=$(grep -E '^POSTGRES_DB=' .env | cut -d= -f2)
```

> `psql -U "$SU"` **sem** `-d` tenta ligar a uma base com o nome do
> utilizador e falha com `database "<user>" does not exist`. Passar sempre
> `-d`, mesmo em comandos de manutenção como `CREATE DATABASE`.

## O que existe

O `backup.sh` produz um ficheiro por schema, em formato custom (`-Fc`):

```
/var/backups/sextante/
├── shared-20260901-0300.dump      # Identity, Tenants, Currency, ExchangeRate
├── financial-20260901-0300.dump   # contas, categorias, transações, metas, regras
├── hangfire-20260901-0300.dump    # jobs recorrentes e histórico
└── messaging-20260901-0300.dump   # outbox Wolverine
```

Um dump de schema **não** contém os roles (`sextante_app`,
`sextante_migrations`) nem as policies RLS que dependem deles: os roles são
objectos de cluster, criados pelo `infra/postgres/01-bootstrap-roles.sh` na
primeira boot do volume. Restaurar para um cluster onde esses roles não
existem falha na atribuição de owner e nas policies.

## Restore completo (perda total)

1. **Provisionar o stack do zero** no destino.

   Na VPS, basta correr o workflow **Deploy Sextante** (`workflow_dispatch`):
   ele recria `/opt/sextante` com o compose, o `infra/` e o `.env` a partir
   dos Secrets. Depois:

   ```bash
   cd /opt/sextante && docker compose up -d postgres
   ```

   Fora da VPS (ex.: máquina local, para um ensaio), a partir do repo:

   ```bash
   git clone <repo> sextante && cd sextante
   cp .env.example .env   # preencher com as MESMAS passwords do original
   docker compose up -d postgres
   ```

   As passwords têm de coincidir com as do `.env` original — os dumps
   referem os roles por nome, e as connection strings da API por password.
   Numa VPS reprovisionada isso significa **não rodar** os Secrets
   `SEXTANTE_APP_PASSWORD` / `SEXTANTE_MIGRATIONS_PASSWORD` antes do restore.

2. **Confirmar que os roles existem** (o init script correu na primeira boot):

   ```bash
   docker compose exec -T postgres psql -U "$SU" -d "$DB" -c '\du' | grep sextante_
   ```

3. **Restaurar schema a schema**, na ordem `shared` → `financial` →
   `messaging` → `hangfire` (não há FKs cross-schema, mas `financial`
   referencia `shared` por ID e a ordem mantém a leitura coerente):

   ```bash
   for schema in shared financial messaging hangfire; do
     docker compose exec -T postgres \
       pg_restore -U "$SU" -d "$DB" --clean --if-exists --no-owner \
       < "/var/backups/sextante/${schema}-<timestamp>.dump"
   done
   ```

   - `--clean --if-exists`: substitui objectos existentes em vez de falhar.
   - `--no-owner`: o owner é reatribuído pelos `GRANT` do init script.

4. **Arrancar a API** (`docker compose up -d`) e deixar as migrations
   correrem. O `MigrationRunner` é idempotente: se o dump já está na versão
   actual, não aplica nada.

5. **Verificar** — é isto que distingue "restaurei" de "tenho os dados":

   ```bash
   # Na VPS, antes de haver domínio (a API só escuta em loopback):
   curl -fsS http://127.0.0.1:8090/api/health    # {"status":"ok",...}

   # Depois do vhost do Caddy (deploy/Caddyfile.sextante):
   curl -I https://<dominio>/api/health          # 200
   ```

   E no browser: login com um utilizador existente, `/transactions` mostra
   o histórico, `/budgets` mostra progresso, o dashboard soma valores.

## Agendamento do backup na VPS

O `backup.sh` corre por **systemd timer**, não por cron. A razão é
concreta: a VPS está em `Europe/Berlin` e o cron interpreta horas na
timezone local, portanto "03:00" andaria entre 01:00 e 02:00 UTC conforme
o DST, e um dos dois saltos anuais chega a repetir ou saltar a execução.
O `OnCalendar=*-*-* 03:00:00 UTC` do timer é imune a isso.

```bash
sudo cp /opt/sextante/infra/backup/sextante-backup.service \
        /opt/sextante/infra/backup/sextante-backup.timer \
        /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now sextante-backup.timer
```

Verificar:

```bash
systemctl list-timers sextante-backup.timer   # confirmar o NEXT em UTC
sudo systemctl start sextante-backup.service  # correr já, uma vez
journalctl -u sextante-backup.service -n 30   # ver o log do script
ls -la /var/backups/sextante/
```

A hora foi escolhida para não colidir com os jobs Hangfire (materializador
de recorrentes às 00:15 UTC, câmbios do BCE às 00:30 UTC).

> O `infra/` em `/opt/sextante` é reescrito a cada deploy, portanto o
> `backup.sh` mantém-se em sincronia com o repo sozinho. As unidades
> systemd, essas, são copiadas **uma vez** para `/etc/systemd/system/` —
> se as mudares no repo, repete o `cp` + `daemon-reload`.

## Restore selectivo (só uma tabela)

Útil quando se apaga algo por engano e não se quer perder o resto:

```bash
docker compose exec -T postgres \
  pg_restore -U "$SU" -d "$DB" --no-owner \
  --table=transactions < financial-<timestamp>.dump
```

`pg_restore` não faz merge — a tabela é substituída. Se as linhas boas e as
más convivem na mesma tabela, restaurar para uma base temporária e copiar
as linhas com `INSERT ... SELECT` é o caminho seguro.

## Restore de teste (sem tocar em produção)

Procedimento a repetir sempre que se quiser confiança sem risco: restaura
para uma base **nova** no mesmo cluster.

```bash
# 1. Base temporária
docker compose exec -T postgres \
  psql -U "$SU" -d "$DB" -c 'CREATE DATABASE sextante_restore_test'

# 2. Restaurar todos os schemas para lá
for schema in shared financial messaging hangfire; do
  docker compose exec -T postgres \
    pg_restore -U "$SU" -d sextante_restore_test --no-owner \
    < "/var/backups/sextante/${schema}-<timestamp>.dump"
done

# 3. Contar o que chegou e comparar com a origem
docker compose exec -T postgres psql -U "$SU" -d sextante_restore_test -t -c \
  "SELECT 'transactions', COUNT(*) FROM financial.transactions
   UNION ALL SELECT 'categories', COUNT(*) FROM financial.categories
   UNION ALL SELECT 'users', COUNT(*) FROM shared.\"AspNetUsers\"
   UNION ALL SELECT 'tenants', COUNT(*) FROM shared.\"Tenants\""

# 4. Limpar
docker compose exec -T postgres \
  psql -U "$SU" -d "$DB" -c 'DROP DATABASE sextante_restore_test'
```

## Restore de teste executado

| Data | Origem dos dumps | Resultado |
|---|---|---|
| _(preencher na Phase 6, tarefa 3.4, com os dumps da VPS)_ | | |

**Ensaio em ambiente local — 2026-09-01**, antes de existir VPS:

- `infra/backup/backup.sh` correu contra a Postgres do `docker compose`
  local e produziu os 4 dumps: `shared` 35 241 B, `financial` 22 155 B,
  `hangfire` 29 882 B, `messaging` 14 931 B.
- Restore dos 4 schemas para `sextante_restore_test` com
  `pg_restore --no-owner`: todos com exit code 0, e as contagens
  corresponderam exactamente à origem — 1 utilizador, 1 tenant, 11
  categorias, 0 transações (a base local só tinha o tenant de
  desenvolvimento).
- Retenção verificada: um dump com 40 dias foi apagado com
  `SEXTANTE_BACKUP_RETENTION_DAYS=30`.
- Falha ruidosa verificada: com a Postgres em baixo, o script termina com
  exit code 1 e não deixa ficheiros truncados no destino.

> Este ensaio valida o **procedimento**, não os dados. O bloqueador
> declarado em `tech-stack.md` §15 é um restore sobre dados reais da VPS —
> tarefa 3.4, ainda em aberto.

## Agendamento do backup na VPS

Systemd timer (preferido — sobrevive a reboots e deixa rasto no journal):

```ini
# /etc/systemd/system/sextante-backup.service
[Unit]
Description=Backup do Sextante (pg_dump por schema)

[Service]
Type=oneshot
WorkingDirectory=/opt/sextante
ExecStart=/opt/sextante/infra/backup/backup.sh
```

```ini
# /etc/systemd/system/sextante-backup.timer
[Unit]
Description=Backup diário do Sextante às 03:00 UTC

[Timer]
OnCalendar=*-*-* 03:00:00 UTC
Persistent=true

[Install]
WantedBy=timers.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now sextante-backup.timer
systemctl list-timers sextante-backup.timer
sudo systemctl start sextante-backup.service   # correr uma vez, já
journalctl -u sextante-backup.service -n 20
```

Alternativa em cron: `0 3 * * * cd /opt/sextante && ./infra/backup/backup.sh >> /var/log/sextante-backup.log 2>&1`.

03:00 UTC fica deliberadamente longe dos jobs Hangfire (materializer às
00:15, ECB às 00:30).

## Notas

- Os dumps vivem no mesmo disco da VPS. Isso protege contra erro humano e
  corrupção lógica, **não** contra perda da máquina. Cópia off-site está
  fora do scope da Phase 6 (`requirements.md`, open question 5) — vale a
  pena marcar como próximo passo operacional.
- O `.env` não é coberto pelo backup e é necessário para restaurar
  (passwords dos roles, `JWT__SIGNING_KEY`). Guardar à parte, num gestor de
  palavras-passe: sem `JWT__SIGNING_KEY` as sessões activas morrem, mas os
  dados continuam acessíveis.
