using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Look.Hud;

/// <summary>
/// ⭐ CE-11005 (RR-5, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — the WORDS the possession HUD shows: a weapon's kind, a round's
/// name, a unit's type name, an action's label and why it cannot be done. Moved verbatim from the map (<c>PossessionStation.Hud</c>, CE-1079, CE-1158,
/// CE-1207), which now forwards here, so a node extension's HUD says exactly what HROT's says. ⛔ No colours, no layout — those stay the station's.
/// </summary>
public static class HudWords
{
    /// <summary>A weapon category's HUD word (<c>RIFLE</c>, <c>MG</c>, <c>CANNON</c>, … <c>WEAPON</c>).</summary>
    public static string KindOf(WeaponCategory c) => c switch
    {
        WeaponCategory.SmallArms => "RIFLE",
        WeaponCategory.MachineGun => "MG",
        WeaponCategory.Autocannon => "AUTOCANNON",
        WeaponCategory.TankGun => "CANNON",
        WeaponCategory.Atgm => "MISSILE",
        WeaponCategory.Mortar => "MORTAR",
        WeaponCategory.Grenade => "GRENADE",
        WeaponCategory.Bomb => "BOMB",          // ⭐ Air Ops
        WeaponCategory.Rocket => "ROCKETS",     // ⭐ Air Ops 6a (AW-3)
        WeaponCategory.Howitzer => "HOWITZER",  // ⭐ IF-1 (I8)
        WeaponCategory.Sam => "SAM",            // ⭐ CE-2187
        _ => "WEAPON",
    };

    /// <summary>⭐ CE-1079 — a round's name from the TKB (its munition template; a trailing " round" dropped), or null.</summary>
    public static string? RoundName(EntityRepository w, long ammo)
    {
        if (ammo == 0 || !w.HasSingletonManaged<ITkbDatabase>()) return null;
        if (!w.GetSingletonManaged<ITkbDatabase>()!.TryGetByType(ammo, out var t) || t == null) return null;
        return t.Name.EndsWith(" round", StringComparison.Ordinal) ? t.Name[..^6] : t.Name;
    }

    /// <summary>⭐ CE-1158 — a unit's type name from the TKB (<c>BMP-2</c>, <c>ZU-23-2</c>), or null.</summary>
    public static string? TypeName(EntityRepository w, Entity e)
    {
        if (e.IsNull || !w.IsAlive(e) || !w.IsComponentTypeRegistered<TkbIdentity>()
            || !w.HasComponent<TkbIdentity>(e) || !w.HasSingletonManaged<ITkbDatabase>()) return null;
        long type = w.GetComponentRO<TkbIdentity>(e).TkbType;
        return w.GetSingletonManaged<ITkbDatabase>()!.TryGetByType(type, out var t) && t != null ? t.Name : null;
    }

    /// <summary>⭐ CE-1158 (§8h U-1) — an action as the HUD names it: <c>Open door</c>, <c>Drag</c>, <c>Drive BMP-2</c>, <c>Man ZU-23-2</c>.</summary>
    public static string Label(EntityRepository w, in UseAction a)
        => a.Verb is UseVerbKind.Drive or UseVerbKind.Pilot or UseVerbKind.Board or UseVerbKind.Man
                 or UseVerbKind.Take or UseVerbKind.Drop or UseVerbKind.PickUpRound && TypeName(w, a.Target) is { } name   // ⭐ CE-1207
            ? $"{a.Label} {name}" : a.Label;

    /// <summary>⭐ CE-1158 (§8h U-3) — why an action cannot be done, in the HUD's words.</summary>
    public static string ReasonText(UseReason r) => r switch
    {
        UseReason.OutOfReach => "too far",
        UseReason.Locked => "locked",
        UseReason.DoorRefuses => "will not move",
        UseReason.NotDown => "not down",
        UseReason.SeatTaken => "seat taken",
        UseReason.OtherSide => "enemy aboard",   // ⭐ R-4096
        UseReason.Unfit => "you are down",
        UseReason.Carrying => "hands full",
        UseReason.NothingToDo => "nothing to do",
        UseReason.Failed => "failed",
        _ => "",
    };
}
