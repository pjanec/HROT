<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-23
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §3 the context keeper, §5b the plays as tools, §5a-3 your L-3 as-built, §11 L-3a
  - STATUS_Backend_LLM_Commander.md — your L-3 report: the Haiku relay run and its three findings (this slice fixes them)
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER, slice L-3a — the briefing a small model can live with (CE-4683)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-23`: *"backend fix"* — the three findings of your L-3 Haiku relay run, fixed BEFORE the laptop run (L-3b), so the 7–8 B models
are not judged on defects we already know.

## Priority
Next, after the row in progress. Continue `STATUS_Backend_LLM_Commander.md` (a new §, L-3a); decisions to `DECISIONS_Backend.md`. Still on the
cloud VM; the proof is the Haiku relay again.

## What L-3a fixes

| # | the finding (your L-3 run) | ⭐ the fix (lean — a different one is yours to choose, said in DECISIONS) | done when (rail, red-proved) |
|---|---|---|---|
| ① | **the notes grow without bound** — a note on every call, 18 KB of a 20 KB user message after 48 calls (CE-3501) | `note` becomes the commander's **running estimate**: it REPLACES the previous one (not appended), capped (≈ 800 characters, cut with a marker); the tool's description says so | a long run: the user message stays bounded — no part of the briefing grows with the number of calls |
| ② | **he loses his own orders** — a snapshot clears the radio log, the `ORDER … given` lines go with it, and he re-sent `SupportByFire` 7 times to 2 squads | every snapshot carries an **"ORDERS IN FORCE"** block: per squad its current order (kind → where, given at, by you or by play X) and its latest progress; ⭐ an order identical to the one in force is a no-op answered *"already in force since mm:ss"* (an `order-state` item) — never re-sent to the squad | a repeated identical order changes nothing and is answered so; the block survives a snapshot |
| ③ | **he barely uses the plays** — with the actions as functions he ran one play, then only direct orders (three plays via the routes in L-2b) | the prefix and the tool descriptions say it plainly: *"prefer `run_play` — the play coordinates your squads; a direct order ENDS the play"*; `run_play` listed FIRST in the tools; the snapshot names the play in force | the prefix and `/tools` carry it (rail); the relay run below shows the effect, either way |
| ④ | **a budget a small model can hold** — the request grew to ≈ 7 350 tokens | a measured budget per call: report the request size (characters, estimated tokens) at the first, the median and the last call; ⭐ target ≤ ≈ 4 000 tokens for the whole run of `llm-platoon-attack` (an 8 B model at `num_ctx` 8 k must hold it plus its answer) — if a part cannot fit, say which and why | a rail: the briefing at the end of a long scripted run stays under the budget |

⛔ No ground truth enters, as before. ⛔ The scripted mind's behaviour does not change (8/8 stays).

## The proof (on the VM)
The Haiku relay run of L-3 again, the same scenario and settings. Report it **side by side with the L-3 run**: calls · tool calls · notes size
at the end · identical orders re-sent (and answered "already in force") · `run_play` uses · request size first / median / last · the checks
(the squad-attack stall CE-4682 still limits the tactical rows — not this slice's defect). Quote 5 exchanges.

## ⛔ Not in L-3a
The laptop run (L-3b — next, the user's machine), continuous mode and several seats (L-4), the replay memory defect (CE-3493).

## Gates (CLAUDE.md rule 8)
Your commander rails (L-1 … L-3) + the new ones (each red-proved) + `DebugApiCommandersTests` + `ScenarioChecksTests` + the scenario with the
scripted mind (8/8) + the relay run. Fold the as-built into the design (obligation ⑤). Close CE-3501 into this row. Report the ids you allocated.
