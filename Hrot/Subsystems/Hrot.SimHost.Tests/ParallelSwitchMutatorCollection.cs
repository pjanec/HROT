using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ CE-4518 — serialises the test classes that flip the PROCESS-WIDE parallel switch (<c>ParallelEntities.Enabled</c>) or resize the shared
/// worker pool to compare serial against parallel runs.
/// <para>📐 Measured `2026-11-13`: once the staff composer read the switch live (X-3, a per-commander phase), <c>SquadManeuverTests</c> — running
/// CONCURRENTLY in another collection — took the parallel path whenever <c>CalloutsTests</c> / <c>StressSystemTests</c> had it on, and shared the
/// one worker pool and the per-job-type runners with them: 27–29 manoeuvre rails failed together, each green alone and green in a run without these
/// two classes. ⭐ Isolation, not an ordering hack: production runs one kernel per process; only the test host runs two worlds at once.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParallelSwitchMutatorCollection
{
    public const string Name = "Parallel switch mutators (Hrot.SimHost)";
}
