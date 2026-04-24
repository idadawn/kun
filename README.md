<div align="center">

# Kun Framework

**From the deep, to the sky.** — 出于深渊，翱翔云端

*一款为 .NET 10 打造的现代化云原生微服务开发框架*

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Build Status](https://img.shields.io/badge/build-pending-lightgrey.svg)]()

</div>

---

> **"北冥有鱼，其名为鲲。鲲之大，不知其几千里也；化而为鸟，其名为鹏。"** ——《庄子·逍遥游》

Kun Framework 的名字源自《庄子》。"鲲"寓意框架从轻量级单体（深渊之鲲）能无缝演化为支撑海量并发的分布式微服务架构（云端之鹏）——这正是本框架追求的演进能力。

## ✨ 核心特性

- 🚀 **AOT-Ready 分层策略** —— 网关、gRPC、CQRS 调度器严格 AOT 兼容；数据层 trimming-safe。定位诚实、落地可行。
- 🧱 **严格 DDD** —— 领域层零基础设施依赖，内置聚合根、值对象、规约模式、领域事件。
- ⚡ **双引擎数据访问** —— EF Core 10（写侧 DDD）+ Dapper AOT（读侧高性能），与 CQRS 天然契合。
- 🔌 **多协议 RPC** —— Minimal API 对外、gRPC 内部、gRPC-Web 浏览器友好。
- 📬 **可靠事件驱动** —— 原生 Outbox 模式，业务与事件同事务原子落库；W3C Trace Context 自动传播。
- 🎭 **内置 Saga 编排器** —— 跨服务长事务、补偿、超时、幂等一体化。
- 🔐 **零信任服务间通信** —— 网关签发短生命周期 S2S JWT，配合 K8s NetworkPolicy 纵深防御。
- 🏢 **多租户 First-Class** —— `ITenantContext` 抽象贯穿全栈。
- 📊 **OpenTelemetry 端到端** —— 追踪、指标、日志开箱即用。

## 🏗️ 架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                    External Clients (Web / Mobile)               │
└───────────────────────────┬─────────────────────────────────────┘
                            │ HTTP/JSON + External JWT
                            ▼
            ┌───────────────────────────────┐
            │       Kun.Gateway (YARP)       │
            │   · External JWT validation    │
            │   · S2S JWT issuance (60s TTL) │
            │   · Rate limiting / gRPC-Web   │
            └───────────────────────────────┘
                            │ Authorization: Bearer {S2S-JWT}
                            ▼
┌────────────────────┐  gRPC   ┌────────────────────┐
│ Service A          │◄───────►│ Service B          │
│ · Minimal API      │         │ · gRPC Endpoint    │
│ · Kun.Mediator     │         │ · Kun.Mediator     │
│ · EF Core (write)  │         │ · EF Core (write)  │
│ · Dapper AOT (read)│         │ · Dapper AOT (read)│
│ · Outbox + Saga    │         │ · Outbox + Saga    │
└─────────┬──────────┘         └─────────┬──────────┘
          │                              │
          ▼                              ▼
  ┌────────────┐  Integration Events  ┌────────────┐
  │ PostgreSQL │◄────── RabbitMQ ────►│ PostgreSQL │
  └────────────┘                      └────────────┘
```

## 📦 模块矩阵

| 模块 | 职责 | 是否 AOT |
| :--- | :--- | :---: |
| `Kun.Core` | 领域抽象、CQRS 接口、Result、`ITenantContext` | ✅ |
| `Kun.Mediator` | `FrozenDictionary` 无反射调度、Pipeline Behaviors | ✅ |
| `Kun.Infrastructure` | 仓储/UoW/规约接口、`IProcessedMessageStore` | ✅ |
| `Kun.EntityFrameworkCore` | 写侧 EF Core 实现、Outbox 落库、租户过滤器 | ⚠️ trim-safe |
| `Kun.Dapper` | 读侧 Dapper AOT 封装 | ✅ |
| `Kun.EventBus` | 领域/集成事件抽象、Outbox 发布器 | ✅ |
| `Kun.EventBus.RabbitMQ` | RabbitMQ 传输、消费端幂等 | ✅ |
| `Kun.Saga` | Saga Orchestration 编排器 | ✅ |
| `Kun.Storage` | 对象存储抽象 + S3/MinIO/本地后端 + 活跃-MIME 附件强制 | ⚠️ trim-safe |
| `Kun.Audit` | 操作审计日志 + SHA-256 哈希链 + Mediator Pipeline Behavior | ✅ |
| `Kun.Security` | S2S JWT 签发/校验、租户上下文注入 | ✅ |
| `Kun.Validation` | SourceGen 验证器 | ✅ |
| `Kun.AspNetCore` | Minimal API 扩展、Pipeline Behaviors | ✅ |
| `Kun.Grpc` | gRPC 拦截器、Result→RpcException 映射 | ✅ |
| `Kun.Gateway` | YARP 扩展、S2S 签发、限流 | ✅ |

## Quick start

Start the full sample stack — PostgreSQL, RabbitMQ, Redis, Catalog API, Order API, and Gateway —
with a single command:

```bash
git clone https://github.com/idadawn/kun.git
cd kun
dotnet workload install aspire   # once per machine
dotnet restore
dotnet run --project samples/Kun.Sample.AppHost
```

The Aspire Dashboard opens at **http://localhost:15888**. All service URLs and ports are visible
in the Dashboard Resources panel. See [`samples/Kun.Sample.AppHost/README.md`](samples/Kun.Sample.AppHost/README.md)
for details.

## Docs

- [Getting started](docs/getting-started.md) — 10-minute hands-on tutorial: run the samples, trigger all three saga paths, observe traces
- [Why CQRS?](docs/architecture/why-cqrs.md) — write/read split, EF Core vs Dapper, pipeline behaviors
- [Why Outbox?](docs/architecture/why-outbox.md) — dual-write problem, atomic enqueue, background publisher, consumer idempotency
- [Why Saga Orchestration?](docs/architecture/why-saga.md) — orchestration vs choreography, three recovery paths, idempotency requirements
- [Architecture whitepaper](docs/whitepaper.md) — full design philosophy, module details, code examples
- [Module map](docs/architecture/module-map.md) — dependency graph and call paths
- [Contributing](CONTRIBUTING.md)

## Project status

| Milestone | Version | Status |
|---|---|---|
| M1 — Core abstractions, Mediator, Validation | `0.1.0-alpha.1` | Done |
| M2 — EF Core write side, Dapper read side, Outbox atomicity | `0.2.0-alpha.1` | Done |
| M3 — RabbitMQ event bus, Outbox publisher, consumer idempotency | `0.3.0-alpha.1` | Done |
| M4 — Saga orchestrator, step executor, timeout detector | `0.5.0-beta.1` | Done |
| M5 — ASP.NET Core host, gRPC, Gateway, S2S JWT | `1.0.0-rc.1` | Done |
| M6 — Sample apps, OpenTelemetry, documentation | `1.0.0` | In progress |

## 🚀 快速开始

### 前置要求

- .NET 10 SDK (`10.0.100` 或更高)
- Docker（本地运行 PostgreSQL / RabbitMQ / Redis）
- PostgreSQL 16+ 或 MySQL 8+

### 克隆与构建

```bash
git clone https://github.com/idadawn/kun.git
cd kun
dotnet restore
dotnet build
dotnet test
```

### 运行示例

所有示例通过 .NET Aspire 一键启动：

```bash
dotnet run --project samples/Kun.Sample.AppHost
```

访问 Aspire Dashboard（默认 `http://localhost:15888`）查看所有服务状态与分布式追踪。

## 📚 文档

- [Getting started](docs/getting-started.md) — 10 分钟上手教程
- [Why CQRS?](docs/architecture/why-cqrs.md) · [Why Outbox?](docs/architecture/why-outbox.md) · [Why Saga?](docs/architecture/why-saga.md) — 架构决策说明
- [📖 架构设计白皮书](docs/whitepaper.md) — 完整设计哲学、模块细节、代码示例
- [🗺️ 模块地图](docs/architecture/module-map.md) — 依赖关系与调用路径
- [🤝 贡献指南](CONTRIBUTING.md)

## 🧭 Claude Skills 内置

本仓库在 `.claude/skills/` 下提供 6 个专为 Kun 定制的技能包。使用 Claude Code 时会自动生效，确保生成的代码严格遵循框架约定：

| Skill | 作用 |
| :--- | :--- |
| `kun-framework-conventions` | 总纲 —— 命名、分层、AOT 规则 |
| `kun-ddd-modeling` | 聚合根、值对象、领域事件设计 |
| `kun-cqrs-mediator` | Handler 编写、Pipeline Behaviors |
| `kun-outbox-events` | 集成事件发布、消费幂等、事件 ID 注册 |
| `kun-saga` | Saga 步骤与补偿、幂等、超时 |
| `kun-testing` | 单元/集成/契约三层测试 |

## 🗺️ 演进路线

- **v1.0 深渊之鲲**: 核心构建块、双引擎 ORM、Outbox、Saga、S2S 安全、多租户抽象
- **v1.5 乘风破浪**: gRPC 完整 AOT、Proto SourceGen、SaaS 租户隔离策略、Saga 可视化
- **v2.0 翱翔云端**: K8s 部署模板、Service Mesh 集成、EF Core 完整 AOT、多区域部署

## 📝 许可证

本项目采用 [MIT 许可证](LICENSE)。

---

<div align="center">

**Kun Framework** —— 化繁为简，助您的 .NET 架构如鲲鹏般扶摇直上九万里。

[白皮书](docs/whitepaper.md) · [贡献](CONTRIBUTING.md) · [路线图](#-演进路线)

</div>
