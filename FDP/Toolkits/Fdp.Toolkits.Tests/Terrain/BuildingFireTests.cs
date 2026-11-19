using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3488</c> (flame FL-4 B-1…B-4; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7c) — a BUILDING burns when its walls do: a timber house burns its walls'
    /// time (600 s), a brick one never; its fire is ONE spot over its footprint held on its damage record (TD-6's second holder, written the way a window is); and a
    /// flame jet on a timber wall sets it alight after 2 s, never a brick wall.
    /// </summary>
    public sealed class BuildingFireTests : IDisposable
    {
        private const long FlameMan = 0x6E01, Fuel = 0x6E02;
        private const string TwoHouses = """
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "building", "label": "Timber",
                  "building": { "footprint": [[0,0],[8,0],[8,6],[0,6]], "material": "timber",
                    "storeys": [ { "height": 2.8, "walls": [
                      { "from": [0,0], "to": [8,0], "thickness": 0.2 }, { "from": [8,0], "to": [8,6], "thickness": 0.2 },
                      { "from": [8,6], "to": [0,6], "thickness": 0.2 }, { "from": [0,6], "to": [0,0], "thickness": 0.2 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } },
              { "type": "Feature", "properties": { "kind": "building", "label": "Brick",
                  "building": { "footprint": [[0,0],[8,0],[8,6],[0,6]], "material": "brick",
                    "storeys": [ { "height": 2.8, "walls": [
                      { "from": [0,0], "to": [8,0], "thickness": 0.3 }, { "from": [8,0], "to": [8,6], "thickness": 0.3 },
                      { "from": [8,6], "to": [0,6], "thickness": 0.3 }, { "from": [0,6], "to": [0,0], "thickness": 0.3 } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 50] } } ] }
            """;

        private readonly EntityRepository _w = new();
        private readonly TerrainWorld _terrain;
        private readonly int _timber, _brick;
        private double _now = 100.0;

        public BuildingFireTests()
        {
            _terrain = TerrainWorldParser.Parse(TwoHouses, "range");
            _timber = Enumerable.Range(0, _terrain.Buildings.Count).Single(i => _terrain.Buildings[i].Label == "Timber");
            _brick = Enumerable.Range(0, _terrain.Buildings.Count).Single(i => _terrain.Buildings[i].Label == "Brick");
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<BuildingDamage>();
            _w.RegisterComponent<FireState>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<WeaponPosture>();
            _w.RegisterComponent<WeaponAmmo>();
            _w.RegisterComponent<VehicleParams>();
            _w.RegisterManagedComponent<TerrainObjectKey>();
            _w.RegisterEvent<BuildingDamageEvent>();
            _w.RegisterEvent<BuildingFireEvent>();
            _w.SetSingletonManaged(_terrain);
            var db = new TkbDatabase();
            var man = new TkbTemplate("test flamethrower man", FlameMan);
            man.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = Fuel, InitialAmmunition = 100 } } });
            db.Register(man);
            var fuel = new TkbTemplate("test flame fuel", Fuel);
            fuel.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, FlameRangeM = 40f, FlameHalfAngleDeg = 6f, FlameIntensity = 1f, BurnSeconds = 60f });
            db.Register(fuel);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            Clock();
        }

        public void Dispose() => _w.Dispose();

        private void Clock() => _w.SetSingleton(new GlobalTime { TotalTime = _now, DeltaTime = 0.1f });

        /// <summary>⭐ B-4 — what burns: the timber house its walls' time (600 s, thermal class wood), the brick house never; its fire is one spot over its
        /// footprint (centre, radius to the farthest corner).</summary>
        [Fact]
        public void CE3488_ATimberHouseBurnsItsWallsTime_ABrickHouseNever()
        {
            Assert.Equal(600f, BuildingFires.BurnSecondsOf(_terrain, _timber));
            Assert.Equal(0f, BuildingFires.BurnSecondsOf(_terrain, _brick));
            Assert.Equal(new[] { _timber }, BuildingFires.Burnable(_terrain).ToArray());
            Assert.False(BuildingFires.TryOf(_terrain, _brick, _now, 1f, out _));
            Assert.True(BuildingFires.TryOf(_terrain, _timber, _now, 1f, out var fire));
            Assert.Equal((24f, 23f, 600f), (fire.X, fire.Y, fire.Seconds));
            Assert.Equal(5f, fire.Radius, 3);   // half the 8 x 6 diagonal
        }

        /// <summary>
        /// ⭐⭐ B-1 — a building set alight gains its fire on its damage RECORD: a request born holding the <c>FireState</c> (in the record's frame — its anchor) and
        /// no breach; a second fire while it burns adds nothing. 🔴 Red-proof: drop the "already burning" test ⇒ a second spot.
        /// </summary>
        [Fact]
        public void CE3488_ABuildingSetAlight_HoldsItsFireOnItsDamageRecord_Once()
        {
            var damage = new BuildingDamageSystem();
            Assert.True(BuildingFires.TryOf(_terrain, _timber, _now, 1f, out var fire));
            _w.Bus.Publish(fire);
            _w.Bus.SwapBuffers();
            damage.Execute(_w, 0.1f);
            _w.Bus.SwapBuffers();
            var request = Assert.Single(_w.Bus.ReadManaged<EntityCreationRequest>().ToArray());
            var born = request.InitialComponents!.OfType<FireState>().Single();
            Assert.Equal(1, born.Count);
            var anchor = WallGroups.Anchor(_terrain, _timber);
            Assert.Equal(new Vector2(24f, 23f) - new Vector2(anchor.X, anchor.Y), new Vector2(born.Get(0).X, born.Get(0).Y));
            Assert.Equal(0, request.InitialComponents!.OfType<BuildingDamage>().Single().Count);   // a fire is no breach

            var e = _w.CreateEntity();   // the spawner makes it
            foreach (var c in request.InitialComponents!)
                switch (c) { case SimTransform t: _w.AddComponent(e, t); break; case BuildingDamage d: _w.AddComponent(e, d); break;
                             case FireState f: _w.AddComponent(e, f); break; case TerrainObjectKey k: _w.SetManagedComponent(e, k); break; }
            _w.Bus.Publish(fire);   // set alight again while it burns
            _w.Bus.SwapBuffers();
            damage.Execute(_w, 0.1f);
            _w.Bus.SwapBuffers();
            Assert.Empty(_w.Bus.ReadManaged<EntityCreationRequest>().ToArray());
            Assert.Equal(1, _w.GetComponentRO<FireState>(e).Count);
            Assert.Equal(1, damage.Ignited);
        }

        /// <summary>
        /// ⭐⭐ B-2 (F-3) — a flame jet held on a TIMBER wall sets the house alight after 2 s, not before; the same jet on the BRICK wall never.
        /// 🔴 Red-proof: both guards dropped — the burnable-panel filter and `TryOf`'s 0 s refusal (each alone holds) ⇒ the brick house is set alight.
        /// </summary>
        [Fact]
        public void CE3488_AFlameJetOnATimberWall_SetsTheHouseAlightAfter2s_OnABrickWallNever()
        {
            var flames = new StructureFlameSystem();
            var thrower = _w.CreateEntity();
            _w.AddComponent(thrower, new SimTransform { Position = new Vector3(5f, 23f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(thrower, new TkbIdentity { TkbType = (TkbTypeId)FlameMan });
            _w.AddComponent(thrower, new VehicleParams { Class = VehicleClass.Pedestrian });
            _w.AddComponent(thrower, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Point = new Vector3(20f, 23f, 1.2f), ActiveMount = 0 });
            _w.AddComponent(thrower, new WeaponAmmo { Count = 1, Load0 = Fuel, LastShotAt0 = double.NegativeInfinity, BurstStartedAt0 = double.NegativeInfinity });

            int Fire(double seconds)
            {
                int lit = 0;
                for (double t = 0; t < seconds - 1e-9; t += 0.1)
                {
                    _now += 0.1; Clock();
                    ref var ammo = ref _w.GetComponentRW<WeaponAmmo>(thrower);
                    if (double.IsNegativeInfinity(ammo.BurstStartedAt0)) ammo.BurstStartedAt0 = _now;
                    ammo.LastShotAt0 = _now;
                    flames.Execute(_w, 0.1f);
                    _w.Bus.SwapBuffers();
                    lit += _w.ReadEvents<BuildingFireEvent>().ToArray().Count(f => f.Building == _timber);
                    Assert.DoesNotContain(_w.ReadEvents<BuildingFireEvent>().ToArray(), f => f.Building == _brick);
                }
                return lit;
            }

            Assert.Equal(0, Fire(1.8));
            Assert.Equal(1, Fire(0.4));                                                     // 2 s of flame on the timber wall

            _w.GetComponentRW<SimTransform>(thrower).Position = new Vector3(5f, 53f, 0f);   // the same jet on the brick house
            _w.GetComponentRW<WeaponPosture>(thrower).Point = new Vector3(20f, 53f, 1.2f);
            Assert.Equal(0, Fire(4.0));
        }
    }
}
