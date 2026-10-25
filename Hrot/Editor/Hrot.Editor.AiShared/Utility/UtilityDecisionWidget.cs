using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Utility;

/// <summary>
/// ⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md §2) — ONE drawing of a <see cref="UtilityDecisionSheet"/>: W1 the settings, W2 with a
/// unit's values, W3 an option opened for an <c>IsOption</c>, W4 a ranking's candidates. The Details view and the Brain tab's Why both draw
/// it. ⭐ The curve "shape" is the RUNNING curve evaluated (<see cref="ResponseCurve.Evaluate"/>), with the unit's raw value as a dot.
/// </summary>
public static class UtilityDecisionWidget
{
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.8f, 0.25f, 1f);
    private static readonly Vector4 Bar = new(0.35f, 0.62f, 0.95f, 1f);

    /// <param name="openOption">W3: the option to open (others closed); -1 = the winner (or the first) open.</param>
    /// <param name="unitLabel">How a unit / candidate is named.</param>
    public static void Draw(UtilityDecisionSheet sheet, string idScope, int openOption = -1, Func<Entity, string>? unitLabel = null,
                            Action? trace = null, bool header = true)
    {
        ImGui.PushID(idScope + "/" + sheet.DecisionId);
        if (header)
        {
            ImGui.TextUnformatted(sheet.Name);
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"{sheet.Kind}{(sheet.Hysteresis > 0 ? $"  ·  stickiness +{sheet.Hysteresis:0.##} to the winner" : "")}");
        }
        if (sheet.Unit is { } unit)
        {
            string who = unitLabel?.Invoke(unit) ?? $"#{unit.Index}";
            if (!sheet.Traced)
            {
                ImGui.TextColored(Dim, $"Live values: off - {who} is not traced.");
                if (trace != null) { ImGui.SameLine(); if (ImGui.SmallButton("Trace this unit")) trace(); }
            }
            else if (sheet.Evaluations == 0) ImGui.TextColored(Dim, $"{who} has not run this decision yet.");
            else if (!sheet.IsRanking)
                ImGui.TextUnformatted($"{who}: winner * {sheet.WinnerName ?? "-"}   margin {sheet.Margin:0.00}"
                                      + (sheet.RunnerUpName != null ? $" over {sheet.RunnerUpName}" : "")
                                      + $"   switched {sheet.Switches}x   evaluated {sheet.Evaluations}x");
        }

        if (sheet.IsRanking) DrawRanking(sheet, unitLabel);
        else
        {
            float top = sheet.Options.Max(o => o.Score ?? 0f);
            for (int i = 0; i < sheet.Options.Count; i++)
            {
                var o = sheet.Options[i];
                bool open = openOption >= 0 ? o.OptionId == openOption : (o.Winner || (i == 0 && !sheet.Options.Any(x => x.Winner)));
                ImGui.SetNextItemOpen(open, ImGuiCond.Appearing);
                string score = o.Score is { } s ? $"  {s:0.00}" : "";
                bool expanded = ImGui.TreeNodeEx($"{(o.Winner ? "* " : "")}{o.Name}{score}###opt{o.OptionId}", ImGuiTreeNodeFlags.SpanAvailWidth);
                ImGui.SameLine(300f);
                if (o.Score is { } sc && top > 0f) DrawBar(sc / top, 120f);
                else ImGui.TextColored(Dim, $"{Mode(o.Mode)}   {o.Considerations.Count} considerations");
                if (o.ZeroedBy != null) { ImGui.SameLine(); ImGui.TextColored(Dim, $"(zeroed by {o.ZeroedBy})"); }
                if (expanded)
                {
                    if (o.Score.HasValue) ImGui.TextColored(Dim, Mode(o.Mode));
                    DrawConsiderations(o, live: sheet.Unit != null && sheet.Traced);
                    ImGui.TreePop();
                }
            }
        }
        ImGui.PopID();
    }

    private static string Mode(ScoringMode m) => m == ScoringMode.WeightedProduct ? "product (compensated)" : "sum";

    private static void DrawConsiderations(SheetOption o, bool live)
    {
        int cols = live ? 6 : 5;
        if (!ImGui.BeginTable("##cons", cols, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn("input", ImGuiTableColumnFlags.WidthStretch);
        if (live) { ImGui.TableSetupColumn("raw"); ImGui.TableSetupColumn("->curve"); ImGui.TableSetupColumn("x weight"); }
        else { ImGui.TableSetupColumn("ctx"); ImGui.TableSetupColumn("weight"); ImGui.TableSetupColumn("curve"); }
        ImGui.TableSetupColumn("shape");
        if (live) ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();
        foreach (var c in o.Considerations)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((c.Modifier ? "x " : "  ") + c.Input);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Describe(c));
            if (live)
            {
                ImGui.TableNextColumn(); ImGui.TextUnformatted(Num(c.Raw));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(Num(c.Curved));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(Num(c.Weighted));
            }
            else
            {
                ImGui.TableNextColumn(); ImGui.TextUnformatted(c.Context.ToString().ToLowerInvariant());
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{c.Weight:0.00}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(c.Curve.Kind + (c.Modifier ? " modifier" : ""));
            }
            ImGui.TableNextColumn();
            DrawCurve(c.Curve, c.Raw, new Vector2(48f, 14f));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Describe(c));
            if (live) { ImGui.TableNextColumn(); ImGui.TextColored(Dim, c.Curve.Kind.ToString()); }
        }
        ImGui.EndTable();
    }

    private static void DrawRanking(UtilityDecisionSheet sheet, Func<Entity, string>? label)
    {
        ImGui.TextColored(Dim, "one scoring, applied to each candidate");
        foreach (var o in sheet.Options)
        {
            ImGui.TextColored(Dim, Mode(o.Mode));
            DrawConsiderations(o, live: false);
        }
        if (sheet.Ranked.Count == 0) return;
        ImGui.Separator();
        float top = sheet.Ranked.Max(r => r.Score);
        for (int i = 0; i < sheet.Ranked.Count; i++)
        {
            var r = sheet.Ranked[i];
            ImGui.TextUnformatted($"{i + 1,2}  {label?.Invoke(r.Entity) ?? $"#{r.Entity.Index}"}  {r.Score:0.00}");
            ImGui.SameLine(300f);
            if (top > 0f) DrawBar(r.Score / top, 120f);
        }
    }

    /// <summary>The considered value, its curve and what it means — the tooltip and the API text.</summary>
    public static string Describe(SheetConsideration c)
        => $"{c.Input} ({c.Context.ToString().ToLowerInvariant()})  ->  {c.Curve.Kind}  slope {c.Curve.Slope:0.##}  exponent {c.Curve.Exponent:0.##}"
           + $"  shift {c.Curve.XShift:0.##}  weight {c.Weight:0.##}"
           + (c.Modifier ? "\nmodifier: multiplies the option's score after the aggregate (1.0 changes nothing)" : "")
           + (c.Raw is { } raw ? $"\nthis unit: raw {raw:0.00} -> {c.Curved:0.00}" : "");

    private static string Num(float? v) => v is { } f ? $"{f:0.00}" : "-";

    private static void DrawBar(float fraction, float width)
    {
        var p = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeight() - 4f;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p + new Vector2(0, 2), p + new Vector2(width * Math.Clamp(fraction, 0f, 1f), 2 + h), ImGui.GetColorU32(Bar));
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight()));
    }

    /// <summary>The curve over [0, 1] as a polyline, the unit's raw value as a dot.</summary>
    private static void DrawCurve(ResponseCurve curve, float? raw, Vector2 size)
    {
        var p = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        uint col = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1f));
        Vector2 At(float x) => p + new Vector2(x * size.X, (1f - Math.Clamp(curve.Evaluate(x), 0f, 1f)) * size.Y);
        var prev = At(0f);
        for (int i = 1; i <= 16; i++) { var next = At(i / 16f); dl.AddLine(prev, next, col); prev = next; }
        if (raw is { } r) dl.AddCircleFilled(At(Math.Clamp(r, 0f, 1f)), 2.5f, ImGui.GetColorU32(Gold));
        ImGui.Dummy(size);
    }
}
