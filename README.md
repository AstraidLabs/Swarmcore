<div align="center">

# 🌐 Swarmcore

**High-performance, production-grade BitTorrent tracker backend**

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-preview-239120?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-source%20of%20truth-4169E1?style=for-the-badge&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7.4-DC382D?style=for-the-badge&logo=redis&logoColor=white)](https://redis.io/)
[![Docker](https://img.shields.io/badge/Docker-compose-2496ED?style=for-the-badge&logo=docker&logoColor=white)](https://www.docker.com/)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE.txt)

> Swarmcore is a distributed, service-based BitTorrent tracker engineered for low-latency announce/scrape operations, clean architectural boundaries, and production reliability.

</div>

---

## 📖 Overview

Swarmcore implements the **BitTorrent tracker protocol** (HTTP + UDP/BEP15) as a set of independently deployable microservices. It separates runtime peer state (in-process, sharded) from persistent configuration and audit data (PostgreSQL), using Redis purely for L2 caching and cross-node coordination — resulting in a lean, scalable, and observable tracker stack.

---

## ✨ Features

| Feature | Description |
|--------|-------------|
| 📡 **Announce & Scrape** | Ultra-lightweight HTTP announce/scrape with strict input validation and peer selection |
| 🔌 **UDP Tracker (BEP15)** | Full UDP protocol support alongside HTTP for maximum client compatibility |
| 🔑 **Access Control** | Passkey-based authentication with L1/L2/L3 policy caching |
| 🛡️ **Rate Limiting** | Per-passkey and per-IP abuse protection with configurable thresholds |
| 📊 **Admin Dashboard** | React 19 admin UI with role-based access, live monitoring, and permission management |
| 📈 **Telemetry** | Async batched event collection and persistence for tracker analytics |
| 🔄 **Cache Coordination** | Redis-based invalidation signaling across distributed nodes |
| 🏗️ **Configuration Service** | Centralized schema ownership with owned PostgreSQL migrations |
| 🩺 **Health Checks** | Startup, liveness, and readiness probes on every service |
| ⚡ **Sharded In-Memory Store** | 64-shard lock-based `PartitionedRuntimeSwarmStore` for hot-path peer state |

---

## 🏛️ Architecture

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

---

## 🗂️ Project Structure

```
Swarmcore/
├── src/
│   ├── BuildingBlocks/          # Shared abstractions & infrastructure
│   ├── Contracts/               # Service boundary contracts
│   │   ├── Admin/
│   │   ├── Configuration/
│   │   ├── Runtime/             # Announce/scrape DTOs
│   │   └── Telemetry/
│   ├── Services/                # Deployable microservices
│   │   ├── Tracker.Gateway/     # 📡 HTTP + UDP tracker
│   │   ├── Tracker.AdminService/# 🖥️  Admin BFF + React UI
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

## 🛠️ Tech Stack

### Backend
| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 · ASP.NET Core Minimal APIs |
| Language | C# (preview) · Nullable enabled |
| Database | PostgreSQL + Entity Framework Core (Npgsql) |
| Cache | Redis 7.4 |
| Protocol | Custom BEncoding (BEP3) · UDP/BEP15 |
| Messaging | `Channel<T>` for telemetry batching |

### Frontend (Admin UI)
| Layer | Technology |
|-------|-----------|
| Framework | React 19.1 |
| Routing | React Router 7.6 |
| Auth | OpenID Connect (oidc-client-ts) |
| Styling | Tailwind CSS 3.4 |
| Build | Vite 6.3 · TypeScript 5.8 |

### Infrastructure
| Component | Technology |
|-----------|-----------|
| Proxy | Nginx 1.29 (Alpine) |
| Containers | Docker & Docker Compose |

---

## 🚀 Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) & Docker Compose
- Ports `80` and `81` available

### Run locally

```bash
git clone https://github.com/AstraidLabs/Swarmcore.git
cd Swarmcore
docker compose up --build
```

| Endpoint | URL | Description |
|---------|-----|-------------|
| Tracker (HTTP) | `http://localhost` | Announce / Scrape |
| Tracker (UDP) | `udp://localhost:6969` | UDP Tracker |
| Admin UI | `http://localhost:81` | Admin Dashboard |

**Default admin credentials:**

```
Username: admin
Password: Swarmcore123!
```

> ⚠️ Change the default credentials before any production deployment.

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Tracker.Gateway.UnitTests

# Run integration tests
dotnet test tests/Tracker.Gateway.IntegrationTests

# Run benchmarks
dotnet run -c Release --project benchmarks/Tracker.Gateway.Benchmarks
```

---

## 📋 Services

| Service | Port | Role |
|---------|------|------|
| `tracker-gateway` | 8080 / UDP 6969 | Announce, scrape, UDP tracker |
| `admin-service` | 8080 | Admin API, OIDC auth, React UI |
| `configuration-service` | internal | Config ownership, DB migrations |
| `telemetry-ingest` | internal | Async telemetry persistence |
| `cache-coordinator` | internal | Redis cache invalidation worker |

---

## 📐 Design Principles

- **No sync DB in hot path** — announce/scrape never blocks on PostgreSQL
- **No `SaveChanges()` in request scope** — telemetry is fire-and-forget via channels
- **No cross-node peer routing** — runtime state is node-local by design
- **PostgreSQL = config & audit truth** — not a runtime data store
- **Redis = L2 cache only** — not a message broker or primary store
- **64-shard peer store** — minimizes lock contention under high concurrency

---

## 📄 License

Distributed under the **GNU General Public License v3.0**. See [`LICENSE.txt`](LICENSE.txt) for details.

---

<div align="center">

Made with ❤️ by [AstraidLabs](https://github.com/AstraidLabs)

</div>
