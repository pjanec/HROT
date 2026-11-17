using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐ <c>CE-4650</c> (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §9 NA-1, R-4281 — the FUN pack: content separate, engine shared) — the fun pack's TKB types. Today ONE: the <b>Native American warrior</b> (2016): the bow
    /// (<see cref="UrbanCombatTkbCatalog.Bow"/>, mount 0) and a thrown tomahawk (the throwing hatchet, <see cref="MunitionTkbCatalog.TkbThrowingHatchet"/>, CE-3247 — mount 1), no armour, the INSURGENT's template and temperament
    /// (<see cref="UrbanCombatTkbCatalog.Insurgent"/> — no descriptor is copied here), his wardrobe <see cref="OutfitCatalog.NativeWarrior"/>. On a horse he is the same type (703, the firing seat, R-4269). ⚠ No knife-range melee: the engine has no
    /// melee MOUNT (<c>Melee</c> is the blow of a man's fists and a dog's bite), so none is declared.
    /// </summary>
    public static class FunTkbCatalog
    {
        /// <summary>The Native American warrior — the next free person type after the archer (2013), crossbowman (2014) and archery target (2015), measured free on every lane 2026-11-17.</summary>
        public const int TkbNativeWarrior = 2016;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new System.ArgumentNullException(nameof(tkb));
            UrbanCombatTkbCatalog.Insurgent(tkb, "Native American warrior", TkbNativeWarrior, OutfitCatalog.NativeWarrior, UrbanCombatTkbCatalog.Bow(), Tomahawk());
        }

        /// <summary>⭐ NA-1 — the TOMAHAWK as a mount: the thrown hatchet, two of them, thrown at 15 m/s inside 25 m (the grenade's throw).</summary>
        public static WeaponMountDto Tomahawk() => new()
        {
            Category = WeaponCategory.Grenade, InitialAmmunition = 2, MuzzleVelocity = 15f, Range = 25f, AmmoGuid = MunitionTkbCatalog.TkbThrowingHatchet,
        };
    }
}
