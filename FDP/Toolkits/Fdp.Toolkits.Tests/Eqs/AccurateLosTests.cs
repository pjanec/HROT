using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="AccurateLineOfSightTest"/> (TASK-EQS-019).
    /// </summary>
    public class AccurateLosTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public AccurateLosTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<TargetMemory>();
            // Consume entity index 0 so observers never have index 0.
            // Index 0 would make rayId=0L for candidate i=0, which accidentally matches
            // the default-initialized (zeroed) ring buffer slot.
            _repo.CreateEntity();
        }

        public void Dispose()
        {
            // Clean up RaycastBatchData NativeArray if created.
            if (_repo.HasSingleton<RaycastBatchData>())
            {
                var batch = _repo.GetSingleton<RaycastBatchData>();
                if (batch.Hits.IsCreated)
                    batch.Hits.Dispose();
            }
            _repo.Dispose();
        }

        private Entity CreateObserverWithThreat(float threatScore, float threatThreshold, float threatX, float threatY)
        {
            var observer = _repo.CreateEntity();
            var mem = new TargetMemory();
            unsafe
            {
                mem.Count          = 1;
                mem.ThreatScores[0] = threatScore;
                mem.PositionsX[0]   = threatX;
                mem.PositionsY[0]   = threatY;
            }
            _repo.AddComponent(observer, mem);
            return observer;
        }

        private void SetupRaycastSingletons(int maxBudget)
        {
            _repo.SetSingleton(new RaycastBatchData
            {
                Hits = new NativeArray<RaycastHit>(PhysicsConstants.RaycastBatchCapacity, Allocator.Persistent),
            });
            _repo.SetSingletonUnmanaged(new EqsSolverGlobalState
            {
                MaxAccurateRaycastsPerSolverTick = maxBudget,
                AccurateRaysSubmittedThisTick    = 0,
            });
        }

        /// <summary>
        /// T-ALU1: Ring buffer already has result (HasHit=1) → candidate resolved, no FlagPendingRay.
        /// </summary>
        [Fact]
        public void AccurateLos_RingBufferHit_CandidateResolved()
        {
            var observer = CreateObserverWithThreat(threatScore: 100f, threatThreshold: 0f, threatX: 10f, threatY: 0f);
            SetupRaycastSingletons(maxBudget: 2048);

            // Pre-fill ring buffer for candidate i=0 with HasHit=1 (blocked = good cover).
            long rayId = ((long)observer.Index << 32) | 0u;
            int  slot  = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            ref var batch = ref _repo.GetSingleton<RaycastBatchData>();
            batch.Hits[slot] = new RaycastHit { RayId = rayId, HasHit = 1 };

            Span<EqsResult> candidates = stackalloc EqsResult[1];
            candidates[0] = new EqsResult { PositionX = 5f, PositionY = 0f, EntityId = 0L };

            var sensor = new EqsSensor { ThreatThreshold = 0f };
            new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, _repo, candidates);

            // Resolved via ring buffer: not rejected, flag bit 0 set (occluded = good cover).
            Assert.NotEqual(-1L, candidates[0].EntityId);
            Assert.NotEqual(0, candidates[0].Flags & 1);
            // FlagPendingRay cleared.
            Assert.Equal(0, candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay);
        }

        /// <summary>
        /// T-ALU1b: Ring buffer has result with HasHit=0 (clear LOS) → candidate rejected.
        /// </summary>
        [Fact]
        public void AccurateLos_RingBufferMiss_CandidateRejected()
        {
            var observer = CreateObserverWithThreat(threatScore: 100f, threatThreshold: 0f, threatX: 10f, threatY: 0f);
            SetupRaycastSingletons(maxBudget: 2048);

            long rayId = ((long)observer.Index << 32) | 0u;
            int  slot  = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
            ref var batch = ref _repo.GetSingleton<RaycastBatchData>();
            // HasHit=0: clear LOS → exposed → reject.
            batch.Hits[slot] = new RaycastHit { RayId = rayId, HasHit = 0 };

            Span<EqsResult> candidates = stackalloc EqsResult[1];
            candidates[0] = new EqsResult { PositionX = 5f, PositionY = 0f, EntityId = 0L };

            var sensor = new EqsSensor { ThreatThreshold = 0f };
            new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, _repo, candidates);

            // Clear LOS = exposed = rejected.
            Assert.Equal(-1L, candidates[0].EntityId);
            Assert.Equal(0, candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay);
        }

        /// <summary>
        /// T-ALU2: Budget=0 → FlagPendingRay set, no rays submitted.
        /// </summary>
        [Fact]
        public void AccurateLos_BudgetZero_FlagPendingRaySet()
        {
            var observer = CreateObserverWithThreat(threatScore: 100f, threatThreshold: 0f, threatX: 10f, threatY: 0f);
            SetupRaycastSingletons(maxBudget: 0);

            // Ring buffer slot does NOT have the matching RayId (default RayId=0 != our computed rayId).
            // (The slot may have RayId=0 by default, but our rayId includes observer.Index so it won't match
            //  unless observer.Index==0 and i==0. Use i=0 but a different slot to be safe.)

            Span<EqsResult> candidates = stackalloc EqsResult[1];
            candidates[0] = new EqsResult { PositionX = 5f, PositionY = 0f, EntityId = 0L };

            var sensor = new EqsSensor { ThreatThreshold = 0f };
            new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, _repo, candidates);

            // Budget=0: no event submitted, but FlagPendingRay still set.
            Assert.NotEqual(0, candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay);
            // AccurateRaysSubmittedThisTick remains 0.
            Assert.Equal(0, _repo.GetSingletonUnmanaged<EqsSolverGlobalState>().AccurateRaysSubmittedThisTick);
        }

        /// <summary>
        /// T-ALU3: Budget=2 with 3 candidates whose ring buffer slots are empty
        /// → exactly 2 rays submitted, all 3 candidates marked pending.
        /// </summary>
        [Fact]
        public void AccurateLos_BudgetTwo_TwoCandidatesSubmitted_AllPending()
        {
            var observer = CreateObserverWithThreat(threatScore: 100f, threatThreshold: 0f, threatX: 10f, threatY: 0f);
            SetupRaycastSingletons(maxBudget: 2);

            // Ensure the ring buffer slots for i=0,1,2 do NOT match (leave default RayId=0).
            // Since observer.Index >= 1 in a fresh repo (entity 0 is usually the first entity,
            // but CreateEntity might assign index 0). Let's force a unique observer index by
            // checking that computed rayIds won't accidentally match default slot RayId=0.
            // For safety, set the slots to a sentinel that won't match.
            ref var batch = ref _repo.GetSingleton<RaycastBatchData>();
            for (int i = 0; i < 3; i++)
            {
                long rayId = ((long)observer.Index << 32) | (uint)i;
                int  slot  = (int)((uint)rayId % (uint)PhysicsConstants.RaycastBatchCapacity);
                // Explicitly set to non-matching RayId.
                batch.Hits[slot] = new RaycastHit { RayId = long.MaxValue, HasHit = 0 };
            }

            Span<EqsResult> candidates = stackalloc EqsResult[3];
            candidates[0] = new EqsResult { PositionX = 1f, PositionY = 0f, EntityId = 0L };
            candidates[1] = new EqsResult { PositionX = 2f, PositionY = 0f, EntityId = 0L };
            candidates[2] = new EqsResult { PositionX = 3f, PositionY = 0f, EntityId = 0L };

            var sensor = new EqsSensor { ThreatThreshold = 0f };
            new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, _repo, candidates);

            // Exactly 2 rays submitted (budget=2).
            Assert.Equal(2, _repo.GetSingletonUnmanaged<EqsSolverGlobalState>().AccurateRaysSubmittedThisTick);

            // All 3 candidates have FlagPendingRay set.
            Assert.NotEqual(0, candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay);
            Assert.NotEqual(0, candidates[1].Flags & AccurateLineOfSightTest.FlagPendingRay);
            Assert.NotEqual(0, candidates[2].Flags & AccurateLineOfSightTest.FlagPendingRay);

            // None rejected (still EntityId=0).
            Assert.NotEqual(-1L, candidates[0].EntityId);
            Assert.NotEqual(-1L, candidates[1].EntityId);
            Assert.NotEqual(-1L, candidates[2].EntityId);
        }

        /// <summary>
        /// T-ALU4: Bypass when threat score is below threshold → no FlagPendingRay, no events.
        /// </summary>
        [Fact]
        public void AccurateLos_BypassWhenThreatBelowThreshold()
        {
            // ThreatScores[0]=10, ThreatThreshold=50 → bypass.
            var observer = CreateObserverWithThreat(threatScore: 10f, threatThreshold: 50f, threatX: 10f, threatY: 0f);
            SetupRaycastSingletons(maxBudget: 2048);

            Span<EqsResult> candidates = stackalloc EqsResult[2];
            candidates[0] = new EqsResult { PositionX = 1f, PositionY = 0f, EntityId = 0L };
            candidates[1] = new EqsResult { PositionX = 2f, PositionY = 0f, EntityId = 0L };

            var sensor = new EqsSensor { ThreatThreshold = 50f };
            new AccurateLineOfSightTest().ExecuteBatch(observer, ref sensor, _repo, candidates);

            // Bypass: no FlagPendingRay, no rejection.
            Assert.Equal(0, candidates[0].Flags & AccurateLineOfSightTest.FlagPendingRay);
            Assert.Equal(0, candidates[1].Flags & AccurateLineOfSightTest.FlagPendingRay);
            Assert.NotEqual(-1L, candidates[0].EntityId);
            Assert.NotEqual(-1L, candidates[1].EntityId);
            // No rays submitted.
            Assert.Equal(0, _repo.GetSingletonUnmanaged<EqsSolverGlobalState>().AccurateRaysSubmittedThisTick);
        }
    }
}
