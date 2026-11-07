using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.Scenario;
using Hrot.Editor.UiFrameRail;
using NodeEditor.Core.Widgets;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-3391 (📄 docs/UX/DESIGN_Scenario_Presets.md §5a) — the PRESET EDITOR in Details ▸ Scenario ▸ Parameters, in a REAL frame (xvfb; a skip is not a pass): the Preset combo / [+] / [⋮], the table's
/// value column following the shown preset (an override is amber + ↺, a value equal to the default is not an override), and every preset edit a header edit through the ONE <c>EditHistory</c>:
/// a rename / remove / retype of a param updates every preset's Args in the SAME step. 🔴 Red-proofs (compile): <c>HeaderParamsSchemaSource.RenameVariable</c> not mapping the presets ⇒ the rename rail is red;
/// <c>SetPresetValue</c> keeping a value equal to the default ⇒ the override / ↺ rail is red.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]   // one ImGui context: the frame rails of this assembly run serially
public sealed class ThePresetEditorTests
{
    private static (EntityRepository Repo, HeaderParamsSchemaSource Src, ParametersDrawer Drawer) Fixture()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<ParamBindings>();
        ScenarioDescription.Stamp(repo, "desc", "scenario", "h", null, "");
        var src = new HeaderParamsSchemaSource(() => repo, new Hrot.Presentation.Edits.EditHistory());
        src.AddVariable(new BlackboardVariableEntry("count", typeof(int), null, DefaultValueJson: "3"));
        src.AddVariable(new BlackboardVariableEntry("rate", typeof(float), null, DefaultValueJson: "1.5"));
        return (repo, src, new ParametersDrawer(src));
    }

    private static Action<Action<ImGuiNET.ImGuiIOPtr>?, int> Stepper(UiFrameSession frame, ParametersDrawer drawer) => (inject, times) =>
    {
        for (int i = 0; i < times; i++)
            frame.Step(() =>
            {
                inject?.Invoke(ImGui.GetIO());
                ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(700, 420), ImGuiNET.ImGuiCond.Always);
                ImGui.Begin("Parameters (CE-3391)");
                drawer.Draw();
                ImGui.End();
            });
    };

    private static void Pos(ImGuiNET.ImGuiIOPtr io, Vector2 p) { io.AddMousePosEvent(p.X, p.Y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(p.X), (int)Math.Round(p.Y)); }

    private static void Click(Action<Action<ImGuiNET.ImGuiIOPtr>?, int> step, Vector2 p)
    {
        step(io => Pos(io, p), 2);
        step(io => { Pos(io, p); io.AddMouseButtonEvent(0, true); }, 1);
        step(io => { Pos(io, p); io.AddMouseButtonEvent(0, false); }, 1);
        step(null, 3);
    }

    private static string? ArgsOf(HeaderParamsSchemaSource s, string preset, string key)
        => s.Presets().FirstOrDefault(p => p.Name == preset)?.Args?[key]?.ToJsonString();

    // ── the data: header edits, one step each (no frame) ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANewPreset_IsACopyOfTheShownOne_NamedPresetN_AndDefaultStaysImplicit()
    {
        var (repo, src, _) = Fixture(); using var _r = repo;
        Assert.Equal("Preset 1", src.AddPresetCopyOf("Default"));
        Assert.True(src.SetPresetValue("Preset 1", "count", "9"));
        Assert.Equal("Preset 2", src.AddPresetCopyOf("Preset 1"));                 // a copy of the one shown …
        Assert.Equal("9", ArgsOf(src, "Preset 2", "count"));                       // … with its overrides
        Assert.Equal(new[] { "Preset 1", "Preset 2" }, src.Presets().Select(p => p.Name));
        Assert.Equal("3", src.Variables.Single(v => v.Name == "count").DefaultValueJson);   // Default (the declared values) untouched
        src.History.Undo();
        Assert.Single(src.Presets());                                              // one undo step per preset edit
    }

    [Fact]
    public void ARenameOrRemoveOrRetypeOfAParam_UpdatesEveryPresetsArgs_InOneUndoStep()
    {
        var (repo, src, _) = Fixture(); using var _r = repo;
        src.AddPresetCopyOf("Default"); src.AddPresetCopyOf("Default");
        src.SetPresetValue("Preset 1", "count", "9"); src.SetPresetValue("Preset 2", "count", "7"); src.SetPresetValue("Preset 2", "rate", "2.5");
        int steps = src.History.Count;

        src.RenameVariable("count", "total");
        Assert.Equal(steps + 1, src.History.Count);                                // ONE step
        Assert.Equal("9", ArgsOf(src, "Preset 1", "total")); Assert.Null(ArgsOf(src, "Preset 1", "count"));
        Assert.Equal("7", ArgsOf(src, "Preset 2", "total")); Assert.Equal("2.5", ArgsOf(src, "Preset 2", "rate"));
        Assert.True(src.History.Undo());
        Assert.Equal("9", ArgsOf(src, "Preset 1", "count")); Assert.Equal("7", ArgsOf(src, "Preset 2", "count"));
        Assert.Contains(src.Variables, v => v.Name == "count");                    // the declaration came back in the same step

        src.RemoveVariable("rate");
        Assert.Null(ArgsOf(src, "Preset 2", "rate")); Assert.Equal("7", ArgsOf(src, "Preset 2", "count"));
        Assert.True(src.History.Undo());
        Assert.Equal("2.5", ArgsOf(src, "Preset 2", "rate"));

        src.RetypeVariable("rate", "int");                                         // a value of the old type is not carried over
        Assert.Null(ArgsOf(src, "Preset 2", "rate"));
        Assert.True(src.History.Undo());
        Assert.Equal("2.5", ArgsOf(src, "Preset 2", "rate"));
    }

    [Fact]
    public void ADefaultCannotBeRenamedOrDeleted_AndAValueEqualToTheDefaultIsNotAnOverride()
    {
        var (repo, src, _) = Fixture(); using var _r = repo;
        Assert.False(src.RenamePreset("Default", "Base")); Assert.Contains("Default", src.LastRefusal);
        Assert.False(src.DeletePreset("Default")); Assert.Contains("Default", src.LastRefusal);
        Assert.Empty(src.Presets());
        src.AddPresetCopyOf("Default");
        Assert.False(src.RenamePreset("Preset 1", "default"));                     // the implicit one's name is taken
        Assert.True(src.SetPresetValue("Preset 1", "count", "9"));
        Assert.True(src.SetPresetValue("Preset 1", "count", "3"));                 // back to the declared default
        Assert.Null(src.Presets().Single().Args);                                  // the key is gone — an override is a difference
    }

    // ── the frame ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TheNewButton_CopiesTheShownPreset_TheComboNeverCallsTheHost_AndAnOverrideIsAmberWithAResetThatRemovesTheKey()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin(800, 520);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);   // the real fonts (the icon glyphs, · and —)
        int hostCalls = 0;
        drawer.PreviewPreset = _ => hostCalls++;
        drawer.MapHolds = () => { hostCalls += 0; return new MapHolding("Default", true, null); };
        drawer.UsesVariants = () => true;
        var step = Stepper(frame, drawer);
        step(null, 4);
        Assert.Equal("Default", drawer.Shown);
        Click(step, (drawer.NewRect.Min + drawer.NewRect.Max) / 2f);              // [+] on Default
        step(null, 3);
        Assert.Equal("Preset 1", drawer.Shown);
        Assert.Equal("Preset 1", Assert.Single(src.Presets()).Name);
        Assert.Equal("Value · Preset 1", drawer.Table.ColumnHeaders[2]);

        // override under the preset: type 9 into the count cell
        var nm = drawer.NameCells.Single(c => c.Name == "count"); var tc = drawer.Table.TypeCell("count");
        var at = new Vector2(tc.Max.X + 60f, (nm.Min.Y + nm.Max.Y) / 2f);
        Click(step, at);
        step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiNET.ImGuiKey.A, true); }, 1);
        step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.A, false); io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, false); }, 1);
        step(io => io.AddInputCharacter('9'), 1);
        step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, true), 1);
        step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, false), 1);
        step(null, 4);
        Assert.Equal("9", ArgsOf(src, "Preset 1", "count"));
        Assert.Equal("3", src.Variables.Single(v => v.Name == "count").DefaultValueJson);   // Default untouched
        step(null, 3);
        Assert.True(drawer.Table.Drawer!.IsChanged(drawer.Table.ValueNode("count")!), "an override reads as changed (amber bar + ↺)");
        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) frame.Screenshot(System.IO.Path.Combine(shot, "presets-editor.png"));

        Assert.True(KitProbe.TryGet(KitProbe.ResetMark, out var a, out var b));
        Click(step, (a + b) / 2f);                                                  // ↺
        step(null, 3);
        Assert.Null(src.Presets().Single().Args);                                   // the key is REMOVED (not set to the default)
        Assert.Equal("3", src.Variables.Single(v => v.Name == "count").DefaultValueJson);
        Assert.False(drawer.Table.Drawer!.IsChanged(drawer.Table.ValueNode("count")!));

        // the combo picks what the table shows and never touches the host
        drawer.Show("Default"); step(null, 3);
        Assert.Equal("Default", drawer.Table.ColumnHeaders[2] == "Default" ? "Default" : drawer.Table.ColumnHeaders[2]);
        Assert.Equal(0, hostCalls);
        Assert.True(drawer.VariantRowDrawn == false, "Default has no variant row");
        drawer.Show("Preset 1"); step(null, 3);
        Assert.True(drawer.VariantRowDrawn, "a preset of a scenario that uses variants has the Variant row");
    }

    [SkippableFact]
    public void AnEyeWorld_GreysThePresetEditing_AndPreviewCallsTheHostWithTheShownPreset()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin(800, 520);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);   // the real fonts (the icon glyphs, · and —)
        src.AddPresetCopyOf("Default");
        string? previewed = null;
        drawer.PreviewPreset = n => previewed = n;
        drawer.MapHolds = () => new MapHolding("Night attack", false, "variant 3");
        var step = Stepper(frame, drawer);
        step(null, 4);
        Click(step, (drawer.NewRect.Min + drawer.NewRect.Max) / 2f);
        Assert.Single(src.Presets());                                               // [+] is greyed in a world that cannot be saved (D5)
        drawer.Show("Preset 1"); step(null, 3);
        Click(step, (drawer.PreviewRect.Min + drawer.PreviewRect.Max) / 2f);
        Assert.Equal("Preset 1", previewed);
    }
}
