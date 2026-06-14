using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Logging;
using Fdp.Core.Serialization;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using Hrot.Diagnostics.Breakpoints;
using Hrot.UI.Common.Facades;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Testable service layer for the AI Debug API. Holds references to the editor's
    /// already-constructed services and implements one method per endpoint, returning a
    /// <see cref="JsonNode"/> payload produced via the inspector-grade DTO path
    /// (<see cref="EntityStateExtractionService"/> / <see cref="EventSerializationHelper"/>)
    /// so payloads pass through the envelope verbatim — never re-cased by the host.
    ///
    /// <para>
    /// <b>Threading:</b> every method that touches <c>_world</c> / <c>NetworkEntityMap</c> /
    /// the time controller assumes it runs on the main thread. <see cref="DebugApiHost"/>
    /// marshals those calls via <see cref="MainThreadJobQueue"/>. Event-history and
    /// scenario-list reads are thread-safe and may run off-thread.
    /// </para>
    /// </summary>
    public sealed class DebugApiService
    {
        private readonly EntityRepository                _world;
        private readonly NetworkEntityMap                _entityMap;
        private readonly IEntityStateExtractionService   _extraction;
        private readonly ITimeTransportFacade            _time;
        private readonly IPreviewController              _preview;
        private readonly IEditorLogic                    _editor;
        private readonly IDiagnosticEventHistoryService  _eventHistory;
        private readonly MasterSyncController             _timeController;
        private readonly Func<ClusterState>              _clusterState;

        // Group M / N dependencies
        private readonly TkbDatabase           _tkbDb;
        private readonly IGeographicTransform  _geoTransform;
        private readonly float                 _spatialGridCellSize;
        private readonly float                 _spatialGridOriginX;
        private readonly float                 _spatialGridOriginY;
        private readonly int                   _spatialGridWidth;
        private readonly int                   _spatialGridHeight;

        // Group G — Breakpoints
        private readonly IDataBreakpointManager? _bpManager;
        private BreakpointId _lastHitBreakpointId;
        private long         _lastHitNetworkId;

        // Group H — Checkpoint / Restore / Diff
        private readonly IComponentDiffService _diffService;
        private readonly Dictionary<string, Dictionary<long, JsonNode?>> _diffBaselines = new();
        private int _nextBaselineId;

        // Group I — Recording + Replay
        private readonly Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? _rrController;
        private bool _isRecording;
        private Guid _activeRecordingExerciseId;
        private string? _lastFdpPath;
        private Fdp.Toolkit.ReplayBrowser.ReplayBrowserContext? _replayContext;
        private Fdp.Toolkit.Diagnostics.EntityStateExtractionService? _replayExtraction;

        // Group J — Log sinks (off-thread safe, lock-guarded)
        private readonly IReadOnlyList<IMessageLogSource> _logSinks;

        // Group K — AI Behavior Traces
        private readonly EditorAiTracerCoordinator?                    _editorTracer;
        private readonly Hrot.BTree.Editor.Debug.BTreeDebugSession?    _btreeSession;
        private readonly Hrot.Hsm.Editor.Debug.HsmDebugSession?        _hsmSession;
        private readonly Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintSession;

        internal static readonly JsonSerializerOptions SearchPredicateJsonOptions = new()
        {
            WriteIndented = false,
            IncludeFields = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        /// <summary>
        /// Scoped <see cref="JsonSerializerOptions"/> for the DebugApi entity dump / diff surface.
        ///
        /// <para>
        /// Cloned from <see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/> (field-aware, case-insensitive,
        /// null-omitting, enum-as-string) with the vector array converters replaced by NaN-safe
        /// equivalents and scalar <c>float</c>/<c>double</c> non-finite sentinel converters added.
        /// This ensures that any entity carrying a <c>NaN</c> or <c>Infinity</c> float (e.g. a
        /// freshly-spawned CivilianPedestrian whose SimTransform/SimVelocity have not settled) is
        /// serialized as valid standard JSON (<c>"NaN"</c>/<c>"Infinity"</c>/<c>"-Infinity"</c>
        /// string sentinels) rather than the named literals rejected by <c>JSON.parse</c> and
        /// <c>JsonNode.Parse</c>.
        /// </para>
        ///
        /// <para>
        /// Blast-radius control: this instance is used ONLY by the DebugApi read surface
        /// (<see cref="DumpToJsonNode"/>). The shared registry singletons
        /// (<see cref="FdpJsonOptionsRegistry.DefaultRelaxed"/> / <c>Indented</c>) are unchanged
        /// so UI panels, MetadataSerializer, and golden snapshots are unaffected.
        /// </para>
        /// </summary>
        internal static readonly JsonSerializerOptions DebugApiDumpOptions = BuildDebugApiDumpOptions();

        private static JsonSerializerOptions BuildDebugApiDumpOptions()
        {
            // Start from a mutable copy of DefaultRelaxed settings (can't clone frozen opts directly).
            var opts = new JsonSerializerOptions
            {
                IncludeFields               = true,
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas         = true,
                ReadCommentHandling         = JsonCommentHandling.Skip,
                DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver            = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            };

            // Add scalar non-finite converters FIRST so they take priority over the default
            // float/double serialization (converters are checked in registration order).
            opts.Converters.Add(new NonFiniteFloatSentinelConverter());
            opts.Converters.Add(new NonFiniteDoubleSentinelConverter());

            // NaN-safe vector converters (replace the shared WriteRawValue-based ones).
            opts.Converters.Add(new DebugApiVector2SafeConverter());
            opts.Converters.Add(new DebugApiVector3SafeConverter());
            opts.Converters.Add(new DebugApiVector4SafeConverter());
            opts.Converters.Add(new DebugApiQuaternionSafeConverter());

            // Keep FixedString and strict-enum converters from DefaultRelaxed.
            opts.Converters.Add(new Fdp.Core.Serialization.Converters.FixedString32Converter());
            opts.Converters.Add(new Fdp.Core.Serialization.Converters.FixedString64Converter());
            opts.Converters.Add(new Fdp.Core.Serialization.Converters.StrictStringEnumConverter());

            opts.MakeReadOnly();
            return opts;
        }

        /// <summary>Default upper bound for event-history queries.</summary>
        public const int DefaultMaxEvents = 200;

        public DebugApiService(
            EntityRepository                world,
            NetworkEntityMap                entityMap,
            IEntityStateExtractionService   extraction,
            ITimeTransportFacade            time,
            IPreviewController              preview,
            IEditorLogic                    editor,
            IDiagnosticEventHistoryService  eventHistory,
            MasterSyncController            timeController,
            Func<ClusterState>              clusterState,
            TkbDatabase?                    tkbDb              = null,
            IGeographicTransform?           geoTransform       = null,
            float                           spatialGridCellSize = 5.0f,
            float                           spatialGridOriginX  = 0f,
            float                           spatialGridOriginY  = 0f,
            int                             spatialGridWidth    = 200,
            int                             spatialGridHeight   = 200,
            IDataBreakpointManager?         bpManager          = null,
            IComponentDiffService?          diffService        = null,
            Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? rrController = null,
            IReadOnlyList<IMessageLogSource>? logSinks          = null,
            EditorAiTracerCoordinator?                    editorTracer      = null,
            Hrot.BTree.Editor.Debug.BTreeDebugSession?    btreeSession      = null,
            Hrot.Hsm.Editor.Debug.HsmDebugSession?        hsmSession        = null,
            Hrot.Blueprints.Core.Debug.BlueprintDebugSession? blueprintSession = null)
        {
            _world            = world            ?? throw new ArgumentNullException(nameof(world));
            _entityMap        = entityMap        ?? throw new ArgumentNullException(nameof(entityMap));
            _extraction       = extraction       ?? throw new ArgumentNullException(nameof(extraction));
            _time             = time             ?? throw new ArgumentNullException(nameof(time));
            _preview          = preview          ?? throw new ArgumentNullException(nameof(preview));
            _editor           = editor           ?? throw new ArgumentNullException(nameof(editor));
            _eventHistory     = eventHistory     ?? throw new ArgumentNullException(nameof(eventHistory));
            _timeController   = timeController   ?? throw new ArgumentNullException(nameof(timeController));
            _clusterState     = clusterState     ?? throw new ArgumentNullException(nameof(clusterState));
            _tkbDb             = tkbDb            ?? new TkbDatabase();
            _geoTransform      = geoTransform     ?? new Fdp.Modules.Geographic.Transforms.WGS84Transform();
            _spatialGridCellSize = spatialGridCellSize;
            _spatialGridOriginX  = spatialGridOriginX;
            _spatialGridOriginY  = spatialGridOriginY;
            _spatialGridWidth    = spatialGridWidth;
            _spatialGridHeight   = spatialGridHeight;
            _bpManager         = bpManager;
            _diffService       = diffService ?? new ComponentDiffService();
            _rrController      = rrController;
            _logSinks          = logSinks ?? Array.Empty<IMessageLogSource>();
            _editorTracer     = editorTracer;
            _btreeSession     = btreeSession;
            _hsmSession       = hsmSession;
            _blueprintSession = blueprintSession;
            if (_bpManager != null)
            {
                _bpManager.OnBreakpointHit += (bp, entity) =>
                {
                    _lastHitBreakpointId = bp.Id;
                    _entityMap.TryGetNetworkId(entity, out _lastHitNetworkId);
                };
            }
        }

        // ── Group A — Status ──────────────────────────────────────────────────

        /// <summary><c>GET /status</c> — full status payload (main thread).</summary>
        public JsonNode GetStatus()
        {
            return new JsonObject
            {
                ["scenario"]     = _editor.LoadedScenarioName,
                ["clusterState"] = CurrentClusterState().ToString(),
                ["simTime"]      = _time.TotalTime,
                ["timeScale"]    = _time.TimeScale,
                ["isPaused"]     = _time.IsPaused,
                ["inPreview"]    = _preview.IsInPreviewMode,
                ["entityCount"]  = _world.EntityCount,
                ["recording"]    = _isRecording,
            };
        }

        // ── Group B — Entity queries ───────────────────────────────────────────

        /// <summary>
        /// <c>GET /entities</c> — list (networkId, name, component type names) (main thread).
        /// Optional filters:
        /// <list type="bullet">
        ///   <item><paramref name="component"/> — only entities that have a component with this type name.</item>
        ///   <item><paramref name="near"/> — only entities within radius <c>r</c> of <c>(x,y)</c> using
        ///   the entity's <c>SimTransform.Position</c> (XZ-plane distance).
        ///   Format: "x,y,r" (comma-separated floats).</item>
        /// </list>
        /// Both filters are composable. When absent, all entities are returned (existing behavior).
        /// </summary>
        public JsonNode ListEntities(string? component = null, string? near = null)
        {
            var dumps = _extraction.ExtractEntities();

            // Parse ?near=x,y,r once before iterating.
            float nearX = 0f, nearY = 0f, nearRadius = 0f;
            bool filterNear = false;
            if (!string.IsNullOrWhiteSpace(near))
            {
                var parts = near.Split(',');
                if (parts.Length == 3
                    && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out nearX)
                    && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out nearY)
                    && float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out nearRadius))
                {
                    filterNear = true;
                }
            }

            bool filterComponent = !string.IsNullOrWhiteSpace(component);

            var arr = new JsonArray();
            foreach (var d in dumps)
            {
                // ── ?component= filter ─────────────────────────────────────────
                if (filterComponent)
                {
                    bool hasComp = d.Components.Keys.Any(k =>
                        string.Equals(k, component, StringComparison.OrdinalIgnoreCase));
                    if (!hasComp) continue;
                }

                // ── ?near= filter ──────────────────────────────────────────────
                if (filterNear)
                {
                    // Extract position from SimTransform component in the dump.
                    Vector3 pos = Vector3.Zero;
                    bool posFound = false;
                    if (d.Components.TryGetValue("SimTransform", out var stObj))
                    {
                        // The component may be a JsonElement (serializer path) or a raw struct (fallback).
                        if (stObj is System.Text.Json.JsonElement je)
                        {
                            if (je.TryGetProperty("Position", out var posEl))
                            {
                                // Vector3 is serialized as [x,y,z] array by both DefaultRelaxed
                                // (Vector3ArrayConverter) and DebugApiDumpOptions (DebugApiVector3SafeConverter).
                                if (posEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    int idx = 0;
                                    float px = 0f, py = 0f, pz = 0f;
                                    foreach (var el in posEl.EnumerateArray())
                                    {
                                        if (idx == 0 && el.TryGetSingle(out var xv)) px = xv;
                                        else if (idx == 1 && el.TryGetSingle(out var yv)) py = yv;
                                        else if (idx == 2 && el.TryGetSingle(out var zv)) pz = zv;
                                        idx++;
                                    }
                                    pos = new Vector3(px, py, pz);
                                    posFound = true;
                                }
                                else if (posEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    // Fallback: old object-style {"X":x,"Y":y,"Z":z} if any
                                    float px = posEl.TryGetProperty("X", out var xEl) && xEl.TryGetSingle(out var xv) ? xv : 0f;
                                    float py = posEl.TryGetProperty("Y", out var yEl) && yEl.TryGetSingle(out var yv) ? yv : 0f;
                                    float pz = posEl.TryGetProperty("Z", out var zEl) && zEl.TryGetSingle(out var zv) ? zv : 0f;
                                    pos = new Vector3(px, py, pz);
                                    posFound = true;
                                }
                            }
                        }
                        else if (stObj is SimTransform st)
                        {
                            pos = st.Position;
                            posFound = true;
                        }
                    }
                    if (!posFound) continue;

                    // XZ-plane radius test (ignore Y elevation) — matches the 2D spatial grid.
                    float dx = pos.X - nearX;
                    float dz = pos.Z - nearY;   // spatial Y in the near param maps to world Z
                    float distSq = dx * dx + dz * dz;
                    if (distSq > nearRadius * nearRadius) continue;
                }

                // ── Include entity ─────────────────────────────────────────────
                var comps = new JsonArray();
                foreach (var name in d.Components.Keys)
                    comps.Add(name);

                arr.Add(new JsonObject
                {
                    ["networkId"]  = d.NetworkId,
                    ["name"]       = ExtractEntityName(d),
                    ["components"] = comps,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>GET /entities/{networkId}</c> — full component dump via the serializer-injected
        /// extraction service. Returns <c>null</c> when the id is unknown (host → 404) (main thread).
        /// </summary>
        public JsonNode? DumpEntity(long networkId)
        {
            // Resolve must go through the map so an unknown id is reported as 404,
            // not silently returned as an empty dump.
            if (!_entityMap.TryGetEntity(networkId, out _))
                return null;

            var dumps = _extraction.ExtractEntities(new List<long> { networkId });
            if (dumps.Count == 0) return null;

            return DumpToJsonNode(dumps[0]);
        }

        // ── Group C — Event history ────────────────────────────────────────────

        /// <summary>
        /// <c>GET /events</c> — event history for the given bus, optionally filtered by type /
        /// since-frame / max. History retrieval + DTO mapping are thread-safe (off-thread OK).
        /// </summary>
        public JsonNode GetEvents(string? bus = "world", string? type = null, uint since = 0, int max = DefaultMaxEvents)
        {
            string provider = string.Equals(bus, "orchestration", StringComparison.OrdinalIgnoreCase)
                ? "Orchestration"
                : "World";

            var history = _eventHistory.GetHistory(new[] { provider });

            IEnumerable<CapturedEventDto> filtered = history;
            if (!string.IsNullOrEmpty(type))
                filtered = filtered.Where(e => string.Equals(e.TypeName, type, StringComparison.OrdinalIgnoreCase));
            if (since > 0)
                filtered = filtered.Where(e => e.Frame >= since);

            if (max < 0) max = DefaultMaxEvents;
            // Most-recent-first, bounded.
            var page = filtered.Reverse().Take(max).ToList();

            var arr = new JsonArray();
            foreach (var e in page)
            {
                JsonNode? payload = null;
                try
                {
                    // EventSerializationHelper produces inspector-grade readable JSON; parse it
                    // back into a JsonNode so it passes through the envelope verbatim.
                    var json = EventSerializationHelper.SerializeToJson(e.RawEvent);
                    payload  = JsonNode.Parse(json);
                }
                catch
                {
                    payload = null; // unserializable payload — keep the metadata row.
                }

                arr.Add(new JsonObject
                {
                    ["frame"]     = e.Frame,
                    ["provider"]  = e.ProviderName,
                    ["type"]      = e.TypeName,
                    ["isManaged"] = e.IsManaged,
                    ["summary"]   = e.Summary,
                    ["payload"]   = payload,
                });
            }
            return arr;
        }

        // ── Group J — Logs ────────────────────────────────────────────────────

        /// <summary>Default maximum log entries to return.</summary>
        public const int DefaultMaxLogs = 200;

        /// <summary>
        /// <c>GET /logs</c> — query the in-memory log sinks off-thread (lock-guarded).
        ///
        /// <para><b>Filter semantics:</b></para>
        /// <list type="bullet">
        ///   <item><paramref name="level"/> — <b>minimum level</b> (inclusive). E.g. <c>"Info"</c>
        ///     includes Info, Warning, Error, Critical. Case-insensitive. When absent, all levels.</item>
        ///   <item><paramref name="logger"/> — exact or prefix match on <c>MessageLogEntry.LoggerName</c>.
        ///     Case-insensitive substring match. When absent, all loggers.</item>
        ///   <item><paramref name="since"/> — ISO-8601 or round-trip DateTime string; only entries
        ///     with <c>Timestamp &gt;= since</c> are included. When absent, all entries.</item>
        ///   <item><paramref name="max"/> — upper bound on the returned count (default 200).
        ///     Most-recent entries are returned first.</item>
        /// </list>
        /// </summary>
        public JsonNode GetLogs(
            string? level  = null,
            string? logger = null,
            string? since  = null,
            int     max    = DefaultMaxLogs)
        {
            // Parse minimum level filter.
            LogSeverity? minLevel = null;
            if (!string.IsNullOrWhiteSpace(level))
            {
                if (Enum.TryParse<LogSeverity>(level, ignoreCase: true, out var parsed))
                    minLevel = parsed;
                // Unknown level string → ignore the filter (return everything).
            }

            // Parse since-timestamp filter.
            DateTime? sinceTime = null;
            if (!string.IsNullOrWhiteSpace(since))
            {
                if (DateTime.TryParse(since,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var parsedDt))
                    sinceTime = parsedDt;
                // Unparseable since → ignore the filter.
            }

            if (max <= 0) max = DefaultMaxLogs;

            // Collect from all registered sinks (off-thread: each sink uses lock-guarded GetMessages()).
            var allEntries = new List<MessageLogEntry>();
            foreach (var sink in _logSinks)
                allEntries.AddRange(sink.GetMessages());

            // Apply filters.
            IEnumerable<MessageLogEntry> filtered = allEntries;

            if (minLevel.HasValue)
                filtered = filtered.Where(e => e.Severity >= minLevel.Value);

            if (!string.IsNullOrWhiteSpace(logger))
                filtered = filtered.Where(e =>
                    e.LoggerName?.IndexOf(logger, StringComparison.OrdinalIgnoreCase) >= 0);

            if (sinceTime.HasValue)
                filtered = filtered.Where(e => e.Timestamp >= sinceTime.Value);

            // Most-recent first, bounded by max.
            var page = filtered.OrderByDescending(e => e.Timestamp).Take(max).ToList();

            var arr = new JsonArray();
            foreach (var e in page)
            {
                arr.Add(new JsonObject
                {
                    ["timestamp"] = e.Timestamp.ToString("O"),  // ISO-8601 round-trip format
                    ["level"]     = e.Severity.ToString(),
                    ["logger"]    = e.LoggerName,
                    ["message"]   = e.Message,
                });
            }
            return arr;
        }

        // ── Group D — Sim / preview / time control ─────────────────────────────

        /// <summary><c>GET /sim/state</c> (main thread).</summary>
        public JsonNode GetSimState() => new JsonObject
        {
            ["isPaused"]  = _time.IsPaused,
            ["inPreview"] = _preview.IsInPreviewMode,
            ["totalTime"] = _time.TotalTime,
            ["timeScale"] = _time.TimeScale,
        };

        /// <summary><c>POST /sim/play</c> — explicit resume; idempotent (never blind-toggles) (main thread).</summary>
        public JsonNode Play()
        {
            // IsPaused is true when not-in-preview OR paused; toggling moves toward "running".
            if (_time.IsPaused)
                _time.TogglePlayPause();
            return GetSimState();
        }

        /// <summary><c>POST /sim/pause</c> — explicit pause; idempotent (main thread).</summary>
        public JsonNode Pause()
        {
            if (!_time.IsPaused)
                _time.TogglePlayPause();
            return GetSimState();
        }

        /// <summary><c>POST /sim/step {count?}</c> — discrete single-step(s) (main thread).</summary>
        public JsonNode Step(int count = 1)
        {
            if (count < 1) count = 1;
            for (int i = 0; i < count; i++)
                _time.Step();
            return GetSimState();
        }

        /// <summary><c>POST /sim/timescale {scale}</c> (main thread).</summary>
        public JsonNode SetTimeScale(float scale)
        {
            _time.SetTimeScale(scale);
            return GetSimState();
        }

        /// <summary><c>POST /preview/enter {startPaused?}</c> (main thread).</summary>
        public JsonNode EnterPreview(bool startPaused = false)
        {
            if (!_preview.IsInPreviewMode)
                _preview.EnterPreviewMode(startPaused);
            return GetSimState();
        }

        /// <summary><c>POST /preview/exit</c> (main thread).</summary>
        public JsonNode ExitPreview()
        {
            if (_preview.IsInPreviewMode)
                _preview.ExitPreviewMode();
            return GetSimState();
        }

        // ── Group E — Scenario list / load / save ──────────────────────────────

        /// <summary><c>GET /scenarios</c> — available scenario names (thread-safe enough; main thread used).</summary>
        public JsonNode ListScenarios()
        {
            var arr = new JsonArray();
            foreach (var s in _editor.AvailableScenarios)
                arr.Add(s);
            return arr;
        }

        /// <summary>
        /// <c>POST /scenario/load {name, waitForReady?}</c>. Initiates the load. When
        /// <paramref name="waitForReady"/> is false, returns immediately. The blocking wait
        /// (poll <c>ClusterStateUpdateEvent.CurrentState == OperatingEdit</c>) is performed by the
        /// caller via <see cref="PollClusterStateIsOperatingEdit"/> across kernel ticks (the job
        /// queue marshals one poll per drain); this method just kicks the load off.
        /// </summary>
        public void BeginLoadScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));
            _editor.LoadScenarioByName(name);
        }

        /// <summary>
        /// Reads the orchestration bus for the latest cluster-state and returns true once it is
        /// <see cref="ClusterState.OperatingEdit"/>. <b>Must run on the main thread</b> — it both
        /// drives <c>IEditorLogic.Update()</c> (which consumes the orchestration events and advances
        /// the load state machine) and inspects the resulting state. <c>LoadedScenarioName</c> is
        /// deliberately NOT used as the completion signal (set at frame 0).
        /// </summary>
        public bool PollClusterStateIsOperatingEdit() => CurrentClusterState() == ClusterState.OperatingEdit;

        /// <summary><c>POST /scenario/save {name}</c> — persists the authored world (main thread).</summary>
        public JsonNode SaveScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));
            _editor.SaveScenarioAs(name);
            return new JsonObject { ["saved"] = name };
        }

        // ── Group F — Commands + discovery + spawn ─────────────────────────────

        /// <summary>
        /// <c>GET /commands</c> — enumerate publishable FDP event types with field schemas.
        /// Thread-safe (registry is read-only after boot).
        /// </summary>
        public JsonNode ListCommands()
        {
            var types = EventType.GetAllRegistered();
            var arr   = new JsonArray();
            foreach (var t in types)
            {
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]   = t.Name,
                    ["fields"] = fa,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>GET /components</c> — enumerate registered ECS component types with field schemas.
        /// Thread-safe (registry is read-only after boot).
        /// </summary>
        public JsonNode ListComponents()
        {
            var types = ComponentTypeRegistry.GetAllTypes();
            var arr   = new JsonArray();
            foreach (var t in types)
            {
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]   = t.Name,
                    ["fields"] = fa,
                });
            }
            return arr;
        }

        /// <summary>
        /// <c>POST /entities/command {eventType, payload, wait?}</c> — deserialize the payload
        /// to the named event CLR type and publish on <c>_world.Bus</c>.
        ///
        /// <para>Wait-gating: if <paramref name="wait"/> is <c>true</c> and time is advancing
        /// (<c>InPreview &amp;&amp; !IsPaused</c>), blocks until a correlated ack
        /// (<c>MissionControlAckEvent</c> by <c>RequestId</c>) arrives or the timeout expires.
        /// Otherwise returns immediately with <c>awaited:false</c>.</para>
        ///
        /// <para>Must run on the main thread.</para>
        /// </summary>
        /// <returns>JsonNode with { awaited, reason? } or null on 400 (unknown type).</returns>
        public (JsonNode? result, string? error) SendCommand(string eventTypeName, JsonNode? payload, bool wait)
        {
            // Resolve event type by name across all registered types.
            var registeredTypes = EventType.GetAllRegistered();
            Type? clrType = registeredTypes.FirstOrDefault(t =>
                string.Equals(t.Name, eventTypeName, StringComparison.OrdinalIgnoreCase));

            if (clrType is null)
                return (null, $"Unknown eventType: '{eventTypeName}'");

            // Determine if time is advancing (InPreview && !Paused).
            bool timeAdvancing = _preview.IsInPreviewMode && !_time.IsPaused;

            // Deserialize the payload JSON to the target CLR type.
            // We use System.Text.Json (default options) for the payload fields since that's
            // what callers provide; the inspector-grade path is for output only.
            object? evt = null;
            if (payload != null)
            {
                try
                {
                    var json = payload.ToJsonString();
                    evt = JsonSerializer.Deserialize(json, clrType);
                }
                catch (Exception ex)
                {
                    return (null, $"Failed to deserialize payload for '{eventTypeName}': {ex.Message}");
                }
            }
            else
            {
                // Create a default instance.
                try { evt = Activator.CreateInstance(clrType); }
                catch { evt = null; }
            }

            // Publish via the appropriate bus method (unmanaged struct → Publish, managed → PublishManaged).
            try
            {
                PublishEventObject(clrType, evt);
            }
            catch (Exception ex)
            {
                return (null, $"Failed to publish event '{eventTypeName}': {ex.Message}");
            }

            // Wait-gating: only meaningful when time is advancing.
            if (!wait || !timeAdvancing)
            {
                return (new JsonObject
                {
                    ["awaited"] = false,
                    ["reason"]  = "sim not running",
                }, null);
            }

            // Time is advancing and wait==true: attempt correlated ack wait (best-effort).
            // For now: publish + return awaited:false with reason "ack-wait not supported for this type".
            // The MissionControlAckEvent correlated path is logged as debt (ADA-04-D01) —
            // it requires polling the bus across kernel ticks which the current synchronous
            // main-thread job does not support without a multi-tick continuation.
            return (new JsonObject
            {
                ["awaited"] = false,
                ["reason"]  = "ack-wait not yet supported; event published",
            }, null);
        }

        /// <summary>
        /// <c>POST /entities/spawn {tkbType, transform?, components?, attributesJson?}</c>
        /// — builds and publishes a <see cref="SpawnEntityCommand"/>. Returns <c>awaited</c>
        /// per the wait rule. Must run on the main thread.
        /// </summary>
        public JsonNode SpawnEntity(
            long     tkbType,
            JsonNode? transform      = null,
            JsonNode? components     = null,
            string?  attributesJson = null)
        {
            var cmd = new SpawnEntityCommand
            {
                TkbType             = tkbType,
                NetworkId           = 0,          // 0 = allocate a new ID
                OwnerNodeId         = 0,
                InitType            = ReliableInitType.None,
                InitialAttributesJson = attributesJson,
            };

            // Parse optional transform.
            if (transform != null)
            {
                try
                {
                    var simTransform = JsonSerializer.Deserialize<SimTransform>(transform.ToJsonString());
                    cmd.InitialTransform = simTransform;
                }
                catch { /* ignore malformed transform */ }
            }

            // Parse optional extra components list (array of { type, data } or typed objects).
            if (components != null && components is JsonArray compArr && compArr.Count > 0)
            {
                cmd.InitialComponents = new List<object>();
                foreach (var item in compArr)
                {
                    if (item is null) continue;
                    // Support { "type": "TypeName", "data": {...} } format.
                    var typeName = item["type"]?.GetValue<string>();
                    var data     = item["data"];
                    if (typeName != null)
                    {
                        var compType = ComponentTypeRegistry.GetAllTypes()
                            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
                        if (compType != null && data != null)
                        {
                            try
                            {
                                var compObj = JsonSerializer.Deserialize(data.ToJsonString(), compType);
                                if (compObj != null)
                                    cmd.InitialComponents.Add(compObj);
                            }
                            catch { /* ignore undeserializable component */ }
                        }
                    }
                }
            }

            _world.Bus.PublishManaged(cmd);

            bool timeAdvancing = _preview.IsInPreviewMode && !_time.IsPaused;
            return new JsonObject
            {
                ["spawned"]  = true,
                ["tkbType"]  = tkbType,
                ["awaited"]  = false,
                ["reason"]   = timeAdvancing ? null : (JsonNode?)"sim not running",
            };
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private ClusterState CurrentClusterState() => _clusterState();

        /// <summary>
        /// Publishes an event object to <c>_world.Bus</c> using reflection to call the
        /// appropriate generic <c>Publish&lt;T&gt;</c> / <c>PublishManaged&lt;T&gt;</c>.
        /// Unmanaged value types → <c>Publish</c>; everything else → <c>PublishManaged</c>.
        /// </summary>
        private void PublishEventObject(Type clrType, object? evt)
        {
            if (clrType.IsValueType)
            {
                // Unmanaged struct path — call Bus.Publish<T>(evt) via reflection.
                var method = typeof(FdpEventBus)
                    .GetMethod(nameof(FdpEventBus.Publish), BindingFlags.Public | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                // Box the value if null (create default).
                var value = evt ?? Activator.CreateInstance(clrType)!;
                method.Invoke(_world.Bus, new[] { value });
            }
            else
            {
                // Managed class/struct path — call Bus.PublishManaged<T>(evt) via reflection.
                var method = typeof(FdpEventBus)
                    .GetMethod(nameof(FdpEventBus.PublishManaged), BindingFlags.Public | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                var value = evt ?? Activator.CreateInstance(clrType)!;
                method.Invoke(_world.Bus, new[] { value });
            }
        }

        private static string? ExtractEntityName(EntityStateDumpDto dump)
        {
            if (dump.Components.TryGetValue("EntityInfo", out var infoObj) &&
                infoObj is JsonElement je && je.ValueKind == JsonValueKind.Object &&
                je.TryGetProperty("Name", out var nameEl) &&
                nameEl.ValueKind == JsonValueKind.String)
            {
                return nameEl.GetString();
            }
            return null;
        }

        private static JsonNode DumpToJsonNode(EntityStateDumpDto dump)
        {
            // Serialize through the DebugApi-scoped NaN-safe options directly to a JsonNode
            // (avoids the string round-trip and the fragile JsonNode.Parse step that rejects
            // NaN/Infinity named literals).  Non-finite floats are emitted as string sentinels
            // ("NaN"/"Infinity"/"-Infinity") so the output is valid standard JSON accepted by
            // Node's JSON.parse and any RFC-8259 parser.
            return JsonSerializer.SerializeToNode(dump, DebugApiDumpOptions)!;
        }

        // ── Group M — TKB catalog ──────────────────────────────────────────────

        /// <summary>GET /tkb/types — list all TKB templates, optionally filtered by category.</summary>
        public JsonNode ListTkbTypes(string? category = null)
        {
            var templates = string.IsNullOrEmpty(category)
                ? _tkbDb.GetAll()
                : _tkbDb.GetEntitiesByCategory(category);
            var arr = new JsonArray();
            foreach (var t in templates)
            {
                arr.Add(new JsonObject
                {
                    ["tkbType"]      = t.TkbType,
                    ["name"]         = t.Name,
                    ["categoryPath"] = t.CategoryPath,
                    ["disType"]      = t.DisType.ToString(),
                });
            }
            return arr;
        }

        /// <summary>GET /tkb/types/{tkbType} — full descriptor for one TKB type.</summary>
        public JsonNode? GetTkbType(long tkbType)
        {
            if (!_tkbDb.TryGetByType(tkbType, out var t))
                return null;

            // Mandatory components
            var mandatoryArr = new JsonArray();
            foreach (var mc in t.MandatoryComponents)
                mandatoryArr.Add(new JsonObject
                {
                    ["componentTypeId"]   = mc.ComponentTypeId,
                    ["isHard"]            = mc.IsHard,
                    ["softTimeoutFrames"] = (long)mc.SoftTimeoutFrames,
                });

            // Child blueprints
            var childArr = new JsonArray();
            foreach (var cb in t.ChildBlueprints)
            {
                try
                {
                    var json = EventSerializationHelper.SerializeToJson(cb);
                    childArr.Add(JsonNode.Parse(json));
                }
                catch { /* skip unserializable */ }
            }

            // Descriptor bag
            var descrArr = new JsonArray();
            foreach (var (type, partId, data) in t.GetAllDescriptors())
            {
                try
                {
                    var dJson = EventSerializationHelper.SerializeToJson(data);
                    descrArr.Add(new JsonObject
                    {
                        ["type"]   = type.Name,
                        ["partId"] = partId,
                        ["data"]   = JsonNode.Parse(dJson),
                    });
                }
                catch { /* skip unserializable descriptor */ }
            }

            return new JsonObject
            {
                ["tkbType"]             = t.TkbType,
                ["name"]                = t.Name,
                ["categoryPath"]        = t.CategoryPath,
                ["disType"]             = t.DisType.ToString(),
                ["mandatoryComponents"] = mandatoryArr,
                ["childBlueprints"]     = childArr,
                ["descriptors"]         = descrArr,
            };
        }

        // ── Group N — world/coordinate info ───────────────────────────────────

        /// <summary>GET /world/info — geo origin, spatial grid extent, terrain/navmesh null.</summary>
        public JsonNode GetWorldInfo()
        {
            var origin = _geoTransform.Origin;
            float extentMinX = _spatialGridOriginX;
            float extentMaxX = _spatialGridOriginX + _spatialGridWidth * _spatialGridCellSize;
            float extentMinY = _spatialGridOriginY;
            float extentMaxY = _spatialGridOriginY + _spatialGridHeight * _spatialGridCellSize;

            return new JsonObject
            {
                ["geo"] = new JsonObject
                {
                    ["origin"] = new JsonObject
                    {
                        ["lat"] = origin.lat,
                        ["lon"] = origin.lon,
                        ["alt"] = origin.alt,
                    },
                },
                ["spatialGrid"] = new JsonObject
                {
                    ["cellSize"] = _spatialGridCellSize,
                    ["originX"]  = _spatialGridOriginX,
                    ["originY"]  = _spatialGridOriginY,
                    ["width"]    = _spatialGridWidth,
                    ["height"]   = _spatialGridHeight,
                    ["extent"]   = new JsonObject
                    {
                        ["minX"] = extentMinX,
                        ["maxX"] = extentMaxX,
                        ["minY"] = extentMinY,
                        ["maxY"] = extentMaxY,
                    },
                },
                ["terrain"]  = JsonValue.Create<object?>(null),
                ["navmesh"]  = JsonValue.Create<object?>(null),
            };
        }

        /// <summary>POST /world/geo-to-local — convert geodetic to local ENU coordinates.</summary>
        public JsonNode GeoToLocal(double lat, double lon, double alt, float? headingDeg)
        {
            var pos = _geoTransform.ToCartesian(lat, lon, alt);
            var obj = new JsonObject
            {
                ["x"] = pos.X,
                ["y"] = pos.Y,
                ["z"] = pos.Z,
            };
            if (headingDeg.HasValue)
            {
                var rot = SimTransformBridgeSystem.HeadingDegToRotation(headingDeg.Value);
                obj["rotation"] = new JsonObject
                {
                    ["x"] = rot.X,
                    ["y"] = rot.Y,
                    ["z"] = rot.Z,
                    ["w"] = rot.W,
                };
            }
            return obj;
        }

        /// <summary>POST /world/local-to-geo — convert local ENU to geodetic coordinates.</summary>
        public JsonNode LocalToGeo(float x, float y, float z, Quaternion? rotation)
        {
            var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(x, y, z));
            var obj = new JsonObject
            {
                ["lat"] = lat,
                ["lon"] = lon,
                ["alt"] = alt,
            };
            if (rotation.HasValue)
            {
                float hdg = SimTransformBridgeSystem.RotationToHeadingDeg(rotation.Value);
                obj["headingDeg"] = hdg;
            }
            return obj;
        }

        // ── Group G — Breakpoints ──────────────────────────────────────────────

        /// <summary>
        /// POST /breakpoints — register a breakpoint from a polymorphic SearchPredicateDto.
        /// Returns { breakpointId } or throws on invalid input (400 via host).
        /// Must run on the main thread.
        /// </summary>
        public JsonNode AddBreakpoint(JsonNode? body)
        {
            if (_bpManager is null)
                throw new InvalidOperationException("Breakpoint manager not available.");

            var conditionNode = body?["condition"];
            if (conditionNode is null)
                throw new ArgumentException("condition is required.");

            SearchPredicateDto condition;
            try
            {
                var json = conditionNode.ToJsonString();
                condition = JsonSerializer.Deserialize<SearchPredicateDto>(json, SearchPredicateJsonOptions)
                    ?? throw new ArgumentException("condition deserialized to null.");
            }
            catch (ArgumentException) { throw; }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid condition: {ex.Message}");
            }

            // Resolve optional filterNetworkId
            Entity? filterEntity = null;
            var filterNode = body?["filterNetworkId"];
            if (filterNode is not null)
            {
                long filterNetworkId = filterNode.GetValue<long>();
                if (!_entityMap.TryGetEntity(filterNetworkId, out var fe))
                    throw new ArgumentException($"filterNetworkId {filterNetworkId} not found.");
                filterEntity = fe;
            }

            int occurrenceThreshold = body?["occurrenceThreshold"]?.GetValue<int>() ?? 1;
            string name = body?["name"]?.GetValue<string>() ?? "";

            var id = _bpManager.AddBreakpoint(condition, filterEntity, occurrenceThreshold, name);

            return new JsonObject { ["breakpointId"] = id.ToString() };
        }

        /// <summary>GET /breakpoints — list all registered breakpoints.</summary>
        public JsonNode ListBreakpoints()
        {
            if (_bpManager is null)
                throw new InvalidOperationException("Breakpoint manager not available.");

            var arr = new JsonArray();
            foreach (var bp in _bpManager.AllBreakpoints)
            {
                arr.Add(new JsonObject
                {
                    ["id"]                  = bp.Id.ToString(),
                    ["conditionSummary"]    = BreakpointConditionSummarizer.Summarize(bp.Condition),
                    ["enabled"]             = bp.Enabled,
                    ["occurrenceThreshold"] = bp.OccurrenceThreshold,
                    ["hitCount"]            = bp.HitCount,
                    ["name"]                = bp.DisplayName,
                });
            }
            return arr;
        }

        /// <summary>DELETE /breakpoints/{id} — remove a breakpoint by its string id (e.g. "BP#3").</summary>
        public void RemoveBreakpoint(string idStr)
        {
            if (_bpManager is null)
                throw new InvalidOperationException("Breakpoint manager not available.");

            BreakpointId id = ParseBreakpointId(idStr);
            _bpManager.Remove(id);
        }

        /// <summary>GET /breakpoints/hits — current pause state + last hit info.</summary>
        public JsonNode GetBreakpointStatus()
        {
            if (_bpManager is null)
                throw new InvalidOperationException("Breakpoint manager not available.");

            JsonNode? lastHit = null;
            if (_lastHitBreakpointId.IsValid)
            {
                lastHit = new JsonObject
                {
                    ["breakpointId"] = _lastHitBreakpointId.ToString(),
                    ["networkId"]    = _lastHitNetworkId,
                };
            }

            return new JsonObject
            {
                ["isPaused"]   = _bpManager.IsPaused,
                ["pausedTick"] = _bpManager.PausedTick,
                ["lastHit"]    = lastHit,
            };
        }

        private BreakpointId ParseBreakpointId(string idStr)
        {
            if (_bpManager is null) throw new InvalidOperationException("No bp manager.");
            foreach (var bp in _bpManager.AllBreakpoints)
            {
                if (string.Equals(bp.Id.ToString(), idStr, StringComparison.OrdinalIgnoreCase))
                    return bp.Id;
            }
            // Also try "N" as shorthand for "BP#N"
            if (!idStr.StartsWith("BP#", StringComparison.OrdinalIgnoreCase))
            {
                string withPrefix = $"BP#{idStr}";
                foreach (var bp in _bpManager.AllBreakpoints)
                {
                    if (string.Equals(bp.Id.ToString(), withPrefix, StringComparison.OrdinalIgnoreCase))
                        return bp.Id;
                }
            }
            throw new ArgumentException($"Breakpoint '{idStr}' not found.");
        }

        // ── Group H — Checkpoint / Restore / Diff ─────────────────────────────────

        /// <summary>POST /checkpoint — single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true).</summary>
        public JsonNode Checkpoint()
        {
            // Reject if live run is active (mutually exclusive)
            var state = CurrentClusterState();
            if (state == ClusterState.OperatingLive)
                throw new InvalidOperationException("Cannot checkpoint during a live run.");

            // Single slot: reject if already in preview (entered via /preview/enter OR a prior /checkpoint)
            if (_preview.IsInPreviewMode)
                throw new InvalidOperationException("Already checkpointed or in preview. Exit preview or restore first.");

            _preview.EnterPreviewMode(startPaused: true);
            return GetStatus();
        }

        /// <summary>POST /checkpoint/restore — rewind to the checkpoint snapshot via IPreviewController.ExitPreviewMode().</summary>
        public JsonNode RestoreCheckpoint()
        {
            if (!_preview.IsInPreviewMode)
                throw new InvalidOperationException("No checkpoint to restore — not in preview mode.");
            _preview.ExitPreviewMode();
            return GetStatus();
        }

        /// <summary>
        /// POST /diff/capture {entities?} — serialize current entity states and store as a named baseline.
        /// Returns { baselineId }.
        /// </summary>
        public JsonNode CaptureBaseline(IEnumerable<long>? entityNetworkIds = null)
        {
            var id = $"BL#{++_nextBaselineId}";
            var snapshot = SerializeEntitySnapshot(entityNetworkIds);
            _diffBaselines[id] = snapshot;
            return new JsonObject { ["baselineId"] = id };
        }

        /// <summary>
        /// POST /diff/compare {baselineId, entities?} — diff the specified baseline against current state.
        /// Returns a DiffNode tree per entity.
        /// </summary>
        public JsonNode CompareBaseline(string baselineId, IEnumerable<long>? entityNetworkIds = null)
        {
            if (!_diffBaselines.TryGetValue(baselineId, out var before))
                throw new ArgumentException($"Unknown baselineId: '{baselineId}'.");

            // When no entity scope is given, snapshot ALL current entities so that entity births
            // (new entities not in the baseline) are captured in the diff union.
            var after = SerializeEntitySnapshot(entityNetworkIds);
            return BuildDiffResult(before, after);
        }

        /// <summary>
        /// POST /diff {entities?} — diff the current checkpoint snapshot against current state.
        /// Requires an active checkpoint (inPreview:true). Captures "before" from the checkpoint
        /// (re-serializes current state as "after").
        /// </summary>
        public JsonNode DiffFromCheckpoint(IEnumerable<long>? entityNetworkIds = null)
        {
            if (!_preview.IsInPreviewMode)
                throw new InvalidOperationException("No checkpoint active. Use POST /checkpoint first, or use /diff/capture + /diff/compare for a checkpoint-independent diff.");

            // Re-serialize current (post-mutation) state as "after".
            // We don't have the pre-mutation "before" stored separately — document as a limitation.
            // The checkpoint itself is the revert point, not a serialized snapshot.
            // For the diff-from-checkpoint case, we capture current state as "after" and return
            // an empty diff (since we don't have the pre-mutation data). This is a design constraint:
            // callers should use capture+compare for a real diff.
            // Instead: the caller should use /diff/capture BEFORE mutating, then /diff/compare after.
            throw new InvalidOperationException("diff-from-checkpoint requires a separate /diff/capture before mutation. Use POST /diff/capture (baselineId), mutate, then POST /diff/compare {baselineId}.");
        }

        private Dictionary<long, JsonNode?> SerializeEntitySnapshot(IEnumerable<long>? networkIds = null)
        {
            var result = new Dictionary<long, JsonNode?>();
            IEnumerable<long> ids = networkIds ?? GetAllNetworkIds();
            foreach (var nid in ids)
            {
                var node = DumpEntity(nid);
                result[nid] = node;
            }
            return result;
        }

        private IEnumerable<long> GetAllNetworkIds()
        {
            var dumps = _extraction.ExtractEntities();
            foreach (var d in dumps)
                yield return d.NetworkId;
        }

        private JsonNode BuildDiffResult(Dictionary<long, JsonNode?> before, Dictionary<long, JsonNode?> after)
        {
            var allIds = new HashSet<long>(before.Keys);
            foreach (var id in after.Keys) allIds.Add(id);

            var entityDiffs = new JsonArray();
            foreach (var nid in allIds.OrderBy(x => x))
            {
                before.TryGetValue(nid, out var bNode);
                after.TryGetValue(nid, out var aNode);

                var diffNodes = _diffService.ComputeTreeDiff(bNode, aNode, epsilonTolerance: 0.001);

                // Only include entities that actually changed
                if (!diffNodes.Any(d => d.IsModified))
                    continue;

                var diffArr = new JsonArray();
                foreach (var dn in diffNodes.Where(d => d.IsModified))
                    diffArr.Add(SerializeDiffNode(dn));

                entityDiffs.Add(new JsonObject
                {
                    ["networkId"] = nid,
                    ["changed"] = true,
                    ["diff"] = diffArr,
                });
            }

            return new JsonObject { ["entities"] = entityDiffs };
        }

        private static JsonNode SerializeDiffNode(DiffNode node)
        {
            if (node is DiffValue val)
            {
                return new JsonObject
                {
                    ["name"]      = val.Name,
                    ["type"]      = "value",
                    ["oldValue"]  = val.OldValue,
                    ["newValue"]  = val.NewValue,
                    ["modified"]  = val.IsModified,
                };
            }
            if (node is DiffObject obj)
            {
                var children = new JsonArray();
                foreach (var child in obj.Children.Where(c => c.IsModified))
                    children.Add(SerializeDiffNode(child));
                return new JsonObject
                {
                    ["name"]     = obj.Name,
                    ["type"]     = "object",
                    ["modified"] = obj.IsModified,
                    ["children"] = children,
                };
            }
            return new JsonObject { ["name"] = node.Name, ["type"] = "unknown", ["modified"] = node.IsModified };
        }

        // ── Group I — Recording ──────────────────────────────────────────────────

        /// <summary>Whether recording is currently active.</summary>
        public bool IsRecording => _isRecording;

        /// <summary>
        /// Phase-1 (main-thread sync): validate, enter preview, set flags, return fdpPath.
        /// Called via RunOnMainThread. Phase-2 is <see cref="CompleteRecordingStartAsync"/>.
        /// </summary>
        public string BeginRecordingStart(string mode)
        {
            if (_rrController is null)
                throw new InvalidOperationException("EcsRecordReplayController not available.");
            if (_isRecording)
                throw new InvalidOperationException("Recording already active. Stop it first.");

            if (string.Equals(mode, "preview", StringComparison.OrdinalIgnoreCase))
            {
                if (_preview.IsInPreviewMode)
                    throw new InvalidOperationException("Already in preview mode (checkpoint slot occupied). Cannot start preview recording.");

                _activeRecordingExerciseId = Guid.NewGuid();
                _lastFdpPath = System.IO.Path.Combine(
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.DefaultStagingDirectory,
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.ExercisesDirectoryName,
                    _activeRecordingExerciseId.ToString(),
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.GetNodeRecordingFileName(0));

                _preview.EnterPreviewMode(startPaused: true);
                // _isRecording is set by CompleteRecordingStartAsync after PrepareRecordingAsync succeeds.
                return _lastFdpPath;
            }
            else if (string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase))
            {
                // DEBT: ADA-I-D01 — live mode requires full cluster setup.
                throw new InvalidOperationException("Live mode recording is not supported in editor mode. Use mode:preview.");
            }
            else
            {
                throw new ArgumentException($"Unknown mode '{mode}'. Use 'preview' or 'live'.");
            }
        }

        /// <summary>
        /// Phase-2 (background-thread async): install the recording module.
        /// Must NOT be called from inside RunOnMainThread — InstallModuleAsync awaits the main
        /// thread's BeforeSync boundary (swapTcs) which would deadlock if the main thread is blocked.
        /// </summary>
        public async System.Threading.Tasks.Task CompleteRecordingStartAsync()
        {
            if (_rrController is null)
                throw new InvalidOperationException("EcsRecordReplayController not available.");
            await _rrController.PrepareRecordingAsync(_activeRecordingExerciseId,
                Fdp.Toolkit.Orchestration.OrchestrationConstants.DefaultStagingDirectory)
                .ConfigureAwait(false);
            _isRecording = true;
        }

        /// <summary>
        /// Phase-1 (background-thread async): finalize the recording module.
        /// Must NOT be called from inside RunOnMainThread — UninstallModuleAsync awaits the main
        /// thread's BeforeSync boundary which would deadlock if the main thread is blocked.
        /// </summary>
        public async System.Threading.Tasks.Task<string?> CompleteRecordingStopAsync()
        {
            if (_rrController is null)
                throw new InvalidOperationException("EcsRecordReplayController not available.");
            if (!_isRecording)
                throw new InvalidOperationException("No active recording.");

            _isRecording = false;

            // Finalize BEFORE the exit rewind (hard ordering rule).
            await _rrController.FinalizeRecordingAsync(maxNetworkId: 0).ConfigureAwait(false);
            return _lastFdpPath;
        }

        /// <summary>
        /// Phase-2 (main-thread sync): exit preview (triggers rewind). Returns status JsonNode.
        /// Called via RunOnMainThread AFTER CompleteRecordingStopAsync.
        /// </summary>
        public JsonNode FinishRecordingStop()
        {
            // For preview mode: now safe to exit (rewind happens here).
            if (_preview.IsInPreviewMode)
                _preview.ExitPreviewMode();

            return new JsonObject
            {
                ["recording"] = false,
                ["fdpPath"]   = _lastFdpPath,
            };
        }

        /// <summary>
        /// Convenience async method for tests that can await across kernel ticks.
        /// DO NOT call this from inside RunOnMainThread — it deadlocks.
        /// For tests: call directly from the test thread, then pump frames during the await.
        /// </summary>
        public async System.Threading.Tasks.Task<JsonNode> StartRecordingAsync(string mode)
        {
            // Phase 1: sync setup (validates + enters preview) — safe to call on any thread
            // when the main loop is not blocked (tests call this directly).
            var fdpPath = BeginRecordingStart(mode);

            // Phase 2: async module install — must NOT block the main thread.
            await CompleteRecordingStartAsync().ConfigureAwait(false);

            return new JsonObject
            {
                ["recording"] = true,
                ["mode"]      = mode,
                ["fdpPath"]   = fdpPath,
            };
        }

        /// <summary>
        /// Convenience async method for tests that can await across kernel ticks.
        /// DO NOT call this from inside RunOnMainThread — it deadlocks.
        /// </summary>
        public async System.Threading.Tasks.Task<JsonNode> StopRecordingAsync()
        {
            var fdpPath = await CompleteRecordingStopAsync().ConfigureAwait(false);
            return FinishRecordingStop();
        }

        // ── Group I — Replay (isolated) ───────────────────────────────────────────

        /// <summary>Whether replay is currently active (queries route to sandbox).</summary>
        public bool IsReplayActive => _replayContext != null;

        /// <summary>Current frame in the replay sandbox (-1 if not active).</summary>
        public int ReplayCurrentFrame => _replayContext?.CurrentFrame ?? -1;

        /// <summary>Total frames in the loaded replay (0 if not active).</summary>
        public int ReplayTotalFrames => _replayContext?.Playback?.TotalFrames ?? 0;

        /// <summary>POST /replay/load {fdpPath} — stand up isolated ReplayBrowserContext (main thread).</summary>
        public JsonNode LoadReplay(string fdpPath)
        {
            if (string.IsNullOrWhiteSpace(fdpPath))
                throw new ArgumentException("fdpPath is required.", nameof(fdpPath));
            if (!System.IO.File.Exists(fdpPath))
                throw new ArgumentException($"File not found: {fdpPath}");

            // Dispose any existing replay context.
            _replayContext?.Dispose();
            _replayContext = null;
            _replayExtraction = null;

            var ctx = new Fdp.Toolkit.ReplayBrowser.ReplayBrowserContext();
            ctx.LoadRecording(fdpPath);

            if (ctx.Playback == null)
                throw new InvalidOperationException($"Failed to load recording from '{fdpPath}'.");

            _replayContext = ctx;
            _replayExtraction = BuildReplayExtractionService(ctx.SandboxRepo);

            return new JsonObject
            {
                ["loaded"]       = true,
                ["fdpPath"]      = fdpPath,
                ["totalFrames"]  = ctx.Playback.TotalFrames,
                ["currentFrame"] = ctx.CurrentFrame,
            };
        }

        /// <summary>POST /replay/seek {frame} — seek to frame in sandbox (main thread).</summary>
        public JsonNode SeekReplay(int frame)
        {
            if (_replayContext is null)
                throw new InvalidOperationException("No replay loaded. Call /replay/load first.");
            _replayContext.SeekToFrame(frame);
            _replayExtraction = BuildReplayExtractionService(_replayContext.SandboxRepo);
            return new JsonObject
            {
                ["frame"]       = _replayContext.CurrentFrame,
                ["totalFrames"] = _replayContext.Playback?.TotalFrames ?? 0,
            };
        }

        /// <summary>POST /replay/step {dir:"forward"|"back"} — step one frame in sandbox (main thread).</summary>
        public JsonNode StepReplay(string dir)
        {
            if (_replayContext is null)
                throw new InvalidOperationException("No replay loaded. Call /replay/load first.");

            bool stepped;
            if (!string.Equals(dir, "back", StringComparison.OrdinalIgnoreCase))
                stepped = _replayContext.StepForward();
            else
                stepped = _replayContext.StepBackward();

            if (stepped)
                _replayExtraction = BuildReplayExtractionService(_replayContext.SandboxRepo);

            return new JsonObject
            {
                ["stepped"]     = stepped,
                ["frame"]       = _replayContext.CurrentFrame,
                ["totalFrames"] = _replayContext.Playback?.TotalFrames ?? 0,
            };
        }

        /// <summary>POST /replay/unload — dispose the replay context (main thread).</summary>
        public JsonNode UnloadReplay()
        {
            _replayContext?.Dispose();
            _replayContext = null;
            _replayExtraction = null;
            return new JsonObject { ["unloaded"] = true };
        }

        /// <summary>
        /// In replay mode, list entities from the SandboxRepo instead of _world.
        /// </summary>
        public JsonNode ListReplayEntities()
        {
            if (_replayContext is null)
                throw new InvalidOperationException("No replay loaded.");

            var arr = new JsonArray();
            if (_replayExtraction != null)
            {
                var dumps = _replayExtraction.ExtractEntities();
                foreach (var d in dumps)
                {
                    var comps = new JsonArray();
                    foreach (var name in d.Components.Keys)
                        comps.Add(name);
                    arr.Add(new JsonObject
                    {
                        ["networkId"]  = d.NetworkId,
                        ["name"]       = ExtractEntityName(d),
                        ["components"] = comps,
                    });
                }
            }
            return arr;
        }

        private static Fdp.Toolkit.Diagnostics.EntityStateExtractionService BuildReplayExtractionService(
            EntityRepository sandboxRepo)
        {
            // Build a NetworkEntityMap populated from the current sandbox state.
            var sandboxMap = new NetworkEntityMap();
            var q = sandboxRepo.Query()
                .With<NetworkIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();
            foreach (var e in q)
            {
                long netId = sandboxRepo.GetComponentRO<NetworkIdentity>(e).Value;
                if (!sandboxMap.TryGetEntity(netId, out _))
                    sandboxMap.Register(netId, e);
            }
            return new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(sandboxRepo, sandboxMap);
        }

        // ── Group K — AI Behavior Traces ──────────────────────────────────────────

        /// <summary>
        /// <c>POST /trace/observe {networkId, on}</c> — arm or disarm trace buffer for an entity.
        /// </summary>
        public JsonNode ObserveTrace(long networkId, bool on)
        {
            if (_editorTracer is null)
                return new JsonObject { ["armed"] = false, ["note"] = "Trace coordinator not available." };

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            if (on)
                _editorTracer.ArmEntity(entity);
            else
                _editorTracer.DisarmEntity(entity);

            return new JsonObject { ["networkId"] = networkId, ["armed"] = on };
        }

        /// <summary>
        /// <c>GET /entities/{networkId}/trace</c> — extract AI behavior trace for an entity.
        /// </summary>
        public JsonNode GetEntityTrace(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            if (!_world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity))
                return new JsonObject { ["networkId"] = networkId, ["tier"] = "none", ["note"] = "Entity has no BehaviorState." };

            byte tier = _world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity).BrainTier;

            if (tier == Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree && _btreeSession != null)
            {
                _btreeSession.Update(_world, entity);
                var snap    = _btreeSession.GetCurrentStateSnapshot();
                var history = _btreeSession.GetRecentNodeHistory(50);

                var histArr = new JsonArray();
                foreach (var h in history)
                {
                    histArr.Add(new JsonObject
                    {
                        ["nodeVisualId"] = h.NodeVisualId.ToString("D"),
                        ["status"]       = h.Status.ToString(),
                        ["timestamp"]    = h.SimulationTime,
                    });
                }

                return new JsonObject
                {
                    ["networkId"]    = networkId,
                    ["tier"]         = "BTree",
                    ["traceArmed"]   = _world.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.BTreeTraceWorkingMemory1024>(entity),
                    ["activeNode"]   = snap?.RunningElementId?.ToString("D"),
                    ["stackPointer"] = snap?.StackPointer ?? 0,
                    ["nodeHistory"]  = histArr,
                };
            }

            if (tier == Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierHsm && _hsmSession != null)
            {
                _hsmSession.Update(_world, entity);
                var snap    = _hsmSession.GetCurrentStateSnapshot();
                var history = _hsmSession.GetRecentTraceHistory(50);

                var histArr = new JsonArray();
                foreach (var h in history)
                {
                    histArr.Add(new JsonObject
                    {
                        ["type"]           = h.GetType().Name,
                        ["simulationTime"] = h.SimulationTime,
                    });
                }

                var leavesArr = new JsonArray();
                if (snap != null)
                    foreach (var leaf in snap.ActiveLeafStableIds)
                        leavesArr.Add(leaf.ToString("D"));

                return new JsonObject
                {
                    ["networkId"]    = networkId,
                    ["tier"]         = "Hsm",
                    ["traceArmed"]   = _world.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.HsmTraceWorkingMemory1024>(entity),
                    ["activeLeafs"]  = leavesArr,
                    ["traceHistory"] = histArr,
                };
            }

            if (_blueprintSession != null)
            {
                return new JsonObject
                {
                    ["networkId"] = networkId,
                    ["tier"]      = "Blueprint",
                    ["note"]      = "Blueprint trace: assetId resolution not available via Debug API.",
                };
            }

            return new JsonObject { ["networkId"] = networkId, ["tier"] = "unknown" };
        }
    }
}
