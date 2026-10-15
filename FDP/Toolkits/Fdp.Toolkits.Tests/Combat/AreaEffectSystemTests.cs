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

        // ── ⭐ M1 (DESIGN_Armour_Hit_Model §3) — a burst ABOVE a hull meets its roof ─────────────

        /// <summary>
        /// ⭐ M1 — a grenade bursting just ABOVE a hull meets its thin roof (0.3 mm: the fragments defeat it); the same grenade
        /// bursting beside it meets the 50 mm side (they cannot). ⛔ Before M1 the face was horizontal, so a roof burst read the
        /// side (or the front, straight above).
        /// </summary>
        [Fact]
        public void M1_ABurstAboveAHull_MeetsItsRoof()
        {
            const long Hull = 777;
            var db = (TkbDatabase)_w.GetSingletonManaged<ITkbDatabase>();
            var t = new TkbTemplate("roof test hull", Hull);
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 1000, ArmorFront = 50, ArmorSide = 50, ArmorRear = 50, ArmorTop = 0.3f });
            db.Register(t);
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>())
                _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var hull = _w.CreateEntity();
            _w.AddComponent(hull, new SimTransform { Position = new Vector3(0, 4, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(hull, new PhysicsCollider { Radius = 2f, Height = 2.5f });
            _w.AddComponent(hull, new Health { Current = 1000f, Max = 1000f });
            _w.AddComponent(hull, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Hull });

            var (above, dAbove) = Burst(Grenade, new Vector3(0.3f, 4, 3.0f), hull);
            var (beside, dBeside) = Burst(Grenade, new Vector3(3.0f, 4, 0.1f), hull);
            Assert.Equal(1f, above.Effects.Single(x => x.Entity == hull).FragmentArmourChance);
            Assert.True(dAbove[0] > 0f, $"above {dAbove[0]}");
            Assert.Equal(0f, beside.Effects.Single(x => x.Entity == hull).FragmentArmourChance);
            Assert.Equal(0f, dBeside[0]);
        }

        // ── ⭐ R-2019 (D-M1 + D-M2; DESIGN_Armour_Hit_Model §7.5) — area effects against vehicles ──────────────

        private const long Bomb = 6020, Pickup = 778, Mbt = 779;

        private Entity Hull(long type, float x, float y, float health, float radius, float height)
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>())
                _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new PhysicsCollider { Radius = radius, Height = height });
            _w.AddComponent(e, new Health { Current = health, Max = health });
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });
            return e;
        }

        private void VehicleTypes()
        {
            var db = (TkbDatabase)_w.GetSingletonManaged<ITkbDatabase>();
            db.Register(new TkbTemplate("Mk-82 bomb", Bomb) { DisType = new DISEntityType { Kind = 2 } });
            var pickup = new TkbTemplate("blast test pickup", Pickup);
            pickup.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 300, ArmorFront = 5, ArmorSide = 5, ArmorRear = 5, ArmorTop = 3, BlastSusceptibility = 1f });
            db.Register(pickup);
            var mbt = new TkbTemplate("blast test MBT", Mbt);
            mbt.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 3000, ArmorFront = 600, ArmorSide = 350, ArmorRear = 200, ArmorTop = 60, BlastSusceptibility = 0f });
            db.Register(mbt);
        }

        /// <summary>
        /// ⭐⭐ R-2019 (AE1 + AE2, acceptance ①–③) — a Mk-82 bursting 20 m from a pickup in the open WRECKS it (its 16 g fragments go through
        /// the 5 mm sheet, and the blast reaches the hull); the same pickup 20 m away BEHIND a building takes no fragments and only the blast
        /// the walls pass (the closed-barrier rule a person gets); an MBT 15 m away takes nothing — no blast (susceptibility 0) and fragments
        /// that cannot pass 350 mm. ⛔ Before: the pickup took 0 (one 0.5 g fragment ≈ 2.4 mm against 5 mm; blast skipped every hull).
        /// ✅ Red-proof: blast back to "personnel only" ⇒ the open pickup's BlastDamage 0 ⇒ red; the fragment mass back to 0.5 g ⇒ its
        /// FragmentArmourChance 0 ⇒ red.
        /// </summary>
        [Fact]
        public void AE2_AMk82_WrecksAPickupInTheOpen_LessBehindABuilding_AndLeavesAnMbtUnhurt()
        {
            VehicleTypes();
            _w.SetSingletonManaged(TerrainWorldParser.Parse(OneStoreyHouse, "range"));   // the house spans x 20–30, y 20–28
            var open = Hull(Pickup, 45f, 12f, 300f, 2f, 1.9f);
            var behind = Hull(Pickup, 25f, 32f, 300f, 2f, 1.9f);
            var tank = Hull(Mbt, 25f, -3f, 3000f, 3f, 2.4f);
            var (rec, damage) = Burst(Bomb, new Vector3(25f, 12f, 0.1f), open, behind, tank);

            var o = rec.Effects.Single(x => x.Entity == open);
            var b = rec.Effects.Single(x => x.Entity == behind);
            var t = rec.Effects.Single(x => x.Entity == tank);
            Assert.Equal(1f, o.FragmentArmourChance);                 // AE1: the bomb's fragments defeat the 5 mm sheet
            Assert.True(o.BlastDamage > 30f, $"open blast {o.BlastDamage}");   // AE2: the blast reaches a hull (0.4 of 150 at 20 m)
            Assert.True(damage[0] >= 300f, $"the open pickup took {damage[0]} of 300");
            Assert.Equal(1f, o.BlastSusceptibility);

            Assert.Equal(0f, b.FragmentExposure);                     // the house stops the fragments
            Assert.True(b.BlastBarrier < 0.1f, $"barrier {b.BlastBarrier}");   // the walls pass a few per cent of the blast
            Assert.True(b.BlastDamage < 0.2f * o.BlastDamage, $"behind {b.BlastDamage} vs open {o.BlastDamage}");
            Assert.True(damage[1] < damage[0]);

            Assert.Equal(0f, t.BlastSusceptibility);
            Assert.Equal(0f, t.BlastDamage);
            Assert.Equal(0f, t.FragmentArmourChance);                 // 13.6 mm fragments against 350 mm
            Assert.Equal(0f, damage[2]);
        }

        /// <summary>
        /// ⭐⭐ AE5 (HANDOFF_Behaviors_To_Backend_Fragments_On_Hulls; DESIGN_Armour_Hit_Model §7.7) — a hull counts the fragments that STRIKE
        /// it, each one round's damage: a pickup in the open 20 m from a Mk-82 is still destroyed; one at 120 m is HOLED, not destroyed
        /// (health left, above the 60 % mobility kill); an MBT is unhurt. The live measurement had 1 253 at 121 m (the person dose).
        /// ✅ Red-proof (run): the hull rule removed (the dose for every target) ⇒ the far pickup takes &gt; 300 ⇒ red.
        /// </summary>
        [Fact]
        public void AE5_AMk82_DestroysAPickupAt20m_OnlyHolesOneAt120m_AndLeavesAnMbtUnhurt()
        {
            VehicleTypes();
            var near = Hull(Pickup, 20f, 0f, 300f, 2.65f, 1.9f);
            var far = Hull(Pickup, 0f, 120f, 300f, 2.65f, 1.9f);
            var tank = Hull(Mbt, -20f, 0f, 3000f, 3f, 2.4f);
            var (rec, damage) = Burst(Bomb, new Vector3(0f, 0f, 0.1f), near, far, tank);

            Assert.True(damage[0] >= 300f, $"the near pickup took {damage[0]} of 300");
            Assert.True(damage[1] > 0f && damage[1] < 120f, $"the far pickup took {damage[1]}: holed, not destroyed, still mobile (60 % of 300)");
            Assert.Equal(0f, damage[2]);
            var f = rec.Effects.Single(x => x.Entity == far);
            Assert.Equal(1f, f.FragmentArmourChance);   // the fragments still defeat 5 mm — fewer of them strike
        }

        // ── ⭐⭐ CE-3203 (Building Interiors §3k.1, W-6″) — ONE fragment law, checked against the published radii ──────────

        /// <summary>
        /// ⭐⭐ CE-3203 — the numbers a SOLDIER takes are realistic: for every fragmenting round of the reference library, a standing man in
        /// the open takes a killing dose at the round's published LETHAL radius, a wounding one at its CASUALTY radius, and next to nothing at
        /// twice that; a prone man takes far less than a standing one. 🔒 User: <i>"No unrealistic numbers for soldier hitting."</i>
        /// ⛔ Before (the dose): the Mk-82's man took 1 253 at 121 m and a 155 mm's man ≈ 0 inside its 100 m casualty radius (the linear fade).
        /// The radii are public figures (FM 3-09 / FM 3-22.90 / TM 43-0001-28 class tables, rounded); a man has 100 health.
        /// ✅ Red-proof (run): a man takes a bullet's per-hit damage (no wound law) ⇒ 10 of 11 rounds fall below 30 at their lethal radius
        /// ⇒ red; a man back on the old dose ⇒ all 11 red. ⚠ The drag alone is NOT pinned here: removing it moves a man by ≤ 40 % inside
        /// twice the casualty radius, which the bands allow (measured).
        /// </summary>
        [Theory]
        [InlineData("M67 grenade", 5f, 15f)]
        [InlineData("60mm mortar HE", 15f, 28f)]
        [InlineData("81mm mortar HE", 25f, 38f)]
        [InlineData("120mm mortar HE", 40f, 75f)]
        [InlineData("105mm howitzer HE", 30f, 55f)]
        [InlineData("155mm howitzer HE", 50f, 100f)]
        [InlineData("Mk-82 bomb", 60f, 150f)]
        [InlineData("70mm rocket HE", 15f, 45f)]
        [InlineData("125mm HE-frag", 25f, 55f)]
        [InlineData("PG-7V HEAT", 2f, 5f)]   // ⭐ a shaped charge fragments only its share of the body (before: 1 014 at 5 m)
        public void W6pp_AStandingMan_IsHitAsThePublishedRadiiSay(string round, float lethalM, float casualtyM)
        {
            Assert.True(Fdp.Toolkit.Tkb.Reference.ReferenceLibrary.Shared.TryGetAmmo(round, out var ammo));
            var w = ammo.Warhead!.Dto;
            Assert.True(w.FragmentCount > 0f && w.FragmentWoundDamage > 0f && w.FragmentDragM > 0f, $"{round}: the law's inputs");
            float Man(float r, Fdp.Toolkit.Tkb.Domain.StanceId stance = Fdp.Toolkit.Tkb.Domain.StanceId.Standing)
                => AreaEffect.Fragments(w, AreaEffect.FragmentTarget.Person, AreaEffect.PersonArea(stance), r, 0f, 1f)!.Value.Damage;

            Assert.True(Man(lethalM) >= 30f, $"{round}: a man at the lethal radius {lethalM} m took {Man(lethalM):0.#}");
            Assert.InRange(Man(casualtyM), 5f, 60f);
            Assert.True(Man(2f * casualtyM) < 10f, $"{round}: a man at {2f * casualtyM} m took {Man(2f * casualtyM):0.#}");
            Assert.True(Man(casualtyM, Fdp.Toolkit.Tkb.Domain.StanceId.Prone) < 0.3f * Man(casualtyM), $"{round}: prone vs standing");
            // the hazard radius is where a standing man's expected damage falls below 1 — beyond it, nothing
            Assert.InRange(Man(w.FragmentRadiusM - 0.5f), 0.9f, 2f);
            Assert.Equal(0f, Man(w.FragmentRadiusM + 0.1f));
        }

        /// <summary>
        /// ⭐⭐ CE-3203 — the live case the user flagged, through the system: a soldier standing in the open 121 m from a Mk-82 is WOUNDED,
        /// not shredded (the dose gave 1 253); one 20 m away is killed. ✅ Red-proof (run): the person on the dose ⇒ the far man &gt; 100 ⇒ red.
        /// </summary>
        [Fact]
        public void W6pp_AMk82_KillsAManAt20m_OnlyWoundsOneAt121m()
        {
            VehicleTypes();
            var near = Soldier(20f, 0f);
            var far = Soldier(0f, 121f);
            var (rec, damage) = Burst(Bomb, new Vector3(0f, 0f, 0.1f), near, far);
            Assert.True(damage[0] >= 100f, $"the man at 20 m took {damage[0]}");
            Assert.InRange(damage[1], 1f, 60f);
            var f = rec.Effects.Single(x => x.Entity == far);
            Assert.Equal(AreaEffect.FragmentTarget.Person, f.Target);
            Assert.InRange(f.FragmentHits, 0.05f, 1.5f);   // a man that far is struck by about one fragment
        }

        // ── ⭐⭐ CE-3203 (DESIGN_Weapon_Systems §5b) — a weapon is broken by fragments and blast, configured per type ─────────

        private const long WsM2 = 600, WsJavelin = 603;

        /// <summary>The two weapon types, as BdcTkbCatalog states them (§5b): area m², casing mm, health.</summary>
        private void WeaponTypes()
        {
            var db = (TkbDatabase)_w.GetSingletonManaged<ITkbDatabase>();
            var m2 = new TkbTemplate("break test M2", WsM2);
            m2.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 80, ArmorFront = 1.5f, ArmorSide = 1.5f, ArmorRear = 1.5f, ArmorTop = 1.5f,
                                                        PresentedAreaM2 = 0.35f, BlastSusceptibility = 1f });
            db.Register(m2);
            var jav = new TkbTemplate("break test Javelin", WsJavelin);
            jav.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 20, PresentedAreaM2 = 0.3f, BlastSusceptibility = 1f });
            db.Register(jav);
        }

        private Entity Weapon(long type, float x, float y, float health)
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>())
                _w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new Health { Current = health, Max = health });
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });
            return e;
        }

        /// <summary>
        /// ⭐⭐ CE-3203 — 🔒 User: <i>"maybe it is pretty easy to break a weapon with a shrapnel hit or blast (especially the sophisticated
        /// ones but likely also the dumb yet fragile mechanical ones)."</i> An 81 mm bomb 10 m away BREAKS a Javelin's command launch unit
        /// (no casing, 20 health — one fragment) but only damages an M2 beside it (a steel receiver, 80 health); a grenade bursting 3 m
        /// from the M2 breaks it too (its blast and a cloud of fragments); a Mk-82 121 m away leaves the M2 working. Each is struck as
        /// EQUIPMENT (its own area), not as a man.
        /// ✅ Red-proof (run): the stated area ignored (weapons hit as a man) ⇒ the M2 at 10 m takes the man's dose ⇒ destroyed ⇒ red.
        /// </summary>
        [Fact]
        public void W6pp_AShellBreaksAJavelinBesideAnM2_ThatOnlyAGrenadeAtThreeMetresBreaks()
        {
            WeaponTypes();
            var m2 = Weapon(WsM2, 10f, 0f, 80f);
            var jav = Weapon(WsJavelin, -10f, 0f, 20f);
            var (rec, damage) = Burst(Mortar, new Vector3(0f, 0f, 0.1f), m2, jav);
            Assert.Equal(AreaEffect.FragmentTarget.Equipment, rec.Effects.Single(x => x.Entity == m2).Target);
            Assert.True(damage[0] > 5f && damage[0] < 80f, $"the M2 at 10 m from an 81 mm took {damage[0]} of 80: damaged, not broken");
            Assert.True(damage[1] >= 20f, $"the Javelin at 10 m took {damage[1]} of 20: broken");

            var close = Weapon(WsM2, 3f, 30f, 80f);
            var (_, dGrenade) = Burst(Grenade, new Vector3(0f, 30f, 0.1f), close);
            Assert.True(dGrenade[0] >= 80f, $"the M2 3 m from a grenade took {dGrenade[0]} of 80");

            VehicleTypes();
            var far = Weapon(WsM2, 0f, 221f, 80f);
            var (_, dBomb) = Burst(Bomb, new Vector3(0f, 100f, 0.1f), far);
            Assert.True(dBomb[0] < 10f, $"the M2 121 m from a Mk-82 took {dBomb[0]} of 80");
        }

        /// <summary>
        /// ⭐ <c>CE-3203</c> C4 (DESIGN_Weapon_Systems §5b) — a Javelin CARRIED on a soldier's back is struck when he is: the soldier types
        /// declare <see cref="Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PersonCarrier"/> (an OPEN carrier — BdcTkbCatalog's rifleman, the urban
        /// catalogue's soldiers), so the carry station is an exposed seat and the boarded weapon's <c>IsEmbarkedTag.Exposed</c> is 1 —
        /// <c>PhysicalPresence.IsHidden</c> does not hide it. The same 81 mm at 10 m that breaks a Javelin on the ground breaks it on his back.
        /// </summary>
        [Fact]
        public void CE3203_C4_AJavelinCarriedOnASoldiersBack_IsStruckWithHim()
        {
            WeaponTypes();
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
            var soldier = Soldier(10f, 0f);
            _w.AddComponent(soldier, Fdp.Toolkit.Behavior.Translators.CarrierTkbTranslator.Seats(Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PersonCarrier()));
            var jav = Weapon(WsJavelin, 10f, 0f, 20f);   // carried: where he is
            Fdp.Toolkit.Behavior.Embarkation.Board(_w, jav, soldier, seat: 1);   // seat 1 = his carry station

            Assert.Equal(1, _w.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(jav).Exposed);
            Assert.False(Fdp.Toolkit.Perception.PhysicalPresence.IsHidden(_w, jav));
            var (rec, damage) = Burst(Mortar, new Vector3(0f, 0f, 0.1f), soldier, jav);
            Assert.Equal(AreaEffect.FragmentTarget.Equipment, rec.Effects.Single(x => x.Entity == jav).Target);
            Assert.True(damage[1] >= 20f, $"the carried Javelin 10 m from an 81 mm took {damage[1]} of 20: broken, as on the ground");
            Assert.True(damage[0] > 0f, "and the man carrying it is hit");
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

        /// <summary>⭐ <c>CE-3110</c> I-2c (R-271) — a passenger inside an ENCLOSED vehicle at the burst takes no blast (the hull takes it,
        /// and if the vehicle dies the passenger dies with it); one on an OPEN cabin is hit like anyone. 📄 DESIGN_Entity_Interactions §7.2a.</summary>
        [Fact]
        public void CE3110_I2c_ABlast_SparesAnEnclosedPassenger_NotAnExposedOne()
        {
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var enclosed = Soldier(10f, 10f);
            _w.AddComponent(enclosed, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = 0 });
            var exposed = Soldier(10.5f, 10f);
            _w.AddComponent(exposed, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = 1 });

            var (_, damage) = Burst(Grenade, new Vector3(10f, 11f, 0f), enclosed, exposed);

            Assert.Equal(0f, damage[0]);
            Assert.True(damage[1] > 0f, "an exposed passenger is hit like anyone");
        }
    }
}
