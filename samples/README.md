# Kun Samples

本目录将包含完整的示例项目，演示如何使用 Kun Framework 构建生产级微服务。

## 计划中的示例

- **Kun.Sample.Catalog**: 商品目录服务，演示 DDD + CQRS + Outbox + gRPC
- **Kun.Sample.Order**: 订单服务，演示 Saga 编排（下单 → 预留库存 → 支付 → 发货）
- **Kun.Sample.AppHost**: .NET Aspire 编排入口，一键拉起全部服务

## 运行示例（待实现）

```bash
cd samples/Kun.Sample.Catalog
dotnet run --project Kun.Sample.AppHost
```

访问 Aspire Dashboard（默认 `http://localhost:15888`）查看所有服务状态与分布式追踪。

## 参与

欢迎提交 PR 补充示例！请遵循 [`.claude/skills/`](../.claude/skills/) 中的框架约定。
