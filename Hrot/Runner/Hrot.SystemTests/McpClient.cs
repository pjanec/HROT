using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.SystemTests;

/// <summary>
/// Typed HTTP wrapper over the AI-debug (MCP) API — one method per route registered in
/// <c>DebugApiHost.BuildRoutes</c> — the single route-registration site, which is where to enumerate
/// the surface rather than guessing at it.
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
    /// <c>POST /shutdown</c> — asks the runner to leave its frame loop, so the editor tears its
    /// subsystems down in order. The fixture calls this FIRST at teardown and keeps the tree-kill only
    /// as the fallback for an editor that is wedged.
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
    /// ⭐⭐ <c>POST /scenario/load/edit</c> — load for AUTHORING, cluster-wide. 📄 <c>MCP_Integration.md</c>
    /// § Group U.
    ///
    /// <para>⭐ With <paramref name="waitForReady"/> the HOST polls the cluster state to
    /// <c>OperatingEdit</c> across kernel ticks (30 s cap) and answers only when the world is actually
    /// loaded — always prefer it over polling from the test, which cannot see the cluster state directly.</para>
    ///
    /// <para>⚠ In <c>--mode all</c> this is PARTIAL: CGF has no edit-load handler yet *(a CGF-lane
    /// follow-up)*, so SimHost loads and CGF does not. ⭐ Use <see cref="LoadScenarioLiveAsync"/> when the two
    /// hosts' worlds must actually match.</para>
    ///
    /// <para>📌 There is deliberately NO mode-less <c>LoadScenarioAsync</c> *(user, `2026-08-24`: "there
    /// should be no alias")* — a caller must say which of the two load modes it means.</para>
    /// </summary>
    public Task<ApiResult> LoadScenarioEditAsync(string name, bool waitForReady = true, CancellationToken ct = default)
        => PostAsync("/scenario/load/edit", new JsonObject { ["name"] = name, ["waitForReady"] = waitForReady }, ct);

    /// <summary>
    /// ⭐⭐⭐ <c>POST /scenario/load/live</c> — load for RUNNING, cluster-wide, on ANY host.
    /// 📄 <c>MCP_Integration.md</c> § Group U. ⭐ Every host has live-load handlers, so this is the mode that
    /// can equalise two hosts' worlds — which is what makes the conformance content diff executable.
    /// </summary>
    public Task<ApiResult> LoadScenarioLiveAsync(string name, bool waitForReady = true, CancellationToken ct = default)
        => PostAsync("/scenario/load/live", new JsonObject { ["name"] = name, ["waitForReady"] = waitForReady }, ct);

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

    /// <summary>Resume the debugger after a hit — also what drains anything staged while it was stopped.</summary>
    public Task<ApiResult> ContinueFromBreakpointAsync(CancellationToken ct = default)
        => PostAsync("/breakpoints/continue", null, ct);

    public Task<ApiResult> StepFromBreakpointAsync(CancellationToken ct = default)
        => PostAsync("/breakpoints/step", null, ct);

    // ── Group Q — blueprint hot-attach ─────────────────────────────────────────

    public Task<ApiResult> GetBlueprintsAsync(CancellationToken ct = default)
        => GetAsync("/blueprints", ct);

    /// <summary>Queued — the ingress system applies it on the next tick, not on the response.</summary>
    public Task<ApiResult> AttachBlueprintAsync(
        long networkId, string blueprint, JsonNode? paramsJson = null, CancellationToken ct = default)
    {
        var body = new JsonObject { ["blueprint"] = blueprint };
        if (paramsJson is not null) body["paramsJson"] = paramsJson.DeepClone();
        return PostAsync($"/entities/{networkId}/attach-blueprint", body, ct);
    }

    public Task<ApiResult> DetachBlueprintAsync(long networkId, string blueprint, CancellationToken ct = default)
        => PostAsync($"/entities/{networkId}/detach-blueprint",
                     new JsonObject { ["blueprint"] = blueprint }, ct);

    // ── Group R — the entity state dump ────────────────────────────────────────

    public Task<ApiResult> GetEntityStateAsync(long networkId, CancellationToken ct = default)
        => GetAsync($"/entities/{networkId}/state", ct);

    // ── Group T — the panel snapshot ───────────────────────────────────────────

    public Task<ApiResult> GetPanelsAsync(CancellationToken ct = default)
        => GetAsync("/panels", ct);

    public Task<ApiResult> GetPanelAsync(string panelId, CancellationToken ct = default)
        => GetAsync($"/panels/{Uri.EscapeDataString(panelId)}", ct);

    public Task<ApiResult> GetGizmoFrameAsync(int? max = null, CancellationToken ct = default)
        => GetAsync("/panels/_gizmo" + (max is null ? "" : $"?max={max}"), ct);

    // ── N0 — the perspective: the reach the whole net depends on ───────────────
    //
    // ⭐⭐⭐ Only the ACTIVE perspective draws, and a panel publishes only when it draws ⇒ without these
    //    two calls the harness can see one perspective's panels and no others.

    public Task<ApiResult> ListPerspectivesAsync(CancellationToken ct = default)
        => GetAsync("/perspectives", ct);

    /// <summary>
    /// ⭐⭐⭐ <c>GET /capabilities</c> — the manifest: every endpoint this host serves *(enumerated from its own
    /// route table)* × the MEASURED availability matrix per perspective.
    /// 📄 <c>Architect_Question_54</c> § Manifest scope · charter <c>D4</c>.
    /// <para>⭐ Conformance reads NOT-PRESENT from here, ⛔ never infers it from a missing panel.</para>
    /// </summary>
    public Task<ApiResult> GetCapabilitiesAsync(CancellationToken ct = default)
        => GetAsync("/capabilities", ct);

    public Task<ApiResult> SwitchPerspectiveAsync(string name, CancellationToken ct = default)
        => PostAsync("/perspective", new JsonObject { ["name"] = name }, ct);

    /// <summary>
    /// ⭐⭐ Switch, then STEP so the new perspective's panels actually draw and publish.
    /// <para>⛔ The step is not politeness — the switch takes effect on the next frame, so a switch
    /// followed immediately by <c>GET /panels</c> reads the OLD perspective's capture and would silently
    /// bless it as the new one's golden. 📄 <c>DESIGN_Regression_Net.md</c> §6.</para>
    /// </summary>
    public async Task<ApiResult> SwitchPerspectiveAndSettleAsync(
        string name, int ticks = 2, CancellationToken ct = default)
    {
        var switched = await SwitchPerspectiveAsync(name, ct).ConfigureAwait(false);
        if (!switched.Ok) return switched;
        await StepAsync(ticks, ct).ConfigureAwait(false);
        return switched;
    }

    // ── Group V — the AI-asset drive surface (cgf==editor slice 2) ─────────────
    //
    // ⭐⭐⭐ 📄 DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md §3/§3a. Without these the harness can
    //    only ever capture an authoring panel in its EMPTY state — the canvas draws the ACTIVE
    //    document, and nothing could make one active over HTTP.

    public Task<ApiResult> ListAssetsAsync(CancellationToken ct = default)
        => GetAsync("/assets", ct);

    /// <summary>⭐ Open by the stable GUID — the URL-segment form (§3a).</summary>
    public Task<ApiResult> OpenAssetAsync(string assetId, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/open", null, ct);

    /// <summary>
    /// ⭐ Open by the HUMAN address. ⚠ The path travels in the BODY — ⛔ a relative path in a URL
    /// segment would need encoding, which §3a rules out.
    /// </summary>
    public Task<ApiResult> OpenAssetByPathAsync(string path, CancellationToken ct = default)
        => PostAsync("/assets/open", new JsonObject { ["path"] = path }, ct);

    public Task<ApiResult> ListDocumentsAsync(CancellationToken ct = default)
        => GetAsync("/documents", ct);

    public Task<ApiResult> ActivateDocumentAsync(string assetId, CancellationToken ct = default)
        => PostAsync($"/documents/{Uri.EscapeDataString(assetId)}/activate", null, ct);

    public Task<ApiResult> FocusPanelAsync(string panelId, CancellationToken ct = default)
        => PostAsync($"/panels/{Uri.EscapeDataString(panelId)}/focus", null, ct);

    /// <summary>⭐ Slice 3 — persist edited assets. ⚠ Runs the shared Save-All: every DIRTY open doc.</summary>
    public Task<ApiResult> SaveAssetAsync(string assetId, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/save", null, ct);

    /// <summary>
    /// ⭐ Slice 3 — recompile and commit into the running registry. ⚠ Compiles from the IN-MEMORY
    /// asset, ⛔ not from disk, so it reflects unsaved edits.
    /// </summary>
    public Task<ApiResult> ReloadAssetAsync(string assetId, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/reload", null, ct);

    /// <summary>
    /// ⭐⭐ Open an asset and STEP, so its canvas and outline have actually drawn before anything reads
    /// them. ⛔ The same frame-boundary contract <see cref="SwitchPerspectiveAndSettleAsync"/> exists for:
    /// a same-frame read returns the PREVIOUS document's capture and would be blessed as this one's.
    /// </summary>
    public async Task<ApiResult> OpenAssetAndSettleAsync(
        string assetId, int ticks = 3, CancellationToken ct = default)
    {
        var opened = await OpenAssetAsync(assetId, ct).ConfigureAwait(false);
        if (!opened.Ok) return opened;
        await StepAsync(ticks, ct).ConfigureAwait(false);
        await Task.Delay(150, ct).ConfigureAwait(false);
        return opened;
    }

    // ── Group M — focus / annotations ──────────────────────────────────────────

    public Task<ApiResult> FocusEntityAsync(long networkId, CancellationToken ct = default)
        => PostAsync($"/entities/{networkId}/focus", null, ct);

    public Task<ApiResult> AddAnnotationAsync(JsonNode body, CancellationToken ct = default)
        => PostAsync("/annotations", body, ct);

    // ── Group W — AI-asset AUTHORING (AQ56 / DESIGN_Mcp_Authoring.md) ──────────
    //
    // ⭐⭐⭐ Read-then-edit-by-guid. ⛔ Every id below is the editor's IN-MEMORY guid; the ids in the
    //    saved .json are deterministic name-derived ones and address nothing here (§3).

    /// <summary>⭐ The whole graph, by in-memory guid — the first call of any authoring sequence.</summary>
    public Task<ApiResult> ReadAssetGraphAsync(string assetId, CancellationToken ct = default)
        => GetAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph", ct);

    /// <summary>⭐ The node kinds THIS graph accepts — ⛔ never guess a kind id.</summary>
    public Task<ApiResult> ListNodeKindsAsync(
        string assetId, string? filter = null, CancellationToken ct = default)
        => GetAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/catalog"
                  + (filter is null ? "" : $"?filter={Uri.EscapeDataString(filter)}"), ct);

    /// <summary>⭐ Add a node; the response carries its new guid AND its pins.</summary>
    public Task<ApiResult> AddGraphNodeAsync(
        string assetId, string kind, float x = 0, float y = 0, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/nodes",
                     new JsonObject { ["kind"] = kind, ["x"] = x, ["y"] = y }, ct);

    /// <summary>⭐ Connect two pins — the host's own link validator runs first.</summary>
    public Task<ApiResult> AddGraphLinkAsync(
        string assetId, string fromPin, string toPin, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/links",
                     new JsonObject { ["fromPin"] = fromPin, ["toPin"] = toPin }, ct);

    /// <summary>⭐ Set an input data pin's literal default.</summary>
    public Task<ApiResult> SetGraphParamAsync(
        string assetId, string pinId, JsonNode? value, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/params",
                     new JsonObject { ["pinId"] = pinId, ["value"] = value }, ct);

    /// <summary>⭐ Remove nodes / links through the editor's own Delete command.</summary>
    public Task<ApiResult> RemoveGraphElementsAsync(
        string assetId, IEnumerable<string>? nodes = null, IEnumerable<string>? links = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["nodes"] = new JsonArray((nodes ?? Array.Empty<string>()).Select(n => (JsonNode)n!).ToArray()),
            ["links"] = new JsonArray((links ?? Array.Empty<string>()).Select(l => (JsonNode)l!).ToArray()),
        };
        return PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/remove", body, ct);
    }

    /// <summary>⭐ Create an asset through the host's own New-Asset path.</summary>
    public Task<ApiResult> CreateAssetAsync(
        string kind, string name, string path = "", string? recipe = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject { ["kind"] = kind, ["name"] = name, ["path"] = path };
        // ⭐ MA-021 — OMITTED rather than null when unset, so the host's "no such recipe" refusal can
        //   only fire on a name the caller actually asked for.
        if (recipe != null) body["recipe"] = recipe;
        return PostAsync("/assets", body, ct);
    }

    /// <summary>
    /// ⭐⭐ <c>MD-006</c> — trigger the CLUSTER-WIDE diagnostic dump. ⛔ Asynchronous: the response
    /// confirms the intent was published, not that files exist.
    /// </summary>
    public Task<ApiResult> TriggerClusterDumpAsync(int[] nodes, CancellationToken ct = default)
        => PostAsync("/cluster/diagnostics/dump",
                     new JsonObject { ["nodes"] = new JsonArray(nodes.Select(n => (JsonNode)n).ToArray()) }, ct);

    /// <summary>⭐⭐ <c>MD-007</c> — in-flight flag + the last successful dump's file manifest.</summary>
    public Task<ApiResult> GetClusterDumpStatusAsync(CancellationToken ct = default)
        => GetAsync("/cluster/diagnostics/status", ct);

    /// <summary>
    /// ⭐⭐ <c>MD-002</c> — this NODE's modules/systems/translators, per subsystem.
    /// ⛔ Not cluster-wide: every node hosts its own endpoint and answers for itself.
    /// </summary>
    public Task<ApiResult> GetArchitectureDiagnosticsAsync(
        string? subsystem = null, CancellationToken ct = default)
        => GetAsync(subsystem == null
                        ? "/diagnostics/architecture"
                        : $"/diagnostics/architecture?subsystem={Uri.EscapeDataString(subsystem)}", ct);

    /// <summary>
    /// ⭐⭐ <c>MA-020</c> — the recipes <c>POST /assets</c> can build from, per kind.
    /// ⛔ Without this an agent can only ever create BLANKS: a recipe is addressable only by NAME.
    /// </summary>
    public Task<ApiResult> ListAssetRecipesAsync(string? kind = null, CancellationToken ct = default)
        => GetAsync(kind == null ? "/assets/recipes" : $"/assets/recipes?kind={Uri.EscapeDataString(kind)}", ct);

    /// <summary>⭐ Scenario authoring's delete — world manipulation, queued like spawn.</summary>
    public Task<ApiResult> DeleteEntityAsync(long networkId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/entities/{networkId}", null, ct);

    // ── Group X — the union backbone, discovery and the editor command bus ────
    //
    // ⭐⭐⭐ apply_graph_command is the WHOLE GraphCommand union — the variants the four typed verbs
    //    cannot express (BTree decorators, HSM regions, reparenting, comments, reroutes, refactors).

    /// <summary>⭐ Every command variant this host accepts, with each one's fields.</summary>
    public Task<ApiResult> ListGraphCommandTypesAsync(string assetId, CancellationToken ct = default)
        => GetAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/command", ct);

    /// <summary>⭐⭐ Apply ONE serialized <c>GraphCommand</c>. The body IS the command.</summary>
    public Task<ApiResult> ApplyGraphCommandAsync(
        string assetId, JsonNode command, CancellationToken ct = default)
        => PostAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/command", command, ct);

    /// <summary>⭐ One node kind's full schema and documentation.</summary>
    public Task<ApiResult> GetNodeKindSchemaAsync(
        string assetId, string kind, CancellationToken ct = default)
        => GetAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/catalog/{Uri.EscapeDataString(kind)}", ct);

    /// <summary>⭐ One node's editable properties with their CURRENT values.</summary>
    public Task<ApiResult> GetNodePropertiesAsync(
        string assetId, string nodeId, CancellationToken ct = default)
        => GetAsync($"/assets/{Uri.EscapeDataString(assetId)}/graph/nodes/{Uri.EscapeDataString(nodeId)}/properties", ct);

    /// <summary>⭐ The EDITOR command bus — ⛔ not <c>/commands</c>, which lists FDP event types.</summary>
    public Task<ApiResult> ListEditorCommandsAsync(string? category = null, CancellationToken ct = default)
        => GetAsync("/editor/commands"
                  + (category is null ? "" : $"?category={Uri.EscapeDataString(category)}"), ct);

    /// <summary>⭐ Describe one editor command.</summary>
    public Task<ApiResult> GetEditorCommandAsync(string commandId, CancellationToken ct = default)
        => GetAsync($"/editor/commands/{Uri.EscapeDataString(commandId)}", ct);

    /// <summary>⭐ Run an editor command through the seam the toolbar and hotkeys use.</summary>
    public Task<ApiResult> InvokeEditorCommandAsync(
        string commandId, JsonNode? args = null, CancellationToken ct = default)
        => PostAsync($"/editor/commands/{Uri.EscapeDataString(commandId)}/invoke",
                     new JsonObject { ["args"] = args ?? new JsonObject() }, ct);

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
