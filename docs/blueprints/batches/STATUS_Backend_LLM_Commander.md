<!--STATUS
state: LIVE · updated: 2026-11-22 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md — the dispatch (CE-4676, L-1)
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — the design (§5 diagrams, §9 gaps, §11 L-1)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works
-->
# STATUS — backend, the LLM commander (L-1 CE-4676 · L-2 CE-4679)

## 1. Now
⏸ `2026-11-23 09:28` — **idle — waiting for a handoff** (self-wake-up: queue empty, no new HANDOFF/STEER/NOTE for backend; CE-3494 §10 (R-3119) built and gated, `STATUS_Backend_Queue.md`).
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
