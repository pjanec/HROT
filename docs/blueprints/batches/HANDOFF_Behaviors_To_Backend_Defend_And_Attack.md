<!--STATUS
state: LIVE — behaviors → backend: a FRAME (you design the details as step 1, then build).
updated: 2026-10-18
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS this: DA-2, DA-3, DA-8, DA-13, DA-14, DA-19, DA-21 (R-2035, R-2036). This frame only points at it.
  - docs/blueprints/Architect_Question_85_Hit_Chance.md — (yours) the spread DA-2's factor joins.
  - docs/DESIGN_Sensors_And_Doctrine.md — (yours) vision and contacts; DA-3 changes WHEN a seen man becomes a contact.
  - docs/DESIGN_Building_Interiors.md — (yours) buildings, storeys; BuildingOf(point) sits beside BR-1's room graph.
-->

# HANDOFF — behaviors → backend: defend and attack a position *(BR-6 … BR-11)*

**Dispatched at:** the commit that adds this file, on `behaviors`. ⭐ Your scope is FROZEN at that commit; documents that change after
it are FYI only — if one invalidates an item, stop that item and say so (R-106), the others go on.

**What it is.** The user approved (R-2035, R-2036, `2026-10-18`) defending and attacking positions with a settable skill, from
untrained insurgents to a skilled unit, plus snipers and squad roles. Behaviors builds the plays, the sensors' logic and the sectors
([DESIGN_Defend_And_Attack_Position](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md), `CE-2273`…`CE-2285`). These
are the engine and data halves. ⭐ Build against that design's rows; fold any deviation back into it.

**Fences.** ⛔ Every factor is **exactly neutral at Regular / with no `CombatProficiency`** — today's rails and demos must not move. ⛔ No
behaviour logic here (no sniper hiding, no sectors). ⛔ `CombatProficiency` itself is behaviors' (`CE-2273`, beside
`CombatTemperament`): read it through `ProficiencyOf`, never the raw field.

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **BR-6** | **the shooter's proficiency in the spread** | DA-2; `HitModel.cs:45-58` | one more factor in `Sigma`: `ProficiencyOf.SpreadFactor(shooter)` — Untrained 2.5 · Militia 1.6 · Regular 1.0 · Veteran 0.8 · Elite 0.65, linear between; 1.0 with no component | rail: the same shot's spread × 2.5 for an untrained shooter, × 1.0 for one with no component (bit-identical to today) |
| **BR-7** | **acquisition time by exposure** | DA-3; `LosStrategies.cs:283-302`, `ContactHysteresis.cs:8-10` | a seen man becomes a contact after a delay that grows with range and with how few of his stance's body points show, and shrinks with the observer's proficiency; **0 for a fully exposed man within 100 m** | rails: a man showing one of three points at 300 m is acquired later than one fully exposed; a fully exposed man at 50 m is acquired the same tick as today; the delay resets when he hides |
| **BR-8** | **infantry machine guns and roles on the wire** | DA-19; `BdcTkbCatalog.cs:24-363`, `TacticalDesignation.cs:8-20` | TKB: an M249-class LMG and a PKM-class LMG, a soldier type carrying each; the wire enum gains `AutomaticRifleman · MachineGunner · Grenadier · Marksman · AntiTank` (and the CGF-local TeamLeader / Medic, if you agree) | rail: a scenario squad with an MG gunner spawns him with the LMG; the designation replicates |
| **BR-9** | **sniper rifle, DMR, a scope** | DA-13; `HeldItems.cs:17,20`, `VisualPerception.cs:63-68` | TKB: a sniper rifle (≈ 0.3 mil) and a DMR (≈ 1 mil); a **scope on the weapon mount** — ×4 detection range in a narrow FOV while aiming, the binocular rule on a weapon | rail: a scoped shooter acquires at 600 m a man he cannot acquire unscoped |
| **BR-10** | **`BuildingOf(point)`** | DA-21; `TerrainBuildings.cs:18-83` | a read-only query: building id + storey for a point (and the room, once BR-1's room graph exists); `None` outside | rail: a point in each storey of `house-2f` returns its building and storey; a point in the street returns None |
| **BR-11** *(later, not now)* | **dug-in holes and trenches** | DA-8; catalogue D7 | a `FightingPosition` world object that lowers its occupant's body points by its depth and joins the navmesh | — |

**Optional, only if measured necessary:** the round's origin on `NearMissEvent` (DA-14). Behaviors uses the gunshot sound contact
first; if a rifle shot proves inaudible at sniper ranges, this becomes an item.

**Ids.** Allocate the tracker rows from your block (rule 3) and name them in your report, so behaviors can cite them from `CE-2274`
(BR-6, BR-7), `CE-2279` (BR-9), `CE-2281` (BR-8) and `CE-2283` (BR-10).
