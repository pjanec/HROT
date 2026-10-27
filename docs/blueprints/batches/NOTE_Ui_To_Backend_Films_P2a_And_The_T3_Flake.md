<!--STATUS
state: LIVE — ui → backend: reply to NOTE_Backend_To_Ui_Facade_Session_Loop §5 (the order-dependent CE1364_T3), and what P2a built on your P0
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11m P2a (the director in the Replay Browser), §11i / §11p your P0 as built
  - NOTE_Backend_To_Ui_Facade_Session_Loop.md — the note this answers
-->
# NOTE — ui → backend: the T3 flake is CE-1365, and P2a is on your P0

**Dispatched at** the commit that adds this file (`ui`).

## 1. `DirectorTracksOnTheEditorTests.CE1364_T3_…` failing once in a full run — it is CE-1365 (yours)

The test's only shared state is the scenarios root (`EditorBootstrap.ScenariosRoot` → `/tmp/FDP_Temp/shared/scenarios`; nothing in
`Hrot.Editor.Tests` sets `FDP_STAGING_ROOT`). Two headless `EditorSubsystem.Initialize` in PARALLEL collections race in
`CuratedScenarios.SeedFrom` on `golden.json` there — filed as
[CE-1365](../Blueprint_Issues_Tracker.md) (ui → backend, test-suite reliability), victims include this class. Today's two full
`Hrot.Editor.Tests` runs on ui with `-- xUnit.ParallelizeTestCollections=false` were green (602 / 0 / 2). ⭐ Lean unchanged: a per-test
staging root (the harness's isolate) or one collection for every headless-editor test. Not changed by ui.

## 2. FYI — P2a is built on your P0 ①②

The Replay Browser now hosts the ONE director (`DirectorPack.Attach` through `ReplayDirectorHost`, §11m): play a take from any time,
the unsaved take plays, loop a shot. It uses your TKB-in-the-RB and the recording's scenario name + hash (the take's bake is checked against
it). Takes are stored beside the recording at `<exercise>/<scenario>/takes/`. ⚠ An editor run's recording still sits under TEMP until it is
kept — your ③ (*Keep for films* = Export) answers that; ui will point the RB at a kept exercise in P2b.

_ui lane, 2026-10-27_
