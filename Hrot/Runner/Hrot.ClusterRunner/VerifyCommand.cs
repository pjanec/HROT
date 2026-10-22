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

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐ <c>CE-4042</c> (docs/DESIGN_Scenario_Regression.md §2a, R-4065/R-4066) — <c>--verify</c> on the command line. Several scenarios: this
/// process is the PARENT and runs each in a CHILD of its own (a fresh process per scenario — a reload still leaks, CE-3294), reading each
/// child's report and exit code. One scenario: this process is the editor that verifies it (<see cref="CommandLineVerify"/> starts the
/// run when the editor is ready and exits with the verdict).
/// </summary>
internal static class VerifyCommand
{
    /// <summary>A child's wall-clock limit (a run that hangs is a failure, never a wait forever).</summary>
    private static readonly TimeSpan ChildLimit = TimeSpan.FromMinutes(30);

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
    public static void PrepareThisProcess(string scenario, bool capture, string report)
    {
        Environment.SetEnvironmentVariable(CommandLineVerify.ScenariosVariable, scenario);
        Environment.SetEnvironmentVariable(CommandLineVerify.CaptureVariable, capture ? "1" : null);
        Environment.SetEnvironmentVariable(CommandLineVerify.ReportVariable, string.IsNullOrWhiteSpace(report) ? null : Path.GetFullPath(report));
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT")))
            Environment.SetEnvironmentVariable("HROT_DEBUG_API_PORT", FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine($"[Verify] {scenario}{(capture ? " (capturing the golden)" : "")} — the debug API on port {Environment.GetEnvironmentVariable("HROT_DEBUG_API_PORT")}");
    }

    /// <param name="outDir">⭐ the nightly's folder (<c>--verify-out</c>): a log and a report per scenario, <c>summary.json</c>, <c>junit.xml</c>;
    /// empty = a fresh folder under the temp directory.</param>
    public static int RunChildren(string[] args, IReadOnlyList<string> scenarios, bool capture, string report, string outDir = "")
    {
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
                if (!p.WaitForExit((int)ChildLimit.TotalMilliseconds)) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } code = 124; }
                else { p.WaitForExit(); code = p.ExitCode; }
            }
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
    public static int RunRestoreChecks(string[] args, IReadOnlyList<string> scenarios, double at, string report, string outDir, double every = 1d)
    {
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
                                 new RestoreCheck(t1, cp, Restore: false, runA, every));
            int codeB = File.Exists(cp)
                ? RunChild(keep, s, Path.Combine(dir, s + ".r1-b.report.json"), Path.Combine(dir, s + ".r1-b.log"),
                           new RestoreCheck(t1, cp, Restore: true, runB, every))
                : -1;
            var item = new JsonObject { ["scenario"] = s, ["checkpointAt"] = t1, ["exitA"] = codeA, ["exitB"] = codeB };
            var problems = new JsonArray();
            if (!File.Exists(cp)) problems.Add($"run A wrote no checkpoint (exit {codeA}; log {s}.r1-a.log)");
            else if (!File.Exists(runA) || !File.Exists(runB)) problems.Add($"a run is missing (A exit {codeA}, B exit {codeB}; logs {s}.r1-a.log, {s}.r1-b.log)");
            else
            {
                JsonObject a = JsonNode.Parse(File.ReadAllText(runA))!.AsObject(), b = JsonNode.Parse(File.ReadAllText(runB))!.AsObject();
                var carried = Carried(cp);   // ⭐ what the checkpoint carries is what its own schema manifest lists
                var diff = ScenarioGolden.CompareFrom(a, b, t1, carried);
                item["golden"] = diff.Count == 0 ? "same" : "different";
                item["goldenDiff"] = new JsonArray(diff.Select(d => (JsonNode?)d).ToArray());
                var notCarried = ScenarioGolden.CompareNotCarried(a, b, t1, carried);
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

    // ⭐ B5 — the component names a checkpoint carries: its schema manifest's types (their short names, as a snapshot names them)
    private static Func<string, bool> Carried(string checkpoint)
    {
        var meta = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(File.ReadAllText(checkpoint + ".meta.json"));
        var names = new HashSet<string>((meta.SchemaManifest ?? new()).Values.Select(v => v.Name.Replace('+', '.').Split('.')[^1]), StringComparer.Ordinal);
        return names.Contains;
    }

    // ⭐ B5 — one child: this binary again, verifying one scenario as one half of R1 (the half rides in the environment)
    private static int RunChild(IEnumerable<string> keep, string scenario, string childReport, string log, RestoreCheck half)
    {
        var psi = CommandLineVerify.SelfStartInfo(keep.Concat(new[] { "--verify", scenario, "--verify-report", childReport }));
        psi.Environment[CommandLineVerify.RestoreCheckVariable] = JsonSerializer.Serialize(half, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        using var writer = new StreamWriter(log);
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit((int)ChildLimit.TotalMilliseconds)) { try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } return 124; }
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
            if (a is "--verify" or "--verify-report" or "--verify-out" or "--verify-restore-at" or "--verify-restore-every") { i++; continue; }
            if (a.StartsWith("--verify=", StringComparison.Ordinal) || a.StartsWith("--verify-report=", StringComparison.Ordinal)
                || a.StartsWith("--verify-out=", StringComparison.Ordinal) || a.StartsWith("--verify-restore-at=", StringComparison.Ordinal)
                || a.StartsWith("--verify-restore-every=", StringComparison.Ordinal)) continue;
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
