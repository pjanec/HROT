using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.SystemTests;

/// <summary>
/// One AI-debug API response, decoded from the host's envelope
/// (<c>{ ok, data, error, awaited }</c> — <see cref="Hrot.Editor.DebugApi.ApiResponse"/>).
///
/// <para><b>Why <see cref="JsonNode"/> and not a DTO per endpoint.</b> The host embeds each
/// handler's payload verbatim, so its casing follows whatever built it: the hand-written
/// <c>JsonObject</c>s use lowercase keys (<c>clusterState</c>), while entity dumps are serialized
/// from a DTO and keep PascalCase (<c>NetworkId</c>). One envelope type plus case-insensitive
/// readers handles both; a DTO per endpoint would have to encode that split twice and would rot
/// against a payload change the test does not otherwise care about.</para>
/// </summary>
public sealed record ApiResult(int StatusCode, bool Ok, JsonNode? Data, string? Error)
{
    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    /// <summary>The request that produced this, for assertion messages. Set by the client.</summary>
    public string Request { get; init; } = "";

    /// <summary>
    /// Throws with the request, status and server error when the call failed. Use it at the top of
    /// a test step so a failure names the HTTP call rather than surfacing as a null-reference three
    /// lines later.
    /// </summary>
    public ApiResult EnsureOk()
    {
        if (!Ok)
            throw new McpRequestException($"{Request} failed ({StatusCode}): {Error ?? "no error text"}");
        return this;
    }

    public JsonNode DataOrThrow()
    {
        EnsureOk();
        return Data ?? throw new McpRequestException($"{Request} returned ok with no data payload.");
    }

    /// <summary>Reads a top-level field of the payload, case-insensitively.</summary>
    public JsonNode? Field(string name)
    {
        if (Data is not JsonObject obj) return null;
        if (obj.TryGetPropertyValue(name, out var direct)) return direct;
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    public T? FieldValue<T>(string name)
    {
        var node = Field(name);
        if (node is null) return default;
        try { return node.GetValue<T>(); }
        catch (Exception) { return JsonSerializer.Deserialize<T>(node.ToJsonString(), CaseInsensitive); }
    }

    public string? String(string name) => Field(name)?.GetValue<string>();
    public bool Bool(string name) => FieldValue<bool>(name);
    public double Double(string name) => FieldValue<double>(name);
    public int Int(string name) => FieldValue<int>(name);
    public long Long(string name) => FieldValue<long>(name);

    /// <summary>The payload as an array; empty when the payload is not one.</summary>
    public JsonArray Array() => Data as JsonArray ?? new JsonArray();

    /// <summary>Deserializes the payload into <typeparamref name="T"/>, case-insensitively.</summary>
    public T As<T>() => JsonSerializer.Deserialize<T>(DataOrThrow().ToJsonString(), CaseInsensitive)!;
}

/// <summary>Thrown when an API call fails or returns an unusable payload.</summary>
public sealed class McpRequestException : Exception
{
    public McpRequestException(string message) : base(message) { }
    public McpRequestException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The <c>GET /status</c> payload (measured against <c>DebugApiService.GetStatus</c>).</summary>
public sealed record StatusDto(
    string? Scenario,
    string ClusterState,
    double SimTime,
    float TimeScale,
    bool IsPaused,
    bool InPreview,
    int EntityCount,
    bool Recording);

/// <summary>The <c>GET /sim/state</c> payload, also returned by every /sim and /preview command.</summary>
public sealed record SimStateDto(bool IsPaused, bool InPreview, double TotalTime, float TimeScale);

/// <summary>One row of <c>GET /entities</c>.</summary>
public sealed record EntityRowDto(long NetworkId, string? Name, string[] Components);

/// <summary>The <c>GET /replay/status</c> payload.</summary>
public sealed record ReplayStatusDto(bool ReplayActive, int CurrentFrame, int TotalFrames);
