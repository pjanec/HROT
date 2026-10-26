using Fdp.Core;
using Fdp.Toolkit.Replay;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> P2 (docs/DESIGN_Replay_Time_Axis_And_Review.md §12l) — the world's replay flag: a world with none is live; a
    /// replay that RESTORES this node's recording stops every writer; a LISTENING replay (the moment is outside this node's recording,
    /// R-4012) stops the writers but not the ghost smoothers; a snapshot sees the live flag; a keyframe never carries it.
    /// </summary>
    public sealed class TheReplayStateTests
    {
        [Fact]
        public void AWorldWithNoFlag_IsLive()
        {
            using var world = new EntityRepository();
            Assert.False(ReplayState.IsActive(world));
            Assert.False(ReplayState.IsRestoring(world));
        }

        [Fact]
        public void Restoring_StopsEveryWriter_Listening_LeavesTheGhostSmoothersRunning_Leave_GoesLive()
        {
            using var world = new EntityRepository();
            ReplayState.Enter(world);
            Assert.True(ReplayState.IsActive(world));
            Assert.True(ReplayState.IsRestoring(world));

            ReplayState.SetListening(world, true);           // the moment is outside this node's recording
            Assert.True(ReplayState.IsActive(world));
            Assert.False(ReplayState.IsRestoring(world));

            ReplayState.SetListening(world, false);          // back inside: restoring again
            Assert.True(ReplayState.IsRestoring(world));

            ReplayState.SetListening(world, true);
            ReplayState.Leave(world);
            Assert.False(ReplayState.IsActive(world));
            Assert.False(ReplayState.IsRestoring(world));
            ReplayState.Enter(world);                        // a later replay does not inherit the old span's listening
            Assert.True(ReplayState.IsRestoring(world));
        }

        [Fact]
        public void ASnapshot_SeesTheLiveFlag_ItIsBorrowedNotCopied()
        {
            using var live = new EntityRepository();
            ReplayState.Ensure(live);
            using var snapshot = new EntityRepository();
            snapshot.SyncFrom(live);
            Assert.False(ReplayState.IsActive(snapshot));

            ReplayState.Enter(live);                         // flipped after the sync: the snapshot borrows the same object
            Assert.True(ReplayState.IsActive(snapshot));
        }
    }
}
