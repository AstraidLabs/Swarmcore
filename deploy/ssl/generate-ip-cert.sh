#!/usr/bin/env bash
# deploy/ssl/generate-ip-cert.sh
#
# Generates self-signed TLS certificates for direct IP access (HTTPS via IP).
# Let's Encrypt does NOT issue certificates for IP addresses; this script creates
# a locally trusted certificate with the IP in the Subject Alternative Name (SAN).
#
# The generated certs are placed in data/certbot/conf/live/<IP>/ so the nginx
# config can use the same path convention as Let's Encrypt certs.
#
# Usage:
#   export SERVER_IP=1.2.3.4
#   bash deploy/ssl/generate-ip-cert.sh
#
# Optional – use separate IPs per service:
#   export TRACKER_IP=1.2.3.4
#   export ADMIN_IP=1.2.3.4   # can be the same
#   bash deploy/ssl/generate-ip-cert.sh

set -euo pipefail

: "${SERVER_IP:=$(curl -s https://api.ipify.org || echo '')}"

TRACKER_IP="${TRACKER_IP:-${SERVER_IP}}"
ADMIN_IP="${ADMIN_IP:-${SERVER_IP}}"

if [[ -z "${TRACKER_IP}" || -z "${ADMIN_IP}" ]]; then
    echo "ERROR: Could not determine server IP. Set SERVER_IP explicitly." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
LETSENCRYPT_DIR="${PROJECT_ROOT}/data/certbot/conf"

generate_ip_cert() {
    local ip="$1"
    local label="$2"
    local live_dir="${LETSENCRYPT_DIR}/live/${ip}"

    echo "==> Generating self-signed cert for IP ${ip} (${label})..."
    mkdir -p "${live_dir}"

    # OpenSSL config with IP in SAN
    local tmp_cfg
    tmp_cfg="$(mktemp)"
    cat > "${tmp_cfg}" <<EOF
[req]
default_bits       = 4096
prompt             = no
default_md         = sha256
distinguished_name = dn
x509_extensions    = v3_req

[dn]
CN = ${ip}

[v3_req]
subjectAltName = @alt_names
keyUsage       = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth

[alt_names]
IP.1 = ${ip}
EOF

    openssl req -x509 -nodes -newkey rsa:4096 \
        -config   "${tmp_cfg}" \
        -keyout   "${live_dir}/privkey.pem" \
        -out      "${live_dir}/fullchain.pem" \
        -days     825
    rm -f "${tmp_cfg}"
    echo "   [ok] cert written to ${live_dir}/"
}

generate_ip_cert "${TRACKER_IP}" "tracker"
if [[ "${ADMIN_IP}" != "${TRACKER_IP}" ]]; then
    generate_ip_cert "${ADMIN_IP}" "admin"
else
    # Reuse the same cert directory for admin
    ADMIN_LIVE="${LETSENCRYPT_DIR}/live/${ADMIN_IP}"
    TRACKER_LIVE="${LETSENCRYPT_DIR}/live/${TRACKER_IP}"
    if [[ "${ADMIN_LIVE}" != "${TRACKER_LIVE}" ]]; then
        mkdir -p "${ADMIN_LIVE}"
        cp -f "${TRACKER_LIVE}/fullchain.pem" "${ADMIN_LIVE}/fullchain.pem"
        cp -f "${TRACKER_LIVE}/privkey.pem"   "${ADMIN_LIVE}/privkey.pem"
    fi
fi

echo ""
echo "Self-signed certificates generated."
echo ""
echo "Update your .env so nginx uses the IP paths:"
echo "  TRACKER_DOMAIN=${TRACKER_IP}"
echo "  ADMIN_DOMAIN=${ADMIN_IP}"
echo ""
echo "Then start the stack:"
echo "  docker compose up -d"
echo ""
echo "NOTE: Browsers will show a security warning because the cert is self-signed."
echo "      Add ${LETSENCRYPT_DIR}/live/${TRACKER_IP}/fullchain.pem to your"
echo "      OS/browser trust store to suppress the warning."
