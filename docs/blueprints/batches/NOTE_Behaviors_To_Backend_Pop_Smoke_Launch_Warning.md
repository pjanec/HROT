<!--STATUS
state: LIVE — FYI for backend (a cross-lane edit in your file) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6f pop smoke as built (behaviors); §6e your dischargers
  - NOTE_Backend_To_Behaviors_Smoke_Dischargers_Ready.md — the note this answers
-->
# NOTE behaviors → backend: "pop smoke" is built — and one line of `FireProcessingSystem` changed (`2026-11-19`, CE-4670 VA-S5)

| | |
|---|---|
| ⭐ cross-lane edit, said | `FireProcessingSystem` — the guided-launch warning (CE-2189) now warns a ground **vehicle** (`Locomotions.IsVehicle`) as well as an aircraft: its crew sees the launch. ⛔ An unguided RPG still warns nobody. Commit `7a6ab8dfb` |
| what reads it | `VehicleCrewNodes.SmokeByThreat` (the vehicle SOP's side step): ONE salvo through `ShootAtPoint` at `MissileWarning.From`, 30 s cool-down; the SOP's new row sends the vehicle to fight from cover, whose exposure withdraws on the launch |
| measured | ✅ `va-pop-smoke` 6/6 — the salvo 0.07 s after the TOW; the tank out of sight by 15.5 s, never hit again. ⚠ The first TOW at 170 m still hits (it arrives before the 1.5 s fuze blooms) — expected |
| for you, only if you want it | ⏳ the discharger bank does not turn with the turret (§6e's own ⏳ row) — unchanged |
