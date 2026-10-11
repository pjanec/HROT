using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐ <c>CE-3174</c> (C-7; <c>docs/DESIGN_Entity_Control.md</c> §8d) — a unit's ammunition, per mount (up to <see cref="MaxMounts"/>),
    /// as the possessing station's HUD shows it. Written ONLY by <see cref="Systems.WeaponAmmoMirrorSystem"/> on the brain (where the
    /// <see cref="WeaponState"/>s live), Brain group, replicated as <c>dtWeaponAmmo</c> — a station on another node has no WeaponState.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponAmmo)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct WeaponAmmo
    {
        public const int MaxMounts = 4;

        /// <summary>Mounts carried (0 – <see cref="MaxMounts"/>).</summary>
        public byte Count;
        public int Ammo0, Ammo1, Ammo2, Ammo3;
        public int Max0, Max1, Max2, Max3;
        /// <summary>Rounds in the current magazine per mount (−1 = no magazine model).</summary>
        public int Mag0, Mag1, Mag2, Mag3;

        public readonly int AmmoOf(int mount) => mount switch { 0 => Ammo0, 1 => Ammo1, 2 => Ammo2, 3 => Ammo3, _ => 0 };
        public readonly int MaxOf(int mount) => mount switch { 0 => Max0, 1 => Max1, 2 => Max2, 3 => Max3, _ => 0 };
        public readonly int MagazineOf(int mount) => mount switch { 0 => Mag0, 1 => Mag1, 2 => Mag2, 3 => Mag3, _ => -1 };

        public void Set(int mount, in WeaponState w)
        {
            int mag = w.MagazineSize > 0 ? w.MagazineRounds : -1;
            switch (mount)
            {
                case 0: Ammo0 = w.Ammo; Max0 = w.MaxAmmo; Mag0 = mag; break;
                case 1: Ammo1 = w.Ammo; Max1 = w.MaxAmmo; Mag1 = mag; break;
                case 2: Ammo2 = w.Ammo; Max2 = w.MaxAmmo; Mag2 = mag; break;
                case 3: Ammo3 = w.Ammo; Max3 = w.MaxAmmo; Mag3 = mag; break;
            }
        }
    }
}
