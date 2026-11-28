<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-23
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §5c (C-1 … C-6) is this slice; §5a-3 / §5a-4 / §5a-5 your L-3, L-3a and the duel
  - STATUS_Backend_LLM_Commander.md — continue it
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER, slice L-3c — the decision contract (CE-4684)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-23`: an outside proposal for an LLM commander (a SITREP, an order schema, an example decision), compared with ours → *"yes.
and pls use haiku at lowest effort so it is closest to local LLMs"*.

## Priority
Next, after the row in progress — **before L-3b** (the laptop run measures the contract we keep). Continue `STATUS_Backend_LLM_Commander.md`
(a new §, L-3c); decisions to `DECISIONS_Backend.md`. On the cloud VM.

## Read first
Design §5c end to end (its class diagram is the spec). The comparison in its opening paragraph says what we ALREADY match — keep all of it.
⚠ The plays, the command chain, perception and navigation are other lanes' code: CALL them, ⛔ do not change them; a needed change is a NOTE.

## What L-3c builds (design §5c)

| # | builds | done when (rail, red-proved) |
|---|---|---|
| C-1 | ⭐ **one decision object per wake** for the chat-model mind: the JSON schema (fields in order: cycle → assessment → intent → orders → contingencies → next_decision; the task enum = your order kinds + the plays + `CONTINUE`; one flat condition shape, no `oneOf`), sent as Ollama's `format`; validated after decoding (ids exist, the task fits the unit, the play fits the commander), repaired ONCE with the errors, else `CONTINUE`. The model log keeps prompt · raw output · validation · orders applied per cycle | a rail per failure: an unknown id, a task the squad cannot do, a malformed object → repaired once → else nothing changes |
| C-2 | **`start` / `complete_when` / `abort_when`** on each order and **≤ 3 contingencies**, executed by a serial `ConditionWatcher` with NO model call; a fired contingency or an abort is an `order-state` item in the next briefing | a contingency fires on its trigger within one frame, and the next briefing says so; an order with `start: UNIT_AT` waits, then goes |
| C-3 | **`next_decision`** (not later than + ≤ 3 wake conditions) replaces the fixed heartbeat for this mind (a floor and a ceiling you choose); the **cycle echo**: a decision made on an older cycle than the newest wake is refused and the model re-asked | a stale cycle is refused; the model's own deadline wakes it |
| C-4 | **terrain facts per named place** in the static block, computed ONCE at seat open from HROT's own sight and navigation: which named places see it, cover (covered / partial / open), whether a vehicle can reach it, travel minutes on foot between neighbours | the facts for `llm-platoon-attack`'s places are listed and match a hand check of two of them |
| C-5 | **enemy confidence** HIGH / MED / LOW per contact (squads that reported it, report age; "heard" when behaviors adds the bit — CE-4682's NOTE asks) | a single old report reads LOW, two squads' fresh reports read HIGH |
| C-6 | **ids only** in the chat mind's vocabulary (no "N m dir of", no grid — kept for the scripted mind and the routes) | the schema's id fields accept only listed ids |

⭐ Keep the L-3a budget: report the request size first / median / last; ≤ ≈ 4 000 tokens.

## ⭐ The proof — Haiku at the LOWEST effort
⚠ User: Haiku at the lowest effort is the closest stand-in for a local 7–8 B model. ⇒ **every relay worker in this slice is spawned with
`model: "haiku"` and `effort: "low"`** (the Agent tool's lowest), and gets only the request JSON, as before.

1. **Baseline first:** re-run the L-3a format (tool calls) at low effort on `llm-platoon-attack` — the L-3a run was at the default effort.
2. **The contract:** the same scenario with the decision contract, low effort.
3. **The duel:** `llm-platoon-duel` with the contract on both sides, low effort.
Report side by side (baseline vs contract): calls · decisions refused / repaired / fell back to CONTINUE · contingencies fired · aborts ·
`next_decision` deadlines it set · request tokens first / median / last · the checks (CE-4682's stall still limits the tactical rows — say so,
not L-3c's defect). Quote 3 whole decisions (assessment, intent, orders, contingencies) as the model wrote them.

## ⛔ Not in L-3c
Fire requests and mission-constraint checks (no fire support at platoon level yet), the laptop (L-3b, next), continuous mode and several seats
on one model (L-4), CE-3493.

## Gates (CLAUDE.md rule 8)
Your commander rails (L-1 … L-3a, the duel) + the new ones (each red-proved) + `DebugApiCommandersTests` + `ScenarioChecksTests` + the scripted
mind on `llm-platoon-attack` (8/8, unchanged) + the three relay runs; the parallel env on the new watcher system. Fold the as-built into design
§5c (obligation ⑤). Report the ids you allocated.
