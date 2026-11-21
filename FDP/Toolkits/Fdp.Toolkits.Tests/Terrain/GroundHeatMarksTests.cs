using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Weather;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4664</c> (thermal drivers TD-14; 📄 docs/designs/thermal/DESIGN_Thermal_Drivers.md §4b) — the ground heat marks: the writer entry point
    /// (<see cref="GroundHeatMarkRules.LeaveStand"/>), the cell record (<see cref="GroundHeatMarkSystem"/>, the crater writer's shape), a cold mark dropped by the next
    /// write, and <see cref="ThermalDriversOps.MarkWarmth"/> cooling by the power class. The wire half (every node, a late joiner) is
    /// <c>Hrot.SimHost.Tests.BodyPartReplicationTests.CE4664_*</c>.
    /// </summary>
    public sealed class GroundHeatMarksTests
    {
        private static HeatMark Stand(float x, float y, double since, double left, float engineOn, ThermalPowerClass power = ThermalPowerClass.Tank)
            => new(HeatMarkKind.VehicleStand, new Vector2(x, y), 0.5f, 6.9f, 3.6f, HeatMarkEnd.Rear, (SimInstant)since, (SimInstant)left, engineOn, power);

        private sealed class Chain
        {
            public readonly EntityRepository W = new();
            public readonly GroundHeatMarkSystem Marks = new();

            public Chain()
            {
                W.RegisterComponent<SimTransform>();
                W.RegisterComponent<GroundHeatMarks>();
                W.RegisterComponent<NetworkAuthority>();
                W.RegisterEvent<GroundHeatMarkEvent>();
            }

            public List<EntityCreationRequest> Tick()
            {
                W.Bus.SwapBuffers();
                Marks.Execute(W, 0.016f);
                W.Bus.SwapBuffers();
                return W.Bus.ReadManaged<EntityCreationRequest>().ToList();
            }

            public Entity Materialise(EntityCreationRequest r)
            {
                var e = W.CreateEntity();
                foreach (var c in r.InitialComponents!)
                    switch (c)
                    {
                        case SimTransform t: W.AddComponent(e, t); break;
                        case GroundHeatMarks d: W.AddComponent(e, d); break;
                    }
                return e;
            }
        }

        /// <summary>
        /// ⭐⭐ The writer entry point: a tank that idled 15 min and drove off ⇒ ONE request born holding its mark (transient, its cell); a second stand in the same cell
        /// joins the SAME entity; a stand under 60 s, or with the engine off, leaves nothing (and publishes nothing).
        /// 🔴 Red-proof: <see cref="GroundHeatMarkRules.Leaves"/> not checking the engine time ⇒ the cold stand is published ⇒ red.
        /// </summary>
        [Fact]
        public void CE4664_AWarmStandLeavesAMark_InItsCellsRecord_AShortOrColdStandLeavesNone()
        {
            var chain = new Chain();
            Assert.False(GroundHeatMarkRules.LeaveStand(chain.W, Stand(10f, 10f, 100, 140, 40f)));        // 40 s: too short
            Assert.False(GroundHeatMarkRules.LeaveStand(chain.W, Stand(10f, 10f, 100, 1000, 0f)));        // parked, engine off: cold
            Assert.Empty(chain.Tick());

            var tank = Stand(10f, 10f, 100, 1000, 900f);                                                 // idled 15 min
            Assert.True(GroundHeatMarkRules.LeaveStand(chain.W, tank));
            var request = Assert.Single(chain.Tick());
            Assert.Equal(GroundHeatMarkRules.TkbType, (long)request.TkbType);
            Assert.True(request.IsTransient);
            var d0 = request.InitialComponents!.OfType<GroundHeatMarks>().Single();
            Assert.Equal((1, 0, 0), ((int)d0.Count, d0.CellX, d0.CellY));
            Assert.Equal(tank, d0.Get(0));
            chain.Materialise(request);

            Assert.True(GroundHeatMarkRules.LeaveStand(chain.W, Stand(40f, 30f, 1200, 1500, 300f, ThermalPowerClass.Truck)));   // the same 64 m cell
            Assert.Empty(chain.Tick());
            var all = new List<Entity>(); foreach (var x in chain.W.Query().With<GroundHeatMarks>().Build()) all.Add(x);
            var e = Assert.Single(all);
            Assert.Equal(2, chain.W.GetComponentRO<GroundHeatMarks>(e).Count);
        }

        /// <summary>
        /// ⭐ §4b "kept until it is COLD … then dropped by the next write to that cell": a car's mark (cold after 1 h) is gone when a mark is written 2 h later;
        /// a tank's left 1 h before that write is kept. 🔴 Red-proof: <see cref="GroundHeatMarkRules.Add"/> not dropping cold marks ⇒ 3 ⇒ red.
        /// </summary>
        [Fact]
        public void CE4664_AColdMark_IsDroppedByTheNextWriteToItsCell()
        {
            var d = default(GroundHeatMarks);
            Assert.True(GroundHeatMarkRules.Add(ref d, Stand(5f, 5f, 0, 600, 600f, ThermalPowerClass.Car), out _));
            Assert.True(GroundHeatMarkRules.Add(ref d, Stand(20f, 5f, 3000, 4200, 600f, ThermalPowerClass.Tank), out _));
            Assert.True(GroundHeatMarkRules.Add(ref d, Stand(30f, 5f, 7000, 7800, 600f), out _));        // written at 7800 s: the car (left at 600) is cold
            Assert.Equal(2, d.Count);
            Assert.Equal(new Vector2(20f, 5f), d.Get(0).At);
            Assert.Equal(new Vector2(30f, 5f), d.Get(1).At);
            Assert.False(GroundHeatMarkRules.Add(ref d, d.Get(1), out bool full));                    // the same mark twice
            Assert.False(full);
        }

        /// <summary>
        /// ⭐ <see cref="ThermalDriversOps.MarkWarmth"/> — warm when the tank leaves (15 min of engine ⇒ ~0.78), falling; at 1 h a car's mark is cold while a tank's is still
        /// warm (power class); wind and rain cool it faster; 0 before it was left and once cold. 🔴 Red-proof: the cooling ignoring the power class ⇒ the car still warm ⇒ red.
        /// </summary>
        [Fact]
        public void CE4664_AMarkCoolsOverItsPowerClassTime_FasterInWindAndRain()
        {
            var calm = WeatherConditions.Standard;
            var tank = Stand(0f, 0f, 0, 1000, 900f, ThermalPowerClass.Tank);
            var car = Stand(0f, 0f, 0, 1000, 900f, ThermalPowerClass.Car);

            float atLeave = ThermalDriversOps.MarkWarmth(tank, 1000, calm);
            Assert.InRange(atLeave, 0.7f, 0.85f);
            Assert.Equal(0f, ThermalDriversOps.MarkWarmth(tank, 900, calm));                              // not yet left
            Assert.True(ThermalDriversOps.MarkWarmth(tank, 1600, calm) < atLeave);
            Assert.Equal(0f, ThermalDriversOps.MarkWarmth(car, 1000 + 3600, calm));                       // a car's mark is cold after 1 h
            Assert.True(ThermalDriversOps.MarkWarmth(tank, 1000 + 3600, calm) > 0.1f);                    // a tank's is still warm
            Assert.Equal(0f, ThermalDriversOps.MarkWarmth(tank, 1000 + 7200, calm));

            var windy = WeatherConditions.SteadyWind(new Vector2(8f, 0f));
            var rain = calm; rain.Precipitation = 1; rain.PrecipitationMmPerHour = 4f;
            float still = ThermalDriversOps.MarkWarmth(tank, 2800, calm);
            Assert.True(ThermalDriversOps.MarkWarmth(tank, 2800, windy) < still);
            Assert.True(ThermalDriversOps.MarkWarmth(tank, 2800, rain) < still);
        }

        /// <summary>⭐ The toolkit's ordinal equals the record's size budget: under the 1024 B command-buffer limit (a creation request carries it).</summary>
        [Fact]
        public unsafe void CE4664_TheRecord_FitsTheCommandBuffer()
            => Assert.True(sizeof(GroundHeatMarks) <= 1024, $"{sizeof(GroundHeatMarks)} B");
    }
}
