using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using CarKinem.Core;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3487</c> (flame FL-3 — F-5, N-3…N-5; docs/designs/weapons/DESIGN_Flame_Weapons.md §7b) — a NAPALM burst lays its fire in an ellipse along the flight
    /// line: every person inside is set alight (a <see cref="DamageAssessedEvent"/> with <see cref="HitEffect.Burning"/>, so it reaches his Health owner), none
    /// outside; and the ground fire's spots run along the axis, in world metres, for the cell-record writer.
    /// </summary>
    public sealed class NapalmSystemTests
    {
        private const long Firebomb = 0x6D01, Man = 0x6D02;
        private readonly EntityRepository _w = new();
        private readonly NapalmSystem _napalm = new();

        public NapalmSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<VehicleParams>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterEvent<GroundFireEvent>();
            _w.SetSingleton(new GlobalTime { TotalTime = 50.0, DeltaTime = 0.1f });
            var db = new TkbDatabase();
            var bomb = new TkbTemplate("test firebomb", Firebomb);
            bomb.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, Indirect = true, AreaLengthM = 80f, AreaWidthM = 25f, BurnSeconds = 300f, FlameIntensity = 1f });
            db.Register(bomb);
            db.Register(new TkbTemplate("test man", Man));
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        private Entity Person(float x, float y)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)Man });
            _w.AddComponent(e, new VehicleParams { Class = VehicleClass.Pedestrian });
            return e;
        }

        /// <summary>
        /// ⭐⭐ N-4 / N-5 — a firebomb flying NORTH bursts at (500, 500): men 35 m north and 10 m east of it are inside the 80 × 25 ellipse and are set alight; men 35 m
        /// EAST (inside the ellipse only if it lay east–west) and 50 m north are not; three ground spots run along the north axis, each 15 m in radius, burning 300 s.
        /// 🔴 Red-proof: the ellipse laid along X whatever the travel ⇒ the man to the east burns, the man to the north does not.
        /// </summary>
        [Fact]
        public void CE3487_ANapalmBurst_SetsAlightEveryManInsideItsEllipse_AlongTheFlightLine_AndLaysTheGroundFire()
        {
            var north = Person(500f, 535f);
            var side = Person(510f, 500f);
            var east = Person(535f, 500f);
            var far = Person(500f, 550f);
            _w.Bus.Publish(new DetonationNotification { Ammo = Firebomb, HitX = 500f, HitY = 500f, HitZ = 0f, TravelX = 0f, TravelY = 150f, TravelZ = -40f });
            _w.Bus.SwapBuffers();
            _napalm.Execute(_w, 0.1f);
            _w.Bus.SwapBuffers();

            var lit = _w.ReadEvents<DamageAssessedEvent>().ToArray();
            Assert.All(lit, d => Assert.Equal(HitEffect.Burning, d.Effect));
            Assert.Equal(new[] { north, side }.OrderBy(e => e.Index), lit.Select(d => d.HitEntity).OrderBy(e => e.Index));
            Assert.All(lit, d => Assert.Equal(Ignition.BurningManSeconds, (float)d.EffectSeconds, 3));

            var spots = _w.ReadEvents<GroundFireEvent>().ToArray();
            Assert.Equal(NapalmSystem.SpotsOf(80f, 25f), spots.Length);
            Assert.All(spots, s => Assert.Equal(500f, s.X, 3));                                  // along the north axis
            Assert.Equal(new[] { 500f - 80f / 3f, 500f, 500f + 80f / 3f }, spots.Select(s => s.Y).OrderBy(y => y).ToArray(), new FloatEq());
            Assert.All(spots, s => { Assert.Equal(15f, s.Radius, 3); Assert.Equal(300f, s.Seconds); });
            Assert.Equal(1, _napalm.Strikes);
        }

        /// <summary>
        /// ⭐ <c>CE-3487</c> + <c>CE-3488</c> (FL-4 B-3) — a napalm burst sets alight every BURNABLE building it reaches (a timber house 20 m along its axis), never a
        /// brick one in the same ellipse, nor a timber one far outside it. 🔴 Red-proof: both guards dropped (the burnable filter and `TryOf`'s 0 s refusal; each alone holds) ⇒ the brick house is lit.
        /// </summary>
        [Fact]
        public void CE3488_ANapalmBurst_SetsAlightTheBurnableBuildingsItReaches()
        {
            const string houses = """
                { "type": "FeatureCollection", "features": [
                  { "type": "Feature", "properties": { "kind": "building", "label": "Near timber", "building": { "footprint": [[0,0],[8,0],[8,6],[0,6]], "material": "timber",
                      "storeys": [ { "height": 2.8, "walls": [ { "from": [0,0], "to": [8,0], "thickness": 0.2 }, { "from": [8,0], "to": [8,6], "thickness": 0.2 },
                        { "from": [8,6], "to": [0,6], "thickness": 0.2 }, { "from": [0,6], "to": [0,0], "thickness": 0.2 } ] } ] } },
                    "geometry": { "type": "Point", "coordinates": [496, 517] } },
                  { "type": "Feature", "properties": { "kind": "building", "label": "Near brick", "building": { "footprint": [[0,0],[8,0],[8,6],[0,6]], "material": "brick",
                      "storeys": [ { "height": 2.8, "walls": [ { "from": [0,0], "to": [8,0], "thickness": 0.3 }, { "from": [8,0], "to": [8,6], "thickness": 0.3 },
                        { "from": [8,6], "to": [0,6], "thickness": 0.3 }, { "from": [0,6], "to": [0,0], "thickness": 0.3 } ] } ] } },
                    "geometry": { "type": "Point", "coordinates": [496, 477] } },
                  { "type": "Feature", "properties": { "kind": "building", "label": "Far timber", "building": { "footprint": [[0,0],[8,0],[8,6],[0,6]], "material": "timber",
                      "storeys": [ { "height": 2.8, "walls": [ { "from": [0,0], "to": [8,0], "thickness": 0.2 }, { "from": [8,0], "to": [8,6], "thickness": 0.2 },
                        { "from": [8,6], "to": [0,6], "thickness": 0.2 }, { "from": [0,6], "to": [0,0], "thickness": 0.2 } ] } ] } },
                    "geometry": { "type": "Point", "coordinates": [560, 500] } } ] }
                """;
            var terrain = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(houses, "range");
            _w.RegisterEvent<BuildingFireEvent>();
            _w.SetSingletonManaged(terrain);
            _w.Bus.Publish(new DetonationNotification { Ammo = Firebomb, HitX = 500f, HitY = 500f, HitZ = 0f, TravelX = 0f, TravelY = 150f });
            _w.Bus.SwapBuffers();
            _napalm.Execute(_w, 0.1f);
            _w.Bus.SwapBuffers();
            var lit = _w.ReadEvents<BuildingFireEvent>().ToArray().Select(f => terrain.Buildings[f.Building].Label).ToArray();
            Assert.Equal(new[] { "Near timber" }, lit);
        }

        private sealed class FloatEq : System.Collections.Generic.IEqualityComparer<float>
        {
            public bool Equals(float a, float b) => System.MathF.Abs(a - b) < 1e-3f;
            public int GetHashCode(float f) => 0;
        }
    }
}
