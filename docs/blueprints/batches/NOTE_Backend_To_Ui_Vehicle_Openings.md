<!--STATUS
state: LIVE
updated: 2026-11-18
current-answer: the whole file (a short note)
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6a: VA-S2 as built (hatches, doors, ramps); §7 the look library (LK-S1, ui) that reads them
-->
# NOTE — backend → ui: a vehicle's hatches, doors and ramps exist (CE-4670 VA-S2)

**What is there now** (backend, `2026-11-18`; design §6a):

| | |
|---|---|
| component | `Fdp.Toolkit.Behavior.Components.VehicleOpenings` (id 290): `Count` + ≤ 8 slots of `OpeningState` (Closed · Opening · Open · Closing) and `Since` — on every node (`dtVehicleOpenings` 243, a late joiner included) |
| what each slot IS | `Fdp.Toolkit.Behavior.VehicleOpeningsCatalog.Of(template)` — same order as the slots: `Id`, `Kind` (Hatch · Door · Ramp · CargoDoor), the seat `Roles` it serves, `OpenSeconds`, `Socket` (a model socket name: `ramp_rear`, `hatch_driver`, `door_front_left`, `canopy` …) |
| the progress | ⭐ `VehicleOpenings.Progress(state, since, openSeconds, now)` (0 closed … 1 open) — **the one rule**: ⛔ please do not re-derive it in `VehicleLook.OpeningProgress`; delegate to it |
| the class templates | on the body profiles in `bodies.json` (by DIS class): MBT 3 hatches · APC / IFV a rear ramp + 3 hatches · utility 2 doors + a cargo door · car 3 doors · helicopter cockpit + cabin doors · jets a canopy · cargo plane a ramp + a crew door. A type overrides by id (`Gen.Openings`) |

**For ui (no action required now):** when LK-S1 lands, `VehicleLook.OpeningProgress(id, now)` reads the component + the catalogue above; our 3D map can then
animate the socket named by the opening (the design's VA-S2 "our 3D map draws them" is yours). The `uc-convoy-hit` scenario dismounts an APC's squad: its
ramp opens and closes — a ready test bed.
