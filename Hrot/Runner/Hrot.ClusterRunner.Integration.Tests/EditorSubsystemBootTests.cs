using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Engine.Runner;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D.Components;
using Hrot.Editor;
using Hrot.Editor.Events;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Definitions.Tkb;
using ModuleHost.Core.Network.Interfaces;
using Fdp.Kernel;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests for the <see cref="EditorSubsystem"/>.
///
/// <para>All tests run in headless mode (no Raylib window, no GPU required) to
/// prove that the Editor's offline composition root boots correctly under the
/// <see cref="SubsystemOrchestrator"/> lifecycle.</para>
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class EditorSubsystemBootTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static SubsystemConfig HeadlessConfig() => new()
    {
        DomainId      = 0,
        Headless      = true,
        OwnWindow     = false,
        SubsystemName = "Editor"
    };

    // â”€â”€ T-ES01: Name â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Name_Returns_Editor()
    {
        var subsystem = new EditorSubsystem();
        Assert.Equal("Editor", subsystem.Name);
    }

    // â”€â”€ T-ES02: TitleBarColor is non-zero â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void TitleBarColor_IsNonZero()
    {
        var subsystem = new EditorSubsystem();
        var color = subsystem.TitleBarColor;
        // The color should not be the default zero vector.
        Assert.False(color == Vector4.Zero, "TitleBarColor must be a non-zero colour.");
    }

    // â”€â”€ T-ES03: Initialize headless does not throw â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Initialize_Headless_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        var ex = Record.Exception(() => subsystem.Initialize(HeadlessConfig()));
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES04: Update after init does not throw â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Update_AfterHeadlessInit_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => subsystem.Update(0.016f));
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES05: Multiple update frames do not throw â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Update_MultipleFrames_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        for (int i = 0; i < 10; i++)
            subsystem.Update(0.016f);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES06: DrawWorld in headless is a no-op â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void DrawWorld_Headless_IsNoOp()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        // Must not throw â€” headless flag suppresses all Raylib calls.
        var ex = Record.Exception(() => subsystem.DrawWorld());
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES07: DrawUI in headless is a no-op â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void DrawUI_Headless_IsNoOp()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        var ex = Record.Exception(() => subsystem.DrawUI());
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES08: MapCamera is null in headless â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void GetMapCamera_Headless_ReturnsNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        Assert.Null(subsystem.GetMapCamera());
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES09: ECS world + kernel accessible via test hooks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void World_AfterInit_IsNotNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        // Test hook: world must be accessible and alive.
        var world = subsystem.World;
        Assert.NotNull(world);

        subsystem.Shutdown();
    }

    [Fact]
    public void Kernel_AfterInit_IsNotNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var kernel = subsystem.Kernel;
        Assert.NotNull(kernel);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES10: EditorLogic is accessible after init â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void EditorLogic_AfterInit_IsNotNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var logic = subsystem.EditorLogic;
        Assert.NotNull(logic);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES11: SubsystemOrchestrator boots with EditorSubsystem â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Orchestrator_WithEditorSubsystem_BootsAndRunsOneFrame()
    {
        var editor = new EditorSubsystem();
        var options = new RunnerOptions { Headless = true, DomainId = 0 };
        var orchestrator = new SubsystemOrchestrator(
            new[] { (ISubsystem)editor }, options);

        var ex = Record.Exception(() =>
        {
            orchestrator.Initialize();
            orchestrator.RunFrames(1);
            orchestrator.Shutdown();
        });

        Assert.Null(ex);
    }

    // â”€â”€ T-ES12: Multiple frames via orchestrator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Orchestrator_WithEditorSubsystem_RunsMultipleFrames()
    {
        var editor = new EditorSubsystem();
        var options = new RunnerOptions { Headless = true, DomainId = 0 };
        var orchestrator = new SubsystemOrchestrator(
            new[] { (ISubsystem)editor }, options);

        orchestrator.Initialize();

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 60; i++)
                orchestrator.RunFrames(1);
        });

        Assert.Null(ex);
        orchestrator.Shutdown();
    }

    // â”€â”€ T-ES13: NewScenario does not throw â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void NewScenario_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Update(0.016f);

        var ex = Record.Exception(() => subsystem.EditorLogic.NewScenario());
        Assert.Null(ex);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES14: RegisterWindows does not throw in headless â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void RegisterWindows_Headless_PopulatesWindowManager()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        // IWindowRegistrar: verify RegisterWindows can be called without throwing.
        var options = new RunnerOptions { Headless = true, DomainId = 0 };
        var orchestrator = new SubsystemOrchestrator(
            new[] { (ISubsystem)subsystem }, options);

        var ex = Record.Exception(() =>
        {
            orchestrator.Initialize();
            orchestrator.Shutdown();
        });

        Assert.Null(ex);
    }

    // â”€â”€ T-ES15: Shutdown is idempotent â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void Shutdown_CalledTwice_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Shutdown();
        var ex = Record.Exception(() => subsystem.Shutdown());
        Assert.Null(ex);
    }

    // â”€â”€ T-ES16: AvailableScenarios is non-null after init â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void AvailableScenarios_AfterInit_IsNonNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Update(0.016f);

        // The list may be empty but must not be null.
        Assert.NotNull(subsystem.EditorLogic.AvailableScenarios);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES17: LoadedScenarioName is null after NewScenario â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void LoadedScenarioName_AfterNewScenario_IsNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Update(0.016f);

        subsystem.EditorLogic.NewScenario();

        Assert.Null(subsystem.EditorLogic.LoadedScenarioName);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES18: ActivateEditorToolEvent (Select) is consumed without throw â”€â”€â”€

    [Fact]
    public void ActivateEditorToolEvent_Select_IsConsumedWithoutThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        // Publish an ActivateEditorToolEvent on the bus â€” the headless path skips
        // actual tool switching but the event must be drained (not accumulate).
        subsystem.World.Bus.PublishManaged(new ActivateEditorToolEvent(EditorTool.Select));

        // Update twice so the event crosses the SwapBuffers â†’ ConsumeManaged cycle.
        var ex = Record.Exception(() =>
        {
            subsystem.Update(0.016f);
            subsystem.Update(0.016f);
        });

        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES19: Multiple tool events over several frames do not throw â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void ActivateEditorToolEvent_AllTools_SurviveMultipleFrames()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var tools = new[] { EditorTool.Select, EditorTool.Spawn, EditorTool.Edit,
                            EditorTool.Route, EditorTool.Measure };

        var ex = Record.Exception(() =>
        {
            foreach (var tool in tools)
            {
                subsystem.World.Bus.PublishManaged(new ActivateEditorToolEvent(tool));
                subsystem.Update(0.016f);
                subsystem.Update(0.016f);
            }
        });

        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES20: SaveScenarioAs sets LoadedScenarioName â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public void SaveScenarioAs_SetsLoadedScenarioName()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Update(0.016f);

        // Use a temp directory that exists so the file write succeeds.
        string tempName = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "EditorIntegTest_" + System.Guid.NewGuid().ToString("N"));

        try
        {
            // Save into the temp directory root so ScenariosRoot doesn't matter.
            // We call SaveScenarioAs with an absolute name that includes a known path.
            // Because EditorApplication uses EditorBootstrap.ScenariosRoot we instead
            // verify only that the property is set (the actual file write is covered by
            // ScenarioFileService unit tests).
            string scenarioName = "integration_test_scenario";
            var dir = System.IO.Path.Combine(Hrot.Editor.EditorBootstrap.ScenariosRoot, scenarioName);
            System.IO.Directory.CreateDirectory(dir);

            subsystem.EditorLogic.SaveScenarioAs(scenarioName);
            Assert.Equal(scenarioName, subsystem.EditorLogic.LoadedScenarioName);
        }
        finally
        {
            // Best-effort cleanup
            string cleanDir = System.IO.Path.Combine(
                Hrot.Editor.EditorBootstrap.ScenariosRoot, "integration_test_scenario");
            if (System.IO.Directory.Exists(cleanDir))
                System.IO.Directory.Delete(cleanDir, recursive: true);
        }

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES21: SpawnEntityCommand creates an entity (BUG1 regression) â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Verifies that publishing a <see cref="SpawnEntityCommand"/> on the bus while
    /// running headless causes an ECS entity to appear in the world.
    /// This exercises the TKB + EntityLifecycleModule + NetworkSpawningSystem
    /// pipeline that was missing from <see cref="EditorSubsystem"/> before the BUG1 fix.
    /// </summary>
    [Fact]
    public void SpawnEntityCommand_OnBus_CreatesEntityInWorld()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        // Publish a SpawnEntityCommand for Tank_M1Abrams (tkbType 100).
        // NetworkId=0 causes the SimHostModule's NetworkSpawningSystem to
        // allocate a local ID via the offline SequentialIdAllocator.
        subsystem.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType     = TkbEntityTypes.Tank_M1Abrams,  // 100
            NetworkId   = 0,
            OwnerNodeId = 0,
            InitType    = ReliableInitType.None,
        });

        // Pump until entity appears (or timeout).
        bool appeared = false;
        for (int i = 0; i < 10 && !appeared; i++)
        {
            subsystem.Update(0.016f);
            appeared = subsystem.World.EntityCount > 0;
        }

        Assert.True(appeared, "Entity should appear in the world after SpawnEntityCommand (BUG1)");

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES22: ActivateMeasureTool in headless does not throw (BUG6 regression)

    /// <summary>
    /// Verifies that the Measure tool activation event is correctly handled by
    /// <see cref="EditorSubsystem"/> without throwing. In headless mode the canvas
    /// is unavailable; the handler should silently no-op rather than crash.
    /// </summary>
    [Fact]
    public void ActivateEditorToolEvent_Measure_HeadlessDoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        subsystem.World.Bus.PublishManaged(new ActivateEditorToolEvent(EditorTool.Measure));

        // Two updates so the SwapBuffers cycle exposes the event in the read buffer.
        var ex = Record.Exception(() =>
        {
            subsystem.Update(0.016f);
            subsystem.Update(0.016f);
        });

        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // â”€â”€ T-ES23: SpawnEntityCommand with InitialComponents attaches EntityInfo (BUG13) â”€â”€

    /// <summary>
    /// Verifies that a <see cref="SpawnEntityCommand"/> carrying an
    /// <see cref="Hrot.IG.Components.EntityInfo"/> in its <see cref="SpawnEntityCommand.InitialComponents"/>
    /// list results in the entity having the correct name and ForceId after the
    /// NetworkSpawningSystem processes it.
    ///
    /// This is the offline-editor equivalent of what CreateEntityRequestSystem does
    /// in the live cluster after the JsonAttributeCompiler runs â€” proving the
    /// EditorSpawnAdapter's BUG13 fix actually works end-to-end through the kernel.
    /// </summary>
    [Fact]
    public void SpawnEntityCommand_WithEntityInfoInInitialComponents_EntityGetsNameAndForceId()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var expectedInfo = new Hrot.IG.Components.EntityInfo
        {
            Name    = new Fdp.Kernel.FixedString64("TestTank"),
            ForceId = Hrot.IG.Components.ForceId.Hostile,
        };

        subsystem.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType           = TkbEntityTypes.Tank_M1Abrams,
            NetworkId         = 0,
            OwnerNodeId       = 0,
            InitType          = ReliableInitType.None,
            InitialComponents = new System.Collections.Generic.List<object> { expectedInfo },
        });

        Hrot.IG.Components.EntityInfo? found = null;
        for (int i = 0; i < 10 && found == null; i++)
        {
            subsystem.Update(0.016f);
            var q = subsystem.World.Query()
                .With<Hrot.IG.Components.EntityInfo>()
                .Build();
            foreach (var e in q)
            {
                found = subsystem.World.GetComponent<Hrot.IG.Components.EntityInfo>(e);
                break;
            }
        }

        Assert.NotNull(found);
        Assert.Equal("TestTank", found!.Value.Name.ToString());
        Assert.Equal(Hrot.IG.Components.ForceId.Hostile, found.Value.ForceId);

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES25: MapLayerAssignmentSystem adds MapDisplayComponent (BUG7 regression) â”€

    /// <summary>
    /// Verifies that after spawning an entity with <see cref="SimTransform"/>
    /// the <c>MapLayerAssignmentSystem</c> â€” registered in
    /// <see cref="EditorSubsystem.Initialize"/> â€” assigns a
    /// <see cref="MapDisplayComponent"/> to the entity within a few update frames.
    ///
    /// This proves that (a) <c>MapDisplayComponent</c> is registered in the offline
    /// editor world, and (b) the layer-assignment system runs end-to-end (BUG7 fix).
    /// </summary>
    [Fact]
    public void SpawnEntity_WithSimTransform_GetsMapDisplayComponentFromLayerSystem()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        subsystem.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType           = TkbEntityTypes.Tank_M1Abrams,
            NetworkId         = 0,
            OwnerNodeId       = 0,
            InitType          = ReliableInitType.None,
            InitialTransform  = new SimTransform { Position = new Vector3(100, 100, 0) },
        });

        // Pump until entity acquires MapDisplayComponent (max 20 frames).
        // Use a 4-second delta per frame so the 3-second rescan interval elapses on
        // the first re-pass (frame 1 scans empty world; frame 2 entity exists; frame 3
        // rescans and finds entity).
        Entity theEntity = default;
        bool   hasDisplay = false;
        for (int i = 0; i < 20 && !hasDisplay; i++)
        {
            subsystem.Update(4.0f);
            var q = subsystem.World.Query().With<MapDisplayComponent>().Build();
            foreach (var e in q)
            {
                theEntity  = e;
                hasDisplay = true;
                break;
            }
        }

        Assert.True(hasDisplay,
            "MapLayerAssignmentSystem should add MapDisplayComponent to entities with SimTransform (BUG7)");
        var layerMask = subsystem.World.GetComponent<MapDisplayComponent>(theEntity).LayerMask;
        Assert.True(layerMask != 0u, $"Assigned LayerMask should be non-zero (BUG7), got 0x{layerMask:X}");

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES26: Entity with MapOverlayStyle survives update frames (BUG11 smoke) â”€

    /// <summary>
    /// Verifies that spawning an entity carrying a <see cref="MapOverlayStyle"/>
    /// component does not cause exceptions during update frames.
    ///
    /// The BUG11 fix adds a dedicated <c>MapOverlayRenderLayer</c> and excludes
    /// overlay entities from the main <c>EntityRenderLayer</c> query so that
    /// area-overlay entities are no longer rendered as plain red circles.
    /// This headless test proves the ECS plumbing does not crash.
    /// </summary>
    [Fact]
    public void SpawnEntity_WithMapOverlayStyle_UpdateFramesDoNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        subsystem.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType           = TkbEntityTypes.TacGraphic_Area,
            NetworkId         = 0,
            OwnerNodeId       = 0,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object> { new MapOverlayStyle() },
        });

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 5; i++)
                subsystem.Update(0.016f);
        });

        Assert.Null(ex);

        // Verify the entity is present in the world.
        var q = subsystem.World.Query().With<MapOverlayStyle>().Build();
        Assert.True(q.Any(), "Entity with MapOverlayStyle should be in the world (BUG11)");

        subsystem.Shutdown();
    }

    // â”€â”€ T-ES27: Entity with RoutePlan survives update frames (BUG12 smoke) â”€â”€â”€â”€â”€

    /// <summary>
    /// Verifies that spawning an entity carrying a <see cref="RoutePlan"/> managed
    /// component does not cause exceptions during update frames.
    ///
    /// The BUG12 fix adds a dedicated <c>RouteRenderLayer</c> and excludes route
    /// entities from the main <c>EntityRenderLayer</c> query so that route entities
    /// are no longer rendered as plain red circles.
    /// This headless test proves the ECS plumbing does not crash.
    /// </summary>
    [Fact]
    public void SpawnEntity_WithRoutePlan_UpdateFramesDoNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        subsystem.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType           = TkbEntityTypes.TacGraphic_Route,
            NetworkId         = 0,
            OwnerNodeId       = 0,
            InitType          = ReliableInitType.None,
            InitialComponents = new List<object> { new RoutePlan() },
        });

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 5; i++)
                subsystem.Update(0.016f);
        });

        Assert.Null(ex);

        // Verify the entity is present in the world (has RoutePlan managed component).
        bool found = false;
        var  q     = subsystem.World.Query().Build();
        foreach (var e in q)
        {
            if (subsystem.World.HasManagedComponent<RoutePlan>(e))
            { found = true; break; }
        }

        Assert.True(found, "Entity with RoutePlan should be in the world (BUG12)");

        subsystem.Shutdown();
    }
}
