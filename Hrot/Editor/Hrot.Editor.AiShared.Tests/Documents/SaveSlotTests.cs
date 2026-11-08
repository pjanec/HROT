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
}
