using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Squad.DangerArea.Fake;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests
{
    /// <summary>
    /// Phase-2 integration tests exercising SquadPerceptionMergeSystem and
    /// DangerAreaRefreshSystem in tandem on a 4-member squad.
    /// Success criteria: SC-P2-04-1 through SC-P2-04-4.
    /// </summary>
    public unsafe class Phase2IntegrationTests : IDisposable
    {
        private EntityRepository _repo;

        public Phase2IntegrationTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<DangerAreaSensor>();
            _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
            _repo.RegisterComponent<PartMetadata>();
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a 4-member squad and one sensor child on the commander.
        /// members[0] and [1] see contact A (entityId=100).
        /// members[2] sees contact B (entityId=200).
        /// members[3] sees nothing.
        /// The sensor child has a FakeDangerAreaProvider with one StreetCrossing descriptor.
        /// </summary>
        private (Entity commander, Entity[] members, Entity sensorChild, FakeDangerAreaProvider fake)
            CreatePhase2World()
        {
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new UnitRoster());
            _repo.AddComponent(commander, new Blackboard1024());
            _repo.AddComponent(commander, new SquadStateMarker());

            var members = new Entity[4];
            for (int i = 0; i < 4; i++)
            {
                var m = _repo.CreateEntity();
                _repo.AddComponent(m, new TargetMemory());
                _repo.AddComponent(m, new UnitSubordinate
                {
                    Commander   = commander,
                    Designation = TacticalDesignation.Undefined
                });
                ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
                UnitRoster.Add(ref roster, (long)m.PackedValue);
                members[i] = m;
            }

            // members[0] sees contact A with higher score.
            ref var mem0 = ref _repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 10f, 0f, 0.8f, tick: 1);

            // members[1] sees contact A with lower score but higher tick.
            ref var mem1 = ref _repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.AddOrUpdateTarget(ref mem1, 100L, 12f, 0f, 0.6f, tick: 2);

            // members[2] sees contact B.
            ref var mem2 = ref _repo.GetComponentRW<TargetMemory>(members[2]);
            TargetMemory.AddOrUpdateTarget(ref mem2, 200L, 30f, 0f, 0.3f, tick: 1);

            // Sensor child for the commander.
            var sensorChild = _repo.CreateEntity();
            _repo.AddComponent(sensorChild, new DangerAreaSensor());
            _repo.AddComponent(sensorChild, new DangerAreaCognitiveBuffer());
            _repo.AddComponent(sensorChild, new PartMetadata());
            ref var meta = ref _repo.GetComponentRW<PartMetadata>(sensorChild);
            meta.ParentEntity = commander;

            var fake = new FakeDangerAreaProvider();
            fake.Add("street-east-01", DangerAreaKind.StreetCrossing, 0.9f);

            return (commander, members, sensorChild, fake);
        }

        private ref SquadCognitiveState GetState(Entity commander)
        {
            ref var bb = ref _repo.GetComponentRW<Blackboard1024>(commander);
            return ref SquadCognitiveState.Project(ref bb);
        }

        private int FindContactIndex(Entity commander, long entityId)
        {
            ref var state = ref GetState(commander);
            var span = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts),
                16);
            for (int i = 0; i < state.Contacts.Count; i++)
                if (span[i].EntityId == entityId) return i;
            return -1;
        }

        // ── SC-P2-04-1: Contacts_MergeCorrectly ──────────────────────────────────

        [Fact]
        public void Contacts_MergeCorrectly()
        {
            var (commander, _, _, _) = CreatePhase2World();

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                _repo, commander, currentTick: 5, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(2, state.Contacts.Count);

            var contacts = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts),
                16);

            int idxA = FindContactIndex(commander, 100L);
            int idxB = FindContactIndex(commander, 200L);
            Assert.True(idxA >= 0, "Contact A (100) not found");
            Assert.True(idxB >= 0, "Contact B (200) not found");

            // Contact A: max threat = 0.8f, both members[0] and [1] = 0x3, latest tick = 2.
            Assert.Equal(0.8f,          contacts[idxA].ThreatScore,       precision: 5);
            Assert.Equal((ushort)0x3,   contacts[idxA].SourceMembersMask);
            Assert.Equal(2u,            contacts[idxA].LastSeenTick);

            // Contact B: threat = 0.3f, only member[2] = 0x4.
            Assert.Equal(0.3f,          contacts[idxB].ThreatScore,       precision: 5);
            Assert.Equal((ushort)0x4,   contacts[idxB].SourceMembersMask);
        }

        // ── SC-P2-04-2: DangerAreaBuffer_HasStreetCrossing ───────────────────────

        [Fact]
        public void DangerAreaBuffer_HasStreetCrossing()
        {
            var (_, _, sensorChild, fake) = CreatePhase2World();

            new DangerAreaRefreshSystem(fake).Run(_repo, sensorChild, currentSimTime: 0f);

            ref readonly var buf = ref _repo.GetComponentRO<DangerAreaCognitiveBuffer>(sensorChild);
            Assert.Equal(1, buf.Count);
            Assert.Equal(DangerAreaKind.StreetCrossing, buf.GetSpanRO()[0].Kind);
        }

        // ── SC-P2-04-3: MemberAdded_SourceMaskGrows ──────────────────────────────

        [Fact]
        public void MemberAdded_SourceMaskGrows()
        {
            var (commander, members, _, _) = CreatePhase2World();

            // First merge (mirrors SC-P2-04-1 state).
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                _repo, commander, currentTick: 5, mergeIntervalTicks: 1);

            // members[3] now also sees contact B.
            ref var mem3 = ref _repo.GetComponentRW<TargetMemory>(members[3]);
            TargetMemory.AddOrUpdateTarget(ref mem3, 200L, 30f, 0f, 0.5f, tick: 5);

            // Re-run merge at next tick.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                _repo, commander, currentTick: 6, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(2, state.Contacts.Count);

            var contacts = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts),
                16);

            int idxB = FindContactIndex(commander, 200L);
            Assert.True(idxB >= 0, "Contact B (200) not found");

            // SourceMembersMask must have bits 2 and 3 set (0xC).
            Assert.Equal((ushort)0xC, contacts[idxB].SourceMembersMask);
            // ThreatScore = max(0.3f from member[2], 0.5f from member[3]).
            Assert.Equal(0.5f, contacts[idxB].ThreatScore, precision: 5);
        }

        // ── SC-P2-04-4: ZeroAlloc_Over100Ticks ───────────────────────────────────

        [Fact]
        public void ZeroAlloc_Over100Ticks()
        {
            var (commander, _, sensorChild, fake) = CreatePhase2World();
            var dangerSystem = new DangerAreaRefreshSystem(fake);

            // Pre-warm to trigger any one-time JIT initialization.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                _repo, commander, currentTick: 1, mergeIntervalTicks: 1);
            dangerSystem.Run(_repo, sensorChild, 0f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 10; t < 110; t++)
            {
                Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                    _repo, commander, (uint)t, mergeIntervalTicks: 1);
                dangerSystem.Run(_repo, sensorChild, t * 0.016f);  // 60 Hz sim time
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            // Allow up to 64 bytes for any one-time lazy initialization that may occur
            // on the first iteration despite the pre-warm pass.
            Assert.True(after - before <= 64,
                $"Allocated {after - before} bytes over 100 ticks (expected 0, tolerance 64).");
        }
    }
}
