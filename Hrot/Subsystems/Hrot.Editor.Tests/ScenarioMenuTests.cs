using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Unit tests for <see cref="ScenarioMenuCommands"/> — MTB-P7-T1 success conditions, carried forward to
/// <c>CE-046</c>'s distinct-item structure.
///
/// <para>⭐⭐⭐ <b><c>CE-046</c> — this file was rewritten against <see cref="IScenarioSession"/>, and the
/// menu-structure rails MOVED with the items.</b> 📄
/// <c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c> §3a; <c>AQ60</c> ruling <b>R2</b>.</para>
///
/// <para>⚠⚠ <b>THE LABEL/PATH CHANGE IS DELIBERATE AND VISIBLE — argued, not slipped in.</b> The old flat
/// <c>File/Scenario/*</c> group is gone; items now live under <c>File/Live</c>, <c>File/Edit</c> and
/// <c>File/Checkpoint</c>. 📐 The old <c>Load Scenario…</c> was the chameleon R2 names: it was labelled
/// generically and meant <i>load for AUTHORING</i>. ⇒ the design's §6 gate anticipates exactly this
/// *("if the editor's existing item labels change, that IS a visible change — argue it in the report")*.
/// ⭐ <b>Every command ID is unchanged</b>, so hotkeys, MCP identity and every id-keyed rail still
/// resolve; only the human-facing text moved.</para>
///
/// <para>Pure logic tests using recording fakes; no ImGui or real filesystem required.</para>
/// </summary>
public sealed class ScenarioMenuTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Recording fake for <see cref="IScenarioSession"/> — replaces the old <c>FakeEditorLogic</c>.
    /// ⭐⭐ Note it is far SMALLER: the registrar used to bind to the whole 20-member editor facade to
    /// reach six scenario verbs, which is precisely why it could not be called from CGF.
    /// </summary>
    private sealed class FakeScenarioSession : IScenarioSession
    {
        public int UpdateCallCount;
        public int ClearWorldCallCount;
        public int NewExerciseCallCount;
        public int SaveCurrentCallCount;
        public int TakeCheckpointCallCount;
        public readonly List<string> SaveAsCalls      = new();
        public readonly List<string> SaveToCalls      = new();
        public readonly List<string> OpenForEditCalls = new();
        public readonly List<string> LoadForLiveCalls = new();
        public string? LoadedScenarioNameValue;

        public IReadOnlyList<SidecarFileInfo> MigrationSidecars { get; set; } = Array.Empty<SidecarFileInfo>();

        public ClusterState CurrentClusterState => ClusterState.Idle;
        public string? LoadedScenarioName => LoadedScenarioNameValue;
        public bool IsDegraded => false;

        public void Update()                        => UpdateCallCount++;
        public void ClearWorld()                    => ClearWorldCallCount++;
        public void NewExercise()                   => NewExerciseCallCount++;
        public void LoadForLive(string name)        => LoadForLiveCalls.Add(name);
        public void OpenForEdit(string name)        => OpenForEditCalls.Add(name);
        public void SaveCurrent()                   => SaveCurrentCallCount++;
        public void SaveAs(string name)             => SaveAsCalls.Add(name);
        public void SaveTo(string filePath)         => SaveToCalls.Add(filePath);
        public void TakeCheckpoint()                => TakeCheckpointCallCount++;

        public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars() => MigrationSidecars;
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
        FakeScenarioSession Session,
        RecordingCommandSet Commands,
        GlobalMenuRegistry Menu)
        CreateRegistrar(
            string?                                          loadedScenarioName = null,
            Action<AssetKindFilter, Action<IEditableAsset?>>? openPicker         = null,
            Action<Action<string>>?                          openSaveAsDialog   = null,
            Action<Action>?                                  confirmNewExercise = null,
            Action<IReadOnlyList<SidecarFileInfo>>?           showMigrationHistory = null,
            FakeScenarioSession?                             session            = null)
    {
        var s        = session ?? new FakeScenarioSession { LoadedScenarioNameValue = loadedScenarioName };
        var commands = new RecordingCommandSet();
        var menu     = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:      (desc, handler) => commands.Register(desc, handler),
            menu:                 menu,
            commands:             commands,
            session:              s,
            openPicker:           openPicker       ?? ((kinds, cb) => { }),
            openSaveAsDialog:     openSaveAsDialog ?? (cb => { }),
            confirmNewExercise:   confirmNewExercise,
            showMigrationHistory: showMigrationHistory);

        return (s, commands, menu);
    }

    // ══ ① The distinct submenu structure (design §3a) ═══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The items live under <c>Live/</c>, <c>Edit/</c> and <c>Checkpoint/</c> — the structure IS
    /// the anti-chameleon guarantee.</b> 🔒 R2 — <i>distinct menu items, no per-host default in the
    /// menu</i>. ⛔ A single generic <c>Load Scenario…</c> that silently means <i>edit</i> is what this
    /// replaces.
    /// </summary>
    [Fact]
    public void MenuItems_Registered_UnderTheDistinctSubmenus()
    {
        var (_, _, menu) = CreateRegistrar();

        Assert.True(menu.Root.Children.ContainsKey("File"));
        var fileNode = menu.Root.Children["File"];

        // ⛔ The old flat "Scenario" group is GONE — that is the point, not a regression.
        Assert.False(fileNode.Children.ContainsKey("Scenario"));

        var live = fileNode.Children["Live"];
        Assert.Equal(2, live.Children.Count);
        Assert.True(live.Children.ContainsKey("New Exercise"));
        Assert.True(live.Children.ContainsKey("Load Scenario"));

        var edit = fileNode.Children["Edit"];
        Assert.Equal(6, edit.Children.Count);
        Assert.True(edit.Children.ContainsKey("Open Scenario"));
        Assert.True(edit.Children.ContainsKey("New Scenario"));
        Assert.True(edit.Children.ContainsKey("Save Scenario"));
        Assert.True(edit.Children.ContainsKey("Save Scenario As"));
        Assert.True(edit.Children.ContainsKey("Migration History"));
        Assert.True(edit.Children.ContainsKey("Save Curated Scenarios to Git"));

        var checkpoint = fileNode.Children["Checkpoint"];
        var takeCheckpoint = Assert.Single(checkpoint.Children);
        Assert.Equal("Take Checkpoint", takeCheckpoint.Key);
    }

    /// <summary>
    /// ⛔⛔ <b>NO <c>File/Save</c> item is registered here.</b> 📐 Measured <c>2026-08-26</c>:
    /// <c>CgfEditorShellToolbar</c>'s shared slot table already emits <c>File/Save</c> → <c>shell.save</c>,
    /// and that handler already branches to the scenario. ⇒ registering a second item at the same path
    /// would be two controls for one action *(ruling 9)*, and would change the toolbar's own menu row —
    /// which R3 forbids.
    /// </summary>
    [Fact]
    public void NoSecondFileSaveItemIsRegistered()
    {
        var (_, _, menu) = CreateRegistrar();

        var fileNode = menu.Root.Children["File"];
        Assert.False(fileNode.Children.ContainsKey("Save"));
    }

    /// <summary>⭐ Every command reaches the command set, keyed by its unchanged id.
    /// ⚠⚠ <b>No count in the NAME</b> — 📌 a count in a test name is a rail that lies the moment the count
    /// moves, which is exactly how this file's predecessor went stale twice.</summary>
    [Fact]
    public void AllCommands_Registered_InCommandSet()
    {
        var (_, commands, _) = CreateRegistrar();

        Assert.Equal(9, commands.RegisterCallCount);
        Assert.NotNull(commands.Get(ScenarioMenuCommands.NewId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.NewExerciseId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.LoadId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.LoadLiveId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveAsId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.MigrationHistoryId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.TakeCheckpointId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.UpdateCuratedId));
    }

    // ══ ② THE ROUTING RAIL — live vs edit are two different calls ═══════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THIS SLICE EXISTS FOR: the two load items route to two DIFFERENT session
    /// verbs.</b> 📄 design §6 *("routing rails — LoadForLive/OpenForEdit publish the live/edit
    /// TransitionStateIntent")*.
    ///
    /// <para>⚠ Asserted as a PAIR in one test on purpose: the failure mode worth catching is not *"live
    /// does nothing"* but *"live and edit both went to the same place"* — which a per-item test would pass
    /// happily. 📌 The same shape as <c>AX-018</c>, where two paths silently agreed to be wrong.</para>
    /// </summary>
    [Fact]
    public void TheTwoLoadItemsRouteToTheTwoDifferentModes()
    {
        var (session, commands, _) = CreateRegistrar(
            openPicker: (kinds, cb) => cb(new FakeScenarioAsset("PickedScenario")));

        Assert.True(commands.Invoke(ScenarioMenuCommands.LoadLiveId).Success);
        Assert.True(commands.Invoke(ScenarioMenuCommands.LoadId).Success);

        Assert.Equal("PickedScenario", Assert.Single(session.LoadForLiveCalls));
        Assert.Equal("PickedScenario", Assert.Single(session.OpenForEditCalls));
    }

    [Fact]
    public void Load_OpensScenarioFilteredModal_AndOpensForEdit()
    {
        AssetKindFilter? capturedFilter = null;
        var (session, commands, _) = CreateRegistrar(
            openPicker: (kinds, cb) =>
            {
                capturedFilter = kinds;
                cb(new FakeScenarioAsset("PickedScenario"));
            });

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.Equal(AssetKindFilter.Scenario, capturedFilter);
        Assert.Equal("PickedScenario", Assert.Single(session.OpenForEditCalls));
    }

    [Fact]
    public void Load_PickerCancelled_DoesNotLoad()
    {
        var (session, commands, _) = CreateRegistrar(openPicker: (kinds, cb) => cb(null));

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.Empty(session.OpenForEditCalls);
        Assert.Empty(session.LoadForLiveCalls);
    }

    // ══ ③ THE CONFIRM BRANCH — New Exercise is destructive ═════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The cluster-wide reset does NOT run until the confirmation resolves.</b>
    /// 🔒 Ruling 53 — <i>a hard reload is a confirmed cluster-wide reset and the confirm belongs where the
    /// OPERATOR sits</i>. ⛔ The failure this pins is the worst kind: an operator clicking a menu item and
    /// losing a running exercise before any prompt appeared.
    /// </summary>
    [Fact]
    public void NewExercise_DoesNotRunUntilTheConfirmationIsResolved()
    {
        var prompt = new ConfirmPromptController();
        var (session, commands, _) = CreateRegistrar(
            confirmNewExercise: run => prompt.Request("New Exercise", "sure?", "Yes", run));

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewExerciseId).Success);

        // ⭐ The prompt is up and NOTHING has happened yet.
        Assert.True(prompt.IsPrompting);
        Assert.Equal(0, session.NewExerciseCallCount);

        prompt.ResolveConfirm();
        Assert.Equal(1, session.NewExerciseCallCount);
        Assert.False(prompt.IsPrompting);
    }

    /// <summary>⭐⭐ And CANCEL means the exercise survives — the other half of the same branch.</summary>
    [Fact]
    public void NewExercise_Cancelled_NeverResetsTheCluster()
    {
        var prompt = new ConfirmPromptController();
        var (session, commands, _) = CreateRegistrar(
            confirmNewExercise: run => prompt.Request("New Exercise", "sure?", "Yes", run));

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewExerciseId).Success);
        prompt.ResolveCancel();

        Assert.Equal(0, session.NewExerciseCallCount);
        Assert.False(prompt.IsPrompting);
    }

    /// <summary>
    /// ⭐⭐ <b>A headless host's log-and-proceed seam runs it immediately</b> — the CGF arm.
    /// 📄 <c>UX_Feature_Modal_Surfaces.md</c> §2.0b: <i>"Headless never pre-flights … the origin still
    /// LOGS what it skipped."</i> ⚠ Asserted so the CGF wiring cannot silently become a no-op prompt.
    /// </summary>
    [Fact]
    public void NewExercise_WithALogAndProceedSeam_RunsImmediately()
    {
        bool logged = false;
        var (session, commands, _) = CreateRegistrar(
            confirmNewExercise: run => { logged = true; run(); });

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewExerciseId).Success);

        Assert.True(logged);
        Assert.Equal(1, session.NewExerciseCallCount);
    }

    /// <summary>
    /// ⚠⚠ <b>NEW EXERCISE IS NOT NEW SCENARIO, and this rail is why both ids exist.</b>
    /// 📐 Measured: the deferred-load state machine calls the LOCAL wipe as step 1 of its own sequence, so
    /// if <c>scenario.new</c> had been pointed at the cluster-wide reset every load would publish a second
    /// <c>Idle</c> intent from inside the handler for the first. ⇒ two verbs, asserted apart.
    /// </summary>
    [Fact]
    public void NewScenarioClearsLocally_WhileNewExerciseResetsTheCluster()
    {
        var (session, commands, _) = CreateRegistrar(confirmNewExercise: run => run());

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewId).Success);
        Assert.Equal(1, session.ClearWorldCallCount);
        Assert.Equal(0, session.NewExerciseCallCount);

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewExerciseId).Success);
        Assert.Equal(1, session.NewExerciseCallCount);
        // ⭐ ClearWorld is the SESSION's business inside NewExercise — the menu did not call it again.
        Assert.Equal(1, session.ClearWorldCallCount);
    }

    // ══ ④ Checkpoint ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <c>File/Checkpoint/Take Checkpoint</c> reaches the session's checkpoint verb.
    /// ⛔ <c>Restore Checkpoint</c> is deliberately ABSENT — the save exists cluster-wide, the restore does
    /// not *(design §8, Feature X)*, and registering it would be a control that cannot work.
    /// </summary>
    [Fact]
    public void TakeCheckpoint_ReachesTheSession_AndNoRestoreItemExists()
    {
        var (session, commands, menu) = CreateRegistrar();

        Assert.True(commands.Invoke(ScenarioMenuCommands.TakeCheckpointId).Success);
        Assert.Equal(1, session.TakeCheckpointCallCount);

        var checkpoint = menu.Root.Children["File"].Children["Checkpoint"];
        Assert.False(checkpoint.Children.ContainsKey("Restore Checkpoint"));
    }

    // ══ ⑤ Save / Save-As ════════════════════════════════════════════════════

    [Fact]
    public void Save_WithLoadedScenario_CallsSaveCurrent()
    {
        var (session, commands, _) = CreateRegistrar(loadedScenarioName: "MyScenario");

        var result = commands.Invoke(ScenarioMenuCommands.SaveId);
        Assert.True(result.Success);
        Assert.Equal(1, session.SaveCurrentCallCount);
        Assert.Empty(session.SaveAsCalls);
    }

    [Fact]
    public void Save_WithoutLoadedScenario_OpensSaveAsDialog()
    {
        var (session, commands, _) = CreateRegistrar(
            loadedScenarioName: null,
            openSaveAsDialog:   cb => cb("NewName"));

        var result = commands.Invoke(ScenarioMenuCommands.SaveId);
        Assert.True(result.Success);
        Assert.Equal(0, session.SaveCurrentCallCount);
        Assert.Equal("NewName", Assert.Single(session.SaveAsCalls));
    }

    [Fact]
    public void SaveAs_OpensSaveAsDialog_AndCallsSaveAs()
    {
        var (session, commands, _) = CreateRegistrar(openSaveAsDialog: cb => cb("RenamedScenario"));

        var result = commands.Invoke(ScenarioMenuCommands.SaveAsId);
        Assert.True(result.Success);
        Assert.Equal("RenamedScenario", Assert.Single(session.SaveAsCalls));
    }

    // ══ ⑥ SERVICEABILITY — ruling 49 / VC-3 ════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A host with no picker gets the items REGISTERED, DISABLED, and SAYING WHY.</b>
    /// 🔒 Ruling 49 — the menu shows every serviceable item; 🔒 VC-3 *(user, <c>2026-08-17</c>)* — an
    /// explanatory label beats a control that looks broken. ⛔ This is the CGF shape, and the failure it
    /// pins is a live-looking <c>Load Scenario</c> on a node that cannot pick one.
    /// </summary>
    [Fact]
    public void WithNoPicker_TheLoadItemsAreDisabledAndExplainThemselves()
    {
        var (_, commands, _) = CreateRegistrar(openPicker: null, openSaveAsDialog: null);

        // ⚠ CreateRegistrar substitutes no-op seams for null, so register directly for this rail.
        var session  = new FakeScenarioSession();
        var cmds     = new RecordingCommandSet();
        var menu     = new GlobalMenuRegistry();
        ScenarioMenuCommands.Register(
            registerCommand: (d, h) => cmds.Register(d, h),
            menu: menu, commands: cmds, session: session,
            openPicker: null, openSaveAsDialog: null);

        foreach (var id in new[] { ScenarioMenuCommands.LoadId, ScenarioMenuCommands.LoadLiveId,
                                   ScenarioMenuCommands.SaveAsId })
        {
            var descriptor = cmds.Get(id);
            Assert.NotNull(descriptor);
            Assert.False(descriptor!.IsEnabled(), $"{id} must be DISABLED with no seam to service it.");
            Assert.NotNull(descriptor.DynamicDisplayName);
            Assert.Contains("unavailable", descriptor.DynamicDisplayName!(), StringComparison.Ordinal);
        }

        // ⭐ And the always-serviceable ones stay live — a blanket disable would be just as wrong.
        Assert.True(cmds.Get(ScenarioMenuCommands.NewExerciseId)!.IsEnabled());
        Assert.True(cmds.Get(ScenarioMenuCommands.NewId)!.IsEnabled());
        Assert.True(cmds.Get(ScenarioMenuCommands.TakeCheckpointId)!.IsEnabled());
    }

    /// <summary>
    /// ⭐ A SERVICEABLE item carries NO dynamic label, so nothing about its rendering changes.
    /// ⚠ Worth pinning: the cheap way to write <see cref="ScenarioMenuCommands"/>'s VC-3 helper would have
    /// been to always return a label, which would silently take over every item's display name.
    /// </summary>
    [Fact]
    public void WithSeamsSupplied_TheItemsCarryNoOverrideLabel()
    {
        var (_, commands, _) = CreateRegistrar(
            openPicker:       (k, cb) => { },
            openSaveAsDialog: cb => { });

        Assert.Null(commands.Get(ScenarioMenuCommands.LoadId)!.DynamicDisplayName);
        Assert.Null(commands.Get(ScenarioMenuCommands.LoadLiveId)!.DynamicDisplayName);
        Assert.Null(commands.Get(ScenarioMenuCommands.SaveAsId)!.DynamicDisplayName);
    }

    [Fact]
    public void EveryAlwaysServiceableCommand_IsEnabled_WhenScenarioLoaded()
    {
        var (_, commands, _) = CreateRegistrar(loadedScenarioName: "LoadedScenario");

        Assert.True(commands.Get(ScenarioMenuCommands.NewId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.NewExerciseId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.LoadId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.LoadLiveId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.SaveId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.SaveAsId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.MigrationHistoryId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.TakeCheckpointId)!.IsEnabled());
    }

    // ══ ⑦ Migration history ════════════════════════════════════════════════

    [Fact]
    public void MigrationHistory_WhenScenarioLoaded_ListsSidecars()
    {
        var sidecars = new List<SidecarFileInfo>
        {
            new("snap_v1.hash", SidecarKind.Snapshot, 1, "abc123"),
            new("journal_v2.hash", SidecarKind.Journal, 2, "def456"),
        };

        IReadOnlyList<SidecarFileInfo>? capturedSidecars = null;
        var session = new FakeScenarioSession
        {
            LoadedScenarioNameValue = "MyScenario",
            MigrationSidecars       = sidecars,
        };

        var (_, commands, _) = CreateRegistrar(
            session:              session,
            showMigrationHistory: list => capturedSidecars = list);

        var result = commands.Invoke(ScenarioMenuCommands.MigrationHistoryId);
        Assert.True(result.Success);
        Assert.NotNull(capturedSidecars);
        Assert.Equal(2, capturedSidecars!.Count);
        Assert.Equal("snap_v1.hash", capturedSidecars[0].FileName);
        Assert.Equal(SidecarKind.Snapshot, capturedSidecars[0].Kind);
    }

    /// <summary>⭐ A null history seam must not throw — the item is registered either way.</summary>
    [Fact]
    public void MigrationHistory_WhenSeamIsNull_DoesNotThrow()
    {
        var session = new FakeScenarioSession
        {
            LoadedScenarioNameValue = "HasScenario",
            MigrationSidecars       = new[] { new SidecarFileInfo("f", SidecarKind.Snapshot, 1, "h") },
        };
        var (_, commands, _) = CreateRegistrar(session: session, showMigrationHistory: null);

        var result = commands.Invoke(ScenarioMenuCommands.MigrationHistoryId);
        Assert.True(result.Success);
    }

    // ══ ⑧ The menu leaf actually invokes the command ════════════════════════

    [Fact]
    public void New_MenuItem_OnClick_InvokesCommand()
    {
        var (session, _, menu) = CreateRegistrar();

        Assert.True(menu.Root.Children.TryGetValue("File", out var fileNode));
        Assert.True(fileNode.Children.TryGetValue("Edit", out var editNode));
        Assert.True(editNode.Children.TryGetValue("New Scenario", out var leaf));
        Assert.NotNull(leaf.OnClick);

        leaf.OnClick();
        Assert.Equal(1, session.ClearWorldCallCount);
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
        var catalog = new FakeAssetCatalog();
        var icons   = new FakeIconProvider();
        var modal   = new AssetPickerModal(catalog, icons);

        var (_, commands, _) = CreateRegistrar(
            openPicker: (kinds, cb) => modal.Open(
                new AssetBrowserPanelOptions
                {
                    Kinds      = kinds,
                    ShowAllTab = kinds == AssetKindFilter.All,
                },
                cb));

        Assert.False(modal.IsOpen);

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.True(modal.IsOpen, "Expected modal.IsOpen = true after scenario.load invoked.");

        Assert.NotNull(modal.Panel);
        Assert.Contains(AssetKind.Scenario, modal.Panel!.Tabs);
    }

    /// <summary>
    /// BATCH-26: When the scenario picker is cancelled (callback(null)),
    /// the modal closes and no load is requested.
    /// </summary>
    [Fact]
    public void Load_UnifiedModal_Cancel_DoesNotLoad()
    {
        var catalog = new FakeAssetCatalog();
        var icons   = new FakeIconProvider();
        var modal   = new AssetPickerModal(catalog, icons);

        var (session, commands, _) = CreateRegistrar(
            openPicker: (kinds, cb) =>
            {
                modal.Open(new AssetBrowserPanelOptions { Kinds = kinds, ShowAllTab = false }, cb);
                // Simulate user cancelling via Esc / X button.
                modal.HandleCancel();
            });

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);

        Assert.False(modal.IsOpen);
        Assert.Empty(session.OpenForEditCalls);
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
