# Nginx + Let's Encrypt – Deployment Note

## Production model

```
Internet → Nginx (port 443, TLS) → Swarmcore (plain HTTP, port 8080)
```

Nginx terminates TLS. The ASP.NET Core application runs on **plain HTTP internally** and
never needs to handle certificates. All public URLs are HTTPS.

Subdomains are resolved at the Nginx layer — the same application instance can serve
multiple hostnames (e.g. `announce.example.com`, `admin.example.com`).

---

## Required configuration

### Public endpoint (tracker-gateway)

```json
{
  "Swarmcore": {
    "PublicEndpoint": {
      "BaseUrl": "https://tracker.example.com",
      "ForceHttps": true,
      "EnableHsts": false,
      "EnableHttpsRedirection": false,
      "BaseDomain": "example.com",
      "AnnounceBaseUrl": "https://announce.example.com",
      "ScrapeBaseUrl": "https://tracker.example.com",
      "AdminBaseUrl": "https://admin.example.com",
      "ApiBaseUrl": "https://api.example.com",
      "AllowedHosts": [
        "tracker.example.com",
        "announce.example.com",
        "admin.example.com",
        "api.example.com"
      ],
      "AllowedSubdomains": ["tracker", "announce", "admin", "api"],
      "EnableWildcardSubdomains": false
    }
  }
}
```

| Key | Purpose | Default |
|-----|---------|---------|
| `BaseUrl` | Default public base URL for announce/scrape | *(required)* |
| `ForceHttps` | Fails startup if any public URL is not `https://` | `true` |
| `EnableHsts` | Emit HSTS header from the app | `false` — let Nginx do it |
| `EnableHttpsRedirection` | HTTP→HTTPS redirect inside the app | `false` — let Nginx do it |
| `BaseDomain` | Root domain for subdomain validation | *(required for subdomain mode)* |
| `AnnounceBaseUrl` | Per-surface announce URL (overrides BaseUrl) | *(falls back to BaseUrl)* |
| `ScrapeBaseUrl` | Per-surface scrape URL | *(falls back to BaseUrl)* |
| `AdminBaseUrl` | Admin UI URL | *(falls back to BaseUrl)* |
| `ApiBaseUrl` | API URL | *(falls back to BaseUrl)* |
| `AllowedHosts` | Allowed Host headers (empty = auto-derived from URLs) | `[]` |
| `AllowedSubdomains` | Allowed subdomain prefixes | `[]` |
| `EnableWildcardSubdomains` | Accept any subdomain of BaseDomain | `false` |

Equivalent via environment variables:
```
Swarmcore__PublicEndpoint__BaseUrl=https://tracker.example.com
Swarmcore__PublicEndpoint__BaseDomain=example.com
Swarmcore__PublicEndpoint__AnnounceBaseUrl=https://announce.example.com
```

The app validates all URLs at startup; a misconfigured value prevents boot.

---

## Forwarded headers

Nginx forwards three headers. Swarmcore reads them via `UseForwardedHeaders()`:

| Nginx header | What it tells the app |
|---|---|
| `X-Forwarded-For` | Real client IP |
| `X-Forwarded-Proto` | `https` — so `Request.Scheme` is correct |
| `X-Forwarded-Host` | Public hostname |

Trusted proxy configuration:

```json
"TrustedProxy": {
  "ForwardLimit": 1,
  "KnownProxies": ["127.0.0.1"],
  "KnownNetworks": ["172.29.0.0/24"]
}
```

Only list proxies you actually control. Listing untrusted IPs allows header spoofing.

---

## Host validation

`HostValidationMiddleware` runs immediately after `UseForwardedHeaders()` and rejects
requests whose `Host` header is not in `AllowedHosts` or does not match a configured
subdomain. This prevents host header injection attacks.

When `AllowedHosts` is empty, hosts are auto-derived from the configured public URLs.

---

## Subdomain support

The application supports host-based routing for different surfaces:

| Subdomain | Surface |
|---|---|
| `announce.example.com` | Tracker announce |
| `tracker.example.com` | Tracker scrape / general |
| `admin.example.com` | Admin UI |
| `api.example.com` | API |

All subdomains resolve to the same Nginx instance. Nginx routes to the correct upstream
(`tracker_gateway` or `admin_backend`) based on `server_name`.

To add a new subdomain:
1. Add DNS record
2. Add to `AllowedHosts` / `AllowedSubdomains` in appsettings
3. Add `server_name` in Nginx template
4. Obtain certificate (or use wildcard cert)

---

## Recommended Nginx configuration

```nginx
server {
    listen 80;
    server_name announce.example.com tracker.example.com admin.example.com api.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name announce.example.com tracker.example.com api.example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;
    add_header Strict-Transport-Security "max-age=63072000" always;

    location / {
        proxy_pass         http://swarmcore:8080;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto https;
        proxy_set_header   X-Forwarded-Host  $host;
    }
}

server {
    listen 443 ssl http2;
    server_name admin.example.com;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;
    add_header Strict-Transport-Security "max-age=63072000" always;

    location / {
        proxy_pass         http://admin-service:8080;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto https;
        proxy_set_header   X-Forwarded-Host  $host;
    }
}
```

HTTP → HTTPS redirect is handled by Nginx (`return 301`). Keep `EnableHttpsRedirection: false`
in the app to avoid double-redirects or loops.

---

## Docker Compose deployment

### Subdomain environment variables

Set in `.env`:

```
TRACKER_DOMAIN=tracker.example.com
ADMIN_DOMAIN=admin.example.com
BASE_DOMAIN=example.com
ANNOUNCE_DOMAIN=announce.example.com
SCRAPE_DOMAIN=tracker.example.com
API_DOMAIN=api.example.com
CERTBOT_EMAIL=admin@example.com
```

### Network architecture

All containers share the `swarmcore` bridge network (subnet `172.29.0.0/24`).
Nginx routes to services by Docker DNS name (`tracker-gateway`, `admin-service`).

```
                  ┌─────────────────┐
Internet ──443──► │  nginx          │
                  │  (TLS termination)
                  └──┬──────────┬───┘
                     │          │
          ┌──────────▼─┐  ┌────▼──────────┐
          │ tracker-   │  │ admin-        │
          │ gateway    │  │ service       │
          │ :8080 HTTP │  │ :8080 HTTP    │
          └────────────┘  └───────────────┘
```

The `tracker-gateway` service handles announce/scrape regardless of which subdomain
was used — host differentiation is in Nginx `server_name` blocks.

### Trusted proxy

Docker containers communicate over `172.29.0.0/24`. This network is configured as
`KnownNetworks` so forwarded headers from Nginx are trusted:

```yaml
Swarmcore__TrustedProxy__KnownNetworks__0: 172.29.0.0/24
```

---

## Let's Encrypt certificate

For multiple subdomains, use a wildcard or SAN certificate:

```bash
certbot certonly --nginx -d example.com -d '*.example.com'
```

Or individual certificates:
```bash
certbot certonly --nginx -d tracker.example.com -d announce.example.com -d admin.example.com
```

Auto-renewal via `certbot renew` (the `certbot` container in docker-compose handles this).

---

## Passkey safety in logs

Passkeys appear in announce URLs: `/announce/<passkey>`. The application **never logs
passkeys in plain form**:

- `TrackerRequestGuardMiddleware` and `TrackerProtocolExceptionMiddleware` redact the
  last path segment: `/announce/abcdef1234567890` → `/announce/abc***890`
- `PasskeyLogSanitizationMiddleware` stores a sanitized path (`/announce/***`) in
  `HttpContext.Items` for downstream diagnostic loggers.
- Nginx `tracker_minimal` log format uses `$uri` (path only, no query string).

Do **not** configure Nginx `access_log` to include `$request_uri` or `$args` if
passkey-in-query-string mode is ever enabled.

---

## Admin security

- Auth cookie (`swarmcore_admin_auth`): `Secure=Always`, `HttpOnly=true`, `SameSite=Lax`
- CSRF cookie (`swarmcore_admin_csrf`): `Secure=Always`, `HttpOnly=false` (SPA needs to read it), `SameSite=Strict`
- Admin and tracker surfaces use separate cookies — tracker subdomains never receive admin cookies.
- `DisableTransportSecurityRequirement: false` in production ensures OpenIddict requires HTTPS.

---

## Startup verification checklist

- [ ] `Swarmcore:PublicEndpoint:BaseUrl` is set to the public `https://` URL
- [ ] Per-surface URLs (`AnnounceBaseUrl`, `AdminBaseUrl`, etc.) are correct
- [ ] `BaseDomain` matches your root domain
- [ ] `AllowedHosts` or `AllowedSubdomains` includes all production hostnames
- [ ] App starts without validation errors (`ValidateOnStart` blocks boot if misconfigured)
- [ ] `curl -I https://tracker.example.com/health/live` returns HTTP 200
- [ ] `curl -v http://tracker.example.com/announce` redirects 301 → https (Nginx)
- [ ] Announce URL returned in torrent clients starts with `https://`
- [ ] App logs show `/announce/abc***890` (not the full passkey) on guard warnings
- [ ] Admin CSRF cookie has `Secure` flag set (inspect in browser DevTools)
- [ ] Admin auth cookie has `Secure` and `HttpOnly` flags set
- [ ] `X-Forwarded-Proto: https` reaches the app: `Request.Scheme` is `https`
- [ ] Request with invalid Host header returns 400
- [ ] Docker containers communicate over the `swarmcore` network
- [ ] Nginx resolves `tracker-gateway` and `admin-service` by Docker DNS
