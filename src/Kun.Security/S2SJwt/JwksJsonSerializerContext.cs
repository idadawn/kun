using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Kun.Security.S2SJwt;

[JsonSerializable(typeof(JsonWebKeySet))]
internal sealed partial class JwksJsonSerializerContext : JsonSerializerContext;
