# Kun.Gateway

Kun's gateway host and reusable YARP extensions. The assembly can run as the standalone gateway or be referenced by a consumer's ASP.NET Core host.

A consumer registers AddKunGateway with its gateway options and route/cluster configuration, configures external authentication and authorization, and maps the reverse proxy. The built-in transform replaces an authenticated external principal's bearer token with a short-lived RSA S2S token. Keep the internal private key on the gateway only; services validate the public JWKS through Kun.Security.

ForwardedClaimTypes is an explicit allowlist for application claims such as sid, identity_revision and permission. It defaults to empty and cannot override issuer, audience, subject, tenant, roles or token lifetime. Downstream applications still verify the session and authorization revision against their identity authority.

Use the gated internal JWKS mapping on an internal listener or network boundary. Configure permitted public routes explicitly; never proxy arbitrary internal endpoints to browsers. The standalone host remains available through dotnet publish, including Native AOT.

See the repository's docs/security/s2s-jwt.md for the full contract, key handling and validation requirements. Source and package are MIT licensed.
