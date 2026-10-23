using System;
using System.Collections.Generic;
using Fdp.Toolkit.Runner;
using Hrot.ClusterRunner.Presentation;
using Xunit;

namespace Hrot.ClusterRunner.Tests.Presentation;

/// <summary>
/// GZH-012: Tests for <see cref="LocalWindowController"/>.
/// </summary>
public class GZH012_Tests
{
    // ── Fake shell ─────────────────────────────────────────────────────────────

    internal sealed class FakePresentationShell : IPresentationShell
    {
        public int InitWindowCallCount    { get; private set; }
        public int SetupImGuiCallCount    { get; private set; }
        public int ShutdownImGuiCallCount { get; private set; }
        public int CloseWindowCallCount   { get; private set; }
        public int LoadAtlasCallCount     { get; private set; }
        public int LoadGizmoFontCallCount { get; private set; }

        // A bare service is sufficient for controller tests: SetUserScale/ApplyPendingRebuild
        // only touch managed state (no ImGui/GPU context needed) unless a bake runs.
        public Fdp.Presentation.Fonts.EditorFontService FontService { get; } = new();

        public void InitWindow(int w, int h, string t, int fps) { InitWindowCallCount++; InitTitle = t; }
        public void SetWindowTitle(string t) => Titles.Add(t);

        /// <summary>⭐ CE-1290 — the title the window was opened with, and every title set after.</summary>
        public string? InitTitle { get; private set; }
        public List<string> Titles { get; } = new();
        public void SetupImGui()      => SetupImGuiCallCount++;
        public void ShutdownImGui()   => ShutdownImGuiCallCount++;
        public void CloseWindow()     => CloseWindowCallCount++;
        public void UnloadAtlasTexture() { }
        // No-op in tests: Raylib GPU context is unavailable in headless test environment.
        public void LoadGizmoFont()   => LoadGizmoFontCallCount++;

        public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
        {
            LoadAtlasCallCount++;
            // Return a zeroed-out atlas -- tests don't need real GPU data.
            return new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static LocalWindowController CreateController(FakePresentationShell shell)
        => new LocalWindowController(
            shell,
            Array.Empty<ISubsystem>(),
            new RunnerOptions(),
            null /* coordinator */);

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GZH012_1_OpenLocalWindow_SetsIsOpen_AndCallsShell()
    {
        var shell = new FakePresentationShell();
        var ctrl = CreateController(shell);

        ctrl.OpenLocalWindow();

        Assert.True(ctrl.IsLocalWindowOpen);
        Assert.Equal(1, shell.InitWindowCallCount);
        Assert.Equal(1, shell.SetupImGuiCallCount);
        Assert.Equal(1, shell.LoadAtlasCallCount);
    }

    [Fact]
    public void GZH012_2_OpenLocalWindow_IsIdempotent()
    {
        var shell = new FakePresentationShell();
        var ctrl = CreateController(shell);

        ctrl.OpenLocalWindow();
        ctrl.OpenLocalWindow();

        Assert.Equal(1, shell.InitWindowCallCount);
    }

    [Fact]
    public void GZH012_3_CloseLocalWindow_ClearsIsOpen_AndCallsShell()
    {
        var shell = new FakePresentationShell();
        var ctrl = CreateController(shell);

        ctrl.OpenLocalWindow();
        ctrl.CloseLocalWindow();

        Assert.False(ctrl.IsLocalWindowOpen);
        Assert.Equal(1, shell.ShutdownImGuiCallCount);
        Assert.Equal(1, shell.CloseWindowCallCount);
    }

    [Fact]
    public void GZH012_4_CloseLocalWindow_IsIdempotent()
    {
        var shell = new FakePresentationShell();
        var ctrl = CreateController(shell);

        ctrl.OpenLocalWindow();
        ctrl.CloseLocalWindow();
        ctrl.CloseLocalWindow();

        Assert.Equal(1, shell.CloseWindowCallCount);
    }
}

/// <summary>
/// ⭐ <c>CE-1290</c> (docs/DESIGN_Map_3D_Mode.md §6ac) — the window's title names the loaded scenario, as an editor's does.
/// 🔒 User, 2026-10-23: <i>"i need to see currently loaded scenario name in the title of the clusterrunner as editors usually do"</i>.
/// </summary>
public class CE1290_WindowTitleTests
{
    /// <summary>A subsystem that offers a title detail — the editor's or CGF's loaded scenario, as a settable field.</summary>
    private sealed class TitledSubsystem : ISubsystem, IWindowTitleSource
    {
        public string? Detail { get; set; }
        public string? WindowTitleDetail => Detail;
        public string Name => "Titled";
        public System.Numerics.Vector4 TitleBarColor => System.Numerics.Vector4.One;
        public void Initialize(SubsystemConfig config) { }
        public void Update(float deltaTime) { }
        public void DrawWorld() { }
        public void DrawUI() { }
        public void Shutdown() { }
    }

    private static (LocalWindowController Ctrl, GZH012_Tests.FakePresentationShell Shell, TitledSubsystem Sub) Open(params ISubsystem[] extra)
    {
        var shell = new GZH012_Tests.FakePresentationShell();
        var sub = new TitledSubsystem();
        var subsystems = new List<ISubsystem> { sub };
        subsystems.AddRange(extra);
        var ctrl = new LocalWindowController(shell, subsystems, new RunnerOptions(), null);
        ctrl.OpenLocalWindow();
        return (ctrl, shell, sub);
    }

    [Theory]
    [InlineData("bt-doors", "HROT Cluster Runner \u2014 bt-doors")]
    [InlineData("  hill-attack ", "HROT Cluster Runner \u2014 hill-attack")]
    [InlineData(null, "HROT Cluster Runner")]
    [InlineData("", "HROT Cluster Runner")]
    [InlineData("   ", "HROT Cluster Runner")]
    public void TheTitleNamesTheScenario_AndIsTheBaseTitleWithoutOne(string? scenario, string expected)
        => Assert.Equal(expected, LocalWindowController.TitleFor(scenario));

    [Fact]
    public void TheWindowOpensWithTheBaseTitle()
    {
        var (ctrl, shell, _) = Open();
        Assert.Equal("HROT Cluster Runner", shell.InitTitle);
        Assert.Equal("HROT Cluster Runner", ctrl.CurrentTitle);
    }

    /// <summary>
    /// A load names the window, a second load renames it, New / clear (the session drops the name) clears it again; and an unchanged
    /// name does NOT set the title again (a per-frame call must be free). ⛔ Red-proof: stop calling <c>SetWindowTitle</c> and the
    /// first assertion fails; drop the <c>title == _title</c> guard and the count fails.
    /// </summary>
    [Fact]
    public void TheTitleFollowsTheLoadedScenario_OnEveryLoad_AndClearsOnNew()
    {
        var (ctrl, shell, sub) = Open();

        ctrl.UpdateWindowTitle();
        Assert.Empty(shell.Titles);                                   // nothing loaded: nothing to set

        sub.Detail = "bt-doors";
        ctrl.UpdateWindowTitle();
        Assert.Equal("HROT Cluster Runner \u2014 bt-doors", Assert.Single(shell.Titles));

        ctrl.UpdateWindowTitle();
        ctrl.UpdateWindowTitle();
        Assert.Single(shell.Titles);                                  // unchanged: not set again

        sub.Detail = "hill-attack";                                   // another load
        ctrl.UpdateWindowTitle();
        Assert.Equal("HROT Cluster Runner \u2014 hill-attack", shell.Titles[^1]);

        sub.Detail = null;                                            // New / clear
        ctrl.UpdateWindowTitle();
        Assert.Equal("HROT Cluster Runner", shell.Titles[^1]);
        Assert.Equal(3, shell.Titles.Count);
        Assert.Equal("HROT Cluster Runner", ctrl.CurrentTitle);
    }

    /// <summary>The first source with a detail wins; a source with none does not hide a later one.</summary>
    [Fact]
    public void TheFirstSourceWithADetailNamesTheWindow()
    {
        var other = new TitledSubsystem { Detail = "from-cgf" };
        var (ctrl, shell, sub) = Open(other);

        ctrl.UpdateWindowTitle();
        Assert.Equal("HROT Cluster Runner \u2014 from-cgf", Assert.Single(shell.Titles));   // the first source has none

        sub.Detail = "from-editor";
        ctrl.UpdateWindowTitle();
        Assert.Equal("HROT Cluster Runner \u2014 from-editor", shell.Titles[^1]);
    }

    /// <summary>A headless run opens no window: nothing is renamed.</summary>
    [Fact]
    public void WithNoWindowOpen_NothingIsRenamed()
    {
        var shell = new GZH012_Tests.FakePresentationShell();
        var sub = new TitledSubsystem { Detail = "x" };
        var ctrl = new LocalWindowController(shell, new ISubsystem[] { sub }, new RunnerOptions(), null);

        ctrl.UpdateWindowTitle();

        Assert.Empty(shell.Titles);
    }
}
