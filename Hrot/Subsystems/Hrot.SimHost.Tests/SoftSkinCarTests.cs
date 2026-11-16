using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-3 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-6, CP-7, KV-3) — the SHIPPED civilian car (TKB 1002) has the ENGINE the
    /// disabling shots aim at: its body profile ("Car", DIS 1.1.*.81) holds a front Engine zone whose wound is a Mobility kill, a soft skin (side armour ≤ 10 mm, so
    /// a penetrating round may reach who sits inside), and <see cref="AimPoint.Engine"/> lies in its front third.
    /// 📐 Measured `2026-11-16`: KV-3's "a car has no engine to aim at" was already overtaken — the Car profile carried the zone; what was missing is the aim point.
    /// </summary>
    public sealed class SoftSkinCarTests
    {
        /// <summary>⭐ CP-7 — the engine zone (front, Mobility) and the soft skin; the aim point in the front third at a third of the height, turned with the car.
        /// ✅ Red-proof: the aim point taken from the rear ⇒ red.</summary>
        [Fact]
        public void CE4622_K3_TheCivilianCar_HasAFrontEngine_ASoftSkin_AndAnEngineAimPoint()
        {
            var db = new TkbDatabase();
            Hrot.Map.Definitions.Tkb.NedTkbCatalog.RegisterAll(db);
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(db);
            Assert.True(db.TryGetByType(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar, out var car));
            var body = BodyZones.Of(car);
            Assert.NotNull(body);
            Assert.Equal(EngineEnd.Front, body!.EngineEnd);
            var engine = body.Zones.Single(z => z.Name == "Engine");
            Assert.Equal(WoundEffect.Mobility, engine.Effect);
            var platform = car.GetDescriptor<CombatPlatformDefDto>();
            Assert.True(platform == null || platform.ArmorSide <= Fdp.Toolkit.Behavior.Systems.EmbarkedStateSystem.SoftSkinMaxArmourMm,
                $"a soft skin: side {platform?.ArmorSide} mm");

            using var w = new EntityRepository();
            w.RegisterComponent<SimTransform>(); w.RegisterComponent<TkbIdentity>();
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(100f, 50f, 0f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI / 2f) });   // facing north
            w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar });
            var at = AimPoint.Engine(w, e);
            Assert.NotNull(at);
            Assert.InRange(at!.Value.X, 99.5f, 100.5f);                    // on the car's axis
            Assert.True(at.Value.Y > 50f + 0.5f, $"in the FRONT (north) of a car facing north: {at}");
            Assert.True(at.Value.Z > 0.1f && at.Value.Z < 1.2f, $"low in the hull: {at.Value.Z:F2} m");
        }
    }
}
