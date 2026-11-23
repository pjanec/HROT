<!--STATUS
state: LIVE · updated: 2026-11-22 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md — the dispatch (CE-4676, L-1)
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — the design (§5 diagrams, §9 gaps, §11 L-1)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works
-->
# STATUS — backend, the LLM commander (L-1 CE-4676 · L-2 CE-4679)

## 1. Now
⭐ `2026-11-23 16:10` **THE DUEL (user: "can we let 2 haiku workers play against each other?" · "yes please") — DONE**: `llm-platoon-duel`, two Haiku commanders one per side through the per-commander relay; six seat defects found and fixed (D-1…D-6, `4fddd69eb`), one grammar defect filed (CE-3502). Report: §Duel; design §5a-5. ⏸ then idle — waiting for a handoff.
⭐ `2026-11-23` **L-3a (CE-4683, the briefing a small model can live with) — DONE**: ① ② ③ ④ built, railed and red-proved; the Haiku relay run side by side with L-3 below; CE-3501 CLOSED. Report: §L-3a; design §5a-4.
⭐ `2026-11-23` **L-3 (CE-4681, the chat-model mind on the cloud VM) — DONE**: ① ② ③ ④ ⑤ ⑥ built and proved; ⑦ blocked (permission — see §L-3). Report: §L-3 below; design §5a-3 / §6a.
⛔ ~~`2026-11-23 09:28` — idle~~ (L-3 taken 10:30) (self-wake-up: queue empty, no new HANDOFF/STEER/NOTE for backend; CE-3494 §10 (R-3119) built and gated, `STATUS_Backend_Queue.md`).
⭐ L-2 (CE-4679): ① ② ③ ④ ⑥ built and railed (design §5a-2, decisions in DECISIONS_Backend CE-4679); ⑤ the scenario DONE (8/8, repeatable); ★ done (6/8, the tactical stall explained; the replay half blocked by CE-3493). ✅ L-2b (CE-4680) DONE: B-1 … B-6, proof 1 (scripted run_play, 8/8), proof 2 (Haiku). NOW: CE-3445 T2 resumed (STATUS_Backend_Queue.md) — the instrumented pair runs.
L-1 (below): DONE: CE-4676 L-1 — every item ① … ⑧ built; the scenario `llm-platoon-attack` passes 7/7 (the confirming run, 331 s). ⭐ NEXT (protocol §7): no new handoff for backend (loop run `2026-11-22`); the remaining second-tier row is
`STATUS_Backend_Queue.md` → **CE-3445 T2** (rare mode-3 stepped divergence) — a resume continues there. ⚠ `backend-wip-llm-commander` is stale
(its content is all on `backend`; the remote refused the delete).

Started at `251e7275f` (`backend` fast-forwarded to `origin/coordinator`). Self-wake-up: `backend self-wake-up`, hourly, into this session (created `2026-11-22`).

## 2. The queue (in order)
| # | item | state | sha | rail | scenario |
|---|---|---|---|---|---|
| ① | `CommanderSeat` (component 217, scenario field; InertSop, no task) | DONE | `9faccd1be` | (the scenario's "own play never runs") | ✅ |
| ② | `CommanderFeedSystem` | DONE | `9faccd1be` | `CE4676_TheFeed_SaysEachReportIntoTheCommanderOnce_WithItsRadioLine` | ✅ |
| ③ | `CommanderChannel` | DONE | `9faccd1be` | (all four rails) | |
| ④ | `MindRunner` + `IMind` + `ScriptedMind` | DONE | `9faccd1be` | `CE4676_TheScriptedMind_SendsTheNearestSquadAtTheSpot_AtOriginSuperior_OffTheFrameThread` | ✅ |
| ⑤ | `CommanderActionSystem` → `CommandOrders` (Superior) | DONE | `9faccd1be` | `CE4676_AnIllegalAction_IsRefusedWithItsReason_AndChangesNothing` | |
| ⑥ | `Gazetteer` | DONE | `9faccd1be` | (the feed rail's "30 m N of house") | |
| ⑦ | the routine SITREP at start | DONE | `9faccd1be` | — (the scenario's "reports to the platoon") | ✅ |
| ⑧ | the scenario `llm-platoon-attack` + the `report` cue | DONE | (this commit) | `ScenarioChecksTests.CE4676_TheReportCue_*` | ✅ PASS (311 s) |
| — | 0 B per steady frame | DONE | `9faccd1be` | `CE4676_ASteadyFrame_WithASeatAndNoNewReport_AllocatesNothing` | |

## 3. Taken from new handoffs
- `2026-11-22` — `HANDOFF_Coordinator_To_Backend_LLM_Commander_L2b.md` (CE-4680, L-2b: run_play / stop_play, the brief, objectives, squad tasks, enemy detail, place notes) — QUEUED after L-2; B-1's premise is measured first.
- `HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md` — taken `2026-11-22` (user: after the linked-client batch, which is DONE — `STATUS_Backend_Linked_Client.md`).

## 4. Notes sent / received
(none yet)

## 5. Gates (rule 8)
| gate | command | result |
|---|---|---|
| the seat rails | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --filter "FullyQualifiedName~CE4676"` | 4 / 4 |
| the same under the parallel env + world guard | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` + the line above | 4 / 4 |
| the CGF pack | `… --filter "FullyQualifiedName~CgfLogicPackTests|FullyQualifiedName~CE4676"` | 22 / 22 (counts +2, +1 uncounted by CE-2468 UF-2 `d081a9ff2` — backend's own) |
| the checks grammar (the report cue) | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj --no-build --filter "FullyQualifiedName~ScenarioChecksTests"` | 28 / 28 |
| ⭐ the scenario (R-4277) | `scripts/scenario-regression.sh llm-platoon-attack` | ✅ PASS — 6/6 (run 2); run 1 FAILED the three report rows: the cue read the UI's mirror world (no report log) — fixed to read through `GET /squad/reports` |
| the command chain (CommandOrders, the staff's reports) | `… --filter "FullyQualifiedName~SquadManeuverTests|FullyQualifiedName~CgfLogicPackTests"` | 395 / 398 before the count fix — the 3 reds were the pack counts, fixed |

## 6. What the run showed (the radio log, `[Commander]` lines)
`00:00` SNAPSHOT → ORDER Move both squads → objective · `00:03` both SITREPs (the routine ask) · `00:03` ALPHA: spot, 3 enemy, house →
ORDER Attack ALPHA → house, SupportByFire BRAVO → support (same second) · `00:18` ALPHA: out of the fight → ORDER BreakContact BRAVO → start.
⚠ Found: Red's sheds see Blue's start (≈150 m, open): Alpha is shot down before it moves 20 m. The scenario is honest about it (the 7th row);
a placement with cover on the approach would give a longer fight — a scenario tweak, not a seat defect. ⚠ The repeated SPOT lines every 15 s are
the squads' standing reporting, not the feed repeating (each is a new report in the history).

## 7. Ids allocated
None new — CE-4676 is the dispatched row; component id 217 (`GlobalComponentIds.CommanderSeat`).

## 8. Red-proofs — 5 / 5 red (each a compiling mutation)
K1 the history head never kept ⇒ the feed rail · K2 an unknown place resolves ⇒ the illegal-action rail · K3 the farthest squad attacks ⇒ the
scripted-mind rail · K4b a heartbeat item every frame ⇒ the 0 B rail (⚠ K4 "the seat query built every frame" SURVIVED — the repository caches the
query, so it allocates nothing: not a defect, the wrong mutation) · K5 the report cue ignores the addressee ⇒ `ScenarioChecksTests.CE4676_*`.

---

# L-2 (CE-4679) — `HANDOFF_Coordinator_To_Backend_LLM_Commander_L2.md`, taken `2026-11-22`

Started at `9481914b6` (merged `coordinator`). ⚠ **CE-3445 T2 is PARKED behind L-2** at a clean point (shape ④ ruled out and pushed, `f533596fd`;
its next step — the instrumented 1- vs 4-thread scale-rig pair — is hours of Release runs and is written in its tracker row). It resumes after L-2.

| # | item | state | sha | rail |
|---|---|---|---|---|
| ① | think-pause pauses the SIM CLOCK (one pause path, a hold that never un-pauses the user's pause, bounded) | DONE | (this commit) | `CE4679_ASlowMind_HoldsTheSimClock_WhileFramesRunOn_AndItsAnswerResumesIt` · `CE4679_AUserPauseDuringTheHold_OutlivesIt` (3 mutations red) |
| ② | the HTTP surface `/commanders/…` (+ `/tools`) | DONE | (this commit) | `DebugApiCommandersTests.CE4679_TheCommanderRoutes_*` · `CE4679_TheStream_*` · `EveryRoute*` 6/6 |
| ③ | `ExternalMind` | DONE | (this commit) | the illegal action refused over HTTP (in `CE4679_TheCommanderRoutes_*`) |
| ④ | the model log beside the recording | DONE | (this commit) | `CE4679_TheModelLog_SitsBesideTheRecording_AndNamesEachOrderAndVerdict` |
| ⑤ | `llm-platoon-attack` gives the commander a fight (≥ 60 s in contact) | DONE — Blue starts behind the L-Block, objective / support behind the Low Wall, six 1.5 m walls on the lane; contact 76 s → past 420 s; the two "≥ 60 s" rows added; ⚠ the L-1 break-contact row WITHDRAWN (DECISIONS CE-4679 #8) | (this commit) | ✅ PASS 8/8 (two runs, the same times: Red fires 76.1 s, attack 78.25 s, contact rows 141.9 s) |
| ⑥ | CE-4677 `/sitrep` stops reading a contact's `alive` from the world | DONE | (this commit) | `DebugApiOrdersTests.CE4677_AContactKilledOutOfSight_StillReadsAsTheSideLastKnewIt` (+ DebugApiOrdersTests 18/18) |
| ★ | the proof run: a Haiku subagent plays Blue's commander through the routes only | ⚠ DONE but for the replay half — see §L-2 ★ below | `a608e6926` `33b68a6ad` | `CE4679_AStepperClock_*`, `CE4679_RoutineSitreps_*` (the two defects it found) |

# L-2b (CE-4680) — `HANDOFF_Coordinator_To_Backend_LLM_Commander_L2b.md` — QUEUED after L-2

## B-1 premise — measured `2026-11-22` (before any code; graph `search_code PlatoonAttack` (60 hits), `search_graph SubordinateOrders` Class + `Release`, `trace_path Release` inbound, + grep)

| the premise | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| a play reaches a platoon as its TASK through the order path its superior uses | ✅ the company: `CommandOrders.Route(world, company, platoon, kind, neutral, "PlatoonAttack", json)` → an `AssignTacticalIntentEvent { Entity = platoon, IntentId = play, Origin = Superior }` (`CompanyBehaviors.cs:450`, `CommandNodes.cs:135`) | ✅ `DESIGN_Orders_Params_Entity_Classes.md` §8/§9 (receiver-side resolution) |
| an order KIND resolves to the platoon's play | ✅ `ResolutionRouting.cs:101–104`: InfantryPlatoon Attack→PlatoonAttack, Hold→PlatoonHold, Bound→PlatoonBound, SupportByFire→PlatoonSupportByFire; Defend→PlatoonDefend (:99) | ✅ same; the routing table is the ONE resolver |
| ending the play releases its squad orders | ✅ `SubordinateOrders.Release` (`SubordinateOrders.cs:84`) — called by `BehaviorOwnedParts.Release` when the run ends (finished, cleared, replaced, re-assigned; NOT paused): a Clear naming the ordered behaviour, per member | ✅ `DESIGN_Orders_Params_Entity_Classes.md` §7 (the class doc says so) |
| the seat has no superior to "give" the play | ⚠ `CommandOrders.Route/Assign` RECORD the order on the GIVER (`SubordinateOrders.Record(world, commander, …)`) — the seat's play has no giver entity ⇒ publish the `AssignTacticalIntentEvent` itself at origin Superior (what Route publishes, minus the giver's record) | ⛔ searched `docs/` — no record of a giver-less Superior task; L-2b's own design §5b says "the commander's own task" |
| ⚠ a direct squad order in the SAME frame as the clear | ⚠ `CommandOrders.Order` records under the commander's CURRENT run (`BehaviorOwnedParts.OwnerOf`) ⇒ an order given before the clear is processed belongs to the play's run and is RELEASED with it. ⇒ the clear first, the direct order on a later frame (once the run is gone) | — (a sequencing fact, to rail) |

⇒ ⭐ the premise HOLDS; B-1 is buildable. The giver-less publish and the clear-then-order sequencing are the two things its rails must pin.

## L-2 ★ — the proof run, as it went (`2026-11-22`)

Haiku-model subagent, routes ONLY (`/briefing`, `/tools`, `/feed?after&wait=30`, `/act`); `llm-platoon-attack` copied with `"Mind": "External", "Mode": "ThinkPause"`
(scratch, never committed). Three runs:

| run | what happened | outcome |
|---|---|---|
| 1 | ⛔ asked ONCE (seq 1), never again: routine SITREPs kept the 30 s heartbeat from firing; also the verify run's STEPPED clock ran on while it thought | two defects → fixed + railed (`a608e6926` the stepped clock, `33b68a6ad` the heartbeat) |
| 2 | 28 questions, 8 orders, **0 refused**; clock held at each question | the replay check ran out of memory (CE-3493) |
| 3 | plain `--verify` (no recording): 19 questions, 8 orders, **0 refused** | **6/8 rows PASS** — every plumbing row, incl. *"a squad attacks within 1 s of the first SPOT"* (81.75 s: the Haiku commander's own Attack); the two TACTICAL rows (contact ≥ 60 s) **FAIL**, explained below |

⭐ **Why the tactical rows failed (run 3):** the commander sent Bravo to support by fire from the start and Alpha forward; at the SPOT it ordered Alpha
to attack, then Bravo too. ⛔ Bravo's `Attack` from the start **did not move it** (it re-tasked Bravo with a `Move` a minute later), and Alpha went
**Set** at the objective behind the wall and stayed — the fight stalled after ~2:20, so no Red shot fell 60 s after the first. The same two stalls
show in the scripted runs ⇒ the squads' attack play, not the commander: NOTE to behaviors `NOTE_Backend_To_Behaviors_Squad_Attack_Stalls.md`.

⛔ **Not shown: "the run replays to the same end".** `--verify-replay` dies of memory on this 420 s scenario — with the seat REMOVED too (2.5 → 8.9 GB
during the live run) ⇒ CE-3493, not the seat. ⚠ The model log is written beside the recording; the unrecorded run 3 had none (rail
`CE4679_TheModelLog_*` proves the file).

The radio log and the answers (run 3, verbatim):
- `00:00:00 NOTE: Opening: Red defends house. Alpha moves to support (cover), Bravo supports by fire from start. Need to learn Red strength.`
- `00:00:00 ORDER Move Alpha Squad → support: given` · `00:00:00 ORDER SupportByFire Bravo Squad → house: given`
- `00:01:21 ALPHA SQUAD: spot, 1 enemy (Individual), house`
- `00:01:21 NOTE: Alpha spotted 1 enemy (Individual) at house. Attack house with Alpha; Bravo continues SupportByFire on house.` · `00:01:21 ORDER Attack Alpha Squad → house: given`
- `00:01:35 ALPHA SQUAD: spot, 2 enemy (Team), house` · `00:01:35 NOTE: Red now 2 (Team) at house; Alpha Amber. Mass force: Bravo attacks house too, Alpha keeps pressing.`
- `00:02:00 ALPHA SQUAD: progress, Set, at objective`
- `00:02:30 NOTE: Bravo has not moved from start under Attack order. Re-task Bravo: Move to objective to join Alpha.`
- `00:04:30 NOTE: 4:30 of ~7 min. Alpha Amber at objective, Bravo Green 60 m E of objective. Commit both: Attack house together.`
- `00:04:33 BRAVO SQUAD: progress, Set, at 20 m E of objective`

## L-2b items
| # | item | state | sha | rail |
|---|---|---|---|---|
| B-1 | `run_play` / `stop_play` — the plays as the commander's task; a direct order ends the play first and lands after its release | DONE | (this commit) | `CE4680_RunPlay_ThePlaysChoreographyRuns_ADirectOrderEndsItFirst_AndLands` (3 mutations red); the plays' own suites 38/38 (`PlatoonAttack/Bound/Defend/Hold`, `CE2436_*` — behaviors' code, run not changed) |
| B-2 | the mission brief `CommanderBrief` (unmanaged, NoReplay, component 320) in the prefix — ⚠ first built managed and never loaded; fixed | DONE | (this commit) | `CE4680_TheBriefing_*` |
| B-3 | the objectives line (`ObjectiveStatus` by `VisibleSides`) | DONE | (this commit) | `CE4680_TheBriefing_*` — a Red-only objective never listed (red-proved) |
| B-4 | each squad's task and progress in the snapshot | DONE — the task from `SubordinateOrders`, else the seat's own last order (📌 a commander with no running task — run 0 — records none) | (this commit) | `CE4680_TheBriefing_*` (red-proved) |
| B-5 | the enemy as reported (size, activity, equipment, age, heard) | DONE but "heard": ⛔ `CommandReport` has no seen / heard bit (`Radius` is spread OR uncertainty) — ask in `NOTE_Backend_To_Behaviors_Squad_Attack_Stalls.md` §2 | (this commit) | `CE4680_TheBriefing_*` |
| B-6 | place notes (`PlaceNote`, unmanaged, NoReplay, component 321) in the prefix | DONE | (this commit) | `CE4680_TheBriefing_*` |
| P-1 | proof 1: `llm-platoon-attack` with the scripted mind on `run_play(PlatoonAttack, house)` | ✅ PASS 8/8, two runs, the same times — PlatoonAttack at 78.25 s, Bravo supports (TargetsDown ~3:00), Alpha closes by fire and movement, every Red down, one Bravo man lost; ⚠ the brief and notes did NOT load then (managed, no translator — found by the Haiku run); fixed (unmanaged), the briefing now carries them | (this commit) | the scenario |
| P-2 | proof 2: the Haiku worker plays it again with the brief, objectives and plays | ✅ DONE — see §L-2b P-2 below: it used `run_play` AND direct orders, every note cites the mission; the house not taken (the squad-attack stall) | | |

## L-2b P-2 — the Haiku run with the mission (`2026-11-22`, routes only, the same four)

⚠ The first L-2b Haiku run found that the brief and notes never LOADED (managed records, no translator) — its briefing had no mission, so it held
all run. Fixed (`23c79a168`, unmanaged + a load/save rail). The run below is on the fixed build: prefix **415 → 1156 chars** (the mission, four
place notes, the plays in the action line).

| | |
|---|---|
| questions | 32 answered, 2 acts refused (sent with no question waiting — 409, nothing changed) |
| tools | ⭐ BOTH: `run_play PlatoonAttack → house` at 0:00 (its opening plan), `run_play PlatoonBound → house` at 1:43, `run_play PlatoonAttack` again at 5:51; direct orders (SupportByFire / Move / Attack) in between — ⭐ the "play ENDED — the order lands once its orders are released" path ran live twice |
| mission | ⭐ cited in every note, e.g. `00:00:00 NOTE: Mission: seize the house (sheds) from Red by 00:07, keep both squads in the fight, no crossing of open ground east of house without fire on it. Plan: PlatoonAttack on house - support by fire from the support position …` |
| outcome | the house NOT taken: Bravo put fire on it (TargetsDown 1:13), Alpha never assaulted — under the plays or under direct Attack (`00:04:48 NOTE: … Alpha reports Set at support and the Attack order has not moved it`) — the squad-attack stall already with behaviors (`NOTE_Backend_To_Behaviors_Squad_Attack_Stalls.md`) |
| rows | PASS: PlatoonAttack within 1 s of the SPOT (37 s), reports, Red fires, the fight lasts a minute. FAIL, explained: "both squads to the objective at the start" and "the platoon's own play never runs" are the SCRIPTED mind's rows (the LLM planned otherwise, and its own `PlatoonBound` reads as "own play" to the task cue); "every Red down" — the stall |

Quotes (verbatim):
- `00:00:36 BRAVO SQUAD: spot, 1 enemy (Individual), house`
- `00:01:13 BRAVO SQUAD: progress, TargetsDown, at 30 m N of objective`
- `00:01:43 NOTE: 103s: PlatoonAttack has gone quiet; squads await orders. Alpha stuck 20 m SW of start, Bravo Amber at 60 m NE of objective with TargetsDown. …`
- `00:02:17 PLAY PlatoonBound ENDED — a direct order to Bravo Squad; it lands once the play's orders are released`
- `00:04:15 NOTE: 255s: Alpha Set at support … Now the assault: Bravo confirms SupportByFire from the objective, Alpha attacks the house …`
- `00:06:34 NOTE: 394s: 26 s left to the 00:07 limit. Alpha has held at support throughout; the house is not taken.`

## L-3 — the chat-model mind on the cloud VM (CE-4681, `2026-11-23`)

Taken at `6ec10c16d` (merge of `origin/coordinator`, the handoff `HANDOFF_Coordinator_To_Backend_LLM_Commander_L3.md`).

| # | item | state | sha | rail / proof |
|---|---|---|---|---|
| ① | `ChatModelMind` — one `POST /api/chat` per wake (model, messages, tools, `stream:false`, `options.num_ctx`, `keep_alive`), the tool calls through `CommanderTools.Parse`; `CommanderMind.Ollama` → `ChatModel` (Roslyn) | DONE | `9b0ac9d6d` | `CE4681_TheChatMind_SendsTheBriefingAndTheTools_AndAToolCallBecomesAnOrder` |
| ② | the stand-in chat server (rails, in process) — a good call · malformed · unknown · refused · plain text · timeout · down | DONE | `9b0ac9d6d` | `CE4681_AMalformedOrUnknownCall_IsRetriedOnce_ThenDropped` · `_PlainText_IsNoOrders` · `_ATimeoutOrAServerDown_IsNoOrders_AndNothingHangs` · `_ARefusedOrder_ComesBackAsARefusalWake` |
| ③ | the conversation: system = prefix (byte-stable), user = what changed | DONE | `9b0ac9d6d` | `CE4681_TwoCalls_ShareTheSystemMessageByteForByte` — and in the relay run: ONE system message across all 48 calls |
| ④ | checks per mind — anchor `scriptedMind` (held 1 s) + `"if"` on the two scripted-only rows | DONE | `9b0ac9d6d`, `d819ad979` | scripted run PASS 8/8 (both rows judged); relay run: both SKIPPED. 📌 first form failed: the anchor sampled the scenario's mind at t=0, before the run's override — found by the laptop dry run |
| ⑤ | the Haiku relay (`scripts/llm/chat_standin.py --mode relay`) | DONE | `9b0ac9d6d` | §L-3 ⑤ below |
| ⑥ | the laptop recipe (`scripts/llm/laptop_run.py`, design §6a) — READY, NOT RUN on a laptop | DONE | `9b0ac9d6d`, `d819ad979` | end to end on the VM against `chat_standin.py --mode script`: 18 calls, the summary 5/8 checks (the stand-in plays badly by design), request 4012 chars |
| ⑦ | a small model on the VM | ⛔ BLOCKED — not by the network (ollama.com, the GitHub release and the registry all answered; the binary and CPU libraries were extracted, 1.5 GB streamed): **starting the downloaded `ollama serve` and pulling a model was refused by the session's permission check** (auto mode). Skipped as the handoff says; the files removed | — | — |

### L-3 ⑤ — the Haiku relay run (`llm-platoon-attack`, `HROT_COMMANDER_MIND=ChatModel`, ThinkPause, endpoint = the relay)

A Haiku subagent saw ONLY each request file (`req-NNNN.json`, the JSON exactly as HROT sent it) and wrote Ollama's response shape back.

| measure | value |
|---|---|
| wakes (calls) | **48** (each answered; 0 timeouts) |
| tool calls | **65** — `note` 48 (one EVERY call), `order` 12 (`SupportByFire` 7, `Move` 5), `ask_sitrep` 3, `run_play` 1 (`PlatoonBound → house`, at 0:00), `hold` 1 |
| verdicts | **17 accepted, 0 refused** (notes take no verdict) |
| malformed / retried | **0 / 0** — Haiku always produced valid tool calls in Ollama's shape |
| answer time (wall, the relay round trip) | mean **6.0 s**, max **9.9 s** — the clock held each time (think-pause) |
| request size | system **1 317 chars** (stable across all 48) + tools **2 315 chars** + user **119 → 25 307 chars** ⇒ request **4 014 → 29 398 chars ≈ 1 000 → 7 350 tokens** |
| ⭐ what grows | at the last call the user message is **19 687 chars, 17 953 of them his NOTES** (48 notes kept for the run — §3), the radio log 1 448, the snapshot 257 ⇒ **CE-3501** |
| checks | PASS: both squads report · Red fires · the fight lasts a minute. SKIPPED (scripted-only): both squads to the objective · the own play never runs. FAIL: *"PlatoonAttack within 1 s of the first SPOT"* (it answered the SPOT with `SupportByFire`, not the play) · *"every Red down"* (Alpha lost on the open ground, the house held — and the squad-attack stall CE-4682 limits every mind) |

Exchanges (radio lines in → tool calls out, verbatim, truncated):
- `00:00:00 SNAPSHOT — Platoon: ALPHA SQUAD at start; BRAVO SQUAD at start;` → `run_play {PlatoonBound, house, support: support}` + `note "Bound on the house: one squad covers from support behind the Low Wall … Keep fire on the 100 m open ground east of the house before either squad crosses it."`
- `00:01:30 (no news — your squads await orders)` → `ask_sitrep Bravo Squad` + `note "Bravo is 40 m SW of start, which is not the direction of the house …"`
- `00:02:00 (no news)` → `order Alpha Move → support` · `order Bravo Move → objective` + `note "Bravo reports 70 m E of the objective … exposed and off route. Ended the bound with direct orders"`
- `00:02:08 BRAVO SQUAD: spot, 1 enemy (Individual), house` → `order Bravo SupportByFire → house`
- `00:02:23 BRAVO SQUAD: spot, 1 enemy (Individual), house` → `order Bravo SupportByFire → house` (again) · `ask_sitrep Alpha` + `note "The radio log shows no record of my SupportByFire order to Bravo … so I am re-issuing it"`
- `00:02:35 ALPHA SQUAD: spot, 1 enemy (Individual), 20 m SE of house` → `order Alpha SupportByFire → house`
- `00:02:59 BRAVO SQUAD: spot, 2 enemy (Team), house` → `order Alpha SupportByFire → house` + `note "… the defence is at least 2 Team plus Individuals"`
- `00:03:14 BRAVO SQUAD: spot, 2 enemy (Team), house` → `order Alpha Move → support` + `note "Alpha reports 60 m E of the objective … on the edge of the open ground … Ordered Alpha to move back"`
- the worker's own summary: a decision point at 00:04 — *"no assault, because fire superiority was not established and the defence grew from 1 Individual to 3 Team"*; Alpha caught in the open during the pull-back (Black), Bravo held at support (Amber).

⭐ **Compared with the L-2b Haiku run (routes, ExternalMind) — the difference is the finding:**

| | L-2b (routes) | L-3 (chat format, tools) |
|---|---|---|
| asked | 32 | 48 (the same heartbeat rule; more wakes this run) |
| the plays | `run_play` THREE times (PlatoonAttack at 0:00 and 5:51, PlatoonBound at 1:43) | ONCE (PlatoonBound at 0:00), then direct orders only — ⭐ with the tools listed as functions it reached for `order` far more than for the play |
| the SPOT | `run_play PlatoonAttack` within 1 s (the row PASSED) | `order SupportByFire` (the row FAILED) |
| re-issued orders | — | ⭐ 7 `SupportByFire` for 2 squads: it re-sent orders it could not find in the radio log — the `ORDER … given` line had moved out of the log at the next snapshot (snapshots clear the log, §3) |
| notes | in most answers | in EVERY answer — 18 KB by the end (CE-3501) |
| bad calls | 2 acts refused (sent with no question waiting) | 0 malformed — the chat shape removed that failure entirely |

⇒ **for L-3b (a 7–8 B model):** ① the notes must be bounded (CE-3501) or the context fills by ~5 min; ② the snapshot should carry each squad's CURRENT order, so a model does not re-issue it (B-4 shows the task — it was there as text, Haiku still re-issued: worth a sharper line); ③ the play/order balance moved toward direct orders once the tools are functions — a small model will need the prefix to say *"prefer run_play; a direct order ends the play"* more plainly.

### L-3 gates
| gate | command | result |
|---|---|---|
| the commander rails (L-1 · L-2 · L-2b · L-3) | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --filter "FullyQualifiedName~CE4681|FullyQualifiedName~CE4676|FullyQualifiedName~CE4679|FullyQualifiedName~CE4680"` | **17 / 17** |
| red-proofs (L-3) | compiling mutations, one per rail: calls not read · the time in the system message · no retry · plain text as malformed · no per-call limit | **5 / 5 red** (two first-try mutations did not compile — redone) |
| the routes | `… Hrot.ClusterRunner.Integration.Tests … --filter DebugApiCommandersTests` | **3 / 3** |
| the checks grammar | `… Hrot.Editor.Tests … --filter ScenarioChecksTests` | **29 / 29** |
| ⭐ the scenario, scripted mind | `scripts/scenario-regression.sh llm-platoon-attack` | **PASS 8/8** (333 s) |
| ⭐ the scenario, the Haiku relay | §L-3 ⑤ | 4/6 judged PASS, 2 SKIPPED, 2 FAIL explained |
| the laptop recipe, VM dry run | `laptop_run.py --endpoint http://localhost:11500 --models stand-in --no-pull` | end to end, summary written |

### L-3 ids allocated
`CE-3501` (the commander's notes grow without bound — 18 KB of a 20 KB user message after 48 wakes). Component ids: none. Rulings: none.

## L-3a — the briefing a small model can live with (CE-4683, `2026-11-23`)

Taken at `1bc0aa466` (merge of `origin/coordinator`, `HANDOFF_Coordinator_To_Backend_LLM_Commander_L3a.md`).

| # | item | state | sha | rail (red-proved) |
|---|---|---|---|---|
| ① | ONE running estimate: an answer's notes joined, capped at 800 chars, REPLACING the last; no longer echoed into the feed; failures / timeouts no longer notes | DONE | `fc41e0515` | `CE4683_TheEstimateIsOne_Replaced_Capped_AndALongRunStaysUnderTheBudget` (mutation: notes appended ⇒ red) |
| ② | `SeatRuntime.InForce` — ORDERS IN FORCE and PLAY IN FORCE in every snapshot; an identical order (same kind, point within 2 m) is a no-op answered *"already in force since mm:ss"*, nothing sent, a play not ended | DONE | `fc41e0515` | `CE4683_ARepeatedIdenticalOrder_IsANoOp_AndTheOrdersInForceSurviveASnapshot` (mutation: `Same` never ⇒ red) |
| ③ | the prefix: *"PREFER run_play … a direct order to a squad ENDS the play"*; `run_play` first in the tools | DONE | `fc41e0515` | `CE4683_ThePlaysArePreferred_InThePrefixAndTheTools_AndTheSnapshotNamesThePlayInForce` (mutation: the play line dropped ⇒ red) |
| ④ | the per-call budget: every model-log call line carries `chars` / `tokens` (≈ chars / 4); a long run stays under 4 000 tokens | DONE | `fc41e0515` | in rail ① (the whole request < 4 000 tokens) |
| + | ⭐ **found by the run:** `ask_sitrep` during a play ENDED the play (it went through the direct-order path — CE-4680 B-1's) | FIXED | `6a6629b56` | `CE4683_AskingForASitrep_DuringAPlay_DoesNotEndThePlay` (mutation: back behind the play check ⇒ red) |

### L-3a — the Haiku relay run, side by side with L-3 (the same scenario, settings and worker prompt)

| | L-3 (`l3-relay`) | ⭐ L-3a |
|---|---|---|
| calls | 48 | **29** |
| tool calls | 65 (note 48 · order 12 · ask_sitrep 3 · run_play 1 · hold 1) | **33** (note 28 · run_play 2 · order 2 · ask_sitrep 1) |
| orders accepted / refused / malformed | 17 / 0 / 0 | 5 / 0 / 0 |
| ⭐ notes at the end | **17 958 chars** (48 notes) | **366 chars** (one estimate) |
| ⭐ identical orders re-sent | 4 (e.g. `SupportByFire` to the same squad and place) | **0** — it never repeated an order; the ORDERS IN FORCE line told it (so no *"already in force"* answer was needed — the no-op is proven by its rail) |
| ⭐ `run_play` | 1 (`PlatoonBound`), then direct orders only | **2** (`PlatoonAttack → house` at 0:00 and again at 1:29 — the second only because ask_sitrep had ENDED the first: the defect fixed above) |
| ⭐ request size, first / median / last (≈ tokens) | 1 003 / 5 213 / 5 929 (max 7 349) | **1 110 / 1 438 / 1 438 (max 1 618)** — under the 4 000 budget all run |
| answer time (mean) | 6.0 s | 4.8 s |
| checks | 4 PASS · 2 SKIP · 2 FAIL | ⭐ **5 PASS** · 2 SKIP · 1 FAIL — *"PlatoonAttack within 1 s of the first SPOT"* now PASSES (37 s: the play ran from the start); *"every Red down"* FAILS — the play stalled 20 m SW of start for over a minute (Bravo never assaulted), the squad-attack stall CE-4682 (behaviors), not this slice |

Exchanges (radio lines in → tool calls out, verbatim, truncated):
- `00:00:00 SNAPSHOT — Platoon: ALPHA SQUAD at start; BRAVO SQUAD at start; ORDERS IN FORCE: none;` → `run_play {PlatoonAttack, house}`
- `00:00:36 BRAVO SQUAD: spot, 1 enemy (Individual), house` → `note "PlatoonAttack on house running. Alpha moving to support … Play continues; Bravo assaults after support set. No open ground east of house without fire on it. Deadline 00:07."`
- `00:03:26 (no news — your squads await orders)` → `note "PlatoonAttack on house in force since 00:01:30 but no progress for about a minute … Bravo … not yet moving to assault. Watching Bravo's move."` (⭐ it READ the PLAY IN FORCE line)
- `00:03:56 (no news)` → `order Alpha SupportByFire → house` · `order Bravo Attack → house` + `note "PlatoonAttack stalled at 20 m SW of start for over a minute, so I took direct control."`
- `00:06:53 ALPHA SQUAD: spot, 1 enemy (Individual), house` → `note "… Deadline 00:07, about 10 seconds left; house not yet taken. No change to orders."` (the snapshot it saw: `ORDERS IN FORCE: ALPHA SQUAD SupportByFire → house since 00:03:56 (you), BRAVO SQUAD Attack → house since 00:03:56 (you)`)

⭐ **For L-3b:** the briefing is now ~1 400 tokens and does not grow — an 8 B model at `num_ctx` 8 k has room for it and its answer. ⚠ The part
that can still grow is the radio log between snapshots: here 10 lines in a minute of contact (SITREPs every 15 s + SPOTs) — bounded by the
snapshot period (120 s); a busier fight would want a shorter snapshot period, not a code change.

### L-3a gates
| gate | command | result |
|---|---|---|
| the commander rails (L-1 … L-3a) | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --filter "FullyQualifiedName~CE4683|FullyQualifiedName~CE4681|FullyQualifiedName~CE4676|FullyQualifiedName~CE4679|FullyQualifiedName~CE4680"` | **21 / 21** |
| red-proofs (L-3a) | compiling mutations, one per rail | **4 / 4 red** |
| the routes | `… --filter DebugApiCommandersTests` | **3 / 3** — updated for L-3a: `run_play` first; a posted note is the estimate, not a feed item |
| the checks grammar | `… --filter ScenarioChecksTests` | **29 / 29** |
| ⭐ the scenario, scripted mind | `scripts/scenario-regression.sh llm-platoon-attack` | **PASS 8/8** (326 s; the same times as before — the scripted mind unchanged) |
| ⭐ the Haiku relay run | above | 5/6 judged PASS, 2 SKIP, 1 FAIL (CE-4682) |

### L-3a ids allocated
None new. `CE-3501` CLOSED into this row.

## Duel — two Haiku commanders, one per side (`2026-11-23`, user: "can we let 2 haiku workers play against each other? could it bring some new info about what might be wrong and needs fixing?" · "yes please")

`llm-platoon-duel` (§5a-5): Blue's Platoon (Alpha, Bravo) vs Red Platoon (Defenders at the sheds, Reserve west of them), both seats `ChatModel`,
ThinkPause, one relay (`chat_standin.py --mode relay --per-commander`, port 11502), one Haiku worker per side seeing only its own queue. ⚠ The run
used the build from BEFORE the fixes (it found them).

| | Blue (Platoon) | Red (Red Platoon) |
|---|---|---|
| calls · accepted · refused · no-ops · malformed | 48 · 4 · 0 · 0 · 0 | 63 · 10 · 0 · 0 · 0 |
| request, max tokens | 1 685 | 2 018 |
| answer time, mean (the relay round trip) | 6.1 s | 7.0 s |
| what it did | `run_play PlatoonAttack → house` at 0:00; the same again at 1:05 (D-4/D-5); at 3:07 dropped the play for direct Attack + SupportByFire — the squads stayed near `start` (CE-4682) | `run_play PlatoonDefend → house`; sitreps; at 4:33 Reserve `SupportByFire` at **`support` — Blue's place (D-2)**, BreakContact to `rear`, Defend at `rear`, Hold at the house |
| end (7:00) | Alpha combat-ineffective, Bravo out of the fight at 6:27; house not taken | Defenders out at 6:12; **Reserve holds the sheds** (Amber) |

| check (`--verify`, 1 118 s wall) | |
|---|---|
| each side's squads report to their own commander (4 rows) | ✅ PASS (3.07 s) |
| Red fires · the fight lasts (Blue fires 60 s after Red's first shot) | ✅ PASS — ⚠ the second is weaker than written: CE-3502 |
| the two cross-side `never` rows | ❌ FAIL — **not a leak**: a list in `by` reads as "anyone" (CE-3502), so they matched Blue's own reports to Blue. Every Red request named only DEFENDERS / RESERVE and every Blue request only ALPHA / BRAVO. Rewritten one name per row |

| found | fixed | rail |
|---|---|---|
| D-1 two seats thinking at once froze a free-running clock for good | ✅ `CommanderSeats` owns ONE pause / resume | `TwoSeats_TheClockRunsAgain_*` (red before the fix) · `TwoSeats_AUserPause*` |
| D-2 Red told Blue's start / objective / support — and USED `support` | ✅ a place drawn for a side is only that side's (the duel's places now carry their side) | `TwoSeats_EachSeatIsToldOnlyItsOwnSidesPlaces` (red-proved: filter off ⇒ line 106) |
| D-3 the play advice spoke only to an attacker | ✅ PlatoonAttack / PlatoonDefend both said | the same |
| D-4 a repeated `run_play` restarted the play (Bravo sent back to the start) | ✅ a no-op, like a repeated order | `Duel_ARepeatedPlayInForce_*` (red-proved: no-op off ⇒ two intents, line 125) |
| D-5 a 65 s old snapshot's "ORDERS IN FORCE: none" read as now | ✅ the snapshot says its age | the same |
| D-6 the heartbeat said "your squads await orders" with orders in force | ✅ "the orders in force stand unless you change them" | the same |
| CE-3502 a list in a check's `by` / `who` / `target` reads as anyone, silently | 📋 filed (lean: a list = any of; until then refuse it) | — |
| ⚠ squads asked for CASEVAC (Red, 4:33, 5:32); the commander has no tool for it | not filed — a vocabulary question for L-4 (the squads' own casualty care runs) | — |
| ⚠ the attack still stalls near `start` under the play and under direct orders | CE-4682 (behaviors) — unchanged | — |

### Duel gates

| gate | result |
|---|---|
| `dotnet build Hrot.SimHost.Tests --no-restore` | 0 errors |
| `dotnet test … --filter TwoSeats\|Duel_\|CE4676\|CE4679\|CE4680\|CE4681\|CE4683 --no-build` | ✅ 25 / 25 |
| red-proofs | D-1 red before the fix · D-2 filter off ⇒ red (line 106) · D-4 no-op off ⇒ red (line 125) |
| `DebugApiCommandersTests` (prefix text) | only asserts "SNAPSHOT" — still true; not run |
| `tracker-counts.py --check` | ✅ |

### Duel ids allocated
`CE-3502` (backend next free `CE-3503`).

