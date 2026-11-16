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
    
        /// <summary>⭐⭐ <c>CE-4623</c> AB-2 (Animals AR-1 / AR-2) — a COW (Quadruped) is a ground body that is no vehicle (no roads preferred, no hull
        /// backing out) and no person; a BIRD is in the air and NEVER an aircraft (radar, air contacts and air defence never see it); the ground class
        /// is moved by the kinematics (no air model), the bird by its own; the preset maps to the class. 🔴 Red-proof: drop <c>Bird</c> from
        /// <c>IsAircraft</c>'s exclusions ⇒ red.</summary>
        [Fact]
        public void CE4623_ACowIsNoVehicleAndNoPerson_ABirdFliesAndIsNoAircraft()
        {
            using var w = World();
            var cow = w.CreateEntity();
            w.AddComponent(cow, new VehicleParams { Class = VehicleClass.Quadruped, Length = 2.4f, Width = 0.9f });
            w.AddComponent(cow, new VehicleState());
            w.AddComponent(cow, new Locomotion { Class = LocomotionClass.Quadruped });
            Agree(w, cow, false, "a cow");
            Assert.Equal(MotionDomain.Ground, Locomotions.DomainOf(LocomotionClass.Quadruped));
            Assert.False(Locomotions.IsAircraft(w, cow));
            Assert.False(global::CarKinem.Controllers.HullClearance.HasHull(w.GetComponentRO<VehicleParams>(cow)));
            Assert.Equal(LocomotionClass.Quadruped, Locomotions.FromVehicleClass(VehicleClass.Quadruped));
            Assert.Null(MotionModelTable.Model(LocomotionClass.Quadruped));

            var bird = w.CreateEntity();
            w.AddComponent(bird, new Locomotion { Class = LocomotionClass.Bird });
            Assert.Equal(MotionDomain.Air, Locomotions.DomainOf(LocomotionClass.Bird));
            Assert.False(Locomotions.IsAircraft(w, bird), "a bird is never an aircraft");
            Assert.IsType<BirdFlightModel>(MotionModelTable.Model(LocomotionClass.Bird));
            Assert.Equal(NavLayerMask.Infantry, NavLayerSelection.For(w, cow, 0u));   // an animal walks where a man walks
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4623</c> AB-6 — a Quadruped whose temperament Jumps (a horse) plans on the infantry layer WITH the <see cref="NavLayerSelection.JumpModifier"/> bit
        /// (the filter that opens the navmesh's jump links); a Quadruped that does not jump (a cow) and a Pedestrian get none. 🔴 Red-proof: drop the temperament test
        /// from <c>NavLayerSelection.For</c> ⇒ the cow carries the bit.
        /// </summary>
        [Fact]
        public void CE4623_OnlyAJumpingQuadruped_CarriesTheJumpModifier()
        {
            using var w = World();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var horseT = new Fdp.Interfaces.TkbTemplate("Horse", 701);
            horseT.AddDescriptor(new AnimalTemperamentDto { Jumps = true });
            var cowT = new Fdp.Interfaces.TkbTemplate("Cow", 700);
            cowT.AddDescriptor(new AnimalTemperamentDto { Jumps = false });
            db.Register(horseT); db.Register(cowT);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);

            Entity Make(VehicleClass cls, int tkb)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new VehicleParams { Class = cls });
                w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (TkbTypeId)tkb });
                return e;
            }
            uint Mask(Entity e) => (uint)NavLayerSelection.For(w, e, 0u);
            Assert.Equal((uint)NavLayerMask.Infantry | NavLayerSelection.JumpModifier, Mask(Make(VehicleClass.Quadruped, 701)));
            Assert.Equal((uint)NavLayerMask.Infantry, Mask(Make(VehicleClass.Quadruped, 700)));
            Assert.Equal((uint)NavLayerMask.Infantry, Mask(Make(VehicleClass.Pedestrian, 701)));   // even a person with a horse's type id
        }
    }
}
