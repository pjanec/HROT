using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Logging;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
// (Phase 5: Fdp.Toolkit.Vis2D.Tools removed with StandardInteractionTool)
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Configuration;
using Hrot.CGF.Systems;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Common.Interactions;
using Hrot.Common.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Hrot.AI.Behaviors.Mappers;
using Hrot.Presentation.Windows;
using Hrot.Presentation.Facades;
using Hrot.Presentation.Renderers;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Menus;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Panels;
using Hrot.SimHost;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Blueprints.Editor.Debug;
using StructEdit.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel    = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpInspectorState       = Fdp.Presentation.Abstractions.InspectorState;
using FdpRepositoryAdapter    = Fdp.Presentation.Adapters.RepositoryAdapter;

namespace Hrot.CGF;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// Migrated in EAM-M003 to use <see cref="HrotNodeBuilder"/> instead of <see cref="CgfApplication"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem, Fdp.Toolkit.Runner.IMapCameraProvider,
    Hrot.Presentation.DebugApi.IProvidesDebugSurface, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
{
    private HrotNodeContext?  _context;
    private NetworkEntityMap? _entityMap;
    private Action?           _cgfNetworkPolling;

    // ── Headless + behavior registry ──────────────────────────────────────────
    private bool               _headless;    private ClusterTimeTransportAdapter? _clusterTimeAdapter;    private BehaviorRegistry?  _behaviorRegistry;
    private TogglableInputGroup?      _toggleInput;
    private TogglableSimulationGroup? _toggleSim;
    private Hrot.Core.Network.INetworkFactory? _networkFactory;
    private PhysicsToolkitModule? _physicsModule;

    // ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
    private EntityRepository?       _bpPreTickSnapshot;
    private DebugSnapshotProvider?  _bpSnapshotProvider;
    private DataBreakpointManager?  _bpManager;
    private DataBreakpointSystem?   _bpSystem;

    /// <summary>
    /// ⭐⭐⭐ Slice 4 (<c>DQ30</c>) — the debug time controller, hoisted to a field because
    /// <see cref="Update"/> has to bracket the kernel update with its step latch. ⛔ Without that
    /// bracket "step one tick" cannot be exactly one tick.
    /// </summary>
    private Hrot.CGF.Debug.CgfClusterDebugTimeController? _debugTimeController;

    /// <summary>
    /// ⭐ The debug time controller, for integration rails that need the cluster barrier anchor
    /// *(`CE-029`: `k = ClusterBarrierWallTicks − PausedTick`)*. ⚠ Mirrors the existing
    /// <see cref="DataBreakpointManager"/> accessor's shape — internal, not public: it is
    /// observability, not a supported host API.
    /// </summary>
    internal Hrot.CGF.Debug.CgfClusterDebugTimeController? DebugTimeController => _debugTimeController;

    // ── cgf==editor slice 1: the AiShared shell ───────────────────────────────
    // 📄 docs/DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md §3 (classDiagram) / §4 (sequenceDiagram).
    // ⭐⭐ The ENTIRE slice is a composition block: every type below already exists in
    //    Hrot.Editor.AiShared and is CONSTRUCTED here, never modified (§7 — AiShared is the
    //    variable-model lane's, and this lane only consumes it).

    /// <summary>
    /// ⭐⭐⭐ The StructEdit edit service, hoisted out of <c>Initialize</c> so the AiShared shell gets
    /// <b>the same instance</b> the breakpoint predicate compiler already uses.
    /// <para>⛔ A second <c>ComponentEditServiceBuilder().Build()</c> in <c>RegisterWindows</c> would be
    /// two implementations of one concept *(ruling 9)*, and <c>PerspectiveWorkspaceServices</c> REQUIRES
    /// this argument — so "I forgot" would have been expressible as "I built a fresh one".</para>
    /// </summary>
    private StructEdit.Core.IComponentEditService? _facetEditService;

    /// <summary>
    /// ⭐⭐ <c>BP-510</c> — this node's view of the current load's staging⇄runtime id table.
    /// <para>⭐ CGF is the SOURCE of that table *(<c>StagingEntityExtractor.OnRemap</c>)*, so unlike the
    /// editor it does not subscribe to the published event — it fills this view on the same line that
    /// publishes it.</para>
    /// </summary>
    private readonly Hrot.Editor.AiShared.Variables.StagingRemapView _stagingRemap = new();

    /// <summary>⭐ One shared entity selection behind every perspective's store, exactly as the editor
    /// does *(<c>EditorSubsystem</c> <c>:304</c>/<c>:834</c>)</summary>
    private readonly Hrot.Editor.AiShared.Selection.SharedEntitySelection _sharedEntitySelection = new();

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-013</c> — the POPULATED asset catalog</b> *(slice 2)*. ⚠ Slice 1 held a bare
    /// <c>new AssetCatalog()</c>, which is why every AiShared window could only render its empty state.
    /// </summary>
    private Hrot.Editor.AiShared.Catalog.AiAssetCatalogBuilder? _aiCatalogBuilder;

    /// <summary>
    /// ⭐⭐ Path segments of the <c>Hrot.AI.Behaviors</c> project file, used to find the SOURCE tree that
    /// holds the authoring assets. ⛔ <b>The same property, the same default and the same propagation the
    /// editor already has</b> *(<c>EditorSubsystem.AiBehaviorsProjectPath</c>, set from
    /// <c>HrotRunnerConfiguration</c> in <c>Program.cs</c>)* — ⭐ one configured value, two hosts, ⛔ not a
    /// second notion of where the assets live.
    /// </summary>
    public string[] AiBehaviorsProjectPath { get; set; } =
        new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };

    /// <summary>
    /// ⭐⭐ <b><c>CE-014</c> — the three pieces the debug API needs to drive this host's authoring shell.</b>
    /// 📄 slice-2 design §3/§5. ⭐ Non-null once <c>BuildAiShell</c> has run *(a non-headless host)*;
    /// ⛔ null on a headless node, which genuinely has no shell — <c>GET /assets</c> then answers 503 with
    /// the wiring explanation rather than an empty list that would look like "no assets here".
    /// </summary>
    internal Hrot.Editor.AiShared.Catalog.AssetCatalog?     AssetShellCatalog   { get; private set; }
    internal Hrot.Editor.AiShared.Documents.AiDocumentManager? AssetShellDocuments { get; private set; }
    internal Fdp.Presentation.WindowManager.WindowManager?  AssetShellWindows   { get; private set; }

    /// <summary>⭐ <c>CE-021</c> — the save action the debug API drives *(assetId → status)*.</summary>
    internal Func<string, string>? AssetShellSave   { get; private set; }

    /// <summary>⭐ <c>CE-021</c> — the reload action the debug API drives *(assetId → status)*.</summary>
    internal Func<string, string>? AssetShellReload { get; private set; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MA-019</c> — CREATE, the last authoring verb CGF did not have.</b>
    /// 📄 <c>Architect_Question_57_Cgf_Authoring_Packaging.md</c> Q57-A/Q57-C.
    ///
    /// <para>⭐⭐ <b>Published, not pushed — the same reference wall as the shell above:</b> <c>Hrot.CGF</c>
    /// cannot see <c>DebugApiService</c>, so <c>ClusterRunner/Program.cs</c> — the one composition root
    /// that sees both — hands this to <c>AttachAssetAuthoring</c>. ⛔ Not a second create implementation:
    /// it is the SAME per-kind <see cref="Hrot.Editor.AiShared.Recipes.INewAssetService"/> contract the
    /// editor's New-Asset dialog runs, composed at THIS host's root.</para>
    ///
    /// <para>⚠ <c>(kind, name, relPath, recipe)</c> → <c>(mintedId | null, status)</c>. ⛔ The id is
    /// returned ONLY once the asset is in the catalog — the <c>MA-004</c> rule.</para>
    /// </summary>
    internal Func<string, string, string, string?, (Guid? AssetId, string Status)>? AssetShellCreate { get; private set; }

    /// <summary>
    /// ⭐⭐ <b><c>MA-020</c> — the per-kind service registry itself</b>, published so the debug API can
    /// project RECIPES from it through the shared <c>RecipePickerSource</c>.
    /// ⛔ Deliberately the registry and not a pre-baked list: <c>AvailableRecipes()</c> re-reads disk on
    /// every call, so a snapshot taken at composition time would go stale the moment a recipe is added.
    /// </summary>
    internal IReadOnlyDictionary<Hrot.Editor.AiShared.AssetKind,
                                 Hrot.Editor.AiShared.Recipes.INewAssetService>? AssetShellNewAssetServices { get; private set; }

    /// <summary>
    /// ⭐ <b><c>MA-022</c></b> — the action-schema exporter, so MCP's <c>get_node_kind_schema</c> answers
    /// with real DTO fields on CGF instead of <c>paramsSource: none:no-exporter-wired</c>.
    /// ⚠ Constructed and <c>Rebuild()</c>-ed here for the same reason the editor does it *(the exporter is
    /// otherwise empty until a catalog watcher fires, and CGF wires no watcher)*.
    /// </summary>
    internal Hrot.Editor.AiShared.Blackboard.IActionSchemaExporter? AssetShellSchemaExporter { get; private set; }

    // ── MA-019: the host-composition facts CREATE needs, hoisted out of BuildAssetCatalog ──
    // ⛔ Not conveniences: the editor's own create path shows a duplicate gets exactly these four wrong
    //    (the Blueprint SOURCE root, the per-contributor Refresh, the assembly refresh, and only THEN
    //    FindByAssetId). ⇒ CGF must hold the same handles to run the same lines.
    private string? _bpRootDir;
    private string? _btreeJsonRootDir;
    private string? _hsmJsonRootDir;
    private Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor? _btreeJsonContrib;
    private Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor?     _hsmJsonContrib;

    private Dictionary<Hrot.Editor.AiShared.AssetKind,
                       Hrot.Editor.AiShared.Recipes.INewAssetService>? _newAssetServices;

    /// <summary>
    /// ⭐ Make the named asset's open document the ACTIVE one. ⚠ A no-op when it is not open — the
    /// caller *(the API route)* has already refused that case with a typed hint, and duplicating the
    /// refusal here would give two answers to one question.
    /// </summary>
    private void ActivateByAssetId(string assetId)
    {
        if (_aiDocumentManager == null || !Guid.TryParse(assetId, out var id)) return;
        var doc = _aiDocumentManager.OpenDocuments.FirstOrDefault(d => d.Asset.AssetId == id);
        if (doc != null) _aiDocumentManager.Activate(doc);
    }

    // ── SLICE 3 (CE-019/020) — the save + hot-reload pipeline ─────────────────
    private Hrot.Blueprints.Editor.Reload.QuickReloadService? _quickReload;
    private Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate? _saveBlueprint;
    private Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate? _saveBTree;
    private Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.SaveDelegate? _saveHsm;

    private Hrot.Editor.AiShared.Documents.WindowManagerPerspectiveSwitcher? _perspectiveSwitcher;
    private Hrot.Editor.AiShared.Documents.AiDocumentManager?                _aiDocumentManager;
    private Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar?      _btreeRegistrar;
    private Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar?      _hsmRegistrar;
    private Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar?      _blueprintRegistrar;

    // ⛔ NO TestHook for "which perspectives did I register". 📐 A first cut added one and nothing could
    //    use it: the shell needs a REAL WindowManager (an ImGui atlas), so no headless unit rail can
    //    construct it — and the claim is already asserted where it is observable, over MCP, by
    //    ClusterConformanceRails.The_cluster_offers_the_asset_perspectives. ⚠ An internal accessor with
    //    no caller is the "built and unreachable" shape this codebase keeps finding; ⭐ better absent.

    // ── Scenario entity creation source (shared with load handlers in Phases 3-4) ──
    private ScenarioEntityCreationRequestSource? _scenarioSource;

    // ── Blueprint materialization (BSA-203) ────────────────────────────────────
    private BlueprintRegistry? _blueprintRegistry;

    /// <summary>
    /// Exposes the scenario entity creation request source for load handlers (Phases 3-4).
    /// Available after <see cref="Initialize"/> has been called.
    /// </summary>
    internal ScenarioEntityCreationRequestSource? ScenarioEntityCreationSource => _scenarioSource;

    // ── Visualization ─────────────────────────────────────────────────────────
    private MapCanvas?                 _canvas;
    private DefaultSelectionState?     _selectionState;
    // (Phase 5: _interactionTool removed; entity selection via ECS gizmos)
    private EntityQuery?               _entityQuery;
    private Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? _cgfGizmoBuffer;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager? _cgfGizmoManager;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem? _cgfDataDrivenGizmoSystem;
    private Fdp.Core.FdpEventBus? _cgfInteractionBus;
    private Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? _cgfGizmoController;
    // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
    internal Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController CgfGizmoController => _cgfGizmoController!;
    // GZH-014: explicit interface implementation — avoids renaming the existing property.
    Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? Hrot.Common.Diagnostics.Gizmos.IGizmoControllable.GizmoController
        => _cgfGizmoController;

    // ── FDP panels ────────────────────────────────────────────────────────────
    private FdpEntityInspectorPanel              _fdpEntityInspector = new();
    private FdpEventBrowserPanel                 _fdpEventBrowser    = null!;
    private DiagnosticEventHistoryService        _fdpEventHistory    = new();
    private FdpRepositoryAdapter?                _fdpRepoAdapter;
    private FdpInspectorState       _fdpInspectorState  = new();
    private uint                    _fdpFrameCount;

    // ── Map context menu ──────────────────────────────────────────────────────
    private DebugGizmoLayer? _cgfGizmoLayer;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <summary>
    /// ⭐⭐⭐ <b><c>Q54</c> — CGF's debug surface: its own world, its own map, and its OWN drive adapter.</b>
    /// 📄 <c>Architect_Question_54</c> Q54-2 *(perspective-scoped dispatch over per-subsystem providers)*.
    ///
    /// <para>⚠⚠ <b>The perspective is <c>"Scenario"</c>, NOT <c>"CGF"</c></b> — 📐 <c>perspectiveMap</c>'s one
    /// entry whose key and value differ *(<c>DESIGN_Perspective_Unification.md</c> §1b/§1e)*. ⛔ Using the
    /// subsystem name here would make the dispatcher unable to resolve the live perspective.</para>
    ///
    /// <para>⭐ The drive facade is the SAME <c>ClusterTimeTransportAdapter</c> the operator's toolbar uses, so
    /// a step issued through this provider travels the operator's own path: intent → this subsystem's bus →
    /// DDS → the master. ⛔ Nothing new is introduced here.</para>
    /// </summary>
    public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
        => new Hrot.Presentation.DebugApi.SubsystemDebugProvider(
            subsystemName: Name,
            perspective:   "Scenario",
            world:         () => _context?.World,
            entityMap:     () => _entityMap,
            drive:         () => _clusterTimeAdapter,
            // ⭐⭐ HN-029: the node's own orchestration bus — the same one its ClusterSlave and
            //    ClusterOpEgressTranslator sit on, so a transition requested here reaches the master by the
            //    path the operator's own "Load into Live" button takes.
            requestTransition: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                   .TransitionsVia(() => _context?.EventBus));

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.57f, 0.47f, 0.04f, 1f);

    /// <summary>Creates CgfSubsystem without a network factory (legacy / headless path).</summary>
    public CgfSubsystem() { }

    /// <summary>Creates CgfSubsystem with an injected protocol factory from the composition root.</summary>
    public CgfSubsystem(Hrot.Core.Network.INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    /// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
    internal NetworkEntityMap? GhostEntityMap => _entityMap;

    /// <summary>TestHook: exposes the CGF ECS world for integration tests.</summary>
    internal Fdp.Core.EntityRepository? World => _context?.World;

    /// <summary>
    /// TestHook: runtime type of the CGF kernel's time controller. Mirrors
    /// <c>SimHostApp.TestHook_TimeControllerType</c> so an integration test can assert both
    /// kernel-owning nodes the same way.
    /// </summary>
    internal Type? TestHook_TimeControllerType => _context?.Kernel?.GetTimeController()?.GetType();

    /// <summary>
    /// TestHook: current <see cref="Fdp.ModuleHost.Time.TimeMode"/> of the CGF kernel's time
    /// controller. CGF sits in the orchestrator's lockstep roster, so on Pause this must reach
    /// <c>Deterministic</c> — if it does not, the node never ACKs and every step after the first
    /// is lost (AS-14). Nothing observed this before Batch 104.
    /// </summary>
    internal Fdp.ModuleHost.Time.TimeMode? TestHook_TimeControllerMode
        => _context?.Kernel?.GetTimeController()?.GetMode();

    /// <summary>Internal test hook: exposes the data breakpoint manager (UBP-P10T2).</summary>
    internal IDataBreakpointManager? DataBreakpointManager => _bpManager;

    /// <summary>Internal test hook: exposes the debug snapshot provider (UBP-P10T2).</summary>
    internal DebugSnapshotProvider? BpSnapshotProvider => _bpSnapshotProvider;

    /// <summary>TestHook: exposes the CGF behavior registry so integration tests can register
    /// scenario-specific behaviors (e.g. UrbanCombat) before the cluster transitions to
    /// OperatingLive and scenario entities begin executing missions.</summary>
    internal BehaviorRegistry? TestHook_BehaviorRegistry => _behaviorRegistry;

    /// <summary>
    /// TestHook: spawns an entity and publishes a <c>DeferredTakeOwnership</c> routing table
    /// that assigns the WorldPos descriptor to <paramref name="muscleNodeId"/>.
    ///
    /// <para>Mirrors what a full <c>CreateEntityRequestSystem(isDefaultProcessor:true)</c> would do
    /// without requiring ExCon wiring in integration tests.</para>
    /// </summary>
    internal long TestHook_SpawnEntityWithSplitAuthority(long tkbType, int muscleNodeId)
    {
        if (_context == null)
            throw new System.InvalidOperationException("CgfSubsystem not initialized.");

        long networkId = _context.IdAllocator?.AllocateId()
            ?? unchecked((long)System.Threading.Interlocked.Increment(ref _testIdCounter));

        // 1. Publish DeferredTakeOwnership FIRST (pre-genesis, before EntityMaster).
        var dtoCmd = new DeferredTakeOwnershipCommand { NetworkId = networkId };
        long worldPosId  = _networkFactory?.WorldPosDescriptorId          ?? 0;
        long navStatusId = _networkFactory?.NavigationStatusDescriptorId   ?? 0;
        if (worldPosId != 0)
            dtoCmd.Grants.Add(new DescriptorGrant { DescriptorTypeId = worldPosId,  NodeId = muscleNodeId });
        if (navStatusId != 0)
            dtoCmd.Grants.Add(new DescriptorGrant { DescriptorTypeId = navStatusId, NodeId = muscleNodeId });
        _context.World.Bus.PublishManaged(dtoCmd);

        // 2. Publish SpawnEntityCommand (CGF/Brain owns entity identity).
        _context.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId   = networkId,
            TkbType     = tkbType,
            OwnerNodeId = _context.NodeId,
            InitType    = Fdp.Toolkit.Replication.ReliableInitType.AllPeers,
            RequestId   = System.Guid.Empty,
        });

        return networkId;
    }

    private int _testIdCounter;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {        _headless = config.Headless;
        int cgfNodeId = config.NodeId != 0 ? config.NodeId : 400;
        string baseTempRoot = OrchestrationConstants.ResolveStagingRoot();
        string isolatedTempRoot = System.IO.Path.Combine(baseTempRoot, "nodes", $"node-{cgfNodeId}");
        string resolvedLogDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");
        // ── Create DDS participant in the Application Shell (Composition Root) ───
        // Rule: only the outermost executable may instantiate DdsParticipant.
        // HrotNodeBuilder no longer has a fallback.
        var shellParticipant = _networkFactory?.Participant;
        if (shellParticipant == null)
        {
            shellParticipant = HrotEnvironment.CreateParticipant(config.DomainId);
            shellParticipant.EnableSenderTracking(new SenderIdentityConfig
            {
                AppDomainId   = config.DomainId,
                AppInstanceId = cgfNodeId,
            });
        }
        // ── Build common infrastructure ────────────────────────────────────────
        var nodeConfig = new HrotNodeConfig
        {
            DomainId            = config.DomainId,
            NodeId              = cgfNodeId,
            // CgfSubsystem always creates a DDS participant — Headless here controls only
            // the Raylib/ImGui window (UI), not the network layer.
            // This mirrors SimHostApp which also hardcodes Headless = false for HrotNodeConfig.
            Headless            = false,
            ExternalParticipant = shellParticipant,
            LocalTempRoot       = isolatedTempRoot,
            LogDirectory        = resolvedLogDir,
            SubsystemName       = "CGF",
        };
        _context = new HrotNodeBuilder(nodeConfig)
            .WithRole("CgfNode", NodeRole.Brain)
            .WithNetworkFactory(_networkFactory)
            .Build();

        _entityMap = _context.EntityMap;
        _context.World.SetSingletonManaged<NetworkEntityMap>(_entityMap!);
        // Behavior resolvers (Phase 2b) reach the geographic transform through this world singleton
        // rather than a registration-time closure, so the CGF node — where the PlatoonHillAttack
        // commander and its vehicles activate — must publish it here (mirrors SimHostApp). Without it
        // geo-aware params (hill/baseline positions) resolve to 0,0,0.
        if (_context.GeoTransform != null)
            _context.World.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(_context.GeoTransform);
        CgfComponentRegistry.RegisterAll(_context.World);

        // ── Register base infrastructure modules ───────────────────────────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // Allocate RaycastBatchData so Action_QueryRaycast can enqueue/query requests on CGF.
        _physicsModule = new PhysicsToolkitModule();
        _physicsModule.Initialize(_context.World);

        // ── Create replication module via factory (Brain role) ─────────────────
        // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
        var behaviorRegistry = new BehaviorRegistry();
        _behaviorRegistry = behaviorRegistry;
        // Blueprint registry (shared by materialization system and serializers). Created here so the
        // single attribute-driven registration pass below can populate both registries at once.
        _blueprintRegistry = new BlueprintRegistry();

        // Single self-registration pass: discovers every [BlueprintRegistrar] in the AI behaviors
        // assembly (curated CgfCuratedBehaviorRegistrar + generated per-asset registrars) and
        // registers each behavior under its own name, binding named resolvers by name. The scanner
        // injects an ActionRegistry populated from the assembly's [FbtRegistrar] so those trees'
        // bound actions/conditions execute real logic at runtime.
        CgfBehaviorSetup.LoadFromAiAssembly(behaviorRegistry, _blueprintRegistry);

        // Expose the registry to the diagnostic renderers so the entity inspector
        // can project BrainBlackboard memory and visualize the BTree execution state.
        BrainBlackboardRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;
        BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor   = behaviorRegistry;

        // Wire the FDP-layer trace emitter to the NLog-backed BehaviorLog (behav-diag-1).
        // Idempotent: safe to overwrite on hot-reload.
        Fdp.Toolkit.Behavior.Diagnostics.BehaviorTraceLog.Instance =
            new Hrot.AI.Behaviors.Logging.BehaviorTraceLogEmitter();

        // Configure network factory for this node so auxiliary translators can be created.
        var nodeFactory = _networkFactory?.ConfigureForNode(_context, NodeRole.Brain, behaviorRegistry);

        var replicationModule = nodeFactory?.CreateReplicationModule();
        if (replicationModule != null)
        {
            _context = _context with
            {
                NedReplication      = replicationModule as Hrot.Common.Abstractions.INedReplicationModule,
                GhostCreationSystem = replicationModule.GhostCreationSystem,
            };
            _context.Kernel.RegisterModule(replicationModule);
        }

        // ── Wire CreateEntityRequestSystem (CGF is the cluster-default processor) ─
        // This makes CGF intercept broadcast CreateEntityRequests (Owner == 0) and spawn
        // entities, delegating WorldPos (kinematics) to the least-loaded Muscle node via
        // DeferredTakeOwnership. SimHost nodes keep isDefaultProcessor=false.
        // Protocol-specific sources and sinks are obtained via the factory (Rule 3).

        // Create the scenario source once; shared with load handlers in Phases 3-4
        // via CgfLogicPack.ScenarioSource.
        _scenarioSource = new ScenarioEntityCreationRequestSource();


        // Expose the blueprint registry to the Entity Inspector renderers so
        // BlueprintBlackboard* components can show per-tier slot summaries.
        Hrot.Presentation.Renderers.BlueprintBlackboard1024Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
        Hrot.Presentation.Renderers.BlueprintBlackboard4096Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
        Hrot.Presentation.Renderers.BlueprintBlackboard16384Renderer.BlueprintRegistryAccessor = _blueprintRegistry;

        // ── Register CGF simulation logic (Brain-specific) ─────────────────────
        var mapperRegistry = new TacticalIntentMapperRegistry();
        mapperRegistry.Register(new DefendAreaMapper());
        mapperRegistry.Register(new HullDownAttackMapper());
        var cgfLogicPack = new CgfLogicPack(behaviorRegistry, _entityMap, _scenarioSource,
            mapperRegistry);
        _context.Kernel.RegisterModule(new BehaviorDiagnosticsModule());
        _context.Kernel.RegisterModule(cgfLogicPack);

        // Execute the Brain systems every frame via two togglable phase groups.
        _toggleInput = new TogglableInputGroup("CgfInput",           cgfLogicPack.InputSystems);
        _toggleSim   = new TogglableSimulationGroup("CgfSimulation", cgfLogicPack.SimulationSystems);

        _context.Kernel.RegisterGlobalSystem(_toggleInput);
        _context.Kernel.RegisterModule(new CgfSimulationModule(_toggleSim));

        var adapters = nodeFactory?.CreateCgfEntityLifecycleAdapters();

        var tkbDb       = _context.TkbDb!;
        var idAllocator = _context.IdAllocator!;
        var elm         = (EntityLifecycleModule)_context.BaseModules
                              .First(m => m is EntityLifecycleModule);

        // 1. Composite request source: always include the scenario source; add the live
        //    NED adapter source only when network is available.
        var requestSources = new System.Collections.Generic.List<IEntityCreationRequestSource>
        {
            _scenarioSource!
        };
        if (adapters != null)
            requestSources.Add(adapters.RequestSource);
        var compositeRequestSource = new CompositeEntityCreationRequestSource(requestSources);

        // 2. ACK sink: real NED sink when connected; null-object for offline / headless runs.
        IEntityAckSink ackSink = adapters?.AckSink ?? new NullEntityAckSink();

        var finalizationSystem = new EntityRequestFinalizationSystem(ackSink, _entityMap!);

        // 3. Register the core genesis pipeline unconditionally (online and offline).
        var requestSystem = new CreateEntityRequestSystem(
            requestSource:        compositeRequestSource,
            ackSink:              ackSink,
            tkbDb:                tkbDb,
            idAllocator:          idAllocator,
            localNodeId:          _context.NodeId,
            jsonAttributeCompiler: adapters?.JsonCompiler,
            finalizationSystem:   finalizationSystem,
            isDefaultProcessor:   true,
            ownershipStrategy:    adapters?.OwnershipStrategy);

        var spawnSystem = new NetworkSpawningSystem(
            tkbDb,
            elm,
            _entityMap!,
            idAllocator,
            _context.NodeId);

        _context.Kernel.RegisterGlobalSystem(spawnSystem);
        _context.Kernel.RegisterGlobalSystem(requestSystem);
        _context.Kernel.RegisterGlobalSystem(finalizationSystem);
        _context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(_entityMap!));
        Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
            _context.Kernel, _blueprintRegistry!);

        // 4. Network-dependent deletion routing: only when a live adapter exists.
        if (adapters != null)
        {
            var deleteSystem = new DeleteEntityRequestSystem(
                adapters.DeleteSource,
                adapters.AckSink,
                _entityMap!,
                finalizationSystem,
                _context.NodeId);

            _context.Kernel.RegisterGlobalSystem(deleteSystem);

            // Store polling action for heartbeat updates in Update().
            _cgfNetworkPolling = adapters.PollNetwork;
        }

        // ── Cluster time control (TM-002) ─────────────────────────────────────────
        // CGF is a kernel-owning node and the orchestrator DOES list it in the lockstep
        // roster (OrchestratorSubsystem: SubsystemName is "SimHost" or "IG" or "CGF"), so the
        // master blocks every step on a FrameAck from this node.  SimHost/IG get the
        // translators that carry that traffic from SharedApplicationBootstrapper Phase 6c; CGF
        // composes through HrotNodeBuilder directly and therefore has to register them itself.
        // Without this the node had a SlaveSyncController but no way to hear a pause or answer a
        // frame order — it stayed Continuous forever and the master's _pendingAcks never cleared,
        // so every step after the first was silently discarded (AS-14's actual root cause).
        SlaveTimeTranslatorRegistration.RegisterOn(
            _context.Kernel, _context.Participant, _context.EventBus, _context.NodeId);

        // Auxiliary translators (time-sync, combat, mission-control) via the injected factory.
        // Mirrors SimHostApp.cs pattern: nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(kernel)
        nodeFactory?.CreateSimHostAuxiliaryTranslators()?.RegisterOn(_context.Kernel);
        nodeFactory?.CreateSimHostPerceptionTranslators()?.RegisterOn(_context.Kernel);
        nodeFactory?.CreateSimHostPathfindingTranslators()?.RegisterOn(_context.Kernel);


        // ── Wire ClusterSlave with EcsRecordReplayController (CGF-Point-4) ────────
        // Create a fresh ClusterSlave manually to strictly control handler registration order.
        var newClusterSlave = new ClusterSlave(_context.NodeId, "CGF", _context.EventBus);

        var nedModuleForAfterSeek = replicationModule as Hrot.Common.Abstractions.INedReplicationModule;
        Action? afterSeekAction = nedModuleForAfterSeek?.AfterSeekCallback;

        var rrController = new Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController(
            _context.Kernel, _context.NodeId, _context.World, afterSeek: afterSeekAction);

        var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);

        // 1. Replay handler (must be first to gate Live-from-Replay branch)
        newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
            rrController,
            inputGroup:            _toggleInput,
            simGroup:              _toggleSim,
            postSimGroup:          null,
            lifecycleGroup:        null,
            bypassLifecycleToggle: null,
            storageDirectory:      isolatedTempRoot,
            suspendGlobalTimePush: _context.Kernel.SuspendGlobalTimePush,
            resumeGlobalTimePush:  _context.Kernel.ResumeGlobalTimePush));

        // 2. CGF-Authoritative Scenario and Episode Load Handlers (must be BEFORE ReferenceLiveLoadHandler)
        var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(_behaviorRegistry!);
        var scenarioLoader     = new HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);
        var behaviorRemapper   = CgfBehaviorSetup.CreateBehaviorRemapper();
        var extractor          = new Hrot.CGF.Orchestration.StagingEntityExtractor();

        // ⭐⭐⭐ BP-509 — the staging→runtime id table reaches the control-plane bus.
        // 📄 DESIGN_Variable_Watch_Pinning.md §5/§8①/§8a. 🔒 User ruling 2026-08-19: the sink lives on the
        //    extractor and THE SUBSYSTEM wires it — ⛔ the extractor never learns about a bus, which is
        //    what keeps Hrot.CGF separately deployable (R-79).
        // ⭐ `_context.EventBus` is the node's own orchestration bus — the same one its ClusterSlave
        //   drains (HN-029's TransitionsVia reads it too), ⛔ not the CGF interaction bus.
        extractor.OnRemap = map =>
        {
            // ⭐⭐ Slice 1 — this node's OWN Watch needs the same table it publishes to the cluster.
            //    ⛔ Not a second copy of the remap: StagingRemapView holds the map the extractor
            //    published and computes only its inverse (R-79 — the remap LOGIC stays in the
            //    extractor). ⚠ Filled on the SAME line that publishes, so a load can never update one
            //    and not the other.
            _stagingRemap.Publish(map);

            _context.EventBus.PublishManaged(
                new Fdp.Toolkit.Orchestration.StagingRemapPublishedEvent
                {
                    StagingToRuntime = map,
                    SourceNodeId     = _context.NodeId,
                });
        };

        // ⭐⭐⭐ HN-037 — AUTHORED IDS COME FROM THE ONE AUTHORITY, not from a second local allocator.
        // 📄 docs/DESIGN_Deterministic_Network_Ids.md §11d ②.
        // 📐 What the standalone `cgfIdAllocator = new SequentialIdAllocator()` cost, measured 2026-08-24:
        //    it seeded at 1 and pre-incremented, so the same scenario that gave the editor 1000–1007 gave
        //    --mode all 2–9 — HN-037, and it was PURELY the second seed, not a second authority. ⛔ It also
        //    put authored ids in the runtime allocator's band, since nothing kept the two apart.
        // ⭐ `_context.IdAllocator` is the DDS client of the master the orchestrator hosts; the master
        //    resets it to 1000 at the world boundary (ClusterMaster.ResetIdAuthorityIfWorldBoundary), so
        //    authored (at load) and runtime (after) now come from ONE monotonic sequence.
        var cgfIdAllocator     = idAllocator;

        newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfScenarioLoadHandler(
            scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World,
            remapper: behaviorRemapper, controller: rrController,
            storageDirectory: isolatedTempRoot));

        newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfEpisodeLoadHandler(
            scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World, behaviorRemapper));

        // 3. Fallback Live Load Handler (claims PrepareLive ONLY if scenario handlers didn't)
        newClusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
            checkpointWorker: null,
            controller: rrController,
            storageDirectory: isolatedTempRoot));

        // 4. Utility handlers
        // ⭐⭐⭐ HN-017 — this node restores its OWN allocator and map on the master's UnloadingPreview.
        // 📄 DESIGN_Deterministic_Network_Ids.md §4c. 🔒 User: "reset must be cluster wide" — and it IS,
        //    because the 2PC broadcast reaches every node and each commits locally. ⛔ No new protocol, and
        //    ⛔ nothing here touches the central id authority.
        // ⚠ Both participants or neither: the allocator alone guarantees a duplicate-id throw from
        //    NetworkEntityMap.Register on the second preview (§2b).
        // ⚠⚠ SUPERSEDED BY HN-037 — this comment used to warn that "CGF HAS **TWO** ALLOCATORS" and that
        //    restoring one would leave the other drifting. 📐 As of HN-037 there is ONE: the scenario-load
        //    handlers were pointed at `_context.IdAllocator`, the same instance the runtime spawn path
        //    (:217, :381) uses. ⇒ ⭐ registering it TWICE here would capture and restore the same allocator
        //    twice per preview — harmless today (idempotent replace) but a lie about the participant count,
        //    so it is registered once.
        // ⭐ The bracket still takes a LIST: the map is a second participant, and "both or neither" still
        //   holds (§2b — the allocator alone guarantees a duplicate-id throw from NetworkEntityMap.Register
        //   on the second preview).
        var cgfRewindables = new System.Collections.Generic.List<Fdp.Toolkit.Orchestration.Preview.IPreviewRewindable>();
        if (_entityMap != null)
        {
            if (_context.IdAllocator != null)
                cgfRewindables.Add(Fdp.Toolkit.Orchestration.Preview.PreviewParticipants.IdAllocator(_context.IdAllocator));
            cgfRewindables.Add(Fdp.Toolkit.Orchestration.Preview.PreviewParticipants.EntityMap(_entityMap));
        }
        newClusterSlave.RegisterHandler(new ReferencePreviewHandler(_context.World, cgfRewindables));
        newClusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));
        newClusterSlave.RegisterHandler(new ReferenceArchiveHandler(
            isolatedTempRoot, _context.NodeId));
        var cgfArchService = new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(_context.Kernel);
        var cgfEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_context.World, _context.EntityMap, scenarioSerializer);
        _fdpEntityInspector.ExtractionService = cgfEntityService;
        _fdpEntityInspector.Serializer        = scenarioSerializer;
        var cgfLogService = new Hrot.Core.Diagnostics.LogArchiveExtractionService(
            string.IsNullOrWhiteSpace(nodeConfig.LogDirectory)
                ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
                : nodeConfig.LogDirectory,
            nodeConfig.SubsystemName,
            nodeConfig.NodeId);
        newClusterSlave.RegisterHandler(new Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler(
            _fdpEventHistory,
            cgfArchService,
            cgfEntityService,
            cgfLogService,
            nodeConfig));

        _context = _context with
        {
            ClusterSlave = newClusterSlave
            // Note: SlaveTranslator is already correctly populated by HrotNodeBuilder earlier
        };



        // ── Initialize ─────────────────────────────────────────────────────────
        _fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
        _context.Kernel.RegisterGlobalSystem(
            new EventHistoryCaptureSystem("World", _fdpEventHistory, _context.World.Bus));
        _context.Kernel.RegisterGlobalSystem(
            new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _context.EventBus));

        // GZ057: CGF entity presentation gizmos. Buffer and registry must be set up
        // before Kernel.Initialize() because the GizmoInteractionModule is registered here.
        _cgfGizmoBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        _cgfInteractionBus = new Fdp.Core.FdpEventBus();
        _cgfGizmoManager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(_cgfGizmoBuffer, _cgfInteractionBus);
        var cgfStatelessRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry();
        var cgfGizmoRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry();
        var cgfSettingsRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();
        // ST-031: ONE reflection call replaces the hand-rolled family list. Like SimHost, CGF declared
        // only its own family plus Presentation and was missing Common's eight projectors entirely
        // (UXI-22). Uniform membership: it declares everything, and component presence decides what draws.
        Fdp.Toolkit.Diagnostics.Gizmos.GizmoReflectionRegistrar.RegisterAll(
            cgfGizmoRegistry, cgfStatelessRegistry, cgfSettingsRegistry);
        _cgfDataDrivenGizmoSystem = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem(
                cgfGizmoRegistry, _cgfGizmoBuffer, isSelectedPredicate: null, interactionBus: _cgfInteractionBus);
        // Route gizmo interaction translators and publisher through the network factory
        // so that CgfSubsystem has no direct dependency on Hrot.Network.NED.
        CycloneNetworkIngressSystem? cgfGizmoIngress = null;
        CycloneEgressSystem? cgfGizmoEgress = null;
        if (_networkFactory != null)
        {
            // CGF is always headless (receives UI interactions from remote viewer).
            var gizmoTranslators = _networkFactory.CreateGizmoTranslators(_cgfInteractionBus, _context.NodeId, headless: true);
            var ingressList = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
            var egressList  = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
            foreach (var t in gizmoTranslators)
            {
                if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Ingress) != 0) ingressList.Add(t);
                if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Egress)  != 0) egressList.Add(t);
            }
            if (ingressList.Count > 0)
                cgfGizmoIngress = new CycloneNetworkIngressSystem(ingressList.ToArray());
            if (egressList.Count > 0)
                cgfGizmoEgress = new CycloneEgressSystem(egressList.ToArray());
            var publisherSystem = _networkFactory.CreateGizmoPublisherSystem(_cgfGizmoBuffer, _context.NodeId);
            if (publisherSystem != null)
                _context.Kernel.RegisterGlobalSystem(publisherSystem);
        }
        var cgfGizmoGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup("GizmoExecution",
            _cgfGizmoManager,
            _cgfDataDrivenGizmoSystem,
            new Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem(cgfStatelessRegistry, _cgfGizmoBuffer));
        // GZH-003: CGF is headless-first; enable only when a terminal connects.
        cgfGizmoGroup.Enabled = false;
        _cgfGizmoController = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController(
            cgfGizmoGroup, _cgfGizmoManager, _cgfDataDrivenGizmoSystem);
        _context.Kernel.RegisterModule(new GizmoInteractionModule(
            _cgfInteractionBus,
            contextIngress: null,
            interactionSystems: new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[]
            {
                cgfGizmoGroup,
            },
            gizmoIngress: cgfGizmoIngress,
            gizmoEgress:  cgfGizmoEgress));
        _context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _cgfInteractionBus));
        // Register canvas menu update so CanvasContextMenuGizmo has state to project.
        _context.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());

        // ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
        // ⭐⭐⭐ cgf==editor SLICE 4 (DQ30) — the no-op time adapter is RETIRED.
        //    📄 docs/DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md §6 item ① ·
        //       docs/UX/UX_Feature_Cgf_Brain_Diagnostics.md §3a · Design_Question_30 §A-§E.
        //    ⛔ It used to read "pause/step are no-ops for slave nodes" — which described the
        //    IMPLEMENTATION, not the intent: ruling 62 says a breakpoint hit on CGF freezes the WHOLE
        //    cluster, and being a slave is why the request travels as an INTENT rather than why it
        //    cannot happen.
        _bpPreTickSnapshot = new EntityRepository();
        CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);

        // ⭐⭐ The halt actuator is the pair of togglable groups built at :461-462; the request path is
        //    the node's orchestration bus, which ClusterOpEgressTranslator already forwards to the
        //    orchestrator's MasterSyncController (the only node holding a roster).
        var bpTimeAdapter          = new Hrot.CGF.Debug.CgfClusterDebugTimeController(
            controlBus:  _context.EventBus,
            inputGroup:  _toggleInput,
            simGroup:    _toggleSim,
            hasCluster:  () => _context?.Participant != null,
            log:         msg => FdpLog<CgfSubsystem>.Warn("[CGF-DEBUG] {0}", msg));
        _debugTimeController        = bpTimeAdapter;
        // ⭐⭐ Slice 1 — HOISTED to a field, not because the breakpoint compiler needed it there, but
        //    because the AiShared shell REQUIRES an IComponentEditService and building a second one in
        //    RegisterWindows would be two implementations of one concept (ruling 9).
        var bpEditSvc              = new ComponentEditServiceBuilder().Build();
        _facetEditService          = bpEditSvc;
        // See BP-29: without _blueprintRegistry, CompileBlueprintVariablePredicate returns a
        // constant-false delegate and blueprint conditional breakpoints silently never fire.
        var bpPredicateCompiler    = new PredicateCompiler(bpEditSvc, _behaviorRegistry, _blueprintRegistry);
        var bpEventScannerCompiler = new EventScannerCompiler(bpEditSvc);
        _bpSnapshotProvider        = new DebugSnapshotProvider(_bpPreTickSnapshot);
        _bpManager                 = new DataBreakpointManager(
            _context.World, _bpPreTickSnapshot, _bpSnapshotProvider,
            bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
        bpTimeAdapter.SetManager(_bpManager);
        _bpSystem                  = new DataBreakpointSystem(_bpManager, _context.World.Bus);

        _context.Kernel.RegisterGlobalSystem(_bpSnapshotProvider);
        _context.Kernel.RegisterGlobalSystem(_bpSystem);

        // ⭐⭐⭐ W3/W5 — THE STAGED-WRITE DRAIN, and this host needs it for the same reason the editor
        //    does. 📄 DESIGN_Staged_Live_Write.md §8.
        // 🔴 W5 removed the drain from DataBreakpointManager.RequestStep/RequestContinue, because the
        //    kernel's PreFrame pull is the one implementation (ruling 9) and a toolbar pause never
        //    calls either method. ⇒ ⛔ WITHOUT THIS LINE a staged edit in a CGF world would queue and
        //    never apply — 📌 exactly the "accepted and silently discarded" failure MIN was built to
        //    end, moved to a second host.
        // 📐 Measured: this is the ONLY other production site that constructs a DataBreakpointManager.
        //    ⚠ Railed by WithNoDrainRegistered_AStagedEditNeverLands, which drives the negative.
        _context.Kernel.RegisterGlobalSystem(
            new Fdp.ModuleHost.Time.ResumeAndDrainSystem(_bpManager));
        // ─────────────────────────────────────────────────────────────────────────

        _context.Kernel.Initialize();

        // ⭐⭐⭐ Slice 4 item ③ — DQ30-C: no WORLD-STATE ingress while the debugger holds the world
        //    frozen; the CONTROL PLANE keeps polling so the resume can arrive.
        //    ⚠ AFTER Kernel.Initialize() on purpose: modules register their ingress systems from
        //    RegisterSystems(), so before this line the scheduler does not yet hold them all.
        WireWorldStateFreezeGate();

        // ── Visualization (non-headless only) ─────────────────────────────────────
        if (!_headless)
        {
            _entityQuery = _context.World.Query().With<NetworkIdentity>().Build();

            _canvas = new MapCanvas();
            _canvas.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);

            _selectionState    = new DefaultSelectionState();
            _fdpRepoAdapter    = new FdpRepositoryAdapter(_context.World);

            // GZ057: add gizmo layer so CGF entity presentation primitives are rendered.
            _cgfGizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, _cgfGizmoBuffer, _cgfInteractionBus!);
            _canvas.AddLayer(_cgfGizmoLayer);
            _canvas.DrawBuffer = _cgfGizmoBuffer;

            // (Phase 5: StandardInteractionTool removed; entity interaction via ECS gizmos)

            // Register context menu handler for right-click in the entity inspector panel.
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                builder.AddItem("Center on entity", () => CenterCameraOnEntity(entity));
                builder.AddItem("Select entity", () =>
                {
                    _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                });
                builder.AddSeparator();
                builder.AddItem("Delete entity", () => DeleteEntity(entity));
                if (_context!.World.HasComponent<Fdp.Core.SimTransform>(entity))
                    builder.AddItem("Rotate", () =>
                    {
                        _selectionState.PrimarySelected = entity;
                        _cgfDataDrivenGizmoSystem!.DeactivateGizmo(entity);
                        var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                            _context.World, entity,
                            onRemove: () => _cgfDataDrivenGizmoSystem!.DeactivateGizmo(entity));
                        _cgfDataDrivenGizmoSystem!.ActivateGizmo(entity, gizmo);
                    });
            }));
        }    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        // Poll network state (e.g. DDS NodeHeartbeat) to keep the cluster cache up-to-date
        // so that BrainMuscleOwnershipStrategy can find the least-loaded Muscle node.
        _cgfNetworkPolling?.Invoke();

        _context?.SlaveTranslator?.Tick();
        _context?.ClusterSlave.Tick();
        _clusterTimeAdapter?.Update();

        // ⭐⭐⭐ Slice 4 (DQ30) — fold the cluster's mode decision and apply DQ30-E's unanswered-freeze
        //    rule. Runs alongside _clusterTimeAdapter.Update() and reads the same non-destructive
        //    buffer; the two are different roles (toolbar transport vs breakpoint freeze), not a
        //    duplicate — see the controller's class remarks.
        _debugTimeController?.ObserveClusterTime();

        // Evict transient primitives and advance persistence clock before backend population.
        _cgfGizmoBuffer?.EndFrame(deltaTime);

        // Use the no-args kernel update so the SlaveSyncController measures the real
        // wall-clock delta between frames.  The legacy Update(float) path would receive
        // dt=0 from the SubsystemOrchestrator in headless mode, zeroing out every
        // DeltaTime-dependent system (e.g. ThreatEvaluationSystem boost/decay).
        // ⭐⭐⭐ Slice 4 §3b — the step actuator brackets EXACTLY ONE kernel update. ⛔ A re-enable that
        //    outlived this bracket would be a silent resume the operator reads as "one step".
        _debugTimeController?.BeginFrame();
        _context?.Kernel.Update();
        _debugTimeController?.EndFrame();

        if (!_headless && _context != null)
        {
            _fdpFrameCount++;
            _canvas?.Update(deltaTime);
        }
        _context?.EventBus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void DrawWorld()
    {
        if (!_headless) _canvas?.Draw();
    }

    /// <inheritdoc/>
    public void DrawUI()
    {
        if (_headless) return;

        // Render the context menu popup via the gizmo layer's ContextMenuAdapter.
        _cgfGizmoLayer?.DrawContextMenu();
    }

    /// <inheritdoc/>
    public MapCameraView? GetCameraView() => _canvas?.Camera?.GetCameraView();

    /// <inheritdoc/>
    public void ApplyCameraView(MapCameraView view) => _canvas?.Camera?.ApplyCameraView(view);

    // Non-interface helper kept for backward-compat with tests.
    public MapCamera? GetMapCamera() => _canvas?.Camera;

    /// <inheritdoc/>
    public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_headless) return;

        // Create a map-pick bridge so component fields tagged [MapPickable] can be edited.
        CanvasMapPickAdapter? cgfCanvasAdapter = _canvas != null && _context?.World != null
            ? new CanvasMapPickAdapter(_canvas, _context.World, globalGizmoManager: _cgfGizmoManager)
            : null;
        MapPickServiceBridge? cgfPickBridge = cgfCanvasAdapter != null
            ? new MapPickServiceBridge(cgfCanvasAdapter, _context!.World)
            : null;

        // ⭐⭐ A9 — this argument is the PERSPECTIVE (see the helper's own doc), so it moves with the
        //    rename; the spawned watch windows' id prefix moves cgf_watch_* → scenario_watch_* and that
        //    is harmless (those ids embed a fresh Guid). ⭐ Sharing ids with the editor is SAFE and
        //    intended: §1b — editor and cgf can never run in one process.
        FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
            _fdpEntityInspector,
            windowManager,
            "Scenario",
            () => _fdpRepoAdapter,
            cgfPickBridge,
            TitleBarColor);

        windowManager.RegisterWindow(new FdpEntityInspectorWindow(
            "cgf_fdp_inspector", "CGF Entity Inspector", "Scenario",
            _fdpEntityInspector,
            () => _fdpRepoAdapter,
            () => _fdpInspectorState,
            TitleBarColor));

        // Register the blackboard view provider so the editor projects typed DTO params.
        _fdpEntityInspector.Reflector.AddBufferViewProvider(new BrainBlackboardViewProvider());
        // Register the heavy blackboard view provider for Blackboard1024.
        _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.Blackboard1024ViewProvider());

        // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType/HeavyDtoType to StructEdit.
        var capturedRegistry = _behaviorRegistry;
        _fdpEntityInspector.Reflector.EditContextFactory = (session, e, type) =>
        {
            if (type != typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)
             && type != typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024)) return null;
            if (!session.HasComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))) return null;
            var ds = session.GetComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))
                as Fdp.Toolkit.Behavior.Components.BehaviorState?;
            if (ds == null) return null;
            if (capturedRegistry?.TryGetDefinition(ds.Value.ActiveBehaviorHash, out var def) != true) return null;
            if (def == null) return null;
            if (type == typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard))
            {
                if (def.ParamsDtoType == null) return null;
                return new StructEdit.Core.EditContext().With("ParamsDtoType", def.ParamsDtoType);
            }
            // Blackboard1024
            if (def.HeavyDtoType == null) return null;
            return new StructEdit.Core.EditContext().With("HeavyDtoType", def.HeavyDtoType);
        };

        windowManager.RegisterWindow(new FdpEventBrowserWindow(
            "cgf_fdp_events", "CGF Event Browser", "Scenario",
            _fdpEventBrowser,
            TitleBarColor));

        windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
            "cgf_architecture_diagnostics", "CGF Architecture Diagnostics", "Scenario",
            new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => _context?.Kernel)),
            TitleBarColor));

        // BP-327 — global window: the module/system execution-stats profiler.
        windowManager.RegisterWindow(new SystemProfilerWindow(
            "cgf_system_profiler", "CGF System Profiler", "Scenario",
            () => _context?.Kernel?.GetExecutionStats(),
            TitleBarColor));

        // ── Time transport controls in status bar ─────────────────────────
        var bus = _context?.EventBus;
        if (bus != null)
        {
            _clusterTimeAdapter = new ClusterTimeTransportAdapter(
                bus, () => _context?.Kernel.CurrentTime.TotalTime ?? 0.0);
            var timeSection = new ClusterTimeControlStatusBarSection(_clusterTimeAdapter);
            windowManager.StatusBar.RegisterSection(
                id:             "cgf_time_controls",
                sortOrder:      100,
                renderDelegate: timeSection.Render,
                perspective:    "Scenario");

            // ⭐⭐⭐ CE-016 — the TOOLBAR transport, and this is a silent-default fix, not a new feature.
            //    📐 Measured `2026-08-25`: the editor registers `MainToolbarTimeControlSection` on its
            //    toolbar (`EditorSubsystem:4715`) AND a status-bar section; CGF and SimHost had only the
            //    status-bar one — ⛔ while HOLDING the very dependency the toolbar section needs, two
            //    lines up. 📌 "A production caller that HAS a dependency must PASS it."
            //    ⭐ Same shared section class, same `ITimeTransportFacade` seam, same ids and sort orders
            //    as the editor — ⛔ nothing invented: a runtime node that can be paused by a breakpoint
            //    (slice 4) plainly wants the transport where the editor puts it.
            if (windowManager.MainToolbar != null)
            {
                var toolbarTimeSection =
                    new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(_clusterTimeAdapter);

                windowManager.MainToolbar.RegisterEntry(
                    "TimeControlGroup", sortOrder: 0,
                    declaredHeight: Fdp.Presentation.WindowManager.MainToolbarManager.DefaultEntryHeight,
                    toolbarTimeSection.Render);

                windowManager.MainToolbar.RegisterSeparator(
                    "ToolbarSep_TimeToPersp", sortOrder: 10);
            }
        }

        // Register the AI Behaviors log tab (dedicated tab for structured AI diagnostics).
        windowManager.MessageLogRegistry?.RegisterSource(AiBehaviorLogTarget.SharedInstance);

        // ⭐⭐⭐ cgf==editor SLICE 1 — the AiShared shell. 📄 §3/§4 of the owning design.
        BuildAiShell(windowManager);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>cgf==editor slice 1 — CGF constructs the SAME AiShared shell the editor builds, and
    /// registers the SAME windows under the asset perspectives.</b>
    /// 📄 <c>docs/DESIGN_Cgf_Editor_Sharing_Slice1_Shell_Adoption.md</c> §3 *(classDiagram)* · §4
    /// *(sequenceDiagram)* · §5 *(the items)*.
    ///
    /// <para>⭐⭐ <b>Every type here already exists</b> — this method CONSTRUCTS and REGISTERS, and
    /// modifies nothing inside <c>Hrot.Editor.AiShared</c> *(§7: that assembly belongs to the
    /// variable-model lane; an AiShared change is a STOP-and-coordinate, not an edit)*. ⇒ the whole
    /// slice is a composition block, mirroring <c>EditorSubsystem.RegisterWindows</c>
    /// <c>:2545-2948</c>.</para>
    ///
    /// <para>⭐⭐ <b>The perspectives are EMERGENT.</b> ⛔ Nothing here declares a perspective list:
    /// <c>WindowManager.GetPerspectives()</c> derives it from what the registrars registered, and
    /// <c>LocalWindowController.ResolveStartupPerspective</c> then picks a real one *(<c>UXI-06</c>,
    /// already built)</para>
    ///
    /// <para>⚠ <b>Ids are shared with the editor deliberately</b> — 📐 the runner throws if both are in
    /// one process *(§7 "process exclusivity")*, so <c>ai_watch_btree</c> here and in the editor can
    /// never collide. ⭐ And the conformance suite compares by <c>PanelKind</c>, not by id.</para>
    /// </summary>
    private void BuildAiShell(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_context == null || _facetEditService == null) return;

        // ── The switcher (§2 :2545) ────────────────────────────────────────────
        // ⭐ The SAME type ClusterRunner/Program.cs already wraps this WindowManager in for
        //   GET /perspectives — ⛔ not a second switcher; this one drives the document manager.
        _perspectiveSwitcher = new Hrot.Editor.AiShared.Documents.WindowManagerPerspectiveSwitcher(windowManager);

        // ── The shared services (§2 :2561 / :2719) ─────────────────────────────
        // ⭐⭐⭐ SLICE 2 (CE-012/013) — the catalog is POPULATED now. ⚠ Slice 1 built a bare
        //    `new AssetCatalog()` and said so; that is what made every window show its empty state.
        _aiCatalogBuilder = BuildAssetCatalog();
        var catalog       = _aiCatalogBuilder.Catalog;

        // ⭐ THREE contributors now, matching the editor: slice 1 had only Blueprint's because
        //   Hrot.CGF did not reference the BTree/HSM editor assemblies. It does *(CE-012)*.
        var referenceCatalog = new Hrot.Editor.AiShared.References.ReferenceCatalog(
            catalog,
            new Hrot.Editor.AiShared.References.IReferenceCatalogContributor[]
            {
                new Hrot.BTree.Editor.Catalog.BTreeBlackboardVariableContributor(),
                new Hrot.BTree.Editor.Catalog.BTreeComposedBlueprintReferenceContributor(),
                new Hrot.Hsm.Editor.Catalog.HsmReferenceContributor(),
                new Hrot.Blueprints.Editor.Catalog.BlueprintReferenceContributor(),
            });

        var refactorService = new Hrot.Editor.AiShared.Refactor.RefactorService(
            referenceCatalog, catalog, new Hrot.Editor.AiShared.Refactor.AtomicMultiFileWriter());

        var debugRegistry = new Hrot.Editor.AiShared.Debug.DebugSessionRegistry();

        // ⭐ The behaviour-action schema, reflected from the already-loaded game assemblies — the same
        //   Rebuild() the editor performs at :2610. CGF loads Hrot.AI.Behaviors, so this is populated.
        var schemaExporter = new Hrot.Editor.AiShared.Blackboard.ActionSchemaExporter();
        schemaExporter.Rebuild();

        // ── The two clock signals (§5 item ①) ──────────────────────────────────
        // ⭐⭐⭐ REQUIRED by PerspectiveWorkspaceServices, and supplied from CGF's REAL state — ⛔ never
        //    a silent default (the 2026-08-16 rule; the ctor throws on null anyway).
        // ⚠⚠ NOTE THE HONEST DIFFERENCE FROM THE EDITOR. The editor's `isSimUp` is
        //    IPreviewController.IsInPreviewMode: it has a PLANNING state in which no world ticks.
        //    ⛔ CGF has no planning state — it is a cluster node whose world ticks from boot — so the
        //    truthful answer here is "the simulation systems are enabled". ⇒ this host reads Running
        //    where the editor reads Planning, and that is a real difference between the hosts, ⛔ not
        //    a wiring gap to paper over with a constant.
        Func<bool> isSimUpSignal  = () => _context != null && (_toggleSim?.Enabled ?? false);
        // ⭐ Both arms of ruling 15, read through the SAME objects the CGF debugger uses: a breakpoint
        //   pause, or the clock itself being halted (deterministic stepping / a cluster pause).
        Func<bool> isFrozenSignal = () => (_bpManager?.IsPaused ?? false)
                                       || Fdp.Toolkit.Time.SimClock.Of(_context?.World).IsHalted;

        var perspectiveServices = new Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceServices(
            catalog, refactorService, debugRegistry, _facetEditService,
            isSimUp:  isSimUpSignal,
            isFrozen: isFrozenSignal)
        {
            // ⭐ CGF HAS a breakpoint manager, so it is PASSED — this is what makes the Watch and
            //   Breakpoints windows exist at all (the registrar builds them only when it is non-null).
            BreakpointManager = _bpManager,
            SchemaExporter    = schemaExporter,

            // ⭐⭐ L0.4 (R-122) — the Details context reads entity selection from the WORLD. `_context`
            //    is nulled on shutdown, so the world is resolved AT CALL TIME rather than captured.
            EntitySelection = new Hrot.Editor.AiShared.Shell.WorldEntitySelectionSource(
                () => _context?.World),

            // ⭐⭐ BP-511 — the staging⇄runtime bridge. CGF HAS both halves (it PUBLISHES the table and
            //    owns the world), so it passes them; ⛔ the resolver is Fdp.Toolkits' one
            //    NetworkIdResolver, not a second lookup.
            EntityIdentity = new Hrot.Editor.AiShared.Variables.WatchEntityIdentity(
                _stagingRemap,
                runtimeId => Fdp.Toolkit.Replication.Services.NetworkIdResolver
                                 .FindEntityByNetworkId(_context?.World, runtimeId),
                RuntimeNetworkIdOf),

            // ⛔⛔ EntityPicker is deliberately ABSENT, and it is NOT a silent default: 📐 measured —
            //    AQ55's pick is IMapPickService.PickEntityAsync, which lives in Hrot.ExCon and is
            //    implemented only by the editor's EditorMapPickAdapter and ExCon's own logic. CGF
            //    references neither and has no such capability, so the Watch's "pick an entity…" entry
            //    is ABSENT here rather than dead (the property's own remark asks for exactly that).
            //
            // ⛔⛔ StagedWrites is absent for two reasons, and BOTH are load-bearing:
            //    ① it resolves a row's address through BlueprintLiveValueWriter, which needs an
            //      IBlueprintDebugSession — CGF constructs none, so a writer here could only refuse;
            //    ② 🔒 the 2026-08-25 STEER keeps the LIVE VARIABLE-VALUE write off this host on purpose:
            //      it carries R-52, a whole-component write that clobbers a tick of BTree/HSM state
            //      (a live-corruption bug that bites the editor too, needing SetComponentFieldRaw), and
            //      it is the variable-model lane's frozen territory.
            //    ⚠⚠ NOT because "slice 1 is read-only" — that framing is SUPERSEDED (design §10). The
            //      windows are taken WHOLESALE; ⭐ this is the ONE place a gate is honest, and the reason
            //      is CORRUPTION, not policy.
        };

        // ── One selection store per perspective, over ONE shared entity selection ──
        var btreeStore     = new Hrot.Editor.AiShared.Selection.EditorSelectionStore(_sharedEntitySelection);
        var hsmStore       = new Hrot.Editor.AiShared.Selection.EditorSelectionStore(_sharedEntitySelection);
        var blueprintStore = new Hrot.Editor.AiShared.Selection.EditorSelectionStore(_sharedEntitySelection);

        // ── CreateRegistrar per asset perspective (§5 item ②) ──────────────────
        // ⚠ NO validators on any of the three, and each SAYS so rather than expressing it by omitting
        //   an argument: the BTree/HSM validators live in editor assemblies CGF does not reference,
        //   and Blueprint has none on either host.
        // ⚠ NO liveValueProvider / writeLive on BTree and HSM — the honest answer per the signature.
        //   ⛔ Blueprint gets none EITHER on this host, and that differs from the editor for a measured
        //   reason: BlueprintLiveValueProvider reads through debugRegistry.ActiveSession, and nothing
        //   on CGF ever puts an IBlueprintDebugSession there (there is no document manager driving
        //   SyncActiveDebugSession). ⭐ Passing one would be a provider that can only answer null.
        var noValidators = System.Array.Empty<Hrot.Editor.AiShared.Validation.IAssetValidator>();

        _btreeRegistrar     = perspectiveServices.CreateRegistrar("BTree",     btreeStore,     noValidators);
        _hsmRegistrar       = perspectiveServices.CreateRegistrar("HSM",       hsmStore,       noValidators);
        _blueprintRegistrar = perspectiveServices.CreateRegistrar("Blueprint", blueprintStore, noValidators);

        _btreeRegistrar.RegisterWindows(windowManager);
        _hsmRegistrar.RegisterWindows(windowManager);
        _blueprintRegistrar.RegisterWindows(windowManager);

        // ── The document manager + the graph canvases (§2 :2948) ───────────────
        _aiDocumentManager = new Hrot.Editor.AiShared.Documents.AiDocumentManager(_perspectiveSwitcher);
        _perspectiveSwitcher.SetDocumentManager(_aiDocumentManager);

        // ⭐ The canvas's whole dependency is the document manager; the renderer is stateless, so one
        //   per perspective is what the editor does too (:3740).
        var adapters = new Hrot.Editor.AiShared.Adapters.AiEditorAdapterBundle(windowManager.Atlas);

        RegisterCanvas(_btreeRegistrar,     "BTree");
        RegisterCanvas(_hsmRegistrar,       "HSM");
        RegisterCanvas(_blueprintRegistrar, "Blueprint");

        void RegisterCanvas(Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar registrar, string kind)
        {
            var renderer = new NodeEditor.UI.Canvas.CanvasRenderer();
            registrar.RegisterExtraWindow(windowManager,
                new Hrot.Editor.AiShared.Windows.AiGraphCanvasWindow(
                    assetKind:  kind,
                    docManager: _aiDocumentManager!,
                    renderer:   new Hrot.Editor.AiShared.Windows.DelegatingCanvasRenderSeam(
                        renderDelegate:    view => renderer.Render(view, null),
                        renderWithFindBar: (view, fb, cmds) => renderer.Render(view, fb, cmds)),
                    pickers: adapters.PickerRegistry,
                    input:   adapters.InputSource));
                    // ⛔ saveDocument is absent, and ⚠⚠ NOT as a gate on editing — 🔒 the 2026-08-25
                    //   STEER forbids gating, and this is not one. 📐 Measured: it is the
                    //   save-on-CLOSE callback for a DIRTY OPEN DOCUMENT, and CGF can open no document
                    //   at all — no document factories are registered with the AiDocumentManager and
                    //   the AssetCatalog is empty (CE-009). ⇒ ⭐ it could never fire, so a delegate here
                    //   would be unreachable code, not a capability.
                    // ⚠ It is passed the day CGF can open an asset — the same day CE-009 closes.
        }

        // ── The Blueprint perspective's two host-specific windows ──────────────
        // ⭐⭐⭐ MEASURED `2026-08-25`, and this is why they are here rather than left to the registrar:
        //    the conformance diff reported `my-blueprint` DIFFERENT — the editor publishes
        //    BlueprintMyBlueprintWindow (7 sections, "No blueprint open.") under the id
        //    `ai_my_blueprint_blueprint`, which REPLACES the registrar's generic AiMyBlueprintWindow at
        //    the same id, while this host published only the generic one ("No asset selected.", 0
        //    sections). ⇒ ⛔ the SAME KIND was being served by two different classes on the two hosts.
        // ⭐ Both live in Hrot.Blueprints.Editor, which this assembly already references — so the port
        //   is a construction, not a new capability.
        // ⚠ Kept in a local: slice 2 must RETARGET it when the active document changes, exactly as the
        //   editor does — an outline nobody retargets renders "No blueprint open." for ever (CE-015b).
        var blueprintOutline = new Hrot.Blueprints.Editor.Windows.BlueprintMyBlueprintWindow();
        _blueprintRegistrar.RegisterExtraWindow(windowManager, blueprintOutline);
        _blueprintRegistrar.RegisterExtraWindow(windowManager,
            new Hrot.Blueprints.Editor.Windows.BlueprintBookmarksWindow(_aiDocumentManager));

        // ── The DOCUMENT FACTORIES (CE-015) ────────────────────────────────────
        // ⭐⭐⭐ MEASURED `2026-08-25`, and this is the finding that separated "the asset opens" from
        //    "the asset is USABLE". 📐 With the catalog populated and `POST /assets/{id}/open` working,
        //    the cluster's graph-canvas reported `hasActiveDocument: true` — and MyBlueprint still said
        //    "No blueprint open." while Details said "No document is open."
        // 🔴 The cause: `AiDocument.ViewState` is populated by a DOCUMENT FACTORY subscribed to
        //    `DocumentOpened`, and CGF had none. ⇒ the document existed and carried NO view state, so
        //    every surface that reads through the canvas context saw nothing.
        // ⭐⭐ This mirrors `EditorSubsystem` `:3916-3963` — the same three factories, the same
        //    arguments, minus the debug sessions this host genuinely does not construct.
        var bpChannelCatalog     = Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance;
        var behaviorActions      = new Hrot.Blueprints.Editor.ActionCatalog.BehaviorActionCatalog(
                                       bpChannelCatalog, schemaExporter);
        var blueprintPalette     = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreatePaletteRegistry(
                                       bpChannelCatalog, behaviorActionCatalog: behaviorActions);
        var blueprintEditService = new Hrot.Blueprints.Editor.NodeDrawers.EditService();
        var blueprintPeerCatalog = new Hrot.Blueprints.Editor.BlueprintPeerSource(
                                       Hrot.Editor.AiShared.AssetRoots.AssetsFor(
                                           Hrot.Editor.AiShared.AssetKind.Blueprint));

        _aiDocumentManager.DocumentOpened += doc =>
        {
            if (doc.ViewState != null) return;   // already populated (re-open of an existing doc)

            switch (doc.Kind)
            {
                case Hrot.Editor.AiShared.AssetKind.BTree:
                    // ⚠ `btreeDebugSession: null` — CGF constructs none (slice 1 §9.4). ⛔ Not a silent
                    //   default: the parameter exists so a host without one can say so.
                    doc.ViewState = Hrot.BTree.Editor.Host.BTreeDocumentFactory.Build(
                        doc.Asset, adapters, btreeStore,
                        btreeDebugSession: null,
                        breakpointManager: _bpManager,
                        actionSchema:      schemaExporter,
                        assetCatalog:      catalog,
                        openBlueprint:     a => _aiDocumentManager?.Open(a));
                    break;

                case Hrot.Editor.AiShared.AssetKind.Hsm:
                    doc.ViewState = Hrot.Hsm.Editor.Host.HsmDocumentFactory.Build(
                        doc.Asset, adapters,
                        hsmDebugSession:   null,
                        breakpointManager: _bpManager);
                    break;

                case Hrot.Editor.AiShared.AssetKind.Blueprint:
                    doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
                        doc.Asset, adapters, blueprintEditService, blueprintPalette,
                        channelCommands:  bpChannelCatalog,
                        peerAssetCatalog: blueprintPeerCatalog,
                        behaviorActions:  behaviorActions,
                        debugSession:     null);
                    break;

                default:
                    // Scenario / Blackboard / Utility are not document-backed kinds.
                    break;
            }

            // ⭐⭐⭐ MA-003 — MARK THE DOCUMENT DIRTY WHEN ITS ASSET CHANGES.
            // 📄 docs/DESIGN_Mcp_Authoring.md §10.4.
            //
            // 🔴🔴 MEASURED `2026-08-25`, and it made CGF's save a SILENT NO-OP after any edit.
            //    📐 `SaveAllAiDocumentsCommand.Execute` skips a document whose `IsDirty` is false, and
            //    `AiDocument.MarkDirty` had exactly ONE production caller in the repo — the EDITOR's
            //    `DocumentOpened` factory (`EditorSubsystem:4016`), which subscribes `Asset.Changed`
            //    ⛔ and does so only when a regeneration scheduler exists. ⇒ CGF, which has none and
            //    never subscribed, could edit a graph and then write NOTHING, reporting success.
            // ⭐ This is the editor's subscription trimmed to what this host has: no scheduler (CGF
            //   regenerates nothing — the reload pipeline recompiles from the in-memory asset), just
            //   the dirty mark that makes CE-020's save reach the file.
            doc.Asset.Changed += () => doc.MarkDirty();
        };

        // ── RETARGET ON ACTIVE-DOCUMENT CHANGE (CE-015b) ───────────────────────
        // ⭐⭐⭐ MEASURED `2026-08-25`, second half of the same finding. With the factories wired the
        //    cluster's `graph-canvas` matched the editor EXACTLY (breadcrumb and all) — and
        //    `my-blueprint` still said "No blueprint open." and `details` still had `assetId: null`.
        // 🔴 Cause: those two do NOT read the document manager. `DetailsContextBuilder.Build` reads the
        //    perspective's `EditorSelectionStore.ActiveAsset`, and `BlueprintMyBlueprintWindow` holds
        //    its own retargeted model — both of which the EDITOR feeds from an `ActiveChanged` handler
        //    (`EditorSubsystem:3012`). ⇒ ⛔ opening a document is NOT enough; the active document has
        //    to be PUSHED into the stores, or every asset-scoped surface reads null forever.
        // ⭐ This is the editor's handler, trimmed to what this host has: the three stores, and the
        //   Blueprint outline. ⛔ No picker-drawer rebuild (no facet pickers here), no legacy store,
        //   no signature window (not registered on CGF).
        _aiDocumentManager.ActiveChanged += () =>
        {
            var active = _aiDocumentManager.Active;

            btreeStore.ActiveAsset     = active?.Kind == Hrot.Editor.AiShared.AssetKind.BTree     ? active.Asset : null;
            hsmStore.ActiveAsset       = active?.Kind == Hrot.Editor.AiShared.AssetKind.Hsm       ? active.Asset : null;
            blueprintStore.ActiveAsset = active?.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint ? active.Asset : null;

            if (active?.Kind == Hrot.Editor.AiShared.AssetKind.Blueprint)
            {
                // ⭐ The BlueprintAsset lives on the canvas context the factory just built.
                var ctx = active.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
                blueprintOutline.Retarget(
                    editableAsset:  active.Asset,
                    blueprintAsset: ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset,
                    hostServices:   ctx?.View.Host,
                    commands:       ctx?.Commands ?? new NodeEditor.Core.Action.EditorCommandsImpl(),
                    view:           ctx?.View,
                    currentGraphId: ctx?.CurrentGraphId,
                    indicators:     ctx?.Indicators);
            }
            else
            {
                blueprintOutline.Retarget(null, null, null, null);
            }
        };

        // ── SLICE 3 (CE-019/020) — SAVE + HOT RELOAD ───────────────────────────
        // 📄 DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md §4/§5/§6 ①②.
        // ⭐⭐⭐ The windows' native editing is taken WHOLESALE (the 2026-08-25 steer) — ⛔ there is no
        //    gating code here and never was. What this block adds is the RUNTIME EFFECT of an edit:
        //    a save path, and a reload that commits the recompiled definition into the SAME registry
        //    the kernel ticks.
        // ⭐ Slice 1 reported this un-wireable because its TRIGGER — a dirty OPEN document — could not
        //   exist on CGF. ⇒ CE-009 removed that blocker; this is the follow-through.
        WireSaveAndReload(windowManager, catalog);

        // ⭐⭐⭐ SLICE 2 (CE-014) — PUBLISH THE ASSET SHELL so the debug API can be handed it.
        // 📄 DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md §3/§5.
        // ⛔⛔ Why this is EXPOSED rather than pushed: on CGF the debug API host is built by
        //    ClusterRunner/Program.cs, ⛔ not by this subsystem — and Hrot.CGF cannot reference
        //    Hrot.Editor (where DebugApiService lives) because Hrot.Editor already references THIS
        //    assembly. ⇒ ⭐ the composition root that can see BOTH does the wiring, exactly as it
        //    already does for AttachPerspectives.
        // ⚠ Assigned even when the debug API is off — this is state about the shell, not about the API.
        AssetShellCatalog   = catalog;
        AssetShellDocuments = _aiDocumentManager;
        AssetShellWindows   = windowManager;

        // ⭐⭐ SLICE 3 (CE-021) — the save/reload actions, published the same way and for the same
        //    reference-wall reason: ClusterRunner/Program.cs is the only assembly that sees both.
        // ⭐ ACTIVATE-then-act: the reload pipeline works on the ACTIVE document, so reloading a
        //   background tab without activating it would recompile the wrong graph.
        AssetShellSave = assetId =>
        {
            ActivateByAssetId(assetId);
            SaveAllAiDocuments();
            return LastSaveStatus;
        };
        AssetShellReload = assetId =>
        {
            ActivateByAssetId(assetId);
            return ReloadActiveAiDocument();
        };

        WireAssetCreation(catalog);

        FdpLog<CgfSubsystem>.Info(
            "[CGF] AiShared shell built — {0} asset(s) indexed, perspectives now include [{1}].",
            catalog.All.Count,
            string.Join(", ", windowManager.GetPerspectives()));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>MA-019</c>/<c>MA-020</c>/<c>MA-022</c> — CREATE on CGF.</b>
    /// 📄 <c>Architect_Question_57_Cgf_Authoring_Packaging.md</c> Q57-A *(construct the per-kind service
    /// dict at THIS root)* and Q57-C *(create reuses the shipped <c>POST /assets</c>)*.
    ///
    /// <para>⭐⭐ <b>The whole of Q57 turned out to be wiring.</b> 📐 Measured: the seam
    /// *(<c>INewAssetService</c>)*, recipe discovery *(<c>RecipePickerSource</c>)* and create-from-recipe
    /// all already live in the SHARED <c>Hrot.Editor.AiShared</c>, and CGF already references the three
    /// per-kind editor assemblies that implement them. ⛔ The only thing behind <c>Hrot.Editor</c> was the
    /// <b>dictionary literal</b> — so no new registry, no new assembly, no new reference *(Q57-A1)*.</para>
    ///
    /// <para>⛔⛔ <b>Scenario is deliberately absent, and that is a measurement, not an omission.</b> 📐
    /// <c>ScenarioNewAssetService</c> takes an <c>IEditorLogic</c> session adapter; CGF has no
    /// <c>IEditorLogic</c>. ⇒ <c>POST /assets {"kind":"Scenario"}</c> answers with the composition
    /// explanation rather than creating something this host cannot open — ⚠ the third-and-fourth kinds
    /// differ per host, and saying so is the honest answer.</para>
    /// </summary>
    private void WireAssetCreation(Hrot.Editor.AiShared.Catalog.AssetCatalog catalog)
    {
        // ⭐ Q57-A1 — the dictionary the editor keeps behind Hrot.Editor, built from services CGF can
        //   already see. ⚠ The JSON roots are passed for the same BUG-A6 reason the editor passes them:
        //   CreateNew must write where THIS host's contributor scans, not into bin/.
        _newAssetServices = new Dictionary<Hrot.Editor.AiShared.AssetKind,
                                           Hrot.Editor.AiShared.Recipes.INewAssetService>
        {
            [Hrot.Editor.AiShared.AssetKind.Blueprint] = new Hrot.Blueprints.Editor.BlueprintNewAssetService(),
            [Hrot.Editor.AiShared.AssetKind.BTree]     = new Hrot.BTree.Editor.BTreeNewAssetService(_btreeJsonRootDir),
            [Hrot.Editor.AiShared.AssetKind.Hsm]       = new Hrot.Hsm.Editor.HsmNewAssetService(_hsmJsonRootDir),
        };

        AssetShellNewAssetServices = _newAssetServices;

        // ⭐⭐ MA-022 — the schema exporter. ⚠ Rebuild() NOW: the exporter is constructed empty and is
        //   otherwise only refreshed by ActionSchemaExporterCatalogWatcher, which this host does not wire.
        //   ⛔ Attaching an un-rebuilt exporter would be worse than none — every kind would report an
        //   EMPTY param list, which reads as "this kind has no params" rather than "nothing reflected".
        var schemaExporter = new Hrot.Editor.AiShared.Blackboard.ActionSchemaExporter();
        schemaExporter.Rebuild();
        AssetShellSchemaExporter = schemaExporter;

        AssetShellCreate = (kindText, name, relPath, recipeName) =>
        {
            if (!Enum.TryParse<Hrot.Editor.AiShared.AssetKind>(kindText, ignoreCase: true, out var kind))
                return (null, $"[ERROR] '{kindText}' is not an AssetKind. Use BTree, Hsm or Blueprint.");

            if (_newAssetServices == null || !_newAssetServices.TryGetValue(kind, out var service))
                return (null, $"[ERROR] This host composes no INewAssetService for {kind}.");

            // ⭐⭐ The recipe is resolved from the kind's OWN AvailableRecipes(), by name — the same list
            //   GET /assets/recipes publishes. ⛔ An unmatched name is REFUSED with the available names
            //   rather than silently falling back to the blank template: creating something other than
            //   what was asked for is the silent-wrong-answer shape MA-004 and MA-017 both caught.
            var (recipe, recipeError) =
                Hrot.Editor.AiShared.Recipes.RecipeByName.Resolve(service, recipeName);
            if (recipeError != null) return (null, recipeError);

            var minted = service.CreateNew(recipe, name, relPath);

            // ⭐ Blueprint is mint-only — the file is written here, at the SOURCE root the contributor
            //   scans. BTree/HSM persist inside CreateNew.
            if (kind == Hrot.Editor.AiShared.AssetKind.Blueprint
             && minted is Hrot.Blueprints.Editor.Variables.BlueprintEditableAssetAdapter adapter)
            {
                var bpPath = Hrot.Editor.AiShared.AssetSavePath.Compose(
                    Hrot.Editor.AiShared.AssetKind.Blueprint, relPath, name,
                    assetRootOverride: _bpRootDir);
                Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(adapter.Asset, bpPath);
            }

            // ⭐ Refresh the assembly contributors, then the JSON contributor for THIS kind — the editor's
            //   BUG-A6 note: RefreshFromAssembly alone leaves a just-written .btree.json undiscovered.
            var aiAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");
            if (aiAsm != null) _aiCatalogBuilder?.RefreshFromAssembly(aiAsm);

            if (kind == Hrot.Editor.AiShared.AssetKind.BTree && _btreeJsonRootDir != null)
                _btreeJsonContrib?.Refresh(rootDirectory: _btreeJsonRootDir);
            if (kind == Hrot.Editor.AiShared.AssetKind.Hsm && _hsmJsonRootDir != null)
                _hsmJsonContrib?.Refresh(rootDirectory: _hsmJsonRootDir);

            // ⚠⚠ The id is returned ONLY once the catalog can resolve it (MA-004). ⛔ Answering with the
            //   minted id before that hands the caller an id GET /assets cannot find.
            var catalogued = catalog.FindByAssetId(minted.AssetId);
            if (catalogued == null)
                return (null,
                        $"[INFO] Created '{minted.Name}', but it is not in the catalog. The file was written "
                      + "outside the directory this host's contributor scans, so nothing can address it — "
                      + "check the asset roots (ruling 67: pass --asset-root on a deployed node).");

            _aiDocumentManager?.Open(catalogued);
            return (catalogued.AssetId, $"[OK] Created {kind}: '{minted.Name}'.");
        };

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Asset creation wired — kinds [{0}], {1} recipe(s) offered.",
            string.Join(", ", _newAssetServices.Keys),
            _newAssetServices.Values.Sum(s => s.AvailableRecipes().Count));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-019</c>/<c>CE-020</c> — SAVE and HOT RELOAD on CGF.</b>
    /// 📄 <c>DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md</c> §6 items ①②④.
    ///
    /// <para>⭐⭐ <b>Mirrors <c>EditorSubsystem</c> <c>:3286-3341</c> *(save)* and <c>:4158-4218</c>
    /// *(the three per-host reload triggers)*.</b> ⛔ Nothing here is new capability: the compile,
    /// the classification *(Cosmetic/Soft/Hard, <c>§17</c>)* and the registry commit all already
    /// exist and are per-host TRIGGERED — CGF simply did not wire the triggers.</para>
    ///
    /// <para>⚠⚠ <b>The two write paths stay distinct, and that is load-bearing</b> *(design §3)*:
    /// this is the ASSET path — edit the graph, write the file, recompile, commit to the registry.
    /// ⛔ It is NOT the live variable-VALUE write *(<c>R-52</c>'s staged <c>Blackboard1024</c>
    /// clobber)*, which stays OFF on this host: <c>writeLive</c> and <c>StagedWrites</c> are still
    /// null in <c>BuildAiShell</c>, and nothing below touches them.</para>
    ///
    /// <para>🔴 <b>Ruling 53 — a HARD reload is a confirmed cluster-wide reset, and the confirm belongs
    /// where the OPERATOR sits.</b> ⛔ This host never pops a modal: it is headless-first, and a modal
    /// on an unattended node is a hang, not a prompt. ⇒ the classification is REPORTED
    /// *(<see cref="LastReloadStatus"/>, and the MCP response)* and the interactive node owns the
    /// confirmation. ⚠ See <c>CE-021</c> — the confirm ROUTE is not built by this slice.</para>
    /// </summary>
    private void WireSaveAndReload(
        Fdp.Presentation.WindowManager.WindowManager windowManager,
        Hrot.Editor.AiShared.Catalog.AssetCatalog catalog)
    {
        if (_context == null || _aiDocumentManager == null) return;

        // ── The reload pipeline (item ①) ───────────────────────────────────────
        // ⭐ The LIGHTWEIGHT FDP coordinator, constructed with the SAME registry instances the kernel
        //   ticks — ⛔ that instance-sharing is the whole mechanism: ApplyQuickReload commits into
        //   `_blueprintRegistry`, and BlueprintTickSystem reads that exact object.
        var reloadCoordinator = new Fdp.Toolkit.Behavior.AiHotReloadCoordinator(
            _behaviorRegistry!, _blueprintRegistry!,
            new Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions());

        // ⭐⭐⭐ RULING 53's ACTUAL REQUIREMENT, and it is NOT a confirm route.
        // 📄 UX_Feature_Modal_Surfaces.md §2.0b: *"Headless never pre-flights — MCP/script/replay
        //    dispatch the authorized request directly. ⚠ The origin still LOGS what it skipped"*, and
        //    §"Risks": *"Headless proceeds silently on destructive work — deliberate — but it means an
        //    MCP agent can [destroy state] with no prompt. The origin-side log IS THE WHOLE SAFETY NET,
        //    so it is a requirement, not a nicety."*
        // ⇒ ⛔ CGF pops NO modal *(it is headless-first; a modal on an unattended node is a hang)*, and
        //   ⭐ this subscription is the safety net that replaces it.
        // ⚠⚠ MEASURED LIMIT: this event is documented *"NOT fired for Quick Reloads"* — it belongs to
        //    the ALC file-watcher path, which this slice does not wire. ⇒ it will not fire today. ⭐ It
        //    is subscribed anyway so the log exists the moment that path is wired, ⛔ and the gap is
        //    REPORTED (CE-023) rather than papered over with a fabricated classification.
        reloadCoordinator.OnHardReloadCompleted += ids =>
            FdpLog<CgfSubsystem>.Warn(
                "[CGF] HARD reload completed — {0} behaviour(s) had live instances RESET. This node is "
              + "headless and did not prompt (ruling 53); this log is the record.",
                ids.Count);

        // ⚠ A CONSOLE, not silence: a failed compile must say so somewhere a headless node can be
        //   read. ⛔ The editor routes this to its message-log window; CGF has no such source, so the
        //   system console is the honest destination — and the status string below is what MCP reads.
        var reloadConsole  = new Hrot.Blueprints.Editor.SystemConsoleOutputConsole();
        var reloadCatalog  = new Hrot.Blueprints.Editor.BlueprintPeerSource(
                                 Hrot.Editor.AiShared.AssetRoots.AssetsFor(
                                     Hrot.Editor.AiShared.AssetKind.Blueprint));

        _quickReload = new Hrot.Blueprints.Editor.Reload.QuickReloadService(
            reloadCatalog,
            new Hrot.Blueprints.Editor.EditorState(),
            reloadConsole,
            new Hrot.Blueprints.Core.Compiler.BlueprintCompiler(),
            reloadCoordinator,
            // ⚠ No debug session on this host (slice 1 §9.4) — the parameter is optional and CGF
            //   genuinely has none. ⛔ Not a silent default.
            session: null);

        // ── The save path (item ②) ─────────────────────────────────────────────
        // ⭐⭐ DEVIATION from §6 ②, argued: the design says "asset→path via AssetRoots".
        //    📐 Measured: `SaveAllAiDocumentsCommand.Execute` already resolves the path from
        //    `asset.SourceFilePath` and SKIPS with a WARNING when it is empty. ⇒ ⛔ a second
        //    AssetRoots-based mapping here would be a competing answer to "where does this asset
        //    live", and the catalog already recorded the real one when it indexed the file.
        //    ⭐ AssetRoots still resolves the reload CATALOG root above — that is a different question.
        _saveBlueprint = (asset, path) =>
        {
            var doc     = _aiDocumentManager?.OpenDocuments
                              .FirstOrDefault(d => d.Asset.AssetId == asset.AssetId);
            var ctx     = doc?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;
            var bpAsset = ctx?.AssetRef as Hrot.Blueprints.Core.Assets.BlueprintAsset;
            if (bpAsset == null) return;
            Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(bpAsset, path);
        };

        _saveBTree = (asset, path) =>
        {
            if (asset is not Hrot.BTree.Editor.Model.BehaviorTreeAsset bt) return;
            var dto  = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(bt);
            var json = Hrot.AiEditor.Persistence.BTree.BTreeJsonServices.Serialize(dto);
            Hrot.AiEditor.Persistence.AtomicFileWriter.Write(
                path, Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json));
        };

        _saveHsm = (asset, path) =>
        {
            if (asset is not Hrot.Hsm.Editor.Model.HsmAsset hsm) return;
            var dto  = Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsm);
            var json = Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Serialize(dto);
            Hrot.AiEditor.Persistence.AtomicFileWriter.Write(
                path, Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(json));
        };

        // ── The main-toolbar affordances (item ④) ──────────────────────────────
        // ⭐⭐⭐ CE-009 §7, discharged for the FIRST time: *"when a later slice adds a feature
        //    CONTROLLED FROM THE TOOLBAR, its button must be wired AND instrumented on CGF too."*
        // ⭐ Registering here makes `MainToolbarManager.Height` non-zero on this host, so the toolbar
        //   now RENDERS as well as publishes — 📌 and the slice-2 rail that asserted CGF had ZERO
        //   entries is updated in the same batch, which is exactly the hand-off §7 designed.
        windowManager.MainToolbar.RegisterEntry(
            "SaveAllAiDocuments", sortOrder: 10,
            Fdp.Presentation.WindowManager.MainToolbarManager.DefaultEntryHeight,
            () => { if (ImGuiNET.ImGui.Button("Save All")) SaveAllAiDocuments(); },
            perspective: null);

        windowManager.MainToolbar.RegisterEntry(
            "QuickReloadAiAsset", sortOrder: 11,
            Fdp.Presentation.WindowManager.MainToolbarManager.DefaultEntryHeight,
            () => { if (ImGuiNET.ImGui.Button("Reload AI")) ReloadActiveAiDocument(); },
            perspective: null);

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Save + hot reload wired — {0} asset(s) indexed, toolbar entries registered.",
            catalog.All.Count);
    }

    /// <summary>
    /// ⭐⭐ <b>Save every dirty open document.</b> ⛔ The SAME shared command the editor uses, with the
    /// same three per-kind delegates — ⛔ not a CGF save path.
    /// </summary>
    internal void SaveAllAiDocuments()
    {
        Hrot.Editor.AiShared.SaveAllAiDocumentsCommand.Execute(
            _aiDocumentManager, _saveBlueprint, _saveBTree, _saveHsm,
            msg => { LastSaveStatus = msg; FdpLog<CgfSubsystem>.Info("[CGF] save: {0}", msg); });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Recompile the ACTIVE document and commit it into the running registry.</b>
    ///
    /// <para>⭐ Three per-host arms, exactly as the editor wires them *(<c>:4158</c>/<c>:4175</c>/
    /// <c>:4198</c>)*: Blueprint compiles from the in-memory <c>BlueprintAsset</c>; BTree and HSM emit
    /// topology + bridge sources and compile those. ⛔ None of them reads the file from disk — ⚠ so a
    /// reload reflects the EDIT, not the last save, which is the editor's own documented behaviour.</para>
    ///
    /// <para>⚠ Returns the status string rather than throwing: a failed compile is a legitimate
    /// outcome of editing, and the caller *(toolbar or MCP)* reports it.</para>
    /// </summary>
    internal string ReloadActiveAiDocument()
    {
        var active = _aiDocumentManager?.Active;
        var ctx    = active?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;

        if (_quickReload == null || active == null)
            return LastReloadStatus = "No active AI document to reload.";

        try
        {
            switch (ctx?.AssetRef)
            {
                case Hrot.Blueprints.Core.Assets.BlueprintAsset bp:
                {
                    var r = _quickReload.TriggerAsync(bp).GetAwaiter().GetResult();
                    return LastReloadStatus = r.Succeeded
                        ? $"Compiled blueprint '{bp.Name}' in {r.DurationMs}ms"
                        : $"Blueprint compile failed: {r.ErrorMessage}";
                }

                case Hrot.BTree.Editor.Model.BehaviorTreeAsset bt:
                {
                    var dto      = Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(bt);
                    var topology = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitTopologyCore(dto);
                    var bridge   = Hrot.AiEditor.Persistence.Emit.BTreeBridgeEmitCore.EmitBridge(dto);
                    var r = _quickReload.TriggerFromSourcesAsync(
                        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
                        $"BTreePatch_{dto.AssetId:N}_{Guid.NewGuid():N}").GetAwaiter().GetResult();
                    return LastReloadStatus = r.Succeeded
                        ? $"Compiled BTree '{dto.Name}' in {r.DurationMs}ms"
                        : $"BTree compile failed: {r.ErrorMessage}";
                }

                case Hrot.Hsm.Editor.Model.HsmAsset hsm:
                {
                    var dto      = Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsm);
                    var topology = Hrot.AiEditor.Persistence.Emit.HsmEmitCore.EmitTopologyCore(dto);
                    var bridge   = Hrot.AiEditor.Persistence.Emit.HsmBridgeEmitCore.EmitBridge(dto);
                    var r = _quickReload.TriggerFromSourcesAsync(
                        new[] { (topology, dto.Name + ".g.cs"), (bridge, dto.Name + ".Registrar.g.cs") },
                        $"HsmPatch_{dto.AssetId:N}_{Guid.NewGuid():N}").GetAwaiter().GetResult();
                    return LastReloadStatus = r.Succeeded
                        ? $"Compiled HSM '{dto.Name}' in {r.DurationMs}ms"
                        : $"HSM compile failed: {r.ErrorMessage}";
                }

                default:
                    // ⚠ A document with no canvas context cannot be recompiled — say WHICH, so the
                    //   caller is not left guessing whether the reload ran.
                    return LastReloadStatus =
                        $"'{active.Asset.Name}' ({active.Kind}) has no compilable canvas context.";
            }
        }
        catch (Exception ex)
        {
            // ⛔ A compile is user input; it must not take the node down. ⭐ Reported, not swallowed.
            FdpLog<CgfSubsystem>.Error("[CGF] reload failed: {0}", ex.Message);
            return LastReloadStatus = $"Reload threw: {ex.Message}";
        }
        finally
        {
            // ⭐⭐ RULING 53's origin-side log, on EVERY reload — not only the Hard ones.
            //    ⛔ A headless node that silently recompiles the brain a live exercise is running is
            //    exactly what the ruling's safety net is for, and the Soft/Hard distinction is NOT
            //    available on this path (CE-023) — so the log records the ACT, not a classification
            //    it cannot honestly make.
            FdpLog<CgfSubsystem>.Info(
                "[CGF] AI asset reload requested for '{0}' — {1}",
                _aiDocumentManager?.Active?.Asset.Name ?? "(none)", LastReloadStatus);
        }
    }

    /// <summary>⭐ The last save report — read by the MCP save route and by rails.</summary>
    internal string LastSaveStatus { get; private set; } = string.Empty;

    /// <summary>⭐ The last reload report — read by the MCP reload route and by rails.</summary>
    internal string LastReloadStatus { get; private set; } = string.Empty;

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-013</c> — build the SAME asset catalog the editor builds.</b>
    /// 📄 <c>DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md</c> §6 item ①.
    ///
    /// <para>⭐⭐ <b>Mirrors <c>EditorSubsystem</c> <c>:986-1061</c>, including the DUAL-LOAD strategy</b>
    /// *(<c>PU-301</c> §3 <c>D4</c>)*: the assembly contributors first, the JSON file contributors after, so
    /// a JSON-authored asset wins an <c>AssetId</c> collision. ⛔ Not a CGF-specific catalog — the same
    /// builder, the same contributor set, so the two hosts cannot index differently.</para>
    ///
    /// <para>⭐ <b>Recursion is not something this method does</b> — 📐 measured: all three file
    /// contributors already enumerate with <c>RecurseSubdirectories = true</c>, so §3a's *"index across
    /// SUBFOLDERS"* holds by construction and <c>SourceFilePath</c> carries the relative folder path.</para>
    ///
    /// <para>🔴 <b>Ruling 67, and it is REPORTED, not silently swallowed.</b> When the source tree is not
    /// found *(a deployed node)* <see cref="Hrot.Editor.AiShared.AssetRoots.ResolveProjectDir"/> answers
    /// null, the JSON roots are null, and this logs a WARNING naming what it searched for. ⛔ The catalog
    /// is then genuinely empty and <c>GET /assets</c> says so — ⚠ a silent empty list is the failure this
    /// slice exists to end.</para>
    /// </summary>
    private Hrot.Editor.AiShared.Catalog.AiAssetCatalogBuilder BuildAssetCatalog()
    {
        // ⭐⭐⭐ RULING 67 RESOLVED — config → source walk-up → output directory, in AiShared's stated
        //    "single authority for roots". ⛔ The bare walk-up that used to be here answered null on a
        //    DEPLOYED node, which is what made authoring on CGF impossible; the config arm is the fix.
        //    ⚠ Always non-null now, so the old "the catalog will be EMPTY" warning is replaced by a
        //    statement of WHICH arm answered — 📌 "empty" and "pointed elsewhere" are different problems
        //    and the log has to distinguish them.
        var aiRootDir = Hrot.Editor.AiShared.AssetRoots.ResolveBase(AiBehaviorsProjectPath);

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Authoring root resolved from {0}.",
            Hrot.Editor.AiShared.AssetRoots.DescribeBase(AiBehaviorsProjectPath));

        if (Hrot.Editor.AiShared.AssetRoots.ConfiguredRoot == null &&
            Hrot.Editor.AiShared.AssetRoots.ResolveProjectDir(AiBehaviorsProjectPath) == null)
        {
            FdpLog<CgfSubsystem>.Warn(
                "[CGF] No configured asset root and no source tree (searched up from CWD + BaseDirectory "
              + "for '{0}'). Falling back to the output directory, so the catalog will be empty unless "
              + "assets were deployed beside the binary. ⇒ ruling 67: pass --asset-root on a deployed node.",
                System.IO.Path.Combine(AiBehaviorsProjectPath));
        }

        string RootFor(Hrot.Editor.AiShared.AssetKind kind) =>
            System.IO.Path.Combine(aiRootDir, Hrot.Editor.AiShared.AssetRoots.AssetsRelative(kind));

        var bpRootDir = RootFor(Hrot.Editor.AiShared.AssetKind.Blueprint);

        // ⚠ No debug session on this host, so BTree symbolication is not wired — the contributor takes it
        //   as an optional argument and CGF genuinely has none (slice 1 §9.4). ⛔ Not a silent default.
        var btreeContrib     = new Hrot.BTree.Editor.Catalog.BTreeAssetContributor(null);
        var hsmContrib       = new Hrot.Hsm.Editor.Catalog.HsmAssetContributor();
        var bpContrib        = new Hrot.Blueprints.Editor.Catalog.BlueprintAssetContributor(bpRootDir);
        var btreeJsonContrib = new Hrot.BTree.Editor.Catalog.BTreeJsonAssetContributor(null);
        var hsmJsonContrib   = new Hrot.Hsm.Editor.Catalog.HsmJsonAssetContributor();

        var btreeJsonRoot = RootFor(Hrot.Editor.AiShared.AssetKind.BTree);
        var hsmJsonRoot   = RootFor(Hrot.Editor.AiShared.AssetKind.Hsm);

        // ⭐ MA-019 — keep the roots and the two JSON contributors reachable: CREATE has to write into the
        //   SAME directory this catalog scans and then Refresh the SAME contributor, or the minted asset
        //   exists on disk and cannot be addressed (the editor's BUG-A6, and ruling 67's own failure mode).
        _bpRootDir        = bpRootDir;
        _btreeJsonRootDir = btreeJsonRoot;
        _hsmJsonRootDir   = hsmJsonRoot;
        _btreeJsonContrib = btreeJsonContrib;
        _hsmJsonContrib   = hsmJsonContrib;

        // ⚠ The null arms are gone: ruling 67's ResolveBase always answers a directory, so "is it there"
        //   is the only remaining question — and a missing root is still worth a warning, since with a
        //   CONFIGURED root it means the config points at a tree with no assets in it.
        if (System.IO.Directory.Exists(btreeJsonRoot))
            btreeJsonContrib.Refresh(rootDirectory: btreeJsonRoot);
        else
            FdpLog<CgfSubsystem>.Warn("[CGF] BTree JSON root not found: {0}", btreeJsonRoot);

        if (System.IO.Directory.Exists(hsmJsonRoot))
            hsmJsonContrib.Refresh(rootDirectory: hsmJsonRoot);
        else
            FdpLog<CgfSubsystem>.Warn("[CGF] HSM JSON root not found: {0}", hsmJsonRoot);

        var builder = new Hrot.Editor.AiShared.Catalog.AiAssetCatalogBuilder(
            btreeContrib, hsmContrib, bpContrib,
            asm => btreeContrib.LoadFrom(asm),
            asm => hsmContrib.LoadFrom(asm),
            ()  => bpContrib.Refresh(),
            bTreeJsonContributor: btreeJsonContrib,
            hsmJsonContributor:   hsmJsonContrib);

        // ⭐⭐ The ASSEMBLY half of the dual load: the compiled BTree/HSM definitions live in the loaded
        //    Hrot.AI.Behaviors assembly, which CGF loads for its own brains. ⛔ Without this the catalog
        //    would carry only the JSON-authored assets and the two hosts would index differently.
        var aiAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Hrot.AI.Behaviors");
        if (aiAsm != null) builder.RefreshFromAssembly(aiAsm);
        else FdpLog<CgfSubsystem>.Warn("[CGF] Hrot.AI.Behaviors assembly not loaded — no compiled AI assets indexed.");

        FdpLog<CgfSubsystem>.Info("[CGF] Asset catalog built — {0} asset(s) indexed.", builder.Catalog.All.Count);
        return builder;
    }

    /// <summary>
    /// ⭐ The runtime <c>NetworkIdentity</c> of an entity, or <c>0</c>. 📌 The inverse direction of
    /// <c>NetworkIdResolver.FindEntityByNetworkId</c>, and the half <c>WatchEntityIdentity</c> needs at
    /// PIN time. ⛔ <c>0</c> for an unreplicated or dead entity — the same "nothing" the editor's own
    /// <c>RuntimeNetworkIdOf</c> answers.
    /// </summary>
    private long RuntimeNetworkIdOf(Entity entity)
        => _context?.World is { } w
        && entity != Entity.Null
        && w.IsAlive(entity)
        && w.HasComponent<NetworkIdentity>(entity)
             ? w.GetComponentRO<NetworkIdentity>(entity).Value
             : 0;

    // ── Private helpers ────────────────────────────────────────────────────────

    private void CenterCameraOnEntity(Entity entity)
    {
        if (_canvas == null || _context == null || !_context.World.IsAlive(entity)) return;

        Vector2 pos;
        if (_context.World.HasComponent<NetworkTransform>(entity))
        {
            ref readonly var nt = ref _context.World.GetComponentRO<NetworkTransform>(entity);
            pos = new Vector2(nt.LastPosition.X, nt.LastPosition.Y);
        }
        else if (_context.World.HasComponent<SimTransform>(entity))
        {
            ref readonly var st = ref _context.World.GetComponentRO<SimTransform>(entity);
            pos = new Vector2(st.Position.X, st.Position.Y);
        }
        else
        {
            return;
        }

        _canvas.Camera.Target = pos;
    }

    private void DeleteEntity(Entity entity)
    {
        if (_context == null || !_context.World.IsAlive(entity)) return;

        if (_context.World.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref _context.World.GetComponentRO<NetworkIdentity>(entity);
            _context.World.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = netId.Value,
                Reason    = "cgf-deleted",
            });
        }

        if (_selectionState?.IsSelected(entity) == true)
        {
            _selectionState.PrimarySelected = null;
            _fdpInspectorState.SelectedEntity = null;
        }
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _cgfNetworkPolling = null;
        _toggleInput = null;
        _toggleSim = null;
        _context?.Kernel.Dispose();
        _physicsModule?.Dispose();
        _physicsModule = null;

        // Guard the participant disposal.
        if (_networkFactory?.Participant == null)
        {
            _context?.Participant?.Dispose();
        }

        _context = null;
    }

    // IEcsModule wrapper that routes TogglableSimulationGroup into the Simulation phase slot.
    // RegisterGlobalSystem rejects SystemPhase.Simulation; it must be registered via RegisterModule.
    private sealed class CgfSimulationModule : IEcsModule
    {
        private readonly TogglableSimulationGroup _group;
        public string Name => "CgfSimulation";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public CgfSimulationModule(TogglableSimulationGroup group) => _group = group;
        public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_group);
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Slice 4 item ③ (<c>DQ30-C</c>) — hands every ingress system on this node the debug
    /// freeze gate.</b> Returns how many it reached.
    ///
    /// <para>⭐⭐ <b>Why a sweep and not a constructor argument.</b> 📐 Measured <c>2026-08-25</c>: CGF's
    /// ingress arrives through <b>five</b> separate registrations — the NED replication module, the
    /// auxiliary / perception / pathfinding translator packs, and
    /// <c>SlaveTimeTranslatorRegistration</c> — and every one of those helpers is shared with SimHost
    /// and IG, which have no debugger to hand over. ⇒ threading a parameter through them would
    /// default it at almost every site, which is exactly the silent-default shape; and it would still
    /// miss any pack added later. ⭐ This reaches all of them, including future ones.</para>
    ///
    /// <para>⭐⭐⭐ <b>It gates the CONTROL PLANE too, and that is safe only because of the category.</b>
    /// <c>SlaveTimeTranslatorRegistration</c>'s ingress system holds the three time translators, all
    /// three now marked <c>ControlPlane</c>, so the gate skips nothing there. ⛔ Were any of them left
    /// at the <c>WorldState</c> default, this node would freeze and never hear its own resume —
    /// <c>DQ30-A</c>'s deadlock. ⚠ That is why the fail-safe default fails LOUDLY, and why the rail
    /// asserts the time translators keep polling rather than merely that world state stops.</para>
    /// </summary>
    private int WireWorldStateFreezeGate()
    {
        if (_context == null || _debugTimeController == null) return 0;

        var gate = _debugTimeController;
        int wired = 0;

        foreach (var system in _context.Kernel.SystemScheduler.GetAllSystems())
        {
            if (system is not Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem ingress) continue;

            ingress.IsWorldStateFrozen = () => gate.IsWorldStateFrozen;
            wired++;
        }

        FdpLog<CgfSubsystem>.Info(
            "[CGF-DEBUG] DQ30-C world-state freeze gate wired onto {0} ingress system(s).", wired);
        return wired;
    }

    // ⛔⛔ RETIRED by cgf==editor slice 4 (DQ30): CgfNoOpTimeController lived here, with all three
    //    request methods empty and only IsPausedByDebugger returning real state — the "interface
    //    called every frame with a dead parameter" variant of the seam law, named as such in UXI-37.
    //    ⭐ Replaced by Hrot.CGF.Debug.CgfClusterDebugTimeController, constructed in Initialize().
}


