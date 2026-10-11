using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ WL-3 (docs/DESIGN_Weapon_Layers.md W-5, §5e) — a weapon brain's per-category behaviour: a HARDCODED class per
    /// <see cref="WeaponCategory"/>, chosen through <see cref="WeaponBrainProfiles.For"/>. 🔒 U-W1: <i>"a hardcoded system customizable
    /// per weapon category"</i>. WL-3 has one question; WL-4 adds mount and ammo choice per category.
    /// </summary>
    public interface IWeaponBrainProfile
    {
        /// <summary>True when this mount is fought through its turret's <see cref="WeaponTask"/> (the brain grants, the muscle
        /// aims and fires); false keeps the per-shot path with its aim time (U-W1: <i>"AimSeconds can stay for simplest weapon
        /// brains not modelling turrets"</i>).</summary>
        bool UsesTurretTask(EntityRepository world, Entity unit, WeaponMountDto? mount);
    }

    /// <summary>⭐ WL-3 — the default profile: a mount that names a turret the unit has goes through the turret task.</summary>
    public sealed class DefaultWeaponBrainProfile : IWeaponBrainProfile
    {
        public bool UsesTurretTask(EntityRepository world, Entity unit, WeaponMountDto? mount)
            => mount != null && mount.TurretIndex >= 0 && world.IsComponentTypeRegistered<WeaponTask>()
               && !TurretParts.Find(world, unit, mount.TurretIndex).IsNull;
    }

    /// <summary>⭐ WL-3 — the ONE table of weapon-brain profiles, by category. Every category takes the default until WL-4.</summary>
    public static class WeaponBrainProfiles
    {
        public static readonly IWeaponBrainProfile Default = new DefaultWeaponBrainProfile();

        /// <summary>The profile that drives a weapon of <paramref name="category"/>.</summary>
        public static IWeaponBrainProfile For(WeaponCategory category) => Default;
    }
}
