using System;
using System.Collections.Generic;
using Fdp.Core;
using Xunit;
using A = Fdp.Tests.RecorderDeltaLogicTests.Position;
using B = Fdp.Tests.RecorderDeltaLogicTests.Velocity;
using C = Fdp.Tests.DeterministicSimulationTests.Position;
using D = Fdp.Tests.DeterministicSimulationTests.Velocity;
using E = Fdp.Tests.DeterministicSimulationTests.Acceleration;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2418</c> — inside <see cref="EntityRepository.QueryIndexScope"/> a query walks only the entities of its rarest included type
    /// (a candidate index built from the masks in one pass). Every answer must be the plain walk's — the same entities in the same order:
    /// random worlds, every include / exclude mix, a component added or an entity destroyed / created INSIDE the scope, an enumerator nested
    /// in another, a mask written out of band (as ghost promotion and spawning do) in a later frame, a wholesale rewrite within one. 🔴 Red-proofs: the
    /// frame dropped from the freshness key ⇒ the out-of-band entity is missed; half the candidates dropped ⇒ the answers differ.
    /// </summary>
    public class QueryCandidateIndexTests
    {
        private static EntityRepository World(int seed, out Random rng)
        {
            var w = new EntityRepository();
            w.RegisterComponent<A>(); w.RegisterComponent<B>(); w.RegisterComponent<C>(); w.RegisterComponent<D>(); w.RegisterComponent<E>();
            rng = new Random(seed);
            for (int i = 0; i < 3000; i++)
            {
                var e = w.CreateEntity();
                if (rng.Next(2) == 0) w.AddComponent(e, new A { X = i });
                if (rng.Next(4) == 0) w.AddComponent(e, new B { VX = i });
                if (rng.Next(10) == 0) w.AddComponent(e, new C { X = i });
                if (rng.Next(50) == 0) w.AddComponent(e, new D { VX = i });
                if (rng.Next(200) == 0) w.AddComponent(e, new E { AX = i });
            }
            for (int i = 0; i < 300; i++)
            {
                int idx = rng.Next(1, 3000);
                var e = new Entity(idx, w.GetEntityIndex().GetMetadata(idx).Generation);
                if (w.IsAlive(e)) w.DestroyEntity(e);
            }
            return w;
        }

        private static IEnumerable<EntityQuery> Queries(EntityRepository w)
        {
            yield return w.Query().With<A>().Build();
            yield return w.Query().With<E>().Build();
            yield return w.Query().With<A>().With<B>().Build();
            yield return w.Query().With<B>().With<D>().Without<C>().Build();
            yield return w.Query().With<C>().Without<A>().Build();
            yield return w.Query().With<A>().With<B>().With<C>().With<D>().Build();
            yield return w.Query().Without<A>().Build();   // nothing included: walks everything
        }

        private static List<int> Walk(EntityQuery q) { var l = new List<int>(); foreach (var e in q) l.Add(e.Index); return l; }

        private static void SameInsideAndOut(EntityRepository w, string when)
        {
            var plain = new List<List<int>>();
            foreach (var q in Queries(w)) plain.Add(Walk(q));
            using (w.QueryIndexScope())
            {
                int k = 0;
                foreach (var q in Queries(w)) { var got = Walk(q); Assert.True(System.Linq.Enumerable.SequenceEqual(plain[k++], got), $"{when}: query {k - 1}: {plain[k - 1].Count} vs {got.Count}"); }
            }
        }

        [Fact]
        public void CE2418_InsideTheScope_EveryQueryAnswersAsThePlainWalk()
        {
            using var w = World(2418, out var rng);
            SameInsideAndOut(w, "a fresh world");

            using (w.QueryIndexScope())
            {
                var before = Walk(w.Query().With<E>().Build());
                var late = w.CreateEntity();                       // a structural change INSIDE the scope
                w.AddComponent(late, new E { AX = 1 });
                var after = Walk(w.Query().With<E>().Build());
                Assert.Equal(before.Count + 1, after.Count);
                Assert.Contains(late.Index, after);

                var outer = new List<int>();                       // an enumerator nested in another
                foreach (var a in w.Query().With<D>().Build())
                {
                    outer.Add(a.Index);
                    _ = Walk(w.Query().With<C>().Build());
                }
                Assert.Equal(Walk(w.Query().With<D>().Build()), outer);
            }

            // a mask written OUT OF BAND between scopes (ghost promotion / playback set bits directly)
            //   on an EXISTING entity, with no structural change at all — the next FRAME's build catches it (those writers run outside the
            //   Export phase, before the frame's first egress scope)
            var withA = Walk(w.Query().With<A>().Without<E>().Build());
            int sneaky = withA[withA.Count / 2];
            using (w.QueryIndexScope()) Assert.DoesNotContain(sneaky, Walk(w.Query().With<E>().Build()));
            w.Tick();
            w.GetEntityIndex().GetComponentMaskUnsafe(sneaky).SetBit(ComponentType<E>.ID);   // no stamp, no counter moves
            using (w.QueryIndexScope()) Assert.Contains(sneaky, Walk(w.Query().With<E>().Build()));
            // a wholesale rewrite (a restore) within the SAME frame that ADDS a bit is caught by the mask epoch
            int other = withA[withA.Count / 3];
            using (w.QueryIndexScope()) Assert.DoesNotContain(other, Walk(w.Query().With<E>().Build()));
            w.GetEntityIndex().GetComponentMaskUnsafe(other).SetBit(ComponentType<E>.ID);
            w.GetEntityIndex().RebuildMetadata();
            using (w.QueryIndexScope()) Assert.Contains(other, Walk(w.Query().With<E>().Build()));
            w.GetEntityIndex().GetComponentMaskUnsafe(other).ClearBit(ComponentType<E>.ID);
            w.GetEntityIndex().GetComponentMaskUnsafe(sneaky).ClearBit(ComponentType<E>.ID);

            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 200; i++)
                {
                    var e = w.CreateEntity();
                    w.AddComponent(e, new B { VX = i });
                    if (rng.Next(3) == 0) w.AddComponent(e, new D { VX = i });
                }
                w.Tick();
                SameInsideAndOut(w, $"round {round}");
            }
        }

        [Fact]
        public void CE2418_ASteadyScopeAllocatesNothing()
        {
            using var w = World(7, out _);
            var q = w.Query().With<B>().With<C>().Build();
            using (w.QueryIndexScope()) _ = Walk(q);   // grow once
            for (int i = 0; i < 3; i++)                 // and run the measured shape once, so first-use JIT work is outside the window
                using (w.QueryIndexScope())
                    foreach (var e in q) { }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                using (w.QueryIndexScope())
                    foreach (var e in q) { }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
