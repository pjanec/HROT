using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Scenarios;
using NodeEditor.Core.Action;
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
    /// ⭐ <b>The CGF shape</b> — headless-first: no picker, no modal browser, and it LOGS instead of
    /// prompting. ⛔ Exactly the seam set <c>CgfSubsystem</c> passes.
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
            openPicker:         null,
            openSaveAsDialog:   null,
            confirmNewExercise: run => { log.Add("new-exercise proceeding without a prompt"); run(); });

        return (s, c, m, log);
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
            "File/Edit/New Scenario",
            "File/Edit/Open Scenario",
            "File/Edit/Save Curated Scenarios to Git",
            "File/Edit/Save Scenario",
            "File/Edit/Save Scenario As",
            "File/Live/Load Scenario",
            "File/Live/New Exercise",
        }, MenuPaths(menu));
    }

    // ══ ② ONLY SERVICEABILITY DIFFERS ═══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The difference between the hosts is ENABLEMENT, never PRESENCE.</b>
    /// 🔒 Ruling 49. ⭐ On the editor the picker-backed items are live with no override label; on CGF they
    /// are greyed and the label names the cause *(VC-3)*. ⛔ Neither host hides them.
    /// </summary>
    [Fact]
    public void OnlyTheEnablementDiffersBetweenTheHosts()
    {
        var editor = EditorShape();
        var cgf    = CgfShape();

        foreach (var id in new[] { ScenarioMenuCommands.LoadId, ScenarioMenuCommands.LoadLiveId,
                                   ScenarioMenuCommands.SaveAsId })
        {
            Assert.True(editor.C.Get(id)!.IsEnabled(), $"{id} must be live on the interactive host.");
            Assert.Null(editor.C.Get(id)!.DynamicDisplayName);

            Assert.False(cgf.C.Get(id)!.IsEnabled(), $"{id} has no seam to service it on CGF.");
            Assert.Contains("unavailable", cgf.C.Get(id)!.DynamicDisplayName!(), StringComparison.Ordinal);
        }
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
    /// ⛔⛔ <b>A CGF-shaped host's disabled load items must be INERT, not merely greyed.</b>
    /// ⚠ Worth its own rail: the handler closes over a nullable seam, and an early-return that was
    /// forgotten would let a re-entrant invoke path load a scenario the operator never chose.
    /// </summary>
    [Fact]
    public void OnCgfTheDisabledLoadItemsDoNothingEvenIfInvoked()
    {
        var (session, commands, _, _) = CgfShape();

        // ⭐ Invoke() itself refuses a disabled command, so this asserts BOTH gates: the refusal AND that
        //   nothing reached the session.
        Assert.False(commands.Invoke(ScenarioMenuCommands.LoadId).Success);
        Assert.False(commands.Invoke(ScenarioMenuCommands.LoadLiveId).Success);
        Assert.Empty(session.OpenForEditCalls);
        Assert.Empty(session.LoadForLiveCalls);
    }
}
