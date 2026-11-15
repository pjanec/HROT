using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐ <c>CE-2436</c> AU-5 — an aircraft ON THE GROUND is no air contact (<see cref="FlightGround.OnTheGround"/>, read by
    /// <see cref="AirContacts.IsAir"/>): a drone parked on the ground — though it reads <c>Airborne</c> 1, the translator's default (measured live:
    /// Red's outpost sheltered from Blue's parked drones at t = 0) — and one its model has put down (<c>Airborne</c> 0) are not; the same drone
    /// 30 m up, or skimming 0.5 m over the ground at 10 m/s, is. ✅ Red-proof: the ground test dropped from <c>IsAir</c> ⇒ the parked drone is air ⇒ red.
    /// </summary>
    public sealed unsafe class FlightGroundTests
    {
        [Fact]
        public void CE2436_AParkedDrone_IsNoAirContact_OneInTheAirIs()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<Locomotion>();
            w.RegisterComponent<FlightState>();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<SimVelocity>();
            w.RegisterComponent<TargetMemory>();

            Entity Drone(Vector3 at, byte airborne, Vector3 velocity)
            {
                var d = w.CreateEntity();
                w.AddComponent(d, new Locomotion { Class = LocomotionClass.Rotorcraft });
                w.AddComponent(d, new FlightState { Airborne = airborne });
                w.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                w.AddComponent(d, new SimVelocity { Linear = velocity });
                return d;
            }
            bool Air(Entity d)
            {
                var mem = new TargetMemory();
                mem.EntityIds[0] = (long)d.PackedValue;
                mem.Count = 1;
                return AirContacts.IsAir(w, in mem, 0);
            }

            Assert.False(Air(Drone(Vector3.Zero, 1, Vector3.Zero)));                            // parked, Airborne still 1
            Assert.False(Air(Drone(new Vector3(5f, 0f, 0f), 0, Vector3.Zero)));                 // put down by its model
            Assert.True(Air(Drone(new Vector3(10f, 0f, 30f), 1, Vector3.Zero)));                // hovering 30 m up
            Assert.True(Air(Drone(new Vector3(15f, 0f, 0.5f), 1, new Vector3(10f, 0f, 0f))));  // skimming low, fast
        }
    }
}
