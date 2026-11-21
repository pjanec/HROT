namespace Hrot.UI.Common.Map3D;   // ⭐ CE-4671 LK-S1 — kept with the move

/// <summary>⭐ CE-4671 LK-S1 — which air-to-air missile a store fires (moved from the map's <c>JetStores</c>, which forwards): the firing-blast rule
/// gives a fighter's IR or radar missile its launch puff.</summary>
public static class AirMissiles
{
    /// <summary>An infrared-guided air-to-air missile (AIM-9X, R-73).</summary>
    public static bool IsIr(ulong ammo) => ammo == (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbAim9x || ammo == (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbR73;

    /// <summary>A radar-guided air-to-air missile (AIM-120, R-77).</summary>
    public static bool IsRadar(ulong ammo) => ammo == (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbAim120 || ammo == (ulong)Hrot.Core.Tkb.MunitionTkbCatalog.TkbR77;
}
