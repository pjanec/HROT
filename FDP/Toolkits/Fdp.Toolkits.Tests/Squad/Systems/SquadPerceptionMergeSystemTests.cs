using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem"/>.
    /// Success criteria: SC-P2-01-1 through SC-P2-01-5.
    /// </summary>
    public unsafe class SquadPerceptionMergeSystemTests : IDisposable
    {
        private EntityRepository _repo;

        public SquadPerceptionMergeSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadStateMarker>();
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private (EntityRepository repo, Entity commander, Entity[] members)
            CreateSquadWorld(int memberCount)
        {
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new UnitRoster());
            _repo.AddComponent(commander, new Blackboard1024());
            _repo.AddComponent(commander, new SquadStateMarker());

            var members = new Entity[memberCount];
            for (int i = 0; i < memberCount; i++)
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
            return (_repo, commander, members);
        }

        private ref SquadCognitiveState GetState(Entity commander)
        {
            ref var bb = ref _repo.GetComponentRW<Blackboard1024>(commander);
            return ref SquadCognitiveState.Project(ref bb);
        }

        private ref SquadContact GetContact(Entity commander, int index)
        {
            ref var state = ref GetState(commander);
            return ref MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts),
                16)[index];
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

        // ── SC-P2-01-1: Three distinct contacts merge to Count==3 ────────────────

        [Fact]
        public void ThreeDistinctContacts_MergeToThree()
        {
            var (repo, commander, members) = CreateSquadWorld(3);

            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 10f, 0f, 0.5f, tick: 1);

            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.AddOrUpdateTarget(ref mem1, 200L, 20f, 0f, 0.3f, tick: 1);

            ref var mem2 = ref repo.GetComponentRW<TargetMemory>(members[2]);
            TargetMemory.AddOrUpdateTarget(ref mem2, 300L, 30f, 0f, 0.4f, tick: 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(3, state.Contacts.Count);

            int idx100 = FindContactIndex(commander, 100L);
            int idx200 = FindContactIndex(commander, 200L);
            int idx300 = FindContactIndex(commander, 300L);

            Assert.True(idx100 >= 0, "Contact 100 not found");
            Assert.True(idx200 >= 0, "Contact 200 not found");
            Assert.True(idx300 >= 0, "Contact 300 not found");

            var contacts = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);

            Assert.Equal((ushort)0b0001, contacts[idx100].SourceMembersMask);
            Assert.Equal((ushort)0b0010, contacts[idx200].SourceMembersMask);
            Assert.Equal((ushort)0b0100, contacts[idx300].SourceMembersMask);
        }

        // ── SC-P2-01-2: Two members see same contact — max threat, both bits ─────

        [Fact]
        public void TwoMembersSeeSameContact_MaxThreatAndBothBitsSet()
        {
            var (repo, commander, members) = CreateSquadWorld(3);

            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 1f, 2f, 0.7f, tick: 20);

            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.AddOrUpdateTarget(ref mem1, 100L, 4f, 5f, 0.4f, tick: 30);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(1, state.Contacts.Count);

            var contacts = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);

            Assert.Equal(0.7f, contacts[0].ThreatScore, precision: 5);
            Assert.Equal((ushort)0b0011, contacts[0].SourceMembersMask);
            Assert.Equal(30u, contacts[0].LastSeenTick);
        }

        // ── SC-P2-01-3: Cadence gate — skips within interval, runs at boundary ───

        [Fact]
        public void CadenceGate_SkipsRunWhenIntervalNotElapsed()
        {
            var (repo, commander, members) = CreateSquadWorld(2);

            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 0f, 0f, 0.5f, tick: 1);

            // First run always proceeds (LastMergeTick == 0).
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 6);
            Assert.Equal(1, GetState(commander).Contacts.Count);

            // Second run: delta = 2 < 6 and epoch unchanged — must skip.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 12, mergeIntervalTicks: 6);
            Assert.Equal(10u, GetState(commander).Contacts.LastMergeTick);

            // Third run: delta = 6 >= 6 — must run.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 16, mergeIntervalTicks: 6);
            Assert.Equal(16u, GetState(commander).Contacts.LastMergeTick);
        }

        // ── SC-P2-01-4: Event-driven forced re-merge on epoch change ────────────

        [Fact]
        public void EventDriven_ForcedRemergeOnEpochChange()
        {
            var (repo, commander, members) = CreateSquadWorld(2);

            // First call: both members have empty TargetMemory.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 100);
            Assert.Equal(0, GetState(commander).Contacts.Count);
            Assert.Equal(10u, GetState(commander).Contacts.LastMergeTick);

            // Add a contact to member 1 — bumps ChangeEpoch.
            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.AddOrUpdateTarget(ref mem1, 200L, 5f, 0f, 0.6f, tick: 10);

            // Only 1 tick elapsed, far below 100 interval — but epoch changed.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 11, mergeIntervalTicks: 100);
            Assert.Equal(1, GetState(commander).Contacts.Count);
            Assert.Equal(11u, GetState(commander).Contacts.LastMergeTick);
        }

        // ── SC-P2-01-5: Capacity eviction — 17th contact evicts lowest ──────────

        [Fact]
        public void CapacityEviction_SeventeenthContactEvictsLowest()
        {
            var (repo, commander, members) = CreateSquadWorld(1);

            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);

            // Fill with 16 distinct contacts (scores 0.1 .. 1.6).
            for (int i = 0; i < 16; i++)
                TargetMemory.AddOrUpdateTarget(ref mem, i + 1L, 0f, 0f, (i + 1) * 0.1f, tick: 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 1, mergeIntervalTicks: 1);
            Assert.Equal(16, GetState(commander).Contacts.Count);

            // Add 17th with score 0.05 — lower than all 16; should be rejected.
            TargetMemory.AddOrUpdateTarget(ref mem, 100L, 0f, 0f, 0.05f, tick: 2);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 2, mergeIntervalTicks: 1);
            Assert.Equal(16, GetState(commander).Contacts.Count);

            // Add entity 101 with score 5.0 — highest; should replace the previous lowest.
            TargetMemory.AddOrUpdateTarget(ref mem, 101L, 0f, 0f, 5.0f, tick: 3);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 3, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(16, state.Contacts.Count);

            var contacts = MemoryMarshal.CreateSpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);

            Assert.True(contacts[0].ThreatScore >= 5.0f, $"Top contact score {contacts[0].ThreatScore} < 5.0f");
        }

        // ── OFX-007: Newer lower-threat sighting must update position ────────────

        [Fact]
        public void MergeContact_NewerLowerThreat_UpdatesPosition()
        {
            var (repo, commander, members) = CreateSquadWorld(2);

            // Member 0: older sighting with higher threat score.
            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 1f, 2f, 0.9f, tick: 10, posZ: 3f);

            // Member 1: newer sighting with lower threat score.
            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.AddOrUpdateTarget(ref mem1, 100L, 5f, 6f, 0.3f, tick: 20, posZ: 7f);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(
                repo, commander, currentTick: 25, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(1, state.Contacts.Count);

            int idx = FindContactIndex(commander, 100L);
            Assert.True(idx >= 0, "Contact 100 not found");

            ref var contact = ref GetContact(commander, idx);
            Assert.Equal(0.9f, contact.ThreatScore, precision: 5);
            Assert.Equal(20u, contact.LastSeenTick);
            // Position must come from the newer tick-20 sighting (member 1), not the
            // older higher-threat tick-10 sighting (member 0).
            Assert.Equal(5f, contact.PositionX, precision: 5);
            Assert.Equal(6f, contact.PositionY, precision: 5);
            Assert.Equal(7f, contact.PositionZ, precision: 5);
        }
    }
}
