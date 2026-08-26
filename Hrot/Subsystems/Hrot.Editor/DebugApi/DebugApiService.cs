using System;
using Hrot.Common.Events;
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
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Diagnostics.Breakpoints;
using Hrot.UI.Common.Facades;
using StructEdit.Core;
using StructEdit.Reflection;

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
    public sealed partial class DebugApiService
    {
        // ══ THE DEPENDENCY SPLIT — editor-supplied, or resolved per ACTIVE PERSPECTIVE ═════════════
        //
        // ⭐⭐⭐ 📄 Architect_Question_54 (RESOLVED) · DESIGN_Headless_Testability.md §6a.
        //
        // ⛔⛔ WHY THIS SHAPE. Measured: this service's nine required deps made it UNCONSTRUCTIBLE in
        //    `--mode all` — `IPreviewController`/`IEditorLogic` are editor-only, and `world`/`entityMap`/
        //    `time` are PER-SUBSYSTEM there (each subsystem gets its own repo, map and bus). ⇒ ⭐ the deps
        //    that differ per perspective are resolved through the dispatcher, and the editor-only ones
        //    answer NOT_SUPPORTED_HERE instead of being faked (charter D3/D4).
        //
        // ⭐⭐ The `_x` members below are PROPERTIES, not fields, on purpose: ~108 call sites read them and
        //    the resolution belongs in ONE place, so the sites are unchanged and cannot each invent a
        //    fallback. ⚠ The leading underscore is kept deliberately — renaming 108 reads would have been a
        //    bigger diff than the change itself, and every one of them still means "my dependency".
        private readonly EntityRepository?               _editorWorld;
        private readonly NetworkEntityMap?               _editorEntityMap;
        private readonly IEntityStateExtractionService?  _editorExtraction;
        private readonly ITimeTransportFacade?           _editorTime;
        private readonly IPreviewController?             _editorPreview;
        private readonly IEditorLogic?                   _editorLogic;
        private readonly Action<Fdp.Toolkit.Orchestration.TransitionStateIntent>? _editorRequestTransition;
        private readonly IDiagnosticEventHistoryService?  _editorEventHistory;
        private readonly MasterSyncController?            _timeController;
        private readonly Func<ClusterState>?             _clusterStateGetter;

        /// <summary>⭐ Set only in the CLUSTER shape; null in the editor. See the block above.</summary>
        private readonly Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher? _dispatcher;

        /// <summary>⭐ Built lazily per active perspective in cluster mode; the editor supplies its own.</summary>
        private IEntityStateExtractionService? _clusterExtraction;
        private EntityRepository? _clusterExtractionFor;

        private EntityRepository _world
            => _editorWorld ?? _dispatcher?.World
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.WorldRead);

        private NetworkEntityMap _entityMap
            => _editorEntityMap ?? _dispatcher?.EntityMap
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EntityMap);

        private ITimeTransportFacade _time
            => _editorTime ?? _dispatcher?.Drive
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.TimeDrive);

        private IPreviewController _preview
            => _editorPreview
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.Preview);

        private IEditorLogic _editor
            => _editorLogic
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EditorAuthoring);

        /// <summary>
        /// ⭐⭐ <b>The host-agnostic cluster-transition publisher</b> — the editor's own bus, or the active
        /// perspective's node bus in <c>--mode all</c>. 📄 <c>MCP_Integration.md</c> § Group U.
        /// </summary>
        private Action<Fdp.Toolkit.Orchestration.TransitionStateIntent> _requestTransition
            => _editorRequestTransition
               ?? _dispatcher?.RequestTransition
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.ScenarioLoad);

        private IDiagnosticEventHistoryService _eventHistory
            => _editorEventHistory
               ?? throw NotSupportedHere("events.read");

        /// <summary>
        /// ⭐ The extraction service for the world currently in scope. In cluster mode it is rebuilt when the
        /// active perspective's world changes — ⛔ never cached across perspectives, which would answer for
        /// the wrong node.
        /// </summary>
        private IEntityStateExtractionService _extraction
        {
            get
            {
                if (_editorExtraction is not null) return _editorExtraction;

                var world = _dispatcher?.World
                            ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.WorldRead);
                var map   = _dispatcher?.EntityMap
                            ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EntityMap);

                if (!ReferenceEquals(world, _clusterExtractionFor) || _clusterExtraction is null)
                {
                    _clusterExtraction    = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(world, map);
                    _clusterExtractionFor = world;
                }
                return _clusterExtraction;
            }
        }

        /// <summary>
        /// ⭐⭐ <b>The typed *"this host does not offer that"* signal</b> — 📄 Q54-1 Option C: a command against
        /// an absent capability answers <c>NOT_SUPPORTED_HERE</c>, ⛔ never a bare 404 and ⛔ never a silent
        /// empty model. 📌 <c>D4</c>: absence must be ASSERTABLE, or a broken panel reads as "not ported yet"
        /// forever.
        /// </summary>
        private static Hrot.Presentation.DebugApi.NotSupportedHereException NotSupportedHere(string capability)
            => new Hrot.Presentation.DebugApi.NotSupportedHereException(capability);

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
        //
        // ⭐⭐⭐ MD-001 — a Func, RE-READ per request, ⛔ never a latched list. 📐 Measured: the editor
        //    builds this service in `Initialize`, but its `MessageLogRegistry` is created in
        //    `RegisterWindows` — which runs LATER, and which is also when subsystems register their own
        //    sources. ⇒ a list captured at construction would be the registry as it was before anyone
        //    had registered anything.
        // 📌 The same lesson `SubsystemDebugProvider`'s lazy accessors carry: value-capturing a
        //    composition-root dependency reports an absence the host acquires seconds later.
        private readonly Func<IReadOnlyList<IMessageLogSource>> _logSinks;

        // Group K — AI Behavior Traces
        private readonly EditorAiTracerCoordinator?                    _editorTracer;
        private readonly Hrot.BTree.Editor.Debug.BTreeDebugSession?    _btreeSession;
        private readonly Hrot.Hsm.Editor.Debug.HsmDebugSession?        _hsmSession;
        private readonly Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintSession;

        // MX1 (Group O) — id→(assetId, name) for the blueprints attached to an entity's blackboard.
        // BlueprintTierSummary.Read needs it to turn a slot's int blueprintId into the asset Guid the
        // debug session addresses variables by.
        private readonly Fdp.Toolkit.Blueprints.BlueprintRegistry? _blueprintRegistry;

        // Group L — Attribute patch + StructEdit component edit
        private readonly JsonAttributeCompiler _attributeCompiler;
        private readonly IComponentEditService _componentEditSvc;

        // Group M — Focus / Annotations (ADA-BATCH-14)
        private readonly DebugPrimitiveBuffer? _primitiveBuffer;

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

        /// <summary>
        /// The dump options, but for reading a client's PATCH: identical converters — so a value
        /// the API emitted is accepted verbatim (HN-002) — with enums relaxed to also accept their
        /// integer form, which the strict dump converter refuses.
        /// </summary>
        /// <remarks>
        /// Deliberately asymmetric, and only in the tolerant direction: the API keeps emitting
        /// canonical enum NAMES, and merely stops rejecting an integer a caller sends back.
        /// </remarks>
        internal static readonly JsonSerializerOptions DebugApiPatchOptions =
            BuildDebugApiDumpOptions(strictEnums: false);

        private static JsonSerializerOptions BuildDebugApiDumpOptions(bool strictEnums = true)
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
            opts.Converters.Add(strictEnums
                ? new Fdp.Core.Serialization.Converters.StrictStringEnumConverter()
                : new System.Text.Json.Serialization.JsonStringEnumConverter());

            opts.MakeReadOnly();
            return opts;
        }

        /// <summary>Default upper bound for event-history queries.</summary>
        public const int DefaultMaxEvents = 200;

        // MX4a — behaviour discovery. The registry already holds behaviourId -> ParamsDtoType, so
        // the schema comes from the SAME definition the runtime parses params with; the mission
        // service (optional) gives exact parity with the editor's mission-task combo for an entity.
        private readonly Fdp.Toolkit.Behavior.BehaviorRegistry? _behaviorRegistry;
        private Hrot.UI.Common.Facades.IMissionEditorService? _missionService;

        /// <summary>
        /// The editor's mission service, used by <c>GET /behaviors?entityId=</c> for exact parity
        /// with the mission-task combo.
        ///
        /// <para><b>Settable because of construction ORDER, not optionality.</b> The editor builds
        /// its mission service AFTER this API host, so the constructor cannot receive it; the
        /// composition root hands it over as soon as it exists. ⚠ Leaving it null would be the
        /// silent-default trap — a caller that HAS the dependency must pass it.</para>
        /// </summary>
        public Hrot.UI.Common.Facades.IMissionEditorService? MissionService
        {
            get => _missionService;
            set => _missionService = value;
        }

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
            Func<IReadOnlyList<IMessageLogSource>>? logSinks    = null,
            EditorAiTracerCoordinator?                    editorTracer      = null,
            Hrot.BTree.Editor.Debug.BTreeDebugSession?    btreeSession      = null,
            Hrot.Hsm.Editor.Debug.HsmDebugSession?        hsmSession        = null,
            Hrot.Blueprints.Core.Debug.BlueprintDebugSession? blueprintSession = null,
            JsonAttributeCompiler?                        attributeCompiler = null,
            IComponentEditService?                        componentEditSvc  = null,
            DebugPrimitiveBuffer?                         primitiveBuffer   = null,
            Fdp.Toolkit.Behavior.BehaviorRegistry?        behaviorRegistry  = null,
            Hrot.UI.Common.Facades.IMissionEditorService? missionService    = null,
            Fdp.Toolkit.Blueprints.BlueprintRegistry?     blueprintRegistry = null,
            Action<Fdp.Toolkit.Orchestration.TransitionStateIntent>? requestTransition = null)
        {
            // ⭐ The EDITOR shape still requires all nine — ⛔ this ctor has not become permissive. The
            //   cluster shape is a SEPARATE ctor below, so an editor wiring bug still fails loudly at boot.
            _editorWorld        = world            ?? throw new ArgumentNullException(nameof(world));
            _editorEntityMap    = entityMap        ?? throw new ArgumentNullException(nameof(entityMap));
            _editorExtraction   = extraction       ?? throw new ArgumentNullException(nameof(extraction));
            _editorTime         = time             ?? throw new ArgumentNullException(nameof(time));
            _editorPreview      = preview          ?? throw new ArgumentNullException(nameof(preview));
            _editorLogic        = editor           ?? throw new ArgumentNullException(nameof(editor));
            _editorEventHistory = eventHistory     ?? throw new ArgumentNullException(nameof(eventHistory));
            _timeController     = timeController   ?? throw new ArgumentNullException(nameof(timeController));
            _clusterStateGetter = clusterState     ?? throw new ArgumentNullException(nameof(clusterState));
            // ⭐⭐ HN-029: OPTIONAL, and legitimately so — the editor's `scenario/load/edit` goes through
            //    IEditorLogic (which drives the same intent plus a local wipe), so a host that wires no
            //    publisher still loads in EDIT. ⛔ `load/live` then answers NOT_SUPPORTED_HERE(scenario.load)
            //    rather than pretending. ⚠ The production caller MUST pass it (CLAUDE.md's silent-default rule:
            //    a caller that HAS the dependency must pass it) — EditorSubsystem does.
            _editorRequestTransition = requestTransition;
            _tkbDb             = tkbDb            ?? new TkbDatabase();
            _geoTransform      = geoTransform     ?? new Fdp.Modules.Geographic.Transforms.WGS84Transform();
            _spatialGridCellSize = spatialGridCellSize;
            _spatialGridOriginX  = spatialGridOriginX;
            _spatialGridOriginY  = spatialGridOriginY;
            _spatialGridWidth    = spatialGridWidth;
            _spatialGridHeight   = spatialGridHeight;
            _bpManager         = bpManager;
            _behaviorRegistry  = behaviorRegistry;
            _missionService    = missionService;
            _blueprintRegistry = blueprintRegistry;
            _diffService       = diffService ?? new ComponentDiffService();
            _rrController      = rrController;
            _logSinks          = logSinks ?? (() => Array.Empty<IMessageLogSource>());
            _editorTracer     = editorTracer;
            _btreeSession     = btreeSession;
            _hsmSession       = hsmSession;
            _blueprintSession = blueprintSession;
            _attributeCompiler = attributeCompiler ?? Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build(_geoTransform);
            _componentEditSvc  = componentEditSvc  ?? new ComponentEditServiceBuilder().Build();
            _primitiveBuffer   = primitiveBuffer;
            if (_bpManager != null)
            {
                _bpManager.OnBreakpointHit += (bp, entity) =>
                {
                    _lastHitBreakpointId = bp.Id;
                    _entityMap.TryGetNetworkId(entity, out _lastHitNetworkId);
                };
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE CLUSTER SHAPE — <c>--mode all</c>.</b> Everything that differs per node is resolved
        /// through the <paramref name="dispatcher"/> *(the ACTIVE perspective's own world, map and drive
        /// facade)*; everything editor-only is simply absent and answers <c>NOT_SUPPORTED_HERE</c>.
        /// 📄 <c>Architect_Question_54</c> Q54-2 · charter <c>D3</c> *("the lifted API accepts absent
        /// capabilities")*.
        ///
        /// <para>⛔⛔ <b>This is a SEPARATE constructor, deliberately.</b> ⭐ The editor ctor still demands all
        /// nine deps, so an editor wiring bug still fails loudly at boot — 📌 relaxing THAT would have turned
        /// a composition-root defect into a runtime 501, which is the silent-default shape.</para>
        /// </summary>
        public DebugApiService(
            Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher dispatcher,
            Func<ClusterState>?                           clusterState      = null,
            TkbDatabase?                                  tkbDb             = null,
            IGeographicTransform?                         geoTransform      = null,
            DebugPrimitiveBuffer?                         primitiveBuffer   = null,
            Func<IReadOnlyList<IMessageLogSource>>?       logSinks          = null,
            Fdp.Toolkit.Behavior.BehaviorRegistry?        behaviorRegistry  = null)
        {
            _dispatcher         = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _clusterStateGetter = clusterState;

            _tkbDb        = tkbDb        ?? new TkbDatabase();
            _geoTransform = geoTransform ?? new Fdp.Modules.Geographic.Transforms.WGS84Transform();

            _spatialGridCellSize = 5.0f;
            _spatialGridOriginX  = 0f;
            _spatialGridOriginY  = 0f;
            _spatialGridWidth    = 200;
            _spatialGridHeight   = 200;

            _diffService       = new ComponentDiffService();
            _logSinks          = logSinks ?? (() => Array.Empty<IMessageLogSource>());   // diagnostics MD-001: lazy Func
            _attributeCompiler = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build(_geoTransform);   // AX-017: moved to Fdp.Toolkits
            _componentEditSvc  = new ComponentEditServiceBuilder().Build();
            _primitiveBuffer   = primitiveBuffer;
            _behaviorRegistry  = behaviorRegistry;
        }

        // ── Group A — Status ──────────────────────────────────────────────────

        /// <summary><c>GET /status</c> — full status payload (main thread).</summary>
        public JsonNode GetStatus()
        {
            // ⭐⭐⭐ /status DEGRADES, it does not throw. 📄 Architect_Question_54 Q54-1 + charter D4.
            //
            // ⛔⛔ This endpoint is the harness's READINESS PROBE and the first thing an agent calls, so in
            //    `--mode all` it must answer even though `scenario`/`inPreview` are editor-only and the
            //    active perspective may offer no world. ⇒ ⭐ an absent field is JSON `null` HERE — and
            //    `GET /capabilities` is what says WHY it is absent. ⚠ A null in /status is never the
            //    "silent empty model" Q54 rejects: it is paired with a manifest that declares the absence.
            //
            // ⭐ Every other endpoint still THROWS NotSupportedHere ⇒ 501 with the capability key. Status is
            //   the one deliberate exception, because a probe that 501s tells you nothing about the host.
            return new JsonObject
            {
                ["scenario"]     = TryRead(() => (JsonNode?)_editor.LoadedScenarioName),
                ["clusterState"] = TryRead(() => (JsonNode?)CurrentClusterState().ToString()),
                ["simTime"]      = TryRead(() => (JsonNode?)_time.TotalTime),
                ["timeScale"]    = TryRead(() => (JsonNode?)_time.TimeScale),
                ["isPaused"]     = TryRead(() => (JsonNode?)_time.IsPaused),
                ["inPreview"]    = TryRead(() => (JsonNode?)_preview.IsInPreviewMode),
                ["entityCount"]  = TryRead(() => (JsonNode?)_world.EntityCount),
                ["recording"]    = _isRecording,
                // ⭐ Which context these numbers describe — ⛔ without it, a cluster status is ambiguous
                //   about WHOSE world it counted.
                ["perspective"]  = _dispatcher?.CurrentPerspective,
            };
        }

        /// <summary>
        /// ⭐ Reads one status field, or <see langword="null"/> when this host does not offer it.
        /// ⛔ Catches ONLY <c>NotSupportedHereException</c> — a real fault must still surface as a 500.
        /// </summary>
        private static JsonNode? TryRead(Func<JsonNode?> read)
        {
            try { return read(); }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
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
            foreach (var sink in _logSinks())
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
            // ⭐⭐⭐ DEGRADES, like /status — and this one was found the hard way. 📐 Measured `2026-08-24` in
            //    `--mode all`: `POST /sim/step` answered NOT_SUPPORTED_HERE(preview.control) even though the
            //    step ITSELF was fully supported, because the RESPONSE PAYLOAD read `_preview`.
            // ⛔⛔ A state payload must never make a supported command look unsupported: that is absence
            //    reported about the wrong thing, which is worse than no answer. ⭐ An absent field is null and
            //    GET /capabilities says why.
            ["isPaused"]  = TryRead(() => (JsonNode?)_time.IsPaused),
            ["inPreview"] = TryRead(() => (JsonNode?)_preview.IsInPreviewMode),
            ["totalTime"] = TryRead(() => (JsonNode?)_time.TotalTime),
            ["timeScale"] = TryRead(() => (JsonNode?)_time.TimeScale),

            // ⭐⭐ HN-028: the ack-gate's own state, so a caller can SEE what POST /sim/step waited for.
            //    ⛔ Not decoration: with the gate wired, this is false by the time a step answers; a true here
            //    right after a 200 means the step returned with the tick still un-acknowledged somewhere on the
            //    roster. ⚠ In --mode all it reads the master through the dispatcher; in the editor it reads the
            //    local controller. Never throws (both arms end in `?? false`), so no TryRead.
            ["isAwaitingStepAcks"] = IsAwaitingStepAcks,
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

        /// <summary>
        /// ⭐⭐⭐ <b>THE ACK-GATE'S TRUTH — <c>true</c> while a step has been issued and some roster node has
        /// not yet acknowledged it.</b>
        /// 📄 <c>DESIGN_Headless_Testability.md</c> §6c *(the correctness hazard)* ·
        /// <c>Architect_Question_54</c> Q54-2 *(issue where the user is, confirm where the truth is)*.
        ///
        /// <para>⛔⛔ <b>Why this is a READ and the wait is NOT inside <see cref="Step"/>.</b> 📐 Measured:
        /// <c>MasterSyncController.Update()</c> — the method that drains the ACKs — runs on the MAIN THREAD
        /// from the orchestrator's per-frame update, and <see cref="Step"/> is itself executed on the main
        /// thread through <c>MainThreadJobQueue</c>. ⇒ 🔴 <b>a blocking wait inside <c>Step()</c> would
        /// deadlock the very loop that clears the flag.</b> ⭐ So the gate lives in the HTTP handler, which
        /// polls this across frames — the RETURN CONTRACT the design asks for is preserved *(the request
        /// completes only when the tick is acknowledged cluster-wide)*; only the location differs.</para>
        ///
        /// <para>⭐ <b>Editor mode answers <c>false</c> immediately</b> — the standalone master has an EMPTY
        /// slave roster, so there is nothing to wait for. ⇒ ⭐⭐ the harness code is identical in both modes,
        /// which is the whole point of the conformance seam.</para>
        /// </summary>
        public bool IsAwaitingStepAcks
            => _timeController?.IsAwaitingStepAcks ?? _dispatcher?.IsAwaitingStepAcks ?? false;

        /// <summary>
        /// ⭐⭐ The active perspective's sim clock, or <see langword="null"/> when this host offers no clock
        /// *(IG and ExCon in <c>--mode all</c>: no time facade)*.
        ///
        /// <para>⭐⭐⭐ <b>Why the ack-gate needs it.</b> <see cref="IsAwaitingStepAcks"/> is
        /// <see langword="false"/> for TWO different reasons — *"the barrier has drained"* and *"the barrier has
        /// not begun"*. 📐 Measured <c>2026-08-24</c> in <c>--mode all</c>: a step is published as an INTENT that
        /// travels over DDS, so 2 ms after issuing it the master has not entered <c>Stepping</c> yet and the flag
        /// still reads <see langword="false"/> ⇒ a gate that waits only on this flag returns having confirmed
        /// NOTHING. ⛔ The same level-vs-edge shape as the scenario-load readiness race
        /// *(see <see cref="BeginLoadScenario"/>)*.
        /// ⇒ ⭐ the clock supplies the MONOTONE PROGRESS the flag cannot: a step that has landed has moved it.</para>
        /// </summary>
        public double? TotalTimeOrNull()
        {
            try { return _time.TotalTime; }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
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
        /// <remarks>
        /// ⭐⭐ HN-029: answers in a CLUSTER host too, from whichever node caches the inventory. ⛔ Otherwise
        /// <c>scenario/load/live</c> would work in <c>--mode all</c> while the endpoint that tells you WHAT to
        /// load refused — a surface an agent cannot actually use.
        /// </remarks>
        public JsonNode ListScenarios()
        {
            var names = _editorLogic?.AvailableScenarios
                        ?? _dispatcher?.AvailableScenariosAnyNode
                        ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EditorAuthoring);

            var arr = new JsonArray();
            foreach (var s in names)
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
        /// ⭐⭐⭐ <b><c>POST /scenario/load/edit</c> — load for AUTHORING, cluster-wide.</b>
        /// 📄 <c>MCP_Integration.md</c> § Group U · state machine owned by <c>docs/designs/mgmt-1/DESIGN.md</c>
        /// §12/§5.5.
        ///
        /// <para>🔒 <b>User, `2026-08-24`:</b> <i>"there are 2 load modes — live and edit … both should be
        /// cluster wide. editor is not special, also uses 2pc for its single process."</i></para>
        ///
        /// <para>⭐⭐ <b>Both arms end in the SAME <c>TransitionStateIntent{OperatingEdit}</c> on a
        /// <c>ClusterMaster</c>.</b> ⚠ Where they differ is the DRIVER, and the difference is real, not
        /// editor-favouritism: <see cref="IEditorLogic.LoadScenarioByName"/> first transitions to
        /// <c>Idle</c> and does a LOCAL wipe *(<c>NewScenario()</c> → <c>WorldResetEvent</c> + <c>SoftClear</c>)*
        /// before requesting <c>OperatingEdit</c>. ⛔ On a multi-node cluster that clearing is each node's own
        /// <c>HrotEditLoadHandler</c>'s job, so the extra hop has nothing to do. ⇒ ⭐ when the editor driver is
        /// present it is used *(also keeping `GoldenCaptureFixture` and every existing rail bit-identical)*;
        /// otherwise the intent is published directly.</para>
        ///
        /// <para>⚠⚠ <b>CGF has NO edit-load handler</b> *(`UXI-37` ruling 65 — a CGF-lane follow-up)*, so in
        /// <c>--mode all</c> an edit load is PARTIAL: SimHost loads, CGF does not. ⛔ Declared in the
        /// conformance baseline rather than crashing — which is why the content diff uses LIVE.</para>
        /// </summary>
        public JsonNode LoadScenarioEdit(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));

            if (_editorLogic is not null)
            {
                _editorLogic.LoadScenarioByName(name);
                return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingEdit), ["via"] = "editor-driver" };
            }

            _requestTransition(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingEdit,
                ScenarioId    = name,
                // ⭐ Guid.Empty, mirroring the orchestrator panel's own "Load into Edit" button: authoring is
                //   not an exercise run, so it gets no ExerciseId.
                ExerciseId    = Guid.Empty,
            });
            return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingEdit), ["via"] = "cluster-intent" };
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>POST /scenario/load/live</c> — load for RUNNING, cluster-wide.</b>
        /// 📄 <c>MCP_Integration.md</c> § Group U.
        ///
        /// <para>⭐ Uniform in every host — ⛔ there is no editor-only live-load driver to prefer, so this arm
        /// has no special case at all. 📐 Handlers exist everywhere *(`HrotScenarioLoadHandler` +
        /// `ReferenceLiveLoadHandler` on SimHost · CGF · editor)*, so this is endpoint plumbing and a readiness
        /// contract — **no new handler**.</para>
        ///
        /// <para>⭐ A fresh <c>ExerciseId</c> per load, mirroring the orchestrator panel's "Load into Live"
        /// button: a live load IS a new exercise run, and the id is what recording/replay keys off.</para>
        /// </summary>
        public JsonNode LoadScenarioLive(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));

            _requestTransition(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingLive,
                ScenarioId    = name,
                ExerciseId    = Guid.NewGuid(),
            });
            return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingLive), ["via"] = "cluster-intent" };
        }

        /// <summary>
        /// Reads the orchestration bus for the latest cluster-state and returns true once it is
        /// <see cref="ClusterState.OperatingEdit"/>. <b>Must run on the main thread</b> — it both
        /// drives <c>IEditorLogic.Update()</c> (which consumes the orchestration events and advances
        /// the load state machine) and inspects the resulting state. <c>LoadedScenarioName</c> is
        /// deliberately NOT used as the completion signal (set at frame 0).
        /// </summary>
        public bool PollClusterStateIsOperatingEdit() => CurrentClusterState() == ClusterState.OperatingEdit;

        /// <summary>
        /// ⭐⭐ <b>The same poll, for ANY target state</b> — <c>HN-029</c> needs <c>OperatingLive</c> as well as
        /// <c>OperatingEdit</c>. ⛔ Must run on the main thread, for the reason
        /// <see cref="PollClusterStateIsOperatingEdit"/> gives: it drives the update that consumes the
        /// orchestration events as well as reading their result.
        /// </summary>
        public bool PollClusterStateIs(ClusterState target) => CurrentClusterState() == target;

        /// <summary>
        /// ⭐ <see cref="WorldEntityCount"/>, degrading to <see langword="null"/> on a host with no world
        /// *(ExCon)*. ⚠ The load-edge check needs a count; without one it can only watch the STATE, and the
        /// caller must know which of the two it got rather than reading `0` as "empty world".
        /// </summary>
        public int? WorldEntityCountOrNull()
        {
            try { return _world.EntityCount; }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The world's entity count — the LOAD EDGE the readiness check needs.</b>
        /// 📄 <c>DESIGN_Headless_Testability.md</c> §6b *(the stepping law)*; the defect it fixes is recorded
        /// in the design's as-built section.
        ///
        /// <para>🔴🔴 <b>Why <see cref="PollClusterStateIsOperatingEdit"/> is not sufficient on a RELOAD, and
        /// this is measured, not theoretical.</b> 📐 `2026-08-24`: <c>OperatingEdit</c> is a LEVEL, and a
        /// reload starts from it ⇒ the very first poll can answer <i>"ready"</i> while the previous world is
        /// still standing and the new one has not been built. ⚠⚠ <b>The two <c>DeterminismRails</c> reload
        /// cases were passing on a ONE-FRAME margin</b>: adding a single extra main-thread job to
        /// <c>POST /sim/step</c> *(the ack-gate)* was enough to make the subsequent read observe an EMPTY
        /// world. ⇒ ⛔ they were measuring a race, not a property.</para>
        ///
        /// <para>⭐⭐ So the host waits for an <b>EDGE</b>: the count must be seen to CHANGE from what it was
        /// when the load was requested *(a load does <c>SoftClear</c> then re-creates, so 8 → 0 → 8 is
        /// observable when polling every frame)*, and then settle. ⛔ Not <c>ListEntities()</c> — that
        /// extracts every component of every entity, per poll, per frame.</para>
        /// </summary>
        public int WorldEntityCount => _world.EntityCount;

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
            var arr = new JsonArray();

            // Unmanaged events (registered via [EventId] attribute on value types).
            foreach (var t in EventType.GetAllRegistered())
            {
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]    = t.Name,
                    ["managed"] = false,
                    ["fields"]  = fa,
                });
            }

            // Managed events (registered lazily via RegisterManaged<T>/PublishManaged<T>).
            // Completeness caveat: only types that have been registered or published at least
            // once appear here. Types never used will be absent.
            var managedTypes = _world.Bus.GetRegisteredManagedEventTypes();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in arr)
                seenNames.Add(entry?["name"]?.GetValue<string>() ?? "");

            foreach (var t in managedTypes)
            {
                if (!seenNames.Add(t.Name)) continue; // skip if already listed (defensive)
                var fields = JsonShapeDescriber.Describe(t);
                var fa = new JsonArray();
                foreach (var f in fields)
                    fa.Add(new JsonObject { ["name"] = f.Name, ["type"] = f.Type });
                arr.Add(new JsonObject
                {
                    ["name"]    = t.Name,
                    ["managed"] = true,
                    ["fields"]  = fa,
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
            // Resolve event type by name across all registered types (unmanaged first, then managed).
            var registeredTypes = EventType.GetAllRegistered();
            Type? clrType = registeredTypes.FirstOrDefault(t =>
                string.Equals(t.Name, eventTypeName, StringComparison.OrdinalIgnoreCase));

            if (clrType is null)
            {
                // Also search managed event types registered on the bus.
                var managedTypes = _world.Bus.GetRegisteredManagedEventTypes();
                clrType = managedTypes.FirstOrDefault(t =>
                    string.Equals(t.Name, eventTypeName, StringComparison.OrdinalIgnoreCase));
            }

            if (clrType is null)
                return (null, $"Unknown eventType: '{eventTypeName}'. List publishable events with GET /commands.");

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
                    ["reason"]  = "sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance.",
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
                ["reason"]   = timeAdvancing ? null : (JsonNode?)"sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance.",
            };
        }

        // ── Group M — Focus + Annotations (ADA-BATCH-14) ────────────────────────

        /// <summary>
        /// <c>POST /entities/{networkId}/focus</c> — publish a <see cref="CenterOnEntityCommand"/>
        /// so the map canvas pans and zooms to the specified entity.
        ///
        /// <para><b>Headless-verifiable:</b> the publish is confirmed by checking event history.
        /// The actual camera move only happens in a windowed session (MANUAL-VERIFY).</para>
        ///
        /// <para>Must run on the main thread.</para>
        /// </summary>
        public JsonNode FocusEntity(long networkId)
        {
            var cmd = new CenterOnEntityCommand { NetworkId = networkId };
            _world.Bus.Publish(cmd);
            return new JsonObject { ["focused"] = true };
        }

        /// <summary>
        /// <c>POST /annotations {type, ...}</c> — write a debug primitive into the gizmo
        /// <see cref="DebugPrimitiveBuffer"/> so it is rendered on the next frame.
        ///
        /// <para>Supported types:
        /// <list type="bullet">
        ///   <item><c>"sphere"</c> — requires <c>x, y, z, radius</c> (float); optional <c>color</c> (hex string like "#FF0000").</item>
        ///   <item><c>"anchor"</c> — requires <c>networkId, x, y, z</c>; optional <c>heading</c>.</item>
        ///   <item><c>"line"</c> — requires nested <c>from:{x,y,z}</c> and <c>to:{x,y,z}</c>; optional <c>color</c>.</item>
        /// </list>
        /// </para>
        ///
        /// <para><b>Headless-verifiable:</b> the buffer write is confirmed by checking
        /// <c>DebugPrimitiveBuffer.Count</c>. The gizmo render only happens in a windowed session
        /// (MANUAL-VERIFY).</para>
        /// </summary>
        /// <returns>Tuple of (JsonNode result, string? error). Error is set when the buffer is
        /// unavailable or the request is malformed.</returns>
        public (JsonNode? result, string? error) AddAnnotation(JsonNode? body)
        {
            if (_primitiveBuffer is null)
                return (null, "DebugPrimitiveBuffer not available (service wired without it).");

            if (body is null)
                return (null, "Request body is required.");

            var type = body["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type))
                return (null, "'type' field is required (sphere | anchor | line).");

            int countBefore = _primitiveBuffer.Count;

            try
            {
                switch (type!.ToLowerInvariant())
                {
                    case "sphere":
                    {
                        float x      = body["x"]?.GetValue<float>()      ?? 0f;
                        float y      = body["y"]?.GetValue<float>()      ?? 0f;
                        float z      = body["z"]?.GetValue<float>()      ?? 0f;
                        float radius = body["radius"]?.GetValue<float>() ?? 5f;
                        var   color  = ParseColor(body["color"]?.GetValue<string>(), new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 255, 0, 200));
                        _primitiveBuffer.DrawSphere(new System.Numerics.Vector3(x, y, z), radius, color);
                        break;
                    }
                    case "anchor":
                    {
                        long  netId   = body["networkId"]?.GetValue<long>()    ?? 0L;
                        float x       = body["x"]?.GetValue<float>()           ?? 0f;
                        float y       = body["y"]?.GetValue<float>()           ?? 0f;
                        float z       = body["z"]?.GetValue<float>()           ?? 0f;
                        float heading = body["heading"]?.GetValue<float>()     ?? 0f;
                        _primitiveBuffer.DrawSpatialAnchor(netId, x, y, z, heading);
                        break;
                    }
                    case "line":
                    {
                        var from = body["from"];
                        var to   = body["to"];
                        if (from is null || to is null)
                            return (null, "'from' and 'to' are required for type 'line'.");
                        float fx = from["x"]?.GetValue<float>() ?? 0f;
                        float fy = from["y"]?.GetValue<float>() ?? 0f;
                        float fz = from["z"]?.GetValue<float>() ?? 0f;
                        float tx = to["x"]?.GetValue<float>() ?? 0f;
                        float ty = to["y"]?.GetValue<float>() ?? 0f;
                        float tz = to["z"]?.GetValue<float>() ?? 0f;
                        var color = ParseColor(body["color"]?.GetValue<string>(), new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 255, 255, 200));
                        _primitiveBuffer.DrawLine(
                            new System.Numerics.Vector3(fx, fy, fz),
                            new System.Numerics.Vector3(tx, ty, tz),
                            color);
                        break;
                    }
                    default:
                        return (null, $"Unknown annotation type '{type}'. Supported: sphere, anchor, line.");
                }
            }
            catch (Exception ex)
            {
                return (null, $"Failed to write annotation: {ex.Message}");
            }

            int countAfter = _primitiveBuffer.Count;
            return (new JsonObject
            {
                ["added"]          = true,
                ["primitiveIndex"] = countBefore,
                ["bufferCount"]    = countAfter,
            }, null);
        }

        private static Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 ParseColor(string? hex,
            Fdp.Toolkit.Diagnostics.Gizmos.Rgba32 fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            hex = hex!.TrimStart('#');
            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(r, g, b, 255);
            }
            return fallback;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ The cluster's state — the editor's own getter, else whichever cluster node caches it.
        /// <para>📐 Measured `2026-08-24`: without the dispatcher arm, <c>POST /scenario/load/live</c> in
        /// <c>--mode all</c> published its intent, the master accepted it and fanned out to 5 nodes, and the
        /// endpoint then answered <c>NOT_SUPPORTED_HERE(cluster.state)</c> — ⛔ the load WORKED and the reply
        /// said it was unsupported, because only the READINESS read was missing. ⚠ Exactly deviation ⑤'s shape
        /// *(a response payload making a supported command look unsupported)*, one layer up.</para>
        /// </summary>
        private ClusterState CurrentClusterState()
            => _clusterStateGetter?.Invoke()
               ?? _dispatcher?.ClusterStateAnyNode
               ?? throw NotSupportedHere("cluster.state");

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

        // ── Group P.0 / S — discovery with schema (MX4a, MX7) ─────────────────

        /// <summary>
        /// <c>GET /behaviors?tkbType=</c> (or <c>?entityId=</c>) — the behaviours an entity of that
        /// type may run, each with the JSON schema of its parameter DTO (<c>MX4a</c>).
        ///
        /// <para><b>Reuse, not a new registry.</b> <see cref="BehaviorDefinition.ParamsDtoType"/>
        /// already holds behaviourId → param DTO — the very type the runtime parses params with — so
        /// the schema an agent authors against and the bytes the engine reads come from ONE
        /// declaration. ⛔ Nothing here maintains a second list.</para>
        ///
        /// <para><b>Two keys, because two questions.</b> <c>tkbType</c> answers "what can a vehicle of
        /// this type do" from <c>BehaviorCatalog</c> — the same catalog the mission panel filters by.
        /// <c>entityId</c> answers "what can THIS entity do" by delegating to the mission service, so
        /// it matches the editor's mission-task combo exactly, including its editor-authored BTree
        /// entries. ⚠ Without a mission service wired, the entityId form resolves the entity's own
        /// TkbType and falls back to the catalog — same answer minus those BTree extras.</para>
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) GetBehaviors(long? tkbType, long? entityId)
        {
            if (_behaviorRegistry is null)
                return (null, "Behavior registry not available.", null);

            IReadOnlyList<string> names;

            if (entityId is not null)
            {
                // Resolve the entity FIRST, whichever path serves the list. The mission service
                // answers an unknown id with an EMPTY list — correct for a UI combo, but over HTTP it
                // is indistinguishable from "this entity can do nothing", and an agent would take the
                // wrong lesson from it. A missing entity is a mistake about the ID, so it is reported
                // as one, with the hint that names GET /entities.
                if (!_entityMap.TryGetEntity(entityId.Value, out var entity))
                    return (null, $"Entity {entityId.Value} not found. List entities with GET /entities.", DebugApiHints.Entity);

                if (_missionService is not null)
                {
                    names = _missionService.GetAvailableBehaviors(entityId.Value);
                }
                else
                {
                    if (!_world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity))
                        return (null, $"Entity {entityId.Value} has no TkbIdentity, so it has no behaviour catalog.", DebugApiHints.TkbType);
                    names = Hrot.Map.Definitions.Tkb.BehaviorCatalog.GetValidBehaviors(
                        _world.GetComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity).TkbType);
                }
            }
            else if (tkbType is not null)
            {
                names = Hrot.Map.Definitions.Tkb.BehaviorCatalog.GetValidBehaviors(tkbType.Value);
            }
            else
            {
                // No key at all: every REGISTERED behaviour, so an agent can still discover the
                // vocabulary before it has an entity to ask about.
                names = _behaviorRegistry.GetRegisteredNames();
            }

            var arr = new JsonArray();
            foreach (var name in names)
            {
                // Only behaviours actually registered in the live registry are offered — a catalog
                // name with no definition cannot be run, so advertising it would be a lie.
                if (!_behaviorRegistry.TryGetId(name, out int id)) continue;
                if (!_behaviorRegistry.TryGetDefinition(id, out var definition) || definition is null) continue;

                arr.Add(new JsonObject
                {
                    ["id"]          = name,
                    ["name"]        = definition.Name,
                    ["brainTier"]   = definition.BrainTier,
                    ["paramSchema"] = DtoJsonSchemaExtractor.ExtractParams(definition.ParamsDtoType),
                });
            }

            return (arr, null, null);
        }

        /// <summary>
        /// <c>GET /breakpoint-types</c> — every condition arm a breakpoint may use, with its param
        /// schema (<c>MX7</c>).
        ///
        /// <para>Set/list/remove already existed (Group G); the gap was that an agent had to author a
        /// <c>SearchPredicateDto</c> <b>blind</b>. The arms are read from the union's own
        /// <c>[JsonDerivedType]</c> attributes — the same declarations the deserializer binds — so
        /// this list cannot drift from what <c>POST /breakpoints</c> accepts.</para>
        /// </summary>
        public JsonNode GetBreakpointTypes() => DtoJsonSchemaExtractor.ExtractPredicateUnion();

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
                    throw new ArgumentException($"filterNetworkId {filterNetworkId} not found. List entities with GET /entities.");
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
            throw new ArgumentException($"Breakpoint '{idStr}' not found. List with GET /breakpoints.");
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
                throw new ArgumentException($"Unknown baselineId: '{baselineId}'. Capture one with POST /diff/capture.");

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
                    Fdp.Toolkit.Orchestration.OrchestrationConstants.ResolveStagingRoot(),
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
                Fdp.Toolkit.Orchestration.OrchestrationConstants.ResolveStagingRoot())
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

        // ── Group L — Live Mutation / Fault Injection ─────────────────────────────

        /// <summary>GET /attributes/schema — registered patchable paths + JSON Schema.</summary>
        public JsonNode GetAttributesSchema()
        {
            var paths = new JsonArray();
            foreach (var p in _attributeCompiler.RegisteredPaths)
                paths.Add(p);
            return new JsonObject
            {
                ["registeredPaths"] = paths,
                ["schema"]          = JsonNode.Parse(_attributeCompiler.ExportSchema()),
            };
        }

        /// <summary>
        /// POST /entities/{networkId}/attribute {patchJson} — compile JSON attribute patch
        /// onto the entity via <see cref="JsonAttributeCompiler"/>.
        /// Authority-aware; unregistered keys silently ignored.
        /// Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error) PatchEntityAttribute(long networkId, string? patchJson)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.");

            if (string.IsNullOrWhiteSpace(patchJson))
                return (null, "patchJson is required.");

            try
            {
                var ctx = _attributeCompiler.CreatePatchContext(_world, entity);
                _attributeCompiler.Compile(patchJson, ctx);
                ctx.FlushDirtyMarks();
            }
            catch (Exception ex)
            {
                return (null, $"Attribute patch failed: {ex.Message}");
            }

            // Return the updated entity dump.
            var node = DumpEntity(networkId);
            return (node, null);
        }

        /// <summary>
        /// POST /entities/{networkId}/component {componentType, patch} — StructEdit escape hatch.
        /// Opens a StructEdit session for the named component, applies the JSON patch fields,
        /// validates via IComponentValidator, and writes the result back to ECS.
        /// Returns 400 if validation fails; the component is unchanged.
        /// Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error) EditEntityComponent(long networkId, string componentType, JsonNode? patch)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.");

            if (string.IsNullOrWhiteSpace(componentType))
                return (null, "componentType is required.");

            if (patch is null)
                return (null, "patch is required.");

            // Resolve the CLR type by name.
            var allTypes = ComponentTypeRegistry.GetAllTypes();
            var clrType  = allTypes.FirstOrDefault(t =>
                string.Equals(t.Name, componentType, StringComparison.OrdinalIgnoreCase));
            if (clrType is null)
                return (null, $"Unknown component type: '{componentType}'. List registered components with GET /components.");

            // Get the boxed component from ECS via reflection.
            object? boxedComponent;
            try
            {
                boxedComponent = GetBoxedComponent(_world, entity, clrType);
            }
            catch (Exception ex)
            {
                return (null, $"Could not read component '{componentType}': {ex.Message}");
            }

            if (boxedComponent is null)
                return (null, $"Entity {networkId} does not have component '{componentType}'.");

            // Open a StructEdit session.
            using var session = _componentEditSvc.Open(boxedComponent, clrType);

            // Apply patch fields to the EditDocument tree.
            // ApplyJsonPatchToDocument throws ArgumentException on type-mismatch/parse failure.
            try
            {
                ApplyJsonPatchToDocument(session.Document.Root, patch);
            }
            catch (ArgumentException ex)
            {
                return (null, $"Invalid patch value: {ex.Message}");
            }

            // Commit (runs IComponentValidator).
            object committed;
            try
            {
                committed = session.Commit();
            }
            catch (EditValidationException ex)
            {
                return (null, $"Validation failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (null, $"Commit failed: {ex.Message}");
            }

            // Write the committed value back to ECS via reflection.
            try
            {
                SetBoxedComponent(_world, entity, clrType, committed);
            }
            catch (Exception ex)
            {
                return (null, $"Could not write component '{componentType}' back to ECS: {ex.Message}");
            }

            // Return the updated entity dump.
            var node = DumpEntity(networkId);
            return (node, null);
        }

        // ── Group L helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Gets a boxed copy of an ECS component by CLR type via reflection.
        /// Works for both managed class components and unmanaged struct components.
        /// </summary>
        private static object? GetBoxedComponent(EntityRepository repo, Entity entity, Type clrType)
        {
            int typeId = ComponentTypeRegistry.GetId(clrType);
            if (typeId < 0)
                throw new InvalidOperationException($"Type '{clrType.Name}' is not registered in ComponentTypeRegistry.");

            if (!repo.HasComponentByTypeId(entity, typeId))
                return null;

            // For managed class components: GetManagedComponentByTypeId returns the object directly.
            if (clrType.IsClass)
                return repo.GetManagedComponentByTypeId(entity, typeId);

            // For unmanaged struct components: serialize round-trip to box the value.
            return GetBoxedUnmanagedViaSerialize(repo, entity, clrType);
        }

        /// <summary>
        /// Gets a boxed unmanaged component by calling a generic helper via reflection.
        /// </summary>
        private static object GetBoxedUnmanagedViaSerialize(EntityRepository repo, Entity entity, Type clrType)
        {
            var method = typeof(DebugApiService)
                .GetMethod(nameof(GetBoxedUnmanagedGeneric),
                    BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(clrType);
            return method.Invoke(null, new object[] { repo, entity })!;
        }

        private static object GetBoxedUnmanagedGeneric<T>(EntityRepository repo, Entity entity) where T : struct
        {
            // GetComponentRO<T> returns ref readonly T — copy to a value then box.
            T value = repo.GetComponentRO<T>(entity);
            return value;
        }

        /// <summary>
        /// Writes a boxed component value back to ECS via reflection.
        /// Handles both managed class and unmanaged struct components.
        /// </summary>
        private static void SetBoxedComponent(EntityRepository repo, Entity entity, Type clrType, object value)
        {
            if (clrType.IsClass)
            {
                // Managed class components: call AddComponent<T> generically.
                var method = typeof(EntityRepository)
                    .GetMethod(nameof(EntityRepository.AddComponent),
                        BindingFlags.Public | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(repo, new object[] { entity, value });
            }
            else
            {
                // Unmanaged struct components: use typed helper.
                var method = typeof(DebugApiService)
                    .GetMethod(nameof(SetBoxedComponentGeneric),
                        BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(clrType);
                method.Invoke(null, new object[] { repo, entity, value });
            }
        }

        private static void SetBoxedComponentGeneric<T>(EntityRepository repo, Entity entity, object value) where T : struct
        {
            repo.SetComponent(entity, (T)value);
        }

        /// <summary>
        /// Walks the StructEdit <see cref="EditNode"/> tree and applies JSON patch values
        /// to leaf nodes whose <see cref="EditNode.JsonPath"/> matches keys in <paramref name="patch"/>.
        /// Non-matching keys are silently ignored (safe; no error).
        /// Throws <see cref="ArgumentException"/> if a matched field value cannot be parsed/deserialized
        /// (surfaced as 400 by the caller).
        /// </summary>
        private static void ApplyJsonPatchToDocument(EditNode root, JsonNode patch)
        {
            // Build a flat map: bare path → EditNode for all leaf nodes with bindings.
            // "$.Current" → key "Current"; "$.Position.X" → key "Position.X"
            var leafMap = new Dictionary<string, EditNode>(StringComparer.OrdinalIgnoreCase);
            CollectLeafNodes(root, leafMap);

            // Walk the JSON patch object and apply matching values.
            if (patch is JsonObject patchObj)
            {
                foreach (var (key, valueNode) in patchObj)
                {
                    if (valueNode is null) continue;
                    ApplyJsonValue(key, valueNode, leafMap, string.Empty);
                }
            }
        }

        private static void CollectLeafNodes(EditNode node, Dictionary<string, EditNode> map)
        {
            if (node.Binding != null && !node.IsReadOnly)
            {
                // JsonPath is rooted at "$" (e.g. "$.Current") — strip the leading "$." so that
                // callers can match using bare field names like "Current" or nested "Position.X".
                var key = node.JsonPath.StartsWith("$.") ? node.JsonPath.Substring(2) : node.JsonPath;
                map[key] = node;
            }
            foreach (var child in node.Children)
                CollectLeafNodes(child, map);
        }

        private static void ApplyJsonValue(
            string key,
            JsonNode valueNode,
            Dictionary<string, EditNode> leafMap,
            string parentPath)
        {
            string fullPath = string.IsNullOrEmpty(parentPath) ? key : $"{parentPath}.{key}";

            // Try exact path match first.
            if (leafMap.TryGetValue(fullPath, out var node))
            {
                var targetType = node.Binding!.ValueType;
                object? deserialized;
                try
                {
                    // HN-002 — parse with the SAME options the dump was written with. The patch
                    // parser used to build its own bare options, so a Vector3 could only be written
                    // in the {X,Y,Z} shape while GET /entities emitted [x,y,z]: read-modify-write
                    // could not round-trip. DebugApiDumpOptions carries the vector converters (which
                    // now accept both shapes) and the NaN sentinels, so what the API hands out is
                    // exactly what it takes back.
                    deserialized = JsonSerializer.Deserialize(
                        valueNode.ToJsonString(), targetType, DebugApiPatchOptions);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"Cannot parse value for field '{fullPath}' (expected {targetType.Name}): {ex.Message}");
                }
                try
                {
                    node.Binding!.SetBoxed(deserialized);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(
                        $"Cannot set field '{fullPath}': {ex.Message}");
                }
                // Matched a leaf — don't recurse into its value as nested object keys
                return;
            }

            // If it's an object, recurse for nested keys (handles nested struct paths).
            if (valueNode is JsonObject nestedObj)
            {
                foreach (var (childKey, childValue) in nestedObj)
                {
                    if (childValue is null) continue;
                    ApplyJsonValue(childKey, childValue, leafMap, fullPath);
                }
            }
        }
    }
}
