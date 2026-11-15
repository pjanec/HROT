<!--STATUS
state: LIVE — backend building CE-4617
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Backend_Radar_SAM.md — the frame (dispatched d287b3380)
  - NOTE_Coordinator_To_Backend_Radar_SAM_Ids.md — the fixed ids (type 114, DIS 1.1.*.28.1, class AirDefenceVehicle, missile 6047) — adopted
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — the owning design; the as-built folds into it
-->

# STATUS — backend: the radar SAM (CE-4617)

Started at `ba3f54ee6` (merged `coordinator@de3be19be`, `behaviors@5da420f19`). Behaviors had marked it started and released it (`5da420f19`): nothing built there.

## ③ Slices

| slice | state |
|---|---|
| **B-1** the radar sensor | ✅ built — `RadarEmission` (497, replicated as `dtRadarEmission` 188, brain-owned) · `RadarSignature` (498, stamped from `SignaturesDto.Radar`) · `RadarPerception` (aircraft-only generator, `RadarSignatureFilter`, the host's sight from the mast, no eye dwell) · the one-list carriage (`SimCombatDef.RadarRange`) · the RCS table · ⏳ the thermal §8.7 trap is measured on the carrier in B-6 (`sam-site` with / without the radar child — the carrier type is B-2) |
| B-2 … B-6 | ⏳ |

## ② Lane decisions *(each: what would change it)*

| # | decision | what would change it |
|---|---|---|
| L-1 | radar cross-sections (m², frontal, open-source orders of magnitude): UH-60 4 · AH-64 3 · F-16 3 · A-10 10 · C-130 40 · FPV 0.01 · recon drone 0.05 · bomber drone 0.3 ⇒ an 8 km radar sees a UH-60 at ~6.4 km, an FPV at ~1.4 km | a sourced figure for a type, or an exercise that needs a type seen sooner / later |
| L-2 | an aircraft with no `RadarSignature` reflects the 10 m² reference | a type that should be stealthy — give it a TKB value |
| L-3 | the radar acquires with NO acquisition dwell (CE-3260's dwell models an eye finding a target: 24.5 s at 5 km) — only the track hysteresis; `StrategySightTest(acquisitionDelay: false)` | a radar model with a lock-on time — then a radar-own delay, not the eye's |
| L-4 | the mast IS the carrier's sight height (`SimCombatDef.EyeHeight` → `SensorMount`, RV-17): its eyes, its radar and its muzzle share it — one sight rule | a launcher whose height matters for the missile's first metres |
| L-5 | the generator gathers to 1.5 × the stated range (a C-130 at 40 m² reaches 1.41 ×); the filter decides per target | a type above ~50 m² |
| L-6 | the clutter floor reads the ground through the same `IWorldQuery` the sight reads (`TerrainWorldLosStrategy.LiveWorldQuery`), once per tick per thread — as the sight's own `BeginBatch` (one query object a tick, the existing pattern) | — |

## ⑤ Ids

`GlobalComponentIds` 497 `RadarEmission`, 498 `RadarSignature` · descriptor `dtRadarEmission` 188 — each measured free on backend, ui, behaviors, coordinator `2026-11-15`.
