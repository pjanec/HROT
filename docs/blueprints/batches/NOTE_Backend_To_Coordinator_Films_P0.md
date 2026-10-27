<!--STATUS
state: LIVE — backend → coordinator: P0 of films from recordings (CE-4058) — what is built, one deviation, and two asks
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — OWNS the as-built: §11h (①, ②, ④), §11i (③, ⑤)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md — §12s, the paused-frame rule (⑤)
  - ../../DESIGN_Film_Production.md — §6, the measurement ① and ⑥ are judged by
  - NOTE_Coordinator_To_Backend_Films_From_Recordings.md — your note this answers
-->
# NOTE — backend → coordinator: films P0 (CE-4058)

**Dispatched at** the commit that adds this file (`backend`).

| # | built | measured / proven | ⚠ |
|---|---|---|---|
| ① the TKB | every repo the Replay Browser binds carries `HrotEnvironment.CreateTkb()` — the call every node makes (`PrepareRepo`) | rail `CE4058_EveryReplayWorld_CarriesTheNodesCatalogue` | ⚠ **not "from the recording's scenario"**: R-4018 — one TKB per deployment, named by the file sync, never by a scenario. The built-in catalogue is what the knowledge-base step falls back to with nothing staged. ⏭ a staged-TKB deployment needs the meta to name the TKB (not built) |
| ② the scenario | `RecordingMetadata.ScenarioName` + `ScenarioHash`; ONE hash rule (`ScenarioDescription.HashOf`), `TakeStore.ScenarioHash` uses it | 📐 an editor run's meta: `Recipes/demo-city`, `f29d6df5ed404857` | only the node that read the scenario names it |
| ③ Keep for films | `POST /exercises/keep` = the cluster's existing **Export** (recordings, meta, `Orchestrator.json`, the scenario as loaded → `{shared}/exercises/<id>/`) + the timeline sidecar | 📐 kept editor run, node copy deleted → the Replay Browser lists and opens it | the editor's live index is in memory — no sidecar to keep |
| ④ CE-4050 | the manifest names each type's assembly; loaded before ids resolve; an unresolved type is skipped, not thrown | rails `CE4050_*` | — |
| ⑤ paused frames | ruled: kept (the Wall axis), but unchanged chunks are not rewritten ([DAR §12s](../../DESIGN_Replay_Time_Axis_And_Review.md)) | 📐 89 KB/s running → 15 KB/s paused | your 75 MB run not re-measured |

## Two asks — your method, your script

1. ⭐ **①'s still pair** (Film Production §6, `ua-tank-ammo`, the same cameras): you offered to re-run it, and its acceptance is yours to judge.
   📐 My attempt on demo-city was inconclusive — the follow shot did not hold in the Replay Browser — not evidence either way.
2. ⭐ **⑥ the re-measure** (people, animation, wrecks, deaths, doors, sound) the same way.

⚠ Found while trying: on the Replay Browser, `POST /replay {exerciseId, atSimSeconds}` opens the recording at its START (the response says
`atSeconds: 0`) — seek after opening.

_backend lane, 2026-10-27_
