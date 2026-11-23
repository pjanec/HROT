<!--STATUS
state: LIVE
build-state: BUILDING — the field + OF1 being built (CE-2493); OF2–OF5 DESIGN
updated: 2026-11-23 (first version — behaviors, on the user's request)
current-answer: §1 the request · §2 doctrine · §5 the field · §6 the suite · §7 the diagrams · §8 claims
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - DESIGN_Urban_Fight_Suite.md — OWNS the realism rubric (never / timeline / outcome / review) this suite reuses on OPEN ground; its U1–U6 are the dense-town twins
  - ../../DESIGN_Scenario_Regression.md — OWNS the check grammar (exposed, still, cover, bunched, near, field, fighting, for) every row here is written in
  - DESIGN_Squad_Tactics.md — OWNS react to contact (§4, S2-D the once-only assault / break / hold choice, R4's 3 : 1) and the plays the suite exercises
  - DESIGN_Assault_Under_Fire.md — OWNS fire and movement in contact (A-7) and the base of fire (A-3); OF2 / OF3 judge it on open ground
  - DESIGN_Defend_And_Attack_Position.md — OWNS PlatoonAttack (DA-9…DA-19, AT-1…AT-13); OF2 judges it
  - DESIGN_City_Battle_Showcase.md — §12 the test ladder's "④a stalemate probe": this suite is that probe at squad / platoon scale, before the town
-->

# DESIGN — the open-field suite: squads and platoons fighting across open ground with scattered cover

🔒 **User, `2026-11-23`:** *"open field with some obstacles so that the soldiers can take cover and not being exposed all the time, but still mostly an
open field … in the city battle … staying in the middle of open field and basically playing a ditch war. Not making attempts to move forward, not
being supported by the other squads. … apply the same methodology … testing scenarios with success condition based on a military doctrine"* · *"Yes, please."*

## 1. The request, in one line

A set of small scenarios on open ground with ~10–15 % scattered cover, each judged by machine against the infantry drills, that shows WHETHER and
WHERE squads and platoons stall — built and run BEFORE any behaviour is changed (the suite measures; the fixes follow from its reds).

## 2. The doctrine — the proven solution for this problem class

| drill (ATP 3-21.8, *Infantry Platoon and Squad*) | what a trained unit does | the timing |
|---|---|---|
| **react to contact** (battle drill) | the men in contact return fire at once and get to cover; the leader locates the enemy and wins the firefight; then decides: attack, break contact, or report and hold for the platoon | fire at once · cover within seconds |
| **fire and movement** (individual movement techniques) | one element fires while the other moves; **rushes of 3–5 s** cover to cover ("I'm up, he sees me, I'm down"), crawls where the cover is low | ≤ 5 s exposed per rush |
| **squad / platoon attack** (battle drill) | a base of fire SUPPRESSES; a manoeuvre element moves to a flank under that fire; the assault goes through while the base fires, then shifts or lifts | the assault moves only while the base suppresses |
| **bounding overwatch** (movement when contact is likely) | one element overwatches from cover, the other bounds to the next cover | each bound ENDS in cover |
| **break contact** (battle drill) | an outmatched element suppresses and withdraws by bounds cover to cover | never a stand in the open |

⭐ **What doctrine does NOT say:** that a squad holds until it has 3 : 1. **3 : 1 is the planning ratio for attacking a prepared defence at the decisive
point**; a squad that meets an equal or smaller enemy wins the firefight and moves. ⇒ the measurable tells of a ditch war are: **nobody moving while the
enemy is still fighting · no element suppressing for another · nobody else coming to help.**

## 3. The rubric

The urban suite's four layers, unchanged (📄 `DESIGN_Urban_Fight_Suite.md` §1): **1 never** (doctrine's hard lines) · **2 timeline** (the drill in time) ·
**3 outcome** (the fight is decided) · **4 review** (judgement, turned into rows when the grammar can say it). Every row about men judges
`fighting: true` (a downed man is not idle).

## 4. INVENTORY *(2026-11-23)*

| query | result |
|---|---|
| `search_graph` `.*(Contact\|Assault\|Suppress\|Bound\|Overwatch\|Flank\|FireAndMovement\|Rush\|PlatoonAttack\|PlatoonHold\|ReactTo).*` Class | 102 (`has_more`) — the plays: `ReactToContactBehavior`, `SquadContactResponseDecision`, `SuppressAndManeuverPlay`, ⭐ `FireAndMovementPlay` / `FireAndMovementManeuver` / `FireAndMovementBehavior`, `BoundingOverwatchPlay`, `TravellingOverwatchManeuver`, `BreakContactPlay`, `SquadHoldPlay`, `PlatoonAttackBehavior`, `PlatoonBoundBehavior`, `PlatoonHoldBehavior`, `SuppressBehavior` |
| `search_graph` `.*(Play\|Maneuver)$` Class | 37 (`has_more: false`) |
| scenarios on OPEN ground with a fight (folder sweep) | `rf-car-squads` (two immortal squads behind cars, static) — ⛔ **no squad or platoon ATTACKING across open ground**; every platoon scenario (`pc-*`) is urban (`cqb-village`, `wide-corridor`) |
| ⚠ coverage | `check_index_coverage` is not available through the CLI — the class counts are what the graph indexed |

## 5. The field — one layout every scenario shares (`basic-desert`, x 60–440, y 440–580)

⭐ `basic-desert` is flat (height 0) and empty north of y 430 (its four features end at y 410), so the field is ALL scenario-placed terrain features
(`TerrainFeature`: Clutter, Wall, Ground ditch / foxhole, Group of trees) — one generator, `scripts/make-open-field-scenarios.py`.

```mermaid
graph LR
  W[x 60-110 the start line - open] --> C1[x 110-250 scattered cover ~12 pc - low walls 1 m, cars, sandbag walls, two tree clumps, a ditch on the south flank]
  C1 --> R[x 250-275 the enemy line - foxholes + sandbags]
  R --> E[x 275-440 open behind]
```
*What the picture shows that prose hid:* the cover is SCATTERED on the approach, never continuous — a unit that wants to close must leave one piece of
cover for the next (the tell OF1–OF3 judge); the ditch gives the south flank a covered route (what a manoeuvre element should find).

## 6. The suite — the expect rows stated UPFRONT (R-4277)

| # | situation | must (timeline / outcome) | never | the cause it probes (§8) |
|---|---|---|---|---|
| **OF1** | a 6-man squad (`InfantrySquadMove` east across the field — a movement to contact) meets a 3-man fire team dug in at x ≈ 260, ~150 m | Blue fires within 5 s of Red's first round · every Blue man in the fight in cover within 20 s · ⭐ the fire team is BEATEN (all out of the fight) within 300 s | ⭐ every Blue man STILL (within 2 m) for 60 s while Red still fights · a Blue man upright in the open in Red's sight 10 s · Blue bunched | C-1 (3 : 1 hold) |
| **OF2** | a platoon (3 squads, `PlatoonAttack`) attacks a squad holding the enemy line from ~300 m | a squad suppresses from cover while another moves · the objective is taken | every squad still 60 s · an assault while the base of fire is silent | C-2 (a bounding squad is not "attacking") |
| **OF3** | a squad bounds across the field (`BoundingOverwatch`), then meets a fire team | each bound ends within 2.5 m of cover · in contact, rushes ≤ 5 s | a man upright in the open in Red's sight 10 s | — (expected to pass) |
| **OF4** | a platoon moving across the field; ONE squad is fired on from the flank | within 60 s another squad moves to support or flank | every squad of the platoon still 60 s | C-3 (the platoon holds) |
| **OF5** | a squad meets a platoon with a machine gun | breaks contact cover to cover | an assault; a stand in the open | the counterweight — fixing C-1 must not make squads charge everything |

⚠ **One sample is not a band** — the deterministic run is one honest sample for layers 1–2; layer 3 bands need seeds (as in the urban suite).

## 7. The diagrams — what the code does today at contact (the suspected path, to be confirmed by OF1 / OF2 / OF4)

```mermaid
sequenceDiagram
  participant SQ as Blue Squad (BasicSquadSop)
  participant R as ReactToContactBehavior
  participant D as SquadContactResponseDecision
  participant PL as Blue Platoon (BasicPlatoonSop)
  SQ->>R: SquadContact / SquadUnderFire → React (pauses the order)
  R->>R: hold 3 s, return fire (DecideAfterSeconds)
  R->>D: decide ONCE: Assault · BreakContact · Hold
  alt our share ≥ 3 : 1 (R4 AssaultShare 0.25)
    D-->>R: Assault → SuppressAndManeuverPlay
  else outmatched
    D-->>R: BreakContact
  else between (6 v 3 = 2 : 1)
    D-->>R: Hold → SquadHoldPlay until the pool is EMPTY
  end
  SQ->>PL: the contact enters the platoon's picture
  PL->>PL: ElementReported → React PlatoonHold (every squad holds)
```
*What the picture shows that prose hid:* nothing on the path between 3 : 1 and outmatched ever MOVES — the squad holds until the enemy is gone, and the
platoon's only contact row holds the other squads too. That is the ditch war, if the runs take this path.

```mermaid
classDiagram
  class BasicSquadSop { <<existing>> Contact / UnderFire → React ReactToContact }
  class ReactToContactBehavior { <<existing>> hold · decide once · drill · hold until clear }
  class SquadContactResponseDecision { <<existing>> Assault · BreakContact · Hold }
  class AfterContact { <<existing>> AssaultShare 0.25 · PressShare 0.4 · Superior() }
  class PressesTheAttack { <<existing>> only OrderKind.Attack (CE-2467) }
  class BasicPlatoonSop { <<existing>> ElementReported → PlatoonHold }
  class PlatoonAttackBehavior { <<existing>> SupportByFire + Bound (OrderKind.Bound) }
  class FireAndMovementPlay { <<existing>> A-7 rushes in contact }
  BasicSquadSop --> ReactToContactBehavior
  ReactToContactBehavior --> SquadContactResponseDecision
  SquadContactResponseDecision ..> AfterContact
  ReactToContactBehavior ..> PressesTheAttack : an ATTACK order is not paused
  PlatoonAttackBehavior ..> PressesTheAttack : its squads are ordered Bound, not Attack
  PlatoonAttackBehavior ..> FireAndMovementPlay
```
*What the picture shows that prose hid:* every box exists — the suite builds NO behaviour; it measures which of these edges a run takes.

## 8. Claims *(code measured `2026-11-23`; the RUN half is what OF1 / OF2 / OF4 measure)*

| # | the claim | code — how it IS | design — how it was MEANT | the run |
|---|---|---|---|---|
| C-1 | a squad in contact below 3 : 1 and not outmatched HOLDS until the enemy is gone | ✅ `ReactToContactBehavior` decides once; `AfterContact.AssaultShare` 0.25 | ⚠ Squad Tactics S2-D / R4 · doctrine §2: win the firefight, then move | OF1 |
| C-2 | a platoon's bounding squad stops at contact | ✅ `PressesTheAttack` keys on `OrderKind.Attack`; `BoundingOverwatch` fulfils `Bound` | ⚠ Assault Under Fire A-7: in contact the assault moves by fire and movement | OF2 |
| C-3 | a reported enemy holds the whole platoon | ✅ `BasicPlatoonSop` — one contact row, `PlatoonHold` | ⚠ doctrine: the platoon leader manoeuvres another squad | OF4 |
| C-4 | the city battle's stalls are C-1…C-3 | ⛔ not measured (the showcase is too large to attribute) | — | the city battle, AFTER the suite is green |

## 9. Decisions — for the user, AFTER the suite shows red *(leans, not rulings)*

| | lean |
|---|---|
| D-1 when a squad may assault / flank | when its fire SUPPRESSES the enemy (fire superiority), not a fixed 3 : 1 headcount |
| D-2 how long a hold may last | ~30 s, then the squad reports and the platoon must act |
| D-3 what the platoon does on a contact | a platoon react-to-contact drill: the squad in contact is the base of fire, another squad flanks — instead of every squad holding |

## 10. Design docs checked

- `DESIGN_Urban_Fight_Suite.md` — applies: the rubric and the check rows are reused as they are.
- `DESIGN_Squad_Tactics.md` §4 — applies: owns C-1's choice (S2-D, R4); a fix to C-1 lands there.
- `DESIGN_Assault_Under_Fire.md` — applies: A-7 is the intended in-contact movement C-2 should reach; mostly BUILT (AU-1…AU-7).
- `DESIGN_Defend_And_Attack_Position.md` — applies: owns `PlatoonAttack`, judged by OF2.
- `DESIGN_City_Battle_Showcase.md` §12 — applies: plans a "stalemate probe"; this suite is that probe at small scale. ⛔ It records no measured open-field stall yet.
