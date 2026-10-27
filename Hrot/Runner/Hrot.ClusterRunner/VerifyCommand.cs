using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2a, R-4065/R-4066) — <c>--verify</c> on the command line. Several scenarios: this
/// process is the PARENT and runs each in a CHILD of its own (a fresh process per scenario — a reload still leaks, CE-3294), reading each
/// child's report and exit code. One scenario: this process is the editor that verifies it (<see cref="CommandLineVerify"/> starts the
/// run when the editor is ready and exits with the verdict).
/// </summary>
internal static class VerifyCommand
{
    /// <summary><c>a,b,c</c> as named, or <c>all</c> — every scenario of the source tree with a <c>checks.json</c>.</summary>
    public static IReadOnlyList<string> Scenarios(string arg)
    {
        if (!string.Equals(arg.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            return arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        var root = Hrot.ScenarioEditor.Services.CuratedScenarios.TryFindSourceScenariosDirectory();
        if (root == null) return Array.Empty<string>();
        return Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, ChecksJson.FileName)))
            .Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>This process verifies <paramref name="scenario"/>: the editor reads the request, and a debug port is set when none is (the
    /// verifier reads the world through the debug service — <see cref="EditorVerifyWorld"/>).</summary>
    public static void PrepareThisProcess(string scenario, bool capture, string report, bool replay = false, double branchAt = 0)
    {
        replay |= branchAt > 0;   // ⭐ H3 is part of the replay check
        Environment.SetEnvironmentVariable(CommandLineVerify.ReplayVariable, replay ? "1" : null);
        Environment.SetEnvironmentVariable(CommandLineVerify.BranchAtVariable,
            branchAt > 0 ? branchAt.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        Environment.SetEnvironmentVariable(CommandLineVerify.ScenariosVariable, scenario);
        Environment.SetEnvironmentVariable(CommandLineVerify.CaptureVariable, capture ? "1" : null);
        Environment.SetEnvironmentVariable(CommandLineVerify.ReportVariable, string.IsNullOrWhiteSpace(report) ? null : Path.GetFullPath(report));
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT")))
            Environment.SetEnvironmentVariable("HROT_DEBUG_API_PORT", FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine($"[Verify] {scenario}{(capture ? " (capturing the golden)" : "")}{(replay ? " (live, against its own recording)" : "")}{(branchAt > 0 ? $" (branched live at {branchAt} s)" : "")} — the debug API on port {Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT")}");
    }

    /// <param name="outDir">⭐ the nightly's folder (<c>--verify-out</c>): a log and a report per scenario, <c>summary.json</c>, <c>junit.xml</c>;
    /// empty = a fresh folder under the temp directory.</param>
    /// <param name="limitMinutes">a child's wall-clock limit (<c>--verify-timeout</c>) — a run that hangs is a failure, never a wait forever.</param>
    public static int RunChildren(string[] args, IReadOnlyList<string> scenarios, bool capture, string report, string outDir = "", int limitMinutes = 60)
    {
        var childLimit = TimeSpan.FromMinutes(Math.Max(1, limitMinutes));
        var dir = !string.IsNullOrWhiteSpace(outDir) ? Path.GetFullPath(outDir)
            : Path.Combine(Path.GetTempPath(), "hrot-verify", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var started0 = Stopwatch.StartNew();
        var cases = new List<(string Scenario, int Code, double Seconds, JsonObject? Item, string Log)>();
        var keep = StripVerify(args);
        var reports = new JsonArray();
        var failed = new List<string>();
        Console.WriteLine($"[Verify] {scenarios.Count} scenarios, each in a fresh process; logs and reports in {dir}");
        foreach (var s in scenarios)
        {
            var childReport = Path.Combine(dir, s + ".json");
            var log = Path.Combine(dir, s + ".log");
            var psi = CommandLineVerify.SelfStartInfo(keep.Concat(new[] { "--verify", s, "--verify-report", childReport }));
            // ⭐ a PRIVATE staging root per child: every editor boot seeds the curated scenarios into its working copy, and a sibling
            //    capturing a golden there made the boot throw "used by another process" (📐 measured, three streams in parallel). The
            //    golden that is kept goes to the source tree anyway (TakeStore.SaveScenarioJson).
            var staging = Path.Combine(dir, ".staging", s);
            psi.Environment["FDP_STAGING_ROOT"] = staging;
            var started = Stopwatch.StartNew();
            int code;
            using (var writer = new StreamWriter(log))
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit((int)childLimit.TotalMilliseconds)) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } code = 124; }
                else { p.WaitForExit(); code = p.ExitCode; }
            }
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            JsonNode? r = File.Exists(childReport) ? JsonNode.Parse(File.ReadAllText(childReport)) : null;
            var item = r?["scenarios"]?[0]?.DeepClone() ?? new JsonObject { ["scenario"] = s, ["status"] = "no report", ["exitCode"] = code, ["log"] = log };
            reports.Add(item);
            cases.Add((s, code, started.Elapsed.TotalSeconds, item as JsonObject, log));
            if (code != 0) failed.Add(s);
            Console.WriteLine($"[Verify] {(code == 0 ? "PASS" : "FAIL")} {s} — exit {code}, {started.Elapsed.TotalSeconds:0} s (log {log})");
            CommandLineVerify.PrintItem(item as JsonObject, Console.Out, "    ");
        }
        var all = new JsonObject
        {
            ["passed"] = failed.Count == 0, ["count"] = scenarios.Count, ["failed"] = new JsonArray(failed.Select(f => (JsonNode?)f).ToArray()),
            ["scenarios"] = reports,
        };
        var text = all.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented);
        File.WriteAllText(Path.Combine(dir, "summary.json"), text);
        File.WriteAllText(Path.Combine(dir, "junit.xml"), JUnit(cases, started0.Elapsed.TotalSeconds));
        if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(Path.GetFullPath(report), text);
        Console.WriteLine($"[Verify] {scenarios.Count - failed.Count} of {scenarios.Count} pass" + (failed.Count > 0 ? $"; failing: {string.Join(", ", failed)}" : ""));
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// ⭐ JUnit XML — the report a CI server (GitHub, Jenkins, GitLab) shows as a test list: one test case per scenario, its failure the
    /// failed conditions and the golden's first difference, its output the log's path.
    /// </summary>
    internal static string JUnit(IReadOnlyList<(string Scenario, int Code, double Seconds, JsonObject? Item, string Log)> cases, double seconds)
    {
        static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        var sb = new System.Text.StringBuilder();
        int failures = cases.Count(c => c.Code != 0);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<testsuites name=\"scenario-regression\" tests=\"{cases.Count}\" failures=\"{failures}\" time=\"{seconds:0.0}\">\n");
        sb.Append($"  <testsuite name=\"scenario-regression\" tests=\"{cases.Count}\" failures=\"{failures}\" time=\"{seconds:0.0}\">\n");
        foreach (var c in cases)
        {
            sb.Append($"    <testcase classname=\"scenarios\" name=\"{X(c.Scenario)}\" time=\"{c.Seconds:0.0}\">\n");
            if (c.Code != 0)
            {
                var lines = new List<string>();
                foreach (var v in (c.Item?["verdicts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(v => (bool?)v["ok"] != true))
                    lines.Add($"FAIL {v["name"]}{(v["heldOutsideItsWindowAt"] is { } o ? $" (held at {o} s, outside its window)" : "")}");
                if (c.Item?["golden"]?.ToString() is { } g && g is not ("same" or "none" or "captured")) lines.Add($"golden {g}");
                foreach (var d in (c.Item?["goldenDiff"] as JsonArray ?? new JsonArray()).Take(10)) lines.Add($"  {d}");
                if (c.Item?["replay"]?.ToString() is { } rp && rp != "same") lines.Add($"replay {rp}");
                foreach (var d in (c.Item?["replayDiff"] as JsonArray ?? new JsonArray()).Take(10)) lines.Add($"  {d}");
                foreach (var look in new[] { "playback", "seek", "branch" })   // ⭐ H1b · H2 · H3 — one look-back shape
                    if (c.Item?[look] is JsonObject pb && pb["verdict"]?.ToString() is { } pv && pv != "same")
                    {
                        lines.Add($"{look} {pv}");
                        foreach (var d in (pb["diff"] as JsonArray ?? new JsonArray()).Take(10)) lines.Add($"  {d}");
                        foreach (var d in (pb["problems"] as JsonArray ?? new JsonArray())) lines.Add($"PROBLEM {d}");
                    }
                foreach (var p in (c.Item?["problems"] as JsonArray ?? new JsonArray())) lines.Add($"PROBLEM {p}");
                foreach (var p in (c.Item?["runProblems"] as JsonArray ?? new JsonArray())) lines.Add($"PROBLEM {p}");
                if (lines.Count == 0) lines.Add($"exit {c.Code} — {c.Item?["status"]}");
                sb.Append($"      <failure message=\"{X(lines[0])}\">{X(string.Join("\n", lines))}</failure>\n");
            }
            sb.Append($"      <system-out>{X("log: " + c.Log)}</system-out>\n");
            sb.Append("    </testcase>\n");
        }
        sb.Append("  </testsuite>\n</testsuites>\n");
        return sb.ToString();
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3299</c> B5 — <b>R1, the restore-equivalence oracle</b> (docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §1k). Per scenario,
    /// two fresh children: A runs it straight and writes checkpoint <c>r1</c> at <paramref name="at"/>; B puts that checkpoint back and runs
    /// on. Every unit's full component state from <paramref name="at"/> on must be the same in both — a difference names the first
    /// snapshot, the units and the components (each is the next thing a checkpoint does not carry, R-3039). Exit 0 when all match.
    /// </summary>
    public static int RunRestoreChecks(string[] args, IReadOnlyList<string> scenarios, double at, string report, string outDir, double every = 1d, int limitMinutes = 60)
    {
        var childLimit = TimeSpan.FromMinutes(Math.Max(1, limitMinutes));
        if (!(every > 0)) every = 1d;   // the snapshot grid; the checkpoint lands on it, so both runs snapshot the same moments
        double t1 = Math.Max(every, Math.Round(at / every) * every);
        var dir = !string.IsNullOrWhiteSpace(outDir) ? Path.GetFullPath(outDir)
            : Path.Combine(Path.GetTempPath(), "hrot-verify-r1", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var keep = StripVerify(args);
        var started0 = Stopwatch.StartNew();
        var cases = new List<(string Scenario, int Code, double Seconds, JsonObject? Item, string Log)>();
        var reports = new JsonArray();
        var failed = new List<string>();
        Console.WriteLine($"[Verify R1] {scenarios.Count} scenarios: a checkpoint at {t1} s, restored in a second fresh process; output in {dir}");
        foreach (var s in scenarios)
        {
            var started = Stopwatch.StartNew();
            string cp = Path.Combine(dir, s + ".r1.fdp"), runA = Path.Combine(dir, s + ".r1-a.json"), runB = Path.Combine(dir, s + ".r1-b.json");
            foreach (var f in new[] { cp, cp + ".meta.json", cp + ".r1.json", runA, runB }) if (File.Exists(f)) File.Delete(f);
            int codeA = RunChild(keep, s, Path.Combine(dir, s + ".r1-a.report.json"), Path.Combine(dir, s + ".r1-a.log"),
                                 CommandLineVerify.RestoreCheckVariable, Json(new RestoreCheck(t1, cp, Restore: false, runA, every)), childLimit);
            int codeB = File.Exists(cp)
                ? RunChild(keep, s, Path.Combine(dir, s + ".r1-b.report.json"), Path.Combine(dir, s + ".r1-b.log"),
                           CommandLineVerify.RestoreCheckVariable, Json(new RestoreCheck(t1, cp, Restore: true, runB, every)), childLimit)
                : -1;
            var item = new JsonObject { ["scenario"] = s, ["checkpointAt"] = t1, ["exitA"] = codeA, ["exitB"] = codeB };
            var problems = new JsonArray();
            if (!File.Exists(cp)) problems.Add($"run A wrote no checkpoint (exit {codeA}; log {s}.r1-a.log)");
            else if (!File.Exists(runA) || !File.Exists(runB)) problems.Add($"a run is missing (A exit {codeA}, B exit {codeB}; logs {s}.r1-a.log, {s}.r1-b.log)");
            else
            {
                JsonObject a = JsonNode.Parse(File.ReadAllText(runA))!.AsObject(), b = JsonNode.Parse(File.ReadAllText(runB))!.AsObject();
                var carried = ScenarioGolden.CarriedBy(cp) ?? (_ => true);   // ⭐ what the checkpoint carries is what its own schema manifest lists
                // ⭐ CE-3316 — from where A really wrote the checkpoint (a quiet step, at most a few after T1)
                double from = ScenarioVerifyRun.ReadSidecar(new RestoreCheck(t1, cp, false, runA, every)).At ?? t1;
                item["checkpointWrittenAt"] = Math.Round(from, 4);
                var diff = ScenarioGolden.CompareFrom(a, b, from, carried);
                item["golden"] = diff.Count == 0 ? "same" : "different";
                item["goldenDiff"] = new JsonArray(diff.Select(d => (JsonNode?)d).ToArray());
                var notCarried = ScenarioGolden.CompareNotCarried(a, b, from, carried);
                if (notCarried.Count > 0) item["notCarriedDiff"] = new JsonArray(notCarried.Select(d => (JsonNode?)d).ToArray());
            }
            if (problems.Count > 0) item["problems"] = problems;
            bool ok = problems.Count == 0 && item["golden"]?.ToString() == "same";
            item["status"] = ok ? "pass" : "fail";
            reports.Add(item);
            cases.Add((s, ok ? 0 : 1, started.Elapsed.TotalSeconds, item, Path.Combine(dir, s + ".r1-b.log")));
            if (!ok) failed.Add(s);
            Console.WriteLine($"[Verify R1] {(ok ? "SAME" : "FAIL")} {s} — {started.Elapsed.TotalSeconds:0} s");
            foreach (var d in (item["goldenDiff"] as JsonArray ?? new JsonArray())) Console.WriteLine($"    {d}");
            foreach (var d in (item["notCarriedDiff"] as JsonArray ?? new JsonArray())) Console.WriteLine($"    (not carried by a checkpoint, by design) {d}");
            foreach (var p in problems) Console.WriteLine($"    PROBLEM {p}");
        }
        var all = new JsonObject
        {
            ["passed"] = failed.Count == 0, ["count"] = scenarios.Count, ["checkpointAt"] = t1,
            ["failed"] = new JsonArray(failed.Select(f => (JsonNode?)f).ToArray()), ["scenarios"] = reports,
        };
        var text = all.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented);
        File.WriteAllText(Path.Combine(dir, "summary.json"), text);
        File.WriteAllText(Path.Combine(dir, "junit.xml"), JUnit(cases, started0.Elapsed.TotalSeconds));
        if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(Path.GetFullPath(report), text);
        Console.WriteLine($"[Verify R1] {scenarios.Count - failed.Count} of {scenarios.Count} restore to the same run" + (failed.Count > 0 ? $"; differing: {string.Join(", ", failed)}" : ""));
        return failed.Count == 0 ? 0 : 1;
    }

    private static string Json<T>(T half) => JsonSerializer.Serialize(half, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

    /// <summary>
    /// ⭐⭐ <c>CE-4045</c> H5 — <b>the review check</b> (docs/DESIGN_Scenario_Regression.md §10-3, R-4086). Per scenario, two fresh LIVE
    /// children: A runs straight; B runs to <paramref name="at"/>, reviews every earlier snapshot time through the master, returns to live and
    /// runs on. B judges the review (against its own live snapshots) and the return (the world it left); this parent judges the run after it
    /// (B from <paramref name="at"/> = A — R1's comparator). Exit 0 when all three hold for every scenario.
    /// </summary>
    public static int RunReviewChecks(string[] args, IReadOnlyList<string> scenarios, double at, string report, string outDir, int limitMinutes = 60)
    {
        var childLimit = TimeSpan.FromMinutes(Math.Max(1, limitMinutes));
        double t1 = Math.Max(1d, Math.Round(at));   // on the one-second snapshot grid, so both halves snapshot the same moments
        var dir = !string.IsNullOrWhiteSpace(outDir) ? Path.GetFullPath(outDir)
            : Path.Combine(Path.GetTempPath(), "hrot-verify-h5", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var keep = StripVerify(args);
        var started0 = Stopwatch.StartNew();
        var cases = new List<(string Scenario, int Code, double Seconds, JsonObject? Item, string Log)>();
        var reports = new JsonArray();
        var failed = new List<string>();
        Console.WriteLine($"[Verify H5] {scenarios.Count} scenarios: live, a review at {t1} s, back to live — against a straight live run; output in {dir}");
        foreach (var s in scenarios)
        {
            var started = Stopwatch.StartNew();
            string runA = Path.Combine(dir, s + ".h5-a.json"), runB = Path.Combine(dir, s + ".h5-b.json"), repB = Path.Combine(dir, s + ".h5-b.report.json");
            foreach (var f in new[] { runA, runB, repB }) if (File.Exists(f)) File.Delete(f);
            int codeA = RunChild(keep, s, Path.Combine(dir, s + ".h5-a.report.json"), Path.Combine(dir, s + ".h5-a.log"),
                                 CommandLineVerify.ReviewVariable, Json(new ReviewCheck(t1, Straight: true, runA)), childLimit);
            int codeB = RunChild(keep, s, repB, Path.Combine(dir, s + ".h5-b.log"),
                                 CommandLineVerify.ReviewVariable, Json(new ReviewCheck(t1, Straight: false, runB)), childLimit);
            var item = new JsonObject { ["scenario"] = s, ["reviewAt"] = t1, ["exitA"] = codeA, ["exitB"] = codeB };
            var problems = new JsonArray();
            var b = File.Exists(repB) ? JsonNode.Parse(File.ReadAllText(repB))?["scenarios"]?[0] as JsonObject : null;
            if (b == null) problems.Add($"run B wrote no report (exit {codeB}; log {s}.h5-b.log)");
            else
            {
                foreach (var key in new[] { "review", "returnDiff" })   // ⭐ the look-back's nested shape (LookBackResult.ToJson) and the return
                    item[key] = b[key]?.DeepClone();
                foreach (var p in (b["problems"] as JsonArray ?? new JsonArray())) problems.Add(p?.DeepClone());
            }
            if (!File.Exists(runA) || !File.Exists(runB)) problems.Add($"a run is missing (A exit {codeA}, B exit {codeB}; logs {s}.h5-a.log, {s}.h5-b.log)");
            else
            {
                var after = ScenarioGolden.CompareFrom(JsonNode.Parse(File.ReadAllText(runA))!.AsObject(), JsonNode.Parse(File.ReadAllText(runB))!.AsObject(), t1);
                item["after"] = after.Count == 0 ? "same" : "different";
                item["afterDiff"] = new JsonArray(after.Select(d => (JsonNode?)d).ToArray());
            }
            if (problems.Count > 0) item["problems"] = problems;
            var review = item["review"] as JsonObject;
            var reviewProblems = review?["problems"] as JsonArray ?? new JsonArray();
            bool ok = problems.Count == 0 && review?["verdict"]?.ToString() == "same" && (item["returnDiff"] as JsonArray ?? new JsonArray()).Count == 0
                      && reviewProblems.Count == 0 && item["after"]?.ToString() == "same";
            item["status"] = ok ? "pass" : "fail";
            reports.Add(item);
            cases.Add((s, ok ? 0 : 1, started.Elapsed.TotalSeconds, item, Path.Combine(dir, s + ".h5-b.log")));
            if (!ok) failed.Add(s);
            Console.WriteLine($"[Verify H5] {(ok ? "SAME" : "FAIL")} {s} — review {review?["verdict"] ?? "?"}, return {((item["returnDiff"] as JsonArray)?.Count == 0 ? "exact" : "differs")}, after {item["after"] ?? "?"} — {started.Elapsed.TotalSeconds:0} s");
            foreach (var (key, list) in new[] { ("reviewDiff", review?["diff"]), ("returnDiff", item["returnDiff"]), ("afterDiff", item["afterDiff"]), ("reviewProblems", review?["problems"]) })
                foreach (var d in (list as JsonArray ?? new JsonArray())) Console.WriteLine($"    {key}: {d}");
            foreach (var d in (review?["notCarried"] as JsonArray ?? new JsonArray())) Console.WriteLine($"    (not recorded, by the manifest) {d}");
            foreach (var p in problems) Console.WriteLine($"    PROBLEM {p}");
        }
        var all = new JsonObject
        {
            ["passed"] = failed.Count == 0, ["count"] = scenarios.Count, ["reviewAt"] = t1,
            ["failed"] = new JsonArray(failed.Select(f => (JsonNode?)f).ToArray()), ["scenarios"] = reports,
        };
        var text = all.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented);
        File.WriteAllText(Path.Combine(dir, "summary.json"), text);
        File.WriteAllText(Path.Combine(dir, "junit.xml"), JUnit(cases, started0.Elapsed.TotalSeconds));
        if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(Path.GetFullPath(report), text);
        Console.WriteLine($"[Verify H5] {scenarios.Count - failed.Count} of {scenarios.Count} review and return exactly" + (failed.Count > 0 ? $"; failing: {string.Join(", ", failed)}" : ""));
        return failed.Count == 0 ? 0 : 1;
    }

    // ⭐ B5 / H5 — one child: this binary again, verifying one scenario as one half of R1 or of the review check (the half rides in the environment)
    private static int RunChild(IEnumerable<string> keep, string scenario, string childReport, string log, string variable, string half, TimeSpan limit)
    {
        var psi = CommandLineVerify.SelfStartInfo(keep.Concat(new[] { "--verify", scenario, "--verify-report", childReport }));
        psi.Environment[variable] = half;
        using var writer = new StreamWriter(log);
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit((int)limit.TotalMilliseconds)) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return 124; }
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>The arguments without <c>--verify</c> / <c>--verify-report</c> and their values (the parent names each child's own).</summary>
    private static List<string> StripVerify(string[] args)
    {
        var keep = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--verify" or "--verify-report" or "--verify-out" or "--verify-restore-at" or "--verify-restore-every" or "--verify-timeout" or "--verify-review-at") { i++; continue; }
            if (a.StartsWith("--verify=", StringComparison.Ordinal) || a.StartsWith("--verify-report=", StringComparison.Ordinal)
                || a.StartsWith("--verify-review-at=", StringComparison.Ordinal)
                || a.StartsWith("--verify-out=", StringComparison.Ordinal) || a.StartsWith("--verify-restore-at=", StringComparison.Ordinal)
                || a.StartsWith("--verify-restore-every=", StringComparison.Ordinal) || a.StartsWith("--verify-timeout=", StringComparison.Ordinal)) continue;
            keep.Add(a);
        }
        return keep;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
