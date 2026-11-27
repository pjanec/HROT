# Note: the director test — what your first run found is fixed; please rerun (CE-11064)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-27 |
| **For** | `unreal-ig` |
| **Answers** | [NOTE_UnrealIg_To_UiIntegration_Director_Test.md](NOTE_UnrealIg_To_UiIntegration_Director_Test.md) (`edd5a8dbc`) and gateway [NOTE-2026-11-27-Unreal-Director-Film-Through-Client.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/NOTE-2026-11-27-Unreal-Director-Film-Through-Client.md) |
| **Rerun on** | HROT `unreal-ig` at this note's commit or later; gateway `ccr-458b1360-afzl8f` with T-085 (package it again) |

## 1. What was wrong and what changed

| yours | cause, measured | now |
|---|---|---|
| **H-1** no `View` live | two things. ① **Our take:** every key was anchored on the M1's first shot (a live event), so with no bake the director held no shot until the shot happened. Reproduced here: `GET /view` 2D until 12.4 s. ② **HROT, the main one:** on a threaded node (the default) the map writes the suggested camera on the presentation's world, while the gateway reads the node's world, so it never saw a camera | ① the take now uses **fixed sim times** (the M1 fires at 12.367 s in every editor run, as you measured). ② CE-11066: the camera and the subtitle text now reach the node's world, so the gateway sees them |
| **H-4** segment 2 skipped | the same anchors: segment 2 starts 1 s before the shot, known only once the shot had happened | fixed by the fixed times. Reproduced here: segment 2 now plays live |
| **H-3** / **G-3** first job went to HROT's map | the gateway counted a render client only once it had a world | gateway T-085: a live render client can film. CE-11067: `GET /director/status` (and the stills' status) now has **`framesTo`**, saying where the frames go and why (e.g. *"HROT's map: the linked client 'dps' cannot film now"*) |
| **G-1** 30 s hang on a load inside a job | the gateway held back the new world's `Session` until a step, and HROT steps only after your `ClientWorld` Ready | gateway T-085: `Session` is never held back, also in a job, and is not counted in `ClockStep.Samples` |
| **H-2** stills: no `s1` "in the recording" | stills on the editor resolve an event anchor from the shown timeline index (the last run's), not from a run of their own | the fixed-time take has no anchors, so C needs none. The mechanism stays as it is |
| **H-5** `/film/shot` pitch -1718.87° | the default pitch in degrees was used as radians | CE-11065 |
| **H-6** the crash after 23 min of Stechovice | a round got an id the map still held for another entity: the id allocator handed it out again | made non-fatal: such a round takes the next id, and the log says so once (`[LightweightRoundIdentity] CE-5061`). The reissue itself stays open (CE-5061) |

## 2. The times, now fixed

The table in §1 of the first note still holds, with **s1 = 12.367 s**: the keys at 4.367 · 6.367 · 9.367 · 11.367 · 13.367 · 18.367, slow
motion 11.367–13.367, and the end at 22.367. ⚠ The take has no anchors now, so `reports[0].found` stays empty. That is expected.

## 3. Rerun

The same tests A, B, B′, C and D-live / D-film as in the first note, with the same commands. ⭐ For B, D-film and C, report `framesTo`.
A `"frames":"auto"` job right after start should now go to you; if it does not, `framesTo` says why.
