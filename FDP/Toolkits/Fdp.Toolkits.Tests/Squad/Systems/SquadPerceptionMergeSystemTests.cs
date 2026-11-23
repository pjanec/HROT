using System;
using System.Linq;
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

        /// <summary>⭐⭐ <c>CE-4666</c> (D-8) — a sound heard only THROUGH AN OPENING is remembered as such (<c>IsHeardThroughAnOpening</c>) and pooled
        /// with <c>HeardThroughFlag</c>; one STRAIGHT hearing fused into it — in the man's memory or in the pool — makes it a plain heard contact.
        /// ✅ Red-proofs: <c>HearContact</c> ignoring <c>via</c> ⇒ the memory bit never set ⇒ red; the pool not clearing the flag on a straight
        /// fusion ⇒ red.</summary>
        [Fact]
        public void CE4666_AShotHeardThroughAnOpening_IsMarkedSo_UntilHeardStraight()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            const byte smallArms = 4;
            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem0, 100f, 0f, 0f, 20f, smallArms, 50f, 1, via: 1);
            Assert.True(TargetMemory.IsAnonymous(in mem0, 0));
            Assert.True(TargetMemory.IsHeardThroughAnOpening(in mem0, 0));

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);
            Assert.Equal(1, GetState(commander).Contacts.Count);
            Assert.True((GetContact(commander, 0).Flags & SquadContact.HeardThroughFlag) != 0, "pooled as heard through an opening");

            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref mem1, 105f, 3f, 0f, 20f, smallArms, 60f, 2);   // the other man hears it straight
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 20, mergeIntervalTicks: 1);
            Assert.Equal(1, GetState(commander).Contacts.Count);
            Assert.True((GetContact(commander, 0).Flags & SquadContact.HeardThroughFlag) == 0, "one straight hearing makes it a source estimate");

            mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem0, 102f, 1f, 0f, 20f, smallArms, 50f, 3);   // and in his own memory too
            Assert.False(TargetMemory.IsHeardThroughAnOpening(in mem0, 0));
            Assert.True(TargetMemory.IsAnonymous(in mem0, 0));
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

        /// <summary>
        /// ⭐⭐ <c>CE-2348</c> ③ (R-2071 E-1, Sensing §8.6b) — A HEARD BURST IS NO ENEMY: a round landing 4 m from a SEEN enemy does not
        /// refresh him (his last-seen tick stays), it is its own burst slot; a second burst there fuses with it (one slot), a rifle shot
        /// there does not; the burst counts no enemy danger; and the squad pool holds only the seen enemy. 📐 Live <c>uc-urban-battle</c>:
        /// the enemy's RPG bursts landing among the convoy kept it "in contact" after the ambush. 🔴 Red-proof: drop the
        /// <c>IsBurst</c> skip in the merge and the pool holds two contacts; drop the HearContact rule and the enemy is refreshed.
        /// </summary>
        [Fact]
        public void CE2348_AHeardBurst_IsNoEnemy_NotRefreshingOne_NotCountedNotPooled()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            const byte burst = TargetMemory.BurstClass, smallArms = 4;
            var enemy = repo.CreateEntity();
            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, (long)enemy.PackedValue, 100f, 0f, 50f, tick: 1);
            TargetMemory.HearContact(ref mem0, 104f, 0f, 0f, 20f, burst, 60f, 5);

            Assert.Equal(2, mem0.Count);
            int seen = mem0.EntityIds[0] == (long)enemy.PackedValue ? 0 : 1, heard = 1 - seen;
            Assert.Equal(1u, mem0.LastSeenTick[seen]);
            Assert.True(TargetMemory.IsBurst(in mem0, heard));
            Assert.Equal(0f, Fdp.Toolkit.Perception.ThreatDanger.OfSlot(repo, members[0], in mem0, heard));

            TargetMemory.HearContact(ref mem0, 106f, 2f, 0f, 20f, burst, 60f, 6);       // a second burst there: the same slot
            Assert.Equal(2, mem0.Count);
            TargetMemory.HearContact(ref mem0, 105f, 1f, 0f, 20f, smallArms, 60f, 7);   // a rifle shot there: never fused into the burst
            int bursts = 0;
            for (int i = 0; i < mem0.Count; i++) if (TargetMemory.IsBurst(in mem0, i)) bursts++;
            Assert.Equal(1, bursts);

            ref var mem1 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref mem1, 300f, 0f, 0f, 20f, burst, 60f, 5);      // only a burst
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            for (int i = 0; i < GetState(commander).Contacts.Count; i++)
                Assert.NotEqual(300f, GetContact(commander, i).PositionX);               // no burst in the pool
            Assert.Contains(Enumerable.Range(0, GetState(commander).Contacts.Count),
                i => GetContact(commander, i).EntityId == (long)enemy.PackedValue);
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

        /// <summary>⭐⭐ <c>CE-2474</c> (Sensing §8.6d ①; 🔒 user: "Can't we consider the same sound heard by multiple people as one single enemy?") —
        /// 📐 the measured <c>uf-corner-assault</c> case: ONE shot heard by four men, every estimate off by more than its circle and one heard round a
        /// corner beside the listener himself (D-8), was FOUR pooled contacts. With the shot's token they are ONE, placed where the straight hearings
        /// put it (the corner one does not drag it); a DIFFERENT shot there stays its own contact.
        /// 🔴 Red-proof: drop the same-sound fusion in <c>MergeHeard</c> ⇒ 4 (then 5) contacts.</summary>
        [Fact]
        public void CE2474_OneShotHeardByFourMen_IsOnePooledContact_WhateverTheirEstimates()
        {
            var (repo, commander, members) = CreateSquadWorld(5);
            const byte smallArms = 4;
            const long shot = 7, another = 8;
            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[0]), 631f, 379f, 0f, 5f, smallArms, 50f, 1, sound: shot);
            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[1]), 642f, 379f, 0f, 5f, smallArms, 50f, 1, sound: shot);
            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[2]), 659f, 334f, 0f, 9.4f, smallArms, 60f, 1, via: 1, sound: shot);
            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[3]), 642f, 378f, 0f, 5f, smallArms, 50f, 1, sound: shot);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            Assert.Equal(1, GetState(commander).Contacts.Count);
            ref var c = ref GetContact(commander, 0);
            Assert.Equal((ushort)0b1111, c.SourceMembersMask);
            Assert.True((c.Flags & SquadContact.AnonymousFlag) != 0);
            Assert.True((c.Flags & SquadContact.HeardThroughFlag) == 0, "a straight hearing makes it a source estimate");
            Assert.InRange(c.PositionY, 375f, 381f);   // ⛔ not dragged toward the corner (334)

            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[4]), 700f, 300f, 0f, 5f, smallArms, 40f, 2, sound: another);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 20, mergeIntervalTicks: 1);
            Assert.Equal(2, GetState(commander).Contacts.Count);
        }

        /// <summary>⭐⭐ <c>CE-2474</c> (§8.6d ① + ②) — one man SEES the rifleman and his ear gives him the shot (<c>AttributeToSeen</c>: the bearing
        /// matches); another man hears the same shot round a corner, 50 m off. The pool holds ONE contact, the identified rifleman, at the seen
        /// position — not the sighting plus a heard phantom the circle rule cannot reach.
        /// 🔴 Red-proof: drop <c>AbsorbHeardBySound</c> ⇒ 2 contacts.</summary>
        [Fact]
        public unsafe void CE2474_AShotAttributedToTheManSeen_TakesEveryOtherHearingOfIt()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            const byte smallArms = 4;
            const long shot = 11;
            var enemy = repo.CreateEntity();
            ref var mem0 = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, (long)enemy.PackedValue, 628f, 387f, 50f, tick: 1);
            System.Span<long> seen = stackalloc long[] { (long)enemy.PackedValue };
            Assert.True(TargetMemory.AttributeToSeen(ref mem0, 650f, 330f, 632f, 377f, TargetMemory.ShotKind, smallArms, 0, seen, 10f, 2, shot));
            Assert.Equal(1, mem0.Count);
            Assert.Equal(shot, mem0.SoundIds[0]);

            TargetMemory.HearContact(ref repo.GetComponentRW<TargetMemory>(members[1]), 659f, 334f, 0f, 9.4f, smallArms, 60f, 2, via: 1, sound: shot);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            Assert.Equal(1, GetState(commander).Contacts.Count);
            ref var c = ref GetContact(commander, 0);
            Assert.Equal((long)enemy.PackedValue, c.EntityId);
            Assert.Equal(628f, c.PositionX);
            Assert.Equal((ushort)0b0011, c.SourceMembersMask);
        }

        /// <summary>⭐⭐ <c>CE-2477</c> (Sensing §8.6e) — 📐 <c>pe-seen-shooter</c>: a man who HEARS one shooter 150 times must not end up surer than a lone
        /// ear can be — the fused circle stops at a third of one hearing's (26 m ⇒ ≥ 8.7 m), so when he then SEES the shooter 2.8 m from the heard point
        /// the sighting absorbs it: ONE contact, not the seen man plus a heard ghost. 🔴 Red-proof: <c>FusedRadius</c> without the floor ⇒ the circle
        /// shrinks to ~2 m ⇒ 2 contacts.</summary>
        [Fact]
        public unsafe void CE2477_ManyHearings_NeverSharperThanALoneEar_SoASightingStillAbsorbsThem()
        {
            var (repo, commander, members) = CreateSquadWorld(1);
            const byte smallArms = 4;
            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);
            for (int k = 0; k < 150; k++)
            {
                float a = k * 2.399f, r = 26f * ((k * 7919) % 100) / 100f;   // spread within the 26 m circle around (100, 250)
                TargetMemory.HearContact(ref mem, 99f + r * System.MathF.Cos(a) * 0.1f, 252f + r * System.MathF.Sin(a) * 0.1f, 0f, 26f, smallArms, 1f, (uint)k + 1);
            }
            Assert.Equal(1, mem.Count);
            Assert.True(mem.Radius[0] >= 26f / 3f - 1e-3f, $"fused radius {mem.Radius[0]}");

            var enemy = repo.CreateEntity();
            Assert.True(TargetMemory.AbsorbBySighting(ref mem, (long)enemy.PackedValue, 100.86f, 249.87f, System.ReadOnlySpan<byte>.Empty));
            Assert.Equal(1, mem.Count);
            Assert.False(TargetMemory.IsAnonymous(in mem, 0));
        }

        /// <summary>⭐⭐ <c>CE-2485</c> (📐 uf-corner-assault) — steps heard ROUND A CORNER (a D-8 point at the corner, 45 m ahead), then the man SEEN behind it
        /// (54 m, 4° off): one contact, the seen man — not the seen man plus the corner point. A man seen NEARER than the corner, or 90° off it, does not take
        /// it. 🔴 Red-proof: without the listener (the old gate: inside the circle only) ⇒ the corner point stays ⇒ red.</summary>
        [Fact]
        public void CE2485_StepsHeardRoundACorner_ThenTheManSeenBehindIt_IsOneContact()
        {
            var (repo, commander, members) = CreateSquadWorld(1);
            const byte steps = 1;
            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem, 642f, 379f, 0f, 2f, steps, 50f, 1, via: 1);
            var near = repo.CreateEntity(); var aside = repo.CreateEntity(); var behind = repo.CreateEntity();
            Assert.False(TargetMemory.AbsorbBySighting(ref mem, (long)near.PackedValue, 646f, 360f, System.ReadOnlySpan<byte>.Empty, true, 652f, 336f));     // nearer than the corner
            Assert.False(TargetMemory.AbsorbBySighting(ref mem, (long)aside.PackedValue, 700f, 336f, System.ReadOnlySpan<byte>.Empty, true, 652f, 336f));    // 90° off
            Assert.True(TargetMemory.AbsorbBySighting(ref mem, (long)behind.PackedValue, 636.5f, 387f, System.ReadOnlySpan<byte>.Empty, true, 652f, 336f));  // beyond, 4° off
            Assert.Equal(1, mem.Count);
            Assert.Equal((long)behind.PackedValue, mem.EntityIds[0]);
        }
    
        /// <summary>⭐⭐ <c>CE-2486</c> (📐 pe-house-shot) — a man he SAW at (103.7, 105.2) and now only HEARS, 2.7 m from there with a 1.1 m circle, is that man (he
        /// moved a few steps): the known contact is refreshed, no heard duplicate. A sound 8 m from him is another. 🔴 Red-proof: no slack ⇒ 2 contacts ⇒ red.</summary>
        [Fact]
        public void CE2486_HeardAFewStepsFromWhereHeWasLastSeen_IsHim()
        {
            var (repo, commander, members) = CreateSquadWorld(1);
            var enemy = repo.CreateEntity();
            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem, (long)enemy.PackedValue, 103.66f, 105.17f, 50f, tick: 1);
            Assert.Equal((long)enemy.PackedValue, TargetMemory.HearContact(ref mem, 105.9f, 106.8f, 0f, 1.1f, 4, 10f, 2));
            Assert.Equal(1, mem.Count);
            // ⭐ CE-2488 — heard outside the circle of the old spot: he is now believed where he was heard
            Assert.Equal(105.9f, mem.PositionsX[0], 3);
            Assert.Equal(106.8f, mem.PositionsY[0], 3);
            // ⭐ CE-2488 — a point round an opening inside his own circle refreshes him but does not move him (it is the way in, D-8)
            Assert.Equal((long)enemy.PackedValue, TargetMemory.HearContact(ref mem, 106.4f, 106.8f, 0f, 0.6f, 4, 10f, 3, via: 1));
            Assert.Equal(105.9f, mem.PositionsX[0], 3);
            // ⭐ CE-2489 (📐 ia-house-hunt) — a point round an opening 3 m off (the stairwell to him) is NOT folded into him: it is the route, its own contact
            Assert.True(TargetMemory.HearContact(ref mem, 108.9f, 106.8f, 0f, 0.9f, 4, 10f, 3, via: 1) < 0, "the way in stays");
            Assert.Equal(2, mem.Count);
            Assert.Equal(105.9f, mem.PositionsX[mem.EntityIds[0] == (long)enemy.PackedValue ? 0 : 1], 3);
            Assert.True(TargetMemory.HearContact(ref mem, 111.0f, 108.0f, 0f, 1.1f, 4, 10f, 3) < 0, "5 m off: another");
            Assert.Equal(3, mem.Count);
        }

        /// <summary>⭐⭐ <c>CE-2492</c> (📐 pe-wall-and-door) — a man only HEARD who walks between shots: heard at (304.1, 304.0), then 1.6 m off a second later,
        /// each 0.7 m — one contact (he could have walked 1.5 m/s); so are two bangs of ONE solve (the same step); 3.2 m apart in one solve is another, and a point heard
        /// round an opening gets no walking slack (CE-2489). 🔴 Red-proof: no walking slack ⇒ 2 slots after a second ⇒ red.</summary>
        [Fact]
        public void CE2492_AManHeardWalkingBetweenShots_IsOneContact()
        {
            var (repo, commander, members) = CreateSquadWorld(1);
            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem, 304.1f, 304.0f, 0f, 0.7f, 4, 30f, 10);
            TargetMemory.HearContact(ref mem, 305.1f, 302.7f, 0f, 0.7f, 4, 30f, 70);   // a second later at 60 Hz
            Assert.Equal(1, mem.Count);

            mem = default;
            TargetMemory.HearContact(ref mem, 304.1f, 304.0f, 0f, 0.7f, 4, 30f, 10);
            TargetMemory.HearContact(ref mem, 305.1f, 302.7f, 0f, 0.7f, 4, 30f, 10);   // the SAME step: two bangs of one solve (📐 P2b, bangs 86 and 88)
            Assert.Equal(1, mem.Count);

            mem = default;
            TargetMemory.HearContact(ref mem, 304.1f, 304.0f, 0f, 0.7f, 4, 30f, 10);
            TargetMemory.HearContact(ref mem, 306.6f, 302.0f, 0f, 0.7f, 4, 30f, 10);   // 3.2 m apart in one solve: nobody walks that in half a second
            Assert.Equal(2, mem.Count);

            mem = default;
            TargetMemory.HearContact(ref mem, 304.1f, 304.0f, 0f, 0.7f, 4, 30f, 10, via: 1);
            TargetMemory.HearContact(ref mem, 305.1f, 302.7f, 0f, 0.7f, 4, 30f, 70, via: 1);
            Assert.Equal(2, mem.Count);
        }

        /// <summary>⭐⭐ <c>CE-2487</c> (📐 pe-house-shot) — CE-2486 the other way round. ① A man HEARD at (106.2, 107.0), sure to 0.7 m, then SEEN 0.9 m from
        /// there is that man: one slot. A man seen 8 m off is another. ② The squad pool: one member sees him at (105.4, 107.0), another heard him round an
        /// opening at (107.9, 108.9), 3.1 m off a 0.7 m circle — one pooled contact. 🔴 Red-proof: no slack ⇒ 2 slots / 2 pooled contacts ⇒ red.</summary>
        [Fact]
        public void CE2487_HeardFirst_ThenSeenAFewStepsFromThePoint_IsHim()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            ref var mem = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.HearContact(ref mem, 106.2f, 107.0f, 0f, 0.7f, 4, 50f, 1);
            var far = repo.CreateEntity(); var him = repo.CreateEntity();
            Assert.False(TargetMemory.AbsorbBySighting(ref mem, (long)far.PackedValue, 114.2f, 107.0f, System.ReadOnlySpan<byte>.Empty), "8 m off: another");
            Assert.True(TargetMemory.AbsorbBySighting(ref mem, (long)him.PackedValue, 105.4f, 107.0f, System.ReadOnlySpan<byte>.Empty));
            Assert.Equal(1, mem.Count);
            Assert.Equal((long)him.PackedValue, mem.EntityIds[0]);

            ref var seer = ref repo.GetComponentRW<TargetMemory>(members[0]);
            seer = default;
            TargetMemory.AddOrUpdateTarget(ref seer, (long)him.PackedValue, 105.4f, 107.0f, 50f, tick: 1);
            ref var ear = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref ear, 107.9f, 108.9f, 0f, 0.7f, 4, 30f, 1, via: 1);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);
            Assert.Equal(1, GetState(commander).Contacts.Count);
            Assert.Equal((long)him.PackedValue, GetContact(commander, 0).EntityId);
        }

        /// <summary>⭐⭐ <c>CE-2491</c> (R-2144, Sensing §8.6g; 📐 pe-house-shot) — one member SEES the shooter (his memory carries an older bang's token);
        /// another hears this bang ONLY round a door 16 m away (far beyond any slack): the squadmate saw its flash, so the pool holds ONE contact, the
        /// seen man. A bang by a man nobody sees, heard there too, stays its own heard contact. 🔴 Red-proof: no ring lookup ⇒ 3 pooled contacts.</summary>
        [Fact]
        public void CE2491_ABangHeardRoundADoor_ThatASquadmateSawFired_IsTheSeenMan()
        {
            var (repo, commander, members) = CreateSquadWorld(2);
            var him = repo.CreateEntity(); var unseen = repo.CreateEntity();
            var ring = Fdp.Toolkit.Perception.Signatures.SoundEmissionRing.Of(repo);
            long older = ring.Append(new Fdp.Toolkit.Perception.Signatures.SoundEmission { Source = him.PackedValue, Kind = 2, Class = 4, Time = (SimInstant)0 });
            long his   = ring.Append(new Fdp.Toolkit.Perception.Signatures.SoundEmission { Source = him.PackedValue, Kind = 2, Class = 4, Time = (SimInstant)0 });
            long theirs = ring.Append(new Fdp.Toolkit.Perception.Signatures.SoundEmission { Source = unseen.PackedValue, Kind = 2, Class = 4, Time = (SimInstant)0 });

            ref var seer = ref repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref seer, (long)him.PackedValue, 104.1f, 107.5f, 50f, tick: 10);
            seer.SoundIds[0] = older;
            ref var ear = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref ear, 120f, 108f, 0f, 0.7f, 4, 30f, 10, via: 1, sound: his);
            TargetMemory.HearContact(ref ear, 120f, 95f, 0f, 0.7f, 4, 30f, 10, via: 1, sound: theirs);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 10, mergeIntervalTicks: 1);
            Assert.Equal(2, GetState(commander).Contacts.Count);
            int identified = 0;
            for (int i = 0; i < 2; i++) if (GetContact(commander, i).EntityId == (long)him.PackedValue) identified++;
            Assert.Equal(1, identified);

            // ⭐ 2 s later (no new sighting): the pool is rebuilt, and the bang he was seen firing is STILL his (time-invariant). A fresh bang of his,
            //   fired 2 s after anyone last saw him, is NOT bound — nobody saw that flash.
            repo.SetSingleton(new GlobalTime { TotalTime = 2d, DeltaTime = 1f / 60f });
            long late = ring.Append(new Fdp.Toolkit.Perception.Signatures.SoundEmission { Source = him.PackedValue, Kind = 2, Class = 4, Time = (SimInstant)2 });
            ref var ear2 = ref repo.GetComponentRW<TargetMemory>(members[1]);
            TargetMemory.HearContact(ref ear2, 120f, 125f, 0f, 0.7f, 4, 30f, 130, via: 1, sound: late);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(repo, commander, currentTick: 130, mergeIntervalTicks: 1);
            Assert.Equal(3, GetState(commander).Contacts.Count);
            identified = 0;
            for (int i = 0; i < 3; i++) if (GetContact(commander, i).EntityId == (long)him.PackedValue) identified++;
            Assert.Equal(1, identified);
        }
    }
}
