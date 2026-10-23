using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1249</c> — the brain journal's ring: what a write keeps, in what order, and what it costs.
    /// 📄 docs/DESIGN_Entity_Brain_View.md §6 BV-9, BV-15, BV-16, BV-27.
    /// </summary>
    public sealed class BrainJournalTests
    {
        private static (EntityRepository World, Entity Unit) Unit()
        {
            var world = TestWorldFactory.Create();
            if (!world.IsComponentTypeRegistered<BrainJournal>()) world.RegisterComponent<BrainJournal>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new BrainJournal());
            return (world, unit);
        }

        /// <summary>⭐ Every write counts in <see cref="BrainJournal.Seq"/>; past 24 the oldest go, and the ones kept read oldest first.
        /// 🔴 Red-proof: drop the <c>% Capacity</c> in <see cref="BrainJournal.At"/> ⇒ red.</summary>
        [Fact]
        public void TheRing_KeepsTheLast24_OldestFirst_AndSeqCountsThemAll()
        {
            var (w, u) = Unit();
            for (int i = 1; i <= 30; i++) BrainJournalOf.Write(w, u, BrainJournalKind.TaskStarted, a: i);

            var j = w.GetComponent<BrainJournal>(u);
            Assert.Equal(30u, j.Seq);
            Assert.Equal(BrainJournal.Capacity, j.Count);
            Assert.Equal(7, j.At(0).A);                     // 1…6 washed out
            Assert.Equal(30, j.At(BrainJournal.Capacity - 1).A);
            Assert.Equal(30u, j.At(BrainJournal.Capacity - 1).Seq);
            Assert.Equal(BrainJournalKind.TaskStarted, j.LastKind);
        }

        /// <summary>⭐ A refusal identical to the newest entry refreshes its time instead of appending — a sense retrying every tick does
        /// not wash the ring out. A different refusal still appends. 🔴 Red-proof: remove the coalescing branch ⇒ red.</summary>
        [Fact]
        public void ARepeatedRefusal_IsOneEntry()
        {
            var (w, u) = Unit();
            for (int i = 0; i < 10; i++)
                BrainJournalOf.Write(w, u, BrainJournalKind.TaskRefused, a: 5, cause: (byte)BrainRefusal.LowerUrgency);
            BrainJournalOf.Write(w, u, BrainJournalKind.TaskRefused, a: 5, cause: (byte)BrainRefusal.StayOnTask);

            var j = w.GetComponent<BrainJournal>(u);
            Assert.Equal(2, j.Count);
            Assert.Equal(2u, j.Seq);
        }

        /// <summary>⭐ BV-15 — a write allocates nothing (unmanaged, by ref). 🔴 Red-proof: box the entry (<c>object o = e;</c>) ⇒ red.</summary>
        [Fact]
        public void AWrite_AllocatesNothing()
        {
            var (w, u) = Unit();
            BrainJournalOf.Write(w, u, BrainJournalKind.MoveStarted, a: 1, point: Vector3.One);   // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                BrainJournalOf.Write(w, u, BrainJournalKind.MoveStarted, a: i, point: new Vector3(i, 0, 0));
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ BV-16 — switched off, the journal is never attached and a write is a no-op; a unit without it never throws.</summary>
        [Fact]
        public void SwitchedOff_NothingIsAttached_AndWritesAreNoOps()
        {
            var w = TestWorldFactory.Create();
            if (!w.IsComponentTypeRegistered<BrainJournal>()) w.RegisterComponent<BrainJournal>();
            var u = w.CreateEntity();
            try
            {
                BrainJournalSettings.Enabled = false;
                BrainJournalOf.Attach(w, u);
                Assert.False(w.HasComponent<BrainJournal>(u));
                BrainJournalOf.Write(w, u, BrainJournalKind.TaskStarted, a: 1);   // no throw
            }
            finally { BrainJournalSettings.Enabled = true; }

            BrainJournalOf.Attach(w, u);
            Assert.True(w.HasComponent<BrainJournal>(u));
        }

        /// <summary>⭐ The entry stays 32 bytes (BV-15: ~0.8 KB per unit).</summary>
        /// <summary>⭐ <c>CE-1250</c> — a move's source is noted for ITS command instance only: the next command reads Unknown.
        /// 🔴 Red-proof: drop the instance check in <c>MoveSourceOf</c> ⇒ red.</summary>
        [Fact]
        public void AMoveSource_BelongsToTheCommandItWasNotedFor()
        {
            var (w, u) = Unit();
            BrainJournalOf.NoteMoveSource(w, u, 7, MoveSource.CoverSearch);
            Assert.Equal(MoveSource.CoverSearch, BrainJournalOf.MoveSourceOf(w, u, 7));
            Assert.Equal(MoveSource.SourceUnknown, BrainJournalOf.MoveSourceOf(w, u, 8));
        }

        [Fact]
        public unsafe void AnEntry_Is32Bytes() => Assert.Equal(32, sizeof(BrainJournalEntry));
    }
}
