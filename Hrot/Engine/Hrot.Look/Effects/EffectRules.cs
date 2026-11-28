using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Map3D;

namespace Hrot.Look.Effects;

/// <summary>⭐ CE-4671 LK-7 (§7a) — the surface an effect happened on: with the kind and the size class, the category every renderer shares (built to map
/// onto DIS's detonation results). ⚠ <see cref="Water"/> is not told apart yet (a burst on water reads <see cref="Ground"/>); <see cref="Person"/> is a body
/// that is not a vehicle (added to §7a's list, which had none for it).</summary>
public enum EffectSurface : byte { Ground = 0, Water, Building, Vehicle, Person, Air }

/// <summary>
/// ⭐ CE-4671 LK-2 / LK-7 (DESIGN_Vehicle_Appearance_State.md §7, §7a, §7b) — ONE effect a renderer shows: our category (<see cref="Kind"/> ×
/// <see cref="SizeClass"/> × <see cref="Surface"/>), the munition's identity (<see cref="MunitionDis"/>), the exact TKB effect type
/// (<see cref="EffectType"/> — what HROT's map draws), where (<see cref="At"/>, <see cref="Rotation"/>, or attached by <see cref="Anchor"/>), and how
/// long and how big (the type's look).
/// </summary>
public struct EffectSpec
{
    public EffectKind Kind;
    public EffectTkbCatalog.CalibreClass SizeClass;
    public EffectSurface Surface;
    /// <summary>The munition's DIS entity type (default for an effect no munition made — a barrier's dust).</summary>
    public DISEntityType MunitionDis;
    /// <summary>The TKB effect type (the round's <c>Effect.Set</c> member, else its calibre class's default).</summary>
    public long EffectType;
    public Vector3 At;
    public Quaternion Rotation;
    /// <summary>Attached to a shooter's weapon (a muzzle flash, VE-J) — null for a world-fixed effect.</summary>
    public EffectAnchor? Anchor;
    /// <summary>Seconds it lives (the type's <c>Duration</c>).</summary>
    public float Lifetime;
    /// <summary>Metres across (the type's <c>Size</c>).</summary>
    public float Size;
    /// <summary>⭐ CE-11010 (RR-10) — for an effect a HIT made (an impact's puff and mark, a detonation's blast and mark): what it struck. Null for a shot's
    /// flash or blast. The map writes it on the effect entity, so a replay keeps it.</summary>
    public EffectCause? Cause;
    /// <summary>⭐ CE-11015 — when it began (sim seconds): the event's time for a spec made live, the recorded <c>Lifespan</c> start for one rebuilt from an
    /// effect entity (<see cref="EffectRules.SpecOf"/>). 0 when the maker did not say.</summary>
    public double StartedAt;
}

/// <summary>
/// ⭐⭐ CE-4671 LK-S1 (DESIGN_Vehicle_Appearance_State.md §7 LK-2, §7b) — WHICH effects an event makes, where and for how long: pure functions over the
/// event, the world's components and the TKB (no wall clock, no frame count, LK-5). Extracted from the map's <c>EffectSpawnSystem</c>, which now spawns
/// its effect entities FROM these specs — so a gateway calling the same functions on its node's events gets the same flashes, blasts and marks.
/// <para>Each method APPENDS to <paramref>into</paramref> in the order the effects were always made, and returns how many it added. ⭐ 0 B: no allocation
/// (a caller's list, reused). ⛔ Decoy releases (flares, chaff) are the spawner's own — a moving effect, not one of these.</para>
/// </summary>
public static class EffectRules
{
    /// <summary>⭐ CE-1113 (§6v AD-V6) — the highest a burst may be above the ground and still scorch it (m).</summary>
    public const float DecalMaxBurstHeight = 2f;

    /// <summary>How far above the ground an impact still counts as on the GROUND (else a building's face) — the category only (m).</summary>
    public const float GroundImpactBand = 0.25f;

    /// <summary>
    /// Every effect of this frame's events, in the order the map makes them: barrier dust, shots, impacts, detonations (a gateway's one call).
    /// </summary>
    public static int ForFrame(EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        int n = 0;
        if (world.Bus.IsRegistered<Fdp.Toolkit.Terrain.ContactBreachEvent>())
            foreach (ref readonly var b in world.ReadEvents<Fdp.Toolkit.Terrain.ContactBreachEvent>()) n += ForContactBreach(in b, tkb, into);
        if (world.Bus.IsRegistered<WeaponFireNotification>())
            foreach (ref readonly var f in world.ReadEvents<WeaponFireNotification>()) n += ForShot(in f, world, tkb, into);
        if (world.Bus.IsRegistered<RoundImpactNotification>())
            foreach (ref readonly var i in world.ReadEvents<RoundImpactNotification>()) n += ForImpact(in i, world, tkb, into);
        if (world.Bus.IsRegistered<DetonationNotification>())
            foreach (ref readonly var d in world.ReadEvents<DetonationNotification>()) n += ForDetonation(in d, world, tkb, into);
        return n;
    }

    /// <summary>
    /// ⭐ CE-4600 (B-3b, V-17) — a barrier BROKE (dust, a large puff) or was BUMPED open (a medium puff) at the contact point; a push in progress and a
    /// wall too strong make none.
    /// </summary>
    public static int ForContactBreach(in Fdp.Toolkit.Terrain.ContactBreachEvent b, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        var kind = (Fdp.Toolkit.Terrain.ContactBreachKind)b.Kind;
        if (kind is not (Fdp.Toolkit.Terrain.ContactBreachKind.Broke or Fdp.Toolkit.Terrain.ContactBreachKind.Bumped)) return 0;
        bool heavy = kind == Fdp.Toolkit.Terrain.ContactBreachKind.Broke;
        return Add(into, tkb, heavy ? EffectTkbCatalog.ImpactPuffLarge : EffectTkbCatalog.ImpactPuffMedium, new Vector3(b.X, b.Y, b.Z), Quaternion.Identity,
                   null, heavy ? EffectTkbCatalog.CalibreClass.Large : EffectTkbCatalog.CalibreClass.Medium, EffectSurface.Building, default);
    }

    /// <summary>⭐ CE-4635 — is this mount a FLAMETHROWER (its shots are fuel: heard as one roar while the jet is lit, seen as the jet)? THE one predicate: the flash rule here and the
    /// map's queued shot sound (<c>EffectSpawnSystem</c>) both ask it.</summary>
    public static bool IsFuelShot(WeaponMountDto? mount) => mount?.Category == WeaponCategory.Flamethrower;

    /// <summary>
    /// A shot (<see cref="WeaponFireNotification"/>): a muzzle flash ATTACHED to the shooter's weapon (VE-J) — none for a refused shot, a decoy release, a
    /// throw (CE-3248), a bomb leaving its rack (CE-1094) or a silent weapon (an arrow, a bolt — CE-4649 BW-9); then what the shot throws besides
    /// (<see cref="FiringBlasts.BlastOf"/>): a backblast behind a recoilless tube, a ground blast round a howitzer, a missile's launch smoke (CE-4618).
    /// ⛔ No tracer (LR-4): a tracer is a real, replicated round. ⭐ CE-4635: ⛔ nothing at all for a flamethrower mount's fuel shot — the jet is its look.
    /// </summary>
    public static int ForShot(in WeaponFireNotification fire, EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        if (!fire.Fired) return 0;   // ⭐ Q94 A-2 (R-3018) — a REFUSED shot never flew
        if (Munitions.IsExpendable(tkb, fire.AmmoGuid)) return 0;   // ⭐ CE-1115 — a flare / chaff release: the spawner's moving decoy, no flash
        if (fire.Shooter == Entity.Null || !world.IsAlive(fire.Shooter) || !world.HasComponent<SimTransform>(fire.Shooter)) return 0;
        if (PersonHands.Threw(world, in fire)) return 0;   // ⭐ CE-3248 — a THROW is no gunshot: no flash, no blast
        var mount = CombatTkb.MountOf(world, fire.Shooter, fire.WeaponIndex);
        // ⭐ CE-4635 (flame weapons §8 U-5) — a FLAMETHROWER's fuel shot (600 a minute, a normal fired notification, no round) makes no flash and no blast: the JET is its look
        //   (EffectLayer draws it from FlameJets.TryGet) — otherwise it would flash like a rifle ten times a second under its own flames.
        if (IsFuelShot(mount)) return 0;
        long fired = fire.AmmoGuid != 0 ? fire.AmmoGuid : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;   // ⭐ CE-1115 — the round actually fired
        float damage = mount?.DamagePerHit ?? 0f;
        var set = EffectTkbCatalog.EffectsFor(tkb, fired, damage);
        var size = SizeOf(tkb, fired, damage);
        var dis = DisOf(tkb, fired);
        var at = world.GetComponentRO<SimTransform>(fire.Shooter).Position;
        // ⭐ CE-11016 — where the shot points is the ONE rule the burst stream uses (target, else the aim posture, else the round's own launch, inside the 45° cone):
        //   so a stream rebuilt from the recorded flash (FireStreams.StreamOfFlash) points where the live one did
        var toward = FireStreams.TowardOf(in fire, world);
        var anchor = new EffectAnchor { Shooter = fire.Shooter, WeaponIndex = fire.WeaponIndex, Toward = toward };
        int n = 0;
        if (mount?.Category != WeaponCategory.Bomb && !Munitions.IsSilent(world, fire.AmmoGuid))
            n += Add(into, tkb, set.MuzzleFlash, at, Quaternion.Identity, anchor, size, EffectSurface.Air, dis);
        switch (FiringBlasts.BlastOf(world, tkb, fire.Shooter, mount))
        {
            case FiringBlast.Backblast:
            {
                var aimAt = toward != Vector3.Zero ? (Vector3)toward : AimPointOf(world, fire.Shooter);
                var back = FiringBlasts.BackblastPoint(at, world.GetComponentRO<SimTransform>(fire.Shooter).Rotation, aimAt);
                n += Add(into, tkb, EffectTkbCatalog.Backblast, back, Quaternion.Identity, null, size, EffectSurface.Ground, dis);
                break;
            }
            case FiringBlast.Ground:
                n += Add(into, tkb, EffectTkbCatalog.GunBlast, at + new Vector3(0f, 0f, 0.3f), Quaternion.Identity, null, size, EffectSurface.Ground, dis);
                break;
            case FiringBlast.Launch:   // ⭐ CE-4618 — the smoke where the round left (the shot's origin), staying there
                n += Add(into, tkb, EffectTkbCatalog.MissileLaunchSmoke, fire.Origin != Vector3.Zero ? fire.Origin : at, Quaternion.Identity, null, size,
                         EffectSurface.Air, dis);
                break;
        }
        return n;
    }

    /// <summary>
    /// ⭐ CE-1129 (VE §10 IM-5 … IM-7) — one surface a round marked (<see cref="RoundImpactNotification"/>): the round's puff (its set's <c>Explosion</c>)
    /// just off the face, and its mark (its <c>Decal</c>) turned onto the face it struck — sized by its round (CE-1199).
    /// </summary>
    public static int ForImpact(in RoundImpactNotification hit, EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        int start = into.Count, n = ImpactCore(in hit, world, tkb, into);
        float damage = n > 0 ? ImpactDamage(world, tkb, in hit) : 0f;
        for (int i = start; i < into.Count; i++)   // ⭐ CE-11010 (RR-10) — what it struck, on every effect it made
        {
            var spec = into[i];
            // ⭐ CE-11024 — kind and shooter; ⭐ CE-11030 — ONE of the round's effects (the first: its puff) is THE impact (Impact != 0), the others (its mark) carry 0, and every one carries the face's normal
            spec.Cause = new EffectCause { Surface = (byte)spec.Surface, Material = hit.Material, Target = Entity.Null, Damage = damage, Ammo = hit.Ammo,
                                           Impact = i == start ? (byte)(hit.Kind + 1) : (byte)0, Shooter = hit.Shooter, Normal = new Vector3(hit.NormalX, hit.NormalY, hit.NormalZ) };
            into[i] = spec;
        }
        return n;
    }

    private static int ImpactCore(in RoundImpactNotification hit, EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        float damage = ImpactDamage(world, tkb, hit);
        var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, damage);
        var size = SizeOf(tkb, hit.Ammo, damage);
        var dis = DisOf(tkb, hit.Ammo);
        var point = new Vector3(hit.X, hit.Y, hit.Z);
        var normal = SurfaceOf(hit);
        var ground = Fdp.Toolkit.World.WorldQuery.Of(world);
        var surface = MathF.Abs(point.Z - (ground?.GroundHeightAt(point.X, point.Y) ?? 0f)) <= GroundImpactBand ? EffectSurface.Ground : EffectSurface.Building;
        return Add(into, tkb, set.Explosion, point + normal * 0.05f, Quaternion.Identity, null, size, surface, dis)
             + Add(into, tkb, set.Decal, point, MarkRotation(normal), null, size, surface, dis);
    }

    /// <summary>
    /// A detonation (<see cref="DetonationNotification"/>): an explosion at the hit point and a mark on the GROUND under it — none when it hit a vehicle or
    /// a person (VE-M), none for a burst in the air (CE-1113); a thrown inert item only LANDS: a little dust (CE-1194).
    /// </summary>
    public static int ForDetonation(in DetonationNotification hit, EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        int start = into.Count, n = DetonationCore(in hit, world, tkb, into);
        for (int i = start; i < into.Count; i++)   // ⭐ CE-11010 (RR-10, D-6) — a replayed explosion tells a unit from the ground
        {
            var spec = into[i];
            spec.Cause = new EffectCause { Surface = (byte)spec.Surface, Target = hit.Target, Damage = hit.Damage, Ammo = hit.Ammo, Shooter = hit.Shooter };   // ⭐ CE-11024
            into[i] = spec;
        }
        return n;
    }

    private static int DetonationCore(in DetonationNotification hit, EntityRepository world, ITkbDatabase? tkb, List<EffectSpec> into)
    {
        var point = new Vector3(hit.HitX, hit.HitY, hit.HitZ);
        var dis = DisOf(tkb, hit.Ammo);
        if (IsInertHandItem(tkb, hit.Ammo))
            return Add(into, tkb, EffectTkbCatalog.ExplosionSmall, point, Quaternion.Identity, null, EffectTkbCatalog.CalibreClass.Small, EffectSurface.Ground, dis);
        var set = EffectTkbCatalog.EffectsFor(tkb, hit.Ammo, hit.Damage);
        var size = SizeOf(tkb, hit.Ammo, hit.Damage);
        bool hitABody = hit.Target != Entity.Null && world.IsAlive(hit.Target);
        float gz = hitABody ? 0f : Fdp.Toolkit.World.WorldQuery.Of(world)?.GroundHeightAt(point.X, point.Y) ?? 0f;
        bool inTheAir = !hitABody && point.Z - gz > DecalMaxBurstHeight;
        var surface = hitABody ? (FiringBlasts.TypeOf(world, tkb, hit.Target).Family == VisualFamily.Person ? EffectSurface.Person : EffectSurface.Vehicle)
                    : inTheAir ? EffectSurface.Air : EffectSurface.Ground;
        int n = Add(into, tkb, set.Explosion, point, Quaternion.Identity, null, size, surface, dis);
        if (!hitABody && !inTheAir) n += Add(into, tkb, set.Decal, new Vector3(point.X, point.Y, gz), Quaternion.Identity, null, size, surface, dis);
        return n;
    }

    /// <summary>
    /// ⭐ <c>CE-1194</c> — a round a hand THROWS and that carries no explosive: a hand item (<see cref="HandItemDto"/> — the stone, the thrown hatchet /
    /// spear / star) whose warhead says <c>Kinetic</c> or <c>Blunt</c>. Its "detonation" is where it lands: dust, not a burst. ⛔ A hand GRENADE keeps its
    /// explosion (the M67 states no warhead here).
    /// </summary>
    public static bool IsInertHandItem(ITkbDatabase? tkb, long ammo)
    {
        if (tkb == null || ammo == 0 || !tkb.TryGetByType(ammo, out var t) || t == null || t.GetDescriptor<HandItemDto>() == null) return false;
        return t.GetDescriptor<WarheadDto>() is { Kind: WarheadKind.Kinetic or WarheadKind.Blunt };
    }

    /// <summary>
    /// ⭐ <c>CE-1199</c> (VE-D) — how hard the round that made <paramref name="hit"/> strikes, so its puff and mark take its calibre class: the shooter's
    /// mount firing that ammo (its own round or one of its loadout), else the round's own number, else 0 (the small class).
    /// </summary>
    public static float ImpactDamage(EntityRepository world, ITkbDatabase? tkb, in RoundImpactNotification hit)
    {
        if (hit.Ammo == 0) return 0f;
        if (!hit.Shooter.IsNull && world.IsAlive(hit.Shooter) && CombatTkb.TryGetTemplate(world, hit.Shooter, out var t)
            && t.GetDescriptor<WeaponSuiteDto>() is { } suite)
            foreach (var mount in suite.Mounts)
            {
                bool fires = unchecked((long)mount.AmmoGuid) == hit.Ammo;
                foreach (var load in mount.Loadout) fires |= unchecked((long)load.AmmoGuid) == hit.Ammo;
                if (fires) return CombatTkb.RoundOf(tkb, mount, hit.Ammo).Damage;
            }
        if (tkb != null && tkb.TryGetByType(hit.Ammo, out var ammo) && ammo != null)
            foreach (var (type, _, data) in ammo.GetAllDescriptors())
                if (type == typeof(AmmoWeaponBallisticsDto) && data is AmmoWeaponBallisticsDto b && b.Damage > 0f) return b.Damage;
        return 0f;
    }

    /// <summary>⭐ CE-1129 (IM-4) — the face a mark lies on: the impact's normal, else back along the round's travel, else up.</summary>
    public static Vector3 SurfaceOf(in RoundImpactNotification hit)
    {
        var n = new Vector3(hit.NormalX, hit.NormalY, hit.NormalZ);
        if (n.LengthSquared() > 1e-6f) return Vector3.Normalize(n);
        var back = -new Vector3(hit.TravelX, hit.TravelY, hit.TravelZ);
        return back.LengthSquared() > 1e-6f ? Vector3.Normalize(back) : Vector3.UnitZ;
    }

    /// <summary>⭐ CE-1129 (IM-6) — the rotation that turns a mark's +Z onto <paramref name="normal"/> (identity for a mark facing up).</summary>
    public static Quaternion MarkRotation(Vector3 normal)
    {
        var n = Vector3.Normalize(normal);
        float d = Vector3.Dot(Vector3.UnitZ, n);
        if (d > 0.9999f) return Quaternion.Identity;
        if (d < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(d, -1f, 1f)));
    }

    /// <summary>LK-7 — a round's size class: by its damage per hit, else its explosive mass (the same classes <c>EffectsFor</c>'s defaults use, CE-1231).</summary>
    public static EffectTkbCatalog.CalibreClass SizeOf(ITkbDatabase? tkb, long ammo, float damage)
    {
        if (damage > 0f) return EffectTkbCatalog.ClassOf(damage);
        float kg = EffectTkbCatalog.ExplosiveKgOf(tkb, ammo);
        return kg > 0f ? EffectTkbCatalog.ClassOfExplosive(kg) : EffectTkbCatalog.CalibreClass.Small;
    }

    /// <summary>LK-7 — a munition's DIS entity type (its identity); default when the TKB does not know it.</summary>
    public static DISEntityType DisOf(ITkbDatabase? tkb, long ammo)
        => tkb != null && ammo != 0 && tkb.TryGetByType(ammo, out var t) && t != null ? t.DisType : default;

    private static Vector3 AimPointOf(EntityRepository world, Entity shooter)
        => WeaponPostureOf.TryAimPoint(world, shooter, out var p) ? p : Vector3.Zero;

    /// <summary>One spec for a TKB effect type with a look (none for type 0 or a type the TKB does not draw — as the spawner always skipped it).</summary>
    private static int Add(List<EffectSpec> into, ITkbDatabase? tkb, long type, Vector3 at, Quaternion rotation, EffectAnchor? anchor,
                           EffectTkbCatalog.CalibreClass size, EffectSurface surface, DISEntityType dis)
    {
        if (type == 0 || EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return 0;
        into.Add(new EffectSpec
        {
            Kind = look.Kind, SizeClass = size, Surface = surface, MunitionDis = dis, EffectType = type,
            At = at, Rotation = rotation, Anchor = anchor, Lifetime = look.Duration, Size = look.Size,
        });
        return 1;
    }

    /// <summary>
    /// ⭐⭐ CE-11015 — the SAME <see cref="EffectSpec"/> rebuilt from an effect ENTITY (what the map spawns and a recording keeps): its type and look from the
    /// TKB, where from <c>SimTransform</c>, when and how long from <c>Lifespan</c>, the gun it rides from <c>EffectAnchor</c>, what a hit struck from
    /// <c>EffectCause</c> (and from that the munition's DIS type and the size class). So a renderer reads effects from STATE — live and in a replay, and right
    /// after a seek — with no events: 🔒 user, 2026-11-20: <i>"isn't it better kept in component states only which are working after replay seek"</i>.
    /// False for an entity that is not an effect the TKB draws. 0 B.
    /// </summary>
    public static bool SpecOf(EntityRepository world, Entity e, ITkbDatabase? tkb, out EffectSpec spec)
    {
        spec = default;
        if (!world.IsAlive(e) || !world.HasComponent<TkbIdentity>(e) || !world.HasComponent<SimTransform>(e)) return false;
        long type = world.GetComponentRO<TkbIdentity>(e).TkbType;
        if (EffectTkbCatalog.VisualOf(tkb, type) is not { } look) return false;
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        spec.Kind = look.Kind; spec.EffectType = type; spec.At = tf.Position; spec.Rotation = tf.Rotation;
        spec.Lifetime = look.Duration; spec.Size = look.Size;
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Time.Lifespan>() && world.HasComponent<Fdp.Toolkit.Time.Lifespan>(e))
        {
            var life = world.GetComponentRO<Fdp.Toolkit.Time.Lifespan>(e);
            spec.StartedAt = life.BornAtSimSeconds.Seconds;
            spec.Lifetime = life.Seconds.Seconds;
        }
        if (world.IsComponentTypeRegistered<EffectAnchor>() && world.HasComponent<EffectAnchor>(e))
        {
            var anchor = world.GetComponentRO<EffectAnchor>(e);
            spec.Anchor = anchor;
            if (anchor.Kind == EffectAnchorKind.Muzzle) spec.Surface = EffectSurface.Air;   // a flash at a muzzle (a wreck's flames sit on its body)
        }
        if (world.IsComponentTypeRegistered<EffectCause>() && world.HasComponent<EffectCause>(e))
        {
            var cause = world.GetComponentRO<EffectCause>(e);
            spec.Cause = cause;
            spec.Surface = (EffectSurface)cause.Surface;
            spec.MunitionDis = DisOf(tkb, cause.Ammo);
            spec.SizeClass = SizeOf(tkb, cause.Ammo, cause.Damage);
        }
        return true;
    }
}
