<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-22
current-answer: the whole file
related-designs:
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — ⭐ THE design (rev 2, leans approved R-4298): §1 the test, §2 the module view, §3 the stream, §4 the minds, §5 the class + sequence diagrams, §9 the gaps, §11 L-1
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md — CommandPicture, the reports (§6), CommandOrders (§7a CC-9)
  - ../../DESIGN_Entity_Control.md — the AI-player seam (C-9 / C-10) the stream later serves over HTTP
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `behaviors`: THE LLM COMMANDER, slice L-1 (CE-4676)

**For:** behaviors (ids `CE-2000`…, `R-2000`…). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN there — later
documents are FYI; one that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-22`: *"start with platoon commander commanding squads on a little scenario … one side … full game AI and other side by large
language model"* · *"build the commander bridge into the HROT"* · *"I need to develop the way how the HROT units can be controlled via LLMs"* ·
*"leans approved, write handoff for L-1"* (R-4298).

## ⭐ Priority
After the row in progress, before your remaining second-tier rows. Status in `STATUS_Behaviors_LLM_Commander.md` (protocol §3), decisions you
take to `DECISIONS_Behaviors.md`.

## Read first
The design END TO END ([DESIGN_LLM_Commander](../../designs/llm-commander/DESIGN_LLM_Commander.md)) — ⭐ build §5's class diagram and sequence; a
deviation is folded back into the design (obligation ⑤). Then Command Chain Example §6 (reports) and §7a (`CommandOrders`).

## What L-1 builds — NO model yet: the plumbing, proven with a scripted mind

| # | builds | design |
|---|---|---|
| ① | **`CommanderSeat`** on a commander: which mind, which mode; set by a scenario field on the commander (R-4298), saved with the scenario. ⭐ While the seat exists the commander's OWN play does not run — its orders come only from the mind | §5, §9 ① |
| ② | **`CommanderFeedSystem`** (serial; on a report, an event or the heartbeat — never every frame): feed items `snapshot` · `report` · `order-state` · `event` · `heartbeat`, each with a seq, a sim time, a JSON form and ONE radio line | §3 |
| ③ | **`CommanderChannel`**: the feed ring (seq) out, the action queue in; thread-safe; ⛔ nothing on the mind's side reads or writes the world | §2, §5 |
| ④ | **`MindRunner` + `IMind` + `ScriptedMind`**: one runner per seat on a background thread; wake on a wake item, else the heartbeat; one call at a time. The scripted mind: fixed rules (*"on a SPOT: the nearest squad attacks the spot, the other supports by fire / holds"*) | §3, §4 |
| ⑤ | **`CommanderActionSystem`** (serial pre-pass): drain the queue, validate (the squad is his and alive, the order is one his squads fulfil, `where` resolves), then **`CommandOrders.Order`** at origin Superior — ⛔ no second order path; a refused action goes back as an `order-state` item with the reason | §3, §7 |
| ⑥ | **`Gazetteer`**: the scenario's named Point map graphics (TKB 8816) and tactical areas + a coarse grid; `where` = a name, or "N m <dir> of <name>", ↔ a position; the radio lines use it | §3, §9 ⑤ |
| ⑦ | the seat asks each squad a routine SITREP at start (`CommandOrders.AskSitrep`) | §9 ④ |
| ⑧ | **the scenario `llm-platoon-attack`** on `test-town`: Blue = a body-less platoon commander (TKB 305) with a seat (ScriptedMind) over 2–3 squads; Red = a squad defending a house under the full game AI (on the pattern of `sq-defend-house`); named points for the house, the start and a support position | §1 |

⭐ **The actions L-1 accepts:** `order(squad, kind, where|target)` with kind ∈ Move · Hold · Attack · Defend · BreakContact (+ the squad plays
`BoundingOverwatch`, `SuppressAndManeuver`, `ClearBuilding` if they resolve for a squad) · `hold(squad)` · `ask_sitrep(squad)` · `note(text)`.

⚠ **To measure before building ①** (claim table in your status file): what a body-less platoon commander does today with no task — does its SOP
react (e.g. to `SquadContact`) and give orders? The seat must stop exactly that, and nothing the squads do themselves.

## Done when — the scenario's checks (R-4277; `scripts/scenario-regression.sh llm-platoon-attack`)

1. After Red is first SPOTted, the scripted mind's orders reach the squads within 1 s of sim time.
2. Every order a squad receives in the run comes from the platoon commander at origin **Superior** — none from the commander's own play.
3. The squads fight with their own AI (they take cover, fire, report): each sends at least one SPOT or SITREP to the commander.
4. The fight ends: Red is beaten or the platoon breaks contact, before the scenario's time limit.

**Rails** (each red-proved by a compiling mutation): the feed emits one `report` item per report addressed to the commander, with its radio line ·
an illegal action (another side's unit, a dead squad, an unknown place) is refused with a reason and changes nothing · the mind's thread touches
no world state (the world guard, `HROT_WORLD_GUARD=1`) · 0 B per steady frame with a seat and no new reports
(`GC.GetAllocatedBytesForCurrentThread`).

## ⛔ Not in L-1
The HTTP routes (`/commanders/...`), the ExternalMind / Haiku worker, Ollama, continuous-mode stale refusal — L-2…L-4 (CE-4678). Fire support.
⇒ keep the channel and `IMind` shaped so L-2 adds the ExternalMind and the routes without touching ②–⑤.

## Gates (CLAUDE.md rule 8)
Your new rails + the command-chain suites that cover `CommandOrders` and the staff's reports (name them) + the parallel env on the two new systems
(`HROT_PARALLEL_SIM=1` `HROT_READ_CHECK=1`) + the scenario. Report the ids you allocated and the design sections you updated.
