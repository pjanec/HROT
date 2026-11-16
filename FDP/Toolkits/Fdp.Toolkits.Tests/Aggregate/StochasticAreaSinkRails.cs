using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Xunit;

namespace Fdp.Toolkits.Tests.Aggregate
{
    /// <summary>
    /// ⭐⭐ <c>CE-8010</c> (ST-3, C-6, RV-8) — the stochastic sink of the area effect: a burst on a unit's footprint downs men in proportion to
    /// the overlap and the density, suppresses and stresses the rest; a burst beside it does nothing; cover cuts it; the same detonation
    /// repeats exactly. ✅ Red-proof: the overlap taken as 0 ⇒ no casualties ⇒ red.
    /// </summary>
    public sealed class StochasticAreaSinkRails
    {
        [Fact]
        public void TheCircleOverlap_IsRight()
        {
            Assert.Equal(0f, StochasticAreaSink.Overlap(10f, 5f, 20f));
            Assert.Equal(MathF.PI * 25f, StochasticAreaSink.Overlap(10f, 5f, 2f), 3);          // inside
            Assert.Equal(MathF.PI * 100f, StochasticAreaSink.Overlap(10f, 10f, 0f), 3);        // coincident
            float half = StochasticAreaSink.Overlap(10f, 10f, 10f);
            Assert.InRange(half, 0.39f * MathF.PI * 100f, 0.40f * MathF.PI * 100f);           // two unit circles a radius apart overlap ~39 %
        }

        private static (EntityRepository W, Entity U) World(PreparedLevel prepared = PreparedLevel.Unprepared, int men = 30)
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<AggregateUnit>(); w.RegisterComponent<UnitSlotTable>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(e, new AggregateUnit { Resolution = AggregateResolution.Stochastic, Width = 100f, Depth = 30f, Prepared = prepared });
            var s = new UnitSlotTable { Count = (byte)men };
            for (int i = 0; i < men; i++) s.Slots[i] = new UnitSlot { Role = SlotRole.Rifleman, Health = (Half)25f, Ammo = 100 };
            w.AddComponent(e, s);
            return (w, e);
        }

        [Fact]
        public void ABurstOnTheFootprint_DownsMen_SuppressesAndStresses_ABurstBesideItDoesNothing()
        {
            int total = 0, fortified = 0;
            for (uint seq = 1; seq <= 40; seq++)
            {
                var (w, u) = World();
                using (w)
                {
                    total += StochasticAreaSink.Apply(w, new Vector3(5f, 0f, 0f), 15f, seq, (SimInstant)10.0);
                    if (seq == 1)
                    {
                        Assert.True(w.GetComponentRO<AggregateUnit>(u).SuppressedShare > 0f);
                        float stress = 0f; var t = w.GetComponentRO<UnitSlotTable>(u);
                        for (int i = 0; i < t.Count; i++) stress += (float)t.Slots[i].Stress;
                        Assert.True(stress > 0f);
                        Assert.Equal(0, StochasticAreaSink.Apply(w, new Vector3(500f, 0f, 0f), 15f, 2u, (SimInstant)11.0));   // beside it
                    }
                }
                var (wf, _) = World(PreparedLevel.Fortified);
                using (wf) fortified += StochasticAreaSink.Apply(wf, new Vector3(5f, 0f, 0f), 15f, seq, (SimInstant)10.0);
            }
            // λ = 30 men × (π·15² / 3000 m²) × 0.5 ≈ 3.5 a burst ⇒ ~140 over 40 bursts
            Assert.InRange(total, 100, 190);
            Assert.True(fortified < total * 0.45f, $"open {total}, fortified {fortified}");
            var (a, _) = World(); var (b, _) = World();
            using (a) using (b)
                Assert.Equal(StochasticAreaSink.Apply(a, new Vector3(5f, 0f, 0f), 15f, 7u, (SimInstant)3.0),
                             StochasticAreaSink.Apply(b, new Vector3(5f, 0f, 0f), 15f, 7u, (SimInstant)3.0));      // the same detonation, the same casualties
        }
    }
}
