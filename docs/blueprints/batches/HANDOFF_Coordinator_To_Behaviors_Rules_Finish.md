<!--STATUS
state: LIVE — coordinator → behaviors: finish the scenario-rules programme's behaviors half (the four open asks from ui and backend + the showcase AI findings)
updated: 2026-11-03
current-answer: the whole handoff
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11 (S4, measures), §00.11a (live edit, RuleEdit 433), §00.13 (S7 P-8b′, the bounty waits for the downing), §10.11 T4 (a deadline disarms), V-2 (`ExpectedAt`)
  - ../../DESIGN_Man_Downed_Event.md — CE-3364, the ONE kill authority
  - ../../DESIGN_Rules_View_UI.md — R4 Coming up / expected bars, RE-7 live Apply (reads ExpectedAt and the refusal)
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-4 `game.round-timer`
  - HANDOFF_Coordinator_To_Ui_Rules_Finish.md · HANDOFF_Coordinator_To_Backend_Rules_Finish.md — the sibling handoffs of this round
-->
# HANDOFF — coordinator → behaviors: **finish the rules programme's behaviors half**

**Dispatched at `313f42a67`** (`coordinator` = `ui@67bd589e8` + `backend@5599f85da`, both already containing `behaviors@611684de3`).
⭐ **Merge `origin/coordinator` first** — it carries backend's S7 P-8b′ and G-9 and ui's HUD/debrief, which items ① and ② touch.
⛔ **Scope is FROZEN at `313f42a67`.** Documents that change after it are FYI only; if one invalidates an item, STOP that item and report (R-106) — never the batch.

## Items, in order

| # | item | design basis | ⭐ lean / acceptance |
|---|---|---|---|
| **①** | ⭐⭐ **Downings with no `ManDownedEvent` on the Brain** — [`NOTE_Backend_To_Behaviors_Uncredited_Downings.md`](NOTE_Backend_To_Behaviors_Uncredited_Downings.md). Crossfire: 1 of 11 deaths (round 4), 2 of 16 (round 5) produced NO event naming the victim (`#1021`, `#1300`: `Deaths` = 1 but their streaks were not reset). ⚠ **cause unknown** — backend lists the candidates (health applied off the Brain with dt 175 not arriving · a downing that is not a hit · the shooter already down) | `DESIGN_Man_Downed_Event.md` (one kill authority) · S7 P-3 / P-8a read it | ⭐ find the cause first and write it into `DESIGN_Man_Downed_Event.md`; then make EVERY transition to out-of-the-fight publish exactly one event on the Brain (uncredited is fine: `Source` null). **Acceptance:** a rail per measured cause; live Crossfire round: credited + uncredited downings == `Deaths` sum |
| **②** | **An Armed trigger's `ExpectedAt` from the time its `when` waits on** — [`NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md`](NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md) + [`NOTE_Ui_To_Behaviors_Round_Clock.md`](NOTE_Ui_To_Behaviors_Round_Clock.md) (CE-4238). ⛔ not a deadline — T4 disarms | V-2 · T4 (`TriggerBehavior.cs:248`) | ⭐ in `TriggerBehavior.WriteViewData`: Armed, no deadline ⇒ the earliest future `ScenarioTime.fromSeconds` of its `when` (OR → min, AND → max, none → 0). ⭐ **Which trigger ends the round: NO new field** — ui derives it from the Do list containing `EndExercise` (ui's lean, agreed). **Acceptance:** rails for OR / AND / none / past; `GET /objectives` on Crossfire shows End's `expectedAt` = 900 |
| **③** | **The live-edit refusal's FULL text** — [`NOTE_Ui_To_Behaviors_Live_Edit_Refusal_Text.md`](NOTE_Ui_To_Behaviors_Live_Edit_Refusal_Text.md). Today `Reason` cuts it to ~55 chars and the journal entry has no text | §00.11a ② as built | ⭐ the full text in the `Reparameterised` journal entry (Cause 1 = refused), keyed by the edit's Seq; `Reason` stays short. Optional *"edit queued (acting)"* entry only if cheap. **Acceptance:** a rail with a two-problem refusal reading both problems back from the journal on a non-owner node |
| **④** | **CE-3361** (two AI findings from the showcases): ① a Red round hit a Red man (Bridgehead, 497.6 s, QRF firing on the crossroads); ② Blue breaks contact (see the row) — [`NOTE_Backend_To_Behaviors_Showcase_Findings.md`](NOTE_Backend_To_Behaviors_Showcase_Findings.md) §2 | the row in [`Blueprint_Issues_Tracker.md`](../Blueprint_Issues_Tracker.md) | ⭐ ① is a fire-discipline check (line of fire through a friend); ② per the row. ⭐ **CE-3362** (Crossfire point A never visited) is very likely the SAME cause as ② — backend's lead: the A squad reads task `Break` under its defend order. **Acceptance:** the live proof the row asks for; say in the report whether CE-3362 cleared with it (backend re-measures) |

⛔ **Not here:** new scoring kinds, any HUD work, the S7 P-leans (they await the user).

## Gates — the report contract (CLAUDE.md rule 8)
One row per gate (command · counts · delta) · the feature suites first (T-1: `ScenarioRulesTests*`, `ManDowned*`) · every red confirmed pre-existing against `313f42a67` · tree clean · `tracker-counts.py --check` · ids allocated from **your** block · design sections updated (① must name the `DESIGN_Man_Downed_Event.md` section it changed).

_coordinator lane, 2026-11-03_
