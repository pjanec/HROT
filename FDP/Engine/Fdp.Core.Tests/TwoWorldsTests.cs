using System;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ POC 2 P-4 / P-5 (CE-4439; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §14.1) — the two-world copies of
    /// <c>EntityRepository.TwoWorlds.cs</c>: World S cloned from World A (every table, the index, the clocks, every singleton including a MANAGED one,
    /// the event registrations), the per-tick S→A sync through a TABLE mask that keeps A's own component bits, and the ROW copy of chosen entities.
    /// </summary>
    public class TwoWorldsTests
    {
        [ComponentId(484)] public struct TwPos { public float X; }
        [ComponentId(485)] public struct TwSimOnly { public int V; }
        [ComponentId(492)] public struct TwUiOnly { public int V; }
        [ComponentId(493)] public struct TwClock { public int T; }
        [ComponentId(496)] public sealed class TwService { public int V; }
        [EventId(9972)] public struct TwEvent { public int V; }

        private static EntityRepository NewA()
        {
            var a = new EntityRepository();
            a.RegisterComponent<TwPos>(); a.RegisterComponent<TwSimOnly>(); a.RegisterComponent<TwUiOnly>();
            a.RegisterEvent<TwEvent>();
            return a;
        }

        private static BitMask512 MaskOf(params int[] ids) { var m = new BitMask512(); foreach (var i in ids) m.SetBit(i); return m; }

        [Fact]
        public void The_clone_carries_tables_entities_every_singleton_and_the_event_registrations()
        {
            using var a = NewA();
            var e = a.CreateEntity();
            a.AddComponent(e, new TwPos { X = 3 });
            a.SetSingletonUnmanaged(new TwClock { T = 7 });
            var svc = new TwService { V = 1 };
            a.SetSingletonManaged(svc);   // 🔴 a managed singleton's table has only an OPTIONAL-parameter constructor (the first live run died here)

            using var s = a.CloneForSimulation();
            Assert.True(s.IsAlive(e));
            Assert.Equal(3, s.GetComponentRO<TwPos>(e).X);
            Assert.Equal(7, s.GetSingletonUnmanagedRO<TwClock>().T);
            Assert.Same(svc, s.GetSingletonManaged<TwService>());
            Assert.True(s.Bus.IsRegistered<TwEvent>());

            s.SetSingletonUnmanaged(new TwClock { T = 8 });   // S owns its singleton tables: A's value stays
            Assert.Equal(7, a.GetSingletonUnmanagedRO<TwClock>().T);
        }

        [Fact]
        public void The_sync_takes_the_masked_tables_and_keeps_As_own_bits_on_the_same_entities()
        {
            using var a = NewA();
            var e1 = a.CreateEntity(); var e2 = a.CreateEntity();
            a.AddComponent(e1, new TwPos { X = 1 }); a.AddComponent(e2, new TwPos { X = 2 });
            using var s = a.CloneForSimulation();

            a.AddComponent(e1, new TwUiOnly { V = 42 });          // UI state only A has
            a.AddComponent(e2, new TwUiOnly { V = 43 });
            s.Tick();                                             // the kernel advances S's version every tick; the sync copies what changed since
            s.GetComponentRW<TwPos>(e1).X = 10;                   // the simulation moves e1 …
            s.AddComponent(e1, new TwSimOnly { V = 5 });          // … gives it a type outside the mask …
            s.DestroyEntity(e2);                                  // … and destroys e2

            a.SyncPresentationFrom(s, MaskOf(484), default);

            Assert.Equal(10, a.GetComponentRO<TwPos>(e1).X);
            Assert.True(a.HasComponent<TwUiOnly>(e1));
            Assert.Equal(42, a.GetComponentRO<TwUiOnly>(e1).V);
            Assert.False(a.HasComponent<TwSimOnly>(e1));          // outside the mask: not copied
            Assert.False(a.IsAlive(e2));                          // the entity index comes from S whole
        }

        [Fact]
        public void The_row_copy_brings_every_component_of_the_chosen_entity_only()
        {
            using var a = NewA();
            var e1 = a.CreateEntity(); var e2 = a.CreateEntity();
            a.AddComponent(e1, new TwPos()); a.AddComponent(e2, new TwPos());
            using var s = a.CloneForSimulation();
            s.Tick();
            s.AddComponent(e1, new TwSimOnly { V = 11 });
            s.AddComponent(e2, new TwSimOnly { V = 22 });
            a.SyncPresentationFrom(s, MaskOf(484), default);

            a.SyncRowsFrom(s, new[] { e1 });

            Assert.True(a.HasComponent<TwSimOnly>(e1));
            Assert.Equal(11, a.GetComponentRO<TwSimOnly>(e1).V);
            Assert.False(a.HasComponent<TwSimOnly>(e2));
        }

        [Fact]
        public void Singletons_in_the_except_mask_are_left_to_A()
        {
            using var a = NewA();
            a.SetSingletonUnmanaged(new TwClock { T = 1 });
            using var s = a.CloneForSimulation();
            s.Tick();
            s.SetSingletonUnmanaged(new TwClock { T = 2 });

            a.SyncPresentationFrom(s, default, MaskOf(493));
            Assert.Equal(1, a.GetSingletonUnmanagedRO<TwClock>().T);
            a.SyncPresentationFrom(s, default, default);
            Assert.Equal(2, a.GetSingletonUnmanagedRO<TwClock>().T);
        }
    }
}
