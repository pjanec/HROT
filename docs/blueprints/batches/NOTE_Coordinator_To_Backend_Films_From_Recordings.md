<!--STATUS
state: LIVE — coordinator → backend: P0 of "films from recordings" (CE-4058) — make a recording a good film source
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Film_Production.md — THE intent (R-4091); §6 the measurement this note rests on
  - ../../DESIGN_Director_Timeline_Editor.md — §11 the build plan: P0 is yours; TE-12 / TE-13 the design of the TKB load and the kept exercise
  - NOTE_Coordinator_To_Ui_Films_From_Recordings.md — the ui half, sent the same day
-->
# NOTE — coordinator → backend: make a recording a good film source (CE-4058)

**Dispatched at** the commit that adds this file (`coordinator`). Your scope is frozen there; documents changed after it are FYI only. This is
a FRAME: design the details as step 1, in the owning designs (TE-12 / TE-13 of Director Timeline), and fold the as-built back.

## The goal

🔒 **User, `2026-10-27` (R-4091):** films — feature demonstrations and the overnight documentation of the regression scenarios — are cut and
rendered from **recordings**, adjusted in the Replay Browser by seeking instead of re-running. [Film Production](../../DESIGN_Film_Production.md)
is the intent; [Director Timeline §11](../../DESIGN_Director_Timeline_Editor.md) the plan. ⭐ **P0 is yours and nothing in the Replay Browser
part can be judged until it lands.**

## What I measured first ([Film Production §6](../../DESIGN_Film_Production.md), `ua-tank-ammo`, live editor vs its recording in the RB, same cameras)

| | result |
|---|---|
| seeks by `atSimSeconds` | ✅ 7 of 7 land on their sim time |
| units' places at a moment | ✅ the same |
| effects (muzzle glow, round, tracer, blast) | ✅ the same — they come from the recorded events |
| ⛔ **bodies** | **every vehicle a plain box** — no hull, turret, tracks, gun, crew. The RB sets no TKB (`ReplayBrowserSubsystem.DebugSurface.cs:51`; `MapInteractionPack.cs:508` reads the world's singleton) |
| frame cost (step + capture, 1280×720) | RB 0.26 s · live editor 0.32 s |
| ⛔ the recording's scenario | not recorded: `RecordingMetadata` has no scenario field; this run's `Description` and `CustomTags` are empty |
| ⚠ size | 14 268 frames / 75 MB for 41 s of sim — the run was paused between HTTP steps and the recorder wrote frames while paused |

## What to build — the items, my leans

| # | item | lean | accepted when |
|---|---|---|---|
| ① ⭐ first | **the Replay Browser loads the TKB** (R-4069, TE-12) | `HrotEnvironment.CreateTkb()` + the same knowledge-base load step every node runs, set as the world singleton in `PrepareRepo` (the choke point every RB repository passes); from the recording's scenario (②) when known, else the built-in catalogue. ⛔ No RB-only loader (CE-4013 D4/D5 removed four) | the still pairs of Film Production §6 show typed bodies in the replay |
| ② | **a recording names its scenario** | the scenario name + its hash (the director's `TakeStore.ScenarioHash` rule) in the meta — a field, not `CustomTags` | the meta of an editor live run and of a verify run carry both |
| ③ | **"Keep for films"** (TE-13) | copy `exercises/<id>/` (the `.fdp`s, meta, the timeline sidecar) **with the scenario as it was** out of the temp root into the exercise archive; the takes of that recording will live in its `takes/` (ui, CE-1303) | a kept editor recording opens in the RB after an editor relaunch |
| ④ | **CE-4050** — a type from an assembly the process never loaded stops playback | yours to design; the RB and the index builder fail the same way | an editor recording plays in the RB to its end |
| ⑤ | **frames recorded while paused** | rule it: needed for the Wall axis (keep), or written once per pause (smaller) — a kept film recording's size depends on it | the rule written in DAR, and the measured size |
| ⑥ | **re-measure after ①**: people (posture, animation), wrecks, deaths, doors, sound | the same method as §6 — I can run it again if you prefer; the script is the shape of Film Production §3's agent steps | a pair per item in Film Production §6 |

## Gates you report (rule 8)
The suites of the projects you touch, `--no-build` column, every red named with its base sha; the RB and the timeline index suites
(`Hrot.ReplayBrowser.Tests`, `TimelineIndexTests`) by name. ⭐ Item ① is proven by a still pair, not only a unit rail.

_coordinator lane, 2026-10-27_
