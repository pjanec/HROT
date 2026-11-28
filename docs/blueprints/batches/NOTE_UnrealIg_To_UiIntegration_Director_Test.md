# Note: the director end to end on Unreal — results (CE-11064)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-27 |
| **For** | the HROT-integration session (and the gateway: [NOTE-2026-11-27-Unreal-Director-Film-Through-Client.md](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/clients/NOTE-2026-11-27-Unreal-Director-Film-Through-Client.md), gateway `c328fd6`) |
| **Answers** | [NOTE_UiIntegration_To_UnrealIg_Director_Test.md](NOTE_UiIntegration_To_UnrealIg_Director_Test.md) §4 |
| **User, verbatim** | *"send notes as well fix what oy can now while hrot lane will be fixind the gateway according to your note"* |
| **Build** | HROT-live at `unreal-ig` `a55b22cf4` (Release; no engine change since `7d4438d90`), editor WITH its window (`HROT_HIDDEN_WINDOW=1`: drawn off screen), gateway `5587295`; Unreal client 1.1 packaged; one seat, `renderRoot` set. Player and render client run one after the other (one Unreal process at a time) |

⭐ **No test passes yet.** The director itself runs every take to `Done` (`s1` = **12.367 s** every run), but neither path reaches Unreal end to end.

## 1. Per test

| | result | measured |
|---|---|---|
| **A — live** | ⛔ FAIL | `/director/play` → `Done`, `error` null, report ok. ⛔ The client got **0 `View` samples** (its own counter, whole run). `GET /view` sampled every 1 s during the take: `mode` **2D** 17×, **FPS** 9×, 3D 1× — the map was not in 3-D, so `CameraSuggestions.Write` (`MapViewModeLayer.cs:52`, *"while 3-D is on"*) never ran and the gateway had nothing to send (H-1) |
| **B — film through you** | ⛔ FAIL | run 1–2: 316 frames, all **HROT's** PNGs (`FILEIO … renderRoot/B/frames` in HROT's log), no `RenderJob` reached the client (H-3). Run 3 (after one scenario load first): the job reached the client, the director's load of the take then stalled 30 s at `LinkedClient` and the item failed (*"the scenario's load made no progress for 30 s (1829 polls, count 32, at: Editor (node 1): LinkedClient)"*) — gateway G-1; and the client dropped the job when its instance went away (ours, fixed — §3). `error`: *"the job asked for the client's sound (sound: client) but the linked client returned no track"* |
| **B′ — reference (`frames: map`)** | ✅ frames | **316 frames**, report ok, 0 late frames, s1 12.367. ⚠ no MP4: **ffmpeg is not installed** on this machine (the same `error` text as B, since `sound: client` was kept as the note said) |
| **C — stills** | ⛔ FAIL | `/director/stills` → `ok:false`: *"no stills: … anchor 's1': no such event in the recording: 0 shots by 'Tank'"* — run right after B/B′ (live runs) and after the reel; which recording it searched is not visible in the answer (H-2) |
| **D — reel, live** | ⛔ FAIL (as A) | both items `Done`/ok, s1 12.367 each; 0 `View` samples at the client |
| **D — reel, film** | ⛔ FAIL | 90 frames (the title card). The job reached the client; both items failed on the 30 s `LinkedClient` stall (G-1) |

Every report carries *"segment 2 goes back to 11.367 s: a live run cannot replay (R-3034) — skipped"* (11.367 = s1-1, the slow-motion key) (H-4).
The `View` rows of §1 of your note could not be sampled (no `View` was sent).

## 2. HROT-side findings

| | finding | ask |
|---|---|---|
| **H-1** ⭐ | A director run does not keep the map in 3-D, so a live director sends no `View`. `DirectorRunner` calls `MapBuild.Request3D()` at the load (`DirectorRunner.cs:636`, `:644`); measured in the editor with its window hidden, `GET /view` reports 2D / FPS through the take. ⭐ User: *"I need to test the director taking shots from unreal, not HROT 3d map"* — the client cannot take a shot HROT never states | the director's camera reaches `CameraSuggestion` whatever the map shows (or the director holds the map in 3-D for its run) |
| **H-2** | Stills (C) find no `s1` in "the recording" right after a live run of the same take that found it | which recording `/director/stills` reads; say it in the answer |
| **H-3** | `RoutingFrameSink.Begin` reads `CanFilm` once; it is false until the client has a ready world, so the first film job after start goes to the map with no word of it (gateway G-3) | the status / report names where the frames went |
| **H-4** | the take's segment 2 is skipped in every live run (R-3034) | whether the take should anchor segment 2 later, or a live run play it without the replay |
| **H-5** | `POST /film/shot {"lookAt":[300,400,0],"distance":200}` (no pitch) answered `pitchDeg: -1718.87` = -30 rad read as degrees | — |
| **H-6** | (Stechovice, earlier the same day) HROT crashed after ~23 min of live `showcases/stechovice-battle`: `InvalidOperationException: NetworkId 6263 already registered` at `NetworkEntityMap.Register` (`NetworkEntityMap.cs:125`) from `LightweightRoundIdentitySystem.Execute` (`:68`), unhandled ⇒ the process ended. Before it, `NavigationSolver` timed out (100 ms) and was circuit-broken repeatedly | — |

## 3. Fixed on our side

The client ended a render job when the job's instance went away (`HrotDpsRender.cpp` `OnJob`). The gateway rebuilds its writers at every world
boundary and writes the same job again (Running), so a director load inside a job killed it after 1–3 frames. ⭐ Now the job is held 10 s for its
JobId to come back; only Done / Failed / Cancelled end it. The rerun of B and D-film follows in the next note, once the gateway's G-1 is in.

Recipe (scratch, nothing in git): `run_dirA.ps1` (player client: A, D-live) and `run_dirB*.ps1` (render client `-HrotDpsRender -deterministicaudio`:
B, D-film, B′, C), HROT debug API 8111, `GET /director/status` every 1 s.
