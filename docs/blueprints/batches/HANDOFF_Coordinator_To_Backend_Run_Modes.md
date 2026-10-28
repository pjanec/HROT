<!--STATUS
state: LIVE — coordinator → backend: the run-modes and content-layout tasks for the backend lane
updated: 2026-10-28
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../PLAN_Run_Modes_And_Content_Build.md — the plan; §2 holds each task's "done when"
  - ../../DESIGN_Content_Layout.md — R-4097 (CE-4089)
  - ../Architect_Question_108_Run_Mode_Picker.md — R-4098 (CE-4090…CE-4095)
-->
# HANDOFF — coordinator → backend: content layout first, then the cluster side of the run modes

**Dispatched at** the commit that adds this line (`coordinator`) — ⭐ RE-STAMPED `2026-10-28` before either lane started (rule 1a; ancestry checked): D20 is the whole world compared ON DEMAND (ninth pass) and autosave is added (CE-4099) and the HTTP surface is explicit (D22, folded into each task). ⭐ **Your scope is FROZEN at that sha** — documents that change after it are FYI
only; if one invalidates a task, STOP that task and report it (never adapt silently). Merge `coordinator` first, push an empty
`chore: started run-modes at <sha>` commit, and name the merge in it.

## The tasks, in this order

| # | task | design | start from |
|---|---|---|---|
| 1 | **CE-4089** the content move — FIRST; ui waits on it | `DESIGN_Content_Layout.md` §1–§4 | `AssetRoots` (`Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs`), `CuratedScenarios` (`Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/CuratedScenarios.cs`), `TerrainCatalog` / `TerrainAssets` (`FDP/Toolkits/Fdp.Toolkits/Terrain/`), the ~15 hard-coded sites in §0 |
| 2 | **CE-4094** HTTP while `Start` shows + **CE-4088** — ui's start gate waits on it | AQ108 §5 M7 | `PerspectiveScopedDispatcher.cs:89,104,108`, `DebugApiService.cs:709`, `DebugApiService.Perspectives.cs:79-111`, `PerspectiveCoordinatorSystem.cs:72-80` |
| 3 | **CE-4093** unsaved changes = the WHOLE WORLD compared ON DEMAND, no indicator — each node hashes a binary keyframe of its whole world against its own baseline; a check round only before a step that would lose edits; the noise test FIRST | AQ108 D20 (final, ninth pass), D22 | `WorldKeyframes.Capture` (`FDP/Toolkits/Fdp.Toolkits/Replay/WorldKeyframes.cs:94`), the master's round machinery (a query round, nothing staged) |
| 4 | **CE-4090** recording is a setting + the gates, and their HTTP 409s | AQ108 D9, D14, D15, §5a, D22 | the table in §5a names every site |
| 5 | **CE-4091** the recording keeps its scenario | AQ108 D7, §5 M2/M3 | `AssetInventoryProcessManager.SnapshotScenario`, `StorageGatewayModule.cs:854`, `ScenarioLoadStep.cs:178` |
| 6 | **CE-4092** start with a scenario; retire `--wait-for` | AQ108 D18, §5 M8 | `ClusterMaster.Bootstrap.cs` (CE-4087), `HrotRunnerConfiguration.cs` |
| 7 | **CE-4095** a new scenario from a checkpoint | AQ108 D8 | `ReferenceCheckpointHandler`, the scenario save extraction |
| 8 | **CE-4099** autosave | AQ108 D23 | `WorldKeyframes.Capture` / apply, `RecordingExportService` (the sandbox world), the scenario save |

⭐ Each task's **done when** is in the plan §2. Sonnet subagents are fine for the mechanical ones (CE-4089 above all) — brief them with the
done-when as success conditions, review their tests and diff (CLAUDE.md, *Sonnet delegation*).

⭐⭐ **Sonnet or your own model — see the plan §2a** (user, token thrift; a SUGGESTION): it marks each task, or each part of a task, as Sonnet-suitable or not. Brief Sonnet with the task's *done when* as success conditions; review its tests and diff, do not re-run them; verify any *"cannot / does not exist"* claim.

## Rules that bind every task

- **One implementation per concept** — every task above extends something that exists (named in the start column); a second copy is a defect.
- **Fold the as-built back into the owning design** before the task closes (obligation ⑤), and mark a superseded state SUPERSEDED.
- **Tests:** the feature's own suite first (T-1); each new rail red-proved by a compiling mutation; build the TEST project before `--no-build`.
- **Report per task** with the gate report contract (CLAUDE.md, Rule 8): verbatim commands and counts, reds confirmed pre-existing at the base
  sha, golden movement as a diff shape, the integration suite for anything cross-node (CE-4090, CE-4092, CE-4093), and the tracker ids you closed.
- Push after each task, so the ui lane can merge what it waits on.

_coordinator lane, 2026-10-28_
