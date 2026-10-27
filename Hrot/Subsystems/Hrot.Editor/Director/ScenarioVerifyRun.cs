using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>⭐ <c>CE-4042</c> — a verify run: <see cref="Capture"/> re-records each scenario's golden (refused for a failing run).
    /// ⭐ <c>CE-4045</c> H1a — <see cref="Replay"/>: the run is LIVE (it records), and its recording, played back, must hold the world the run
    /// had at every snapshot (docs/DESIGN_Scenario_Regression.md §10-1, R-4085). ⭐ H1b — then the PRODUCT's cluster replay of that run
    /// (entered and sought through the master, by recorded sim time) must show the same world (§10-2 ②).
    /// ⭐ <c>CE-4061</c> — a <see cref="RunPlan"/>: the runner hands it to <see cref="ScenarioVerifyRun"/>.
    /// ⭐ H3 — <see cref="BranchAt"/> (with <see cref="Replay"/>): after H1b / H2, the replay is sought to that run time, BRANCHED live there and
    /// run on to <c>runFor</c>; the branch must go on as the straight live run did (§10-7).</summary>
    public sealed record VerifyOptions(bool Capture = false, RestoreCheck? R1 = null, bool Replay = false, ReviewCheck? Review = null,
                                       double? BranchAt = null) : RunPlan;

    /// <summary>
    /// ⭐ <c>CE-4045</c> H5 (docs/DESIGN_Scenario_Regression.md §10-3, R-4086) — one half of the review check. Both halves run LIVE and write
    /// their full snapshots (every <see cref="Every"/> s) to <see cref="RunOut"/>. <see cref="Straight"/>: just that. Otherwise: at
    /// <see cref="At"/> the run enters a During Action Review, seeks every earlier snapshot time and compares it with the live one, returns
    /// to live, compares the world with the one it left, and runs on; the command line compares the two halves from <see cref="At"/>.
    /// </summary>
    public sealed record ReviewCheck(double At, bool Straight, string RunOut, double Every = 1d);

    /// <summary>
    /// ⭐ <c>CE-3299</c> B5 — one half of R1, the restore-equivalence oracle (restore design §1k). <see cref="Restore"/> false: run from 0 and
    /// write checkpoint <c>r1</c> to <see cref="File"/> at <see cref="At"/> (after that moment's snapshot, before its actions). True: put
    /// <see cref="File"/> back at the start and run on from <see cref="At"/>. Both snapshot every <see cref="Every"/> s, every component in
    /// full, and write the run to <see cref="RunOut"/>; the command line compares the two from <see cref="At"/>.
    /// </summary>
    public sealed record RestoreCheck(double At, string File, bool Restore, string RunOut, double Every = 1d)
    {
        /// <summary>Beside the checkpoint: the time it was written and the actions already applied (the restored run must not repeat them).</summary>
        public string SidecarPath => File + ".r1.json";
    }

    /// <summary>⭐ <c>CE-4061</c> — a director report's verify result (null on a film or a scout).</summary>
    public static class VerifyReports
    {
        public static VerifyResult? Verify(this ItemReport report) => report.Result as VerifyResult;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4061</c> (docs/DESIGN_Director_Timeline_Editor.md §11h, CR-1) — the editor's SCENARIO VERIFIER, plugged into the film
    /// <see cref="DirectorRunner"/> through its one seam (<see cref="IRunExtension"/>). It was the runner's own code until the film runner had
    /// to be shared (the Replay Browser, P1); the behaviour is unchanged:
    /// <list type="bullet">
    /// <item><b>CE-4042</b> — each scenario's checks over its run, and its golden (docs/DESIGN_Scenario_Regression.md);</item>
    /// <item><b>CE-3299 B5</b> — R1, the restore-equivalence halves (a checkpoint written, or put back);</item>
    /// <item><b>CE-4045 H1a</b> — a LIVE run against its own recording; <b>H1b</b> — the product's replay of it; <b>H5</b> — a review of the live run.</item>
    /// </list>
    /// The runner loads, pauses and steps (one step per frame); this decides the take it runs (no camera), observes each frame, holds the step
    /// where it looks back, and judges the item — in its own phases when it looks back (<see cref="PhaseName"/>).
    /// </summary>
    public sealed class ScenarioVerifyRun : IRunExtension
    {
        private readonly IVerifyHost _vhost;
        private readonly Func<IShotWorld, IVerifyWorld?> _verifyWorldOf;
        private IRunContext _ctx = null!;
        private IVerifyWorld? _verifyWorld;

        private ScenarioVerification? _verification;
        private bool _firstLoad;
        private double _t0;
        private string _phase = "";
        // ⭐ CE-3299 B5 — R1: the clock a restored checkpoint moves to, the frames waited for it, and whether the checkpoint was written
        private double _r1Target = double.NaN;
        private int _r1Frames;
        private bool _r1Written;
        // ⭐ CE-4045 H1a — the live run's recording: its file, the sim frame the run began on, and the comparison in progress
        private string? _liveRecording;
        private long _simFrame0;
        private VerifyResult? _pendingVerify;
        private IRecordedRun? _recorded;
        private ScenarioGolden? _recordedSnapshots;
        private int _checkIndex;
        // ⭐ CE-4045 H5 / H1b — the LOOK-BACK, one loop for both: a review of the live run (H5) or a replay of the finished one (H1b). The live
        //   snapshots to look back at, the one being looked at, how long the clock has stood there
        private enum LookMode { Review, Replay, Seek, Branch }
        private LookMode _look;
        private bool _reviewDone;   // H5: the run's review happened (or could not)
        private bool _lookEntered;  // the product is in the review / replay (a replay is entered from Idle, so it waits for it)
        private List<double>? _lookTimes;
        private JsonArray? _lookLive;
        private int _lookIndex;
        private double _lookTarget, _liveAtRel, _phaseStartWall;
        private int _standing;
        private ScenarioGolden? _lookSnapshots, _atLive;
        // the outcomes, kept until the run's result exists (Finish for the review, EndPlayback for the replay)
        private LookBackResult? _review, _playback, _seek, _branch;   // ⭐ H2 — _seek: the replay sought again, shuffled · H3 — _branch
        private readonly List<string> _returnDiff = new();
        private LookBackResult Look => _look switch { LookMode.Review => _review!, LookMode.Replay => _playback!, LookMode.Seek => _seek!, _ => _branch! };
        private string LookTag => _look switch { LookMode.Review => "H5", LookMode.Replay => "H1b", LookMode.Seek => "H2", _ => "H3" };
        private string LookNoun => _look switch { LookMode.Review => "review", LookMode.Replay => "replay", LookMode.Seek => "seek", _ => "branch" };
        // ⭐ CE-4045 H3 — the branch: its run time (on the snapshot grid), the actions the live run had applied by then, the snapshot step,
        //   where the branch stands (0 = seeking the replay there · 1 = asked to go live · 2 = running on), and the clock it came live on
        private double _branchT = double.NaN, _every = 1d, _branchClock = double.NaN;
        private IReadOnlyList<int>? _doneAtBranch;
        private int _branchStage;
        /// <summary>⭐ H2 — the shuffle's seed: one order, the same on every run (a difference repeats).</summary>
        public const int SeekShuffleSeed = 4045;

        /// <param name="host">the editor's host — checkpoints, the live run, its review and replay.</param>
        /// <param name="verifyWorld">⭐ CE-4042 — what a verify run reads and edits (the editor's entity dump, combat rows and component
        /// edit), over the runner's units; null when the host has none (a verify run is then refused).</param>
        public ScenarioVerifyRun(IVerifyHost host, Func<IShotWorld, IVerifyWorld?> verifyWorld)
        {
            _vhost = host ?? throw new ArgumentNullException(nameof(host));
            _verifyWorldOf = verifyWorld ?? throw new ArgumentNullException(nameof(verifyWorld));
        }

        private IDirectorHost H => _ctx.Host;
        private ItemReport Report => _ctx.Report;
        private ReelItem Item => _ctx.Item;
        private VerifyOptions Options => (VerifyOptions)_ctx.Request.Plan!;
        private bool ReplayCheck => Options.Replay;   // ⭐ CE-4045 H1a — this item runs LIVE and is compared with its recording
        private static double LoadTimeoutSeconds => DirectorRunner.LoadTimeoutSeconds;

        // ── the seam ───────────────────────────────────────────────────────────────────────────────

        public void Attach(IRunContext context)
        {
            _ctx = context ?? throw new ArgumentNullException(nameof(context));
            _verifyWorld = _verifyWorldOf(context.Units);
        }

        public string ModeName => "Verify";
        public string PhaseName => _phase;

        public IReadOnlyList<string> Validate(PlayRequest request)
        {
            var problems = new List<string>();
            if (request.Plan is not VerifyOptions o) { problems.Add($"a {request.Plan?.GetType().Name} is not a verify plan"); return problems; }
            foreach (var it in request.Reel.Items)
                foreach (var p in LoadChecks(it.Scenario).Problems) problems.Add($"{it.Scenario}: {p}");
            if (_verifyWorld == null) problems.Add("this host cannot verify (no entity dump / component edit wired)");
            if (o.Replay && (o.Capture || o.R1 != null))
                problems.Add("a replay check runs LIVE: it records no golden and writes no checkpoint (drop capture / R1)");
            if (o.Review != null && (o.Capture || o.R1 != null || o.Replay))
                problems.Add("a review check runs LIVE on its own: no capture, no R1, no replay check in the same run");
            if (o.BranchAt is { } bt && (!o.Replay || !(bt > 0)))
                problems.Add("a branch (H3) is part of the replay check, at a run time after 0 (replay + branchAt > 0)");
            return problems;
        }

        /// <summary>⭐ CE-4045 — this item runs LIVE (it records): a replay check (H1a) or either half of a review check (H5).</summary>
        public bool LoadsLive(PlayRequest request) => request.Plan is VerifyOptions o && (o.Replay || o.Review != null);

        // ⭐ a first load in a fresh process: nothing loaded by a runner yet and no scenario open (the editor's own load leaves it
        //   OperatingEdit) — the only run a golden may be recorded from (Regression_Net §6 ④, CE-3294)
        public void BeforeLoad() => _firstLoad = !_vhost.HasLoadedAScenario;

        public void Load(string scenario) => _vhost.LoadLive(scenario);   // ⭐ H1a — a live run records (R-3053); the preview never does
        public bool IsLoaded => _vhost.IsOperatingLive;

        public RunStart? BeginItem() => RestoringCheckpoint() ? null : BeginVerify();

        public void OnFrame(double rel)
        {
            _verification?.Observe(rel);
            if (_ctx.Request.Plan is VerifyOptions o && (o.Replay || o.Review != null))
                _liveRecording ??= _vhost.LiveRecordingPath;   // ⭐ H1a — named while it records (the recorder forgets it on close)
        }

        public bool HoldsStep(double rel, long retired, long issued)
        {
            // ⭐ CE-4045 H5 — at the review's time: no step more; once the master has taken every step asked for, look back
            if (Options.Review is { Straight: false } rc && !_reviewDone && rel + 1e-9 >= rc.At)
            {
                if (retired >= issued) BeginReview(rel);
                return true;
            }
            return false;
        }

        public void BeforeStep(double rel)
        {
            // ⭐ CE-3316 — on a step with no slot run in flight (as the recorder writes one), a few steps late at most
            if (Options.R1 is { Restore: false } r1 && !_r1Written && rel + 1e-9 >= r1.At
                && (!_vhost.SlotRunInFlight || ++_r1Frames > Fdp.Toolkit.Replay.RecorderTickSystem.MaxCheckpointDeferSteps))
                WriteCheckpoint(r1, rel);
            // ⭐ CE-4045 H3 — the actions the straight run has applied when it stands at the branch time (as R1's checkpoint keeps them): the
            //   branch must not apply them again
            if (_look != LookMode.Branch && !double.IsNaN(_branchT) && _doneAtBranch == null && _verification != null && rel + 1e-9 >= _branchT)
                _doneAtBranch = _verification.DoneActions;
            _verification?.ApplyDue(rel);   // ⭐ an action lands on its exact step (every frame is a step boundary)
        }

        public bool Finish(ItemReport r)
        {
            if (_look == LookMode.Branch && _branchStage == 2) { FinishBranch(r); return true; }   // ⭐ H3 — the branch ran to runFor
            if (_verification == null) return false;
            var v = _verification.Finish(_ctx.Rel, r.Steps);
            if (!_firstLoad) v.MarkReload();
            if (Options.R1 is { } r1)   // ⭐ B5 — the run goes to the command line, which compares the two halves
            {
                if (!r1.Restore && !_r1Written) v.Problems.Add($"R1: the run ended before the checkpoint time {r1.At} s");
                if (v.GoldenJson != null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(r1.RunOut))!); File.WriteAllText(r1.RunOut, v.GoldenJson.ToJsonString()); }
            }
            if (v.Golden == "captured" && v.GoldenJson != null)
                v.Written.AddRange(H.Takes.SaveScenarioJson(Item.Scenario, ScenarioGolden.FileName, v.GoldenJson));
            if (Options.Review is { } rc)   // ⭐ H5 — the half goes to the command line, which compares the two from At
            {
                v.Golden = "none";
                if (v.GoldenJson != null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rc.RunOut))!); File.WriteAllText(rc.RunOut, v.GoldenJson.ToJsonString()); }
                if (!rc.Straight)
                {
                    if (!_reviewDone) v.Problems.Add($"H5: the run ended before the review time {rc.At} s");
                    v.Review = _review;
                    v.ReturnDiff.AddRange(_returnDiff);
                }
            }
            _verification = null;
            if (ReplayCheck) { BeginReplayCheck(v); return true; }
            r.Result = v;
            r.Status = v.Passed ? "pass" : "fail";
            return false;
        }

        public void Tick()
        {
            switch (_phase)
            {
                case "Checking": TickChecking(); break;
                case "Reviewing": TickReviewing(); break;
                case "Returning": TickReturning(); break;
                case "Branching": TickBranching(); break;
            }
        }

        public void Reset()
        {
            _verification = null; _phase = "";
            _r1Target = double.NaN; _r1Frames = 0; _r1Written = false;
            _liveRecording = null; _pendingVerify = null; _recordedSnapshots = null; _checkIndex = 0; CloseRecorded();
            _reviewDone = false; _lookEntered = false; _lookTimes = null; _lookLive = null; _lookIndex = 0; _standing = 0; _lookSnapshots = null; _atLive = null;
            _review = null; _playback = null; _seek = null; _branch = null; _returnDiff.Clear();
            _branchT = double.NaN; _branchClock = double.NaN; _doneAtBranch = null; _branchStage = 0; _look = LookMode.Review;
        }

        public void Stop() => CloseRecorded();

        private void Enter(string phase)
        {
            _phase = phase;
            _ctx.EnterExtensionPhase();
        }

        // ── the item ───────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ CE-4042 — the scenario's checks (and its golden): no take, no camera; the run lasts <c>runFor</c>, one step per frame.</summary>
        private RunStart? BeginVerify()
        {
            var loaded = LoadChecks(Item.Scenario);
            if (loaded.Value is not { } checks) { _ctx.Fail(string.Join("; ", loaded.Problems)); return null; }
            _ctx.Units.Reset();
            var missing = ScenarioVerification.MissingNames(checks, _ctx.Units);
            if (missing.Count > 0) { Report.Problems.AddRange(missing); _ctx.Fail("the checks name what this scenario does not have"); return null; }
            // ⭐ CE-4045 H1a — a replay check compares the run with its own recording, not with golden.json (a live run is not the preview
            //   the golden was recorded from — RH-2), every component in full, every second at least (📐 ua-tank-ammo's 10 s grid gave 7
            //   points for 60 s — too coarse to say WHEN a recording first drops something)
            bool replay = ReplayCheck;
            var review = Options.Review;
            bool live = replay || review != null;
            var golden = checks.GoldenEvery is null || live ? null : H.Takes.LoadScenarioJson(Item.Scenario, ScenarioGolden.FileName).Value;
            var take = new Take { Scenario = Item.Scenario, Name = "_verify", TickHz = 60, EndAt = new AtExpr(checks.RunFor) };
            var r1 = Options.R1;
            bool restored = r1 is { Restore: true };
            double at = restored ? ReadSidecar(r1!).At ?? r1!.At : 0d;   // ⭐ CE-3316 — where A really wrote it (on a quiet step)
            _t0 = restored ? H.SimTime - at : H.SimTime;   // ⭐ B5 — a restored run goes on from there, on the same grid
            _simFrame0 = SimFrameNow();
            double? every = r1?.Every ?? review?.Every ?? (replay ? Math.Min(checks.GoldenEvery ?? 1d, 1d) : null);
            _every = every ?? 1d;
            // ⭐ H3 — the branch time on the snapshot grid (a live snapshot there is what the branch starts from)
            if (Options.BranchAt is { } bt) _branchT = Math.Ceiling(bt / _every - 1e-4) * _every;
            _verification = new ScenarioVerification(checks, golden, Options.Capture, _verifyWorld!, _t0, startAt: at, snapshotEvery: every);
            if (restored) _verification.MarkDone(ReadSidecar(r1!).Done);
            return new RunStart(take, _t0);
        }

        /// <summary>
        /// ⭐ <c>CE-3299</c> B5 — R1's restoring half: the checkpoint is put back (the cluster restore's own sequence) and the clock asked to
        /// its mark, then the run waits until the clock is there. True while that is still going on.
        /// </summary>
        private bool RestoringCheckpoint()
        {
            if (Options.R1 is not { Restore: true } r1) return false;
            if (double.IsNaN(_r1Target))
            {
                try { _r1Target = _vhost.RestoreCheckpoint(r1.File); }
                catch (Exception ex) { _ctx.Fail($"R1: the checkpoint {r1.File} could not be restored — {ex.Message}"); }
                return true;
            }
            if (Math.Abs(H.SimTime - _r1Target) <= 1e-6) return false;
            if (++_r1Frames > 120) _ctx.Fail($"R1: the clock did not reach the checkpoint's {_r1Target} s (at {H.SimTime} s)");
            return true;
        }

        // ⭐ B5 — R1's writing half: the world at At, and beside it the actions already applied
        private void WriteCheckpoint(RestoreCheck r1, double rel)
        {
            _r1Written = true;
            if (File.Exists(r1.File)) File.Delete(r1.File);
            if (File.Exists(r1.File + ".meta.json")) File.Delete(r1.File + ".meta.json");
            _vhost.WriteCheckpoint(r1.File, "r1");
            var done = new JsonArray(_verification!.DoneActions.Select(i => (JsonNode?)i).ToArray());
            File.WriteAllText(r1.SidecarPath, new JsonObject { ["at"] = rel, ["doneActions"] = done }.ToJsonString());
        }

        /// <summary>⭐ B5 / CE-3316 — the checkpoint's sidecar: the run time it was written at, and the actions applied by then.</summary>
        public static (double? At, IReadOnlyList<int> Done) ReadSidecar(RestoreCheck r1)
        {
            if (!File.Exists(r1.SidecarPath)) return (null, Array.Empty<int>());
            var node = JsonNode.Parse(File.ReadAllText(r1.SidecarPath));
            var done = (node?["doneActions"] as JsonArray ?? new JsonArray())
                .Select(n => (int?)n ?? -1).Where(i => i >= 0).ToList();
            return ((double?)node?["at"], done);
        }

        private Loaded<ScenarioChecks> LoadChecks(string scenario)
        {
            var json = _ctx.Host.Takes.LoadScenarioJson(scenario, ChecksJson.FileName);
            if (json.Value is null) return new Loaded<ScenarioChecks>(null, json.Problems);
            var problems = new List<string>();
            var checks = ChecksJson.Parse(json.Value, scenario, problems);
            return new Loaded<ScenarioChecks>(problems.Count == 0 ? checks : null, problems);
        }

        // ── ⭐ CE-4045 H1a — the live run against its own recording (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) ─────────────

        /// <summary>The live part is over: the run's own snapshots are the reference. Stop the run, so the recorder closes the file.</summary>
        private void BeginReplayCheck(VerifyResult v)
        {
            _pendingVerify = v;
            v.Golden = "none";   // ⭐ RH-2 — a live run is not the preview a golden was recorded from: not compared here
            if (_liveRecording == null) { EndReplayCheck("failed", "the live run recorded nothing (no recording file was named while it ran)"); return; }
            _vhost.StopLive();
            Enter("Checking");
            _phaseStartWall = H.WallNow;
        }

        /// <summary>⭐ Once per frame: wait for the file to close, then match ONE live snapshot per frame to its recorded sim frame.</summary>
        private void TickChecking()
        {
            var v = _pendingVerify!;
            var live = (v.GoldenJson?["snapshots"] as JsonArray) ?? new JsonArray();
            if (_recorded == null)
            {
                if (!(_vhost.IsIdle && _vhost.LiveRecordingPath == null))
                {
                    if (H.WallNow - _phaseStartWall > LoadTimeoutSeconds)
                        EndReplayCheck("failed", $"the live run did not stop, or its recording did not close, within {LoadTimeoutSeconds:0} s");
                    return;
                }
                if (!File.Exists(_liveRecording)) { EndReplayCheck("failed", $"the recording {_liveRecording} is not on disk"); return; }
                _recorded = _vhost.OpenRecording(_liveRecording!);
                _recordedSnapshots = new ScenarioGolden(_recorded.World, full: true);
                _checkIndex = 0;
                return;
            }
            if (_checkIndex < live.Count)
            {
                double t = ConditionBook.Num(live[_checkIndex]?["t"]) ?? 0d;
                long frame = SimFrameAt(t);
                if (!_recorded.AdvanceToSimFrame(frame))
                {
                    EndReplayCheck("failed", $"the recording has no sim frame {frame} (t {t} s): it stands at {_recorded.SimFrame} — "
                                             + $"{_checkIndex} of {live.Count} snapshots matched");
                    return;
                }
                _recordedSnapshots!.Snapshot(t);
                _checkIndex++;
                return;
            }
            var carried = ScenarioGolden.CarriedBy(_liveRecording!);
            var liveJson = new JsonObject { ["snapshots"] = live.DeepClone() };
            var recorded = _recordedSnapshots!.SnapshotsJson();
            var diff = ScenarioGolden.CompareFrom(liveJson, recorded, 0d, carried);
            v.ReplaySnapshots = _checkIndex;
            if (carried == null) v.ReplayNotCarried.Add($"⚠ the recording has no schema manifest ({_liveRecording}.meta.json) — every component compared");
            else v.ReplayNotCarried.AddRange(ScenarioGolden.CompareNotCarried(liveJson, recorded, 0d, carried));
            v.ReplayDiff.AddRange(diff);
            EndReplayCheck(diff.Count == 0 ? "same" : "different", null);
        }

        private void EndReplayCheck(string verdict, string? why)
        {
            CloseRecorded();
            var v = _pendingVerify!;
            v.Replay = verdict;
            if (why != null) v.ReplayDiff.Insert(0, why);
            BeginPlayback(v);   // ⭐ H1b — the product's replay of the same run, whatever H1a found (a failed H1a is not a reason to skip it)
        }

        // ── ⭐ CE-4045 H1b — the product's cluster REPLAY of the finished run (docs/DESIGN_Scenario_Regression.md §10-2 ②) ──────────

        /// <summary>The recording is judged (H1a): look back at EVERY live snapshot through the master's replay — the same loop as the review
        /// (<see cref="TickReviewing"/>), entered from Idle and left to Idle.</summary>
        private void BeginPlayback(VerifyResult v)
        {
            _look = LookMode.Replay;
            _playback = new LookBackResult();
            if (!_vhost.IsIdle) _vhost.StopLive();   // ⚠ a replay is entered from Idle; a run that did not stop (H1a failed) is stopped, never left behind
            var live = (v.GoldenJson?["snapshots"] as JsonArray) ?? new JsonArray();
            if (!BeginLookBack(live, double.PositiveInfinity)) { EndPlayback(); return; }
            _phaseStartWall = H.WallNow;   // the wait for Idle is timed from here, not from H1a's check
            Enter("Reviewing");
        }

        /// <summary>The replay is left (Idle) and compared: the item is judged.</summary>
        private void EndPlayback()
        {
            var v = _pendingVerify!;
            _pendingVerify = null;
            v.Playback = _playback;
            v.Seek = _seek;
            v.Branch = _branch;
            if (_branch != null) { v.BranchAt = _branchT; v.BranchClock = double.IsNaN(_branchClock) ? null : _branchClock; }
            var r = Report;
            r.Result = v;
            r.Status = v.Passed ? "pass" : "fail";
            _ctx.EndItem();
        }

        private void CloseRecorded()
        {
            _recorded?.Dispose();
            _recorded = null;
        }

        /// <summary>The sim frame a live snapshot at run time <paramref name="t"/> was taken on — the frame the run began on, plus one per
        /// step of 1/60 s (a verify run steps at 60 Hz — <see cref="BeginVerify"/>).</summary>
        private long SimFrameAt(double t) => _simFrame0 + (long)Math.Round(t * Report.TickHz);

        private long SimFrameNow() => H.World is { } w && w.HasSingletonUnmanaged<Fdp.Core.GlobalTime>()
            ? w.GetSingletonUnmanaged<Fdp.Core.GlobalTime>().FrameNumber : 0;

        // ── ⭐ CE-4045 H5 / H1b — the LOOK-BACK: ONE loop for a During Action Review of the live run (docs/DESIGN_Scenario_Regression.md
        //    §10-3) and for the cluster REPLAY of the finished one (§10-2 ②). Enter, then for each live snapshot time wait until the world's
        //    clock STANDS there, snapshot, seek on; then come back (review: to live · replay: to Idle) and compare. The mode decides only the
        //    state it waits for, the entry call and the way back ───────────────────────────────────────────────────────────────────────────

        private double HalfStep => 0.5 / Math.Max(1, Report.TickHz);

        /// <summary>The live run stands at the review's time with nothing in flight: remember the world, review back to the start.</summary>
        private void BeginReview(double rel)
        {
            _look = LookMode.Review;
            _review = new LookBackResult();
            _liveAtRel = rel;
            _atLive = new ScenarioGolden(_verifyWorld!, full: true);
            _atLive.Snapshot(rel);
            var live = (_verification!.Run?.SnapshotsJson()["snapshots"] as JsonArray) ?? new JsonArray();
            if (!BeginLookBack(live, rel + 1e-6)) { _reviewDone = true; return; }
            _vhost.EnterReview(_lookTarget);
            _lookEntered = true;
            Enter("Reviewing");
        }

        /// <summary>The live snapshots up to <paramref name="upTo"/> are the moments to look back at; false when there are none.</summary>
        private bool BeginLookBack(JsonArray live, double upTo)
        {
            _lookLive = live;
            _lookTimes = live.Select(n => ConditionBook.Num(n?["t"]) ?? double.NaN).Where(t => t <= upTo).ToList();
            if (_lookTimes.Count == 0) { Look.Problems.Add($"{LookTag}: no live snapshot to look back at"); return false; }
            _lookSnapshots = new ScenarioGolden(_verifyWorld!, full: true);
            _lookIndex = 0;
            _lookTarget = _t0 + _lookTimes[0];
            _lookEntered = false;
            _standing = 0;
            _phaseStartWall = H.WallNow;
            return true;
        }

        /// <summary>⭐ Once per frame: wait until the clock STANDS at the target (a seek lands on a later frame), snapshot, seek on.</summary>
        private void TickReviewing()
        {
            if (!_lookEntered)   // ⭐ H1b — a replay is entered from Idle: once the master has stopped the live run
            {
                if (_vhost.IsIdle) { _vhost.EnterReplay(_lookTarget); _lookEntered = true; _phaseStartWall = H.WallNow; }
                else if (H.WallNow - _phaseStartWall > LoadTimeoutSeconds)
                {
                    Look.Problems.Add($"{LookTag}: the live run did not stop (Idle) within {LoadTimeoutSeconds:0} s — no replay to look at");
                    CompareLookBack();
                    EndPlayback();
                }
                return;
            }
            bool inLook = _look == LookMode.Review ? _vhost.IsOperatingReview : _vhost.IsOperatingReplay;
            if (!inLook || Math.Abs(H.SimTime - _lookTarget) > HalfStep) _standing = 0;
            else _standing++;
            double t = _lookTimes![_lookIndex];
            if (_standing >= 3)
            {
                _lookSnapshots!.Snapshot(t);
                _lookIndex++;
            }
            else if (H.WallNow - _phaseStartWall > LoadTimeoutSeconds)
            {
                Look.Problems.Add($"{LookTag}: the {LookNoun} did not stand at {t} s within {LoadTimeoutSeconds:0} s (state {(inLook ? LookNoun : "not " + LookNoun)}, clock {H.SimTime - _t0:0.####} s)");
                _lookIndex++;
            }
            else return;
            if (_lookIndex < _lookTimes.Count)
            {
                _lookTarget = _t0 + _lookTimes[_lookIndex];
                _standing = 0;
                _phaseStartWall = H.WallNow;
                if (inLook) _vhost.SeekReview(_lookTarget);   // ⭐ the same SeekReplayIntent for a review and a replay
                return;
            }
            if (_look == LookMode.Replay && BeginSeekCheck()) return;   // ⭐ H2 — the same replay, sought again in a shuffled order
            if (_look != LookMode.Review && BeginBranch()) return;      // ⭐ H3 — then the same replay, branched live
            if (_look == LookMode.Review) { _vhost.ExitReview(); _lookTarget = _t0 + _liveAtRel; }
            else _vhost.StopLive();   // ⭐ H1b / H2 — leave the replay: an Idle transition, nothing is left running
            _standing = 0;
            _phaseStartWall = H.WallNow;
            Enter("Returning");
        }

        /// <summary>⭐ Once per frame: a review waits until live stands at the point it left and compares the world; a replay waits for Idle.
        /// Then the look-back is compared with the live run.</summary>
        private void TickReturning()
        {
            if (_look != LookMode.Review)   // ⭐ H1b / H2 / H3 — back to Idle, then the item is judged
            {
                if (!_vhost.IsIdle)
                {
                    if (H.WallNow - _phaseStartWall <= LoadTimeoutSeconds) return;
                    Look.Problems.Add($"{LookTag}: the {LookNoun} did not stop (Idle) within {LoadTimeoutSeconds:0} s");
                }
                if (_look != LookMode.Branch) CompareLookBack();   // H3 is compared when its run ends (FinishBranch)
                EndPlayback();
                return;
            }
            if (!_vhost.IsOperatingLive || Math.Abs(H.SimTime - _lookTarget) > HalfStep) _standing = 0;
            else _standing++;
            if (_standing < 3)
            {
                if (H.WallNow - _phaseStartWall <= LoadTimeoutSeconds) return;
                _review!.Problems.Add($"H5: live did not come back to {_liveAtRel} s within {LoadTimeoutSeconds:0} s (clock {H.SimTime - _t0:0.####} s)");
            }
            var after = new ScenarioGolden(_verifyWorld!, full: true);
            after.Snapshot(_liveAtRel);
            _returnDiff.AddRange(ScenarioGolden.CompareFrom(_atLive!.SnapshotsJson(), after.SnapshotsJson(), _liveAtRel));
            CompareLookBack();
            _reviewDone = true;
            _phase = "";
            _ctx.ResumePlaying();
        }

        /// <summary>
        /// ⭐ <c>CE-4045</c> H2 (docs/DESIGN_Scenario_Regression.md §10, RH-6) — the in-order playback (H1b) is over and judged: seek the SAME
        /// replay to the same moments again in a shuffled, seeded order — backward seeks included — and compare each with what H1b showed
        /// there (not with live: a playback difference is H1b's, so H2 can only blame the seek). 📌 CE-4056 (a seek BACK stood one seek
        /// behind) was found live and only there; in-order seeks never go back. False when there is nothing to shuffle (fewer than two).
        /// </summary>
        private bool BeginSeekCheck()
        {
            var playback = ByTime(_lookSnapshots!.SnapshotsJson());
            var times = ((playback["snapshots"] as JsonArray) ?? new JsonArray()).Select(n => ConditionBook.Num(n?["t"]) ?? double.NaN)
                                                                              .Where(t => !double.IsNaN(t)).ToList();
            if (times.Count < 2) return false;
            CompareLookBack();   // H1b first, against live
            var order = new List<double>(times);
            var rng = new Random(SeekShuffleSeed);
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            if (order.SequenceEqual(times)) (order[0], order[1]) = (order[1], order[0]);   // never the in-order walk H1b already made
            _look = LookMode.Seek;
            _seek = new LookBackResult();
            _lookLive = playback["snapshots"] as JsonArray;   // the reference: what the playback showed at each moment
            _lookTimes = order;
            _lookSnapshots = new ScenarioGolden(_verifyWorld!, full: true);
            _lookIndex = 0;
            _lookTarget = _t0 + order[0];
            _standing = 0;
            _phaseStartWall = H.WallNow;
            _vhost.SeekReview(_lookTarget);
            return true;
        }

        // ── ⭐ CE-4045 H3 — the BRANCH: the replay, sought to T and branched LIVE (D18), runs on as the straight run did (docs/DESIGN_Scenario_
        //    Regression.md §10-7, RH-7). R1's oracle on the replay path: the straight run from T against the branch from T, on what the
        //    recording carries (the branch is rebuilt from it) ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>The look-back is over (in the replay): judge it, then seek the replay to T for the branch. False when no branch is asked.</summary>
        private bool BeginBranch()
        {
            if (Options.BranchAt is null) return false;
            CompareLookBack();   // H1b / H2 first
            _look = LookMode.Branch;
            _branch = new LookBackResult();
            if (_doneAtBranch == null)
            {
                _branch.Problems.Add($"H3: the live run never stood at the branch time {_branchT} s (runFor is shorter)");
                return false;
            }
            _branchStage = 0;
            _lookTarget = _t0 + _branchT;
            _standing = 0;
            _phaseStartWall = H.WallNow;
            _vhost.SeekReview(_lookTarget);
            Enter("Branching");
            return true;
        }

        /// <summary>⭐ Once per frame: stand at T in the replay, branch, then wait until the branch is live with its clock STANDING (paused) —
        /// and run on from there, its run time counted from the clock it came up on (📐 CE-4065: not T's sim time yet).</summary>
        private void TickBranching()
        {
            if (_branchStage == 0)
            {
                if (!_vhost.IsOperatingReplay || Math.Abs(H.SimTime - _lookTarget) > HalfStep) _standing = 0;
                else _standing++;
                if (_standing < 3) { BranchTimeout($"the replay did not stand at {_branchT} s (clock {H.SimTime - _t0:0.####} s)"); return; }
                _vhost.BranchLive();
                _branchStage = 1;
                _standing = 0;
                _branchClock = double.NaN;
                _phaseStartWall = H.WallNow;
                return;
            }
            if (!_vhost.IsOperatingLive) { BranchTimeout("the branch did not come live"); return; }
            if (!H.Paused) H.Pause();
            if (double.IsNaN(_branchClock) || Math.Abs(H.SimTime - _branchClock) > 1e-9) { _branchClock = H.SimTime; _standing = 0; return; }
            if (++_standing < 3) return;
            // the branch stands still on its own clock: the straight run's world at T must be what it holds — run on from here
            if (LoadChecks(Item.Scenario).Value is not { } checks) { _branch!.Problems.Add("H3: the checks could not be read again"); LeaveBranch(); return; }
            double t0 = H.SimTime - _branchT;
            _verification = new ScenarioVerification(checks, null, false, _verifyWorld!, t0, startAt: _branchT, snapshotEvery: _every);
            _verification.MarkDone(_doneAtBranch!);
            _branchStage = 2;
            _phase = "";
            _ctx.ResumePlaying(t0);
        }

        private void BranchTimeout(string why)
        {
            if (H.WallNow - _phaseStartWall <= LoadTimeoutSeconds) return;
            _branch!.Problems.Add($"H3: {why} within {LoadTimeoutSeconds:0} s");
            LeaveBranch();
        }

        /// <summary>The branch ran to <c>runFor</c>: compare it with the straight run from T, then leave it (Idle).</summary>
        private void FinishBranch(ItemReport r)
        {
            var vb = _verification!.Finish(_ctx.Rel, r.Steps);
            _verification = null;
            var live = new JsonObject { ["snapshots"] = ((_pendingVerify!.GoldenJson?["snapshots"] as JsonArray) ?? new JsonArray()).DeepClone() };
            var branch = new JsonObject { ["snapshots"] = ((vb.GoldenJson?["snapshots"] as JsonArray) ?? new JsonArray()).DeepClone() };
            var carried = _liveRecording is { } path ? ScenarioGolden.CarriedBy(path) : null;
            var result = _branch!;
            result.Snapshots = (branch["snapshots"] as JsonArray)!.Count;
            result.Diff.AddRange(ScenarioGolden.CompareFrom(live, branch, _branchT, carried));
            if (carried != null) result.NotCarried.AddRange(ScenarioGolden.CompareNotCarried(live, branch, _branchT, carried));
            else result.NotCarried.Add("⚠ no recording manifest was found — every component compared");
            result.Problems.AddRange(vb.Problems.Select(p => "H3 run: " + p));   // e.g. an action anchored on a moment before T (never seen here)
            LeaveBranch();
        }

        /// <summary>The branch is judged (or could not be): leave whatever runs for Idle, then the item is judged.</summary>
        private void LeaveBranch()
        {
            _branchStage = 3;
            var result = _branch!;
            result.Verdict = result.Diff.Count == 0 && result.Problems.Count == 0 ? "same" : "different";
            if (!_vhost.IsIdle) _vhost.StopLive();
            _standing = 0;
            _phaseStartWall = H.WallNow;
            Enter("Returning");
        }

        /// <summary>The snapshots in time order (a look-back may visit them in another).</summary>
        private static JsonObject ByTime(JsonObject golden)
        {
            var sorted = ((golden["snapshots"] as JsonArray) ?? new JsonArray())
                .OrderBy(n => ConditionBook.Num(n?["t"]) ?? double.NaN).Select(n => n!.DeepClone()).ToArray();
            var o = golden.DeepClone().AsObject();
            o["snapshots"] = new JsonArray(sorted);
            return o;
        }

        /// <summary>
        /// The look-back against the live run, on what the recording carries (what a review or a replay can show).
        /// ⚠ The comparator pairs snapshots by INDEX: compare only the live snapshots at the times the product actually STOOD at
        /// (📐 measured: a review that never reached 0 s shifted every later pair by one second).
        /// </summary>
        private void CompareLookBack()
        {
            var result = Look;
            var seen = ByTime(_lookSnapshots!.SnapshotsJson());   // ⭐ H2 visits in a shuffled order; the comparator pairs by index
            var seenTimes = ((seen["snapshots"] as JsonArray) ?? new JsonArray()).Select(n => ConditionBook.Num(n?["t"]) ?? double.NaN).ToList();
            var live = new JsonObject { ["snapshots"] = new JsonArray((_lookLive ?? new JsonArray())
                .Where(n => ConditionBook.Num(n?["t"]) is { } t && seenTimes.Any(st => Math.Abs(st - t) < 1e-4)).Select(n => n!.DeepClone()).ToArray()) };
            var carried = _liveRecording is { } path ? ScenarioGolden.CarriedBy(path) : null;
            result.Snapshots = _lookSnapshots.Snapshots;
            result.Diff.AddRange(ScenarioGolden.CompareFrom(live, seen, 0d, carried));
            if (carried != null) result.NotCarried.AddRange(ScenarioGolden.CompareNotCarried(live, seen, 0d, carried));
            else result.NotCarried.Add("⚠ no recording manifest was found — every component compared");
            result.Verdict = result.Diff.Count == 0 && result.Problems.Count == 0 ? "same" : "different";
        }
    }
}
