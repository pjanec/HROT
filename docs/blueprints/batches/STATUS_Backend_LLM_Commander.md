<!--STATUS
state: LIVE · updated: 2026-11-22 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md — the dispatch (CE-4676, L-1)
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — the design (§5 diagrams, §9 gaps, §11 L-1)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works
-->
# STATUS — backend, the LLM commander (L-1 CE-4676 · L-2 CE-4679)

## 1. Now
⏸ `2026-11-24 15:35` — **idle — waiting for a handoff** (self-wake-up: CE-4686 done; no new HANDOFF/STEER/NOTE for backend on coordinator / behaviors / ui; no new DECIDED answers. Open leans in §Aggregates 2: abort a waiting order whose condition's unit left the fight (backend); a company hold-back attack at its split (AGG lane)).
⭐ `2026-11-24` **CE-4686 (the four findings of the LLM commander over aggregated units) — DONE**: ① HQ-3b relay · ② roles · ③ SupportByFire (root cause: the reconciler CLEARED it at the split — fixed) · ④ BoundOrder; 11 red-proved rails; scripted ✅ 11/11, Haiku low effort ✅ 7/7 judged. Report: §Aggregates 2; designs: Command Chain §8d, Orders §16, Split One Level §10, LLM §5d.1.
⛔ ~~`2026-11-24 10:38` — idle~~ (CE-4686 taken 13:40) **idle (self-wake-up: CE-4685 done; no new HANDOFF/STEER/NOTE for backend on coordinator / behaviors / ui; no new DECIDED answers. The open leans from §Aggregates — SupportByFire as a KIND for a platoon, a role line per platoon under a whole-unit order — wait for the user).
⭐ `2026-11-24` **CE-4685 (the LLM commander over AGGREGATED units, `HANDOFF_Coordinator_To_Backend_LLM_Commander_Aggregates.md`) — DONE**: A-1 … A-4 built, 3 rails red-proved + the relay rail extended (red-proved); ⭐ user mid-slice: *"We need to introduce the order kinds to higher level echelons. We cannot call directly a behavior name"* — the seat now gives its whole unit an order KIND (built, railed, red-proved); `llm-company-aggregated` scripted ✅ 9/9, Haiku low effort ✅ 5/5 judged (4 scripted-only SKIP); a split platoon's relayed move died at the split — fixed (cross-lane, CE-8307's relay). Report: §Aggregates; design §5d.
⛔ ~~`2026-11-24 07:29` — idle~~ (CE-4685 taken 08:20) **idle — waiting for a handoff** (self-wake-up: L-3c done; CE-11047 (ui's staged-presentation garbage) fixed `c77363b9a` — the Stechovice re-measure is ui's; no new HANDOFF/STEER/NOTE for backend; the next slice is L-3b on the user's laptop).
⭐ `2026-11-23` **L-3c (CE-4684, the decision contract) — BUILT**: C-1 … C-6 railed and red-proved (11 rails); the low-effort baseline and the contract run on `llm-platoon-attack` side by side below; the contract duel ✅ 10/10. Report: §L-3c; design §5c.1.
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

## L-3c — the decision contract (CE-4684, `2026-11-23`; handoff `HANDOFF_Coordinator_To_Backend_LLM_Commander_L3c.md`, merged `coordinator` at `998bea849`)

| # | item | state | commit | rail (red-proved by a compiling mutation) |
|---|---|---|---|---|
| C-1 | one decision per wake; the seat's schema as `format`; validate · repair once · CONTINUE | DONE | `276df8598` | `CE4684_ADecision_IsSentWithTheSeatsSchema_AndItsOrderLands` · `_AnUnknownIdAndAWrongCycle_AreRepairedOnce` (no repair ⇒ red) · `_ATaskTheSquadCannotDo_ThenAMalformedAnswer_FallsBackToContinue` (task check off ⇒ red) |
| C-2 | start / complete_when / abort_when, ≤ 3 contingencies, `ConditionWatcher` | DONE | `276df8598` | `_AnOrderWaitsForItsStart_ThenGoes` (start ignored ⇒ red) · `_AContingencyFires_AndAnAbortHolds_AndTheNextBriefingSaysSo` (trigger never holds ⇒ red) |
| C-3 | next_decision (floor 10 s / ceiling 300 s) + the cycle echo | DONE | `276df8598` · `de229e2e3` | `_AStaleCycle_IsRefused_AndTheDecisionOnTheNewerCycleLands_NoLoop` (cycle check off ⇒ red; refusal a wake ⇒ red) · `_TheModelsOwnDeadline_AndItsWakeCondition_WakeIt` (deadline ignored ⇒ red) |
| C-4 | terrain facts per place, once at seat open | DONE | `276df8598` · `de229e2e3` | `_TerrainFacts_SayWhoSeesEachPlace_ItsCover_VehiclesAndMinutes` (building check off ⇒ red) · `_TerrainFacts_JudgeAPlaceWhereAManStands_NotARingAroundIt` (ring back ⇒ red) |
| C-5 | enemy confidence HIGH / MED / LOW | DONE | `276df8598` | `_EnemyConfidence_FromHowManySquadsReportedItAndHowLongAgo` (second squad never counted ⇒ red) |
| C-6 | ids only for a decision seat | DONE | `276df8598` | `_IdsOnly_ABearingIsNotAPlace` (place check off ⇒ red) |

**Found by the runs and fixed** (each with its rail):
- ⛔ **a refusal loop** — the first contract run refused 15 decisions in a row at 00:00:36: a stale decision's refusal was a WAKE, arrived while the model answered the newer news and staled that answer too. A refusal is no longer a wake (the newer news re-asks the model). Run stopped, fixed, re-run.
- ⛔ **wrong terrain facts** — the C-4 hand check: a 6 m ring read the objective (behind the Low Wall, "cover from the house") as seen from the house. A place is now judged where a man stands.

**C-4 hand check** (`llm-platoon-attack`, the facts as the model gets them vs the scenario's own notes):

| place | the note | the facts | |
|---|---|---|---|
| `start` | "behind the L-Block, out of sight of the house" | seen from: none — the house sees neither it nor is seen from it | ✅ |
| `objective` | "cover from the house" | seen from: support (only — the same wall, 10 m apart) | ✅ (❌ before the fix: "house, support") |
| `house` | `support` "sees the sheds' south side over the wall" | seen from: none | ⚠ open — the point sits among the sheds; the note speaks of the building's wall |

**The proof — Haiku at the LOWEST effort** (`model: haiku`, `effort: low`; each worker sees only the request JSON):

| `llm-platoon-attack` | ① baseline — L-3a tool calls | ② the contract |
|---|---|---|
| calls | 26 | 58 |
| orders given / refused / no-ops | 9 / 0 / 0 | 6 / 1 / 1 (orders now come once, then CONTINUE) |
| decisions applied · stale refused · repaired · fell back to CONTINUE | — | 47 · 11 · 1 · 0 |
| contingencies armed → fired · aborts · own wakes fired | — | 11 → 1 · 0 · 10 |
| `next_decision` deadlines it set | — | 60, 60, then mostly 15–30 s (min 15) |
| request tokens first / median / last (max) | 1 159 / 1 468 / 1 465 (1 764) | 1 776 / 2 539 / 2 150 (3 429) — under ≈ 4 000 |
| answer time (relay round trip) | 5.1 s | 6.3 s |
| checks (2 scripted-only rows SKIPPED) | ✅ 6/6 — **Red beaten** (first relay run to pass it) | ✅ 6/6 — Red beaten |

⚠ What it costs: the schema adds ~600 tokens to every request, and the model chose short deadlines — 2.2× the calls. CE-4682's stall still shows (Alpha
held near `start` for a minute under the play); both runs got there anyway.

Three decisions as the model wrote them (contract run):
- **00:00:08, cycle 6** — *assessment:* "Both squads still at start; Alpha is setting the support position behind the Low Wall. PlatoonAttack is running and no contact yet." · *enemy likely:* "Red holds the south side of the house and will engage as soon as our squads show at the wall or cross the open ground east of the house." · *main risk:* "The assault crossing the open ground east of the house before support is set and firing on the south side." · *intent:* main effort Platoon — "Keep the PlatoonAttack running: support sets behind the Low Wall, then the assault goes in on the house once fire covers the south side. No change to orders." · *orders:* none · *contingencies:* none · *next:* 60 s, wake on CONTACT Alpha Squad / CONTACT Bravo Squad.
- **00:03:11, cycle 58** — *assessment:* "Alpha has spotted an enemy at the house and is still on its move … Bravo is Amber, at support, keeping fire on the house; a team of two Red is 20 m west of the house." · *main risk:* "Alpha does not reach support in time to assault before 00:07." · *intent:* main effort Alpha Squad — "Alpha reaches support; the contingency sends it into the assault on the house once it is there, with Bravo's fire from support on the south side." · *orders:* none · *contingency:* UNIT_AT Alpha Squad support → Alpha Squad Attack house ("Assault the house from support while Bravo keeps fire on it.") — ⭐ it FIRED at 00:03:24 with no model call · *next:* 20 s, wake on UNIT_AT Alpha Squad support / STRENGTH_BELOW Bravo Squad 50 %.
- **00:04:28, cycle 104** — *assessment:* "Bravo is set at support at Red strength … Alpha is Amber … it has not reported progress toward the house since." · *main risk:* "Alpha has not moved for about 25 s; the house is not taken before 00:07 unless Alpha crosses now." · *intent:* main effort Alpha Squad — "Alpha's attack on the house continues … No change to the orders in force; I am checking Alpha's position at the next decision." · *orders:* none · *next:* 20 s, wake on CONTACT Alpha Squad / STRENGTH_BELOW Alpha Squad 50 %.

**③ The duel with the contract on both sides** (`llm-platoon-duel`, two low-effort workers through the per-commander relay; the runner copied aside so
the gate builds could not touch it):

| | Blue (Platoon) | Red (Red Platoon) | first duel (tools, default effort, pre-fix build) |
|---|---|---|---|
| calls · decisions · stale refused · repaired · CONTINUE fallbacks | 63 · 56 · 7 · 1 · 0 | 66 · 57 · 9 · 1 · 0 | Blue 48 / Red 63 calls |
| orders given · refused · no-ops | 13 · 0 · 29 | 4 · 2 (BreakContact for a squad out of the fight) · 1 | Blue 4 / Red 10 accepted, 0 no-ops |
| contingencies armed → fired · own wakes fired | 63 → 3 · 8 | 66 → 0 · 5 | — |
| deadlines (min / median / max s) | 30 / 40 / 60 | 30 / 30 / 60 | — |
| tokens first / median / last (max) | 1 766 / 2 607 / 2 613 (3 448) | 1 593 / 2 533 / 1 975 (3 690) | max 1 685 / 2 018 |
| answer time (the relay round trip, two workers at once) | 15.3 s | 14.5 s | 6–7 s |
| checks | ✅ **10 / 10** — each side's squads report only to their own commander (the four cross-side rows, one name per row since CE-3502), the fight happens and lasts | | 6 / 8 (the two list-`by` rows — CE-3502) |

The game: Blue ran PlatoonAttack, switched to fire and movement, Alpha went Black at ~5:03; Red ran PlatoonDefend, ended it to put the Reserve in, the
Reserve held the sheds to the end — Red held, as in the first duel.

📌 **A contingency re-armed every decision fires again once its trigger already holds** — Blue re-sent the same `UNIT_AT Alpha Squad support → Alpha
SupportByFire house` contingency with each decision and it fired three times (3:16, 3:36, 4:06); the repeat order was a no-op each time, so no harm
here. ⭐ Lean for later: a contingency fires on its trigger BECOMING true after it is armed (an edge), so re-arming one that already holds does not
re-fire it — not changed in L-3c (scope), worth one line in the prefix or the watcher when L-3b shows it costs.

📌 The worker noted twice that a squad asking for CASEVAC has no order to answer it (as the duel did) — an L-4 vocabulary question, not filed.

### L-3c gates

| gate | result |
|---|---|
| `dotnet build Hrot.SimHost.Tests --no-restore` | 0 errors |
| the commander rails (L-1 … L-3a, the duel, L-3c) `--filter CE4684\|TwoSeats\|Duel_\|CE4676\|CE4679\|CE4680\|CE4681\|CE4683` | ✅ 36 / 36 |
| the same under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` (the watcher is a serial system) | ✅ 36 / 36 |
| red-proofs | 11 rails, each red under its mutation (one build, all mutations), green restored |
| `DebugApiCommandersTests` | ✅ 3 / 3 |
| `ScenarioChecksTests` | ✅ 29 / 29 |
| the scripted mind on `llm-platoon-attack` | ✅ 8 / 8 (PlatoonAttack at 78.25 s — unchanged) |
| the laptop recipe's dry run (`chat_standin.py --mode script` now answers decisions) | ✅ end to end, 12 calls |
| the relay runs | ① baseline ✅ 6/6 · ② contract ✅ 6/6 · ③ the contract duel ✅ 10/10 |

### L-3c ids allocated
none (the slice's own id is the coordinator's CE-4684).

## Aggregates — the seat over aggregated units (CE-4685, `2026-11-24`; handoff `HANDOFF_Coordinator_To_Backend_LLM_Commander_Aggregates.md`)

| # | built | rail (each red-proved) | commit |
|---|---|---|---|
| A-1 | the seat at any echelon: echelon / subordinates from the CLASS (header DIS type ⇒ TKB ⇒ `AggregateUnit.Echelon`); an aggregated row says `aggregated, N men` once its slot table holds slots | `Aggregates_ACompanySeat_IsBriefedAsACompany_OverItsAggregatedPlatoons` | `d38fef063` |
| A-2 | ⭐ his whole unit gets an order **KIND** (`Order.<kind>`), the routing table runs his class's play; the kinds offered are those his class resolves (a company: Attack, Defend) | `Aggregates_ACompanySeat_GivesItsWholeUnitAKind_TheTableRunsItsClassesPlay` | `5a71fdd5e` |
| A-3 | a stochastic subordinate's split is ONE waking radio line ("near the enemy — it has split …; your orders to it stand"); same entity, orders still land | `Aggregates_APlatoonThatSplits_IsNewsOnce_AndTheSeatsNextOrderStillLands` | `d38fef063` |
| A-4 | `llm-company-aggregated` (berlin, inside both grids): a seated company over three stochastic platoons, Red's squad 650 m south | the scenario | `7d8722971` |
| ⭐ found | `UnitRelayBehaviors.Relay` (CE-8307, AGG lane — cross-lane) ended Success before a child had started, and ending it cleared the children's moves ⇒ every split platoon stopped where it split. Fix: a child given the order but not yet running is busy for 3 s sim | `CE8307_ASplitPlatoonsMove_IsRelayedToEverySquad_ByKind_SameParams` (extended) | `5455dba72` |

### Aggregates — the proof

| `llm-company-aggregated` | ① scripted | ② Haiku, LOWEST effort, the contract (kinds) |
|---|---|---|
| checks | ✅ **9 / 9** — orders by kind reach the stochastic platoons (`StochasticUnitMove` 0.25 s); `Attack` → the table ran `CompanyAttack` at the first split (39.25 s); 2nd Platoon split (38.25 s) and still reported (48 s); its squads fired (110.8 s); Red fired | ✅ **5 / 5 judged**, 4 scripted-only SKIPPED — reports before the split (3 s); 2nd Platoon split at 370 s and reported after (373 s); a split platoon's man fired (116 s); Red fired |
| calls | — | 19 (wall mean 7.1 s) |
| decisions applied · stale refused · repaired · CONTINUE fallbacks | — | 17 · 2 · 0 · 0 |
| orders: whole unit · subordinates · no-ops ("already in force") | — | 1 (`Attack` → position, support) · 3 (BreakContact, SupportByFire ×2) · 4 |
| contingencies armed | — | 4 (none fired) |
| request tokens first / median / last (max) | — | 1 826 / 2 455 / 2 973 (2 973) — under ≈ 4 000 |

Three decisions as the model wrote them:
- **00:00:00, cycle 1** — *situation:* "All three platoons are out of contact at the assembly area start, 700 m south of Red's defended position." · *risk:* "Crossing the open ground … without fire on it." · *intent* (main effort Blue Company): "Establish support by fire from the support position …; then assault the objective with the company, keeping a platoon in reserve." · *orders:* **Blue Company `Attack` → position, support_at support** (the KIND; the table ran `CompanyAttack`) · *contingency:* CONTACT 1st Platoon → 1st Platoon BreakContact → start.
- **00:03:09, cycle 47** — *situation:* "3rd Platoon … now Red on strength (combat ineffective) on its old Defend order, which I did not intend …" · *risk:* "The attack cannot be re-sent as a company order …" · *orders:* 3rd Platoon BreakContact → start; 2nd Platoon SupportByFire → position (ENDS the company's Attack) · *contingency:* UNIT_AT 2nd Platoon support → 2nd Platoon Attack → position.
- **00:06:33, cycle 110 — after the split (370 s)** — *situation:* "2nd Platoon is Green but stalled at 180 m NE of the support position, with its support-by-fire task in force …" · *risk:* "a 1st Platoon assault now would go in without fire on the position, which the mission forbids." · *orders:* none (CONTINUE) — the assault waits for fire on the position.

### Aggregates — what did NOT work with aggregated units, and whose it is

| finding | owner |
|---|---|
| ⛔ **a company hears no SPOT of infantry**: a commander's staff composes SPOT from its OWN contacts and relays only indirect fire / air defence its subordinates report (`StaffSystems.Relay`, CE-2310 HQ-3). A platoon command entity senses nothing ⇒ the company seat knows the enemy only from the briefing, strength colours and splits. The scripted mind attacks on a split instead | the command chain (`DESIGN_Command_Chain_Example.md` §6, behaviors) — a design question: should a commander relay his subordinates' SPOTs up? |
| ⚠ the company's `Attack` (`CompanyAttack`) gives a platoon a **Defend** as part of its choreography; the model read it as *"an old Defend order I did not intend"* and broke the play to save that platoon | seat (backend) — the orders a whole-unit order gives are shown as `(<echelon> order Attack)` but not WHY; a role line per platoon (base of fire / assault / reserve) is the lean |
| ⚠ 2nd Platoon's `SupportByFire` **stalled ~180 m NE of the support position** after the split | ⛔ not root-caused. The seat sends a subordinate's SupportByFire as `Attack` + `HoldBack` (R-8303, built for squads); for a PLATOON the table has a `SupportByFire` kind row (`PlatoonSupportByFire`), and a hold-back Attack has no physical platoon play (R-8392). ⇒ lean: send the KIND `SupportByFire` where the subordinate's class resolves it — the same rule as A-2. Seat (backend), to measure first |
| ✅ fixed: the relay ended under its children (above) | AGG lane's code, fixed here (cross-lane, said) |
| ⚠ Bound has no neutral order DTO — the whole unit's Bound carries `PlatoonBound`'s own fields | orders (`DESIGN_Orders_Params_Entity_Classes.md`) |

### Aggregates gates

| gate | result |
|---|---|
| the commander + relay rails `--filter CE4684\|TwoSeats\|Duel_\|CE4676\|CE4679\|CE4680\|CE4681\|CE4683\|Aggregates_\|CE8307\|X2` | ✅ 52 / 52 |
| the same under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | ✅ 52 / 52 |
| red-proofs | A-1 (echelon dropped), A-2 (own kinds fixed to four), A-3 (split check dropped), the relay grace (0) — each red, green restored |
| `ScenarioChecksTests` | ✅ 29 / 29 |
| `DebugApiCommandersTests` | ✅ 3 / 3 |
| `agg-platoon-under-company` · `agg-platoon-splits-into-squads` (run, not changed) | ✅ 5 / 5 · ✅ 28 / 28 — ⚠ both print "GOLDEN different" (a warning, RT-3); not checked against the base |
| `llm-platoon-attack`, scripted (the kind path on a real platoon) | ✅ 8 / 8 — `PlatoonAttack` at 78.25 s, unchanged |
| `llm-company-aggregated` | ✅ scripted 9 / 9 · ✅ Haiku 5 / 5 judged |

### Aggregates ids allocated
none (the slice's own id is the coordinator's CE-4685).

## Aggregates 2 — the four findings (CE-4686, `2026-11-24`; handoff `HANDOFF_Coordinator_To_Backend_LLM_Commander_Aggregates_2.md`, merged `coordinator` at `a8e866305`)

⭐ **① and ④ are user-authorised CROSS-LANE edits** (behaviors' command chain; the orders design and the Bound plays), and so are ③'s change to the AGG
lane's reconciler, ②'s read-only interface on four behaviors plays and ④'s map letter in the ui keymap — each said in its commit.

| # | built | rail (each red-proved by a compiling mutation) | commit |
|---|---|---|---|
| ① | **HQ-3b**: a commander of UNITS relays its subordinates' ground SPOTs up — grouped (50 m), own age, ≤ 2 groups per sender, refreshed on a move / 15 s; a full picture never loses a support element to a ground report. The seat names the source (`reported by 2nd Platoon`) | `CE4686_APlatoonRelaysItsSquadsSpotUp_AsOneGroup_WithItsOwnAge` · `…AtMostTwoGroups_TheFreshest_AreRelayed` · `…AFullPicture_AGroundReportNeverEvictsASupportElement` · `…ACompanySeat_SeesARelayedSpot_WithItsSourceAndAge`; `CE2310_…` amended | `eece5cc40`, `26602fa5d` |
| ② | the snapshot shows each unit's ROLE in the running whole-unit order (`IPlayRoles` on PlatoonAttack / CompanyAttack / PlatoonDefend / CompanyDefend, read through `OccurrenceSlotCatalog`); both prefixes say a role's orders are the order's | `CE4686_ACompanySeat_ReadsEachPlatoonsRoleInItsRunningAttack` | `c153f8b52` |
| ③ | the base of fire STANDS at `support_at`, fires at the objective; a physical platoon gets the KIND (`PlatoonSupportByFire`); ⭐ at a split a hold-back attack is RE-ISSUED as `PlatoonSupportByFire`, not cleared (R-8392 amended); a failed order leaves the in-force table; a unit out of the fight carries no order in force | `…TheKindSupportByFire_ResolvesFor…` (measured) · `…ASupportByFire_StandsAtTheSupportPlace_…` · `CE8307_…_AHoldBackAttackBecomesItsSupportByFire` · `…AnOrderItsUnitReportedFailed_IsGivenAgain_…` · `…AUnitOutOfTheFight_CarriesNoOrderInForce` | `03cab7e19`, `410037d29`, `c21be7343` |
| ④ | `BoundOrder { Position, SupportPoint, Speed }` — read by PlatoonBound, BoundingOverwatch and (by Position) the stochastic attack; PlatoonBound's cover never to (0, 0); Bound's map letter `N` | `BehaviorParamsValidationTests.CE4686_OneBoundOrder_MeansTheSame…` + the R-8300 contract rail (now walks Bound) | `19062a7ad`, `410037d29` |

### ③ — the claim table (the stall, measured in two runs)

| the claim | code — how it IS | design — how it was MEANT |
|---|---|---|
| the base of fire was told to stand on Red's position | ✅ `Decision.ToAction` passed `Where: objective`; `AttackOrder.HoldBack`'s Position = where it STANDS | ✅ CE-8302 R-8341 |
| ⭐ the real stall: a STOCHASTIC platoon on support by fire had its order CLEARED at its split | ✅ measured (Haiku run 1): `[ResolutionFit] unit #2: its order has no play at Physical — cleared` at 6:21, TaskFailed 150 m short | ⛔ R-8392 assumed "no platoon play holds back" — false since `PlatoonSupportByFire` fulfils SupportByFire |
| the model's re-issue was swallowed | ✅ measured: "already in force since 00:00:00 — nothing sent" after the TaskFailed | — |
| the fix works live | ✅ measured (Haiku run 2): `unit #2 re-issues its Attack order as PlatoonSupportByFire` at its split; 2nd Platoon reached `support` (50 m NW at 6:00, at it by 8:00) — destroyed on the way by Red's 4 teams | — |

### Aggregates 2 — the proof (`llm-company-aggregated`, all rows + ① and ③'s)

| | CE-4685 Haiku | ① scripted | ② Haiku, LOWEST effort, the contract |
|---|---|---|---|
| checks | 5 / 5 judged (no ① / ③ rows) | ✅ **11 / 11** — the company runs `CompanyAttack` at the first split (39.25 s); ⭐ a platoon RELAYS Red's squad up (87.3 s); ⭐ a split platoon's man fires AT Red (102.6 s) | ✅ **7 / 7 judged**, 4 scripted-only SKIP — ⭐ ① relayed SPOT at 132.2 s; ⭐ ③ fire at Red 126.9 s |
| did the company see the enemy (①) | ⛔ never | ✅ | ✅ `enemy 4 (Team), 15 s old, MED at position, reported by 2nd Platoon` (00:04:00 snapshot) |
| did it keep the play running (②) | ⛔ ended CompanyAttack to undo a role | ✅ CompanyAttack ran | ⚠ not exercised: the model ordered the platoons directly (no whole-unit order) — proven by its rail only |
| fire on the position (③) | ⛔ stalled 180 m off | ✅ | ✅ 2nd Platoon reached the support place firing; the split re-issued its order as PlatoonSupportByFire |
| calls · decisions · stale · repaired · CONTINUE fallbacks | 19 · 17 · 2 · 0 · 0 | — | 26 · 26 · 0 · 0 · 0 |
| contingencies armed | 4 | — | 12 |
| request tokens first / median / last (max) | 1 826 / 2 455 / 2 973 | — | 1 930 / 2 659 / 2 621 (3 545) — under ≈ 4 000 |

Three decisions as the model wrote them (run ②):
- **00:00:00, cycle 1** — *situation:* "Blue Company is staged at the assembly area … Red holds the position at (300,50) with open ground around it." · *risk:* "The assault cannot cross open ground without fire on the position, and the 2nd Platoon walk to the support area takes about 6 minutes." · *orders:* **2nd Platoon SupportByFire, objective position, support_at support** (⭐ ③: stands at support, fires at the position) · 1st Platoon Attack position, start UNIT_AT 2nd Platoon support · 3rd Platoon Hold · *contingency:* STRENGTH_BELOW 1st Platoon 50 % → 3rd Platoon Attack.
- **00:04:10, cycle 78** — *situation:* "2nd Platoon is out of the fight … The base of fire it was to give is gone. Red has at least four teams and a medic at the position." · *orders:* 3rd Platoon SupportByFire (support) · 1st Platoon Attack, start UNIT_AT 3rd Platoon support.
- ⭐ **00:04:12, cycle 81 — on a RELAYED SPOT** ("2ND PLATOON: spot, 4 enemy (Team), 12 s old, position" — the platoon's relay of its squads' sighting, ①) — *situation:* "… Red has four teams and a medic at the position … 1st Platoon's earlier assault order still waits on 2nd Platoon, which will not arrive, so it must be replaced." · *risk:* "Crossing open ground without a base of fire would be destroyed." · *orders:* 3rd Platoon SupportByFire (support) · **1st Platoon Hold** (the assault waits for the new base of fire).

### Aggregates 2 — what did not work, and whose

| finding | owner |
|---|---|
| ✅ fixed here: a destroyed platoon's order stayed "in force" (found by run ②) | seat (backend) — `c21be7343` |
| ⚠ a contingency / waiting order keyed on a unit that is OUT OF THE FIGHT (`UNIT_AT 2nd Platoon support`) never fires and never aborts — the model had to replace it by hand | seat (backend, the L-3c watcher) — lean: abort a waiting order whose condition's unit left the fight |
| ⚠ a COMPANY's hold-back attack still waits at its split (no company support-by-fire play) | the AGG lane / the plays (no `CompanySupportByFire`) |
| ⚠ Blue never HIT Red in 8 min in any run (fire AT Red: yes) — Red's 4 teams in cover vs. a slow approach over open ground | the scenario (not a defect measured) |

### Aggregates 2 gates

| gate | result |
|---|---|
| the commander + relay + new rails (`CE4684 TwoSeats Duel_ CE4676 CE4679 CE4680 CE4681 CE4683 Aggregates_ CE8307 X2 CE4686`) | ✅ 62 / 62 |
| the same + the HQ-3 rails under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | ✅ 64 / 64 |
| ① the command chain's suite `SquadManeuverTests` (whole) | ✅ 432 / 432 |
| ③④ orders: `TacticalIntentResolutionSystemTests` · `ScenarioRulesTests` · `BehaviorParamsValidationTests` (R-8300 incl. Bound) | ✅ 194 / 194 |
| `ScenarioChecksTests` + `OrderVocabularyTests` · `TheOrdersTabTests` · Core `ParamsDtoRegistry` / `BehaviorFit` / routing · Presentation `MenuOrders` / `RtsCommandTool` / `RuleEditorR5b` | ✅ 39 · 5 · 22 · 72 (K2 was red until Bound declared its letter) |
| red-proofs | 11 mutations, each red, restored |
| ① battalion + aggregate scenarios (run, not changed) | ✅ `cc-battalion` · ✅ `agg-battalion-fight` · ✅ `agg-platoon-under-company` · ✅ `agg-platoon-splits-into-squads` — ⚠ the three agg ones print "GOLDEN different" (a warning, RT-3; they did before this slice too, CE-4685 §gates — not checked against the base) |

### Aggregates 2 ids allocated
none (the slice's own id is the coordinator's CE-4686).

