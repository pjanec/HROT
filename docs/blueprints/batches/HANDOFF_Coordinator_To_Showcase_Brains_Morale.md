<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-14
current-answer: the whole file
related-designs:
  - ../Architect_Question_101_Shelling_Fear_And_Shelled_Areas.md — ⭐ THE design basis: §R (the measured inventory, the claim table, the approved leans C / D / E)
  - ../../DESIGN_Temperament_And_Stress.md — OWNS the stress model the burst now feeds (§7 as built); fold C's as-built in here
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS the platoon fall-back (E-3a, DA-66…DA-70) and the attack abort (AT-10); fold E's as-built in here
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.6a R-2071 E-1…E-4 (a burst is never an enemy, it alerts the calm); C and D are E-3 / E-4
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md — the danger-area sensor and crossing drill D feeds
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — §3a "morale and rout", the must-have
  - HANDOFF_Ui_To_Showcase_Brains.md — this lane's main frame; this file adds three items to it
-->

# HANDOFF — coordinator → `showcase-brains`: morale — a burst is fire, a broken platoon falls back (R-4227)

**For:** the `showcase-brains` lane. **From:** coordinator. **Dispatched at `<sha>`** (the commit that carries this file); ⛔ **scope FROZEN
there** — later documents are FYI; one that invalidates an item ⇒ STOP that item and report it (R-106, never the batch).
🔒 User, `2026-11-14`: *"agreed with the leans but you need to measure the unknowns first"* · *"approved"* (R-4227).
⭐ Autonomous, no questions: each item's decisions are made below; anything left open is decided the closest way and named in the report.

## 0. Start

`git fetch origin showcase-brains coordinator` · on `showcase-brains`, `git merge origin/coordinator` (this file, AQ101 §R, R-4227) · push a
`chore: started morale at <sha>` commit · read [AQ101 §R](../Architect_Question_101_Shelling_Fear_And_Shelled_Areas.md) end to end — its
claim table is the measured state (`2026-11-14`) every item starts from.

## 1. The items — in this order

| # | row | what | design basis | where (measured) |
|---|---|---|---|---|
| 1 | **CE-4582** | ⭐ **a shell burst is fire**: every person within 2 × the warhead's injury radius gets `SensorChangedEvent { What = NearMiss }` and `HitModel.StampUnderFire` — ONE helper both callers use (the round's `ReportNearMisses` and the burst), ⛔ not a copy | AQ101 §R C | today the only producer is `BallisticsSystem.ReportNearMisses` (`BallisticsSystem.cs:348-380`); the burst's radius and position are where `AreaEffectSystem` applies the area effect (`AreaEffectSystem.cs:70-116`). ⚠ the near-miss EVENT carries the position stress / callouts read — publish what `NearMissSensingSystem` turns into the sense, or the sense directly; pick the one that keeps the networked path (`NearMissTranslators`) working and say which |
| 2 | **CE-7015** | ⭐ **the rout**: one helper beside `CombatEffective` — *members fighting AND not broken* (`Injury.IsInTheFight` && `!Stresses.IsBroken`) — read by `PlatoonDefendBehavior.SlotFailing` (both arms: the baseline fraction and the not-in-the-fight check) and by `PlatoonAttackBehavior`'s assault-Black count behind `AssaultLost` | AQ101 §R E | `PlatoonDefendBehavior.cs:591-620`, `PlatoonAttackBehavior.cs:656-660`, `CombatEffective.cs:32-40`. ⛔ `CombatEffective.FightingMembers` / `IsInTheFight` and `Injury.IsInTheFight` stay UNCHANGED (SITREP colour CB-37, centroid, replay search, 72 targeting sites). ⚠ a SITREP-Red path (`SlotFailing` without a baseline) stays as is |
| 3 | **CE-4583** | **the shelled area**: the squad pool keeps burst heat (a burst adds heat at its point, merged within 15 m); 3 bursts within 60 s ⇒ a `DangerAreaKind.Shelled` circle round them; cools after 2 min with no burst; the march's danger sensor treats it as a threatened area (Hold or cross) | AQ101 §R D (= §2 D, unchanged), R-2071 E-4 | the precedent `DangerAreaKind.GasCloud = 6` (`DangerAreaDescriptor.cs:20`, CE-3227) and `DangerAreaSettings.cs:36-45` (⚠ its `kind <= GasCloud` bound must take the new kind). ⏭ ⛔ no navigation area cost — routing round it is a backend item, not here |

## 2. Rules that bind every item

| | |
|---|---|
| ⛔⛔ **the parallel rule** (CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL", R-4225) | item 2 reads OTHER entities' `Stress` from a platoon's brain — **declare it** (a stable read, or the tick-start view); the squad pool heat of item 3 is the squad's own state. Each item's rails run with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` |
| ⛔ **no allocation per frame** (R-2084) | the burst helper and the count are loops over what exists; no LINQ |
| ⭐ **T-1 first** | the feature suites: `StressSystemTests` (Hrot.SimHost.Tests), `AreaEffectSystemTests` (Fdp.Toolkits.Tests), the platoon defend / attack suite (find it: `scripts/find.sh HalfTheLineFailing`), `DangerAreaSensorSystemTests` / `DangerAreaCrossingManeuverTests`. ⭐ new rails go INTO these, each red-proved by one compiling mutation |
| ⭐ **the as-built goes back into the designs** | C → Temperament §7 (a burst is a stress input) and AQ101 §R; E → Defend & Attack E-3a / AT-10 (who counts as lost); D → Squad Tactics' danger-area table. Prior state marked superseded, never overwritten silently |

## 3. Acceptance

1. A rail: a burst 6 m from a calm Regular raises one near-miss sense and stamps under-fire; 40 m off (outside 2 × radius) — nothing. A
   civilian in range takes the same edge into `CivilianFearSystem` (no new civilian code).
2. A rail: a defending platoon whose line squads are all alive but half their men broken ⇒ `HalfTheLineFailing` true ⇒ `FallBackOrders`;
   the same men unbroken ⇒ false. The SITREP colour of those squads is unchanged (asserted).
3. A rail: three bursts within 60 s and 15 m ⇒ one `Shelled` area; none for 2 min ⇒ gone; two bursts ⇒ none.
4. `python3 scripts/stepped-hash.py --threads 1,4,8` on the showcase scenario: the same world at every count.
5. **Live, once** (T3, background): the mortar barrage on a Green defending platoon — it breaks and falls back to its fall-back point; a
   Veteran one holds. Report the frame counts, not screenshots in git.

## 4. Report

Per the gate-report contract (CLAUDE.md rule 8): one row per gate with counts, the red-proofs, every red confirmed pre-existing at the base sha,
the designs updated (doc + section), ids used (CE-4582, CE-4583, CE-7015; any new ruling from `R-7011`). Update `STATUS_Showcase_Brains.md`.
