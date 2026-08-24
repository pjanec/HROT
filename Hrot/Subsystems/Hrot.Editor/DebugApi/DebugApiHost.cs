using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Lightweight <see cref="HttpListener"/>-based HTTP host for the AI Debug API.
    /// No ASP.NET Core / generic host dependency.
    ///
    /// <para>
    /// Routing is table-driven (<see cref="RouteEntry"/>). World-touching handlers run on the
    /// main thread via <see cref="MainThreadJobQueue"/>; the payload they return is a
    /// <see cref="JsonNode"/> that is embedded verbatim in the response envelope so it is never
    /// re-cased by the host's CamelCase options (the keys inside a JsonNode are written as-is).
    /// </para>
    /// </summary>
    public sealed class DebugApiHost : IDisposable
    {
        private readonly int _port;
        private readonly MainThreadJobQueue _jobQueue;
        private readonly Action _shutdownCallback;
        private readonly HttpListener _listener = new HttpListener();
        private readonly List<RouteEntry> _routes = new();
        private DebugApiService? _service;
        private bool _disposed;

        /// <summary>Maximum wall-clock seconds to wait for a scenario load to reach OperatingEdit.</summary>
        private const double ScenarioReadyTimeoutSeconds = 30.0;

        /// <summary>
        /// ⚠⚠ <b><c>HN-015</c> — REGISTERING THE SAFE-FLOAT CONVERTERS HERE DOES NOT FIX THE 500. Measured,
        /// and recorded so nobody repeats the attempt.</b>
        ///
        /// <para>📐 <b>The symptom:</b> spawn one entity at runtime, then <c>GET /entities</c> ⇒ HTTP 500,
        /// <i>"positive and negative infinity cannot be written as valid JSON"</i>. ⇒ one entity with a
        /// non-finite float takes down the WHOLE listing.</para>
        ///
        /// <para>⛔⛔ <b>But the throw is UPSTREAM of this options object.</b> 📐 The payload reaches the host
        /// as an already-built <c>JsonNode</c> *(see the class remarks — deliberately, so keys are not
        /// re-cased)*, and the write that fails happens inside
        /// <c>EntityStateExtractionService.ExtractEntities</c> → <c>ScenarioSerializer.SerializeEntity</c>.
        /// ⇒ ⛔ converters added to the HOST's options are never consulted for it. 📌 <b>Tried and measured
        /// on `2026-08-23`: the 500 was unchanged</b>, so the registration was reverted rather than left in
        /// place looking like a fix.</para>
        ///
        /// <para>⭐⭐ <b>Where the real fix has to go, and why it is not a drive-by:</b> either the SCENARIO
        /// SERIALIZER learns sentinels *(⚠ that changes the on-disk scenario format — persistence blast
        /// radius)*, or <c>ExtractEntities</c> becomes resilient PER ENTITY so one bad row cannot kill the
        /// listing *(⭐ the containment fix, and the disproportionate part of the defect)*, or the
        /// un-initialised transform that carries the <c>Infinity</c> is fixed at the source. ⇒ ⭐ a design
        /// call, not a serialisation tweak — 📌 <c>DebugApiSafeFloatConverters.cs</c> still has ZERO
        /// application sites, and that remains a real finding.</para>
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public DebugApiHost(int port, MainThreadJobQueue jobQueue, Action shutdownCallback)
        {
            _port = port;
            _jobQueue = jobQueue;
            _shutdownCallback = shutdownCallback;
        }

        /// <summary>
        /// Supplies the service layer once the editor has finished initializing. Until this is
        /// called, capability endpoints return 503; <c>/status</c> and <c>/shutdown</c> still work.
        /// </summary>
        public void AttachService(DebugApiService service) => _service = service;

        /// <summary>Starts the HTTP listener and the background accept loop.</summary>
        public void Start()
        {
            BuildRoutes();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        // ── Route table ─────────────────────────────────────────────────────────

        private delegate Task<RouteResult> RouteHandler(RequestContext ctx);

        private sealed record RouteEntry(string Method, string Template, RouteHandler Handler);

        private readonly record struct RouteResult(int Status, JsonNode? Data, string? Error, JsonNode? Hint = null);

        private static RouteResult Ok(JsonNode? data) => new(200, data, null);

        /// <summary>
        /// Fails a request, optionally attaching the machine-readable pointer for
        /// <paramref name="hintCategory"/> (<c>MX8</c>). ⭐ Pass a category wherever the caller could
        /// have got the input right by asking the API first — a bad condition, an unknown entity, an
        /// unregistered component. ⛔ Never hand-write a <c>seeEndpoint</c> here; the map owns them.
        /// </summary>
        private static RouteResult Fail(int status, string error, string? hintCategory = null)
            => new(status, null, error, DebugApiHints.For(hintCategory));

        private void BuildRoutes()
        {
            // Group A — status
            _routes.Add(new("GET", "/status", _ => RunMain(s => s.GetStatus())));

            // Group B — entities (with optional ?component= and ?near= filters)
            _routes.Add(new("GET", "/entities", ctx =>
            {
                var comp = ctx.Query("component");
                var near = ctx.Query("near");
                return RunMain(s => s.ListEntities(comp, near));
            }));
            _routes.Add(new("GET", "/entities/{networkId}", async ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Fail(400, "Invalid networkId.");
                var node = await _jobQueue.RunOnMainThread(() => Service().DumpEntity(id)).ConfigureAwait(false);
                return node is null
                    ? Fail(404, $"Entity {id} not found. List entities with GET /entities.", DebugApiHints.Entity)
                    : Ok(node);
            }));

            // Group C — event history (retrieval + DTO mapping are thread-safe → no marshalling)
            _routes.Add(new("GET", "/events", ctx =>
            {
                var bus   = ctx.Query("bus") ?? "world";
                var type  = ctx.Query("type");
                uint.TryParse(ctx.Query("since"), out var since);
                int max   = int.TryParse(ctx.Query("max"), out var m) ? m : DebugApiService.DefaultMaxEvents;
                return Task.FromResult(Ok(Service().GetEvents(bus, type, since, max)));
            }));

            // Group J — Logs (off-thread: sinks are lock-guarded, no RunMain needed)
            _routes.Add(new("GET", "/logs", ctx =>
            {
                var level  = ctx.Query("level");
                var logger = ctx.Query("logger");
                var since  = ctx.Query("since");
                int max    = int.TryParse(ctx.Query("max"), out var m) ? m : DebugApiService.DefaultMaxLogs;
                return Task.FromResult(Ok(Service().GetLogs(level, logger, since, max)));
            }));

            // Group D — sim / preview / time
            _routes.Add(new("GET",  "/sim/state",     _   => RunMain(s => s.GetSimState())));
            _routes.Add(new("POST", "/sim/play",      _   => RunMain(s => s.Play())));
            _routes.Add(new("POST", "/sim/pause",     _   => RunMain(s => s.Pause())));
            _routes.Add(new("POST", "/sim/step",      ctx =>
            {
                int count = ctx.Body?["count"]?.GetValue<int>() ?? 1;
                return RunMain(s => s.Step(count));
            }));
            _routes.Add(new("POST", "/sim/timescale", ctx =>
            {
                var scale = ctx.Body?["scale"]?.GetValue<float>() ?? 1f;
                return RunMain(s => s.SetTimeScale(scale));
            }));
            _routes.Add(new("POST", "/preview/enter", ctx =>
            {
                bool startPaused = ctx.Body?["startPaused"]?.GetValue<bool>() ?? false;
                return RunMain(s => s.EnterPreview(startPaused));
            }));
            _routes.Add(new("POST", "/preview/exit",  _ => RunMain(s => s.ExitPreview())));

            // Group E — scenarios
            _routes.Add(new("GET",  "/scenarios", _ => RunMain(s => s.ListScenarios())));
            _routes.Add(new("POST", "/scenario/load", HandleScenarioLoad));
            _routes.Add(new("POST", "/scenario/save", ctx =>
            {
                var name = ctx.Body?["name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(Fail(400, "name is required.", DebugApiHints.Scenario));
                return RunMain(s => s.SaveScenario(name!));
            }));

            // Group F — commands + discovery + spawn
            _routes.Add(new("GET", "/commands", _ =>
                // Registry is safe off-thread (read-only after boot), but marshalling to
                // main thread avoids any race on late-registering event types.
                RunMain(s => s.ListCommands())));

            _routes.Add(new("GET", "/components", _ =>
                RunMain(s => s.ListComponents())));

            _routes.Add(new("POST", "/entities/command", async ctx =>
            {
                var eventType = ctx.Body?["eventType"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(eventType))
                    return Fail(400, "eventType is required.", DebugApiHints.Event);
                var payload = ctx.Body?["payload"];
                bool wait   = ctx.Body?["wait"]?.GetValue<bool>() ?? false;

                var (result, error) = await _jobQueue.RunOnMainThread(() =>
                    Service().SendCommand(eventType!, payload, wait)).ConfigureAwait(false);

                if (error != null)
                    return Fail(400, error, DebugApiHints.Event);
                return Ok(result);
            }));

            _routes.Add(new("POST", "/entities/spawn", async ctx =>
            {
                if (!long.TryParse(ctx.Body?["tkbType"]?.ToString(), out var tkbType))
                    return Fail(400, "tkbType (long) is required.", DebugApiHints.TkbType);

                var transform      = ctx.Body?["transform"];
                var components     = ctx.Body?["components"];
                var attributesJson = ctx.Body?["attributesJson"]?.GetValue<string>();

                var node = await _jobQueue.RunOnMainThread(() =>
                    Service().SpawnEntity(tkbType, transform, components, attributesJson))
                    .ConfigureAwait(false);
                return Ok(node);
            }));

            // Group P.0 / S — discovery WITH SCHEMA (MX4a, MX7). These exist so an agent never has
            // to author a behaviour's params or a breakpoint condition blind; DebugApiHints points
            // every schema-shaped rejection back at them.
            _routes.Add(new("GET", "/behaviors", ctx =>
            {
                long? tkbType  = long.TryParse(ctx.Query("tkbType"),  out var t) ? t : null;
                long? entityId = long.TryParse(ctx.Query("entityId"), out var e) ? e : null;

                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetBehaviors(tkbType, entityId);
                    // The right pointer depends on WHAT was wrong — a bad entity id sends the caller
                    // to /entities, not to the behaviour catalog — so the service names the category.
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Behavior)
                        : Ok(result);
                });
            }));

            _routes.Add(new("GET", "/breakpoint-types", _ => RunMain(s => s.GetBreakpointTypes())));

            // Group M — TKB catalog
            _routes.Add(new("GET", "/tkb/types", ctx =>
            {
                var category = ctx.Query("category");
                return Task.FromResult(Ok(Service().ListTkbTypes(category)));
            }));

            _routes.Add(new("GET", "/tkb/types/{tkbType}", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("tkbType"), out var tkbType))
                    return Task.FromResult(Fail(400, "Invalid tkbType."));
                var node = Service().GetTkbType(tkbType);
                return Task.FromResult(node is null
                    ? Fail(404, $"TKB type {tkbType} not found.", DebugApiHints.TkbType)
                    : Ok(node));
            }));

            // Group N — world/coordinate info
            _routes.Add(new("GET", "/world/info", _ =>
                Task.FromResult(Ok(Service().GetWorldInfo()))));

            _routes.Add(new("POST", "/world/geo-to-local", ctx =>
            {
                double lat = ctx.Body?["lat"]?.GetValue<double>() ?? 0;
                double lon = ctx.Body?["lon"]?.GetValue<double>() ?? 0;
                double alt = ctx.Body?["alt"]?.GetValue<double>() ?? 0;
                float? headingDeg = ctx.Body?["headingDeg"] is JsonNode hNode
                    ? hNode.GetValue<float>()
                    : (float?)null;
                return Task.FromResult(Ok(Service().GeoToLocal(lat, lon, alt, headingDeg)));
            }));

            _routes.Add(new("POST", "/world/local-to-geo", ctx =>
            {
                float x = ctx.Body?["x"]?.GetValue<float>() ?? 0;
                float y = ctx.Body?["y"]?.GetValue<float>() ?? 0;
                float z = ctx.Body?["z"]?.GetValue<float>() ?? 0;
                System.Numerics.Quaternion? rotation = null;
                if (ctx.Body?["rotation"] is JsonObject rotObj)
                {
                    rotation = new System.Numerics.Quaternion(
                        rotObj["x"]?.GetValue<float>() ?? 0,
                        rotObj["y"]?.GetValue<float>() ?? 0,
                        rotObj["z"]?.GetValue<float>() ?? 0,
                        rotObj["w"]?.GetValue<float>() ?? 1);
                }
                return Task.FromResult(Ok(Service().LocalToGeo(x, y, z, rotation)));
            }));

            // Group G — Breakpoints (ADA-BATCH-07)
            // NOTE: GET /breakpoints/hits must be registered BEFORE DELETE /breakpoints/{id}
            // to avoid ambiguity. The GET /breakpoints/hits route has two literal segments
            // so it matches before the parameterized DELETE route.
            _routes.Add(new("GET", "/breakpoints/hits", _ => RunMain(s => s.GetBreakpointStatus())));

            // ⭐ Resume after a hit. Measured while building MX1/MX9: the API could ENTER the paused
            // state (arm a breakpoint, let it fire) and had no way OUT of it — and the staged-write
            // drain is gated on the debugger not being rewound, so every later live write was queued
            // and never applied. Deleting the breakpoint does NOT resume; only these do. See MX-009.
            _routes.Add(new("POST", "/breakpoints/continue", _ => RunMainResult(s =>
            {
                var (result, error, hintCategory) = s.ContinueFromBreakpoint(step: false);
                return error != null ? Fail(400, error, hintCategory ?? DebugApiHints.Breakpoint) : Ok(result);
            })));

            _routes.Add(new("POST", "/breakpoints/step", _ => RunMainResult(s =>
            {
                var (result, error, hintCategory) = s.ContinueFromBreakpoint(step: true);
                return error != null ? Fail(400, error, hintCategory ?? DebugApiHints.Breakpoint) : Ok(result);
            })));

            _routes.Add(new("POST", "/breakpoints", async ctx =>
            {
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().AddBreakpoint(ctx.Body), null); }
                    catch (ArgumentException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                // MX8: authoring a SearchPredicateDto blind is the mistake this hint exists for.
                return error != null ? Fail(400, error, DebugApiHints.Condition) : Ok(node);
            }));

            _routes.Add(new("GET", "/breakpoints", _ => RunMain(s => s.ListBreakpoints())));

            _routes.Add(new("DELETE", "/breakpoints/{id}", async ctx =>
            {
                var idStr = ctx.RouteValue("id");
                if (string.IsNullOrWhiteSpace(idStr)) return Fail(400, "breakpoint id is required.", DebugApiHints.Breakpoint);
                var error = await _jobQueue.RunOnMainThread<string?>(() =>
                {
                    try { Service().RemoveBreakpoint(idStr!); return null; }
                    catch (ArgumentException ex) { return ex.Message; }
                }).ConfigureAwait(false);
                return error != null ? Fail(404, error, DebugApiHints.Breakpoint) : Ok(new JsonObject { ["removed"] = idStr });
            }));

            // Group H — Checkpoint / Restore / Diff (ADA-BATCH-08)
            _routes.Add(new("POST", "/checkpoint", async ctx =>
            {
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().Checkpoint(), null); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                if (error != null)
                {
                    // 409 for live-run conflict, 400 for already-in-preview
                    int status = error.Contains("live run") ? 409 : 400;
                    return Fail(status, error);
                }
                return Ok(node);
            }));

            _routes.Add(new("POST", "/checkpoint/restore", async ctx =>
            {
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().RestoreCheckpoint(), null); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("POST", "/diff/capture", async ctx =>
            {
                List<long>? ids = null;
                if (ctx.Body?["entities"] is JsonArray eArr)
                {
                    ids = new List<long>();
                    foreach (var item in eArr)
                        ids.Add(item!.GetValue<long>());
                }
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().CaptureBaseline(ids), null); }
                    catch (Exception ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("POST", "/diff/compare", async ctx =>
            {
                var baselineId = ctx.Body?["baselineId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(baselineId))
                    return Fail(400, "baselineId is required.", DebugApiHints.Baseline);
                List<long>? ids = null;
                if (ctx.Body?["entities"] is JsonArray eArr2)
                {
                    ids = new List<long>();
                    foreach (var item in eArr2)
                        ids.Add(item!.GetValue<long>());
                }
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().CompareBaseline(baselineId!, ids), null); }
                    catch (ArgumentException ex) { return (null, ex.Message); }
                    catch (Exception ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error, DebugApiHints.Baseline) : Ok(node);
            }));

            // Group I — Recording + Replay (ADA-BATCH-10)
            //
            // IMPORTANT: PrepareRecordingAsync / FinalizeRecordingAsync internally call
            // InstallModuleAsync / UninstallModuleAsync which await swapTcs.Task — a
            // TaskCompletionSource that is only fulfilled when the main thread reaches its
            // next BeforeSync boundary. Therefore these async calls MUST run from the
            // background HTTP thread (i.e. from this async lambda) and MUST NOT be called
            // from inside RunOnMainThread (which would block the main thread, preventing
            // swapTcs from ever completing → permanent deadlock).
            //
            // Pattern: phase-1 sync (via RunOnMainThread) → phase-2 async (background thread).
            _routes.Add(new("POST", "/recording/start", async ctx =>
            {
                var mode = ctx.Body?["mode"]?.GetValue<string>() ?? "preview";

                // Phase 1 (main thread): validate state, enter preview, set exercise ID + fdpPath.
                var (fdpPath, phase1Error) = await _jobQueue.RunOnMainThread<(string?, string?)>(() =>
                {
                    try   { return (Service().BeginRecordingStart(mode), null); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                    catch (ArgumentException ex)          { return (null, ex.Message); }
                }).ConfigureAwait(false);

                if (phase1Error != null)
                    return Fail(409, phase1Error);

                // Phase 2 (background thread): install recording kernel module — must NOT be
                // inside RunOnMainThread to avoid deadlock with swapTcs.
                try
                {
                    await Service().CompleteRecordingStartAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail(500, $"Recording module install failed: {ex.Message}");
                }

                return Ok(new JsonObject
                {
                    ["recording"] = true,
                    ["mode"]      = mode,
                    ["fdpPath"]   = fdpPath,
                });
            }));

            _routes.Add(new("POST", "/recording/stop", async ctx =>
            {
                // Phase 1 (background thread): finalize recording kernel module — must NOT be
                // inside RunOnMainThread for same swapTcs deadlock reason as /recording/start.
                string? fdpPath;
                try
                {
                    fdpPath = await Service().CompleteRecordingStopAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) { return Fail(400, ex.Message, DebugApiHints.Recording); }
                catch (Exception ex) { return Fail(500, ex.Message); }

                // Phase 2 (main thread): exit preview (triggers rewind), return status.
                var node = await _jobQueue.RunOnMainThread(() => Service().FinishRecordingStop())
                    .ConfigureAwait(false);
                return Ok(node);
            }));

            _routes.Add(new("POST", "/replay/load", async ctx =>
            {
                var fdpPath = ctx.Body?["fdpPath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(fdpPath)) return Fail(400, "fdpPath is required.", DebugApiHints.Recording);
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().LoadReplay(fdpPath!), null); }
                    catch (ArgumentException ex) { return (null, ex.Message); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("POST", "/replay/seek", async ctx =>
            {
                int frame = ctx.Body?["frame"]?.GetValue<int>() ?? 0;
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().SeekReplay(frame), null); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("POST", "/replay/step", async ctx =>
            {
                var dir = ctx.Body?["dir"]?.GetValue<string>() ?? "forward";
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().StepReplay(dir), null); }
                    catch (InvalidOperationException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("GET", "/replay/status", _ =>
                RunMain(s => new JsonObject
                {
                    ["replayActive"] = s.IsReplayActive,
                    ["currentFrame"] = s.ReplayCurrentFrame,
                    ["totalFrames"]  = s.ReplayTotalFrames,
                })
            ));

            _routes.Add(new("GET", "/replay/entities", _ =>
                RunMain(s =>
                {
                    if (!s.IsReplayActive)
                        return (JsonNode)new JsonObject { ["error"] = "No replay loaded" };
                    return s.ListReplayEntities();
                })
            ));

            _routes.Add(new("POST", "/replay/unload", _ => RunMain(s => s.UnloadReplay())));

            // Group K — AI Behavior Traces (ADA-BATCH-12)
            _routes.Add(new("POST", "/trace/observe", async ctx =>
            {
                long networkId = ctx.Body?["networkId"]?.GetValue<long>() ?? 0;
                bool on        = ctx.Body?["on"]?.GetValue<bool>() ?? false;
                if (networkId == 0) return Fail(400, "networkId is required.");
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    var result = Service().ObserveTrace(networkId, on);
                    if (result is JsonObject obj && obj["error"] is not null)
                        return (null, obj["error"]!.GetValue<string>());
                    return (result, null);
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("GET", "/entities/{networkId}/trace", async ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Fail(400, "Invalid networkId.");
                var node = await _jobQueue.RunOnMainThread(() => Service().GetEntityTrace(id)).ConfigureAwait(false);
                return Ok(node);
            }));

            // Group L — Live Mutation / Fault Injection (ADA-BATCH-13)
            _routes.Add(new("GET", "/attributes/schema", _ =>
                Task.FromResult(Ok(Service().GetAttributesSchema()))));

            _routes.Add(new("POST", "/entities/{networkId}/attribute", async ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Fail(400, "Invalid networkId.");

                // Accept patchJson as EITHER a JSON string OR a nested JSON object.
                string? patchJson = null;
                var patchJsonNode = ctx.Body?["patchJson"];
                if (patchJsonNode is System.Text.Json.Nodes.JsonValue jv)
                {
                    try { patchJson = jv.GetValue<string>(); }
                    catch { return Fail(400, "patchJson must be a JSON string or a JSON object."); }
                }
                else if (patchJsonNode is System.Text.Json.Nodes.JsonObject || patchJsonNode is System.Text.Json.Nodes.JsonArray)
                {
                    patchJson = patchJsonNode.ToJsonString();
                }

                if (string.IsNullOrWhiteSpace(patchJson))
                    return Fail(400, "patchJson is required.");
                var (node, error) = await _jobQueue.RunOnMainThread(() =>
                    Service().PatchEntityAttribute(id, patchJson!)).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("POST", "/entities/{networkId}/component", async ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Fail(400, "Invalid networkId.");
                var componentType = ctx.Body?["componentType"]?.GetValue<string>();
                var patch         = ctx.Body?["patch"];
                if (string.IsNullOrWhiteSpace(componentType))
                    return Fail(400, "componentType is required.", DebugApiHints.Component);
                var (node, error) = await _jobQueue.RunOnMainThread(() =>
                    Service().EditEntityComponent(id, componentType!, patch)).ConfigureAwait(false);
                return error != null ? Fail(400, error, DebugApiHints.Component) : Ok(node);
            }));

            // ── Group O — Variable addressing (MX1): the watch's own tuple, over HTTP ─────────
            //
            // A variable is addressed as (entity, asset, path) — never as a component and a byte
            // offset — so an agent reads and writes what the designer sees in the Details panel.
            // `asset` accepts the blueprint's NAME or its asset Guid, and may be omitted when the
            // entity carries exactly one blueprint.
            _routes.Add(new("GET", "/entities/{networkId}/variables", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                var asset = ctx.Query("asset");
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetEntityVariables(id, asset);
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Variable)
                        : Ok(result);
                });
            }));

            _routes.Add(new("GET", "/entities/{networkId}/variable", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                var asset = ctx.Query("asset");
                var path  = ctx.Query("path");
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetEntityVariable(id, asset, path);
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Variable)
                        : Ok(result);
                });
            }));

            // ⭐ STAGES, never writes through: the value lands on the next advancing tick, exactly as
            //   a Details-panel edit does. Answering 200 with pending:true is the honest report.
            _routes.Add(new("POST", "/entities/{networkId}/variable", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                var asset = ctx.Body?["asset"]?.GetValue<string>();
                var path  = ctx.Body?["path"]?.GetValue<string>();
                var value = ctx.Body?["value"];
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.StageEntityVariable(id, asset, path, value);
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Variable)
                        : Ok(result);
                });
            }));

            // ── Group Q — blueprint hot-attach (MX2) ──────────────────────────────────────────
            //
            // The runtime mechanism already exists; these publish the same lifecycle events the ingress
            // system consumes, so an attach behaves exactly as one authored in the editor would.
            _routes.Add(new("GET", "/blueprints", _ => RunMainResult(s =>
            {
                var (result, error, hintCategory) = s.GetBlueprints();
                return error != null ? Fail(400, error, hintCategory ?? DebugApiHints.Blueprint) : Ok(result);
            })));

            _routes.Add(new("POST", "/entities/{networkId}/attach-blueprint", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                var blueprint  = ctx.Body?["blueprint"]?.GetValue<string>();
                var paramsJson = ctx.Body?["paramsJson"]?.ToJsonString();
                // A JSON object is accepted as well as a string — an agent should not have to escape.
                if (ctx.Body?["paramsJson"] is System.Text.Json.Nodes.JsonValue pv
                    && pv.TryGetValue<string>(out var raw)) paramsJson = raw;
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.AttachBlueprint(id, blueprint, paramsJson);
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Blueprint)
                        : Ok(result);
                });
            }));

            _routes.Add(new("POST", "/entities/{networkId}/detach-blueprint", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                var blueprint = ctx.Body?["blueprint"]?.GetValue<string>();
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.DetachBlueprint(id, blueprint);
                    return error != null
                        ? Fail(hintCategory == DebugApiHints.Entity ? 404 : 400, error, hintCategory ?? DebugApiHints.Blueprint)
                        : Ok(result);
                });
            }));

            // ── Group R — the entity state dump (MX3) ─────────────────────────────────────────
            _routes.Add(new("GET", "/entities/{networkId}/state", ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Task.FromResult(Fail(400, "Invalid networkId.", DebugApiHints.Entity));
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetEntityState(id);
                    return error != null ? Fail(404, error, hintCategory ?? DebugApiHints.Entity) : Ok(result);
                });
            }));

            // ── Group T — the panel snapshot, read (MX9) ──────────────────────────────────────
            //
            // The UI made machine-readable without pixels. ⚠ ROUTE ORDER MATTERS: TryMatch takes the
            // FIRST template whose segment count and literals match, so "_gizmo" must be registered
            // BEFORE "{panelId}" or it would be captured as a panel id.
            _routes.Add(new("GET", "/panels", _ => RunMain(s => s.GetPanels())));

            _routes.Add(new("GET", "/panels/_gizmo", ctx =>
            {
                int max = int.TryParse(ctx.Query("max"), out var m) ? m : 500;
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetGizmoFrame(max);
                    return error != null ? Fail(404, error, hintCategory ?? DebugApiHints.Panel) : Ok(result);
                });
            }));

            _routes.Add(new("GET", "/panels/{panelId}", ctx =>
            {
                var panelId = ctx.RouteValue("panelId");
                return RunMainResult(s =>
                {
                    var (result, error, hintCategory) = s.GetPanel(panelId);
                    return error != null ? Fail(404, error, hintCategory ?? DebugApiHints.Panel) : Ok(result);
                });
            }));

            // ── N0 — the perspective, read and switched ───────────────────────────────────────
            //
            // ⭐⭐⭐ This is what makes three of the four editor perspectives reachable at all: a panel
            //    publishes only when its draw runs, and only the ACTIVE perspective draws.
            // ⚠ ROUTE ORDER: "/perspectives" (GET, plural) and "/perspective" (POST, singular) are
            //    different templates with different verbs, so neither can shadow the other.
            _routes.Add(new("GET", "/perspectives", _ => RunMainResult(s =>
            {
                var (result, error, hintCategory) = s.GetPerspectives();
                return error != null ? Fail(503, error, hintCategory) : Ok(result);
            })));

            _routes.Add(new("POST", "/perspective", ctx => RunMainResult(s =>
            {
                var (result, error, hintCategory) = s.SwitchPerspective(ctx.Body);
                // ⭐ 503 means "not wired" and 400 means "you asked for a perspective that does not
                //   exist" — ⛔ collapsing them would make a composition-root defect look like a bad
                //   request, which is the reading error that costs an afternoon.
                if (error == null) return Ok(result);
                return Fail(error.StartsWith("No perspective access", StringComparison.Ordinal) ? 503 : 400,
                            error, hintCategory);
            })));

            // Group M — Focus + Annotations (ADA-BATCH-14)
            _routes.Add(new("POST", "/entities/{networkId}/focus", async ctx =>
            {
                if (!long.TryParse(ctx.RouteValue("networkId"), out var id))
                    return Fail(400, "Invalid networkId.");
                var node = await _jobQueue.RunOnMainThread(() => Service().FocusEntity(id)).ConfigureAwait(false);
                return Ok(node);
            }));

            _routes.Add(new("POST", "/annotations", async ctx =>
            {
                var (node, error) = await _jobQueue.RunOnMainThread(() =>
                    Service().AddAnnotation(ctx.Body)).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));
        }

        private async Task<RouteResult> HandleScenarioLoad(RequestContext ctx)
        {
            var name = ctx.Body?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                return Fail(400, "name is required.");

            bool waitForReady = ctx.Body?["waitForReady"]?.GetValue<bool>() ?? false;

            await _jobQueue.RunOnMainThread<object?>(() => { Service().BeginLoadScenario(name!); return null; })
                           .ConfigureAwait(false);

            if (!waitForReady)
                return Ok(new JsonObject { ["loading"] = name, ["awaited"] = false });

            // Poll across kernel ticks until OperatingEdit or wall-clock timeout.
            // Each RunOnMainThread call yields to the main thread for one drain cycle,
            // so the kernel ticks naturally between polls.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int pollCount = 0;
            while (sw.Elapsed.TotalSeconds < ScenarioReadyTimeoutSeconds)
            {
                bool ready = await _jobQueue.RunOnMainThread(() => Service().PollClusterStateIsOperatingEdit())
                                            .ConfigureAwait(false);
                pollCount++;
                if (ready)
                    return Ok(new JsonObject { ["loaded"] = name, ["awaited"] = true });
            }
            return Fail(504, $"Scenario '{name}' did not reach OperatingEdit within {ScenarioReadyTimeoutSeconds}s ({pollCount} polls).");
        }

        private DebugApiService Service()
            => _service ?? throw new InvalidOperationException("Debug API service not attached yet.");

        private Task<RouteResult> RunMain(Func<DebugApiService, JsonNode?> fn)
            => _jobQueue.RunOnMainThread(() => Ok(fn(Service())));

        /// <summary>
        /// Like <see cref="RunMain"/>, but the handler builds its OWN <see cref="RouteResult"/> —
        /// for world-touching endpoints that can fail with a status and a hint rather than only
        /// returning a payload.
        /// </summary>
        private Task<RouteResult> RunMainResult(Func<DebugApiService, RouteResult> fn)
            => _jobQueue.RunOnMainThread(() => fn(Service()));

        // ── Accept / dispatch loop ────────────────────────────────────────────

        private async Task AcceptLoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }

                _ = Task.Run(() => HandleRequestAsync(ctx));
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                var method = ctx.Request.HttpMethod.ToUpperInvariant();
                var path   = ctx.Request.Url?.AbsolutePath ?? "/";

                // /shutdown is handled inline (no service needed).
                if (method == "POST" && path == "/shutdown")
                {
                    await WriteResponseAsync(ctx, 200, new ApiResponse(true)).ConfigureAwait(false);
                    _shutdownCallback?.Invoke();
                    return;
                }

                // Before the service is attached (early startup, or foundation-only hosting),
                // /status answers with a minimal { ok:true } so liveness checks succeed.
                if (method == "GET" && path == "/status" && _service is null)
                {
                    await WriteResponseAsync(ctx, 200, new ApiResponse(true)).ConfigureAwait(false);
                    return;
                }

                if (!TryMatch(method, path, out var entry, out var routeValues))
                {
                    await WriteResponseAsync(ctx, 404, new ApiResponse(false, Error: "Not found")).ConfigureAwait(false);
                    return;
                }

                if (_service is null)
                {
                    await WriteResponseAsync(ctx, 503, new ApiResponse(false, Error: "Debug API not ready")).ConfigureAwait(false);
                    return;
                }

                JsonNode? body = await ReadBodyAsync(ctx).ConfigureAwait(false);
                var reqCtx = new RequestContext(ctx.Request, routeValues, body);

                RouteResult result;
                try
                {
                    result = await entry.Handler(reqCtx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = Fail(500, ex.Message);
                }

                var envelope = result.Error is null
                    ? new ApiResponse(true, Data: result.Data)
                    : new ApiResponse(false, Error: result.Error, Hint: result.Hint);
                await WriteResponseAsync(ctx, result.Status, envelope).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { await WriteResponseAsync(ctx, 500, new ApiResponse(false, Error: ex.Message)).ConfigureAwait(false); }
                catch { /* response already started */ }
            }
        }

        private bool TryMatch(string method, string path, out RouteEntry entry, out Dictionary<string, string> routeValues)
        {
            routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var r in _routes)
            {
                if (!string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase)) continue;

                var tSegs = r.Template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (tSegs.Length != segs.Length) continue;

                bool match = true;
                var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < tSegs.Length; i++)
                {
                    if (tSegs[i].StartsWith("{") && tSegs[i].EndsWith("}"))
                        captured[tSegs[i].Trim('{', '}')] = Uri.UnescapeDataString(segs[i]);
                    else if (!string.Equals(tSegs[i], segs[i], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    entry = r;
                    routeValues = captured;
                    return true;
                }
            }
            entry = null!;
            return false;
        }

        private static async Task<JsonNode?> ReadBodyAsync(HttpListenerContext ctx)
        {
            if (!ctx.Request.HasEntityBody) return null;
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonNode.Parse(text); }
            catch { return null; }
        }

        private static async Task WriteResponseAsync(HttpListenerContext ctx, int statusCode, ApiResponse obj)
        {
            var json  = JsonSerializer.Serialize(obj, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }

        // ── Per-request context ───────────────────────────────────────────────

        private sealed class RequestContext
        {
            private readonly HttpListenerRequest _request;
            private readonly Dictionary<string, string> _routeValues;

            public JsonNode? Body { get; }

            public RequestContext(HttpListenerRequest request, Dictionary<string, string> routeValues, JsonNode? body)
            {
                _request = request;
                _routeValues = routeValues;
                Body = body;
            }

            public string? RouteValue(string key) => _routeValues.TryGetValue(key, out var v) ? v : null;
            public string? Query(string key) => _request.QueryString[key];
        }
    }

    /// <summary>
    /// Standard API response envelope. <c>Data</c> is a <see cref="JsonNode"/> embedded verbatim.
    ///
    /// <para><c>Hint</c> (<c>MX8</c>) is the machine-readable half of an error: where a caller that
    /// got the input wrong should look — <c>{ seeEndpoint, why }</c>, filled from
    /// <see cref="DebugApiHints"/>. ⭐ The prose in <c>Error</c> is unchanged and still carries the
    /// human explanation; this spares an agent parsing an endpoint name out of a sentence.</para>
    /// </summary>
    public record ApiResponse(bool Ok, JsonNode? Data = null, string? Error = null, bool? Awaited = null, JsonNode? Hint = null);
}
