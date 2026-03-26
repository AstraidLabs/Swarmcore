# Dev Browser Local Access

## Purpose

The default `docker-compose.yml` is production-oriented:

- nginx host-based routing
- `admin.example.com` / `tracker.example.com`
- HTTPS redirects
- certbot volumes

That is fine for edge-like testing, but it is inconvenient for a local browser smoke test.

For local development in a browser, use the override file:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev-browser.yml up -d --build
```

## Local URLs

After startup, use:

- Admin UI: `http://localhost:18081`
- Tracker HTTP surface: `http://localhost:18080`
- smtp4dev UI: `http://localhost:5000`

UDP announce stays on:

- `udp://localhost:6969`

## Admin Login

Bootstrap account:

- username: `BeeAdmin`
- password: `BeePassword123!`

## What the override changes

### Admin service

- binds host port `18081 -> 8080`
- disables OpenIddict transport security requirement for local HTTP
- changes public/admin base URLs to `http://localhost:18081`
- registers `localhost` and `127.0.0.1` as allowed hosts
- overrides bootstrap SPA redirect URIs to `http://localhost:18081/oidc/callback`

### Tracker gateway

- binds host port `18080 -> 8080`
- changes announce/scrape/base URLs to `http://localhost:18080`
- disables forced HTTPS
- registers `localhost` and `127.0.0.1` as allowed hosts

## Recommendation

Use:

- `docker-compose.yml` for edge-like domain/TLS testing
- `docker-compose.dev-browser.yml` for localhost browser testing
