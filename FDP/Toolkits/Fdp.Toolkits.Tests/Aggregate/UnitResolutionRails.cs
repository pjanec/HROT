using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8012</c> (ST-5, §16a / §16b, SV-4 / SV-6 / SV-7 / SV-14, X-3 / X-4, C-7; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md)
    /// — the resolution manager and the production switch: a physical enemy approaching splits the unit (Planning → Materialising → Handover,
    /// children created HELD by transient requests with derived identities, the latest slot table mapped onto them); the enemy leaving and the
    /// quiet time passing merges it back (the children's state summed in, a killed one's slot down, the centroid); a second split gives the
    /// SAME identities; a withdrawn trigger cancels; the pins win; C-7 forces the handover at once.
    /// </summary>
    public sealed class UnitResolutionRails
    {
        private static readonly TkbTypeId Rifleman = new(7001);

        private sealed class Host : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity Unit, Enemy;
            public readonly BusResolutionSwitch Switch;
            public readonly ResolutionManagerSystem Manager;
            public readonly List<Guid> Created = new();
            public double T;

            public Host(ResolutionPin pin = ResolutionPin.NoPin, float enemyAt = 3000f, byte echelon = 0,
                        Func<EntityRepository, Entity, AggregateResolution, string?>? blocked = null)
            {
                // ⭐ X2-B — each composite slot's designation: slot 1 the leader, the rest wingmen (what StochasticUnitFill gives a squad)
                Switch = new BusResolutionSwitch
                {
                    ChildType = (_, _, _) => Rifleman,
                    ChildDesignation = (_, _, slot) => slot == 1 ? Fdp.Core.CommandHierarchy.TacticalDesignation.SquadLeader : Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman,
                };
                W.RegisterComponent<SimTransform>(); W.RegisterComponent<EntityInfo>(); W.RegisterComponent<AggregateUnit>();
                W.RegisterComponent<UnitSlotTable>(); W.RegisterComponent<AggregateChild>(); W.RegisterComponent<EntityGuid>();
                W.RegisterComponent<Health>(); W.RegisterComponent<GlobalTime>();
                W.RegisterComponent<PlannedPosts>(); W.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
                W.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
                Manager = new ResolutionManagerSystem(Switch) { SwitchBlocked = blocked };
                Unit = W.CreateEntity();
                W.AddComponent(Unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                W.AddComponent(Unit, new EntityInfo { ForceId = ForceId.Friend, Name = "Alpha" });
                W.AddComponent(Unit, new EntityGuid(new Guid("8e5a1d3c-0000-4000-8000-000000008012")));
                W.AddComponent(Unit, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 60f, Pin = pin, Echelon = echelon });
                var s = new UnitSlotTable { Count = 6 };
                for (int i = 0; i < 6; i++) s.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, CompositeSlot = (byte)(i + 1) };
                s.Slots[2].Health = (Half)12.5f;   // a wounded man
                s.Slots[5].Health = (Half)0f;      // a dead one: never created
                W.AddComponent(Unit, s);
                Enemy = W.CreateEntity();
                W.AddComponent(Enemy, new SimTransform { Position = new Vector3(0, enemyAt, 0), Rotation = Quaternion.Identity });
                W.AddComponent(Enemy, new EntityInfo { ForceId = ForceId.Hostile });
            }

            /// <summary>One second: the manager, then what <c>CreateEntityRequestSystem</c> would do with this frame's requests.</summary>
            public void Step()
            {
                T += 1.0;
                W.SetSingleton(new GlobalTime { TotalTime = T, FrameNumber = (long)T });   // one perception step a second (PerceptionClock)
                Manager.Execute(W, 1f);
                W.Bus.SwapBuffers();
                foreach (var r in W.Bus.ReadManaged<EntityCreationRequest>())
                {
                    Assert.True(r.IsTransient);                     // SV-4: never saved
                    var c = W.CreateEntity();
                    foreach (var o in r.InitialComponents!)
                        switch (o)
                        {
                            case EntityGuid g: W.AddComponent(c, g); Created.Add(g.Value); break;
                            case SimTransform t: W.AddComponent(c, t); break;
                            case AggregateChild a: W.AddComponent(c, a); break;
                            case EntityInfo i: W.AddComponent(c, i); break;
                        }
                    W.AddComponent(c, new Health { Current = 100f, Max = 100f });
                }
            }

            public void MoveEnemy(float y) => W.GetComponentRW<SimTransform>(Enemy).Position = new Vector3(0, y, 0);
            public AggregateUnit U => W.GetComponentRO<AggregateUnit>(Unit);
            public List<Entity> Children()
            {
                var l = new List<Entity>();
                foreach (var c in W.Query().With<AggregateChild>().Build()) l.Add(c);
                return l;
            }
            public void Dispose() => W.Dispose();
        }

        /// <summary>⭐ The split, then the merge, then the same identities on the second split. ✅ Red-proof: the handover not mapping the slot's
        /// health ⇒ the wounded man's child is at full health ⇒ red.</summary>
        [Fact]
        public void ASplitThenAMerge_KeepsEveryIdentityAndTheState()
        {
            using var h = new Host();
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);       // 3 km: nothing
            h.MoveEnemy(500f);                                                    // within band 400 + lead 200
            h.Step();                                                             // NotSplitting → Planning
            Assert.Equal(SplitStage.Planning, h.U.Splitting);
            h.Step();                                                             // Planning → Materialising: 5 requests (the dead slot is not one)
            Assert.Equal(SplitStage.Materialising, h.U.Splitting);
            Assert.Equal(5, h.Children().Count);
            foreach (var c in h.Children()) Assert.Equal(1, h.W.GetComponentRO<AggregateChild>(c).Held);
            var first = new List<Guid>(h.Created);
            h.Step();                                                             // every child present ⇒ the handover
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            Assert.Equal(SplitStage.NotSplitting, h.U.Splitting);
            foreach (var c in h.Children())
            {
                var tag = h.W.GetComponentRO<AggregateChild>(c);
                Assert.Equal(0, tag.Held);
                Assert.Equal(tag.Slot == 2 ? 50f : 100f, h.W.GetComponentRO<Health>(c).Current);   // X-4: the wounded man is wounded
            }
            Assert.Contains(UnitSplits.ChildGuid(new Guid("8e5a1d3c-0000-4000-8000-000000008012"), 3), first);   // SV-4: the derived key

            // the physical fight kills one child; the enemy leaves; the quiet time passes ⇒ the merge
            var victim = h.Children().Find(c => h.W.GetComponentRO<AggregateChild>(c).Slot == 0);
            h.W.DestroyEntity(victim);
            h.MoveEnemy(3000f);
            for (int s = 0; s < (int)ResolutionBands.Default.QuietSeconds - 1; s++) h.Step();   // the policy's quiet time, less one step
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);          // not before the quiet time
            h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Empty(h.Children());
            var slots = h.W.GetComponentRO<UnitSlotTable>(h.Unit);
            Assert.False(slots.IsUp(0));                                          // the killed man stays dead (SV-1)
            Assert.Equal(12.5f, (float)slots.Slots[2].Health, 2);                 // the wounded one keeps his wound
            Assert.Equal(4, slots.Present());

            // a second split gives the SAME children (identity continuity, X-3)
            h.Created.Clear();
            h.MoveEnemy(500f);
            h.Step(); h.Step();
            foreach (var g in h.Created) Assert.Contains(g, first);
            Assert.Equal(4, h.Created.Count);
        }

        /// <summary>⭐ CE-8013 ④ (§16b UNOBSERVED) — the enemy gone and the quiet time over, a split unit still does NOT merge while a physical
        /// sensor of another side holds one of its men (acquired) — men never vanish in front of a trainee; the moment it loses him, the quiet
        /// time runs and the unit merges. ✅ Red-proof: the observed check removed ⇒ the unit merges in front of the sensor ⇒ red.</summary>
        [Fact]
        public unsafe void AnObservedSplitUnit_DoesNotMergeAutomatically()
        {
            using var h = new Host();
            h.W.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorContactList>();
            h.W.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            h.MoveEnemy(500f);
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            var watched = h.Children()[0];
            var sensor = h.W.CreateEntity();                                       // the enemy's optics, a mount child of the enemy
            h.W.AddComponent(sensor, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = h.Enemy });
            var list = new Fdp.Toolkit.Perception.Components.SensorContactList { Count = 1 };
            list.EntityIds[0] = (long)watched.PackedValue;
            list.State[0] = (byte)Fdp.Toolkit.Perception.Components.SensorContactState.Acquired;
            h.W.AddComponent(sensor, list);
            h.MoveEnemy(3000f);                                                    // far — but its sensor still holds the man
            for (int s = 0; s < 90; s++) h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            h.W.GetComponentRW<Fdp.Toolkit.Perception.Components.SensorContactList>(sensor).State[0] = (byte)Fdp.Toolkit.Perception.Components.SensorContactState.Lost;
            for (int s = 0; s < (int)ResolutionBands.Default.QuietSeconds - 1; s++) h.Step();   // the policy's quiet time, less one step
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);          // the quiet time starts when he is lost
            h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
        }

        /// <summary>⭐ SV-7 — the trigger withdrawn while materialising ⇒ the held children removed, the unit stays stochastic; ⭐ hysteresis — a
        /// split unit with the enemy just outside the band does NOT merge.</summary>
        [Fact]
        public void AWithdrawnTrigger_Cancels_AndTheMarginHolds()
        {
            using var h = new Host();
            h.MoveEnemy(500f);
            h.Step(); h.Step();
            Assert.Equal(SplitStage.Materialising, h.U.Splitting);
            h.MoveEnemy(1000f);                                                   // beyond band + lead + margin (700)
            h.Step();
            Assert.Equal(SplitStage.NotSplitting, h.U.Splitting);
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Empty(h.Children());

            h.MoveEnemy(500f);
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            h.MoveEnemy(450f);                                                    // outside the band, inside band + margin
            for (int s = 0; s < 120; s++)
            {
                h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);       // EVERY step: no merge-and-resplit flip-flop
            }
        }

        /// <summary>
        /// ⭐ F-23 (R-4286) — HYSTERESIS: with an enemy PARKED at 550 m (inside the split trigger, band + lead = 600, but outside the old merge
        /// line, band + margin = 500) the unit splits ONCE and never merges back in 5 min (measured before: a flip every ~63 s). Beyond the trigger
        /// + margin (800) it merges only after 30 s with nobody watching and no man in CONTACT (a child that saw an enemy within 2 steps).
        /// ✅ Red-proof: the merge line back at band + margin ⇒ it merges at 550 m ⇒ red; the contact check skipped ⇒ it merges while a man fights ⇒ red.
        /// </summary>
        [Fact]
        public void F23_AnEnemyParkedBetweenTheLines_SplitsOnce_AndAMergeWaitsForQuiet()
        {
            using var h = new Host();
            h.MoveEnemy(550f);
            int splits = 0; var was = h.U.Resolution;
            for (int s = 0; s < 300; s++)
            {
                h.Step();
                if (was == AggregateResolution.Stochastic && h.U.Resolution == AggregateResolution.Physical) splits++;
                if (splits == 1) Assert.Equal(AggregateResolution.Physical, h.U.Resolution);    // once physical, it stays
                was = h.U.Resolution;
            }
            Assert.Equal(1, splits);

            h.MoveEnemy(750f);                                                    // beyond the trigger, inside trigger + margin: still no merge
            for (int s = 0; s < 60; s++) { h.Step(); Assert.Equal(AggregateResolution.Physical, h.U.Resolution); }

            h.MoveEnemy(900f);                                                    // beyond 800 — but a man is in contact for 40 s
            var man = h.Children()[0];
            h.W.AddComponent(man, new Fdp.Toolkit.Perception.Components.TargetMemory());
            for (int s = 0; s < 40; s++)
            {
                h.Step();
                ref var mem = ref h.W.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(man);
                unsafe { mem.Count = 1; mem.EntityIds[0] = 42; mem.LastSeenTick[0] = (uint)h.T; }   // he sees an enemy, every step
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            }
            for (int s = 0; s < 29; s++) { h.Step(); Assert.Equal(AggregateResolution.Physical, h.U.Resolution); }   // quiet, but not yet 30 s
            for (int s = 0; s < 3; s++) h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);         // 30 s quiet beyond the line: merged
        }

        /// <summary>⭐ F-22 (R-4286) — a split's children are NAMED <c>&lt;unit&gt;/&lt;composite slot&gt;</c> (checks, goldens and the UI name them).
        /// ✅ Red-proof: the name not set ⇒ empty ⇒ red.</summary>
        [Fact]
        public void F22_TheChildrenAreNamed_UnitSlashSlot()
        {
            using var h = new Host();
            h.MoveEnemy(500f);
            h.Step(); h.Step();
            var names = new List<string>();
            foreach (var c in h.Children()) names.Add(h.W.GetComponentRO<EntityInfo>(c).Name);
            names.Sort(StringComparer.Ordinal);
            Assert.Equal(new[] { "Alpha/1", "Alpha/2", "Alpha/3", "Alpha/4", "Alpha/5" }, names);   // slot 6 is dead: never created
        }

        /// <summary>
        /// ⭐ DP3-B (R-4286) — a unit being PLANNED (its planner added <see cref="PlannedPosts"/>) waits in Planning for Ready, then its men are
        /// created AT the planned posts (DP3-D ①; the rest at the fallback); not Ready ⇒ the deadline (10 s) and the fallback; a FORCED split never
        /// waits; a withdrawn trigger clears the plan; a unit nobody plans for goes at once (as before).
        /// ✅ Red-proof: the wait skipped ⇒ Materialising on the first step ⇒ red; the planned points not used ⇒ the men on the line ⇒ red.
        /// </summary>
        [Fact]
        public void DP3B_APlannedUnit_WaitsForThePlan_AndItsMenStandOnThePosts()
        {
            // ① Ready after 3 s: the men at the planned posts (two planned, three on the fallback)
            using (var h = new Host())
            {
                var plan = new PlannedPosts();
                h.W.AddComponent(h.Unit, plan);
                h.MoveEnemy(500f);
                for (int s = 0; s < 3; s++) { h.Step(); Assert.Equal(SplitStage.Planning, h.U.Splitting); }
                ref var p = ref h.W.GetComponentRW<PlannedPosts>(h.Unit);
                p.Set(0, new Vector3(11, 22, 3), default, PlannedPosts.PostedFlag); p.Set(1, new Vector3(44, 55, 6), default, PlannedPosts.PostedFlag); p.Count = 2; p.Ready = 1;
                h.Step(); h.Step();
                var at = new List<Vector3>();
                foreach (var c in h.Children()) at.Add((Vector3)h.W.GetComponentRO<SimTransform>(c).Position);
                Assert.Equal(5, at.Count);
                Assert.Contains(new Vector3(11, 22, 3), at);
                Assert.Contains(new Vector3(44, 55, 6), at);
            }
            // ② never Ready: the deadline, then the fallback
            using (var h = new Host())
            {
                h.W.AddComponent(h.Unit, new PlannedPosts());
                h.MoveEnemy(500f);
                for (int s = 0; s < 9; s++) { h.Step(); Assert.Equal(SplitStage.Planning, h.U.Splitting); }
                h.Step(); h.Step();
                Assert.NotEqual(SplitStage.Planning, h.U.Splitting);
                Assert.Equal(5, h.Children().Count);
            }
            // ③ forced (within 300 m): no wait at all
            using (var h = new Host())
            {
                h.W.AddComponent(h.Unit, new PlannedPosts());
                h.MoveEnemy(250f);
                h.Step();
                Assert.Equal(SplitStage.Materialising, h.U.Splitting);
            }
            // ④ withdrawn: the plan cleared
            using (var h = new Host())
            {
                h.W.AddComponent(h.Unit, new PlannedPosts());
                h.MoveEnemy(500f);
                h.Step();
                h.MoveEnemy(2000f);
                h.Step();
                Assert.Equal(SplitStage.NotSplitting, h.U.Splitting);
                Assert.False(h.W.HasComponent<PlannedPosts>(h.Unit));
            }
            // ⑤ nobody plans: at once, as before
            using (var h = new Host())
            {
                h.MoveEnemy(500f);
                h.Step(); h.Step();
                Assert.Equal(SplitStage.Materialising, h.U.Splitting);
            }
        }

        /// <summary>⭐ SV-14 — the instructor's pin wins: PinStochastic never splits (even with the enemy close); PinPhysical splits at 3 km.
        /// ⭐ C-7 — an enemy within the forced range skips the planning: Materialising on the first step.</summary>
        [Fact]
        public void ThePinsWin_AndAForcedHandoverSkipsThePlanning()
        {
            using (var h = new Host(ResolutionPin.PinStochastic, enemyAt: 100f))
            {
                for (int s = 0; s < 5; s++) h.Step();
                Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
                Assert.Empty(h.Children());
            }
            using (var h = new Host(ResolutionPin.PinPhysical))
            {
                h.Step(); h.Step(); h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            }
            using (var h = new Host(enemyAt: 200f))
            {
                h.Step();
                Assert.Equal(SplitStage.Materialising, h.U.Splitting);           // straight past Planning
                h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            }
        }

        [Fact]
        public void TheSwitch_RefusesToSplitWithoutTheTkbResolver()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<UnitSlotTable>();
            var u = w.CreateEntity();
            w.AddComponent(u, new UnitSlotTable());
            Assert.Throws<InvalidOperationException>(() => new BusResolutionSwitch().Materialise(w, u, ReadOnlySpan<Vector3>.Empty));
        }
        /// <summary>⭐⭐ <c>X-2</c> X2-B (R-8354; DESIGN_X2_Order_Reresolution.md §8 S1) — a SQUAD's handover publishes ONE <c>CmdAssignSubordinate</c> per released
        /// man (the dead one never created, so never assigned), under the unit, with his composite leaf's designation; a PLATOON's publishes none
        /// (F-X2-1: its men are not its squads). ✅ Red-proofs: the publish dropped ⇒ 0 ≠ 5 · the designation taken from the slot index, not the
        /// composite slot ⇒ the leader is not slot 1's man.</summary>
        [Fact]
        public void X2B_ASquadsHandover_AssignsEveryReleasedMan_ToTheUnit_UnderHisLeafsDesignation_APlatoonsNone()
        {
            foreach (byte echelon in new byte[] { 0, 1 })
            {
                using var h = new Host(enemyAt: 500f, echelon: echelon);
                h.Step(); h.Step();                                              // Planning, Materialising
                Assert.Empty(h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());   // nothing while held
                h.Step();                                                        // the handover
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
                var assigns = h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
                if (echelon != 0) { Assert.Empty(assigns); continue; }
                Assert.Equal(5, assigns.Length);                                 // the five live men (slot 5 is dead)
                var men = h.Children();
                foreach (var a in assigns)
                {
                    Assert.Equal(h.Unit, a.Commander);
                    Assert.Contains(a.Subordinate, men);
                    int slot = h.W.GetComponentRO<AggregateChild>(a.Subordinate).Slot;   // slot index 0 ⇔ composite slot 1
                    Assert.Equal(slot == 0 ? Fdp.Core.CommandHierarchy.TacticalDesignation.SquadLeader : Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, a.Designation);
                }
                Assert.Equal(5, new HashSet<Entity>(Array.ConvertAll(assigns, a => a.Subordinate)).Count);   // one each
            }
        }

        /// <summary>⭐⭐ <c>X-2</c> X2-B (F-X2-4) — a MERGE leaves no <c>UnitRoster</c> on the unit (the men are gone; a kept roster, even empty, would
        /// drop the unit out of the staff composer's stochastic arm). ✅ Red-proof: the removal dropped ⇒ the roster survives ⇒ red.</summary>
        [Fact]
        public void X2B_AMerge_LeavesNoRoster()
        {
            using var h = new Host(ResolutionPin.PinPhysical);
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();         // what UnitHierarchySystem builds from the assigns
            foreach (var c in h.Children()) Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, c);
            h.W.AddComponent(h.Unit, roster);
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.False(h.W.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(h.Unit));
        }

        /// <summary>⭐⭐ <c>X-2</c> X2-D (R-8357, SV-10 / SV-14) — while <see cref="ResolutionManagerSystem.SwitchBlocked"/> holds a reason, an AUTOMATIC
        /// split and an automatic merge WAIT; the reason gone, they go; the instructor's PIN goes regardless. ✅ Red-proofs: the split's check
        /// dropped ⇒ it plans while blocked ⇒ red · the merge's check dropped ⇒ it merges while blocked ⇒ red · the check applied to the pin ⇒ the
        /// pinned split waits ⇒ red.</summary>
        [Fact]
        public void X2D_ABlockedAutomaticSwitch_Waits_APinnedOneGoes()
        {
            string? why = "no physical play for Recover";
            using (var h = new Host(enemyAt: 500f, blocked: (_, _, _) => why))
            {
                for (int i = 0; i < 3; i++) h.Step();
                Assert.Equal(SplitStage.NotSplitting, h.U.Splitting);          // the automatic split waits
                Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
                why = null;
                h.Step();
                Assert.Equal(SplitStage.Planning, h.U.Splitting);              // the reason gone: it goes
                h.Step(); h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);

                why = "no stochastic play for Embark";                           // now the merge
                h.MoveEnemy(3000f);
                for (int s = 0; s < (int)ResolutionBands.Default.QuietSeconds + 3; s++) h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);    // the automatic merge waits
                why = null;
                h.Step();
                Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            }
            why = "blocked";
            using (var h = new Host(ResolutionPin.PinPhysical, blocked: (_, _, _) => why))
            {
                h.Step(); h.Step(); h.Step();
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);    // the pin goes (SV-14)
                h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
                h.Step();
                Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);  // and back, pinned
            }
        }
    }
}
