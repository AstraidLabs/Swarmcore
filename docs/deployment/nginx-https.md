# Nginx + Let's Encrypt – Deployment Note

## Production model

```
Internet → Nginx (port 443, TLS) → Swarmcore (127.0.0.1:5000, plain HTTP)
```

Nginx terminates TLS. The ASP.NET Core application runs on **plain HTTP internally** and
never needs to handle certificates. All public URLs are HTTPS.

---

## Required configuration

Set the public base URL before deployment. Edit `appsettings.json` (or an environment
variable / secrets file):

```json
{
  "Swarmcore": {
    "PublicEndpoint": {
      "BaseUrl": "https://tracker.example.com",
      "ForceHttps": true,
      "EnableHsts": false,
      "EnableHttpsRedirection": false
    }
  }
}
```

| Key | Purpose | Default |
|-----|---------|---------|
| `BaseUrl` | Publicly advertised base URL for announce/scrape | *(required)* |
| `ForceHttps` | Fails startup if `BaseUrl` is not `https://` | `true` |
| `EnableHsts` | Emit HSTS header from the app | `false` — let Nginx do it |
| `EnableHttpsRedirection` | Redirect HTTP→HTTPS inside the app | `false` — let Nginx do it |

The app validates `BaseUrl` at startup; a misconfigured value prevents boot.

Equivalent via environment variable:
```
SWARMCORE__PUBLICENDPOINT__BASEURL=https://tracker.example.com
```

---

## Forwarded headers

Nginx forwards three headers. Swarmcore reads them via `UseForwardedHeaders()`:

| Nginx header | What it tells the app |
|---|---|
| `X-Forwarded-For` | Real client IP |
| `X-Forwarded-Proto` | `https` — so `Request.Scheme` is correct |
| `X-Forwarded-Host` | Public hostname |

Trusted proxy is configured in `appsettings.json`:

```json
"TrustedProxy": {
  "ForwardLimit": 1,
  "KnownProxies": ["127.0.0.1"],
  "KnownNetworks": []
}
```

Only list proxies you actually control. Listing untrusted IPs allows header spoofing.

---

## Recommended Nginx configuration

```nginx
server {
    listen 80;
    server_name tracker.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name tracker.example.com;

    ssl_certificate     /etc/letsencrypt/live/tracker.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/tracker.example.com/privkey.pem;

    # Optional: HSTS here keeps it off the app layer
    add_header Strict-Transport-Security "max-age=31536000" always;

    location / {
        proxy_pass         http://127.0.0.1:5000;
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

## Let's Encrypt certificate

```bash
certbot certonly --nginx -d tracker.example.com
```

Auto-renewal via `certbot renew` (systemd timer or cron). Nginx reloads automatically
when using the `--nginx` plugin.

---

## Passkey safety in logs

Passkeys appear in announce URLs: `/announce/<passkey>`. The application **never logs
passkeys in plain form**. Both `TrackerRequestGuardMiddleware` and
`TrackerProtocolExceptionMiddleware` redact the last path segment before writing to the
log, e.g.:

```
/announce/abcdef1234567890  →  /announce/abc***890
```

Do **not** configure Nginx `access_log` to log query strings if passkey-in-query-string
mode is ever enabled.

---

## Local development without Nginx

Override in `appsettings.Development.json` (already committed):

```json
{
  "Swarmcore": {
    "PublicEndpoint": {
      "BaseUrl": "http://localhost:5000",
      "ForceHttps": false
    }
  }
}
```

---

## Startup verification checklist

- [ ] `Swarmcore:PublicEndpoint:BaseUrl` is set to the public `https://` URL
- [ ] App starts without validation errors (`ValidateOnStart` blocks boot if misconfigured)
- [ ] `curl -I https://tracker.example.com/health/live` returns HTTP 200
- [ ] `curl -v http://tracker.example.com/announce` redirects 301 → https (Nginx)
- [ ] Announce URL returned in torrent clients starts with `https://`
- [ ] App logs show `/announce/abc***890` (not the full passkey) on guard warnings
- [ ] Admin CSRF cookie has `Secure` flag set (inspect in browser DevTools)
- [ ] `X-Forwarded-Proto: https` reaches the app: `Request.Scheme` is `https`
