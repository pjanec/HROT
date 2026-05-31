using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Inspector;
using Hrot.Blueprints.Editor.Reload;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Verifies that BlueprintWindowRegistrar registers all expected windows (BPF-035).
/// </summary>
public sealed class BlueprintWindowRegistrarTests
{
    private sealed class SpyWindowRegistry : IBlueprintWindowRegistry
    {
        public List<string> RegisteredNames { get; } = new();
        public void Register(string name, Func<IBlueprintEditorWindow> factory)
            => RegisteredNames.Add(name);
    }

    private sealed class StubAssetCatalog : IAssetCatalog
    {
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => Array.Empty<AssetCatalogEntry>();
    }

    private sealed class FakeEditorCoordinator : IBlueprintEditorCoordinator
    {
        public event Action<ReloadCompletedInfo>? OnReloadCompleted;
        public event Action<string, ReloadSource>? OnReloadFailed;
    }

    private static BlueprintWindowRegistrar MakeRegistrar()
    {
        var console     = new MockOutputConsole();
        var catalog     = new StubAssetCatalog();
        var store       = new EditorSelectionStore();
        var dirty       = new DirtyTracker();
        var state       = new EditorState();
        var session     = new MockDebugSession();
        var coord       = new FakeEditorCoordinator();
        var fdpCoord    = new AiHotReloadCoordinator(
            new BehaviorRegistry(),
            new BlueprintRegistry(),
            new AiHotReloadCoordinatorOptions());
        var qrs         = new QuickReloadService(catalog, state, console,
                              new Core.Compiler.BlueprintCompiler(), fdpCoord);
        var frs         = new FullRebuildService(console);
        var drawers     = new DrawerRegistry();

        return new BlueprintWindowRegistrar(catalog, store, dirty, state, session, coord, qrs, frs, drawers);
    }

    [Fact]
    public void RegisterWindows_Registers_All_Expected_Windows()
    {
        var registry  = new SpyWindowRegistry();
        var registrar = MakeRegistrar();

        registrar.RegisterWindows(registry);

        var expected = new[]
        {
            "Asset Browser",
            "Graph Editor",
            "Inspector",
            "Debug Panel",
            "Watch Panel",
            "Callstack",
            "Hot Reload Log",
        };

        foreach (var name in expected)
            Assert.Contains(name, registry.RegisteredNames);
    }
}
