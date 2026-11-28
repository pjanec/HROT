# Note: the director rerun on Unreal — live passes; film through the client still fails (CE-11064)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-27 |
| **For** | the HROT-integration session (and the gateway: [NOTE-2026-11-27-Unreal-Director-Rerun.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/NOTE-2026-11-27-Unreal-Director-Rerun.md)) |
| **Answers** | [NOTE_UiIntegration_To_UnrealIg_Director_Test_Rerun.md](NOTE_UiIntegration_To_UnrealIg_Director_Test_Rerun.md) §3 |
| **Build** | HROT-live at `unreal-ig` `65ad9c520` (Release), gateway `aa51c94` (T-085) packaged into it; Unreal client 1.1 packaged (with `e34edddbb`, the job held across a world boundary); one seat, `renderRoot` set; editor WITH its window (`HROT_HIDDEN_WINDOW=1`) |

⭐ **A and D-live pass. B′ passes. B, D-film and C do not**: B went to HROT's map (the gateway hears no render client before the first world),
D-film reached the client and stalled on frame 1 (a frame in flight across the gateway's writer rebuild is lost), C is refused on a live host.

## 1. Per test

| | result | measured |
|---|---|---|
| **A — live** | ✅ PASS | `Done`, report ok, 1344 steps, simEnd 22.383, segment 2 played (no skipped replays). The client counted **267 `View` samples** during the take (0 before). `/film/shot` with no pitch now answers `pitchDeg: -30` (CE-11065 ✅) |
| **D — reel, live** | ✅ PASS | both items `Done`/ok; the client's `View` count went 267 → **813** |
| **B — film through you** (the first job after start) | ⛔ FAIL | `framesTo`: *"HROT's map: the linked client 'dps' cannot film now"* — from the first status (phase `Loading`). 765 frames, all HROT's PNGs; `error`: *"…sound: client… the linked client returned no track"*. ⭐ Cause is in the gateway (its G-4): it reads `RenderClient` only while a world is loaded, and HROT had no world before this job's own load. Our client had announced itself 13 s before the job |
| **B′ — reference (`frames: map`)** | ✅ frames | `framesTo`: *"HROT's map (frames: map)"*, 765 frames, report ok. ⚠ no MP4 on disk, though `movie` names `renderRoot5/B2/ua-tank-ammo-ig-director-check.mp4` (H-8) |
| **D — reel, film** | ⛔ FAIL | `framesTo`: *"the linked client 'dps'"* ✅. ⭐ The 30 s `LinkedClient` hang is **gone** (G-1 ✅): the new world reached the client 0.3 s into the job, `ClientWorld` Ready at once, and our client kept the job across the writer rebuild (*"back after 0.29 s - the job goes on"*). ⛔ Then: *"capture failed: no FrameDone for frame 1 within 30 s"*. The client had answered 2 frames (both silence, the card) and was never asked for another — gateway G-5 |
| **C — stills** | ⛔ REFUSED | `/director/stills` (the note's command) → `ok:false`: *"stills need a source that seeks (a recording): this host plays a live run … open the recording in the Replay Browser (POST /director/stills {recording})"* (H-7) |

## 2. HROT-side findings

| | finding | ask |
|---|---|---|
| **H-7** | test C's command cannot run on the editor host now: stills want a recording, and a director take run leaves `recording: null` in its report | which recording C should name (a take run that records, or a bake), and the command for it |
| **H-8** | B and B′ report `movie: …mp4` with no file on disk (ffmpeg is installed and on PATH); both also carry the *"no track"* error, since the note keeps `sound: client` for a map job | `movie` only when the file was written; and whether a map job with `sound: client` should fail the movie or fall back to HROT's sound |
| **H-3 ✅** | `framesTo` says it plainly — that is exactly what we needed | — |

## 3. Not done on our side yet

A repeated `FrameRequest` (the same JobId and index — what a gateway re-send after a rebuild, G-5, would look like) would be rendered
twice by our client. We will make it answer a repeat from the frame it already presented once the gateway says how it re-sends.

Recipe (scratch, nothing in git): `run_dirA5.ps1` (player client: A, D-live) then `run_dirB5.ps1` (render client: B first after start,
D-film, B′, C), HROT debug API 8111, `GET /director/status` every 1 s. `ddsmonitor` still writes an empty file when stopped, so the wire
was read from the two logs and the gateway's code.
