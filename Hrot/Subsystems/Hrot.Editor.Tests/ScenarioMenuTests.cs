using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.DER;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Unit tests for <see cref="ScenarioMenuCommands"/> — MTB-P7-T1 success conditions.
/// Pure logic tests using recording fakes; no ImGui or real filesystem required.
/// </summary>
public sealed class ScenarioMenuTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>Recording fake for <see cref="IEditorLogic"/>.</summary>
    private sealed class FakeEditorLogic : IEditorLogic
    {
        public int NewScenarioCallCount;
        public int SaveCurrentScenarioCallCount;
        public readonly List<string> SaveScenarioAsCalls = new();
        public readonly List<string> LoadScenarioByNameCalls = new();
        public string? LoadedScenarioNameValue;

        public string? LoadedScenarioName => LoadedScenarioNameValue;
        public IReadOnlyList<string> AvailableScenarios => Array.Empty<string>();
        public IReadOnlyList<SidecarFileInfo> MigrationSidecars { get; set; } = Array.Empty<SidecarFileInfo>();
        public bool IsScenarioDegraded => false;

        public void Update() { }
        public void NewScenario() => NewScenarioCallCount++;
        public void SaveScenario(string filePath) { }
        public void LoadScenario(string filePath) { }
        public void LoadScenarioByName(string scenarioName) => LoadScenarioByNameCalls.Add(scenarioName);
        public void SaveCurrentScenario() => SaveCurrentScenarioCallCount++;
        public void SaveScenarioAs(string scenarioName) => SaveScenarioAsCalls.Add(scenarioName);
        public void ActivateTool(EditorTool tool) { }
        public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents) { }
        public IDerRepo View => null!;
        public Task SwitchToExternalAsync() => Task.CompletedTask;
        public Task SwitchToInternalAsync() => Task.CompletedTask;
        public SimHostMode CurrentMode => SimHostMode.Internal;
        public void CenterOnEntity(long entityId) { }
        public void SelectEntity(long entityId) { }
        public void OpenRenameDialog(long entityId) { }
        public void RebuildAndReloadAI() { }

        public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()
            => MigrationSidecars;
    }

    /// <summary>
    /// A recording <see cref="IEditorCommands"/> implementation for adapter tests.
    /// Captures every registered command descriptor and its handler.
    /// </summary>
    private sealed class RecordingCommandSet : IEditorCommands
    {
        private readonly Dictionary<string, (EditorCommandDescriptor Descriptor, Action<EditorCommandContext> Action)> _commands
            = new(StringComparer.Ordinal);

        public int RegisterCallCount => _commands.Count;
        public event Action<string>? AvailabilityChanged;

        public IReadOnlyList<EditorCommandDescriptor> All
        {
            get
            {
                var list = new List<EditorCommandDescriptor>();
                foreach (var kv in _commands)
                    list.Add(kv.Value.Descriptor);
                return list;
            }
        }

        public EditorCommandDescriptor? Get(string commandId)
            => _commands.TryGetValue(commandId, out var c) ? c.Descriptor : null;

        public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null)
        {
            if (!_commands.TryGetValue(commandId, out var cmd))
                return new EditorCommandResult(false, $"Unknown command: {commandId}");

            if (!cmd.Descriptor.IsEnabled())
                return new EditorCommandResult(false, "Command not enabled.");

            cmd.Action(ctx ?? default);
            return new EditorCommandResult(true, null);
        }

        public void Register(EditorCommandDescriptor descriptor, Action<EditorCommandContext> action)
        {
            _commands[descriptor.Id] = (descriptor, action);
        }

        public void NotifyAvailabilityChanged(string commandId)
            => AvailabilityChanged?.Invoke(commandId);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static (
        FakeEditorLogic EditorLogic,
        RecordingCommandSet Commands,
        GlobalMenuRegistry Menu,
        Action<AssetKindFilter, Action<IEditableAsset?>>? PickerCapture,
        Action<Action<string>>? SaveAsCapture,
        Action<IReadOnlyList<SidecarFileInfo>>? MigrationCapture)
        CreateRegistrar(string? loadedScenarioName = null)
    {
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = loadedScenarioName };
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        Action<AssetKindFilter, Action<IEditableAsset?>>? pickerCapture = null;
        Action<Action<string>>? saveAsCapture = null;
        Action<IReadOnlyList<SidecarFileInfo>>? migrationCapture = null;

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) => pickerCapture?.Invoke(kinds, cb),
            openSaveAsDialog:   cb => saveAsCapture?.Invoke(cb),
            showMigrationHistory: list => migrationCapture?.Invoke(list));

        return (editorLogic, commands, menu, pickerCapture, saveAsCapture, migrationCapture);
    }

    // ── MenuItems_Registered_UnderScenario ───────────────────────────────────

    [Fact]
    public void MenuItems_Registered_UnderScenario()
    {
        var (_, _, menu, _, _, _) = CreateRegistrar();

        // Verify "Scenario" top-level menu node exists.
        Assert.True(menu.Root.Children.ContainsKey("Scenario"));
        var scenarioNode = menu.Root.Children["Scenario"];

        // Five sub-items expected: New, Save, Save As, Load, Migration History.
        Assert.Equal(5, scenarioNode.Children.Count);
        Assert.True(scenarioNode.Children.ContainsKey("New"));
        Assert.True(scenarioNode.Children.ContainsKey("Save"));
        Assert.True(scenarioNode.Children.ContainsKey("Save As"));
        Assert.True(scenarioNode.Children.ContainsKey("Load"));
        Assert.True(scenarioNode.Children.ContainsKey("Migration History"));
    }

    // ── New_Invoke_EditorLogic ───────────────────────────────────────────────

    [Fact]
    public void FiveCommands_Registered_InCommandSet()
    {
        var (_, commands, _, _, _, _) = CreateRegistrar();

        Assert.Equal(5, commands.RegisterCallCount);
        Assert.NotNull(commands.Get(ScenarioMenuCommands.NewId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveAsId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.LoadId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.MigrationHistoryId));
    }

    [Fact]
    public void New_Invoke_CallsEditorLogicNewScenario()
    {
        var (editorLogic, commands, _, _, _, _) = CreateRegistrar();
        var result = commands.Invoke(ScenarioMenuCommands.NewId);
        Assert.True(result.Success);
        Assert.Equal(1, editorLogic.NewScenarioCallCount);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_WhenScenarioLoaded_CallsSaveCurrentScenario()
    {
        var (editorLogic, commands, _, _, _, _) = CreateRegistrar(loadedScenarioName: "MyScenario");
        var result = commands.Invoke(ScenarioMenuCommands.SaveId);
        Assert.True(result.Success);
        Assert.Equal(1, editorLogic.SaveCurrentScenarioCallCount);
    }

    [Fact]
    public void Save_WhenNoScenarioLoaded_RoutesToSaveAs()
    {
        var editorLogic = new FakeEditorLogic { LoadedScenarioNameValue = null };

        var menu2 = new GlobalMenuRegistry();
        var commands2 = new RecordingCommandSet();

        // Re-create with saveAs that actually captures.
        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands2.Register(desc, handler),
            menu:               menu2,
            commands:           commands2,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) => { },
            openSaveAsDialog:   cb =>
            {
                // Simulate user confirming Save-As with a name.
                cb("NewName");
            },
            showMigrationHistory: null);

        var result2 = commands2.Invoke(ScenarioMenuCommands.SaveId);
        Assert.True(result2.Success);
        Assert.Equal(0, editorLogic.SaveCurrentScenarioCallCount);
        var saveCall = Assert.Single(editorLogic.SaveScenarioAsCalls);
        Assert.Equal("NewName", saveCall);
    }

    // ── SaveAs ───────────────────────────────────────────────────────────────

    [Fact]
    public void SaveAs_Invoke_OpensSaveAsDialog_AndCallsSaveScenarioAs()
    {
        var editorLogic = new FakeEditorLogic();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) => { },
            openSaveAsDialog:   cb =>
            {
                // Simulate user confirming Save-As with a name.
                cb("Combat/NewName");
            },
            showMigrationHistory: null);

        var result = commands.Invoke(ScenarioMenuCommands.SaveAsId);
        Assert.True(result.Success);
        var saveCall = Assert.Single(editorLogic.SaveScenarioAsCalls);
        Assert.Equal("Combat/NewName", saveCall);
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_OpensScenarioFilteredModal_AndCallsLoadScenarioByName()
    {
        AssetKindFilter? capturedFilter = null;
        var editorLogic = new FakeEditorLogic();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) =>
            {
                capturedFilter = kinds;
                // Simulate user picking a scenario asset.
                cb(new FakeScenarioAsset("PickedScenario"));
            },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: null);

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.Equal(AssetKindFilter.Scenario, capturedFilter);
        var loadCall = Assert.Single(editorLogic.LoadScenarioByNameCalls);
        Assert.Equal("PickedScenario", loadCall);
    }

    [Fact]
    public void Load_PickerCancelled_DoesNotCallLoadScenarioByName()
    {
        var editorLogic = new FakeEditorLogic();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) =>
            {
                // Simulate user cancelling the picker.
                cb(null);
            },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: null);

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.Empty(editorLogic.LoadScenarioByNameCalls);
    }

    // ── Migration History ────────────────────────────────────────────────────

    [Fact]
    public void MigrationHistory_WhenScenarioLoaded_ListsSidecars()
    {
        var sidecars = new List<SidecarFileInfo>
        {
            new("snap_v1.hash", SidecarKind.Snapshot, 1, "abc123"),
            new("journal_v2.hash", SidecarKind.Journal, 2, "def456"),
        };

        IReadOnlyList<SidecarFileInfo>? capturedSidecars = null;
        var editorLogic = new FakeEditorLogic
        {
            LoadedScenarioNameValue = "MyScenario",
            MigrationSidecars = sidecars,
        };
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) => { },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: list => capturedSidecars = list);

        var result = commands.Invoke(ScenarioMenuCommands.MigrationHistoryId);
        Assert.True(result.Success);
        Assert.NotNull(capturedSidecars);
        Assert.Equal(2, capturedSidecars!.Count);
        Assert.Equal("snap_v1.hash", capturedSidecars[0].FileName);
        Assert.Equal(SidecarKind.Snapshot, capturedSidecars[0].Kind);
    }

    [Fact]
    public void MigrationHistory_DisabledWhenNoScenarioLoaded()
    {
        var (_, commands, _, _, _, _) = CreateRegistrar(loadedScenarioName: null);

        var descriptor = commands.Get(ScenarioMenuCommands.MigrationHistoryId);
        Assert.NotNull(descriptor);
        Assert.False(descriptor!.IsEnabled());
    }

    [Fact]
    public void MigrationHistory_EnabledWhenScenarioLoaded()
    {
        var (_, commands, _, _, _, _) = CreateRegistrar(loadedScenarioName: "LoadedScenario");

        var descriptor = commands.Get(ScenarioMenuCommands.MigrationHistoryId);
        Assert.NotNull(descriptor);
        Assert.True(descriptor!.IsEnabled());
    }

    // ── Menu leaf nodes have correct OnClick handlers ───────────────────────

    [Fact]
    public void New_MenuItem_OnClick_InvokesCommand()
    {
        var (editorLogic, commands, menu, _, _, _) = CreateRegistrar();

        Assert.True(menu.Root.Children.TryGetValue("Scenario", out var scenarioNode));
        Assert.True(scenarioNode.Children.TryGetValue("New", out var leaf));
        Assert.NotNull(leaf.OnClick);

        leaf.OnClick();
        Assert.Equal(1, editorLogic.NewScenarioCallCount);
    }

    [Fact]
    public void Save_MenuItem_HasEnabledState()
    {
        var (_, _, menu, _, _, _) = CreateRegistrar();

        Assert.True(menu.Root.Children.TryGetValue("Scenario", out var scenarioNode));
        Assert.True(scenarioNode.Children.TryGetValue("Save", out var leaf));
        // Enabled state should always be true (GetEnabled is set via adapter).
        Assert.NotNull(leaf.GetEnabled);
        Assert.True(leaf.GetEnabled());
    }

    // ── Edge case: MigrationHistory handler is no-op when seam is null ──────

    [Fact]
    public void MigrationHistory_WhenSeamIsNull_DoesNotThrow()
    {
        var editorLogic = new FakeEditorLogic
        {
            LoadedScenarioNameValue = "HasScenario",
            MigrationSidecars = new[] { new SidecarFileInfo("f", SidecarKind.Snapshot, 1, "h") },
        };
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) => { },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: null); // null seam

        // Invoking should not throw.
        var result = commands.Invoke(ScenarioMenuCommands.MigrationHistoryId);
        Assert.True(result.Success);
    }

    // ── BATCH-26: scenario.load → unified modal opens ──────────────────────

    /// <summary>
    /// BATCH-26: Invoking the <c>scenario.load</c> command opens the unified
    /// AssetPickerModal and sets its IsOpen to true.  The openPicker seam is
    /// wired to the real AssetPickerModal in production; this test verifies
    /// that the seam → modal chain works correctly.
    /// </summary>
    [Fact]
    public void Load_Invoke_OpensUnifiedModal()
    {
        var editorLogic = new FakeEditorLogic();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();
        var catalog = new FakeAssetCatalog();
        var icons = new FakeIconProvider();
        var modal = new AssetPickerModal(catalog, icons);

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) =>
            {
                // BATCH-26: opens the unified modal (same as production).
                modal.Open(
                    new AssetBrowserPanelOptions
                    {
                        Kinds = kinds,
                        ShowAllTab = kinds == AssetKindFilter.All
                    },
                    cb);
            },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: null);

        Assert.False(modal.IsOpen);

        // Invoke scenario.load — this should open the modal.
        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.True(modal.IsOpen, "Expected modal.IsOpen = true after scenario.load invoked.");

        // Verify the picker is scenario-filtered.
        Assert.NotNull(modal.Panel);
        Assert.Contains(AssetKind.Scenario, modal.Panel!.Tabs);
    }

    /// <summary>
    /// BATCH-26: When the scenario picker is cancelled (callback(null)),
    /// the modal closes and LoadScenarioByName is NOT called.
    /// </summary>
    [Fact]
    public void Load_UnifiedModal_Cancel_DoesNotLoad()
    {
        var editorLogic = new FakeEditorLogic();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();
        var catalog = new FakeAssetCatalog();
        var icons = new FakeIconProvider();
        var modal = new AssetPickerModal(catalog, icons);

        ScenarioMenuCommands.Register(
            registerCommand:    (desc, handler) => commands.Register(desc, handler),
            menu:               menu,
            commands:           commands,
            editorLogic:        editorLogic,
            openPicker:         (kinds, cb) =>
            {
                modal.Open(
                    new AssetBrowserPanelOptions
                    {
                        Kinds = kinds,
                        ShowAllTab = false
                    },
                    cb);
                // Simulate user cancelling via Esc / X button.
                modal.HandleCancel();
            },
            openSaveAsDialog:   cb => { },
            showMigrationHistory: null);

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);

        // Modal must be closed after cancel.
        Assert.False(modal.IsOpen);
        Assert.Empty(editorLogic.LoadScenarioByNameCalls);
    }

    // ── Fake catalog + icon provider for BATCH-26 unified modal tests ────

    private sealed class FakeAssetCatalog : IAssetCatalog
    {
        public IReadOnlyList<IEditableAsset> All => Array.Empty<IEditableAsset>();
        public IEditableAsset? FindByAssetId(Guid assetId) => null;
        public IEditableAsset? FindByName(string name) => null;
        public IReadOnlyList<IEditableAsset> WhereDependsOn(Guid assetId) => Array.Empty<IEditableAsset>();
#pragma warning disable CS0067
        public event Action<AssetKind>? Changed;
#pragma warning restore CS0067
    }

    private sealed class FakeIconProvider : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle)
        {
            handle = new IconHandle(1, 16, 16);
            return true;
        }
    }

    // ── Fake scenario asset for picker tests ────────────────────────────────

    private sealed class FakeScenarioAsset : IEditableAsset
    {
        public FakeScenarioAsset(string name) { Name = name; }
        public Guid AssetId => Guid.NewGuid();
        public string Name { get; }
        public AssetKind Kind => AssetKind.Scenario;
        public string SourceFilePath => "";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
