using System.Collections.Generic;
using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ CE-4445 M-2 — the runtime detector of an in-place mutation of a managed component (<see cref="ManagedMutationCheck"/>,
    /// <c>HROT_MANAGED_MUTATION_CHECK=1</c>). The switch is read once per process, so the rails drive its core directly: a read is recorded
    /// (<see cref="ManagedMutationCheck.OnRead"/>), the frame ends (<c>EndFrameCore</c>).
    /// 🔴 Red-proof: <c>EndFrameCore</c> skipping the fingerprint comparison ⇒ the first rail fails.
    /// </summary>
    [Collection("ManagedMutationCheck")]   // a process-wide recorder: the rails do not run in parallel with each other
    public class ManagedMutationCheckTests
    {
        [ComponentId(495)] public sealed class MmcBag { public List<int> Items = new(); public int Count; }

        private static (EntityRepository Repo, Entity E, IComponentTable Table) World()
        {
            var w = new EntityRepository();
            w.RegisterManagedComponent<MmcBag>();
            var e = w.CreateEntity();
            w.SetManagedComponent(e, new MmcBag());
            w.Tick();
            Assert.True(w.TryGetTable(typeof(MmcBag), out var t));
            return (w, e, t);
        }

        [Fact]
        public void A_read_object_mutated_in_place_without_a_stamp_is_a_hit()
        {
            ManagedMutationCheck.Reset();
            var (w, e, t) = World();
            using var _ = w;
            var bag = w.GetComponentRO<MmcBag>(e);
            ManagedMutationCheck.OnRead(t, e.Index, bag);
            bag.Items.Add(7);                      // ⛔ the in-place mutation
            ManagedMutationCheck.EndFrameCore();
            Assert.Equal(1, ManagedMutationCheck.DistinctHits);
        }

        [Fact]
        public void Replacing_the_component_whole_is_not_a_hit()
        {
            ManagedMutationCheck.Reset();
            var (w, e, t) = World();
            using var _ = w;
            var bag = w.GetComponentRO<MmcBag>(e);
            ManagedMutationCheck.OnRead(t, e.Index, bag);
            var next = new MmcBag { Count = 1 };   // ✅ a new instance …
            next.Items.AddRange(bag.Items);
            next.Items.Add(7);
            w.Tick();
            w.SetManagedComponent(e, next);        // … set whole
            ManagedMutationCheck.EndFrameCore();
            Assert.Equal(0, ManagedMutationCheck.DistinctHits);
        }

        public struct MmcLink { public int Id; public string? Name; }
        public struct MmcPoint { public float X; public string? Note; public MmcLink Link; }
        [ComponentId(494)] public sealed record MmcRoute { public IReadOnlyList<MmcPoint> Points { get; init; } = new List<MmcPoint>(); }

        /// <summary>📌 the rig's first false positive: a struct nested past the depth limit was hashed by the identity of a NEW box each read.</summary>
        [Fact]
        public void Nested_structs_past_the_depth_limit_are_not_a_hit()
        {
            ManagedMutationCheck.Reset();
            var w = new EntityRepository();
            using var _ = w;
            w.RegisterManagedComponent<MmcRoute>();
            var e = w.CreateEntity();
            w.SetManagedComponent(e, new MmcRoute { Points = new List<MmcPoint> { new() { X = 1, Note = "a", Link = new MmcLink { Id = 3, Name = "n" } } } });
            w.Tick();
            Assert.True(w.TryGetTable(typeof(MmcRoute), out var t));
            ManagedMutationCheck.OnRead(t, e.Index, w.GetComponentRO<MmcRoute>(e));
            ManagedMutationCheck.EndFrameCore();
            Assert.Equal(0, ManagedMutationCheck.DistinctHits);
        }

        [Fact]
        public void An_unchanged_read_and_a_stamped_write_are_not_hits()
        {
            ManagedMutationCheck.Reset();
            var (w, e, t) = World();
            using var _ = w;
            var bag = w.GetComponentRO<MmcBag>(e);
            ManagedMutationCheck.OnRead(t, e.Index, bag);
            ManagedMutationCheck.EndFrameCore();                       // read, nothing changed
            Assert.Equal(0, ManagedMutationCheck.DistinctHits);

            var b2 = w.GetComponentRO<MmcBag>(e);
            ManagedMutationCheck.OnRead(t, e.Index, b2);
            w.Tick();
            w.GetComponentRW<MmcBag>(e).Count = 5;                      // the RW path stamps the chunk: recorded
            ManagedMutationCheck.EndFrameCore();
            Assert.Equal(0, ManagedMutationCheck.DistinctHits);
        }
    }
}
