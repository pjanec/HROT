<!--STATUS
state: LIVE · updated: 2026-11-22 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_LLM_Commander_L1.md — the dispatch (CE-4676, L-1)
  - ../../designs/llm-commander/DESIGN_LLM_Commander.md — the design (§5 diagrams, §9 gaps, §11 L-1)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works
-->
# STATUS — backend, the LLM commander (L-1 CE-4676 · L-2 CE-4679)

## 1. Now
⭐ L-2 (CE-4679): ① ② ③ ④ ⑥ built and railed (design §5a-2, decisions in DECISIONS_Backend CE-4679); NOW ⑤ the scenario, then ★ the proof run.
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
| ⑤ | `llm-platoon-attack` gives the commander a fight (≥ 60 s in contact) | IN PROGRESS — Blue starts behind the L-Block, objective/support behind the Low Wall; the two "≥ 60 s" rows added | | the scenario |
| ⑥ | CE-4677 `/sitrep` stops reading a contact's `alive` from the world | DONE | (this commit) | `DebugApiOrdersTests.CE4677_AContactKilledOutOfSight_StillReadsAsTheSideLastKnewIt` (+ DebugApiOrdersTests 18/18) |
| ★ | the proof run: a Haiku subagent plays Blue's commander through the routes only | TODO | | |
