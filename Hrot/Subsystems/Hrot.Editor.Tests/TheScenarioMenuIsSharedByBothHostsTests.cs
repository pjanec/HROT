using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.Core.Action;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-046</c> — THE CONFORMANCE CLAIM AT UNIT LEVEL: both hosts get the SAME scenario items
/// from the SAME registrar, and only their SERVICEABILITY differs.</b>
/// 📄 <c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c> §3 ④/⑤, §6; <c>AQ60</c> ruling <b>R2</b>
/// *(distinct items, no chameleons, <b>no per-host default in the menu</b>)*; ruling <b>49</b>
/// *(the menu shows every serviceable item — presence is not conditional)*; ruling <b>58</b>
/// *(one registration list, no host-private menu list)*.
///
/// <para>⭐⭐ <b>Why this rail exists when the E2E conformance suite already checks a subset.</b>
/// 📐 Measured <c>2026-08-26</c>: <c>ClusterConformanceRails</c>'s <c>global-menu</c>
/// <c>SubsetShape</c> is keyed by <c>path</c> and compares <c>visible</c>, so it needed <b>no
/// extension</b> for the new items — it generalises for free *(and that is the finding, not a gap)*.
/// ⛔ But a SUBSET check cannot fail when the two sets are supposed to be EQUAL: a CGF that registered
/// none of these items would still be *"a subset"*. ⇒ this rail asserts <b>EQUALITY of the item set</b>,
/// which is the actual design claim, and it does so headlessly in milliseconds rather than needing a
/// booted two-host cluster.</para>
///
/// <para>⚠ <b>What it deliberately does NOT assert:</b> that the real <c>CgfSubsystem</c> passes these
/// exact seams. That is composition, and only the E2E conformance suite sees it. ⭐ Stated rather than
/// glossed — this rail proves the REGISTRAR is host-agnostic, not that the host wired it.</para>
/// </summary>
public sealed class TheScenarioMenuIsSharedByBothHostsTests
{
    // ── The two host SHAPES, as seam sets ────────────────────────────────────

    private sealed class Session : IScenarioSession
    {
        public readonly List<string> OpenForEditCalls = new();
        public readonly List<string> LoadForLiveCalls = new();
        public int NewExerciseCallCount;
        public int TakeCheckpointCallCount;

        public ClusterState CurrentClusterState => ClusterState.Idle;
        public string? LoadedScenarioName => null;
        public bool IsDegraded => false;

        public void Update() { }
        public void ClearWorld() { }
        public void NewExercise() => NewExerciseCallCount++;
        public void LoadForLive(string n) => LoadForLiveCalls.Add(n);
        public void OpenForEdit(string n) => OpenForEditCalls.Add(n);
        public void SaveCurrent() { }
        public void SaveAs(string n) { }
        public void SaveTo(string p) { }
        public void TakeCheckpoint() => TakeCheckpointCallCount++;
        public readonly List<Guid> OpenRecordingCalls = new();
        public void OpenRecording(Guid exerciseId) => OpenRecordingCalls.Add(exerciseId);
        public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars() => Array.Empty<SidecarFileInfo>();
    }

    private sealed class Commands : IEditorCommands
    {
        private readonly Dictionary<string, (EditorCommandDescriptor D, Action<EditorCommandContext> A)> _c
            = new(StringComparer.Ordinal);

        public event Action<string>? AvailabilityChanged;

        public IReadOnlyList<EditorCommandDescriptor> All
        {
            get { var l = new List<EditorCommandDescriptor>(); foreach (var kv in _c) l.Add(kv.Value.D); return l; }
        }

        public IEnumerable<string> Ids => _c.Keys;

        public EditorCommandDescriptor? Get(string id) => _c.TryGetValue(id, out var c) ? c.D : null;

        public EditorCommandResult Invoke(string id, EditorCommandContext? ctx = null)
        {
            if (!_c.TryGetValue(id, out var c)) return new EditorCommandResult(false, "unknown");
            if (!c.D.IsEnabled()) return new EditorCommandResult(false, "not enabled");
            c.A(ctx ?? default);
            return new EditorCommandResult(true, null);
        }

        public void Register(EditorCommandDescriptor d, Action<EditorCommandContext> a) => _c[d.Id] = (d, a);
        public void NotifyAvailabilityChanged(string id) => AvailabilityChanged?.Invoke(id);
    }

    private sealed class Asset : IEditableAsset
    {
        public Asset(string name) { Name = name; }
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

    /// <summary>
    /// ⭐ <b>The EDITOR shape</b> — an interactive host: it has a picker, a Save-As dialog, and it PROMPTS
    /// before the destructive reset *(ruling 53 — the confirm belongs where the operator sits)*.
    /// </summary>
    private static (Session S, Commands C, GlobalMenuRegistry M, ConfirmPromptController P) EditorShape()
    {
        var s = new Session();
        var c = new Commands();
        var m = new GlobalMenuRegistry();
        var p = new ConfirmPromptController();

        ScenarioMenuCommands.Register(
            registerCommand:    (d, h) => c.Register(d, h),
            menu:               m,
            commands:           c,
            session:            s,
            openPicker:         (kinds, cb) => cb(new Asset("Picked")),
            openSaveAsDialog:   cb => cb("Named"),
            confirmNewExercise: run => p.Request("New Exercise", "sure?", "Yes", run));

        return (s, c, m, p);
    }

    /// <summary>
    /// ⭐ <b>The CGF shape</b> — ⭐⭐⭐ <b>as of <c>CE-049</c> (Axis-C E2) it HAS a picker and a Save-As
    /// browser.</b> 📄 <c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c> §3 ③.
    ///
    /// <para>⚠⚠ <b>This shape CHANGED, and the change is the E2 acceptance criterion.</b> Before E2 it
    /// passed <c>openPicker: null</c> / <c>openSaveAsDialog: null</c> and the rails asserted the items were
    /// DISABLED-with-cause — 📌 Slice A's own note said they *"light up for free the day a picker is
    /// composed here (Axis-C E2)"*. ⇒ ⭐ that day arrived, so the assertion flipped. ⛔ The greyed-with-cause
    /// behaviour is NOT gone — it moved to <see cref="NoModalShape"/>, which is now the host shape that
    /// exhibits it.</para>
    ///
    /// <para>⭐ It still LOGS instead of prompting for <c>New Exercise</c> — a picker is not a confirmation
    /// dialog, and ruling 53 is about the destructive reset, which stays headless-first here.</para>
    /// </summary>
    private static (Session S, Commands C, GlobalMenuRegistry M, List<string> Log) CgfShape()
    {
        var s   = new Session();
        var c   = new Commands();
        var m   = new GlobalMenuRegistry();
        var log = new List<string>();

        ScenarioMenuCommands.Register(
            registerCommand:    (d, h) => c.Register(d, h),
            menu:               m,
            commands:           c,
            session:            s,
            openPicker:         (kinds, cb) => cb(new Asset("PickedOnCgf")),
            openSaveAsDialog:   cb => cb("NamedOnCgf"),
            confirmNewExercise: run => { log.Add("new-exercise proceeding without a prompt"); run(); });

        return (s, c, m, log);
    }

    /// <summary>
    /// ⭐⭐ <b>The NO-MODAL shape — a host that genuinely cannot host a picker.</b>
    /// 🔒 Ruling 49 / <c>VC-3</c>: the items are still REGISTERED, but DISABLED with the cause in the
    /// label. 📄 The design's §2 risk names this as *"the correct end state, not a bug"*.
    ///
    /// <para>⭐ Kept as a first-class shape rather than deleted with the pre-E2 CGF wiring: it is the
    /// contract for any future host *(a thin ExCon node, a headless runner that still builds a menu)*, and
    /// deleting the rail would let that contract rot silently.</para>
    /// </summary>
    private static (Session S, Commands C, GlobalMenuRegistry M) NoModalShape()
    {
        var s = new Session();
        var c = new Commands();
        var m = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:  (d, h) => c.Register(d, h),
            menu:             m,
            commands:         c,
            session:          s,
            openPicker:       null,
            openSaveAsDialog: null);

        return (s, c, m);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4096</c> (AQ108 D16) — the SAME registrar marks the SAME scenario items allowed in Idle on both hosts (so the Start surface offers
    /// them on either), and the surface's button runs the menu's OWN binding: <c>Open Scenario</c> reaches <c>session.OpenForEdit</c>, <c>Load
    /// Scenario</c> reaches <c>LoadForLive</c>. ⛔ Save / Save As / Take Checkpoint / New Exercise are not offered in Idle.
    /// 🔴 Red-proof: drop <c>allowedInIdle: true</c> from <c>Open Scenario</c> ⇒ the set assertion fails.
    /// </summary>
    [Fact]
    public void CE4096_BothHostShapes_MarkTheSameScenarioItemsAllowedInIdle_AndTheSurfaceRunsTheMenusBinding()
    {
        var editor = EditorShape();
        var cgf = CgfShape();
        // ⭐ CE-4098 — "Open Recording (AAR)" joins them (D16): offered whether or not a host has an inventory (a host without one shows it greyed with its cause)
        var expected = new[] { "File/New Scenario", "File/Open Recording", "File/Open Scenario" };   // ⭐ CE-4097 part two: the Live/Edit load items fold into ONE Open

        foreach (var m in new[] { editor.M, cgf.M })
            Assert.Equal(expected, m.AllowedInIdleItems().Select(a => a.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());

        editor.M.AllowedInIdleItems().Single(a => a.Path == "File/Open Scenario").Invoke();
        Assert.Single(editor.S.OpenForEditCalls);
        cgf.M.AllowedInIdleItems().Single(a => a.Path == "File/Open Scenario").Invoke();
        Assert.Single(cgf.S.OpenForEditCalls);   // CGF opens INTO EDITING too; its armed mode (Live) decides ▶
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1439</c> (AQ108 §2.12) — <b>the THIRD operator host.</b> The REAL <c>OrchestratorSubsystem</c> registers the scenario menu through the same registrar: the SAME command ids and the SAME
    /// menu paths as the editor shape, and the SAME allowed-in-Idle items (so its Start surface offers what the editor's and CGF's do) — by construction, not by a copy of the list.
    /// 🔴 Mutation: the Orchestrator's <c>ComposeScenarioActions</c> registering nothing ⇒ the id set is empty; dropping <c>allowedInIdle</c> in the registrar ⇒ the Idle set differs (as for the other shapes).
    /// </summary>
    [Fact]
    public void CE1439_TheOrchestratorHost_RegistersTheSameItems_AsTheEditorShape_SameIdsPathsAndIdleFlags()
    {
        var editor = EditorShape();
        var host = new Hrot.Orchestrator.OrchestratorSubsystem();
        host.Initialize(new Fdp.Toolkit.Runner.SubsystemConfig { Headless = true, DomainId = 224 });
        try
        {
            var wm = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 16f, 16f));
            host.ComposeScenarioActions(wm);

            var ids = wm.ShellCommands.All.Where(d => d.Category == "Scenario").Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
            Assert.Equal(editor.C.Ids.OrderBy(i => i, StringComparer.Ordinal).ToArray(), ids);

            var orchestratorFilePaths = MenuPaths(wm.GlobalMenu).Where(p => p.StartsWith("File/", StringComparison.Ordinal)).ToArray();
            Assert.Equal(MenuPaths(editor.M), orchestratorFilePaths);

            var idle = wm.GlobalMenu.AllowedInIdleItems().Select(a => a.Path).Where(p => p.StartsWith("File/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.Equal(editor.M.AllowedInIdleItems().Select(a => a.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray(), idle);
        }
        finally
        {
            host.Shutdown();
        }
    }

    private static string[] MenuPaths(GlobalMenuRegistry menu)
    {
        var paths = new List<string>();
        Walk(menu.Root, "");
        paths.Sort(StringComparer.Ordinal);
        return paths.ToArray();

        void Walk(MenuItemNode node, string prefix)
        {
            foreach (var kv in node.Children)
            {
                var path = prefix.Length == 0 ? kv.Key : $"{prefix}/{kv.Key}";
                if (kv.Value.Children.Count == 0) paths.Add(path);
                else                              Walk(kv.Value, path);
            }
        }
    }

    // ══ ① THE ITEM SET IS IDENTICAL ═════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Both hosts get the SAME menu paths — no host-private list, no per-host default.</b>
    /// 🔒 R2 + ruling 58. ⛔ The failure this pins is the one the pre-slice code had: CGF registered
    /// NOTHING scenario-related, and the difference was invisible until someone opened its File menu.
    /// </summary>
    [Fact]
    public void BothHostsRegisterTheIdenticalMenuPaths()
    {
        var editor = EditorShape();
        var cgf    = CgfShape();

        Assert.Equal(MenuPaths(editor.M), MenuPaths(cgf.M));
    }

    /// <summary>⭐⭐ …and the identical command IDs, which is what the conformance rail compares by.</summary>
    [Fact]
    public void BothHostsRegisterTheIdenticalCommandIds()
    {
        var editor = EditorShape();
        var cgf    = CgfShape();

        Assert.Equal(
            editor.C.Ids.OrderBy(i => i, StringComparer.Ordinal).ToArray(),
            cgf.C.Ids.OrderBy(i => i, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// ⭐⭐ <b>The paths are the DESIGNED ones</b> — pinned literally, so a rename has to be deliberate.
    /// 📄 design §3a. ⚠ Pinned as a full set rather than per-item: the property that matters is *"these
    /// and no others"*, and a per-item test cannot catch an extra item appearing.
    /// </summary>
    [Fact]
    public void TheMenuPathsAreTheOnesTheDesignSpecifies()
    {
        var (_, _, menu, _) = EditorShape();

        Assert.Equal(new[]
        {
            "File/Checkpoint/Take Checkpoint",
            "File/Edit/Migration History",
            "File/Edit/Save Curated Scenarios to Git",
            "File/Edit/Save Scenario",
            "File/Edit/Save Scenario As",
            "File/Live/New Exercise",
            "File/New Scenario",     // ⭐ CE-4097 part two — Open / New are top-level; ⛔ no File/Live/Load Scenario: the armed mode decides ▶
            "File/Open Recording",   // ⭐ CE-4098
            "File/Open Scenario",
        }, MenuPaths(menu));
    }

    // ══ ⭐ CE-4098 (AQ108 D16/D19) — File › Open Recording ═══════════════════

    private static readonly Guid RecordingA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid RecordingB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    /// <summary>A host shape with a recording inventory: the REAL launcher over a fake picker, its opener the session's <c>OpenRecording</c>.</summary>
    private static (Session S, Commands C, GlobalMenuRegistry M, Func<Action<PickerResult>?> Pending, Func<PickerRequest?> Request)
        RecordingShape(Func<IReadOnlyList<ExerciseInventoryItem>> inventory)
    {
        var s = new Session();
        var c = new Commands();
        var m = new GlobalMenuRegistry();
        Action<PickerResult>? pending = null;
        PickerRequest? request = null;
        var launcher = new RecordingPickerLauncher((r, cb) => { request = r; pending = cb; }, inventory, s.OpenRecording);

        ScenarioMenuCommands.Register(
            registerCommand:     (d, h) => c.Register(d, h),
            menu:                m,
            commands:            c,
            session:             s,
            openPicker:          (kinds, cb) => cb(new Asset("Picked")),
            openSaveAsDialog:    cb => cb("Named"),
            openRecordingPicker: launcher.Open);

        return (s, c, m, () => pending, () => request);
    }

    /// <summary>
    /// ⭐⭐ CE-4098 success 4 — File › Open Recording lists the master's exercises (the picker's entries are the inventory) and a pick goes through
    /// the session's ONE opener (<c>IScenarioSession.OpenRecording</c> → <c>ReplaySessionIntents.EnterReplay</c>) exactly once with the picked exercise
    /// id; it is allowed in Idle, so the Start screen's model offers it and its button runs the SAME binding.
    /// 🔴 Red-proof: drop <c>allowedInIdle: true</c> from the Open Recording registration ⇒ the Start-model assertion fails.
    /// </summary>
    [Fact]
    public void CE4098_OpenRecording_ListsTheMastersExercises_OpensThePickThroughTheOneOpener_AndShowsOnTheStartScreen()
    {
        var inv = new[]
        {
            new ExerciseInventoryItem(RecordingA, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(3), "alpha"),
            new ExerciseInventoryItem(RecordingB, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(4), "beta"),
        };
        var (session, commands, menu, pending, request) = RecordingShape(() => inv);

        // on the Start screen's model, and the button is the menu's own binding
        var idle = menu.AllowedInIdleItems().Single(a => a.Path == ScenarioMenuCommands.OpenRecordingMenuPath);
        Assert.Contains(menu.AllowedInIdleItems(), a => a.Path == "File/Open Recording");
        Assert.True(commands.Get(ScenarioMenuCommands.OpenRecordingId)!.IsEnabled());
        Assert.Null(commands.Get(ScenarioMenuCommands.OpenRecordingId)!.DynamicDisplayName);

        idle.Invoke();   // the Start button
        Assert.NotNull(request());
        var listed = request()!.ItemsProvider().ToList();
        Assert.Equal(new[] { RecordingB, RecordingA }.Select(AssetRecents.RecordingKey), listed.Select(e => e.Id));   // the master's exercises, newest first

        pending()!(new PickerResult(new[] { listed[1] }));   // the user picks alpha
        Assert.Equal(new[] { RecordingA }, session.OpenRecordingCalls);

        pending()!(new PickerResult(Array.Empty<PickerEntry>()));   // a cancel opens nothing more
        Assert.Single(session.OpenRecordingCalls);
    }

    /// <summary>
    /// ⭐ CE-4098 — a host with NO recording inventory still carries the item (ruling 49), greyed with its cause, and it is still on the Idle list:
    /// the two host families agree on presence; only serviceability differs.
    /// </summary>
    [Fact]
    public void CE4098_AHostWithNoRecordingInventory_KeepsTheItemGreyedWithItsCause()
    {
        var (_, commands, menu, _) = CgfShape();   // CgfShape passes no openRecordingPicker

        var d = commands.Get(ScenarioMenuCommands.OpenRecordingId)!;
        Assert.False(d.IsEnabled());
        Assert.Contains("no recording inventory", d.DynamicDisplayName!());
        Assert.Contains(menu.AllowedInIdleItems(), a => a.Path == "File/Open Recording");
    }

    // ══ ② ONLY SERVICEABILITY DIFFERS ═══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-049</c>'s ACCEPTANCE CRITERION: after E2 the two hosts are IDENTICAL — same items,
    /// same enablement, same plain labels.</b> 📄 <c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c> §6
    /// *("CGF's Open/New items ENABLED and functional")*.
    ///
    /// <para>⚠⚠ <b>This rail INVERTED at E2, deliberately.</b> Before the picker was composed it asserted
    /// CGF's three seam-backed items were DISABLED-with-cause; that was correct then and is wrong now.
    /// ⛔ The greyed behaviour was not deleted — <see cref="AHostWithNoModalKeepsTheItemsGreyedWithCause"/>
    /// owns it, which is the honest place for it: it is a property of *"no modal composed"*, ⛔ never of
    /// *"being CGF"*.</para>
    /// </summary>
    [Fact]
    public void AfterE2TheTwoHostsAgreeOnEnablementToo()
    {
        var editor = EditorShape();
        var cgf    = CgfShape();

        foreach (var id in new[] { ScenarioMenuCommands.LoadId,
                                   ScenarioMenuCommands.SaveAsId })
        {
            Assert.True(editor.C.Get(id)!.IsEnabled(), $"{id} must be live on the interactive host.");
            Assert.Null(editor.C.Get(id)!.DynamicDisplayName);

            Assert.True(cgf.C.Get(id)!.IsEnabled(), $"{id} must be live on CGF now that E2 composed a picker.");
            Assert.Null(cgf.C.Get(id)!.DynamicDisplayName);
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>…and CGF's picker actually REACHES the session — enabled is not the same as functional.</b>
    /// ⚠ The failure this pins is the one that matters after E2: a menu item that is live, clickable, and
    /// silently does nothing because the seam was wired to a picker nobody draws. 📐 The production half of
    /// that hazard is the two <c>DrawFrame</c> calls in <c>CgfSubsystem.DrawUI</c>; this rail pins the
    /// seam→session half, which is the part a unit test can see.
    /// </summary>
    [Fact]
    public void OnCgfTheEnabledLoadItemsReachTheSession()
    {
        var (session, commands, _, _) = CgfShape();

        Assert.True(commands.Invoke(ScenarioMenuCommands.LoadId).Success);

        Assert.Equal("PickedOnCgf", Assert.Single(session.OpenForEditCalls));
    }

    /// <summary>
    /// ⭐⭐ <b>A host that composes NO modal keeps the items registered, disabled, and self-explaining.</b>
    /// 🔒 Ruling 49 + <c>VC-3</c>. ⭐ The design's §2 risk states this explicitly: *"If a host genuinely
    /// cannot host a modal, the items stay greyed-with-cause — that is the correct end state, a finding,
    /// not a failure to force."*
    /// </summary>
    [Fact]
    public void AHostWithNoModalKeepsTheItemsGreyedWithCause()
    {
        var editor  = EditorShape();
        var noModal = NoModalShape();

        // ⭐ Presence is identical — the capability difference never removes an item.
        Assert.Equal(MenuPaths(editor.M), MenuPaths(noModal.M));

        foreach (var id in new[] { ScenarioMenuCommands.LoadId,
                                   ScenarioMenuCommands.SaveAsId })
        {
            Assert.False(noModal.C.Get(id)!.IsEnabled(), $"{id} has no seam to service it.");
            Assert.Contains("unavailable", noModal.C.Get(id)!.DynamicDisplayName!(), StringComparison.Ordinal);
        }

        // ⭐ And the always-serviceable items stay live — a blanket disable would be just as wrong.
        Assert.True(noModal.C.Get(ScenarioMenuCommands.NewExerciseId)!.IsEnabled());
        Assert.True(noModal.C.Get(ScenarioMenuCommands.TakeCheckpointId)!.IsEnabled());
    }

    /// <summary>
    /// ⭐⭐ <b>The host-agnostic items behave IDENTICALLY on both</b> — same click, same session verb.
    /// ⚠ This is the half that makes the equality meaningful: identical paths with different meanings
    /// would be the chameleon R2 forbids, wearing a shared name.
    /// </summary>
    [Fact]
    public void TheHostAgnosticItemsReachTheSameSessionVerbsOnBothHosts()
    {
        var editor = EditorShape();
        var cgf    = CgfShape();

        Assert.True(editor.C.Invoke(ScenarioMenuCommands.TakeCheckpointId).Success);
        Assert.True(cgf.C.Invoke(ScenarioMenuCommands.TakeCheckpointId).Success);
        Assert.Equal(1, editor.S.TakeCheckpointCallCount);
        Assert.Equal(1, cgf.S.TakeCheckpointCallCount);

        // ⭐ New Exercise reaches the same verb on both — the editor via a resolved prompt, CGF directly.
        Assert.True(editor.C.Invoke(ScenarioMenuCommands.NewExerciseId).Success);
        Assert.Equal(0, editor.S.NewExerciseCallCount);   // still behind the prompt
        editor.P.ResolveConfirm();
        Assert.Equal(1, editor.S.NewExerciseCallCount);

        Assert.True(cgf.C.Invoke(ScenarioMenuCommands.NewExerciseId).Success);
        Assert.Equal(1, cgf.S.NewExerciseCallCount);
        Assert.Single(cgf.Log);                            // ⭐ and the log IS the safety net
    }

    /// <summary>
    /// ⛔⛔ <b>A no-modal host's disabled load items must be INERT, not merely greyed.</b>
    /// ⚠ Worth its own rail: the handler closes over a nullable seam, and an early-return that was
    /// forgotten would let a re-entrant invoke path load a scenario the operator never chose.
    /// </summary>
    [Fact]
    public void OnANoModalHostTheDisabledLoadItemsDoNothingEvenIfInvoked()
    {
        var (session, commands, _) = NoModalShape();

        // ⭐ Invoke() itself refuses a disabled command, so this asserts BOTH gates: the refusal AND that
        //   nothing reached the session.
        Assert.False(commands.Invoke(ScenarioMenuCommands.LoadId).Success);
        Assert.Empty(session.OpenForEditCalls);
        Assert.Empty(session.LoadForLiveCalls);
    }
}
