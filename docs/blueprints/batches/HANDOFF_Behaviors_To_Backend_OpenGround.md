<!--STATUS
state: LIVE — DISPATCHED at 4a9b037f5 (2026-10-14; the commit that adds this file sits on it); scope frozen there.
updated: 2026-10-14
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — OWNS the DangerAlongRoute solve (§10.7, §10.7a as-built): the classifier, its kinds, B2/R-194 (no threat in the answer).
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad's crossing choice D3 (§7.7.7: Cross / Bound / Hold) and the demos (§7.8.4).
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — §17.6.1: the cross-lane EQS fixes this lane made (FYI, item D).
-->

# HANDOFF — what the behaviours lane needs from backend: the OpenGround danger-area kind

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). ⭐ **Dispatched at `4a9b037f5`**: your
scope is FROZEN there; documents that change after it are FYI. If a later document invalidates an item, **STOP that item and
report it**; do every item that is not blocked (R-106).

## 0. Why

🔒 User, `2026-10-14`: *"Each behavior needs to be demoed in some scenario"* · *"records the third as item to do later as it need
new features"* · *"pls write handoff to backend lane about what need from them"*.

The squad's danger-crossing choice D3 ([Squad Tactics §7.7.7](../../designs/group-maneuvers/DESIGN_Squad_Tactics.md)) has three
options. **Bound** (cross by bounding overwatch) scores from *"the area is open ground"* (`SquadCrossingDecision`:
`StrengthRatio × KindIs(OpenGround) × Threat`). 📐 Measured: `DangerAlongRouteClassifier` produces only `StreetCrossing` and
`Intersection` (`DangerAlongRouteClassifier.cs:15`). So `KindIs(OpenGround)` is always 0, and Bound **always scores 0 on a real
route**; it is reachable only in the rail `CE2159_D3_*`. ⇒ **[CE-2161](../Blueprint_Issues_Tracker.md)** (the live Bound demo) waits on
your **[CE-3081](../Blueprint_Issues_Tracker.md)** (danger-area kinds after v1).

## 1. Before anything

1. Read `docs/blueprints/RULINGS.md`, then [`DESIGN_Utility_AI_Demo_Scenarios.md`](../../DESIGN_Utility_AI_Demo_Scenarios.md) §10.7
   + §10.7a. ⭐ These items are FRAMES: **step 1 of item A is YOUR design** (UML in §10.7, per CLAUDE.md "WHO DESIGNS").
2. **Ids:** CE-3081 is your row; any finding gets a new row from **your** block.

## 2. The items

| # | what | lean | blast radius |
|---|---|---|---|
| **A** ⭐ | **OpenGround in `DangerAlongRouteClassifier`** — CE-3081's first kind. §10.7: *"a run with no cover within the corridor"* | see A1–A4 | the classifier + its tests; `DangerAreaDescriptor.Kind` already has `OpenGround = 1` |
| **B** | **a terrain stretch to demo it on**: an open run of ≥ 40 m on `test-town` (or the demo terrain you prefer) that a squad march route crosses, with no cover within the corridor | name an existing one if there is one; add a field only if there is none | terrain data only |
| **C** | **owner question:** are `CommanderUtilityTickSystem` and the `ManeuverSelect` decision kind still wanted? | **keep them**: `Hrot.SquadCoordination.md` §8.0 names them the intended mechanism and `ForceManeuverMapper` and the authoring analyzer use them. Just say whether they are dead | none if kept |
| **D** | **FYI — cross-lane edits this lane made in your code** (user-approved), for your next merge | review only | listed below |

### A — the decisions inside it (your design; these are my leans)

| | question | lean | why |
|---|---|---|---|
| A1 | what counts as "cover" | the judgement the cover query already uses: a point is covered when sight is blocked **at crouch height**. For a run sample: *open* when no obstacle lies within `corridorHalfWidth` on either side (`IWorldQuery.SightBlocked` / `Trace` across the corridor) | 📌 Squad Tactics §7.6.2 measured that a Low Wall is no cover because a point candidate is judged crouched. One definition of cover, not two |
| A2 | the shortest run that counts | ≥ 20 m | a shorter gap between buildings is a gap, not a field; the crossing drill already handles gaps as streets |
| A3 | a run that is both road and open | the **road kind wins**; OpenGround only off-road | leaves every StreetCrossing / Intersection answer unchanged (no golden or demo moves) |
| A4 | handles and `FeatureId` | as today: near and far handles `StandOff` outside the run; `FeatureId` stable across re-plans (hash the exit point on the 10 m grid) | the crossing behaviours and `TacticalFeatureHandles` already consume exactly that |

⛔ **Fences:** no threat in the answer (B2 / R-194: the Brain rates it). Do not change the two v1 kinds' output. ChokePoint and
CrestLine are not needed for this.

**Acceptance (A):** classifier rails covering:
- an open run found;
- a road run still a StreetCrossing;
- a run with a wall inside the corridor not open;
- a run shorter than the minimum not open.

Then the commander's danger sensor lists an OpenGround area on B's stretch (`GET /entities/{squad}/sensors`). **Our side after
that (CE-2161):** the scenario and the checker. The squad picks Bound, then bounds to beyond the far side.

### D — what we changed in backend-owned code (all pushed on `behaviors`)

| commit | where | what | wire |
|---|---|---|---|
| `bf3410949` | `Hrot.Network.NED` `EqsSensorConfigIngressTranslator` | a live config following an ended one drops the carrier's `SensorEvalState` (a new lifetime starts fresh) | none |
| `a73dbca5a` | `Fdp.Toolkits` `EqsChildSensor` | **epoch per lifetime**: bits 8–15 carry a per-part lifetime, the refresh count narrows to 8 bits (EQS §17.6.1) | no schema change; one extra config sample on a same-frame re-make (was swallowed — the bug) |
| `c4ecfa64e` | `Fdp.Toolkits` StarterPack | `ManeuverSelectStarterDecision` + `Phase3IntegrationTests` retired; superseded by `SquadCrossingDecision` | none |

## 3. Report back

In the CE-3081 row:
- the design section;
- the rails;
- the stretch's coordinates.

Then tell this lane, so CE-2161 can start.
