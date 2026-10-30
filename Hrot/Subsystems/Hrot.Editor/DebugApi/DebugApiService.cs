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
using Hrot.UI.Common.RunMode;
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
        private readonly Hrot.Presentation.DebugApi.IClusterMasterControl? _editorMaster;   // ⭐ R-3052…R-3055 — the editor's own core, on its bus
        private readonly Func<IReadOnlyList<Fdp.Toolkit.Orchestration.ExerciseInventoryItem>?>? _editorExercises;
        private readonly Func<float?>? _editorReplayDuration;
        private readonly Func<Fdp.Toolkit.Orchestration.ReplayAxis?>? _editorReplayAxis;   // CE-3329
        private readonly Func<Fdp.Toolkit.Orchestration.ClusterArmedMode?>? _editorArmedMode;   // CE-3341 — the cluster's armed mode, from the in-process master's broadcast
        private readonly Func<bool?>? _editorRecordLiveRunsOff;   // CE-4090 — the setting, from the in-process master's broadcast
        private readonly Func<bool?>? _editorAskToKeepRecordings;   // CE-4100 — the same, for the Ask-to-keep setting
        private readonly Func<Fdp.Toolkit.Orchestration.ClusterView?>? _editorClusterView;   // CE-3344 — the in-process node's ONE cluster read model (the 409 gate)
        private readonly Func<bool?>? _editorRunNotRecorded;   // CE-4090 — the in-process master's "this run was not recorded"
        private readonly Func<Guid, Fdp.Toolkit.Orchestration.UnsavedCheckOutcome?>? _editorUnsavedCheck;   // CE-4093 — the in-process master's check round answer
        private readonly Func<bool?>? _editorScenarioDirty;   // CE-4093 — the in-process master's last verdict
        private readonly IDiagnosticEventHistoryService?  _editorEventHistory;
        private readonly MasterSyncController?            _timeController;
        private readonly Func<ClusterState>?             _clusterStateGetter;
        private readonly Func<EntityRepository, Hrot.Common.EntityCreation.EntityCreation?>? _entityCreationGetter;   // CE-515 (was CE-271 seam ⑤)

        /// <summary>⭐ Set only in the CLUSTER shape; null in the editor. See the block above.</summary>
        private readonly Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher? _dispatcher;

        /// <summary>⭐ Built lazily per active perspective in cluster mode; the editor supplies its own.</summary>
        private IEntityStateExtractionService? _clusterExtraction;
        private EntityRepository? _clusterExtractionFor;

        private EntityRepository _world
            => _editorWorld ?? _dispatcher?.World
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.WorldRead);

        private Hrot.UI.Common.Map3D.Director.WorldUnits? _filmUnits;

        /// <summary>
        /// ⭐ CE-4038 D-9 (docs/DESIGN_Film_Director.md) — <c>POST /film/shot {spec, t}</c>: a take's shot (its own keys: follow · over + toward ·
        /// lookAt · eye + target, orbit, to, fov) <paramref name="t"/> sim seconds into it, by the ONE copy of the shot math — so film.py
        /// keeps none. Units by name, in this host's world (the active perspective's on a cluster). Main thread.
        /// </summary>
        public Hrot.UI.Common.Map3D.FilmShot? EvaluateShot(Hrot.UI.Common.Map3D.Director.ShotSpec spec, double t)
            => Hrot.UI.Common.Map3D.Director.ShotEvaluator.Evaluate(spec, t, _filmUnits ??= new Hrot.UI.Common.Map3D.Director.WorldUnits(() => _world));

        private NetworkEntityMap _entityMap
            => _editorEntityMap ?? _dispatcher?.EntityMap
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EntityMap);

        private ITimeTransportFacade _time
            => _editorTime ?? _dispatcher?.Drive
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.TimeDrive);

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-110</c> — THIS HOST'S TKB CATALOG, resolved exactly like <see cref="_world"/> and
        /// <see cref="_time"/>: the editor's own, else the ACTIVE PERSPECTIVE's.</b>
        /// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.10.
        ///
        /// <para>🔴🔴 <b>This used to be <c>_tkbDb = tkbDb ?? new TkbDatabase()</c> — a private EMPTY
        /// catalog.</b> 📐 Measured `2026-08-28`: <c>ClusterRunner/Program.cs:429</c> passes no
        /// <c>tkbDb:</c>, so on <c>--mode all</c> <c>GET /tkb/types</c> answered <c>[]</c> and
        /// <c>/tkb/types/303</c> answered <i>"not found"</i> — ⛔ while every node's
        /// <c>HrotNodeBuilder:197</c> had built a real catalog from <c>HrotEnvironment.CreateTkb()</c>.</para>
        ///
        /// <para>⛔⛔ <b>THROWS rather than returning an empty catalog, and that is the whole point.</b>
        /// ⚠⚠ The old default is the *"lying in the safe-looking direction"* shape this file's own
        /// <c>BP-487</c> remarks call worse than lying loudly: <b>an empty list is a valid-looking
        /// answer</b>, so nothing downstream can tell *"this node knows no templates"* from *"nobody wired
        /// the catalog."* 📌 It cost a wrong root-cause hypothesis for <c>CE-103</c> — the empty result was
        /// read as proof that the cluster's TKB genuinely differed from the editor's. ⭐ Ruling 49:
        /// absent-and-explained beats present-and-broken.</para>
        /// </summary>
        private Fdp.Interfaces.ITkbDatabase _tkbDb
            => _editorTkbDb ?? _dispatcher?.TkbDb
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.TkbRead);

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-236</c> — THIS NODE'S GEOGRAPHIC TRANSFORM, and it is the SIMULATION'S OWN.</b>
        /// Resolved exactly like <see cref="_tkbDb"/>: the injected instance, else the world singleton
        /// this node's simulation actually converts with.
        ///
        /// <para>
        /// 🔒 <b>User ruling, <c>2026-09-08</c>:</b> <i>"we should always have that world singleton
        /// available. Not having it is a bug. And it must be shared … among ai debug mcp api, the
        /// scenario loader &amp; json parameter interpreter etc. All implementations must point to the
        /// same source, one single GeographicTransform implementation"</i>, and every consumer must read
        /// it <i>"VIA THE INTERFACE (never directly looking up the singleton)"</i> — hence
        /// <see cref="IGeographicTransform"/> here, resolved once in one property rather than
        /// <c>GetSingletonManaged</c> scattered through the routes.
        /// </para>
        ///
        /// <para>
        /// 🔴🔴 <b>This used to be <c>_geoTransform = geoTransform ?? new WGS84Transform()</c>.</b>
        /// ⛔ <c>WGS84Transform</c> has NO default origin — <c>_originLat/_originLon/_originAlt</c> are
        /// plain fields defaulting to <c>0</c> and only <c>SetOrigin</c> fills them, while every
        /// simulation node runs on the Berlin origin that <c>HrotEnvironment.CreateGeoTransform()</c>
        /// sets. 📐 Measured <c>2026-09-08</c>: <c>ClusterRunner/Program.cs:447</c> passed no
        /// <c>geoTransform:</c>, so on <c>--mode all</c> <c>GET /world/info</c> and
        /// <c>POST /world/geo-to-local</c> answered against <b>0°N 0°E</b> — the Gulf of Guinea — while
        /// the simulation converted against Berlin. ⚠ Not a small error: a different continent, reported
        /// as success.
        /// </para>
        ///
        /// <para>
        /// ⛔ <b>THROWS rather than substituting an origin</b>, the same reasoning as <c>_tkbDb</c> and
        /// ruling 49 (<i>absent-and-explained beats present-and-broken</i>): a coordinate computed
        /// against the wrong origin is a valid-looking answer, so no caller can tell it from a right one.
        /// </para>
        /// </summary>
        private IGeographicTransform GeoTransform
            => _geoTransform
               ?? WorldGeoTransformOrNull()
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.WorldRead);

        /// <summary>
        /// The world singleton, when this node has a world and it publishes one. Null rather than
        /// throwing so <see cref="GeoTransform"/> owns the single refusal.
        /// </summary>
        private IGeographicTransform? WorldGeoTransformOrNull()
        {
            EntityRepository? world = _editorWorld ?? _dispatcher?.World;
            return world is not null && world.HasSingletonManaged<IGeographicTransform>()
                ? world.GetSingletonManaged<IGeographicTransform>()
                : null;
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>BP-487</c> — THIS HOST'S MAP FEED, resolved the same way as
        /// <see cref="_world"/>/<see cref="_time"/>: the editor's own buffer, else the ACTIVE PERSPECTIVE's.
        /// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.6 ·
        /// <c>DESIGN_UI_Observability_Snapshot.md</c> STATUS ③ *(the filed finding)*.
        ///
        /// <para>🔴 <b>Before this, <c>--mode all</c> had NO gizmo feed at all.</b> 📐 Measured `2026-08-27`:
        /// <c>_primitiveBuffer</c> is passed by <c>EditorSubsystem.cs:1901</c> and by nobody else, so
        /// <c>ClusterRunner/Program.cs:429</c> built the cluster service without one and
        /// <c>GET /panels/_gizmo</c> answered 404 — while <b>CGF, IG and SimHost each drive a buffer of their
        /// own</b>. ⇒ the textbook silent default: <i>a production caller that HAS a dependency must PASS
        /// it</i>.</para>
        ///
        /// <para>⛔ <b>NULLABLE on purpose, not throwing like <see cref="_world"/>.</b> ⭐ Its two callers
        /// *(<c>GetGizmoFrame</c>, <c>AddAnnotation</c>)* already answer with a written explanation of WHY
        /// there is no feed, which is the better message — ⚠ turning that into
        /// <c>NOT_SUPPORTED_HERE</c> would lose it.</para>
        /// </summary>
        private DebugPrimitiveBuffer? _gizmoFeed => _primitiveBuffer ?? _dispatcher?.GizmoBuffer;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-259am</c> — SAY WHICH OF THE TWO ABSENCES IT IS, because they have different
        /// fixes and the old text asserted the wrong one.</b>
        /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.8c.
        ///
        /// <para>🔴 <b>The miss this repairs.</b> Both callers said <i>"this host has no debug primitive
        /// buffer for the active perspective"</i> — ⛔ a claim about the HOST, and on
        /// <c>--mode replaybrowser</c> it was simply false: that host holds a buffer and fills it every
        /// frame. 📐 The real cause was that the subsystem contributed NO PROVIDER at all, so the
        /// dispatcher had an empty list and could not route anywhere. ⚠ The message sent a session hunting
        /// a missing buffer that existed — ⭐ the same *"absent vs unwired"* confusion <c>CE-110</c> and
        /// <c>CE-193</c> are both about, one level further out.</para>
        ///
        /// <para>⭐ Three distinguishable states, each with the action it implies:
        /// <list type="number">
        ///   <item><b>no provider claims the active perspective</b> ⇒ that subsystem implements no
        ///     <c>IProvidesDebugSurface</c>, or its <c>perspective:</c> string does not match. The routable
        ///     names are printed so a mismatch is visible rather than inferred.</item>
        ///   <item><b>a provider claims it and passes no buffer</b> ⇒ either the subsystem draws no gizmos
        ///     *(ExCon — a legitimate absence, ruling 49)* or it holds one and forgot to forward it
        ///     *(the silent default)*.</item>
        ///   <item><b>no dispatcher at all</b> ⇒ the editor-shaped host was built without its own buffer.</item>
        /// </list></para>
        /// </summary>
        private string GizmoFeedAbsenceReason()
        {
            if (_dispatcher is null)
                return "this host was built with no debug primitive buffer and no perspective dispatcher, "
                     + "so there is no gizmo feed to read.";

            string active = _dispatcher.CurrentPerspective;
            var routable  = _dispatcher.RoutablePerspectives;

            if (_dispatcher.Active() is null)
                return $"no debug provider claims the active perspective '{active}', so nothing can be "
                     + "routed to. This is NOT a statement that the host draws no gizmos — it may well "
                     + "hold a buffer. Either that subsystem implements no IProvidesDebugSurface, or its "
                     + "provider's `perspective:` string does not match. Routable perspectives here: "
                     + (routable.Count == 0 ? "(none)" : string.Join(", ", routable))
                     + ". See GET /capabilities.";

            return $"the provider for perspective '{active}' passes no DebugPrimitiveBuffer, so this "
                 + "perspective reports no gizmo feed. That is legitimate for a subsystem that draws no "
                 + "gizmos (ExCon), and a defect for one that holds a buffer and does not forward it. "
                 + "Check GET /capabilities for panels.gizmo.";
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-066</c> — THIS HOST'S MISSION EDITOR, resolved exactly like
        /// <see cref="_gizmoFeed"/>: the editor's own, else the ACTIVE PERSPECTIVE's.
        /// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.9.
        ///
        /// <para>🔴 Before this, every <c>/missions/*</c> route answered *"no mission service"* on
        /// <c>--mode all</c> — 📐 while <c>CgfSubsystem:1095</c> had built the <b>same shared</b>
        /// <c>ScenarioMissionService</c> the editor builds at <c>:1962</c>. ⛔ Only the editor handed its
        /// instance over *(`:1967`)*, which is the trap <see cref="MissionService"/>'s own remarks name.</para>
        ///
        /// <para>⚠ <see cref="MissionService"/> stays a settable property: the EDITOR builds its service
        /// after this host, so the setter is about construction ORDER. ⭐ The cluster arm needs no setter —
        /// the provider is read live, which is strictly better.</para>
        /// </summary>
        private Hrot.UI.Common.Facades.IMissionEditorService? _missionEditor
            => _missionService ?? _dispatcher?.MissionEditor;

        private IPreviewController _preview
            => _editorPreview
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.Preview);

        private IEditorLogic _editor
            => _editorLogic
               ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.EditorAuthoring);

        /// <summary>
        /// ⭐⭐⭐ <c>R-3052</c>…<c>R-3055</c> — <b>the cluster master</b>: the editor's own (it is a one-node cluster whose master reads its
        /// bus), else whichever node's bus reaches it. <see langword="null"/> when no subsystem here has an orchestration bus.
        /// 📄 <c>docs/DESIGN_Http_Control_Through_The_Cluster_Master.md</c> §3.
        /// </summary>
        private Hrot.Presentation.DebugApi.IClusterMasterControl? MasterOrNull
            => _editorMaster ?? _dispatcher?.MasterAnyNode;

        /// <summary>⭐ <see cref="MasterOrNull"/>, or <c>NOT_SUPPORTED_HERE(scenario.load)</c>.</summary>
        private Hrot.Presentation.DebugApi.IClusterMasterControl _master
            => MasterOrNull ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.ScenarioLoad);

        private IDiagnosticEventHistoryService _eventHistory
            => _editorEventHistory
               // ⭐ RB-5 — the active provider's history (the Replay Browser's: the shown frame's recorded events)
               ?? _dispatcher?.EventHistory
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

                // ⭐⭐⭐ CE-171 — the ACTIVE subsystem's OWN service first. ⛔⛔ The fallback below builds one
                //    with NO ScenarioSerializer, which silently switches EntityStateExtractionService to its
                //    REFLECTION path: every inline fixed array collapses to `{"FixedElementField": N}` and the
                //    typed DTO the translators exist to emit — a behaviour's decoded BehaviorParameters — is
                //    lost. 📌 CGF already builds a serializer-backed one; it was simply unreachable from here.
                //    ⚠ Not cached: the active perspective can change between calls, and caching would answer
                //    for the wrong node — the same hazard the fallback's own cache-key guards against.
                var provided = _dispatcher?.Extraction;
                if (provided is not null) return provided;

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
        /// <summary>
        /// ⭐ The EDITOR's own catalog, handed in by <c>EditorSubsystem</c>. ⛔ <see langword="null"/> in the
        /// cluster shape, where <see cref="_tkbDb"/> follows the ACTIVE PERSPECTIVE instead.
        /// </summary>
        // ⚠ CE-203 widened to the interface: EditorSubsystem's catalog now comes from
        //   HrotNodeContext.TkbDb (typed ITkbDatabase), and _tkbDb below was ALREADY the
        //   interface — this field was the only concrete link left in the chain.
        private readonly Fdp.Interfaces.ITkbDatabase? _editorTkbDb;
        // ⭐ CE-236: NULLABLE on purpose — see the GeoTransform property. A composition root that has the
        //   node's transform passes it; one that does not falls through to the world singleton rather
        //   than to a private, origin-less WGS84Transform.
        private readonly IGeographicTransform? _geoTransform;

        // Group G — Breakpoints
        private readonly IDataBreakpointManager? _bpManager;
        private BreakpointId _lastHitBreakpointId;
        private long         _lastHitNetworkId;

        // Group H — Checkpoint / Restore / Diff
        private readonly IComponentDiffService _diffService;
        private readonly Dictionary<string, Dictionary<long, JsonNode?>> _diffBaselines = new();
        private int _nextBaselineId;

        // ⭐ The editor's ONE recorder (the shared host composition's): what /status reports as recording, and the replay file the
        //   journal history and recording search scan on this host (R-3053 — a live run records; the API starts nothing).
        private readonly Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? _rrController;

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
        // ⭐⭐⭐ CE-476 — each resolves like `_world`: the EDITOR's own value, else the ACTIVE PERSPECTIVE's AI debug
        //    surface (cluster ctor). 📄 docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md §2 D1–D4.
        //    🔴 The cluster ctor took none of them, so on --mode all /trace/observe answered "Trace coordinator not
        //    available", /entities/{id}/trace "tier: unknown" and /variables "No blueprint debug session" — while CGF
        //    held all of them. ⛔ The silent-default shape: a caller that HAS the dependency must pass it.
        private readonly EditorAiTracerCoordinator?                    _editorTracerValue;
        private readonly Hrot.BTree.Editor.Debug.BTreeDebugSession?    _btreeSessionValue;
        private readonly Hrot.Hsm.Editor.Debug.HsmDebugSession?        _hsmSessionValue;
        private readonly Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintSessionValue;

        // MX1 (Group O) — id→(assetId, name) for the blueprints attached to an entity's blackboard.
        // BlueprintTierSummary.Read needs it to turn a slot's int blueprintId into the asset Guid the
        // debug session addresses variables by.
        private readonly Fdp.Toolkit.Blueprints.BlueprintRegistry? _blueprintRegistryValue;

        /// <summary>⭐ CE-476 — the cluster's per-world AI debug surface (null in the editor shape).</summary>
        private readonly Func<EntityRepository, Hrot.Editor.AiComposition.AiDebugSurface?>? _aiDebugSurfaceGetter;

        /// <summary>⭐ CE-476 D4 — the trace-ARMING tracer, one per world, created on first use (cluster only).</summary>
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, EditorAiTracerCoordinator>
            _clusterTracers = new();

        /// <summary>The active perspective's AI debug surface, or null when this host or perspective has none.</summary>
        private Hrot.Editor.AiComposition.AiDebugSurface? ActiveAiDebugSurface
            => _aiDebugSurfaceGetter is { } get && _dispatcher?.World is { } w ? get(w) : null;

        private EditorAiTracerCoordinator? _editorTracer
            => _editorTracerValue
               ?? (ActiveAiDebugSurface is { } surface
                   ? _clusterTracers.GetValue(surface.World, w => new EditorAiTracerCoordinator(w))
                   : null);

        private Hrot.BTree.Editor.Debug.BTreeDebugSession? _btreeSession
            => _btreeSessionValue ?? ActiveAiDebugSurface?.BTree;

        private Hrot.Hsm.Editor.Debug.HsmDebugSession? _hsmSession
            => _hsmSessionValue ?? ActiveAiDebugSurface?.Hsm;

        private Hrot.Blueprints.Core.Debug.BlueprintDebugSession? _blueprintSession
            => _blueprintSessionValue ?? ActiveAiDebugSurface?.Blueprint;

        private Fdp.Toolkit.Blueprints.BlueprintRegistry? _blueprintRegistry
            => _blueprintRegistryValue ?? ActiveAiDebugSurface?.Blueprints;

        // Group L — Attribute patch + StructEdit component edit
        private readonly JsonAttributeCompiler? _injectedAttributeCompiler;
        private JsonAttributeCompiler? _builtAttributeCompiler;

        /// <summary>
        /// ⭐ <c>CE-236</c> — built LAZILY. It used to need <see cref="GeoTransform"/> (this service is constructed BEFORE the
        /// world exists on some hosts — the ordering that made <c>_behaviorRegistry</c> a <c>Func</c> in <c>CE-169</c>, and what
        /// forced an old <c>?? new WGS84Transform()</c> default that answered against the wrong origin). ⭐ <c>CE-4073</c>: it needs
        /// none now (the position attributes are local ENU metres), so laziness is only a startup saving.
        /// </summary>
        private JsonAttributeCompiler _attributeCompiler
            => _injectedAttributeCompiler
               ?? (_builtAttributeCompiler ??=
                   Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build());
        private readonly IComponentEditService _componentEditSvc;

        // Group M — Focus / Annotations (ADA-BATCH-14)
        private readonly DebugPrimitiveBuffer? _primitiveBuffer;

        // CE-3187 (R-2005 / R-2007): the shared options — enums by name only, as the schema advertises.
        internal static readonly JsonSerializerOptions SearchPredicateJsonOptions = FdpJsonOptionsRegistry.DefaultRelaxed;

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
        /// The dump options, for reading a client's PATCH: identical converters, so a value the API emitted is accepted
        /// verbatim (HN-002).
        /// </summary>
        /// <remarks>
        /// ⛔ HISTORY: this used to relax enums to accept their integer form. CE-3187 / R-2007 ("json always should read
        /// enums as strings not numeric constants") retired that — the API emits names, and a caller sends names back.
        /// </remarks>
        internal static readonly JsonSerializerOptions DebugApiPatchOptions = DebugApiDumpOptions;

        private static JsonSerializerOptions BuildDebugApiDumpOptions()
        {
            // CE-3187 (R-2005): the shared options plus one delta — non-finite-safe scalar and vector converters, inserted
            // FIRST so they take priority (converters are checked in registration order).
            return FdpJsonOptionsRegistry.Derive(o =>
            {
                o.Converters.Insert(0, new NonFiniteFloatSentinelConverter());
                o.Converters.Insert(1, new NonFiniteDoubleSentinelConverter());
                o.Converters.Insert(2, new DebugApiVector2SafeConverter());
                o.Converters.Insert(3, new DebugApiVector3SafeConverter());
                o.Converters.Insert(4, new DebugApiVector4SafeConverter());
                o.Converters.Insert(5, new DebugApiQuaternionSafeConverter());
            });
        }

        /// <summary>Default upper bound for event-history queries.</summary>
        public const int DefaultMaxEvents = 200;

        // MX4a — behaviour discovery. The registry already holds behaviourId -> JsonParamsDtoType, so
        // the schema comes from the SAME definition the runtime parses params with; the mission
        // service (optional) gives exact parity with the editor's mission-task combo for an entity.
        // ⭐⭐ CE-169 — TWO sources, because the two composition roots differ in TIMING, not in intent.
        //   ① The EDITOR builds its registry before the service, so it hands over a VALUE.
        //   ② The CLUSTER cannot: `Program.cs` constructs this service at :429 and only calls
        //      `orchestrator.Run()` at :675, so CGF's registry does not exist yet at construction time.
        //      A captured value would be null FOREVER — which is precisely the defect this fixes.
        //   ⇒ the cluster hands over a Func, resolved lazily on first use. 📌 Same shape, same reason as
        //     `logSinks` directly below it, whose comment says the window manager "may not exist yet".
        private readonly Fdp.Toolkit.Behavior.BehaviorRegistry?         _behaviorRegistryValue;
        private readonly Func<Fdp.Toolkit.Behavior.BehaviorRegistry?>?  _behaviorRegistryGetter;

        /// <summary>
        /// The node's behaviour registry, or <c>null</c> when this host genuinely has none.
        /// ⛔ Never fabricate an empty registry here — an empty one reports "no behaviours" as though
        /// that were the node's truth, which is the <c>CE-110</c> shape the TkbDatabase comment warns
        /// about. A null answers "not available", and that is an honest answer.
        /// </summary>
        private Fdp.Toolkit.Behavior.BehaviorRegistry? _behaviorRegistry
            => _behaviorRegistryValue ?? _behaviorRegistryGetter?.Invoke();

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
            Fdp.Interfaces.ITkbDatabase?    tkbDb              = null,
            IGeographicTransform?           geoTransform       = null,
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
            // ⭐⭐⭐ R-3052…R-3055 — the editor's own cluster master, through its orchestration bus (OrchestrationBusMasterControl).
            Hrot.Presentation.DebugApi.IClusterMasterControl? master = null,
            // ⭐ CE-515 — the editor's pack, the same seam every cluster node exposes (cgf==editor).
            Func<EntityRepository, Hrot.Common.EntityCreation.EntityCreation?>? entityCreation = null,
            // ⭐⭐ CE-4002 — the editor's in-process master's roster and the cancel onto its bus. ⚠ Optional for the
            //    same reason as requestTransition; ⛔ the production caller HAS both and must pass them.
            Func<IReadOnlyList<Hrot.Presentation.DebugApi.ClusterRosterEntry>?>? clusterRoster = null,
            // ⭐ R-3055 — the master's exercise inventory and the loaded replay's length (the core's ClusterUiCache).
            Func<IReadOnlyList<Fdp.Toolkit.Orchestration.ExerciseInventoryItem>?>? exercises = null,
            Func<float?>? replayDuration = null,
            // ⭐ CE-3329 R-3058 — the replay session's time axis (the core's ClusterUiCache)
            Func<Fdp.Toolkit.Orchestration.ReplayAxis?>? replayAxis = null,
            // ⭐⭐ CE-4087 — the in-process master's load-gate text (GET /cluster/roster → load.notice). ⛔ The editor HOLDS the master.
            Func<string?>? clusterLoadNotice = null,
            // ⭐ CE-4090 (AQ108 D14) — the in-process master's "this run was not recorded" (the core's ClusterUiCache). ⛔ The editor HOLDS it.
            Func<bool?>? runNotRecorded = null,
            Func<bool?>? recordLiveRunsOff = null,
            Func<bool?>? askToKeepRecordings = null,   // ⭐ AQ108 D15 (CE-4100 ①)
            Func<Fdp.Toolkit.Orchestration.ClusterArmedMode?>? armedMode = null,   // ⭐ CE-3341
            // ⭐ CE-4093 (AQ108 D20) — the in-process master's check round answer + last verdict (the core's ClusterUiCache). ⛔ The editor HOLDS them.
            Func<Guid, Fdp.Toolkit.Orchestration.UnsavedCheckOutcome?>? unsavedCheck = null,
            Func<bool?>? scenarioDirty = null,
            // ⭐⭐ CE-3344 (R-3069) — the editor's node's ONE cluster read model; CanOperate gates the HTTP cluster routes. ⛔ The editor HOLDS it (silent-default rule).
            Func<Fdp.Toolkit.Orchestration.ClusterView?>? clusterView = null)
        {
            _editorClusterView = clusterView;
            _editorRecordLiveRunsOff = recordLiveRunsOff;
            _editorAskToKeepRecordings = askToKeepRecordings;
            _editorArmedMode = armedMode;
            _editorRunNotRecorded = runNotRecorded;
            _editorUnsavedCheck = unsavedCheck;
            _editorScenarioDirty = scenarioDirty;
            _entityCreationGetter = entityCreation;
            _editorClusterLoadNotice = clusterLoadNotice;
            _editorReplayAxis     = replayAxis;
            _editorClusterRoster  = clusterRoster;
            _editorExercises      = exercises;
            _editorReplayDuration = replayDuration;
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
            _editorMaster = master;
            _editorTkbDb       = tkbDb            ?? new TkbDatabase();   // ⭐ the editor DOES pass one; kept non-null so its shape is unchanged
            _geoTransform      = geoTransform;   // ⭐ CE-236: no origin-less default — see GeoTransform
            _bpManager         = bpManager;
            _behaviorRegistryValue  = behaviorRegistry;
            _missionService    = missionService;
            _blueprintRegistryValue = blueprintRegistry;
            _diffService       = diffService ?? new ComponentDiffService();
            _rrController      = rrController;
            _logSinks          = logSinks ?? (() => Array.Empty<IMessageLogSource>());
            _editorTracerValue     = editorTracer;
            _btreeSessionValue     = btreeSession;
            _hsmSessionValue       = hsmSession;
            _blueprintSessionValue = blueprintSession;
            _injectedAttributeCompiler = attributeCompiler;   // CE-236: else built lazily from GeoTransform
            _componentEditSvc  = componentEditSvc  ?? new ComponentEditServiceBuilder().Build();
            _primitiveBuffer   = primitiveBuffer;
            if (_bpManager != null)
            {
                _bpManager.OnBreakpointHit += (bp, entity) =>
                {
                    _lastHitBreakpointId = bp.Id;
                    _lastHitName = bp.DisplayName;
                    _lastHitSummary = BreakpointConditionSummarizer.Summarize(bp.Condition);
                    _lastHitWhat = _bpManager.LastHitDetail;   // ⭐ CE-1323 — "entered Withdrawing" for a behaviour step
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
            Fdp.Interfaces.ITkbDatabase?                  tkbDb             = null,
            IGeographicTransform?                         geoTransform      = null,
            DebugPrimitiveBuffer?                         primitiveBuffer   = null,
            Func<IReadOnlyList<IMessageLogSource>>?       logSinks          = null,
            // ⭐ CE-169 — a Func, not a value: CGF's registry is built during subsystem boot, which
            //   happens AFTER this service is constructed. See the field comment for the measurement.
            Func<Fdp.Toolkit.Behavior.BehaviorRegistry?>? behaviorRegistry  = null,
            // ⭐⭐⭐ CE-515 (was CE-271 seam ⑤, IG-only) — the entity-creation pack of the subsystem that OWNS the
            //   ACTIVE perspective's world, so POST /entities/create-request creates on the SELECTED node through the
            //   request path (routing + grants). A Func for the same boot-order reason: the pack exists only after
            //   the subsystem builds it.
            Func<EntityRepository, Hrot.Common.EntityCreation.EntityCreation?>? entityCreation = null,
            // ⭐⭐⭐ CE-476 — the node's AI debug surface, resolved against the ACTIVE perspective's world. A Func for
            //   the same boot-order reason as behaviorRegistry (CGF composes it during Initialize, after this ctor).
            Func<EntityRepository, Hrot.Editor.AiComposition.AiDebugSurface?>? aiDebugSurface = null)
        {
            _dispatcher         = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _aiDebugSurfaceGetter = aiDebugSurface;
            _clusterStateGetter = clusterState;
            _entityCreationGetter = entityCreation;

            // ⭐⭐⭐ CE-110 — ⛔⛔ NO `?? new TkbDatabase()` HERE. That default is what made /tkb/* answer
            //    `[]` on every cluster node: the composition root passes nothing, so the service latched a
            //    private EMPTY catalog and reported it as this node's truth. ⭐ Left null, `_tkbDb` falls
            //    through to the ACTIVE PERSPECTIVE's real catalog (`_dispatcher.TkbDb`), and if a node
            //    genuinely has none the route says NOT_SUPPORTED_HERE instead of lying with an empty list.
            _editorTkbDb  = tkbDb;
            // ⭐⭐⭐ CE-236 — ⛔⛔ NO `?? new WGS84Transform()` HERE, for exactly CE-110's reason one line
            //    above. That default had NO ORIGIN (WGS84Transform's origin fields default to 0 and only
            //    SetOrigin fills them), so --mode all answered /world/geo-to-local against 0°N 0°E while
            //    every simulation node ran on Berlin. Left null, GeoTransform falls through to the world
            //    singleton — the one the simulation itself converts with.
            _geoTransform = geoTransform;


            _diffService       = new ComponentDiffService();
            _logSinks          = logSinks ?? (() => Array.Empty<IMessageLogSource>());   // diagnostics MD-001: lazy Func
            _injectedAttributeCompiler = null;   // AX-017 lives in Fdp.Toolkits; CE-236 builds it lazily from GeoTransform
            _componentEditSvc  = new ComponentEditServiceBuilder().Build();
            _primitiveBuffer   = primitiveBuffer;
            _behaviorRegistryGetter = behaviorRegistry;
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
                // ⭐ R-3053 — a LIVE run records on every node; nothing else does (the API no longer starts a recording).
                //   ⭐ CE-4090 (AQ108 D14, D22) — and for the run the CLUSTER shows: `recording` is false when the master loaded the live run with
                //   "Record live runs" off (the same fact the 409 gates answer), whatever this node's recorder holds; unknown ⇒ the node's own.
                ["recording"]    = RunIsRecorded(),
                // ⭐ Which context these numbers describe — ⛔ without it, a cluster status is ambiguous
                //   about WHOSE world it counted.
                ["perspective"]  = _dispatcher?.CurrentPerspective ?? _perspectives?.CurrentPerspective,
                // ⭐⭐ CE-4094 (AQ108 M7/D22) — where the reads above were answered from: equal to `perspective` except while `Start`
                //   shows, when it is the REMEMBERED perspective. ⛔ Without it `perspective: Start` would leave the caller guessing WHICH
                //   node's world /entities counted.
                ["routedTo"]     = _dispatcher?.RoutedTo ?? RoutedToFromSwitcher(),
                // ⭐⭐ CE-4093 (AQ108 D22) — the LAST check round's verdict (true = the open scenario has unsaved changes); null when none ran or the cluster
                //   left editing since. ⛔ Reading it never runs a round — POST /scenario/check-unsaved does.
                ["scenarioDirty"] = ScenarioDirtyOrNull,
                // ⭐⭐ CE-4097 part two (AQ108 D22) — what ▶ would run from editing on this host: preview | live | aar (null: this host has no run choice). Read-only; POST /run-mode sets it.
                ["armedMode"]    = TryRead(() => (JsonNode?)ArmedModeText()),
                // ⭐⭐ CE-3344 (R-3069) — cluster READY from this host (ClusterView.CanOperate): true = the cluster routes work; false + `notOperable` = they answer 409 with that reason;
                //   absent when this host keeps no cluster read model. ⭐ What a test harness polls before it drives the cluster.
                ["canOperate"]   = ClusterNotOperableReasonKnown() is { } known ? (JsonNode?)(known.Reason is null) : null,
                ["notOperable"]  = ClusterNotOperableReasonKnown()?.Reason,
                // ⭐⭐ CE-1451 — this node's LOAD as its heartbeat reports it (phase, step, failure) and the run gate + loading screen its controls read. Each is absent
                //   (null) where the host has none: no cluster view ⇒ no `load`; no run choice ⇒ no `runGate`; no composed screen ⇒ no `loadingScreen`.
                // ⭐ CE-3354 ② — WHICH node answers: its unique name and id (the selected perspective's node in a multi-node process); null where the host keeps no cluster view.
                ["node"]         = TryRead(NodeOrNull),
                ["load"]         = TryRead(LoadStatusOrNull),
                ["runGate"]      = TryRead(RunGateOrNull),
                ["loadingScreen"] = TryRead(LoadingScreenOrNull),
            };
        }

        /// <summary>
        /// ⭐ CE-4090 (AQ108 D22) — <c>GET /status.recording</c>: is the run the cluster shows RECORDED. True in a live run, a review or a recording
        /// (AAR) that has its recording; false in Idle / Edit / Preview and in a live run the master loaded with "Record live runs" off. ⭐ Read from the
        /// master's broadcast (one fact, every host); a host that has no cluster read model answers its own recorder's state (as before), or null.
        /// </summary>
        private JsonNode? RunIsRecorded()
        {
            var notRecorded = RunNotRecordedOrNull;
            var state = CurrentClusterStateOrNull();
            if (notRecorded is { } off && state is { } s)
                return !off && (s == ClusterState.OperatingLive || ClusterStates.IsReplaying(s));
            return _rrController is { } rr ? (JsonNode?)(rr.ActiveRecordingModule is not null) : null;
        }

        /// <summary>⭐ CE-4094 — the editor shape's `routedTo` (no dispatcher): the window manager's perspective, or — while Start shows — the remembered one.</summary>
        private string? RoutedToFromSwitcher()
            => _perspectives is not { } sw ? null
             : sw.CurrentPerspective == Fdp.Presentation.WindowManager.WindowManager.StartPerspectiveName ? sw.RememberedPerspective
             : sw.CurrentPerspective;

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
            // ⭐ RB-5 — a replayed world's events are captured as "Replay" (ReplayBrowserContext): the world bus of that world
            string[] providers = string.Equals(bus, "orchestration", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Orchestration" }
                : new[] { "World", "Replay" };

            var history = _eventHistory.GetHistory(providers);

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
                string?   payloadError = null;
                try
                {
                    // EventSerializationHelper produces inspector-grade readable JSON; parse it
                    // back into a JsonNode so it passes through the envelope verbatim.
                    var json = EventSerializationHelper.SerializeToJson(e.RawEvent);
                    payload  = JsonNode.Parse(json);
                }
                catch (Exception ex)
                {
                    // ⭐⭐ CE-191 — keeping the metadata row is RIGHT (one unserializable event must not
                    //   cost the caller the whole history). ⛔ Reporting nothing was not: a null payload
                    //   read identically to "this event genuinely carried no data".
                    // ⭐ Reported ON THE ROW rather than failing the read — the same shape
                    //   DescribeCommand already uses for isEnabledError.
                    payload      = null;
                    payloadError = $"{ex.GetType().Name}: {ex.Message}";
                }

                arr.Add(new JsonObject
                {
                    ["frame"]     = e.Frame,
                    ["provider"]  = e.ProviderName,
                    ["type"]      = e.TypeName,
                    ["isManaged"] = e.IsManaged,
                    ["summary"]   = e.Summary,
                    ["payload"]   = payload,
                    ["payloadError"] = payloadError,
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

        /// <summary><c>POST /sim/step {count?, dt?}</c> — discrete single-step(s) (main thread); <paramref name="dt"/> sizes
        /// each step (the film director's 1/20 or 1/15 s, `DESIGN_Film_Director.md` D-1), else 1/60 s.</summary>
        public JsonNode Step(int count = 1, float? dt = null)
        {
            if (count < 1) count = 1;
            for (int i = 0; i < count; i++)
            {
                if (dt is { } seconds) _time.StepBy(seconds);
                else                   _time.Step();
            }
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

        /// <summary>
        /// ⭐⭐ <c>CE-104</c> — <c>isPaused</c> for the pause gate, or <see langword="null"/> on a host that has
        /// no time controller to ask. ⛔ Mirrors <see cref="TotalTimeOrNull"/> exactly: a clockless perspective
        /// must DEGRADE to "nothing to confirm" rather than make a supported command look unsupported.
        /// </summary>
        public bool? IsPausedOrNull()
        {
            try { return _time.IsPaused; }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
        }

        /// <summary>
        /// ⭐ <c>CE-3077</c> — whether the host is in preview, for the step route, or <see langword="null"/> on a host with no
        /// preview controller to ask. ⛔ Mirrors <see cref="IsPausedOrNull"/>: a host that cannot answer degrades.
        /// </summary>
        /// <summary>⭐ CE-3329 row 21 — the cluster is editing (a step from here ENTERS the dry run, through the cluster).</summary>
        public bool InEdit() => _time.Mode == Hrot.UI.Common.Facades.TransportMode.Edit;

        public bool? InPreviewOrNull()
        {
            try { return _preview.IsInPreviewMode; }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
        }

        /// <summary>
        /// ⭐ CE-4024 — whether sim time is advancing: in preview (a host with no preview controller — a cluster node — is always live)
        /// and not paused. 📌 Measured 2026-10-17: the spawn route read <c>_preview</c> directly AFTER publishing the request, so on CGF it
        /// created the entity and then answered <c>NOT_SUPPORTED_HERE(preview.control)</c>.
        /// </summary>
        private bool TimeAdvancing() => InPreviewOrNull() != false && IsPausedOrNull() != true;

        /// <summary><c>POST /sim/timescale {scale}</c> (main thread).</summary>
        public JsonNode SetTimeScale(float scale)
        {
            _time.SetTimeScale(scale);
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
            // ⭐ CE-4101 — through the editor's ONE guarded door, with HTTP's refusing asker (nothing is prompted; a run is stopped, a loss is refused)
            if (_editor.Door is { } door) door.Open(name, HttpAsker(null), UnsavedAnswer.Unchanged);
            else _editor.LoadScenarioByName(name);   // an editor logic with no session door (a fake)
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4101</c> (AQ108 §2.11a, D22) — <b>what HTTP's route learned from the cluster's check round</b> before it asked the editor's door: <paramref name="Answer"/> is the round's verdict, <paramref name="ChangedNodes"/> the nodes
        /// that differ (for the refusal's text), <paramref name="Discard"/> the body's <c>discardUnsaved: true</c> (the door then does not even ask). The DECISION — and so the 409 — is the door's, answered through
        /// <see cref="RefusingAsker"/>; ⛔ not a second check in the route.
        /// </summary>
        public sealed record HttpUnsavedGate(UnsavedAnswer Answer, string? ChangedNodes, bool Discard);

        /// <summary>The 409 text of a load / stop that would lose unsaved edits (HTTP's own words, unchanged since CE-4093).</summary>
        public static string UnsavedRefusal(string? changedNodes)
            => "the scenario has unsaved changes (nodes: " + changedNodes + "): save it first (POST /scenario/save), "
             + "or send {\"discardUnsaved\": true} to load anyway.";

        /// <summary>The marker a service answer carries in its hint slot when the door REFUSED the move: the route answers 409 (a state conflict), not 400.</summary>
        public const string ConflictHint = "door.refused";

        /// <summary>⭐ CE-4101 — HTTP's asker over <paramref name="gate"/>: nothing is prompted; a run is stopped, unsaved edits are refused with <see cref="UnsavedRefusal"/> unless the body discarded them.</summary>
        private static RefusingAsker HttpAsker(HttpUnsavedGate? gate)
            => new(q => q.Kind == DoorQuestionKind.Unsaved ? UnsavedRefusal(gate?.ChangedNodes) : null, gate?.Discard ?? false);

        /// <summary>
        /// ⭐ CE-4101 — the door that DECIDES for HTTP: this host's scenario session's when it has one (the editor), else a deciding-only door over the cluster state this service reads (a cluster node with no editor): the
        /// SAME class and the same questions, ⛔ never a second copy of the decision.
        /// </summary>
        private ScenarioDoor HttpDoor => _editorLogic?.Door ?? (_decidingDoor ??= new ScenarioDoor(raw: null, () => ClusterReplayControls.ModeOf(CurrentClusterStateOrNull() ?? ClusterState.Idle)));
        private ScenarioDoor? _decidingDoor;

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
        public JsonNode LoadScenarioEdit(string name, HttpUnsavedGate? gate = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));

            var asker = HttpAsker(gate);
            var answered = gate?.Answer ?? UnsavedAnswer.Unchanged;
            if (_editorLogic?.Door is { } editorDoor)
            {
                // ⭐ CE-4101 — the editor's ONE guarded door, asked with HTTP's refusing asker: a run is stopped (the session leaves a dry run itself, CE-1025), a loss is refused — the 409 is the door's answer
                var stoppedRun = RunToStopForLoad();
                editorDoor.Open(name, asker, answered);
                if (asker.Refusal is { } refusal) return new JsonObject { ["refused"] = refusal };
                return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingEdit), ["via"] = "editor-driver",
                                        ["stoppedRun"] = stoppedRun };
            }

            // no session door here (a cluster node, a fake editor logic): the SAME decision, made by HTTP's own deciding door; this host's execution below
            JsonNode? sent = null;
            HttpDoor.Admit(DoorMove.OpeningScenario(name), asker, answered, () => sent = SendEditLoad(name));
            if (asker.Refusal is { } refused) return new JsonObject { ["refused"] = refused };
            return sent ?? throw new InvalidOperationException("the door neither admitted nor refused the edit load");
        }

        private JsonNode SendEditLoad(string name)
        {
            var stopped = LeaveRunForLoad();
            if (_editorLogic is not null)
            {
                _editorLogic.LoadScenarioByName(name);   // a host whose editor logic has no session door (a fake): as it always was
                return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingEdit), ["via"] = "editor-driver",
                                        ["stoppedRun"] = stopped };
            }

            var editRequest = Guid.NewGuid();
            _lastLoad = (editRequest, name);   // ⭐ CE-3354 ④ — GET /status.load and waitForReady follow THIS request's outcome
            ScenarioLoads.Request(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            {
                TransactionId = editRequest,
                TargetState   = ClusterState.OperatingEdit,
                ScenarioId    = name,
                // ⭐ Guid.Empty, mirroring the orchestrator panel's own "Load into Edit" button: authoring is
                //   not an exercise run, so it gets no ExerciseId.
                ExerciseId    = Guid.Empty,
            });
            return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingEdit), ["via"] = "cluster-intent",
                                    ["requestId"] = editRequest.ToString(),
                                    ["unloadFirst"] = ScenarioLoads.IsWaitingForIdle, ["stoppedRun"] = stopped };
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
        public JsonNode LoadScenarioLive(string name, bool startPaused = true, bool forceRecording = false, HttpUnsavedGate? gate = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));

            // ⭐ CE-4101 — the DECISION is the editor's ONE door's (HTTP's refusing asker: a run is stopped, a loss is refused); the EXECUTION stays this route's own load (it carries ForceRecording and
            //   the startPaused TimeMode, which the session's raw live load does not) — so ⛔ nothing is decided here, only published once the door lets the move through.
            JsonNode? sent = null;
            void Go() => sent = SendLiveLoad(name, startPaused, forceRecording);
            var asker = HttpAsker(gate);
            HttpDoor.Admit(DoorMove.LoadingLive(name), asker, gate?.Answer ?? UnsavedAnswer.Unchanged, Go);
            if (asker.Refusal is { } refusal) return new JsonObject { ["refused"] = refusal };
            return sent ?? throw new InvalidOperationException("the door neither admitted nor refused the live load");
        }

        private JsonNode SendLiveLoad(string name, bool startPaused, bool forceRecording)
        {
            var stopped = LeaveRunForLoad();

            var liveRequest = Guid.NewGuid();
            _lastLoad = (liveRequest, name);   // ⭐ CE-3354 ④
            ScenarioLoads.Request(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            {
                TransactionId = liveRequest,
                TargetState   = ClusterState.OperatingLive,
                ScenarioId    = name,
                ExerciseId    = Guid.NewGuid(),
                // ⭐ Q86 §4-G — the clock is reset to 0 either way; startPaused (default true) decides whether it runs
                //   once the load is done. The default keeps the documented load → POST /sim/play flow.
                TimeMode      = startPaused ? "Deterministic" : null,
                // ⭐ CE-4090 (AQ108 D14, §5a) — the scenario verifier compares the run's RECORDING against a golden: its live load records
                //   whatever the cluster's "Record live runs" setting says. ⛔ Not reachable from HTTP — only the verifier's own load asks.
                ForceRecording = forceRecording,
            });
            return new JsonObject { ["requested"] = name, ["target"] = nameof(ClusterState.OperatingLive), ["via"] = "cluster-intent",
                                    ["requestId"] = liveRequest.ToString(),
                                    ["startPaused"] = startPaused,
                                    ["unloadFirst"] = ScenarioLoads.IsWaitingForIdle, ["stoppedRun"] = stopped };
        }

        // ⭐⭐ CE-3354 ④ (R-3075, DESIGN_Cluster_Load_Phase §11.10a) — the last load this API sent: "loaded" is ITS request's op status Success,
        //   which the master publishes only when every node's load chain resolved (the filming node's 3-D map included) — ⛔ never the
        //   announced state, which the master advances at the round's start.
        private (Guid RequestId, string Scenario)? _lastLoad;

        /// <summary>⭐ CE-3354 ④ — the phase of load <paramref name="requestId"/> from the cluster's op outcomes (<see cref="Fdp.Toolkit.Orchestration.ClusterView.OutcomeOf"/>):
        /// Loading until the master answers, then Loaded or Failed (with its reason); <see langword="null"/> where this host keeps no cluster view.</summary>
        public (string Phase, string? Reason)? LoadPhaseOf(Guid requestId)
        {
            if ((_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode) is not { } view) return null;
            return view.OutcomeOf(requestId) switch
            {
                null => ("Loading", null),
                { Status: Fdp.Toolkit.Orchestration.OrchestrationStatusCode.Success } => ("Loaded", null),
                { } o => ("Failed", o.Reason ?? o.Status.ToString()),
            };
        }

        /// <summary>⭐ <c>CE-1025</c> (R-1031, load design §9.1) — the HTTP load is non-interactive (ruling 53): no prompt; a running dry run
        /// is left WITHOUT a rewind (its restore point forgotten) and a live run is stopped by the load's own unload to Idle. Returns the run
        /// it stopped ("a live run" / "a dry run"), or null — the answer says so.</summary>
        private string? LeaveRunForLoad()
        {
            var run = RunToStopForLoad();
            if (InPreviewOrNull() == true) _preview.ForgetPreview();
            return run;
        }

        /// <summary>The run a load would stop ("a live run" / "a dry run"), or null — the ONE definition (<see cref="Hrot.Editor.AiShared.Scenarios.ScenarioLoadGuard"/>), with no side effect.</summary>
        private string? RunToStopForLoad()
        {
            // ⭐ a host with NO preview controller (a `--mode all` cluster node) is never in a dry run: degrade, as InPreviewOrNull does —
            //   📌 behaviors, 2026-10-23: reading _preview directly made every live load answer NOT_SUPPORTED_HERE(preview.control) there
            bool inPreview = InPreviewOrNull() == true;
            return Hrot.Editor.AiShared.Scenarios.ScenarioLoadGuard.RunningRun(inPreview, CurrentClusterState());
        }

        // ⭐⭐ CE-295 — loads go through the ONE shared sequence: from any state but Idle it asks for Idle (the unload),
        //   and the load goes out once the cluster reports Idle. A single request from Live/Edit planned an EMPTY path
        //   (already in the target state) and only copied the files — every later live load was a silent no-op.
        //   📄 docs/DESIGN_Cluster_Load_Phase.md §9. Late-bound: the sender and the state follow the active perspective.
        private Fdp.Toolkit.Orchestration.ScenarioLoadSequence? _scenarioLoads;
        private Fdp.Toolkit.Orchestration.ScenarioLoadSequence ScenarioLoads
            => _scenarioLoads ??= new Fdp.Toolkit.Orchestration.ScenarioLoadSequence(
                   send:         intent => _master.Transition(intent),
                   currentState: () => CurrentClusterState());

        /// <summary>
        /// ⭐ <c>CE-295</c> — sends a load that waits for the unload once the cluster is Idle. <b>Main thread.</b> Returns
        /// true when no load is waiting any more (sent now, or none was waiting).
        /// </summary>
        public bool PumpScenarioLoad()
        {
            ScenarioLoads.Pump();
            return !ScenarioLoads.IsWaitingForIdle;
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

        /// <summary>
        /// ⭐⭐⭐ <b><c>POST /scenario/save {name}</c> — persists the authored world, cluster-wide.</b>
        ///
        /// <para>⭐⭐ Symmetric with <see cref="LoadScenarioEdit"/>: two arms ending in the SAME cluster save.
        /// When the editor driver is present (<c>--mode all</c>'s brain, or the standalone editor),
        /// <see cref="IEditorLogic.SaveScenarioAs"/> is used — it already publishes
        /// <c>ExecuteStorageOpIntent{SaveScenarioJson}</c> through <c>EditorScenarioSession</c>, so the fan-out
        /// + merge pipeline runs the same everywhere (proved by <c>T-A</c>). ⛔ On a cluster NODE with no editor
        /// driver (a headless SimHost/CGF/IG/ExCon serving its own debug port), the intent is published directly
        /// on that node's control-plane bus via the dispatcher — mirroring <c>LoadScenarioEdit</c>'s
        /// <c>cluster-intent</c> arm — so <c>/scenario/save</c> is no longer editor-only.</para>
        ///
        /// <para>⚠ The old behaviour on a cluster node was a <b>501</b> (<c>/scenario</c> gated on
        /// <c>editor.authoring</c>). <c>CE-277(c0)</c> gives the route its OWN capability
        /// (<see cref="Hrot.Presentation.DebugApi.DebugCapabilities.SaveScenarioJson"/>) so a node that CAN
        /// trigger the save advertises it honestly.</para>
        /// </summary>
        public JsonNode SaveScenario(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scenario name is required.", nameof(name));

            // ⭐ R-3052 — the same intent on every host, the editor included (it is a one-node cluster): ⛔ no editor-driver arm.
            var master = MasterOrNull ?? throw NotSupportedHere(Hrot.Presentation.DebugApi.DebugCapabilities.SaveScenarioJson);
            master.Storage(new Fdp.Toolkit.Orchestration.ExecuteStorageOpIntent
            {
                RequestId    = Guid.NewGuid(),
                Operation    = Fdp.Toolkit.Orchestration.StorageOpType.SaveScenario,
                ScenarioName = name,
            });
            return new JsonObject { ["saved"] = name, ["via"] = "cluster-intent" };
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
            bool timeAdvancing = TimeAdvancing();

            // Deserialize the payload JSON to the target CLR type.
            // We use System.Text.Json (default options) for the payload fields since that's
            // what callers provide; the inspector-grade path is for output only.
            object? evt = null;
            if (payload != null)
            {
                try
                {
                    var json = payload.ToJsonString();
                    // ⭐ CE-3168 — the CE-225 shape (SpawnTransformJsonOptions): FDP events declare public FIELDS, which the
                    //   default options silently skip, so every payload used to publish an all-default event and answer ok.
                    //   Fields bind now, an unknown key RAISES (the catch below refuses it), and an Entity field takes the
                    //   unit's NETWORK id — the id this API speaks everywhere else.
                    evt = JsonSerializer.Deserialize(json, clrType, CommandJsonOptions());
                }
                catch (Exception ex)
                {
                    return (null, $"Failed to deserialize payload for '{eventTypeName}': {ex.Message}");
                }
            }
            else
            {
                // Create a default instance.
                //
                // ⭐⭐ CE-191 — this was `catch { evt = null; }`, and the null then went straight into
                //   PublishEventObject below. Whatever that did, the caller was never told the event they
                //   asked for had not been constructed. A type without a usable parameterless constructor
                //   is a refusal, not a publish.
                try { evt = Activator.CreateInstance(clrType); }
                catch (Exception ex)
                {
                    return (null,
                            $"'{eventTypeName}' could not be default-constructed: {ex.GetType().Name}: "
                          + $"{ex.Message}. Send a 'payload' object for this type instead.");
                }

                if (evt == null)
                    return (null,
                            $"'{eventTypeName}' default-constructed to null, so there is nothing to publish. "
                          + "Send a 'payload' object for this type.");
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
        /// ⭐ <c>CE-3168</c> — how <c>POST /entities/command</c> binds a payload: fields included, case-insensitive, an
        /// unmapped key refused, enums by name (CE-3187 / R-2007 — no longer by number), and an <see cref="Entity"/> given as
        /// its network id (resolved against THIS node's map). Built per call: the map is the active perspective's.
        /// </summary>
        private JsonSerializerOptions CommandJsonOptions()
            // CE-3187 (R-2005): the shared options plus two deltas — unmapped keys refused, and the per-perspective Entity converter.
            => FdpJsonOptionsRegistry.Derive(o =>
            {
                o.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
                o.Converters.Insert(0, new EntityByNetworkIdConverter(_entityMap));
            });

        /// <summary>⭐ <c>CE-3168</c> — an <see cref="Entity"/> in a command payload is the unit's network id (a number).</summary>
        private sealed class EntityByNetworkIdConverter : System.Text.Json.Serialization.JsonConverter<Entity>
        {
            private readonly NetworkEntityMap _map;
            public EntityByNetworkIdConverter(NetworkEntityMap map) => _map = map;

            public override Entity Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
            {
                long id = reader.TokenType switch
                {
                    JsonTokenType.Number => reader.GetInt64(),
                    JsonTokenType.String when long.TryParse(reader.GetString(), out var parsed) => parsed,
                    _ => throw new JsonException("An entity is given as its network id (a number)."),
                };
                if (!_map.TryGetEntity(id, out var entity))
                    throw new JsonException($"No entity with network id {id} on this node. List entities with GET /entities.");
                return entity;
            }

            public override void Write(Utf8JsonWriter writer, Entity value, JsonSerializerOptions options)
            {
                if (_map.TryGetNetworkId(value, out long id)) writer.WriteNumberValue(id);
                else writer.WriteNullValue();
            }
        }

        /// <summary>
        /// <summary>
        /// 🔴🔴 <b>CE-225 — why this needs its own options, and why the default silently lost every
        /// spawn position.</b>
        /// </summary>
        /// <remarks>
        /// <para><c>SimTransform</c> declares <b>public FIELDS</b>, not properties
        /// (<c>public Vector3 Position; public Quaternion Rotation;</c>), and
        /// <c>System.Text.Json</c> <b>ignores fields unless <see cref="JsonSerializerOptions.IncludeFields"/>
        /// is set</b>. So a bare <c>Deserialize&lt;SimTransform&gt;</c> could never bind a position from ANY
        /// payload — it returned a default-constructed struct, the entity was created at the origin, and the
        /// endpoint answered <c>ok:true</c>. 📐 Measured: both the shape this API's own SKILL documents
        /// (<c>{"position":{"x":..}}</c>) and the shape the entity dump round-trips landed at
        /// <c>[0,0,z]</c>.</para>
        ///
        /// <para>⛔⛔ <b>That is precisely the failure <c>CE-191</c> added the surrounding catch for</b> —
        /// its comment calls an origin spawn reported as success "the worst possible shape of failure". The
        /// catch never fired because <b>nothing threw</b>: unbound members are not an error by default.
        /// ⇒ <c>JsonUnmappedMemberHandling.Disallow</c> is what makes that guard real: a payload whose
        /// members do not bind now RAISES, and the existing catch refuses the spawn instead of misplacing
        /// the entity.</para>
        ///
        /// <para>⭐ <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> so the documented
        /// lower-case example binds as written, rather than the caller having to discover the field
        /// casing.</para>
        /// </remarks>
        internal static readonly JsonSerializerOptions SpawnTransformJsonOptions = new()
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling      = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };

        /// <c>POST /entities/spawn</c> — creates an entity THROUGH THE NODE'S CREATION PACK
        /// (<see cref="Hrot.Common.EntityCreation.EntityCreation.RequestEntityCreation"/>), exactly as every other author
        /// does. Returns <c>awaited</c> per the wait rule. Must run on the main thread.
        /// <para>⭐ <c>CE-515</c> ③ (approved <c>2026-10-02</c>, R-174 one creation path): it published a raw
        /// <see cref="SpawnEntityCommand"/>, which bypassed the request system and therefore the ownership GRANT — an
        /// entity spawned this way was owned by nobody's roles. ⭐ <c>ownerNodeId = 0</c> now means <b>this node</b>
        /// (the node you are talking to creates and owns it, then grants role groups as any creator does), keeping
        /// "spawn here" — never the request path's "forward to the arbiter".</para>
        /// ⛔ The API contract (params, notes, the <c>CE-292</c> reliable-init knob) is documented ONCE, in the
        /// <c>DebugApiRouteDocs</c> entry for this route — do not restate it here (it would drift). This summary
        /// is for code readers only.
        /// </summary>
        /// <remarks>
        /// ⭐⭐⭐ <c>CE-191</c> — returns <c>(node, error)</c> rather than a bare node, because the two
        /// optional parses below <b>used to be discarded silently</b>. See their comments.
        /// </remarks>
        /// <param name="ownerNodeId">
        /// <c>0</c> (default) ⇒ this node creates and owns it. Another node's id ⇒ the request is routed to that node,
        /// which creates it (as <c>POST /entities/create-request</c> does). ⛔ <b>SUPERSEDED (<c>CE-515</c> ③):</b>
        /// <i>"default 0 ⇒ the spawning node claims NO authority"</i> (<c>CE-269</c>'s experiment knob) — an unowned
        /// entity is not a state push-only ownership can produce.
        /// </param>
        public (JsonNode? Node, string? Error) SpawnEntity(
            long     tkbType,
            JsonNode? transform      = null,
            JsonNode? components     = null,
            string?  attributesJson = null,
            int      ownerNodeId    = 0,
            bool     reliable       = false,
            double   reliableTimeoutSeconds = 0)
        {
            var (creation, noPack) = ResolveEntityCreation();
            if (creation == null) return (null, noPack);

            SimTransform? initialTransform = null;
            List<object>? initialComponents = null;

            // Parse optional transform.
            //
            // ⭐⭐⭐ CE-191 — this was `catch { /* ignore malformed transform */ }`. A body whose transform
            //   did not deserialize spawned the entity AT THE ORIGIN and answered ok:true. For a spatial
            //   simulation that is the worst possible shape of failure: the caller is told the thing exists
            //   where they asked, and it is somewhere else. Refuse instead.
            if (transform != null)
            {
                try
                {
                    initialTransform = JsonSerializer.Deserialize<SimTransform>(
                        transform.ToJsonString(), SpawnTransformJsonOptions);
                }
                catch (Exception ex)
                {
                    return (null,
                            $"'transform' could not be read as a SimTransform: {ex.GetType().Name}: {ex.Message}. "
                          + "Nothing was spawned — it would otherwise have been placed at the origin and "
                          + "reported as success.");
                }
            }

            // Parse optional extra components list (array of { type, data } or typed objects).
            //
            // ⭐⭐⭐ CE-191 — this block had FOUR silent drops, and the entity spawned WITHOUT the component
            //   you asked for while the response said ok:true:
            //     ① a null array entry            → `continue`
            //     ② an entry with no "type"       → the `if (typeName != null)` never fired
            //     ③ an UNKNOWN type name          → `compType != null` was false ⇒ **a typo'd component
            //                                        vanished**, which is the one a caller is most likely
            //                                        to hit and least able to notice
            //     ④ a payload that would not bind → `catch { }`
            //   ⛔ All four now refuse the whole spawn. A partially-built entity is worse than none: the
            //   caller goes on to assert against a thing that is quietly missing half its state.
            if (components is JsonArray compArr && compArr.Count > 0)
            {
                initialComponents = new List<object>();
                for (int i = 0; i < compArr.Count; i++)
                {
                    JsonNode? item = compArr[i];
                    if (item is null)
                        return (null, $"'components[{i}]' is null; expected {{ \"type\": \"<name>\", \"data\": {{…}} }}.");

                    string? typeName = item["type"]?.GetValue<string>();
                    JsonNode? data   = item["data"];

                    if (string.IsNullOrWhiteSpace(typeName))
                        return (null,
                                $"'components[{i}]' has no 'type'. Expected "
                              + "{ \"type\": \"<component type name>\", \"data\": {…} }.");

                    var compType = ComponentTypeRegistry.GetAllTypes()
                        .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));

                    if (compType == null)
                        return (null,
                                $"'components[{i}].type' names '{typeName}', which is not a registered "
                              + "component type. Nothing was spawned — it would otherwise have been created "
                              + "without that component and reported as success. Read GET /components for "
                              + "the registered names.");

                    if (data == null)
                        return (null, $"'components[{i}]' ({typeName}) has no 'data' object to deserialize.");

                    object? compObj;
                    // CE-3191: the API's own patch options — fields bound (components are field structs), vectors as arrays or
                    //   objects (HN-002), enums by name. Default options bound no field, so every component spawned at default.
                    try { compObj = JsonSerializer.Deserialize(data.ToJsonString(), compType, DebugApiPatchOptions); }
                    catch (Exception ex)
                    {
                        return (null,
                                $"'components[{i}].data' does not bind to {typeName}: "
                              + $"{ex.GetType().Name}: {ex.Message}. Nothing was spawned.");
                    }

                    if (compObj == null)
                        return (null, $"'components[{i}].data' deserialized to null for {typeName}. Nothing was spawned.");

                    initialComponents.Add(compObj);
                }
            }

            // ⭐ CE-515 ③ — through the pack: the request system orders the spawn and the grant together.
            int owner = ownerNodeId == 0 ? creation.NodeId : ownerNodeId;
            Guid requestId = creation.RequestEntityCreation(
                tkbType,
                transform:             initialTransform,
                initialComponents:     initialComponents,
                owner:                 owner,
                // ⭐ CE-292 — reliable=true engages the cross-node construction barrier (§3b). The creator holds
                //   the entity Constructing until the capability-filtered peers (advertising fdp.reliable-init)
                //   report Active, or the timeout aborts via EntityMaster dispose. ⚠ The request's default is
                //   AllPeers; this route keeps its own default (no wait) explicitly.
                initType:              reliable ? ReliableInitType.AllPeers : ReliableInitType.None,
                initialAttributesJson: attributesJson,
                reliableInitTimeout:   (reliable && reliableTimeoutSeconds > 0)
                                           ? TimeSpan.FromSeconds(reliableTimeoutSeconds)
                                           : (TimeSpan?)null);

            bool timeAdvancing = TimeAdvancing();
            return (new JsonObject
            {
                ["spawned"]     = true,
                ["tkbType"]     = tkbType,
                ["reliable"]    = reliable,
                ["requestId"]   = requestId.ToString(),
                ["ownerNodeId"] = owner,
                ["awaited"]  = false,
                ["reason"]   = timeAdvancing ? null : (JsonNode?)"sim not running — time only advances in preview while unpaused; call POST /preview/enter then POST /sim/play, or POST /sim/step to advance.",
            }, null);
        }

        /// <summary>The active perspective's creation pack — the one creation path both creation routes go through
        /// (<c>CE-515</c>).</summary>
        private (Hrot.Common.EntityCreation.EntityCreation? Creation, string? Error) ResolveEntityCreation()
        {
            var world    = _editorWorld ?? _dispatcher?.World;
            var creation = world is null ? null : _entityCreationGetter?.Invoke(world);
            return creation != null
                ? (creation, null)
                : (null,
                   "The active perspective's node has no entity-creation pack wired into the debug API (a node "
                 + "with no ECS world, or one not yet initialised). Select an ECS node's perspective: every node "
                 + "that builds EntityCreationPack (SimHost, CGF, IG, Stride, the editor) exposes one. Nothing was created.");
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-271</c> seam ⑤ — <c>POST /entities/create-request {tkbType, ownerNodeId, transform?,
        /// attributesJson?}</c>. Publishes an <see cref="Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest"/> on the
        /// node's bus (<c>CE-3228</c>), so it flows through the real request path
        /// (<c>ForwardingEntityCreationRequestSource</c> → <c>CreateEntityRequestSystem</c>).
        ///
        /// <para>With <c>ownerNodeId</c> = this node, the node creates + owns the entity and grants its role groups;
        /// with <c>ownerNodeId = 0</c> the request is forwarded to the broadcast arbiter. ⭐ <c>CE-515</c> ③:
        /// <see cref="SpawnEntity"/> goes through the same pack now — the only difference is that its
        /// <c>ownerNodeId = 0</c> means THIS node.</para>
        /// </summary>
        public (JsonNode? Node, string? Error) CreateEntityViaRequestPath(
            long      tkbType,
            int       ownerNodeId,
            JsonNode? transform      = null,
            string?   attributesJson = null)
        {
            var (creation, noPack) = ResolveEntityCreation();
            if (creation == null) return (null, noPack);
            Action<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> enqueue = creation.Publish;

            List<object>? initialComponents = null;
            if (transform != null)
            {
                try
                {
                    var simTransform = JsonSerializer.Deserialize<SimTransform>(
                        transform.ToJsonString(), SpawnTransformJsonOptions);
                    initialComponents = new List<object> { simTransform };
                }
                catch (Exception ex)
                {
                    return (null,
                        $"'transform' could not be read as a SimTransform: {ex.GetType().Name}: {ex.Message}. "
                      + "Nothing was enqueued.");
                }
            }

            var request = new Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest
            {
                RequestId             = Guid.NewGuid(),
                OwnerAppInstanceId    = ownerNodeId,
                TkbType               = tkbType,
                InitialAttributesJson = attributesJson,
                InitialComponents     = initialComponents,
            };

            enqueue(request);
            return (new JsonObject
            {
                ["enqueued"]           = true,
                ["requestId"]          = request.RequestId.ToString(),
                ["ownerAppInstanceId"] = ownerNodeId,
                ["tkbType"]            = tkbType,
                ["note"]               = "Routed through CreateEntityRequestSystem. Advance the sim to process; "
                                       + "read back with GET /entities on each node to see ownership distribution.",
            }, null);
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
            // ⭐⭐ BP-487 — resolve through the ACTIVE PERSPECTIVE on a cluster host, exactly as
            //    GetGizmoFrame does. ⚠ An annotation drawn into a buffer no host renders would be a
            //    silent no-op that reported success.
            var buffer = _gizmoFeed;
            if (buffer is null)
                // ⭐ CE-259am — the reason, not a claim about the host. See GizmoFeedAbsenceReason.
                return (null, "Nothing to draw into: " + GizmoFeedAbsenceReason());

            if (body is null)
                return (null, "Request body is required.");

            var type = body["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type))
                return (null, "'type' field is required (sphere | anchor | line).");

            int countBefore = buffer.Count;

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
                        buffer.DrawSphere(new System.Numerics.Vector3(x, y, z), radius, color);
                        break;
                    }
                    case "anchor":
                    {
                        long  netId   = body["networkId"]?.GetValue<long>()    ?? 0L;
                        float x       = body["x"]?.GetValue<float>()           ?? 0f;
                        float y       = body["y"]?.GetValue<float>()           ?? 0f;
                        float z       = body["z"]?.GetValue<float>()           ?? 0f;
                        float heading = body["heading"]?.GetValue<float>()     ?? 0f;
                        buffer.DrawSpatialAnchor(netId, x, y, z, heading);
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
                        buffer.DrawLine(
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

            int countAfter = buffer.Count;
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

        internal static JsonNode DumpToJsonNode(EntityStateDumpDto dump)
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
        /// <para><b>Reuse, not a new registry.</b> <see cref="BehaviorDefinition.JsonParamsDtoType"/>
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

                // ⭐⭐ CE-066 — the RESOLVED editor, so a cluster host gets the same exact-parity list the
                //    editor gets instead of silently falling through to the TKB catalog below. ⚠ The
                //    fallback is a correct answer, just a COARSER one — which is precisely the kind of
                //    quiet downgrade that made this seam worth routing.
                if (_missionEditor is not null)
                {
                    names = _missionEditor.GetAvailableBehaviors(entityId.Value);
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
                    // CE-226: pass the DEFINITION, not just the DTO type — a generated behaviour has no DTO
                    // struct and describes its parameters through ManagedBlackboardVariables instead.
                    ["paramSchema"] = DtoJsonSchemaExtractor.ExtractParams(definition),
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

        /// <summary>
        /// GET /tkb/resolve?type= — ⭐ buildings programme Stage 0: every combat/perception parameter of the type with its
        /// value and PROVENANCE (Explicit · Generated(formula) · EngineFallback · NotApplicable), from the same
        /// <see cref="Fdp.Toolkit.Tkb.Parameters.ParameterResolver"/> rules the translators and the fire chain use.
        /// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §4.
        /// </summary>
        public JsonNode? ResolveTkbParameters(long tkbType)
        {
            if (!_tkbDb.TryGetByType(tkbType, out var t))
                return null;
            var arr = new JsonArray();
            int fallbacks = 0;
            foreach (var p in Fdp.Toolkit.Tkb.Parameters.ParameterResolver.ResolveAll(t, _tkbDb))
            {
                if (p.Provenance == Fdp.Toolkit.Tkb.Parameters.ParameterProvenance.EngineFallback) fallbacks++;
                arr.Add(new JsonObject
                {
                    ["name"]       = p.Name,
                    ["value"]      = p.Value is float v ? JsonValue.Create(v) : null,
                    ["provenance"] = p.Provenance.ToString(),
                    ["source"]     = p.Source,
                });
            }
            return new JsonObject
            {
                ["tkbType"]         = t.TkbType,
                ["name"]            = t.Name,
                ["disType"]         = t.DisType.ToString(),
                ["engineFallbacks"] = fallbacks,
                ["parameters"]      = arr,
            };
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
                // ⭐⭐ CE-191 — this used to `catch { skip }`, which SHORTENED THE ARRAY silently: a caller
                //   counting child blueprints got a wrong number with nothing to indicate it. The row now
                //   stays and names its own failure.
                try
                {
                    var json = EventSerializationHelper.SerializeToJson(cb);
                    childArr.Add(JsonNode.Parse(json));
                }
                catch (Exception ex)
                {
                    childArr.Add(new JsonObject
                    {
                        ["serializationError"] = $"{ex.GetType().Name}: {ex.Message}",
                    });
                }
            }

            // Descriptor bag
            var descrArr = new JsonArray();
            foreach (var (type, partId, data) in t.GetAllDescriptors())
            {
                // ⭐⭐ CE-191 — same as the child blueprints above: skipping made the descriptor bag SHORT
                //   with no way to tell an absent descriptor from an unserializable one. The row survives
                //   and carries its own error.
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
                catch (Exception ex)
                {
                    descrArr.Add(new JsonObject
                    {
                        ["type"]   = type.Name,
                        ["partId"] = partId,
                        ["data"]   = null,
                        ["serializationError"] = $"{ex.GetType().Name}: {ex.Message}",
                    });
                }
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

        /// <summary>
        /// GET /world/info — geo origin, the spatial grids, the resident terrain and the navmesh, read LIVE from the
        /// active perspective's world. ⭐ CE-3028: <c>terrain</c>/<c>navmesh</c> were hard-coded null, and
        /// <c>spatialGrid</c> reported composition constants that CE-3018's terrain rebase had made wrong.
        /// </summary>
        public JsonNode GetWorldInfo() => WorldInfoReport.Build(_world, GeoTransform.Origin);

        /// <summary>GET /terrain/levels — ⭐ buildings Stage 1: the terrain levels at a point (ground = 0).</summary>
        public JsonNode GetTerrainLevels(float x, float y) => TerrainReport.Levels(_world, x, y);

        /// <summary>GET /terrain/query — ⭐ buildings Stage 1: a sight trace with every crossed occluder (dry run).</summary>
        public JsonNode QueryTerrain(System.Numerics.Vector3 from, System.Numerics.Vector3 to) => TerrainReport.Query(_world, from, to);

        /// <summary>GET /terrain/query?purpose=fire — ⭐ R-217: a round of the given penetration/damage carried through the terrain.</summary>
        public JsonNode QueryTerrainFire(System.Numerics.Vector3 from, System.Numerics.Vector3 to, float penetration, float damage)
            => TerrainReport.QueryFire(_world, from, to, penetration, damage);

        /// <summary>GET /combat/shots — ⭐ tuning T-4: the last rounds fired on this node, as the combat systems recorded them.</summary>
        public JsonNode GetShots(int last, long? shooter, long? target)
            => CombatReport.Shots(_world, _editorEntityMap ?? _dispatcher?.EntityMap, last, shooter, target);

        /// <summary>GET /combat/detonations — ⭐ CE-1032 (W-10): the last warhead bursts on this node, as the area effect decided them.</summary>
        public JsonNode GetDetonations(int last)
            => CombatReport.Detonations(_world, _editorEntityMap ?? _dispatcher?.EntityMap, last);

        /// <summary>GET /perception/los — ⭐ tuning T-4: why one unit does (not) see another, as this node's perception decides it.</summary>
        public JsonNode ExplainLos(long observer, long target)
            => CombatReport.Los(_world, _editorEntityMap ?? _dispatcher?.EntityMap, observer, target);

        /// <summary>GET /perception/hearing — ⭐ AQ106 L: what a unit could hear in the last seconds and why not, as this node's hearing decides it.</summary>
        public JsonNode ExplainHearing(long listener, double seconds)
            => CombatReport.Hearing(_world, _editorEntityMap ?? _dispatcher?.EntityMap, listener, seconds);

        /// <summary>GET /doors — ⭐ buildings Stage 1: the doors the terrain defines.</summary>
        public JsonNode GetDoors() => TerrainReport.Doors(_world);

        /// <summary>POST /world/geo-to-local — convert geodetic to local ENU coordinates.</summary>
        public JsonNode GeoToLocal(double lat, double lon, double alt, float? headingDeg)
        {
            var pos = GeoTransform.ToCartesian(lat, lon, alt);
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
            var (lat, lon, alt) = GeoTransform.ToGeodetic(new Vector3(x, y, z));
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
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(
                    Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);

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

            // ⭐ CE-1323 (DBG-9) — a behaviour step is checked against the behaviour NOW: a state / event / transition name that is not in it
            //   is a 400 that lists what is (it would otherwise never fire, silently)
            Guid? element = null;
            if (StepIn(condition) is { } step)
            {
                if (string.IsNullOrWhiteSpace(step.Behaviour))
                    throw new ArgumentException("a BehaviourStep condition needs 'behaviour' (the running behaviour's name, GET /entities/{id}/brain).");
                if (_behaviorRegistry is { } registry
                    && Hrot.Editor.Debugging.BehaviourStepResolver.Resolve(step, registry, Fdp.Toolkit.Behavior.BehaviorHash.FromName(step.Behaviour), out var why) is null)
                    throw new ArgumentException($"BehaviourStep cannot match: {why}.");
                if (string.IsNullOrEmpty(name)) name = BreakpointConditionSummarizer.Step(step);
                if (step.Element != Guid.Empty) element = step.Element;
                else if (step.Node != Guid.Empty) element = step.Node;
            }

            var id = _bpManager.AddBreakpoint(condition, filterEntity, occurrenceThreshold, name, element);

            return new JsonObject
            {
                ["breakpointId"]     = id.ToString(),
                ["conditionSummary"] = BreakpointConditionSummarizer.Summarize(condition),
            };
        }

        private string? _lastHitName, _lastHitSummary, _lastHitWhat;

        private static BehaviourStepPredicateDto? StepIn(SearchPredicateDto c) => c switch
        {
            BehaviourStepPredicateDto st => st,
            CompoundPredicateDto cp      => cp.Conditions.OfType<BehaviourStepPredicateDto>().FirstOrDefault(),
            _                            => null,
        };

        /// <summary>GET /breakpoints — list all registered breakpoints.</summary>
        public JsonNode ListBreakpoints()
        {
            if (_bpManager is null)
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(
                    Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);

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
                    ["kind"]                = BreakpointKind.Of(bp).ToString(),   // ⭐ CE-1323 — Node / Journal / Data
                    ["filterNetworkId"]     = bp.FilterEntity is { } fe && _entityMap.TryGetNetworkId(fe, out var fnid) ? (JsonNode?)fnid : null,
                });
            }
            return arr;
        }

        /// <summary>DELETE /breakpoints/{id} — remove a breakpoint by its string id (e.g. "BP#3").</summary>
        public void RemoveBreakpoint(string idStr)
        {
            if (_bpManager is null)
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(
                    Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);

            BreakpointId id = ParseBreakpointId(idStr);
            _bpManager.Remove(id);
        }

        /// <summary>GET /breakpoints/hits — current pause state + last hit info.</summary>
        public JsonNode GetBreakpointStatus()
        {
            if (_bpManager is null)
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(
                    Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);

            JsonNode? lastHit = null;
            if (_lastHitBreakpointId.IsValid)
            {
                lastHit = new JsonObject
                {
                    ["breakpointId"]     = _lastHitBreakpointId.ToString(),
                    ["networkId"]        = _lastHitNetworkId,
                    ["name"]             = _lastHitName,
                    ["conditionSummary"] = _lastHitSummary,
                    ["what"]             = _lastHitWhat,   // ⭐ CE-1323 — what a behaviour step saw ("entered Withdrawing"); null for data breakpoints
                };
            }

            // ⭐ CE-1258 (BV-29) — what the hit tick wrote to the unit's brain journal (the world shown is the tick's START)
            if (lastHit is JsonObject hit && _bpManager.IsPaused && _entityMap.TryGetEntity(_lastHitNetworkId, out var hitEntity)
                && JournalWrittenByTheHitTick(hitEntity) is { } written)
                hit["journalWrittenByTheTick"] = written;

            return new JsonObject
            {
                ["isPaused"]   = _bpManager.IsPaused,
                ["pausedTick"] = _bpManager.PausedTick,
                ["lastHit"]    = lastHit,
            };
        }

        private BreakpointId ParseBreakpointId(string idStr)
        {
            if (_bpManager is null)
                throw new Hrot.Presentation.DebugApi.NotSupportedHereException(
                    Hrot.Presentation.DebugApi.DebugCapabilities.Breakpoints);
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

            // ⭐⭐⭐ CE-476 D5/D6 — a Behavior-dispatch blueprint (BrainTier 3): the Blueprint debug session's own snapshot
            //    (CaptureLiveBehaviorState — the same decode and the same snapshot type as an Instance blueprint).
            //    📄 docs/blueprints/DESIGN_Cluster_Ai_Debug_Surface.md. 🔴 This was a stub that named no asset and no
            //    state, on every host. ⚠ There is no "active graph" to report: a Behavior blueprint ticks its one root
            //    graph, and its latent cursor records only a resume index — reported as resumeAt.
            if (tier == Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBlueprint)
            {
                var behaviour = _blueprintSession?.CaptureLiveBehaviorState(entity);
                if (behaviour is null)
                {
                    return new JsonObject
                    {
                        ["networkId"] = networkId,
                        ["tier"]      = "Blueprint",
                        ["dispatch"]  = "Behavior",
                        ["note"]      = _blueprintSession is null
                            ? "No blueprint debug session on this node, so the blueprint behaviour cannot be decoded."
                            : "The blueprint behaviour is not in this node's registry, or its root block is absent.",
                    };
                }

                var variables = new JsonObject();
                foreach (var (name, value) in behaviour.FieldValues)
                    variables[name] = ToJson(value);

                return new JsonObject
                {
                    ["networkId"]     = networkId,
                    ["tier"]          = "Blueprint",
                    ["dispatch"]      = "Behavior",
                    ["behavior"]      = behaviour.AssetName,
                    ["resumeAt"]      = behaviour.Cursor?.ResumeAt,
                    ["waitUntilTime"] = behaviour.Cursor?.WaitUntilTime,
                    ["variables"]     = variables,
                    ["nodeHistory"]   = BlueprintNodeHistory(entity),
                };
            }

            // ⭐ CE-476 — an Instance blueprint attached to the entity: name what it carries (the stub named nothing).
            var attached = AttachedBlueprints(entity);
            if (attached.Count > 0)
            {
                var assets = new JsonArray();
                foreach (var slot in attached)
                    assets.Add(new JsonObject { ["asset"] = slot.Name, ["assetId"] = slot.AssetId.ToString("D") });
                return new JsonObject
                {
                    ["networkId"]   = networkId,
                    ["tier"]        = "Blueprint",
                    ["dispatch"]    = "Instance",
                    ["assets"]      = assets,
                    ["nodeHistory"] = BlueprintNodeHistory(entity),
                };
            }

            return new JsonObject { ["networkId"] = networkId, ["tier"] = "unknown" };
        }

        /// <summary>
        /// The Blueprint session's recent node entries for <paramref name="entity"/>. ⚠ Probes fire only in an
        /// INSTRUMENTED build, so an empty array is the normal answer for a release-compiled blueprint.
        /// </summary>
        private JsonArray BlueprintNodeHistory(Entity entity)
        {
            var history = new JsonArray();
            if (_blueprintSession is null) return history;
            foreach (var h in _blueprintSession.GetNodeHistory(entity, 50))
                history.Add(new JsonObject { ["nodeId"] = h.NodeId, ["tick"] = h.Tick, ["timestamp"] = h.SimTime });
            return history;
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
        /// Unregistered keys are silently ignored (a mixed-version sender is not an error).
        /// Must run on the main thread.
        ///
        /// <para>⭐⭐⭐ <b><c>CE-3003</c> — a write this node does not own is ASKED of the owner, never dropped.</b>
        /// 📐 Measured live <c>2026-10-02</c>: on a non-owner the compile skipped every key at the authority gate,
        /// the route answered with the unchanged dump, and no node sent an <c>UpdateEntityAttributeRequest</c> —
        /// so §5.7 E4/E6 (an edit on a non-owner applied by the owner) could not be driven.
        /// ⭐ The rule is the drag gizmo's (<c>AX-007</c>, <see cref="Fdp.Toolkit.Replication.Attributes.EntityWriteRouter"/>):
        /// apply locally what this node owns, and send the rest to the owner as a request. ⭐ It rides the JSON arm
        /// of the request the owner already applies (<c>UpdateEntityAttributeRequestSystem</c>) — no new message
        /// (<c>R-158</c>).</para>
        ///
        /// <para>⭐ <b>The response says which happened</b> — <c>write.route</c> is <c>direct</c>, <c>requested</c>
        /// or <c>noMatch</c> — because <i>"it landed"</i> and <i>"the owner will apply it"</i> are different outcomes
        /// to whoever called (<see cref="Fdp.Toolkit.Replication.Patching.EntityWriteRoute"/>). ⚠ A <c>requested</c>
        /// write is not yet in the dump: read it back from the owner, or again after a tick.</para>
        ///
        /// <para>⛔ <c>CE-191</c>: a write that cannot be honoured REFUSES. A refused component that no network
        /// descriptor covers has no remote owner to ask (a networkless host, or a node-local component), and an
        /// entity with no network identity cannot be addressed — both answer an error, never <c>ok</c>.</para>
        /// </summary>
        public (JsonNode? result, string? error) PatchEntityAttribute(long networkId, string? patchJson)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.");

            if (string.IsNullOrWhiteSpace(patchJson))
                return (null, "patchJson is required.");

            EcsPatchContext ctx;
            try
            {
                ctx = _attributeCompiler.CreatePatchContext(_world, entity);
                _attributeCompiler.Compile(patchJson, ctx);
                ctx.FlushDirtyMarks();
            }
            catch (Exception ex)
            {
                return (null, $"Attribute patch failed: {ex.Message}");
            }

            string route = ctx.HasRefusedAny ? "requested" : ctx.HasAppliedAny ? "direct" : "noMatch";
            if (ctx.HasRefusedAny)
            {
                var map = OwnershipDescriptorMap();
                var unaddressable = ctx.RefusedComponentIds.Where(cid => !map.IsBoundToAnyDescriptor(cid)).ToList();
                if (unaddressable.Count > 0)
                    return (null,
                        $"Entity {networkId}: this node does not own component(s) {string.Join(", ", unaddressable.Select(ComponentLabel))} " +
                        "and no network descriptor carries them, so there is no owner to ask. Nothing was requested." +
                        (ctx.HasAppliedAny ? " The components this node owns WERE applied." : ""));

                if (!Fdp.Toolkit.Replication.Attributes.EntityAttributeChangeRequests.TryPublishJsonPatch(_world, entity, patchJson!))
                    return (null,
                        $"Entity {networkId} has no network identity on this node, so its owner cannot be addressed. Nothing was requested." +
                        (ctx.HasAppliedAny ? " The components this node owns WERE applied." : ""));
            }

            // Return the updated entity dump, with how the write was routed.
            var node = DumpEntity(networkId);
            if (node is JsonObject obj)
            {
                var applied   = new JsonArray();
                foreach (var cid in ctx.AppliedComponentIds) applied.Add(ComponentLabel(cid));
                var requested = new JsonArray();
                foreach (var cid in ctx.RefusedComponentIds) requested.Add(ComponentLabel(cid));
                obj["write"] = new JsonObject
                {
                    ["route"]               = route,
                    ["appliedComponents"]   = applied,
                    ["requestedComponents"] = requested,
                };
            }
            return (node, null);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3003</c> — the map that answers <i>"does a network descriptor carry this component, so another
        /// node can own it?"</i>: the active node's NED ownership map (the one <c>GET …/ownership</c> reads), falling
        /// back to the world's map on a host with no NED transport.
        /// <para>📐 ⛔ NOT the world map alone. Measured on the IG: <c>AttributeInterpreterProvider.GetDescriptorMap</c>
        /// is contributed by <c>CycloneEgressSystem</c> from EGRESS translators only, and a pure IG publishes no
        /// <c>WorldPos</c> — so the world map says <c>SimTransform</c> is not networked at all, which is right for
        /// <i>"what do I republish"</i> and wrong for <i>"who owns it"</i>. ⭐ Asked through
        /// <c>DescriptorOwnershipMap.IsBoundToAnyDescriptor</c>, the forward table, for the same reason.</para>
        /// </summary>
        private Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap OwnershipDescriptorMap()
            => _dispatcher?.DescriptorMap
               ?? Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.GetDescriptorMap(_world);

        /// <summary>A component id rendered as its type name when registered, else the bare id.</summary>
        private static string ComponentLabel(int componentId)
            => ComponentTypeRegistry.GetType(componentId)?.Name ?? componentId.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

            // ⭐⭐ CE-3003 — ⛔ never write another node's component into this node's REPLICA. 📐 That write is
            //   overwritten by the owner's next sample and reaches nobody, so answering it with the edited dump
            //   would be CE-191's "ok about work that was discarded". ⭐ A component a network descriptor carries
            //   and this node does not claim is another node's (one ownership truth: claim == record) ⇒ refuse,
            //   naming the route that DOES reach the owner. ⚠ A component no descriptor carries is node-local and
            //   is written here as before — so a networkless host (editor) is unaffected.
            int componentTypeId = ComponentTypeRegistry.GetId(clrType);
            var descriptorMap = OwnershipDescriptorMap();
            if (componentTypeId >= 0
                && descriptorMap.IsBoundToAnyDescriptor(componentTypeId)
                && !_world.HasAuthority(entity, componentTypeId))
                return (null,
                    $"Entity {networkId}: component '{clrType.Name}' is owned by another node, so editing it here would " +
                    "change only this node's replica and be overwritten by the owner. Nothing was written. Use " +
                    "POST /entities/{networkId}/attribute, which asks the owner, or select the owner's perspective " +
                    "(GET /entities/{networkId}/ownership names it).");

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
