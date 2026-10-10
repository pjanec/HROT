using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-11) — the area effect, with numbers written here: posture behind a low
    /// wall (W-6′), a vehicle shielding a soldier (W-6′ entities), a roof burst sparing the floor below (W-7′ closed barriers), the
    /// diffraction shadow (W-7′), a breached door (W-9), and what gets NO area effect (a kinetic round, a remote detonation, the struck
    /// entity). The warheads are the reference library's (M67, 81 mm) — through the same resolver the system uses.
    /// 📄 docs/DESIGN_Building_Interiors.md §3k.
    /// </summary>
    public sealed class AreaEffectSystemTests : IDisposable
    {
        private const long Grenade = 6001, Mortar = 6002, Ball = 6003;
        private readonly EntityRepository _w;
        private readonly AreaEffectSystem _sys = new();

        public AreaEffectSystemTests()
        {
            _w = NewWorld();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("81mm mortar HE", Mortar) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("5.56x45 ball", Ball));
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        /// <summary>A world with this suite's component types (a replayed world registers the recording's types the same way).</summary>
        private static EntityRepository NewWorld()
        {
            var _w = new EntityRepository();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<StanceIntent>();
            _w.RegisterComponent<DoorState>();
            _w.RegisterManagedComponent<TerrainObjectKey>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterEvent<DoorCommandEvent>();
            return _w;
        }

        public void Dispose() => _w.Dispose();

        // ── fixtures ─────────────────────────────────────────────────────────────────────────

        private Entity Soldier(float x, float y, float z = 0f, StanceId stance = StanceId.Standing)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, z), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new StanceIntent { TargetStance = stance });
            return e;
        }

        private (DetonationRecord Record, float[] Damage) Burst(long ammo, Vector3 at, params Entity[] targets)
            => Burst(ammo, at, Entity.Null, false, targets);

        private (DetonationRecord Record, float[] Damage) Burst(long ammo, Vector3 at, Entity struck, bool remote, params Entity[] targets)
        {
            _w.Bus.Publish(new DetonationNotification { Target = struck, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo, IsRemote = remote });
            _w.Bus.SwapBuffers();
            long before = DetonationLog.Peek(_w)?.Count ?? 0;
            _sys.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            var events = _w.Bus.Read<DamageAssessedEvent>().ToArray();
            var log = DetonationLog.Peek(_w);
            var record = log != null && log.Count > before ? log.Recent(1).Single() : null!;
            return (record, targets.Select(t => events.Where(d => d.HitEntity == t).Sum(d => d.TotalDamage)).ToArray());
        }

        private static TerrainPrism Wall(Vector2 min, Vector2 max, float height) => new()
        {
            Kind = TerrainPrismKind.Wall,
            Footprint = new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) },
            BaseZ = 0, TopZ = height, Min = min, Max = max,
        };

        // ── W-6′ — fragments by body point, behind a low wall ────────────────────────────────

        [Fact]
        public void W11_BehindAHalfMetreWall_AStandingManIsExposed_AProneManIsNot()
        {
            _w.SetSingletonManaged(new TerrainWorld
            {
                BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
                Prisms = new[] { Wall(new Vector2(-10, 4), new Vector2(10, 4.3f), 0.5f) },
            });
            var standing = Soldier(-1, 6);
            var prone = Soldier(1, 6, stance: StanceId.Prone);
            var (rec, damage) = Burst(Grenade, new Vector3(0, 0, 0.1f), standing, prone);

            var s = rec.Effects.Single(x => x.Entity == standing);
            var p = rec.Effects.Single(x => x.Entity == prone);
            Assert.True(s.FragmentExposure >= 0.5f, $"standing exposure {s.FragmentExposure}");   // the chest and head clear the wall
            Assert.True(p.FragmentExposure <= 0.1f, $"prone exposure {p.FragmentExposure}");      // both prone points are behind it
            Assert.Equal("Prone", p.Stance);
            Assert.Equal(2, p.BodyPoints);
            Assert.True(damage[0] > 10f * Math.Max(damage[1], 0.1f), $"standing {damage[0]} vs prone {damage[1]}");
            Assert.Equal("reference library: M67 grenade", rec.WarheadSource[..30]);

            // ⭐ CE-3117 — one ray per body point rated, in target order, for the map: the prone man's rays end in the wall (an
            //   obstacle at its near face, y = 4) and carry almost nothing; the standing man's head ray is clear.
            Assert.Equal(rec.Effects.Sum(x => x.BodyPoints), rec.Rays.Count);
            var proneRays = rec.Rays.Where(r => MathF.Abs(r.To.X - 1f) < 0.01f).ToList();
            Assert.Equal(2, proneRays.Count);
            Assert.All(proneRays, r =>
            {
                Assert.NotNull(r.StopAt);
                Assert.InRange(r.StopAt!.Value.Y, 3.9f, 4.4f);
                Assert.True(r.Transmission < 0.1f, $"prone ray transmission {r.Transmission}");
            });
            var head = rec.Rays.Where(r => MathF.Abs(r.To.X + 1f) < 0.01f).OrderBy(r => r.To.Z).Last();
            Assert.Null(head.StopAt);
            Assert.Equal(1f, head.Transmission);
            Assert.Equal(new Vector3(1, 6, 0), p.At);
        }

        // ── W-6′ — a vehicle between the burst and a soldier stops the fragments ────────────

        [Fact]
        public void W11_AVehicleBetween_ShieldsTheSoldierBehindIt_NotTheOneBeside()
        {
            var car = _w.CreateEntity();
            _w.AddComponent(car, new SimTransform { Position = new Vector3(0, 4, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(car, new PhysicsCollider { Radius = 2f, Height = 2.5f });
            var hidden = Soldier(0, 8);
            var open = Soldier(6, 6);
            var (rec, damage) = Burst(Grenade, new Vector3(0, 0, 0.1f), hidden, open);

            Assert.Equal(0f, rec.Effects.Single(x => x.Entity == hidden).FragmentExposure);
            Assert.StartsWith("entity #", rec.Effects.Single(x => x.Entity == hidden).ShieldedBy);
            // ⭐ CE-3117 — a ray a collider stops ends at the point of the ray nearest that collider (the car's centre, y = 4; a ray
            //   that climbs to the head passes nearest a little short of it).
            Assert.All(rec.Rays.Where(r => MathF.Abs(r.To.Y - 8f) < 0.01f), r =>
            {
                Assert.Equal(0f, r.Transmission);
                Assert.InRange(r.StopAt!.Value.Y, 3.5f, 4.1f);
            });
            Assert.Equal(1f, rec.Effects.Single(x => x.Entity == open).FragmentExposure);
            Assert.Equal(0f, damage[0]);    // 8 m: beyond the M67's blast, and every fragment line ends in the car
            Assert.True(damage[1] > 20f, $"open {damage[1]}");
        }

        // ── W-7′ — a roof burst spares the floor below (closed barrier), not the man on the roof ──

        [Fact]
        public void W11_AMortarOnTheRoof_SparesTheRoomBelow()
        {
            _w.SetSingletonManaged(TerrainWorldParser.Parse(OneStoreyHouse, "range"));
            var below = Soldier(25, 24, 0);
            var onRoof = Soldier(27, 24, 3);
            var (rec, damage) = Burst(Mortar, new Vector3(25, 24, 3f + AreaEffect.BurstStandOffMetres), below, onRoof);

            var b = rec.Effects.Single(x => x.Entity == below);
            Assert.True(b.BlastBarrier < 0.1f, $"barrier {b.BlastBarrier}");   // 0.2 m of concrete ≈ 5 %
            Assert.Equal(0f, b.FragmentExposure);                               // a fragment does not go through a slab
            Assert.True(damage[0] < 15f, $"below {damage[0]}");
            Assert.True(damage[1] > 100f, $"on the roof {damage[1]}");
        }

        // ── W-7′ — the diffraction shadow behind a wall taller than the target ───────────────

        [Fact]
        public void W11_BehindATallWall_TheBlastIsShadowed_AndRecoversFurtherBack()
        {
            Assert.Equal(AreaEffect.ShadowFloor, AreaEffect.ShadowFactor(2f, 0f));
            Assert.Equal(1f, AreaEffect.ShadowFactor(2f, 6f));
            Assert.InRange(AreaEffect.ShadowFactor(2f, 3f), 0.6f, 0.7f);

            _w.SetSingletonManaged(new TerrainWorld
            {
                BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50),
                Prisms = new[] { Wall(new Vector2(-10, 1.5f), new Vector2(10, 1.8f), 2.0f) },
            });
            var close = Soldier(0, 2.2f, stance: StanceId.Crouched);
            var (rec, damage) = Burst(Mortar, new Vector3(0, 0, 0.1f), close);
            var c = rec.Effects.Single();
            Assert.InRange(c.BlastShadow, AreaEffect.ShadowFloor, 0.5f);       // right behind a 2 m wall
            Assert.Equal(0f, c.FragmentExposure);                               // fragments do not bend
            Assert.True(damage[0] > 0f, "a close, large burst still hurts a hidden target");
        }

        // ── W-9 — a door in the lethal radius with a clear line is breached ─────────────────

        [Fact]
        public void W11_ALockedDoorBesideTheBurst_IsBreached()
        {
            _w.SetSingletonManaged(TerrainWorldParser.Parse(OneStoreyHouse, "range"));
            var door = _w.CreateEntity();
            _w.AddComponent(door, new DoorState { State = TerrainDoorState.Locked });
            _w.SetManagedComponent(door, new TerrainObjectKey { Key = "range/R/front" });

            var (rec, _) = Burst(Mortar, new Vector3(25, 19, 0.1f));
            Assert.Equal(new[] { "range/R/front" }, rec.DoorsBreached);
            var cmd = _w.Bus.Read<DoorCommandEvent>().ToArray();
            Assert.Contains(cmd, c => c.Door == door && c.Verb == DoorVerb.Breach);
        }

        // ── what gets NO area effect ─────────────────────────────────────────────────────────

        [Fact]
        public void W11_AKineticRound_AndARemoteDetonation_GetNoAreaEffect_TheStruckEntityIsInTheBurst()
        {
            var a = Soldier(1, 1);
            var b = Soldier(2, 0);
            Assert.Null(Burst(Ball, Vector3.Zero, a).Record);                      // no warhead
            Assert.Null(Burst(0, Vector3.Zero, a).Record);                         // unknown munition
            Assert.Null(Burst(Grenade, Vector3.Zero, Entity.Null, true, a).Record); // remote: its node assessed it
            var (rec, damage) = Burst(Grenade, new Vector3(1, 1, 0.5f), a, false, a, b);
            var struck = rec.Effects.Single(x => x.Entity == a);                    // at the burst, and never its own cover
            Assert.Equal(1f, struck.FragmentExposure);
            Assert.True(damage[0] > damage[1] && damage[1] > 0f, $"struck {damage[0]}, beside {damage[1]}");
        }

        private const string OneStoreyHouse = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,60,60],"groundZ":0},"features":[
              {"type":"Feature","properties":{"kind":"building","label":"R","doors":{"front":"locked"},"building":{
                 "footprint":[[0,0],[10,0],[10,8],[0,8]],
                 "storeys":[{"height":3,"walls":[
                      {"from":[0,0],"to":[10,0],"openings":[{"kind":"door","at":4.4,"width":1.2,"doorId":"front"}]},
                      {"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
               "geometry":{"type":"Point","coordinates":[20,20]}}]}
            """;

        // ── CE-3117 — what the map draws is RECORDED state (R-226) ───────────────────────────

        /// <summary>
        /// ⭐⭐ <c>CE-3117</c> (R-226) — a burst reaches the map through the RECORDER: the mirror copies it into the
        /// <see cref="DetonationTraces"/> singleton with a write that MOVES its version, so a delta frame after a keyframe that had no
        /// burst carries it, and a world restored from the two holds the burst, its rays and its sim time. ⛔ Writing through the ref
        /// <c>GetSingletonUnmanaged</c> returns would leave the version alone and this rail red: the delta would carry nothing.
        /// </summary>
        [Fact]
        public void CE3117_ABurst_ReachesARestoredWorld_ThroughAKeyframeAndADelta()
        {
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.5 });
            using var k0 = new System.IO.MemoryStream();
            var recorder = new Fdp.Core.FlightRecorder.RecorderSystem();
            using (var w = new System.IO.BinaryWriter(k0, System.Text.Encoding.UTF8, leaveOpen: true)) recorder.RecordKeyframe(_w, w, 0L);
            uint since = _w.GlobalVersion;
            _w.Tick();

            var hidden = Soldier(0, 8);
            var car = _w.CreateEntity();
            _w.AddComponent(car, new SimTransform { Position = new Vector3(0, 4, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(car, new PhysicsCollider { Radius = 2f, Height = 2.5f });
            var (rec, _) = Burst(Grenade, new Vector3(0, 0, 0.1f), hidden);
            CombatTraceSystem.MirrorDetonations(_w, DetonationLog.Peek(_w)!, 12.5);
            using var d1 = new System.IO.MemoryStream();
            using (var w = new System.IO.BinaryWriter(d1, System.Text.Encoding.UTF8, leaveOpen: true)) recorder.RecordDeltaFrame(_w, since, w, 0L);

            using var restored = NewWorld();
            var playback = new Fdp.Core.FlightRecorder.PlaybackSystem();
            k0.Position = 0; d1.Position = 0;
            using (var r = new System.IO.BinaryReader(k0)) playback.ApplyFrame(restored, r);
            Assert.False(restored.HasSingletonUnmanaged<DetonationTraces>() && restored.GetSingletonUnmanaged<DetonationTraces>().Count > 0,
                "the keyframe was taken before the burst");
            using (var r = new System.IO.BinaryReader(d1)) playback.ApplyFrame(restored, r);

            Assert.True(restored.HasSingletonUnmanaged<DetonationTraces>(), "the delta carried the traces");
            var t = restored.GetSingletonUnmanaged<DetonationTraces>();
            Assert.Equal(1, t.Count);
            var burst = t.SlotsRO()[0];
            Assert.Equal(12.5, burst.Time);
            Assert.Equal(new Vector3(0, 0, 0.1f), burst.Burst);
            Assert.Equal(rec.FragmentRadius, burst.FragmentRadius);
            Assert.Equal(Math.Min(rec.Rays.Count, DetonationTraces.RaysPerBurst), burst.RayCount);
            Assert.All(burst.RaysRO().ToArray(), ray => Assert.Equal(1, ray.HasStop));   // every line to the hidden man ends in the car
            Assert.Equal(12.5, restored.GetSingletonUnmanaged<GlobalTime>().TotalTime);    // the clock the gizmo ages it against
        }

        /// <summary>
        /// ⭐ <c>CE-3117</c> — a shot is mirrored while it flies (dashed on the map, no end) and its end is mirrored when it ends;
        /// a mirror with nothing new adds nothing.
        /// </summary>
        [Fact]
        public void CE3117_AShot_IsTracedInFlight_ThenItsEnd_AndAnIdleMirrorAddsNothing()
        {
            var log = ShotLog.For(_w);
            var shot = log.Add(new ShotRecord { Muzzle = new Vector3(0, 0, 1.5f), Aim = new Vector3(0, 50, 1.5f) });
            CombatTraceSystem.MirrorShots(_w, log, 3.0);
            var flying = _w.GetSingletonUnmanaged<ShotTraces>().SlotsRO()[0];
            Assert.Equal(ShotOutcome.InFlight, flying.Outcome);
            Assert.Equal(0, flying.HasEnd);
            Assert.Equal(new Vector3(0, 50, 1.5f), flying.End);

            ShotLog.End(shot, ShotOutcome.Hit, 7u, new Vector3(0, 20, 1.4f));
            CombatTraceSystem.MirrorShots(_w, log, 3.1);
            var hit = _w.GetSingletonUnmanaged<ShotTraces>().SlotsRO()[0];
            Assert.Equal(ShotOutcome.Hit, hit.Outcome);
            Assert.Equal(1, hit.HasEnd);
            Assert.Equal(new Vector3(0, 20, 1.4f), hit.End);
            Assert.Equal(3.0, hit.Time);   // stamped when it was fired, not when it ended

            CombatTraceSystem.MirrorShots(_w, log, 3.2);   // nothing new: no second slot
            Assert.Equal(1, _w.GetSingletonUnmanaged<ShotTraces>().Count);
            Assert.Equal(1L, _w.GetSingletonUnmanaged<ShotTraces>().NextSeq);
        }
    }
}
