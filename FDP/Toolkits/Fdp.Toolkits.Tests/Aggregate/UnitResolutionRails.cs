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
                        Func<EntityRepository, Entity, AggregateResolution, string?>? blocked = null,
                        Func<EntityRepository, Entity, IReadOnlyList<SplitChild>?>? composition = null)
            {
                // ⭐ X2-B — each composite slot's designation: slot 1 the leader, the rest wingmen (what StochasticUnitFill gives a squad)
                Switch = new BusResolutionSwitch
                {
                    ChildType = (_, _, _) => Rifleman,
                    ChildDesignation = (_, _, slot) => slot == 1 ? Fdp.Core.CommandHierarchy.TacticalDesignation.SquadLeader : Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman,
                    Composition = composition,
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
                            case AggregateUnit u: W.AddComponent(c, u); break;      // ⭐ CE-8307 — a UNIT child (a platoon's squad)
                            case UnitSlotTable t: W.AddComponent(c, t); break;
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
        /// <summary>⭐⭐ <c>X-2</c> X2-B (R-8354; DESIGN_X2_Order_Reresolution.md §8 S1) — a squad's handover publishes ONE <c>CmdAssignSubordinate</c> per released
        /// man (the dead one never created, so never assigned), under the unit, with his composite leaf's designation. ⭐ CE-8307: whatever the echelon
        /// (the roster takes the split's DIRECT children — a platoon's are its squads, <see cref="OL1_APlatoonSplits_IntoItsSquads_EachAStochasticUnit_WithItsLatestShare"/>).
        /// ✅ Red-proofs: the publish dropped ⇒ 0 ≠ 5 · the designation taken from the slot index, not the composite slot ⇒ the leader is not slot 1's man.</summary>
        [Fact]
        public void X2B_ASquadsHandover_AssignsEveryReleasedMan_ToTheUnit_UnderHisLeafsDesignation()
        {
            foreach (byte echelon in new byte[] { 0, 1 })
            {
                using var h = new Host(enemyAt: 500f, echelon: echelon);
                h.Step(); h.Step();                                              // Planning, Materialising
                Assert.Empty(h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());   // nothing while held
                h.Step();                                                        // the handover
                Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
                var assigns = h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
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

        /// <summary>⭐ <c>CE-8307</c> (R-8389; supersedes X-2's R-8372 rail) — the roster takes the split's DIRECT children only while they FIT it: a unit
        /// whose split makes 20 men (no composite to group them) assigns nobody. ✅ Red-proof: <c>RostersItsChildren</c> always true ⇒ 20 assigns ⇒ red.</summary>
        [Fact]
        public void X2I_AUnitWhoseDirectChildrenExceedARoster_AssignsNobody()
        {
            using var h = new Host(enemyAt: 500f);
            var big = new UnitSlotTable { Count = 20 };
            for (int i = 0; i < 20; i++) big.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, CompositeSlot = (byte)(i + 1) };
            h.W.SetComponent(h.Unit, big);
            Assert.False(UnitSplits.RostersItsChildren(20));
            Assert.True(UnitSplits.RostersItsChildren(3));
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            Assert.Empty(h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray());
        }

        // ── ⭐⭐ CE-8307 — split ONE level (DESIGN_Split_One_Level.md; R-8382 … R-8390) ────────────────────────────────────────────────────────

        private static readonly TkbTypeId Squad = new(7002);
        private static readonly Guid AlphaGuid = new("8e5a1d3c-0000-4000-8000-000000008012");

        /// <summary>"Alpha" — a platoon of 3 squads × 4 men (leaves 1–4, 5–8, 9–12); each squad's own composite is its 4 men. A child (it has an
        /// <see cref="AggregateChild"/>) answers with the men, the platoon with the squads — what <c>StochasticUnitFill.Composition</c> gives.</summary>
        private static IReadOnlyList<SplitChild>? PlatoonOf3x4(EntityRepository w, Entity unit)
        {
            var list = new List<SplitChild>();
            if (w.HasComponent<AggregateChild>(unit))
                for (int k = 1; k <= 4; k++) list.Add(new SplitChild(k, Rifleman, k == 1 ? Fdp.Core.CommandHierarchy.TacticalDesignation.SquadLeader : Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, k, 1, false, 0));
            else
                for (int k = 1; k <= 3; k++) list.Add(new SplitChild(k, Squad, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, 1 + (k - 1) * 4, 4, true, 0));
            return list;
        }

        private static Host PlatoonHost(float enemyAt = 500f)
        {
            var h = new Host(enemyAt: enemyAt, echelon: 1, composition: PlatoonOf3x4);
            var t = new UnitSlotTable { Count = 12 };
            for (int i = 0; i < 12; i++)
                t.Slots[i] = new UnitSlot { Role = i % 4 == 0 ? SlotRole.Leader : SlotRole.Rifleman, Health = (Half)25f, Stress = (Half)(0.05f * i), Ammo = (ushort)(100 + i), CompositeSlot = (byte)(i + 1) };
            t.Slots[5].Health = (Half)0f;      // squad 2's second man is dead: never created
            t.Slots[9].Health = (Half)12.5f;   // squad 3's second man is wounded
            h.W.SetComponent(h.Unit, t);
            return h;
        }

        private static Entity ChildNamed(Host h, string name)
        {
            foreach (var c in h.W.Query().With<AggregateChild>().IncludeAll().Build())
                if ((string)h.W.GetComponentRO<EntityInfo>(c).Name == name) return c;
            return Entity.Null;
        }

        /// <summary>⭐⭐ CE-8307 (R-8382 / R-8383 / R-8385) — a PLATOON's split creates its 3 SQUADS, not its 12 men: each a held STOCHASTIC unit keyed
        /// <c>child:&lt;s&gt;</c>, named <c>Alpha/&lt;s&gt;</c>, carrying its share of the table (its 4 slots, renumbered 1–4, health / stress / ammunition
        /// equal to the parent's); the handover writes the LATEST share (a loss while held counts, D-P4) and rosters the 3 squads. ✅ Red-proofs: the
        /// share not renumbered ⇒ squad 2's slots read 5–8 ⇒ red · the handover not rewriting the share ⇒ squad 1's 3rd man still 25 ⇒ red.</summary>
        [Fact]
        public void OL1_APlatoonSplits_IntoItsSquads_EachAStochasticUnit_WithItsLatestShare()
        {
            using var h = PlatoonHost();
            h.Step(); h.Step();                                                   // Planning, Materialising
            Assert.Equal(SplitStage.Materialising, h.U.Splitting);
            var kids = h.Children();
            Assert.Equal(3, kids.Count);                                          // the squads — never 11 men
            var parent = h.W.GetComponentRO<UnitSlotTable>(h.Unit);
            for (int s = 1; s <= 3; s++)
            {
                var c = ChildNamed(h, "Alpha/" + s);
                Assert.False(c.IsNull);
                Assert.Equal(UnitSplits.ChildGuid(AlphaGuid, s), h.W.GetComponentRO<EntityGuid>(c).Value);
                var tag = h.W.GetComponentRO<AggregateChild>(c);
                Assert.Equal(1, tag.Held); Assert.Equal(s, tag.Slot);
                var u = h.W.GetComponentRO<AggregateUnit>(c);
                Assert.Equal(AggregateResolution.Stochastic, u.Resolution);
                var share = h.W.GetComponentRO<UnitSlotTable>(c);
                Assert.Equal(4, share.Count);
                for (int j = 0; j < 4; j++)
                {
                    var p = parent.Slots[(s - 1) * 4 + j];
                    Assert.Equal(j + 1, share.Slots[j].CompositeSlot);            // its OWN composite's leaf
                    Assert.Equal((float)p.Health, (float)share.Slots[j].Health);
                    Assert.Equal((float)p.Stress, (float)share.Slots[j].Stress);
                    Assert.Equal(p.Ammo, share.Slots[j].Ammo);
                    Assert.Equal(p.Role, share.Slots[j].Role);
                }
            }
            h.W.GetComponentRW<UnitSlotTable>(h.Unit).Slots[2].Health = (Half)5f;   // the stochastic fight goes on while they are held
            h.Step();                                                             // the handover
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            Assert.Equal(5f, (float)h.W.GetComponentRO<UnitSlotTable>(ChildNamed(h, "Alpha/1")).Slots[2].Health);   // D-P4: the LATEST share
            foreach (var c in h.Children()) Assert.Equal(0, h.W.GetComponentRO<AggregateChild>(c).Held);
            var assigns = h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
            Assert.Equal(3, assigns.Length);                                      // the platoon's roster = its squads
            foreach (var a in assigns) { Assert.Equal(h.Unit, a.Commander); Assert.True(h.W.HasComponent<AggregateUnit>(a.Subordinate)); }
        }

        /// <summary>⭐⭐ CE-8307 (R-8384 / R-8385; D-P7 "the SAME cascade") — the squads are units the manager visits like any other: with the enemy
        /// still inside the band each splits on the next steps into ITS men, named <c>Alpha/&lt;s&gt;/&lt;n&gt;</c> (the dead man of squad 2 never
        /// created), each man's state from his squad's table. ✅ Red-proof: a unit child born Physical ⇒ it never splits ⇒ no men ⇒ red.</summary>
        [Fact]
        public void OL2_TheSquads_ThenSplitIntoTheirMen_ByTheSameManager()
        {
            using var h = PlatoonHost();
            h.Step(); h.Step(); h.Step();                                         // the platoon: Planning, Materialising, handover
            SquadsToMen(h);                                                       // each squad: (awaits the order, R-8397) Planning, Materialising, handover
            for (int s = 1; s <= 3; s++)
                Assert.Equal(AggregateResolution.Physical, h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/" + s)).Resolution);
            int men = 0;
            foreach (var c in h.Children()) if (!h.W.HasComponent<AggregateUnit>(c)) men++;
            Assert.Equal(11, men);                                                // 12 less the dead one
            Assert.True(ChildNamed(h, "Alpha/2/2").IsNull);                       // the dead man
            var wounded = ChildNamed(h, "Alpha/3/2");
            Assert.False(wounded.IsNull);
            Assert.Equal(ChildNamed(h, "Alpha/3"), h.W.GetComponentRO<AggregateChild>(wounded).Unit);
            Assert.Equal(50f, h.W.GetComponentRO<Health>(wounded).Current);       // X-4 through two levels: 12.5 of 25 ⇒ 50 of 100
            Assert.Equal(UnitSplits.ChildGuid(UnitSplits.ChildGuid(AlphaGuid, 3), 2), h.W.GetComponentRO<EntityGuid>(wounded).Value);
        }

        /// <summary>⭐⭐ CE-8307 (R-8383 / R-8390; D-P7 "sum, then remove") — the merge goes LEAVES FIRST: the enemy gone and the quiet time over, the
        /// squads merge their men, and only then the platoon its squads — every slot's health, stress and ammunition back bit for bit, the dead
        /// one down, nothing left over. ✅ Red-proof: the parent's leaves-first wait dropped ⇒ the platoon merges over split squads ⇒ their men
        /// are orphaned ⇒ red.</summary>
        [Fact]
        public void OL3_TheMerge_GoesLeavesFirst_AndConservesEverySlot()
        {
            using var h = PlatoonHost();
            var before = h.W.GetComponentRO<UnitSlotTable>(h.Unit);
            h.Step(); h.Step(); h.Step(); SquadsToMen(h);                         // platoon → squads → men
            h.MoveEnemy(3000f);
            for (int s = 0; s < (int)ResolutionBands.Default.QuietSeconds + 4; s++) h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Empty(h.Children());                                           // no squad, no man left behind
            var after = h.W.GetComponentRO<UnitSlotTable>(h.Unit);
            Assert.Equal(12, after.Count);
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal((float)before.Slots[i].Health, (float)after.Slots[i].Health);
                Assert.Equal((float)before.Slots[i].Stress, (float)after.Slots[i].Stress);
                Assert.Equal(before.Slots[i].Ammo, after.Slots[i].Ammo);
            }
            Assert.Equal(11, after.Present());
        }

        /// <summary>⭐ CE-8307 (R-8390, SV-14) — the instructor's PIN merges the whole tree in ONE step (the squads' men, then the squads).
        /// ✅ Red-proof: the pin's cascade dropped ⇒ the squads' men are orphaned ⇒ red.</summary>
        [Fact]
        public void OL4_ThePin_MergesTheWholeTree_InOneStep()
        {
            using var h = PlatoonHost();
            h.Step(); h.Step(); h.Step(); SquadsToMen(h);
            Assert.Equal(11 + 3, h.Children().Count);
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Empty(h.Children());
            Assert.Equal(11, h.W.GetComponentRO<UnitSlotTable>(h.Unit).Present());
        }

        /// <summary>Steps until every squad of "Alpha" is Physical (each awaits its parent's order — none in this world — until the deadline, R-8397).</summary>
        private static void SquadsToMen(Host h, int max = 30)
        {
            for (int i = 0; i < max; i++)
            {
                bool all = true;
                for (int s = 1; s <= 3; s++)
                {
                    var c = ChildNamed(h, "Alpha/" + s);
                    if (c.IsNull || h.W.GetComponentRO<AggregateUnit>(c).Resolution != AggregateResolution.Physical) all = false;
                }
                if (all) return;
                h.Step();
            }
            Assert.Fail("the squads never split into their men");
        }

        private static int Men(Host h)
        {
            int n = 0;
            foreach (var c in h.Children()) if (!h.W.HasComponent<AggregateUnit>(c)) n++;
            return n;
        }

        /// <summary>⭐⭐ CE-8307 (R-8393, review F2; X-5 "before contact", C-7) — a FORCED split materialises the WHOLE subtree while held (the squads,
        /// then their men, all held) and every level hands over in ONE tick: no squad is ever a released stochastic unit in contact. ✅ Red-proof:
        /// <c>DeepMaterialise</c> returning false at once ⇒ the squads hand over stochastic ⇒ red.</summary>
        [Fact]
        public void F2_AForcedSplit_MaterialisesTheWholeSubtreeHeld_AndHandsOverEveryLevelInOneTick()
        {
            using var h = PlatoonHost(enemyAt: 250f);
            for (int i = 0; i < 6 && h.U.Resolution != AggregateResolution.Physical; i++)
            {
                h.Step();
                foreach (var c in h.Children())
                    if (h.W.HasComponent<AggregateUnit>(c))
                        Assert.False(h.W.GetComponentRO<AggregateChild>(c).Held == 0 && h.W.GetComponentRO<AggregateUnit>(c).Resolution == AggregateResolution.Stochastic,
                            "a squad released stochastic in contact");
            }
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            for (int s = 1; s <= 3; s++) Assert.Equal(AggregateResolution.Physical, h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/" + s)).Resolution);
            Assert.Equal(11, Men(h));
            foreach (var c in h.Children()) Assert.Equal(0, h.W.GetComponentRO<AggregateChild>(c).Held);   // every level released in the one tick
        }

        /// <summary>⭐ CE-8307 (R-8394, review F3) — a split squad whose every man is killed merges at once (its slots down), so the platoon's leaves-first
        /// merge is not held forever. ✅ Red-proof: the empty-unit merge dropped ⇒ the squad stays Physical ⇒ the platoon never merges ⇒ red.</summary>
        [Fact]
        public void F3_AWipedOutSquad_MergesAtOnce_AndThePlatoonStillMerges()
        {
            using var h = PlatoonHost();
            h.Step(); h.Step(); h.Step(); SquadsToMen(h);
            var two = ChildNamed(h, "Alpha/2");
            foreach (var c in h.Children()) if (!h.W.HasComponent<AggregateUnit>(c) && h.W.GetComponentRO<AggregateChild>(c).Unit == two) h.W.DestroyEntity(c);
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.W.GetComponentRO<AggregateUnit>(two).Resolution);
            Assert.Equal(0, h.W.GetComponentRO<UnitSlotTable>(two).Present());
            h.MoveEnemy(3000f);
            for (int s = 0; s < (int)ResolutionBands.Default.QuietSeconds + 4; s++) h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Equal(8, h.W.GetComponentRO<UnitSlotTable>(h.Unit).Present());   // squad 2's three are down
        }

        /// <summary>⭐ CE-8307 (R-8395, review F4; SV-14) — the instructor's PinPhysical on the platoon holds for its squads: with no enemy anywhere they
        /// split into their men too; lifting the pin lifts it below. ✅ Red-proof: <c>EffectivePin</c> returning the own pin ⇒ the squads stay stochastic ⇒ red.</summary>
        [Fact]
        public void F4_APinnedPlatoon_PinsItsSquadsToo()
        {
            using var h = PlatoonHost(enemyAt: 3000f);
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinPhysical;
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            h.Step(); h.Step(); h.Step();
            for (int s = 1; s <= 3; s++) Assert.Equal(AggregateResolution.Physical, h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/" + s)).Resolution);
            Assert.Equal(ResolutionPin.NoPin, h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/1")).Pin);   // derived, never copied
        }

        /// <summary>⭐ CE-8307 (R-8396, review F9; X-3) — the roster is assigned in KEY order (a play picks squads by roster position) and the same squads
        /// come back in the same order on a second split. ✅ Red-proof: the key sort dropped (with the children created in reverse) ⇒ 3, 2, 1 ⇒ red.</summary>
        [Fact]
        public void F9_TheRoster_IsInKeyOrder_StableAcrossSplitMergeSplit()
        {
            using var h = PlatoonHost(enemyAt: 3000f);
            var orders = new List<List<Guid>>();
            for (int round = 0; round < 2; round++)
            {
                h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinPhysical;
                Fdp.Core.CommandHierarchy.CmdAssignSubordinate[] assigns = Array.Empty<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>();
                for (int i = 0; i < 4 && assigns.Length == 0; i++) { h.Step(); assigns = h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray(); }
                Assert.Equal(3, assigns.Length);
                orders.Add(new List<Guid>(Array.ConvertAll(assigns, a => h.W.GetComponentRO<EntityGuid>(a.Subordinate).Value)));
                h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
                h.Step();
                Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            }
            for (int s = 0; s < 3; s++) Assert.Equal(UnitSplits.ChildGuid(AlphaGuid, s + 1), orders[0][s]);
            Assert.Equal(orders[0], orders[1]);
        }

        /// <summary>⭐ CE-8307 (R-8398, review F12) — a MIXED composite: an HQ man directly under the platoon (key 1) beside two squads (keys 2, 3): the man
        /// is created as a man (<c>Alpha/1</c>), the squads as units carrying their leaves, and the roster is the three in key order.
        /// ✅ Red-proof: every direct child treated as a unit ⇒ the HQ man becomes a 1-slot "unit" ⇒ red.</summary>
        [Fact]
        public void F12_AMixedComposite_MakesItsHqManAMan_AndItsSquadsUnits()
        {
            using var h = new Host(enemyAt: 500f, echelon: 1, composition: (w, u) => w.HasComponent<AggregateChild>(u) ? null : new List<SplitChild>
            {
                new(1, Rifleman, Fdp.Core.CommandHierarchy.TacticalDesignation.Commander, 1, 1, false, 0),
                new(2, Squad, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, 2, 4, true, 0),
                new(3, Squad, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, 6, 4, true, 0),
            });
            var t = new UnitSlotTable { Count = 9 };
            for (int i = 0; i < 9; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, CompositeSlot = (byte)(i + 1) };
            h.W.SetComponent(h.Unit, t);
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            var hq = ChildNamed(h, "Alpha/1");
            Assert.False(hq.IsNull);
            Assert.False(h.W.HasComponent<AggregateUnit>(hq));
            Assert.Equal(4, h.W.GetComponentRO<UnitSlotTable>(ChildNamed(h, "Alpha/2")).Count);
            Assert.Equal(4, h.W.GetComponentRO<UnitSlotTable>(ChildNamed(h, "Alpha/3")).Count);
            var assigns = h.W.Bus.Read<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>().ToArray();
            Assert.Equal(3, assigns.Length);
            Assert.Equal(hq, assigns[0].Subordinate);
            Assert.Equal(Fdp.Core.CommandHierarchy.TacticalDesignation.Commander, assigns[0].Designation);
            Assert.Equal(ChildNamed(h, "Alpha/3"), assigns[2].Subordinate);
        }

        /// <summary>⭐ CE-8307 (R-8390, review "untested") — the pin while a squad is MID-SPLIT: the squad's split is cancelled (its held men removed), then
        /// the platoon merges, in one step, nothing left over. ✅ Red-proof: BOTH the mid-split cancel and the tree removal dropped ⇒ the held men survive ⇒ red (either alone holds it: the merge removes the squad tree too — measured).</summary>
        [Fact]
        public void PinMidSplit_CancelsTheSquadsSplit_AndMergesTheWholeTree()
        {
            using var h = PlatoonHost();
            h.Step(); h.Step(); h.Step();                                         // the platoon hands over its squads
            for (int i = 0; i < 30; i++)
            {
                h.Step();
                var one = h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/1"));
                if (one.Splitting == SplitStage.Materialising) break;             // squad 1's men created, held
            }
            Assert.Equal(SplitStage.Materialising, h.W.GetComponentRO<AggregateUnit>(ChildNamed(h, "Alpha/1")).Splitting);
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Empty(h.Children());
            Assert.Equal(11, h.W.GetComponentRO<UnitSlotTable>(h.Unit).Present());
        }

        /// <summary>⭐⭐ CE-8307 (R-8387, review "untested") — a COMPANY cascades three levels under a forced split: its 2 platoons, their 4 squads, the
        /// squads' 16 men — all held until ONE handover, then every level Physical, the men named <c>Alpha/p/s/n</c>. ✅ Red-proof: <c>DeepMaterialise</c>
        /// not recursing ⇒ the squads hand over stochastic ⇒ red.</summary>
        [Fact]
        public void Company_CascadesThreeLevels_OnAForcedSplit()
        {
            static int Depth(EntityRepository w, Entity u)
            {
                int d = 0;
                while (w.HasComponent<AggregateChild>(u)) { u = w.GetComponentRO<AggregateChild>(u).Unit; d++; }
                return d;
            }
            IReadOnlyList<SplitChild>? Comp(EntityRepository w, Entity u)
            {
                var list = new List<SplitChild>();
                int d = Depth(w, u);
                if (d == 0) for (int k = 1; k <= 2; k++) list.Add(new SplitChild(k, Squad, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, 1 + (k - 1) * 8, 8, true, 1));
                else if (d == 1) for (int k = 1; k <= 2; k++) list.Add(new SplitChild(k, Squad, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, 1 + (k - 1) * 4, 4, true, 0));
                else for (int k = 1; k <= 4; k++) list.Add(new SplitChild(k, Rifleman, Fdp.Core.CommandHierarchy.TacticalDesignation.Wingman, k, 1, false, 0));
                return list;
            }
            using var h = new Host(enemyAt: 250f, echelon: 2, composition: Comp);
            var t = new UnitSlotTable { Count = 16 };
            for (int i = 0; i < 16; i++) t.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, CompositeSlot = (byte)(i + 1) };
            h.W.SetComponent(h.Unit, t);
            for (int i = 0; i < 8 && h.U.Resolution != AggregateResolution.Physical; i++)
            {
                h.Step();
                foreach (var c in h.Children())
                    if (h.W.HasComponent<AggregateUnit>(c))
                        Assert.False(h.W.GetComponentRO<AggregateChild>(c).Held == 0 && h.W.GetComponentRO<AggregateUnit>(c).Resolution == AggregateResolution.Stochastic);
            }
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            Assert.Equal(16, Men(h));
            Assert.Equal(6, h.Children().Count - 16);                            // 2 platoons + 4 squads
            foreach (var c in h.Children())
            {
                Assert.Equal(0, h.W.GetComponentRO<AggregateChild>(c).Held);
                if (h.W.HasComponent<AggregateUnit>(c)) Assert.Equal(AggregateResolution.Physical, h.W.GetComponentRO<AggregateUnit>(c).Resolution);
            }
            Assert.False(ChildNamed(h, "Alpha/2/1/4").IsNull);
        }

        /// <summary>⭐ CE-8307 (R-8399, review F8; X-4) — a merge folds the squads' unit-level state back: the latest contact, the least prepared, the most
        /// suppressed. ✅ Red-proof: the fold dropped ⇒ the platoon keeps its pre-split contact time ⇒ red.</summary>
        [Fact]
        public void F8_AMerge_FoldsTheSquadsUnitState()
        {
            using var h = PlatoonHost(enemyAt: 3000f);
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinPhysical;
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Prepared = PreparedLevel.Fortified;
            h.Step(); h.Step(); h.Step();
            Assert.Equal(AggregateResolution.Physical, h.U.Resolution);
            var two = ChildNamed(h, "Alpha/2");
            ref var su = ref h.W.GetComponentRW<AggregateUnit>(two);
            su.LastContactAt = (SimInstant)123.0;
            su.SuppressedShare = 0.6f;
            su.Prepared = PreparedLevel.Unprepared;
            h.W.GetComponentRW<AggregateUnit>(h.Unit).Pin = ResolutionPin.PinStochastic;
            h.Step();
            Assert.Equal(AggregateResolution.Stochastic, h.U.Resolution);
            Assert.Equal(123.0, (double)h.U.LastContactAt, 3);
            Assert.Equal(0.6f, h.U.SuppressedShare, 3);
            Assert.Equal(PreparedLevel.Unprepared, h.U.Prepared);
        }
    }
}

