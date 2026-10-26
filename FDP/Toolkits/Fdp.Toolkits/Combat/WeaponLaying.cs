using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3232</c> (B-10, AW-5; docs/DESIGN_Civilians_And_Crowd_Control.md §7.11 LD-1) — which of a man's BUILT-IN mounts he has LAID
    /// DOWN (bit i = mount i). On the man, on his brain's node — the truth; every other node reads the bit from <see cref="WeaponAmmo"/>
    /// (mirrored by <c>WeaponAmmoMirrorSystem</c>). ⚠ Not a field in <see cref="WeaponState"/>: that struct is saved and recorded as raw
    /// bytes, so its size is in every file.
    /// </summary>
    [ComponentId(GlobalComponentIds.LaidDownWeapons)]
    [StructLayout(LayoutKind.Sequential)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct LaidDownWeapons
    {
        /// <summary>Bit i set: mount i is laid down.</summary>
        public byte Mounts;
    }

    /// <summary>
    /// ⭐ <c>CE-3232</c> (B-10, §7.11) — a weapon LAID DOWN (surrender, AW-5): it cannot fire and it arms nobody. ⭐ The ONE reader
    /// (<see cref="IsLaidDown"/>) answers on every node — the brain's own mask where the brain runs, the replicated <see cref="WeaponAmmo"/> bit
    /// everywhere else — so no gate asks which node it is on. The hands are never laid down (they are no weapon, B-8).
    /// </summary>
    public static class WeaponLaying
    {
        /// <summary>The mounts a mask can name (a byte).</summary>
        public const int MaxMounts = 8;

        /// <summary>Lay mount <paramref name="mount"/> of <paramref name="man"/> down (or take it up again). On his brain's node.</summary>
        public static bool LayDown(EntityRepository world, Entity man, int mount, bool down = true)
        {
            if (mount < 0 || mount >= MaxMounts || !world.IsAlive(man)) return false;
            if (!world.IsComponentTypeRegistered<LaidDownWeapons>()) world.RegisterComponent<LaidDownWeapons>();
            byte bit = (byte)(1 << mount);
            if (!world.HasComponent<LaidDownWeapons>(man)) world.AddComponent(man, default(LaidDownWeapons));
            ref var laid = ref world.GetComponentRW<LaidDownWeapons>(man);
            laid.Mounts = down ? (byte)(laid.Mounts | bit) : (byte)(laid.Mounts & ~bit);
            return true;
        }

        /// <summary>Lay every built-in WEAPON mount of <paramref name="man"/> down (or take them up) — the hands excepted. Returns how many.</summary>
        public static int LayDownAll(EntityRepository world, Entity man, bool down = true)
        {
            int n = 0;
            int count = MountCount(world, man);
            for (int i = 0; i < count && i < MaxMounts; i++)
            {
                if (PersonHands.IsHands(CombatTkb.MountOf(world, man, i))) continue;
                if (LayDown(world, man, i, down)) n++;
            }
            return n;
        }

        /// <summary>
        /// Is mount <paramref name="mount"/> of <paramref name="man"/> laid down — on his brain's node from his own mask, on any other node
        /// from the replicated <see cref="WeaponAmmo"/> (mounts 0…3; a higher one reads not laid down there — the stated limit).
        /// </summary>
        public static bool IsLaidDown(ISimulationView view, Entity man, int mount)
        {
            if (mount < 0 || mount >= MaxMounts || view is not EntityRepository world || !world.IsAlive(man)) return false;
            if (OwnsBrain(world, man) || !world.IsComponentTypeRegistered<WeaponAmmo>() || !world.HasComponent<WeaponAmmo>(man))
                return world.IsComponentTypeRegistered<LaidDownWeapons>() && world.HasComponent<LaidDownWeapons>(man)
                       && (world.GetComponentRO<LaidDownWeapons>(man).Mounts & (1 << mount)) != 0;
            return world.GetComponentRO<WeaponAmmo>(man).IsLaidDown(mount);
        }

        /// <summary>Has <paramref name="man"/> laid down EVERY built-in weapon mount (at least one; the hands do not count)?</summary>
        public static bool AllLaidDown(ISimulationView view, Entity man)
        {
            if (view is not EntityRepository world || !world.IsAlive(man)) return false;
            int count = MountCount(world, man);
            int weapons = 0;
            for (int i = 0; i < count && i < MaxMounts; i++)
            {
                if (PersonHands.IsHands(CombatTkb.MountOf(world, man, i))) continue;
                weapons++;
                if (!IsLaidDown(world, man, i)) return false;
            }
            return weapons > 0;
        }

        /// <summary>His type's built-in mounts (0 without a suite).</summary>
        private static int MountCount(EntityRepository world, Entity man)
            => CombatTkb.TryGetTemplate(world, man, out var t) && t.GetDescriptor<WeaponSuiteDto>() is { } suite ? suite.Mounts.Count : 0;

        private static bool OwnsBrain(EntityRepository world, Entity man)
            => world.IsComponentTypeRegistered<BehaviorState>() && world.HasComponent<BehaviorState>(man) && world.HasAuthority<BehaviorState>(man);
    }
}
