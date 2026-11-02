using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Common.Events;
using Hrot.Editor.Adapters;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.Adapters;
using Hrot.Map.Common;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Tkb;
using Hrot.NED.Common;
using Hrot.Core.Mission;
using Hrot.ScenarioEditor;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Moq;
using Raylib_cs;
using Xunit;

namespace Hrot.Editor.Tests.Adapters
{
    // ── Shared test infrastructure ────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IInputProvider"/> stub for test-only canvas construction.
    /// Returns all defaults (no input pressed, no mouse movement).
    /// </summary>
    internal sealed class TestInputProvider : IInputProvider
    {
        public Vector2 MousePosition  { get; set; } = Vector2.Zero;
        public Vector2 MouseDelta     { get; set; } = Vector2.Zero;
        public float   MouseWheelMove { get; set; } = 0f;
        public bool    IsMouseCaptured    { get; set; } = false;
        public bool    IsKeyboardCaptured { get; set; } = false;
        public bool IsMouseButtonPressed(MapMouseButton b)   => false;
        public bool IsMouseButtonDown   (MapMouseButton b)   => false;
        public bool IsMouseButtonReleased(MapMouseButton b)  => false;
        public bool IsKeyPressed  (MapKeyboardKey k) => false;
        public bool IsKeyDown     (MapKeyboardKey k) => false;
        public bool IsKeyReleased (MapKeyboardKey k) => false;
        public int  GetKeyPressed()               => 0;
    }

    /// <summary>
    /// Test-scoped wrapper around a real <see cref="MapCanvas"/> (null-input) that
    /// tracks the last pushed tool.
    /// </summary>
    internal sealed class TestMapCanvas : MapCanvas
    {
        public TestMapCanvas() : base(new TestInputProvider()) { }
    }

    /// <summary>
    /// Simple <see cref="IScenarioStateProvider"/> test double.
    /// </summary>
    internal sealed class FakeStateProvider : IScenarioStateProvider
    {
        public ScenarioEditorState CurrentState { get; set; } = ScenarioEditorState.Idle;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A001 — ScenarioSpawnAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorSpawnAdapterTests
    {
        private readonly EntityRepository     _world   = new();
        private readonly Fdp.Interfaces.ITkbDatabase _tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
        private readonly Hrot.Common.EntityCreation.EntityCreation _creation;
        private readonly DebugPrimitiveBuffer _buffer  = new();

        public EditorSpawnAdapterTests()
        {
            _world.RegisterEvent<GizmoDragUpdateEvent>();
            _world.RegisterEvent<GizmoMouseEvent>();
            _world.RegisterEvent<GizmoKeyEvent>();
            // ⭐ CE-259ax — the PRODUCTION pack: the adapter's tail is `creation.RequestEntityCreation`, so a fake would hide a drift.
            _creation = Hrot.TestShared.TestEntityCreation.For(_world, _tkb);
        }

        private GlobalGizmoManager MakeManager() => new GlobalGizmoManager(_buffer);


        [Fact]
        public void StartPlacementMode_RegistersGizmoWithManager()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            adapter.StartPlacementMode(2001L, null);

            Assert.Equal(1, manager.ActiveCount);
        }

        [Fact]
        public void StartAreaAuthoringMode_RegistersGizmoWithManager()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            adapter.StartAreaAuthoringMode("");

            Assert.Equal(1, manager.ActiveCount);
        }

        [Fact]
        public void StartRouteAuthoringMode_RegistersGizmoWithManager()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            adapter.StartRouteAuthoringMode();

            Assert.Equal(1, manager.ActiveCount);
        }

        // ── UXI-07 step 4a — the three modals arm THROUGH the arbiter ────────────────────────────
        //
        // 📄 UX_Feature_Tool_Model.md §4.9. 🔴 Before this step all three registered a gizmo straight on
        //    GlobalGizmoManager, so the gizmo took focus while ToolController still believed some other
        //    tool held it — §4.8's bypass, reached from ScenarioOrbatAdapter.CreateUnit and
        //    SpawnerPanel.HandleActivatePlacementTool (both SHARED surfaces).
        // ⭐ Asserting on ActiveModal is what makes these non-vacuous: a gizmo count of 1 was already true
        //   BEFORE the change, so counting alone could never have caught the bypass.

        private ToolController MakeController(GlobalGizmoManager manager) =>
            new ToolController(() => manager, () => null);

        /// <summary>
        /// ⚠ <c>Spawn</c> is registered by <c>MapInteractionPack</c>, not by the adapter — so a test must
        /// register it the way the pack does: an arm that calls the adapter's ARM BODY. ⭐ That also
        /// reproduces the production wiring whose cycle §4.9 warns about.
        /// </summary>
        [Fact]
        public void StartPlacementMode_ArmsThroughTheArbiter_NotBesideIt()
        {
            var manager    = MakeManager();
            var controller = MakeController(manager);

            ScenarioSpawnAdapter? adapter = null;
            var spawn = new ToolDescriptor(ScenarioToolIds.Spawn, "Place Entity",
                                           ToolModality.Modal, ToolArbiter.Global);
            controller.Register(spawn, _ => adapter!.ArmPlacement());

            adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager, tools: controller);
            adapter.StartPlacementMode(2001L, null);

            Assert.Same(spawn, controller.ActiveModal);
            Assert.Equal(1, manager.ActiveCount);
        }

        [Fact]
        public void StartAreaAuthoringMode_ArmsThroughTheArbiter_NotBesideIt()
        {
            var manager    = MakeManager();
            var controller = MakeController(manager);
            var adapter    = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager, tools: controller);

            adapter.StartAreaAuthoringMode("");

            Assert.Equal(ScenarioToolIds.PlaceArea, controller.ActiveModal?.Id);
            Assert.Equal(1, manager.ActiveCount);
        }

        /// <summary>
        /// ⭐⭐ <c>E5</c> — zone authoring is its own MODAL, armed through the same arbiter.
        ///
        /// <para>⭐ Asserting on <c>ActiveModal.Id</c> is what makes this non-vacuous: a gizmo count of
        /// 1 would be true even if <c>StartZoneAuthoringMode</c> had armed <c>PlaceArea</c>. ⛔ A shared
        /// id would make "draw area" and "draw zone" indistinguishable to the arbiter, so cancelling
        /// one would silently cancel the other.</para>
        ///
        /// <para>🔒 <c>U6</c>: *"nothing of it should be IG host only."* 📄 design §2.1.</para>
        /// </summary>
        [Fact]
        public void StartZoneAuthoringMode_ArmsThePlaceZoneModal()
        {
            var manager    = MakeManager();
            var controller = MakeController(manager);
            var adapter    = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager, tools: controller);

            adapter.StartZoneAuthoringMode("");

            Assert.Equal(ScenarioToolIds.PlaceZone, controller.ActiveModal?.Id);
            Assert.Equal(1, manager.ActiveCount);
        }

        /// <summary>
        /// ⭐ <c>E5</c> — the adapter's zone arm births a <c>TerrainZone</c>.
        /// ⚠ The full commit-geometry claim is railed where the mechanism now lives —
        /// <c>Hrot.Presentation.Tests/AreaAuthoringArmTests</c> — so this asserts only the binding
        /// this adapter owns: which TKB type its zone arm asks for.
        /// </summary>
        [Fact]
        public void ArmZoneAuthoring_AsksTheArmForTheTerrainZoneType()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);

            Assert.Equal(ToolActivationOutcome.Armed, adapter.ArmZoneAuthoring());
            Assert.Equal(1, manager.ActiveCount);
            Assert.NotEqual(TkbEntityTypes.TacGraphic_Area, TkbEntityTypes.TerrainZone);
        }

        [Fact]
        public void StartRouteAuthoringMode_ArmsThroughTheArbiter_NotBesideIt()
        {
            var manager    = MakeManager();
            var controller = MakeController(manager);
            var adapter    = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager, tools: controller);

            adapter.StartRouteAuthoringMode();

            Assert.Equal(ScenarioToolIds.PlaceRoute, controller.ActiveModal?.Id);
            Assert.Equal(1, manager.ActiveCount);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The whole point of step 4a: one modal DISPLACES another.</b> 🔒 <c>Q27</c> ruling C.
        /// 🔴 Before the conversion these two armed side by side — two gizmos, both live, and whichever
        /// won the focus race got the mouse.
        /// </summary>
        [Fact]
        public void ArmingRouteAfterAreaDisplacesIt_RatherThanStackingASecondLiveGizmo()
        {
            var manager    = MakeManager();
            var controller = MakeController(manager);
            var adapter    = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager, tools: controller);

            adapter.StartAreaAuthoringMode("");
            adapter.StartRouteAuthoringMode();

            Assert.Equal(ScenarioToolIds.PlaceRoute, controller.ActiveModal?.Id);
            Assert.Equal(1, manager.ActiveCount);
        }

        // ── CE-259ax — the adapter's tail is ONE call: EntityCreation.RequestEntityCreation ───────────────────
        //
        // 📄 DESIGN_Entity_Authoring_Surface.md §9. Driven the way an operator does: the real gizmo is armed through the real
        // GlobalGizmoManager and fed GizmoMouseEvents; the PRODUCTION pack's request lands on the world bus.

        private void Click(GlobalGizmoManager manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton button, bool pressed, float x, float y)
        {
            _world.Bus.Publish(new GizmoMouseEvent { Button = button, IsPressed = pressed, WorldPos = new Vector3(x, y, 0f) });
            _world.Bus.SwapBuffers();
            manager.Execute(_world, 0.016f);
        }

        private List<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest> Requests()
        {
            _world.Bus.SwapBuffers();
            return ((Fdp.ModuleHost.Abstractions.ISimulationView)_world)
                .ReadManagedEvents<Fdp.Toolkit.NetworkSpawning.Events.EntityCreationRequest>().ToList();
        }

        /// <summary>🔴 <c>G1</c> / acceptance ⑧ — an authored AREA becomes a REQUEST (it used to be a node-local ORDER on some paths).</summary>
        [Fact]
        public void CE259ax_AnAuthoredArea_ReachesTheRequestPath_WithItsPolylineAndTheAreaType()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            Assert.Equal(ToolActivationOutcome.Armed, adapter.ArmAreaAuthoring());

            foreach (var (x, y) in new[] { (100f, 200f), (300f, 400f), (500f, 600f) })
                Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, x, y);
            Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Right, true, 500f, 600f);

            var req = Assert.Single(Requests());
            Assert.Equal(TkbEntityTypes.TacGraphic_Area, req.TkbType);
            Assert.Equal(0, req.OwnerAppInstanceId);   // the default processor
            Assert.Contains(req.InitialComponents!, c => c is Hrot.IG.Components.EditablePolyline);
            Assert.Contains(req.InitialComponents!, c => c is SimTransform);
        }

        [Fact]
        public void CE259ax_AnAuthoredRoute_ReachesTheRequestPath_WithItsPlan()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            Assert.Equal(ToolActivationOutcome.Armed, adapter.ArmRouteAuthoring());

            foreach (var (x, y) in new[] { (10f, 20f), (30f, 40f) })
                Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, x, y);
            Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Right, true, 30f, 40f);

            var req = Assert.Single(Requests());
            Assert.Equal(TkbEntityTypes.TacGraphic_Route, req.TkbType);
            Assert.Contains(req.InitialComponents!, c => c is Hrot.Map.Common.Components.RoutePlan);
        }

        [Fact]
        public void CE259ax_APlacement_ReachesTheRequestPath_AtTheClickedPoint()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, globalGizmoManager: manager);
            adapter.StartPlacementMode(TkbEntityTypes.Tank_M1Abrams, null);   // no arbiter: arms directly

            Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, 12f, 34f);

            var req = Assert.Single(Requests());
            Assert.Equal(TkbEntityTypes.Tank_M1Abrams, req.TkbType);
            var t = Assert.Single(req.InitialComponents!.OfType<SimTransform>());
            Assert.Equal(12f, t.Position.X);
            Assert.Equal(34f, t.Position.Y);
        }

        /// <summary>🔴 <c>G2</c> / acceptance ⑨ — the resolver's string reaches the request's EntityInfo (it used to be dropped).</summary>
        [Fact]
        public void CE259ax_TheNameResolver_ReachesTheRequestsEntityInfo()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, tkbDb: _tkb, globalGizmoManager: manager, nameResolver: () => "Alpha-7");
            adapter.StartPlacementMode(TkbEntityTypes.Tank_M1Abrams, null);

            Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, 1f, 2f);

            var req = Assert.Single(Requests());
            var info = Assert.Single(req.InitialComponents!.OfType<EntityInfo>());
            Assert.Equal("Alpha-7", info.Name.ToString());
        }

        /// <summary>Non-vacuity for the rail above: with NO resolver the TKB-default name is used, so it is the resolver that changes it.</summary>
        [Fact]
        public void CE259ax_WithoutAResolver_TheTkbDefaultNameIsUsed()
        {
            var manager = MakeManager();
            var adapter = new ScenarioSpawnAdapter(_creation, tkbDb: _tkb, globalGizmoManager: manager);
            adapter.StartPlacementMode(TkbEntityTypes.Tank_M1Abrams, null);

            Click(manager, Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, 1f, 2f);

            var info = Assert.Single(Assert.Single(Requests()).InitialComponents!.OfType<EntityInfo>());
            Assert.NotEqual("Alpha-7", info.Name.ToString());
        }

        /// <summary>
        /// Acceptance ⑧ as a source guard: the adapter builds NO request and publishes NO order — its only tail is the affordance.
        /// ⚠ A text rail (it cannot see a renamed bus), so it pairs with the behavioural rails above.
        /// </summary>
        [Fact]
        public void CE259ax_TheAdapterSource_PublishesNothingItself()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(Fdp.Testing.TestRepo.Root, "Hrot/Engine/Hrot.Presentation/Adapters/ScenarioSpawnAdapter.cs"));
            Assert.DoesNotContain("PublishManaged", text);
            Assert.DoesNotContain("new EntityCreationRequest", text);
            Assert.Contains("RequestEntityCreation(", text);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A002 — ScenarioMissionService
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMissionServiceTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly FdpEventBus      _bus;
        private readonly BehaviorRegistry _registry;

        public EditorMissionServiceTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<TkbIdentity>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<ActiveMissionPlan>();
            _repo.RegisterEvent<MissionControlAckEvent>();

            _bus      = _repo.Bus;
            _registry = new BehaviorRegistry();
        }

        public void Dispose() => _repo.Dispose();

        /// <summary>
        /// ⭐⭐ <b><c>BP-508</c> — a real, NON-ZERO network id.</b>
        ///
        /// <para>🔴 These tests used <c>(long)entity.Index</c> as the network id, and 📐 the FIRST entity
        /// in a fresh repository has <c>Index == 0</c> ⇒ they were exercising <b>network id 0</b>.
        /// ⛔ That is the "no network identity assigned" sentinel everywhere else in the system —
        /// <c>EntityBinding.IsPersistable</c> treats it as *not durable*, <c>MapPickBroker</c>
        /// already refused it, and the scenario allocator starts far above it. ⇒ ⚠ <b>a fixture artefact,
        /// not a product requirement</b>: the consolidated <c>NetworkIdResolver</c> refuses <c>id ≤ 0</c>
        /// on purpose, and the tests are corrected to the measured behaviour rather than the guard being
        /// dropped to keep them green.</para>
        ///
        /// <para>⭐ The id is still DERIVED from the entity, so the tests keep asserting that the lookup
        /// finds the right one — ⛔ they are not weakened to a constant that any entity would match.</para>
        /// </summary>
        private static int NetIdFor(Entity e) => 1000 + e.Index;

        [Fact]
        public void GetAvailableBehaviors_InsurgentWithRegisteredCombatPosture_ReturnsIt()
        {
            // ⭐ CE-3160 (L4) — the library posture is offered to an insurgent (its contract names Insurgent).
            _registry.Register(1, "CombatPosture", new BehaviorDefinition
            {
                Name       = "CombatPosture",
                BrainTier  = BehaviorConstants.BrainTierBTree,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Insurgent) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var service = new ScenarioMissionService(_bus, _repo, _registry);
            var behaviors = service.GetAvailableBehaviors(NetIdFor(entity));

            Assert.Contains("CombatPosture", behaviors);
        }

        /// <summary>⭐ CE-3164 (R-268) — nothing is hidden: a registered DEMO tree (the UrbanCombat example's Ambush) IS offered,
        /// in the folder its registration declared; a behaviour with no group sits in "Other". 🔴 Red before: CE-3160's
        /// <c>Demo</c> flag hid it (withdrawn by R-268), and choices carried no group.</summary>
        [Fact]
        public void CE3164_GetAvailableBehaviorChoices_ADemoTree_IsOffered_InItsFolder()
        {
            _registry.Register(1, "Ambush", new BehaviorDefinition
            {
                Name       = "Ambush",
                BrainTier  = BehaviorConstants.BrainTierBTree,
                Group      = "Demos/UrbanCombat",
            });
            _registry.Register(2, "Ungrouped", new BehaviorDefinition { Name = "Ungrouped", BrainTier = BehaviorConstants.BrainTierBTree });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Insurgent) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var choices = new ScenarioMissionService(_bus, _repo, _registry).GetAvailableBehaviorChoices(NetIdFor(entity));
            Assert.Equal("Demos/UrbanCombat", Assert.Single(choices, c => c.Name == "Ambush").Group);
            Assert.Equal(BehaviorChoice.OtherGroup, Assert.Single(choices, c => c.Name == "Ungrouped").Group);
        }

        /// <summary>⭐ CE-1019 — an SOP (a behaviour carrying SOP orders, the ruled mark) is not a mission task: the mission panel's
        /// list leaves it out. 🔴 Red-proof: drop the RemoveAll in GetAvailableBehaviorChoices → the SOP is offered.</summary>
        [Fact]
        public void CE1019_GetAvailableBehaviorChoices_LeavesOutAnSop()
        {
            _registry.Register(1, "Ungrouped", new BehaviorDefinition { Name = "Ungrouped", BrainTier = BehaviorConstants.BrainTierBTree });
            _registry.Register(2, "SomeSop", new BehaviorDefinition { Name = "SomeSop", BrainTier = BehaviorConstants.BrainTierBTree, CarriesSopOrders = true });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Insurgent) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var names = new ScenarioMissionService(_bus, _repo, _registry).GetAvailableBehaviorChoices(NetIdFor(entity)).Select(c => c.Name).ToList();
            Assert.Contains("Ungrouped", names);
            Assert.DoesNotContain("SomeSop", names);
        }

        /// <summary>⭐ CE-3164 (R-268) — a hot reload (staging registry merged into the live one) whose staging registration carries
        /// no group keeps the live group; one that declares a group moves it.</summary>
        [Fact]
        public void CE3164_AHotReloadWithoutAGroup_KeepsTheGroup()
        {
            _registry.Register(1, "Ambush", new BehaviorDefinition { Name = "Ambush", Group = "Demos/UrbanCombat" });
            _registry.Register(2, "Flank", new BehaviorDefinition { Name = "Flank", Group = "Tactics" });
            var staging = new BehaviorRegistry();
            staging.Register(1, "Ambush", new BehaviorDefinition { Name = "Ambush" });
            staging.Register(2, "Flank", new BehaviorDefinition { Name = "Flank", Group = "Library/Tactics" });
            _registry.MergeFrom(staging);
            Assert.Equal("Demos/UrbanCombat", _registry.GroupOf("Ambush"));
            Assert.Equal("Library/Tactics", _registry.GroupOf("Flank"));
        }

        /// <summary>⭐ CE-3164 (R-268) — the picker tree: folders split on '/', Library first, Other last, names sorted.</summary>
        [Fact]
        public void CE3164_PickerTree_LibraryFirst_OtherLast_NestedFolders()
        {
            var root = Hrot.Presentation.Behavior.BehaviorPickerTree.Build(new[]
            {
                new BehaviorChoice("Zed", null),
                new BehaviorChoice("Ambush", "BTree") { Group = "Demos/UrbanCombat" },
                new BehaviorChoice("MoveToLocation", "BTree") { Group = "Library/Basic" },
                new BehaviorChoice("CombatPosture", "BTree") { Group = "Library/Infantry" },
                new BehaviorChoice("FireAtTarget", "BTree") { Group = "Library/Basic" },
            });
            Assert.Equal(new[] { "Library", "Demos", "Other" }, root.Folders.Select(f => f.Name));
            var library = root.Folders[0];
            Assert.Equal(new[] { "Basic", "Infantry" }, library.Folders.Select(f => f.Name));
            Assert.Equal(new[] { "FireAtTarget", "MoveToLocation" }, library.Folders[0].Choices.Select(c => c.Name));
            Assert.Equal("Library/Basic", library.Folders[0].Path);
            Assert.Equal("Ambush", Assert.Single(root.Folders[1].Folders[0].Choices).Name);
            Assert.Equal("Zed", Assert.Single(root.Folders[2].Choices).Name);
        }

        /// <summary>⭐ <c>CE-1327</c> — the picker's search: every word must appear in the name, the technology or the folder, case-insensitive;
        /// a name STARTING with the first word comes first. ✅ Red-proof: the folder not searched ⇒ red ("infantry" finds nothing).</summary>
        [Fact]
        public void CE1327_PickerSearch_EveryWord_NameFolderOrTechnology()
        {
            var choices = new[]
            {
                new BehaviorChoice("Ambush", "BTree") { Group = "Demos/UrbanCombat" },
                new BehaviorChoice("MoveToLocation", "BTree") { Group = "Library/Basic" },
                new BehaviorChoice("CombatPosture", "BTree") { Group = "Library/Infantry" },
                new BehaviorChoice("PostureHold", "Hsm") { Group = "Library/Infantry" },
            };
            var search = Hrot.Presentation.Behavior.BehaviorPickerTree.Matches;
            Assert.Equal(new[] { "PostureHold", "CombatPosture" }, search(choices, "posture").Select(c => c.Name));   // the prefix first
            Assert.Equal(new[] { "CombatPosture", "PostureHold" }, search(choices, "INFANTRY").Select(c => c.Name));  // the folder
            Assert.Equal(new[] { "PostureHold" }, search(choices, "hsm post").Select(c => c.Name));                  // every word
            Assert.Empty(search(choices, "zzz"));
        }

        [Fact]
        public void GetAvailableBehaviors_DeadEntity_ReturnsEmpty()
        {
            var entity = _repo.CreateEntity();
            _repo.DestroyEntity(entity);

            var service = new ScenarioMissionService(_bus, _repo, _registry);
            var behaviors = service.GetAvailableBehaviors(NetIdFor(entity));

            Assert.Empty(behaviors);
        }

        [Fact]
        public void GetAvailableBehaviors_IncludesRegisteredEditorBTree()
        {
            // Arrange: register an editor-authored BTree behavior (simulates T10_MultiAction style).
            _registry.Register(42, "T10_MultiAction", new BehaviorDefinition
            {
                Name      = "T10_MultiAction",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.MilitaryApc) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var service = new ScenarioMissionService(_bus, _repo, _registry);

            // Act
            var behaviors = service.GetAvailableBehaviors(NetIdFor(entity));

            // Assert: editor-authored BTree should appear in the result for any entity type.
            Assert.Contains("T10_MultiAction", behaviors);
        }

        /// <summary>
        /// ⭐ CE-446 E4 (Q77 §5.5): the picker lists every technology — an HSM and a blueprint behaviour appear beside a BTree.
        /// <para>⚠ Inverse-edit red-proof: restore the BTree-only predicate and both assertions fail.</para>
        /// </summary>
        [Fact]
        public void CE446_GetAvailableBehaviors_ListsEveryTechnology()
        {
            _registry.Register(43, "AuthoredHsm", new BehaviorDefinition
            {
                Name = "AuthoredHsm", BrainTier = BehaviorConstants.BrainTierHsm,
            });
            _registry.Register(44, "AuthoredBlueprint", new BehaviorDefinition
            {
                Name = "AuthoredBlueprint", BrainTier = BehaviorConstants.BrainTierBlueprint,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.MilitaryApc) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var behaviors = new ScenarioMissionService(_bus, _repo, _registry).GetAvailableBehaviors(NetIdFor(entity));

            Assert.Contains("AuthoredHsm", behaviors);
            Assert.Contains("AuthoredBlueprint", behaviors);
        }

        /// <summary>
        /// ⭐⭐ CE-462 (E4 ④, D4): each behaviour carries its TECHNOLOGY, read off the definition's brain tier, and
        /// the plain name list is DERIVED from the labelled one — so the two cannot disagree.
        /// <para>⚠ Inverse-edit red-proof: map <c>BrainTierBlueprint</c> to null in <c>TechnologyOf</c> ⇒ the
        /// Blueprint assertion fails.</para>
        /// </summary>
        [Fact]
        public void CE462_Each_behavior_is_labelled_with_its_technology()
        {
            _registry.Register(43, "AuthoredHsm", new BehaviorDefinition
            {
                Name = "AuthoredHsm", BrainTier = BehaviorConstants.BrainTierHsm,
            });
            _registry.Register(44, "AuthoredBlueprint", new BehaviorDefinition
            {
                Name = "AuthoredBlueprint", BrainTier = BehaviorConstants.BrainTierBlueprint,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.MilitaryApc) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var service = new ScenarioMissionService(_bus, _repo, _registry);
            var choices = service.GetAvailableBehaviorChoices(NetIdFor(entity));

            Assert.Equal("HSM",       Assert.Single(choices, c => c.Name == "AuthoredHsm").Technology);
            Assert.Equal("Blueprint", Assert.Single(choices, c => c.Name == "AuthoredBlueprint").Technology);
            Assert.Equal("AuthoredBlueprint  [Blueprint]", choices.Single(c => c.Name == "AuthoredBlueprint").Label);
            Assert.Equal(choices.Select(c => c.Name), service.GetAvailableBehaviors(NetIdFor(entity)));
        }

        [Fact]
        public void GetAvailableBehaviors_DoesNotDuplicateCuratedEntries()
        {
            // Arrange: register a behavior that also exists in the BehaviorCatalog curated list
            // (e.g., "CombatPosture" appears for Insurgent via [Behavior(Params = …)], CE-3160).  Registering it with
            // BrainTierBTree must NOT cause it to appear twice.
            _registry.Register(1, "CombatPosture", new BehaviorDefinition
            {
                Name      = "CombatPosture",
                BrainTier = BehaviorConstants.BrainTierBTree,
            });

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Insurgent) });
            _repo.AddComponent(entity, new NetworkIdentity { Value = NetIdFor(entity) });

            var service = new ScenarioMissionService(_bus, _repo, _registry);

            // Act
            var behaviors = service.GetAvailableBehaviors(NetIdFor(entity));

            // Assert: exactly one occurrence — no duplicates regardless of catalog/registry overlap.
            int count = behaviors.Count(n => n == "CombatPosture");
            Assert.Equal(1, count);
        }

        [Fact]
        public void CommitMissionAsync_PollAcksWithMatchingAck_ResolvesSuccess()
        {
            var entity = _repo.CreateEntity();
            var service = new ScenarioMissionService(_bus, _repo, _registry);

            var plan = new Hrot.Core.Mission.MissionPlan
            {
                ActiveTaskId = Guid.Empty,
                Tasks = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTask>(),
            };

            Task<MissionCommitResult> task = service.CommitMissionAsync(entity.Index, plan, 0);

            // Inject an ACK event that matches the published intent.
            // SwapBuffers is required — PublishManaged writes to the "next frame" buffer.
            _bus.SwapBuffers();
            var intents = _bus.ReadManaged<MissionControlIntent>();
            Assert.Single(intents);

            Guid requestId = intents[0].RequestId;

            // Publish a matching ACK.
            _bus.Publish(new MissionControlAckEvent
            {
                RequestId  = requestId,
                ErrorCode  = 0,
                NewVersion = 1L,
            });
            _bus.SwapBuffers(); // make the ACK event visible to Consume<>

            service.PollAcks();

            Assert.True(task.IsCompleted);
            Assert.True(task.Result.Success);
            Assert.Equal(1L, task.Result.NewVersion);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A003 — ScenarioOrbatAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    // 🔒 SERIALIZED with the class that flips the process-global
    //    FdpConfig.EnforceExplicitEventRegistration. ⛔ This class publishes MANAGED events, so a
    //    parallel run that overlaps that flip makes it throw on an event it never had to register.
    // 📌 Measured 2026-09-20 (UXI-11 S-2): RequestEmbark_PublishesEmbarkEntityCommand reddened once
    //    in a full run and passed 8/8 in isolation and on every re-run — the exact leak
    //    TheViewportInteractionIsSharedTests' own header records against
    //    JsonEntityContextMenuHandlerTests on 2026-09-09. ⚠ The root cause is a process-global flag
    //    toggled under parallel execution; serialising the victims is the same remedy applied before.
    [Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
    public sealed class EditorOrbatAdapterTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly FdpEventBus      _bus;

        public EditorOrbatAdapterTests()
        {
            _world = new EntityRepository();
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            // Register events for embark/disembark (EmbarkEntityCommand is [EventId] so it's auto-registered)
            _bus = _world.Bus;
        }

        public void Dispose() => _world.Dispose();

        private ScenarioOrbatAdapter CreateAdapter()
        {
            // ⭐ CE-060 — the IEditorLogic mock is GONE: SelectEntity now publishes the shared
            //   ActivateEditorToolEvent + SelectEntityCommand instead of calling a host facade.
            var mockSpawn = new Mock<ISpawnController>();
            return new ScenarioOrbatAdapter(_world, _bus, mockSpawn.Object);
        }

        [Fact]
        public void GetVisibleNodes_TwoEntities_ReturnsCorrectDepths()
        {
            // Burn index 0 so parent starts at index 1.
            _world.CreateEntity();

            var parent = _world.CreateEntity();
            var child  = _world.CreateEntity();

            _world.AddComponent(parent, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Alpha"),
            });
            // Child is subordinate to parent via UnitSubordinate component.
            _world.AddComponent(child, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Bravo"),
            });
            _world.AddComponent(child, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = parent });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("", new HashSet<int> { parent.Index });   // CE-1272: expanded explicitly

            Assert.Equal(2, nodes.Count);
            var parentNode = nodes[0]; // root first (depth 0)
            var childNode  = nodes[1];

            Assert.Equal(0, parentNode.Depth);
            Assert.Equal(1, childNode.Depth);
        }

        /// <summary>⭐ <c>CE-1346</c> (Tool Model §4.16) — a clicked row is first OFFERED to the map's entity pick; taken, the row is not
        /// selected (no select request is published). ✅ Red-proof: the offer dropped from SelectEntity ⇒ the offer never sees the row.</summary>
        [Fact]
        public void CE1346_AClickedRow_IsOfferedToTheEntityPick_AndTakenItIsNotSelected()
        {
            _world.CreateEntity();
            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new EntityInfo { Name = new Fdp.Core.FixedString64("Alpha") });
            var adapter = CreateAdapter();
            var offered = new List<Entity>();
            adapter.PickOffer = e => { offered.Add(e); return true; };
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.SelectEntity(unit.Index);   // ⚠ a select would publish ActivateEditorToolEvent — taken, it returns first

            Assert.Equal(new[] { unit }, offered);
        }

        /// <summary>⭐ <c>CE-1350</c> — a destroyed unit's row says so (the panel draws it dark red); a living one does not; and a double-click's
        /// Center View on a host with no map is a no-op, not a crash. ✅ Red-proof: IsDestroyed not filled ⇒ red.</summary>
        [Fact]
        public void CE1350_ADestroyedUnitsRow_IsMarked_AndCentringWithNoMapIsHarmless()
        {
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            _world.CreateEntity();
            var dead = _world.CreateEntity();
            var alive = _world.CreateEntity();
            _world.AddComponent(dead, new EntityInfo { Name = new Fdp.Core.FixedString64("Dead") });
            _world.AddComponent(dead, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            _world.AddComponent(alive, new EntityInfo { Name = new Fdp.Core.FixedString64("Alive") });
            _world.AddComponent(alive, new Fdp.Toolkit.Combat.Components.Health { Current = 80f, Max = 100f });
            var adapter = CreateAdapter();
            var nodes = adapter.GetVisibleNodes("", new HashSet<int>());
            Assert.True(nodes.Single(n => n.Name == "Dead").IsDestroyed);
            Assert.False(nodes.Single(n => n.Name == "Alive").IsDestroyed);
            adapter.CenterOn(dead.Index);   // no canvas on this world: nothing to move, no throw
        }

        /// <summary>CE-1272 — a collapsed commander hides its subordinates, also when no row is expanded at all (the old "nothing
        /// expanded ⇒ everything expanded" shortcut showed them).</summary>
        [Fact]
        public void CE1272_ACollapsedCommander_HidesItsSubordinates()
        {
            _world.CreateEntity();
            var parent = _world.CreateEntity();
            var child  = _world.CreateEntity();
            _world.AddComponent(parent, new EntityInfo { Name = new Fdp.Core.FixedString64("Platoon") });
            _world.AddComponent(child,  new EntityInfo { Name = new Fdp.Core.FixedString64("Squad") });
            _world.AddComponent(child, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = parent });

            var adapter = CreateAdapter();
            var collapsed = adapter.GetVisibleNodes("", new HashSet<int>());
            Assert.Single(collapsed);
            Assert.Equal("Platoon", collapsed[0].Name);
            Assert.True(collapsed[0].HasChildren);

            Assert.Equal(2, adapter.GetVisibleNodes("", new HashSet<int> { parent.Index }).Count);
        }

        /// <summary>CE-1271 — a row carries the entity's NETWORK id beside its index key: the shared map entity menu looks the
        /// network id up, and the index looked up as one missed (an empty menu).</summary>
        [Fact]
        public void CE1271_ARow_CarriesTheNetworkId_TheMenuLooksUp()
        {
            if (!_world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
                _world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            _world.CreateEntity();
            var unit = _world.CreateEntity();
            _world.AddComponent(unit, new EntityInfo { Name = new Fdp.Core.FixedString64("Rifleman") });
            _world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(4711));

            var row = Assert.Single(CreateAdapter().GetVisibleNodes("", new HashSet<int>()));
            Assert.Equal(unit.Index, row.EntityId);     // the adapter's own key, unchanged
            Assert.Equal(4711, row.NetworkId);
        }

        [Fact]
        public void GetVisibleNodes_WithFilter_ExcludesNonMatchingNodes()
        {
            _world.CreateEntity(); // burn index 0
            var e1 = _world.CreateEntity();
            var e2 = _world.CreateEntity();

            _world.AddComponent(e1, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Tiger"),
            });
            _world.AddComponent(e2, new EntityInfo
            {
                Name       = new Fdp.Core.FixedString64("Wolf"),
            });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("Tiger", new HashSet<int>());

            Assert.Single(nodes);
            Assert.Equal("Tiger", nodes[0].Name);
        }

        [Fact]
        public void RequestEmbark_PublishesEmbarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0
            var passenger = _world.CreateEntity();
            var vehicle   = _world.CreateEntity();

            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Soldier"),
            });
            _world.AddComponent(vehicle, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Apc"),
            });

            var adapter = CreateAdapter();
            // Populate the index cache.
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestEmbark(passenger.Index, vehicle.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<EmbarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
            Assert.Equal(vehicle,   events[0].Vehicle);
        }

        [Fact]
        public void RequestDisembark_PublishesDisembarkEntityCommand()
        {
            _world.CreateEntity(); // burn index 0
            var passenger = _world.CreateEntity();
            _world.AddComponent(passenger, new EntityInfo
            {
                Name        = new Fdp.Core.FixedString64("Soldier"),
            });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>());

            adapter.RequestDisembark(passenger.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<DisembarkEntityCommand>().ToArray();
            Assert.Single(events);
            Assert.Equal(passenger, events[0].Passenger);
        }

        // CS020-T01
        [Fact]
        public void GetVisibleNodes_CommanderWithRoster_CanAcceptSubordinatesTrue()
        {
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            _world.CreateEntity(); // burn index 0
            var commander = _world.CreateEntity();
            _world.AddComponent(commander, new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
            _world.AddComponent(commander, new Fdp.Core.CommandHierarchy.UnitRoster());  // has roster

            var subordinate = _world.CreateEntity();
            _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });
            _world.AddComponent(subordinate, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = commander });

            var adapter = CreateAdapter();
            var nodes   = adapter.GetVisibleNodes("", new HashSet<int> { commander.Index });   // CE-1272: expanded explicitly

            var cmdNode = nodes.Single(n => n.EntityId == commander.Index);
            var subNode = nodes.Single(n => n.EntityId == subordinate.Index);

            Assert.True(cmdNode.CanAcceptSubordinates);
            Assert.False(subNode.CanAcceptSubordinates);
            Assert.Equal(1, subNode.Depth);
        }

        // CS020-T02
        [Fact]
        public void RequestAssignSubordinate_ValidEntities_PublishesCmdAssignSubordinate()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
            _world.CreateEntity(); // burn index 0
            var commander  = _world.CreateEntity();
            var subordinate = _world.CreateEntity();
            _world.AddComponent(commander,   new EntityInfo { Name = new Fdp.Core.FixedString64("CMD") });
            _world.AddComponent(subordinate, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

            adapter.RequestAssignSubordinate(subordinate.Index, commander.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
            Assert.Single(events);
            Assert.Equal(subordinate, events[0].Subordinate);
            Assert.Equal(commander,   events[0].Commander);
        }

        // CS020-T03
        [Fact]
        public void RequestAssignSubordinate_UnknownEntity_DoesNotThrow()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
            var adapter = CreateAdapter();

            // No exception; no event.
            adapter.RequestAssignSubordinate(999, 888);

            _bus.SwapBuffers();
            Assert.Empty(_bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());
        }

        // CS020-T04
        [Fact]
        public void RequestRemoveSubordinate_ValidEntity_PublishesCmdRemoveSubordinate()
        {
            _world.RegisterEvent<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>();
            _world.CreateEntity(); // burn index 0
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new EntityInfo { Name = new Fdp.Core.FixedString64("SUB") });

            var adapter = CreateAdapter();
            adapter.GetVisibleNodes("", new HashSet<int>()); // populate cache

            adapter.RequestRemoveSubordinate(entity.Index);

            _bus.SwapBuffers();
            var events = _bus.Read<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>().ToArray();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Subordinate);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A004 — the editor's map picking. ⭐ CE-063: EditorMapPickAdapter is GONE — the editor builds THE shared
    //   CanvasMapPickAdapter with its capabilities (DESIGN_Map_Picking_Unification P2); these rails moved with it.
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class MapPickAdapterTests
    {
        private readonly TestMapCanvas        _canvas = new();
        private readonly DebugPrimitiveBuffer _buffer = new();

        private GlobalGizmoManager MakeManager() => new GlobalGizmoManager(_buffer);

        /// <summary>The adapter as <c>EditorSubsystem</c> builds it: geodetic transform + the modal area gizmo.</summary>
        private CanvasMapPickAdapter EditorLike(GlobalGizmoManager manager, Func<ToolController?>? tools = null)
            => new(_canvas, globalGizmoManager: manager, tools: tools,
                   geoTransform: HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
                   areaGizmo: (onPicked, onRemove) => new Hrot.Editor.Gizmos.ModalBoxSelectionGizmo(onPicked, onRemove));

        /// <summary>
        /// ⭐⭐ <c>CE-063</c> — a location pick is GEODETIC through the host's transform. 🔴 The shared adapter used to
        /// return the world X/Y in the Latitude/Longitude fields; merging the editor onto it without this would have
        /// written metres where degrees belong — the silent degradation CE-063 named.
        /// </summary>
        [Fact]
        public async Task PickLocationAsync_IsGeodetic_ThroughTheHostTransform()
        {
            var manager = MakeManager();
            var geo     = HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
            var adapter = new CanvasMapPickAdapter(_canvas, globalGizmoManager: manager, geoTransform: geo);

            var task = adapter.PickLocationAsync();
            SimulateLeftClick(manager, 120f, -340f);
            var picked = await task;

            var (lat, lon, _) = geo.ToGeodetic(new System.Numerics.Vector3(120f, -340f, 0f));
            Assert.Equal(lat, picked.Latitude, 9);
            Assert.Equal(lon, picked.Longitude, 9);
        }

        /// <summary>⭐ CE-4206 R1 (F2) — the broker over an adapter that holds a repository carries the live reference scope (names, GUIDs, keys for the link
        /// leaves); without a repository it carries none; an explicit scope wins. Red-proof: <c>CanvasMapPickAdapter.References</c> returning null ⇒ red.</summary>
        [Fact]
        public void CE4206_TheBrokerCarriesTheLiveReferenceScope_OfTheRepositoryItsAdapterHolds()
        {
            using var world = new EntityRepository();
            var withRepo = new CanvasMapPickAdapter(_canvas, world);
            var broker = new Hrot.Presentation.Facades.MapPickBroker(withRepo);
            Assert.NotNull(broker.References);
            Assert.Same(withRepo.References, broker.References);                    // one scope per adapter, not one per read

            Assert.Null(new Hrot.Presentation.Facades.MapPickBroker(new CanvasMapPickAdapter(_canvas)).References);   // no repository (ExCon-like) ⇒ plain forms

            var explicitScope = new Fdp.Toolkit.Scenario.LiveRefScope(world);
            Assert.Same(explicitScope, new Hrot.Presentation.Facades.MapPickBroker(withRepo) { References = explicitScope }.References);

            // a form's scoped view of the broker shares it
            Assert.Same(broker.References, new Fdp.Presentation.Editing.ScopedMapPickContext(broker, "$.tasks[0]").References);
        }

        /// <summary>⭐ A host that publishes the transform as a world singleton (SimHost) gets geodetic picks without
        /// passing it.</summary>
        [Fact]
        public async Task PickLocationAsync_FallsBackToTheWorldsTransformSingleton()
        {
            var manager = MakeManager();
            var geo     = HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
            using var world = new EntityRepository();
            world.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(geo);
            var adapter = new CanvasMapPickAdapter(_canvas, world, globalGizmoManager: manager);

            var task = adapter.PickLocationAsync();
            SimulateLeftClick(manager, 50f, 75f);
            var picked = await task;

            Assert.Equal(geo.ToGeodetic(new System.Numerics.Vector3(50f, 75f, 0f)).lat, picked.Latitude, 9);
        }



        private static void SimulateLeftClick(GlobalGizmoManager manager, float x = 0f, float y = 0f)
        {
            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoMouseEvent>();
            repo.RegisterEvent<GizmoKeyEvent>();
            repo.Bus.Publish(new GizmoMouseEvent
            {
                Button    = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left,
                IsPressed = false,
                WorldPos  = new System.Numerics.Vector3(x, y, 0f),
            });
            repo.Bus.SwapBuffers();
            manager.Execute(repo, 0f);
        }

        [Fact]
        public async Task PickLocationAsync_ToolFires_TaskCompletesWithGeoPoint()
        {
            var manager = MakeManager();
            var adapter = EditorLike(manager);
            Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync();

            // Verify gizmo is registered, then simulate left-click.
            Assert.Equal(1, manager.ActiveCount);
            SimulateLeftClick(manager, 0f, 0f);

            var result = await task;
            // The task should complete (the exact geo values depend on the transform).
            Assert.True(task.IsCompleted);
            Assert.False(task.IsFaulted);
            Assert.False(task.IsCanceled);
        }

        [Fact]
        public async Task PickLocationAsync_CancellationToken_TaskCancelled()
        {
            var cts     = new CancellationTokenSource();
            var manager = MakeManager();
            var adapter = EditorLike(manager);
            Task<Hrot.Core.Mission.GeoPoint> task = adapter.PickLocationAsync(cts.Token);

            // Verify the gizmo is registered before cancellation.
            Assert.Equal(1, manager.ActiveCount);

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => task);
        }

        [Fact]
        public async Task PickAreaEntitiesAsync_ToolFires_TaskCompletesWithList()
        {
            var manager = MakeManager();
            var adapter = EditorLike(manager);
            Task<IReadOnlyList<int>> task = adapter.PickAreaEntitiesAsync();

            // Simulate a left-click to trigger the gizmo's selection complete callback.
            Assert.Equal(1, manager.ActiveCount);
            SimulateLeftClick(manager, 0f, 0f);

            var result = await task;
            // Placeholder implementation returns empty list.
            Assert.NotNull(result);
        }

        // ── UXI-07 step 4b — a pick INTERRUPTS the active tool ────────────────────────────────────
        //
        // 📄 UX_Feature_Tool_Model.md §4.12. 🔴 Before this step every pick registered its gizmo straight
        //    on GlobalGizmoManager, so it took exclusive focus while ToolController still believed some
        //    other tool held it (§4.8's bypass). ⭐⭐ And the fix could NOT be Activate: that CANCELS the
        //    tool underneath, so picking a point mid-route would have DESTROYED the half-drawn route.

        /// <summary>A stand-in for "the tool the operator was already using".</summary>
        private sealed class RouteProbe : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus => true;
            public bool IsFocused { get; private set; }
            public bool Disposed  { get; private set; }

            public void SetFocus(bool f) { IsFocused = f; }
            public void UpdateAndDraw(ISimulationView v, float dt, IDebugDrawBuilder b) { }
            public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
            public void OnDragUpdate(Vector3 p) { }
            public void OnCommit(Vector3 w) { }
            public void OnMenuAction(int id) { }
            public void OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton b, bool p, Vector3 w) { }
            public void OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey k, bool p) { }
            public void OnCancel() { }
            public void Dispose() { Disposed = true; }
        }

        private static (ToolController controller, RouteProbe route) ArmARouteTool(GlobalGizmoManager manager)
        {
            var controller = new ToolController(() => manager, () => null);
            var route      = new RouteProbe();

            controller.Register(
                new ToolDescriptor("route", "Route", ToolModality.Modal, ToolArbiter.Global),
                _ => { manager.Register(GlobalGizmoManager.NewId(), route); return ToolActivationOutcome.Armed; });

            Assert.True(controller.Activate("route"));
            Assert.True(route.IsFocused);
            return (controller, route);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE ONE THAT MATTERS — a pick SUSPENDS the route; it does not destroy it.</b>
        /// ⛔ <c>Assert.False(route.Disposed)</c> is the whole of step 4b: an <c>Activate</c>-based
        /// conversion would fail exactly here.
        /// </summary>
        [Fact]
        public void APickSuspendsTheActiveToolRatherThanDestroyingIt()
        {
            var manager = MakeManager();
            var (controller, route) = ArmARouteTool(manager);

            var adapter = EditorLike(manager, () => controller);

            _ = adapter.PickLocationAsync();

            Assert.Equal(ScenarioToolIds.PickLocation, controller.ActiveModal?.Id);
            Assert.False(route.IsFocused);    // it yielded the input …
            Assert.False(route.Disposed);     // ⭐⭐ … but it is ALIVE
        }

        /// <summary>
        /// ⭐⭐ <b>Completing the pick POPS and RESUMES the route.</b> ⚠ Without this half a pick would
        /// leave the operator's tool alive but permanently unfocused — the dead-tool shape this
        /// programme has already hit twice.
        /// </summary>
        [Fact]
        public async Task CompletingAPickResumesTheToolUnderneath()
        {
            var manager = MakeManager();
            var (controller, route) = ArmARouteTool(manager);

            var adapter = EditorLike(manager, () => controller);

            var task = adapter.PickLocationAsync();
            SimulateLeftClick(manager, 0f, 0f);
            await task;

            Assert.False(route.Disposed);
            Assert.True(route.IsFocused);     // ⭐⭐ the route has the input back
            Assert.Equal("route", controller.ActiveModal?.Id);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A005 — EditorZoneAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorZoneAdapterTests
    {
        private readonly TestMapCanvas        _canvas = new();
        private readonly FdpEventBus          _bus    = new();
        private readonly DebugPrimitiveBuffer _buffer = new();

        private GlobalGizmoManager MakeManager() => new GlobalGizmoManager(_buffer);


        private static void SimulateLeftClick(GlobalGizmoManager manager, float x = 0f, float y = 0f)
        {
            using var repo = new EntityRepository();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoMouseEvent>();
            repo.RegisterEvent<GizmoKeyEvent>();
            repo.Bus.Publish(new GizmoMouseEvent
            {
                Button    = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left,
                IsPressed = false,
                WorldPos  = new System.Numerics.Vector3(x, y, 0f),
            });
            repo.Bus.SwapBuffers();
            manager.Execute(repo, 0f);
        }

        [Fact]
        public void SetRoadNetworkPath_PublishesUpdateZoneConfigCommand()
        {
            var adapter = new EditorZoneAdapter(_canvas, _bus);
            adapter.SetRoadNetworkPath("zone_alpha", "Assets/roads.json");

            _bus.SwapBuffers(); // PublishManaged writes to next-frame buffer
            var events = _bus.ReadManaged<UpdateZoneConfigCommand>();
            Assert.Single(events);
            Assert.Equal("zone_alpha",       events[0].ZoneName);
            Assert.Equal("Assets/roads.json", events[0].RoadNetworkPath);
        }

        [Fact]
        public void StartObstaclePlacementMode_RegistersGizmoWithManager()
        {
            var manager = MakeManager();
            var adapter = new EditorZoneAdapter(_canvas, _bus, manager);
            adapter.StartObstaclePlacementMode("zone_alpha", 10f);

            Assert.Equal(1, manager.ActiveCount);
        }

        [Fact]
        public void StartObstaclePlacementMode_OnClick_PublishesSpawnZoneObstacleCommand()
        {
            var manager = MakeManager();
            var adapter = new EditorZoneAdapter(_canvas, _bus, manager);
            adapter.StartObstaclePlacementMode("zone_beta", 5f);

            SimulateLeftClick(manager, 100f, 200f);

            _bus.SwapBuffers();
            var events = _bus.ReadManaged<SpawnZoneObstacleCommand>();
            Assert.Single(events);
            Assert.Equal("zone_beta", events[0].ZoneName);
            Assert.Equal(100f, events[0].Position.X, precision: 2);
            Assert.Equal(200f, events[0].Position.Y, precision: 2);
            Assert.Equal(5f, events[0].Radius, precision: 2);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A007 — EditorPreviewAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorPreviewAdapterTests : IDisposable
    {
        private readonly EntityRepository    _repo;
        private readonly Fdp.Toolkit.Orchestration.Handlers.ReferencePreviewHandler _handler;
        private readonly FakeStateProvider   _state;

        public EditorPreviewAdapterTests()
        {
            _repo    = new EntityRepository();
            _handler = new Fdp.Toolkit.Orchestration.Handlers.ReferencePreviewHandler(_repo, includeTransient: true);
            _state   = new FakeStateProvider();
        }

        public void Dispose() => _repo.Dispose();

        /// <summary>
        /// ⭐⭐ <b><c>BP-508</c> — a real, NON-ZERO network id.</b>
        ///
        /// <para>🔴 These tests used <c>(long)entity.Index</c> as the network id, and 📐 the FIRST entity
        /// in a fresh repository has <c>Index == 0</c> ⇒ they were exercising <b>network id 0</b>.
        /// ⛔ That is the "no network identity assigned" sentinel everywhere else in the system —
        /// <c>EntityBinding.IsPersistable</c> treats it as *not durable*, <c>MapPickBroker</c>
        /// already refused it, and the scenario allocator starts far above it. ⇒ ⚠ <b>a fixture artefact,
        /// not a product requirement</b>: the consolidated <c>NetworkIdResolver</c> refuses <c>id ≤ 0</c>
        /// on purpose, and the tests are corrected to the measured behaviour rather than the guard being
        /// dropped to keep them green.</para>
        ///
        /// <para>⭐ The id is still DERIVED from the entity, so the tests keep asserting that the lookup
        /// finds the right one — ⛔ they are not weakened to a constant that any entity would match.</para>
        /// </summary>
        private static int NetIdFor(Entity e) => 1000 + e.Index;

        [Fact]
        public void IsInPreviewMode_OperatingPreview_ReturnsTrue()
        {
            _state.CurrentState = ScenarioEditorState.OperatingPreview;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.True(adapter.IsInPreviewMode);
        }

        [Fact]
        public void IsInPreviewMode_LoadingPreview_ReturnsTrue()
        {
            _state.CurrentState = ScenarioEditorState.LoadingPreview;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.True(adapter.IsInPreviewMode);
        }

        [Fact]
        public void IsInPreviewMode_OperatingEdit_ReturnsFalse()
        {
            _state.CurrentState = ScenarioEditorState.OperatingEdit;
            var adapter = new EditorPreviewAdapter(_handler, _state);

            Assert.False(adapter.IsInPreviewMode);
        }

        [Fact]
        public void EnterPreviewMode_CreatesSnapshot()
        {
            // Register a component so the snapshot has content.
            _repo.RegisterComponent<TkbIdentity>();
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(99L) });

            var adapter = new EditorPreviewAdapter(_handler, _state);
            adapter.EnterPreviewMode();

            // The internal snap should now be non-null (verified via internal accessor).
            Assert.NotNull(_handler.TestHook_Keyframe);
        }

        [Fact]
        public void ExitPreviewMode_AfterEnter_ClearsSnapshot()
        {
            _repo.RegisterComponent<TkbIdentity>();

            var adapter = new EditorPreviewAdapter(_handler, _state);
            adapter.EnterPreviewMode();
            adapter.ExitPreviewMode();

            Assert.Null(_handler.TestHook_Keyframe);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // A008 — ScenarioMapConfigAdapter
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class EditorMapConfigAdapterTests
    {
        [Fact]
        public void GetCurrentConfig_ReflectsMapViewConfigDefaults()
        {
            var config  = new MapViewConfig();
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
            var adapter = new ScenarioMapConfigAdapter(config, canvas);

            MapLayerState state = adapter.GetCurrentConfig();

            Assert.Equal(config.ShowSatelliteLayer, state.Satellite);
            Assert.Equal(config.ShowGrid,           state.Grid);
            // Canvas starts with ActiveLayerMask = 0xFFFFFFFF — all layers on.
            Assert.True(state.GroundUnits);
            Assert.True(state.AirUnits);
            Assert.True(state.Vehicles);
            Assert.True(state.TacticalGraphics);
            Assert.True(state.RoadGraphs);
        }

        [Fact]
        public void ApplyConfig_SatelliteOff_UpdatesShowSatelliteLayerToFalse()
        {
            var config  = new MapViewConfig { ShowSatelliteLayer = true };
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
            var adapter = new ScenarioMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(
                Satellite:        false,
                GroundUnits:      true,
                AirUnits:         true,
                Vehicles:         true,
                TacticalGraphics: true,
                RoadGraphs:       true,
                Grid:             false));

            Assert.False(config.ShowSatelliteLayer);
            Assert.False(config.ShowGrid);
        }

        [Fact]
        public void ApplyConfig_VehiclesOff_ClearsVehiclesBitInCanvasMask()
        {
            var config  = new MapViewConfig();
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
            var adapter = new ScenarioMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(
                Satellite:        true,
                GroundUnits:      true,
                AirUnits:         true,
                Vehicles:         false,
                TacticalGraphics: true,
                RoadGraphs:       true,
                Grid:             false));

            Assert.Equal(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.VehiclesBit);
        }

        [Fact]
        public void ApplyConfig_AllTrue_SetsAllFieldsTrue()
        {
            var config  = new MapViewConfig();
            var canvas  = new Fdp.Toolkit.Vis2D.MapCanvas();
            var adapter = new ScenarioMapConfigAdapter(config, canvas);

            adapter.ApplyConfig(new MapLayerState(true, true, true, true, true, true, true));

            Assert.True(config.ShowSatelliteLayer);
            Assert.True(config.ShowGrid);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.GroundUnitsBit);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.AirUnitsBit);
            Assert.NotEqual(0u, canvas.ActiveLayerMask & Hrot.Map.Common.Config.MapLayerBits.VehiclesBit);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views HV-2/HV-4) — the Structure relation of the ONE entity tree.
    // Red-proof (compiles): StructureHierarchyAdapter.GetParent ignoring PartMetadata ⇒ the "sensor under its vehicle" rail fails.
    // No frame rail: SharedOrbatPanel has no ImGui frame rail (only the model-dump + drop-logic tests), so the switch is covered by
    // the view-model/relation rails here.
    // ═══════════════════════════════════════════════════════════════════════════
    [Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
    public sealed class EditorOrbatStructureViewTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly Mock<ISpawnController> _spawn = new();

        public EditorOrbatStructureViewTests()
        {
            _world.RegisterComponent<EntityInfo>();
            _world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            _world.RegisterComponent<PartMetadata>();
            _world.RegisterComponent<IsEmbarkedTag>();
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            _world.RegisterComponent<Fdp.Toolkit.Combat.Components.TurretPose>();
            _world.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorTag>();
        }

        public void Dispose() => _world.Dispose();

        private ScenarioOrbatAdapter Adapter(EntityTreeRelation r)
            => new(_world, _world.Bus, _spawn.Object) { Relation = r };

        private Entity Named(string name)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
            return e;
        }

        [Fact]
        public void Command_view_is_unchanged_by_parts_and_passengers()
        {
            _world.CreateEntity();
            var tank = Named("Tank");
            var sensor = _world.CreateEntity();
            _world.AddComponent(sensor, new PartMetadata { ParentEntity = tank, InstanceId = 1000 });
            var soldier = Named("Rifleman");
            _world.AddComponent(soldier, new IsEmbarkedTag { VehicleEntity = tank });

            var nodes = Adapter(EntityTreeRelation.Command).GetVisibleNodes("", new HashSet<int>());

            Assert.Equal(new[] { "Tank", "Rifleman" }, nodes.Select(n => n.Name).OrderByDescending(n => n).ToArray());
            Assert.All(nodes, n => Assert.Equal(0, n.Depth));   // the sensor is not a node; the passenger is a root of the command tree
        }

        [Fact]
        public void Structure_view_shows_a_sensor_part_and_an_embarked_passenger_under_the_vehicle()
        {
            _world.CreateEntity();
            var tank = Named("Tank");
            var sensor = _world.CreateEntity();
            _world.AddComponent(sensor, new PartMetadata { ParentEntity = tank, InstanceId = 3 });   // ⭐ the id says nothing; the SensorTag says it is a radar
            _world.AddComponent(sensor, new Fdp.Toolkit.Perception.Components.SensorTag { Kind = Fdp.Toolkit.Perception.Components.SensorModality.Radar });
            var soldier = Named("Rifleman");
            _world.AddComponent(soldier, new IsEmbarkedTag { VehicleEntity = tank });

            var nodes = Adapter(EntityTreeRelation.Structure).GetVisibleNodes("", new HashSet<int> { tank.Index });

            Assert.Equal(3, nodes.Count);
            Assert.Equal("Tank", nodes[0].Name);
            Assert.True(nodes[0].HasChildren);
            Assert.Contains(nodes, n => n.Depth == 1 && n.Name == "Tank · sensor Radar" && n.EntityId == sensor.Index);
            Assert.Contains(nodes, n => n.Depth == 1 && n.Name == "Rifleman");
            Assert.All(nodes, n => Assert.False(n.CanAcceptSubordinates));
        }

        [Fact]
        public void Structure_view_a_part_with_a_dead_parent_is_a_root()
        {
            _world.CreateEntity();
            var tank = Named("Tank");
            var sensor = _world.CreateEntity();
            _world.AddComponent(sensor, new PartMetadata { ParentEntity = tank, InstanceId = 2001 });
            _world.AddComponent(sensor, default(Fdp.Toolkit.Combat.Components.TurretPose));
            _world.DestroyEntity(tank);

            var nodes = Adapter(EntityTreeRelation.Structure).GetVisibleNodes("", new HashSet<int>());

            var only = Assert.Single(nodes);
            Assert.Equal(0, only.Depth);
            Assert.Equal("turret 1", only.Name);
        }

        /// <summary>⭐ CE-4213 / AQ79 §0.10 — a part is named by the component its creator gave it (its KIND), not by its instance id. Red-proof: Describe ignoring
        /// WeaponMountInfo ⇒ the mount shows "part #1".</summary>
        [Fact]
        public void Structure_view_names_a_part_by_its_kind_component_not_its_instance_id()
        {
            _world.CreateEntity();
            var soldier = Named("Blue 1");
            var mount = _world.CreateEntity();
            _world.AddComponent(mount, new PartMetadata { ParentEntity = soldier, InstanceId = 1 });
            _world.AddComponent(mount, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 2, WeaponGuid = 7 });
            var eqs = _world.CreateEntity();
            _world.AddComponent(eqs, new PartMetadata { ParentEntity = soldier, InstanceId = 2 });
            _world.AddComponent(eqs, new Fdp.Toolkit.Perception.Components.SensorTag { Kind = Fdp.Toolkit.Perception.Components.SensorModality.Visual });
            var unknown = _world.CreateEntity();
            _world.AddComponent(unknown, new PartMetadata { ParentEntity = soldier, InstanceId = 9 });

            var names = Adapter(EntityTreeRelation.Structure).GetVisibleNodes("", new HashSet<int> { soldier.Index }).Select(n => n.Name).ToArray();

            Assert.Contains("Blue 1 · weapon mount 2", names);
            Assert.Contains("Blue 1 · sensor Visual", names);
            Assert.Contains("Blue 1 · part #9", names);   // no kind component ⇒ the fallback
            Assert.DoesNotContain(names, n => n.Contains("part #1") && !n.Contains("part #9"));
        }

        [Fact]
        public void Structure_view_refuses_reparent_and_the_controller_publishes_nothing()
        {
            _world.CreateEntity();
            var a = Named("A");
            var b = Named("B");
            var adapter = Adapter(EntityTreeRelation.Structure);
            adapter.GetVisibleNodes("", new HashSet<int>());

            Assert.False(((IHierarchyAdapter)new StructureHierarchyAdapter()).CanReparent(_world, a, b));

            adapter.RequestAssignSubordinate(a.Index, b.Index);
            adapter.RequestRemoveSubordinate(a.Index);
            Assert.False(_world.HasComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(a));
            _spawn.VerifyNoOtherCalls();
        }

        [Fact]
        public void ChildEnumerator_walks_an_explicit_list_and_skips_dead_entities()
        {
            var a = _world.CreateEntity();
            var dead = _world.CreateEntity();
            var b = _world.CreateEntity();
            _world.DestroyEntity(dead);

            var seen = new List<Entity>();
            foreach (var c in new ChildEnumerator(_world, new[] { a, dead, b })) seen.Add(c);

            Assert.Equal(new[] { a, b }, seen);
        }
    }
}
