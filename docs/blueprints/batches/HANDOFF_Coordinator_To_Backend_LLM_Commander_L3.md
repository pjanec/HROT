<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-23
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design: §3 the stream and the context keeper, §4 the minds, §5a / §5a-2 / §5b your as-built, §6 the laptop, §11 L-3
  - STATUS_Backend_LLM_Commander.md — your L-1 / L-2 / L-2b report; continue it
  - NOTE_Backend_To_Behaviors_Squad_Attack_Stalls.md — the squad-attack stall (CE-4682, behaviors) that limits every mind on llm-platoon-attack
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `backend`: THE LLM COMMANDER, slice L-3 on the CLOUD VM (CE-4681)

**For:** backend (ids `CE-3000`…, `R-3000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there;
a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-23`: *"write the L-3 handoff for backend, running on cloud VM (not my physical machine) - the run on my machine would be a next
phase once we test whatever we could without it, using Haiku"*.

## ⭐ What this slice is
The **language-model mind** — HROT talking to an Ollama-style chat server itself — built and proved **on the cloud VM**: no GPU, no user laptop.
The real model on the user's RTX 3070 is the NEXT phase (L-3b), started from what this slice leaves ready. ⭐ The stand-in for the real model is
**Haiku**, answering the EXACT chat requests HROT sends.

## Priority
Next, after the row in progress. Continue `STATUS_Backend_LLM_Commander.md` (a new §, L-3); decisions to `DECISIONS_Backend.md`.

## What L-3 builds

| # | builds | done when (rail, red-proved) |
|---|---|---|
| ① | **`ChatModelMind`** (the design's OllamaMind): on each wake, ONE chat request to a configured endpoint — Ollama's `POST /api/chat` (`messages`, `tools` = your `/tools` list, `stream: false`, `options.num_ctx`, `keep_alive`) — and the answer's `tool_calls` become actions through the SAME validation as every mind. Off the frame thread (your `MindRunner`); bounded by the think-pause limit | a rail against the stand-in server (②): the request carries the briefing and the tools; a tool call becomes an accepted order |
| ② | **a stand-in chat server** for rails: answers from a script (a good tool call · a malformed one · an unknown tool · a refused order · plain text with no tool call · a timeout · a server down) | each case is a rail: a malformed / unknown call is retried ONCE with the error as a tool message, then dropped; no answer ⇒ "no orders", the run goes on, nothing hangs |
| ③ | **the conversation shape** the design §3 names: a STABLE prefix first (system message: role, mission, places, plays, actions) and the changing part after (snapshot, radio log, notes, the new items) — byte-identical prefix between calls so a server can reuse its cache | a rail: two consecutive requests share the same first message byte for byte |
| ④ | **checks per mind**: the `llm-platoon-attack` rows that only the scripted mind can pass (*"both squads to the objective at the start"*, *"the platoon's own play never runs"* — your L-2b P-2 finding) apply to the scripted run only | the scripted run still 8/8; a model run is not failed by those rows |
| ⑤ | ⭐ **the Haiku relay — the proof without a GPU**: the stand-in server's third mode hands each chat request (the JSON as HROT sent it: `messages` + `tools`) to a Claude Code subagent on the **haiku** model, which answers in Ollama's response format (`message.content` + `message.tool_calls`). ⛔ The subagent gets ONLY the request JSON — no routes, no files | see "The proof" below |
| ⑥ | **ready for the laptop (L-3b), not run**: a runbook section + script — install Ollama, pull `qwen2.5:7b-instruct`, `llama3.1:8b`, `qwen3:8b` (4-bit), the settings (`num_ctx` 8–16 k, `keep_alive`), the editor headless or on the 2-D map, the run of `llm-platoon-attack` per model, and a per-model summary (orders, refusals, answer time, checks passed) | the script runs end to end against the stand-in server on the VM (`--endpoint http://localhost:<port>`) |
| ⑦ | *optional, cheap only:* if the VM's network lets Ollama install and pull a SMALL model (≤ 1.5 B, CPU), one run with it — ⛔ if the install or the pull is blocked, say so and skip; never widen the environment for it | a run summary, or "blocked: <host>" |

## ⭐ The proof (on the VM)
1. `llm-platoon-attack` with `"Mind": "ChatModel"` (endpoint = the Haiku relay), `"Mode": "ThinkPause"`, plays to the end.
2. **Every request and answer is in the model log** beside the run; the report quotes 5–10 exchanges (the radio lines in, the tool calls out).
3. Count and report: wakes, tool calls, accepted / refused / malformed / retried, the briefing's size in characters and estimated tokens per
   call (an 8 B model's context is the next phase's limit).
4. Compare with the L-2b Haiku run (the routes, ExternalMind): did the chat format and the tool schema change what it did? ⭐ The difference is
   the finding — it tells the next phase what a small model will struggle with.
5. ⚠ The house may still not fall — the squads' attack stall is behaviors' (CE-4682). Report the tactical rows, but a stall-caused failure is not
   L-3's defect.

## ⛔ Not in L-3
The real model on the user's laptop (L-3b — the next phase, after this one), continuous mode and several seats (L-4), the replay memory defect
(CE-3493 — stays in your queue).

## Gates (CLAUDE.md rule 8)
Your L-1 / L-2 / L-2b rails + the new ones (each red-proved) + `DebugApiCommandersTests` + `ScenarioChecksTests` + the scenario with the
scripted mind (8/8) and the relay run. Fold the as-built into the design (§5a-3) and its §6 laptop section (obligation ⑤). Report the ids you
allocated.
