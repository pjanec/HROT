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

    /// <summary>
    /// ⭐⭐⭐ <c>CE-046</c> (Axis-C <b>E1</b>) — <b>the SAME scenario session the editor runs, over CGF's own
    /// world and orchestration bus.</b> 📄 <c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c> §3 ③.
    ///
    /// <para>🎯 The user's principle: CGF ≡ the editor bar distributed-vs-no-network ⇒ most stuff shared.
    /// ⭐ Nothing was re-architected for this — <c>EditorScenarioSession</c> already took its world as a
    /// constructor parameter, so *"CGF has no scenario session"* was a wiring gap, not a design gap.</para>
    ///
    /// <para>⛔ This is what retires the <c>saveScenario: null</c> / *"CGF has no IEditorLogic scenario
    /// session"* absence recorded in <see cref="WireSaveAndReload"/> and in <c>MA-019</c> §G.</para>
    /// </summary>
    private Hrot.Editor.AiShared.Scenarios.EditorScenarioSession? _scenarioSession;

    /// <summary>
    /// ⭐⭐ <c>CE-049</c> (Axis-C <b>E2</b>) — the ONE shared create-core *(ruling 9)*, replacing the
    /// ~50-line duplicate of <c>EditorSubsystem.CreateAssetCore</c> that used to live inline in
    /// <see cref="WireAssetCreation"/>. 📄 <c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c> §3 ②.
    /// ⭐ Held as a field so both the MCP surface *(<see cref="AssetShellCreate"/>)* and the interactive
    /// New-Asset dialog run the same instance.
    /// </summary>
    private Hrot.Editor.AiShared.Browser.AssetCreateController? _assetCreateController;

    /// <summary>
    /// ⭐⭐ <c>CE-049</c> — CGF's OWN shell picker registry, separate from the canvas windows'
    /// <c>AiEditorAdapterBundle.PickerRegistry</c>.
    /// <para>⚠⚠ <b>Separate DELIBERATELY, and the editor learned this first</b> *(<c>BATCH-29</c>: "Separate
    /// from adapterBundle.PickerRegistry (which canvas windows already DrawFrame) to avoid
    /// double-DrawFrame")*. ⛔ Reusing the canvas registry here would draw every shell picker twice.</para>
    /// </summary>
    private NodeEditor.UI.Picker.PickerRegistry? _shellPickers;
    private NodeEditor.UI.Dialogs.SaveAsBrowserDialog? _saveAsBrowser;
    private NodeEditor.Core.Interfaces.IIconProvider? _shellIconProvider;
    private Hrot.Editor.AiShared.Browser.AssetPickerLauncher? _assetPickerLauncher;
    private Hrot.Editor.AiShared.Browser.NewAssetLauncher? _newAssetLauncher;

    /// <summary>
    /// ⭐⭐ <c>CE-054</c> — the perspective-switch radio group. Held as a field for the same reason the
    /// editor holds one: the section registers toolbar entries at construction and must outlive the
    /// composition scope. ⛔ <c>null</c> on a toolbar-less host.
    /// </summary>
    private Fdp.Presentation.WindowManager.PerspectiveToolbarSection? _perspectiveToolbarSection;
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
    /// ⭐⭐⭐ <c>CE-059</c> — the AI-graph debug session and the registry the <c>debug.*</c> toolbar group
    /// reads. ⚠ Both were reachable-but-unbuilt: the registry was a LOCAL in <c>BuildAiShell</c>, and the
    /// session was never constructed although this file already holds all three of its ctor arguments
    /// (<c>_blueprintRegistry</c>, the world, and <c>CgfClusterDebugTimeController</c>).
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <c>CE-061</c> — the Scenario-perspective panels and their adapters.
    /// 📄 <c>docs/DESIGN_Cgf_Scenario_Windows_Slice.md</c>. ⛔ All shared types; ⚠ Preview and Zone are
    /// deliberately absent (design §4 — the editor-only planning state and the un-moved gizmo).
    /// </summary>
    private Hrot.Map.Common.Config.MapViewConfig?                _mapViewConfig;
    private SpawnerPanel?                                        _spawnerPanel;
    private MissionPanel?                                        _missionPanel;
    private ConfigPanel?                                         _configPanel;
    private SharedOrbatPanel?                                    _sharedOrbatPanel;
    private Hrot.UI.Common.Adapters.ScenarioSpawnAdapter?        _spawnAdapter;
    private Hrot.UI.Common.Adapters.ScenarioMissionService?      _missionService;
    private Hrot.UI.Common.Adapters.ScenarioMapConfigAdapter?    _mapConfigAdapter;
    private Hrot.UI.Common.Adapters.ScenarioOrbatAdapter?        _orbatAdapter;

    private Hrot.Editor.AiShared.Debug.DebugSessionRegistry?     _aiDebugRegistry;

    /// <summary>
    /// ⭐⭐ <c>CE-071</c> — CGF's shared comparison session registry, kept on the instance so the three
    /// document-factory <c>Build</c> sites can compose the canvas annotation renderer against the SAME
    /// state the comparison panels read. 📄 <c>docs/DESIGN_Comparison_Ui_Mounting.md</c> §4 `D2`/`D3`.
    /// </summary>
    private Hrot.Editor.AiShared.Comparison.ComparisonSessionRegistry? _comparisonSessionRegistry;
    private Hrot.Blueprints.Core.Debug.BlueprintDebugSession?    _blueprintDebugSession;

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
            // ⭐⭐⭐ BP-487 — CGF's OWN map feed: the very buffer its DebugGizmoLayer draws (line ~1096) and
            //    its canvas takes as DrawBuffer (line ~1098), fed by GlobalGizmoManager + StatelessGizmoSystem.
            //    ⇒ GET /panels/_gizmo now reports what CGF's map is actually submitting, which is the ONE
            //    channel that can answer "does the cluster's map draw the scenario's entities?" — the user's
            //    `2026-08-27` symptom. 📄 DESIGN_Subsystem_Composition_Unification.md §5.6.
            // ⚠ Lazy for the measured reason in SubsystemDebugProvider's ctor remarks: the buffer is created
            //   in Initialize (line ~851), AFTER the composition root builds this provider.
            gizmoBuffer:   () => _cgfGizmoBuffer,
            // ⭐⭐⭐ CE-066 — CGF's OWN mission editor: the very ScenarioMissionService its Mission panel
            //    commits through (built at line ~1095, the SAME shared adapter EditorSubsystem:1962 builds).
            //    ⛔ Until this line, all four `/missions/*` routes answered "no mission service" on
            //    `--mode all` while this host had one the whole time — and that omission is also why the
            //    routes sat UNCLASSIFIED in CapabilityFor, which kept the manifest rail red.
            // ⚠ Lazy: the service is created during window registration, well after this provider is built.
            missionEditor: () => _missionService,
            // ⭐⭐⭐ CE-110 — CGF's OWN catalog. ⭐ Read off the world singleton (registered by CE-111 in
            //    RegisterDomainComponents) rather than from `_context.TkbDb` directly, so that what /tkb/*
            //    reports is provably the instance CGF's CreateEntityRequestSystem and NetworkSpawningSystem
            //    resolve against — 📌 see TkbFrom's remarks on why a private handle is the subtler lie.
            tkbDb:         Hrot.Presentation.DebugApi.SubsystemDebugProvider
                               .TkbFrom(() => _context?.World),
            // ⭐⭐ HN-029: the node's own orchestration bus — the same one its ClusterSlave and
            //    ClusterOpEgressTranslator sit on, so a transition requested here reaches the master by the
            //    path the operator's own "Load into Live" button takes.
            requestTransition: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                   .TransitionsVia(() => _context?.EventBus),
            // ⭐⭐ MD-002 — CGF's own kernel snapshot, the same one its Architecture Diagnostics window
            //    already renders (line ~1038). ⚠ Lazy: _context is null until Initialize.
            // ⭐⭐ MD-006 — same bus, same argument as requestTransition above.
            requestDiagnosticDump: Hrot.Presentation.DebugApi.SubsystemDebugProvider
                                       .DumpsVia(() => _context?.EventBus),
            architecture:  () => _context?.Kernel is null
                                 ? null
                                 : new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(
                                       () => _context?.Kernel));

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

        // ⭐⭐⭐ CE-111 — THE TKB CATALOG AS A WORLD SINGLETON, and this is the same omission the comment
        //    directly above documents for the geo transform: CGF held the dependency and never published it.
        // 📐 Measured 2026-08-28: SimHostNodeBootstrapper:179 and IgNodeBootstrapper:133 both register it;
        //    CGF passed `_context.TkbDb` straight to CreateEntityRequestSystem and NetworkSpawningSystem
        //    (line ~650/~659) and registered NOTHING ⇒ every consumer that resolves it FROM THE WORLD found
        //    nothing and degraded SILENTLY, because all of them guard with HasSingletonManaged:
        //      · DisEntityTypeTranslator:38    — DIS entity types not translated on CGF
        //      · EntityPresentationGizmoShared:60 — the map's per-entity presentation falls back
        // ⚠ Both are `if (has) …` with no else, so there was no log line and no failure — 📌 the shape
        //   ruling 53 exists to catch, and a direct `cgf==editor` violation.
        // ⛔ NOT a fix for the empty /tkb answer (that was CE-110's silent default); this is the production
        //   half found while measuring it, and the two are independent.
        if (_context.TkbDb != null)
            _context.World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(_context.TkbDb);

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

        // ⭐⭐⭐ CE-138 — CGF'S TKB→ECS PROJECTION LIST. It had NONE, on the node the design names the
        //    "entity spawning authority" (docs/projects/relationships/Hrot-Simulation-Pipeline.md §2),
        //    whose §4.3 spawn step reads verbatim "Apply TKB template components".
        // 📐 Measured 2026-08-30: NetworkSpawningSystem's `translators` argument was omitted (⇒
        //    Array.Empty) and elm.SetTranslators was never called, so BOTH projection routes were
        //    zero-iteration loops. CGF-spawned entities carried NetworkIdentity, NetworkOwnership,
        //    TkbIdentity and a DIS header — and none of their type's kinematics, combat, perception,
        //    behaviour or presentation. Rails: Hrot.SimHost.Tests/TkbTranslatorSpawnParityRails.cs.
        // 🔒 User ruling 2026-08-30: "the tkb idea is very simple and I think the usage rules should be
        //    same or very similar on cgf and simhost." ⇒ this is SimHost's list, verbatim.
        // ⭐⭐ Safe by construction, and this is the point of tkb-1/DESIGN.md §6.5b: every translator
        //    guards each write with IsComponentTypeRegistered<T>(), so a component CGF never registered
        //    stays a no-op no matter how many translators it is handed. The narrowing lever is the
        //    REGISTRATION SET, never the list — a short list fails silently for every entity, whereas an
        //    unregistered component fails loudly at one site.
        var translators = new System.Collections.Generic.List<Fdp.Interfaces.ITkbEntityTranslator>
        {
            new Fdp.Toolkit.Spatial.SpatialCoreTkbTranslator(),
            new CarKinem.Tkb.VehicleKinematicsTkbTranslator(),
            new Fdp.Toolkit.Behavior.Translators.BehaviorTkbTranslator(),
            new Fdp.Toolkit.Combat.Translators.CombatTkbTranslator(),
            new Fdp.Toolkit.Perception.Translators.PerceptionTkbTranslator(),
            new Hrot.SimHost.Diagnostics.AiDiagnosticsTkbTranslator(),
            new Hrot.Map.Definitions.Tkb.PresentationTkbTranslator(),
        }.AsReadOnly();

        // §6.3: "the translator list is identical for all three systems within the same node" — so the
        // SAME instance goes to the ELM (→ BlueprintApplicationSystem) and to NetworkSpawningSystem.
        // ⚠ Must precede the kernel's RegisterSystems, which is what reads it.
        elm.SetTranslators(translators);

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
            _context.NodeId,
            // ⭐ CE-138 — the argument this call omitted. Without it ProcessSpawn step 4's translator
            //   loop ran zero times on the node that spawns everything. See the list above.
            translators: translators);

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

        // ⭐⭐⭐ CE-046 (design §3 ③) — the shared scenario session, over THIS node's world and bus.
        // ⚠ `zoneService: null` is a genuine ABSENCE, not a silent default: CGF composes no
        //   ZoneManagerService at all (the editor does, at EditorSubsystem:1117), so there is no value
        //   this caller is withholding. ⛔ CLAUDE.md's rule is "a caller that HAS a dependency must pass
        //   it" — this one does not have one.
        // ⭐ The world BUS is passed, so NewScenario/ClearWorld fires WorldResetEvent here exactly as it
        //   does in the editor — that event is what the load handlers and gizmo caches key off.
        var cgfScenarioFileService = new Hrot.ScenarioEditor.Services.ScenarioFileService(
            scenarioSerializer, _context.World.Bus);
        _scenarioSession = new Hrot.Editor.AiShared.Scenarios.EditorScenarioSession(
            cgfScenarioFileService,
            _context.EventBus,
            _context.World,
            // ⭐⭐⭐ CE-057 — THE SHARED SCENARIOS ROOT, which is what the editor saves into.
            // 🔴🔴 The comment that stood here said *"CGF's scenarios live under its own node staging
            //    root … the editor's NAS-backed ClusterConfiguration.NasBasePath … would be the wrong
            //    answer anyway"*. 📐 MEASURED `2026-08-27` and it is WRONG on both halves:
            //    · `/tmp/FDP_Temp/nodes/node-N/scenarios` DOES NOT EXIST — the node directory holds only
            //      `recording_ledger`. The orchestrator STAGES shared → node on load; nothing authors there.
            //    · the authored scenarios are in `/tmp/FDP_Temp/shared/scenarios` (3 of them), which is
            //      exactly `ClusterConfiguration.Default.NasBasePath` + `scenarios` — i.e. the editor's
            //      root — and `Default` is a fresh `new()`, so no loaded config makes it differ.
            // ⇒ ⭐ this host now resolves the SAME root, from `Fdp.Toolkits` (reachable here) rather than
            //   from `Hrot.Orchestrator` (not reachable) ⇒ ⛔ no second authority, and `SaveAs` on CGF
            //   lands where the picker lists and where the loader stages from.
            () => OrchestrationConstants.GetSharedScenariosRoot());
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

        // ⭐⭐⭐ CE-102 / HN-039 — THE EDIT-LOAD HANDLER CGF HAS NEVER HAD.
        //
        // 🔒 User visual check `2026-08-28`: *"when i load hill-attack scenario using the toolbar button, it
        //    does NOT show on the map … editor shows it nicely."* 📐 Traced end to end: the toolbar's
        //    `shell.openAsset` → picker → `AssetPickActionRouter` → for a Scenario asset →
        //    `EditorScenarioSession.OpenForEdit` → a cluster transition to `OperatingEdit` — and NOTHING on
        //    this node claimed it. ⛔ `CgfScenarioLoadHandler.CanHandle(intent)` accepts `PrepareState` ONLY
        //    when `TargetState == OperatingLive`, so the edit target was explicitly declined; the load then
        //    answered ok:true with an empty world (measured: entityCount 0, gizmo frame all grid lines).
        //
        // ⭐⭐ Why the SHARED handler and not a CGF-private one: it is the same handler the editor and SimHost
        //    register, and ruling 65 settles the principle — *"Bringing editing machinery onto a runtime node
        //    is perfectly OK."* ⛔ A `CgfEditLoadHandler` would be a second implementation of one concept.
        //    ⚠ What blocked it was one required argument: it threw on a null `IZoneManagerService`, which this
        //      host genuinely does not compose (see :736). That is now optional AND REPORTED there.
        // ⚠⚠ KNOWN LIMIT, stated rather than discovered later: this does NOT pass CGF's `behaviorRemapper`,
        //    which the LIVE path does. Entities load and render; whether their behaviours bind on this host
        //    is CE-103's question, not this one. 📄 §5c.17.
        newClusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
            scenarioSerializer, scenarioLoader,
            zoneService: null,          // ⭐ declared absence — the handler warns if the scenario has zones
            extractor, _scenarioSource!, cgfIdAllocator,
            world: _context.World));

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
        // UXI-23 S2b: the buffer, both registries, the reflection pass and the three systems come from
        // the shared pack. 🔒 The pack CONSTRUCTS; CGF still SCHEDULES, below.
        var cgfMapInteraction = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext
            {
                World = _context.World,
                // CGF is a dumb terminal for handles — it draws all active gizmos, like IG.
                IsSelectedPredicate = null,
                // GZH-003: CGF is headless-first; enable only when a terminal connects.
                StartEnabled = false,
            });

        _cgfGizmoBuffer           = cgfMapInteraction.Buffer;
        _cgfInteractionBus        = cgfMapInteraction.InteractionBus;
        _cgfGizmoManager          = cgfMapInteraction.GlobalManager;
        _cgfDataDrivenGizmoSystem = cgfMapInteraction.DataDrivenSystem;
        var cgfStatelessRegistry  = cgfMapInteraction.StatelessRegistry;
        var cgfGizmoRegistry      = cgfMapInteraction.GizmoRegistry;
        var cgfSettingsRegistry   = cgfMapInteraction.Settings;
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
        // UXI-23 S2b: the group and its three members come from the pack; CGF schedules them below.
        var cgfGizmoGroup = cgfMapInteraction.GizmoGroup;
        _cgfGizmoController = cgfMapInteraction.Gate;
        _context.Kernel.RegisterModule(new GizmoInteractionModule(
            _cgfInteractionBus,
            contextIngress: null,
            interactionSystems: new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[]
            {
                cgfGizmoGroup,
            },
            gizmoIngress: cgfGizmoIngress,
            gizmoEgress:  cgfGizmoEgress));
        // ⭐⭐ UXI-23 S3: report anything this host constructed but did not schedule (§3.2e).
        foreach (string problem in cgfMapInteraction.Unserviceable(new object[] { cgfGizmoGroup }))
            Fdp.Core.Logging.FdpLog<CgfSubsystem>.Info("[Map] {0}", problem);
        _context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _cgfInteractionBus));
        // Register canvas menu update so CanvasContextMenuGizmo has state to project.
        _context.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());
        // ⭐⭐⭐ UXI-23 S1 — CGF showed entities only because the SCENARIO FILE authors
        //    MapDisplayComponent; nothing on this host ever recomputed the layer mask, so an
        //    entity spawned at runtime (or one whose layer membership changed) kept a stale or
        //    absent value. 🔒 Ruling ③: the host schedules the shared system.
        _context.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Map.MapLayerAssignmentSystem());

        // ⭐⭐⭐ CE-051 (Axis-C E3) — THE SHARED VIEWPORT INTERACTION, the same module the editor registers.
        // 📄 docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md §3 ⑤ and §6 (the two-way reconciliation).
        //
        // ⭐⭐ This is the line that retires CGF's hand-rolled parallels. Before E3 this host reached the
        //    viewport primitives through direct context-menu callbacks that had independently DRIFTED from
        //    the editor's drain — see the deleted `CenterCameraOnEntity`'s replacement,
        //    `CenterOnEntitySystem`, whose remarks record the live bug that drift had produced.
        //
        // ⚠⚠ Resolvers, not instances — and here the reason is even sharper than in the editor: `_canvas`
        //    and `_selectionState` are created in RegisterWindows, which runs LATER than this method and
        //    ⛔ not at all when headless. ⇒ a captured instance would be null forever on this host.
        // ⭐ `StartPlacementMode` is deliberately NOT supplied: CGF composes no EditorSpawnAdapter, so the
        //   Spawn tool REPORTS that rather than silently doing nothing (ruling 49 applied to a tool).
        _context.Kernel.RegisterModule(new Hrot.ScenarioEditor.ScenarioEditorModule(
            fileService: null,
            interaction: new Hrot.ScenarioEditor.ScenarioEditorModule.InteractionDeps(
                Selection:    () => _selectionState,
                Gizmos:       () => _cgfDataDrivenGizmoSystem,
                Camera:       () => _canvas?.Camera,
                GlobalGizmos: () => _cgfGizmoManager,
                // ⭐⭐⭐ CE-061 — StartPlacementMode is SUPPLIED now, and it has to be.
                // ⚠⚠ Until this batch it was legitimately absent — CGF composed no spawn adapter, so the
                //    Spawn tool reported itself unserviceable (ruling 49, and `TheViewportInteractionIs
                //    SharedTests` documents that as CGF's case). ⛔ E5 built the adapter, which makes the
                //    omission a SILENT DEFAULT instead: *"a production caller that HAS a dependency must
                //    PASS it."* Leaving it null would have shipped a spawner window whose tool refuses.
                // ⚠ Resolved at CALL TIME on purpose: this module is registered from Initialize, while
                //   `_spawnAdapter` is built later in the non-headless block — a captured value would be
                //   permanently null. ⭐ A headless node still has none, and then the report is honest.
                StartPlacementMode: () => _spawnAdapter?.StartPlacementModeWithLastType(),
                // ⭐⭐ The inspector follow-through CGF's own "Select entity" item used to do inline. ⛔ It
                //    is a host panel concern, so it stays a hook rather than being pushed into the shared
                //    assembly — see SelectEntitySystem's `alsoSelect` remarks.
                AlsoSelect:   entity => _fdpInspectorState.SelectedEntity = entity)));

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

        // ⭐⭐⭐ CE-052 (Axis-C E4) — THE INSPECTOR'S MUTATION INTERCEPTOR, which this host never set.
        // 📄 docs/DESIGN_Cgf_View_Inspector_Slice.md §1 / §4 ①.
        //
        // 🔴🔴 THE DEFECT, measured 2026-08-26: this method has CONSTRUCTED a DataBreakpointManager since
        //    `UBP-P10T2`, and `_fdpEntityInspector.Reflector.MutationInterceptor` was left NULL. ⇒ a data
        //    breakpoint the operator set NEVER TRIPPED on an inspector-driven component edit on CGF — no
        //    throw, no log, no failing assertion. The editor sets it (EditorSubsystem :4534) with the
        //    comment "wire MutationInterceptor early so it is set in headless mode too."
        //
        // ⭐⭐⭐ This is CLAUDE.md's SILENT-DEFAULT shape verbatim: *"a production caller that HAS a
        //    dependency must PASS it."* ⛔ Not a harmlessly-defaulted optional — the caller held the value
        //    and did not hand it over, which is the exact property that rule says distinguishes the three
        //    real instances from the harmless majority.
        //
        // ⚠ Set HERE, before any headless early-return, for the editor's stated reason: MCP-driven
        //   mutations are the headless case that matters most on this host.
        _fdpEntityInspector.Reflector.MutationInterceptor = _bpManager;

        // ⭐⭐⭐ CE-059 — THE BLUEPRINT DEBUG SESSION, which this host never constructed.
        // 📄 The user's `--mode all` check, 2026-08-27: *"Editor has also lots of toolbar buttons for
        //    debugging, none shown."*
        //
        // 🔴🔴 MEASURED: `AiDebugCommands.Register` had exactly ONE caller repo-wide
        //    (`EditorSubsystem`), so the six `debug.*` commands did not exist on CGF at all. ⛔ And
        //    registering them alone would have produced a group that is PERMANENTLY DISABLED, because
        //    every one of them gates `IsEnabled` on `IDebugSessionRegistry.ActiveSession` and nothing
        //    on this host ever put a session there. ⇒ ruling 49 would have made that WORSE than absent.
        //
        // ⭐⭐⭐ THE SILENT-DEFAULT SHAPE AGAIN, and this is the third instance in this file after
        //    CE-052: the three ctor arguments are all in scope RIGHT HERE — `_blueprintRegistry`
        //    (built at :508), the world, and `bpTimeAdapter` (the IEngineDebugTimeController built 20
        //    lines up). ⛔ *"A production caller that HAS a dependency must PASS it."* The editor's own
        //    construction (:1428-1436) is mirrored member for member, including `SetLiveRepository` for
        //    sub-tick recording and the eager `Attach()`.
        //
        // ⚠ `Attach()` sets the global `DebugProbe.Sink`. That is safe by CONFIGURATION, not by luck:
        //   `HrotRunnerConfiguration.Validate` REJECTS `editor` together with `cgf`, so the two hosts
        //   can never both attach in one process. ⛔ If that rule is ever relaxed, this needs
        //   `MultiplexingProbeSink` — which already exists for exactly that case.
        // ⛔ NOT wired: the debounced session SAVE the editor attaches (`ScheduleDebugSessionSave`). That
        //   is editor-side layout persistence, not a debug capability, and this host has no equivalent.
        var bpBlueprintSession = new Hrot.Blueprints.Core.Debug.BlueprintDebugSession(
            _blueprintRegistry!, _context.World, bpTimeAdapter);
        bpBlueprintSession.SetDataBreakpointManager(_bpManager);
        bpBlueprintSession.SetLiveRepository(_context.World);
        bpBlueprintSession.Attach();
        _blueprintDebugSession = bpBlueprintSession;

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

            // ⭐⭐⭐ CE-061 (Axis-C E5 item ④) — THE SCENARIO-PERSPECTIVE PANELS + ADAPTERS.
            // 📄 docs/DESIGN_Cgf_Scenario_Windows_Slice.md §3/§7 ④.
            // 🔒 User, 2026-08-27: *"the editor has many windows in its Scenario perspective like mission
            //    editor, orbat, entity placement, entity spawner, cgf offers just Entity inspector, Event
            //    Browser, architecture diagnostic, System profiler."*
            //
            // ⭐⭐ EVERY ARGUMENT ALREADY EXISTED ON THIS HOST — 📐 that is the finding, and it is why E5
            //    is a wiring slice: the adapters were host-agnostic already (four of five had ZERO
            //    IEditorLogic references) and merely sat in `Hrot.Editor`, which this assembly cannot see.
            // ⛔ NOTHING here is a CGF-private implementation: same panels, same adapters, same window
            //    types the editor now registers through.
            _mapViewConfig     = new Hrot.Map.Common.Config.MapViewConfig();
            _spawnerPanel      = new SpawnerPanel(Hrot.UI.Common.Panels.ScenarioSpawnerCatalog.Default);
            // ⚠ MissionPanel's first argument is the node id the editor passes as a literal 0; this host
            //   has a REAL one, and passing it is the point of "the editor is a one-node cluster".
            _missionPanel      = new MissionPanel(
                _context.NodeId, Hrot.Presentation.Behavior.BehaviorUiSetup.CreateRegistry());
            _configPanel       = new ConfigPanel();
            _sharedOrbatPanel  = new SharedOrbatPanel();

            var cgfJsonCompiler = Fdp.Toolkit.Replication.Attributes.AttributeCompilerFactory.Build(
                _context.GeoTransform!);
            _spawnAdapter      = new Hrot.UI.Common.Adapters.ScenarioSpawnAdapter(
                _context.World.Bus, cgfJsonCompiler, _context.TkbDb, _scenarioSource, _cgfGizmoManager);
            _missionService    = new Hrot.UI.Common.Adapters.ScenarioMissionService(
                _context.World.Bus, _context.World, _behaviorRegistry!);
            _mapConfigAdapter  = new Hrot.UI.Common.Adapters.ScenarioMapConfigAdapter(_mapViewConfig, _canvas);
            // ⭐ CE-060 made this ctor host-agnostic: its one `IEditorLogic.ActivateTool` call is now the
            //   shared ActivateEditorToolEvent + SelectEntityCommand that E3's systems drain on BOTH hosts.
            _orbatAdapter      = new Hrot.UI.Common.Adapters.ScenarioOrbatAdapter(
                _context.World, _context.World.Bus, _spawnAdapter);

            // GZ057: add gizmo layer so CGF entity presentation primitives are rendered.
            _cgfGizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, _cgfGizmoBuffer, _cgfInteractionBus!);
            _canvas.AddLayer(_cgfGizmoLayer);
            _canvas.DrawBuffer = _cgfGizmoBuffer;

            // (Phase 5: StandardInteractionTool removed; entity interaction via ECS gizmos)

            // Register context menu handler for right-click in the entity inspector panel.
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                // ⭐⭐⭐ CE-051 (Axis-C E3) — EVERY ITEM HERE NOW PUBLISHES THE SHARED COMMAND. 📄
                //    docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md §3 ⑤ / §6.
                //
                // 🔴🔴 What was here, and why it had to die rather than sit beside the shared path:
                //    · "Center on entity" called a hand-rolled `CenterCameraOnEntity` that set
                //      `Camera.Target` directly. 📐 MEASURED BROKEN: `MapCamera.Update` overwrites
                //      `InnerCamera.Target` from `_targetTarget` every frame (EnableSmoothing defaults to
                //      false), and CGF never set `_targetTarget` — so centring sent the camera to the
                //      ORIGIN on the next frame. The editor's arm called `FocusOn`, which is the seam that
                //      works. ⇒ routing through the shared system FIXES a live defect.
                //    · "Select entity" wrote the selection state inline — the editor published a command
                //      that NOTHING READ. ⇒ the shared SelectEntitySystem is the first real consumer, and
                //      this host's inspector follow-through is now its `AlsoSelect` hook.
                //    · "Rotate" duplicated the editor's gizmo block. ⭐ Its one genuine extra — selecting
                //      the entity first — stays HERE, because the context menu acts on the clicked entity
                //      while the toolbar acts on the selection. That is a CALLER concern, so the shared
                //      body needs no host branch.
                builder.AddItem("Center on entity", () => PublishForEntity(entity,
                    netId => new Hrot.Common.Events.CenterOnEntityCommand { NetworkId = netId }));
                builder.AddItem("Select entity", () => PublishForEntity(entity,
                    netId => new Hrot.Common.Events.SelectEntityCommand { NetworkId = netId }));
                builder.AddSeparator();
                builder.AddItem("Delete entity", () => DeleteEntity(entity));
                if (_context!.World.HasComponent<Fdp.Core.SimTransform>(entity))
                    builder.AddItem("Rotate", () =>
                    {
                        // ⭐ Select first (the caller concern above), then let the SHARED drain do the work.
                        _selectionState.PrimarySelected = entity;
                        _context.World.Bus.Publish(
                            new Hrot.Common.Events.ActivateEditorToolEvent(Hrot.Common.EditorTool.Rotate));
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

        // ⭐⭐ CE-046 — pump the shared scenario session: it drains ClusterStateUpdateEvent and advances
        //    the deferred edit-open state machine. ⚠ MUST run after ClusterSlave.Tick (which is what
        //    publishes the state updates) and BEFORE the EventBus.SwapBuffers() at the end of this
        //    method, or the events it needs are gone.
        _scenarioSession?.Update();

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

        // ⭐⭐⭐ CE-049 (Axis-C E2) — the shell picker + Save-As browser frames.
        // ⛔ WITHOUT THESE TWO LINES THE PICKERS ARE INVISIBLE: `PickerRegistry.OpenPicker` only queues a
        //    request; `DrawFrame` is what renders it. ⚠ That failure would look exactly like "the menu
        //    item does nothing" — a live-looking control that silently no-ops, which is the shape ruling
        //    49 and VC-3 both exist to prevent. 📐 Mirrors EditorSubsystem.DrawUI :2614-2618.
        // ⭐ These are CGF's OWN registry, not the canvas windows' — see WirePickerShell's remarks on
        //   double-DrawFrame.
        _shellPickers?.DrawFrame();
        if (_saveAsBrowser != null && _shellIconProvider != null)
            _saveAsBrowser.DrawFrame(_shellIconProvider);
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

        // ⭐⭐⭐ CE-058 — THE PERSPECTIVE ICON KEYS, and this is why CE-054's buttons looked wrong.
        // 📄 The user's `--mode all` check, 2026-08-27: *"instead of graphical icons (as rendered in the
        //    editor) there are plain imgui buttons in the toolbar?? … cgf must be using some different
        //    toolbar code, not shared with editor"*.
        // 📐 MEASURED — and the one part of that diagnosis which was NOT true is the interesting part:
        //    both hosts build the SAME `PerspectiveToolbarSection` over the same `SilkIconProvider`. What
        //    differed is that `RegisterPerspectiveIconKey` had exactly ONE caller repo-wide (five inline
        //    calls in `EditorSubsystem`), so here `GetPerspectiveIconKey` returned null and the section
        //    took its DOCUMENTED text-label fallback. ⇒ ⭐ the plain buttons were the shared code's own
        //    graceful degradation, ⛔ not a second toolbar implementation.
        Hrot.Editor.AiShared.Windows.PerspectiveIconKeys.Register(windowManager);

        // Create a map-pick bridge so component fields tagged [MapPickable] can be edited.
        CanvasMapPickAdapter? cgfCanvasAdapter = _canvas != null && _context?.World != null
            ? new CanvasMapPickAdapter(_canvas, _context.World, globalGizmoManager: _cgfGizmoManager)
            : null;
        MapPickServiceBridge? cgfPickBridge = cgfCanvasAdapter != null
            ? new MapPickServiceBridge(cgfCanvasAdapter, _context!.World)
            : null;

        // ⭐⭐ A9 — the helper's third argument is the PERSPECTIVE (see its own doc); the spawned watch
        //    windows' id prefix is cgf_watch_* → scenario_watch_* and that is harmless (those ids embed a
        //    fresh Guid). ⭐ Sharing ids with the editor is SAFE and intended: §1b — editor and cgf can
        //    never run in one process.
        // ⭐⭐⭐ PHASE 2 SLICE ② — the helper CALL moved into `DiagnosticsWindowsBundle` (below), which
        //    wires it for all four hosts. ⚠ It used to run HERE, before the inspector window; 📐 measured
        //    safe to move because the helper registers NOTHING eagerly — its own `RegisterWindow` sits
        //    inside the "Inspect…" click handler — so the registered set cannot change (§5c.7.2).

        // ⭐⭐⭐ CE-061 (Axis-C E5 item ④) — THE FOUR SCENARIO-PERSPECTIVE WINDOWS.
        // ⭐ The SAME `Hrot.Presentation.Windows` types the editor now registers through — ⛔ CGF-private
        //   wrappers are exactly what E5 deleted (two copies existed: Hrot.Editor and Hrot.ExCon).
        // ⚠ Each is guarded on its own panel+adapter pair rather than on one flag: a host that cannot
        //   service a window must not show it (ruling 49), and the pairs are independent.
        // ⭐ Mission takes `cgfPickBridge`'s underlying `CanvasMapPickAdapter` — the shared IMapPickService
        //   this host ALREADY built two lines up, ⛔ not a second pick implementation (design §8 D2).
        if (_spawnerPanel != null && _spawnAdapter != null)
            windowManager.RegisterWindow(new Hrot.Presentation.Windows.SpawnerPanelWindow(
                _spawnerPanel, _spawnAdapter,
                Hrot.Presentation.Windows.ScenarioPanelWindowIds.CgfSpawner, "Scenario", TitleBarColor));

        if (_missionPanel != null && _missionService != null && cgfCanvasAdapter != null)
            windowManager.RegisterWindow(new Hrot.Presentation.Windows.MissionPanelWindow(
                _missionPanel, _missionService, cgfCanvasAdapter,
                Hrot.Presentation.Windows.ScenarioPanelWindowIds.CgfMission, "Scenario", TitleBarColor));

        if (_configPanel != null && _mapConfigAdapter != null)
            windowManager.RegisterWindow(new Hrot.Presentation.Windows.ConfigPanelWindow(
                _configPanel, _mapConfigAdapter,
                Hrot.Presentation.Windows.ScenarioPanelWindowIds.CgfConfig, "Scenario", TitleBarColor));

        if (_sharedOrbatPanel != null && _orbatAdapter != null)
            windowManager.RegisterWindow(new Hrot.Presentation.Windows.SharedOrbatPanelWindow(
                _sharedOrbatPanel, _orbatAdapter, _orbatAdapter,
                Hrot.Presentation.Windows.ScenarioPanelWindowIds.CgfOrbat, "Scenario", TitleBarColor));

        // ⭐⭐⭐ PHASE 2 SLICE ② — the FIVE diagnostics sites (inspector, the "Inspect…" wiring, event
        //    browser, architecture diagnostics, system profiler) are now ONE shared bundle,
        //    `Hrot.Presentation.Windows.DiagnosticsWindowsBundle`. 📐 They were copy-pasted across FOUR
        //    hosts = 20 sites; ids/titles are DERIVED, so they cannot drift apart again.
        // ⭐ This host passes ONE colour: unlike IG/SimHost it uses the same shade for the spawned
        //   "Inspect…" watch windows as for its diagnostics windows (§5c.7.2 G2).
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §5c.7.
        Fdp.Toolkit.Runner.UiBundleHost.Compose(
            new Fdp.Toolkit.Runner.IUiBundle[]
            {
                new DiagnosticsWindowsBundle(new DiagnosticsHostServices(
                    IdPrefix:       "cgf_",
                    TitlePrefix:    "CGF",
                    Perspective:    "Scenario",
                    Inspector:      _fdpEntityInspector,
                    RepoAdapter:    () => _fdpRepoAdapter,
                    InspectorState: () => _fdpInspectorState,
                    EventBrowser:   _fdpEventBrowser,
                    TitleBarColor:  TitleBarColor,
                    // ⭐ This host builds its own service, so its lazy `() => _context?.Kernel` binding
                    //   is untouched (design §5c.7 F2).
                    ArchitecturePanel: new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                        new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => _context?.Kernel)),
                    // BP-327 — the module/system execution-stats profiler.
                    ExecutionStats: () => _context?.Kernel?.GetExecutionStats(),
                    PickBridge:     cgfPickBridge)),
            },
            new Fdp.Toolkit.Runner.UiBundleContext(windowManager));

        // ⭐⭐ PHASE 2 SLICE ② — the ~30-line blackboard-reflection block that used to sit here was
        //    duplicated VERBATIM in EditorSubsystem. ⛔ It is NOT in the bundle: IG and SimHost do none
        //    of it, and folding it in would hand them a capability they do not have — the very trap
        //    `IUiBundle`'s doc warns about. ⭐ One implementation, exactly two callers (§5c.7 F5 / G3).
        Hrot.Presentation.Windows.BlackboardReflection.Apply(_fdpEntityInspector, _behaviorRegistry);

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
            // ⭐⭐ PHASE 2 SLICE ③ — one shared registrar, `ShellTimeControlToolbar` (design §5c.8 H1).
            // ⭐⭐⭐ CE-090 — THE SEPARATOR IS THE SAME HERE AS ON THE EDITOR, and the boolean that used
            //    to let them differ is GONE. 🔒 User ruling `2026-08-27`: *"we are unifying the UI, so
            //    obviously the stuff should look same and they CAN'T look different by design if they are
            //    rendered by single shared code where host-type gates are undesired."*
            //    ⭐ CE-016 §7 removed the separator here when it stood in front of a perspective group
            //      this host did not register; CE-054 gave it that group, so the reason it was removed no
            //      longer holds. 📄 §5c.14.
            // ⚠ THE `MainToolbar != null` GUARD IS GONE — a DEAD BRANCH (design §5c.8 H2): 📐 measured,
            //   `WindowManager:406` is `private readonly MainToolbarManager _mainToolbar = new();` behind
            //   an expression-bodied property, so it can never be null. The editor removed its own copy
            //   for the same reason.
            Hrot.UI.Common.Panels.ShellTimeControlToolbar.Register(
                windowManager.MainToolbar, _clusterTimeAdapter);
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

        // ⭐ CE-059 — hoisted to a FIELD. ⛔ It was a local, so the toolbar block in WireSaveAndReload
        //   could not reach it and the `debug.*` group could not be registered at all.
        var debugRegistry = _aiDebugRegistry = new Hrot.Editor.AiShared.Debug.DebugSessionRegistry();

        // ⭐ The behaviour-action schema, reflected from the already-loaded game assemblies — the same
        //   Rebuild() the editor performs at :2610. CGF loads Hrot.AI.Behaviors, so this is populated.
        var schemaExporter = new Hrot.Editor.AiShared.Blackboard.ActionSchemaExporter();
        schemaExporter.Rebuild();

        // ── ⭐⭐⭐ CE-071 (D3) — THE COMPARISON CAPABILITY, mirroring the editor's :2679-2687 ──────
        // 📄 docs/DESIGN_Comparison_Ui_Mounting.md §4 D3/D4.
        //
        // 🔒 cgf==editor is the programme's goal, reaffirmed by the user 2026-08-27.
        // 📐 Measured before this: CGF set NONE of SanitizerRegistry / ExportBuilder / SessionRegistry, so
        //    BlackboardAuthoringWindow's three-way guard failed and its _comparisonToolbar was NULL ⇒
        //    ⛔ CGF had no "Compare with…" entry ANYWHERE, while the editor did.
        // ⚠⚠ That was NOT the "caller HAS it and does not pass it" trap: CGF never CONSTRUCTED these, so
        //    it was a capability never granted rather than an argument never forwarded. ⭐ The cost of
        //    granting it is this block — the sanitizers need only the catalog, which CGF already has.
        //
        // ⭐⭐ D4 / ruling 58 — ONE registration list, no host conditionals. These are the same four
        //    sanitizers in the same order the editor registers, plus Blackboard's, which NEITHER host
        //    registered before (⇒ blackboard assets silently could not be compared on either host).
        var comparisonSanitizers = new Hrot.Editor.AiShared.Comparison.SanitizerRegistry();
        comparisonSanitizers.Register(new Hrot.BTree.Editor.Comparison.BTreeComparisonSanitizer(catalog));
        comparisonSanitizers.Register(new Hrot.Hsm.Editor.Comparison.HsmComparisonSanitizer(catalog));
        comparisonSanitizers.Register(new Hrot.Blueprints.Editor.Comparison.BlueprintComparisonSanitizer(
            new Hrot.Editor.AiShared.Comparison.NoOpComparisonMigrationAdapter(),
            new Hrot.Editor.AiShared.Comparison.NoOpMetaEnvelopeSanitizer(),
            catalog));
        comparisonSanitizers.Register(new Hrot.Editor.AiShared.Comparison.BlackboardComparisonSanitizer());

        var comparisonExportBuilder = new Hrot.Editor.AiShared.Comparison.ComparisonExportBuilder();
        // ⭐⭐ D2 — ONE registry, kept on the instance so the three document Build sites can compose the
        //    canvas annotation renderer against the SAME state the panels read.
        var comparisonSessionRegistry = _comparisonSessionRegistry =
            new Hrot.Editor.AiShared.Comparison.ComparisonSessionRegistry();

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

            // ⭐⭐⭐ CE-071 (D3) — CGF HAS these now, so it PASSES them. Without all three,
            //    BlackboardAuthoringWindow builds no ComparisonToolbarAction and the registrar builds no
            //    comparison panels ⇒ the whole feature stays dark on this host.
            SanitizerRegistry = comparisonSanitizers,
            ExportBuilder     = comparisonExportBuilder,
            SessionRegistry   = comparisonSessionRegistry,

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
        //   ⛔ Blueprint gets none EITHER on this host.
        // ⚠⚠ SUPERSEDED REASONING, corrected 2026-08-27 (CE-059). This comment used to say *"nothing on
        //    CGF ever puts an IBlueprintDebugSession there (there is no document manager driving
        //    SyncActiveDebugSession)"*. 📐 Both halves are now FALSE: this host constructs and attaches a
        //    BlueprintDebugSession (:963) and `ActiveDebugSessionMirror.Wire` drives the registry from
        //    `_aiDocumentManager`. ⇒ ⭐ the provider would NO LONGER be one that can only answer null,
        //    and wiring it is a real follow-on — filed as CE-062, ⛔ deliberately NOT smuggled in here:
        //    it needs the editor's provider construction measured, and this batch's job was the toolbar.
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
                        openBlueprint:     a => _aiDocumentManager?.Open(a),
                        // ⭐⭐⭐ CE-071 — the comparison annotation renderer, same as the editor.
                        //    📄 DESIGN_Comparison_Ui_Mounting.md. It joins this kind's built-in set.
                        extraRenderers:    Hrot.Editor.AiShared.Comparison.Rendering
                            .ComparisonCanvasRenderers.For(_comparisonSessionRegistry, doc.Asset.AssetId));
                    break;

                case Hrot.Editor.AiShared.AssetKind.Hsm:
                    doc.ViewState = Hrot.Hsm.Editor.Host.HsmDocumentFactory.Build(
                        doc.Asset, adapters,
                        hsmDebugSession:   null,
                        breakpointManager: _bpManager,
                        // ⭐⭐⭐ CE-071 — see the BTree arm above.
                        extraRenderers:    Hrot.Editor.AiShared.Comparison.Rendering
                            .ComparisonCanvasRenderers.For(_comparisonSessionRegistry, doc.Asset.AssetId));
                    break;

                case Hrot.Editor.AiShared.AssetKind.Blueprint:
                    doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
                        doc.Asset, adapters, blueprintEditService, blueprintPalette,
                        channelCommands:  bpChannelCatalog,
                        peerAssetCatalog: blueprintPeerCatalog,
                        behaviorActions:  behaviorActions,
                        debugSession:     null,
                        // ⭐⭐⭐ CE-071 — see the BTree arm above.
                        extraRenderers:   Hrot.Editor.AiShared.Comparison.Rendering
                            .ComparisonCanvasRenderers.For(_comparisonSessionRegistry, doc.Asset.AssetId));
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
        //
        // ⚠⚠ CE-049 — WireAssetCreation now runs BEFORE this call, and the ORDER IS LOAD-BEARING:
        //    WireSaveAndReload registers ScenarioMenuCommands, whose openPicker/openSaveAsDialog seams
        //    are the launchers WireAssetCreation + WirePickerShell build. ⛔ Registering the menu first
        //    would hand it nulls and leave every item greyed — which is exactly the state E2 removes.
        //    📐 Verified independent: neither method read the other's output before this reorder.
        WireAssetCreation(catalog);
        WirePickerShell(windowManager, adapters, catalog);

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

        // ⭐⭐⭐ CE-049 (Axis-C E2) — THE DUPLICATE CREATE-CORE IS GONE. Ruling 9.
        // 📄 docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md §2 (the inventory row) / §3 ②.
        //
        // 🔴 What was here: a ~50-line RE-DERIVATION of `EditorSubsystem.CreateAssetCore` — same four
        //    composition facts, re-typed. 📐 And the two had already DRIFTED in three places: this copy
        //    had no non-document-kind branch, no try/catch around the Blueprint write, and (better than
        //    the editor's) a remedy in its "not in the catalog" message. ⇒ ⭐ the shared controller keeps
        //    the better text and the editor's branch, so neither host regressed.
        //
        // ⭐ `saveMintOnlyAsset` unwraps the adapter HERE because the unwrap type lives in
        //   Hrot.Blueprints.Editor, which the shared assembly does not reference — the same reason the
        //   editor passes its own `saveAsBlueprintToFile`.
        _assetCreateController = new Hrot.Editor.AiShared.Browser.AssetCreateController(
            services:          _newAssetServices,
            saveMintOnlyAsset: (asset, path) =>
            {
                if (asset is Hrot.Blueprints.Editor.Variables.BlueprintEditableAssetAdapter adapter)
                    Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(adapter.Asset, path);
            },
            // ⭐⭐ CE-091 (J2 K1) — the SIX-LINE JSON kind-dispatch lambda that stood here is gone:
            //    `AiAssetCatalogBuilder.RefreshJsonContributors` owns that policy now (the method its own
            //    doc had promised and nobody had built). ⭐ The editor passes the same method group.
            // ⛔ The other four delegates STAY — a deliberate reversal of §5c.10 K2 (WITHDRAWN): they are
            //   the TEST SEAM seven rails inject to assert the create sequence.
            findCatalogued:         id => catalog.FindByAssetId(id),
            refreshFromAssembly:    asm => _aiCatalogBuilder?.RefreshFromAssembly(asm),
            refreshJsonContributor: k => _aiCatalogBuilder?.RefreshJsonContributors(k),
            openDocument:     a => _aiDocumentManager?.Open(a),
            blueprintRootDir: () => _bpRootDir);

        AssetShellCreate = _assetCreateController.CreateByName;

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Asset creation wired — kinds [{0}], {1} recipe(s) offered.",
            string.Join(", ", _newAssetServices.Keys),
            _newAssetServices.Values.Sum(s => s.AvailableRecipes().Count));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-049</c> (Axis-C <b>E2</b>) — THE PICKER SHELL: this is what lights up Slice A's greyed
    /// <c>Open Scenario</c> / <c>New Scenario</c> items.</b>
    /// 📄 <b><c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c></b> §3 ③, §4, §5.
    ///
    /// <para>⭐⭐ <b>Every class here is SHARED and pre-existing; only the composition is new.</b>
    /// 📐 Measured <c>2026-08-26</c> *(design §1)*: CGF already had the whole service/create layer
    /// *(<c>MA-019</c>…<c>023</c>)* and already built an <c>AiEditorAdapterBundle</c>. ⇒ the gap was
    /// literally the two <c>null</c>s CGF passed for <c>openPicker</c>/<c>openSaveAsDialog</c>.</para>
    ///
    /// <para>⭐⭐⭐ <b>THE MEASURED RISK IN THE DESIGN §2 IS RESOLVED: CGF CAN host the modal.</b> 📐 The
    /// evidence is structural, not hopeful — <see cref="BuildAiShell"/> is reached only from
    /// <see cref="RegisterWindows"/>, which returns early when <c>_headless</c>. ⇒ if this method runs at
    /// all, this node has a <c>WindowManager</c> and an ImGui context. ⛔ On a genuinely headless CGF the
    /// shell is never built, so <c>ScenarioMenuCommands</c> is never registered either — ⚠ meaning the
    /// *"greyed-with-cause"* end state is a UNIT-rail property *(Slice A's rail asserts it)*, not something
    /// a headless run exhibits.</para>
    ///
    /// <para>⚠⚠ <b>A SEPARATE <see cref="PickerRegistry"/> from the canvas windows', and the editor learned
    /// this the hard way</b> *(<c>BATCH-29</c>: "Separate from adapterBundle.PickerRegistry (which canvas
    /// windows already DrawFrame) to avoid double-DrawFrame")*. ⛔ Reusing <c>adapters.PickerRegistry</c>
    /// here would draw every shell picker twice per frame.</para>
    /// </summary>
    private void WirePickerShell(
        Fdp.Presentation.WindowManager.WindowManager windowManager,
        Hrot.Editor.AiShared.Adapters.AiEditorAdapterBundle adapters,
        Hrot.Editor.AiShared.Catalog.AssetCatalog catalog)
    {
        _shellIconProvider = adapters.IconProvider;

        _shellPickers = new NodeEditor.UI.Picker.PickerRegistry();
        _shellPickers.SetServices(adapters.IconProvider, adapters.EditorTheme);

        _saveAsBrowser = new NodeEditor.UI.Dialogs.SaveAsBrowserDialog();

        // ⭐ The router is the SAME shared type the editor uses; only its two delegates are per-host.
        //   ⭐⭐ `loadScenario` goes to the Slice A session's OpenForEdit — ⛔ NOT to a CGF-private load
        //   path. That is the whole point of E1 landing first.
        var router = new Hrot.Editor.AiShared.Browser.AssetPickActionRouter(
            openDocument: asset => _aiDocumentManager?.Open(asset),
            loadScenario: name  => _scenarioSession?.OpenForEdit(name));

        _assetPickerLauncher = new Hrot.Editor.AiShared.Browser.AssetPickerLauncher(
            openPicker: _shellPickers.OpenPicker,
            catalog:    catalog,
            router:     router);

        // ⭐⭐ The New-Asset flow: recipe picker → Save-As browser for the name/folder → the ONE
        //    create-core. ⚠ `_assetCreateController` is non-null here because WireAssetCreation runs
        //    immediately before this method (see the ordering note at its call site).
        _newAssetLauncher = new Hrot.Editor.AiShared.Browser.NewAssetLauncher(
            openPicker:         _shellPickers.OpenPicker,
            services:           _newAssetServices!,
            showNewAssetDialog: (kind, recipe) => ShowNewAssetDialog(catalog, kind, recipe),
            // ⭐ MA-020's lesson, applied at the point it was found: the two describe seams were optional
            //   and NOBODY PASSED THEM, so every recipe rendered with a null description while
            //   EditorMetadata.Recipe carried one. ⛔ This caller HAS them, so it passes them.
            describe:           Hrot.Blueprints.Editor.RecipeMetadataAdapter.DescribeRecipe,
            recipeCategory:     Hrot.Blueprints.Editor.RecipeMetadataAdapter.RecipeCategory);

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Picker shell composed — Open/New asset and scenario pickers are live on this host.");
    }

    /// <summary>
    /// ⭐ Seeds and opens the Save-As browser for a NEW asset of <paramref name="kind"/>, then runs the
    /// shared create-core. Mirrors <c>EditorSubsystem.ShowNewAssetDialog</c> — ⭐ and the request itself is
    /// built by the SHARED <c>AssetSaveAsRequests.Build</c>, so the two hosts cannot drift.
    /// </summary>
    private void ShowNewAssetDialog(
        Hrot.Editor.AiShared.Catalog.AssetCatalog catalog,
        Hrot.Editor.AiShared.AssetKind kind,
        Hrot.Editor.AiShared.IEditableAsset recipe)
    {
        if (_newAssetServices == null || _assetCreateController == null) return;

        var folderPicker = new Hrot.Editor.AiShared.Browser.FolderPickerState(
            Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
                catalog.All, kind, Hrot.Editor.AiShared.Browser.AssetSaveAsRequests.DefaultBaseFolderFor));

        string initialName = _newAssetServices[kind].IsBlankTemplate(recipe)
            ? $"New{kind}"
            : recipe.Name;

        var request = Hrot.Editor.AiShared.Browser.AssetSaveAsRequests.Build(
            catalog, kind, $"New {kind}", initialName, "", "Create", folderPicker);

        _saveAsBrowser?.Open(request, result =>
        {
            if (!result.Confirmed) return;
            var (_, status) = _assetCreateController.Create(kind, recipe, result.Name, result.DestinationPath);
            FdpLog<CgfSubsystem>.Info("[CGF] {0}", status);
        });
    }

    /// <summary>
    /// ⭐⭐ <c>CE-049</c> — the scenario Save-As browser, the <c>openSaveAsDialog</c> seam
    /// <c>ScenarioMenuCommands</c> takes. Mirrors <c>EditorSubsystem.openScenarioSaveAs</c> and builds its
    /// request through the same shared builder.
    ///
    /// <para>⭐ The chosen destination and name are joined into the FULL scenario name the session expects
    /// *(<c>folder/name</c>)*, with the leading slash trimmed — ⚠ the editor does exactly this, and a
    /// mismatch here would write the scenario to a path its own loader could not find.</para>
    /// </summary>
    private void OpenScenarioSaveAsDialog(
        Hrot.Editor.AiShared.Catalog.AssetCatalog catalog,
        Action<string> onNamed)
    {
        if (_saveAsBrowser == null || _scenarioSession == null) return;

        var currentName = _scenarioSession.LoadedScenarioName ?? "";
        int lastSlash   = currentName.LastIndexOf('/');
        string initialName = lastSlash >= 0 ? currentName.Substring(lastSlash + 1) : currentName;

        var folderPicker = new Hrot.Editor.AiShared.Browser.FolderPickerState(
            Hrot.Editor.AiShared.Browser.AssetFolderDerivation.KnownSubfolders(
                catalog.All, Hrot.Editor.AiShared.AssetKind.Scenario,
                Hrot.Editor.AiShared.Browser.AssetSaveAsRequests.DefaultBaseFolderFor));

        var request = Hrot.Editor.AiShared.Browser.AssetSaveAsRequests.Build(
            catalog, Hrot.Editor.AiShared.AssetKind.Scenario, "Save Scenario As",
            initialName, "", "Save", folderPicker);

        _saveAsBrowser.Open(request, result =>
        {
            if (!result.Confirmed) return;

            string dest = result.DestinationPath.TrimStart('/');
            string fullName = string.IsNullOrEmpty(dest) ? result.Name : dest + "/" + result.Name;
            onNamed(fullName);
        });
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
        // ⭐⭐⭐ PHASE 2 SLICE ① — the three bodies below are now ONE implementation, shared with the
        //    editor: `Hrot.Editor.AiShared.Documents.AiAssetSavers`. 📐 Before this, the editor carried
        //    its own semantically-identical, syntactically-drifted copies. ⛔ The only step that stays
        //    here is `ToDto` / the concrete cast — AiShared cannot name these asset types without a
        //    circular project reference (design §5c.6.2, and `SaveAllAiDocumentsCommand`'s own §PU-602
        //    note). 📄 docs/DESIGN_Subsystem_Composition_Unification.md §5c.6.
        _saveBlueprint = (asset, path) =>
        {
            if (Hrot.Editor.AiShared.Documents.AiAssetSavers.ResolveAssetRef(_aiDocumentManager, asset.AssetId)
                is not Hrot.Blueprints.Core.Assets.BlueprintAsset bpAsset) return;
            Hrot.Blueprints.Editor.SaveActiveBlueprintCommand.Save(bpAsset, path);
        };

        _saveBTree = (asset, path) =>
        {
            if (asset is not Hrot.BTree.Editor.Model.BehaviorTreeAsset bt) return;
            Hrot.Editor.AiShared.Documents.AiAssetSavers.SaveBTree(
                Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(bt), path);
        };

        _saveHsm = (asset, path) =>
        {
            if (asset is not Hrot.Hsm.Editor.Model.HsmAsset hsm) return;
            Hrot.Editor.AiShared.Documents.AiAssetSavers.SaveHsm(
                Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsm), path);
        };

        // ── The main-toolbar affordances — CE-016 §7 (A2) ──────────────────────
        // ⭐⭐⭐ THE TWO AD-HOC `ImGui.Button` ENTRIES ARE GONE. 📐 They were `"Save All"` and
        //    `"Reload AI"` at sortOrder 10/11 — raw buttons with no icon, no command id, no enablement,
        //    and no MCP identity. ⇒ they diverged from the editor's toolbar BY CONSTRUCTION, which is
        //    what the `main-toolbar` known-divergence entry in the conformance rail recorded.
        // ⭐⭐ Now CGF registers the SAME commands at the SAME ids and sort orders, through the SAME
        //    shared table the editor uses — `CgfEditorShellToolbar` (ruling 58: one registration list).
        // 📄 DESIGN_Cgf_Shell_Command_Toolbar_Slice.md §3 ③.
        Hrot.Editor.AiShared.Documents.ShellSaveCommands.Register(
            windowManager.ShellCommands.Register,
            _aiDocumentManager!,
            saveBlueprint: _saveBlueprint,
            saveBTree:     _saveBTree,
            saveHsm:       _saveHsm,
            // ⛔ `saveScenario` is the per-ASSET-KIND delegate for a Scenario *document* in the AI
            //   document manager. ⭐ There is no such document kind on either host — the editor passes
            //   nothing here either — so this stays null. ⚠ NOT the same thing as the scenario SESSION
            //   seams below, which is what CE-046 supplies.
            saveScenario:  null,
            // ⚠ Save-As needs a modal browser this host does not compose. ⛔ It must still be a
            //   NO-OP-WITH-A-REASON rather than a crash: the shared command is registered either way,
            //   and a headless node reaching it says so instead of throwing.
            requestSaveAs: doc => FdpLog<CgfSubsystem>.Warn(
                "[CGF] Save-As is not available on this host (no modal browser composed); "
              + "'{0}' was not saved under a new name.", doc.Asset.Name),
            report:        msg => FdpLog<CgfSubsystem>.Info("[CGF] {0}", msg),
            // ⭐⭐⭐ CE-046 (design §3a, the `File/Save` row) — the SCENARIO seams, now that this host has
            //    a session. ⛔ NO NEW MENU ITEM: the shared slot table below already emits `File/Save`
            //    bound to `shell.save`, and that handler branches HERE when `isScenarioContext` says so.
            //    ⇒ supplying these three is what makes the existing item scenario-capable on CGF, and it
            //    is why ruling R3 (no toolbar changes) is respected — the slot is untouched.
            // ⚠ `isScenarioContext` is TRUE whenever no AI document is active: this host's only other
            //   save target is an AI graph, so "not editing a graph" is exactly "the scenario is the
            //   thing you'd be saving". ⛔ Not a perspective query — CGF has no scenario perspective.
            isScenarioContext:     () => _aiDocumentManager?.Active == null,
            hasLoadedScenario:     () => !string.IsNullOrEmpty(_scenarioSession?.LoadedScenarioName),
            saveScenarioAction:    () => _scenarioSession?.SaveCurrent(),
            // ⛔ requestScenarioSaveAs stays UNSUPPLIED: Save-As needs a name, and this host composes no
            //   modal browser to ask for one. ⇒ `scenario.saveAs` is not registered here, and `File/Save`
            //   with nothing loaded is a no-op rather than a lie. Ruling 49 — the absence is real.
            requestScenarioSaveAs: null);

        var toolbarIcons = new Hrot.Editor.AiShared.Adapters.SilkIconProvider(windowManager.Atlas);

        // ⭐ CE-058's icon keys are registered at the TOP of RegisterWindows (shared table), which is
        //   ordered before this section — what BuildRadioModel's first frame needs.

        // ⭐⭐⭐ CE-054 — THE PERSPECTIVE-SWITCH BUTTONS, which this host never showed.
        // 📄 The user's `--mode cgf` visual check, 2026-08-26, symptom 3.
        //
        // 🔴 MEASURED: `PerspectiveToolbarSection` was constructed in exactly ONE place repo-wide —
        //    `EditorSubsystem.cs:4448`. ⇒ on CGF the perspective radio group simply did not exist, so a
        //    host with several registered perspectives offered no way to switch between them.
        // ⭐ Same type, same sortOrder 20 (§8's perspective group range 20–29), same icon provider shape —
        //   ⛔ no CGF-private switcher, and nothing new invented.
        // ⚠ Guarded on MainToolbar for the reason CgfEditorShellToolbar documents: a toolbar-less host
        //   still composes commands, and the section only lays out buttons.
        // ⚠ PHASE 2 SLICE ③ (H2) — the `MainToolbar != null` guard is GONE: 📐 measured, `WindowManager`
        //   exposes a `readonly … = new()` field, so it is never null. ⛔ A guard against an impossible
        //   state reads as a real capability check and invites the next reader to add a third.
        _perspectiveToolbarSection = new Fdp.Presentation.WindowManager.PerspectiveToolbarSection(
            windowManager, toolbarIcons, windowManager.MainToolbar, sortOrder: 20);

        // ⭐⭐⭐ CE-059 — THE AI-DEBUG COMMAND GROUP. 📄 The user's 2026-08-27 `--mode all` check.
        // ⚠⚠ THIS REVERSES THE ARGUED OMISSION recorded ~90 lines below (now marked SUPERSEDED). ⭐ The
        //    old argument's PREMISE was measured and it was right at the time — *"CGF has NO debug
        //    session"* — so the fix is not to bind the ids to something else (which is what that note
        //    correctly refused, and still refuses: `debug.pause` is AI-GRAPH stepping, ⛔ never cluster
        //    time). ⭐⭐ The fix is that this host now HAS the session CE-059 constructs at :963, so the
        //    same ids mean the same thing on both hosts and the SAME-by-id rail is satisfied by
        //    construction rather than by omission.
        // ⭐ Registered BEFORE RegisterCommonCore, for the editor's stated reason: the layout helper emits
        //   a button only for a command the shell can already service, so every registrar must run first.
        if (_aiDebugRegistry != null)
        {
            Hrot.Blueprints.Editor.Debug.AiDebugCommands.Register(
                windowManager.ShellCommands.Register, _aiDebugRegistry);

            // ⭐⭐ …and the wire WITHOUT WHICH the group would be permanently disabled (ruling 49 —
            //    present-and-broken is worse than absent). Same shared mirror the editor uses.
            Hrot.Editor.AiShared.Debug.ActiveDebugSessionMirror.Wire(
                _aiDocumentManager, _aiDebugRegistry, () => _blueprintDebugSession);
        }

        // ⭐⭐⭐ PHASE 1 — COMPOSED AS A BUNDLE, not called as a static. 📄
        //    docs/DESIGN_Subsystem_Composition_Unification.md §5b.
        // ⭐⭐ What changed and what did NOT:
        //    · the shared table, the HostServices subset and the derivation are IDENTICAL — ⛔ this is not
        //      a re-implementation, `ShellCommandCoreBundle` calls the very same RegisterCommonCore;
        //    · the toolbar and the menu now come from ONE UiBundleContext ⇒ ⭐ they cannot be two
        //      different hosts' registries, which the six-argument static could not prevent.
        // ⚠ `windowManager.MainToolbar` is NOT passed and NOT guarded here any more: 📐 measured, it
        //   returns an inline-initialised readonly field and is NEVER null, so the old
        //   `if (windowManager.MainToolbar != null)` was a dead branch and the comment explaining a
        //   "toolbar-less host" described a state that cannot occur.
        var shellCoreBundle = new Hrot.Editor.AiShared.Windows.ShellCommandCoreBundle(
            windowManager.ShellCommands,
            toolbarIcons,
            new Hrot.Editor.AiShared.Windows.CgfEditorShellToolbar.HostServices(
                // ⭐⭐⭐ CE-049 (Axis-C E2) item ④ — OpenAsset / NewAsset are SUPPLIED now. 📄
                //    docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md §3 ④.
                // ⛔ The prior comment here said "CGF composes no AssetPickerLauncher or NewAssetLauncher
                //   — the editor builds both from catalogs + a router this host does not wire." 📐 That is
                //   the state E2 ended: WirePickerShell composes both over CGF's own catalog and
                //   WindowManager, so the buttons AND their File-menu items derive from the shared table
                //   with NO new toolbar model — which is what ruling R3 requires.
                // ⭐ `AssetKindFilter.All` matches the editor's own OpenAsset wiring exactly.
                OpenAsset:            _assetPickerLauncher != null
                    ? (Action)(() => _assetPickerLauncher.Open(Hrot.Editor.AiShared.Browser.AssetKindFilter.All))
                    : null,
                NewAsset:             _newAssetLauncher != null
                    ? (Action)(() => _newAssetLauncher.Open())
                    : null,
                CompileReload:        () => ReloadActiveAiDocument(),
                CompileReloadEnabled: () => _aiDocumentManager?.Active != null));
            // ⭐⭐⭐ UXI-05 item ④ — CGF's File menu, emitted from the SAME table as its toolbar.
            // ⛔ GLOBAL scope (menuPerspective left null): design §6 — these File items are
            //    cross-perspective on both hosts. The PER-PERSPECTIVE model exists and is railed; the
            //    first item that genuinely differs per perspective is what should use it.
            // ⚠⚠ MEASURED CONSEQUENCE — CGF gains exactly ONE item, `File/Save`, ⛔ not the four the
            //    handoff lists. That is the derivation working, not a shortfall:
            //    · `File/Open Asset…` / `File/New Asset…` — this host supplies no handler, so no
            //      descriptor, so no item. 📌 The same ruling-49 absence CE-016 §9.4 already recorded
            //      for their toolbar buttons; ⭐ they appear for free the day a picker is composed.
            //    · `File/Reload` — the shared slot deliberately carries NO MenuPath, because the EDITOR
            //      has no File/Reload today and one table cannot give CGF an item the editor does not
            //      get without an `if (host==…)`, which ruling 58 forbids. 📄 See the Layout note.
        // ⚠ The menu is no longer an ARGUMENT — the bundle takes it off the shared context, which is the
        //   point: it and the toolbar are now guaranteed to be the same host's registries.

        // ⭐ ONE list. ⛔ A host with fewer bundles is a SUBSET, never a branch (§3.3 / ruling 58).
        //   ⚠ One entry today by design: the first adopter proves the seam, it does not populate it.
        Fdp.Toolkit.Runner.UiBundleHost.Compose(
            new Fdp.Toolkit.Runner.IUiBundle[] { shellCoreBundle },
            new Fdp.Toolkit.Runner.UiBundleContext(windowManager));

        // ⭐⭐ Non-null by construction: `Compose` above called `RegisterInto`, which sets this. ⛔ The
        //    throw is NOT defensive noise — a null here means the bundle silently did not register, which
        //    is precisely the "a feature is quietly absent on this host" failure the whole seam exists to
        //    make impossible. ⚠ The property stays NULLABLE on purpose: "never composed" and "composed and
        //    registered nothing" are different facts, and collapsing them is the conflation this codebase
        //    keeps paying for (BP-487's manifest cell, CE-064's empty loop).
        var toolbarIds = shellCoreBundle.RegisteredToolbarIds
            ?? throw new InvalidOperationException(
                "the shell-command-core bundle reported no toolbar ids after composition — it did not run.");

        // ⭐⭐⭐ CE-046 (design §3 ④, §3a) — THE DISTINCT SCENARIO ITEMS, from the SAME registrar the
        //    editor uses. 📄 docs/DESIGN_Cgf_Scenario_Session_Slice.md. Ruling R2 — distinct items, no
        //    chameleons, no per-host default in the menu.
        // ⭐⭐ This is the line that closes the gap design §2 measured: *"CGF registers only the
        //    engine-default Settings … the wall is ScenarioMenuCommands binding to the editor-only
        //    IEditorLogic."* It now binds to IScenarioSession, so there is no wall left.
        if (_scenarioSession != null)
            Hrot.Editor.AiShared.Scenarios.ScenarioMenuCommands.Register(
                registerCommand: windowManager.ShellCommands.Register,
                menu:            windowManager.GlobalMenu,
                commands:        windowManager.ShellCommands,
                session:         _scenarioSession,
                // ⭐⭐⭐ CE-049 (Axis-C E2) — THE TWO NULLS ARE GONE. 📄
                //    docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md §3 ③.
                // ⭐ Slice A's own note said these items "light up for free the day a picker is composed
                //   here (Axis-C E2)" — and it was literally free: the seams changed, the menu code did
                //   not. ⇒ the items are now ENABLED with plain labels, on both hosts, from one registrar.
                // ⚠ Still null-CONDITIONAL, not unconditionally non-null: if the picker shell somehow did
                //   not compose, ruling 49's greyed-with-cause remains the honest fallback rather than a
                //   live-looking control that throws.
                openPicker:       _assetPickerLauncher != null
                    ? (kinds, callback) => _assetPickerLauncher.Open(kinds, callback)
                    : null,
                openSaveAsDialog: _saveAsBrowser != null
                    ? onNamed => OpenScenarioSaveAsDialog(catalog, onNamed)
                    : null,
                // ⭐⭐⭐ Ruling 53 + UX_Feature_Modal_Surfaces.md §2.0b — this host is headless-first: a
                //    modal on an unattended node is a hang, not a prompt. ⇒ it PROCEEDS, and the log IS
                //    the safety net. ⛔ Passing null would proceed SILENTLY, which is the one option the
                //    ruling forbids.
                confirmNewExercise: run =>
                {
                    FdpLog<CgfSubsystem>.Warn(
                        "[CGF] New Exercise requested — finishing the running exercise and clearing the "
                      + "world CLUSTER-WIDE. This node is headless-first and did not prompt (ruling 53); "
                      + "this log is the record.");
                    run();
                },
                showMigrationHistory: sidecars => FdpLog<CgfSubsystem>.Info(
                    "[CGF] {0} migration sidecar(s) for the loaded scenario.", sidecars.Count));

        // ⚠⚠ SUPERSEDED 2026-08-27 by CE-059 — the group IS registered now, ~100 lines above.
        // ⭐⭐ HISTORY, kept because HALF of it is still binding. The omission argued here rested on two
        //    claims; the FIRST has been overtaken and the SECOND still holds:
        //    ⛔ OVERTAKEN — *"CGF has NO debug session (this very file passes `session: null` to
        //      QuickReloadService)"*. True when written; ⭐ CE-059 constructs and attaches a
        //      BlueprintDebugSession at :963 from three arguments this file already held.
        //    ⭐⭐ STILL BINDING — `debug.*` is AI-GRAPH session stepping; `CgfClusterDebugTimeController`
        //      is CLUSTER-TIME control (RequestPause/Resume/StepOneTick over the whole cluster). ⛔ Binding
        //      the time controller to `debug.continue`/`debug.pause` would still make ONE ID MEAN TWO
        //      THINGS across hosts, which the SAME-by-id rail exists to prevent. ⇒ CE-059 does NOT do
        //      that: it supplies the AI-graph session the ids already meant. ⭐ Cluster-time stepping keeps
        //      its own separate affordance, `MainToolbarTimeControlSection`.

        FdpLog<CgfSubsystem>.Info(
            "[CGF] Shell toolbar adopted — {0} asset(s) indexed, entries [{1}].",
            catalog.All.Count, string.Join(", ", toolbarIds));
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
        // ⭐⭐⭐ PHASE 2 SLICE ① — the POLICY below (kind dispatch, the default arm, the try/catch and
        //    ruling 53's origin-side log) now lives ONCE, in
        //    `Hrot.Editor.AiShared.Documents.AiAssetReload`, shared with the editor. 📐 The editor's own
        //    path had NONE of the three: no try/catch, no log, no default arm (design §5c.6.4).
        //    ⛔ What stays here is only what names CGF's concrete types: the `ToDto` map and the
        //    QuickReloadService adapter. 📄 DESIGN_Subsystem_Composition_Unification.md §5c.6.
        if (_quickReload == null || _aiDocumentManager?.Active == null)
            return LastReloadStatus = Hrot.Editor.AiShared.Documents.AiAssetReload.NoActiveDocument;

        var qrs = _quickReload;
        var ctx = _aiDocumentManager?.Active?.ViewState as Hrot.Editor.AiShared.Windows.AiCanvasContext;

        // ⭐ The compiler, expressed in terms AiShared can name — `QuickReloadResult` lives on the far
        //   side of the reference cycle, so the adapter belongs here and is one expression.
        Hrot.Editor.AiShared.Documents.AiAssetReload.CompileSources compile = (sources, asmName) =>
        {
            var r = qrs.TriggerFromSourcesAsync(
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(sources, s => (s.Source, s.FileName))),
                asmName).GetAwaiter().GetResult();
            return new Hrot.Editor.AiShared.Documents.AiAssetReload.CompileOutcome(
                r.Succeeded, r.ErrorMessage, r.DurationMs);
        };

        // ⚠ An arm returning null means "right kind, no model to compile" — the shared policy then
        //   supplies the one `NoCompilableContext` wording, which is what keeps this host's old
        //   runtime-type dispatch byte-identical to the editor's kind dispatch.
        var arms = new Hrot.Editor.AiShared.Documents.AiReloadArms(
            Blueprint: () =>
            {
                if (ctx?.AssetRef is not Hrot.Blueprints.Core.Assets.BlueprintAsset bp) return null;
                var r = qrs.TriggerAsync(bp).GetAwaiter().GetResult();
                return Hrot.Editor.AiShared.Documents.AiAssetReload.FormatBlueprint(
                    bp.Name,
                    new Hrot.Editor.AiShared.Documents.AiAssetReload.CompileOutcome(
                        r.Succeeded, r.ErrorMessage, r.DurationMs));
            },
            BTree: () => ctx?.AssetRef is Hrot.BTree.Editor.Model.BehaviorTreeAsset bt
                ? Hrot.Editor.AiShared.Documents.AiAssetReload.ReloadBTree(
                      Hrot.BTree.Editor.Persistence.BehaviorTreeAssetMapper.ToDto(bt), compile)
                : null,
            Hsm: () => ctx?.AssetRef is Hrot.Hsm.Editor.Model.HsmAsset hsm
                ? Hrot.Editor.AiShared.Documents.AiAssetReload.ReloadHsm(
                      Hrot.Hsm.Editor.Persistence.HsmAssetMapper.ToDto(hsm), compile)
                : null);

        return LastReloadStatus = Hrot.Editor.AiShared.Documents.AiAssetReload.Reload(
            _aiDocumentManager,
            arms,
            log: (name, status) => FdpLog<CgfSubsystem>.Info(
                "[CGF] AI asset reload requested for '{0}' — {1}", name, status));
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
    /// <para>🔴 <b>Ruling 67, and it is REPORTED, not silently swallowed.</b> Every root comes from
    /// <see cref="Hrot.Editor.AiShared.AssetRoots.ResolveAssetsRoot"/> — config → source walk-up → output
    /// directory — and this logs WHICH arm answered, plus a warning when only the last one did. ⛔ The
    /// catalog may then be genuinely empty and <c>GET /assets</c> says so — ⚠ a silent empty list is the
    /// failure this slice exists to end.</para>
    ///
    /// <para>⚠⚠ <b>This paragraph used to describe a null-answering <c>ResolveProjectDir</c> and null JSON
    /// roots.</b> ⛔ That stopped being true when ruling 67 landed here and the text was not updated — 📌
    /// exactly the *"the design is behind the code"* rot obligation ⑤ exists to prevent, found while
    /// carrying the same fix to the editor (<c>CE-093</c>).</para>
    /// </summary>
    private Hrot.Editor.AiShared.Catalog.AiAssetCatalogBuilder BuildAssetCatalog()
    {
        // ⭐⭐⭐ RULING 67 RESOLVED — config → source walk-up → output directory, in AiShared's stated
        //    "single authority for roots". ⛔ The bare walk-up that used to be here answered null on a
        //    DEPLOYED node, which is what made authoring on CGF impossible; the config arm is the fix.
        //    ⚠ Always non-null now, so the old "the catalog will be EMPTY" warning is replaced by a
        //    statement of WHICH arm answered — 📌 "empty" and "pointed elsewhere" are different problems
        //    and the log has to distinguish them.
        // ⭐⭐⭐ CE-098 (J1-a) — the root-reporting policy lives in AssetRoots now; this host supplies only
        //    its own routing. ⭐ Same shape as `warnMissingRoot` below: shared BODY, host PREFIX. 📄 §5c.15.
        Hrot.Editor.AiShared.AssetRoots.ReportBase(
            info: m => FdpLog<CgfSubsystem>.Info("[CGF] {0}", m),
            warn: m => FdpLog<CgfSubsystem>.Warn("[CGF] {0}", m),
            AiBehaviorsProjectPath);

        // ⭐⭐⭐ CE-093 (J1) — this local function WAS `ResolveAssetsRoot`, spelled out.
        //    📐 `AssetRoots.ResolveAssetsRoot(kind, segments)` is defined as
        //    `Path.Combine(ResolveBase(segments), AssetsRelative(kind))` — byte-for-byte what `RootFor`
        //    computed. ⇒ ⛔ ruling 9: the shared resolver existed and this host re-spelled it. ⭐ Adopting
        //    it is behaviour-preserving HERE and is the same call the editor now makes, which is the
        //    point — one resolver, so the two hosts cannot drift again.
        string RootFor(Hrot.Editor.AiShared.AssetKind kind) =>
            Hrot.Editor.AiShared.AssetRoots.ResolveAssetsRoot(kind, AiBehaviorsProjectPath);

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

        var builder = new Hrot.Editor.AiShared.Catalog.AiAssetCatalogBuilder(
            btreeContrib, hsmContrib, bpContrib,
            asm => btreeContrib.LoadFrom(asm),
            asm => hsmContrib.LoadFrom(asm),
            ()  => bpContrib.Refresh(),
            bTreeJsonContributor: btreeJsonContrib,
            hsmJsonContributor:   hsmJsonContrib,
            // ⭐⭐ CE-091 (J2 K1) — the JSON refresh path, as delegates for the same documented reason the
            //    LoadFrom callbacks above are delegates: these contributors' projects reference AiShared,
            //    so it cannot name their types. ⚠ Roots resolved AT CALL TIME (the fields are assigned
            //    later in this method).
            bTreeJsonRefresh: root => btreeJsonContrib.Refresh(rootDirectory: root),
            bTreeJsonRootDir: () => _btreeJsonRootDir,
            hsmJsonRefresh:   root => hsmJsonContrib.Refresh(rootDirectory: root),
            hsmJsonRootDir:   () => _hsmJsonRootDir,
            // ⭐⭐ CE-095 (J1 K5) — the missing-root warning, routed to this host's log. ⚠ The message BODY
            //    is now the shared one, so the two hosts cannot word the same fault differently; the
            //    `[CGF]` prefix stays here because the routing is the host's.
            warnMissingRoot:  msg => FdpLog<CgfSubsystem>.Warn("[CGF] {0}", msg));

        // ⭐⭐⭐ CE-095 (J1 K5) — the initial JSON refresh, now the SAME call every later refresh makes.
        //    🔴 What was here: an inline `Directory.Exists` + `Refresh` + `Warn` pair per kind — a second
        //       implementation of the policy `RefreshJsonContributors` owns, differing in that one clause.
        //    ⚠ Moved to AFTER construction (it was before): `AddContributor` calls `Rebuild()` and each
        //      contributor's `ContributorChanged` re-triggers it, so the cache is correct either way.
        builder.RefreshJsonContributors(Hrot.Editor.AiShared.AssetKind.BTree);
        builder.RefreshJsonContributors(Hrot.Editor.AiShared.AssetKind.Hsm);

        // ⭐⭐⭐ CE-053 — THE SCENARIO CONTRIBUTOR, which this host never had.
        // 📄 The user's `--mode cgf` visual check, 2026-08-26, symptoms 4/5/6 — ONE root, three symptoms.
        //
        // 🔴🔴 MEASURED: CGF's catalog carried ZERO AssetKind.Scenario entries, because
        //    ScenarioCatalogContributor lived in Hrot.Editor. ⇒ `File/Edit/Open Scenario`,
        //    `File/Live/Load Scenario` and `File/Open Asset`'s Scenario tab were all EMPTY.
        //    ⚠ CE-049 wired this host's picker and never gave its catalog anything to show — the picker
        //    worked perfectly and had nothing to list, which is why every model-level rail stayed green.
        //
        // ⭐ Mirrors EditorSubsystem:1078, and ⭐⭐ CE-057 makes the SOURCE the same too. 📐 The editor
        //   projects `IEditorLogic.AvailableScenarios`, whose one production source is
        //   `EditorSubsystem:1812`: `ScenarioEnumeration.EnumerateRelPaths(EditorBootstrap.ScenariosRoot)`
        //   — the SAME function over the SAME root this line now uses. ⇒ ⛔ the difference that made this
        //   picker empty is gone; what remains is only the indirection through IEditorLogic, which exists
        //   on that host because its panels read the list through the facade.
        builder.Catalog.AddContributor(new Hrot.Editor.AiShared.Catalog.ScenarioCatalogContributor(
            () => Hrot.Editor.AiShared.Catalog.ScenarioEnumeration.EnumerateRelPaths(
                      OrchestrationConstants.GetSharedScenariosRoot()),
            // ⭐⭐ CE-064 — the ROOT is passed, so each scenario asset carries a real SourceFilePath and
            //   `open_asset_by_path` can reach it. ⛔ This caller HAS the root (same expression above), so
            //   omitting it would be a silent default, not an honest absence.
            scenariosRoot: () => OrchestrationConstants.GetSharedScenariosRoot()));

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
        // ⭐ AX-008 — ROUTED to the shared resolver `2026-08-25`. 📐 This body and EditorSubsystem's were
        //   line-for-line identical, each commenting that the other existed; the Axis-B egress would have
        //   been the third. ⇒ NetworkIdResolver owns both directions now.
        => Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(_context?.World, entity);

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-051</c> — publishes a network-id-keyed command for a clicked entity, resolving the id
    /// through the ONE resolver this host already exposes.
    ///
    /// <para>⚠ The context menu hands us an <c>Entity</c>; the shared commands are keyed by NETWORK id,
    /// because they must work from MCP and from a remote node too. ⛔ An entity with no network identity is
    /// skipped rather than published with <c>0</c>, which would centre on whatever happens to hold id 0.</para>
    /// </summary>
    private void PublishForEntity<T>(Entity entity, Func<long, T> command) where T : unmanaged
    {
        if (_context == null || !_context.World.IsAlive(entity)) return;
        long netId = RuntimeNetworkIdOf(entity);
        if (netId <= 0) return;
        _context.World.Bus.Publish(command(netId));
    }

    // ⭐⭐⭐ CE-051 (Axis-C E3) — `CenterCameraOnEntity` IS GONE. 📄 design §3 ⑤ / §6.
    //    🔴 It was MEASURED BROKEN: it assigned `Camera.Target`, which `MapCamera.Update` overwrites from
    //       `_targetTarget` on the very next frame — and this host never set `_targetTarget`, so centring
    //       moved the view to the ORIGIN. ⭐ `CenterOnEntitySystem` (shared) calls `FocusOn` instead, and
    //       it KEEPS this method's better half: NetworkTransform preferred over SimTransform, which is the
    //       fresher position on a host that does not own the entity.

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
        // QA-001: dispose the whole node context — kernel THEN world. This used to be
        // `_context?.Kernel.Dispose()`, which leaked the EntityRepository on every CGF teardown.
        // (CgfApplication, which builds its own world, already disposed it — this path did not.)
        _context?.Dispose();
        // QA-005: the breakpoint machinery owns TWO more repositories — the pre-tick snapshot built
        // here and the post-tick snapshot the manager builds for itself. Both leaked until now.
        _bpManager?.Dispose();
        _bpManager = null;
        _bpPreTickSnapshot?.Dispose();
        _bpPreTickSnapshot = null;
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


