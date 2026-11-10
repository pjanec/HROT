using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐ <c>CE-4364</c> (L-2, docs/DESIGN_Scale_Rig.md) — the SCALE RIG as a rail: <c>scripts/scale-rig.py</c> loads a terrain with N soldiers /
/// vehicles / aircraft on patrol, measures every node and checks the R-4192 budgets (load ≤ 30 s warm, frame ≤ 10 ms, 0 B garbage per
/// steady frame, ≤ 4 GB per node). ⛔ Not in the usual run: opt in with <c>HROT_SCALE_RIG=1</c> (it starts a cluster and runs minutes);
/// <c>HROT_SCALE_RIG_ARGS</c> overrides the load (default: <c>--terrain scale-1 --men 1000 --vehicles 50 --aircraft 10</c>).
/// The budgets live in the script (one place); this rail fails on its verdict and prints its table.
/// </summary>
public class ScaleRigRail
{
    private readonly ITestOutputHelper _out;
    public ScaleRigRail(ITestOutputHelper output) => _out = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("HROT_SCALE_RIG") == "1";

    [Fact(Timeout = 3_600_000), Trait("Category", "Scale")]
    public void CE4364_TheScaleLoad_MeetsTheBudgets()
    {
        if (!Enabled) return;   // opt-in (R-4192 rig): it runs a whole cluster
        var repo = Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()
                   ?? throw new InvalidOperationException("the scale rig runs from the source tree");
        string json = Path.Combine(Path.GetTempPath(), $"scale-rig-{Guid.NewGuid():N}.json");
        string args = Environment.GetEnvironmentVariable("HROT_SCALE_RIG_ARGS") ?? "--terrain scale-1 --men 1000 --vehicles 50 --aircraft 10";
        var psi = new ProcessStartInfo("python3", $"scripts/scale-rig.py {args} --json \"{json}\"")
        {
            WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        _out.WriteLine(stdout);
        if (stderr.Length > 0) _out.WriteLine(stderr);

        Assert.True(File.Exists(json), $"the rig wrote no report (exit {p.ExitCode})");
        var report = JsonNode.Parse(File.ReadAllText(json))!;
        string verdict = (string?)report["verdict"] ?? "?";
        Assert.True(verdict == "MEETS", $"scale rig: {verdict}");
    }
}
