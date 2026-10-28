<!--STATUS
state: LIVE — coordinator → backend: surface a branched exercise's parent on the inventory (for ui's CE-4097 ■, AQ108 D5); answers NOTE_Ui_To_Backend_Run_Origin_For_Stop.md with one correction
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Run_Origin_For_Stop.md — ui's ask (on `ui`, `2de6b79b6`)
  - ../Architect_Question_108_Run_Mode_Picker.md — D5 (■ of a branched run returns to the parent recording at the branch point), §2.8 (ui's CE-4097 core)
-->
# HANDOFF — coordinator → backend: a branched exercise's parent and branch point on the inventory

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. ⭐ A small item — do it **after CE-4093's master check round**
(ui builds against that next), before CE-4089. ⭐ **Sonnet** (plan §2a's criteria: a field threaded through named sites).

## ⚠ The correction to ui's note

ui measured *"`grep ParentExercise` in `FDP/` and `Hrot/` → 0"*. ⛔ **Not so** — the recording ALREADY knows its parent:
`RecordingMetadata.ParentExerciseId` + `BranchWallTicks` (`FDP/Engine/Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs:107-110`, CE-3299 D18),
written by `RecordingModule.cs:89` from `RecordingBranch` (`FDP/Engine/Fdp.Core/FlightRecorder/RecordingBranch.cs:16`). What is missing is only the
**inventory** carrying it to the hosts — ⛔ so no new provenance, no second record of the branch.

## The task

| # | what | where |
|---|---|---|
| 1 | `ExerciseInventoryItem` gains `ParentExerciseId` (Guid, empty = not a branch) and the branch point — ⭐ keep the metadata's own unit (`BranchWallTicks`), plus sim seconds only if a host cannot convert (ui seeks the replay by its axis — check `ReplayPosition` / the axis before adding a second unit) | `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterCqrsEvents.cs:5` |
| 2 | fill it from the metadata where the inventory reads a finished exercise | `StorageGatewayModule.cs:813` (and `:744`) |
| 3 | fill it for the LIVE run too — the master knows it branched (the branch trajectory / `RecordingBranch`) | `AssetInventoryProcessManager.cs:136,150` |
| 4 | the wire / HTTP form of the inventory carries both, old peers read empty | the inventory's DTO + `/exercises` |

**Done when:** a live run branched from a recording lists, on every host's inventory, the parent's id and the branch point; a run from a load lists
empty; the recording's metadata is the one source (a rail reads both and asserts they agree). Then post a one-line NOTE to ui (it switches ■'s
branch case over).

Gate report contract as in the run-modes handoff; fold the as-built into AQ108 D5.

_coordinator lane, 2026-10-28_
