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
            public readonly BusResolutionSwitch Switch = new() { ChildType = (_, _, _) => Rifleman };
            public readonly ResolutionManagerSystem Manager;
            public readonly List<Guid> Created = new();
            public double T;

            public Host(ResolutionPin pin = ResolutionPin.NoPin, float enemyAt = 3000f)
            {
                W.RegisterComponent<SimTransform>(); W.RegisterComponent<EntityInfo>(); W.RegisterComponent<AggregateUnit>();
                W.RegisterComponent<UnitSlotTable>(); W.RegisterComponent<AggregateChild>(); W.RegisterComponent<EntityGuid>();
                W.RegisterComponent<Health>(); W.RegisterComponent<GlobalTime>();
                Manager = new ResolutionManagerSystem(Switch);
                Unit = W.CreateEntity();
                W.AddComponent(Unit, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                W.AddComponent(Unit, new EntityInfo { ForceId = ForceId.Friend });
                W.AddComponent(Unit, new EntityGuid(new Guid("8e5a1d3c-0000-4000-8000-000000008012")));
                W.AddComponent(Unit, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 60f, Pin = pin });
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
                W.SetSingleton(new GlobalTime { TotalTime = T });
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
            for (int s = 0; s < 59; s++) h.Step();
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
            for (int s = 0; s < 59; s++) h.Step();
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
    }
}
