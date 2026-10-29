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
        public readonly List<string> LoadScenarioCalls = new();
        public int CloseCallCount;
        public int TakeCheckpointCallCount;

        public ClusterState CurrentClusterState => ClusterState.Idle;
        public string? LoadedScenarioName => null;
        public bool IsDegraded => false;

        public void Update() { }
        public void ClearWorld() { }
        public void Close() => CloseCallCount++;
        public void LoadScenario(string n) => LoadScenarioCalls.Add(n);
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
    /// ⭐ <b>The EDITOR shape</b> — an interactive host: it has a picker and a Save-As dialog. (⭐ CE-1443: New Exercise and its confirmation are gone; Close asks through the door.)
    /// </summary>
    private static (Session S, Commands C, GlobalMenuRegistry M) EditorShape()
    {
        var s = new Session();
        var c = new Commands();
        var m = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (d, h) => c.Register(d, h),
            menu:               m,
            commands:           c,
            session:            s,
            openPicker:         (kinds, cb) => cb(new Asset("Picked")),
            openSaveAsDialog:   cb => cb("Named"));

        return (s, c, m);
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
    /// </summary>
    private static (Session S, Commands C, GlobalMenuRegistry M) CgfShape()
    {
        var s   = new Session();
        var c   = new Commands();
        var m   = new GlobalMenuRegistry();

        ScenarioMenuCommands.Register(
            registerCommand:    (d, h) => c.Register(d, h),
            menu:               m,
            commands:           c,
            session:            s,
            openPicker:         (kinds, cb) => cb(new Asset("PickedOnCgf")),
            openSaveAsDialog:   cb => cb("NamedOnCgf"));

        return (s, c, m);
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
    /// ⭐⭐ <c>CE-4096</c> / <c>CE-1443</c> S2 (AQ108 D16, §2.15) — the SAME registrar marks the SAME items allowed in Idle on both hosts (so the Start surface offers them on either): <i>Load Scenario… · New Scenario… · Load Checkpoint… · Open Exercise Recording… · Close</i>
    /// (Exit is the window controller's); the surface's button runs the menu's OWN binding: <i>Load Scenario…</i> reaches <c>session.LoadScenario</c> (the ONE load — the armed mode decides, R-1043), on CGF too.
    /// ⛔ Save Checkpoint · Save / Save As · Migration History · the curated copy are not offered in Idle.
    /// 🔴 Red-proof: drop <c>allowedInIdle: true</c> from Load Scenario… ⇒ the set assertion fails.
    /// </summary>
    [Fact]
    public void CE4096_BothHostShapes_MarkTheSameScenarioItemsAllowedInIdle_AndTheSurfaceRunsTheMenusBinding()
    {
        var editor = EditorShape();
        var cgf = CgfShape();
        var expected = new[] { FileMenuLayout.Close, FileMenuLayout.LoadCheckpoint, FileMenuLayout.LoadScenario, FileMenuLayout.NewScenario, FileMenuLayout.OpenExerciseRecording }
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        foreach (var m in new[] { editor.M, cgf.M })
            Assert.Equal(expected, m.AllowedInIdleItems().Select(a => a.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray());

        editor.M.AllowedInIdleItems().Single(a => a.Path == FileMenuLayout.LoadScenario).Invoke();
        Assert.Equal("Picked", Assert.Single(editor.S.LoadScenarioCalls));
        Assert.Empty(editor.S.OpenForEditCalls);
        cgf.M.AllowedInIdleItems().Single(a => a.Path == FileMenuLayout.LoadScenario).Invoke();
        Assert.Equal("PickedOnCgf", Assert.Single(cgf.S.LoadScenarioCalls));   // CGF loads into the CLUSTER's armed mode too — no host decides
        Assert.Empty(cgf.S.OpenForEditCalls);
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
    /// ⭐⭐ <c>CE-1443</c> S1 — <b>the paths are the DESIGNED ones</b> (AQ108 §2.15), pinned literally from the layout table, so a rename has to be deliberate: the leaves this registrar owns, flat under <c>File</c>, in the table's DRAW order.
    /// ⚠ Pinned as a full set rather than per-item: the property that matters is *"these and no others"* — ⛔ no <c>File/Live</c>, <c>File/Edit</c>, <c>File/Checkpoint</c>, <i>New Exercise</i>.
    /// </summary>
    [Fact]
    public void TheMenuPathsAreTheOnesTheDesignSpecifies()
    {
        var (_, _, menu) = EditorShape();

        var mine = new[]
        {
            FileMenuLayout.LoadScenario, FileMenuLayout.NewScenario, FileMenuLayout.LoadCheckpoint, FileMenuLayout.OpenExerciseRecording, FileMenuLayout.SeparatorAfterLoads, FileMenuLayout.SaveCheckpoint,
            FileMenuLayout.SaveScenario, FileMenuLayout.SaveScenarioAs, FileMenuLayout.MigrationHistory, FileMenuLayout.SaveCuratedToGit, FileMenuLayout.SeparatorBeforeClose, FileMenuLayout.Close,
        };
        Assert.Equal(mine.OrderBy(p => p, StringComparer.Ordinal).ToArray(), MenuPaths(menu));

        // the DRAW order is the table's, whatever the registration order was
        var drawn = menu.Root.Children["File"].OrderedChildren().Select(c => "File/" + c.Name).ToArray();
        Assert.Equal(FileMenuLayout.InOrder.Where(drawn.Contains).ToArray(), drawn);
        Assert.Equal(mine, drawn);
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
        Assert.Contains(menu.AllowedInIdleItems(), a => a.Path == "File/Open Exercise Recording…");
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
        var (_, commands, menu) = CgfShape();   // CgfShape passes no openRecordingPicker

        var d = commands.Get(ScenarioMenuCommands.OpenRecordingId)!;
        Assert.False(d.IsEnabled());
        Assert.Contains("no recording inventory", d.DynamicDisplayName!());
        Assert.Contains(menu.AllowedInIdleItems(), a => a.Path == "File/Open Exercise Recording…");
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
        var (session, commands, _) = CgfShape();

        Assert.True(commands.Invoke(ScenarioMenuCommands.LoadId).Success);

        Assert.Equal("PickedOnCgf", Assert.Single(session.LoadScenarioCalls));   // ⭐ CE-1443: the session's ONE load
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
        Assert.True(noModal.C.Get(ScenarioMenuCommands.CloseId)!.IsEnabled());
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

        // ⭐ CE-1443 — Close reaches the same verb on both (the door asks, on every host the same way; no per-host confirmation seam is left)
        Assert.True(editor.C.Invoke(ScenarioMenuCommands.CloseId).Success);
        Assert.Equal(1, editor.S.CloseCallCount);
        Assert.True(cgf.C.Invoke(ScenarioMenuCommands.CloseId).Success);
        Assert.Equal(1, cgf.S.CloseCallCount);
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
        Assert.Empty(session.LoadScenarioCalls);
        Assert.Empty(session.OpenForEditCalls);
        Assert.Empty(session.LoadForLiveCalls);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1442</c> (U1, S7; §11.7 ②) — the run gate reaches EVERY scenario command through the ONE registration helper: while the gate is closed a command registered through <c>RegisterCommand</c> reports
    /// <c>IsEnabled = false</c> and a label carrying the reason, and the Start surface's idle action for it (which reads the same menu node) is disabled with that label; invoking it does nothing. Close (a LEAVE) is never gated.
    /// Open again ⇒ plain labels, enabled. 🔴 Mutations: <c>RegisterCommand</c> without the <c>closedReason</c> wrap ⇒ the enabled / label cells red; the Open Recording call passing <c>null</c> instead of <c>closedReason</c> ⇒ its row reds;
    /// the Close call passing <c>closedReason</c> ⇒ the leave row reds.
    /// </summary>
    [Fact]
    public void CE1442_AClosedRunGate_GreysEveryScenarioCommand_AndTheStartSurfacesIdleAction_WithTheReason()
    {
        var s = new Session();
        var c = new Commands();
        var m = new GlobalMenuRegistry();
        string? closed = "the cluster is not ready — waiting for: Brain (CGF)";
        ScenarioMenuCommands.Register(
            registerCommand:     (d, h) => c.Register(d, h),
            menu:                m,
            commands:            c,
            session:             s,
            openPicker:          (kinds, cb) => cb(new Asset("Picked")),
            openSaveAsDialog:    cb => cb("Named"),
            openRecordingPicker: () => { },
            closedReason:        () => closed);

        foreach (var id in new[] { ScenarioMenuCommands.LoadId, ScenarioMenuCommands.NewId, ScenarioMenuCommands.OpenRecordingId, ScenarioMenuCommands.LoadCheckpointId, ScenarioMenuCommands.SaveAsId, ScenarioMenuCommands.TakeCheckpointId })
        {
            var d = c.Get(id)!;
            Assert.False(d.IsEnabled());
            Assert.Contains("waiting for: Brain (CGF)", d.DynamicDisplayName!());
            Assert.False(c.Invoke(id).Success);
        }
        Assert.Empty(s.OpenForEditCalls);
        Assert.Empty(s.LoadScenarioCalls);

        foreach (var idle in m.AllowedInIdleItems().Where(a => a.Path.StartsWith("File/", StringComparison.Ordinal) && a.Path != FileMenuLayout.Close))
        {
            Assert.False(idle.Enabled());                                                 // the Start surface reads these very nodes
            Assert.Contains("waiting for: Brain (CGF)", idle.Label());
        }

        Assert.True(c.Get(ScenarioMenuCommands.CloseId)!.IsEnabled());                   // a LEAVE is never gated
        Assert.Null(c.Get(ScenarioMenuCommands.CloseId)!.DynamicDisplayName);
        Assert.True(m.AllowedInIdleItems().Single(a => a.Path == FileMenuLayout.Close).Enabled());   // … nor on the Start surface

        closed = null;
        Assert.True(c.Get(ScenarioMenuCommands.LoadId)!.IsEnabled());
        Assert.Equal("Load Scenario…", c.Get(ScenarioMenuCommands.LoadId)!.DynamicDisplayName?.Invoke() ?? "Load Scenario…");
        Assert.True(m.AllowedInIdleItems().Single(a => a.Path == FileMenuLayout.LoadScenario).Enabled());
    }

    /// <summary>
    /// ⭐ CE-1442 — the silent-default rail, on the CONSTRUCTED object: the REAL Orchestrator host (which holds its run choice) passes the gate to the shared registration — before its node has heard any cluster state, its Open Scenario command is
    /// disabled with the gate's reason (a host that forgot <c>closedReason</c> would leave it enabled). 🔴 Mutation: drop <c>closedReason:</c> from <c>OrchestratorSubsystem</c>'s registration ⇒ <c>IsEnabled</c> is true.
    /// </summary>
    [Fact]
    public void CE1442_TheOrchestratorHost_PassesItsRunGateToTheSharedRegistration()
    {
        var host = new Hrot.Orchestrator.OrchestratorSubsystem();
        host.Initialize(new Fdp.Toolkit.Runner.SubsystemConfig { Headless = true, DomainId = 225 });
        try
        {
            var wm = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 16f, 16f));
            host.ComposeScenarioActions(wm);
            var open = wm.ShellCommands.Get(ScenarioMenuCommands.LoadId)!;
            Assert.False(open.IsEnabled());
            Assert.Contains("no cluster state", open.DynamicDisplayName!());
        }
        finally
        {
            host.Shutdown();
        }
    }
}
