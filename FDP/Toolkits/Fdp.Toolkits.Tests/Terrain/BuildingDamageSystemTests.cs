using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (docs/DESIGN_Terrain_Import_UTM.md §8.2 B-2, B-4, B-5) — a detonation breaches a building's wall, end to end: a burst of the
    /// reference library's warheads (<c>AreaEffectSystem.BreachWalls</c>) → <see cref="BuildingDamageEvent"/> → <see cref="BuildingDamageSystem"/> → a
    /// creation request for the building's damage entity (made, here, by this fixture standing in for the creation system) → the breach in every view's
    /// <see cref="DoorStates.Damage"/>, so sight passes it the frame after. Plus merge, capacity (a second entity), the pending list, ownership and replay.
    /// </summary>
    public sealed class BuildingDamageSystemTests : IDisposable
    {
        private const long Charge = 6101, Tank = 6102, Howitzer = 6103, Ball = 6104, Grenade = 6105, Bomb = 6106;
        private readonly EntityRepository _w = new();
        private readonly TerrainWorld _terrain = BreachFixtures.House();
        private readonly int _south;
        private readonly AreaEffectSystem _area = new();
        private readonly BuildingDamageSystem _damage = new();

        public BuildingDamageSystemTests()
        {
            _south = BreachFixtures.SouthWall(_terrain);
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<StanceIntent>();
            _w.RegisterComponent<DoorState>();
            _w.RegisterComponent<BuildingDamage>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterManagedComponent<TerrainObjectKey>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterEvent<DoorCommandEvent>();
            _w.RegisterEvent<BuildingDamageEvent>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("8 kg demolition charge", Charge) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("125mm HE-frag", Tank) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("155mm howitzer HE", Howitzer) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("5.56x45 ball", Ball));
            db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("Mk-82 bomb", Bomb) { DisType = new DISEntityType { Kind = 2 } });
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingletonManaged(_terrain);
        }

        public void Dispose() => _w.Dispose();

        // ── fixtures ─────────────────────────────────────────────────────────────────────────

        /// <summary>One detonation, assessed (<c>AreaEffectSystem</c>); the record of what it decided.</summary>
        private DetonationRecord Burst(long ammo, Vector3 at)
        {
            _w.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo });
            _w.Bus.SwapBuffers();
            long before = DetonationLog.Peek(_w)?.Count ?? 0;
            _area.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();   // the BuildingDamageEvents it published are readable now
            var log = DetonationLog.Peek(_w);
            return log != null && log.Count > before ? log.Recent(1).Single() : null!;
        }

        /// <summary>One tick of <see cref="BuildingDamageSystem"/>; the requests it published, in order.</summary>
        private List<EntityCreationRequest> Tick()
        {
            _damage.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            return _w.Bus.ReadManaged<EntityCreationRequest>().ToList();
        }

        /// <summary>The creation system, as far as this suite needs it: makes the entity a request describes, owned by this node.</summary>
        private Entity Materialise(EntityCreationRequest request)
        {
            var e = _w.CreateEntity();
            foreach (var c in request.InitialComponents!)
                switch (c)
                {
                    case SimTransform t: _w.AddComponent(e, t); break;
                    case BuildingDamage d: _w.AddComponent(e, d); break;
                    case TerrainObjectKey k: _w.SetManagedComponent(e, k); break;
                }
            return e;
        }

        private List<Entity> DamageEntities()
        {
            var list = new List<Entity>();
            foreach (var e in _w.Query().With<BuildingDamage>().Build()) list.Add(e);
            return list;
        }

        private (Vector3 From, Vector3 To) ThroughTheWallAt(float x) => (new Vector3(x, 15f, 1.0f), new Vector3(x, 25f, 1.0f));

        private bool Blocked(Vector3 a, Vector3 b) => _terrain.SegmentBlocked(a, b, DoorStates.Of(_w, _terrain));

        // ── the chain ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ A demolition charge on the south wall ⇒ ONE breach event, a creation request holding the breach and the building's key, and — once the
        /// entity is made — a line through the passage that is open the same frame. 🔴 Red-proof: drop the <c>BreachWalls</c> call in <c>Assess</c> ⇒ no
        /// record, no request.
        /// </summary>
        [Fact]
        public void ADemolitionCharge_OnTheSouthWall_MakesAPassage_AndSightPassesItOnceTheEntityIsHere()
        {
            var (from, to) = ThroughTheWallAt(25f);
            Assert.True(Blocked(from, to));                                                             // the wall is whole

            var rec = Burst(Charge, new Vector3(25f, 19.75f, 1.0f));                                    // 0.1 m off the face (the burst's stand-off)
            var record = Assert.Single(rec.BuildingBreaches);
            Assert.Contains("range/H", record);
            Assert.Contains("Passage", record);
            var ev = Assert.Single(_w.Bus.Read<BuildingDamageEvent>().ToArray());
            Assert.Equal(_south, ev.Panel); Assert.Equal(0, ev.Building);

            var requests = Tick();
            var request = Assert.Single(requests);
            Assert.Equal(BuildingDamages.TkbType, (long)request.TkbType);
            Assert.True(request.IsTransient);
            Assert.Equal("range/H", request.InitialComponents!.OfType<TerrainObjectKey>().Single().Key);
            var asked = request.InitialComponents!.OfType<BuildingDamage>().Single();
            Assert.Equal(1, asked.Count);
            Assert.Equal(BreachKind.Passage, asked.Get(0).Kind);
            Assert.True(Blocked(from, to));                                                             // nothing yet: the entity is not made

            Materialise(request);
            Assert.False(Blocked(from, to));                                                            // the line passes the moment the entity is there
            var (fromBeside, toBeside) = ThroughTheWallAt(22f);
            Assert.True(Blocked(fromBeside, toBeside));                                                 // the wall beside the passage still stops it
            Assert.Empty(rec.DoorsBreached);
        }

        /// <summary>
        /// ⭐ B-4 — a second blast overlapping the first is MERGED into the one entity (Count stays 1, the rectangle grows); a third into the same hole
        /// changes nothing and writes nothing. 🔴 Red-proof: have <c>BuildingDamages.Add</c> append instead of merging ⇒ Count is 2.
        /// </summary>
        [Fact]
        public void ASecondBlastOnTheSamePanel_IsMergedIntoTheOneEntity()
        {
            Burst(Charge, new Vector3(25f, 19.75f, 1.0f));
            Materialise(Assert.Single(Tick()));
            var e = Assert.Single(DamageEntities());
            var first = _w.GetComponentRO<BuildingDamage>(e).Get(0);

            Burst(Charge, new Vector3(25.6f, 19.75f, 1.0f));
            Assert.Empty(Tick());                                                                       // no new request: the entity exists
            var d = _w.GetComponentRO<BuildingDamage>(e);
            Assert.Equal(1, d.Count);
            Assert.True(d.Get(0).Width > first.Width + 0.3f, $"{first.Width} → {d.Get(0).Width}");
            Assert.Equal(first.From, d.Get(0).From);                                                    // grown on the east side only
            long applied = _damage.Applied;

            Burst(Charge, new Vector3(25.3f, 19.75f, 1.0f));                                            // inside what is already open
            Tick();
            Assert.Equal(applied, _damage.Applied);                                                     // nothing written
        }

        /// <summary>
        /// ⭐ B-4 — seventeen non-overlapping breaches: sixteen fill the entity, the seventeenth opens a SECOND one for the same building, and the view's table holds
        /// all seventeen. The same run exercises the pending list (the breaches that arrive while the first request is in flight wait, keyed by the building).
        /// 🔴 Red-proof: let <c>Apply</c> pick a FULL entity as the one with room (<c>d.Count &lt;= Capacity</c>) ⇒ the seventeenth is silently dropped and the table holds 16.
        /// </summary>
        [Fact]
        public void SeventeenDisjointBreaches_FillOneEntity_AndTheSeventeenthOpensASecond_WhileTheFirstRequestIsPending()
        {
            for (int i = 0; i < 17; i++)
                _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = _south, From = i * 0.5f, To = i * 0.5f + 0.3f, Bottom = 1f, Top = 1.5f, Kind = (byte)BreachKind.Hole });
            _w.Bus.SwapBuffers();

            var first = Assert.Single(Tick());                                                          // ONE request for the building; sixteen wait on it
            Assert.Equal(1, _damage.PendingCount);
            Assert.Empty(Tick());                                                                       // still in flight: nothing new is asked
            Materialise(first);

            var second = Assert.Single(Tick());                                                         // the entity is here: the 16 pending land (it holds 1 + 15), the 17th has no room
            var entities = DamageEntities();
            Assert.Equal(16, _w.GetComponentRO<BuildingDamage>(entities.Single()).Count);
            Assert.Equal(1, _damage.PendingCount);                                                      // waiting on the second entity
            Materialise(second);
            Assert.Empty(Tick());
            Assert.Equal(0, _damage.PendingCount);

            entities = DamageEntities();
            Assert.Equal(2, entities.Count);
            Assert.Equal(new[] { 1, 16 }, entities.Select(x => (int)_w.GetComponentRO<BuildingDamage>(x).Count).OrderBy(n => n).ToArray());
            Assert.Equal(17, DoorStates.Of(_w, _terrain).Damage!.Count);                                // the view unions them
        }

        /// <summary>
        /// ⭐ B-2 — only entities THIS node owns are written. A replica of the building's damage (another node's, recorded owner 2) is never touched: a breach decided
        /// here asks for an entity of its own, and the view unions both. 🔴 Red-proof: drop the ownership test in <c>OwnsDamage</c> ⇒ the replica is written and no
        /// request is made.
        /// </summary>
        [Fact]
        public void AReplicaOfAnotherNodesDamage_IsNeverWritten_ABreachDecidedHereMakesAnEntityOfItsOwn()
        {
            var other = BreachFixtures.Damage(_w, _terrain, 0, new WallBreach(_south, 8f, 9f, 1f, 1.6f, BreachKind.Hole));
            _w.AddComponent(other, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            Assert.False(BuildingDamageSystem.OwnsDamage(_w, other));

            Burst(Charge, new Vector3(25f, 19.75f, 1.0f));
            var request = Assert.Single(Tick());
            Assert.Equal(1, _w.GetComponentRO<BuildingDamage>(other).Count);                           // untouched
            Materialise(request);
            Assert.Equal(2, DoorStates.Of(_w, _terrain).Damage!.Count);                                 // both nodes' breaches
            Assert.False(Blocked(ThroughTheWallAt(25f).From, ThroughTheWallAt(25f).To));              // this node's passage
            Assert.False(Blocked(new Vector3(28.5f, 15f, 1.2f), new Vector3(28.5f, 25f, 1.2f)));      // the other node's hole (x 28 .. 29, z 1 .. 1.6)
        }

        /// <summary>
        /// ⭐ B-5 — what does NOT breach: a rifle round (no warhead), a grenade (0.24 kg — below the table's minimum), and a heavy shell far from any wall. 🔴 Red-proof:
        /// drop the <c>ExplosiveKg &lt;= 0</c> / <c>MinKg</c> test ⇒ the grenade makes a hole.
        /// </summary>
        [Fact]
        public void AKineticRound_AGrenade_AndAFarShell_BreachNothing()
        {
            Assert.Null(Burst(Ball, new Vector3(25f, 19.75f, 1.0f)));                                   // no warhead at all: no record
            Assert.Empty(Burst(Grenade, new Vector3(25f, 19.75f, 1.0f)).BuildingBreaches);
            Assert.Empty(Burst(Howitzer, new Vector3(25f, 60f, 1.0f)).BuildingBreaches);                // 40 m from the south wall
            Assert.Empty(_w.Bus.Read<BuildingDamageEvent>().ToArray());
            Assert.Empty(Tick());
            Assert.Empty(DamageEntities());
        }

        /// <summary>
        /// ⭐ B-6 — "the line clear except for the panel itself": a burst INSIDE the house against the north wall breaches that wall (and only it); a 94 kg bomb in the
        /// open against the SOUTH wall reaches the north wall too (8 m behind it) and does NOT breach it — the south wall is in the way. 🔴 Red-proof: have
        /// <c>LineClearToFace</c> return true ⇒ the bomb breaches the north wall as well.
        /// </summary>
        [Fact]
        public void AWallBehindAnotherWall_IsSpared_TheLineMustBeClear()
        {
            int north = Array.FindIndex(_terrain.Panels.ToArray(), p => MathF.Abs(p.A.Y - 28f) < 1e-3f && MathF.Abs(p.B.Y - 28f) < 1e-3f);
            var inside = Burst(Howitzer, new Vector3(25f, 26.8f, 1.5f));                                // inside, 1.05 m off the north wall's inner face
            Assert.Single(inside.BuildingBreaches);
            Assert.Contains($"panel {north} ", inside.BuildingBreaches[0]);

            var bomb = Burst(Bomb, new Vector3(25f, 19.75f, 1.5f));                                     // outside, against the south wall; 94 kg reaches the north wall at 8 m
            Assert.Contains(bomb.BuildingBreaches, b => b.Contains($"panel {_south} "));
            Assert.DoesNotContain(bomb.BuildingBreaches, b => b.Contains($"panel {north} "));
        }

        /// <summary>A replay owns the world: nothing is decided into it, nothing written. 🔴 Red-proof: drop the <c>ReplayState</c> guard in <c>BuildingDamageSystem.Execute</c> ⇒ a request is published.</summary>
        [Fact]
        public void DuringAReplay_TheSystemWritesAndRequestsNothing()
        {
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = _south, From = 4f, To = 6f, Bottom = 0f, Top = 2.2f, Kind = (byte)BreachKind.Passage });
            _w.Bus.SwapBuffers();
            Fdp.Toolkit.Replay.ReplayState.Enter(_w);
            Assert.Empty(Tick());
            Assert.Equal(0, _damage.PendingCount);
            Assert.Empty(DamageEntities());
        }

        /// <summary>
        /// B-5 — a burst asks the grid for the panels near it; it does not scan every panel (a large terrain has 100 000). The grid is a superset of the panels within
        /// reach, in ascending order, deduplicated.
        /// </summary>
        [Fact]
        public void ThePanelGrid_ReturnsTheNearbyPanelsOnly_AFractionOfTheTerrain()
        {
            var big = BigTown(30, 30);                                                                  // 900 houses, 3 600 panels, 900 m a side
            var grid = big.BuildingPanels;
            Assert.Equal(3600, grid.PanelCount);
            Assert.Same(grid, big.BuildingPanels);                                                      // built once

            var at = new Vector2(20f + 10 * 30f + 5f, 20f + 10 * 30f + 4f);                            // the middle of the house in row 10, column 10
            var near = new List<int>();
            grid.Near(at, 10f, near);
            Assert.InRange(near.Count, 4, 40);                                                          // that house's walls and a few neighbours', not 3 600
            Assert.Equal(near.OrderBy(i => i).Distinct().ToList(), near);
            for (int i = 0; i < big.Panels.Count; i++)                                                  // a superset of every panel within the radius
            {
                var p = big.Panels[i];
                float dMid = Vector2.Distance(at, (p.A + p.B) / 2f);
                if (dMid <= 5f) Assert.Contains(i, near);
            }
        }

        private static TerrainWorld BigTown(int columns, int rows)
        {
            var sb = new StringBuilder("{ \"type\": \"FeatureCollection\", \"features\": [");
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    if (r + c > 0) sb.Append(',');
                    sb.Append("{ \"type\": \"Feature\", \"properties\": { \"kind\": \"building\", \"label\": \"B").Append(r).Append('_').Append(c)
                      .Append("\", \"building\": { \"footprint\": [[0,0],[10,0],[10,8],[0,8]], \"storeys\": [ { \"height\": 3, \"walls\": [")
                      .Append("{\"from\":[0,0],\"to\":[10,0]},{\"from\":[10,0],\"to\":[10,8]},{\"from\":[10,8],\"to\":[0,8]},{\"from\":[0,8],\"to\":[0,0]}] } ] } },")
                      .Append("\"geometry\": { \"type\": \"Point\", \"coordinates\": [").Append(20 + c * 30).Append(',').Append(20 + r * 30).Append("] } }");
                }
            sb.Append("] }");
            return TerrainWorldParser.Parse(sb.ToString(), "town");
        }
    }
}
