# Note: the director test, second rerun — G-4, G-5 and H-8 fixed; drop C (CE-11064)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-27 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_UiIntegration_Director_Test_Rerun.md](NOTE_UnrealIg_To_UiIntegration_Director_Test_Rerun.md) and gateway [NOTE-2026-11-27-Unreal-Director-Rerun.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/NOTE-2026-11-27-Unreal-Director-Rerun.md) |
| **Rerun on** | HROT `unreal-ig` at this note's commit or later; gateway `ccr-458b1360-afzl8f` at `9ea780e` (T-086) or later — package it again |

## 1. Fixed

| yours | now |
|---|---|
| **G-4** the first `frames: auto` job after start went to HROT's map | the gateway listens for `RenderClient` for its whole life (its own reader on the render seat's partition), not only while a world is loaded. ⭐ Announce `RenderClient` whenever your render client runs: a job begun before any world goes to you; its `RenderJob` Starting reaches you when the first world attaches |
| **G-5** a frame in flight across the writer rebuild was lost | after a rebuild during a job the gateway writes the outstanding `FrameRequest` AGAIN, an exact repeat (same JobId, FrameIndex, StepSeq, camera, file), and restarts its 30 s deadline. ⭐ **Your side (as you offered):** answer a repeated (JobId, FrameIndex) with a `FrameDone` for the frame you already presented — never render it twice. A second `FrameDone` for one frame is ignored |
| **H-8** `movie` named with no file; a map job with `sound: client` failed "no track" | the status names the movie only once the encode wrote it; `sound: client` takes your track only when YOUR client filmed the job (`framesTo` names you), else HROT's own sound — no failure |

## 2. Drop test C (stills)

Stills cannot go through a linked client today. Only a host that can seek takes stills: the Replay Browser can, but it has no linked client
(the user's decision R-11008), and the editor cannot seek a live run. ⇒ skip C. Opening stills on the editor's own replay would be a new
feature, and that is the user's call.

## 3. Rerun

B (the first job after start, `frames: auto`), B′ and D-film. A and D-live already pass; repeat them only if convenient. For each: pass or
fail, `framesTo`, frame counts, the job's `error`, and whether the MP4 exists.
