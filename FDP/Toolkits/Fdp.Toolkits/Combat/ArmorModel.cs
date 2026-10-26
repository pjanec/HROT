using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>Which face of a target a shot strikes. ⭐ M1 (D-A) — <see cref="Top"/>: the round arrives descending ≥
    /// <see cref="ArmorModel.TopDescentDegrees"/>. 📄 <c>docs/designs/vehicle-tactics/DESIGN_Armour_Hit_Model.md</c> §2.</summary>
    public enum Facing : byte { Front, Side, Rear, Top }

    /// <summary>⭐ M2 (D-B) — what defeats armour: a kinetic round, or a shaped-charge jet (which reads the <c>…Ce</c> columns).
    /// <see cref="Tandem"/> reads the same column as <see cref="ShapedCharge"/> until ERA exists (D-C).</summary>
    public enum Penetrator : byte { Kinetic, ShapedCharge, Tandem }

    /// <summary>
    /// ⭐⭐ <c>CE-3071</c> (R-212) — ammunition vs armour: ONE model for the damage the simulation applies AND the effectiveness
    /// the AI expects, so the two can never disagree. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §9.
    /// <para>Facing from the target's yaw (&lt; 60° front, &gt; 120° rear, else side; turrets ignored) · penetration chance
    /// <c>P = clamp((pen / armour − 0.8) / 0.4, 0, 1)</c>, no armour ⇒ 1 · damage of a hit = <c>DamagePerHit × P</c>, the
    /// EXPECTED value with no dice (A2), so replays and the determinism rails stay exact · an unknown munition (no
    /// <c>DamagePerHit</c>) keeps the flat <see cref="CombatConstants.DefaultBulletDamage"/>.</para>
    /// </summary>
    public static class ArmorModel
    {
        /// <summary>Below this angle (degrees) between the target's forward and the shooter, the shot hits the front.</summary>
        public const float FrontArcDegrees = 60f;
        /// <summary>Above this angle (degrees), the shot hits the rear.</summary>
        public const float RearArcDegrees  = 120f;

        /// <summary>⭐ M1 (D-A) — a round arriving at least this steeply (degrees below horizontal) strikes the <see cref="Facing.Top"/>.</summary>
        public const float TopDescentDegrees = 45f;
        private static readonly float SinTopDescent = MathF.Sin(TopDescentDegrees * (MathF.PI / 180f));

        /// <summary>
        /// The face of <paramref name="target"/> a shot from <paramref name="from"/> strikes — ⭐ M1: in 3-D, the round travelling
        /// from <paramref name="from"/> to the target's position raised by <paramref name="aimHeight"/> (its centre of mass; 0 = its
        /// feet). See <see cref="FacingOfArrival"/>.
        /// </summary>
        public static Facing FacingOf(in SimTransform target, Vector3 from, float aimHeight = 0f)
            => FacingOfArrival(in target, target.Position + new Vector3(0f, 0f, aimHeight) - from);

        /// <summary>
        /// ⭐ M1 (D-A) — the face a round TRAVELLING along <paramref name="travel"/> strikes: <see cref="Facing.Top"/> when it descends
        /// ≥ <see cref="TopDescentDegrees"/> (straight down included — it was <see cref="Facing.Front"/>); else the horizontal
        /// bearing it came FROM against the target's yaw (&lt; 60° front, &gt; 120° rear, else side; turrets ignored, D-G). A zero
        /// travel (unknown) ⇒ <see cref="Facing.Front"/>.
        /// </summary>
        public static Facing FacingOfArrival(in SimTransform target, Vector3 travel)
        {
            float len = travel.Length();
            if (len < 1e-6f) return Facing.Front;
            if (-travel.Z / len >= SinTopDescent) return Facing.Top;
            var fwd = Vector3.Transform(Vector3.UnitX, target.Rotation);
            var toSource = new Vector2(-travel.X, -travel.Y);
            var f2 = new Vector2(fwd.X, fwd.Y);
            if (toSource.LengthSquared() < 1e-6f || f2.LengthSquared() < 1e-6f) return Facing.Front;
            float cos = Vector2.Dot(Vector2.Normalize(f2), Vector2.Normalize(toSource));
            float deg = MathF.Acos(Math.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
            return deg < FrontArcDegrees ? Facing.Front : deg > RearArcDegrees ? Facing.Rear : Facing.Side;
        }

        /// <summary>The KINETIC armour (mm) on <paramref name="facing"/>; 0 when the platform declares none.</summary>
        public static float ArmourFor(CombatPlatformDefDto? platform, Facing facing) => ArmourFor(platform, facing, Penetrator.Kinetic);

        /// <summary>
        /// ⭐ M2 (D-B) — the armour (mm) <paramref name="facing"/> offers <paramref name="penetrator"/>: a shaped charge reads the
        /// face's <c>…Ce</c> column, 0 there ⇒ the kinetic value (every type is unchanged until its data says otherwise). The
        /// <see cref="Facing.Top"/> reads <c>ArmorTop</c>, 0 ⇒ <c>ArmorRear</c> (the thinnest declared face, never "no armour").
        /// </summary>
        public static float ArmourFor(CombatPlatformDefDto? platform, Facing facing, Penetrator penetrator)
        {
            if (platform == null) return 0f;
            float kinetic = facing switch
            {
                Facing.Front => platform.ArmorFront,
                Facing.Side  => platform.ArmorSide,
                Facing.Top   => platform.ArmorTop > 0f ? platform.ArmorTop : platform.ArmorRear,
                _            => platform.ArmorRear,
            };
            if (penetrator == Penetrator.Kinetic) return kinetic;
            float ce = facing switch
            {
                Facing.Front => platform.ArmorFrontCe,
                Facing.Side  => platform.ArmorSideCe,
                Facing.Top   => platform.ArmorTopCe,
                _            => platform.ArmorRearCe,
            };
            return ce > 0f ? ce : kinetic;
        }

        /// <summary>⭐ R-2019 D-M2 — the side armour at which a hull with no stated <c>BlastSusceptibility</c> shrugs off blast (derived:
        /// <c>1 − side / this</c>, clamped).</summary>
        public const float SoftSkinArmourMm = 40f;

        /// <summary>
        /// ⭐ R-2019 D-M2 (DESIGN_Armour_Hit_Model §7.5) — the share of a burst's blast a target takes: a PERSON (<paramref name="hullHeight"/>
        /// ≤ 0) or a hull with no platform record takes it all; a hull takes its type's stated
        /// <see cref="CombatPlatformDefDto.BlastSusceptibility"/>, or one derived from its side armour when it states none (−1). The ONE rule the
        /// burst (<c>AreaEffectSystem</c>) and the weapon brain's estimate (<c>AmmoChoice</c>) read (R-212 A1).
        /// </summary>
        public static float BlastSusceptibility(CombatPlatformDefDto? platform, float hullHeight)
        {
            // ⭐ CE-3203 — a weapon (equipment: a stated presented area) takes its own share, as a hull does
            if (platform == null || (hullHeight <= 0f && platform.PresentedAreaM2 <= 0f)) return 1f;
            if (platform.BlastSusceptibility >= 0f) return Math.Clamp(platform.BlastSusceptibility, 0f, 1f);
            return Math.Clamp(1f - platform.ArmorSide / SoftSkinArmourMm, 0f, 1f);
        }

        /// <summary>⭐ AE5 (DESIGN_Armour_Hit_Model §7.7) — the area a hull presents to a burst (m²): its bounding cylinder's silhouette,
        /// <c>2 · radius · hull height</c> (a pickup ≈ 10 m²). 0 for a person (no hull height).</summary>
        public static float PresentedArea(float colliderRadius, float hullHeight)
            => hullHeight > 0f ? 2f * MathF.Max(colliderRadius, 0.3f) * hullHeight : 0f;

        /// <summary>The chance a round of <paramref name="penetration"/> mm defeats <paramref name="armour"/> mm.</summary>
        public static float PenetrationChance(float penetration, float armour)
        {
            if (armour <= 0f) return 1f;
            return Math.Clamp((penetration / armour - 0.8f) / 0.4f, 0f, 1f);
        }

        /// <summary>The expected damage of one hit: <c>DamagePerHit × P</c> (A2 — no dice).</summary>
        public static float ExpectedDamage(float penetration, float damagePerHit, float armour)
            => damagePerHit * PenetrationChance(penetration, armour);

        /// <summary>
        /// The damage one hit does: <see cref="ExpectedDamage"/> for a known munition. An UNKNOWN munition —
        /// <paramref name="penetration"/> ≤ 0: an external detonation, or a weapon whose TKB entry carries no numbers — ignores
        /// armour and does its <paramref name="damagePerHit"/>, or the flat <see cref="CombatConstants.DefaultBulletDamage"/>
        /// when that is unknown too. ⛔ Never 0 for an unknown round: that would make every unknown shot harmless on armour.
        /// </summary>
        public static float HitDamage(float penetration, float damagePerHit, float armour)
        {
            if (penetration <= 0f) return Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(damagePerHit);
            return ExpectedDamage(penetration, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(damagePerHit), armour);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3071</c> (R-212, A3 revised) — reads the FIXED combat numbers of a unit from the TKB by its type, where they are
    /// used: armour (<see cref="CombatPlatformDefDto"/>) and a mount's weapon (<see cref="WeaponMountDto"/>). ⛔ Nothing copies
    /// them onto a component — nothing changes them after spawn. The TKB is the world's <see cref="ITkbDatabase"/> singleton
    /// (published by SimHost, CGF and the editor); a world without it, or a unit without <see cref="TkbIdentity"/>, has no
    /// numbers and the callers fall back to today's flat behaviour.
    /// </summary>
    public static class CombatTkb
    {
        /// <summary>
        /// ⭐ <c>CE-2165</c> V3 — the LIVE world's TKB database as a source, for code that runs on a snapshot (the EQS solver: its replica
        /// world carries no TKB). It reads the reference at call time — nothing is copied; the TKB is one constant object
        /// (🔒 user, 2026-10-14: <i>"Certainly not snapshotting it please. Pass reference somehow."</i>). The twin of
        /// <c>TerrainWorldSource.Live</c>.
        /// </summary>
        public static Func<ITkbDatabase?> Live(EntityRepository world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return () => world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
        }

        /// <summary>⭐ <c>CE-2165</c> V3 — the TKB entry of <paramref name="entity"/>'s type from <paramref name="db"/>, read on any view
        /// (a solver snapshot included): its <see cref="TkbIdentity"/>.</summary>
        public static bool TryGetTemplate(ISimulationView view, ITkbDatabase? db, Entity entity, out TkbTemplate template)
        {
            template = null!;
            if (db == null || entity.IsNull || !view.IsAlive(entity)) return false;
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<TkbIdentity>()) return false;
            if (!view.HasComponent<TkbIdentity>(entity)) return false;
            return db.TryGetByType(view.GetComponentRO<TkbIdentity>(entity).TkbType, out template) && template != null;
        }

        /// <summary>The TKB entry of <paramref name="entity"/>'s type, if the world and the entity carry one.</summary>
        public static bool TryGetTemplate(EntityRepository world, Entity entity, out TkbTemplate template)
        {
            template = null!;
            if (world == null || !world.IsAlive(entity)) return false;
            if (!world.HasSingletonManaged<ITkbDatabase>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return false;
            if (!world.HasComponent<TkbIdentity>(entity)) return false;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(world.GetComponent<TkbIdentity>(entity).TkbType, out template) && template != null;
        }

        /// <summary>The armour and health declaration of <paramref name="entity"/>'s type, or null.</summary>
        public static CombatPlatformDefDto? PlatformOf(EntityRepository world, Entity entity)
            => TryGetTemplate(world, entity, out var t) ? t.GetDescriptor<CombatPlatformDefDto>() : null;

        /// <summary>Mount <paramref name="mountIndex"/> of <paramref name="owner"/>'s type, or null.</summary>
        public static WeaponMountDto? MountOf(EntityRepository world, Entity owner, int mountIndex)
        {
            if (!TryGetTemplate(world, owner, out var t)) return null;
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            return suite != null && mountIndex >= 0 && mountIndex < suite.Mounts.Count ? suite.Mounts[mountIndex] : null;
        }

        /// <summary>⭐ WL-2 — turret <paramref name="turretIndex"/> of <paramref name="owner"/>'s type (DESIGN_Weapon_Layers W-9), or null.</summary>
        /// <remarks>⭐ <c>CE-1356</c> — a weapon standing in a STATION that declares an arc (a door gun) gets its own turret NARROWED by that arc,
        /// so everything that reads the turret's limits — the slew, the in-arc test and its OutOfArc refusal, the operator's aim clamp, the
        /// drawn gun ring — keeps it out of the cabin. 🔴 The M134's turret is full circle (the same gun stands on pintles elsewhere: the arc
        /// is the mount's, not the gun's) and only the station carried the door's 20°…160°, which the operator's fire never read.
        /// 🔒 User, 2026-10-25: "the minigun mounted on side is not restricted where it can fire, it can turn inside the helicopter".</remarks>
        public static TurretDto? TurretOf(EntityRepository world, Entity owner, int turretIndex)
        {
            if (!TryGetTemplate(world, owner, out var t)) return null;
            var suite = t.GetDescriptor<TurretSuiteDto>();
            var own = suite != null && turretIndex >= 0 && turretIndex < suite.Turrets.Count ? suite.Turrets[turretIndex] : null;
            return own != null && WeaponSystems.TryStationArc(world, owner, out var station) ? WithinStation(own, station) : own;
        }

        /// <summary>⭐ <c>CE-1356</c> — <paramref name="own"/> narrowed by <paramref name="station"/>'s arc, turned into the weapon's frame (it faces
        /// the station's Facing): its traverse is the overlap of both, its elevation too. An arc that cannot be one interval in the weapon's
        /// frame (it would cross straight behind it) leaves the turret as it is. Pure.</summary>
        public static TurretDto WithinStation(TurretDto own, in Fdp.Toolkit.Behavior.Components.StationSeat station)
        {
            const float Deg = 180f / MathF.PI;
            float lo = (station.TraverseMin - station.Facing) * Deg, hi = (station.TraverseMax - station.Facing) * Deg;
            while (lo < -180f) { lo += 360f; hi += 360f; }
            while (lo >= 180f) { lo -= 360f; hi -= 360f; }
            float elLo = MathF.Max(own.ElevationMinDeg, station.ElevationMin * Deg), elHi = MathF.Min(own.ElevationMaxDeg, station.ElevationMax * Deg);
            if (elLo > elHi) (elLo, elHi) = (own.ElevationMinDeg, own.ElevationMaxDeg);
            if (hi - lo >= 359.9f || hi > 180.001f) return own with { ElevationMinDeg = elLo, ElevationMaxDeg = elHi };
            float tLo = MathF.Max(own.TraverseMinDeg, lo), tHi = MathF.Min(own.TraverseMaxDeg, hi);
            if (tLo > tHi) (tLo, tHi) = (lo, hi);   // no overlap: the station's (where it physically stands) wins
            return own with { TraverseMinDeg = tLo, TraverseMaxDeg = tHi, ElevationMinDeg = elLo, ElevationMaxDeg = elHi };
        }

        /// <summary>⭐ <c>CE-3158</c> G3 — the warhead of the round <paramref name="mount"/> fires (its loaded ammo), or null: a kinetic
        /// round, no ammo, or no TKB database in this world. The ONE lookup the flight (<c>FireProcessingSystem</c>) and the burst's
        /// friendly-line exemption (<c>FireAtPointExecutor</c>, an indirect warhead flies over friends) both read.</summary>
        public static WarheadDto? WarheadOf(EntityRepository world, WeaponMountDto? mount)
            => WarheadOf(world, mount != null ? unchecked((long)mount.AmmoGuid) : 0L);

        /// <summary>⭐ M8 — the warhead of the munition type <paramref name="ammo"/> (a loaded round of a loadout), or null.</summary>
        public static WarheadDto? WarheadOf(EntityRepository world, long ammo)
        {
            if (ammo == 0 || world == null || !world.HasSingletonManaged<ITkbDatabase>()) return null;
            return Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(world.GetSingletonManaged<ITkbDatabase>(), ammo).Warhead;
        }

        /// <summary>
        /// ⭐ M8 (D-I) — the penetration (mm RHA) and damage per hit of ONE round of <paramref name="ammo"/> fired from
        /// <paramref name="mount"/>. The mount's own round (<paramref name="ammo"/> 0 or its <c>AmmoGuid</c>) reads exactly what it read
        /// before (<see cref="PenetrationOf"/>, <c>DamagePerHit</c>). ANOTHER round of a loadout reads its own numbers: the R-217 pair
        /// lookup for that ammo with the mount's own penetration NOT used as a fallback (it belongs to the mount's default round), and its
        /// <see cref="AmmoWeaponBallisticsDto.Damage"/> (this weapon, then generic), else the mount's <c>DamagePerHit</c>.
        /// </summary>
        public static (float Penetration, float Damage) RoundOf(EntityRepository world, WeaponMountDto? mount, long ammo)
            => RoundOf(DbOf(world), mount, ammo);

        /// <summary>⭐ <c>CE-2165</c> V1 (behaviors, additive) — <see cref="RoundOf(EntityRepository, WeaponMountDto?, long)"/> with the TKB handed
        /// in, for a reader on a solver snapshot (no TKB singleton there). The one implementation; the world overload delegates.</summary>
        public static (float Penetration, float Damage) RoundOf(ITkbDatabase? db, WeaponMountDto? mount, long ammo)
        {
            if (mount == null) return (0f, 0f);
            if (ammo == 0 || ammo == unchecked((long)mount.AmmoGuid))
                return (mount.DamagePerHit > 0f ? Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(db, mount).Value : 0f, mount.DamagePerHit);
            var asLoaded = mount with { AmmoGuid = unchecked((ulong)ammo), Penetration = 0f };
            float pen = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(db, asLoaded).Value;
            float dmg = mount.DamagePerHit;
            if (db != null && db.TryGetByType(ammo, out var a) && a != null)
            {
                float generic = 0f;
                foreach (var (type, _, data) in a.GetAllDescriptors())
                {
                    if (type != typeof(AmmoWeaponBallisticsDto) || data is not AmmoWeaponBallisticsDto b || b.Damage <= 0f) continue;
                    if (b.WeaponGuid != 0 && unchecked((ulong)b.WeaponGuid) == mount.WeaponGuid) { generic = b.Damage; break; }
                    if (b.WeaponGuid == 0 && generic <= 0f) generic = b.Damage;
                }
                if (generic > 0f) dmg = generic;
            }
            return (pen, dmg);
        }

        /// <summary>⭐ M3 — what the munition <paramref name="ammo"/> (a TKB type id) defeats armour with: its warhead's
        /// <see cref="WarheadKind.ShapedCharge"/> ⇒ <see cref="Penetrator.ShapedCharge"/> (<see cref="WarheadDto.Tandem"/> ⇒
        /// <see cref="Penetrator.Tandem"/>), anything else — no warhead, unknown ammo, no TKB database — ⇒ kinetic.</summary>
        public static Penetrator PenetratorOf(EntityRepository world, long ammo) => PenetratorOfAmmo(world, ammo);

        /// <summary>⭐ <c>CE-2186</c> (W2) — the relative cost of one round of the munition type <paramref name="ammo"/>
        /// (<see cref="MunitionCostDto"/>); 0 when unknown.</summary>
        public static float CostOf(EntityRepository world, long ammo)
        {
            // ⭐ CE-2186 (W2, R-2020) — the munition's relative cost (MunitionCostDto); 0 = unknown, no TKB, or no ammo
            if (ammo == 0 || world == null || !world.HasSingletonManaged<ITkbDatabase>()) return 0f;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(ammo, out var t) && t != null ? t.GetDescriptor<MunitionCostDto>()?.RelativeCost ?? 0f : 0f;
        }

        /// <summary>⭐ M4 (D-D) — the guidance of the munition type <paramref name="ammo"/> (its <see cref="MissileDto"/>), or null: a
        /// round that is not a missile, unknown ammo, or no TKB database.</summary>
        /// <summary>⭐ <c>CE-3235</c> (B-13) — the <see cref="HandItemDto"/> of munition type <paramref name="ammo"/>: present = a hand may pick
        /// a lying one up.</summary>
        public static HandItemDto? HandItemOf(EntityRepository world, long ammo)
        {
            if (ammo == 0 || world == null || !world.HasSingletonManaged<ITkbDatabase>()) return null;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(ammo, out var t) && t != null ? t.GetDescriptor<HandItemDto>() : null;
        }

        public static MissileDto? MissileOf(EntityRepository world, long ammo)
        {
            if (ammo == 0 || world == null || !world.HasSingletonManaged<ITkbDatabase>()) return null;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(ammo, out var t) && t != null ? t.GetDescriptor<MissileDto>() : null;
        }

        /// <summary>
        /// ⭐ D-L (DESIGN_Missiles_And_Ammo §4) — may this mount fire while its unit moves? Its own <see cref="WeaponMountDto.FireOnMove"/>,
        /// else by category: an ATGM or a mortar must stand; small arms, machine guns, autocannon, grenades and a stabilised tank gun
        /// fire on the move (at twice the spread — <see cref="HitModel"/>). An unknown mount fires as before.
        /// </summary>
        public static bool FiresOnTheMove(WeaponMountDto? mount)
            => mount?.FireOnMove ?? mount?.Category switch { WeaponCategory.Atgm or WeaponCategory.Sam or WeaponCategory.Mortar or WeaponCategory.Howitzer => false, _ => true };

        private static Penetrator PenetratorOfAmmo(EntityRepository world, long ammo) => PenetratorOf(DbOf(world), ammo);

        /// <summary>⭐ <c>CE-2165</c> V1 (behaviors, additive) — <see cref="PenetratorOf(EntityRepository, long)"/> with the TKB handed in.</summary>
        public static Penetrator PenetratorOf(ITkbDatabase? db, long ammo)
        {
            if (ammo == 0 || db == null) return Penetrator.Kinetic;
            var w = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.Warhead(db, ammo).Warhead;
            return w?.Kind != WarheadKind.ShapedCharge ? Penetrator.Kinetic : w.Tandem ? Penetrator.Tandem : Penetrator.ShapedCharge;
        }

        private static ITkbDatabase? DbOf(EntityRepository world)
            => world != null && world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;

        /// <summary>
        /// ⭐ Buildings §3d P1 (R-217) — the penetration (mm RHA) of the round <paramref name="mount"/> fires: its loaded ammo × this
        /// weapon, else the ammo's generic profile, else the mount's own value (<c>ParameterResolver.MountPenetration</c> — the rule
        /// <c>GET /tkb/resolve</c> reports). A world without a TKB database reads the mount's own value.
        /// </summary>
        public static float PenetrationOf(EntityRepository world, WeaponMountDto mount) => PenetrationWithSourceOf(world, mount).Value;

        /// <inheritdoc cref="PenetrationOf"/> With where the value came from (the shot log records it).
        public static (float Value, string Source) PenetrationWithSourceOf(EntityRepository world, WeaponMountDto mount)
        {
            var db = world != null && world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
            var (v, source, provenance) = Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(db, mount);
            return (v, $"{provenance}: {source}");
        }

        /// <summary>The unit a mount belongs to: the parent of a mount child (<see cref="PartMetadata"/>), else itself.</summary>
        public static Entity OwnerOf(EntityRepository world, Entity mountOrOwner)
            => world.IsComponentTypeRegistered<PartMetadata>() && world.HasComponent<PartMetadata>(mountOrOwner)
                ? world.GetComponent<PartMetadata>(mountOrOwner).ParentEntity
                : mountOrOwner;
    }
}
