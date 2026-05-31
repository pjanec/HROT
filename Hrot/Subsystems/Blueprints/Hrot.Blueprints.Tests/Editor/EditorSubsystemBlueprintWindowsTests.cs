using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Inspector;
using Hrot.Blueprints.Editor.Reload;
using Hrot.Editor;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// FIX3-001: verifies that EditorSubsystem.RegisterWindows -- the production caller -- invokes
/// BlueprintWindowRegistrar and registers all 7 blueprint editor windows in the WindowManager.
/// EditorSubsystem.RegisterWindows is the production entry point that LocalWindowController
/// calls for every IWindowRegistrar subsystem, so it must be in the test's execution path.
/// </summary>
public sealed class EditorSubsystemBlueprintWindowsTests
{
    private sealed class StubAssetCatalog : IAssetCatalog
    {
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => Array.Empty<AssetCatalogEntry>();
    }

    private sealed class FakeEditorCoordinator : IBlueprintEditorCoordinator
    {
        public event Action<Hrot.Blueprints.Editor.ReloadCompletedInfo>? OnReloadCompleted;
        public event Action<string, Hrot.Blueprints.Editor.ReloadSource>? OnReloadFailed;
    }

    private static BlueprintWindowRegistrar MakeBlueprintRegistrar()
    {
        var console     = new MockOutputConsole();
        var catalog     = new StubAssetCatalog();
        var store       = new EditorSelectionStore();
        var dirty       = new DirtyTracker();
        var state       = new EditorState();
        var session     = new MockDebugSession();
        var coord       = new FakeEditorCoordinator();
        var fdpCoord    = new Fdp.Toolkit.Behavior.AiHotReloadCoordinator(
                              new BehaviorRegistry(),
                              new BlueprintRegistry(),
                              new Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions());
        var qrs         = new QuickReloadService(catalog, state, console,
                              new Core.Compiler.BlueprintCompiler(), fdpCoord);
        var frs         = new FullRebuildService(console);
        var drawers     = new DrawerRegistry();

        return new BlueprintWindowRegistrar(catalog, store, dirty, state, session, coord, qrs, frs, drawers);
    }

    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersAllBlueprintWindows()
    {
        // Arrange: use default (no-Initialize) ctor and inject registrar via InternalsVisibleTo.
        var subsystem = new EditorSubsystem();
        subsystem.BlueprintWindowRegistrar = MakeBlueprintRegistrar();

        var atlas = new IconAtlas(IntPtr.Zero, 16f, 16f);
        var wm    = new WindowManager(atlas);

        // Act: this is the same method LocalWindowController calls in production.
        subsystem.RegisterWindows(wm);

        // Assert: all 7 blueprint editor windows must appear in the WindowManager.
        var expectedWindows = new[]
        {
            "Asset Browser",
            "Graph Editor",
            "Inspector",
            "Debug Panel",
            "Watch Panel",
            "Callstack",
            "Hot Reload Log",
        };

        foreach (var name in expectedWindows)
            Assert.True(wm.TryGetWindow(name, out _),
                $"Expected blueprint window '{name}' to be registered by EditorSubsystem.RegisterWindows.");
    }
}
