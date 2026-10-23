<!--STATUS
state: LIVE — ui → behaviors: a measured behaviour finding to check (CE-1269); ui builds nothing for it.
updated: 2026-10-23
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_98_Cover_Reaction_Fights_Back.md — (behaviors) OWNS FightFromCover's end (AQ98 A–C, R-2070): quiet in cover ends it, WouldReturn keeps it while there is nothing to resume
  - docs/DESIGN_Entity_Brain_View.md — (ui) OWNS the brain journal that showed this (CE-1249, CE-1250 causes)
-->

# HANDOFF — ui → behaviors: a lone soldier restarts FightFromCover every 1–3 s against the same contact (`CE-1269`)

**Dispatched at** `ui@180c4845a` · **tracker:** [`CE-1269`](../Blueprint_Issues_Tracker.md) (filed by ui for behaviors) · **your
design:** [AQ98](../Architect_Question_98_Cover_Reaction_Fights_Back.md) / `R-2070` · **found by** the brain journal's live check
([Agent Shared View §10.1](../../DESIGN_Agent_Shared_View.md)).

## What the journal shows *(headless editor, `df-lone-soldier-reflex`, unit 1000 "Reflex", at `ui@ec556fdb5` — your CE-1250 causes included)*

```
0.37  started FightFromCover (Reaction, Contact), replacing Idle after FirstThreat
0.45  move action 1 to (231, 171) (CoverSearch)
8.85  FightFromCover ended: Success
8.87  started Idle (Sop)
8.95  started FightFromCover (Reaction, Contact), replacing Idle          ← no cause
9.05  move action 1 to (231, 171) (CoverSearch)
11.15 FightFromCover ended: Success
11.35 started FightFromCover (Reaction, Contact), replacing Idle          ← no cause
12.35 FightFromCover ended: Success
12.45 started FightFromCover (Reaction, Contact), replacing Idle          ← no cause
```

Same contact, same cover point, a fresh cover search each time. ⚠ This is exactly the loop `FightFromCoverBehavior.WouldReturn`'s own
comment says it prevents (*"ending would only let the SOP's enemy-in-sight row start the reaction again a frame later — the cover
search restarting every few seconds"*, `FightFromCoverBehavior.cs:119`).

## Claim table

| claim | evidence | status |
|---|---|---|
| the restarts come from a CONDITION row (the enemy-in-sight row), not a sense row | sense rows note a cause (`SopConditions.cs:74` → `TakeReactionCause`); the first start reads *"after FirstThreat"*, every restart reads none | ✅ measured |
| the reaction **replaced** Idle — nothing is paused | journal *"replacing Idle"* (an SOP-started Idle) ⇒ `PausedTaskOf` is null ⇒ `WouldReturn` = `EnemyInSight` | ✅ measured |
| at the tick it ended he still remembered the hostile, seen visually ~13 ticks before | journal break on `TaskEnded` (`POST /entities/1000/journal/break {kinds:[TaskEnded]}`), then `GET /entities/1000/memory`: one slot, `networkId 1002`, not anonymous, `lastSeenTick 686`, modalities 9, alive; the sim stood at ~699 frames | ✅ measured (⚠ the frame count is read from `simTime × 60`, not from `GlobalTime.FrameNumber` directly) |
| ⇒ `EnemyInSight` was true, so the quiet end (line 91) was blocked, and the Success came from **PeekAndFire's own end** — *"nothing left to fight"*, `TopAim` false (`PeekAndFireNodes.cs:315-319`, returned at `FightFromCoverBehavior.cs:96`) | inferred from the two rows above | ⛔ **not measured** — which exit fired is the first thing to check (Debug this unit + the BTree trace, or a breakpoint in `Run`) |
| if so, the root is two definitions of "an enemy" disagreeing: `TopAim`/`TopThreat` (Engageable: in the fight, not neutral) says none while `EnemyInSight` (visual, seen within 60 ticks, in the fight, `KillZone.Hostile`) says one | code read only | ⛔ not measured |

## What to check *(your call — ui builds nothing here)*

1. Which exit returned Success: the quiet end, or PeekAndFire's *"nothing left to fight"*.
2. If PeekAndFire's: why `TopAim` finds nothing while the memory holds a visible hostile — and whether its end should defer to the same
   `WouldReturn` guard (end only when there is a task to resume or no enemy in sight), so the two ends agree.
3. Your SOP suite has the rail shape already (`BasicInfantrySopTests.CE2079_*`: *"older than this run: no second reaction"*) — a rail
   that keeps a visible hostile remembered and asserts ONE FightFromCover run would pin it.

📐 Reproduce: `scripts/cluster-node.sh start editor 8131` → `POST /scenario/load/live {name:"df-lone-soldier-reflex"}` →
`POST /sim/step {count:900}` → `GET /entities/1000/journal`. Not deterministic run to run (the first end was at 8.85 s once, 7.15 s
another time); the restart pattern held in all three runs.

---
_ui lane, 2026-10-23_
