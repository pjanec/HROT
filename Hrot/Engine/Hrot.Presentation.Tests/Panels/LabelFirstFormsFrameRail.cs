using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Utils;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Panels;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ U-13 (docs/UX/DESIGN_UI_Conventions.md §3 C4, §3.1 — R-4145, R-4147, R-4155) — the label-first sweep.
/// (①) the SOURCE rail: a visible-label input widget (<c>InputText("Name", …)</c>, <c>Combo("Kind", …)</c>, <c>Checkbox("Loop", …)</c> …) outside the named §3.1 exceptions is a failure —
/// ⛔ a new raw label-input anywhere else fails here. The exceptions are the user-approved families (toolbar / option checkboxes · a selector on the same line as its action buttons · a field of several flags ·
/// the StructEdit field drawers inside a form's value cell); each carries its one-line reason below. Radio buttons are option groups (their text IS the option) and are not scanned.
/// (②) the FRAME rails: fields drawn by SEPARATE calls share ONE label column (<c>FormGrid.BeginField</c>), a panel that moved really draws through <c>FormGrid</c> with its control right of the label column,
/// and a tree table (<c>ImGuiPropertyTree</c>, the raw editor) takes the ONE width rule (≤ 25 % of the width, 90 px floor).
/// 🔴 Red-proof (compiles): <c>ConfigPanel</c> drawing <c>Checkbox("Grid", …)</c> raw again ⇒ the source rail names it ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class LabelFirstFormsFrameRail
{
    private static readonly string[] Widgets =
    {
        "InputText", "InputTextWithHint", "InputTextMultiline", "InputInt", "InputInt2", "InputInt3", "InputInt4", "InputFloat", "InputFloat2", "InputFloat3", "InputFloat4", "InputDouble",
        "DragFloat", "DragFloat2", "DragFloat3", "DragFloat4", "DragInt", "DragInt2", "DragInt3", "DragFloatRange2", "SliderFloat", "SliderFloat2", "SliderFloat3", "SliderFloat4", "SliderInt", "SliderAngle",
        "Checkbox", "CheckboxFlags", "Combo", "BeginCombo", "ColorEdit3", "ColorEdit4", "ColorPicker3", "ColorPicker4", "ListBox", "InputScalar", "DragScalar", "SliderScalar",
    };

    /// <summary>(file name, label starts with, ⭐ the one-line reason). Every entry is a §3.1 family the user approved (R-4147 / U-13).</summary>
    private static readonly (string File, string Label, string Why)[] Allowed =
    {
        // toolbar / option checkboxes — the family of Q-C3's filter-checkbox rows
        ("EventBrowserPanel.cs", "Pause", "toolbar option"), ("EventBrowserPanel.cs", "Current Frame Only", "toolbar option"),
        ("MessageLogPanel.cs", "Enable Notifications", "toolbar option"), ("MessageLogPanel.cs", "Time", "column toggle in a toolbar"), ("MessageLogPanel.cs", "Logger", "column toggle in a toolbar"),
        ("ReplaySearchPanel.cs", "Filter by Target Entity", "option beside its 'Fill from Selected' button"),
        ("MigrationAlertManager.cs", "Don't show", "a dialog's opt-out footer"), ("BrainStopWhen.cs", "only this unit", "option inside a toolbar row"),
        ("EntityBrainView.cs", "on map", "a per-row pin toggle"), ("ExportDeliveryModal.cs", "Show full export", "a dialog option"), ("RulesWindow.cs", "Follow now", "toolbar option"),
        ("DirectorWindow.cs", "Loop shot", "transport-row option"), ("DirectorWindow.cs", "Pilot view", "toolbar option"), ("DirectorWindow.cs", "Director camera", "toolbar option"), ("DirectorWindow.cs", "replace", "option beside the Promote button"),
        // a selector on the same line as its action buttons — a toolbar row
        ("ClusterScenarioPanel.cs", "Step (s)", "transport row"), ("ClusterScenarioPanel.cs", "Speed", "transport row"),
        ("ClusterScenarioPanel.cs", "Select Scenario", "selector + Load buttons on one line"), ("ClusterScenarioPanel.cs", "Select Exercise", "selector + Open AAR on one line"),
        ("SimHostControlPanels.cs", "Speed", "transport row"), ("ReportsWindow.cs", "Unit", "filter row"), ("ReportsWindow.cs", "Kind", "filter row"),
        // the Orders tab's task sentence — "then [preset] [5] min", "give up after [10] min": the inputs sit INSIDE the sentence (DESIGN_Mission_Plan_Editor §5.11, the ConditionEditor family)
        ("OrdersTab.cs", "then", "the task's then-sentence preset"), ("OrdersTab.cs", "min##", "inline in the task's sentence"), ("OrdersTab.cs", "%##", "inline in the task's sentence"),
        ("OrdersTab.cs", "give up after", "the task's give-up sentence"),
        // the condition editor's hosts (U-15 / U-16) — the Expert view tick is a view option; the sentence rows' inline inputs sit INSIDE the sentence (DESIGN_Condition_Editor §4)
        ("DataBreakpointManagerPanel.cs", "Expert view", "view option"), ("RuleDetails.cs", "Expert view", "view option"), ("ReplaySearchPanel.cs", "Expert view", "view option"),
        ("RuleDetails.cs", "times at most", "inline in the timing sentence"),
        ("ConditionEditor.cs", "N##n", "inline in a sentence row"), ("ConditionEditor.cs", "times", "inline in a sentence row"), ("ConditionEditor.cs", "no end", "inline in a sentence row"),
        // search / filter boxes — the hint is the label (Q-C3)
        ("AssetBrowserPanel.cs", "Filter", "search box"), ("SpawnerPanel.cs", "Search", "search box"),
        // a field made of several flags — label in column 1, each flag keeps its text inside column 2 (§3.1)
        ("ClusterDiagnosticsPanel.cs", "Events", "the Dump kinds field's flags"), ("ClusterDiagnosticsPanel.cs", "Entities", "the Dump kinds field's flags"),
        ("ClusterDiagnosticsPanel.cs", "Architecture", "the Dump kinds field's flags"), ("ClusterDiagnosticsPanel.cs", "Logs", "the Dump kinds field's flags"),
        ("WhenNodeDrawer.cs", "Rising", "the Edges field's flags"), ("WhenNodeDrawer.cs", "Falling", "the Edges field's flags"),
        // the StructEdit field drawers — already inside a form's value cell (Details forms D1+ owns them)
        ("ImGuiPropertyTreeAdapter.cs", "", "StructEdit field drawer"), ("ComponentEditDrawer.cs", "", "StructEdit field drawer"),
        ("QuaternionEulerFieldDrawer.cs", "", "StructEdit field drawer"),
    };

    private static readonly string[] NotProduct =
    {
        "FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/", "FDP/ExtDeps/FastHSM/demos/", "FDP/ExtDeps/FastBTree/demos/", "FDP/Examples/",
    };

    [Fact]
    public void NoProductionWidgetCarriesAVisibleLabel_ExceptTheNamedSection31Families()
    {
        string root = Fdp.Testing.TestRepo.Root;
        var rx = new Regex(@"\.(" + string.Join("|", Widgets) + @")\(\s*\$?""(?!#)([^""]*)""", RegexOptions.Compiled);
        var bad = new List<string>();
        var used = new HashSet<(string, string)>();
        foreach (var file in Fdp.Testing.RepoSources.CsFiles(root))
        {
            string rel = file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
            if (rel.Contains(".Tests/", StringComparison.Ordinal) || rel.Contains("/Tests/", StringComparison.Ordinal) || rel.Contains("/tests/", StringComparison.Ordinal)) continue;
            if (NotProduct.Any(n => rel.StartsWith(n, StringComparison.Ordinal))) continue;
            string text = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            foreach (Match m in rx.Matches(text))
            {
                string label = m.Groups[2].Value;
                var ok = Allowed.FirstOrDefault(a => a.File == name && label.StartsWith(a.Label, StringComparison.Ordinal));
                if (ok.File != null) { used.Add((ok.File, ok.Label)); continue; }
                bad.Add($"{rel}:{text.Substring(0, m.Index).Count(c => c == '\n') + 1} {m.Value.Replace("\n", " ")}");
            }
        }
        Assert.True(bad.Count == 0, "visible-label widgets outside the §3.1 families (move them onto FormGrid): " + string.Join(" | ", bad));
        var stale = Allowed.Where(a => !used.Contains((a.File, a.Label))).Select(a => a.File + ":" + a.Label).ToArray();
        Assert.True(stale.Length == 0, "allowed exceptions that no longer exist (delete them): " + string.Join(", ", stale));
    }

    private sealed class Sample { public int Alpha = 1; public float Bravo = 2f; public string LongerMemberName = "x"; }

    [SkippableFact]
    public void FieldsOfSeparateCalls_ShareOneLabelColumn_AndTheTreeTakesTheOneRule()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(640, 360);
        new EditorFontService().Initialize(1f, 1f);
        var ctl = new Vector2[3];
        float v = 0.5f; int n = 3; bool b = true;
        float treeCol = 0f;

        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(600, 330));
            ImGui.Begin("##lf", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            FormGrid.BeginField("lf.fields", "Short"); ImGui.SliderFloat("##a", ref v, 0f, 1f); ctl[0] = ImGui.GetItemRectMin(); FormGrid.EndField();
            ImGui.TextDisabled("(anything between the fields)");
            FormGrid.BeginField("lf.fields", "A much longer label"); ImGui.InputInt("##b", ref n); ctl[1] = ImGui.GetItemRectMin(); FormGrid.EndField();
            FormGrid.BeginField("lf.fields", "Flag"); ImGui.Checkbox("##c", ref b); ctl[2] = ImGui.GetItemRectMin(); FormGrid.EndField();
            ImGui.Separator();
            ImGuiPropertyTree.Render(new Sample());
            treeCol = FormGrid.ColumnWidthOf(ImGuiPropertyTree.TableFormId);
            ImGui.End();
        }
        for (int i = 0; i < 8; i++) frame.Step(Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(Path.Combine(dir, "label-first-fields.png"));

        Assert.True(Math.Abs(ctl[0].X - ctl[1].X) < 0.5f && Math.Abs(ctl[1].X - ctl[2].X) < 0.5f, $"every field's control starts at ONE x ({ctl[0].X}, {ctl[1].X}, {ctl[2].X})");
        float col = FormGrid.ColumnWidthOf("lf.fields");
        Assert.True(col > 60f, $"the shared column learned the widest label (got {col})");
        Assert.True(ctl[0].X >= 10f + col - 1f, "the control is right of the label column");
        float cap = Math.Max(0.25f * 600f, FormGrid.MinLabelColumn * ImGui.GetFontSize() / FormGrid.ReferenceFontSize);
        Assert.True(treeCol > 0f && treeCol <= cap + 1f, $"the property tree's name column is FormGrid's rule, ≤ the 25 % cap (got {treeCol}, cap {cap})");
    }

    [SkippableFact]
    public void ConfigPanel_DrawsLabelFirst_ThroughFormGrid()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(520, 300);
        new EditorFontService().Initialize(1f, 1f);
        var panel = new ConfigPanel();
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(500, 280));
            ImGui.Begin("##cfg", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            panel.DrawContent(null!);
            ImGui.End();
        }
        for (int i = 0; i < 6; i++) frame.Step(Draw);
        float col = FormGrid.ColumnWidthOf("cfg.form");
        Assert.True(col > 40f, $"the Config panel is a FormGrid form (label column {col})");
    }
}
