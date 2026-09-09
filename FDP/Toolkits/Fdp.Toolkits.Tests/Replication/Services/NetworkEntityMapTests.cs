using System;
using Xunit;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Tests.Services
{
    public class NetworkEntityMapTests
    {
        [Fact]
        public void Register_And_Get_Works()
        {
            var map = new NetworkEntityMap();
            var entity = new Entity(123);
            long netId = 1001;

            map.Register(netId, entity);

            Assert.True(map.TryGetEntity(netId, out var resultEntity));
            Assert.Equal(entity, resultEntity);

            Assert.True(map.TryGetNetworkId(entity, out var resultNetId));
            Assert.Equal(netId, resultNetId);
        }

        [Fact]
        public void Unregister_MovesToGraveyard()
        {
            var map = new NetworkEntityMap(graveyardDurationFrames: 10);
            var entity = new Entity(123);
            long netId = 1001;
            uint frame = 50;

            map.Register(netId, entity);
            map.Unregister(netId, frame);

            Assert.False(map.TryGetEntity(netId, out _));
            Assert.True(map.IsGraveyard(netId));
        }

        [Fact]
        public void PruneGraveyard_RemovesOldEntries()
        {
            var map = new NetworkEntityMap(graveyardDurationFrames: 10);
            long netId = 1001;
            uint deathFrame = 50;
            
            // Manually simulate unregister (or just expose AddToGraveyard via internal/test helper, 
            // but public Unregister is easy enough if we register first)
            map.Register(netId, new Entity(1));
            map.Unregister(netId, deathFrame);

            // Check
            Assert.True(map.IsGraveyard(netId));
            
            // Frame 55 (diff 5) -> Should keep
            map.PruneGraveyard(55);
            Assert.True(map.IsGraveyard(netId));
            
            // Frame 61 (diff 11 > 10) -> Should remove
            map.PruneGraveyard(61);
            Assert.False(map.IsGraveyard(netId));
        }

        [Fact]
        public void Register_ReusesGraveyardId_RemovesFromGraveyard()
        {
            var map = new NetworkEntityMap();
            long netId = 1001;
            map.Register(netId, new Entity(1));
            map.Unregister(netId, 10);
            
            Assert.True(map.IsGraveyard(netId));
            
            // Reuse ID
            map.Register(netId, new Entity(2));
            
            Assert.False(map.IsGraveyard(netId));
            Assert.True(map.TryGetEntity(netId, out var e));
            Assert.Equal(new Entity(2), e);
        }

        // ── CE-144: the graveyard is now TICKED by the shared code ───────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>CE-144</c> acceptance ⑤ — <b><c>DisposalMonitoringSystem</c> prunes BOTH:
        /// dead entities into the graveyard, and the graveyard itself.</b>
        ///
        /// <para>📐 Until <c>2026-09-03</c> <c>PruneGraveyard</c> had <b>zero production callers</b>, so
        /// the list only ever grew. ⛔ Nothing observed it — <c>IsGraveyard</c> has no production
        /// readers — so the payoff is BOUNDED MEMORY rather than corrected behaviour, and this rail says
        /// so rather than implying otherwise.</para>
        ///
        /// <para>⚠ It also pins the CLOCK. <c>PruneDeadEntities</c> stamps
        /// <c>SimulationTick</c> (the documented frame clock), not <c>GlobalVersion</c> — the two diverge
        /// under a mid-tick debug burst, and a window denominated in frames must be aged on the frame
        /// clock. 🔒 <c>EntityRepository</c>: <i>"Frame-index / wall-tick consumers must read this, NOT
        /// GlobalVersion"</i>.</para>
        /// </summary>
        [Fact]
        public void DisposalMonitoringSystem_PrunesDeadEntitiesAndThenTheGraveyard()
        {
            var repo = new EntityRepository();
            var map  = new NetworkEntityMap(graveyardDurationFrames: 3);
            var sys  = new Fdp.Toolkit.Replication.Systems.DisposalMonitoringSystem(map);

            var entity = repo.CreateEntity();
            const long netId = 4242;
            map.Register(netId, entity);

            repo.DestroyEntity(entity);

            // Tick 1: the dead entity is noticed and its id moves into the graveyard.
            repo.Tick();
            sys.Execute(repo, 1f / 60f);
            Assert.False(map.TryGetEntity(netId, out _));
            Assert.True(map.IsGraveyard(netId),
                "PruneDeadEntities did not move the destroyed entity's id into the graveyard.");

            // Inside the window it must STAY — otherwise the window means nothing.
            repo.Tick();
            sys.Execute(repo, 1f / 60f);
            Assert.True(map.IsGraveyard(netId),
                "The id was retired while still inside graveyardDurationFrames. Either the prune ignores " +
                "the window, or PruneDeadEntities stamped a different clock than PruneGraveyard reads.");

            // Past the window it must be retired — this is the half that had no production caller.
            for (int i = 0; i < 5; i++) { repo.Tick(); sys.Execute(repo, 1f / 60f); }
            Assert.False(map.IsGraveyard(netId),
                "The graveyard still holds the id well past its window. PruneGraveyard is not being " +
                "ticked, so the list grows without bound for the life of the node.");
        }
    }
}
