using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// AIE-015: verifies that EditorSubsystem.RegisterWindows wires the new shared AI editor
/// perspective infrastructure: three distinct perspectives (BTree, HSM, Blueprint) each with
/// their side-panel windows, and a single global Asset Browser window.
///
/// Previously this class tested the retired BlueprintWindowRegistrar path (FIX3-001).
/// That path has been replaced; see BlueprintWindowRegistrarTests for the registrar unit tests.
/// </summary>
public sealed class EditorSubsystemBlueprintWindowsTests
{
    private static WindowManager MakeWindowManager()
        => new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f));

    // ── Per-perspective side-panel ids produced by PerspectiveWorkspaceRegistrar ──────────────

    private static readonly string[] BTreeWindowIds =
    [
        "ai_inspector_btree",
        "ai_runtime_inspector_btree",
        "ai_trace_timeline_btree",
        "ai_find_results_btree",
        "ai_blackboard_variables_btree",
        "ai_diagnostics_btree",
    ];

    private static readonly string[] HsmWindowIds =
    [
        "ai_inspector_hsm",
        "ai_runtime_inspector_hsm",
        "ai_trace_timeline_hsm",
        "ai_find_results_hsm",
        "ai_blackboard_variables_hsm",
        "ai_diagnostics_hsm",
    ];

    private static readonly string[] BlueprintWindowIds =
    [
        "ai_inspector_blueprint",
        "ai_runtime_inspector_blueprint",
        "ai_trace_timeline_blueprint",
        "ai_find_results_blueprint",
        "ai_blackboard_variables_blueprint",
        "ai_diagnostics_blueprint",
    ];

    /// <summary>
    /// Success condition AIE-015 SC1:
    /// Three distinct OwningPerspective groups (BTree/HSM/Blueprint) plus the global
    /// Asset Browser are all registered after a single RegisterWindows call.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersThreePerspectives_AndGlobalBrowser()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // All six BTree side-panel windows must be present.
        foreach (var id in BTreeWindowIds)
            Assert.True(wm.TryGetWindow(id, out _),
                $"Expected BTree window '{id}' to be registered.");

        // All six HSM side-panel windows must be present.
        foreach (var id in HsmWindowIds)
            Assert.True(wm.TryGetWindow(id, out _),
                $"Expected HSM window '{id}' to be registered.");

        // All six Blueprint side-panel windows must be present.
        foreach (var id in BlueprintWindowIds)
            Assert.True(wm.TryGetWindow(id, out _),
                $"Expected Blueprint window '{id}' to be registered.");

        // Global Asset Browser must be registered and have Global scope.
        Assert.True(wm.TryGetWindow("ai_asset_browser", out var browser),
            "Expected the global Asset Browser ('ai_asset_browser') to be registered.");
        Assert.Equal(WindowScope.Global, browser!.Scope);
    }

    /// <summary>
    /// BTree perspective windows have OwningPerspective == "BTree".
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_BTreeWindows_HaveOwningPerspective_BTree()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        foreach (var id in BTreeWindowIds)
        {
            Assert.True(wm.TryGetWindow(id, out var win),
                $"Window '{id}' not found.");
            Assert.Equal("BTree", win!.OwningPerspective);
        }
    }

    /// <summary>
    /// HSM perspective windows have OwningPerspective == "HSM".
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_HsmWindows_HaveOwningPerspective_HSM()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        foreach (var id in HsmWindowIds)
        {
            Assert.True(wm.TryGetWindow(id, out var win),
                $"Window '{id}' not found.");
            Assert.Equal("HSM", win!.OwningPerspective);
        }
    }

    /// <summary>
    /// Blueprint perspective windows have OwningPerspective == "Blueprint".
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_BlueprintWindows_HaveOwningPerspective_Blueprint()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        foreach (var id in BlueprintWindowIds)
        {
            Assert.True(wm.TryGetWindow(id, out var win),
                $"Window '{id}' not found.");
            Assert.Equal("Blueprint", win!.OwningPerspective);
        }
    }
}
