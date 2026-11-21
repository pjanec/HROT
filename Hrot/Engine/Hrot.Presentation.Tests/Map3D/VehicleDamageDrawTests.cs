using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D
{
    /// <summary>
    /// ⭐ <c>CE-4670</c> (vehicle appearance VA-S1) — what our 3D map draws from <c>VehicleLook</c>: the tank kit's parts map to the simulation's (the turret,
    /// the gun, each track, the hull), a destroyed left track darkens only the left track, and a Catastrophic kill throws the turret beside the hull.
    /// </summary>
    public sealed class VehicleDamageDrawTests
    {
        [Fact]
        public void CE4670_TheTankKitsParts_MapToTheSimulationsParts()
        {
            var kit = ShapeKits.Tank.Parts;
            Assert.Equal(VehicleDamageDraw.GearLeftBit, VehicleDamageDraw.BitOf(kit[0]));    // track, left
            Assert.Equal(VehicleDamageDraw.GearRightBit, VehicleDamageDraw.BitOf(kit[1]));   // track, right
            Assert.Equal(VehicleDamageDraw.HullBit, VehicleDamageDraw.BitOf(kit[2]));
            Assert.Equal(VehicleDamageDraw.TurretBit, VehicleDamageDraw.BitOf(kit[3]));
            Assert.Equal(VehicleDamageDraw.GunBit, VehicleDamageDraw.BitOf(kit[4]));
            Assert.True(VehicleDamageDraw.Thrown(kit[3]) && VehicleDamageDraw.Thrown(kit[4]) && !VehicleDamageDraw.Thrown(kit[2]));
        }

        /// <summary>🔴 Red-proof: mapping every Hull-role part to the hull darkens the whole body for a track hit.</summary>
        [Fact]
        public void CE4670_ADestroyedLeftTrack_DarkensOnlyTheLeftTrack()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<Health>(); w.RegisterComponent<TkbIdentity>(); w.RegisterComponent<PartDamage>();
            var db = new TkbDatabase();
            var t = new TkbTemplate("T1", 1) { DisType = new DISEntityType { Kind = 1, Domain = 1, Category = 1 } };
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 1000f, ArmorFront = 500f, ArmorSide = 80f, ArmorRear = 40f, ArmorTop = 30f });
            db.Register(t);
            w.SetSingletonManaged<ITkbDatabase>(db);
            var v = w.CreateEntity();
            w.AddComponent(v, new TkbIdentity { TkbType = (TkbTypeId)1L });
            w.AddComponent(v, new Health { Current = 800f, Max = 1000f });
            var pd = new PartDamage { Plan = BodyPlan.TrackedVehicle };
            pd.Set(VehiclePart.RunningGearL, 40);
            w.AddComponent(v, pd);

            byte mask = VehicleDamageDraw.DestroyedMask(w, v);
            Assert.Equal(VehicleDamageDraw.GearLeftBit, mask);
            var kit = ShapeKits.Tank.Parts;
            Assert.True(VehicleDamageDraw.Destroyed(mask, kit[0]));
            for (int i = 1; i < kit.Length; i++) Assert.False(VehicleDamageDraw.Destroyed(mask, kit[i]));
        }

        [Fact]
        public void CE4670_AThrownTurret_LandsBesideTheHull_Lower()
        {
            var size = new Vector3(7.9f, 3.7f, 2.4f);
            var ring = new Vector3(0f, 0f, 0.62f * size.Z);
            var moved = Vector3.Transform(ring, VehicleDamageDraw.ThrownTransform(size));
            Assert.True(moved.Y > size.Y / 2f, $"beside the hull: {moved}");
            Assert.True(moved.Z < ring.Z, $"lower than the ring: {moved}");
        }
    }
}
