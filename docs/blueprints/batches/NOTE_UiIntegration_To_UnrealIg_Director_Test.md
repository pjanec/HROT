# Note: test HROT's film director end to end on Unreal — live and to film (CE-11064)

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **User, verbatim** | *"i would like to test the director with the unreal. could you please write a note to the unreal to run a test proving all the director features work? pls prepare some test run for them"* |
| **Why** | Each piece has been tested on its own: your render client against the plugin's fake station (`hrot_dps_render7.py`), and the gateway against HROT's real controller with a fake client. The whole chain has never run: [DD-022](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-022-Dps-Director-Rendering.md) §9 says *"Not done here: a live run (HROT editor, a real director, the Unreal render client)"*. This test is that run. |
| **Prepared** | the take [`scenarios/demos/weapons-and-fires/ua-tank-ammo/takes/ig-director-check.take.json`](../../../scenarios/demos/weapons-and-fires/ua-tank-ammo/takes/ig-director-check.take.json) and the reel [`films/ig-director-check.reel.json`](../../../films/ig-director-check.reel.json). Terrain `basic-desert` (you have it: `fl-napalm-strike`) |

## 0. Setup — as in your linked-load runs

- HROT editor, `unreal-ig` at this note's commit or later, **with a window** (not `--headless`). The director writes the camera only while the 3-D map is
  shown, and a capture needs a renderer. Use the gateway extension with `dps.renderSeat` and `dps.renderRoot` set.
- Two Unreal processes: your **player client** for tests A and D-live, and your **render client** (`-HrotDpsRender -HrotDpsRenderRoot=<the gateway's renderRoot>`)
  for tests B, C and D-film. They may run together on different seats.
- Every `/director/play` LOADS the scenario again, through the linked-client load step, so your `ClientWorld` gates each run.
- ⭐ All times below are relative to **`s1`, the M1's first shot**. It was 12.37 s sim time in our run, but runs differ. Read this run's value from
  `GET /director/status` → `reports[0].found.s1`. The take starts at `s1-8` and ends at `s1+10`.

## 1. What the take does, and what you should see

| sim time | the director | on `View` (live) | in `FrameRequest` (film) |
|---|---|---|---|
| s1-8 | a fixed camera: eye (300, 200, 40), looking at (300, 450, 0), FoV 50 · subtitle **bottom**, fading in over 0.5 s | `Source` = `ViewSourceDirector` · `CutSeq` +1 · `Position` / `Orientation` / `FovDeg` 50 · `Subtitle`, `SubtitlePositionBottom`, `SubtitleAlpha` 0 → 1 | the same pose, subtitle and alpha per frame |
| s1-6 | a 2 s **fly** (ease in-out) into an **orbit** round (300, 400, 0), 260 m out, pitch -30 | no new `CutSeq` during the fly (blend); then `AngularVelocity` ≠ 0 while it orbits | the pose moves frame by frame |
| s1-3 | **follow the M1** ("Tank"), 25 m · subtitle **top** · graphics: a blue **ring** on the M1, a red **arrow** to the T-72 | `CutSeq` +1 · `TargetId` = the M1's id, `Distance` 25 (which `ViewMode` member a director follow maps to is not checked — record it) | `Graphics`: Ring + Arrow, world points resolved each frame |
| s1-1 | **over the M1's shoulder** toward the T-72, FoV 30 · **slow motion ×0.25** until s1+1 · subtitle **middle** (`SubtitlePositionCentre`) · a **still** key | `CutSeq` +1 · `FovDeg` 30 · the clock runs at a quarter (`SimTime` +0.25 s per wall second) · ⚠ HROT plays its own sound at that rate; whether the client is told the rate is not checked | `FrameKindRepeat` frames appear (the same scene again; advance your track one frame, draw nothing) |
| s1 | the M1 fires its first round — heard and seen in slow motion | `Fire`, the round, the impact as usual | your track has the shot |
| s1+1 | a **dip to black**: 0.5 s out, the cut at black, 0.5 s in · **follow the T-72** at 30 m · a white **label** "T-72" | `Fade` 0 → 1 → 0 · `CutSeq` +1 at black · `TargetId` = the T-72's id | `Fade` per frame · `Graphics`: Label |
| s1+6 | a 1.5 s **linear fly** to a wide view: eye (380, 330, 25), FoV 70 | `FovRate` ≠ 0 during the fly | — |
| s1+10 | the end | `Source` goes back from Director | the job ends: `RenderJob` Done, then your `RenderJobResult` |

## 2. The tests

```bash
c() { curl -s -X POST "http://localhost:<debug port>$1" -H 'Content-Type: application/json' -d "$2"; echo; }
```

| | do | pass when |
|---|---|---|
| **A — live** | `c /director/play '{"scenario":"ua-tank-ammo","take":"ig-director-check"}'`, then watch your player client; `GET /director/status` until `Done` | every row of the `View` column above, in order. ⭐ Record `View` samples (`ddsmonitor`, or your own log) so the check is the samples, not your eye |
| **B — film through you** | `c /director/play '{"scenario":"ua-tank-ammo","take":"ig-director-check","capture":{"dir":"<dir>","fps":30,"size":"1280x720","frames":"auto","sound":"client"}}'` | `GET /director/status` ends `Done` with `error` null and an MP4. ⭐ **Your PNGs** are in it (not HROT's map), every frame index reached you in order (Scene / Repeat), and HROT accepted your track (it fails the job if the sample total misses `round(frames × 48000 / 30)` by more than 1024) |
| **B′ — reference** | the same with `"frames":"map"` | the frame count equals B's. ⚠ The sim times come from the run, so compare counts and cues, not pixels |
| **C — stills** | `c /director/stills '{"scenario":"ua-tank-ammo","take":"ig-director-check","everyKey":true}'` | one PNG per camera key plus `contact-sheet.png` (the default folder is beside the take, `takes/ig-director-check.stills/`). ⭐ Say whether the stills came from you or from HROT's map. HROT opens its own still job for them; we have not run that with a linked client |
| **D — the reel** | live: `c /director/play '{"reel":"ig-director-check"}'` · film: the same with B's `capture` | part 1, a **title card** "Part 2", then part 2. ⭐ On film, the card is HROT's own picture and you get `FrameKindSilence` frames for it, with silence in your track |

## 3. Known gaps — do not count them as failures, but say what you saw

- **The take's sound on/off switch is not on the wire** (a take may say `"sound": false`). This take keeps sound on, so nothing depends on it.
- **The live slow-motion sound rate:** HROT plays its own sound at ×0.25 in the slow stretch. We have not checked whether the gateway tells you the rate.
- **Your render client has no slow motion or reel-boundary fade in its track yet** (your round-7 note). B and D-film may therefore have the shot's sound at
  normal speed, and an abrupt cut at the card. Note it; HROT does not detect it.
- `Graphics` and `LayersOn` / `LayersOff` exist on the film path only. Live `View` has no graphics, and this take sets no layers.

## 4. Report back

A `NOTE_UnrealIg_To_UiIntegration_Director_Test.md` with, per test: pass or fail, the measured `s1`, the frame counts (B, B′), the job's `error` if
any, and for A the `View` samples at each row's time (`CutSeq`, `TargetId`, `Fade`, `SubtitlePosition`, `SubtitleAlpha`, `FovDeg`). A screenshot per
row in chat is welcome; ⛔ none in git.
