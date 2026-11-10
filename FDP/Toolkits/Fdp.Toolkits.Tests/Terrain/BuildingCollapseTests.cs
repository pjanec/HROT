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
    /// ⭐⭐ <c>CE-4391</c> S-2 (docs/DESIGN_Terrain_Import_UTM.md §8 D-7, §8.2 B-9) — a heavy bomb COLLAPSES a building, end to end: <c>AreaEffectSystem</c> decides it (and kills
    /// the people inside through the blast's own damage path), <see cref="BuildingDamageSystem"/> writes <c>Collapsed</c> into this node's entity, and every view's
    /// <see cref="DoorStates.Damage"/> then shows the mound. The terrain's <c>collapseMinKg</c> is lowered to 90 kg here: no shipped munition reaches the default 200 kg.
    /// </summary>
    public sealed class BuildingCollapseTests : IDisposable
    {
        private const long Charge = 6101, Tank = 6102, Howitzer = 6103, Ball = 6104, Grenade = 6105, Bomb = 6106;
        private readonly EntityRepository _w = new();
        private readonly TerrainWorld _terrain = BreachFixtures.HouseCollapsingAt(90f);   // the reference library's heaviest bomb is 94 kg
        private readonly int _south;
        private readonly AreaEffectSystem _area = new();
        private readonly BuildingDamageSystem _damage = new();

        public BuildingCollapseTests()
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

        private static List<DamageAssessedEvent> Damage(EntityRepository w) => w.Bus.Read<DamageAssessedEvent>().ToArray().ToList();

        private Entity Man(float x, float y)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            return e;
        }

        /// <summary>
        /// ⭐ A 94 kg bomb on the house's roof collapses it: ONE collapse event, the man inside takes lethal damage through <see cref="DamageAssessedEvent"/> (the blast's own path)
        /// in the SAME assessment, a man far outside takes none, no breach is added for the collapsed building, the entity is made born collapsed, and sight across the former
        /// interior passes above the mound. 🔴 Red-proof: drop the <c>CollapseBuildings</c> call ⇒ no collapse record and no lethal event; drop the footprint test on the casualty
        /// loop ⇒ the man outside is hit.
        /// </summary>
        [Fact]
        public void AHeavyBombOnTheRoof_CollapsesTheBuilding_KillsTheManInside_AndSparesTheManFarOutside()
        {
            var inside = Man(25f, 24f);
            var outside = Man(25f, 5000f);   // beyond the 94 kg blast reach (a few hundred metres)
            var rec = Burst(Bomb, new Vector3(25f, 24f, 3f + AreaEffect.BurstStandOffMetres));
            Assert.Single(rec.BuildingBreaches, r => r.Contains("COLLAPSED"));
            Assert.DoesNotContain(rec.BuildingBreaches, r => r.Contains(" panel "));                  // a collapsed building's breaches are moot
            var events = _w.Bus.Read<BuildingDamageEvent>().ToArray();
            var collapse = Assert.Single(events);
            Assert.Equal(1, collapse.Collapse); Assert.Equal(0, collapse.Building);

            var dmg = Damage(_w);
            Assert.True(dmg.Where(d => d.HitEntity == inside).Sum(d => d.TotalDamage) >= 200f, "the man inside takes at least twice his health");
            Assert.Empty(dmg.Where(d => d.HitEntity == outside));

            var request = Assert.Single(Tick());
            var asked = request.InitialComponents!.OfType<BuildingDamage>().Single();
            Assert.Equal(1, asked.Collapsed); Assert.Equal(0, asked.Count);
            Assert.Equal(BuildingDamages.TkbType, (long)request.TkbType);
            var low = (From: new Vector3(15f, 24f, 0.5f), To: new Vector3(35f, 24f, 0.5f));
            var high = (From: new Vector3(15f, 24f, 1.6f), To: new Vector3(35f, 24f, 1.6f));
            Assert.True(Blocked(high.From, high.To));                                                   // the walls, until the entity is here
            Materialise(request);
            Assert.True(Blocked(low.From, low.To));
            Assert.False(Blocked(high.From, high.To));

            long applied = _damage.Applied;
            Burst(Bomb, new Vector3(25f, 24f, 3f));                                                     // a second bomb on the rubble: not collapsed twice
            Assert.Empty(Tick());
            Assert.Equal(applied, _damage.Applied);
        }

        /// <summary>
        /// ⭐ A 155 mm (6.6 kg) shell does NOT collapse the building — it breaches a wall as before — and the man inside is hurt only by the shell's own blast. 🔴 Red-proof: set
        /// the mass guard in <c>AreaEffectSystem</c> AND in <c>BreachRule.Collapses</c> ⇒ the shell collapses it.
        /// </summary>
        [Fact]
        public void AHowitzerShell_DoesNotCollapse_ItBreachesAWall()
        {
            var rec = Burst(Howitzer, new Vector3(25f, 19.75f, 1.5f));
            Assert.DoesNotContain(rec.BuildingBreaches, r => r.Contains("COLLAPSED"));
            Assert.Contains(rec.BuildingBreaches, r => r.Contains($"panel {_south} "));
            Assert.All(_w.Bus.Read<BuildingDamageEvent>().ToArray(), e => Assert.Equal(0, e.Collapse));
        }

        /// <summary>A collapse decided while the building's first creation request is in flight waits on it and is applied when the entity appears (the pending path).</summary>
        [Fact]
        public void ACollapseArrivingWhileAnEntityIsPending_IsAppliedWhenItAppears_AndASetFlagIsWrittenOnce()
        {
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = _south, From = 4f, To = 6f, Bottom = 1f, Top = 1.5f, Kind = (byte)BreachKind.Hole });
            _w.Bus.SwapBuffers();
            var first = Assert.Single(Tick());                                                          // a breach asks for the entity
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = -1, Collapse = 1 });
            _w.Bus.SwapBuffers();
            Assert.Empty(Tick());                                                                       // waits
            var e = Materialise(first);
            Assert.Empty(Tick());                                                                       // the entity is here: the collapse is written into it
            Assert.Equal(1, _w.GetComponentRO<BuildingDamage>(e).Collapsed);
            Assert.Equal(1, _w.GetComponentRO<BuildingDamage>(e).Count);
            Assert.Equal(0, _damage.PendingCount);
            long applied = _damage.Applied;
            _w.Bus.Publish(new BuildingDamageEvent { Building = 0, Panel = -1, Collapse = 1 });
            _w.Bus.SwapBuffers();
            Tick();
            Assert.Equal(applied, _damage.Applied);                                                     // already collapsed: nothing written
        }
    }
}
