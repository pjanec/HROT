using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Hrot.CGF.Configuration;
using Hrot.Common.Events;
using Hrot.Core.Mission;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.Map.Common.Events;
using Hrot.Map.Definitions.Behavior;
using Hrot.Presentation.Orders;
using Hrot.UI.Common.Adapters;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ CE-4359 (docs/UX/DESIGN_Mission_Plan_Editor.md §5.13, R-4184; mockup <c>task-picker-visual.svg</c>) — <b>+ Add task</b> opens the ONE <c>PickerWindow</c> (Tree layout, side pane) over the production behaviour
/// registry: a Rifleman is offered <b>44</b> tasks in the fixed folders (Orders 4 · Movement 8 · Combat 14 · Weapons and vehicles 7 · Casualties and engineering 6 · Crowd and surrender 2 · Reactions 3) — the
/// tests / demos / samples (44) behind the off <i>Show tests and demos</i> tick, the operator and the SOPs never; names are words; the last pick is first. Frame rails run under
/// <c>xvfb-run -a -s "-screen 0 1600x1200x24"</c> and skip without a display — ⛔ a skip is not a gate row.
/// 🔴 Red-proof: <c>TaskPicker.IsHiddenGroup</c> returning false ⇒ the Rifleman lists 89, the tick hides nothing.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheTaskPickerTests : IDisposable
{
    private readonly EntityRepository _repo = new();
    private readonly BehaviorRegistry _registry = new();
    private readonly TkbDatabase _tkb = HrotEnvironment.CreateTkb();

    public TheTaskPickerTests()
    {
        _repo.RegisterComponent<TkbIdentity>();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterManagedComponent<ActiveMissionPlan>();
        _repo.RegisterEvent<MissionControlAckEvent>();
        CgfBehaviorSetup.LoadFromAiAssembly(_registry);
    }

    public void Dispose() => _repo.Dispose();

    private (Entity Entity, int Net, DISEntityType Dis, ScenarioMissionService Svc) Unit(long tkbType)
    {
        Assert.True(_tkb.TryGetByType((TkbTypeId)tkbType, out var t));
        var e = _repo.CreateEntity();
        int net = 4000 + e.Index;
        _repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)tkbType });
        _repo.AddComponent(e, new NetworkIdentity { Value = net });
        _repo.SetDisType(e, t.DisType);
        return (e, net, t.DisType, new ScenarioMissionService(_repo.Bus, _repo, _registry));
    }

    private List<PickerEntry> EntriesOf(long tkbType)
    {
        var u = Unit(tkbType);
        return TaskPicker.Entries(u.Dis, u.Svc.GetAvailableBehaviorChoices(u.Net), ParamsDtoRegistry.Default);
    }

    private static Dictionary<string, List<string>> Folders(IEnumerable<PickerEntry> entries, bool hidden)
    {
        var d = new Dictionary<string, List<string>>();
        foreach (var e in entries)
        {
            var info = (TaskInfo)e.Tag!;
            if (info.Hidden != hidden || string.IsNullOrEmpty(e.Category)) continue;
            string root = e.Category!.Split('/')[0];
            (d.TryGetValue(root, out var l) ? l : d[root] = new List<string>()).Add(e.Name);
        }
        return d;
    }

    // ── the model ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void H5_ARifleman_IsOffered44Tasks_InTheFixedFolders_TheOrdersInTheFixedOrder_44AreBehindTheTick()
    {
        var entries = EntriesOf(TkbEntityTypes.Infantry_Rifleman);
        var shown = Folders(entries, hidden: false);
        Assert.Equal(new[] { TaskPicker.OrdersFolder, "Movement", "Combat", "Weapons and vehicles", "Casualties and engineering", "Crowd and surrender", "Reactions" },
            TaskPicker.FolderOrder.Where(shown.ContainsKey).ToArray());                                        // the fixed order, "My behaviours" absent (no authored asset)
        Assert.Equal(new[] { 4, 8, 14, 7, 6, 2, 3 }, TaskPicker.FolderOrder.Where(shown.ContainsKey).Select(f => shown[f].Count).ToArray());
        Assert.Equal(44, shown.Values.Sum(l => l.Count));                                                      // 90 offered → 44 real tasks (+ the Wait leaf)
        Assert.Equal(new[] { "Move", "Hold", "Attack", "Observe and fire" }, shown[TaskPicker.OrdersFolder]); // the class's generic orders, fixed order, named without the class
        Assert.Contains("Fight from cover", shown["Combat"]);
        Assert.Contains("Move to location", shown["Movement"]);
        Assert.DoesNotContain(entries, e => e.Id is "beh:OperatorControl" or "beh:InertSop");                  // never offered: not tasks
        Assert.DoesNotContain(entries, e => e.Id is "beh:PersonMove");                                         // the order is in the Orders folder once, not twice
        int hidden = entries.Count(e => ((TaskInfo)e.Tag!).Hidden);
        Assert.Equal(44, hidden);                                                                              // Tests/* 27 · Samples* 10 · Demos/* 7
        Assert.Equal(44 + 1 + 44, entries.Count);                                                              // + Wait
    }

    [Fact]
    public void H5_ATank_GetsItsOwnOrdersAndVehicleTasks_NotTheInfantryDrills()
    {
        var shown = Folders(EntriesOf(TkbEntityTypes.Tank_M1Abrams), hidden: false);
        Assert.Equal(new[] { "Move", "Hold", "Attack" }, shown[TaskPicker.OrdersFolder].Take(3).ToArray());
        var all = shown.Values.SelectMany(l => l).ToList();
        Assert.Contains("Vehicle fight from cover", all);
        Assert.DoesNotContain("Squad march", all);
    }

    [Fact]
    public void H5_NamesAreWords_TheCodeNameIsInThePreview()
    {
        Assert.Equal("Move to location", TaskNames.Words("MoveToLocation"));
        Assert.Equal("Fight from cover", TaskNames.Words("FightFromCover"));
        Assert.Equal("Observe and fire", TaskNames.OrderWords("ObserveFire"));
        Assert.Equal("Move", TaskNames.OrderWords("Move"));
        var move = EntriesOf(TkbEntityTypes.Infantry_Rifleman).Single(e => e.Id == "order:Move");
        var info = (TaskInfo)move.Tag!;
        Assert.Equal("Move", info.Title);
        Assert.Contains("PersonMove", info.Subtitle);                                                          // the code name, in the preview's grey line
        Assert.True(info.Description.Length > 0);
        var fight = (TaskInfo)EntriesOf(TkbEntityTypes.Infantry_Rifleman).Single(e => e.Id == "beh:FightFromCover").Tag!;
        Assert.Contains("Combat", fight.Subtitle); Assert.Contains("FightFromCover", fight.Subtitle);
        Assert.Contains("cover", fight.Description);
    }

    // ── the picker on a real frame ──────────────────────────────────────────────────────────

    private sealed class NoIcons : IIconProvider { public bool TryGet(string key, out IconHandle handle) { handle = default; return false; } }

    [SkippableFact]
    public void H5_TheOpenPicker_ShowsTheFoldersInOrder_TheTickRevealsTheTests_RecentIsFirst_AndAnIdleFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var u = Unit(TkbEntityTypes.Infantry_Rifleman);
        var registry = new PickerRegistry();
        registry.SetServices(new NoIcons(), new NodeEditor.Core.DefaultTheme());
        var request = TaskPicker.Request("Rifleman 1 (Infantry)", u.Dis, u.Svc.GetAvailableBehaviorChoices(u.Net), ParamsDtoRegistry.Default);
        TaskPick? picked = null;
        using var frame = UiFrameHarness.Begin(1240, 640);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        registry.OpenPicker(request, r => picked = TaskPicker.PickOf(r));
        for (int i = 0; i < 6; i++) frame.Step(registry.DrawFrame);

        var rows = registry.TreeRows();
        var roots = rows.Where(r => r.IsFolder && r.Depth == 0).Select(r => r.Label).ToArray();
        Assert.Equal(new[] { TaskPicker.OrdersFolder, "Movement", "Combat", "Weapons and vehicles", "Casualties and engineering", "Crowd and surrender", "Reactions" }, roots);
        Assert.Equal("Wait", rows[^1].Label);                                                                  // the plan's pause: a root leaf after the folders
        Assert.Equal(45, registry.ListedCount);                                                                // 44 tasks + Wait; the tests are hidden

        for (int i = 0; i < 3; i++) frame.Step(registry.DrawFrame);
        Assert.Contains("Move to location", registry.TreeRows().Select(r => r.Label));                           // Orders and Movement start open, the rest closed
        Assert.DoesNotContain("Fight from cover", registry.TreeRows().Select(r => r.Label));
        registry.ExpandFolder("Combat");
        for (int i = 0; i < 3; i++) frame.Step(registry.DrawFrame);
        var labels = registry.TreeRows().Select(r => r.Label).ToList();
        int o = labels.IndexOf(TaskPicker.OrdersFolder);
        Assert.Equal(new[] { "Move", "Hold", "Attack", "Observe and fire" }, labels.Skip(o + 1).Take(4).ToArray());

        registry.ShowHidden = true;                                                                            // the tick
        Assert.Equal(45 + 44, registry.ListedCount);
        registry.ShowHidden = false;
        Assert.Equal(45, registry.ListedCount);

        // 0 B: a drawn idle frame (the tree is built once per list, so the frames after a refilter are the warm-up)
        Action drawPicker = registry.DrawFrame;   // one delegate: a method group converted per call allocates
        for (int i = 0; i < 3; i++) frame.Step(drawPicker);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5; i++) frame.Step(drawPicker);
        long drawn = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, drawn);

        Assert.True(registry.SelectEntry("beh:FightFromCover"));                                               // the preview pane of the chosen task (mockup)
        for (int i = 0; i < 3; i++) frame.Step(drawPicker);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : "";
        if (dir != "") { System.IO.Directory.CreateDirectory(dir); frame.Screenshot(System.IO.Path.Combine(dir, "task-picker.png")); }

        // recent first: a pick is remembered, and listed before any folder when the picker opens again
        Assert.True(registry.ConfirmEntry("beh:FightFromCover", "add"));
        Assert.Equal(new TaskPick(TaskPickKind.Behaviour, "FightFromCover"), picked);
        registry.OpenPicker(request, _ => { });
        for (int i = 0; i < 3; i++) frame.Step(registry.DrawFrame);
        Assert.Equal("beh:FightFromCover", registry.RecentSection()[0].Id);
    }

    // ── + Add task on the Orders tab ───────────────────────────────────────────────────────

    [SkippableFact]
    public void H5_AddTaskOnTheOrdersTab_OpensThePicker_AndThePickJoinsThePlan()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var u = Unit(TkbEntityTypes.Infantry_Rifleman);
        var registry = new PickerRegistry();
        registry.SetServices(new NoIcons(), new NodeEditor.Core.DefaultTheme());
        var tab = new OrdersTab(() => u.Svc, () => _repo, pickers: () => registry);
        var ctx = new DetailsContext(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { u.Entity }, null, "Scenario", VariableRunState.Planning);
        using var frame = UiFrameHarness.Begin(1240, 640);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw() => frame.Step(() =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(620, 480), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Orders");
            tab.Draw(ctx, "t");
            ImGuiNET.ImGui.End();
            registry.DrawFrame();
        });
        Draw(); Draw();
        var draft = tab.Draft!;
        int before = draft.Tasks.Count;
        tab.ApplyTaskOp(draft, new NodeEditor.Core.Widgets.ListOp(NodeEditor.Core.Widgets.ListOpKind.Add));       // the list's [+ Add task]
        Draw();
        Assert.NotEmpty(registry.TreeRows());                                                                       // the picker is open, the plain popup is not used
        Assert.True(registry.ConfirmEntry("order:Attack", "add"));
        Draw(); Draw();
        Assert.Equal(before + 1, tab.Draft!.Tasks.Count);
        Assert.Equal("Order.Attack", tab.Draft.Tasks[^1].BehaviorId);
        Assert.True(tab.Dirty);
    }
}

