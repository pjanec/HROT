using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fdp.Tests.Determinism
{
    /// <summary>
    /// ⭐⭐ <c>CE-4044</c> — <b>no ECS system reads the WALL CLOCK</b> — the enforcement of 🔒 R-143 (<i>"no wall clock anywhere, whole sim driven
    /// by sim time ONLY"</i>), whose approved exemptions are R-244 (a debounce that only schedules background work) and R-4013 (paced I/O). A system's effect on the world must be a function of the
    /// simulation (its steps, its sim time), never of how fast the machine is: a stepped run must repeat in a fresh process and
    /// on a loaded machine, which is what the scenario verifier's goldens rely on.
    /// <para>📐 The case: <c>MapLayerAssignmentSystem</c> stamped <c>MapDisplayComponent</c> within a 1 ms STOPWATCH budget per frame
    /// and rescanned every 3 s of WALL time, so <c>bt-grenade-posture</c>'s golden differed between two fresh runs, one on a loaded
    /// machine (coordinator, NOTE_Coordinator_To_Backend_Scenario_Determinism.md §2).</para>
    /// <para>⚠ A tripwire, not a proof: a file that DECLARES a system and names a wall clock (<c>Stopwatch</c>, <c>DateTime.Now</c>/<c>UtcNow</c>,
    /// <c>Environment.TickCount</c>, <c>TimeSliceMetric.WallClockTime</c>) outside a comment. A clock that only feeds a log or a
    /// profile belongs in <see cref="Exempt"/> with its reason.</para>
    /// </summary>
    public sealed class NoSystemSchedulesOnTheWallClockTests
    {
        private static readonly Dictionary<string, string> Exempt = new()
        {
            ["FDP/Toolkits/Fdp.Toolkits/Replay/RecorderTickSystem.cs"] =
                "stamps a recorded frame's WALL time (metadata the replay seeks by), only when no time controller supplies one; never world state",
            ["FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs"] = "times each translator for the profile panel; decides nothing",
            ["FDP/Network/Fdp.Network.Cyclone/Systems/CycloneEgressSystem.cs"]        = "times each translator for the profile panel; decides nothing",
            ["FDP/Network/Fdp.Network.Cyclone/Systems/CycloneIngressSystem.cs"]       = "times each translator for the profile panel; decides nothing",
            ["FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/CarKinematicsSystem.cs"]    = "performance logging behind EnablePerformanceLogging; decides nothing",
            ["FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs"] =
                "a frame budget on DRAWING gizmos (the draw builder); writes no world state",
            ["FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs"] =
                "a frame budget on DRAWING gizmos and routing the editor's input focus; decides no simulation state",
            ["Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs"] = "throttles its own log lines; decides nothing",
            ["Hrot/Engine/Hrot.Core/Services/StaticObstacleBake.cs"] =
                "R-244 (approved): the obstacle rebuild's quiet-period debounce only schedules background work; WHEN an obstacle enters the simulation is the lifecycle gate's",
            ["Hrot/Engine/Hrot.Traffic/TrafficModules.cs"] =
                "⏳ R-143 exemption NOT YET RULED: SilenceClock (CE-4021) times a traffic node's SILENCE to drop a dead node's cars — accrues only while cluster time advances, decides nothing while the node publishes",
        };

        private static readonly Regex DeclaresSystem = new(@"\bclass\s+\w+[^{;]*\bIEcsModuleSystem\b", RegexOptions.Compiled);
        private static readonly Regex WallClock = new(
            @"\bStopwatch\b|\bDateTime\.(Utc)?Now\b|\bEnvironment\.TickCount(64)?\b|\bTimeSliceMetric\.WallClockTime\b", RegexOptions.Compiled);

        [Fact]
        public void NoSystem_ReadsTheWallClock_OrIsAnExemptionWithAReason()
        {
            var root = RepoRoot();
            Assert.NotNull(root);

            var offenders = new List<string>();
            var hit = new HashSet<string>();
            foreach (var (rel, src) in ProductionSources(root!))
            {
                if (!DeclaresSystem.IsMatch(src)) continue;
                var lines = src.Split('\n');
                var at = new List<int>();
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*")) continue;
                    var code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    if (WallClock.IsMatch(code)) at.Add(i + 1);
                }
                if (at.Count == 0) continue;
                hit.Add(rel);
                if (!Exempt.ContainsKey(rel))
                    offenders.Add($"{rel}:{string.Join(",", at)} — a system reads the wall clock; schedule on sim time / steps instead, or exempt it with its reason");
            }
            foreach (var stale in Exempt.Keys.Where(k => !hit.Contains(k)))
                offenders.Add($"{stale}: exempt but no longer reads a wall clock — remove the exemption");

            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        private static IEnumerable<(string Rel, string Source)> ProductionSources(string root)
        {
            foreach (var top in new[] { "FDP", "Hrot", "Stride" })
            {
                var dir = Path.Combine(root, top);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                    if (rel.Split('/').Any(p => p.EndsWith(".Tests") || p.EndsWith("Tests") || p == "TestShared" || p.EndsWith(".TestKit")
                                             || p.Contains("Benchmark"))) continue;
                    yield return (rel, File.ReadAllText(file));
                }
            }
        }

        private static string? RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "HROT.sln"))) return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}
