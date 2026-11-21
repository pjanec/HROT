using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-2468 UF-2 (docs/designs/minefields/DESIGN_Minefields.md §9b, §9b.1 K2, K4, K6, K7) — the <c>UnderFire</c> place-knowledge kind: one record per (place, side),
    /// merge NEWEST with the point frozen at the first learn, a refresh with the same evidence time changes nothing, and NOTHING LASTS FOREVER — Indirect cools at 120 s and
    /// is retired at 240 s, Direct at 600 s / 1 200 s, by sim time.
    /// </summary>
    public sealed class UnderFireKnowledgeTests
    {
        private const int Blue = 1, Red = 2;

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly UnderFireWriter Writer = new();
            public double Now;
            public Rig()
            {
                W.RegisterComponent<UnderFireKnowledge>(); W.RegisterComponent<SimTransform>(); W.RegisterComponent<NetworkAuthority>();
                W.RegisterEvent<UnderFireLearned>();
                W.SetSingleton(new GlobalTime());
            }
            /// <summary><paramref name="ticks"/> writer ticks at <see cref="Now"/>; each tick's creation requests become this node's entities.</summary>
            public void Tick(int ticks = 1)
            {
                for (int i = 0; i < ticks; i++)
                {
                    W.SetSingleton(new GlobalTime { TotalTime = Now });
                    W.Bus.SwapBuffers();
                    Writer.Execute(W, 0.016f);
                    W.Bus.SwapBuffers();
                    foreach (var r in W.Bus.ReadManaged<EntityCreationRequest>().ToList())
                    {
                        var e = W.CreateEntity();
                        foreach (var c in r.InitialComponents!) if (c is UnderFireKnowledge k) W.AddComponent(e, k);
                    }
                }
            }
            /// <summary>Ages the world by <paramref name="seconds"/> and lets one ageing pass run.</summary>
            public void Age(double seconds) { Now += seconds; Tick(UnderFireWriter.CoolCheckTicks); }
            public List<UnderFireKnowledge> Records()
            {
                var l = new List<UnderFireKnowledge>();
                foreach (var e in W.Query().With<UnderFireKnowledge>().WithLifecycle(EntityLifecycle.AllButTearDown).Build()) l.Add(W.GetComponentRO<UnderFireKnowledge>(e));
                return l;
            }
            public void Dispose() => W.Dispose();
        }

        /// <summary>⭐ K7 + merge newest — evidence within 25 m of a record of the same side and cause updates it (newest time, range and strength; the point stays the
        /// first); farther, or another side, is another record. 🔴 Red-proof: no merge search (a new subject per learn) ⇒ three records for one side.</summary>
        [Fact]
        public void UF2_EvidenceNearARecord_UpdatesIt_NewestWins_ThePointStaysTheFirst()
        {
            using var rig = new Rig();
            UnderFireEvents.LearnDirect(rig.W, Blue, new Vector3(99, 100, 0), 150f, 0.6f, (SimInstant)10.0); rig.Tick();   // cell (3, 4) of the 25 m grid
            UnderFireEvents.LearnDirect(rig.W, Blue, new Vector3(110, 105, 0), 180f, 0.9f, (SimInstant)20.0); rig.Tick();  // 12 m away, newer, across the cell line (4, 4)
            UnderFireEvents.LearnDirect(rig.W, Blue, new Vector3(108, 100, 0), 90f, 0.1f, (SimInstant)15.0); rig.Tick();    // older than what is held: ignored

            var one = Assert.Single(rig.Records());
            Assert.Equal(new Vector3(99, 100, 0), one.Point);
            Assert.Equal((SimInstant)20.0, one.Header.LearnedAt);
            Assert.Equal(180f, one.RangeM);
            Assert.Equal(0.9f, one.EnemyStrength);
            Assert.Equal(KnowledgeSource.Contact, one.Header.Source);
            Assert.Equal((SubjectKind.Place, PlaceKnowledgeKind.UnderFire, (byte)(Blue + 1)), (one.Header.SubjectKind, one.Header.Kind, one.Header.SidePlus1));

            UnderFireEvents.LearnDirect(rig.W, Blue, new Vector3(160, 100, 0), 150f, 0.5f, (SimInstant)21.0); rig.Tick();   // 60 m away
            UnderFireEvents.LearnDirect(rig.W, Red, new Vector3(100, 100, 0), 150f, 0.5f, (SimInstant)22.0); rig.Tick();    // the other side, same place
            UnderFireEvents.LearnIndirect(rig.W, Blue, new Vector3(100, 100, 0), 25f, (SimInstant)23.0); rig.Tick();        // the other cause, same place
            Assert.Equal(4, rig.Records().Count);
        }

        /// <summary>⭐ K7 — the subject is derived from the cause and the 25 m cell of the FIRST centre: the same on every node, a different cell a different subject.</summary>
        [Fact]
        public void UF2_TheSubject_IsStable_FromTheCauseAndTheFirstCentre()
        {
            Assert.Equal(UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(101, 101, 0)), UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(124, 110, 7)));
            Assert.NotEqual(UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(101, 101, 0)), UnderFireRules.SubjectOf(UnderFireCause.Indirect, new Vector3(101, 101, 0)));
            Assert.NotEqual(UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(101, 101, 0)), UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(126, 101, 0)));
            Assert.NotEqual(UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(-10, 0, 0)), UnderFireRules.SubjectOf(UnderFireCause.Direct, new Vector3(10, 0, 0)));
        }

        /// <summary>⭐ K6 — a refresh that carries the same evidence time (no new burst) changes nothing: no write, the record unchanged.
        /// 🔴 Red-proof: <c>Holds</c> comparing with &lt; instead of &lt;= ⇒ the refresh is written again.</summary>
        [Fact]
        public void UF2_ARefreshWithTheSameEvidenceTime_ChangesNothing()
        {
            using var rig = new Rig();
            UnderFireEvents.LearnIndirect(rig.W, Blue, new Vector3(0, 0, 0), 25f, (SimInstant)5.0); rig.Tick();
            long applied = rig.Writer.Applied;
            UnderFireEvents.LearnIndirect(rig.W, Blue, new Vector3(3, 0, 0), 30f, (SimInstant)5.0); rig.Tick();
            Assert.Equal(applied, rig.Writer.Applied);
            Assert.Equal(25f, Assert.Single(rig.Records()).RangeM);
        }

        /// <summary>⭐⭐ NOTHING LASTS FOREVER (K2, K4) — by sim time: Indirect is Cooled at 120 s after its last evidence and gone at 240 s; Direct at 600 s and 1 200 s.
        /// 🔴 Red-proofs: no ageing pass ⇒ never Cooled; no retire ⇒ never gone.</summary>
        [Theory]
        [InlineData(UnderFireCause.Indirect, 120.0)]
        [InlineData(UnderFireCause.Direct, 600.0)]
        public void UF2_ARecordCools_AtItsCauseCoolTime_AndIsRetired_AtTwiceThat(UnderFireCause cause, double cool)
        {
            using var rig = new Rig();
            if (cause == UnderFireCause.Direct) UnderFireEvents.LearnDirect(rig.W, Blue, Vector3.Zero, 150f, 0.5f, (SimInstant)0.0);
            else UnderFireEvents.LearnIndirect(rig.W, Blue, Vector3.Zero, 25f, (SimInstant)0.0);
            rig.Tick();
            Assert.True(Assert.Single(rig.Records()).FreshAt(rig.Now));

            rig.Age(cool - 1);
            Assert.Equal(UnderFireKnowledge.StateActive, Assert.Single(rig.Records()).Header.State);
            rig.Age(2);
            var cooled = Assert.Single(rig.Records());
            Assert.Equal(UnderFireKnowledge.StateCooled, cooled.Header.State);
            Assert.False(cooled.FreshAt(rig.Now));

            rig.Age(cool - 2);   // 2 × cool − 1
            Assert.Single(rig.Records());
            rig.Age(2);
            Assert.Empty(rig.Records());
            Assert.Equal(1, rig.Writer.Retired);
        }

        /// <summary>⭐ new evidence wakes a Cooled record (the same place shelled again): Active, the new time, and the clock starts over.</summary>
        [Fact]
        public void UF2_NewEvidence_WakesACooledRecord()
        {
            using var rig = new Rig();
            UnderFireEvents.LearnIndirect(rig.W, Blue, Vector3.Zero, 25f, (SimInstant)0.0); rig.Tick();
            rig.Age(130);
            Assert.Equal(UnderFireKnowledge.StateCooled, Assert.Single(rig.Records()).Header.State);
            UnderFireEvents.LearnIndirect(rig.W, Blue, new Vector3(5, 0, 0), 25f, (SimInstant)rig.Now); rig.Tick();
            var woken = Assert.Single(rig.Records());
            Assert.Equal(UnderFireKnowledge.StateActive, woken.Header.State);
            Assert.True(woken.FreshAt(rig.Now));
        }

        /// <summary>⭐ the event goes through a batch's buffer too (callable from the parallel brain phase, R-4212): played back, the writer learns it.</summary>
        [Fact]
        public void UF2_LearnDirect_ThroughABatchBuffer_Lands()
        {
            using var rig = new Rig();
            var buffer = new EntityCommandBuffer();
            UnderFireEvents.LearnDirect(buffer, Red, new Vector3(50, 50, 0), 120f, 0.4f, (SimInstant)1.0);
            buffer.Playback(rig.W);
            rig.Tick();
            var r = Assert.Single(rig.Records());
            Assert.Equal((byte)(Red + 1), r.Header.SidePlus1);
            Assert.Equal(UnderFireCause.Direct, r.Cause);
        }
    }
}
