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

    public static int RunChildren(string[] args, IReadOnlyList<string> scenarios, bool capture, string report)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hrot-verify", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var keep = StripVerify(args);
        var reports = new JsonArray();
        var failed = new List<string>();
        Console.WriteLine($"[Verify] {scenarios.Count} scenarios, each in a fresh process; logs and reports in {dir}");
        foreach (var s in scenarios)
        {
            var childReport = Path.Combine(dir, s + ".json");
            var log = Path.Combine(dir, s + ".log");
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            foreach (var a in keep) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--verify"); psi.ArgumentList.Add(s);
            psi.ArgumentList.Add("--verify-report"); psi.ArgumentList.Add(childReport);
            psi.Environment.Remove("HROT_DEBUG_API_PORT");   // each child takes a free port of its own
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
            if (code != 0) failed.Add(s);
            Console.WriteLine($"[Verify] {(code == 0 ? "PASS" : "FAIL")} {s} — exit {code}, {started.Elapsed.TotalSeconds:0} s (log {log})");
            CommandLineVerify.PrintItem(item as JsonObject, Console.Out, "    ");
        }
        var all = new JsonObject
        {
            ["passed"] = failed.Count == 0, ["count"] = scenarios.Count, ["failed"] = new JsonArray(failed.Select(f => (JsonNode?)f).ToArray()),
            ["scenarios"] = reports,
        };
        if (!string.IsNullOrWhiteSpace(report))
            File.WriteAllText(Path.GetFullPath(report), all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[Verify] {scenarios.Count - failed.Count} of {scenarios.Count} pass" + (failed.Count > 0 ? $"; failing: {string.Join(", ", failed)}" : ""));
        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>The arguments without <c>--verify</c> / <c>--verify-report</c> and their values (the parent names each child's own).</summary>
    private static List<string> StripVerify(string[] args)
    {
        var keep = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--verify" or "--verify-report") { i++; continue; }
            if (a.StartsWith("--verify=", StringComparison.Ordinal) || a.StartsWith("--verify-report=", StringComparison.Ordinal)) continue;
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
