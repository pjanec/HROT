using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ CE-7035 (X-1; docs/DESIGN_Parallel_Simulation.md §18.2) — the squad drills ALWAYS run once as the parallel brain phase with the read
    /// check on (<c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c>), so a new undeclared cross-entity read in squad / brain code fails at once, naming
    /// the type — not at a determinism run weeks later. ⭐ The check is fixed per process (<c>ParallelReadCheck.Compiled</c>, read once so the JIT
    /// drops it when off), hence a child test run of <see cref="SquadManeuverTests"/>; inside that child (the switch set) this rail is a no-op.
    /// 🔴 Red-proof: one declaration removed from <c>BrainReads</c> (<c>DoorState</c>) ⇒ the child's room-clear drills throw ⇒ red, naming it.
    /// </summary>
    public sealed class DrillsUnderTheReadCheckTests
    {
        [Fact]
        public void TheDrills_AsTheParallelPhase_MakeNoUndeclaredCrossRead()
        {
            if (Environment.GetEnvironmentVariable("HROT_READ_CHECK") is "1" or "2") return;   // this run IS the checked one
            var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("test");
            psi.ArgumentList.Add(typeof(DrillsUnderTheReadCheckTests).Assembly.Location);
            psi.ArgumentList.Add("--filter");
            psi.ArgumentList.Add("FullyQualifiedName~" + nameof(SquadManeuverTests));
            psi.Environment["HROT_PARALLEL_SIM"] = "1";
            psi.Environment["HROT_READ_CHECK"] = "1";
            using var child = Process.Start(psi)!;
            var stderr = child.StandardError.ReadToEndAsync();
            string output = child.StandardOutput.ReadToEnd();
            Assert.True(child.WaitForExit(900_000), "the checked drill run did not finish in 15 min");
            stderr.Wait();

            var summary = Regex.Match(output, @"(Passed|Failed)!\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)");
            Assert.True(summary.Success, "the checked drill run printed no summary:\n" + Tail(output + stderr.Result));
            var undeclared = Regex.Matches(output, @"a read of (\w+) on entity");
            var types = new System.Collections.Generic.SortedSet<string>();
            foreach (Match m in undeclared) types.Add(m.Groups[1].Value);
            Assert.True(child.ExitCode == 0 && summary.Groups[2].Value == "0",
                $"{summary.Groups[2].Value} drill(s) red under HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1; undeclared cross reads of: "
                + (types.Count == 0 ? "(none — see the output)" : string.Join(", ", types))
                + " — declare each in the reading code's owner (BrainReads, AiBrainReads, a phase's stable reads)\n" + Tail(output));
        }

        private static string Tail(string s) => s.Length <= 4000 ? s : s[^4000..];
    }
}
