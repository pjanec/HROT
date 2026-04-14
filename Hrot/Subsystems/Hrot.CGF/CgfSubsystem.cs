using System.Linq;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using FDP.Toolkit.ImGui.Utils;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Layers;
using FDP.Toolkit.Vis2D.Tools;
using FDP.Toolkit.Vis2D.Defaults;
using Hrot.Presentation.Windows;
using FdpEntityInspectorPanel = FDP.Toolkit.ImGui.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel    = FDP.Toolkit.ImGui.Panels.EventBrowserPanel;
using FdpRepositoryAdapter    = FDP.Toolkit.ImGui.Adapters.RepositoryAdapter;
using FdpInspectorState       = FDP.Toolkit.ImGui.Abstractions.InspectorState;
using Hrot.Map.Common;
using Hrot.CGF.Brains;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Hrot.CGF.Configuration;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.Engine.Runner;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.CGF;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// Migrated in EAM-M003 to use <see cref="HrotNodeBuilder"/> instead of <see cref="CgfApplication"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem, Fdp.Engine.Runner.IMapCameraProvider, IWindowRegistrar
{
    private HrotNodeContext?  _context;
    private NetworkEntityMap? _entityMap;
    private Action?           _cgfNetworkPolling;

    // ── Headless + doctrine registry ──────────────────────────────────────────
    private bool               _headless;
    private DoctrineRegistry?  _doctrineRegistry;
    private SystemGroup?       _simGroup;
    private Hrot.Core.Network.INetworkFactory? _networkFactory;

    // ── Visualization ─────────────────────────────────────────────────────────
    private MapCanvas?                 _canvas;
    private DefaultSelectionState?     _selectionState;
    private CgfDebugVisualizerAdapter? _visualizerAdapter;
    private StandardInteractionTool?   _interactionTool;
    private EntityQuery?               _entityQuery;

    // ── FDP panels ────────────────────────────────────────────────────────────
    private FdpEntityInspectorPanel _fdpEntityInspector = new();
    private FdpEventBrowserPanel    _fdpEventBrowser    = new();
    private FdpRepositoryAdapter?   _fdpRepoAdapter;
    private FdpInspectorState       _fdpInspectorState  = new();
    private uint                    _fdpFrameCount;

    // ── Map context menu ──────────────────────────────────────────────────────
    private Entity _pendingContextMenuEntity;
    private bool   _openContextMenuThisFrame;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

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
    internal Fdp.Kernel.EntityRepository? World => _context?.World;

    /// <summary>TestHook: exposes the CGF doctrine registry so integration tests can register
    /// scenario-specific doctrines (e.g. UrbanCombat) before the cluster transitions to
    /// OperatingLive and scenario entities begin executing missions.</summary>
    internal DoctrineRegistry? TestHook_DoctrineRegistry => _doctrineRegistry;

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
        dtoCmd.Grants.Add(new DescriptorGrant
        {
            DescriptorTypeId = _networkFactory?.WorldPosDescriptorId ?? 2L,
            NodeId           = muscleNodeId,
        });
        _context.World.Bus.PublishManaged(dtoCmd);

        // 2. Publish SpawnEntityCommand (CGF/Brain owns entity identity).
        _context.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId   = networkId,
            TkbType     = tkbType,
            OwnerNodeId = _context.NodeId,
            InitType    = Fdp.ModuleHost_Core.Network.Interfaces.ReliableInitType.AllPeers,
            RequestId   = System.Guid.Empty,
        });

        return networkId;
    }

    private int _testIdCounter;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {        _headless = config.Headless;
        // ── Create DDS participant in the Application Shell (Composition Root) ───
        // Rule: only the outermost executable may instantiate DdsParticipant.
        // HrotNodeBuilder no longer has a fallback.
        var shellParticipant = _networkFactory?.Participant;
        if (shellParticipant == null)
        {
            int cgfNodeId = config.NodeId != 0 ? config.NodeId : 400;
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
            NodeId              = config.NodeId != 0 ? config.NodeId : 400,
            // CgfSubsystem always creates a DDS participant — Headless here controls only
            // the Raylib/ImGui window (UI), not the network layer.
            // This mirrors SimHostApp which also hardcodes Headless = false for HrotNodeConfig.
            Headless            = false,
            ExternalParticipant = shellParticipant,
            SubsystemName       = "CGF",
        };
        _context = new HrotNodeBuilder(nodeConfig)
            .WithRole("CgfNode", NodeRole.Brain)
            .Build();

        _entityMap = _context.EntityMap;
        CgfComponentRegistry.RegisterAll(_context.World);

        // ── Register base infrastructure modules ───────────────────────────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // ── Create replication module via factory (Brain role) ─────────────────
        // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
        var doctrineRegistry = new DoctrineRegistry();
        CgfDoctrineSetup.RegisterAll(doctrineRegistry, _context.GeoTransform!);
        _doctrineRegistry = doctrineRegistry;

        // Configure network factory for this node so auxiliary translators can be created.
        var nodeFactory = _networkFactory?.ConfigureForNode(_context, NodeRole.Brain, doctrineRegistry);

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

        // ── Register CGF simulation logic (Brain-specific) ─────────────────────
        var cgfLogicPack = new CgfLogicPack(doctrineRegistry, _entityMap);
        _context.Kernel.RegisterModule(cgfLogicPack);

        // Execute the Brain systems every frame via a SystemGroup.
        var simGroup = new SystemGroup();
        simGroup.Create(_context.World);
        _simGroup = simGroup;
        cgfLogicPack.RegisterSystems(simGroup);

        var adapters = nodeFactory?.CreateCgfEntityLifecycleAdapters();
        if (adapters != null)
        {
            var tkbDb       = _context.TkbDb!;
            var idAllocator = _context.IdAllocator!;
            var elm         = (EntityLifecycleModule)_context.BaseModules
                                  .First(m => m is EntityLifecycleModule);

            var finalizationSystem = new EntityRequestFinalizationSystem(adapters.AckSink, _entityMap!);

            var requestSystem = new CreateEntityRequestSystem(
                requestSource:        adapters.RequestSource,
                ackSink:              adapters.AckSink,
                tkbDb:                tkbDb,
                idAllocator:          idAllocator,
                localNodeId:          _context.NodeId,
                jsonAttributeCompiler: adapters.JsonCompiler,
                finalizationSystem:   finalizationSystem,
                isDefaultProcessor:   true,
                ownershipStrategy:    adapters.OwnershipStrategy);

            var deleteSystem = new DeleteEntityRequestSystem(
                adapters.DeleteSource,
                adapters.AckSink,
                _entityMap!,
                finalizationSystem,
                _context.NodeId);

            var spawnSystem = new NetworkSpawningSystem(
                tkbDb,
                elm,
                _entityMap!,
                idAllocator,
                _context.NodeId);

            _context.Kernel.RegisterGlobalSystem(spawnSystem);
            _context.Kernel.RegisterGlobalSystem(requestSystem);
            _context.Kernel.RegisterGlobalSystem(deleteSystem);
            _context.Kernel.RegisterGlobalSystem(finalizationSystem);

            // Store polling action for heartbeat updates in Update().
            _cgfNetworkPolling = adapters.PollNetwork;
        }

        // Auxiliary translators (time-sync, combat, mission-control) via the injected factory.
        // Mirrors SimHostApp.cs pattern: nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(kernel)
        nodeFactory?.CreateSimHostAuxiliaryTranslators()?.RegisterOn(_context.Kernel);

        // ── Initialize ─────────────────────────────────────────────────────────
        _context.Kernel.Initialize();
        // ── Visualization (non-headless only) ─────────────────────────────────────
        if (!_headless)
        {
            _entityQuery = _context.World.Query().With<NetworkIdentity>().Build();

            _canvas = new MapCanvas();
            _canvas.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);

            _selectionState    = new DefaultSelectionState();
            _visualizerAdapter = new CgfDebugVisualizerAdapter(_doctrineRegistry);
            _fdpRepoAdapter    = new FdpRepositoryAdapter(_context.World);

            var renderLayer = new EntityRenderLayer(
                "CGF Entities", -1, _context.World, _entityQuery, _visualizerAdapter, _selectionState)
                { Canvas = _canvas };
            _canvas.AddLayer(renderLayer);

            _interactionTool = new StandardInteractionTool(_context.World, _entityQuery, _visualizerAdapter);

            _interactionTool.OnEntitySelectRequest += (entity, augment) =>
            {
                if (!_context.World.IsAlive(entity)) return;
                if (augment)
                    _selectionState.AddSelection(entity);
                else
                {
                    _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                }
            };

            _interactionTool.OnRegionSelected += entities =>
            {
                _selectionState.ClearSelection();
                foreach (var e in entities)
                    _selectionState.AddSelection(e);
            };

            _interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
            {
                if (btn == MouseButton.Right && hitEntity != Entity.Null)
                {
                    _selectionState.PrimarySelected = hitEntity;
                    _fdpInspectorState.SelectedEntity = hitEntity;
                    _pendingContextMenuEntity = hitEntity;
                    _openContextMenuThisFrame = true;
                }
            };

            _canvas.SwitchTool(_interactionTool);

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
        _simGroup?.Run();   // tick CGF Brain logic (BTree / mission / locomotion dispatch)
#pragma warning disable CS0618 // legacy Update(float) used intentionally in CgfSubsystem
        _context?.Kernel.Update(deltaTime);
#pragma warning restore CS0618
        if (!_headless && _context != null)
        {
            _fdpFrameCount++;
            _fdpEventBrowser.Update(_context.EventBus, _fdpFrameCount);
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

        // Render the CGF Hover Label Tooltip
        if (!ImGui.GetIO().WantCaptureMouse && _canvas != null && _context?.World != null && _visualizerAdapter != null)
        {
            var mouseWorld = _canvas.Camera.ScreenToWorld(Raylib.GetMousePosition());
            var hovered    = _canvas.PickTopmostEntity(mouseWorld);

            if (hovered.HasValue && hovered.Value != Entity.Null)
            {
                var label = _visualizerAdapter.GetHoverLabel((ISimulationView)_context.World, hovered.Value);
                if (!string.IsNullOrEmpty(label))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(label);
                    ImGui.EndTooltip();
                }
            }
        }

        if (_openContextMenuThisFrame)
        {
            ImGui.OpenPopup("##cgf_map_ctx");
            _openContextMenuThisFrame = false;
        }

        if (ImGui.BeginPopup("##cgf_map_ctx"))
        {
            if (_pendingContextMenuEntity != Entity.Null
                && _context?.World != null
                && _context.World.IsAlive(_pendingContextMenuEntity))
            {
                if (ImGui.MenuItem("Center on entity"))
                    CenterCameraOnEntity(_pendingContextMenuEntity);

                if (ImGui.MenuItem("Select entity"))
                {
                    if (_selectionState != null)
                        _selectionState.PrimarySelected = _pendingContextMenuEntity;
                    _fdpInspectorState.SelectedEntity = _pendingContextMenuEntity;
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Delete entity"))
                    DeleteEntity(_pendingContextMenuEntity);
            }

            ImGui.EndPopup();
        }
    }

    /// <inheritdoc/>
    public MapCameraView? GetCameraView() => _canvas?.Camera?.GetCameraView();

    /// <inheritdoc/>
    public void ApplyCameraView(MapCameraView view) => _canvas?.Camera?.ApplyCameraView(view);

    // Non-interface helper kept for backward-compat with tests.
    public MapCamera? GetMapCamera() => _canvas?.Camera;

    /// <inheritdoc/>
    public void RegisterWindows(FDP.Toolkit.ImGui.WindowManager.WindowManager windowManager)
    {
        if (_headless) return;

        windowManager.RegisterWindow(new FdpEntityInspectorWindow(
            "cgf_fdp_inspector", "CGF Entity Inspector", "CGF",
            _fdpEntityInspector,
            () => _fdpRepoAdapter,
            () => _fdpInspectorState,
            TitleBarColor));

        windowManager.RegisterWindow(new FdpEventBrowserWindow(
            "cgf_fdp_events", "CGF Event Browser", "CGF",
            _fdpEventBrowser,
            TitleBarColor));
    }

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
        _simGroup?.Dispose();
        _simGroup = null;
        _context?.Kernel.Dispose();
        _context?.Participant?.Dispose();
        _context = null;
    }

}


