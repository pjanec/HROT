# Note: the director, second rerun on Unreal — every test passes (CE-11064)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-27 |
| **For** | the HROT-integration session (and the gateway: [NOTE-2026-11-27-Unreal-Director-Rerun2.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/NOTE-2026-11-27-Unreal-Director-Rerun2.md)) |
| **Answers** | [NOTE_UiIntegration_To_UnrealIg_Director_Test_Rerun2.md](NOTE_UiIntegration_To_UnrealIg_Director_Test_Rerun2.md) §3 |
| **Build** | HROT-live at `unreal-ig` `9c38cb332` (Release), gateway `9ea780e` (T-086) packaged into it; Unreal client 1.1 packaged with `3c1ba8ad0` + `7687cf1d9` (below); one seat, `renderRoot` set; editor WITH its window (`HROT_HIDDEN_WINDOW=1`) |

⭐ **A, D-live, B, B′ and D-film pass.** C dropped as you said. G-4, G-5 and H-8 are fixed as measured below. Two defects of OUR client
showed up only now that the jobs reach it (§2), both fixed.

## 1. Per test

| | result | `framesTo` | frames | `error` | MP4 |
|---|---|---|---|---|---|
| **A — live** | ✅ | — | — | null; report ok, simEnd 22.383, no skipped replays | — |
| **D — reel, live** | ✅ | — | — | null; both items ok | — |
| **B — film through you**, the first job after start | ✅ | *"the linked client 'dps'"* | **765** (Unreal: 599 scenes, 45 silence, 121 repeat), report ok | null | ✅ `B/ua-tank-ammo-ig-director-check.mp4`, 9.1 MB, with the client's track |
| **B′ — reference (`frames: map`)** | ✅ | *"HROT's map (frames: map)"* | 765, report ok | null (H-8: HROT's sound, no "no track") | ✅ `B2/ua-tank-ammo-ig-director-check.mp4`, 2.1 MB |
| **D — reel, film** | ✅ | *"the linked client 'dps'"* | **1530** (1198 scenes, 90 silence, 242 repeat), both items ok | null | ✅ `Dfilm/ig-director-check.mp4`, 1.8 MB |

The client counted 806 `View` samples over A + D-live. G-5's re-send was seen and answered without a second render: B 1, D-film 4
(*"frame N repeated by the station - answered from the frame already presented"*). The track lengths match the frames exactly
(D-film 1530 frames × 48000 / 30 = 2448000 samples; written 2448256).

## 2. Fixed on our side (both seen in the first try of this rerun)

| | what failed | fix |
|---|---|---|
| **B** | ⛔ *"no FrameDone for frame 0 within 30 s"*: the job (now sent before any world, G-4 ✅) started on our start level; our client then loaded the terrain level, and the render client, a WORLD subsystem, died with the old world: the job dropped, its DDS readers gone through the 4.9 s blocking load, frame 0's request lost | `7687cf1d9`: the render client is a GAME-INSTANCE subsystem; the job and its readers live through the load; the per-world setup is applied again in the new world |
| **D-film** | ⛔ *"File '' is not a path under the render root"* at frame 256 — the take's slow-motion segment (sim 11.367): our client had no `FrameKindRepeat` and treated it as a scene | `7687cf1d9`: a Repeat is drawn as the client guide says (no image, one film frame of sound); a frame that owns no step skips the barrier |

`3c1ba8ad0`: a repeated (JobId, FrameIndex) is answered from the frame already presented (your §1, G-5).

## 3. One gateway finding, not blocking (G-6, in the gateway note)

From this build on, the step barrier counts **one world sample more than `ClockStep.Samples`** on most scene frames (B 376 of 599 frames,
D-film 777 of 1198; the excess is 1 in 1152 cases, 2 in one). Not before (`over 0` on the earlier runs). The client presents anyway, as the
guide says, so no frame waited; it is for the gateway to find which topic it writes and does not count.

Recipe (scratch, nothing in git): `run_dirA6.ps1` (player client: A, D-live), `run_dirB7.ps1` (render client: B first after start, D-film);
B′ from `run_dirB6.ps1`. HROT debug API 8111, `GET /director/status` every 1 s.
