using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
/// ⭐⭐⭐ <b><c>VC-3</c>'s rails — THE CURATED-SCENARIOS ITEM IS THERE, AND IT SAYS WHY WHEN IT CANNOT
/// RUN.</b>
/// 🔴 <b>User, visual check <c>2026-08-22</c>:</b> <i>"missing the item to update the git-stored curated
/// scenario set."</i>
///
/// <para>⭐⭐⭐ <b>What the measurement actually found — and it is NOT what the handoff guessed.</b> The
/// handoff's lean was <i>"the walk-up probe does not find the repo, so the item is disabled (and
/// disabled items may not show)"</i>. 📐 Measured, three separate facts:</para>
///
/// <list type="number">
/// <item>⭐ <b>The item IS registered</b> — <c>ScenarioMenuCommands</c> registers
///   <c>scenario.updateCurated</c> unconditionally under <c>File/Scenario</c>.</item>
/// <item>⭐ <b>A DISABLED leaf IS drawn</b> — <c>WindowManager.RenderGlobalMenu</c> passes
///   <c>enabled</c> to <c>ImGui.MenuItem</c> *(greyed, not skipped)*. ⛔ So the "disabled items may
///   not show" hypothesis is FALSE.</item>
/// <item>⛔⛔ <b>…but the leaf carried NO REASON.</b> <c>RegisterCommand</c> never set
///   <c>DynamicDisplayName</c>, so <c>MenuCommandAdapter.ApplyLeafNode</c> copied a <c>null</c>
///   <c>DynamicLabel</c> ⇒ a greyed item with the plain label, which reads as <i>"not implemented"</i>
///   to someone hunting for it. ⭐ <b>That is the defect</b>, and it is exactly the gap against the
///   layout feature's <i>"Save current as default (unavailable — …)"</i> precedent the handoff
///   cites.</item>
/// </list>
///
/// <para>⚠ <b>The probe itself measured CLEAN here:</b> <c>&lt;repo&gt;/scenarios</c> exists with 3
/// curated scenarios, so <c>CanSaveToGit()</c> is true from a checkout. ⛔ I cannot reproduce the
/// user's run location from this container — ⭐ so the fix is to make the item SELF-EXPLAINING, which
/// turns <i>"missing"</i> into <i>"here is why"</i> wherever it runs. 📌 Stated rather than claimed
/// fixed: if it still reads as unavailable on the next baseline, the label now names the cause.</para>
/// </summary>
public sealed class TheCuratedScenariosItemSaysWhyTests
{
    // ⭐ CE-046 — the item moved with the rest of the scenario group into the distinct `File/Edit`
//   submenu (design §3a). ⛔ The COMMAND ID is unchanged; only the human-facing path moved.
    private const string MenuPath = "File/Edit/Save Curated Scenarios to Git";

    // ── the smallest fakes that let the registrar run ────────────────────────

    /// <summary>⭐ CE-046 — the registrar now binds to <see cref="IScenarioSession"/>, so the stub shrank
    /// from the whole 20-member editor facade to the scenario verbs alone.</summary>
    private sealed class NoSession : IScenarioSession
    {
        public ClusterState CurrentClusterState => ClusterState.Idle;
        public string? LoadedScenarioName => null;
        public bool IsDegraded => false;
        public void Update() { }
        public void ClearWorld() { }
        public void NewExercise() { }
        public void LoadForLive(string scenarioName) { }
        public void OpenForEdit(string scenarioName) { }
        public void SaveCurrent() { }
        public void SaveAs(string scenarioName) { }
        public void SaveTo(string filePath) { }
        public void TakeCheckpoint() { }
        public IReadOnlyList<SidecarFileInfo> GetMigrationSidecars() => Array.Empty<SidecarFileInfo>();
    }

    private sealed class Commands : IEditorCommands
    {
        private readonly Dictionary<string, (EditorCommandDescriptor D, Action<EditorCommandContext> A)> _c = new();
        public event Action<string>? AvailabilityChanged;
        public IReadOnlyList<EditorCommandDescriptor> All
        {
            get { var l = new List<EditorCommandDescriptor>(); foreach (var kv in _c) l.Add(kv.Value.D); return l; }
        }
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

    /// <summary>⭐ Runs the PRODUCTION registrar with a caller-chosen availability answer — ⛔ the one
    /// input that differs between a checkout and a deployed build.</summary>
    private static (GlobalMenuRegistry Menu, int Saves) RegisterWith(bool canSave)
    {
        int saves = 0;
        var menu = new GlobalMenuRegistry();
        var commands = new Commands();

        ScenarioMenuCommands.Register(
            registerCommand:      (d, h) => commands.Register(d, h),
            menu:                 menu,
            commands:             commands,
            session:              new NoSession(),
            openPicker:           (_, _) => { },
            openSaveAsDialog:     _ => { },
            isCuratedSaveEnabled: () => canSave,
            saveCuratedToGit:     () => saves++);

        return (menu, saves);
    }

    /// <summary>⭐ Walk the menu trie by path. ⚠ <c>MenuCommandAdapter.FindNode</c> is
    /// <c>internal</c> to <c>Fdp.Presentation</c>, so this walks the public <c>Children</c> the way
    /// <c>RenderGlobalMenu</c> itself does — ⛔ a copy of the traversal, not of the RULE.</summary>
    private static MenuItemNode? Find(GlobalMenuRegistry menu)
    {
        var node = menu.Root;
        foreach (var part in MenuPath.Split('/'))
            if (!node.Children.TryGetValue(part, out node!)) return null;
        return node;
    }

    private static MenuItemNode Leaf(GlobalMenuRegistry menu) => Find(menu)!;

    // ══ ① it is registered, either way ═══════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The item EXISTS whether or not it can run.</b> ⛔ The alternative — registering it only
    /// from a checkout — is what would genuinely make it "missing", and it teaches the operator
    /// nothing. 📌 The registrar's own comment already says <i>"always registered so the absence is
    /// explainable"</i>; this rail is what holds that to it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheItemIsRegistered_RegardlessOfWhetherItCanRun(bool canSave)
        => Assert.NotNull(Find(RegisterWith(canSave).Menu));

    // ══ ② the reason ═════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE FIX: disabled ⇒ the LABEL names the cause.</b>
    /// ⛔ Before this, the leaf's <c>DynamicLabel</c> was <c>null</c> and a greyed
    /// "Save Curated Scenarios to Git" was indistinguishable from an unimplemented one.
    /// 📌 The user's <c>2026-08-17</c> ruling: <i>"showing explanatory tooltip would be better than
    /// allowing user to click the button and then saying that it is not possible."</i>
    /// </summary>
    [Fact]
    public void WhenItCannotRun_TheLabelSaysWhy()
    {
        var leaf = Leaf(RegisterWith(canSave: false).Menu);

        Assert.False(leaf.GetEnabled!());
        Assert.Contains("unavailable", leaf.ResolveLabel());
        Assert.Contains("source tree", leaf.ResolveLabel());
    }

    /// <summary>
    /// ⭐⭐ <b>…and it does NOT nag when it can run.</b> ⚠ The negative half matters: a label that always
    /// carried the caveat would be worse than none, and it is the half a one-sided rail would miss.
    /// </summary>
    [Fact]
    public void WhenItCanRun_TheLabelIsPlainAndTheItemIsEnabled()
    {
        var leaf = Leaf(RegisterWith(canSave: true).Menu);

        Assert.True(leaf.GetEnabled!());
        Assert.Equal("Save Curated Scenarios to Git", leaf.ResolveLabel());
        Assert.DoesNotContain("unavailable", leaf.ResolveLabel());
    }

    // ══ ③ clicking it ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The enabled item actually runs the save; the disabled one does not.</b>
    /// ⚠ Not decoration: <c>MenuCommandAdapter</c> re-checks <c>IsEnabled</c> inside the click, so a
    /// greyed item that somehow got clicked must still be inert — ⛔ and the enabled path must reach
    /// the composition root's handler, which is the half that would silently rot if the delegate were
    /// dropped.
    /// </summary>
    [Fact]
    public void OnlyTheEnabledItem_ActuallySaves()
    {
        int enabledSaves = 0, disabledSaves = 0;

        Run(canSave: true,  () => enabledSaves++);
        Run(canSave: false, () => disabledSaves++);

        Assert.Equal(1, enabledSaves);
        Assert.Equal(0, disabledSaves);

        static void Run(bool canSave, Action onSave)
        {
            var menu = new GlobalMenuRegistry();
            var commands = new Commands();
            ScenarioMenuCommands.Register(
                registerCommand:      (d, h) => commands.Register(d, h),
                menu:                 menu, commands: commands, session: new NoSession(),
                openPicker:           (_, _) => { }, openSaveAsDialog: _ => { },
                isCuratedSaveEnabled: () => canSave,
                saveCuratedToGit:     onSave);

            var node = menu.Root;
            foreach (var part in MenuPath.Split('/')) node = node.Children[part];
            node.OnClick!();
        }
    }
}
