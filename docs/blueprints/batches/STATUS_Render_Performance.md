<!--STATUS
state: LIVE — in progress (overnight, autonomous)
updated: 2026-11-14
current-answer: the whole file — DONE / GATES / DECISIONS / BUGS FIXED / OPEN at the top
related-designs:
  - ../../DESIGN_Map_3D_Mode.md — §6ak the design built here (CE-4554)
  - HANDOFF_Coordinator_To_Windows_Render_Performance.md — the handoff (dispatched c1a645d63, stamped 165e3398f)
  - STATUS_Windows_Core_Utilisation.md (exp/windows-threaded-measure) — §8 the earlier numbers
-->

# STATUS — render performance, overnight on the Windows machine (CE-4555 … CE-4562)

**Branch** `exp/render-perf` from `exp/threaded-build@b89ade339`, merged `coordinator@165e3398f` (the handoff and Map 3D §6ak). **Machine** Ryzen 7 6800H
(8 c / 16 t), Radeon 680M, Windows 11. Every measurement: Release, hidden window, uncapped (`--target-fps 0`), `scale-rig-scale-1-1000-2s` live and playing.

## DONE

| # | item | state |
|---|---|---|
| G-1 · CE-4555 | `LitInstanceBatch` + the instanced program in `LitShader` | ✅ `029427648` |
| G-2 · CE-4556 | the body layer fills the batch | ✅ `029427648` |
| G-3 · CE-4557 | the screen-size bands | ✅ `6aaf70d30` |
| G-4 · CE-4558 | the pick on input, through the drawn boxes | ✅ `6aaf70d30` |
| G-5 · CE-4559 | the 2-D terrain layer from a cache | ✅ `dd2a90ee2` |
| G-6 · CE-4560 | sound sources within hearing | ✅ `6aaf70d30` |
| G-7 · CE-4561 | the message log's visible rows | ✅ `6aaf70d30` |
| G-8 · CE-4562 | the proof on this machine | ⬜ |
| G-9 · CE-4562 | the as-built into Map 3D §6ak | ✅ (this commit) — §6ak BUILT, §6ak.1 as built, §6ak.6 the deviations and the measured result; People Display §7g |

## GATES

**Before** (this branch before any change, `rp.py`; three 2-s windows each):

| pose | frame mean ms | p95 ms | UI thread busy | GPU 3-D (this process) |
|---|---|---|---|---|
| 2-D map (the editor's start view) | 43 · 30 · 23 | 46 | 89 % | 10 % |
| street (`lookAt [2750,2750,1.7]`, 30 m, pitch −8) | 42 · 36 · 31 | 58 | 92 % | 15 % |
| whole terrain (centre, 7 500 m, pitch −60) | 91 · 91 · 88 | 108 | 94 % | 16 % |

The UI thread at the whole-terrain pose (10 s sampled): `EntityBodyLayer3D.Draw3D` 48 % (parts 32 %, doors 13 %), the staged pull 25 % (of it
`StatelessGizmoSystem` 17 %), the pick 8 %, sound 4.5 %, the terrain ≈ 3 %.

## GATES — ⭐ the final measurement (after G-1 … G-8, `fefaabc37`; Release, hidden, uncapped, `rp.py`, three 2-s windows)

| pose | before | after | target | UI thread | GPU 3-D | bodies |
|---|---|---|---|---|---|---|
| whole terrain | 91 · 91 · 88 ms | **21.8 · 22.4 · 22.3 ms** | ≤ 33 ms ✅ | 97 % | 52 % | 1 call · 29 far · 1 091 hidden · 6 911 doors hidden |
| street | 42 · 36 · 31 ms | **15.4 · 15.9 · 15.8 ms** | ≤ 16.7 ms ✅ | 97 % | 31 % | none in view at this pose |
| 2-D map | 43 · 30 · 23 ms | **12.4 · 12.5 · 12.1 ms** | — | — | 14 % | — |

| gate | result |
|---|---|
| the new rails (`BodyScaleFrameRail` 7, `AudioTests.CE4560_*`, `MessageLogCostRail.ASteadyLog_*`, `Map3DTests.CE1073_*`) | ✅ all green on this machine's GL (hidden window, NOT skipped); each red-proved by a compiling mutation (the batch's mapping, the pick walk, the search rate, the log's change check, the cache's transform) |
| `Hrot.Presentation.Tests` whole, Debug, `--no-build` | 24 failed / 2 116 passed / 11 skipped — ⭐ **0 new against the base** `d103a02be` (25 there: the same 24 + `CE1073`, fixed here). The 24 predate this branch (rules, debrief, storage, catalogue, director, the wire-sphere and demo-city frame rails …) |
| `Fdp.Presentation.Tests` whole | 5 failed / 836 — the same 5 at the base |
| `Map3DFrameRail` + `BodyScaleFrameRail` (frame rails, real GL) | 52 / 54 + 7 / 7 — the 2 reds are the base's (`CE4388_TheCachedWireSphere…`, `CE1227_V_DemoCity…`, no bodies drawn in either) |
| 0 B per steady frame (body layer + batch) | ✅ the rail; ⚠ once, in one whole-suite run, one frame of the 20 allocated 6 KB (passed in the next whole-suite run and every class run) — OPEN |
| the threaded 1 000-man session, `HROT_WORLD_GUARD=1` | see G-8 below |
| `tracker-counts.py --check` | ✅ |

## GATES — after G-1 … G-4 (before G-5 … G-7; `rp.py`, the same poses)

| pose | frame mean ms | p95 ms | UI thread busy | GPU 3-D (this process) | bodies |
|---|---|---|---|---|---|
| 2-D map | 23 · 23 · 23 | 41 | 97 % | 15 % | — |
| street | 20 · 20 · 20 | 35 | 98 % | 24 % | none in view at this pose |
| whole terrain | 32 · 35 · 35 | 64 | 98 % | 39 % | 1 draw call · 29 far · 1 091 hidden · 6 911 doors hidden |

The UI thread at the whole terrain then: the staged pull 23 % (`StatelessGizmoSystem` 14 %), the body layer 21 % (the hidden doors' leaf look-ups 11 % — fixed
after this run, A-9), sound 12 % (G-6), effects 11 %, ImGui 9 %, the message log 4.5 % (G-7), the terrain 3.5 %.

## DECISIONS

| # | problem | options | chosen | why | where |
|---|---|---|---|---|---|
| A-1 | where the instanced program lives (§6ak.1 names an `InstancedLitShader` class) | a separate class · a second program inside `LitShader` | **inside `LitShader`** (`Instanced`, compiled on first use) | it must get the SAME per-frame atmosphere and eye (`ApplyAtmosphere` / `ApplyFrame`) and the same fragment text; a separate class would need every setter mirrored | `LitShader.cs` |
| A-2 | the batch's name and home (§6ak.1 `BodyBatch`) | `BodyBatch` in the body layer's assembly · a generic batch beside `LitShader` | **`LitInstanceBatch`** in `Fdp.Presentation/Vis3D` | the GL plumbing knows nothing of bodies; any lit layer can use it | `LitInstanceBatch.cs` |
| A-3 | D-5 says one paint vec4; a face needs ~20 floats (tones, axes, look, mood) | a second draw path for faces · more per-instance data | **five data vec4s per instance**, the face axes from a table generated out of `BodySurface.AxesOf` | §6ak D-5's own fallback ("a second vec4"); faces stay in the one batch; the table is derived, never copied | `BodySurface.Glsl`, `LitShader.InstancedVertex` |
| A-4 | the instancing calls (`rlSetVertexAttributeDivisor`, `rlDraw…Instanced`) and per-instance colour + paint (`DrawMeshInstanced` takes transforms only) | `DrawMeshInstanced` · our own instance buffer through `Rlgl` | **our own buffer** with explicit attribute locations 6–15 | per-instance colour and paint; a persistent buffer grown, never shrunk (`DrawMeshInstanced` allocates a GPU buffer per call) | `LitInstanceBatch.Flush` |
| A-6 | §6ak D-2 "one box at the body's size" read thin at the Full / Far edge: the figure's arms and rifle stand out of the TKB size (measured: the box covered 0.45 of what the figure covered) | the TKB size · a fixed width · the figure's own bounds | **the bounds of the figure's own parts in that stance** (cached per stance), across × `FarFill` 0.5 — measured at the edge: area 0.94 of the expected, colour 19 levels apart | the box follows the model (crouched, prone, a dead man lying) without a second description of it | `EntityBodyLayer3D.FarBox` |
| A-7 | the Far colour of a camouflaged man (the torso's base colour read olive against a brown-looking figure) | the base · the mean of the figure's pixels | **the base mixed half and half with the mean of the three camouflage tones** | what the eye averages at a few pixels; no per-frame image work | `EntityBodyLayer3D.FarColour` |
| A-8 | what "height on screen" measures | the height · the largest extent | **the largest extent** (a 6 m car seen from above keeps its length); SUMO's ambient bodies take the same bands; the selected body and the operator's own are always Full (D-7) | a long low vehicle must not vanish before a man does | `EntityBodyLayer3D.BandFor` |
| A-9 | 6 911 hidden doors still cost 11 % of the whole-terrain frame (each leaf looked up through a managed key and a map before the size test hid it) | cache the leaves · a cheap pre-test | **a pre-test on the door entity's position with 3 m (more than any leaf)**: below 2 px it is hidden before any look-up; the exact leaf test stays for the rest. A door hidden for its size is not in the pick index (it was pickable through the 6-px margin; at < 2 px nobody aims at a door) | the cost is the look-up, not the draw | `EntityBodyLayer3D.Draw3D`, `DoorReachMetres` |
| A-10 | which pass records the pick boxes (a film draws the same layer with its own camera) | every pass · the window's | **only the window's pass** (`RenderContext.Film` false) | the pick is the window's | `EntityBodyLayer3D.Draw3D` |
| A-11 | D-6 "pick only when the mouse or the camera moved" vs the per-frame cache reset (`MapCamera3D.Update`: "a new frame: entities may have moved") and the station's crosshair (`PossessionStationTests.CE1158_*` change what is ahead without moving) | keep the reset · drop it everywhere · by camera mode | **the FREE camera keeps its picks across frames** (cleared by a pose change and by any mouse press or release, so a click is never stale); **a DRIVEN camera (first person, a follow, a film) re-picks every frame** as before | the hover is what D-6 measured; a station's prompt must follow what changes ahead of it at once; a click must never select from a stale hover | `MapCamera3D.Update`, `MapCanvas` input |
| A-12 | the camera cached ONE (pixel, filter): two callers asking for different pixels evicted each other, so the pick ran every frame anyway (measured: twice a frame in the rail) | one entry · a few | **four entries** | every caller of a still frame is answered from the cache | `MapCamera3D.Pick` |
| A-13 | G-6's mechanism ("only the sources within hearing, at the audio's own rate") | a spatial index · a rate · distance first | **search at 20 Hz** (`MoverSearchHz`), **distance first** against the widest range the type can have (a type's own, else a jet's 4 km) before any component, TKB or locomotion read; per-type acoustic cache; the held few (≤ 8) follow their source every frame between searches (pan, Doppler); a body first heard triggers a search next frame (so a newly moving source sounds at once) | the world changes slowly for the ear; the ear needs only the loudest 8 | `SoundSources.Movers` |
| A-14 | G-7 — the log was already virtualised (W-P1); the cost left was filtering every message and summing every row's height each frame | a source version · change detection in the panel | **the panel detects the change** (count, first and last entry, filter text, hidden severities, a hash of the hidden loggers) and keeps the filtered rows with their heights' running total; the visible band by binary search | `IMessageLogSource` has no version; a log only appends and trims | `MessageLogPanel.DrawMessageList` |
| A-15 | G-5's cache (the 2-D terrain was 21 % of the 2-D frame: 17 % its strokes and labels through the gizmo renderer, 4 % the cull) | cache the cull · a render texture | **a render texture of the plan**, re-rendered when the camera's offset / target / zoom, the framebuffer, the plan, the overlay or the damage changed, else ONE quad; colour blended as on screen and alpha as coverage (separate factors), composited premultiplied (a translucent fill blends once); a film's pass draws directly | the strokes are the cost, and a still map is the common case | `TerrainLayer2D.Draw` |
| A-16 | the window's 4× MSAA does not reach a render texture | a 2× supersample · the screen's size | **the screen's size** — measured: at 2× the plan's 1-px outlines (GL lines, one texel wide at any scale) came out at half strength; at 1× only a fill's edge lacks the MSAA smoothing (the rail compares within one pixel) | thin lines exact over smoother fill edges | `TerrainLayer2D.CacheSupersample` |
| A-5 | a driver that refuses the instanced program | draw nothing · plain colour · the old per-draw path | **the old per-draw path, paint included** (`BodySurface.Apply`) | the same picture on every GL; the rail compares the two paths | `LitInstanceBatch.FlushOneByOne` |

## BUGS FIXED

| bug | fix | rail |
|---|---|---|
| the body layer allocated ≈ 1.4 KB a steady frame (16 bodies): a capturing lambda per frame (`DrawCarried`'s `wreckOf`, `DrawRopes`' `RemoveAll`), a new rope query and a new `WorldQuery` view per frame, a boxed enumerator per head (`foreach` over `IReadOnlyList`), `ToList` in the motion tracker's sweep — and the new batch's `stackalloc` initializer (64 B) | each made allocation-free (`029427648`) | `BodyScaleFrameRail.CE4555_ASteadyFrame_AllocatesNothing` |
| `SoundSources.MovingDefault` built a new `AcousticSignatureDto` for every entity every frame; `Movers` built a new query every frame, sorted with a capturing lambda and listed the gone with `new List` | static defaults, a cached query, a static comparison, a reused list | `AudioTests.CE4560_MovingSources_AreSearchedAtTheAudiosRate_AndHeldEveryFrameBetween` |
| `Map3DTests.CE1073_EveryFigurePose_KeepsTheHead_AtHeadIndex` formatted with the machine's culture (`0,20` on cs-CZ — red at base, R-4221) | `InvariantCulture` | itself |
| (this run) `BodyScaleFrameRail.cs` was committed in `029427648` with its comments double-encoded (a PowerShell `Get-Content`/`Set-Content` round trip in the cs-CZ code page) | re-encoded | — |

## OPEN

| # | what | evidence | lean |
|---|---|---|---|
| O-1 | ⚠ **the editor crashes on SHUTDOWN** — `AccessViolationException` in `EntityRepository.SyncPresentationCore` (the sim worker's `StagingWorlds.Publish`), once in `NativeChunkTable<TkbIdentity>` | in the BASE run of this branch (before any change, `rp/base`), in last night's core-utilisation runs (`s1on`, `dcon`) and in this run's sessions — always after `/shutdown`, never during a step | pre-existing; a native-memory race between the sim worker's publish and the world's disposal — backend, a tracker row needs an id from the coordinator's block |
| O-2 | the steady-frame allocation rail allocated once (6 KB in one of 20 frames) in one whole-suite run | passed in the next whole-suite run and in every class run | a one-time growth inside `DrawParts` when other suites ran first — watch; not reproduced |
| O-3 | the Mermaid blocks of Map 3D §6ak.1 were not run through `scripts/mermaid-check.mjs` | no Node on this machine | the coordinator's VM, before merge |
| O-4 | what is left of the frame is spread thin | whole terrain: the staged pull (the gizmo system 8–14 %), the effects' sphere draws (`EffectLayer.Draw3D` 11–17 %, mostly native: a blend-mode flush per flash), ImGui ≈ 10 % | the next round, if the targets tighten |
| O-5 | a door hidden for its size (< 2 px) is not pickable (A-9) | — | nobody aims at a 2-px door; the free camera re-picks when it moves closer |
