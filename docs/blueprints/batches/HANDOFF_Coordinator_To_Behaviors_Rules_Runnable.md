<!--STATUS
state: LIVE — coordinator → behaviors: make the rules programme's behaviors features RUNNABLE AND TESTABLE BY HAND (two small data asks + live-verified manual-test rows)
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../MANUAL_TEST_Scenario_Evaluation.md — ⭐ the deliverable: your rows' steps, run live
  - ../../DESIGN_Scenario_Rules.md — §00.11 (S4), §00.11a (live edit, RuleEdit 433), §10.11 T4 (a deadline disarms), V-2 (`ExpectedAt`)
  - ../../DESIGN_Man_Downed_Event.md — CE-3364, the one kill authority (item ④, optional)
  - HANDOFF_Coordinator_To_Backend_Rules_Runnable.md · HANDOFF_Coordinator_To_Ui_Rules_Runnable.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → behaviors: **runnable and testable by hand**

**Dispatched at `ae04e0841`.** ⭐ Merge `origin/coordinator` first (it carries `ui@67bd589e8` + `backend@eb88c5159`). Replaces
`HANDOFF_Coordinator_To_Behaviors_Rules_Finish.md` (withdrawn unstarted).
⛔ **Scope is FROZEN at `ae04e0841`.** A later document that invalidates an item ⇒ STOP that item and report (R-106), never the batch.

🔒 **User, `2026-11-03`:** *"rewrite the handoffs … to the level all the features are runnable and testable manually, no need to have all bugs fixed."*
⇒ ⭐ **The goal is a person can try every feature by hand.** A bug that does not stop a row is NOTED in the row, not fixed here.

⭐ **Do ① and ② FIRST and push** — ui's round clock (A6) and live-edit refusal (A5) wait on them.

| # | item | basis | ⭐ lean / done when |
|---|---|---|---|
| **①** | **An Armed trigger's `ExpectedAt` from the time its `when` waits on** (CE-4238; `NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md`, `NOTE_Ui_To_Behaviors_Round_Clock.md`). ⛔ not a deadline — T4 disarms | V-2 · `TriggerBehavior.cs:248` | ⭐ `WriteViewData`: Armed, no deadline ⇒ the earliest future `ScenarioTime.fromSeconds` (OR → min, AND → max, none → 0). Which trigger ends the round: ui reads the `EndExercise` step, **no new field**. Done: rails OR / AND / none / past; Crossfire `GET /objectives` End `expectedAt` = 900 |
| **②** | **The live-edit refusal's full text** in the `Reparameterised` journal entry, keyed by the edit's Seq (`NOTE_Ui_To_Behaviors_Live_Edit_Refusal_Text.md`) | §00.11a ② | `Reason` stays short. Done: a two-problem refusal read back whole on a non-owner node |
| **③** | ⭐⭐ **Write and LIVE-RUN your rows** of [`MANUAL_TEST_Scenario_Evaluation.md`](../../MANUAL_TEST_Scenario_Evaluation.md): **A1** (objectives tick and report) · **A2** (a trigger's Do, step by step) · **A6** data half · **B1** (Bridgehead per-member standards, role / standard means) — co-write A1/A2 with ui's screen half | the guide §2 | each row: exact steps from a fresh start → what you see; *verified* = branch@sha + date. Anything broken that STOPS a row ⇒ fix it here if small, else a tracker row and say so in the guide |
| **④** | *(only if time remains — not a gate)* the downings with no `ManDownedEvent` (`NOTE_Backend_To_Behaviors_Uncredited_Downings.md`: ~1 in 10 deaths on Crossfire) · CE-3361 (Red hits Red; Blue breaks contact — likely also CE-3362, point A never visited) | the notes / rows | ⛔ not required for this batch; note them in rows C2 / C1 as known |

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate · T-1 feature suites first (`ScenarioRulesTests*`, `TriggerBehavior*`) · every red confirmed pre-existing against `ae04e0841` · tree clean · ids from **your** block ·
⭐ **the guide rows you verified, by id** (the real acceptance of this batch) · designs updated as built.

_coordinator lane, 2026-11-03_
