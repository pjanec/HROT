using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.SystemTests;

/// <summary>
/// Typed HTTP wrapper over the AI-debug (MCP) API — one method per route registered in
/// <c>DebugApiHost.BuildRoutes</c> (47 routes, enumerated from that single registration site).
///
/// <para><b>This client is where the paid-for gotchas live</b>, so no test rediscovers them:
/// <c>/replay/load</c> takes <c>fdpPath</c> (not <c>path</c>); <c>/scenario/load</c> can await
/// readiness itself via <c>waitForReady</c> rather than being polled from the test; entering
/// preview twice is gated by <see cref="EnterPreviewIfNeededAsync"/>.</para>
///
/// <para><b>Every method returns <see cref="ApiResult"/> rather than throwing on a non-2xx</b> —
/// several smoke cases assert on a REJECTION (a bad condition, an unknown entity), and a client
/// that threw would make the negative cases the awkward path. Call <see cref="ApiResult.EnsureOk"/>
/// when the call is expected to succeed.</para>
/// </summary>
public sealed class McpClient : IDisposable
{
    private readonly HttpClient _http;

    public Uri BaseUrl { get; }

    /// <summary>
    /// Supplied by <see cref="EditorProcessFixture"/>: called when the transport cannot reach the
    /// editor, to say whether the process has DIED and what it last printed. Without it a crashed
    /// editor surfaces as a bare "connection refused" on every remaining case, which names the
    /// symptom and hides the cause.
    /// </summary>
    public Func<string?>? DiagnoseUnreachable { get; set; }

    public McpClient(Uri baseUrl, TimeSpan? timeout = null)
    {
        BaseUrl = baseUrl;
        _http = new HttpClient { BaseAddress = baseUrl, Timeout = timeout ?? TimeSpan.FromSeconds(60) };
    }

    // ── Group A — status / lifecycle ───────────────────────────────────────────

    public Task<ApiResult> GetStatusAsync(CancellationToken ct = default) => GetAsync("/status", ct);

    /// <summary>
    /// <c>POST /shutdown</c>. ⚠ In the editor's wiring the shutdown callback is a no-op, so this
    /// answers <c>ok</c> without stopping anything — the fixture kills the process tree instead.
    /// Kept for completeness of the surface, not used for teardown.
    /// </summary>
    public Task<ApiResult> ShutdownAsync(CancellationToken ct = default) => PostAsync("/shutdown", null, ct);

    // ── Group B — entities ─────────────────────────────────────────────────────

    public Task<ApiResult> ListEntitiesAsync(string? component = null, string? near = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(component)) q.Add($"component={Uri.EscapeDataString(component)}");
        if (!string.IsNullOrWhiteSpace(near)) q.Add($"near={Uri.EscapeDataString(near)}");
        return GetAsync("/entities" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), ct);
    }

    public Task<ApiResult> GetEntityAsync(long networkId, CancellationToken ct = default)
        => GetAsync($"/entities/{networkId}", ct);

    // ── Group C / J — event history and logs ───────────────────────────────────

    public Task<ApiResult> GetEventsAsync(string bus = "world", string? type = null, uint since = 0, int? max = null, CancellationToken ct = default)
    {
        var q = new List<string> { $"bus={Uri.EscapeDataString(bus)}" };
        if (!string.IsNullOrWhiteSpace(type)) q.Add($"type={Uri.EscapeDataString(type)}");
        if (since > 0) q.Add($"since={since}");
        if (max is not null) q.Add($"max={max}");
        return GetAsync("/events?" + string.Join("&", q), ct);
    }

    public Task<ApiResult> GetLogsAsync(string? level = null, string? logger = null, string? since = null, int? max = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(level)) q.Add($"level={Uri.EscapeDataString(level)}");
        if (!string.IsNullOrWhiteSpace(logger)) q.Add($"logger={Uri.EscapeDataString(logger)}");
        if (!string.IsNullOrWhiteSpace(since)) q.Add($"since={Uri.EscapeDataString(since)}");
        if (max is not null) q.Add($"max={max}");
        return GetAsync("/logs" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), ct);
    }

    // ── Group D — sim / preview / time ─────────────────────────────────────────

    public Task<ApiResult> GetSimStateAsync(CancellationToken ct = default) => GetAsync("/sim/state", ct);
    public Task<ApiResult> PlayAsync(CancellationToken ct = default) => PostAsync("/sim/play", null, ct);
    public Task<ApiResult> PauseAsync(CancellationToken ct = default) => PostAsync("/sim/pause", null, ct);

    public Task<ApiResult> StepAsync(int count = 1, CancellationToken ct = default)
        => PostAsync("/sim/step", new JsonObject { ["count"] = count }, ct);

    public Task<ApiResult> SetTimeScaleAsync(float scale, CancellationToken ct = default)
        => PostAsync("/sim/timescale", new JsonObject { ["scale"] = scale }, ct);

    public Task<ApiResult> EnterPreviewAsync(bool startPaused = false, CancellationToken ct = default)
        => PostAsync("/preview/enter", new JsonObject { ["startPaused"] = startPaused }, ct);

    public Task<ApiResult> ExitPreviewAsync(CancellationToken ct = default) => PostAsync("/preview/exit", null, ct);

    /// <summary>
    /// Enters preview only when not already in it. The service-side <c>EnterPreview</c> is itself
    /// guarded, but <c>/recording/start</c> is NOT — it throws when the session is already
    /// previewing — so tests that mix the two need this gate to stay order-independent.
    /// </summary>
    public async Task<ApiResult> EnterPreviewIfNeededAsync(bool startPaused = false, CancellationToken ct = default)
    {
        var state = await GetSimStateAsync(ct).ConfigureAwait(false);
        if (state.Ok && state.Bool("inPreview")) return state;
        return await EnterPreviewAsync(startPaused, ct).ConfigureAwait(false);
    }

    // ── Group E — scenarios ────────────────────────────────────────────────────

    public Task<ApiResult> ListScenariosAsync(CancellationToken ct = default) => GetAsync("/scenarios", ct);

    /// <summary>
    /// <c>POST /scenario/load</c>. With <paramref name="waitForReady"/> the HOST polls the cluster
    /// state to <c>OperatingEdit</c> across kernel ticks (30 s cap) and answers only when the world
    /// is actually loaded — always prefer it over polling from the test, which cannot see the
    /// cluster state directly.
    /// </summary>
    public Task<ApiResult> LoadScenarioAsync(string name, bool waitForReady = true, CancellationToken ct = default)
        => PostAsync("/scenario/load", new JsonObject { ["name"] = name, ["waitForReady"] = waitForReady }, ct);

    public Task<ApiResult> SaveScenarioAsync(string name, CancellationToken ct = default)
        => PostAsync("/scenario/save", new JsonObject { ["name"] = name }, ct);

    // ── Group F — commands, discovery, spawn ───────────────────────────────────

    public Task<ApiResult> ListCommandsAsync(CancellationToken ct = default) => GetAsync("/commands", ct);
    public Task<ApiResult> ListComponentsAsync(CancellationToken ct = default) => GetAsync("/components", ct);

    public Task<ApiResult> SendCommandAsync(string eventType, JsonNode? payload = null, bool wait = false, CancellationToken ct = default)
        => PostAsync("/entities/command", new JsonObject
        {
            ["eventType"] = eventType,
            ["payload"] = payload?.DeepClone(),
            ["wait"] = wait,
        }, ct);

    public Task<ApiResult> SpawnEntityAsync(long tkbType, JsonNode? transform = null, JsonNode? components = null, string? attributesJson = null, CancellationToken ct = default)
        => PostAsync("/entities/spawn", new JsonObject
        {
            ["tkbType"] = tkbType,
            ["transform"] = transform?.DeepClone(),
            ["components"] = components?.DeepClone(),
            ["attributesJson"] = attributesJson,
        }, ct);

    // ── Group P.0 / S — discovery WITH SCHEMA (MX4a, MX7) ──────────────────────

    /// <summary>
    /// <c>GET /behaviors</c> — the behaviours available, each with its param-DTO JSON schema.
    /// Key it by <paramref name="tkbType"/> ("what can this KIND of entity do") or by
    /// <paramref name="entityId"/> ("what can THIS entity do" — mission-combo parity); with neither,
    /// every registered behaviour.
    /// </summary>
    public Task<ApiResult> GetBehaviorsAsync(long? tkbType = null, long? entityId = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (tkbType is not null) q.Add($"tkbType={tkbType}");
        if (entityId is not null) q.Add($"entityId={entityId}");
        return GetAsync("/behaviors" + (q.Count > 0 ? "?" + string.Join("&", q) : ""), ct);
    }

    /// <summary>
    /// <c>GET /breakpoint-types</c> — every condition arm a breakpoint may use, with its param
    /// schema. This is what turns authoring a <c>SearchPredicateDto</c> from guesswork into lookup.
    /// </summary>
    public Task<ApiResult> GetBreakpointTypesAsync(CancellationToken ct = default)
        => GetAsync("/breakpoint-types", ct);

    // ── Group M — TKB catalog ──────────────────────────────────────────────────

    public Task<ApiResult> ListTkbTypesAsync(string? category = null, CancellationToken ct = default)
        => GetAsync("/tkb/types" + (string.IsNullOrWhiteSpace(category) ? "" : $"?category={Uri.EscapeDataString(category)}"), ct);

    public Task<ApiResult> GetTkbTypeAsync(long tkbType, CancellationToken ct = default)
        => GetAsync($"/tkb/types/{tkbType}", ct);

    // ── Group N — world / coordinates ──────────────────────────────────────────

    public Task<ApiResult> GetWorldInfoAsync(CancellationToken ct = default) => GetAsync("/world/info", ct);

    public Task<ApiResult> GeoToLocalAsync(double lat, double lon, double alt = 0, float? headingDeg = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["lat"] = lat, ["lon"] = lon, ["alt"] = alt };
        if (headingDeg is not null) body["headingDeg"] = headingDeg.Value;
        return PostAsync("/world/geo-to-local", body, ct);
    }

    public Task<ApiResult> LocalToGeoAsync(float x, float y, float z, JsonNode? rotation = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["x"] = x, ["y"] = y, ["z"] = z };
        if (rotation is not null) body["rotation"] = rotation.DeepClone();
        return PostAsync("/world/local-to-geo", body, ct);
    }

    // ── Group G — breakpoints ──────────────────────────────────────────────────

    /// <summary><paramref name="condition"/> is a polymorphic <c>SearchPredicateDto</c> — it needs its <c>$type</c> arm.</summary>
    public Task<ApiResult> AddBreakpointAsync(JsonNode condition, long? filterNetworkId = null, int occurrenceThreshold = 1, string? name = null, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["condition"] = condition.DeepClone(),
            ["occurrenceThreshold"] = occurrenceThreshold,
        };
        if (filterNetworkId is not null) body["filterNetworkId"] = filterNetworkId.Value;
        if (name is not null) body["name"] = name;
        return PostAsync("/breakpoints", body, ct);
    }

    public Task<ApiResult> ListBreakpointsAsync(CancellationToken ct = default) => GetAsync("/breakpoints", ct);

    public Task<ApiResult> RemoveBreakpointAsync(string breakpointId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/breakpoints/{Uri.EscapeDataString(breakpointId)}", null, ct);

    /// <summary><c>GET /breakpoints/hits</c> — <c>{ isPaused, pausedTick, lastHit }</c>.</summary>
    public Task<ApiResult> GetBreakpointHitsAsync(CancellationToken ct = default) => GetAsync("/breakpoints/hits", ct);

    // ── Group H — checkpoint / restore / diff ──────────────────────────────────

    public Task<ApiResult> CheckpointAsync(CancellationToken ct = default) => PostAsync("/checkpoint", null, ct);
    public Task<ApiResult> RestoreCheckpointAsync(CancellationToken ct = default) => PostAsync("/checkpoint/restore", null, ct);

    public Task<ApiResult> DiffCaptureAsync(IEnumerable<long>? entities = null, CancellationToken ct = default)
        => PostAsync("/diff/capture", EntitiesBody(entities), ct);

    public Task<ApiResult> DiffCompareAsync(string baselineId, IEnumerable<long>? entities = null, CancellationToken ct = default)
    {
        var body = EntitiesBody(entities) ?? new JsonObject();
        body["baselineId"] = baselineId;
        return PostAsync("/diff/compare", body, ct);
    }

    private static JsonObject? EntitiesBody(IEnumerable<long>? entities)
    {
        if (entities is null) return null;
        var arr = new JsonArray();
        foreach (var id in entities) arr.Add(id);
        return new JsonObject { ["entities"] = arr };
    }

    // ── Group I — recording / replay ───────────────────────────────────────────

    public Task<ApiResult> StartRecordingAsync(string mode = "preview", CancellationToken ct = default)
        => PostAsync("/recording/start", new JsonObject { ["mode"] = mode }, ct);

    public Task<ApiResult> StopRecordingAsync(CancellationToken ct = default) => PostAsync("/recording/stop", null, ct);

    /// <summary>⚠ The field is <c>fdpPath</c>, not <c>path</c> — the mistake cost an iteration once already.</summary>
    public Task<ApiResult> LoadReplayAsync(string fdpPath, CancellationToken ct = default)
        => PostAsync("/replay/load", new JsonObject { ["fdpPath"] = fdpPath }, ct);

    public Task<ApiResult> SeekReplayAsync(int frame, CancellationToken ct = default)
        => PostAsync("/replay/seek", new JsonObject { ["frame"] = frame }, ct);

    public Task<ApiResult> ReplayStepAsync(string dir = "forward", CancellationToken ct = default)
        => PostAsync("/replay/step", new JsonObject { ["dir"] = dir }, ct);

    public Task<ApiResult> GetReplayStatusAsync(CancellationToken ct = default) => GetAsync("/replay/status", ct);
    public Task<ApiResult> ListReplayEntitiesAsync(CancellationToken ct = default) => GetAsync("/replay/entities", ct);
    public Task<ApiResult> UnloadReplayAsync(CancellationToken ct = default) => PostAsync("/replay/unload", null, ct);

    // ── Group K — behaviour traces ─────────────────────────────────────────────

    public Task<ApiResult> ObserveTraceAsync(long networkId, bool on, CancellationToken ct = default)
        => PostAsync("/trace/observe", new JsonObject { ["networkId"] = networkId, ["on"] = on }, ct);

    public Task<ApiResult> GetEntityTraceAsync(long networkId, CancellationToken ct = default)
        => GetAsync($"/entities/{networkId}/trace", ct);

    // ── Group L — live mutation / fault injection ──────────────────────────────

    public Task<ApiResult> GetAttributesSchemaAsync(CancellationToken ct = default) => GetAsync("/attributes/schema", ct);

    /// <summary><paramref name="patch"/> may be a JSON object or a JSON string — the host accepts both.</summary>
    public Task<ApiResult> PatchEntityAttributeAsync(long networkId, JsonNode patch, CancellationToken ct = default)
        => PostAsync($"/entities/{networkId}/attribute", new JsonObject { ["patchJson"] = patch.DeepClone() }, ct);

    public Task<ApiResult> SetComponentAsync(long networkId, string componentType, JsonNode patch, CancellationToken ct = default)
        => PostAsync($"/entities/{networkId}/component", new JsonObject
        {
            ["componentType"] = componentType,
            ["patch"] = patch.DeepClone(),
        }, ct);

    // ── Group O — variable addressing (the watch's tuple, over HTTP) ───────────
    //
    // `asset` is the blueprint's NAME or its asset Guid, and may be omitted when the entity carries
    // exactly one blueprint.

    public Task<ApiResult> GetEntityVariablesAsync(long networkId, string? asset = null, CancellationToken ct = default)
        => GetAsync($"/entities/{networkId}/variables"
                    + (string.IsNullOrWhiteSpace(asset) ? "" : $"?asset={Uri.EscapeDataString(asset)}"), ct);

    public Task<ApiResult> GetEntityVariableAsync(long networkId, string path, string? asset = null, CancellationToken ct = default)
    {
        var q = new List<string> { $"path={Uri.EscapeDataString(path)}" };
        if (!string.IsNullOrWhiteSpace(asset)) q.Add($"asset={Uri.EscapeDataString(asset)}");
        return GetAsync($"/entities/{networkId}/variable?" + string.Join("&", q), ct);
    }

    /// <summary>STAGES the write — it lands on the next advancing tick, not on the response.</summary>
    public Task<ApiResult> StageEntityVariableAsync(
        long networkId, string path, JsonNode value, string? asset = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["path"] = path, ["value"] = value.DeepClone() };
        if (!string.IsNullOrWhiteSpace(asset)) body["asset"] = asset;
        return PostAsync($"/entities/{networkId}/variable", body, ct);
    }

    // ── Group M — focus / annotations ──────────────────────────────────────────

    public Task<ApiResult> FocusEntityAsync(long networkId, CancellationToken ct = default)
        => PostAsync($"/entities/{networkId}/focus", null, ct);

    public Task<ApiResult> AddAnnotationAsync(JsonNode body, CancellationToken ct = default)
        => PostAsync("/annotations", body, ct);

    // ── Transport ──────────────────────────────────────────────────────────────

    private Task<ApiResult> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);
    private Task<ApiResult> PostAsync(string path, JsonNode? body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body, ct);

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var label = $"{method} {path}";
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var why = DiagnoseUnreachable?.Invoke();
            var suffix = why is null ? "" : $" — {why}";
            throw new McpRequestException($"{label} could not reach the editor at {BaseUrl}: {ex.Message}{suffix}", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonNode? envelope;
            try
            {
                envelope = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new McpRequestException($"{label} returned unparseable body ({(int)response.StatusCode}): {Truncate(text)}", ex);
            }

            bool ok = envelope?["ok"]?.GetValue<bool>() ?? response.IsSuccessStatusCode;
            var data = envelope?["data"];
            var error = envelope?["error"]?.GetValue<string>();
            var hint = envelope?["hint"];

            return new ApiResult((int)response.StatusCode, ok, data?.DeepClone(), error, hint?.DeepClone())
            {
                Request = label,
            };
        }
    }

    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400] + "…";

    public void Dispose() => _http.Dispose();
}
