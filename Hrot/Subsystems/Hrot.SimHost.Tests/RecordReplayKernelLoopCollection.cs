using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ <c>BP-534</c> / <c>TM-036</c> — the five classes that drive a REAL record → replay stack behind a background kernel loop
/// run with NOTHING else in parallel.
/// </summary>
/// <remarks>
/// 📐 Measured <c>2026-10-04</c> on the full suite, one row per configuration:
/// <list type="bullet">
///   <item>default (all parallel): a wall-clock timeout in roughly every second or third run — the victim rotates
///     (<c>PrepareRecordingAsync_InstallsRecordingModule</c> 10 s, <c>TeardownReplay_PreservesEntityRepositoryState</c>,
///     <c>ClusterSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch</c> 20 s);</item>
///   <item>serial among themselves only: 1 failure in 14 runs;</item>
///   <item>serial against everything (this collection): 0 in 8 runs, same wall-clock;</item>
///   <item>the failing test body alone, 300 iterations: 0 hangs, the first install ~30 ms.</item>
/// </list>
/// A stage trace of <c>ModuleHostKernel.InstallModuleAsync</c> under the full suite showed every install completing (the
/// background topology build up to ~1.3 s) and, at a timeout, the kernel loop managing 23 frames in 5 s (~200 ms per frame
/// instead of 16) ⇒ the tests are starved of CPU while the suite runs, not hung in product code.
/// <para>⭐ Why isolation and not a longer timeout or a filter-around (<c>R-131</c>): these tests are wall-clock bound by design
/// (a real kernel loop, real recording files), and each <c>AsyncRecorder</c> pre-allocates ~130 MB. ⚠ If a timeout ever
/// reappears inside this collection, it is no longer contention — chase it as a hang.</para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RecordReplayKernelLoopCollection
{
    public const string Name = "Record/replay behind a kernel loop (Hrot.SimHost)";
}
