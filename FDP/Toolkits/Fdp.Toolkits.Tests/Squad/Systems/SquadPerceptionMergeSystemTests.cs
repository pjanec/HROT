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
            _repo.RegisterComponent<SquadCognitiveState>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadCognitiveState>();
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
            _repo.AddComponent(commander, default(SquadCognitiveState));
            _repo.AddComponent(commander, default(SquadCognitiveState));

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
                UnitRoster.Add(ref roster, m);
                members[i] = m;
            }
            return (_repo, commander, members);
        }

        private ref SquadCognitiveState GetState(Entity commander)
        {
            return ref _repo.GetComponentRW<SquadCognitiveState>(commander);
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

        /// <summary>⭐ <c>CE-3063</c> ② — two members HEARING one shot are ONE squad contact: their circles meet, so the pool fuses
        /// them by position (the radius shrinks, both members' bits set), flagged heard; a shot heard far away stays a second contact.</summary>
        [Fact]
        public void CE3063_TwoMembersHearingOneShot_AreOnePooledContact()
        {
            var (repo, commander, members) = CreateSquadWorld(3);
            const byte smallArms = 4;
            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem0, 100f, 0f, 0f, 20f, smallArms, 50f, 1);
            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref mem1, 110f, 5f, 0f, 20f, smallArms, 60f, 1);
            ref var mem2 = ref repo.GetComponentRW<TargetMemory>(members[2]);
            TargetMemory.HearContact(ref mem2, -400f, 0f, 0f, 20f, smallArms, 40f, 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            ref var state = ref GetState(commander);
            Assert.Equal(2, state.Contacts.Count);
            ref var fused = ref GetContact(commander, 0);   // sorted by score: the fused one (60) first
            Assert.True((fused.Flags & SquadContact.AnonymousFlag) != 0, "a heard contact stays flagged heard in the pool");
            Assert.Equal((ushort)0b0011, fused.SourceMembersMask);
            Assert.Equal(60f, fused.ThreatScore);
            Assert.InRange(fused.PositionX, 100f, 110f);   // between the two estimates
            Assert.True((GetContact(commander, 1).Flags & SquadContact.AnonymousFlag) != 0);
        }

        /// <summary>⭐ CE-2107 — one member SEES the enemy, another HEARS his shot inside the sound's circle: the pool holds ONE
        /// contact, the sighting (its position, both members' bits, the higher score), not the sighting plus an anonymous point
        /// that <c>SquadEnemyStrengthRatio</c> would count as a second armed enemy.</summary>
        [Fact]
        public void CE2107_AShotOneMemberHears_AndAnotherSees_IsOnePooledContact()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            const byte smallArms = 4;
            repo.RegisterComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();
            var enemy = repo.CreateEntity();
            repo.AddComponent(enemy, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter { FiringClass = smallArms });

            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, (long)enemy.PackedValue, 100f, 0f, 50f, tick: 1);
            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref mem1, 105f, 3f, 0f, 20f, smallArms, 60f, 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            Assert.Equal(1, GetState(commander).Contacts.Count);
            ref var c = ref GetContact(commander, 0);
            Assert.Equal((long)enemy.PackedValue, c.EntityId);
            Assert.True((c.Flags & SquadContact.AnonymousFlag) == 0, "the sighting absorbs the sound — the contact is identified");
            Assert.Equal((ushort)0b0011, c.SourceMembersMask);
            Assert.Equal(60f, c.ThreatScore);
            Assert.Equal(100f, c.PositionX);   // the sighting's position, not the sound's estimate
        }

        /// <summary>⭐ CE-2107 — the guard: a sound the seen entity cannot make (a rifle shot by a tracked engine), or a sound whose
        /// circle does not hold the sighting, stays its own heard contact.</summary>
        [Fact]
        public void CE2107_AHeardContactStays_WhenTheSightingCannotMakeIt_OrLiesOutsideTheCircle()
        {
            var (repo, commander, members) = CreateSquadWorld(3);
            const byte smallArms = 4, trackedEngine = 3;
            repo.RegisterComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>();
            var tank = repo.CreateEntity();
            repo.AddComponent(tank, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter { MovingClass = trackedEngine });
            var rifleman = repo.CreateEntity();
            repo.AddComponent(rifleman, new Fdp.Toolkit.Perception.Signatures.AcousticEmitter { FiringClass = smallArms });

            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, (long)tank.PackedValue, 100f, 0f, 50f, tick: 1);
            TargetMemory.AddOrUpdateTarget(ref mem0, (long)rifleman.PackedValue, 300f, 0f, 40f, tick: 1);
            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref mem1, 102f, 0f, 0f, 20f, smallArms, 60f, 1);   // over the tank: wrong class
            ref var mem2 = ref repo.GetComponentRW<TargetMemory>(members[2]);
            TargetMemory.HearContact(ref mem2, 340f, 0f, 0f, 20f, smallArms, 30f, 1);   // the rifleman's class, 40 m off: outside 20

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            Assert.Equal(4, GetState(commander).Contacts.Count);
            int heard = 0;
            for (int i = 0; i < 4; i++) if ((GetContact(commander, i).Flags & SquadContact.AnonymousFlag) != 0) heard++;
            Assert.Equal(2, heard);
        }
    }
}
