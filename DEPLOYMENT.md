# BeeTracker Deployment Bootstrap

## Scope

This document defines the supported local/staging bootstrap flow for BeeTracker and the locked production deployment pattern for the current phase.

BeeTracker deployment is split into:

- `tracker-gateway` for tracker protocol traffic
- `admin-service` for admin BFF/auth and admin API
- `configuration-service` for internal configuration writes and schema ownership
- `cache-coordinator` for Redis coordination workers
- `telemetry-ingest` for telemetry persistence
- PostgreSQL as source of truth
- Redis as L2 cache and coordination
- Nginx as the external reverse proxy

## Prerequisites

- Docker Desktop or Docker Engine with Compose support
- .NET 10 SDK only if you want to run services directly outside Docker

## Local Compose Startup

Run the full local stack from the repo root:

```powershell
docker compose up --build
```

Expected public endpoints:

- Tracker plane: `http://localhost`
- Admin plane: `http://localhost:81`

Important internal-only services:

- `configuration-service`
- `cache-coordinator`
- `telemetry-ingest`

These are intentionally not exposed on host ports in the default stack.

## First Admin Login

Local bootstrap admin credentials come from `Tracker.AdminService.Api/appsettings.json`:

- User name: `admin`
- Password: `BeeTracker123!`

Login path:

- `http://localhost:81/account/login`

## Migration Ownership

Schema migration is explicit and owned per schema family:

- `configuration-service`
  - owns configuration, audit, and maintenance schema
- `admin-service`
  - owns ASP.NET Identity and OpenIddict schema under `admin_auth`
- `telemetry-ingest`
  - owns telemetry schema
- `tracker-gateway`
  - does not own schema migration

Services do not use `EnsureCreated`. Startup bootstrap runs `Database.Migrate()` through the owning service before readiness is marked.

## Startup Sequence

Each deployable service follows this startup contract:

1. load typed options
2. validate required configuration
3. wait for required dependencies with bounded retries
4. apply owned migrations if the service owns schema
5. start runtime hosted services
6. mark readiness only after bootstrap succeeds

Readiness implications:

- `configuration-service`, `admin-service`, and `telemetry-ingest` stay not ready until migrations succeed
- `tracker-gateway` waits for PostgreSQL and Redis connectivity, but does not migrate schema
- `cache-coordinator` waits for Redis connectivity before readiness

## Health Endpoints

HTTP services expose:

- `/health/live`
- `/health/ready`
- `/health/startup`

Compose health checks are configured for:

- `postgres`
- `redis`
- `tracker-gateway`
- `admin-service`

## Troubleshooting

### Services stay not ready

Check logs for bootstrap failures:

```powershell
docker compose logs configuration-service
docker compose logs admin-service
docker compose logs tracker-gateway
docker compose logs telemetry-ingest
```

Typical causes:

- PostgreSQL not reachable
- Redis not reachable
- migration failure due to incompatible local database state

### Admin login does not work

Check:

- `admin-service` is healthy
- `http://localhost:81/account/login` is routed by Nginx
- bootstrap user exists in the logs after admin migration/bootstrap

### Tracker traffic does not work

Check:

- `http://localhost/health/ready`
- `tracker-gateway` logs
- Nginx logs and the trusted proxy configuration in compose

## Production Pattern

The recommended production deployment shape for this phase is:

- external Nginx or load balancer in front of multiple `tracker-gateway` nodes
- `admin-service` exposed separately from tracker plane
- `configuration-service` internal-only
- `cache-coordinator` and `telemetry-ingest` internal-only workers
- PostgreSQL and Redis externalized or managed outside the application hosts

Rolling restart behavior:

1. stop sending traffic to the instance
2. wait for drain
3. terminate the process
4. bring the instance back and wait for readiness

Migration strategy:

- run schema-owning services first
- allow them to apply additive migrations
- only then scale or roll application replicas
- do not let every replica race to apply the same migrations

## Notes

- Admin auth is separate from the tracker protocol plane
- Runtime peer/swarm state remains node-local
- PostgreSQL is not used as announce peer runtime storage
- Redis remains L2 cache and coordination only
