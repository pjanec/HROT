<!--STATUS
state: LIVE · updated: 2026-11-22 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md — the dispatch (CE-4676, L-1)
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — the design (§5 diagrams, §9 gaps, §11 L-1)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works
-->
# STATUS — backend, the LLM commander (L-1, CE-4676)

## 1. Now
IN PROGRESS: CE-4676 — investigation (the command chain's `CommandOrders`, `CommandPicture`, `SquadReportLog`, the scenarios `cc-platoon-bound` / `sq-defend-house`, named points). Next: the build in the handoff's order ① → ⑧.

Started at `251e7275f` (`backend` fast-forwarded to `origin/coordinator`). Self-wake-up: `backend self-wake-up`, hourly, into this session (created `2026-11-22`).

## 2. The queue (in order)
| # | item | state | sha | rail | scenario |
|---|---|---|---|---|---|
| ① | `CommanderSeat` (scenario field; InertSop, no task) | TODO | | | |
| ② | `CommanderFeedSystem` | TODO | | | |
| ③ | `CommanderChannel` | TODO | | | |
| ④ | `MindRunner` + `IMind` + `ScriptedMind` | TODO | | | |
| ⑤ | `CommanderActionSystem` → `CommandOrders.Order` (Superior) | TODO | | | |
| ⑥ | `Gazetteer` | TODO | | | |
| ⑦ | the routine SITREP at start | TODO | | | |
| ⑧ | the scenario `llm-platoon-attack` | TODO | | | |

## 3. Taken from new handoffs
- `HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md` — taken `2026-11-22` (user: after the linked-client batch, which is DONE — `STATUS_Backend_Linked_Client.md`).

## 4. Notes sent / received
(none yet)

## 5. Gates (rule 8)
(filled per item)
