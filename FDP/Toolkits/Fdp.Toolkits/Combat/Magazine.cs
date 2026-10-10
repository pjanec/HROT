using System;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-5 (peek-and-fire D9, R-239) — the MAGAZINE rule, the one place both fire executors and the dispatcher
    /// read it. 🔒 User: <i>"keep Ammo as total carried and add rounds-in-magazine, magazine size and reload time remaining." —
    /// "YES"</i>. <see cref="WeaponState.Ammo"/> stays the TOTAL carried (every reader unchanged); a mount with a
    /// <see cref="WeaponState.MagazineSize"/> fires from <see cref="WeaponState.MagazineRounds"/> and reloads for
    /// <see cref="WeaponState.ReloadSeconds"/> when it empties; a mount without one (size 0) fires from its whole load, as before.
    /// 📄 docs/DESIGN_Peek_And_Fire.md §6 D9, §7 P-5.
    /// </summary>
    public static class Magazine
    {
        /// <summary>A freshly loaded weapon: the first magazine full (or no magazine model when <paramref name="magazineSize"/> ≤ 0).</summary>
        public static void Load(ref WeaponState w, int magazineSize, float reloadSeconds)
        {
            w.MagazineSize = Math.Max(magazineSize, 0);
            w.MagazineRounds = w.MagazineSize > 0 ? Math.Min(w.MagazineSize, w.Ammo) : 0;
            w.ReloadSeconds = w.MagazineSize > 0 ? reloadSeconds : 0f;
            w.ReloadSecondsRemaining = 0f;
        }

        /// <summary>May a round leave now (ammunition left, and — with a magazine — a round in it and no reload running)?</summary>
        public static bool Ready(in WeaponState w)
            => w.Ammo > 0 && (w.MagazineSize <= 0 || (w.ReloadSecondsRemaining <= 0f && w.MagazineRounds > 0));

        /// <summary>True while a reload is running.</summary>
        public static bool Reloading(in WeaponState w) => w.ReloadSecondsRemaining > 0f;

        /// <summary>One round leaves: the total and the magazine drop; an emptied magazine with rounds left starts the reload.</summary>
        public static void Spend(ref WeaponState w)
        {
            w.Ammo--;
            if (w.MagazineSize <= 0) return;
            w.MagazineRounds--;
            StartIfEmpty(ref w);
        }

        /// <summary>An empty magazine with rounds left and no reload running starts one (a weapon loaded empty, or a lost tick).</summary>
        public static void StartIfEmpty(ref WeaponState w)
        {
            if (w.MagazineSize > 0 && w.MagazineRounds <= 0 && w.Ammo > 0 && w.ReloadSecondsRemaining <= 0f)
                w.ReloadSecondsRemaining = MathF.Max(w.ReloadSeconds, float.Epsilon);
        }

        /// <summary>Advances a running reload; when it ends the magazine is filled from what is left.</summary>
        public static void Tick(ref WeaponState w, float dt)
        {
            if (w.ReloadSecondsRemaining <= 0f) return;
            w.ReloadSecondsRemaining -= dt;
            if (w.ReloadSecondsRemaining > 0f) return;
            w.ReloadSecondsRemaining = 0f;
            w.MagazineRounds = Math.Min(w.MagazineSize, w.Ammo);
        }
    }
}
