using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Hrot.UI.Common.Facades;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>ONE SUBSYSTEM'S DEBUG SURFACE — what it can be READ from and DRIVEN through.</b>
/// 📄 <c>docs/blueprints/Architect_Question_54_Cluster_Mcp_Contract.md</c> *(RESOLVED)* — Q54-2 Option B:
/// perspective-scoped dispatch over per-subsystem providers.
///
/// <para>⛔⛔ <b>Why per-subsystem and NOT one <c>ClusterReadDriveService</c>.</b> 📌 Q54: editor-only
/// features do not stay editor-only — they MIGRATE into the subsystems *(charter <c>D3</c>)*, and a single
/// frozen cluster service would have to be re-split every time one lands. ⇒ ⭐ each subsystem contributes
/// its own read+drive surface as its features arrive.</para>
///
/// <para>⭐⭐ <b>Almost everything here already existed.</b> The role-correct *"how do I step"* is each
/// slave's own <see cref="ITimeTransportFacade"/> — <c>ClusterTimeTransportAdapter</c> on a slave
/// *(publishes <c>StepTimeIntent</c> → DDS → the master)*, the direct facade in the editor. ⇒ ⛔ this
/// interface introduces no new stepping mechanism; it selects the existing one **by active
/// perspective**.</para>
///
/// <para>⚠ <b>Nullable members are the point, not sloppiness</b> *(charter <c>D3</c>: the lifted API accepts
/// absent capabilities)*. A subsystem with no ECS world returns <see langword="null"/> for
/// <see cref="World"/>; one that cannot drive time returns <see langword="null"/> for <see cref="Drive"/>.
/// ⛔ The dispatcher then answers <c>NOT_SUPPORTED_HERE</c> — it does NOT fabricate an empty world, which
/// would be the false green <c>D4</c> exists to kill.</para>
/// </summary>
/// <summary>
/// ⭐⭐ <b><c>MD-007</c> — what a node can say about the last cluster diagnostic dump.</b>
///
/// <para>⛔⛔ <b>PRIMITIVES, not the cache object</b>, and that is the established pattern here:
/// <see cref="ISubsystemDebugProvider.ClusterState"/> and <c>AvailableScenarios</c> are also projected out
/// of <c>ClusterUiCache</c> rather than exposing it. ⇒ <c>Hrot.Presentation</c> needs no reference to
/// <c>Hrot.Orchestrator</c>.</para>
///
/// <para>⚠ <paramref name="ManifestPaths"/> is lossless: the cached manifest carries ONLY
/// <c>FileManifestEntry.RelativeDest</c> — its <c>SourceUnc</c> is stripped before caching, which the
/// cache's own doc-comment states.</para>
/// </summary>
/// <param name="InFlight">A cluster transaction is open right now.</param>
/// <param name="ManifestPaths">
/// Destination paths of the files the most recent SUCCESSFUL dump produced, relative to the NAS base.
/// ⚠ EMPTY until the first successful dump completes — ⛔ empty is "none yet", not "it failed".
/// </param>
public sealed record DiagnosticDumpStatus(bool InFlight, IReadOnlyList<string> ManifestPaths);

public interface ISubsystemDebugProvider
{
    /// <summary>⭐ The subsystem's own name — <c>ISubsystem.Name</c>, e.g. <c>"CGF"</c>. For diagnostics and the manifest.</summary>
    string SubsystemName { get; }

    /// <summary>
    /// ⭐⭐ <b>The PERSPECTIVE this provider answers for</b> — the finer key
    /// *(<c>DESIGN_Perspective_Unification.md</c> §1b)*.
    /// <para>⚠ It is NOT always the subsystem name: 📐 CGF's perspective is <c>"Scenario"</c>, the one entry
    /// in <c>perspectiveMap</c> whose key and value differ.</para>
    /// </summary>
    string Perspective { get; }

    /// <summary>⭐ The subsystem's authoritative entity repository, or <see langword="null"/> when it has none.</summary>
    EntityRepository? World { get; }

    /// <summary>⭐ Its network-id → entity map, or <see langword="null"/>.</summary>
    NetworkEntityMap? EntityMap { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>The role-correct drive seam.</b> On a slave this is the subsystem's own
    /// <c>ClusterTimeTransportAdapter</c>, so a step issued here travels the SAME path the operator's
    /// button does: <c>StepTimeIntent</c> → its own event bus → DDS → the master.
    /// <para>⛔ <see langword="null"/> when this subsystem cannot drive time.</para>
    /// </summary>
    ITimeTransportFacade? Drive { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-487</c> — THIS NODE'S MAP FEED: the debug primitives it submits for drawing.</b>
    /// 📄 <c>DESIGN_UI_Observability_Snapshot.md</c> STATUS ③ *(the finding, verbatim: "the gizmo publish
    /// [has] ONE production caller … while four other hosts drive a gizmo buffer — harmless while the debug
    /// API is Editor-only, <b>blocking for cross-host conformance</b>")* ·
    /// <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.6 *(the classDiagram this member is drawn in)*.
    ///
    /// <para>⛔⛔ <b>Why it belongs HERE and not as a <c>DebugApiService</c> field.</b> 📐 The editor passes
    /// its buffer straight to the ctor *(<c>EditorSubsystem.cs:1901</c>)* because the editor IS one node.
    /// ⚠ <c>--mode all</c> runs CGF <b>and</b> IG <b>and</b> SimHost, each with its own buffer and its own
    /// map ⇒ a single latched buffer would answer for whichever host happened to be constructed, ⭐ so the
    /// feed must follow the ACTIVE PERSPECTIVE exactly as <see cref="World"/> and <see cref="Drive"/> do.
    /// 📌 That is the whole reason this interface exists.</para>
    ///
    /// <para>⛔ <see langword="null"/> when the subsystem draws no gizmos — 📐 measured `2026-08-27`:
    /// <b>ExCon</b> builds no buffer, so it reports the feed ABSENT rather than an empty one (ruling 49:
    /// absent-and-explained beats present-and-broken). ⭐ CGF, IG and SimHost all have one.</para>
    ///
    /// <para>⚠⚠ <b>What it does NOT reach:</b> the primitives SUBMITTED for drawing, ⛔ never what a human
    /// sees — no rasterisation, no picking, no ImGui hit-testing
    /// *(<c>DESIGN_Subsystem_Composition_Unification.md</c> §5.4)*.</para>
    /// </summary>
    Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? GizmoBuffer { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-066</c> — THIS NODE'S MISSION EDITOR: the seam <c>/missions/*</c> commits through.</b>
    /// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.9.
    ///
    /// <para>🔴 <b>The third instance of one defect in a single batch, and it is the same shape as
    /// <see cref="GizmoBuffer"/> above.</b> 📐 Measured `2026-08-27`: <c>CgfSubsystem</c> constructs the
    /// <b>same shared</b> <c>ScenarioMissionService</c> the editor does *(`:1095` vs
    /// `EditorSubsystem:1962`)*, but only the editor hands its instance to the debug service
    /// *(`EditorSubsystem:1967`)*. ⇒ ⛔ all four <c>/missions</c> routes answered *"no mission service"* on
    /// <c>--mode all</c> — while the host had one the whole time.</para>
    ///
    /// <para>⚠⚠ <b>And <c>DebugApiService.MissionService</c>'s own doc-comment states the rule it was
    /// breaking:</b> <i>"the composition root hands it over as soon as it exists. Leaving it null would be
    /// the silent-default trap — a caller that HAS the dependency must pass it."</i> 📌 The comment was
    /// written for the editor and the cluster root never read it.</para>
    ///
    /// <para>⛔ <see langword="null"/> where the node hosts no mission editing — measured: IG, SimHost and
    /// ExCon build none, so <c>mission.edit</c> is honestly FALSE for their perspectives.</para>
    ///
    /// <para>⚠ <b>NOT the <c>Hrot.ExCon</c> interface of the same name.</b> 📌 Two distinct
    /// <c>IMissionEditorService</c> types exist; this is <c>Hrot.UI.Common.Facades</c>' one — the port the
    /// editor's Mission panel and <c>DebugApiService.Missions</c> both commit through.</para>
    /// </summary>
    Hrot.UI.Common.Facades.IMissionEditorService? MissionEditor { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>REQUEST A CLUSTER-WIDE STATE TRANSITION from this node</b> — the host-agnostic scenario-load
    /// seam. 📄 <c>MCP_Integration.md</c> § Group U.
    ///
    /// <para>🔒 <b>User, `2026-08-24`:</b> <i>"both should be cluster wide. editor is not special, also uses
    /// 2pc for its single process."</i></para>
    ///
    /// <para>⭐⭐ <b>This introduces NO new mechanism.</b> 📐 Measured: every host already publishes
    /// <c>TransitionStateIntent</c> onto its own orchestration bus, and every one of those buses already
    /// reaches a <c>ClusterMaster</c> — <b>directly</b> where the host owns one *(the orchestrator's
    /// <c>_bus</c>; the editor's own ONE-NODE master on <c>_orchestrationBus</c>)*, or via
    /// <c>ClusterOpEgressTranslator</c> → DDS → <c>ClusterOpMasterTranslator</c> on a slave
    /// *(CGF · SimHost · IG · ExCon all wire one)*. ⇒ ⭐ this member SELECTS that existing seam per
    /// perspective, exactly as <see cref="Drive"/> does for stepping.</para>
    ///
    /// <para>⭐ <b>Deliberately narrower than "the bus".</b> The endpoint needs to request a transition, not
    /// to publish arbitrary events; handing out an <c>FdpEventBus</c> would let the debug host inject
    /// anything into a node's control plane. 📌 The same reasoning that made <c>HN-028</c> expose one
    /// <c>bool?</c> instead of the whole <c>MasterSyncController</c>.</para>
    ///
    /// <para>⛔ <see langword="null"/> when this node cannot request one ⇒ <c>NOT_SUPPORTED_HERE</c>.</para>
    /// </summary>
    Action<TransitionStateIntent>? RequestTransition { get; }

    /// <summary>
    /// ⭐⭐ <b>This node's view of the CLUSTER's state</b> — what the scenario-load readiness gate waits on.
    /// <see langword="null"/> when this node tracks no such view.
    ///
    /// <para>⚠⚠ <b>Unlike every other member here, this is NOT per-perspective data</b> — there is one cluster
    /// and one state. ⭐ It is exposed per provider only because the CQRS read-model that caches it
    /// *(<c>ClusterUiCache</c>, fed by <c>ClusterStateUpdateEvent</c>)* is built by whichever subsystems happen
    /// to render cluster UI. ⇒ see <c>PerspectiveScopedDispatcher.ClusterStateAnyNode</c> for why falling back
    /// to another provider is legitimate HERE and nowhere else.</para>
    /// </summary>
    ClusterState? ClusterState { get; }

    /// <summary>
    /// ⭐ The scenario names this node knows about, or <see langword="null"/> when it tracks none — so
    /// <c>GET /scenarios</c> can answer in a cluster host, not only in the editor.
    /// <para>⚠ Same caveat as <see cref="ClusterState"/>: it is cluster-wide inventory, cached per node.</para>
    /// </summary>
    IReadOnlyList<string>? AvailableScenarios { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-006</c> — publish a CLUSTER-WIDE DIAGNOSTIC DUMP from this node.</b>
    /// 📄 <c>DESIGN_Mcp_Diagnostics_Federation.md</c> §8.5.
    ///
    /// <para>⭐⭐ <b>Identical in shape and mechanism to <see cref="RequestTransition"/>, deliberately.</b>
    /// 📐 <c>ClusterDiagnosticsPanel</c>'s Execute button publishes exactly this intent onto its own
    /// orchestration bus, and every host's bus already reaches a <c>ClusterMaster</c> — directly where it
    /// owns one, or via <c>ClusterOpEgressTranslator</c> → DDS on a slave. ⇒ ⛔ <b>this introduces NO new
    /// collection mechanism</b>: the dump-diag pipeline already fans out and pulls to NAS. It selects the
    /// existing trigger from a second surface.</para>
    ///
    /// <para>⚠ <see langword="null"/> when this subsystem has no orchestration bus.</para>
    /// </summary>
    Action<ExecuteDiagnosticDumpIntent>? RequestDiagnosticDump { get; }

    /// <summary>
    /// ⭐⭐ <b><c>MD-007</c> — the last dump's outcome, from whichever node caches it.</b>
    ///
    /// <para>⭐⭐⭐ <b>The read model is <c>ClusterUiCache</c>, and this is exactly what the panel renders</b>
    /// — 📐 <c>ClusterDiagnosticsPanel.SyncManifestFromCache</c> reads <c>LastDiagnosticManifest</c>, and its
    /// results section shows nothing else. ⛔ It is NOT <c>DiagnosticsDumpProcessManager</c>, which exposes
    /// only <c>Tick()</c>; 📌 measuring that class instead of the panel is what produced a false
    /// *"there is no status read-model"* claim in the first cut of this slice.</para>
    ///
    /// <para>⚠ Same cluster-wide-fact rationale as <see cref="ClusterState"/>: one cluster, one last dump,
    /// so reading it from whichever node keeps a cache answers the same question.</para>
    /// </summary>
    DiagnosticDumpStatus? DumpStatus { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-002</c> — this subsystem's ARCHITECTURE snapshot: its modules, systems and
    /// translators, read from ITS OWN <c>ModuleHostKernel</c>.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §2.2.
    ///
    /// <para>⭐⭐ <b>Per SUBSYSTEM, not per node — and that is a correction the measurement forced.</b>
    /// 📐 The design said *"each node answers for its own kernel"*, but a <c>--mode all</c> node runs
    /// SimHost, IG, CGF and the orchestrator side by side and **each holds its own kernel**. ⇒ one
    /// snapshot per node would have had to pick one and silently drop the rest.</para>
    ///
    /// <para>⚠ <see langword="null"/> for a subsystem with no kernel — 📐 measured: ExCon and the
    /// Orchestrator construct <c>ArchitectureDiagnosticsService(() =&gt; null)</c> precisely because they
    /// have none. ⛔ Same contract as <see cref="World"/> and <see cref="Drive"/>: absence is reported,
    /// not fabricated.</para>
    /// </summary>
    Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService? Architecture { get; }

    /// <summary>
    /// ⭐⭐ <b>What this subsystem CAN do, measured from wired dependencies — never a hand-authored table.</b>
    /// 📄 Q54 § Manifest scope: <i>"each provider DERIVES its own cells from ground truth"</i>; a
    /// hand-written *"works here / not there"* table is `CLAUDE.md` §M's green-and-false rot.
    /// </summary>
    IReadOnlyDictionary<string, bool> DescribeCapabilities();
}

/// <summary>
/// ⭐⭐ <b>A subsystem that can contribute a debug provider.</b> ⭐ Separate from
/// <see cref="ISubsystemDebugProvider"/> so a subsystem exposes its surface without BEING one — the
/// provider is built after <c>Initialize</c>, when the world and the adapter exist.
/// <para>⚠ Returning <see langword="null"/> is legal and means *"nothing to contribute in this
/// configuration"* — 📌 a subsystem may run in a mode where it has no world at all.</para>
/// </summary>
public interface IProvidesDebugSurface
{
    ISubsystemDebugProvider? CreateDebugProvider();
}

/// <summary>
/// ⭐⭐⭐ <b>The plain implementation every subsystem can hand back</b> — a record of what it wired.
/// ⛔ Deliberately dumb: it holds no logic, so a provider cannot lie about a capability it merely intends
/// to have. 📌 The capability cells are computed from the members being non-null, in ONE place.
/// </summary>
public sealed class SubsystemDebugProvider : ISubsystemDebugProvider
{
    private readonly Func<EntityRepository?>? _world;
    private readonly Func<NetworkEntityMap?>? _entityMap;
    private readonly Func<ITimeTransportFacade?>? _drive;
    private readonly Func<Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer?>? _gizmoBuffer;
    private readonly Func<Hrot.UI.Common.Facades.IMissionEditorService?>? _missionEditor;
    private readonly Func<Action<TransitionStateIntent>?>? _requestTransition;
    private readonly Func<ClusterState?>? _clusterState;
    private readonly Func<IReadOnlyList<string>?>? _availableScenarios;
    private readonly Func<Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService?>? _architecture;
    private readonly Func<Action<ExecuteDiagnosticDumpIntent>?>? _requestDiagnosticDump;
    private readonly Func<DiagnosticDumpStatus?>? _dumpStatus;

    /// <summary>
    /// ⭐⭐⭐ <b>THE ACCESSORS ARE LAZY, AND THAT IS MEASURED — NOT DEFENSIVE STYLE.</b>
    ///
    /// <para>📐 Measured `2026-08-24`: a first cut captured the dependencies BY VALUE at provider
    /// construction, and <c>GET /capabilities</c> reported <c>time.drive:false</c> for <b>SimHost and
    /// CGF</b> — the two subsystems that definitely have a drive adapter. 🔴 The reason:
    /// <c>_clusterTimeAdapter</c> is created in <c>RegisterWindows</c>, which runs when the window opens,
    /// i.e. AFTER the composition root builds the providers.</para>
    ///
    /// <para>⇒ ⭐⭐ a value-captured provider would have reported a capability ABSENT that the subsystem
    /// gains seconds later — ⛔ the manifest lying in the safe-looking direction, which is worse than
    /// lying loudly. ⭐ With accessors, <see cref="DescribeCapabilities"/> measures at READ time, so the
    /// matrix is live.</para>
    /// </summary>
    public SubsystemDebugProvider(
        string subsystemName,
        string perspective,
        Func<EntityRepository?>? world = null,
        Func<NetworkEntityMap?>? entityMap = null,
        Func<ITimeTransportFacade?>? drive = null,
        // ⭐⭐ BP-487 — the node's map feed. ⚠ A Func for the SAME measured reason as `drive` above: CGF
        //    builds `_cgfGizmoBuffer` in Initialize, i.e. AFTER the composition root builds this provider.
        Func<Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer?>? gizmoBuffer = null,
        // ⭐⭐ CE-066 — the node's mission editor. ⚠ A Func for the same measured reason: CGF builds its
        //    ScenarioMissionService during window registration, long after the providers are built.
        Func<Hrot.UI.Common.Facades.IMissionEditorService?>? missionEditor = null,
        Func<Action<TransitionStateIntent>?>? requestTransition = null,
        Func<ClusterState?>? clusterState = null,
        Func<IReadOnlyList<string>?>? availableScenarios = null,
        Func<Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService?>? architecture = null,
        Func<Action<ExecuteDiagnosticDumpIntent>?>? requestDiagnosticDump = null,
        Func<DiagnosticDumpStatus?>? dumpStatus = null)
    {
        SubsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
        Perspective   = perspective   ?? throw new ArgumentNullException(nameof(perspective));
        _world        = world;
        _entityMap    = entityMap;
        _drive        = drive;
        _gizmoBuffer  = gizmoBuffer;
        _missionEditor = missionEditor;
        _requestTransition = requestTransition;
        _clusterState = clusterState;
        _availableScenarios = availableScenarios;
        _architecture = architecture;
        _requestDiagnosticDump = requestDiagnosticDump;
        _dumpStatus = dumpStatus;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ONE way a subsystem contributes <see cref="ISubsystemDebugProvider.RequestTransition"/>:
    /// publish onto its own orchestration bus.</b> 📄 <c>MCP_Integration.md</c> § Group U.
    ///
    /// <para>⭐ Every host does exactly this and nothing else — 📐 measured: the orchestrator's <c>_bus</c> and
    /// the editor's <c>_orchestrationBus</c> are read DIRECTLY by their own <c>ClusterMaster</c>; CGF · SimHost ·
    /// IG *(all three via <c>NodeBootstrapper</c>)* and ExCon each wire a <c>ClusterOpEgressTranslator</c> onto
    /// theirs, which carries the intent to the master over DDS. ⇒ ⛔ four hand-written copies of one lambda
    /// would be four places for it to drift; this is the single implementation *(ruling 9)*.</para>
    ///
    /// <para>⚠ The bus is fetched through a <see cref="Func{T}"/> and re-read on every access — subsystem buses
    /// are created in <c>Initialize</c> and NULLED in <c>Shutdown</c>. 📌 The same lesson as the lazy capability
    /// accessors below, and as <c>HN-028</c>'s master read.</para>
    /// </summary>
    public static Func<Action<TransitionStateIntent>?> TransitionsVia(Func<FdpEventBus?> orchestrationBus)
    {
        if (orchestrationBus is null) throw new ArgumentNullException(nameof(orchestrationBus));
        return () =>
        {
            var bus = orchestrationBus();
            return bus is null ? null : intent => bus.PublishManaged(intent);
        };
    }

    /// <summary>
    /// ⭐⭐ <b><c>MD-006</c> — the dump analog of <see cref="TransitionsVia"/>, and the same one-implementation
    /// argument applies:</b> every host publishes onto its own orchestration bus and every one of those buses
    /// reaches a <c>ClusterMaster</c>. ⛔ Four hand-written copies of one lambda would be four places to drift.
    /// ⚠ The bus is fetched through a <see cref="Func{T}"/> and re-read on every access — subsystem buses are
    /// created in <c>Initialize</c> and NULLED in <c>Shutdown</c>.
    /// </summary>
    public static Func<Action<ExecuteDiagnosticDumpIntent>?> DumpsVia(Func<FdpEventBus?> orchestrationBus)
    {
        if (orchestrationBus == null) throw new ArgumentNullException(nameof(orchestrationBus));
        return () =>
        {
            var bus = orchestrationBus();
            return bus is null ? null : intent => bus.PublishManaged(intent);
        };
    }

    public string SubsystemName { get; }
    public string Perspective { get; }
    public EntityRepository? World => _world?.Invoke();
    public NetworkEntityMap? EntityMap => _entityMap?.Invoke();
    public ITimeTransportFacade? Drive => _drive?.Invoke();
    public Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer?.Invoke();
    public Hrot.UI.Common.Facades.IMissionEditorService? MissionEditor => _missionEditor?.Invoke();
    public Action<TransitionStateIntent>? RequestTransition => _requestTransition?.Invoke();
    public ClusterState? ClusterState => _clusterState?.Invoke();
    public IReadOnlyList<string>? AvailableScenarios => _availableScenarios?.Invoke();
    public Fdp.ModuleHost.Diagnostics.IArchitectureDiagnosticsService? Architecture => _architecture?.Invoke();
    public Action<ExecuteDiagnosticDumpIntent>? RequestDiagnosticDump => _requestDiagnosticDump?.Invoke();
    public DiagnosticDumpStatus? DumpStatus => _dumpStatus?.Invoke();

    /// <summary>
    /// ⭐⭐⭐ <b>MEASURED from what is wired</b> — ⛔ never declared. 📌 Q54's one real risk: a hand-authored
    /// matrix stays green while the code drifts.
    /// </summary>
    public IReadOnlyDictionary<string, bool> DescribeCapabilities() => new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        [DebugCapabilities.WorldRead]   = World is not null,
        [DebugCapabilities.EntityMap]   = EntityMap is not null,
        [DebugCapabilities.TimeDrive]   = Drive is not null,
        [DebugCapabilities.ScenarioLoad] = RequestTransition is not null,
        // ⭐ MD-002 — per subsystem, because each holds its OWN kernel. ⚠ False for ExCon and the
        //   Orchestrator, which genuinely have none, and that is the honest cell rather than an empty
        //   snapshot that would read as "this subsystem runs no modules".
        [DebugCapabilities.ArchitectureDiagnostics] = Architecture is not null,
        // ⭐ MD-006 — measured from the orchestration bus being reachable, exactly like ScenarioLoad.
        [DebugCapabilities.ClusterDiagnosticsDump] = RequestDiagnosticDump is not null,

        // ⭐⭐⭐ BP-487 — the gizmo frame IS per-provider, and saying otherwise was a MANIFEST LIE.
        // ⚠⚠ THIS COMMENT USED TO READ: *"Panels and the gizmo frame are PROCESS-WIDE statics
        //    (PanelSnapshot / the primitive buffer), so they are not a per-provider capability — the
        //    dispatcher reports them once."* 🔴 It conflated TWO DIFFERENT THINGS:
        //      · `panels.read`  → PanelSnapshot — genuinely a process-wide STATIC ⇒ still not claimed here.
        //      · `panels.gizmo` → a DebugPrimitiveBuffer — ⛔ NEVER static. 📐 Measured `2026-08-27`: one
        //        buffer PER SUBSYSTEM (CgfSubsystem._cgfGizmoBuffer, IgApplication.GizmoBuffer,
        //        SimHostVisualization.GizmoBuffer) and ExCon has NONE.
        // ⇒ CapabilityManifest hard-coded `panels.gizmo = true` on every perspective row on the strength of
        //    that sentence, so `--mode all` CLAIMED a feed that answered 404 — 📌 exactly the "lying in the
        //    safe-looking direction" this class's own ctor remarks call worse than lying loudly.
        [DebugCapabilities.GizmoFrame] = GizmoBuffer is not null,

        // ⭐⭐ CE-066 — same argument as the gizmo feed one line up: the mission editor is PER SUBSYSTEM
        //    (CGF builds one, IG/SimHost/ExCon do not), so `/missions` can be classified and its cell
        //    MEASURED instead of the routes sitting unclassified — which is what kept
        //    `The_manifest_describes_this_host_truthfully` red before its matrix loop was ever reached.
        [DebugCapabilities.MissionEdit] = MissionEditor is not null,
    };
}

/// <summary>⭐ The capability keys, in one place so the manifest and the rails cannot spell them differently.</summary>
public static class DebugCapabilities
{
    public const string WorldRead = "world.read";
    public const string EntityMap = "world.entityMap";
    public const string TimeDrive = "time.drive";
    public const string Panels    = "panels.read";
    public const string GizmoFrame = "panels.gizmo";
    /// <summary>⭐ CE-066 — reading and committing an entity's mission plan (`/missions/*`).</summary>
    public const string MissionEdit = "mission.edit";
    public const string Preview   = "preview.control";
    public const string EditorAuthoring = "editor.authoring";

    /// <summary>⭐ MD-002 — this subsystem can report its modules/systems/translators (it has a kernel).</summary>
    public const string ArchitectureDiagnostics = "diagnostics.architecture";

    /// <summary>⭐ MD-006 — this subsystem can trigger the cluster-wide diagnostic dump from its own bus.</summary>
    public const string ClusterDiagnosticsDump = "diagnostics.clusterDump";

    /// <summary>
    /// ⭐⭐ <b>Requesting a cluster-wide scenario load</b> — <c>scenario/load/live</c> · <c>scenario/load/edit</c>.
    /// <para>⛔ Deliberately NOT <see cref="EditorAuthoring"/>: 📌 while `/scenario/load` was hardwired to
    /// <c>IEditorLogic</c>, a cluster refusal read *"authoring is absent here"* — true of the editor's DRIVER
    /// but easily misread as *"a cluster cannot load scenarios"*, which is false. ⭐ Its own key says which
    /// capability is actually missing.</para>
    /// </summary>
    public const string ScenarioLoad = "scenario.load";
}
