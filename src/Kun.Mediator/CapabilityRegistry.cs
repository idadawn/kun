using System.Collections.Frozen;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Kun.Mediator;

/// <summary>命令或查询的稳定标识与 JSON 请求／响应契约。</summary>
public sealed class CapabilityDescriptor
{
    private readonly string _requestSchema;
    private readonly string _responseSchema;
    /// <summary>从调用方的源生成元数据建立机器可读契约。</summary>
    public CapabilityDescriptor(string id, JsonTypeInfo request, JsonTypeInfo response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        Id = id;
        RequestType = request.Type;
        ResponseType = response.Type;
        _requestSchema = Export(request);
        _responseSchema = Export(response);
    }
    /// <summary>稳定能力标识。</summary>
    public string Id { get; }
    /// <summary>请求 CLR 类型。</summary>
    public Type RequestType { get; }
    /// <summary>响应 CLR 类型。</summary>
    public Type ResponseType { get; }
    /// <summary>请求 Schema 的独立节点。</summary>
    public JsonNode RequestSchema => JsonNode.Parse(_requestSchema)!;
    /// <summary>响应 Schema 的独立节点。</summary>
    public JsonNode ResponseSchema => JsonNode.Parse(_responseSchema)!;
    private static string Export(JsonTypeInfo info)
    {
        var schema = info.GetJsonSchemaAsNode(new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        if (schema is JsonObject obj) obj["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        return schema.ToJsonString();
    }
}
/// <summary>只读能力目录；调用权限与 scope 过滤由上层服务完成。</summary>
public interface ICapabilityRegistry
{
    /// <summary>按稳定 ID 查找能力。</summary>
    CapabilityDescriptor? Find(string id);
    /// <summary>以 ID 顺序枚举全部声明。</summary>
    IReadOnlyList<CapabilityDescriptor> GetAll();
}
internal sealed class CapabilityRegistry(IEnumerable<CapabilityDescriptor> entries) : ICapabilityRegistry
{
    private readonly FrozenDictionary<string, CapabilityDescriptor> _entries = entries.ToFrozenDictionary(e => e.Id, StringComparer.Ordinal);
    public CapabilityDescriptor? Find(string id) => _entries.GetValueOrDefault(id);
    public IReadOnlyList<CapabilityDescriptor> GetAll() => Array.AsReadOnly(_entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray());
}
