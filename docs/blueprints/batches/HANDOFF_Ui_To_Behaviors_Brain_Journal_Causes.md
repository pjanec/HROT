<!--STATUS
state: LIVE — ui → behaviors: CE-1250, the part of the brain journal only behaviors' code can fill.
updated: 2026-10-23
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Entity_Brain_View.md — (ui) OWNS the journal: BV-10 the entries, BV-12 the two causes, BV-14 SquadDrillTrace kept, §7 P2b this slice, §11 as-built
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — (behaviors) §7.8.1 OWNS SquadDrillTrace; the play entries are written beside it, it stays
-->

# HANDOFF — ui → behaviors: the brain journal's causes and play entries (`CE-1250`)

**Dispatched at** `ui@16bf5faa5` · **tracker:** [`CE-1250`](../Blueprint_Issues_Tracker.md) (filed by ui; renumber in your block on
pickup if you like — the row stays) · **design:** [Entity Brain View](../../DESIGN_Entity_Brain_View.md) BV-10, BV-12, BV-14, §7 P2b ·
ruling `R-1026` (approved by the user).

**What already works (ui built it, `2026-10-23`):** every brain unit carries a `BrainJournal` (component 460, 24 entries, NoScenario,
`--brain-journal off` switches it off). Task start / pause / resume / end / clear / refusal, the SOP, utility decision switches, moves and
HSM state changes are written. The Brain tab and `GET /entities/{id}/journal` show them. ⇒ ⭐ **Nothing blocks on you.** The journal just
cannot yet say WHY a reaction started, WHY a unit moved, or what its squad's play did.

## ⭐ ui built the receiving half — you fill the producers

| what you set | where | what it shows once set | ui side (built, this commit) |
|---|---|---|---|
| **① `AssignBehaviorEvent.Cause`** (`SensorChange`, new field, default 0) | `SopActions` (FDP `Behavior/SopActions.cs:92`) — the sense the reaction answers; any other reaction publisher you know of | *"started TakeCover (Reaction, Hit) after NearMiss"* | the field; the ingress passes it into the `TaskStarted` entry's `Cause`; the tab / route text. Rail `SopSlotTests.CE1249_AReactionPausingTheTask…` |
| **② the move's source** — call `BrainJournalOf.NoteMoveSource(world, self, channel.ActionInstanceId, MoveSource.X)` right after the new instance is stamped | ⭐ **one place:** `LocomotionMoveTo.Issue` (`Hrot.AI.Behaviors/Brains/LocomotionMoveTo.cs`) — give it a `MoveSource source = MoveSource.SourceUnknown` parameter, then fill the callers. **46 calls in 18 files**; lean order: squad posts, EQS cover (`EqsCombatNodes`, `EqsTacticsNodes`), orders, flee, doors | *"move action 1 to (412, 220) (CoverSearch)"* | `MoveSource` enum (Order, CoverSearch, SquadPost, FollowLeader, Flee, Door, Route, Wander, Operator); `NoteMoveSource` / `MoveSourceOf`; the dispatcher's `MoveStarted` entry reads it for that instance only. Rail `BrainJournalTests.AMoveSource_BelongsToTheCommandItWasNotedFor` |
| **③ the play entries** — `BrainJournalOf.Write(world, commander, PlayStarted / PlayPhase / PlayEnded, a: play, b: phase, cause: event)` | ⭐ inside the three `SquadDrillTrace` statics — `Started`, `PhaseChanged`, `Ended` (`Squad/State/SquadDrillTrace.cs`; called from `SquadManeuverRunner.cs:408/416/438/455`) — so every play writes both, `SquadDrillTrace` stays (BV-14) | *"play 3: phase 2"* — ui will name plays / phases with `SquadDrillNames` once the kinds land | the kinds exist; the text has a numeric fallback |
| **④ `OrderIssued`** — `Write(world, commander, OrderIssued, a: member's network id, b: behaviour hash)` | where the commander's order to a member is issued (`SquadManeuverRunner.cs:~507`, beside `SquadDrillTrace.Order`) | *"ordered #1043: SquadPostMove"* | kind + text exist |

### ⚠ The one design change you need to know — `MoveToParams.Source` cannot exist

BV-12 said to add `Source` to `MoveToParams`. **Measured: the struct is at its 32-byte limit** (`NavigationActions.cs:34`; CE-3128 already
had to pack `RoadUse` into `Flags` bits 5–7) **and all 8 `Flags` bits are taken** (0 replan, 1 on-surface, 2 release-hold, 3 corridor
preview, 4 auto-send path, 5–7 road use). ⇒ the source rides in the journal instead (②): the issuer notes it for the command INSTANCE,
the dispatcher reads it once when that instance starts, and a later command without a note reads Unknown. Folded into BV-12 as-built.
⚠ A move started by something other than `Issue` (a TKB order, the operator's station) reads Unknown until its writer notes a source.

## Acceptance (§8 P2b)

- a near miss → the reaction's `TaskStarted` entry names `NearMiss`;
- a squad-post move → its `MoveStarted` entry reads `SquadPost`;
- a play phase change → a `PlayPhase` entry with its cause; `SquadDrillTrace` unchanged (`GET /entities/{id}/squad` still reads it).

⭐ Rails belong in your own suites (the squad / SOP ones). The journal costs one presence check when the unit has none
(`BrainJournalOf.Write` is a no-op there), so no gating is needed at the call sites.

---
_ui lane, 2026-10-23_
