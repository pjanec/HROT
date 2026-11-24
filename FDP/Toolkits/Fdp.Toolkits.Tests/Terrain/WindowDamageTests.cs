using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.World;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-5033</c> GW-2 (📄 docs/DESIGN_Glass_Windows.md D-3, D-4, D-7, D-8, D-9, D-10) — the window state and its rules: two rounds crack a pane (their
    /// u, v kept), the third breaks it; a man firing out breaks it at once; a grenade's fragments (with a clear line) or a big blast's pressure (through walls)
    /// shatter it; a breach over it breaks it silently; a broken pane is skipped by every trace of the view, and nothing of it touches the breach table
    /// (no navmesh rebake); saved with the building's damage and read back.
    /// </summary>
    public sealed class WindowDamageTests : IDisposable
    {
        private const long Grenade = 6105, Bomb = 6106;
        private readonly EntityRepository _w = new();
        private readonly TerrainWorld _terrain;
        private readonly int _south;
        private readonly AreaEffectSystem _area = new();
        private readonly BuildingDamageSystem _damage = new();

        // the brick house of BreachFixtures with a glazed window in its south wall: x 22 .. 23.2, z 0.9 .. 2.1; the house never collapses
        private const string WindowHouse = """
            { "type": "FeatureCollection", "features": [ { "type": "Feature",
                "properties": { "kind": "building", "label": "H",
                  "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                    "storeys": [ { "height": 3, "walls": [
                      { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick", "openings": [ { "kind": "window", "at": 2, "width": 1.2 } ] },
                      { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                      { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                      { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
                "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
            """;

        private static readonly Vector3 Outside = new(22.6f, 15f, 1.5f), Inside = new(22.6f, 25f, 1.5f);

        public WindowDamageTests()
        {
            _terrain = TerrainWorldParser.Parse(WindowHouse, "range", new TerrainAssets { Breaches = BreachTable.Shared.WithOverrides("{ \"collapseMinKg\": 1000000 }", "test") });
            _south = BreachFixtures.SouthWall(_terrain);
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<StanceIntent>();
            _w.RegisterComponent<DoorState>();
            _w.RegisterComponent<BuildingDamage>();
            _w.RegisterComponent<WindowDamage>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterManagedComponent<TerrainObjectKey>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterEvent<DoorCommandEvent>();
            _w.RegisterEvent<BuildingDamageEvent>();
            _w.RegisterEvent<WindowHitEvent>();
            _w.RegisterEvent<WindowBrokenEvent>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("Mk-82 bomb", Bomb) { DisType = new DISEntityType { Kind = 2 } });
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingletonManaged(_terrain);
        }

        public void Dispose() => _w.Dispose();

        // ── fixtures ─────────────────────────────────────────────────────────────────────────

        private List<EntityCreationRequest> Tick()
        {
            _damage.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            return _w.Bus.ReadManaged<EntityCreationRequest>().ToList();
        }

        private List<EntityCreationRequest> Hit(byte shatter = 0, float u = 0.5f, float v = 0.5f)
        {
            _w.Bus.Publish(new WindowHitEvent { Building = 0, Panel = _south, Opening = 0, U = u, V = v, Shatter = shatter });
            _w.Bus.SwapBuffers();
            return Tick();
        }

        private Entity Materialise(EntityCreationRequest request)
        {
            var e = _w.CreateEntity();
            foreach (var c in request.InitialComponents!)
                switch (c)
                {
                    case SimTransform t: _w.AddComponent(e, t); break;
                    case BuildingDamage d: _w.AddComponent(e, d); break;
                    case WindowDamage d: _w.AddComponent(e, d); break;
                    case TerrainObjectKey k: _w.SetManagedComponent(e, k); break;
                }
            return e;
        }

        private WindowEntry Entry(Entity e) => Assert.Single(_w.GetComponentRO<WindowDamage>(e).ToArray());

        private bool PaneInTheWay() => _terrain.QueryFire(Outside, Inside, DoorStates.Of(_w, _terrain)).Any(c => c.Kind == TerrainWorld.PaneKind);

        private int Broken() => _w.Bus.Read<WindowBrokenEvent>().ToArray().Length;

        private void Burst(long ammo, Vector3 at)
        {
            _w.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo });
            _w.Bus.SwapBuffers();
            _area.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
        }

        // ── the record's rule ────────────────────────────────────────────────────────────────

        [Fact]
        public void TwoHits_Crack_TheThirdBreaks_AndDropsTheCracks()
        {
            var d = new WindowDamage { Building = 0 };
            Assert.Equal(WindowChange.Cracked, WindowDamages.Hit(ref d, 5, 0, 0.25f, 0.75f));
            Assert.Equal(WindowChange.Cracked, WindowDamages.Hit(ref d, 5, 0, 1f, 0f));
            var w = d.Get(0);
            Assert.Equal((byte)2, w.Hits);
            Assert.Equal((64, 191, 255, 0), ((int)w.U0, (int)w.V0, (int)w.U1, (int)w.V1));
            Assert.Equal(WindowChange.Broke, WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f));
            Assert.True(d.Get(0).Broken);
            Assert.Equal(new WindowEntry(5, 0, WindowDamages.BrokenHits, 0, 0, 0, 0), d.Get(0));
            Assert.Equal(WindowChange.Unchanged, WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f));
            Assert.Equal(WindowChange.Unchanged, WindowDamages.Break(ref d, 5, 0));
        }

        [Fact]
        public void AFullRecord_EvictsTheOldestCrackedWindow_NeverABrokenOne()
        {
            var d = new WindowDamage { Building = 0 };
            WindowDamages.Break(ref d, 0, 0);                                     // the oldest: broken, never evicted
            for (int i = 1; i < WindowDamage.Capacity; i++) WindowDamages.Hit(ref d, i, 0, 0.5f, 0.5f);
            Assert.Equal(WindowDamage.Capacity, d.Count);
            Assert.Equal(WindowChange.Cracked, WindowDamages.Hit(ref d, 999, 0, 0.5f, 0.5f));
            Assert.Equal(-1, d.IndexOf(1, 0));                                     // the oldest CRACKED one went
            Assert.True(d.Get(d.IndexOf(0, 0)).Broken);
            Assert.True(d.IndexOf(999, 0) >= 0);

            var full = new WindowDamage { Building = 0 };
            for (int i = 0; i < WindowDamage.Capacity; i++) WindowDamages.Break(ref full, i, 0);
            Assert.Equal(WindowChange.Full, WindowDamages.Hit(ref full, 999, 0, 0.5f, 0.5f));
        }

        /// <summary>⭐ <c>CE-11051</c> — each window keeps the time of its LAST hit (a crack or the break); a hit on a broken pane changes nothing, so moves nothing;
        /// an eviction carries every survivor's time with it. 🔴 Red-proof: drop the time's shift in <c>MakeRoom</c> ⇒ the survivors read their neighbours' times.</summary>
        [Fact]
        public void CE11051_EachWindow_KeepsItsLastHitsTime_ThroughAnEviction()
        {
            var d = new WindowDamage { Building = 0 };
            WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f, 1d);
            WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f, 2d);
            Assert.Equal(2d, d.HitAtOf(0).Seconds);
            WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f, 3d);                       // breaks
            Assert.Equal(3d, d.HitAtOf(0).Seconds);
            Assert.Equal(WindowChange.Unchanged, WindowDamages.Hit(ref d, 5, 0, 0.5f, 0.5f, 4d));
            Assert.Equal(3d, d.HitAtOf(0).Seconds);                                // nothing changed: no new time

            var full = new WindowDamage { Building = 0 };
            WindowDamages.Break(ref full, 0, 0, 100d);
            for (int i = 1; i < WindowDamage.Capacity; i++) WindowDamages.Hit(ref full, i, 0, 0.5f, 0.5f, 100d + i);
            WindowDamages.Hit(ref full, 999, 0, 0.5f, 0.5f, 500d);                 // evicts window 1 (the oldest cracked)
            for (int i = 0; i < full.Count; i++)
            {
                var w = full.Get(i);
                double want = w.Panel == 999 ? 500d : 100d + w.Panel;
                Assert.Equal(want, full.HitAtOf(i).Seconds);
            }
        }

        /// <summary>⭐ <c>CE-11051</c> — the writer stamps a window hit with the sim time of the frame it writes (born so on a new record, moved on the next hit).</summary>
        [Fact]
        public void CE11051_TheWriter_StampsAHit_WithTheSimTime()
        {
            _w.SetSingleton(new GlobalTime { TotalTime = 7d });
            var e = Materialise(Assert.Single(Hit()));
            Assert.Equal(7d, _w.GetComponentRO<WindowDamage>(e).HitAtOf(0).Seconds);
            _w.SetSingleton(new GlobalTime { TotalTime = 9d });
            Hit();
            Assert.Equal(9d, _w.GetComponentRO<WindowDamage>(e).HitAtOf(0).Seconds);
        }

        // ── the writer, end to end ───────────────────────────────────────────────────────────

        [Fact]
        public void ThreeRounds_TheFirstAsksForTheEntity_TheThirdBreaksThePane_AndTracesPassIt()
        {
            Assert.True(PaneInTheWay());
            var request = Assert.Single(Hit(u: 0.25f, v: 0.75f));
            Assert.Equal("range/H", request.InitialComponents!.OfType<TerrainObjectKey>().Single().Key);
            Assert.Equal(0, request.InitialComponents!.OfType<BuildingDamage>().Single().Count);   // no breach — only a window
            var e = Materialise(request);
            Assert.Equal((byte)1, Entry(e).Hits);
            Assert.Equal((byte)64, Entry(e).U0);

            Assert.Empty(Hit());
            Assert.Equal((byte)2, Entry(e).Hits);
            Assert.True(PaneInTheWay());                                           // cracked: still glass
            Assert.Equal(0, Broken());

            Assert.Empty(Hit());
            Assert.True(Entry(e).Broken);
            Assert.Equal(1, Broken());                                             // the shatter is heard
            Assert.False(PaneInTheWay());                                          // broken: an open hole again
            Assert.Null(DoorStates.Of(_w, _terrain).Damage);                       // ⭐ D-7 — no breach, no navmesh hash moved
        }

        [Fact]
        public void AShatter_BreaksAnIntactPane_AtOnce()
        {
            var e = Materialise(Assert.Single(Hit(shatter: 1)));
            Assert.True(Entry(e).Broken);
            Assert.False(PaneInTheWay());
        }

        [Fact]
        public void AnEntityMadeBeforeWindows_GainsThemOnItsFirstHit()
        {
            var e = BreachFixtures.Damage(_w, _terrain, 0);                         // a damage entity with no WindowDamage
            Assert.Empty(Hit());
            Assert.True(_w.HasComponent<WindowDamage>(e));
            Assert.Equal((byte)1, Entry(e).Hits);
        }

        [Fact]
        public void ABreachOverTheWindow_BreaksIt_Silently()
        {
            var e = BreachFixtures.Damage(_w, _terrain, 0);
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = _south, From = 1.5f, To = 3.5f, Bottom = 0f, Top = 2.5f, Kind = (byte)BreachKind.Passage });
            _w.Bus.SwapBuffers();
            Tick();
            Assert.True(Entry(e).Broken);
            Assert.Equal(0, Broken());                                             // the breach's debris is the effect, not the glass
        }

        [Fact]
        public void ABreachBesideTheWindow_LeavesIt()
        {
            var e = BreachFixtures.Damage(_w, _terrain, 0);
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = _south, From = 5f, To = 7f, Bottom = 0f, Top = 2.5f, Kind = (byte)BreachKind.Passage });
            _w.Bus.SwapBuffers();
            Tick();
            Assert.Equal(0, _w.HasComponent<WindowDamage>(e) ? _w.GetComponentRO<WindowDamage>(e).Count : 0);
        }

        // ── a round, a burst ─────────────────────────────────────────────────────────────────

        [Fact]
        public void ARound_ReportsThePaneItPassed_WhereItCrossed()
        {
            var q = TerrainWorldQuery.ForView(_w, _terrain);
            var panes = new List<PaneHit>();
            float dmg = 100f, pen = 5f;
            Assert.False(TerrainPenetration.Carry(q, Outside, Inside, ref dmg, ref pen, out _, null, null, panes));
            var hit = Assert.Single(panes);
            Assert.Equal((_south, 0), _terrain.PaneOf(hit.Piece - _terrain.Prisms.Count));
            var (u, v) = WindowDamages.PaneUv(_terrain, _south, 0, hit.At);
            Assert.Equal(0.5f, u, 2);
            Assert.Equal(0.5f, v, 2);
        }

        [Fact]
        public void AGrenade_FourMetresOutside_ShattersThePane_ByItsFragments()
        {
            Burst(Grenade, new Vector3(22.6f, 15.5f, 1.0f));   // beyond its pressure radius (≈ 3.4 m), within its fragments' (8 m), a clear line
            var hit = Assert.Single(_w.Bus.Read<WindowHitEvent>().ToArray());
            Assert.Equal((0, _south, 0, (byte)1), (hit.Building, hit.Panel, hit.Opening, hit.Shatter));
        }

        [Fact]
        public void AGrenade_BehindABrickWall_SparesThePane()
        {
            // west of the house, 4.6 m from the pane — beyond the grenade's pressure radius (≈ 3.4 m), its fragments' line through the west wall
            Burst(Grenade, new Vector3(18f, 20.5f, 1.0f));
            Assert.Empty(_w.Bus.Read<WindowHitEvent>().ToArray());
        }

        [Fact]
        public void ABomb_TwentyMetresAway_ShattersThePane_ThroughWalls()
        {
            Burst(Bomb, new Vector3(25f, 40f, 1.0f));   // 20 m north of the pane: the house's own walls stand between (pressure ≈ 27 m)
            Assert.Single(_w.Bus.Read<WindowHitEvent>().ToArray(), h => h.Shatter == 1 && h.Panel == _south);
        }

        // ── the save ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheWindows_AreSavedWithTheBuildingsDamage_AndReadBack()
        {
            var e = BreachFixtures.Damage(_w, _terrain, 0);
            var d = new WindowDamage { Building = 0 };
            WindowDamages.Hit(ref d, _south, 0, 0.25f, 0.75f);
            _w.AddComponent(e, d);
            var section = TerrainObjectsSection.Write(_w)!;
            var doc = new System.Text.Json.Nodes.JsonObject { [TerrainObjectsSection.Name] = section };
            var saved = TerrainObjectsSection.ReadBuildingDamage(doc)["range/H"];
            var w = Assert.Single(saved.Windows);
            Assert.Equal(new WindowEntry(BuildingPanelIndex.ToLocal(_terrain, _south), 0, 1, 64, 191, 0, 0), w);

            var loaded = BuildingDamageLoad.WindowsOf(_terrain, 0, saved);
            Assert.Equal(d.Get(0), loaded.Get(0));
            Assert.Single(BuildingDamageLoad.Resolve(_terrain, new Dictionary<string, SavedBuildingDamage> { ["range/H"] = saved }, null));   // a window alone still makes an entity
        }
    }
}
