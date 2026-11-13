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
| G-3 · CE-4557 | the screen-size bands | ⬜ |
| G-4 · CE-4558 | the pick on input, through the drawn boxes | ⬜ |
| G-5 · CE-4559 | the 2-D terrain layer from a cache | ⬜ |
| G-6 · CE-4560 | sound sources within hearing | ⬜ |
| G-7 · CE-4561 | the message log's visible rows | ⬜ |
| G-8 · CE-4562 | the proof on this machine | ⬜ |
| G-9 · CE-4562 | the as-built into Map 3D §6ak | ⬜ |

## GATES

**Before** (this branch before any change, `rp.py`; three 2-s windows each):

| pose | frame mean ms | p95 ms | UI thread busy | GPU 3-D (this process) |
|---|---|---|---|---|
| 2-D map (the editor's start view) | 43 · 30 · 23 | 46 | 89 % | 10 % |
| street (`lookAt [2750,2750,1.7]`, 30 m, pitch −8) | 42 · 36 · 31 | 58 | 92 % | 15 % |
| whole terrain (centre, 7 500 m, pitch −60) | 91 · 91 · 88 | 108 | 94 % | 16 % |

The UI thread at the whole-terrain pose (10 s sampled): `EntityBodyLayer3D.Draw3D` 48 % (parts 32 %, doors 13 %), the staged pull 25 % (of it
`StatelessGizmoSystem` 17 %), the pick 8 %, sound 4.5 %, the terrain ≈ 3 %.

## DECISIONS

| # | problem | options | chosen | why | where |
|---|---|---|---|---|---|
| A-1 | where the instanced program lives (§6ak.1 names an `InstancedLitShader` class) | a separate class · a second program inside `LitShader` | **inside `LitShader`** (`Instanced`, compiled on first use) | it must get the SAME per-frame atmosphere and eye (`ApplyAtmosphere` / `ApplyFrame`) and the same fragment text; a separate class would need every setter mirrored | `LitShader.cs` |
| A-2 | the batch's name and home (§6ak.1 `BodyBatch`) | `BodyBatch` in the body layer's assembly · a generic batch beside `LitShader` | **`LitInstanceBatch`** in `Fdp.Presentation/Vis3D` | the GL plumbing knows nothing of bodies; any lit layer can use it | `LitInstanceBatch.cs` |
| A-3 | D-5 says one paint vec4; a face needs ~20 floats (tones, axes, look, mood) | a second draw path for faces · more per-instance data | **five data vec4s per instance**, the face axes from a table generated out of `BodySurface.AxesOf` | §6ak D-5's own fallback ("a second vec4"); faces stay in the one batch; the table is derived, never copied | `BodySurface.Glsl`, `LitShader.InstancedVertex` |
| A-4 | the instancing calls (`rlSetVertexAttributeDivisor`, `rlDraw…Instanced`) and per-instance colour + paint (`DrawMeshInstanced` takes transforms only) | `DrawMeshInstanced` · our own instance buffer through `Rlgl` | **our own buffer** with explicit attribute locations 6–15 | per-instance colour and paint; a persistent buffer grown, never shrunk (`DrawMeshInstanced` allocates a GPU buffer per call) | `LitInstanceBatch.Flush` |
| A-5 | a driver that refuses the instanced program | draw nothing · plain colour · the old per-draw path | **the old per-draw path, paint included** (`BodySurface.Apply`) | the same picture on every GL; the rail compares the two paths | `LitInstanceBatch.FlushOneByOne` |

## BUGS FIXED

| bug | fix | rail |
|---|---|---|
| the body layer allocated ≈ 1.4 KB a steady frame (16 bodies): a capturing lambda per frame (`DrawCarried`'s `wreckOf`, `DrawRopes`' `RemoveAll`), a new rope query and a new `WorldQuery` view per frame, a boxed enumerator per head (`foreach` over `IReadOnlyList`), `ToList` in the motion tracker's sweep — and the new batch's `stackalloc` initializer (64 B) | each made allocation-free (`029427648`) | `BodyScaleFrameRail.CE4555_ASteadyFrame_AllocatesNothing` |
| (this run) `BodyScaleFrameRail.cs` was committed in `029427648` with its comments double-encoded (a PowerShell `Get-Content`/`Set-Content` round trip in the cs-CZ code page) | re-encoded | — |

## OPEN
