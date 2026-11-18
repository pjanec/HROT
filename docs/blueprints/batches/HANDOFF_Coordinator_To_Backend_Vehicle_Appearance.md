<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — ⭐ THE DESIGN (approved R-4296): §3 VA-1…VA-12, §3a partial damage, §4 components and TKB, §5 UML, §6 slices
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — `HeatSource` and `FireState` (your thermal rows) that `VehicleLook` reads
  - HANDOFF_Coordinator_To_Ui_Look_Library.md — ui creates `Hrot.Look` (LK-S1); `VehicleLook` lives there
-->
# HANDOFF — coordinator → `backend`: RENDERER-FACING VEHICLE STATE (CE-4670: VA-S1…VA-S3)

🔒 User, `2026-11-18`: *"approved, fold both into the design"* · *"write the handoffs for ui and backend"*. Lights, hatches and partial damage matter
most (the request). Simulation state, recorded, replicated, renderer-agnostic, our 3D map consumes it.

## Priority
AFTER your thermal rows (`VehicleLook` reads `HeatSource` / `FireState`), BEFORE your remaining second-tier (T2) rows. ⚠ VA-S1 needs ui's `Hrot.Look`
(LK-S1, ui's note tells you it exists) — if it is not there when you reach VA-S1, take VA-S2 first.

## The rows

| order | slice | builds | done when (R-4277) |
|---|---|---|---|
| 1 | **VA-S1** partial damage exposed + the kill kind | `VehicleLook` in `Hrot.Look`: `PartState(part)` = Intact · Damaged · Destroyed · Burning from `PartDamage` + the zone thresholds + `FireState` (§3a); `Health.Destruction` (None · KnockedOut · BurntOut · Catastrophic) APPENDED to `Health` / `EntityDamage` (R-169), decided once by the Health owner at destruction from the killing hit's part and the type's TKB stowage (`AmmoStowage` / `FuelStowage` parts, `SmoulderSeconds`); our 3D map draws a damaged / destroyed part and a thrown turret from `VehicleLook` (ui's map files — cross-lane, say it, NOTE ui) | ⭐ new scenario: an MBT hit on the left track shows that part Destroyed + the mobility kill on every node; an ammunition hit kills Catastrophic; a late joiner sees both |
| 2 | **VA-S2** hatches, doors, ramps | TKB `Gen.Openings` per CLASS (MBT, APC / IFV, truck, car, helicopter, jet; per-type override — HL-12's rule): id · kind (Hatch · Door · Ramp · CargoDoor) · seats · open seconds · socket; **`VehicleOpenings`** (Count + per opening State · Since) written by the BOARDING ARBITER (the `PassengerBuffer` owner, `Embarkation.cs:53`) — embark / dismount open and close; a crew "head out" is a request to the owner; `VehicleLook.OpeningProgress(id, now)`; the descriptor + translators (wire types and enums of their own, R-4295) | ⭐ new scenario: an IFV dismount opens the ramp, the squad leaves, it closes; a late joiner sees the right state |
| 3 | **VA-S3** lights | TKB `Gen.Lights` per CLASS: id · kind (Head · Tail · Brake · Interior · Convoy · Searchlight · Nav · AntiCollision · Landing) · socket; **`VehicleLights`** (OnMask · Discipline · Since · SearchAz · SearchEl) written by the vehicle's BRAIN by the light rule (discipline from SOP / ROE / an order × darkness from the weather light curve R-3017 × `HeatSource` × airborne), a station's switches by command; brake lights DERIVED from deceleration (`VehicleLook.Lights(now)`), never stored | ⭐ new scenario: a convoy at night drives blacked out under its SOP, headlights under Normal; a helicopter's anti-collision light on while its engine runs |

Later (not this handoff): VA-S4 rotors (after your CE-4658), VA-S5 smoke dischargers (+ behaviors' "pop smoke"), VA-S6 the gateway / DIS mapping.

## ⭐ Ids — measured free on every lane branch `2026-11-18` (fixed here)
| what | ids |
|---|---|
| component ids | **290** `VehicleOpenings` · **291** `VehicleLights` (≈ 112 ids are free — see the design §3's re-measure; the "space is full" note in `GlobalComponentIds.cs` R-10000 is an overcount: fix that comment when you add these) |
| descriptor ordinals | **243** `dtVehicleOpenings` · **244** `dtVehicleLights` (after the thermal 240–242) |

## Gates (rule 8)
Per row: the touched classes + the replication suites of each new descriptor + the hit-location / `Wounds` suites (VA-S1) + the embarkation suites
(VA-S2), every new rail red-proved; a wire append ⇒ name the integration suite you ran (row 8); §6a — the row's scenario + the feature's set only, the
wide set once at the end in the background. Fold any deviation back into the design (obligation ⑤).
