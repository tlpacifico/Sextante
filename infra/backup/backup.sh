#!/usr/bin/env bash
#
# Backup do Sextante — um dump por schema, com retenção.
#
# Corre no host da VPS, a partir do directório do repo (onde vive o
# docker-compose.yml e o .env). Uso típico via cron/systemd timer:
#
#   /opt/sextante/infra/backup/backup.sh >> /var/log/sextante-backup.log 2>&1
#
# Variáveis de ambiente (todas opcionais):
#   SEXTANTE_BACKUP_DIR             destino dos dumps (default /var/backups/sextante)
#   SEXTANTE_BACKUP_RETENTION_DAYS  dias a manter (default 30)
#   SEXTANTE_COMPOSE_DIR            directório do docker-compose.yml
#                                   (default: dois níveis acima deste script)
#
# Falha ruidosamente: qualquer dump com erro ou suspeitosamente pequeno
# aborta o script com exit code != 0. Um backup silenciosamente vazio é
# pior que nenhum backup — é a diferença entre saber e não saber.

set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="${SEXTANTE_COMPOSE_DIR:-$(cd "${SCRIPT_DIR}/../.." && pwd)}"
BACKUP_DIR="${SEXTANTE_BACKUP_DIR:-/var/backups/sextante}"
RETENTION_DAYS="${SEXTANTE_BACKUP_RETENTION_DAYS:-30}"

# Schemas de negócio + infraestrutura. `messaging` (outbox Wolverine) e
# `hangfire` (jobs) entram porque um restore sem eles perde mensagens
# pendentes e o histórico de jobs recorrentes.
SCHEMAS=(shared financial hangfire messaging)

# Um dump abaixo disto quase certamente significa "schema vazio" ou erro
# silencioso — o header de um dump -Fc válido já ocupa umas centenas de bytes.
MIN_DUMP_BYTES=512

log() {
    printf '%s  %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"
}

fail() {
    log "ERRO: $*"
    exit 1
}

[[ -f "${COMPOSE_DIR}/docker-compose.yml" ]] \
    || fail "docker-compose.yml não encontrado em ${COMPOSE_DIR} (usar SEXTANTE_COMPOSE_DIR)."

[[ -f "${COMPOSE_DIR}/.env" ]] \
    || fail ".env não encontrado em ${COMPOSE_DIR} — as credenciais do Postgres vêm de lá."

# Lê chaves do .env sem o executar: valores como
# ASPNETCORE_URLS=http://+:80;https://+:443 fariam o shell interpretar o `;`
# como separador de comandos se fizéssemos `source`.
read_env() {
    local key="$1" default="${2:-}" line value
    line="$(grep -E "^[[:space:]]*${key}=" "${COMPOSE_DIR}/.env" | tail -n 1 || true)"

    if [[ -z "${line}" ]]; then
        printf '%s' "${default}"
        return
    fi

    value="${line#*=}"
    value="${value%\"}"
    value="${value#\"}"
    value="${value%\'}"
    value="${value#\'}"
    printf '%s' "${value}"
}

POSTGRES_USER="$(read_env POSTGRES_USER postgres)"
POSTGRES_DB="$(read_env POSTGRES_DB sextante)"

mkdir -p "${BACKUP_DIR}"

TIMESTAMP="$(date -u +'%Y%m%d-%H%M')"
log "Backup iniciado (db=${POSTGRES_DB}, destino=${BACKUP_DIR}, retenção=${RETENTION_DAYS}d)"

for schema in "${SCHEMAS[@]}"; do
    target="${BACKUP_DIR}/${schema}-${TIMESTAMP}.dump"

    # `-Fc` (custom) permite restore selectivo e é comprimido.
    # `-T` no exec: sem TTY, para o stdout binário não ser corrompido.
    if ! docker compose --project-directory "${COMPOSE_DIR}" exec -T postgres \
        pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
        --schema="${schema}" --format=custom > "${target}"; then
        rm -f "${target}"
        fail "pg_dump falhou no schema '${schema}'."
    fi

    size="$(wc -c < "${target}" | tr -d '[:space:]')"
    if (( size < MIN_DUMP_BYTES )); then
        rm -f "${target}"
        fail "dump do schema '${schema}' tem apenas ${size} bytes — tratado como falha."
    fi

    log "OK  ${schema}: ${size} bytes → $(basename "${target}")"
done

# Retenção. Corre depois dos dumps de hoje para que uma falha acima nunca
# apague o histórico existente.
deleted=0
while IFS= read -r -d '' old; do
    rm -f "${old}"
    log "Removido por retenção: $(basename "${old}")"
    deleted=$((deleted + 1))
done < <(find "${BACKUP_DIR}" -maxdepth 1 -name '*.dump' -type f -mtime "+${RETENTION_DAYS}" -print0)

log "Backup concluído. ${#SCHEMAS[@]} dumps criados, ${deleted} removidos por retenção."
