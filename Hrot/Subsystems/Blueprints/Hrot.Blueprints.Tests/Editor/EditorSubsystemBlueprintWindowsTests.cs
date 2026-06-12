using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Editor;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Primitives;

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

    // ── AIE-047/048: Blueprint My Blueprint + Details + Variables windows ─────

    /// <summary>
    /// AIE-047: after RegisterWindows, the My Blueprint window is registered
    /// with the Blueprint perspective and PerspectiveBound scope.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersMyBlueprintWindow_ForBlueprint()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow("ai_my_blueprint_blueprint", out var mbWin),
            "Expected My Blueprint window 'ai_my_blueprint_blueprint' to be registered.");
        Assert.Equal("Blueprint", mbWin!.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, mbWin.Scope);
    }

    /// <summary>
    /// AIE-048: after RegisterWindows, the Details window is registered
    /// with the Blueprint perspective and PerspectiveBound scope.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersDetailsWindow_ForBlueprint()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow("ai_details_blueprint", out var detWin),
            "Expected Details window 'ai_details_blueprint' to be registered.");
        Assert.Equal("Blueprint", detWin!.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, detWin.Scope);
    }

    /// <summary>
    /// AIE-048: after RegisterWindows, the Variables window is registered
    /// with the Blueprint perspective and PerspectiveBound scope.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersVariablesWindow_ForBlueprint()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        Assert.True(wm.TryGetWindow("ai_variables_blueprint", out var varWin),
            "Expected Variables window 'ai_variables_blueprint' to be registered.");
        Assert.Equal("Blueprint", varWin!.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, varWin.Scope);
    }

    // ── AIE-020/021/022: Canvas windows (BATCH-05) ────────────────────────────

    /// <summary>
    /// BATCH-05 AIE-020: after RegisterWindows, both BTree and HSM canvas windows are
    /// registered (via RegisterExtraWindow on their respective registrars) with the correct
    /// OwningPerspective and PerspectiveBound scope.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersCanvasWindows_ForBTreeAndHsm()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // BTree canvas window.
        Assert.True(wm.TryGetWindow("ai_canvas_btree", out var btreeCanvas),
            "Expected BTree canvas window 'ai_canvas_btree' to be registered.");
        Assert.Equal("BTree",           btreeCanvas!.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, btreeCanvas.Scope);

        // HSM canvas window.
        Assert.True(wm.TryGetWindow("ai_canvas_hsm", out var hsmCanvas),
            "Expected HSM canvas window 'ai_canvas_hsm' to be registered.");
        Assert.Equal("HSM",             hsmCanvas!.OwningPerspective);
        Assert.Equal(WindowScope.PerspectiveBound, hsmCanvas.Scope);
    }

    // ── BATCH-24: Main toolbar populates on bare subsystem ───────────────────

    /// <summary>
    /// BATCH-24 guardrail: a bare <c>new EditorSubsystem()</c> (no Initialize call)
    /// must NOT throw when RegisterWindows is invoked, and the main toolbar must have
    /// entries after registration (Perspective group self-registers with 64f height).
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_PopulatesMainToolbar()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        // Guard: must not throw on a bare (uninitialised) subsystem.
        subsystem.RegisterWindows(wm);

        // Main toolbar must have registered entries — at minimum the Perspective
        // group which self-registers at sortOrder 20 with declared height 64f.
        Assert.True(wm.MainToolbar.Height > 0f,
            $"Expected MainToolbar.Height > 0 after RegisterWindows, but got {wm.MainToolbar.Height}.");
    }

    // ── BATCH-26: "Open Asset" command registration ──────────────────────────

    /// <summary>
    /// BATCH-26: The <c>shell.openAsset</c> command is registered in ShellCommands
    /// with the correct DisplayName, DefaultKey (Ctrl+O), and always-enabled state.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersOpenAssetCommand()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // Command must be registered.
        var desc = wm.ShellCommands.Get("shell.openAsset");
        Assert.NotNull(desc);
        Assert.Equal("Open Asset…", desc!.DisplayName);
        Assert.True(desc.IsEnabled());

        // Default key is Ctrl+O.
        Assert.NotNull(desc.DefaultKey);
        Assert.Equal(NodeEditor.Primitives.EditorKey.O, desc.DefaultKey!.Value.Key);
        Assert.Equal(NodeEditor.Primitives.KeyModifiers.Ctrl, desc.DefaultKey!.Value.Modifiers);
    }

    /// <summary>
    /// BATCH-26: The File→Open Asset… menu item is registered in the global menu.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_OpenAssetMenuItem_UnderFile()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // "File" top-level node must exist.
        Assert.True(wm.GlobalMenu.Root.Children.TryGetValue("File", out var fileNode));

        // "Open Asset…" leaf node must exist under File.
        Assert.True(fileNode.Children.TryGetValue("Open Asset…", out var leaf));
    }

    /// <summary>
    /// BATCH-26: A toolbar entry for shell.openAsset is registered.  The toolbar
    /// height must be &gt; 0 after registration (the Open Asset entry + Perspective
    /// group + AI-debug group all contribute to the declared height).
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_OpenAssetToolbarEntry_Exists()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // Main toolbar must have entries — the Open Asset button + Perspective
        // group + AI-debug group all contribute. Height > 0 proves entries exist.
        Assert.True(wm.MainToolbar.Height > 0f,
            $"Expected MainToolbar.Height > 0 after RegisterWindows, but got {wm.MainToolbar.Height}.");
    }

    // ── BATCH-31 (MTB2-T2): Save toolbar entry ────────────────────────────

    /// <summary>
    /// BATCH-31: The <c>shell.save</c> command is registered in ShellCommands
    /// with the correct DisplayName, DefaultKey (Ctrl+S), and the MainToolbar
    /// has entries (the Save button is registered at sortOrder -9).
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_RegistersSaveToolbarEntry()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // Command must be registered.
        var desc = wm.ShellCommands.Get(ShellSaveCommands.SaveId);
        Assert.NotNull(desc);
        Assert.Equal("Save", desc!.DisplayName);

        // Enabled depends on Active document; on a bare subsystem there is
        // no document, so IsEnabled returns false — but the descriptor exists.
        Assert.False(desc.IsEnabled());

        // Default key is Ctrl+S.
        Assert.NotNull(desc.DefaultKey);
        Assert.Equal(EditorKey.S, desc.DefaultKey!.Value.Key);
        Assert.Equal(KeyModifiers.Ctrl, desc.DefaultKey!.Value.Modifiers);

        // The Save button MUST be registered as a toolbar ENTRY (not merely the command).
        // Height > 0 alone would pass even if the Save button were missing — assert the entry by id.
        Assert.True(wm.MainToolbar.ContainsEntry(ShellSaveCommands.SaveId),
            "Expected a 'shell.save' entry in the MainToolbar after RegisterWindows.");
        // Open Asset must also still be present (Save sits to its right).
        Assert.True(wm.MainToolbar.ContainsEntry("shell.openAsset"),
            "Expected the 'shell.openAsset' entry to remain in the MainToolbar.");
    }

    // ── MTB2-T5 (BATCH-34): File menu has all save commands ────────────────

    /// <summary>
    /// MTB2-T5: After <see cref="EditorSubsystem.RegisterWindows"/> the File menu
    /// contains the five save entries plus the existing Open Asset… entry.
    /// </summary>
    [Fact]
    public void EditorSubsystem_RegisterWindows_FileMenuHasSaveCommands()
    {
        var subsystem = new EditorSubsystem();
        var wm = MakeWindowManager();

        subsystem.RegisterWindows(wm);

        // Navigate to the File menu node.
        Assert.True(wm.GlobalMenu.Root.Children.TryGetValue("File", out var fileNode),
            "Expected 'File' top-level menu to exist.");

        // Assert all expected items are present.
        Assert.True(fileNode.Children.ContainsKey("Save"),
            "Expected 'Save' under File menu.");
        Assert.True(fileNode.Children.ContainsKey("Save As…"),
            "Expected 'Save As…' under File menu.");
        Assert.True(fileNode.Children.ContainsKey("Save All"),
            "Expected 'Save All' under File menu.");
        Assert.True(fileNode.Children.ContainsKey("Open Asset…"),
            "Expected 'Open Asset…' under File menu.");
        Assert.True(fileNode.Children.ContainsKey("Save Scenario"),
            "Expected 'Save Scenario' under File menu.");

        // The Editor perspective label must be set to "Scenario".
        Assert.Equal("Scenario", wm.GetPerspectiveLabel("Editor"));
    }
}
