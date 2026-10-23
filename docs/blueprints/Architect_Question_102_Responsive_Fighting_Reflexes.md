<!--STATUS
state: LIVE — ✅ APPROVED 2026-10-23 on the chat summary (user: "Okay, write the scenario first, then implement it") — A…J as leaned below
updated: 2026-10-23
build-state: READY-TO-BUILD — order §6: the scenarios FIRST (baseline, measured red), then A…I
current-answer: §0 the idea · §3 the diagrams · §4 the sub-questions with their approved leans · §5 the scenarios · §6 the order
stale-below: nothing
known-rot: none yet
known-conflict: docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c D-2b accepts the dead ground under the upper windows — H here answers it for ONE man (the hunter); the rest of D-2 stands
related-designs:
  - docs/DESIGN_Peek_And_Fire.md — OWNS PeekAndFire (§8 spot heat, §10 the library node, §11 look-then-shoot, D12 "under fire = stay down", D14 "aim where remembered"); A, B, C, D reuse it
  - docs/blueprints/Architect_Question_97_Default_Reflexes_Soldiers_And_Civilians.md — OWNS the soldier's own SOP reflexes and why they stand aside under command (R-2062); this question puts the fighting reflexes BELOW that gate
  - docs/blueprints/Architect_Question_98_Cover_Reaction_Fights_Back.md — OWNS FightFromCover (R-2070, C″ CE-1269); unchanged
  - docs/blueprints/Architect_Question_96_Movement_Under_Fire.md — OWNS the under-fire move reflex (UnderFireMoveSystem, R-2061); I's grenade reflex follows its pattern
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS SquadDefend posts (DA-40…DA-45 the 4 m bound, DA-38 Displace); H widens the bound for one hunter
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS SuppressAndManeuver (§3), FireAndMovement (§7.6), BasicSquadSop's ReactToContact; G wires them as the default squad answer
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — OWNS the grenade before room entry (BC-4, CB-6/CB-30, RoomGrenade.GrenadeCheck); I reuses that gate outside the drill
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS picking up a landed round (AW-8, B-13 RoundPickUp) and the thrown-back canister (B-14); I's throw-back reuses it
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — OWNS the look node (LD-3); C changes what an unseen threat's look points at
  - docs/DESIGN_Decision_Layer.md — OWNS the utility decisions (§3.3e G6/A1 out of sight ⇒ flank or firing position); A and E change two of its inputs
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — the demo/check conventions the five scenarios of §5 follow
-->

# Architect Question 102 — responsive fighting reflexes: duck, find him again, grenades, flank

> 🔒 **User, 2026-10-23:** *"there are cases where the soldiers don't respond in a way that is expected from amusing first-person shooter
> … They are standing in the windows … without trying to take cover when the player starts firing at them … when the player is standing in
> front of the house … close to the wall … they are not trying to find him … without leaving the house."*
>
> 🔒 *"the grenade thing should apply also for the attacker … the intuitive thing is to throw inside a grenade … playing hide and seek behind
> obstacles in otherwise open area … they prefer to stay behind the obstacle without doing much, instead of … flanking him … or at least
> switching the points … pick sites pseudo-randomly … a new testing scenario with one friend and one enemy. And also a squad against
> another squad in the same terrain."*
>
> 🔒 *"when the grenade is thrown, then everyone who see it or hear it should be taking cover … run away and lay down with heads far from the
> grenade … a special counter Reaction is to pick the grenade and throw it back … if the grenade fuse is still expected to last some time."*
>
> 🔒 *"The open ground play should work also for the outside of houses where the house plays the obstacle … whether to take this way
> [through the building] or go around … I don't know how to decide."* · *"being in a house gives the character some kind of advantage …
> On the other hand, it makes it more difficult to shoot at someone who is outside hiding close to the wall."*
>
> 🔒 *"Will this reflexive behavior become part of every soldier?"* → yes (§0) · *"Okay, write the scenario first, then implement it."*

## 0. The idea

**A soldier in a fight never just waits: he keeps working to see the enemy without being seen.** Four reflexes, built ONCE in the parts
every soldier already runs, so no scenario needs a special behaviour:

| reflex | in one line |
|---|---|
| ① **under fire → get down** | duck under the sill / behind the car / prone for 1–3 s, come up somewhere else |
| ② **lost sight → find him again** | look where he was last SEEN; change spot, peek the other side, then flank |
| ③ **grenade near me → away or flat** | run out of its radius, else drop flat facing away; throw it back when the fuse allows |
| ④ **he hides within throw → grenade** | over his cover, through his window or door — under the ROE |

| layer | home | who gets it |
|---|---|---|
| the fight (①②④) | the shared combat blocks: `CombatPostureDecision`, `PeekAndFireNodes`, `LookNodes`, the EQS templates | every soldier who fights, under command or not |
| survival (③) | a reflex system BELOW the behaviours (the `UnderFireMoveSystem` pattern, AQ96) | every person — civilians run only |
| squad tactics | the squad's default SOP and its plays | every squad with the default squad SOP |

## 1. INVENTORY *(2026-10-23 — `scripts/cbm.sh search_graph` + grep; ⚠ `check_index_coverage` is not available through the CLI, so no
negative claim below is a coverage-checked one)*

| query (label Class) | total | what it found |
|---|---|---|
| `.*(PeekAndFire\|FightFromCover\|CombatPosture\|WindowDuel).*` | 25 | `PeekAndFireNodes/Params/State`, `FightFromCoverBehavior`, `CombatPostureBehavior`, `CombatPostureDecision`, `WindowDuelBehavior`, `VehicleFightFromCover*` |
| `.*(Flank\|FiringPosition\|ThrowingPosition\|CoverFromTarget).*` | 7 | `FindFlankingPosition`, `FindOpenFiringPosition`, `FindThrowingPosition`, `FindWindowFiringPosition`, `FindCoverFromTarget`, `FiringPositionMemory` |
| `.*(UnderFire\|NearMiss\|Suppress).*` | 35 | `NearMissSensingSystem`, `EntityUnderFire` (wire), `SuppressAndManeuverBehavior/Play/Maneuver`, `SuppressBehavior`, `SuppressNodes` |
| `.*(Grenade\|Throw\|PickUp).*` | 32 | `RoomGrenade.GrenadeCheck`, `HiddenTargetThrow`, `RoundPickUp` (+ `PickUpRoundEvent`/`PickUpGrantEvent`, wire payloads), `RoundPickUpSystem`, `ThrowArc`, `Civilians.Throwables` |
| `.*(Look\|Gaze).*Node.*` | 5 | `LookNodes`, `LookNodeState`, `LookAtEntityNode`, `LookAtPointNode` |
| grep `Obstacle_` (TKB) | 4 types | the parked car 8806 (4.5×1.8×1.5 m, `car-body`), sandbags 8807, concrete block 8808, crate 8809 — `BdcTkbCatalog.cs:1106-1114` |
| grep `.*GrenadeDanger.*\|LiveGrenade\|grenade.{0,40}(flee\|evade\|prone)` | 0 | ⛔ nothing senses a grenade near a man |

## 2. How it is — the claims the leans rest on

| claim | code (how it IS) | design (how it was MEANT) |
|---|---|---|
| an UNHURT man never picks TakeCover or HoldProne | ✅ both carry `HealthFraction` on `InverseLinear` (0 at full health) in a weighted product (`CombatPostureDecision.cs:45,58`; `ResponseCurveEvaluate.cs:23`) | ⛔ Peek & Fire D12 *"under fire = stay down"*; no design asks for "only the wounded hide" |
| the posture decision has no under-fire input | ✅ `CombatPostureDecision.cs:40-66` | ⛔ searched Decision Layer §3, none |
| a squad man's own SOP reflexes stand aside | ✅ `BasicInfantrySop.btree.json:380` (`UnderCommand` first), `SopConditions.cs:105` | ✅ AQ97 A (R-2062) |
| the window-duel man ducks because peek-and-fire IS his task | ✅ `WindowDuelBehavior.cs:36`, `PeekAndFireNodes.cs:724-746` | ✅ Peek & Fire D10/P-8 (*"demo-local"*) |
| a posted man may not move > 4 m from his post | ✅ `EqsTacticsNodes.cs:41-45`, `PeekAndFireNodes.cs:901,915` | ✅ Defend & Attack DA-44/DA-45 |
| the squad moves men between PLANNED posts by distance only | ✅ `SquadDefendBehavior.cs:217-231` | ✅ DA-38; ⛔ nothing designed for regaining sight below a window |
| his eyes follow a HIDDEN threat's true position | ✅ `LookNodes.cs:96` → `LookRequest.AtEntity` → `LookAtExecutors.cs:87` follows the entity | ⛔ Peek & Fire D14: aim where REMEMBERED |
| a lone man's flank / firing-position / close-to-throw exist — but only inside Advance, and only while the contact is LIVE (~14 s) | ✅ `CombatPostureBehavior.cs:186-199`, `AttackApproachDecision.cs` (every option needs `HaveLiveTarget`) | ✅ Decision Layer §3.3e G6/A1 |
| ⇒ after the live window a hidden enemy leaves only Hold (idle) | ⚠ inferred from the two rows above — ⭐ **the baseline scenario S1 measures it first** | — |
| the next peek spot is the best-scoring one, cooled by heat — deterministic | ✅ `PeekAndFireNodes.cs:292-295` (heat rules); no random | ✅ Peek & Fire §8 B2–B4 |
| the squad's suppress-and-flank exists | ✅ `SuppressAndManeuverBehavior.cs:1-80` (who sees = base of fire; the rest flank via `FindFlankingPosition`) | ✅ Squad Tactics §3 (CE-2123) |
| a squad type may have NO default SOP | ✅ squad 302 in `pc-defend-house` (`BdcTkbCatalog.cs:901,923`) | ⛔ none says a squad may have no SOP |
| a frag grenade lies where it lands until its fuse | ✅ `BallisticsSystem.cs:93-119` (CE-1032 W-4) | ✅ Building Interiors §3k |
| a landed grenade can be picked up and thrown | ✅ `RoundPickUp`, `RoundPickUpSystem` (CE-3235); a thrown-back canister keeps its burn | ✅ Civilians AW-8 (*"a canister, a grenade"*), B-14 |
| prone protects from a burst | ✅ `bt-grenade-posture` (B4) | ✅ Building Interiors §3f |
| the grenade before room entry is built, gated by intensity | ✅ `RoomDrill.cs:55-61,218`, `RoomGrenade.GrenadeCheck` | ✅ Building Clearing CB-6, CB-30, BC-4 (CE-2260) |
| ⛔ does a LYING round exist on the BRAIN node (replicated)? | ⛔ not measured | ⛔ not searched — **I-1 measures it first** |

## 3. The design

### 3.1 Classes *(grey = exists, unchanged · blue = exists, changed · green = new)*

```mermaid
classDiagram
  class CombatPostureDecision { +TakeCover: UnderFire, health floor
    +HoldProne: UnderFire, health floor }
  class AttackApproachDecision { +Flank/FiringPosition/CloseToThrow: LiveOrRecentlyLost }
  class StandardInputs { +UnderFire() +RecentlyLostContact() }
  class PeekAndFireNodes { +NextSpot: seeded pick among top-k, never the last }
  class LookNodes { +threat in sight: follow · unseen: look at remembered spot }
  class CombatPostureBehavior { posted window man: PeekAndFire }
  class RouteCost { +Score(route, threatSeenFrom) exposure·time·unknownBuilding·exit }
  class BuildingSafety { +KnownSafe(building, unit) }
  class GrenadeDangerSystem { +each frame: live rounds near persons }
  class GrenadeReflex { run · drop flat · throw back }
  class SquadDefendBehavior { +Hunter: one man, building radius }
  class BasicSquadSop { +contact lasting: SuppressAndManeuver }
  class RoomGrenade { GrenadeCheck CB-6 }
  class RoundPickUp
  class UnderFireMoveSystem
  class SuppressAndManeuverBehavior
  class FindFlankingPosition
  class FindWindowFiringPosition
  StandardInputs <.. CombatPostureDecision
  StandardInputs <.. AttackApproachDecision
  CombatPostureBehavior --> PeekAndFireNodes
  CombatPostureBehavior --> LookNodes
  AttackApproachDecision --> FindFlankingPosition
  FindFlankingPosition ..> RouteCost : scores candidates
  RouteCost --> BuildingSafety
  SquadDefendBehavior --> FindWindowFiringPosition : hunter, building-wide
  BasicSquadSop --> SuppressAndManeuverBehavior
  GrenadeDangerSystem --> GrenadeReflex
  GrenadeReflex --> RoundPickUp : throw back
  GrenadeReflex ..> UnderFireMoveSystem : same pattern (a reflex below the task)
  PeekAndFireNodes ..> RoomGrenade : window / door grenade gate
  style CombatPostureDecision fill:#cfe2ff
  style AttackApproachDecision fill:#cfe2ff
  style StandardInputs fill:#cfe2ff
  style PeekAndFireNodes fill:#cfe2ff
  style LookNodes fill:#cfe2ff
  style CombatPostureBehavior fill:#cfe2ff
  style SquadDefendBehavior fill:#cfe2ff
  style BasicSquadSop fill:#cfe2ff
  style RouteCost fill:#d1f7d6
  style BuildingSafety fill:#d1f7d6
  style GrenadeDangerSystem fill:#d1f7d6
  style GrenadeReflex fill:#d1f7d6
```
*What the picture shows that prose hid:* only four boxes are new; everything a reflex needs to DO (peek, flank, throw, pick up, suppress and
flank) exists — the defects are in the INPUTS that choose (A, E) and in a missing sense (I).

### 3.2 Sequence — 1v1 behind cars: lost sight escalates instead of idling (E, D, C)

```mermaid
sequenceDiagram
  participant B as Blue (behind car 1)
  participant P as CombatPosture + decisions
  participant Q as EQS (cover / flank / throw)
  participant R as Red (behind car 2)
  R->>B: shots (near misses)
  B->>P: UnderFire ⇒ TakeCover wins (A)
  P->>B: PeekAndFire: duck 1–3 s, next spot = seeded pick ≠ last (D)
  Note over B,R: Red ducks too — no one in sight
  P->>B: look at Red's REMEMBERED spot (C)
  loop lost sight (RecentlyLostContact, E)
    alt < ~5 s
      P->>B: peek from the other end / height
    else 5–10 s
      P->>Q: FindOpenFiringPosition (another car)
    else > ~10 s
      P->>Q: FindFlankingPosition, routes scored by RouteCost (F)
    end
    opt Red within throw (~25 m)
      P->>Q: FindThrowingPosition ⇒ grenade over car 2 (CloseToThrow)
    end
  end
```
*What the picture shows:* the escalation is ONE timer on the contact's last sighting feeding inputs the decisions already read — not a new
behaviour.

### 3.3 Sequence — a grenade lands (I)

```mermaid
sequenceDiagram
  participant G as live grenade (lying, fuse t)
  participant D as GrenadeDangerSystem
  participant M as a man within 2× its lethal radius
  participant T as his task (any)
  G->>D: position, fuse left, lethal radius
  D->>M: sees it (LOS) or heard it land (≤ 15 m)?
  alt fuse left ≥ pick + throw time and he is the nearest able man
    D->>M: throw it back (RoundPickUp ⇒ throw toward its thrower)
  else he can leave the radius before the fuse
    D->>M: run directly away (pauses T's move, like UnderFireMoveSystem)
  else
    D->>M: drop prone, feet toward it
  end
  D->>T: hand the task back after the burst
```

### 3.4 Modules — who runs what, each frame

```mermaid
graph TD
  BT[BrainTickSystem - brain node] --> CP[CombatPosture tree]
  CP --> DEC[CombatPostureDecision / AttackApproachDecision]
  CP --> PF[PeekAndFireNodes]
  CP --> LK[LookNodes]
  BT --> SQ[squad plays: SquadDefend Hunter, SuppressAndManeuver]
  SOP[BasicSquadSop - every squad type] --> SQ
  GDS[GrenadeDangerSystem - brain node, after perception] --> REF[GrenadeReflex]
  REF -. pauses the move of .-> ANY[any task]
  UFM[UnderFireMoveSystem - exists] -. same phase .-> GDS
  NODE{{lying rounds on the brain node?}} -. I-1 measures .-> GDS
  style GDS fill:#d1f7d6
  style REF fill:#d1f7d6
  style NODE fill:#ffe0b3
```
*Caption:* the one unmeasured edge is orange — the danger system can only run where the brain is if the lying round is there too
(replicated); if not, the round's node raises a "grenade near X" event instead (I-1 decides, before I is built).

## 4. The sub-questions — ✅ approved leans (2026-10-23)

| # | question | ✅ lean | rejected (one line each) |
|---|---|---|---|
| **A** | Why does an unhurt man never take cover? | add `UnderFire` (hit / near miss within ~3 s, `RecentSensesOf`) to TakeCover and HoldProne; health becomes a FLOOR (0.3), not a zero | a new "duck" reaction — under command it would be gated (R-2062) |
| **B** | Posted window defenders | a man posted at a window/frame fights through `PeekAndFire` (its window shape) — duck, peek, fire, switch window — whatever the decision's Suppress/Hold says; the post radius stays | lifting `UnderCommand` — pauses his post order (R-2062 ②) |
| **C** | Where an unseen threat is looked at | in sight now (visual, ≤ 0.5 s) ⇒ follow him; else ⇒ his remembered spot at the eyes of the stance he was seen in (standing 1.6 · crouched 1.0 · prone 0.3 m) | always the remembered spot — a visible man's look would lag |
| **D** | The next peek spot | a SEEDED pick among the top-k (3) spots within 85 % of the best score, never the last used; seed = unit id × spot count (repeatable per run) | true random — unrepeatable tests · best-only — learnable |
| **E** | Lost sight on open ground | `RecentlyLostContact` (a remembered hostile last seen ≤ 60 s) replaces `HaveLiveTarget` in the approach options' gate; the escalation by its age: < 5 s peek elsewhere · 5–10 s another firing position · > 10 s flank · within throw ⇒ grenade | a new "Hunt" behaviour — duplicates Advance's approaches |
| **F** | Through a building or around; a house as cover | route candidates (with and without building interiors) scored by `RouteCost` = exposure from the threat's last-seen spot + time + unknown-building risk + exposed exit; a building counts only if `KnownSafe` (empty in memory or held by his side; a Veteran may clear a small one); near ties ⇒ D's seeded pick · a house window is just a cover point in the same search (must SEE the target's spot), with a grenade-trap penalty within throw range and a one-exit penalty | "always through when there's a door" — the enemy may hold it · "never use houses" — gives up the best cover |
| **G** | Squad v squad | every squad type gets `BasicSquadSop`; its contact answer escalates to `SuppressAndManeuver` (who sees = base of fire; the rest flank on `FindFlankingPosition` scored by F's `RouteCost`) when the contact lasts > ~10 s without a kill | a new flank play — SuppressAndManeuver is it |
| **H** | A known enemy under the defended house | `SquadDefend` assigns ONE hunter (nearest free) to a remembered hostile within ~30 m of the building, unseen > 2 s; his bound widens to the building's openings (`FindWindowFiringPosition` toward the remembered spot, inside the building only); he may drop a grenade (I-④); back to his post after ~15–20 s quiet | everyone hunts — opens the sectors · nobody — the user's complaint |
| **I** | Grenades | **I-1** measure where the lying round lives · **I-2** `GrenadeDangerSystem`: a person within 2× the lethal radius who SEES the round or heard it land (≤ 15 m) · **I-3** reflex: throw back (nearest able man, fuse left ≥ pick + throw, `RoundPickUp`) › run directly away (if he leaves the radius in time) › drop prone facing away; the task resumes after · **I-4** attackers: the existing `RoomGrenade.GrenadeCheck` (CB-6 / CB-30 intensity, no friend in the radius, no non-combatant) gates a grenade through any door or window of a building an attacker assaults — inside or outside `ClearBuilding` | grenade reactions as SOP rows — gated under command · a second ROE rule for frags — CB-6 already is it |
| **J** | Skill | untrained: duck + blind fire at the last spot only · Regular: all of the above within his area, known-safe buildings only · Veteran: switches at once when shot at, other storeys, clears a small building on the way | one level for all — loses the militia/veteran contrast PC-15 built |

**Reach:** A changes a decision every `CombatPosture` user runs (attackers holding hide more under fire — measured on the attack demos);
E changes the approach gate (more flanking everywhere); the rest are additive.

## 5. The scenarios — written FIRST, run as a baseline (measured red), then green one by one

| id | ground | cast | passes when | seed |
|---|---|---|---|---|
| **S1** `rf-car-duel` | `basic-desert`, six parked cars (8806) in two loose rows ~40 m apart | 1 Blue rifleman v 1 Red rifleman, own SOP, no orders | neither idle-hidden > 12 s · ≥ 2 distinct peek spots each, never the same twice running · ≥ 1 flank or firing-position move · a grenade over cover once within 25 m | fixed |
| **S2** `rf-car-squads` | the same ground | a Blue squad v a Red squad (default squad SOP) | a base of fire holds while the rest move · ≥ 70 % of the flankers' path out of the enemy's sight · the flank ends in an assault or the enemy breaks off | fixed |
| **S3** `rf-house-obstacle` | `test-town`, a two-door house between them | 1 v 1; variant A the house empty in memory, variant B unknown | A: the flanker may go THROUGH; B: he goes AROUND | fixed |
| **S4** `rf-wall-hugger` | `test-town` CQB house, 3 window defenders (SquadDefend) | a scripted Red attacker fires at a window, then hugs the wall | defenders duck when shot at (B) · ONE moves inside to see him (H) · a grenade dropped (I-4) · no head tracking him through the wall (C) | fixed |
| **S5** `rf-grenade-reaction` | open ground | a grenade lands among 4 Blue men | all who see/hear it run or drop · one throws it back when the fuse allows | fixed |

⭐ Each gets a check in `scripts/utility-demo-check.py` and an in-process twin in the feature's own suite (`SquadManeuverTests`,
`TacticsTreesTests`, `BasicInfantrySopTests`).

## 6. Order *(each step: rails red-proved, the scenario's numbers re-measured)*

| # | what | lane |
|---|---|---|
| 1 | S1…S5 written + their checks; run ⇒ the baseline numbers recorded here (§7) | behaviors |
| 2 | A + B + C + D (the duck, the window defender, the eyes, the spot pick) | behaviors |
| 3 | E + F (lost sight escalation, route cost, house as cover) | behaviors |
| 4 | G (squad default SOP + SuppressAndManeuver escalation) | behaviors |
| 5 | H (the hunter) | behaviors |
| 6 | I-1 measure → I-2…I-4 (grenade danger, throw-back, attacker grenades) — ⚠ may need a backend replica of lying rounds | behaviors (+ backend if I-1 says so) |
| 7 | J tuned on all five; fold the as-built into Peek & Fire, Defend & Attack, Squad Tactics, Decision Layer | behaviors |

## 7. Baseline *(filled by step 1)*

| scenario | measured | |
|---|---|---|
| S1 … S5 | — | — |
