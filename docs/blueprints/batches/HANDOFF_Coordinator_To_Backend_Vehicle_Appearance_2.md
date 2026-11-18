<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — ⭐ approved (R-4296): VA-5 rotors, VA-9 smoke dischargers, VA-11 the gateway; §6 slices
  - HANDOFF_Coordinator_To_Behaviors_Pop_Smoke.md — behaviors' "pop smoke" decision, which needs your VA-S5 mount first
-->
# HANDOFF — coordinator → `backend`: VEHICLE APPEARANCE, the rest (CE-4670: VA-S4, VA-S5, VA-S6)

🔒 User, `2026-11-18`: *"yes, write the handoffs"* (after: rotors cheap now that the engine state exists; smoke launchers; the gateway's remaining rows).

## Priority
After your DETERMINISM rows (CE-2440, CE-3445, CE-2448, CE-3449), BEFORE your scale-cost rows (CE-3425, CE-4395), CE-3460 and CE-167.

| order | slice | builds (the design is the spec) | done when (R-4277, §6a) |
|---|---|---|---|
| 1 | **VA-S4** rotors and engines (VA-5) | ⛔ NO new component: `VehicleLook.RotorSpeed(now)` 0..1 and starting / stopping from `HeatSource` (State + Since) + `FlightState.Airborne` + the type's TKB `SpinUpSeconds` / `SpinDownSeconds` (per power class, overridden per type); an engine-sound level for a renderer from the same | ⭐ a scenario: a helicopter on the ground spins up before take-off and down after shutdown — the speed computed on a replica matches the owner's at every sample; 0 B steady |
| 2 | **VA-S5** smoke dischargers (VA-9) | a weapon-mount kind `SmokeDischarger` + its salvo pattern in the TKB (rounds, spread, distance); its round makes the EXISTING `AreaEffectCloud` (Smoke, IR-blocking by type — CE-4662); the station can fire it; the MBT / IFV classes get one. ⛔ NOT the "pop smoke when threatened" decision — that is behaviors' (`HANDOFF_Coordinator_To_Behaviors_Pop_Smoke.md`): leave behaviors a NOTE when the mount works | a rail: a salvo makes N clouds at the pattern's points on every node; they block sight and the thermal sensor |
| 3 | **VA-S6** the gateway rows still missing (VA-11) | your TD-S7 table (`NED_DDS_Protocol.md` §"Appearance and thermal mapping") gains the LIGHTS (each group → the DIS light bits), the OPENINGS (hatch closed / popped / open, ramp), `Health.Destruction`, and the effect category → DIS detonation-result table (LK-7, §7a — pin the exact SISO codes) | the table, every row sourced |

Gates as usual (rule 8; a wire append ⇒ name the integration suite, row 8). Fold any deviation into the design (obligation ⑤).
