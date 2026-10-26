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
        /// <summary>⭐ CE-1079 (DESIGN_Entity_Control §8e) — the round in each mount's breech (its munition TKB type; 0 = the mount's own,
        /// no loadout) and how many of that type are left (−1 = no loadout).</summary>
        public long Load0, Load1, Load2, Load3;
        public int LoadRounds0, LoadRounds1, LoadRounds2, LoadRounds3;
        /// <summary>⭐ CE-1079 — bit <c>i</c> set: mount <c>i</c> is reloading or switching its round (the HUD says LOADING).</summary>
        public byte Busy;
        /// <summary>⭐ CE-3232 (B-10, §7.11 LD-2) — bit <c>i</c> set: mount <c>i</c> is LAID DOWN (cannot fire, arms nobody); mirrored from
        /// the brain's <see cref="Fdp.Toolkit.Combat.LaidDownWeapons"/>.</summary>
        public byte LaidDown;
        /// <summary>⭐ <c>CE-1358</c> (DESIGN_Weapon_Layers §5k) — bit i: mount i's barrel is TOO HOT (held until it cools to half). Appended.</summary>
        public byte Hot;
        /// <summary>⭐ <c>CE-1358</c> — each mount's barrel heat, percent of too hot (0..100). Appended.</summary>
        public byte Heat0, Heat1, Heat2, Heat3;

        public readonly int AmmoOf(int mount) => mount switch { 0 => Ammo0, 1 => Ammo1, 2 => Ammo2, 3 => Ammo3, _ => 0 };
        public readonly int MaxOf(int mount) => mount switch { 0 => Max0, 1 => Max1, 2 => Max2, 3 => Max3, _ => 0 };
        public readonly int MagazineOf(int mount) => mount switch { 0 => Mag0, 1 => Mag1, 2 => Mag2, 3 => Mag3, _ => -1 };
        public readonly long LoadedOf(int mount) => mount switch { 0 => Load0, 1 => Load1, 2 => Load2, 3 => Load3, _ => 0L };
        public readonly int LoadedRoundsOf(int mount) => mount switch { 0 => LoadRounds0, 1 => LoadRounds1, 2 => LoadRounds2, 3 => LoadRounds3, _ => -1 };
        public readonly bool IsBusy(int mount) => mount is >= 0 and < MaxMounts && (Busy & (1 << mount)) != 0;
        public readonly bool IsHot(int mount) => mount is >= 0 and < MaxMounts && (Hot & (1 << mount)) != 0;
        public readonly int HeatPercentOf(int mount) => mount switch { 0 => Heat0, 1 => Heat1, 2 => Heat2, 3 => Heat3, _ => 0 };
        public readonly bool IsLaidDown(int mount) => mount is >= 0 and < MaxMounts && (LaidDown & (1 << mount)) != 0;

        public void Set(int mount, in WeaponState w) => Set(mount, w, null);

        /// <summary>⭐ CE-1079 — a mount's counts and, when it carries one, its loadout's breech.</summary>
        public void Set(int mount, in WeaponState w, AmmoLoadout? load)
        {
            int mag = w.MagazineSize > 0 ? w.MagazineRounds : -1;
            long loaded = load is { Count: > 0 } l ? l.LoadedAmmo : 0L;
            int loadedRounds = load is { Count: > 0 } l2 ? l2.RoundsAt(l2.Loaded) : -1;
            bool busy = w.ReloadSecondsRemaining > 0f || load is { SwitchRemaining: > 0f };
            switch (mount)
            {
                case 0: Ammo0 = w.Ammo; Max0 = w.MaxAmmo; Mag0 = mag; Load0 = loaded; LoadRounds0 = loadedRounds; break;
                case 1: Ammo1 = w.Ammo; Max1 = w.MaxAmmo; Mag1 = mag; Load1 = loaded; LoadRounds1 = loadedRounds; break;
                case 2: Ammo2 = w.Ammo; Max2 = w.MaxAmmo; Mag2 = mag; Load2 = loaded; LoadRounds2 = loadedRounds; break;
                case 3: Ammo3 = w.Ammo; Max3 = w.MaxAmmo; Mag3 = mag; Load3 = loaded; LoadRounds3 = loadedRounds; break;
                default: return;
            }
            if (busy) Busy |= (byte)(1 << mount); else Busy &= (byte)~(1 << mount);
            // ⭐ CE-1358 — the barrel, for the station HUD
            byte heat = (byte)System.Math.Clamp((int)System.MathF.Round(w.Heat * 100f), 0, 100);
            switch (mount) { case 0: Heat0 = heat; break; case 1: Heat1 = heat; break; case 2: Heat2 = heat; break; case 3: Heat3 = heat; break; }
            if (w.Overheated != 0) Hot |= (byte)(1 << mount); else Hot &= (byte)~(1 << mount);
        }
    }
}
