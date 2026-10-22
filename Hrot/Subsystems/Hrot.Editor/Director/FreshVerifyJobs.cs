using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2a) — a verify over HTTP that runs in a FRESH process: the same command line
    /// <c>--verify a,b --verify-out &lt;dir&gt; [--verify-capture]</c> started as a child of this editor, so each scenario is a first load in a
    /// process of its own (CE-3294) — the only run whose verdict is the regression verdict and the only one a golden may be recorded from.
    /// 🔒 User: <i>"Verify and re-record golden must be accessible from ai debug http as well as from command line."</i>
    /// </summary>
    public sealed class FreshVerifyJobs
    {
        private sealed record Job(string Id, IReadOnlyList<string> Scenarios, bool Capture, string Out, Process Process, DateTime Started);

        private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
        private int _next;

        /// <summary>Starts the child; the report lands in <c>&lt;out&gt;/summary.json</c> (read it with <see cref="Status"/>).</summary>
        public JsonObject Start(IReadOnlyList<string> scenarios, bool capture)
        {
            var id = $"v{System.Threading.Interlocked.Increment(ref _next)}-{DateTime.UtcNow:HHmmss}";
            var dir = Path.Combine(Path.GetTempPath(), "hrot-verify", "http-" + id);
            Directory.CreateDirectory(dir);
            var args = new List<string> { "--mode", "editor", "--headless", "--verify", string.Join(",", scenarios), "--verify-out", dir };
            if (capture) args.Add("--verify-capture");
            var psi = CommandLineVerify.SelfStartInfo(args);
            var log = new StreamWriter(Path.Combine(dir, "run.log")) { AutoFlush = true };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            p.Exited += (_, _) => { lock (log) log.Dispose(); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _jobs[id] = new Job(id, scenarios, capture, dir, p, DateTime.UtcNow);
            return Describe(_jobs[id]);
        }

        public JsonObject? Status(string id) => _jobs.TryGetValue(id, out var j) ? Describe(j) : null;

        public JsonArray List() => new(_jobs.Values.OrderBy(j => j.Started).Select(j => (JsonNode?)Describe(j, withReport: false)).ToArray());

        private static JsonObject Describe(Job j, bool withReport = true)
        {
            bool done = j.Process.HasExited;
            var o = new JsonObject
            {
                ["job"] = j.Id, ["state"] = done ? "done" : "running", ["exitCode"] = done ? j.Process.ExitCode : null,
                ["passed"] = done ? j.Process.ExitCode == 0 : null,
                ["scenarios"] = new JsonArray(j.Scenarios.Select(s => (JsonNode?)s).ToArray()), ["capture"] = j.Capture,
                ["out"] = j.Out, ["junit"] = Path.Combine(j.Out, "junit.xml"), ["log"] = Path.Combine(j.Out, "run.log"),
                ["seconds"] = Math.Round((DateTime.UtcNow - j.Started).TotalSeconds, 1),
            };
            var summary = Path.Combine(j.Out, "summary.json");
            if (withReport && done && File.Exists(summary))
                try { o["report"] = JsonNode.Parse(File.ReadAllText(summary)); }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { o["report"] = $"unreadable: {ex.Message}"; }
            return o;
        }
    }
}
