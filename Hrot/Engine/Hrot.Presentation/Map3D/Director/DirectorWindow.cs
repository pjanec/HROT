using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.WindowManager;
using ImGuiNET;

namespace Hrot.UI.Common.Map3D.Director
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
    public sealed class DirectorWindow : ManagedWindow, IDirectorTabContext
    {
        public const string WindowId = "editor_director";

        private readonly Func<DirectorRunner?> _runner;
        private readonly Func<TakeStore?> _store;
        private readonly IReadOnlyList<IDirectorWindowTab> _tabs;   // ⭐ CE-1303 P1 — contributed by the host, drawn after the film tabs
        /// <summary>The tabs the host contributed, after the film tabs.</summary>
        public IReadOnlyList<IDirectorWindowTab> Tabs => _tabs;
        private const int PickReel = 0, PickTake = 1;   // 2 + i = _tabs[i]
        private int _pick = PickReel;
        private int _reel, _scenario, _take;
        private DirectorRunner? _ctxRunner;
        private TakeStore? _ctxStore;
        private readonly DirectorTakeSession _takes;
        private string? _message;

        // ⭐ CE-1364 T3 — the playback rate is the session's (the timeline's J / L change the same number)
        private float _speed { get => _takes.Speed; set => _takes.Speed = value; }

        private readonly Func<double>? _playhead;

        /// <param name="perspective">⭐ CE-1303 P2a (§11m D11) — the host's perspective ("Scenario" in the editor, "ReplayBrowser" in the RB).</param>
        /// <param name="playhead">⭐ CE-1303 P2a — the source's sim second now (the host's playhead): <i>Play from playhead</i> starts the take there.</param>
        public DirectorWindow(Func<DirectorRunner?> runner, Func<TakeStore?> store, DirectorTakeSession takes,
                              IReadOnlyList<IDirectorWindowTab>? tabs = null, System.Numerics.Vector4? titleBarColor = null,
                              string perspective = "Scenario", Func<double>? playhead = null)
            : base(WindowId, "Director", perspective, WindowScope.PerspectiveBound)
        {
            _playhead = playhead;
            _runner = runner;
            _store = store;
            _takes = takes ?? throw new ArgumentNullException(nameof(takes));
            _tabs = tabs ?? Array.Empty<IDirectorWindowTab>();
            IsOpen = false;
            if (titleBarColor is { } c) TitleBarColor = c;
        }

        protected override void DrawClientArea()
        {
            var runner = _runner();
            var store = _store();
            if (runner == null || store == null) { ImGui.TextDisabled("The director is not available on this host."); return; }

            if (ImGui.RadioButton("Reel", _pick == PickReel)) _pick = PickReel;
            ImGui.SameLine();
            if (ImGui.RadioButton("One take", _pick == PickTake)) _pick = PickTake;
            for (int i = 0; i < _tabs.Count; i++)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton(_tabs[i].Name, _pick == 2 + i)) _pick = 2 + i;
            }

            if (_pick >= 2 && _pick - 2 < _tabs.Count)
            {
                _ctxRunner = runner; _ctxStore = store;
                _tabs[_pick - 2].Draw(this);
                DrawStatus(runner);
                return;
            }

            Reel? chosen = null;
            if (_pick == PickReel)
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
            if (_pick == PickTake && chosen != null) DrawEdit(chosen.Items[0].Scenario, chosen.Items[0].Take);

            ImGui.BeginDisabled(chosen == null);
            if (ImGui.Button("Play")) Start(runner, new PlayRequest(chosen!, _speed));
            ImGui.SameLine();
            if (ImGui.Button("Prepare (scout)")) Start(runner, new PlayRequest(chosen!, Scout: true));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Runs each take once, stepped as the film will be, and bakes its anchor times into the take.");
            // ⭐ CE-1303 P2a — CR-4 (§11m D5–D7): iterate on the take — play it from the playhead, the panels kept on screen to edit it, and loop the
            //   shot so the next pass shows the edit (a recording seeks; the editor fast-forwards to the playhead and cannot loop)
            if (_pick == PickTake && _playhead != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Play from playhead"))
                {
                    // ⭐ CE-1303 P2b (§11o S5) — the playhead is a SOURCE second; the film second that shows it comes from the take's segments
                    var request = new PlayRequest(chosen!, _speed, Cinema: false);
                    StartFrom(runner, request, runner.FilmTimeAt(request, _playhead()));
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Plays this take from the playhead's time, the timeline kept on screen: the open take's unsaved edits are played.");
            }
            ImGui.EndDisabled();
            bool loop = runner.LoopShot;
            if (ImGui.Checkbox("Loop shot", ref loop)) runner.LoopShot = loop;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("At the end of the shot on screen, go back to its start (a recording only) — an edit shows on the next pass.");
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
            ImGui.TextUnformatted($"Editing {scenario}/{take}{(editor!.Dirty ? " * (unsaved - Play plays these edits)" : "")} - Event Timeline: K adds a key, drag to move, Ctrl+Z undoes");
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

            // ⭐ CE-1303 P2b (§11o S11) — the camera of the selected key (else the key on screen at the playhead) from the pilot's view; the pilot ⇄ program view
            if (_takes.Director is not { } tracks) return;
            if (ImGui.Button("Update key from view") && !tracks.UpdateKeyFromView(_playhead?.Invoke() ?? 0d))
                _message = "no key to update (select one, or put the playhead after one) or no 3-D pilot camera";
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The selected camera key (else the key on screen at the playhead) takes the view you flew to. U on the timeline. Ctrl+Z undoes.");
            ImGui.SameLine();
            bool pilot = FilmStudio.Pilot;
            ImGui.BeginDisabled(IsCapturing());
            if (ImGui.Checkbox("Pilot view", ref pilot)) FilmStudio.Pilot = pilot;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: you fly the map camera while the take plays (the program keeps running). Off: the director's camera.");
            DrawSelected(editor!, tracks);
        }

        private bool IsCapturing() => _runner() is { Busy: true, Mode: DirectorMode.Capture };

        /// <summary>⭐ CE-1303 P2b — the selected time segment's speed and gap, or the selected subtitle's text, position and fade (each change one undo step).</summary>
        private void DrawSelected(TakeEditor editor, DirectorTracks tracks)
        {
            var take = editor.Take;
            if (tracks.SelectedSegment is { } si && si < take.Segments.Count)
            {
                var seg = take.Segments[si];
                float speed = (float)seg.Speed;
                ImGui.SetNextItemWidth(160f);
                ImGui.SliderFloat("Segment speed", ref speed, 0.1f, 8f, "%.2fx", ImGuiSliderFlags.Logarithmic);
                if (ImGui.IsItemDeactivatedAfterEdit() && speed > 0f) editor.Apply(new TakeEdit.SetSegment(si, seg with { Speed = Math.Round(speed, 2) }));
                ImGui.SameLine();
                bool dip = seg.Gap == SegmentGap.Dip;
                if (ImGui.Checkbox("Dip into it", ref dip)) editor.Apply(new TakeEdit.SetSegment(si, seg with { Gap = dip ? SegmentGap.Dip : SegmentGap.Cut }));
            }
            else if (tracks.SelectedSubtitle is { } ui && TakeSubtitles.Of(take, TakeTimeline.Plan(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null)) is { } subs && ui < subs.Count)
            {
                var sub = subs[ui];
                if (_subtitleEdit?.Index != ui || _subtitleEdit.Value.Version != editor.Version) _subtitleEdit = (ui, editor.Version, sub.Text);
                string text = _subtitleEdit.Value.Text;
                ImGui.SetNextItemWidth(320f);
                if (ImGui.InputText("Subtitle", ref text, 512)) _subtitleEdit = (ui, editor.Version, text);
                if (ImGui.IsItemDeactivatedAfterEdit() && text != sub.Text) editor.Apply(new TakeEdit.SetSubtitle(ui, sub with { Text = text }));
                int pos = (int)sub.Position;
                ImGui.SetNextItemWidth(120f);
                if (ImGui.Combo("Position", ref pos, SubtitlePositions, SubtitlePositions.Length)) editor.Apply(new TakeEdit.SetSubtitle(ui, sub with { Position = (SubtitlePosition)pos }));
                ImGui.SameLine();
                float fade = (float)sub.Fade;
                ImGui.SetNextItemWidth(120f);
                ImGui.SliderFloat("Fade", ref fade, 0f, 2f, "%.1f s");
                if (ImGui.IsItemDeactivatedAfterEdit()) editor.Apply(new TakeEdit.SetSubtitle(ui, sub with { Fade = Math.Round(fade, 2) }));
            }
        }

        private static readonly string[] SubtitlePositions = { "bottom", "top", "middle" };
        private (int Index, int Version, string Text)? _subtitleEdit;

        DirectorRunner IDirectorTabContext.Runner => _ctxRunner!;
        TakeStore IDirectorTabContext.Store => _ctxStore!;
        void IDirectorTabContext.Start(PlayRequest request) { if (_ctxRunner != null) Start(_ctxRunner, request); }

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
                    IDirectorWindowTab? owner = null;
                    string? owned = null;
                    foreach (var tab in _tabs) if ((owned = tab.ReportLine(r)) != null) { owner = tab; break; }
                    var line = owned
                        ?? r.Status + (r.Missing.Count > 0 ? $" · missing {string.Join(", ", r.Missing)}" : "")
                                   + (r.Mismatches.Count > 0 ? $" · {r.Mismatches.Count} drifted" : "")
                                   + (r.Steps > 0 ? $" · ×{r.RealtimeFactor:0.00}" : "");
                    ImGui.TextUnformatted(line);
                    if (ImGui.IsItemHovered() && owner?.ReportDetail(r) is { } detail)
                        ImGui.SetTooltip(string.Join("\n", detail));
                    else if (ImGui.IsItemHovered() && (r.Problems.Count + r.Mismatches.Count + r.Unresolved.Count) > 0)
                        ImGui.SetTooltip(string.Join("\n", r.Problems.Concat(r.Mismatches).Concat(r.Unresolved)));
                    ImGui.TableNextColumn();
                    ImGui.PushID(i);
                    ImGui.BeginDisabled(runner.Busy);
                    if (ImGui.SmallButton(owner != null ? "Again" : "Replay"))
                        Start(runner, owner?.Again(r) ?? new PlayRequest(Reel.Of(r.Scenario, r.Take), _speed));
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

        private void StartFrom(DirectorRunner runner, PlayRequest request, double filmTime)
        {
            var problems = runner.PlayFrom(request, filmTime);
            _message = problems.Count == 0 ? null : string.Join("\n", problems.Take(6));
        }
    }
}
