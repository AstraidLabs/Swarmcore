<div align="center">
<p align="center">
  <a href="https://github.com/AstraidLabs/BeeTracker">
    <img src="./logo.png" alt="Logo projektu" width="600">
  </a>
</p>

**The BitTorrent tracker that gets out of the way — and lets your swarm fly.**

[![Build](https://github.com/AstraidLabs/BeeTracker/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/ci.yml)
[![Unit Tests](https://github.com/AstraidLabs/BeeTracker/actions/workflows/unit-tests.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/unit-tests.yml)
[![Integration Tests](https://github.com/AstraidLabs/BeeTracker/actions/workflows/integration-tests.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/integration-tests.yml)


[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-preview-239120?style=flat-square&logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1?style=flat-square&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7.4-DC382D?style=flat-square&logo=redis&logoColor=white)](https://redis.io/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat-square&logo=docker&logoColor=white)](https://www.docker.com/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square)](LICENSE.txt)

> Most BitTorrent trackers are either fast *or* manageable. BeeTracker is both — a production-ready tracker built on .NET 10 that handles high announce volumes without breaking a sweat, and gives you a real admin interface to actually run it. Free, open-source, and `docker compose up` away from running.

</div>

---

## Why BeeTracker?

You've seen the options. Static configs, scripts held together with duct tape, trackers that haven't seen a commit since 2014. BeeTracker is different — engineered from the ground up for modern infrastructure, with a clean architecture that scales and an operational experience that doesn't make you want to quit.

**Zero database hits on the announce path.** Peer state lives entirely in a 64-shard in-memory store. Requests come in, responses go out — no disk, no waiting. PostgreSQL handles what it's good at (config, audit, telemetry). Redis handles the rest. Your hot path stays hot.

### What you get

| | |
|---|---|
| 📡 **HTTP + UDP Announce & Scrape** | Full BitTorrent tracker protocol — HTTP and UDP (BEP15) both supported out of the box, covering every major client |
| 🔑 **Passkey Access Control** | Per-user passkey authentication with a three-level policy cache (memory → Redis → PostgreSQL) so auth never slows you down |
| 🛡️ **Rate Limiting** | Per-passkey and per-IP abuse protection. Configure thresholds, not workarounds |
| 📈 **Built-in Telemetry** | Every announce event is captured asynchronously — no request waits for a write. Analytics without the overhead |
| 🩺 **Health & Readiness Probes** | Startup, liveness, and readiness checks on every service, ready for any orchestration layer |

### What most trackers don't have — BeeTracker does

| | |
|---|---|
| 📊 **Real Admin Dashboard** | A proper React 19 UI with role-based access control, live swarm monitoring, and user management. Not a config file. An actual interface |
| 🔄 **Distributed Cache Coordination** | Running multiple tracker nodes? Redis-based invalidation keeps every node's policy cache in sync automatically, no manual intervention needed |

---

---

## Architecture

### System Diagram

```
                        ┌─────────────────────┐
                        │     Nginx Proxy      │
                        │  :80 tracker  :81 admin│
                        └────────┬────────────┘
                                 │
              ┌──────────────────┼──────────────────┐
              │                  │                  │
     ┌────────▼──────┐  ┌────────▼──────┐  ┌───────▼────────┐
     │ Tracker       │  │ Admin         │  │ Configuration  │
     │ Gateway       │  │ Service       │  │ Service        │
     │ :8080 HTTP    │  │ :8080 API+UI  │  │ (internal)     │
     │ :6969 UDP     │  │               │  │                │
     └───────┬───────┘  └───────┬───────┘  └───────┬────────┘
             │                  │                   │
     ┌───────▼───────────────────▼───────────────────▼────────┐
     │                PostgreSQL (persistent)                  │
     │        config · audit · telemetry · users              │
     └────────────────────────────┬───────────────────────────┘
                                  │
     ┌────────────────────────────▼───────────────────────────┐
     │                   Redis 7.4 (L2 cache)                 │
     │           policy cache · cache invalidation            │
     └────────────────────────────────────────────────────────┘

  ┌─────────────────────┐    ┌─────────────────────┐
  │ Telemetry Ingest    │    │ Cache Coordinator   │
  │ (background worker) │    │ (background worker) │
  └─────────────────────┘    └─────────────────────┘
```

### How It Works

**Announce / Scrape flow:**

1. A BitTorrent client sends an HTTP or UDP request → Nginx routes it to Tracker Gateway
2. Gateway validates the passkey: L1 memory → L2 Redis → L3 PostgreSQL (cold path only, then cached)
3. Peer state is read/written in the 64-shard in-memory store — no disk I/O on this path
4. The response is BEncoded and returned immediately
5. The event is dropped onto a `Channel<T>`; TelemetryIngest persists it asynchronously in the background

**Cache invalidation:**

When configuration changes, Configuration Service writes an invalidation signal to Redis. The Cache Coordinator worker picks it up and evicts stale L1/L2 entries across all running nodes.

**Admin operations:**

Admin Service hosts both the BFF API and the compiled React 19 UI. All writes go through Configuration Service to preserve schema ownership boundaries. Authentication is handled via OpenID Connect.

---

## Deployment

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) & Docker Compose
- Ports `80` and `81` available

### Run locally

```bash
git clone https://github.com/AstraidLabs/BeeTracker.git
cd BeeTracker
docker compose up --build
```

| Endpoint | URL | Description |
|----------|-----|-------------|
| Tracker (HTTP) | `http://localhost` | Announce / Scrape |
| Tracker (UDP) | `udp://localhost:6969` | UDP Tracker |
| Admin UI | `http://localhost:81` | Admin Dashboard |

**Default admin credentials:**

```
Username: admin
Password: BeeTracker123!
```

> ⚠️ Change the default credentials before any production deployment.

### Services

| Service | Port | Role |
|---------|------|------|
| `tracker-gateway` | 8080 / UDP 6969 | Announce, scrape, UDP tracker |
| `admin-service` | 8080 | Admin API, OIDC auth, React UI |
| `configuration-service` | internal | Config ownership, DB migrations |
| `telemetry-ingest` | internal | Async telemetry persistence |
| `cache-coordinator` | internal | Redis cache invalidation worker |

### Production

For full production deployment guidance see [`DEPLOYMENT.md`](DEPLOYMENT.md). Key recommendations:

- Nginx reverse proxy with TLS termination
- Multiple `tracker-gateway` replicas behind a load balancer (peer state is node-local by design)
- PostgreSQL 17+ and Redis 7.4+ as managed or self-hosted services
- Configure `PROD_DEPLOY_HOST` and `PROD_DEPLOY_KEY` secrets in GitHub to enable the production deployment workflow

---

## Development & Testing

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview quality)
- Docker — required for integration tests (Testcontainers)

### Run tests

```bash
# All tests
dotnet test

# Unit tests only
dotnet test BeeTracker.slnx --filter "FullyQualifiedName~UnitTests|FullyQualifiedName~ArchitectureTests"

# Integration tests (requires Docker)
dotnet test BeeTracker.slnx --filter "FullyQualifiedName~IntegrationTests|FullyQualifiedName~SmokeTests"

# Single test project
dotnet test tests/Tracker.Gateway.UnitTests

# Benchmarks
dotnet run -c Release --project benchmarks/Tracker.Gateway.Benchmarks
```

### Project structure

```
BeeTracker/
├── src/
│   ├── BuildingBlocks/          # Shared abstractions & infrastructure
│   ├── Contracts/               # Service boundary contracts
│   ├── Services/                # Deployable microservices
│   │   ├── Tracker.Gateway/     # HTTP + UDP tracker
│   │   ├── Tracker.AdminService/# Admin BFF + React UI
│   │   ├── Tracker.ConfigurationService/
│   │   ├── Tracker.CacheCoordinator/
│   │   ├── Tracker.TelemetryIngest/
│   │   └── Tracker.UdpTracker/
│   └── Shared/                  # Cross-cutting concerns
│       ├── Caching.Redis/
│       ├── Hosting/
│       ├── Persistence.Postgres/
│       └── Serialization.BEncoding/
├── tests/                       # Unit & integration tests
├── benchmarks/                  # BenchmarkDotNet performance tests
└── deploy/nginx/                # Reverse proxy configuration
```

---

## FAQ

**Do I need the .NET SDK to run BeeTracker?**
No — Docker is sufficient for production. The .NET SDK is only needed for local development and running tests directly.

**Can I run multiple Tracker Gateway instances?**
Yes. Runtime peer state is intentionally node-local. Peers connected to different nodes will discover each other through subsequent announce cycles, which is standard BitTorrent tracker behavior. Use a load balancer in front of multiple `tracker-gateway` replicas.

**Why both PostgreSQL and Redis?**
PostgreSQL is the single source of truth for configuration, users, audit logs, and telemetry. Redis is strictly an L2 cache — it accelerates policy reads and coordinates cache invalidation between nodes. They serve different roles, not duplicate ones.

**Is UDP tracker (BEP15) fully supported?**
Yes. BeeTracker implements the complete UDP tracker protocol as defined in BEP15, alongside HTTP.

**How does passkey access control work?**
Each client authenticates via a passkey embedded in the announce URL. The associated access policy is cached at three levels — L1 (local process memory), L2 (Redis), L3 (PostgreSQL) — so that repeated announces incur near-zero lookup overhead.

**Where is admin authentication handled?**
Admin Service uses OpenID Connect (OIDC) for authentication. User identities and role assignments are managed through the integrated Identity/RBAC layer.

**How do I enable HTTPS?**
See [`docs/deployment/nginx-https.md`](docs/deployment/nginx-https.md) for a step-by-step guide on configuring TLS termination at the Nginx layer.

---

<div align="center">

Made with ❤️ by [AstraidLabs](https://github.com/AstraidLabs)

</div>
