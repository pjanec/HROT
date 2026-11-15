<!--STATUS
state: LIVE — backend building CE-4617
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Radar_SAM.md — the frame (dispatched d287b3380)
  - NOTE_Coordinator_To_Backend_Radar_SAM_Ids.md — the fixed ids (type 114, DIS 1.1.*.28.1, class AirDefenceVehicle, missile 6047) — adopted, ⚠ EXCEPT the missile: 6047 is taken (⑤)
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — the owning design; the as-built folds into it
-->

# STATUS — backend: the radar SAM (CE-4617)

Started at `ba3f54ee6` (merged `coordinator@de3be19be`, `behaviors@5da420f19`). Behaviors had marked it started and released it (`5da420f19`): nothing built there.

## ③ Slices

| slice | state |
|---|---|
| **B-1** the radar sensor | ✅ built — `RadarEmission` (497, replicated as `dtRadarEmission` 188, brain-owned) · `RadarSignature` (498, stamped from `SignaturesDto.Radar`) · `RadarPerception` (aircraft-only generator, `RadarSignatureFilter`, the host's sight from the mast, no eye dwell) · the one-list carriage (`SimCombatDef.RadarRange`) · the RCS table · ⏳ the thermal §8.7 trap is measured on the carrier in B-6 (`sam-site` with / without the radar child — the carrier type is B-2) |
| **B-2** the missile, the launcher, the vehicle | ✅ built — type **114** `AirDefence_RadarSam` (DIS 1.1.222.28.1, class `AirDefenceVehicle` = `1.1.*.28`) · ONE mount: 4 missiles on turret 0 (0–85°), no reload, `RoundsInFlight` 2 / `SalvoIntervalS` 2 (new `WeaponMountDto` fields, honoured in `AimAndFireExecutor.TurretPath`) · missile **6050** (radar, 60°, 800 m/s, 18 s, 0.5 km min, chaff rejection 0.9, the library's new `"Radar SAM warhead"` proximity-fused) + a placeholder look row · chaff's END-GAME gate (`CountermeasureSystem.InChaffEndGame`) and the aircraft holding its chaff for it (`AirAttackNodes.ChaffTooEarly`, behaviors' file, said) · rails: two launches 2 s apart then the flight · a UH-60 at 4 km and an F-16 crossing at 250 m/s 3 km out hit (a 20° cone loses the jet) · an aircraft 2 km beyond the terrain hurt as much as over it · four chaff in the end-game defeat 20–40 % (measured 1 − 0.9⁴, seeded) |
| **B-3** the fire-control rule and the behaviour | ✅ built — `FireControlSight` (`Sees` per mount, `SeesForAnyMount` before the pick) at the executor's two sites and `AirSense.FireAt`; `SightNow` untouched, `SightNow.Holds` added · `RadarSamEngage` (`RadarSamNodes`, behaviors' assembly, said) from a NEW `BasicVehicleSop` row ahead of the air row · salvo by type 2 / 1 / 0 · Off while relocating, deactivator Off · `CanEngageAir` reads ammunition (`AirDefenceWeapons.HasRounds`) · the FPV refused by the missile's lock floor (`MissileDto.MinTargetRcsM2`, the one round gate `AmmoChoice.InDomain`) · rails: radar-only contact seen by the SAM's launcher, never by an M2 or an IR launcher; empty ⇒ neither row; the node's on → track → salvo of 2 → down → off → relocate, and the quiet path |
| B-4 … B-6 | ⏳ |

## ② Lane decisions *(each: what would change it)*

| # | decision | what would change it |
|---|---|---|
| L-1 | radar cross-sections (m², frontal, open-source orders of magnitude): UH-60 4 · AH-64 3 · F-16 3 · A-10 10 · C-130 40 · FPV 0.01 · recon drone 0.05 · bomber drone 0.3 ⇒ an 8 km radar sees a UH-60 at ~6.4 km, an FPV at ~1.4 km | a sourced figure for a type, or an exercise that needs a type seen sooner / later |
| L-2 | an aircraft with no `RadarSignature` reflects the 10 m² reference | a type that should be stealthy — give it a TKB value |
| L-3 | the radar acquires with NO acquisition dwell (CE-3260's dwell models an eye finding a target: 24.5 s at 5 km) — only the track hysteresis; `StrategySightTest(acquisitionDelay: false)` | a radar model with a lock-on time — then a radar-own delay, not the eye's |
| L-4 | the mast IS the carrier's sight height (`SimCombatDef.EyeHeight` → `SensorMount`, RV-17): its eyes, its radar and its muzzle share it — one sight rule | a launcher whose height matters for the missile's first metres |
| L-5 | the generator gathers to 1.5 × the stated range (a C-130 at 40 m² reaches 1.41 ×); the filter decides per target | a type above ~50 m² |
| L-7 | the missile (an SA-8 / Tor class, rounded): 800 m/s, 18 s, 0.5–8 km, a 60° seeker cone, chaff rejection 0.9, the shared 4 m proximity fuze; its warhead a new library row `"Radar SAM warhead"` (frag, 15 kg, 4.5 kg explosive) | a sourced figure; a target the 4 m fuze misses |
| L-8 | chaff's end-game = the round within 1.5 km of the cloud OR 3 s from it at its own speed; the aircraft holds chaff until the warning's time to go (`TimeToImpactS − elapsed`) is inside 3 s — dropped at the launch it would always be wasted | a measured defeat rate outside 20–40 % on `sam-site` |
| L-9 | the vehicle: a 6×6 truck (17.5 t, 16 m/s), splinter protection (20 / 10 / 10 / 5 mm, 400 health), the mast 6 m (its sight height), the launcher's lock 2 s, 60 °/s traverse; side 2 (Russia) by default — a scenario's faction overrides | a tracked chassis (one catalogue line), a sourced figure |
| L-10 | the salvo's NEXT one waits for the last round's estimated flight (the guided profile's rule), the two of one salvo 2 s apart | — |
| L-11 | an FPV is refused by a SEEKER LOCK FLOOR on the missile (`MinTargetRcsM2` 0.03 m²: FPV 0.01 refused, recon 0.05 taken) in the one round gate — not a type list | a drone class that must be refused / taken against its cross-section |
| L-12 | drone vs manned = SISO DIS air category 50 (unmanned) ⇒ 1 missile, else 2 | a type DIS-coded outside 50 |
| L-13 | relocation: 350 m straight away from the last aircraft (its heading when none) at 8 m/s, given up after 90 s; ⏸ no cover search yet | a site where straight away is into the enemy — then the EQS cover query |
| L-14 | the salvo's outcome = the target out of the fight, or 18 s (one flight) after the salvo left | a longer-flying round |
| L-15 | the cue is the SOP row's fresh air sense (any sense); the radar's own contacts exist only while it radiates, and it is off between engagements (relocation outlasts the 3 s freshness) — so its own radar does not re-cue it | a re-cue measured on `sam-site` |
| L-6 | the clutter floor reads the ground through the same `IWorldQuery` the sight reads (`TerrainWorldLosStrategy.LiveWorldQuery`), once per tick per thread — as the sight's own `BeginBatch` (one query object a tick, the existing pattern) | — |

## ⑤ Ids

`GlobalComponentIds` 497 `RadarEmission`, 498 `RadarSignature` · descriptor `dtRadarEmission` 188 — each measured free on backend, ui, behaviors, coordinator `2026-11-15`.
Type **114** `AirDefence_RadarSam`, DIS **1.1.222.28.1**, class **`AirDefenceVehicle`** (`1.1.*.28`) — as the ids note.
⚠ Missile **6050** `TkbRadarSamMissile`, NOT the note's 6047: 6047–6049 are the mortar SMOKE bombs (`Tkb60mmMortarSmoke` …, behaviors `CE-7025`) on every lane — **ui: key the missile look on 6050** (the as-built). Its DIS 2.1.222.1.0.0.0.
⚠ For ui: `MunitionTkbCatalog.IsBuiltIn` covers 6001–6035 only, so 6036+ (6050 too) are not "a round" to a host with no TKB (the replay browser) — pre-existing, not changed here.
