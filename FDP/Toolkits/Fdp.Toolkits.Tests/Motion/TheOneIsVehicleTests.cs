using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-1373</c> — there is ONE <c>IsVehicle</c> (<see cref="Locomotions.IsVehicle"/>: a ground body that is not a person, read through <see cref="Locomotions.Of"/>), and
    /// <c>VehicleCover.IsVehicle</c> and <c>PathRequests.IsVehicle</c> forward to it. Both call sites agree on a body with <see cref="VehicleParams"/>, on a <see cref="Locomotion"/>-only wheeled body
    /// (the one behaviour change: it is a vehicle now — before, neither knew it), on a pedestrian, on a flyer and on a bare <see cref="VehicleState"/> (still a vehicle).
    /// 🔴 Red-proof: <c>PathRequests.IsVehicle</c> back to its own <c>VehicleParams</c> rule ⇒ the Locomotion-only row.
    /// </summary>
    public sealed class TheOneIsVehicleTests
    {
        private static EntityRepository World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<Locomotion>();
            w.RegisterComponent<VehicleParams>();
            w.RegisterComponent<VehicleState>();
            return w;
        }

        private static void Agree(EntityRepository w, Entity e, bool expected, string what)
        {
            Assert.True(expected == Locomotions.IsVehicle(w, e), $"{what}: Locomotions.IsVehicle");
            Assert.True(expected == VehicleCover.IsVehicle(w, e), $"{what}: VehicleCover.IsVehicle");
            Assert.True(expected == PathRequests.IsVehicle(w, e), $"{what}: PathRequests.IsVehicle");
        }

        [Fact]
        public void BothCallSitesAgree_OnEveryKindOfBody()
        {
            using var w = World();

            var car = w.CreateEntity();                                                    // a TKB ground vehicle: params + state + class
            w.AddComponent(car, new VehicleParams { Class = VehicleClass.PersonalCar });
            w.AddComponent(car, new VehicleState());
            w.AddComponent(car, new Locomotion { Class = LocomotionClass.Wheeled });
            Agree(w, car, true, "a car with VehicleParams");

            var paramsOnly = w.CreateEntity();                                             // a body built before CE-2168: the preset implies the class
            w.AddComponent(paramsOnly, new VehicleParams { Class = VehicleClass.Tank });
            Agree(w, paramsOnly, true, "a tank with VehicleParams only");

            var locoOnly = w.CreateEntity();                                               // the case the two copies disagreed on
            w.AddComponent(locoOnly, new Locomotion { Class = LocomotionClass.Wheeled });
            Agree(w, locoOnly, true, "a Locomotion-only wheeled body");

            var man = w.CreateEntity();                                                    // SimHost infantry carries VehicleParams (Pedestrian) AND VehicleState
            w.AddComponent(man, new VehicleParams { Class = VehicleClass.Pedestrian });
            w.AddComponent(man, new VehicleState());
            w.AddComponent(man, new Locomotion { Class = LocomotionClass.Pedestrian });
            Agree(w, man, false, "a pedestrian");

            var heli = w.CreateEntity();
            w.AddComponent(heli, new Locomotion { Class = LocomotionClass.Rotorcraft });
            Agree(w, heli, false, "a rotorcraft");

            var bare = w.CreateEntity();                                                   // no class anywhere: a bare kinematic body is still a vehicle
            w.AddComponent(bare, new VehicleState());
            Agree(w, bare, true, "a bare VehicleState");

            Agree(w, w.CreateEntity(), false, "a body with none of them");
        }
    }
}
