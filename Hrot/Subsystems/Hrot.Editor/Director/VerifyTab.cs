using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.UI.Common.Map3D.Director;
using ImGuiNET;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-1303</c> P1 (docs/DESIGN_Director_Timeline_Editor.md §11j) — the editor's Verify tab of the Director window, cut out of
    /// <c>DirectorWindow</c> so the window can live in <c>Hrot.Presentation</c>. <c>CE-4042</c>: a scenario with a <c>checks.json</c> is run as the
    /// regression check runs it (judged, compared with its golden); its row says pass/fail with every verdict. 🔒 <i>"Verify button ok, per scenario."</i>
    /// </summary>
    internal sealed class VerifyTab : IDirectorWindowTab
    {
        private int _verify;

        public string Name => "Verify";

        /// <summary>⭐ CE-4042 — pick a scenario that has checks; Verify, or re-record its golden (refused for a failing run).</summary>
        public void Draw(IDirectorTabContext ctx)
        {
            var runner = ctx.Runner;
            var names = ctx.Store.ScenariosWith(ChecksJson.FileName);
            if (names.Count == 0) { ImGui.TextDisabled("No scenario has checks (scenarios/<name>/checks.json)."); return; }
            _verify = Math.Clamp(_verify, 0, names.Count - 1);
            var arr = names.ToArray();
            ImGui.Combo("Scenario##verify", ref _verify, arr, arr.Length);
            ImGui.BeginDisabled(runner.Busy);
            if (ImGui.Button("Verify")) ctx.Start(VerifyRequest(arr[_verify], capture: false));
            ImGui.SameLine();
            if (ImGui.Button("Re-record golden")) ctx.Start(VerifyRequest(arr[_verify], capture: true));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Runs it and writes golden.json (working copy and source tree) — refused when a condition fails. Read its diff before committing.");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!runner.Busy);
            if (ImGui.Button("Stop")) runner.Stop();
            ImGui.EndDisabled();
        }

        public string? ReportLine(ItemReport report) =>
            report.Verify() is { } v ? $"{report.Status} · {v.Verdicts.Count(x => x.Ok)}/{v.Verdicts.Count} conditions · golden {v.Golden}" : null;

        public IEnumerable<string>? ReportDetail(ItemReport report) =>
            report.Verify() is { } v
                ? v.Verdicts.Select(x => $"{(x.Ok ? "PASS" : "FAIL")}  {x.Name}{(x.Time is { } t ? $"  ({t:0.##} s)" : "")}{(x.Outside is { } o ? $"  — held at {o:0.##} s, outside its window" : "")}")
                   .Concat(v.GoldenDiff).Concat(v.Problems).Concat(report.Problems)
                : null;

        public PlayRequest? Again(ItemReport report) => report.Verify() != null ? VerifyRequest(report.Scenario, capture: false) : null;

        private static PlayRequest VerifyRequest(string scenario, bool capture) =>
            new(Reel.Of(scenario, "_verify"), Plan: new VerifyOptions(capture));
    }
}
