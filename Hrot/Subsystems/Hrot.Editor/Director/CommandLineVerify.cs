using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2a) — the editor's half of <c>--verify</c>: the runner sets
    /// <see cref="ScenariosVariable"/>; once the editor is ready this starts a verify run on the director, prints the verdict, writes the report
    /// and leaves through the ordered teardown with exit code 0 (every scenario passes) or 1. Called once per frame after the director's tick.
    /// </summary>
    public sealed class CommandLineVerify
    {
        public const string ScenariosVariable = "HROT_VERIFY";
        public const string CaptureVariable = "HROT_VERIFY_CAPTURE";
        public const string ReportVariable = "HROT_VERIFY_REPORT";
        /// <summary>Frames to wait in Idle before the first load (the editor's own start-up settles first).</summary>
        private const int SettleFrames = 30;
        /// <summary>The editor never reaching Idle is a failure, not a wait forever.</summary>
        private const int ReadyLimitFrames = 60 * 120;

        private readonly string[] _scenarios;
        private readonly bool _capture;
        private readonly string? _report;
        private int _frames, _idleFrames;
        private bool _started, _done;

        private CommandLineVerify(string[] scenarios, bool capture, string? report)
        {
            _scenarios = scenarios;
            _capture = capture;
            _report = report;
        }

        /// <summary>The request the runner left in the environment; null when this is not a verify run.</summary>
        public static CommandLineVerify? FromEnvironment()
        {
            var s = Environment.GetEnvironmentVariable(ScenariosVariable);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return new CommandLineVerify(s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Environment.GetEnvironmentVariable(CaptureVariable) == "1", Environment.GetEnvironmentVariable(ReportVariable));
        }

        /// <param name="ready">The editor can load a scenario now (its cluster is Idle).</param>
        /// <param name="exit">Leaves the frame loop through the ordered teardown (<c>SubsystemConfig.RequestAppExit</c>).</param>
        public void Tick(DirectorRunner director, bool ready, Action exit)
        {
            if (_done) return;
            _frames++;
            if (!_started)
            {
                _idleFrames = ready ? _idleFrames + 1 : 0;
                if (_idleFrames < SettleFrames)
                {
                    if (_frames > ReadyLimitFrames) Finish(2, $"the editor did not become ready to load in {ReadyLimitFrames} frames", null, exit);
                    return;
                }
                var reel = new Reel
                {
                    Name = "verify",
                    Items = _scenarios.Select(s => new ReelItem { Scenario = s, Take = "_verify" }).ToList(),
                };
                var refused = director.Play(new PlayRequest(reel, Verify: new VerifyOptions(_capture)));
                if (refused.Count > 0) { Finish(2, "not started: " + string.Join("; ", refused), null, exit); return; }
                _started = true;
                return;
            }
            if (director.Phase is not (DirectorPhase.Done or DirectorPhase.Stopped)) return;
            var items = director.Reports.Select(r => (JsonNode?)Item(r)).ToArray();
            bool passed = director.Reports.Count > 0 && director.Reports.All(r => r.Status == "pass");
            Finish(passed ? 0 : 1, director.Error, new JsonArray(items), exit);
        }

        private static JsonObject Item(ItemReport r)
        {
            var o = r.Verify?.ToJson() ?? new JsonObject();
            o["scenario"] = r.Scenario;
            o["status"] = r.Status;
            o["steps"] = r.Steps;
            o["simEnd"] = Math.Round(r.SimEnd, 3);
            o["wallSeconds"] = Math.Round(r.WallSeconds, 1);
            if (r.Problems.Count > 0) o["runProblems"] = new JsonArray(r.Problems.Select(p => (JsonNode?)p).ToArray());
            return o;
        }

        private void Finish(int code, string? error, JsonArray? items, Action exit)
        {
            _done = true;
            var report = new JsonObject { ["passed"] = code == 0, ["error"] = error, ["scenarios"] = items ?? new JsonArray() };
            Console.WriteLine($"[Verify] {(code == 0 ? "PASS" : "FAIL")}{(error != null ? " — " + error : "")}");
            foreach (var item in report["scenarios"]!.AsArray().OfType<JsonObject>()) PrintItem(item, Console.Out, "  ");
            if (!string.IsNullOrWhiteSpace(_report))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_report))!);
                File.WriteAllText(_report, report.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented));
            }
            Environment.ExitCode = code;
            exit();
        }

        /// <summary>
        /// ⭐ How this binary starts ANOTHER copy of itself with <paramref name="args"/> — the one place both the command line's parent
        /// (a child per scenario) and the HTTP <c>fresh</c> verify (a child per request) do it. Under <c>dotnet X.dll</c> the dll is the first
        /// argument; a published executable starts itself.
        /// </summary>
        public static System.Diagnostics.ProcessStartInfo SelfStartInfo(System.Collections.Generic.IEnumerable<string> args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment.Remove("HROT_DEBUG_API_PORT");   // a child takes a free port of its own
            psi.Environment.Remove(ScenariosVariable);
            return psi;
        }

        /// <summary>One scenario's report as lines: each verdict, the golden, what went wrong.</summary>
        public static void PrintItem(JsonObject? item, TextWriter w, string indent)
        {
            if (item is null) return;
            w.WriteLine($"{indent}{item["scenario"]}: {item["status"]} — {item["steps"]} steps, sim {item["simEnd"]} s, wall {item["wallSeconds"]} s");
            foreach (var v in (item["verdicts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                w.WriteLine($"{indent}  {((bool?)v["ok"] == true ? "PASS" : "FAIL")}  {v["name"]}{(v["t"] is { } t ? $"  (t {t} s)" : "")}{(v["kind"]?.ToString() == "never" && (bool?)v["ok"] != true ? "  ← broken" : "")}{(v["heldOutsideItsWindowAt"] is { } o ? $"  ← held at {o} s, outside its window" : "")}");
            if (item["golden"] is { } g) w.WriteLine($"{indent}  GOLDEN {g}");
            foreach (var d in (item["goldenDiff"] as JsonArray ?? new JsonArray())) w.WriteLine($"{indent}    {d}");
            foreach (var p in (item["written"] as JsonArray ?? new JsonArray())) w.WriteLine($"{indent}  wrote {p}");
            foreach (var p in (item["problems"] as JsonArray ?? new JsonArray())) w.WriteLine($"{indent}  PROBLEM {p}");
            foreach (var p in (item["runProblems"] as JsonArray ?? new JsonArray())) w.WriteLine($"{indent}  PROBLEM {p}");
        }
    }
}
