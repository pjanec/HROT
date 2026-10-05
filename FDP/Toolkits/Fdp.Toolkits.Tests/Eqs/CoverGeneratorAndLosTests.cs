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

        // LOS stubs (the 3-D seam — CE-210): always clear (exposed) / always blocked (occluded).
        private sealed class ExposedLosService : ILosService
        {
            public bool HasLineOfSight(Vector3 eye, Vector3 aim) => true;
        }

        private sealed class BlockedLos : ILosService
        {
            public bool HasLineOfSight(Vector3 eye, Vector3 aim) => false;
        }

        private sealed class RecordingLos : ILosService
        {
            public Vector3 LastEye;
            public bool HasLineOfSight(Vector3 eye, Vector3 aim) { LastEye = eye; return true; }
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

            // Freshness[0] = 10f < ThreatThreshold = 50f  => bypass.
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

            // Freshness[0] = 100f > ThreatThreshold = 50f => LOS test active.
            var sensor = new EqsSensor { ThreatThreshold = 50f, ContextSlot1 = targetEntity };
            var test = new CheapLineOfSightTest(new ExposedLosService()); // always clear
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Exposed: candidate rejected with sentinel -1L.
            Assert.Equal(-1L, candidates[0].EntityId);
        }

        // T-LOS4: CheapLineOfSightTest keeps occluded candidates; §4.2 bit 1 (HasLOSToContext1) is judged and clear.
        [Fact]
        public unsafe void CheapLineOfSightTest_KeepsOccludedCandidates_JudgesHasLosBitClear()
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
            var test = new CheapLineOfSightTest(new BlockedLos()); // always blocked
            test.ExecuteBatch(observer, ref sensor, _repo, candidates.AsSpan());

            // Occluded: candidate kept; HasLOSToContext1 judged (meaningful) and false. ⛔ SUPERSEDED (EQS §19.5): bit 0 = "covered".
            Assert.Equal(0L, candidates[0].EntityId);
            Assert.Equal(2, candidates[0].FlagsMeaningful & 2);
            Assert.Equal(0, candidates[0].Flags & 2);
        }

        /// <summary>⭐ <c>CE-3063</c> ③ — with NO entity in slot 1 the sensor's context POINT is the other side (a heard contact):
        /// the sight is judged from that point at the default eye height, so a candidate it sees is dropped. Without the point
        /// (mask clear) the test has nothing to judge against and keeps the candidate unjudged, as before.</summary>
        [Fact]
        public void CE3063_CheapLineOfSight_JudgesFromTheContextPoint_WhenSlotOneIsEmpty()
        {
            var observer = _repo.CreateEntity();
            var los = new RecordingLos();
            var test = new CheapLineOfSightTest(los) { Viewer = EqsLosViewer.Slot };

            var withPoint = new EqsSensor { ContextPoint1 = new Vector3(40f, 5f, 0f), ContextPointMask = EqsSensor.Point1Bit };
            var seen = new[] { new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f } };
            test.ExecuteBatch(observer, ref withPoint, _repo, seen.AsSpan());
            Assert.Equal(-1L, seen[0].EntityId);   // seen from the heard point ⇒ not cover
            Assert.Equal(40f, los.LastEye.X);
            Assert.Equal(5f, los.LastEye.Y);
            Assert.True(EqsContext.AnchorPosition(_repo, observer, withPoint, 1, out var anchor) && anchor == withPoint.ContextPoint1);

            var noPoint = new EqsSensor { ContextPoint1 = new Vector3(40f, 5f, 0f) };   // mask clear ⇒ no point
            var kept = new[] { new EqsResult { EntityId = 0L, PositionX = 1f, PositionY = 0f } };
            test.ExecuteBatch(observer, ref noPoint, _repo, kept.AsSpan());
            Assert.Equal(0L, kept[0].EntityId);
            Assert.Equal(0, (int)kept[0].FlagsMeaningful);
        }
    }
}
