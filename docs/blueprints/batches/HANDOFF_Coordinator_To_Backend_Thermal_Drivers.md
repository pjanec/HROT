<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — ⭐ THE DESIGN (approved R-4293, READY-TO-BUILD): §3–§3e the decisions, §4 the components, §5 the UML
  - PROTOCOL_Autonomous_Lanes.md — HOW you work (push per row, status file, decisions skipped and written)
  - HANDOFF_Coordinator_To_Terrain_Thermal_Drivers.md — terrain's slice (TD-S6) of the same design
-->

# HANDOFF — coordinator → `backend`: THERMAL DRIVERS (CE-4657…CE-4661, CE-4663, CE-4664)

**For:** backend. **From:** coordinator. **Dispatched at the commit that adds this file.**
🔒 User, `2026-11-18`: *"approved"* (the design, R-4293) · *"write the handoffs for backend and terrain, maybe with a bit higher priority than non
critical bugs"*.

## ⭐ Priority — where this goes in your queue
**After the P1 and P5 rows of your bug queue** (CE-3458, CE-3452, CE-9023, CE-3457 — the user's priority bugs), **BEFORE its second-tier (T2)
rows.** Finish the row in progress first (protocol §2 ④: a handoff goes after your current row).

## Read first
- ⭐⭐⭐ the design, end to end: [`DESIGN_Thermal_Drivers.md`](../../designs/thermal/DESIGN_Thermal_Drivers.md) — the rule (TD-1): **drivers, never temperatures** —
  a state + the time it began, written only when it changes; every node computes "now". §3a–§3e record what the review against code changed and why.
- [`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md) — unchanged: push per row, `STATUS_Backend_Queue.md` (add these rows in §3 "taken"),
  decisions to `DECISIONS_Backend.md`.
- CLAUDE.md: unmanaged recorded components; hot path 0 B; the parallel rules (`HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + the world guard on every
  new system's rails); R-4277 (a feature works when its scenarios pass).

## The rows — in order

| row | slice | builds (the design section is the spec) | done when |
|---|---|---|---|
| **CE-4657** | TD-S1 | `HeatSource {State, Since, MotionSince}`, `FireState` (≤ 8 spots), `GroundHeatMarks` (cell record, `ICellRecord<T>`), their descriptors + translators in the SHARED pack; `ThermalDriversOps` (closed form: signature, barrel heat, mark warmth, exertion/load derived from speed); `ThermalPerception` re-based on it; `ThermalHeatSystem` RETIRES; `ThermalState` keeps only TKB parameters (§4, §5) | `ThermalPerception`'s own suite (T-1) green with unchanged verdicts; 0 B per steady frame (rail) |
| **CE-4658** | TD-S2 | `HeatSourceSystem` on the node that moves the entity — the rule with hysteresis (TD-3), the possessed ignition, the authored start state (saved) | ⭐ new scenario **`th-idling-tank`** with checks: a crewed, idling tank is seen by a thermal sensor at night, a parked empty one is not; a man who just ran reads warmer than one who stood |
| **CE-4659** | TD-S3 | append `BurstRounds`, `BurstStartedAt`, `LastShotAt`, `HeatAtLastShot` per mount to `WeaponAmmo` / `EntityWeaponAmmo` (R-169 append); retire `Heat0..3`; ⚠ confirm the brain-node mirror sees the burst's end in time (§3d); the station HUD reads the computed heat (ui's file — a cross-lane edit, say it + a note to ui) | a burst sends ONE sample; the computed barrel heat equals `BarrelHeat`'s at any later time (rail) |
| **CE-4660** | TD-S4 | ignition in the damage path (a fuelled type destroyed — TKB `FireSeconds` > 0; an incendiary hit); `BurnSystem` on the Health owner, the `BleedOutSystem` pattern, per burning spot; the three holders (§3c); W3 `WreckSpawnSystem` READS `FireState` instead of deciding (ui's file — cross-lane, say it) | ⭐ new scenario **`th-burning-wreck`**: a late-joining node sees the wreck burning until it goes out; a man inside a burning spot loses Health in steps on every node |
| **CE-4661** | TD-S5 | `Health.StateSince` appended to `EntityDamage`, stamped by the Health owner when the derived band changes | a body's `StateSince` is the time of death on every node, a late joiner included (rail) |
| **CE-4664** | TD-S8 | `GroundHeatMarks` written by `HeatSourceSystem` when a stand ≥ ~60 s with the engine on ends; `MarkWarmth`; ⛔ never drawn in the normal views | ⭐ new scenario **`th-vehicle-left-warm-spot`**: a tank idles 15 min and drives off — a mark of its footprint (hot end rear) on every node, a late joiner included; a parked cold truck leaves none |
| **CE-4663** | TD-S7 | the DIS mapping table (TD-11) into `docs/reference/NED_DDS_Protocol.md` — a document | the table with the damage thresholds |

## ⭐ Ids — measured `2026-11-18` on every lane branch (fixed here)
| what | ids | ⚠ |
|---|---|---|
| descriptor ordinals | **240** `dtHeatSource` · **241** `dtFireState` · **242** `dtGroundHeatMarks` | free on every lane; 223–239 are the agg lanes', 207–209 convoy's |
| component ids | **487** `HeatSource` · **488** `FireState` · **493** `GroundHeatMarks` (coordinator block 476–495) | ⚠⚠ the component space is nearly FULL; these three are free in PRODUCTION but each is used by a TEST-LOCAL struct (`Tiny2`/`RPos` 487, `Tiny3`/`RVel` 488, `TwClock` + `WorldKeyframesTests` 493). Move those test fixtures to another test-only id first; if that cannot be done safely ⇒ `DECISIONS_Backend.md`, do not guess |

## Gates (CLAUDE.md rule 8 — the report substitutes for my run)
Per row: the touched classes + `ThermalPerception`'s suite + the replication suites of each new descriptor, every new rail red-proved; the parallel
env on every new system; the three new scenarios via `scripts/scenario-regression.sh`; row 8: the replication / late-joiner path is cross-node ⇒ name
the integration suite you ran. Fold any as-built deviation back into the design (obligation ⑤).
