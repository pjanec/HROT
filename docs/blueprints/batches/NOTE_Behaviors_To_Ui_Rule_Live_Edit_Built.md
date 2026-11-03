<!--STATUS
state: LIVE
updated: 2026-11-03
current-answer: whole file (a short note)
related-designs:
  - docs/DESIGN_Scenario_Rules.md — §00.11a OWNS the live edit (the sequence, L1–L5, build rulings R1–R4, the AS BUILT)
  - docs/blueprints/batches/NOTE_Ui_To_Behaviors_Rule_Editor_Asks.md — ask ② this answers
-->
# Behaviors → ui: the rule LIVE EDIT is built (ask ②)

**Write:** one change list through `EntityWriteRouter` (applied here on the owner, a change-request to the owner elsewhere — the same path as `Rule.Request`):

| attribute | type | meaning |
|---|---|---|
| `Rule.Params` (id 43) | string | the rule's COMPLETE new params JSON (the shape a scenario carries — not a delta) |
| `Rule.EditSeq` (44) | int | a new value = a new edit; the owner applies each Seq ONCE (last seen + 1) |
| `Rule.EditOrigin` (45) | int | a `BehaviorOrigin` (the debug API sends Operator) — shown in the journal |

**HTTP:** `POST /objectives/{networkId}/edit {"params": {…}}` → `{networkId, seq, route: direct|requested}` (same shape as `/control`). The edit lands at the owner's next tick.

**Result is read from the status and the journal** (nothing else is sent back):
- applied ⇒ `ObjectiveStatus` keeps its state (FireCount, progress, cooldown clock, tally); one journal entry **"edited by <origin>"** (`BrainJournalKind.Reparameterised`);
- refused ⇒ status `Reason` = **"edit refused: <problem>"** (cut to ~55 characters — the status field is short; the journal entry has no text) + a journal entry **"edit refused by <origin>"**; the rule runs on unchanged.

**Which kinds can be edited live:** Trigger, HoldArea, ReachArea, ClearAreaObjective, Counter, HitRatio, Count, ReportLatency, TimeTo, Sample, AllMustPass, WeightedMean, SelectionMean, MemberEvaluation. **Every other kind** (NoHarm, Points, Best, Bounty, …) refuses with **"not editable live, restart the rule"**.

**Refusal reasons you may see:** *not editable live, restart the rule* · the kind's own validator problems (bad `when`, unknown action, missing field …, joined with "; ") · *the params did not parse* · *the entity runs no behaviour*. A KIND change is impossible: the edit carries no behaviour name.

**A trigger that is Acting WAITS:** the edit is held (nothing shown yet) until its `do` list ends, then applies whole — so the Edit button can show "pending" while `TriggerState = Acting`. SR-109: the scenario file is never touched.
