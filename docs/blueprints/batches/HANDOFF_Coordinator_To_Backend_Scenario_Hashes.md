<!--STATUS
state: LIVE — coordinator → backend: the recorded copy's hash and the saved scenario's hash where every host can read them (ui's CE-4097 AAR → Preview, AQ108 §3 / D7); answers NOTE_Ui_To_Backend_Recorded_Hash (on `ui`, `6725defa6`)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §3 row "AAR → Preview" (two buttons when the recorded version differs), D7, §2.8a (ui's `RecordedVersionDiffers` seam)
  - HANDOFF_Coordinator_To_Backend_Run_Origin.md — the same shape: a fact the master already holds, put on the inventory every host receives
-->
# HANDOFF — coordinator → backend: two hashes on the inventories

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. ⭐ Small; **Sonnet** (a field threaded through named sites).
Do it after the task in hand; nothing on ui blocks on it (ui shows the one-button confirmation until it lands).

✅ ui's measurement holds (checked): the recorded hash lives only in the copy's stamp (`RecordedScenarioStamp`,
`FDP/Toolkits/Fdp.Toolkits/Scenario/RecordedScenarioStamp.cs:19`, master storage); `ExerciseInventoryItem` has no hash; the scenario inventory is
names only.

| # | what | where |
|---|---|---|
| 1 | `ExerciseInventoryItem` gains `ScenarioHash` (empty = unknown / pre-CE-4091 recording) — read from the copy's stamp, the same way the branch fields were added | `ClusterCqrsEvents.cs:5`, `StorageGatewayModule`, `AssetInventoryProcessManager` (the live run's: the go-live stamp) |
| 2 | the saved scenarios' inventory carries each scenario's CURRENT `scenario.json` hash | wherever `AvailableScenarios` is filled (`ClusterUiCache` via the inventory event, `ClusterCqrsEvents.cs:222`), and `GET /scenarios` |
| 3 | ⭐ ONE hash rule for both: `ScenarioDescription.HashOf` (what the stamp uses) — ⛔ no second hash | — |

**Done when:** a recording made from a scenario that was then edited and saved shows a DIFFERENT hash from the scenario's current one on every
host; one made and not re-saved shows the SAME; old recordings show empty (ui then treats them as "unknown", one button). Post a one-line NOTE to
ui. Gate report contract as in the run-modes handoff; fold the as-built into AQ108 D7.

_coordinator lane, 2026-10-29_
