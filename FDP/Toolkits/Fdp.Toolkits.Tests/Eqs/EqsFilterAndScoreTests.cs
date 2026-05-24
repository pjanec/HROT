using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="FactionFilterTest"/> and <see cref="DistanceScoreTest"/>
    /// (TASK-EQS-010).
    /// </summary>
    public class EqsFilterAndScoreTests : System.IDisposable
    {
        private readonly EntityRepository _repo;

        public EqsFilterAndScoreTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<EntityInfo>();
            _repo.RegisterComponent<SimTransform>();
        }

        public void Dispose() => _repo.Dispose();

        // ── FactionFilterTest ─────────────────────────────────────────────────────

        // T-F1: FactionFilter rejects wrong factions; positional candidate untouched.
        [Fact]
        public void FactionFilter_RejectsWrongFactions_KeepsHostileAndPositional()
        {
            // Create entities with each ForceId.
            var friendEnt = _repo.CreateEntity();
            _repo.AddComponent(friendEnt, new EntityInfo { ForceId = ForceId.Friend });

            var hostileEnt = _repo.CreateEntity();
            _repo.AddComponent(hostileEnt, new EntityInfo { ForceId = ForceId.Hostile });

            var neutralEnt = _repo.CreateEntity();
            _repo.AddComponent(neutralEnt, new EntityInfo { ForceId = ForceId.Neutral });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = (long)friendEnt.PackedValue },
                new EqsResult { EntityId = (long)hostileEnt.PackedValue },
                new EqsResult { EntityId = (long)neutralEnt.PackedValue },
                new EqsResult { EntityId = 0L }, // positional
            };

            // FactionFilter = 0b100 = 4 => only Hostile (bit 2) accepted.
            var sensor  = new EqsSensor { FactionFilter = 4u };
            var filter  = new FactionFilterTest();
            var span    = candidates.AsSpan();

            filter.ExecuteBatch(Entity.Null, ref sensor, _repo, span);

            Assert.Equal(-1L, span[0].EntityId); // Friend rejected
            Assert.Equal((long)hostileEnt.PackedValue, span[1].EntityId); // Hostile kept
            Assert.Equal(-1L, span[2].EntityId); // Neutral rejected
            Assert.Equal(0L,  span[3].EntityId); // positional untouched
        }

        // T-F2: FactionFilter skips candidates that are already rejected (-1L).
        [Fact]
        public void FactionFilter_SkipsAlreadyRejected_NoException()
        {
            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = -1L },
            };

            var sensor = new EqsSensor { FactionFilter = 4u };
            var filter = new FactionFilterTest();
            var span   = candidates.AsSpan();

            // Must not throw even though no entity with PackedValue=-1 exists.
            filter.ExecuteBatch(Entity.Null, ref sensor, _repo, span);

            Assert.Equal(-1L, span[0].EntityId);
        }

        // ── DistanceScoreTest ─────────────────────────────────────────────────────

        // T-F3: DistanceScore skips rejected candidates; scores positional candidates.
        [Fact]
        public void DistanceScore_SkipsRejected_ScoresPositional()
        {
            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = -1L, Score = 0f, PositionX = 0f, PositionY = 0f }, // rejected
                new EqsResult { EntityId = 0L,  Score = 0f, PositionX = 5f, PositionY = 0f }, // positional at (5,0)
            };

            var sensor = new EqsSensor { SearchRadius = 10f };
            var scorer = new DistanceScoreTest();
            var span   = candidates.AsSpan();

            scorer.ExecuteBatch(observer, ref sensor, _repo, span);

            Assert.Equal(0f, span[0].Score); // rejected: score untouched
            Assert.True(span[1].Score > 0f); // positional at distance 5: score > 0
        }

        // T-F4: DistanceScore gives higher score to the closer candidate.
        [Fact]
        public void DistanceScore_CloserCandidateHigherScore()
        {
            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = Vector3.Zero,
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 1L, Score = 0f, PositionX = 2f, PositionY = 0f }, // distance 2
                new EqsResult { EntityId = 2L, Score = 0f, PositionX = 8f, PositionY = 0f }, // distance 8
            };

            var sensor = new EqsSensor { SearchRadius = 10f };
            var scorer = new DistanceScoreTest();
            var span   = candidates.AsSpan();

            scorer.ExecuteBatch(observer, ref sensor, _repo, span);

            // Closer candidate (distance 2) should have higher score than distant (distance 8).
            Assert.True(span[0].Score > span[1].Score);
        }
    }
}
