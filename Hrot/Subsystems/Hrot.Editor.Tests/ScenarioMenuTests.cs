using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using System.Linq;
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
/// <para>⭐⭐ <b>CE-1443 (U2a, AQ108 §2.15a) — the submenus are GONE again.</b> The File menu is ONE flat list (<c>FileMenuLayout</c>): <i>Load Scenario… · New Scenario… · Load Checkpoint… · Open Exercise Recording… · Save Checkpoint · the editing group · Close · Exit</i>;
/// <i>New Exercise</i> is replaced by <i>Close</i> and its confirmation controller is deleted. The rails below were rewritten to that contract (the old assertions are named in the report).</para>
///
/// <para>⚠⚠ <b>(history) THE LABEL/PATH CHANGE WAS DELIBERATE AND VISIBLE — argued, not slipped in.</b> The old flat
/// <c>File/Scenario/*</c> group went; items then lived under <c>File/Live</c>, <c>File/Edit</c> and
/// <c>File/Checkpoint</c> (⛔ superseded by CE-1443). 📐 The old <c>Load Scenario…</c> was the chameleon R2 names: it was labelled
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
        public int CloseCallCount;
        public ClusterState State = ClusterState.Idle;
        public readonly List<string> LoadScenarioCalls = new();
        public readonly List<string> RestoreCheckpointCalls = new();
        public int SaveCurrentCallCount;
        public int TakeCheckpointCallCount;
        public readonly List<string> SaveAsCalls      = new();
        public readonly List<string> SaveToCalls      = new();
        public readonly List<string> OpenForEditCalls = new();
        public readonly List<string> LoadForLiveCalls = new();
        public string? LoadedScenarioNameValue;

        public IReadOnlyList<SidecarFileInfo> MigrationSidecars { get; set; } = Array.Empty<SidecarFileInfo>();

        public ClusterState CurrentClusterState => State;
        public string? LoadedScenarioName => LoadedScenarioNameValue;
        public bool IsDegraded => false;

        public void Update()                        => UpdateCallCount++;
        public void ClearWorld()                    => ClearWorldCallCount++;
        public void Close()                         => CloseCallCount++;
        public void LoadScenario(string name)       => LoadScenarioCalls.Add(name);
        public void RestoreCheckpoint(string label) => RestoreCheckpointCalls.Add(label);
        public void LoadForLive(string name)        => LoadForLiveCalls.Add(name);
        public void OpenForEdit(string name)        => OpenForEditCalls.Add(name);
        public void SaveCurrent()                   => SaveCurrentCallCount++;
        public void SaveAs(string name)             => SaveAsCalls.Add(name);
        public void SaveTo(string filePath)         => SaveToCalls.Add(filePath);
        public void TakeCheckpoint()                => TakeCheckpointCallCount++;
        public readonly List<Guid> OpenRecordingCalls = new();
        public void OpenRecording(Guid exerciseId)  => OpenRecordingCalls.Add(exerciseId);

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
            showMigrationHistory: showMigrationHistory);

        return (s, commands, menu);
    }

    // ══ ① The ONE flat File menu (AQ108 §2.15 / §2.15a) ═════════════════════

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> S1 — <b>the File menu this registrar draws has NO submenus</b>: the items it registers are exactly §2.15's, flat under <c>File</c>, and <c>File/Live</c> · <c>File/Edit</c> · <c>File/Checkpoint</c> · <i>New Exercise</i> are gone.
    /// 🔴 Red-proof: re-registering any item under a submenu (e.g. <c>File/Edit/Save Scenario</c>) ⇒ the submenu assertion; re-adding <c>New Exercise</c> ⇒ the exact-set assertion.
    /// </summary>
    [Fact]
    public void CE1443_S1_TheFileMenu_IsFlat_AndExactlyTheDesignedItems()
    {
        var (_, _, menu) = CreateRegistrar();
        var file = menu.Root.Children["File"];

        Assert.All(file.Children.Values, c => Assert.Empty(c.Children));   // no submenu anywhere under File
        foreach (var gone in new[] { "Live", "Edit", "Checkpoint", "Scenario", "Terrain…", "New Exercise", "Open Scenario", "New Scenario", "Open Recording", "Take Checkpoint" })
            Assert.False(file.Children.ContainsKey(gone), $"File/{gone} must be gone");

        // the items THIS registrar owns (the asset items, Save / Save As… / Save All and Exit are other registrars')
        var mine = new[]
        {
            FileMenuLayout.LoadScenario, FileMenuLayout.NewScenario, FileMenuLayout.LoadCheckpoint, FileMenuLayout.OpenExerciseRecording, FileMenuLayout.SaveCheckpoint,
            FileMenuLayout.SaveScenario, FileMenuLayout.SaveScenarioAs, FileMenuLayout.SaveScenarioAsRecipe, FileMenuLayout.MigrationHistory, FileMenuLayout.SaveCuratedToGit, FileMenuLayout.Close,
        };
        var registered = LeafPaths(menu).Where(x => !x.Contains("/sep-")).ToArray();
        Assert.Equal(mine.OrderBy(x => x, StringComparer.Ordinal), registered.OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>The leaf paths of the <c>File</c> menu in DRAW order (<see cref="MenuItemNode.OrderedChildren"/>), whatever their visibility.</summary>
    private static string[] LeafPaths(GlobalMenuRegistry menu)
        => menu.Root.Children["File"].OrderedChildren().Where(c => c.Children.Count == 0).Select(c => "File/" + c.Name).ToArray();

    /// <summary>⭐ <c>CE-1443</c> — the draw order is the layout table's (rank), not the registration order: registered here in a different order, drawn as <see cref="FileMenuLayout.InOrder"/> says.</summary>
    [Fact]
    public void CE1443_TheFileMenu_IsDrawnInTheLayoutTablesOrder()
    {
        var (_, _, menu) = CreateRegistrar();
        var drawn = LeafPaths(menu);
        var expected = FileMenuLayout.InOrder.Where(drawn.Contains).ToArray();
        Assert.Equal(expected, drawn);
        Assert.Equal(FileMenuLayout.LoadScenario, drawn[0]);
        Assert.Equal(FileMenuLayout.Close, drawn[^1]);   // (Exit is the window controller's own registration)
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

        Assert.Equal(11, commands.RegisterCallCount);   // ⭐ CE-1443 — New Exercise is gone; Load Checkpoint, Close and (B2) Save Scenario as Recipe are new
        Assert.NotNull(commands.Get(ScenarioMenuCommands.OpenRecordingId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.NewId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.LoadCheckpointId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.CloseId));
        Assert.Null(commands.Get("scenario.newExercise"));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.LoadId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveAsId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.SaveAsRecipeId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.MigrationHistoryId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.TakeCheckpointId));
        Assert.NotNull(commands.Get(ScenarioMenuCommands.UpdateCuratedId));
    }

    // ══ ② THE ROUTING RAIL — Load Scenario is the ONE load (R-1043) ════════

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> S3 (R-1043) — <b>Load Scenario… ends in the session's ONE <c>LoadScenario</c></b> (which resolves the cluster's armed mode): ⛔ not in <c>OpenForEdit</c>, not in <c>LoadForLive</c> — no host seam, no per-item mode.
    /// The picker is the scenario-filtered one; a cancelled pick loads nothing.
    /// 🔴 Red-proof: the handler calling <c>session.OpenForEdit</c> (the withdrawn "Open always opens into editing") ⇒ the empty-list assertions; calling <c>LoadForLive</c> ⇒ the same.
    /// </summary>
    [Fact]
    public void CE1443_S3_LoadScenario_EndsInTheSessionsOneLoad()
    {
        var (session, commands, _) = CreateRegistrar(
            openPicker: (kinds, cb) => cb(new FakeScenarioAsset("PickedScenario")));

        Assert.True(commands.Invoke(ScenarioMenuCommands.LoadId).Success);

        Assert.Equal("PickedScenario", Assert.Single(session.LoadScenarioCalls));
        Assert.Empty(session.OpenForEditCalls);                             // the menu names no mode: the armed mode decides, once, in the choice
        Assert.Empty(session.LoadForLiveCalls);
        Assert.Null(commands.Get("scenario.loadLive"));
        Assert.Equal("File/Load Scenario…", ScenarioMenuCommands.LoadScenarioMenuPath);
    }

    [Fact]
    public void Load_OpensScenarioFilteredModal_AndLoadsThePick()
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
        Assert.Equal("PickedScenario", Assert.Single(session.LoadScenarioCalls));   // ⭐ CE-1443: the one load, not OpenForEdit
    }

    [Fact]
    public void Load_PickerCancelled_DoesNotLoad()
    {
        var (session, commands, _) = CreateRegistrar(openPicker: (kinds, cb) => cb(null));

        var result = commands.Invoke(ScenarioMenuCommands.LoadId);
        Assert.True(result.Success);
        Assert.Empty(session.LoadScenarioCalls);
        Assert.Empty(session.OpenForEditCalls);
        Assert.Empty(session.LoadForLiveCalls);
    }

    // ══ ③ CLOSE — back to Idle through the one door (replaces New Exercise) ═══

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> S6 — <b>Close calls the session's ONE <c>Close</c></b> (= <c>Door.Leave(LeaveReason.Close)</c>), is NEVER gated (enabled while the run gate is closed and the cluster is in error — it is how an operator leaves) and is offered on Start;
    /// and it is not New Scenario (the LOCAL wipe stays apart).
    /// 🔴 Red-proofs: the handler calling <c>ClearWorld</c> ⇒ the count; registering Close with the gate's <c>closedReason</c> ⇒ the closed-gate assertions; <c>allowedInIdle</c> false ⇒ the Idle assertion.
    /// </summary>
    [Fact]
    public void CE1443_S6_Close_IsTheSessionsOneClose_NeverGated_AndOnStart()
    {
        var session = new FakeScenarioSession();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();
        string? closed = "waiting for: Brain (CGF)";
        ScenarioMenuCommands.Register((d, h) => commands.Register(d, h), menu, commands, session, (k, cb) => { }, cb => { }, closedReason: () => closed);

        Assert.True(commands.Invoke(ScenarioMenuCommands.CloseId).Success);       // the gate is CLOSED (and an error would close it the same way): Close still works
        Assert.Equal(1, session.CloseCallCount);
        Assert.Equal(0, session.ClearWorldCallCount);                              // not New Scenario: no local wipe
        Assert.Null(commands.Get(ScenarioMenuCommands.CloseId)!.DynamicDisplayName);   // no gate label on Close

        // while the gate is closed a GATED item is disabled with the reason — Close, by contrast, is the one that is not
        Assert.False(commands.Get(ScenarioMenuCommands.NewId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.CloseId)!.IsEnabled());

        Assert.Contains(menu.AllowedInIdleItems(), a => a.Path == FileMenuLayout.Close);   // offered on Start
    }

    /// <summary>
    /// ⚠⚠ <b>NEW SCENARIO IS NOT CLOSE, and this rail is why both ids exist</b> (it kept the New-Exercise rail's point). 📐 The deferred-load state machine calls the LOCAL wipe as step 1 of its own sequence, so if <c>scenario.new</c> had been pointed at the
    /// cluster-wide leave every load would publish a second <c>Idle</c> intent from inside the handler for the first. ⇒ two verbs, asserted apart.
    /// </summary>
    [Fact]
    public void NewScenarioClearsLocally_WhileCloseLeavesTheCluster()
    {
        var (session, commands, _) = CreateRegistrar();

        Assert.True(commands.Invoke(ScenarioMenuCommands.NewId).Success);
        Assert.Equal(1, session.ClearWorldCallCount);
        Assert.Equal(0, session.CloseCallCount);

        Assert.True(commands.Invoke(ScenarioMenuCommands.CloseId).Success);
        Assert.Equal(1, session.CloseCallCount);
        Assert.Equal(1, session.ClearWorldCallCount);   // Close did not wipe locally
    }

    // ══ ④ Checkpoint ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <c>CE-1443</c> — <b>Save Checkpoint</b> reaches the session's checkpoint verb when the host passes no run choice, and the host's own (the choice's, with its toast) when it does; its reason — the choice's — disables it with the words.
    /// ⭐ <b>Load Checkpoint…</b> is registered on every host: with a picker it hands the chosen label to the session's <c>RestoreCheckpoint</c> (the existing restore); with none (every host today — the live run's marks are not on the cluster view) it is DISABLED and says why.
    /// </summary>
    [Fact]
    public void CE1443_SaveCheckpoint_And_LoadCheckpoint_ReachTheSession()
    {
        var (session, commands, _) = CreateRegistrar();
        Assert.True(commands.Invoke(ScenarioMenuCommands.TakeCheckpointId).Success);
        Assert.Equal(1, session.TakeCheckpointCallCount);                          // no choice passed: the session's own

        var s2 = new FakeScenarioSession();
        var c2 = new RecordingCommandSet();
        var taken = 0;
        string? why = "this run was not recorded";
        ScenarioMenuCommands.Register((d, h) => c2.Register(d, h), new GlobalMenuRegistry(), c2, s2, (k, cb) => { }, cb => { },
            takeCheckpoint: () => taken++, checkpointDisabledReason: () => why,
            openCheckpointPicker: pick => pick("cp-1"));
        Assert.False(c2.Get(ScenarioMenuCommands.TakeCheckpointId)!.IsEnabled());          // the choice's reason greys it …
        Assert.Contains("this run was not recorded", c2.Get(ScenarioMenuCommands.TakeCheckpointId)!.DynamicDisplayName!());   // … and says it
        why = null;
        Assert.True(c2.Invoke(ScenarioMenuCommands.TakeCheckpointId).Success);
        Assert.Equal(1, taken);                                                    // the host's (the choice's) checkpoint, not the session's
        Assert.Equal(0, s2.TakeCheckpointCallCount);

        Assert.True(c2.Invoke(ScenarioMenuCommands.LoadCheckpointId).Success);
        Assert.Equal("cp-1", Assert.Single(s2.RestoreCheckpointCalls));            // the existing restore, by the picked label

        var none = commands.Get(ScenarioMenuCommands.LoadCheckpointId)!;
        Assert.False(none.IsEnabled());                                            // no picker on this host: registered, disabled …
        Assert.Contains("unavailable", none.DynamicDisplayName!());                // … and saying why (ruling 49)
    }

    // ══ ③a VISIBILITY — the editing group only while editing, Save Checkpoint only in a live run (S2) ═══

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1443</c> S2 — <b>the context groups are HIDDEN (not greyed) outside their state</b>, whoever registered the item and in whatever order: the asset items and Save / Save All are other registrars' (here registered BEFORE and AFTER the scenario
    /// registrar, like the shell toolbar and the editor), and the layout rule still hides them. The editing group draws ONLY in <c>OperatingEdit</c>; <i>Save Checkpoint</i> ONLY in <c>OperatingLive</c>; the first four, Close and Exit always; the separator
    /// before Close only when a context group above it is drawn. The published model (<c>BuildViewModel</c>) says the same, and on Start (the Idle-only bar) the surface offers exactly the first four + Close + Exit, in draw order.
    /// 🔴 Red-proofs: the editing rule dropped from <c>FileMenuLayout.Apply</c> ⇒ the Idle / Live rows draw the group; <c>SaveCheckpoint</c>'s rule dropped ⇒ the Edit row draws it; the separator's rule dropped ⇒ the Idle row draws a lone separator.
    /// </summary>
    /// <summary>
    /// ⭐ <c>CE-1443</c> part B (AQ108 §2.15 "Save names what it saves") — <c>File/Save</c> and <c>File/Save As…</c> (the shell's: a DOCUMENT's) are drawn only while a document is the save target; the scenario's own
    /// <i>Save Scenario 'x'</i> / <i>… As…</i> stay — so the menu never shows two items saving the scenario. A host with no document saver (the Orchestrator: null) never draws them. The scenario items NAME the scenario.
    /// 🔴 Mutation: the <c>Save or SaveAs</c> rule dropped from <c>FileMenuLayout.Apply</c> ⇒ File/Save drawn with no document ⇒ red.
    /// </summary>
    [Fact]
    public void CE1443_TheShellSave_IsDrawnOnlyForADocument_AndTheScenarioItemsNameTheScenario()
    {
        var session = new FakeScenarioSession { State = ClusterState.OperatingEdit };
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();
        menu.RegisterItem(FileMenuLayout.Save, () => { });
        menu.RegisterItem(FileMenuLayout.SaveAs, () => { });
        bool document = false;
        ScenarioMenuCommands.Register((d, h) => commands.Register(d, h), menu, commands, session, (k, cb) => { }, cb => { }, openRecordingPicker: () => { }, documentToSave: () => document);

        MenuItemNode Node(string path) => menu.Root.Children["File"].Children[path["File/".Length..]];
        bool Drawn(string path) => menu.Offers(Node(path), null);

        Assert.False(Drawn(FileMenuLayout.Save));
        Assert.False(Drawn(FileMenuLayout.SaveAs));
        Assert.True(Drawn(FileMenuLayout.SaveScenario));
        document = true;
        Assert.True(Drawn(FileMenuLayout.Save));
        Assert.True(Drawn(FileMenuLayout.SaveAs));

        session.LoadedScenarioNameValue = "hill-attack";
        Assert.Equal("Save Scenario 'hill-attack'", commands.Get(ScenarioMenuCommands.SaveId)!.DynamicDisplayName!());
        Assert.Equal("Save Scenario 'hill-attack' As…", commands.Get(ScenarioMenuCommands.SaveAsId)!.DynamicDisplayName!());

        // the Orchestrator: no document saver ⇒ the shell's items are never drawn
        var orchestratorMenu = new GlobalMenuRegistry();
        orchestratorMenu.RegisterItem(FileMenuLayout.Save, () => { });
        ScenarioMenuCommands.Register((d, h) => commands.Register(d, h), orchestratorMenu, commands, session, (k, cb) => { }, cb => { }, openRecordingPicker: () => { });
        Assert.False(orchestratorMenu.Offers(orchestratorMenu.Root.Children["File"].Children["Save"], null));
    }

    /// <summary>
    /// ⭐ CE-1443 B2 — <i>Save Scenario 'x' as Recipe…</i> is the scenario's Save As under the recipe name <c>recipes/scenarios/&lt;name&gt;</c> (which New Scenario re-reads), named in its label.
    /// 🔴 Mutation: the handler calling <c>session.SaveAs(name)</c> (no recipe name) ⇒ red.
    /// </summary>
    [Fact]
    public void CE1443_B2_SaveScenarioAsRecipe_SavesUnderTheRecipeName()
    {
        var session = new FakeScenarioSession { State = ClusterState.OperatingEdit, LoadedScenarioNameValue = "hill-attack" };
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();
        ScenarioMenuCommands.Register((d, h) => commands.Register(d, h), menu, commands, session, (k, cb) => { }, cb => cb("hill-template"), openRecordingPicker: () => { });

        var item = commands.Get(ScenarioMenuCommands.SaveAsRecipeId)!;
        Assert.Equal("Save Scenario 'hill-attack' as Recipe…", item.DynamicDisplayName!());
        commands.Invoke(ScenarioMenuCommands.SaveAsRecipeId);
        Assert.Equal("recipes/scenarios/hill-template", Assert.Single(session.SaveAsCalls));
    }

    [Fact]
    public void CE1443_S2_TheEditingGroup_And_SaveCheckpoint_AreDrawnOnlyInTheirState_AndStartShowsTheFirstFourPlusCloseAndExit()
    {
        var session = new FakeScenarioSession();
        var commands = new RecordingCommandSet();
        var menu = new GlobalMenuRegistry();

        // other registrars' items, BEFORE the scenario registrar (the shell toolbar) …
        foreach (var path in new[] { FileMenuLayout.NewAsset, FileMenuLayout.OpenAsset, FileMenuLayout.NewBehavior, FileMenuLayout.NewAction, FileMenuLayout.NewCondition, FileMenuLayout.Save })
            menu.RegisterItem(path, () => { });
        ScenarioMenuCommands.Register((d, h) => commands.Register(d, h), menu, commands, session, (k, cb) => { }, cb => { }, openRecordingPicker: () => { }, documentToSave: () => true);
        // … and AFTER it (the editor's Save As / Save All, the window controller's Exit)
        menu.RegisterItem(FileMenuLayout.SaveAs, () => { });
        menu.RegisterItem(FileMenuLayout.SaveAsRecipe, () => { });
        menu.RegisterItem(FileMenuLayout.SaveAll, () => { });
        menu.RegisterItem(FileMenuLayout.Exit, () => { }, allowedInIdle: true);

        MenuItemNode Node(string path) => menu.Root.Children["File"].Children[path["File/".Length..]];
        bool Drawn(string path) => menu.Offers(Node(path), null);

        var alwaysDrawn = new[] { FileMenuLayout.LoadScenario, FileMenuLayout.NewScenario, FileMenuLayout.LoadCheckpoint, FileMenuLayout.OpenExerciseRecording, FileMenuLayout.Close, FileMenuLayout.Exit, FileMenuLayout.SeparatorAfterLoads };
        foreach (var state in new[] { ClusterState.Idle, ClusterState.OperatingEdit, ClusterState.OperatingPreview, ClusterState.OperatingLive, ClusterState.OperatingReplay })
        {
            session.State = state;
            var editing = state == ClusterState.OperatingEdit;
            var live = state == ClusterState.OperatingLive;

            foreach (var path in alwaysDrawn) Assert.True(Drawn(path), $"{state}: {path} is always drawn");
            foreach (var path in FileMenuLayout.EditingGroup) Assert.Equal(editing, Drawn(path));
            Assert.Equal(live, Drawn(FileMenuLayout.SaveCheckpoint));
            Assert.Equal(editing || live, Drawn(FileMenuLayout.SeparatorBeforeClose));

            // the published model is the same truth (it reads the very Offers)
            var model = menu.BuildViewModel("Scenario").Items.ToDictionary(i => i.Path, i => i.Visible);
            foreach (var path in FileMenuLayout.EditingGroup) Assert.Equal(editing, model[path]);
            Assert.Equal(live, model[FileMenuLayout.SaveCheckpoint]);
        }

        // Start: the Idle-only bar — exactly the first four + Close + Exit, in the table's order
        session.State = ClusterState.Idle;
        menu.IdleOnlyWhen = () => true;
        var offered = menu.AllowedInIdleItems().Select(a => a.Path).ToArray();
        Assert.Equal(new[] { FileMenuLayout.LoadScenario, FileMenuLayout.NewScenario, FileMenuLayout.LoadCheckpoint, FileMenuLayout.OpenExerciseRecording, FileMenuLayout.Close, FileMenuLayout.Exit }, offered);
        foreach (var path in FileMenuLayout.EditingGroup.Append(FileMenuLayout.SaveCheckpoint)) Assert.False(Drawn(path));
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

        foreach (var id in new[] { ScenarioMenuCommands.LoadId,
                                   ScenarioMenuCommands.SaveAsId })
        {
            var descriptor = cmds.Get(id);
            Assert.NotNull(descriptor);
            Assert.False(descriptor!.IsEnabled(), $"{id} must be DISABLED with no seam to service it.");
            Assert.NotNull(descriptor.DynamicDisplayName);
            Assert.Contains("unavailable", descriptor.DynamicDisplayName!(), StringComparison.Ordinal);
        }

        // ⭐ And the always-serviceable ones stay live — a blanket disable would be just as wrong.
        Assert.True(cmds.Get(ScenarioMenuCommands.CloseId)!.IsEnabled());
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
        // ⭐ CE-1443 part B (AQ108 §2.15): a SAVE item always names what it saves — its label is the naming rule, not an "unavailable" override
        Assert.Equal("Save Scenario As…", commands.Get(ScenarioMenuCommands.SaveAsId)!.DynamicDisplayName!());
    }

    [Fact]
    public void EveryAlwaysServiceableCommand_IsEnabled_WhenScenarioLoaded()
    {
        var (_, commands, _) = CreateRegistrar(loadedScenarioName: "LoadedScenario");

        Assert.True(commands.Get(ScenarioMenuCommands.NewId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.CloseId)!.IsEnabled());
        Assert.True(commands.Get(ScenarioMenuCommands.LoadId)!.IsEnabled());
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
        Assert.True(fileNode.Children.TryGetValue("New Scenario…", out var leaf));   // ⭐ CE-1443: File/New Scenario…
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
        Assert.Empty(session.LoadScenarioCalls);
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
