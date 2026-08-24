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
    private readonly Func<Action<TransitionStateIntent>?>? _requestTransition;
    private readonly Func<ClusterState?>? _clusterState;
    private readonly Func<IReadOnlyList<string>?>? _availableScenarios;

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
        Func<Action<TransitionStateIntent>?>? requestTransition = null,
        Func<ClusterState?>? clusterState = null,
        Func<IReadOnlyList<string>?>? availableScenarios = null)
    {
        SubsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
        Perspective   = perspective   ?? throw new ArgumentNullException(nameof(perspective));
        _world        = world;
        _entityMap    = entityMap;
        _drive        = drive;
        _requestTransition = requestTransition;
        _clusterState = clusterState;
        _availableScenarios = availableScenarios;
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

    public string SubsystemName { get; }
    public string Perspective { get; }
    public EntityRepository? World => _world?.Invoke();
    public NetworkEntityMap? EntityMap => _entityMap?.Invoke();
    public ITimeTransportFacade? Drive => _drive?.Invoke();
    public Action<TransitionStateIntent>? RequestTransition => _requestTransition?.Invoke();
    public ClusterState? ClusterState => _clusterState?.Invoke();
    public IReadOnlyList<string>? AvailableScenarios => _availableScenarios?.Invoke();

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
        // ⭐ Panels and the gizmo frame are PROCESS-WIDE statics (PanelSnapshot / the primitive buffer), so
        //   they are not a per-provider capability — the dispatcher reports them once. ⛔ Claiming them here
        //   per subsystem would suggest a routing that does not exist.
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
    public const string Preview   = "preview.control";
    public const string EditorAuthoring = "editor.authoring";

    /// <summary>
    /// ⭐⭐ <b>Requesting a cluster-wide scenario load</b> — <c>scenario/load/live</c> · <c>scenario/load/edit</c>.
    /// <para>⛔ Deliberately NOT <see cref="EditorAuthoring"/>: 📌 while `/scenario/load` was hardwired to
    /// <c>IEditorLogic</c>, a cluster refusal read *"authoring is absent here"* — true of the editor's DRIVER
    /// but easily misread as *"a cluster cannot load scenarios"*, which is false. ⭐ Its own key says which
    /// capability is actually missing.</para>
    /// </summary>
    public const string ScenarioLoad = "scenario.load";
}
