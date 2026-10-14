using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// ⭐⭐ M8 (D-I; <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §2) — the ROUNDS a mount carries when its TKB mount
    /// declares a <c>Loadout</c> (a tank gun: APFSDS, HEAT, HE): up to <see cref="MaxTypes"/> munition types and how many of each, the one
    /// LOADED now and the switch running. Beside the mount's <see cref="WeaponState"/>, which stays the magazine and the TOTAL
    /// (<see cref="WeaponState.Ammo"/> = the sum of <c>Rounds</c>). On the mount entity — the owner for mount 0, the mount child
    /// otherwise — on the Brain, like <see cref="WeaponState"/>. A mount with no loadout has no component and fires its one
    /// <c>AmmoGuid</c>, as before. The weapon brain (<see cref="AmmoChoice"/>) picks the round; the behaviour never names one (R-261).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.AmmoLoadout)]
    public struct AmmoLoadout
    {
        public const int MaxTypes = 4;

        /// <summary>Munition types carried (0 – <see cref="MaxTypes"/>).</summary>
        public byte Count;

        /// <summary>The index of the round in the breech.</summary>
        public byte Loaded;

        /// <summary>Seconds left of a round switch (one reload, D-I); 0 = ready. Drained by <c>WeaponDispatcherSystem</c>.</summary>
        public float SwitchRemaining;

        /// <summary>The target the loaded round was chosen for — the choice is made once per engagement.</summary>
        public Entity ChosenFor;

        public long Ammo0, Ammo1, Ammo2, Ammo3;
        public int Rounds0, Rounds1, Rounds2, Rounds3;

        public readonly long AmmoAt(int i) => i switch { 0 => Ammo0, 1 => Ammo1, 2 => Ammo2, 3 => Ammo3, _ => 0L };
        public readonly int RoundsAt(int i) => i switch { 0 => Rounds0, 1 => Rounds1, 2 => Rounds2, 3 => Rounds3, _ => 0 };

        public void SetRounds(int i, int n)
        {
            switch (i) { case 0: Rounds0 = n; break; case 1: Rounds1 = n; break; case 2: Rounds2 = n; break; case 3: Rounds3 = n; break; }
        }

        /// <summary>The munition type in the breech (0 = none).</summary>
        public readonly long LoadedAmmo => Count > 0 ? AmmoAt(Loaded) : 0L;

        /// <summary>Rounds of every type together.</summary>
        public readonly int Total => RoundsAt(0) + RoundsAt(1) + RoundsAt(2) + RoundsAt(3);

        /// <summary>The loadout a TKB mount declares (the first <see cref="MaxTypes"/> entries), the first one loaded.</summary>
        public static AmmoLoadout From(IReadOnlyList<Fdp.Toolkit.Tkb.Domain.AmmoLoadDto> loads)
        {
            var l = new AmmoLoadout();
            for (int i = 0; i < loads.Count && l.Count < MaxTypes; i++)
            {
                int k = l.Count++;
                switch (k)
                {
                    case 0: l.Ammo0 = unchecked((long)loads[i].AmmoGuid); break;
                    case 1: l.Ammo1 = unchecked((long)loads[i].AmmoGuid); break;
                    case 2: l.Ammo2 = unchecked((long)loads[i].AmmoGuid); break;
                    case 3: l.Ammo3 = unchecked((long)loads[i].AmmoGuid); break;
                }
                l.SetRounds(k, System.Math.Max(0, loads[i].Rounds));
            }
            return l;
        }
    }
}
