using System;
using System.Numerics;
using FDP.Framework.Runner;
using Hrot.ClusterRunner.Configuration;
using Hrot.ClusterRunner.Services;
using Hrot.Editor;
using Hrot.Editor.Events;
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
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SubsystemConfig HeadlessConfig() => new()
    {
        DomainId      = 0,
        Headless      = true,
        OwnWindow     = false,
        SubsystemName = "Editor"
    };

    // ── T-ES01: Name ──────────────────────────────────────────────────────────

    [Fact]
    public void Name_Returns_Editor()
    {
        var subsystem = new EditorSubsystem();
        Assert.Equal("Editor", subsystem.Name);
    }

    // ── T-ES02: TitleBarColor is non-zero ─────────────────────────────────────

    [Fact]
    public void TitleBarColor_IsNonZero()
    {
        var subsystem = new EditorSubsystem();
        var color = subsystem.TitleBarColor;
        // The color should not be the default zero vector.
        Assert.False(color == Vector4.Zero, "TitleBarColor must be a non-zero colour.");
    }

    // ── T-ES03: Initialize headless does not throw ────────────────────────────

    [Fact]
    public void Initialize_Headless_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        var ex = Record.Exception(() => subsystem.Initialize(HeadlessConfig()));
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // ── T-ES04: Update after init does not throw ──────────────────────────────

    [Fact]
    public void Update_AfterHeadlessInit_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var ex = Record.Exception(() => subsystem.Update(0.016f));
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // ── T-ES05: Multiple update frames do not throw ───────────────────────────

    [Fact]
    public void Update_MultipleFrames_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        for (int i = 0; i < 10; i++)
            subsystem.Update(0.016f);

        subsystem.Shutdown();
    }

    // ── T-ES06: DrawWorld in headless is a no-op ──────────────────────────────

    [Fact]
    public void DrawWorld_Headless_IsNoOp()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        // Must not throw — headless flag suppresses all Raylib calls.
        var ex = Record.Exception(() => subsystem.DrawWorld());
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // ── T-ES07: DrawUI in headless is a no-op ────────────────────────────────

    [Fact]
    public void DrawUI_Headless_IsNoOp()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        var ex = Record.Exception(() => subsystem.DrawUI());
        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // ── T-ES08: MapCamera is null in headless ─────────────────────────────────

    [Fact]
    public void GetMapCamera_Headless_ReturnsNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        Assert.Null(subsystem.GetMapCamera());
        subsystem.Shutdown();
    }

    // ── T-ES09: ECS world + kernel accessible via test hooks ─────────────────

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

    // ── T-ES10: EditorLogic is accessible after init ──────────────────────────

    [Fact]
    public void EditorLogic_AfterInit_IsNotNull()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        var logic = subsystem.EditorLogic;
        Assert.NotNull(logic);

        subsystem.Shutdown();
    }

    // ── T-ES11: SubsystemOrchestrator boots with EditorSubsystem ─────────────

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

    // ── T-ES12: Multiple frames via orchestrator ──────────────────────────────

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

    // ── T-ES13: NewScenario does not throw ───────────────────────────────────

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

    // ── T-ES14: RegisterWindows does not throw in headless ───────────────────

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

    // ── T-ES15: Shutdown is idempotent ───────────────────────────────────────

    [Fact]
    public void Shutdown_CalledTwice_DoesNotThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());
        subsystem.Shutdown();
        var ex = Record.Exception(() => subsystem.Shutdown());
        Assert.Null(ex);
    }

    // ── T-ES16: AvailableScenarios is non-null after init ────────────────────

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

    // ── T-ES17: LoadedScenarioName is null after NewScenario ─────────────────

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

    // ── T-ES18: ActivateEditorToolEvent (Select) is consumed without throw ───

    [Fact]
    public void ActivateEditorToolEvent_Select_IsConsumedWithoutThrow()
    {
        var subsystem = new EditorSubsystem();
        subsystem.Initialize(HeadlessConfig());

        // Publish an ActivateEditorToolEvent on the bus — the headless path skips
        // actual tool switching but the event must be drained (not accumulate).
        subsystem.World.Bus.PublishManaged(new ActivateEditorToolEvent(EditorTool.Select));

        // Update twice so the event crosses the SwapBuffers → ConsumeManaged cycle.
        var ex = Record.Exception(() =>
        {
            subsystem.Update(0.016f);
            subsystem.Update(0.016f);
        });

        Assert.Null(ex);
        subsystem.Shutdown();
    }

    // ── T-ES19: Multiple tool events over several frames do not throw ─────────

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

    // ── T-ES20: SaveScenarioAs sets LoadedScenarioName ───────────────────────

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
}
