using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Panels.Breakpoints;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Xunit;
using DpBreakpoint = Hrot.Diagnostics.Breakpoints.Breakpoint;
using DpBpId = Hrot.Diagnostics.Breakpoints.BreakpointId;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-16 / C5 (docs/UX/DESIGN_Condition_Editor.md §6, R-4155) — the ONE condition editor in its other hosts: the replay search and the data breakpoints (the Replay Browser's and the editor's: one panel).
/// Per host: (①) the friendly view draws the condition as sentence rows in a real frame, (②) the Expert view brings back the old surface and the condition goes with it, (③) an unfilled blank holds the commit.
/// ⭐ The mission task's <i>Advanced condition</i> has no host to wire yet — the Mission plan editor (M5) has not built it (DESIGN_Mission_Plan_Editor §M5); the editor's <c>ConditionContexts.Mission</c> waits for it.
/// 🔴 Red-proof (compiles): <c>ReplaySearchPanel</c>'s Expert toggle not carrying <c>_friendly</c> into the session ⇒ the Expert-view assertion of the search rail fails.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class ConditionEditorHostsFrameRail
{
    private sealed class NopSearch : IRecordingSearchService
    {
        public IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter = null, System.Threading.CancellationToken ct = default) => Array.Empty<SearchResultDto>();
        public IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter = null, System.Threading.CancellationToken ct = default) => Array.Empty<LifecycleSearchResultDto>();
    }

    [SkippableFact]
    public void TheReplaySearch_ReadsAsSentences_AndTheExpertViewBringsBackTheTree()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var panel = new ReplaySearchPanel(Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), new NopSearch(), _ => { }, _ => { }, (_, _) => { });
        panel.FriendlyCondition = new ScenarioTimePredicateDto { FromSeconds = 60 };
        using var frame = UiFrameHarness.Begin();
        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 800));
                    ImGui.Begin("Search", ImGuiWindowFlags.NoCollapse);
                    panel.DrawContent();
                    ImGui.End();
                });
        }
        Step(4);
        Assert.False(panel.ExpertView);
        Assert.Single(panel.ConditionRows);
        Assert.Contains("after 1:00", panel.ConditionRows[0]);
        Assert.Equal(0, panel.TreeRowsDrawn);                       // the friendly view: no struct-editor tree

        panel.ExpertView = true; Step(4);                           // the Expert view: today's mode radio + tree, holding the SAME condition
        Assert.True(panel.TreeRowsDrawn > 0, "the Expert view draws the struct editor's rows");
        panel.ExpertView = false; Step(4);                          // and back: the condition survived the round trip
        Assert.Contains("after 1:00", panel.ConditionRows[0]);
    }

    private sealed class Manager : IDataBreakpointManager
    {
        public readonly List<DpBreakpoint> Bps = new();
        public readonly List<SearchPredicateDto?> Updates = new();
        public IReadOnlyList<DpBreakpoint> AllBreakpoints => Bps;
        public bool IsPaused { get; set; }
        public long PausedTick => 0;
        public int PendingMutationsCount => 0;
        public bool HasMountedDelegates => false;
        public bool HasStatefulTrackers => false;
        public ISimulationView ActiveView => throw new NotImplementedException();
        public DpBpId Add(DpBreakpoint bp) => bp.Id;
        public readonly List<SearchPredicateDto> Added = new();
        public DpBpId AddBreakpoint(SearchPredicateDto cond, Entity? filter = null, int thresh = 1, string name = "", Guid? srcId = null)
        {
            var id = Id(100 + Added.Count);
            Added.Add(cond);
            Bps.Add(new DpBreakpoint { Id = id, Enabled = true, Condition = cond });
            return id;
        }
        public void Remove(DpBpId id) { }
        public void SetEnabled(DpBpId id, bool e) { }
        public void UpdateCondition(DpBpId id, SearchPredicateDto? c)
        {
            Updates.Add(c);
            int i = Bps.FindIndex(b => b.Id == id);
            if (i >= 0) Bps[i] = new DpBreakpoint { Id = id, Enabled = Bps[i].Enabled, Condition = c };
        }
        public void MarkAsWatch(DpBpId id, bool w) { }
        public void SaveWatches(string p) { }
        public void LoadWatches(string p) { }
        public void OnHotReloadCompleted() { }
        public void OnHotReloadBegin() { }
        public void StageMutation(Entity entity, Type componentType, object componentValue) { }
        public void OnHit(DpBreakpoint bp, Entity e) { }
        public void OnExternalHit(string tag, Entity e) { }
        public void RequestStep() { }
        public void RequestContinue() { }
        public void EvaluateStatefulBreakpoints(EntityRepository repo) { }
        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates => Array.Empty<(DpBreakpoint, CompiledComponentPredicate)>();
        public IReadOnlyList<(DpBreakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners => Array.Empty<(DpBreakpoint, CompiledEventScanner)>();
#pragma warning disable CS0067
        public event Action<DpBreakpoint, Entity>? OnBreakpointHit;
        public event Action<bool>? OnPauseStateChanged;
#pragma warning restore CS0067
    }

    private static SearchPredicateDto Blank() => new OnEntityPredicateDto { Ref = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(10), default), Predicate = new SpatialBoundingPredicateDto() };   // "#10 is inside …" with the area still blank

    private static DpBpId Id(int v) => (DpBpId)Activator.CreateInstance(typeof(DpBpId), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { v }, null)!;

    [SkippableFact]
    public void TheDataBreakpoints_ReadAsSentences_ApplyWritesThemBack_AndTheExpertViewBringsBackTheListing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var manager = new Manager();
        var id = Id(1);
        manager.Bps.Add(new DpBreakpoint { Id = id, Enabled = true, Condition = new ScenarioTimePredicateDto { FromSeconds = 60 } });
        var panel = new DataBreakpointManagerPanel(manager, new TemporalStatusBannerState());
        panel.ShowCondition(id);
        using var frame = UiFrameHarness.Begin();
        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 800));
                    ImGui.Begin("Breakpoints", ImGuiWindowFlags.NoCollapse);
                    panel.DrawContent();
                    ImGui.End();
                });
        }
        Step(4);
        Assert.Single(panel.ConditionRows);
        Assert.Contains("after 1:00", panel.ConditionRows[0]);
        Assert.Null(panel.ConditionProblem);
        Assert.Empty(manager.Updates);                              // nothing is written until Apply

        panel.ExpertView = true; Step(4);                           // the Expert view: today's read-only listing, no sentence rows drawn
        Assert.True(panel.ExpertView);
        int drawn = panel.FriendlyDraws;
        Step(3);
        Assert.Equal(drawn, panel.FriendlyDraws);                   // the friendly view is not drawn while the Expert view shows
        panel.ExpertView = false; Step(4);
        Assert.True(panel.FriendlyDraws > drawn);
        Assert.Single(panel.ConditionRows);
    }

    [SkippableFact]
    public void AnUnfilledBlank_HoldsTheSearchAndTheBreakpointsApply()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var manager = new Manager();
        var id = Id(2);
        manager.Bps.Add(new DpBreakpoint { Id = id, Enabled = true, Condition = Blank() });
        var bps = new DataBreakpointManagerPanel(manager, new TemporalStatusBannerState());
        bps.ShowCondition(id);
        var search = new ReplaySearchPanel(Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), new NopSearch(), _ => { }, _ => { }, (_, _) => { });
        search.FriendlyCondition = Blank();
        using var frame = UiFrameHarness.Begin();
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 800));
                ImGui.Begin("Hosts", ImGuiWindowFlags.NoCollapse);
                bps.DrawContent(); search.DrawContent();
                ImGui.End();
            });
        Assert.StartsWith("Choose an area", bps.ConditionProblem);
        Assert.StartsWith("Choose an area", search.ConditionProblem);
        Assert.Empty(manager.Updates);
    }

    private static string U6Shot(string name)   // evidence, never committed
    {
        string dir = Environment.GetEnvironmentVariable("HROT_FRAME_SCREENSHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-frames");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, name);
    }

    private static (DataBreakpointManagerPanel Panel, Manager Manager, Hrot.Editor.UiFrameRail.UiFrameSession Frame, Action<int> Step) U6Rig(int rows, out DpBpId[] ids)
    {
        var manager = new Manager();
        ids = new DpBpId[rows];
        for (int i = 0; i < rows; i++)
        {
            ids[i] = Id(i + 1);
            manager.Bps.Add(new DpBreakpoint { Id = ids[i], Enabled = true, Condition = new ScenarioTimePredicateDto { FromSeconds = 60 * (i + 1) } });
        }
        var panel = new DataBreakpointManagerPanel(manager, new TemporalStatusBannerState());
        var frame = UiFrameHarness.Begin(900, 420);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Step(int n)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(900, 420));
                    ImGui.Begin("Breakpoints", ImGuiWindowFlags.NoCollapse);
                    panel.DrawContent();
                    ImGui.End();
                });
        }
        return (panel, manager, frame, Step);
    }

    /// <summary>⭐ U-6 (CE-4336; the user's report: "Add puts a New breakpoint row in the list at once, and Show condition does nothing") — <i>+ Add</i> opens the CONDITION first: no row exists until <i>Add breakpoint</i> (held while nothing is chosen),
    /// the editor is ON SCREEN (inside the window, under the list), <i>Cancel</i> adds nothing, and the committed row is selected with its condition showing. Real panel, real frame (900 x 420, three rows). 🔴 Red-proof (compiles): the toolbar's "+ Add"
    /// handler adding a "New Breakpoint" row at once (the old <c>AddBreakpoint</c>) ⇒ the "no row yet" assertion goes red.</summary>
    [SkippableFact]
    public void U6_Add_OpensTheConditionFirst_NoRowUntilCommitted_TheEditorIsOnScreen()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (panel, manager, frame, step) = U6Rig(3, out var ids);
        using var _ = frame;
        step(4);
        Assert.False(panel.Adding);
        panel.StartAdd();
        step(5);
        Assert.True(panel.Adding);
        Assert.Equal(3, manager.Bps.Count);                                   // no "New Breakpoint" row: the condition comes first
        Assert.Empty(manager.Added);
        Assert.True(panel.ConditionTop > 0f && panel.ConditionTop < 420f - 40f, $"the new-breakpoint editor starts inside the window ({panel.ConditionTop:0} of 420)");
        Assert.True(panel.GridRect.Max.Y <= panel.ConditionTop + 1f, "the list ends above the editor, never under it");
        Assert.False(panel.CommitAdd());                                      // nothing chosen yet: "Add breakpoint" holds
        frame.Screenshot(U6Shot("breakpoints-add-condition-first.png"));

        panel.AddDraft = new ScenarioTimePredicateDto { FromSeconds = 300 };  // what the editor's rows would write
        step(3);
        Assert.Single(panel.ConditionRows);
        Assert.True(panel.CommitAdd());
        step(4);
        Assert.False(panel.Adding);
        Assert.Single(manager.Added);
        Assert.Equal(4, manager.Bps.Count);
        Assert.Equal(manager.Bps[3].Id, panel.SelectedId);                    // the new row is selected, its condition showing, on screen
        Assert.True(panel.ConditionTop > 0f && panel.ConditionTop < 420f - 40f);

        panel.StartAdd(); step(3);
        panel.CancelAdd(); step(2);
        Assert.False(panel.Adding);
        Assert.Equal(4, manager.Bps.Count);                                   // Cancel adds nothing
    }

    /// <summary>⭐ U-6 (CE-4336) — the row menu's <i>Show condition</i> works on ANY row (not only a selected one): each of three rows shows its own condition ON SCREEN under the list (it used to draw below a table that filled the window), and a second pick hides it.
    /// 🔴 Red-proof (compiles): the list drawn with no bounded height ⇒ the editor lands below the window ⇒ red.</summary>
    [SkippableFact]
    public void U6_ShowCondition_WorksOnAnyRow_AndTheEditorIsOnScreen()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (panel, manager, frame, step) = U6Rig(3, out var ids);
        using var _ = frame;
        step(4);
        Assert.Equal(0f, panel.ConditionTop);                                 // nothing shown yet
        for (int i = 2; i >= 0; i--)                                          // the LAST row first: it was never the selected one
        {
            panel.ToggleCondition(ids[i]);
            step(4);
            Assert.Equal(ids[i], panel.SelectedId);
            Assert.True(panel.ConditionTop > 0f && panel.ConditionTop < 420f - 40f, $"row {i}: the condition editor is on screen ({panel.ConditionTop:0} of 420)");
            Assert.True(panel.GridRect.Max.Y <= panel.ConditionTop + 1f, $"row {i}: the list ends above it");
            Assert.Single(panel.ConditionRows);
            Assert.Contains($"after {i + 1}:00", panel.ConditionRows[0]);      // ITS condition, not another row's
            if (i == 2) frame.Screenshot(U6Shot("breakpoints-show-condition-last-row.png"));
        }
        panel.ToggleCondition(ids[0]);                                        // a second pick on the row whose condition shows hides it
        step(3);
        Assert.Equal(0f, panel.ConditionTop);
    }
}
