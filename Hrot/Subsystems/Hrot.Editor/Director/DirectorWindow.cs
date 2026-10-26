using System;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.Map3D.Director;
using ImGuiNET;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> B6 (docs/DESIGN_Film_Director.md §4 "The panel") — <i>View › Director</i>: pick a reel or a scenario's take · Play /
    /// Stop / Next / Pause · <i>Replay this take</i> · <i>Prepare</i> (scout: bake the anchor times) · speed · a row per item with its last
    /// report (✅ / ⚠ mismatch / ⛔ beat missing). 🔒 <i>"also from some ui panel for the user without ai debug http"</i> — it calls the SAME
    /// <see cref="DirectorRunner"/> methods the <c>/director/*</c> routes call. Capture is the routes' (a cloud VM has no one at the panel).
    /// <para>⭐ <c>CE-4042</c> — <i>Verify</i>: a scenario with a <c>checks.json</c> is run as the regression check runs it (judged, compared
    /// with its golden), and its row says pass/fail with every verdict. 🔒 <i>"Verify button ok, per scenario."</i> ⚠ In this editor each
    /// verify after the first is a reload (CE-3294) — the command line's <c>--verify</c> is the regression verdict.</para>
    /// </summary>
    internal sealed class DirectorWindow : ManagedWindow
    {
        public const string WindowId = "editor_director";

        private readonly Func<DirectorRunner?> _runner;
        private readonly Func<TakeStore?> _store;
        private enum Pick { Reel, Take, Verify }
        private Pick _pick = Pick.Reel;
        private int _reel, _scenario, _take, _verify;
        private readonly DirectorTakeSession _takes;
        private string? _message;

        // ⭐ CE-1364 T3 — the playback rate is the session's (the timeline's J / L change the same number)
        private float _speed { get => _takes.Speed; set => _takes.Speed = value; }

        public DirectorWindow(Func<DirectorRunner?> runner, Func<TakeStore?> store, DirectorTakeSession takes)
            : base(WindowId, "Director", "Scenario", WindowScope.PerspectiveBound)
        {
            _runner = runner;
            _store = store;
            _takes = takes ?? throw new ArgumentNullException(nameof(takes));
            IsOpen = false;
            TitleBarColor = Hrot.Editor.Windows.EditorWindowColor.TitleBar;
        }

        protected override void DrawClientArea()
        {
            var runner = _runner();
            var store = _store();
            if (runner == null || store == null) { ImGui.TextDisabled("The director is not available on this host."); return; }

            if (ImGui.RadioButton("Reel", _pick == Pick.Reel)) _pick = Pick.Reel;
            ImGui.SameLine();
            if (ImGui.RadioButton("One take", _pick == Pick.Take)) _pick = Pick.Take;
            ImGui.SameLine();
            if (ImGui.RadioButton("Verify", _pick == Pick.Verify)) _pick = Pick.Verify;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Run a scenario as the regression check does: judged by its checks.json, compared with its golden.json.");

            if (_pick == Pick.Verify) { DrawVerify(runner, store); DrawStatus(runner); return; }

            Reel? chosen = null;
            if (_pick == Pick.Reel)
            {
                var reels = store.ReelNames();
                if (reels.Count == 0) ImGui.TextDisabled($"No reels (films/*.reel.json{(store.FilmsRoot == null ? " — no films/ folder found" : "")}).");
                else
                {
                    _reel = Math.Clamp(_reel, 0, reels.Count - 1);
                    var names = reels.ToArray();
                    ImGui.Combo("##reel", ref _reel, names, names.Length);
                    var loaded = store.LoadReel(reels[_reel]);
                    chosen = loaded.Value;
                    if (loaded.Problems.Count > 0) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.6f, 0.2f, 1f), loaded.Problems[0]);
                }
            }
            else
            {
                var all = store.AllTakes();
                if (all.Count == 0) ImGui.TextDisabled("No takes (scenarios/<name>/takes/*.take.json).");
                else
                {
                    _scenario = Math.Clamp(_scenario, 0, all.Count - 1);
                    var scenarios = all.Select(x => x.Scenario).ToArray();
                    if (ImGui.Combo("Scenario", ref _scenario, scenarios, scenarios.Length)) _take = 0;
                    var takes = all[_scenario].Takes.ToArray();
                    _take = Math.Clamp(_take, 0, takes.Length - 1);
                    ImGui.Combo("Take", ref _take, takes, takes.Length);
                    chosen = Reel.Of(scenarios[_scenario], takes[_take]);
                }
            }

            float speed = _speed;
            ImGui.SliderFloat("Speed", ref speed, 0.25f, 2f, "%.2fx");
            _speed = speed;
            if (runner.Busy && ImGui.IsItemDeactivatedAfterEdit()) runner.SetSpeed(_speed);
            if (_pick == Pick.Take && chosen != null) DrawEdit(chosen.Items[0].Scenario, chosen.Items[0].Take);

            ImGui.BeginDisabled(chosen == null);
            if (ImGui.Button("Play")) Start(runner, new PlayRequest(chosen!, _speed));
            ImGui.SameLine();
            if (ImGui.Button("Prepare (scout)")) Start(runner, new PlayRequest(chosen!, Scout: true));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Runs each take once, stepped as the film will be, and bakes its anchor times into the take.");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!runner.Busy);
            if (ImGui.Button("Stop")) runner.Stop();
            ImGui.SameLine();
            if (ImGui.Button("Next")) runner.Next();
            ImGui.SameLine();
            if (ImGui.Button(runner.Paused ? "Resume" : "Pause")) { if (runner.Paused) runner.Resume(); else runner.Pause(); }
            ImGui.EndDisabled();

            DrawStatus(runner);
        }

        /// <summary>⭐ CE-1364 T3 — open the picked take on the Event Timeline's Camera / Time tracks (one <see cref="TakeEditor"/>, shared with the panel); save · close · undo · redo.</summary>
        private void DrawEdit(string scenario, string take)
        {
            var editor = _takes.Editor;
            bool isOpen = editor != null && editor.Take.Scenario == scenario && editor.Take.Name == take;
            if (!isOpen)
            {
                if (ImGui.Button("Edit on the timeline")) _message = _takes.Open(scenario, take) is { Count: > 0 } p ? string.Join("\n", p) : null;
                return;
            }
            ImGui.TextUnformatted($"Editing {scenario}/{take}{(editor!.Dirty ? " * (unsaved - Play runs the saved file)" : "")} - Event Timeline: K adds a key, drag to move, Ctrl+Z undoes");
            ImGui.BeginDisabled(!editor.Dirty);
            if (ImGui.Button("Save")) _message = _takes.Save() is not null ? null : "nothing to save";
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!_takes.CanUndo);
            if (ImGui.Button("Undo")) _takes.UndoLast();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!_takes.CanRedo);
            if (ImGui.Button("Redo")) _takes.RedoLast();
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Close")) _takes.Close();
        }

        /// <summary>⭐ CE-4042 — pick a scenario that has checks; Verify, or re-record its golden (refused for a failing run).</summary>
        private void DrawVerify(DirectorRunner runner, TakeStore store)
        {
            var names = store.ScenariosWith(ChecksJson.FileName);
            if (names.Count == 0) { ImGui.TextDisabled("No scenario has checks (scenarios/<name>/checks.json)."); return; }
            _verify = Math.Clamp(_verify, 0, names.Count - 1);
            var arr = names.ToArray();
            ImGui.Combo("Scenario##verify", ref _verify, arr, arr.Length);
            ImGui.BeginDisabled(runner.Busy);
            if (ImGui.Button("Verify")) Start(runner, VerifyRequest(arr[_verify], capture: false));
            ImGui.SameLine();
            if (ImGui.Button("Re-record golden")) Start(runner, VerifyRequest(arr[_verify], capture: true));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Runs it and writes golden.json (working copy and source tree) — refused when a condition fails. Read its diff before committing.");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!runner.Busy);
            if (ImGui.Button("Stop")) runner.Stop();
            ImGui.EndDisabled();
        }

        private static PlayRequest VerifyRequest(string scenario, bool capture) =>
            new(Reel.Of(scenario, "_verify"), Verify: new VerifyOptions(capture));

        private void DrawStatus(DirectorRunner runner)
        {
            if (_message != null) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.4f, 1f), _message);
            var st = runner.Status();
            ImGui.Separator();
            ImGui.Text($"{st["phase"]}  {st["mode"]}  item {(int?)st["item"] + 1}/{st["items"]}  shot {st["shot"]}  t {st["simTime"]}  ×{st["realtimeFactor"]}");
            if (st["error"] is { } err) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), err.ToString());

            if (ImGui.BeginTable("##reports", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("");
                ImGui.TableSetupColumn("take");
                ImGui.TableSetupColumn("report");
                ImGui.TableSetupColumn("");
                for (int i = 0; i < runner.Reports.Count; i++)
                {
                    var r = runner.Reports[i];
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r.Status switch
                    {
                        "ok" when r.Mismatches.Count == 0 && r.Problems.Count == 0 => "OK",
                        "ok" => "!",
                        "pass" => "OK",
                        "beat-missing" or "failed" or "fail" => "X",
                        "playing" or "loading" => ">",
                        _ => "-",
                    });
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{r.Scenario}/{r.Take}");
                    ImGui.TableNextColumn();
                    var v = r.Verify;
                    var line = v != null
                        ? $"{r.Status} · {v.Verdicts.Count(x => x.Ok)}/{v.Verdicts.Count} conditions · golden {v.Golden}"
                        : r.Status + (r.Missing.Count > 0 ? $" · missing {string.Join(", ", r.Missing)}" : "")
                                   + (r.Mismatches.Count > 0 ? $" · {r.Mismatches.Count} drifted" : "")
                                   + (r.Steps > 0 ? $" · ×{r.RealtimeFactor:0.00}" : "");
                    ImGui.TextUnformatted(line);
                    if (ImGui.IsItemHovered() && v != null)
                        ImGui.SetTooltip(string.Join("\n", v.Verdicts.Select(x => $"{(x.Ok ? "PASS" : "FAIL")}  {x.Name}{(x.Time is { } t ? $"  ({t:0.##} s)" : "")}{(x.Outside is { } o ? $"  — held at {o:0.##} s, outside its window" : "")}")
                            .Concat(v.GoldenDiff).Concat(v.Problems).Concat(r.Problems)));
                    else if (ImGui.IsItemHovered() && (r.Problems.Count + r.Mismatches.Count + r.Unresolved.Count) > 0)
                        ImGui.SetTooltip(string.Join("\n", r.Problems.Concat(r.Mismatches).Concat(r.Unresolved)));
                    ImGui.TableNextColumn();
                    ImGui.PushID(i);
                    ImGui.BeginDisabled(runner.Busy);
                    if (ImGui.SmallButton(v != null ? "Again" : "Replay"))
                        Start(runner, v != null ? VerifyRequest(r.Scenario, capture: false) : new PlayRequest(Reel.Of(r.Scenario, r.Take), _speed));
                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
        }

        private void Start(DirectorRunner runner, PlayRequest request)
        {
            var problems = runner.Play(request);
            _message = problems.Count == 0 ? null : string.Join("\n", problems.Take(6));
        }
    }
}
