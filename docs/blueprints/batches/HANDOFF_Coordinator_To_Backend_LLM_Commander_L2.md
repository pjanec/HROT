<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-22
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §3 the stream and the HTTP surface, §4 the minds (ExternalMind), §5a L-1 as built (your own), §11 L-2
  - STATUS_Backend_LLM_Commander.md — your L-1 report: think-pause holds the FRAME (decision 1) — L-2 replaces it
  - ../../blueprints/DESIGN_Paused_And_Stepped_Execution.md — OWNS pause / step; the seat's hold must use the one pause path
  - ../../DESIGN_Entity_Control.md — C-9 / C-10, the AI player's fog of war (CE-4677)
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER, slice L-2 (CE-4679)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there —
later documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-22`: *"simulated by Haiku worker as the first iteration, and later … a real local language model using Ollama"* · *"the
information might need to be streamed into the LLM as the simulation goes"* · *"yes, write the L-2 handoff for backend"*.

## ⭐ Priority
Next, after the row in progress. Status: continue `STATUS_Backend_LLM_Commander.md` (a new §, L-2); decisions to `DECISIONS_Backend.md`.

## Read first
The design §3, §4 and your own §5a. ⭐ L-2 adds an OUTSIDE mind and the HTTP stream on the SAME channel — nothing in the feed system, the action
system or the gazetteer should need to change; if one does, say why in the report.

## What L-2 builds

| # | builds | done when (rail, red-proved) |
|---|---|---|
| ① | ⭐ **think-pause pauses the SIM CLOCK, not the frame** (your L-1 decision 1): while a ThinkPause seat's mind is thinking, the simulation is paused through the ONE pause path the editor's `/sim/pause` uses, and resumed when the answer is in — the frame, the UI and the HTTP server keep running. ⚠ A seat's hold never un-pauses a pause the USER set (a hold reason / count, not a toggle); a hung mind is bounded by a wall-time limit, after which the seat answers "no orders" and resumes | a mind that takes 10 s: sim time does not advance during it, frames keep coming, a user pause during it stays after it |
| ② | **the HTTP surface** (design §3): `GET /commanders` · `GET /commanders/{id}/briefing` · `GET /commanders/{id}/feed?after=<seq>&wait=<s>` (long-poll: at once if there are items, else up to `wait`) · `GET /commanders/{id}/stream` (server-sent events) · `POST /commanders/{id}/act {seq, actions:[…]}` · `POST /commanders/{id}/mind {kind, mode}` · `GET /commanders/{id}/tools` — the actions as a JSON-schema tool list (L-3's Ollama mind uses the same list) | each route's rail; `/help` lists them; the briefing is byte-for-byte what a mind is given |
| ③ | **`ExternalMind`**: a wake publishes the briefing and waits for `POST /act` (bounded, as ①); actions go through the SAME validation as any mind's; the refusal goes back as an `order-state` item | an illegal posted action is refused with its reason and changes nothing (your L-1 rail, through HTTP) |
| ④ | **the model log beside the recording** (R-4298): every wake's briefing (or its new part), the mind's raw answer and the actions accepted / refused, as a file next to the run's recording | the file exists after a run and names each order the recording holds |
| ⑤ | **the scenario gives the commander a fight** (your L-1 finding): move Blue's start or add cover on the approach so Alpha is not shot down within 20 m — the checks stay as they are | `llm-platoon-attack` passes with the scripted mind AND the squads are in contact for at least 60 s (a new check row) |
| ⑥ | **CE-4677** — `GET /sitrep` stops reading a contact's `alive` from the world (drop it, or derive it from the side's own knowledge) | a rail: a contact killed out of every own unit's sight still reads as the side last knew it |

## ⭐ The proof run — the Haiku worker
Your session spawns a **Claude Code subagent on the haiku model** as the Blue platoon commander. Its whole brief: the scenario's `GET
/commanders/{id}/briefing` and `/tools`, then a loop `GET …/feed?after=&wait=30` → decide → `POST …/act`, until the scenario ends. ⛔ It gets no
other route (no `/sitrep`, no `/entities`) — it sees only what the commander sees.

Done when:
1. `llm-platoon-attack` with `"Mind": "External"` and `"Mode": "ThinkPause"`, played by the Haiku worker, **passes its checks** (or fails a
   TACTICAL row — say which and why; a plumbing failure is a defect).
2. **No illegal action is accepted** in the run (the log of ④ shows every refusal and its reason).
3. **The run replays** from its recording to the same end (the orders are recorded like any other).
4. The report quotes **5–10 lines of the radio log and the worker's answers**, so the user can judge how the commander reasoned.

## ⛔ Not in L-2
Ollama (L-3), the continuous wake scheduler and stale-action refusal, several seats on one model (L-4), fire support.

## Gates (CLAUDE.md rule 8)
Your L-1 rails + the new ones (each red-proved) + the routes' rails + `ScenarioChecksTests` + the scenario with the scripted mind, and the Haiku run
above; the pause path is cross-cutting (the clock) ⇒ name and run the paused / stepped execution suite that covers it (row 8). Fold any deviation
into the design (obligation ⑤). Report the ids you allocated.
