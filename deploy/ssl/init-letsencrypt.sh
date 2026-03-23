#!/usr/bin/env bash
# deploy/ssl/init-letsencrypt.sh
#
# Bootstraps Let's Encrypt TLS certificates for TRACKER_DOMAIN and ADMIN_DOMAIN.
# Run this script ONCE on the host before starting the full stack with HTTPS.
#
# Requirements:
#   - docker / docker compose v2 available
#   - ports 80 and 81 reachable from the internet (HTTP-01 challenge)
#   - TRACKER_DOMAIN and ADMIN_DOMAIN DNS records pointing to this machine
#
# Usage:
#   export TRACKER_DOMAIN=tracker.example.com
#   export ADMIN_DOMAIN=admin.example.com
#   export CERTBOT_EMAIL=admin@example.com
#   bash deploy/ssl/init-letsencrypt.sh

set -euo pipefail

: "${TRACKER_DOMAIN:?Set TRACKER_DOMAIN before running this script}"
: "${ADMIN_DOMAIN:?Set ADMIN_DOMAIN before running this script}"
: "${CERTBOT_EMAIL:?Set CERTBOT_EMAIL before running this script}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# Volume paths used by docker-compose
LETSENCRYPT_DIR="${PROJECT_ROOT}/data/certbot/conf"
CERTBOT_WWW="${PROJECT_ROOT}/data/certbot/www"

mkdir -p "${LETSENCRYPT_DIR}" "${CERTBOT_WWW}"

echo "==> Creating temporary self-signed certificates so nginx can start..."
for domain in "${TRACKER_DOMAIN}" "${ADMIN_DOMAIN}"; do
    live_dir="${LETSENCRYPT_DIR}/live/${domain}"
    mkdir -p "${live_dir}"
    openssl req -x509 -nodes -newkey rsa:4096 \
        -keyout "${live_dir}/privkey.pem" \
        -out    "${live_dir}/fullchain.pem" \
        -days   1 \
        -subj   "/CN=${domain}" \
        2>/dev/null
    echo "   [ok] dummy cert for ${domain}"
done

echo "==> Starting nginx..."
(cd "${PROJECT_ROOT}" && docker compose up -d nginx)
sleep 2

echo "==> Requesting Let's Encrypt certificates..."
for domain in "${TRACKER_DOMAIN}" "${ADMIN_DOMAIN}"; do
    echo "   Requesting cert for ${domain}..."
    docker compose -f "${PROJECT_ROOT}/docker-compose.yml" run --rm certbot \
        certonly --webroot \
        --webroot-path /var/www/certbot \
        --email "${CERTBOT_EMAIL}" \
        --agree-tos \
        --no-eff-email \
        --force-renewal \
        -d "${domain}"
    echo "   [ok] cert issued for ${domain}"
done

echo "==> Reloading nginx with real certificates..."
(cd "${PROJECT_ROOT}" && docker compose exec nginx nginx -s reload)

echo ""
echo "Done! HTTPS is active for:"
echo "  Tracker : https://${TRACKER_DOMAIN}"
echo "  Admin   : https://${ADMIN_DOMAIN}:8443"
echo ""
echo "Certbot auto-renewal is handled by the certbot service in docker-compose."
