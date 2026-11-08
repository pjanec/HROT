using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Editor.AiShared.Documents;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.RunMode;
using NodeEditor.Core.Action;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// ⭐⭐ <c>CE-3408</c> (R-5, AQ108 §0 D27) — the Save SLOT: ONE button (<c>shell.save</c>) whose glyph changes — the floppy in Edit (greyed with <c>SaveDisabledReason</c> as its tooltip when the world cannot be saved, and in a Preview), the amber FLAG
/// (take a checkpoint) in Live / DAR / AAR, greyed with <c>CheckpointDisabledReason</c> where it cannot (an AAR). The indicator's pen, the slot and File › Save read the SAME <c>SaveDisabledReason</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]   // the frame rail owns the ImGui context
public sealed class SaveSlotTests
{
    private sealed class Control : IRunScenarioControl
    {
        public string? LoadedScenarioName { get; set; } = "hill-attack";
        public string? SaveDisabledReason { get; set; }
        public readonly List<string> Calls = new();
        public void LoadForLive(string scenarioName) { }
        public void OpenForEdit(string scenarioName) { }
        public void OpenRecording(Guid exerciseId) { }
        public void SaveCurrent() => Calls.Add("SaveCurrent");
        public void TakeCheckpoint() => Calls.Add("TakeCheckpoint");
    }

    private static readonly Guid Run = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static (EditorCommandDescriptor Slot, RunModeChoice Choice, Control C, Action<EditorCommandContext> Invoke) Slot(ClusterState state, bool notRecorded = false, string? saveReason = null)
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var facade = new ClusterTimeTransportAdapter(bus, () => view, () => 5);
        var control = new Control { SaveDisabledReason = saveReason };
        var choice = facade.AttachRunChoice(control);
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = (Fdp.Core.ExerciseId)Run, RunNotRecorded = notRecorded });
        bus.SwapBuffers(); view.Update(); facade.Update(); bus.SwapBuffers();
        var descriptors = new List<EditorCommandDescriptor>(); var actions = new List<Action<EditorCommandContext>>();
        ShellSaveCommands.Register((d, a) => { descriptors.Add(d); actions.Add(a); }, new AiDocumentManager(perspectiveSwitchCallback: _ => { }),
            null, null, null, null, _ => { }, isScenarioContext: () => true, hasLoadedScenario: () => true, saveScenarioAction: () => control.SaveCurrent(),
            scenarioName: () => control.LoadedScenarioName, runChoice: () => choice);
        int i = descriptors.FindIndex(d => d.Id == ShellSaveCommands.SaveId);
        return (descriptors[i], choice, control, actions[i]);
    }

    /// <summary>Edit: the floppy (no dynamic glyph), enabled where the world can be saved; greyed with the reason as the tooltip where it cannot (a variant world). The pen reads the same answer.
    /// 🔴 Mutations: <c>SlotReason</c> returning null ⇒ the variant row enabled; <c>IsEnabled</c> back to <c>true</c> ⇒ the same.</summary>
    [Fact]
    public void R5_Edit_Floppy_GreyedWithTheSameReasonThePenReads()
    {
        var ok = Slot(ClusterState.OperatingEdit);
        Assert.True(ok.Slot.IsEnabled());
        Assert.Null(ok.Slot.DynamicGlyph!());
        Assert.Equal("Save Scenario 'hill-attack'", ok.Slot.DynamicDisplayName!());
        Assert.Equal("Edit", ok.Choice.Indicator.Word);
        Assert.Equal(IconPalette.Yellow, ok.Choice.Indicator.Color);   // the pen

        var variant = Slot(ClusterState.OperatingEdit, saveReason: "built with variant 3");
        Assert.False(variant.Slot.IsEnabled());
        Assert.Contains("built with variant 3", variant.Slot.DynamicDisplayName!());
        Assert.Equal(IconPalette.Violet, variant.Choice.Indicator.Color);   // the eye: the pen and the slot disagree never
        Assert.Equal(variant.Choice.SaveSlotDisabledReason, variant.C.SaveDisabledReason);
    }

    /// <summary>A Preview: the floppy, greyed with the session's refusal (a preview runs a copy of the edit world).</summary>
    [Fact]
    public void R5_Preview_FloppyGreyed()
    {
        var p = Slot(ClusterState.OperatingPreview, saveReason: "a preview runs a copy of the edit world");
        Assert.False(p.Slot.IsEnabled());
        Assert.Null(p.Slot.DynamicGlyph!());
        Assert.Contains("a preview runs a copy of the edit world", p.Slot.DynamicDisplayName!());
    }

    /// <summary>Live / DAR: the amber FLAG, enabled, takes a checkpoint; an unrecorded run greys it with CheckpointDisabledReason; AAR: greyed with today's reason.
    /// 🔴 Mutations: <c>DynamicGlyph</c> null ⇒ the floppy in a run; <c>SaveSlotDisabledReason</c> reading the scenario's reason in a run ⇒ the Live rows grey.</summary>
    [Fact]
    public void R5_InARun_AmberFlag_TakesACheckpoint_GreyedWhereItCannot()
    {
        foreach (var state in new[] { ClusterState.OperatingLive, ClusterState.OperatingReview })
        {
            var s = Slot(state, saveReason: "a live run is not the scenario — Save takes a checkpoint");
            Assert.True(s.Slot.IsEnabled());                                          // the scenario's refusal is NOT the slot's: it checkpoints
            var g = s.Slot.DynamicGlyph!()!.Value;
            Assert.Equal(IconPalette.Amber, g.Color);
            Assert.Equal("", g.Glyph);
            Assert.Equal("Take a checkpoint (Ctrl+S)", s.Slot.DynamicDisplayName!());
            s.Invoke(default);
            Assert.Equal(new[] { "SaveCurrent" }, s.C.Calls);                          // the session's save — the run choice's intercept turns it into the checkpoint (CE-4097 D8)
        }
        var unrecorded = Slot(ClusterState.OperatingLive, notRecorded: true);
        Assert.False(unrecorded.Slot.IsEnabled());
        Assert.Contains("this run was not recorded", unrecorded.Slot.DynamicDisplayName!());
        var aar = Slot(ClusterState.OperatingReplay);
        Assert.False(aar.Slot.IsEnabled());
        Assert.NotNull(aar.Slot.DynamicGlyph!());
        Assert.Contains("a recording cannot take a checkpoint — go live from a moment first", aar.Slot.DynamicDisplayName!());
    }

    // ── the real toolbar slot on a real frame ─────────────────────────────────────────────────────────────────────

    private sealed class Commands : IEditorCommands
    {
        public readonly List<EditorCommandDescriptor> List = new();
        public IReadOnlyList<EditorCommandDescriptor> All => List;
        public EditorCommandDescriptor? Get(string id) => List.Find(d => d.Id == id);
        public EditorCommandResult Invoke(string id, EditorCommandContext? ctx = null) => new(true, null);
        public event Action<string>? AvailabilityChanged { add { } remove { } }
    }

    private sealed class NoIcons : NodeEditor.Core.Interfaces.IIconProvider
    {
        public bool TryGet(string key, out NodeEditor.Core.Interfaces.IconHandle handle) { handle = default; return false; }
    }

    /// <summary>
    /// On a real frame (the app's fonts, Xvfb) the REAL <c>ToolbarCommandAdapter</c> draws the slot: in Live the amber FLAG, full strength; in an AAR the same flag greyed (its reason is the tooltip). ⚠ The floppy is the atlas sprite — no atlas on a rail frame, so
    /// the Edit rows draw the adapter's text fallback; their enabled state is the descriptor rails above. Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>save-slot.png</c>.
    /// 🔴 Mutation: the adapter ignoring <c>DynamicGlyph</c> ⇒ no amber pixels ⇒ red.
    /// </summary>
    [SkippableFact]
    public void R5_TheSlot_DrawsTheAmberFlag_InARun_AndDimmedWhereItCannot_OnARealFrame()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        var rows = new[] { ClusterState.OperatingEdit, ClusterState.OperatingLive, ClusterState.OperatingReplay };
        var slots = rows.Select(r => Slot(r)).ToArray();
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(240, 150);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var managers = slots.Select(s =>
        {
            var cmds = new Commands(); cmds.List.Add(s.Slot);
            var toolbar = new Fdp.Presentation.WindowManager.MainToolbarManager();
            Fdp.Presentation.WindowManager.ToolbarCommandAdapter.Register(toolbar, cmds, s.Slot.Id, new NoIcons(), 0);
            return toolbar;
        }).ToArray();
        void Draw()
        {
            for (int i = 0; i < managers.Length; i++)
            {
                ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, i * 50)); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(240, 50));
                ImGuiNET.ImGui.Begin("##slot" + i, ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                managers[i].RenderInline();
                ImGuiNET.ImGui.End();
            }
        }
        for (int i = 0; i < 5; i++) frame.Step(Draw);
        var shot = Raylib_cs.Raylib.LoadImageFromScreen();
        try
        {
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "save-slot.png"));
            int Amber(int row)
            {
                int n = 0;
                for (int x = 0; x < 120; x++) for (int y = row * 50; y < row * 50 + 50; y++)
                {
                    var c = Raylib_cs.Raylib.GetImageColor(shot, x, y);
                    if (Math.Abs(c.R - 255) <= 30 && Math.Abs(c.G - 178) <= 40 && Math.Abs(c.B - 51) <= 50) n++;
                }
                return n;
            }
            Assert.True(Amber(1) > 15, $"Live: the amber flag ({Amber(1)} px)");
            Assert.True(Amber(2) < 5, $"AAR: the flag is dimmed ({Amber(2)} px)");
            Assert.True(Amber(0) < 5, "Edit: no flag");
        }
        finally { Raylib_cs.Raylib.UnloadImage(shot); }
    }

    // ── ⭐ R-8 / CE-3412 — the fidelity render: EVERY state of ind.png from the REAL toolbar + the REAL Save slot, one frame ───────────────────────────────────

    private sealed class WorldEnder : Hrot.UI.Common.RunMode.IExerciseEnder { public bool CanEnd { get; set; } public bool HasEnded { get; set; } public bool End() => false; }

    private sealed record Row(string Name, Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter F, RunModeChoice Choice, Fdp.Presentation.WindowManager.MainToolbarManager Slot, Hrot.UI.Common.Panels.MainToolbarTimeControlSection Bar);

    private static Row BuildRow(string name, ClusterState state, bool running = false, double clock = 0, bool notRecorded = false, string? saveReason = null, bool ended = false, bool canEnd = false)
    {
        var bus = new FdpEventBus();
        var view = new ClusterView(bus);
        var facade = new ClusterTimeTransportAdapter(bus, () => view, () => clock);
        var control = new Control { SaveDisabledReason = saveReason };
        var choice = facade.AttachRunChoice(control);
        if (ended || canEnd) facade.Ender = new WorldEnder { HasEnded = ended, CanEnd = canEnd };
        bus.Publish(new Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent { TargetMode = running ? Fdp.ModuleHost.Time.TimeMode.Continuous : Fdp.ModuleHost.Time.TimeMode.Deterministic });
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ExerciseId = (Fdp.Core.ExerciseId)Run, RunNotRecorded = notRecorded });
        bus.SwapBuffers(); view.Update(); facade.Update(); bus.SwapBuffers(); choice.Update();
        var cmds = new Commands(); var descriptors = new List<EditorCommandDescriptor>();
        ShellSaveCommands.Register((d, a) => descriptors.Add(d), new AiDocumentManager(perspectiveSwitchCallback: _ => { }),
            null, null, null, null, _ => { }, isScenarioContext: () => true, hasLoadedScenario: () => true, saveScenarioAction: () => control.SaveCurrent(),
            scenarioName: () => control.LoadedScenarioName, runChoice: () => choice);
        cmds.List.Add(descriptors.Find(d => d.Id == ShellSaveCommands.SaveId)!);
        var toolbar = new Fdp.Presentation.WindowManager.MainToolbarManager();
        Fdp.Presentation.WindowManager.ToolbarCommandAdapter.Register(toolbar, cmds, ShellSaveCommands.SaveId, new NoIcons(), 0);
        return new Row(name, facade, choice, toolbar, new Hrot.UI.Common.Panels.MainToolbarTimeControlSection(facade, choice: choice));
    }

    /// <summary>
    /// Every state of <c>ind.png</c> (indicator · ▶ ▾ · ■ · ⏪ · Save + the tooltip column) on a real frame, the Edit row HOVERED (the mouse over the indicator). Asserts what the picture says in pixels: Live · ended's ■ is the light
    /// right-from-bracket (no green triangle), a DAR's ⏏ the same; the briefing's ■ and ⏪ are dim. Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>r8-toolbar-states.png</c>.
    /// 🔴 Mutation: <c>BtnShape.Eject</c> back to the green triangle ⇒ the ended row's green count &gt; 0 and its light count falls ⇒ red.
    /// </summary>
    [SkippableFact]
    public void R8_EveryStateOfIndPng_OnARealFrame_WithTheCloseGlyph()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        var rows = new[]
        {
            BuildRow("Edit · saveable", ClusterState.OperatingEdit),
            BuildRow("Edit · HOVERED", ClusterState.OperatingEdit),
            BuildRow("Edit · variant preset (not saveable)", ClusterState.OperatingEdit, saveReason: "built with variant 3"),
            BuildRow("Preview running", ClusterState.OperatingPreview, running: true, clock: 5),
            BuildRow("Live · BRIEFING (paused at 0:00) · recorded", ClusterState.OperatingLive),
            BuildRow("Live · RUNNING · recorded", ClusterState.OperatingLive, running: true, clock: 60),
            BuildRow("Live · running · NOT recorded", ClusterState.OperatingLive, running: true, clock: 60, notRecorded: true),
            BuildRow("Live · ENDED", ClusterState.OperatingLive, clock: 754, ended: true),
            BuildRow("DAR (review)", ClusterState.OperatingReview, clock: 30),
            BuildRow("AAR (replay)", ClusterState.OperatingReplay, clock: 30),
        };
        const int H = 40, W = 1100;
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(W, H * rows.Length + 4);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        bool hover = false;
        void Draw()
        {
            const ImGuiNET.ImGuiWindowFlags bare = ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoSavedSettings;
            if (hover) { var io = ImGuiNET.ImGui.GetIO(); io.AddMousePosEvent(22, H * 1 + 20); Raylib_cs.Raylib.SetMousePosition(22, H * 1 + 20); }
            for (int i = 0; i < rows.Length; i++)
            {
                ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, i * H)); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(520, H));
                ImGuiNET.ImGui.Begin("##r8bar" + i, bare);
                rows[i].Bar.Render();
                ImGuiNET.ImGui.End();
                ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(520, i * H)); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(60, H));
                ImGuiNET.ImGui.Begin("##r8slot" + i, bare);
                rows[i].Slot.RenderInline();
                ImGuiNET.ImGui.End();
                ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(590, i * H)); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(W - 590, H));
                ImGuiNET.ImGui.Begin("##r8note" + i, bare | ImGuiNET.ImGuiWindowFlags.NoInputs);
                ImGuiNET.ImGui.TextDisabled(rows[i].Name); ImGuiNET.ImGui.TextWrapped(rows[i].Choice.IndicatorTooltip());
                ImGuiNET.ImGui.End();
            }
        }
        for (int i = 0; i < 5; i++) frame.Step(Draw);
        hover = true;
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        var shot = Raylib_cs.Raylib.LoadImageFromScreen();
        try
        {
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "r8-toolbar-states.png"));
            int Count(int row, int x0, int x1, Func<Raylib_cs.Color, bool> what)
            {
                int n = 0;
                for (int x = x0; x < x1; x++) for (int y = row * H; y < (row + 1) * H; y++) if (what(Raylib_cs.Raylib.GetImageColor(shot, x, y))) n++;
                return n;
            }
            bool Green(Raylib_cs.Color c) => c.G > 150 && c.R < 90 && c.B < 90;
            bool Light(Raylib_cs.Color c) => c.R > 170 && c.G > 190 && c.B > 215;
            // the ⏏ / close slot sits right of ▶ and the step button: scan the row's middle, away from the indicator (Live's red / green dot) and the time text
            Assert.Equal(0, Count(7, 100, 330, Green));                      // Live · ended: no green eject triangle
            Assert.True(Count(7, 100, 330, Light) > 20, "Live · ended: the light right-from-bracket glyph is drawn");
            Assert.Equal(0, Count(8, 100, 330, Green));                      // DAR: the same glyph for ⏏ back to live
            Assert.True(Count(8, 100, 330, Light) > 20, "DAR: the light right-from-bracket glyph is drawn");
        }
        finally { Raylib_cs.Raylib.UnloadImage(shot); }
    }

    /// <summary>
    /// The popups and dialogs of <c>story.png</c> on a real frame: ② the Edit indicator menu, ⑥ the Live · ended menu, ⑤ <i>End the exercise?</i>, ⑦ <i>Close the live session?</i>. Asserts the words and buttons the picture shows; renders
    /// (never in git, <c>HROT_RAIL_SHOTS</c>) <c>r8-menu-edit.png</c>, <c>r8-menu-ended.png</c>, <c>r8-dialog-end.png</c>, <c>r8-dialog-close.png</c>.
    /// 🔴 Mutation: the dialog's title or the ended menu's first label changed ⇒ red.
    /// </summary>
    [SkippableFact]
    public void R8_TheMenusAndDialogsOfTheStory_OnARealFrame()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        var edit = BuildRow("edit", ClusterState.OperatingEdit);
        var endedMenu = BuildRow("ended", ClusterState.OperatingLive, clock: 754, ended: true);
        var endDlg = BuildRow("end", ClusterState.OperatingLive, running: true, clock: 60, canEnd: true);
        var closeDlg = BuildRow("close", ClusterState.OperatingLive, clock: 754, ended: true);
        endDlg.Choice.Stop();
        closeDlg.Choice.Stop();
        Assert.Equal("End the exercise?", endDlg.Choice.Pending!.Title);
        Assert.Equal("Close the live session?", closeDlg.Choice.Pending!.Title);
        var endButtons = endDlg.Choice.Pending!.Buttons.Select(b => b.Label).ToArray();
        var closeButtons = closeDlg.Choice.Pending!.Buttons.Select(b => b.Label).ToArray();
        Assert.Equal(new[] { "End the exercise", "Keep running" }, endButtons);           // ⑤ — the picture's buttons
        Assert.Equal(new[] { "Close the session", "Cancel" }, closeButtons);              // ⑦
        var editItems = edit.Bar.ModeMenu()!.Items;
        Assert.All(editItems.Take(3), i => Assert.NotNull(i.Glyph));                                        // ② Edit pen · Live ● · AAR ▶ each wear their glyph
        Assert.Equal(Fdp.Presentation.Fonts.IconsFontAwesome6.Pen, editItems[0].Glyph);
        var endedItems = endedMenu.Bar.ModeMenu()!.Items;
        Assert.Equal(endedMenu.Choice.LiveColour, endedItems[0].GlyphColor);                               // ⑥ Run again: Live's own colour (red recorded)
        Assert.Equal(Fdp.Presentation.Fonts.IconsFontAwesome6.Flag, endedItems[1].Glyph);                  // ⑥ Restart from a checkpoint ▸ — the amber flag
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(760, 330);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Shot(Row r, string file, bool openMenu)
        {
            bool open = openMenu;
            void Draw()
            {
                ImGuiNET.ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(760, 40));
                ImGuiNET.ImGui.Begin("##r8", ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                if (open) { ImGuiNET.ImGui.OpenPopup("##mt_mode_popup"); open = false; }
                r.Bar.Render();
                ImGuiNET.ImGui.End();
            }
            for (int i = 0; i < 4; i++) frame.Step(Draw);
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, file));
        }
        Shot(edit, "r8-menu-edit.png", true);
        Shot(endedMenu, "r8-menu-ended.png", true);
        Shot(endDlg, "r8-dialog-end.png", false);
        Shot(closeDlg, "r8-dialog-close.png", false);
    }
}
