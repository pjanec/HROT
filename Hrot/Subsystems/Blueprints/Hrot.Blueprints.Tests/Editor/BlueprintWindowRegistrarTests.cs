using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Inspector;

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

    private sealed class FakeEditorCoordinator : IBlueprintEditorCoordinator
    {
        public event Action<ReloadCompletedInfo>? OnReloadCompleted;
        public event Action<string, ReloadSource>? OnReloadFailed;
    }

    private static BlueprintWindowRegistrar MakeRegistrar()
    {
        var store   = new EditorSelectionStore();
        var dirty   = new DirtyTracker();
        var state   = new EditorState();
        var session = new MockDebugSession();
        var coord   = new FakeEditorCoordinator();
        var drawers = new DrawerRegistry();

        return new BlueprintWindowRegistrar(store, dirty, state, session, coord, drawers);
    }

    [Fact]
    public void RegisterWindows_Registers_All_Expected_Windows()
    {
        var registry  = new SpyWindowRegistry();
        var registrar = MakeRegistrar();

        registrar.RegisterWindows(registry);

        // ⛔ L5 — "Inspector" REMOVED: the 70-line Blueprints stub is retired (Q38's list).
        //    ⭐ The four below are unchanged, so this rail still says the registry registers what it
        //      registers — ⛔ it did not become "whatever the registrar happens to do".
        var expected = new[]
        {
            "Debug Panel",
            "Watch Panel",
            "Callstack",
            "Hot Reload Log",
        };

        foreach (var name in expected)
            Assert.Contains(name, registry.RegisteredNames);

        // ⭐⭐ THE OTHER HALF, and it is what keeps this rail honest: a retired entry must be ABSENT.
        //    ⚠ Deleting a row from an expected-list rail proves nothing on its own — the rail would
        //      pass just as well if the registrar registered it anyway.
        Assert.DoesNotContain("Inspector", registry.RegisteredNames);
    }

    // FIX2-005: engine IWindowRegistrar path must register all 5 windows in WindowManager
    // (AssetBrowserWindow removed — MTB-P7-T5 retirement; GraphEditorWindow removed — BF-UX1 FIX D).
    [Fact]
    public void BlueprintWindowRegistrar_RegistersAllWindows_ViaEngineInterface()
    {
        var registrar       = MakeRegistrar();
        var engineRegistrar = (Fdp.Toolkit.Runner.IWindowRegistrar)registrar;
        var atlas           = new IconAtlas(IntPtr.Zero, 16f, 16f);
        var wm              = new WindowManager(atlas);

        engineRegistrar.RegisterWindows(wm);

        // ⛔ L5 — "Inspector" removed here too; see the sibling rail above for why, and for the
        //    DoesNotContain half that stops the removal being a silent weakening.
        var expected = new[]
        {
            "Debug Panel",
            "Watch Panel",
            "Callstack",
            "Hot Reload Log",
        };

        foreach (var name in expected)
            Assert.True(wm.TryGetWindow(name, out _),
                $"Expected window '{name}' to be registered in WindowManager.");
    }
}
