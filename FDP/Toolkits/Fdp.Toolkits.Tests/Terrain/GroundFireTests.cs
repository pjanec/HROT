using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Tests.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-3487</c> (flame FL-3 N-1, N-2, N-6; docs/designs/weapons/DESIGN_Flame_Weapons.md §7b; thermal TD-6) — a terrain cell's GROUND FIRE: the cell-record writer
    /// (CE-5000) with <see cref="FireState"/> as the record. A spot crosses in WORLD metres and is held in the cell anchor's frame (so <c>BurnSystem</c>, which reads a
    /// holder's spots in its frame, finds it where it burns); a burning VEHICLE's <c>FireState</c> is never taken for a record; a spot that has gone out leaves a
    /// <see cref="HeatMarkKind.BurnedArea"/> mark once.
    /// </summary>
    public sealed class GroundFireTests
    {
        private sealed class Chain
        {
            public readonly EntityRepository W = new();
            public readonly GroundFireSystem Fire = new();

            public Chain()
            {
                W.RegisterComponent<SimTransform>();
                W.RegisterComponent<FireState>();
                W.RegisterComponent<TkbIdentity>();
                W.RegisterComponent<NetworkAuthority>();
                W.RegisterEvent<GroundFireEvent>();
                W.RegisterEvent<GroundHeatMarkEvent>();
                At(100.0);
            }

            public void At(double t) => W.SetSingletonUnmanaged(new GlobalTime { TotalTime = t, DeltaTime = 0.1f, TimeScale = 1f });

            public List<EntityCreationRequest> Tick()
            {
                W.Bus.SwapBuffers();
                Fire.Execute(W, 0.1f);
                W.Bus.SwapBuffers();
                return W.Bus.ReadManaged<EntityCreationRequest>().ToList();
            }

            public Entity Materialise(EntityCreationRequest r)
            {
                var e = W.CreateEntity();
                W.AddComponent(e, new TkbIdentity { TkbType = r.TkbType });   // the spawner names its type
                foreach (var c in r.InitialComponents!)
                    switch (c)
                    {
                        case SimTransform t: W.AddComponent(e, t); break;
                        case FireState f: W.AddComponent(e, f); break;
                    }
                return e;
            }
        }

        private static FireSpot Spot(float x, float y, double since, float seconds) => new(x, y, 0f, 15f, (SimInstant)since, seconds, 1f);

        /// <summary>
        /// ⭐⭐ N-1 — a spot in WORLD metres becomes ONE transient request of the fire record's type, its cell set and the spot held in the cell anchor's frame;
        /// a second spot in the same cell joins the same record; a burning VEHICLE (a <c>FireState</c> of another type in cell 0, 0) is never written to.
        /// 🔴 Red-proof: the writer's <c>IsRecord</c> hook returning true for every holder ⇒ the second spot lands on the burning car.
        /// </summary>
        [Fact]
        public void CE3487_AGroundFireSpot_IsHeldOnItsCellsRecord_InTheAnchorsFrame_NeverOnABurningVehicle()
        {
            var chain = new Chain();
            var car = chain.W.CreateEntity();   // a burning car standing in cell (0, 0)
            chain.W.AddComponent(car, new TkbIdentity { TkbType = (TkbTypeId)1002 });
            chain.W.AddComponent(car, new SimTransform { Position = new Vector3(10, 10, 0), Rotation = Quaternion.Identity });
            var carFire = new FireState();
            carFire.TryAdd(new FireSpot(0, 0, 1, 3, (SimInstant)90.0, 120f, 1f), 90.0);
            chain.W.AddComponent(car, carFire);

            var first = Spot(20f, 30f, 100.0, 300f);                       // in cell (0, 0) — the car's cell
            chain.W.Bus.Publish(GroundFireEvent.Of(in first));
            var request = Assert.Single(chain.Tick());
            Assert.Equal(GroundFireRules.TkbType, (long)request.TkbType);
            Assert.True(request.IsTransient);
            var born = request.InitialComponents!.OfType<FireState>().Single();
            Assert.Equal((0, 0, (byte)1), (born.RecordCellX, born.RecordCellY, born.Count));
            var anchor = GroundDamages.Anchor(0, 0, 0f);
            Assert.Equal(new Vector3(20f, 30f, 0f) - anchor, new Vector3(born.Get(0).X, born.Get(0).Y, born.Get(0).Z));   // the anchor's frame
            Assert.Equal(anchor, (Vector3)request.InitialComponents!.OfType<SimTransform>().Single().Position);
            var record = chain.Materialise(request);

            chain.W.Bus.Publish(GroundFireEvent.Of(Spot(40f, 30f, 100.0, 300f)));
            Assert.Empty(chain.Tick());                                    // joins the record, asks for none
            Assert.Equal(2, chain.W.GetComponentRO<FireState>(record).Count);
            Assert.Equal(1, chain.W.GetComponentRO<FireState>(car).Count);   // ⛔ never the car's
        }

        /// <summary>
        /// ⭐ N-6 — a spot that has GONE OUT leaves ONE <see cref="HeatMarkKind.BurnedArea"/> mark where it burned (its world place, its size), and its slot is freed:
        /// the next tick leaves no second mark. A spot still burning leaves none. 🔴 Red-proof: the mark published without freeing the slot ⇒ two marks.
        /// </summary>
        [Fact]
        public void CE3487_ASpotGoneOut_LeavesOneScorch_WhereItBurned()
        {
            var chain = new Chain();
            chain.W.Bus.Publish(GroundFireEvent.Of(Spot(20f, 30f, 100.0, 50f)));
            chain.W.Bus.Publish(GroundFireEvent.Of(Spot(40f, 30f, 100.0, 300f)));
            var record = chain.Materialise(chain.Tick().Single());
            chain.Tick();   // the second spot joins

            chain.At(200.0);   // the first has gone out (100 + 50), the second burns on
            chain.Tick();
            var marks = chain.W.ReadEvents<GroundHeatMarkEvent>().ToArray();
            var mark = Assert.Single(marks);
            Assert.Equal((byte)HeatMarkKind.BurnedArea, mark.Kind);
            Assert.Equal((20f, 30f), (mark.X, mark.Y));
            Assert.Equal(30f, mark.Length);
            Assert.True(GroundHeatMarkRules.Leaves(mark.Mark), "the heat-mark writer takes it — a 50 s fire too (the 60 s minimum is a vehicle STAND's)");
            Assert.Equal(1, chain.Fire.Scorched);

            chain.Tick();
            Assert.Empty(chain.W.ReadEvents<GroundHeatMarkEvent>().ToArray());   // once
            Assert.True(chain.W.GetComponentRO<FireState>(record).IsBurning(200.0));
        }
    }
}
