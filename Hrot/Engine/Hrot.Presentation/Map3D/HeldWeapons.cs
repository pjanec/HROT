using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1236 (docs/DESIGN_People_Display.md §5 U7a, R-1022) — what a soldier's weapon LOOKS like in his hands: the DRAWING's own vocabulary,
/// finer than the observer's <see cref="ItemLook"/> (Civilians AW-15 files a machine gun, an ATGM and a SAM under "Rifle" on purpose — what an
/// AI concludes it sees), read from data every node has: the mount's category, its scope, its munition's shape.
/// </summary>
public enum HeldLook : byte
{
    /// <summary>The figure's rifle (the default — an assault rifle or carbine).</summary>
    Rifle,
    /// <summary>A pistol in one hand, the other hand on it.</summary>
    Pistol,
    /// <summary>A belt-fed gun: longer, heavier, a box under it, a folded bipod.</summary>
    MachineGun,
    /// <summary>A sniper / marksman rifle: longer, a scope on top.</summary>
    ScopedRifle,
    /// <summary>An RPG-class launcher: a slim tube with a bulbous warhead at its front.</summary>
    Rpg,
    /// <summary>A missile tube (Javelin, NLAW): the thick tube N-4 drew on the shoulder, a sight box on its side.</summary>
    MissileTube,
    /// <summary>A MANPADS (Igla, Stinger): a long thin tube with a grip box under it and a seeker cap.</summary>
    Manpads,
    /// <summary>A mortar the MAN carries as his own mount (MortarTeam 2005): deployed on the ground in front of him while his weapon is
    /// up, on his back at ease.</summary>
    Mortar,
}

/// <summary>⭐ CE-1173 / CE-1174 (People Display §5 U12, U13) — what a held weapon's colour must tell the truth about.</summary>
public enum HeldMarking : byte
{
    None,
    /// <summary>U12 — it LOOKS like a rifle or pistol and fires nothing: drawn orange (a toy's body and safety tip).</summary>
    Toy,
    /// <summary>U13 — it fires only non-lethal rounds (the baton-round launcher): a yellow band round it.</summary>
    LessLethal,
}

/// <summary>⭐ CE-1236 (People Display §5 U7a) — reading a soldier's held and slung weapons from the replicated
/// <see cref="Fdp.Toolkit.Combat.Components.WeaponPosture.ActiveMount"/> and his type's mounts. Pure: the rails call it without a GL context.</summary>
public static class HeldWeapons
{
    /// <summary>The look of <paramref name="mount"/> (null ⇒ the rifle).</summary>
    public static HeldLook LookOf(ITkbDatabase? tkb, WeaponMountDto? mount)
    {
        if (mount == null) return HeldLook.Rifle;
        if (mount.Appearance?.Look == ItemLook.Pistol) return HeldLook.Pistol;
        if (MunitionTkbCatalog.IsMortar(tkb, mount)) return HeldLook.Mortar;   // ⭐ CE-1238 — the one rule the brain reads too
        var shape = MunitionTkbCatalog.VisualOf(tkb, unchecked((long)mount.AmmoGuid))?.Shape;
        switch (mount.Category)
        {
            case WeaponCategory.Sam: return HeldLook.Manpads;
            case WeaponCategory.Atgm: return shape == MunitionShape.Rocket ? HeldLook.Rpg : HeldLook.MissileTube;
            case WeaponCategory.MachineGun: return HeldLook.MachineGun;
        }
        if (shape == MunitionShape.Rocket) return HeldLook.Rpg;            // the insurgent's RPG names no category (its round says it)
        if (shape == MunitionShape.Missile) return HeldLook.MissileTube;
        if (mount.ScopeMagnification > 1f) return HeldLook.ScopedRifle;
        return HeldLook.Rifle;
    }

    /// <summary>The look of a weapon SYSTEM of <paramref name="kind"/> held in a man's hands (a carried Igla, a pick-up rifle).</summary>
    public static HeldLook LookOf(WeaponKind kind) => kind switch
    {
        WeaponKind.Manpads => HeldLook.Manpads,
        WeaponKind.Launcher => HeldLook.Rpg,
        WeaponKind.Atgm => HeldLook.MissileTube,
        WeaponKind.Pistol => HeldLook.Pistol,
        _ => HeldLook.Rifle,
    };

    /// <summary>
    /// ⭐ CE-1173 (U12) — a TOY: a mount whose appearance is a rifle or a pistol and that names no round at all (G6: "looks like a rifle
    /// and fires nothing"). ⚠ Only a mount that STATES its appearance: an old mount that names no round is a weapon
    /// (<c>ItemAppearances.FiresLethal</c>'s rule), never a toy.
    /// </summary>
    public static bool IsToy(WeaponMountDto? mount)
        => mount?.Appearance is { Look: ItemLook.Rifle or ItemLook.Pistol } && mount.AmmoGuid == 0 && mount.Loadout.All(l => l.AmmoGuid == 0);

    /// <summary>
    /// ⭐ CE-1173 / CE-1174 — the marking of a held <paramref name="mount"/>: <see cref="HeldMarking.Toy"/> (<see cref="IsToy"/>),
    /// <see cref="HeldMarking.LessLethal"/> when every round it names is non-lethal (<c>ItemAppearances.FiresLethal</c> — Blunt, Irritant,
    /// Stun, Smoke), else none. The hands mount and a grenade are thrown, not held as a weapon: none.
    /// </summary>
    public static HeldMarking MarkingOf(EntityRepository world, WeaponMountDto? mount)
    {
        if (mount == null || mount.Hands || mount.Category is WeaponCategory.Grenade or WeaponCategory.Countermeasure) return HeldMarking.None;
        if (IsToy(mount)) return HeldMarking.Toy;
        bool namesRound = mount.AmmoGuid != 0 || mount.Loadout.Any(l => l.AmmoGuid != 0);
        return namesRound && !Fdp.Toolkit.Combat.ItemAppearances.FiresLethal(world, mount) ? HeldMarking.LessLethal : HeldMarking.None;
    }

    /// <summary>The mount in <paramref name="man"/>'s hands when it is one of his OWN (<c>ActiveMount</c> below 32); null for a weapon
    /// system he controls (its own kit draws it, coloured by its type).</summary>
    public static WeaponMountDto? OwnMountInHands(EntityRepository world, Entity man)
    {
        int active = world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()
                     && world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>(man)
            ? world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(man).ActiveMount : 0;
        return Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(active) ? null : Fdp.Toolkit.Combat.CombatTkb.MountOf(world, man, active);
    }

    /// <summary>A look that is a TUBE (it replaces the rifle along its line, and on the shoulder when aimed).</summary>
    public static bool IsTube(HeldLook look) => look is HeldLook.Rpg or HeldLook.MissileTube or HeldLook.Manpads;

    /// <summary>A look worth showing slung when it is NOT in his hands: a tube or a mortar (a spare rifle or pistol is not drawn).</summary>
    public static bool IsSlungShown(HeldLook look) => IsTube(look) || look == HeldLook.Mortar;

    /// <summary>
    /// What <paramref name="man"/> has in his hands (<c>ActiveMount</c>: his own mount, or 32 + k a weapon system he controls) and the first
    /// launcher or mortar of his OWN that is not (drawn slung on his back; <see cref="HeldLook.Rifle"/> = none).
    /// <paramref name="fromLoad"/> is the controlled weapon system when the held look IS that entity (its carried kit is then not drawn).
    /// </summary>
    public static (HeldLook InHands, HeldLook Slung) Of(EntityRepository world, Entity man, ITkbDatabase? tkb, out Entity fromLoad)
    {
        fromLoad = Entity.Null;
        int active = world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponPosture>()
                     && world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>(man)
            ? world.GetComponentRO<Fdp.Toolkit.Combat.Components.WeaponPosture>(man).ActiveMount : 0;
        HeldLook inHands;
        if (Fdp.Toolkit.Combat.WeaponSystems.IsControlledIndex(active))
        {
            var held = Fdp.Toolkit.Combat.WeaponSystems.ControlledAt(world, man, active);
            inHands = held.IsNull ? LookOf(tkb, Fdp.Toolkit.Combat.CombatTkb.MountOf(world, man, 0)) : LookOf(WeaponKits.TypeOf(world, tkb, held).Kind);
            if (!held.IsNull && IsTube(inHands)) fromLoad = held;
        }
        else inHands = LookOf(tkb, Fdp.Toolkit.Combat.CombatTkb.MountOf(world, man, active));
        var slung = HeldLook.Rifle;
        for (int i = 0; i < 8; i++)
        {
            if (i == active) continue;
            var mount = Fdp.Toolkit.Combat.CombatTkb.MountOf(world, man, i);
            if (mount == null) break;
            var look = LookOf(tkb, mount);
            if (IsSlungShown(look) && look != inHands) { slung = look; break; }
        }
        return (inHands, slung);
    }
}
