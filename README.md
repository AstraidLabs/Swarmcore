<div align="center">

# BeeTracker

**High-performance BitTorrent tracker built on .NET 10**

[![Build](https://github.com/AstraidLabs/BeeTracker/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/ci.yml)
[![Unit Tests](https://github.com/AstraidLabs/BeeTracker/actions/workflows/unit-tests.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/unit-tests.yml)
[![Integration Tests](https://github.com/AstraidLabs/BeeTracker/actions/workflows/integration-tests.yml/badge.svg?branch=master)](https://github.com/AstraidLabs/BeeTracker/actions/workflows/integration-tests.yml)


[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-preview-239120?style=flat-square&logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1?style=flat-square&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7.4-DC382D?style=flat-square&logo=redis&logoColor=white)](https://redis.io/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?style=flat-square&logo=docker&logoColor=white)](https://www.docker.com/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square)](LICENSE.txt)

> BeeTracker is a production-grade, microservice-based BitTorrent tracker engineered for maximum throughput, minimal latency, and operational reliability.

</div>

---

## Overview

BeeTracker implements the **BitTorrent tracker protocol** (HTTP + UDP/BEP15) as a set of independently deployable services. Runtime peer state lives in a sharded in-memory store, keeping the announce/scrape hot path completely free of database I/O. PostgreSQL owns configuration and audit data; Redis serves as an L2 cache and cross-node coordination layer.

**Five things that make BeeTracker fast and reliable:**

- **Zero database latency on the hot path** — announce/scrape never touches PostgreSQL
- **64-shard in-memory peer store** — lock contention scales linearly with cores, not with load
- **Three-level passkey cache** — L1 (local memory) → L2 (Redis) → L3 (PostgreSQL)
- **Full UDP tracker support (BEP15)** — alongside HTTP for maximum client compatibility
- **Fire-and-forget telemetry** — async batched writes via `Channel<T>`, requests never wait for persistence

---

## Features

| Feature | Description |
|---------|-------------|
| 📡 **Announce & Scrape** | Ultra-lightweight HTTP announce/scrape with strict input validation and peer selection |
| 🔌 **UDP Tracker (BEP15)** | Full UDP protocol support alongside HTTP for maximum client compatibility |
| 🔑 **Access Control** | Passkey-based authentication with L1/L2/L3 policy caching |
| 🛡️ **Rate Limiting** | Per-passkey and per-IP abuse protection with configurable thresholds |
| 📊 **Admin Dashboard** | React 19 admin UI with role-based access, live monitoring, and permission management |
| 📈 **Telemetry** | Async batched event collection and persistence for tracker analytics |
| 🔄 **Cache Coordination** | Redis-based invalidation signaling across distributed nodes |
| 🏗️ **Configuration Service** | Centralized schema ownership with owned PostgreSQL migrations |
| 🩺 **Health Checks** | Startup, liveness, and readiness probes on every service |
| ⚡ **Sharded Peer Store** | 64-shard lock-based `PartitionedRuntimeSwarmStore` for hot-path peer state |

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
