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

        private readonly record struct RouteResult(int Status, JsonNode? Data, string? Error);

        private static RouteResult Ok(JsonNode? data) => new(200, data, null);
        private static RouteResult Fail(int status, string error) => new(status, null, error);

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
                return node is null ? Fail(404, $"Entity {id} not found.") : Ok(node);
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
                if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(Fail(400, "name is required."));
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
                    return Fail(400, "eventType is required.");
                var payload = ctx.Body?["payload"];
                bool wait   = ctx.Body?["wait"]?.GetValue<bool>() ?? false;

                var (result, error) = await _jobQueue.RunOnMainThread(() =>
                    Service().SendCommand(eventType!, payload, wait)).ConfigureAwait(false);

                if (error != null)
                    return Fail(400, error);
                return Ok(result);
            }));

            _routes.Add(new("POST", "/entities/spawn", async ctx =>
            {
                if (!long.TryParse(ctx.Body?["tkbType"]?.ToString(), out var tkbType))
                    return Fail(400, "tkbType (long) is required.");

                var transform      = ctx.Body?["transform"];
                var components     = ctx.Body?["components"];
                var attributesJson = ctx.Body?["attributesJson"]?.GetValue<string>();

                var node = await _jobQueue.RunOnMainThread(() =>
                    Service().SpawnEntity(tkbType, transform, components, attributesJson))
                    .ConfigureAwait(false);
                return Ok(node);
            }));

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
                return Task.FromResult(node is null ? Fail(404, $"TKB type {tkbType} not found.") : Ok(node));
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

            _routes.Add(new("POST", "/breakpoints", async ctx =>
            {
                var (node, error) = await _jobQueue.RunOnMainThread<(JsonNode?, string?)>(() =>
                {
                    try { return (Service().AddBreakpoint(ctx.Body), null); }
                    catch (ArgumentException ex) { return (null, ex.Message); }
                }).ConfigureAwait(false);
                return error != null ? Fail(400, error) : Ok(node);
            }));

            _routes.Add(new("GET", "/breakpoints", _ => RunMain(s => s.ListBreakpoints())));

            _routes.Add(new("DELETE", "/breakpoints/{id}", async ctx =>
            {
                var idStr = ctx.RouteValue("id");
                if (string.IsNullOrWhiteSpace(idStr)) return Fail(400, "breakpoint id is required.");
                var error = await _jobQueue.RunOnMainThread<string?>(() =>
                {
                    try { Service().RemoveBreakpoint(idStr!); return null; }
                    catch (ArgumentException ex) { return ex.Message; }
                }).ConfigureAwait(false);
                return error != null ? Fail(404, error) : Ok(new JsonObject { ["removed"] = idStr });
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
                    return Fail(400, "baselineId is required.");
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
                return error != null ? Fail(400, error) : Ok(node);
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
                catch (InvalidOperationException ex) { return Fail(400, ex.Message); }
                catch (Exception ex) { return Fail(500, ex.Message); }

                // Phase 2 (main thread): exit preview (triggers rewind), return status.
                var node = await _jobQueue.RunOnMainThread(() => Service().FinishRecordingStop())
                    .ConfigureAwait(false);
                return Ok(node);
            }));

            _routes.Add(new("POST", "/replay/load", async ctx =>
            {
                var fdpPath = ctx.Body?["fdpPath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(fdpPath)) return Fail(400, "fdpPath is required.");
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
                    : new ApiResponse(false, Error: result.Error);
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

    /// <summary>Standard API response envelope. <c>Data</c> is a <see cref="JsonNode"/> embedded verbatim.</summary>
    public record ApiResponse(bool Ok, JsonNode? Data = null, string? Error = null, bool? Awaited = null);
}
