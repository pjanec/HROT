using System;
using Fdp.Core.Jobs;

namespace Hrot.ClusterRunner.Configuration
{
    /// <summary>
    /// ⭐⭐ U-2 / U-3 (CE-4531 / CE-4532, R-4224) — the PRODUCT's performance defaults, resolved and applied ONCE at start, before any kernel or
    /// host is built: an explicit <c>HROT_*</c> environment variable &gt; <c>-m all</c> SERIAL (the AI-debug network mode — threading would hide
    /// its unstaged subsystems' windows, Threaded Presentation §14.8) &gt; the user file (<see cref="PerformanceSettings.FileName"/> beside the
    /// layout files) &gt; ON. Every HROT process runs this <c>Main</c>, so the editor, the player, the server and a separate CGF / SimHost node
    /// agree by construction (R-4215). The threaded node is a STAGED UI host's — today the editor only: a host the sim worker ticks draws only when
    /// it is an <c>ISimSteppedSubsystem</c> (<c>SubsystemOrchestrator.Draws</c>), and the player has no staged presentation yet ⇒ with the node
    /// on its windows would be blank (U-3, measured 2026-11-13; CE-3446 builds the player's staging). Every other node — the player included —
    /// runs the parallel phases.
    /// </summary>
    public static class PerformanceStartup
    {
        /// <summary>What this process runs with (set by <see cref="Apply"/>; the menu compares the saved file against it).</summary>
        public static ResolvedPerformance Running { get; private set; }

        /// <summary>Is this process a staged UI host (the threaded node applies: the editor)?</summary>
        public static bool UiHost { get; private set; }

        /// <summary>Is this process <c>-m all</c> (serial by default)?</summary>
        public static bool SerialMode { get; private set; }

        /// <summary>The user file's path (null: no user folder — the defaults apply).</summary>
        public static string? UserPath => PathOverride ?? Hrot.UI.Common.UserFiles.TryPathOf(PerformanceSettings.FileName);

        /// <summary>Tests: a file elsewhere than the real user folder.</summary>
        internal static string? PathOverride { get; set; }

        /// <summary>Resolves for <paramref name="config"/> (its mode validated) and applies it; returns the start-up line.</summary>
        public static string Apply(HrotRunnerConfiguration config, Func<string, string?>? env = null)
        {
            env ??= Environment.GetEnvironmentVariable;
            var (resolved, serial, ui) = ResolveFor(config, env);
            SerialMode = serial;
            UiHost = ui;
            ApplyResolved(resolved, env);
            return PerformanceSettings.Describe(in resolved);
        }

        /// <summary>What <paramref name="config"/>'s process would run with (pure: nothing applied) — the rails' view of every mode.</summary>
        internal static (ResolvedPerformance Resolved, bool SerialMode, bool UiHost) ResolveFor(HrotRunnerConfiguration config, Func<string, string?> env)
        {
            bool serial = string.Equals(config.ModeString.Trim(), "all", StringComparison.OrdinalIgnoreCase);
            // ⭐ U-3 — only a host whose presentation is staged (the editor); the player joins when CE-3446 stages it
            bool ui = config.RequestedSubsystems.Contains("editor");
            return (PerformanceSettings.Resolve(PerformanceSettings.Load(UserPath), env, serial, ui), serial, ui);
        }

        /// <summary>The switches themselves: the threaded node, the per-entity phases, the pool's budget (an explicit <c>HROT_SIM_THREADS</c>
        /// is read by the pool itself).</summary>
        internal static void ApplyResolved(in ResolvedPerformance resolved, Func<string, string?> env)
        {
            Running = resolved;
            Fdp.ModuleHost.ThreadedKernelGate.Configure(resolved.ThreadedKernelMode);
            ParallelEntities.Enabled = resolved.ParallelSimulation;
            if (string.IsNullOrEmpty(env("HROT_SIM_THREADS")))
                Fdp.Core.FdpConfig.MaxDegreeOfParallelism = resolved.WorkerThreads > 0 ? resolved.WorkerThreads : -1;
        }

        /// <summary>The saved choice resolved as THIS process would at its next start (the menu's "restart to apply").</summary>
        public static ResolvedPerformance SavedForNextStart()
            => PerformanceSettings.Resolve(PerformanceSettings.Load(UserPath), Environment.GetEnvironmentVariable, SerialMode, UiHost);
    }
}
