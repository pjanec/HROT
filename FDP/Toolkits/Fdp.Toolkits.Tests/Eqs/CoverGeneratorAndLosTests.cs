using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CoverPointsGenerator"/> and <see cref="CheapLineOfSightTest"/>
    /// (TASK-EQS-013).
    /// </summary>
    public class CoverGeneratorAndLosTests : IDisposable
    {
        private readonly EntityRepository _repo;

        // LOS stub: always returns true (clear = exposed).
        private sealed class ExposedLosService : ILosService
        {
            public bool HasCheapLineOfSight(Vector2 from, Vector2 to) => true;
        }

        public CoverGeneratorAndLosTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<TargetMemory>();
        }

        public void Dispose() => _repo.Dispose();

        // T-CG1: CoverPointsGenerator produces positional candidates (EntityId=0).
        [Fact]
        public void CoverPointsGenerator_ProducesPositionalCandidates()
        {
            var provider = new ManualCoverProvider(new[]
            {
                new CoverPoint { PositionX = 3f, PositionY = 0f, Quality = 1f },
                new CoverPoint { PositionX = 7f, PositionY = 0f, Quality = 0.8f },
            });
            _repo.SetSingletonManaged<ICoverProvider>(provider);

            var observer = _repo.CreateEntity();
            _repo.AddComponent(observer, new SimTransform
            {
                Position = System.Numerics.Vector3.Zero,
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var sensor = new EqsSensor { SearchRadius = 10f };
            var candidates = new EqsResult[16];
            var gen = new CoverPointsGenerator();

            int count = gen.Generate(observer, ref sensor, _repo, candidates.AsSpan());

            Assert.Equal(2, count);
            // Both candidates are positional (EntityId=0).
            Assert.Equal(0L, candidates[0].EntityId);
            Assert.Equal(0L, candidates[1].EntityId);
            // PositionX values match the provider points (order may vary).
            bool hasPos3 = (Math.Abs(candidates[0].PositionX - 3f) < 0.001f)
                        || (Math.Abs(candidates[1].PositionX - 3f) < 0.001f);
            bool hasPos7 = (Math.Abs(candidates[0].PositionX - 7f) < 0.001f)
                        || (Math.Abs(candidates[1].PositionX - 7f) < 0.001f);
            Assert.True(hasPos3, "Expected cover point at x=3");
            Assert.True(hasPos7, "Expected cover point at x=7");
        }

        // T-LOS1: CheapLineOfSightTest skips when TargetMemory.Count == 0 (bypass).
        [Fact]
        public unsafe void CheapLineOfSightTest_BypassWhenNoThreats_CandidatesUnchanged()
        {
            var observer = _repo.CreateEntity();
            var mem = new TargetMemory(); // Count = 0 by default.
            _repo.AddComponent(observer, mem);

            // Context slot entity with SimTransform -- needed to reach the Count==0 bypass gate.
            var targetEntity = _repo.CreateEntity();
            _repo.AddComponent(targetEntity, new SimTransform
            {
                Position = new System.Numerics.Vector3(10f, 0f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f, Score = 1f },
                new EqsResult { EntityId = 0L, PositionX = 2f, PositionY = 0f, Score = 1f },
            };

            var sensor = new EqsSensor { ThreatThreshold = 50f, ContextSlot1 = targetEntity };
            var test = new CheapLineOfSightTest(new ExposedLosService());
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Bypass: both candidates unchanged.
            Assert.Equal(0L, candidates[0].EntityId);
            Assert.Equal(0L, candidates[1].EntityId);
            Assert.Equal(1f, candidates[0].Score);
            Assert.Equal(1f, candidates[1].Score);
        }

        // T-LOS2: CheapLineOfSightTest skips when threat score < ThreatThreshold (bypass).
        [Fact]
        public unsafe void CheapLineOfSightTest_BypassWhenScoreBelowThreshold_CandidatesUnchanged()
        {
            var observer = _repo.CreateEntity();
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 1L, posX: 10f, posY: 0f, scoreBoost: 10f, tick: 1);
            _repo.AddComponent(observer, mem);

            // Context slot entity -- needed to reach the threshold bypass gate.
            var targetEntity = _repo.CreateEntity();
            _repo.AddComponent(targetEntity, new SimTransform
            {
                Position = new System.Numerics.Vector3(10f, 0f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f },
                new EqsResult { EntityId = 0L, PositionX = 2f, PositionY = 0f },
            };

            // ThreatScores[0] = 10f < ThreatThreshold = 50f  => bypass.
            var sensor = new EqsSensor { ThreatThreshold = 50f, ContextSlot1 = targetEntity };
            var test = new CheapLineOfSightTest(new ExposedLosService());
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Bypass triggered: candidates unchanged.
            Assert.Equal(0L, candidates[0].EntityId);
            Assert.Equal(0L, candidates[1].EntityId);
        }

        // T-LOS3: CheapLineOfSightTest rejects exposed candidates (ExposedLosService).
        [Fact]
        public unsafe void CheapLineOfSightTest_RejectsExposedCandidates()
        {
            var observer = _repo.CreateEntity();
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 1L, posX: 10f, posY: 0f, scoreBoost: 100f, tick: 1);
            _repo.AddComponent(observer, mem);

            // Context slot 1 entity provides threat position for the LOS test.
            var targetEntity = _repo.CreateEntity();
            _repo.AddComponent(targetEntity, new SimTransform
            {
                Position = new System.Numerics.Vector3(10f, 0f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f },
            };

            // ThreatScores[0] = 100f > ThreatThreshold = 50f => LOS test active.
            var sensor = new EqsSensor { ThreatThreshold = 50f, ContextSlot1 = targetEntity };
            var test = new CheapLineOfSightTest(new ExposedLosService()); // always clear
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Exposed: candidate rejected with sentinel -1L.
            Assert.Equal(-1L, candidates[0].EntityId);
        }

        // T-LOS4: CheapLineOfSightTest keeps occluded candidates and sets flag bit 0.
        [Fact]
        public unsafe void CheapLineOfSightTest_KeepsOccludedCandidates_SetsFlagBit0()
        {
            var observer = _repo.CreateEntity();
            var mem = new TargetMemory();
            TargetMemory.AddOrUpdateTarget(ref mem, entityId: 1L, posX: 10f, posY: 0f, scoreBoost: 100f, tick: 1);
            _repo.AddComponent(observer, mem);

            // Context slot 1 entity provides threat position for the LOS test.
            var targetEntity = _repo.CreateEntity();
            _repo.AddComponent(targetEntity, new SimTransform
            {
                Position = new System.Numerics.Vector3(10f, 0f, 0f),
                Rotation = System.Numerics.Quaternion.Identity,
            });

            var candidates = new EqsResult[]
            {
                new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f, Flags = 0 },
            };

            var sensor = new EqsSensor { ThreatThreshold = 50f, ContextSlot1 = targetEntity };
            var test = new CheapLineOfSightTest(new BlockedLosService()); // always blocked
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Occluded: candidate kept, flag bit 0 set.
            Assert.Equal(0L, candidates[0].EntityId);
            Assert.Equal(1, candidates[0].Flags & 1);
        }
    }
}
