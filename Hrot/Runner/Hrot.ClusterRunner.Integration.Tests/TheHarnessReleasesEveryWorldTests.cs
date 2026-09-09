using System;
using Fdp.Core;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>QA-004</c> — a node teardown must RELEASE every world it built.</b>
/// 📄 Durable record: <c>docs/TESTING_Harness_And_Goldens.md</c> §"The leak that read as flakiness".
///
/// <para>⛔⛔ <b>This is the rail for a defect that fails SILENTLY and had been misread as flaky tests
/// for ~40 batches.</b> 📐 Measured 2026-08-26: four subsystems disposed their kernel and none disposed
/// the <see cref="EntityRepository"/> the kernel ran on, so every harness in the suite left one behind.
/// A leaked repository throws nothing and logs nothing — it holds an <c>int[1_000_000]</c> free list
/// plus one <c>NativeChunkTable</c> per registered component until the process dies. Accumulated over a
/// run it exhausted a 16 GB box, and the resulting <c>OutOfMemoryException</c>s and DDS teardown faults
/// presented as an unrelated, rotating set of "flaky" failures plus a test-host crash.</para>
///
/// <para>⚠ <see cref="EntityRepository.LiveInstanceCount"/> is PROCESS-WIDE, so this collection is
/// <c>DisableParallelization</c>: another collection constructing a world concurrently would make the
/// before/after delta meaningless. ⛔ That is not an ordering hack hiding a defect — it is what makes a
/// process-wide measurement legitimate, and it is the ONLY reason the attribute is here.</para>
/// </summary>
[Collection("WorldLeakRails")]
public sealed class TheHarnessReleasesEveryWorldTests
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE WHOLE CLAIM IN ONE NUMBER.</b> A full five-subsystem harness — Orchestrator,
    /// SimHost, IG, ExCon and CGF — is built and disposed; the live-repository count must come back to
    /// where it started.
    ///
    /// <para>⚠ Deliberately asserted as a DELTA, never an absolute: other assemblies and earlier tests
    /// legitimately hold worlds, and the question here is only whether THIS round-trip left anything
    /// behind.</para>
    /// </summary>
    [Fact]
    public void A_full_harness_round_trip_leaves_no_world_behind()
    {
        int before = EntityRepository.LiveInstanceCount;

        using (var harness = new HrotRunnerHarness())
        {
            // ⭐ A booted harness must actually HOLD worlds — otherwise a broken harness that builds
            //    nothing would satisfy the delta assertion below vacuously.
            Assert.True(EntityRepository.LiveInstanceCount > before,
                "the harness constructed no EntityRepository at all — the rail below would be vacuous");

            Assert.NotNull(harness.SimHost.World);
        }

        int leaked = EntityRepository.LiveInstanceCount - before;

        // ⭐ A bare count says "something leaked" and stops there. Re-run with FDP_TRACK_REPO_LEAKS=1
        //    and the failure names the construction site — which is how QA-006's SnapshotPool was
        //    found after the count alone had pointed only at "ten of them, somewhere".
        Assert.True(leaked == 0,
            $"{leaked} EntityRepository instance(s) survived the harness teardown.\n" +
            string.Join("\n---\n", EntityRepository.DumpLiveOrigins()));
    }

    /// <summary>
    /// ⭐⭐ <b>The same claim, pinned to ONE named world</b>, so a failure says <i>which</i> teardown path
    /// forgot rather than only <i>that</i> the count drifted.
    ///
    /// <para>⭐ <c>SimHostApp</c> is the one measured to leak first and the one whose world every
    /// replication test runs on; <c>SimHostSubsystem.World</c> returns <see langword="null"/> after
    /// shutdown, which is exactly why the reference has to be captured while the node is up.</para>
    /// </summary>
    [Fact]
    public void The_simhost_world_is_disposed_by_shutdown_not_merely_dropped()
    {
        EntityRepository world;

        using (var harness = new HrotRunnerHarness("simhost", domainId: 211))
        {
            world = harness.SimHost.World
                    ?? throw new InvalidOperationException("SimHost exposed no world after boot");
            Assert.False(world.IsDisposed);
        }

        Assert.True(world.IsDisposed,
            "SimHostApp.Shutdown released the kernel but left its EntityRepository alive — QA-001");
    }
}

/// <summary>
/// ⚠ Serialises the world-leak rails against every other collection — see the note on
/// <see cref="TheHarnessReleasesEveryWorldTests"/> for why a process-wide counter needs it.
/// </summary>
[CollectionDefinition("WorldLeakRails", DisableParallelization = true)]
public sealed class WorldLeakRailsCollection { }
