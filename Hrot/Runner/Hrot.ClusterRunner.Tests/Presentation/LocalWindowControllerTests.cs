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

        public void InitWindow(int w, int h, string t, int fps) => InitWindowCallCount++;
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
