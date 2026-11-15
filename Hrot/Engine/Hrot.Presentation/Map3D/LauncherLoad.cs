using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4618 (docs/DESIGN_Map_3D_Mode.md §6al.1, the first AMMUNITION-DRIVEN 3-D visual) — the 3-D map's question "how many rounds does mount <c>m</c> of this entity still
/// hold?", read from the replicated <see cref="WeaponAmmo"/> (written on the brain by <c>WeaponAmmoMirrorSystem</c>, replicated as <c>dtWeaponAmmo</c> — so every node, a
/// display node included, has it). It draws the radar SAM launcher's covers (<see cref="KitPart.Round"/>); ⭐ the same reader serves any other round-count visual (a rocket
/// pod, an ATGM tube). ⛔ No allocation: a static call over the component.
/// </summary>
public static class LauncherLoad
{
    /// <summary>"Full" — what a unit with no <see cref="WeaponAmmo"/> yet (a scenario being edited, a world that never registered it) shows: every part drawn.</summary>
    public const int Full = int.MaxValue;

    /// <summary>The rounds left in mount <paramref name="mount"/> of <paramref name="e"/>; <see cref="Full"/> when nothing says (no component, or the mount is not in it).</summary>
    public static int RoundsLeft(EntityRepository world, Entity e, int mount)
    {
        if (!world.IsComponentTypeRegistered<WeaponAmmo>() || !world.HasComponent<WeaponAmmo>(e)) return Full;
        ref readonly var ammo = ref world.GetComponentRO<WeaponAmmo>(e);
        return mount >= 0 && mount < ammo.Count ? ammo.AmmoOf(mount) : Full;
    }
}
