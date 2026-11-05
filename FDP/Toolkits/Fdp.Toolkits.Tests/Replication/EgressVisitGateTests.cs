using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Utilities;
using Xunit;

namespace Fdp.Toolkit.Replication.Tests
{
    /// <summary>
    /// ⭐ CE-2407 (docs/DESIGN_Egress_Change_Detection.md §5, E1) — the egress visit gate: a rail per hazard H1–H8, the structural case, the
    /// sweep bound, the kill switch, zero allocation, and the EQUIVALENCE rail (a gated translator publishes exactly what an ungated one does
    /// when only declared inputs change). The fake translator here has the shape every opted-in translator has: its own query, then
    /// <c>Visit</c>, then its own last-value cache.
    /// </summary>
    [Collection("EgressVisitGate")]   // the gate's switches are static
    public sealed class EgressVisitGateTests : IDisposable
    {
        private readonly int _savedSweep = EgressVisitGate.SweepPeriod;
        private readonly bool _savedEnabled = EgressVisitGate.Enabled;
        public void Dispose() { EgressVisitGate.SweepPeriod = _savedSweep; EgressVisitGate.Enabled = _savedEnabled; }

        private sealed class Translator
        {
            public readonly EgressVisitGate Gate = new(typeof(Health));
            public readonly HashSet<int> Visited = new();
            public readonly Dictionary<int, float> Last = new();
            public readonly List<(uint Frame, int Entity, float Value)> Published = new();
            public bool Gated = true;
            public void Scan(EntityRepository w)
            {
                if (Gated) Gate.Begin(w);
                Visited.Clear();
                foreach (var e in w.Query().With<NetworkIdentity>().With<Health>().Build())
                {
                    if (Gated && !Gate.Visit(e)) continue;
                    Visited.Add(e.Index);
                    float v = w.GetComponentRO<Health>(e).Current;
                    if (Last.TryGetValue(e.Index, out var prev) && prev == v) continue;
                    Last[e.Index] = v;
                    Published.Add((w.SimulationTick, e.Index, v));
                }
            }
        }

        private static (EntityRepository World, List<Entity> Units) World(int n)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>(); w.RegisterComponent<Health>(); w.RegisterComponent<SimTransform>();
            w.RegisterComponent<NetworkAuthority>(); w.RegisterComponent<PartMetadata>(); w.RegisterComponent<ReplicationRoute>();
            w.RegisterManagedComponent<DescriptorOwnership>();
            var units = new List<Entity>();
            for (int i = 0; i < n; i++)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new NetworkIdentity(1000 + i));
                w.AddComponent(e, new Health { Current = 10, Max = 10 });
                w.AddComponent(e, new SimTransform());
                w.AddComponent(e, new NetworkAuthority(1, 1));
                units.Add(e);
            }
            return (w, units);
        }

        private static void Frame(EntityRepository w, Translator t) { w.Tick(); t.Scan(w); }

        private static void Settle(EntityRepository w, Translator t) { Frame(w, t); Frame(w, t); }   // first = full scan, second = quiet

        [Fact]
        public void AQuietWorld_VisitsOnlyTheSweepSlice_AndAllocatesNothing()
        {
            EgressVisitGate.SweepPeriod = 8;
            var (w, _) = World(64); var t = new Translator();
            Frame(w, t);
            Assert.Equal(64, t.Visited.Count);                                       // the first scan is full
            Frame(w, t); Frame(w, t);
            Assert.Equal(8, t.Visited.Count);                                        // 64 / 8: the sweep slice only
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { w.Tick(); t.Gate.Begin(w); foreach (var e in w.Query().With<NetworkIdentity>().With<Health>().Build()) t.Gate.Visit(e); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);
        }

        [Fact]
        public void H4_AWriteToADeclaredInput_IsVisitedNextFrame_AndOneToAnUndeclaredComponentIsNot()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(40); var t = new Translator(); Settle(w, t);
            w.GetComponentRW<Health>(u[5]).Current = 3;
            w.GetComponentRW<SimTransform>(u[30]).Position = default;   // not an input of this translator
            Frame(w, t);
            Assert.Contains(u[5].Index, t.Visited);
            Assert.Contains((w.SimulationTick, u[5].Index, 3f), t.Published);
        }

        [Fact]
        public void H6_TheEntitysOwnChunk_IsWhatCounts_AWriteInALaterChunkIsSeen_AndTheFirstChunkIsNotDisturbed()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(5000);                                                 // SimTransform is wide ⇒ several chunks over 5000 entities
            var gate = new EgressVisitGate(typeof(SimTransform));
            void Scan(HashSet<int> into) { into.Clear(); gate.Begin(w); foreach (var e in w.Query().With<SimTransform>().Build()) if (gate.Visit(e)) into.Add(e.Index); }
            var visited = new HashSet<int>();
            w.Tick(); Scan(visited); w.Tick(); Scan(visited);
            var last = u[4999];
            w.GetComponentRW<SimTransform>(last).Rotation = new WorldRotation(0, 0, 1, 0);   // a real change (the position is already default)
            w.Tick(); Scan(visited);
            Assert.Contains(last.Index, visited);
            Assert.DoesNotContain(u[0].Index, visited);                              // the first chunk was not written
        }

        /// <summary>⭐ A chunk version moves for ANY write in the chunk (thousands of small components share one); the gate compares the entity's
        /// own bytes, so a neighbour's write and a same-value rewrite do not visit this entity, and a real change still does.
        /// 🔴 Red-proof: <c>Same</c> returning false ⇒ the neighbours are visited and this rail fails.</summary>
        [Fact]
        public void ANeighboursWrite_AndASameBytesRewrite_DoNotVisit_ARealChangeDoes()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(200); var t = new Translator(); Settle(w, t);
            w.GetComponentRW<Health>(u[10]).Current = 10;                          // the same value, rewritten
            w.GetComponentRW<Health>(u[11]).Current = 4;                           // a real change in the same chunk
            Frame(w, t);
            Assert.Equal(new[] { u[11].Index }, t.Visited);
            Assert.Contains((w.SimulationTick, u[11].Index, 4f), t.Published);
        }

        [Fact]
        public void H8_MarkDirty_IsVisitedNextFrame()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(40); var t = new Translator();
            SmartEgressUtil.MarkPublished(w, u[7], 42);                              // the publication state exists ⇒ MarkDirty edits it IN PLACE (no structure)
            Settle(w, t);
            Assert.DoesNotContain(u[7].Index, t.Visited);
            SmartEgressUtil.MarkDirty(w, u[7], 42);
            Frame(w, t);
            Assert.Contains(u[7].Index, t.Visited);
        }

        [Fact]
        public void H1_AuthorityArriving_AnywhereIncludingAPartsRoot_MakesAFullScan()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(40); var t = new Translator(); Settle(w, t);
            long full = t.Gate.FullScans;
            w.SetComponent(u[0], new NetworkAuthority(2, 2));                        // a grant to this node of ONE entity
            Frame(w, t);
            Assert.Equal(full + 1, t.Gate.FullScans);
            Assert.Equal(40, t.Visited.Count);                                       // every entity: a part of that root may be in the list
            Frame(w, t);
            Assert.Equal(full + 1, t.Gate.FullScans);                                // and only that frame
            w.SetManagedComponent(u[3], new DescriptorOwnership());                  // a descriptor record
            Frame(w, t);
            Assert.Equal(full + 2, t.Gate.FullScans);
        }

        [Fact]
        public void H3_ASeekRestoreOrWipe_AndTheClockGoingBack_MakeAFullScan()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, _) = World(20); var t = new Translator(); Settle(w, t);
            long full = t.Gate.FullScans;
            SmartEgressUtil.ForceMarkAllDirty(w); Frame(w, t);
            Assert.Equal(full + 1, t.Gate.FullScans);
            WorldEpoch.Advance(w); Frame(w, t);
            Assert.Equal(full + 2, t.Gate.FullScans);
            WorldEpoch.Restored(w); Frame(w, t);
            Assert.Equal(full + 3, t.Gate.FullScans);
            Frame(w, t);
            Assert.Equal(full + 3, t.Gate.FullScans);
            w.ResetGlobalVersion(2);                                                 // playback sets the clock back
            t.Scan(w);
            Assert.Equal(full + 4, t.Gate.FullScans);
            Assert.Equal(20, t.Visited.Count);
        }

        [Fact]
        public void Structural_AComponentRemovedOrAdded_IsVisitedNextFrame()
        {
            EgressVisitGate.SweepPeriod = 100_000;
            var (w, u) = World(40); var t = new Translator(); Settle(w, t);
            w.RemoveComponent<SimTransform>(u[11]);                                  // e.g. the route tag a query excludes, removed
            w.AddComponent(u[12], new ReplicationRoute());
            Frame(w, t);
            Assert.Contains(u[11].Index, t.Visited);
            Assert.Contains(u[12].Index, t.Visited);
        }

        [Fact]
        public void H5_H2_AnUnsignalledChange_IsVisitedWithinTheSweepPeriod_EveryEntity()
        {
            EgressVisitGate.SweepPeriod = 8;
            var (w, u) = World(64); var t = new Translator(); Settle(w, t);
            var seen = new HashSet<int>();
            for (int f = 0; f < 8; f++) { Frame(w, t); seen.UnionWith(t.Visited); }
            Assert.Equal(64, seen.Count);                                            // nobody waits longer than the period (H2's bound, H5's delivery)
        }

        [Fact]
        public void TheKillSwitch_VisitsEverything()
        {
            var (w, _) = World(30); var t = new Translator(); Settle(w, t);
            EgressVisitGate.Enabled = false;
            Frame(w, t);
            Assert.Equal(30, t.Visited.Count);
        }

        [Fact]
        public void Equivalence_TheGatedTranslatorPublishesExactlyWhatTheUngatedOneDoes()
        {
            EgressVisitGate.SweepPeriod = 8;
            var (w, u) = World(300);
            var gated = new Translator(); var reference = new Translator { Gated = false };
            var rng = new Random(7);
            for (int f = 0; f < 300; f++)
            {
                w.Tick();
                int writes = rng.Next(0, 6);
                for (int k = 0; k < writes; k++) w.GetComponentRW<Health>(u[rng.Next(u.Count)]).Current = rng.Next(0, 5);
                if (f % 37 == 0) w.GetComponentRW<SimTransform>(u[rng.Next(u.Count)]).Position = default;
                if (f % 53 == 0) { var e = w.CreateEntity(); w.AddComponent(e, new NetworkIdentity(9000 + f)); w.AddComponent(e, new Health { Current = 1 }); u.Add(e); }
                if (f % 61 == 0) { var gone = u[rng.Next(u.Count)]; w.DestroyEntity(gone); u.Remove(gone); }
                gated.Scan(w); reference.Scan(w);
            }
            Assert.NotEmpty(reference.Published);
            Assert.Equal(reference.Published, gated.Published);
            Assert.True(gated.Gate.Skipped > gated.Gate.Visited, $"visited {gated.Gate.Visited}, skipped {gated.Gate.Skipped}");
        }
    }
}
