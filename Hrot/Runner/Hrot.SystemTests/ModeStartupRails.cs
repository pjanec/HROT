using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace Hrot.SystemTests;

/// <summary>
/// <b>ST-019 — every <c>--mode</c> starts and keeps running.</b>
///
/// <para><b>Why this exists.</b> "Does each mode still start?" was a MANUAL gate row for many batches.
/// It was asked for and not delivered, and the user then hit <c>--mode all</c> dying on frame one: a
/// strict-mode violation, where <c>ClusterSlave</c> published <c>NodeHeartbeatEvent</c> on a bus nobody
/// had registered it on. Every unit rail was green throughout — composing a process is precisely the
/// thing unit tests cannot see.</para>
///
/// <para><b>Why it must TICK, not merely launch.</b> Under <c>--mode all</c> each subsystem gets its own
/// isolated <c>FdpEventBus</c> — that is the documented design, and it is why that crash happened:
/// registering events in one subsystem's bootstrap does nothing for another's. The fault therefore
/// appears on the first PUBLISH, not at startup, so a rail that only checked the process launched would
/// have stayed green straight through it. These cases hold the process for a window and require it to
/// still be alive, having logged nothing fatal.</para>
///
/// <para>⚠ <b>What this rail does NOT prove.</b> It proves a mode composes and survives; it cannot prove
/// forward progress. A subsystem that starts, ticks once and then silently stalls would pass. Frame-count
/// evidence needs a per-mode control plane, and today only the editor has one (<c>GET /status</c>) —
/// <c>PanelSnapshotTests</c> covers that side. Stated here so nobody reads a green as more than it is.</para>
/// </summary>
[Trait("Category", "SystemModes")]
public sealed class ModeStartupRails
{
    private readonly ITestOutputHelper _output;

    public ModeStartupRails(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// How long a mode must stay up. Measured, not guessed: the <c>--mode all</c> crash landed at ~3 s
    /// and the <c>--mode ig</c> one at ~1 s, both well inside this. Override for a slow host.
    /// </summary>
    private static int WindowSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable("HROT_MODE_RAIL_WINDOW"), out var s) && s > 0 ? s : 10;

    /// <summary>
    /// ⭐ <b>The mode ledger.</b> Every mode the runner accepts, and whether it is currently expected to
    /// come up. ⛔ <c>stridemock</c> is deliberately absent — the subsystem was retired (<c>ST-015</c>)
    /// and that token now throws.
    ///
    /// <para>⚠ <b><c>ig</c> is quarantined WITH A TRIPWIRE, not filtered.</b> <c>R-131</c> forbids a
    /// permanent filter-around, so <see cref="AQuarantinedMode_IsStillBroken"/> asserts the opposite of
    /// health for it: the day it starts cleanly, that case FAILS and tells you to move it back. A
    /// quarantine that cannot outlive its defect is a ledger entry, not a filter.</para>
    /// </summary>
    /// <remarks>
    /// The second column is the mode string the runner is expected to ECHO in its startup banner, which
    /// is not always the one passed in: ⭐ <c>--mode all</c> is an ALIAS and the banner names what it
    /// expanded to. Spelling that out here pins the expansion at process level -- the charter had to
    /// measure "all = five subsystems" by hand, and asserting <c>mode=all</c> would have been a check
    /// that could never pass. 📌 It did not: the first version of this rail asserted exactly that and
    /// reddened on a mode that was perfectly healthy.
    /// </remarks>
    public static TheoryData<string, string> HealthyModes => new()
    {
        { "editor",        "mode=editor,"        },
        { "all",           "mode=orchestrator,simhost,ig,excon,cgf," },
        { "simhost",       "mode=simhost,"       },
        { "cgf",           "mode=cgf,"           },
        { "excon",         "mode=excon,"         },
        { "orchestrator",  "mode=orchestrator,"  },
        { "replaybrowser", "mode=replaybrowser," },
    };

    /// <summary>Modes known broken, with the finding that owns each. See <see cref="HealthyModes"/>.</summary>
    public static TheoryData<string, string> QuarantinedModes => new()
    {
        { "ig", "ST-020" },
    };

    [SystemSmokeTheory]
    [MemberData(nameof(HealthyModes))]
    public async Task EveryMode_StartsAndKeepsRunning(string mode, string expectedBanner)
    {
        var run = await RunModeAsync(mode).ConfigureAwait(false);
        _output.WriteLine(run.Describe());

        Assert.True(run.Output.Contains(expectedBanner, StringComparison.OrdinalIgnoreCase),
            $"--mode {mode} never logged a startup banner containing \"{expectedBanner}\", so either it "
            + $"did not get as far as composing subsystems, or the mode no longer expands to what this "
            + $"rail expects.{run.OutputTail()}");

        Assert.False(run.Crashed,
            $"--mode {mode} died with an unhandled exception. This is the class of defect this rail "
            + $"exists for -- a mode that composes but cannot survive its first frames.{run.OutputTail()}");

        Assert.True(run.StillAlive,
            $"--mode {mode} exited on its own with code {run.ExitCode} inside {WindowSeconds}s. It is "
            + $"expected to keep running until stopped.{run.OutputTail()}");
    }

    /// <summary>
    /// The tripwire on <see cref="QuarantinedModes"/>. ⚠ <b>A failure here is GOOD NEWS</b> — it means the
    /// mode was fixed and its entry must move to <see cref="HealthyModes"/>.
    /// </summary>
    [SystemSmokeTheory]
    [MemberData(nameof(QuarantinedModes))]
    public async Task AQuarantinedMode_IsStillBroken(string mode, string finding)
    {
        var run = await RunModeAsync(mode).ConfigureAwait(false);
        _output.WriteLine(run.Describe());

        Assert.True(run.Crashed || !run.StillAlive,
            $"--mode {mode} now starts cleanly, so {finding} appears to be FIXED. ⭐ This case failing is "
            + $"the intended signal: move \"{mode}\" from QuarantinedModes to HealthyModes and close "
            + $"{finding}. Do not delete this assertion to make it pass.{run.OutputTail()}");
    }

    // ── the launch ────────────────────────────────────────────────────────────────────────────────

    private sealed record ModeRun(
        string Mode, bool StillAlive, int? ExitCode, bool Crashed, string Output)
    {
        public string Describe() =>
            $"--mode {Mode}: alive={StillAlive} exit={(ExitCode?.ToString() ?? "-")} crashed={Crashed}";

        public string OutputTail()
        {
            if (string.IsNullOrWhiteSpace(Output)) return " The runner produced no output.";
            var lines = Output.Split('\n');
            var tail = lines.Length <= 40 ? Output : string.Join('\n', lines[^40..]);
            return $"\n──── runner output (last {Math.Min(lines.Length, 40)} lines) ────\n{tail}";
        }
    }

    /// <summary>
    /// Boots the real <c>Hrot.ClusterRunner</c> in one mode, holds it for the window, and reports what
    /// happened. Uses <see cref="SystemTestEnvironment"/> for discovery and <see cref="XvfbDisplay"/> for
    /// the display, so there is no second launcher and no second copy of the orphan-avoidance.
    /// </summary>
    private static async Task<ModeRun> RunModeAsync(string mode)
    {
        var dll = SystemTestEnvironment.EditorDll
            ?? throw new InvalidOperationException("Hrot.ClusterRunner.dll not resolved.");

        var output = new StringBuilder();
        var outputLock = new object();
        void Capture(string? line)
        {
            if (line is null) return;
            lock (outputLock) { if (output.Length < 256 * 1024) output.AppendLine(line); }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--mode");
        psi.ArgumentList.Add(mode);
        // Without this a multi-subsystem mode blocks waiting for peers that will never arrive.
        psi.ArgumentList.Add("--no-wait");

        // Per-run staging, so one mode's recordings cannot collide with another's.
        var staging = Path.Combine(Path.GetTempPath(), "hrot-moderail-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(staging);
        psi.Environment["FDP_STAGING_ROOT"] = staging;

        XvfbDisplay? display = null;
        Process? process = null;
        try
        {
            if (!SystemTestEnvironment.IsWindows)
            {
                display = new XvfbDisplay();
                display.ApplyTo(psi);
            }

            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Capture(e.Data);
            process.ErrorDataReceived += (_, e) => Capture(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Hold it for the window, but stop early if it dies -- a dead process tells us everything
            // we need and there is no reason to make the suite wait out the rest.
            try
            {
                await process.WaitForExitAsync(
                    new CancellationTokenSource(TimeSpan.FromSeconds(WindowSeconds)).Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Still running at the deadline -- the healthy outcome.
            }

            string text;
            lock (outputLock) text = output.ToString();

            bool alive = !process.HasExited;
            return new ModeRun(
                Mode: mode,
                StillAlive: alive,
                ExitCode: alive ? null : process.ExitCode,
                Crashed: text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("Strict Mode Violation", StringComparison.OrdinalIgnoreCase),
                Output: text);
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(
                            new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException
                                                or NotSupportedException) { }
                finally { process.Dispose(); }
            }

            display?.Dispose();

            try { Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// The <see cref="TheoryAttribute"/> counterpart of <see cref="SystemSmokeFactAttribute"/> — skips itself,
/// with a stated reason, on a host that cannot launch a runner.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SystemSmokeTheoryAttribute : TheoryAttribute
{
    public SystemSmokeTheoryAttribute()
    {
        if (SystemTestEnvironment.SkipReason is { } reason)
            Skip = reason;
    }
}
