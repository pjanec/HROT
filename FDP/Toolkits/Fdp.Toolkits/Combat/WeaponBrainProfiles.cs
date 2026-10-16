using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ WL-3 (docs/DESIGN_Weapon_Layers.md W-5, §5e) — a weapon brain's per-category behaviour: a HARDCODED class per
    /// <see cref="WeaponCategory"/>, chosen through <see cref="WeaponBrainProfiles.For"/>. 🔒 U-W1: <i>"a hardcoded system customizable
    /// per weapon category"</i>. ⭐ WL-5 (§5g, W-12b/c): the categories now differ.
    /// </summary>
    public interface IWeaponBrainProfile
    {
        /// <summary>True when this mount is fought through its turret's <see cref="WeaponTask"/> (the brain grants, the muscle
        /// aims and fires); false keeps the per-shot path with its aim time (U-W1: <i>"AimSeconds can stay for simplest weapon
        /// brains not modelling turrets"</i>).</summary>
        bool UsesTurretTask(EntityRepository world, Entity unit, WeaponMountDto? mount);

        /// <summary>⭐ W-12c — at most this many granted rounds in the air at once; <c>0</c> = no limit (the magazine is granted).</summary>
        int RoundsInFlight { get; }
    }

    /// <summary>⭐ WL-3 / W-12b — a mount that names a turret the unit has goes through the turret task (TankGun, Autocannon, a turret
    /// MachineGun, and an untagged mount — today's rule, so nothing untagged changes path).</summary>
    public class TurretWeaponProfile : IWeaponBrainProfile
    {
        public bool UsesTurretTask(EntityRepository world, Entity unit, WeaponMountDto? mount)
            => mount != null && mount.TurretIndex >= 0 && world.IsComponentTypeRegistered<WeaponTask>()
               && !TurretParts.Find(world, unit, mount.TurretIndex).IsNull;

        public virtual int RoundsInFlight => 0;
    }

    /// <summary>⭐ W-12b — SmallArms, Grenade, Mortar: always the per-shot path (aim time, one intent per round), turret or not.</summary>
    public sealed class PerShotWeaponProfile : IWeaponBrainProfile
    {
        public bool UsesTurretTask(EntityRepository world, Entity unit, WeaponMountDto? mount) => false;
        public int RoundsInFlight => 0;
    }

    /// <summary>⭐ W-12c — an ATGM: a turret weapon that guides ONE round — the next is granted only after the last one's flight
    /// (<c>AimAndFireExecutor.TurretPath</c>, the estimate in §5g G-2).</summary>
    public sealed class GuidedWeaponProfile : TurretWeaponProfile
    {
        public override int RoundsInFlight => 1;
    }

    /// <summary>⭐ WL-3 — the ONE table of weapon-brain profiles, by category (W-5; W-12b/c, §5g G-1).</summary>
    public static class WeaponBrainProfiles
    {
        public static readonly IWeaponBrainProfile Turret = new TurretWeaponProfile();
        public static readonly IWeaponBrainProfile PerShot = new PerShotWeaponProfile();
        public static readonly IWeaponBrainProfile Guided = new GuidedWeaponProfile();

        /// <summary>The profile that drives a weapon of <paramref name="category"/>.</summary>
        public static IWeaponBrainProfile For(WeaponCategory category) => category switch
        {
            WeaponCategory.SmallArms or WeaponCategory.Grenade or WeaponCategory.Mortar => PerShot,
            WeaponCategory.Bomb => PerShot,
            WeaponCategory.Rocket => PerShot,
            WeaponCategory.Howitzer => PerShot,   // ⭐ IF-1 (I8) — a gun firing at a point by its fire solution   // ⭐ Air Ops 6a (AW-3) — a rocket pod: each round its own intent; the ripple is the round count   // ⭐ Air Ops slice 4 — the Release profile: the per-shot path, held by its cue (AimAndFireExecutor.HoldOf)
            WeaponCategory.Atgm or WeaponCategory.Sam => Guided,   // ⭐ CE-2187 — a MANPADS locks and fires like an ATGM
            _ => Turret,   // TankGun, Autocannon, MachineGun (a hull one names no turret), Unknown
        };

        /// <summary>
        /// ⭐ CE-1078 (DESIGN_Entity_Control §8e D-1) — an OPERATOR fires this weapon once per trigger press (a tank gun, an ATGM, a
        /// mortar, a grenade); every other weapon (small arms, MG, autocannon, an untagged mount) keeps firing while the trigger is held.
        /// </summary>
        public static bool FiresPerPress(WeaponCategory category)
            => category is WeaponCategory.TankGun or WeaponCategory.Atgm or WeaponCategory.Mortar or WeaponCategory.Grenade
                         or WeaponCategory.Bomb or WeaponCategory.Howitzer or WeaponCategory.Sam;
    }

    /// <summary>⭐ <c>CE-1127</c> (docs/DESIGN_Entity_Control.md §8g F-1) — how an OPERATOR's trigger works for a weapon.</summary>
    public enum OperatorFireMode : byte
    {
        /// <summary>Each round goes to the point under the crosshair when it leaves — a rifle, a mount with no turret, an unguided rocket.</summary>
        Sight = 0,
        /// <summary>A press holds the point (on a unit, a building or the ground) and the round leaves when the weapon bears — a turret, a
        /// thrown or arc weapon, a guided one.</summary>
        Lock = 1,
    }

    /// <summary>⭐ <c>CE-1127</c> (§8g F-1) — the ONE rule for <see cref="OperatorFireMode"/>: the brain's latch and the station's HUD ask it.</summary>
    public static class OperatorFireModes
    {
        /// <summary>
        /// LOCK when <paramref name="mount"/> is fought through a turret <paramref name="unit"/> has (a slow turret, a remote weapon station),
        /// is a thrown / arc weapon (Grenade, Mortar, Howitzer, Bomb, or an <c>Indirect</c> warhead) or a guided one (Atgm, Sam, or a guided
        /// round loaded — <c>NeedsLock</c>); SIGHT for everything else (hand weapons, a pintle or tripod MG, an unguided rocket). 🔒 User,
        /// 2026-10-16: <i>"The locking mode is good for grenade throwing and grenade launcher and mortar and howitzer and similar"</i> ·
        /// <i>"locking mode for slow turrets as well"</i> · <i>"RPG-type unguided rockets: SIGHT"</i>.
        /// </summary>
        public static OperatorFireMode Of(EntityRepository world, Entity unit, WeaponMountDto? mount, long loadedAmmo = 0)
        {
            if (mount == null) return OperatorFireMode.Sight;
            if (mount.TurretIndex >= 0 && world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()
                && !TurretParts.Find(world, unit, mount.TurretIndex).IsNull) return OperatorFireMode.Lock;
            if (mount.Category is WeaponCategory.Grenade or WeaponCategory.Mortar or WeaponCategory.Howitzer or WeaponCategory.Bomb
                               or WeaponCategory.Atgm or WeaponCategory.Sam) return OperatorFireMode.Lock;
            if (CombatTkb.WarheadOf(world, loadedAmmo != 0 ? loadedAmmo : unchecked((long)mount.AmmoGuid)) is { Indirect: true }) return OperatorFireMode.Lock;
            return Executors.AimAndFireExecutor.NeedsLock(world, mount, loadedAmmo) ? OperatorFireMode.Lock : OperatorFireMode.Sight;
        }
    }
}
