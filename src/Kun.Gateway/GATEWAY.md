# Kun.Gateway

Kun's gateway host and reusable YARP extensions. The assembly can run as the standalone gateway or be referenced by a consumer's ASP.NET Core host.

A consumer registers AddKunGateway with its gateway options and route/cluster configuration, configures external authentication and authorization, and maps the reverse proxy. The built-in transform replaces an authenticated external principal's bearer token with a short-lived RSA S2S token. Keep the internal private key on the gateway only; services validate the public JWKS through Kun.Security.

The issuer uses a closed canonical claim set: aud/roles arrays, user/service principal kind, UUID tenant/session/membership/project identifiers, scope, revision and a fresh UUID jti. A platform tenant is explicit JSON null; user sessions/revisions are required, and service session_id equals jti. Lifetimes are 1–300 seconds. Supply authoritative context through IS2sPrincipalResolver and choose the downstream audience through IssueForAudienceAsync. Missing identity fields fail closed. Arbitrary permission/email/trace claims are not copied; the old forwarding/trace switches do not expand this contract. Downstream applications still verify session and authorization revision against their authority.

Use the gated internal JWKS mapping on an internal listener or network boundary. Configure permitted public routes explicitly; never proxy arbitrary internal endpoints to browsers. The standalone host remains available through dotnet publish, including Native AOT.

## Route and proxy boundary

`AddKunGateway` registers the authenticated fallback policy and fills missing YARP route policies with `Default` authorization and `kun-default` rate limiting (100 requests/minute per tenant, per process). A public route must explicitly set `AuthorizationPolicy: "Anonymous"`. Existing named authorization and rate policies remain effective. Rejection returns HTTP 429 and `Retry-After` when the limiter supplies a retry time. Anonymous routes discard unvalidated incoming Authorization headers.

A consuming host must call `UseForwardedHeaders()`, `UseAuthentication()`, `UseAuthorization()` and `UseRateLimiter()` in that order before `MapReverseProxy()`. The standalone host already does this. Forwarded headers are disabled by default: set `TrustedProxyAddresses` or `TrustedProxyNetworks` to the actual ingress peers and keep `ForwardLimit` bounded. The IP limiter reads only the normalized socket address; it never parses raw X-Forwarded-For.

## Signing keys and telemetry

Replace the private PEM at `SigningKeyPath` atomically. The gateway checks it on signing/JWKS access, at most once per `KeyReloadInterval` (default one second), and shares the resulting key ring between both operations. Old public keys remain for `KeyOverlapPeriod` (default five minutes); old private keys are disposed immediately. Invalid replacements retain the last valid key and emit a warning. `PublicKeyPath` is retained for legacy standalone JWKS mapping; AddKunGateway derives JWKS directly from its signing ring. Restarting loses the in-memory overlap history, so finish overlap before a restart or coordinate rotation across replicas.

The standalone host registers `AddKunObservability` under service name `kun-gateway` with EF instrumentation disabled. `/healthz`, `/readyz` and JWKS are explicitly anonymous; the internal JWKS endpoint retains its existing environment/additional gate. Client-credentials authentication and machine-principal storage belong to the consuming identity authority; this host exchanges already validated external principals.

See the repository's docs/security/s2s-jwt.md for the full contract, key handling and validation requirements. Source and package are MIT licensed.

## 普通包的 NativeAOT 消费

`MapKunJwks` 和 `MapKunInternalJwks` 使用显式 `RequestDelegate`，无需消费者重新生成库内部端点或开启反射 JSON。两个入口保持 `AllowAnonymous` 和成功响应的 `Cache-Control: no-store`；`additionalGate` 拒绝时返回 404，内部入口在非 Development 环境仍须显式设置 `ExposeInternalJwks=true`。不要通过关闭 AOT 诊断来规避端点绑定错误。

仓库的 `tests/Kun.Integration.AotTests/GatewayPackageAotPublishTests.cs` 先普通打包本库及依赖，再从仓库外以 PackageReference 发布独立 Linux 原生宿主，执行门控与密钥热轮换回归。它与本库自身的 `dotnet publish` 门禁共同覆盖宿主和包消费两条路径。
