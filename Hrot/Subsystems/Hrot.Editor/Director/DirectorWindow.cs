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
    /// </summary>
    internal sealed class DirectorWindow : ManagedWindow
    {
        public const string WindowId = "editor_director";

        private readonly Func<DirectorRunner?> _runner;
        private readonly Func<TakeStore?> _store;
        private bool _reelMode = true;
        private int _reel, _scenario, _take;
        private float _speed = 1f;
        private string? _message;

        public DirectorWindow(Func<DirectorRunner?> runner, Func<TakeStore?> store)
            : base(WindowId, "Director", "Scenario", WindowScope.PerspectiveBound)
        {
            _runner = runner;
            _store = store;
            IsOpen = false;
            TitleBarColor = Hrot.Editor.Windows.EditorWindowColor.TitleBar;
        }

        protected override void DrawClientArea()
        {
            var runner = _runner();
            var store = _store();
            if (runner == null || store == null) { ImGui.TextDisabled("The director is not available on this host."); return; }

            if (ImGui.RadioButton("Reel", _reelMode)) _reelMode = true;
            ImGui.SameLine();
            if (ImGui.RadioButton("One take", !_reelMode)) _reelMode = false;

            Reel? chosen = null;
            if (_reelMode)
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

            ImGui.SliderFloat("Speed", ref _speed, 0.25f, 2f, "%.2fx");
            if (runner.Busy && ImGui.IsItemDeactivatedAfterEdit()) runner.SetSpeed(_speed);

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
                        "beat-missing" or "failed" => "X",
                        "playing" or "loading" => ">",
                        _ => "-",
                    });
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{r.Scenario}/{r.Take}");
                    ImGui.TableNextColumn();
                    var line = r.Status + (r.Missing.Count > 0 ? $" · missing {string.Join(", ", r.Missing)}" : "")
                             + (r.Mismatches.Count > 0 ? $" · {r.Mismatches.Count} drifted" : "")
                             + (r.Steps > 0 ? $" · ×{r.RealtimeFactor:0.00}" : "");
                    ImGui.TextUnformatted(line);
                    if (ImGui.IsItemHovered() && (r.Problems.Count + r.Mismatches.Count + r.Unresolved.Count) > 0)
                        ImGui.SetTooltip(string.Join("\n", r.Problems.Concat(r.Mismatches).Concat(r.Unresolved)));
                    ImGui.TableNextColumn();
                    ImGui.PushID(i);
                    ImGui.BeginDisabled(runner.Busy);
                    if (ImGui.SmallButton("Replay")) Start(runner, new PlayRequest(Reel.Of(r.Scenario, r.Take), _speed));
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
