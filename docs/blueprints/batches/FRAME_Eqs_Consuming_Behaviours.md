<!--STATUS
state: LIVE — a FRAME (backend → behaviors), design + discussion task, not a build order
updated: 2026-10-04
current-answer: the whole file
related-designs:
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — §19 owns the terrain EQS slice this frame consumes (templates, tests, rules); §17.6 owns the child-sensor recipe.
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — owns a behaviour's child-sensor lifetime (CE-485/486).
  - docs/DESIGN_Terrain_World.md — owns the terrain the queries read.
-->

# FRAME — behaviours that USE the terrain EQS queries *(backend → behaviors lane)*

> 🔒 **User, `2026-10-04`:** *"We will hand the eqs-using behaviors as a design and discussion task to the behavior lane."*
> ⭐ The behaviors lane **designs** this (step 1: its own design doc with UML in `docs/`), discusses it with the user, then builds.

## Goal

Units that **take cover, fall back, flank and pick firing positions** on a real terrain — driven by the EQS queries the
backend built in `CE-3030`, and shown working in a live `tt-nav-los`-style run on the editor **and** `--mode all`.

## What already exists *(measured `2026-10-04`)*

| piece | where | state |
|---|---|---|
| templates `FindCoverFromTarget`, `FindOpenFiringPosition`, `FindFlankingPosition`, `FindSafeRetreatPoint`, `FindThreatsInView` | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/` · EQS design §19.6 | ✅ built, registry-discovered, cross-node rail green |
| sensor inputs: slot 0 = self (optional for a child sensor), slot 1 = target, `SearchRadius`, `FactionFilter` (threat forces), `ThreatThreshold` (0 = always) | EQS design §19.5 | ✅ |
| child-sensor lifecycle (spawn / refresh / destroy, behaviour-owned) | `EqsChildSensor`, `EqsLifecycleNodes`, blueprint `SpawnEqsSensor` / `ReadEqsResult` / `When` triggers | ✅ |
| `MoveToOptimalCover` BTree action (reads the top result, issues a MoveTo — now a `PathToPoint`, `CE-3026`) | `EqsCombatNodes.cs` | ✅ |
| `HideInCover_BT` / `_v2` (BTree definitions) | `HideInCoverBehavior.cs` | ⚠ defined; **no scenario runs them** |
| utility `CombatPostureDecision` (`In.EqsTopScore(FindCoverFromTarget / FindSafeRetreatPoint)`) | `Utility/StarterPack/CombatPostureDecision.cs` | ⚠ **no shipped asset evaluates it**; its comment *"FindSafeRetreatPoint … not built yet (CE-2051)"* is stale |

## Fences

| ⛔ | |
|---|---|
| no change to the EQS solver, templates or sight rules without a backend round | a missing query block is a request to the backend lane, with the use case |
| no second cover/LOS mechanism in behaviour code | read the sensor's results; ⛔ do not ray-cast from a behaviour |
| movement goes through MoveTo (`PathToPoint`) | ⛔ never write `NavState` / a straight `DirectPoint` for a tactical move |

## Decisions for the design — with the backend's leans *(the behaviors lane owns the final call, with the user)*

| # | question | lean |
|---|---|---|
| D1 | which first behaviour | ⭐ **"take cover from the nearest threat, fall back if overrun"** — one unit, `FindCoverFromTarget` + `FindSafeRetreatPoint`, the smallest loop that proves cover end to end |
| D2 | BTree, blueprint or utility as the first host | ⭐ **blueprint** — `SpawnEqsSensor`/`ReadEqsResult`/`When` already exist and the editor can author and run it; the utility decision follows once one consumer works |
| D3 | where the target (slot 1) comes from | ⭐ the unit's `TargetMemory` top threat (perception's answer) — re-pointed with an epoch bump when it changes |
| D4 | when to re-query | ⭐ standing sensor + `ScoreDelta` publish policy; act on `TopChanged` / threshold crossings, not every tick |
| D5 | acceptance scenario | ⭐ extend `scenarios/tt-nav-los`: a rifleman near the Tower with a hostile to the north-east; expected — moves to a point the hostile cannot see |

## Acceptance

① a design doc in `docs/` (classDiagram + sequenceDiagram + module diagram) linking EQS §19 both ways ·
② the behaviour's own rails · ③ a live run on the editor and `--mode all` showing the unit ending hidden from the threat
(checked with the terrain sight, as `EqsDistributedTests.FindCoverFromTarget_OverTheTerrain_*` does).
