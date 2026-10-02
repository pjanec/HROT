using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

// Each HrotRunnerHarness uses a unique DDS domain ID (Interlocked.Increment from base 100),
// so test classes are safe to run in parallel — different domains never see each other's traffic.
// MaxParallelThreads = 4 limits simultaneous DDS participants to a manageable level.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]

// EditorOfflineTests use RCU hot-plug which schedules background drain tasks on the thread
// pool.  Running this collection in parallel with DDS-heavy tests exhausts all 4 parallel
// slots, starves the thread pool, and causes SwitchToExternalAsync to time out.  Marking the
// collection non-parallel ensures the RCU drain tasks always find a free thread.
[CollectionDefinition("EditorOfflineTests", DisableParallelization = true)]
public sealed class EditorOfflineTestsCollection { }

// ── 2026-09-01: two collections were NAMED by test classes but never DEFINED ──────────────
//
// 🔴 MEASURED, per class, full suite vs isolation:
//      EqsContextSlotTests            4 red in-suite   →  7/7 PASS alone
//      Eqs.AccurateLosPhaseTests      3 red in-suite   →  3/3 PASS alone
//      Eqs.EqsDistributedTests        1 red in-suite   →  3/3 PASS alone
//      AllSubsystemsClusterTransition 2 red in-suite   →  2/2 PASS alone
//   ⇒ 10 of the suite's 31 failures were NOT DEFECTS. They are this file's line-10 disease,
//     one level out: thread-pool starvation while DDS-heavy collections share the 4 slots.
//
// ⛔ WHY IT WENT UNNOTICED: `[Collection("EqsIntegrationTests")]` / `[Collection("HeavyE2ETests")]`
//    compile and run happily with NO matching [CollectionDefinition]. xUnit groups the classes
//    (so they are sequential *among themselves*) but, absent a definition, it cannot know they
//    should not run alongside OTHER collections. So the attribute LOOKS like it isolates them
//    and does only half the job — silently. Naming a collection is not the same as defining it.
//
// ⚠ The line-5 comment above stays TRUE and is not what broke: domains really are unique per
//   harness, and these failures are not cross-talk. They are contention. Same conclusion the
//   EditorOfflineTests block reached; these two collections simply never got the same treatment.
[CollectionDefinition("EqsIntegrationTests", DisableParallelization = true)]
public sealed class EqsIntegrationTestsCollection { }

[CollectionDefinition("HeavyE2ETests", DisableParallelization = true)]
public sealed class HeavyE2ETestsCollection { }

internal static class ThreadPoolInit
{
    /// <summary>
    /// Pre-warm the thread pool to avoid the 500ms starvation delay that occurs when
    /// async continuations (DdsCommandClient TCS, gateway await chains) need new threads
    /// while the 4 parallel test threads are blocking in PumpUntil.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
        => ThreadPool.SetMinThreads(32, 32);
}
