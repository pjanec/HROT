using Hrot.Map.Common;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// ⭐⭐ <b>The spawner catalog every windowed host offers.</b> 📄 <c>DESIGN_Cgf_Scenario_Windows_Slice.md</c>.
///
/// <para>📐 <b>Measured <c>2026-08-27</c>:</b> the list was an inline array literal inside
/// <c>EditorSubsystem</c> (15 entries) and a NEAR-duplicate inside <c>ExConSubsystem</c> (9 entries, two
/// labels spelled differently: <c>"Tank Platoon (Empty)"</c> vs <c>"Tank Platoon"</c>). ⇒ a third copy on
/// CGF would have made three, so this is the one list.</para>
///
/// <para>⚠⚠ <b>It carries the EDITOR's list verbatim, and ExCon is deliberately NOT changed.</b> The
/// charter is <i>cgf == editor</i>, so the editor is the reference — ⛔ but whether ExCon's shorter list
/// and different labels are intent or drift is a question for the host that owns that file, and
/// <c>ExConSubsystem</c> is the BACKEND lane's. 📌 Recorded as a finding rather than silently
/// harmonised: quietly rewriting another lane's operator-visible labels is not a wiring fix.</para>
/// </summary>
public static class ScenarioSpawnerCatalog
{
    /// <summary>The entries, in the editor's declared order.</summary>
    public static readonly TkbCatalogEntry[] Default =
    {
        new(TkbEntityTypes.Tank_M1Abrams,         "M1 Abrams"),
        new(TkbEntityTypes.IFV_Bradley,           "M2 Bradley IFV"),
        new(TkbEntityTypes.Truck_HMMWV,           "HMMWV"),
        new(TkbEntityTypes.Tank_T72,              "T-72"),
        new(TkbEntityTypes.Infantry_Rifleman,     "Infantry Rifleman"),
        new(TkbEntityTypes.Infantry_Officer,      "Infantry Officer"),
        new(TkbEntityTypes.CivilianPedestrian,    "Civilian Pedestrian"),
        new(TkbEntityTypes.CivilianCar,           "Civilian Car"),
        new(TkbEntityTypes.MilitaryApc,           "Military APC"),
        new(TkbEntityTypes.InfantrySoldier,       "Infantry Soldier"),
        new(TkbEntityTypes.Insurgent,             "Insurgent"),
        new(TkbEntityTypes.Unit_TankPlatoon,      "Tank Platoon"),
        new(TkbEntityTypes.Unit_InfantrySquad,    "Infantry Squad"),
        new(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto-Spawn)"),
    };
}
