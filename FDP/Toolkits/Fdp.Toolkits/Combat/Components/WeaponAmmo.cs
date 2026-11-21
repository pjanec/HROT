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
    public struct WeaponAmmo : System.IEquatable<WeaponAmmo>
    {
        public const int MaxMounts = 4;

        /// <summary>
        /// ⭐ <c>CE-4366</c> (L-4, docs/DESIGN_Scale_Rig.md §5) — the bits compared, as <see cref="System.ValueType.Equals(object)"/> compared
        /// them for this all-primitive struct — but with no boxing: the mirror asked <c>Equals(object)</c> for every unit every frame (two
        /// boxes each; 0.2 MB per frame at 96 men, 2.3 MB at 1 000 — the brain's biggest garbage).
        /// </summary>
        public readonly bool Equals(WeaponAmmo other)
            => MemoryMarshal.AsBytes(new System.ReadOnlySpan<WeaponAmmo>(in this)).SequenceEqual(MemoryMarshal.AsBytes(new System.ReadOnlySpan<WeaponAmmo>(in other)));

        public override readonly bool Equals(object? obj) => obj is WeaponAmmo o && Equals(o);
        public override readonly int GetHashCode() => System.HashCode.Combine(Count, Ammo0, Ammo1, Load0, Busy, Hot, LastShotAt0);

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
        /// <summary>⛔ <c>CE-4659</c> (thermal drivers TD-5, §3d): RETIRED — never written (0). Was each mount's barrel heat sampled as a percent, which sent a
        /// sample per percent while a barrel cooled; the heat is now COMPUTED from the last burst (<see cref="HeatPercentAt"/>). Kept for the layout. Appended.</summary>
        public byte Heat0, Heat1, Heat2, Heat3;

        // ── ⭐ CE-4659 (thermal drivers TD-5, §3d; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4f) — each mount's LAST BURST: a driver, never a temperature.
        //    Written by the mirror on a shot only (the round count already makes that a sample, so the burst adds none), and nothing while a barrel cools.
        /// <summary>Rounds of the mount's current / last burst (shots less than <see cref="Systems.WeaponAmmoMirrorSystem.BurstGapSeconds"/> apart).</summary>
        public int BurstRounds0, BurstRounds1, BurstRounds2, BurstRounds3;
        /// <summary>Sim time (s) the burst began / its last shot; −∞ = never fired.</summary>
        public double BurstStartedAt0, BurstStartedAt1, BurstStartedAt2, BurstStartedAt3;
        public double LastShotAt0, LastShotAt1, LastShotAt2, LastShotAt3;
        /// <summary>The barrel's heat (0..1 of too hot) just after the last shot.</summary>
        public float HeatAtLastShot0, HeatAtLastShot1, HeatAtLastShot2, HeatAtLastShot3;
        /// <summary>Heat the barrel loses per second (the mount's TKB rule — a reader on another node has no <see cref="WeaponState"/>). ⚠ beyond §3d's four fields:
        /// the closed form needs the rate.</summary>
        public float CoolPerSecond0, CoolPerSecond1, CoolPerSecond2, CoolPerSecond3;

        public readonly int AmmoOf(int mount) => mount switch { 0 => Ammo0, 1 => Ammo1, 2 => Ammo2, 3 => Ammo3, _ => 0 };
        public readonly int MaxOf(int mount) => mount switch { 0 => Max0, 1 => Max1, 2 => Max2, 3 => Max3, _ => 0 };
        public readonly int MagazineOf(int mount) => mount switch { 0 => Mag0, 1 => Mag1, 2 => Mag2, 3 => Mag3, _ => -1 };
        public readonly long LoadedOf(int mount) => mount switch { 0 => Load0, 1 => Load1, 2 => Load2, 3 => Load3, _ => 0L };
        public readonly int LoadedRoundsOf(int mount) => mount switch { 0 => LoadRounds0, 1 => LoadRounds1, 2 => LoadRounds2, 3 => LoadRounds3, _ => -1 };
        public readonly bool IsBusy(int mount) => mount is >= 0 and < MaxMounts && (Busy & (1 << mount)) != 0;
        public readonly bool IsHot(int mount) => mount is >= 0 and < MaxMounts && (Hot & (1 << mount)) != 0;
        /// <summary>⭐ CE-4659 — mount <paramref name="mount"/>'s barrel heat (0..1 of too hot) at <paramref name="now"/>, computed from its last shot
        /// (<see cref="BarrelHeat.At"/>) — the same on every node.</summary>
        public readonly float HeatAt(int mount, double now) => mount switch
        {
            0 => BarrelHeat.At(HeatAtLastShot0, LastShotAt0, CoolPerSecond0, now),
            1 => BarrelHeat.At(HeatAtLastShot1, LastShotAt1, CoolPerSecond1, now),
            2 => BarrelHeat.At(HeatAtLastShot2, LastShotAt2, CoolPerSecond2, now),
            3 => BarrelHeat.At(HeatAtLastShot3, LastShotAt3, CoolPerSecond3, now),
            _ => 0f,
        };

        /// <summary>⭐ CE-4659 — <see cref="HeatAt"/> as the HUD's percent (0..100).</summary>
        public readonly int HeatPercentAt(int mount, double now) => System.Math.Clamp((int)System.MathF.Round(HeatAt(mount, now) * 100f), 0, 100);

        /// <summary>⭐ CE-4659 — the hottest barrel of the unit at <paramref name="now"/> (0..1).</summary>
        public readonly float HottestBarrelAt(double now)
        {
            float best = 0f;
            for (int m = 0; m < Count && m < MaxMounts; m++) best = System.MathF.Max(best, HeatAt(m, now));
            return best;
        }

        public readonly int BurstRoundsOf(int mount) => mount switch { 0 => BurstRounds0, 1 => BurstRounds1, 2 => BurstRounds2, 3 => BurstRounds3, _ => 0 };
        public readonly double BurstStartedAtOf(int mount) => mount switch { 0 => BurstStartedAt0, 1 => BurstStartedAt1, 2 => BurstStartedAt2, 3 => BurstStartedAt3, _ => double.NegativeInfinity };
        public readonly double LastShotAtOf(int mount) => mount switch { 0 => LastShotAt0, 1 => LastShotAt1, 2 => LastShotAt2, 3 => LastShotAt3, _ => double.NegativeInfinity };

        /// <summary>⭐ CE-4659 — sets mount <paramref name="mount"/>'s burst record.</summary>
        public void SetBurst(int mount, int rounds, double startedAt, double lastShotAt, float heatAtLastShot, float coolPerSecond)
        {
            switch (mount)
            {
                case 0: BurstRounds0 = rounds; BurstStartedAt0 = startedAt; LastShotAt0 = lastShotAt; HeatAtLastShot0 = heatAtLastShot; CoolPerSecond0 = coolPerSecond; break;
                case 1: BurstRounds1 = rounds; BurstStartedAt1 = startedAt; LastShotAt1 = lastShotAt; HeatAtLastShot1 = heatAtLastShot; CoolPerSecond1 = coolPerSecond; break;
                case 2: BurstRounds2 = rounds; BurstStartedAt2 = startedAt; LastShotAt2 = lastShotAt; HeatAtLastShot2 = heatAtLastShot; CoolPerSecond2 = coolPerSecond; break;
                case 3: BurstRounds3 = rounds; BurstStartedAt3 = startedAt; LastShotAt3 = lastShotAt; HeatAtLastShot3 = heatAtLastShot; CoolPerSecond3 = coolPerSecond; break;
            }
        }

        /// <summary>A record with no mount ever fired (its times −∞).</summary>
        public static WeaponAmmo Empty()
        {
            var a = new WeaponAmmo();
            for (int m = 0; m < MaxMounts; m++) a.SetBurst(m, 0, double.NegativeInfinity, double.NegativeInfinity, 0f, 0f);
            return a;
        }
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
            // ⭐ CE-1358 — too hot (a latch: changes twice per overheat). ⛔ CE-4659: the sampled heat percent retired — the HUD computes it (HeatPercentAt)
            if (w.Overheated != 0) Hot |= (byte)(1 << mount); else Hot &= (byte)~(1 << mount);
        }
    }
}
