#!/usr/bin/env bash
# deploy/ssl/renew.sh
#
# Forces a certificate renewal and reloads nginx.
# Normally not needed – the certbot service in docker-compose runs renewal
# automatically every 12 hours.  Use this script for manual/emergency renewals.
#
# Usage:
#   bash deploy/ssl/renew.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

echo "==> Renewing certificates..."
(cd "${PROJECT_ROOT}" && docker compose run --rm certbot renew)

echo "==> Reloading nginx..."
(cd "${PROJECT_ROOT}" && docker compose exec nginx nginx -s reload)

echo "Done."
