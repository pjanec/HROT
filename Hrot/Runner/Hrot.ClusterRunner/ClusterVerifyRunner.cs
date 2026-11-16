using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Hrot.Editor.Director;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐⭐ RT-5 (R-4279, docs/DESIGN_Scenario_Regression.md §11.3) — THE CLUSTER TIER: <c>--verify &lt;s&gt; --mode all</c>. Per scenario this
/// process (the PARENT) boots ONE fresh <c>--mode all</c> cluster (its own debug port, staging root and DDS domain; under <c>xvfb-run</c> when
/// there is no display), loads the scenario live, plays it, and judges it with the SAME <see cref="ScenarioVerification"/> / condition book
/// as the editor tier — in SIM time (read from <c>/status</c>), over HTTP through <see cref="ClusterVerifyWorld"/>. ⛔ NO golden in this
/// tier: a replicated run is not bit-deterministic (R-4279). ⭐ RT-6: every run also passes <see cref="ReplicationSanity"/>. The summary,
/// junit and printed lines are the editor tier's own (<see cref="VerifyCommand.JUnit"/>, <see cref="CommandLineVerify.PrintItem"/>).
/// </summary>
internal static class ClusterVerifyRunner
{
    /// <summary>A run that does not reach its end in this much wall time per sim second (plus the boot) is a failure, never a wait forever.</summary>
    public const double WallPerSimSecond = 6d, BootSeconds = 240d;

    public static int Run(string[] args, IReadOnlyList<string> scenarios, string report, string outDir, int limitMinutes)
    {
        var dir = !string.IsNullOrWhiteSpace(outDir) ? Path.GetFullPath(outDir)
            : Path.Combine(Path.GetTempPath(), "hrot-verify-cluster", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var all0 = Stopwatch.StartNew();
        var cases = new List<(string Scenario, int Code, double Seconds, JsonObject? Item, string Log)>();
        var reports = new JsonArray();
        var failed = new List<string>();
        Console.WriteLine($"[Verify] cluster tier (--mode all): {scenarios.Count} scenario(s), each in a fresh cluster; logs and reports in {dir}");
        for (int i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var started = Stopwatch.StartNew();
            var log = Path.Combine(dir, s + ".log");
            JsonObject item;
            try { item = RunOne(s, i, dir, log, limitMinutes); }
            catch (Exception e) { item = Item(s, "error", null, new[] { $"the cluster run threw: {e.GetType().Name}: {e.Message}" }, 0, started.Elapsed.TotalSeconds); }
            item["log"] = log;
            int code = (bool?)item["passed"] == true ? 0 : 1;
            File.WriteAllText(Path.Combine(dir, s + ".json"), item.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented));
            reports.Add(item);
            cases.Add((s, code, started.Elapsed.TotalSeconds, item, log));
            if (code != 0) failed.Add(s);
            Console.WriteLine($"[Verify] {(code == 0 ? "PASS" : "FAIL")} {s} (cluster) — {started.Elapsed.TotalSeconds:0} s (log {log})");
            CommandLineVerify.PrintItem(item, Console.Out, "    ");
            foreach (var p in (item["problems"] as JsonArray ?? new JsonArray())) Console.WriteLine($"      PROBLEM {p}");
        }
        var summary = new JsonObject
        {
            ["tier"] = "cluster", ["passed"] = failed.Count == 0, ["count"] = scenarios.Count,
            ["failed"] = new JsonArray(failed.Select(f => (JsonNode?)f).ToArray()), ["scenarios"] = reports,
        };
        var text = summary.ToJsonString(Fdp.Core.Serialization.FdpJsonOptionsRegistry.Indented);
        File.WriteAllText(Path.Combine(dir, "summary.json"), text);
        File.WriteAllText(Path.Combine(dir, "junit.xml"), VerifyCommand.JUnit(cases, all0.Elapsed.TotalSeconds).Replace("scenario-regression", "scenario-regression-cluster"));
        if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(Path.GetFullPath(report), text);
        Console.WriteLine($"[Verify] cluster tier: {scenarios.Count - failed.Count} of {scenarios.Count} pass" + (failed.Count > 0 ? $"; failing: {string.Join(", ", failed)}" : ""));
        return failed.Count == 0 ? 0 : 1;
    }

    private static JsonObject RunOne(string scenario, int index, string dir, string log, int limitMinutes)
    {
        var wall = Stopwatch.StartNew();
        var problems = new List<string>();
        // the checks, from the source tree (the parent boots no editor)
        var root = Hrot.ScenarioEditor.Services.CuratedScenarios.TryFindSourceScenariosDirectory()
                   ?? throw new InvalidOperationException("no source scenarios directory found");
        var folder = Fdp.Toolkit.Orchestration.ScenarioFolderIndex.Enumerate(root).FirstOrDefault(f => string.Equals(f.Name, scenario, StringComparison.Ordinal));
        var checksPath = folder.Directory is null ? null : Path.Combine(folder.Directory, ChecksJson.FileName);
        if (checksPath is null || !File.Exists(checksPath)) return Item(scenario, "not started", null, new[] { $"no {ChecksJson.FileName} for '{scenario}'" }, 0, 0);
        var parse = new List<string>();
        var checks = ChecksJson.Parse((JsonObject)JsonNode.Parse(File.ReadAllText(checksPath))!, scenario, parse);
        if (parse.Count > 0) return Item(scenario, "not started", null, parse, 0, 0);
        checks = checks with { GoldenEvery = null };   // ⛔ no golden in the cluster tier (R-4279)

        int port = FreePort();
        var staging = Path.Combine(dir, ".staging", scenario);
        using var proc = Boot(port, 120 + index % 100, staging, log);
        using var world = new ClusterVerifyWorld(port);
        try
        {
            var limit = TimeSpan.FromSeconds(BootSeconds + checks.RunFor * WallPerSimSecond);
            if (limitMinutes > 0 && TimeSpan.FromMinutes(limitMinutes) < limit) limit = TimeSpan.FromMinutes(limitMinutes);
            // ① the cluster answers and can operate
            if (!Until(() => world.Get("/status")?["canOperate"]?.GetValue<bool>() == true, TimeSpan.FromSeconds(BootSeconds), proc))
                return Item(scenario, "error", null, new[] { proc.HasExited ? $"the cluster exited (code {proc.ExitCode}) before it could operate" : "the cluster never reported canOperate" }, 0, wall.Elapsed.TotalSeconds);
            // ② load live (a load resets sim time to 0), wait for the settled live state, then the Scenario perspective (the utility check's protocol)
            var (_, loadError) = world.Post("/scenario/load/live", new JsonObject { ["name"] = scenario, ["waitForReady"] = true });
            if (!Until(() => world.Get("/status")?["clusterState"]?.ToString() == "OperatingLive", TimeSpan.FromSeconds(120), proc))
                return Item(scenario, "error", null, new[] { $"the load never reached OperatingLive{(loadError != null ? $" ({loadError})" : "")}" }, 0, wall.Elapsed.TotalSeconds);
            world.Post("/perspective", new JsonObject { ["name"] = "Scenario" });
            world.Refresh();
            double t0 = Num(world.Get("/status")?["simTime"]) ?? 0;
            var run = new ScenarioVerification(checks, golden: null, capture: false, world, t0);
            var missing = ScenarioVerification.MissingNames(checks, world);
            if (missing.Count > 0) return Item(scenario, "not started", null, missing, 0, wall.Elapsed.TotalSeconds);
            world.Post("/sim/play", new JsonObject());
            // ③ the run, judged in SIM time: every action due, every sample due, until runFor
            double rel = 0; long polls = 0;
            while (wall.Elapsed < limit)
            {
                if (proc.HasExited) { problems.Add($"the cluster exited mid-run (code {proc.ExitCode}) at sim {rel:0.##} s — see the log (a module exception is fatal: FDP_FAIL_FAST)"); break; }
                if (Num(world.Get("/status")?["simTime"]) is { } now) rel = now - t0;
                run.ApplyDue(rel);
                run.Observe(rel);
                polls++;
                if (rel >= checks.RunFor) break;
                System.Threading.Thread.Sleep(50);
            }
            if (rel < checks.RunFor && problems.Count == 0) problems.Add($"the run reached sim {rel:0.##} s of {checks.RunFor} s in its wall limit ({limit.TotalSeconds:0} s)");
            var result = run.Finish(Math.Min(rel, checks.RunFor), polls);
            // ④ RT-6 — replication sanity, from the node's own architecture
            var sanity = ReplicationSanity.Judge(proc.HasExited ? null : world.Get("/diagnostics/architecture"));
            var item = result.ToJson();
            var all = (item["problems"] as JsonArray)!;
            foreach (var p in problems.Concat(sanity.Problems)) all.Add(p);
            item["passed"] = (bool?)item["passed"] == true && problems.Count == 0 && sanity.Problems.Count == 0;
            item["replicationNotes"] = new JsonArray(sanity.Notes.Select(n => (JsonNode?)n).ToArray());
            item["scenario"] = scenario;
            item["tier"] = "cluster";
            item["status"] = (bool?)item["passed"] == true ? "pass" : "fail";
            item["steps"] = polls;
            item["simEnd"] = Math.Round(rel, 3);
            item["wallSeconds"] = Math.Round(wall.Elapsed.TotalSeconds, 1);
            return item;
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { proc.WaitForExit(10_000); } catch (InvalidOperationException) { }
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A fresh <c>--mode all</c> cluster: this runner, its own port / staging root / domain; under <c>xvfb-run -a</c> on a Linux with no display.</summary>
    private static Process Boot(int port, int domain, string staging, string log)
    {
        var self = CommandLineVerify.SelfStartInfo(new[] { "--mode", "all", "--domain", domain.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        var psi = self;
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            psi = new ProcessStartInfo("xvfb-run") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(self.FileName);
            foreach (var a in self.ArgumentList) psi.ArgumentList.Add(a);
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) psi.Environment[(string)e.Key] = (string?)e.Value;
            foreach (var k in new[] { "HROT_DEBUG_API_PORT", CommandLineVerify.ScenariosVariable }) psi.Environment.Remove(k);
        }
        psi.Environment["HROT_DEBUG_API_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["FDP_STAGING_ROOT"] = staging;
        var writer = new StreamWriter(log) { AutoFlush = true };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (writer) writer.WriteLine(e.Data); };
        p.Exited += (_, _) => { lock (writer) writer.Flush(); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static bool Until(Func<bool> done, TimeSpan limit, Process proc)
    {
        var w = Stopwatch.StartNew();
        while (w.Elapsed < limit)
        {
            if (proc.HasExited) return false;
            if (done()) return true;
            System.Threading.Thread.Sleep(500);
        }
        return false;
    }

    private static JsonObject Item(string scenario, string status, JsonArray? verdicts, IEnumerable<string> problems, long steps, double wall) => new()
    {
        ["passed"] = false, ["tier"] = "cluster", ["scenario"] = scenario, ["status"] = status,
        ["verdicts"] = verdicts ?? new JsonArray(), ["problems"] = new JsonArray(problems.Select(p => (JsonNode?)p).ToArray()),
        ["steps"] = steps, ["simEnd"] = 0, ["wallSeconds"] = Math.Round(wall, 1),
    };

    private static double? Num(JsonNode? n) => ConditionBook.Num(n);

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
