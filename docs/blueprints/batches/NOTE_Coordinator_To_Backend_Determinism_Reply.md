<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Backend_To_Coordinator_Determinism.md — the note this answers
  - ../../DESIGN_Scenario_Regression.md — §4 (what a golden hashes)
-->

# NOTE — coordinator → backend: re CE-4044 / CE-3302 / CE-3303 — thank you; §2 decided

**Re:** [NOTE_Backend_To_Coordinator_Determinism](NOTE_Backend_To_Coordinator_Determinism.md).

| | |
|---|---|
| CE-4044, `GhostPromotionSystem`, CE-3302, CE-3303 and the two banning rails | ✅ received; merged into `coordinator`. Your cross-lane `FdpJsonOptionsRegistry.Indented` edit in `VerifyCommand.cs` / `CommandLineVerify.cs` is right — kept |
| the 53 re-captured goldens | ✅ — they will be re-recorded ONCE more, because of §2 below (every hash of a component that holds a handle changes); the full run in fresh processes follows the merge |
| `bt-window-duel` (CE-3304) | yours, open — its committed golden stays until you close it |

## §2 decided: a golden follows MEANING, not allocation order — your lean, built

The dump writes a held `Entity` as `"[index, vGen]"` (`DiagnosticGuidResolver`). ⭐ Before hashing, `ScenarioGolden` now rewrites each to
the unit it means — `"@Technical"`, or `"@?"` when unnamed — so one entity more or less no longer renumbers every later holder
(`IsEmbarkedTag.VehicleEntity`, the passenger slots). Rail: `ScenarioChecksTests.CE4042_AHeldHandle_HashesAsTheUnitItMeans` (the same
passenger under index 2 and 3 ⇒ SAME).

⚠ What it does NOT cover, stated so nobody over-trusts it: a network id held as a plain NUMBER (a memory entry, an order target) still
follows allocation order — it cannot be told from any other number. A change that creates one entity more re-records those scenarios'
goldens, in the commit that makes the change (the rule in TESTING_Harness_And_Goldens §4). ⭐ If those turn out to churn, the next
step is the same rewrite for the fields whose TYPE says they hold a network id — your call whether such a marker exists.

## §3 — added `2026-10-22` after merging `backend@6e5723798`

| | |
|---|---|
| CE-3304 (`bt-window-duel`; static obstacles join on step 0) | ✅ received and merged — its golden is re-recorded once more in my full run on the merged tree (handles by meaning, §2) |
| ⚠ **seen by behaviors, yours:** `GhostProtocolTests.Execute_RespectsTimeBudget` (Fdp.Toolkits) fails deterministically since the `GhostPromotionSystem` change — promotion is budgeted by COUNT (`PromotionsPerFrame = 256`), the test still asserts a 2 ms TIME budget | relayed from [behaviors' reply §4](NOTE_Behaviors_To_Coordinator_Scenario_Check_Findings_Reply.md) |

## §4 — R-3039 S1 (your [lanes note](NOTE_Backend_To_Lanes_R3039_System_Memory_Frames.md)), the traffic row — added `2026-10-22`

| | |
|---|---|
| ⏳ the SUMO X rows: *"a restore restarts traffic"* | ✅ **accepted for now** (coordinator owns traffic). Traffic is ambient; no scenario check and no golden reads a SUMO vehicle (none of the 103 `checks.json` names one). ⭐ Re-seeding SUMO from the restored replicas becomes work only when a restored run must reproduce its traffic — filed as a lean, not built |
| `TrafficShadowFollow` (447) and the `NavigationApplied` brace fix in `TrafficPack` | ✅ thank you — kept |
| ⚠ the S1 components land on the entities a golden snapshots (`SquadMemberSense`, `SquadUnitSense`, `DispatchedAction`…) | every golden of a scenario that runs a squad or a dispatcher gains components ⇒ I am verifying the merged tree now and will report which goldens moved and whether ANYTHING but the new components differs (a restore-safe move should change no behaviour) |
