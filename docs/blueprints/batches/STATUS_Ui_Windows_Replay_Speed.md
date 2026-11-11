<!--STATUS
state: LIVE — in progress
updated: 2026-11-11
current-answer: §1 the numbers (W-1 / W-2) · §2 what is under budget · §3 fixes (W-3) · §4 CE-4407 (W-4)
related-designs:
  - ../../DESIGN_Replay_Playback_Performance.md — OWNS the replay speed work; §3a carries the "measured on Windows" rows from §1 here
  - ../../DESIGN_Change_Gated_Systems.md — CE-1480, the change gate the replay frame rides
  - HANDOFF_Coordinator_To_Ui_Windows_Replay_Speed.md — the handoff (dispatched at d493c195f)
  - STATUS_Ui_Windows_Render_Under_Load.md — the same machine, live; the static 3-D costs (bodies, effects) are that work's
-->

# STATUS — replay speed on the user's Windows machine (CE-4432, CE-1478, CE-4407)

Started `66b5620a3` (merged coordinator, backend, ui at `15f07617d`); re-merged `origin/ui` (terrain authoring, CE-1481…1487) before measuring.
Every run hidden (`HROT_HIDDEN_WINDOW=1`): frame times are the off-screen render target's — GPU-honest, no vsync / compositor; uncapped (`--target-fps 0`), Release.

**The recording:** backend's rig regenerated (`scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10`), opened LIVE in the hidden
editor, played ~3 min (sim 181 s), kept → exercise `06ea3c8d-…` (format v8, ~8 900 entities, 209 s of wall time; a typical delta 2–4 MB raw, the
contact frames 23–73 MB). ⚠ A first recording made before the `origin/ui` merge became unreadable after it — `TerrainFeature`'s layout changed and the
format's schema check is exact (DES-008) — and that is how CE-1950 / CE-1951 were found (§3). No recording older than v8 existed on this machine.

**Method** (scratchpad script, not committed): `POST /replay {exerciseId}` — the same intent the start screen's *Open recording* publishes
(`RecordingPickerLauncher`: "the ONE construction of the replay intent that `POST /replay {exerciseId}` … also end[s] in"); per view a 15 s paused
segment, then `/sim/play` for 30 s (60 s at 3-D mid) from 20 % of the recording, `HROT_FRAME_STATS=1` + `HROT_REPLAY_STATS=1`; then seeks to 25 / 75 /
99.9 % timed until the status settles (0.2 s polling ⇒ ±0.4 s), and a one-frame step (`/replay/seek {frames:1}`). Views: 3-D near (150 m), mid
(900 m), far (5 km), 2-D whole terrain (7 km).

## 1. The numbers (W-1 the editor, W-2 the Replay Browser)

| | editor (W-1) | Replay Browser (W-2) |
|---|---|---|
| open → replaying | 1.6 s | 4.6 s |
| 1× playback keeps pace | ✅ 30.8 s recorded in 31.1 s wall (every view) | ✅ same |
| frames applied | ~7.3 / s (the recording's rate), 100 % prefetched once playing | same |
| apply (read → repair) | median 2.5 ms, max 25 ms; repair 0.7 ms | median 3.0 ms; repair 0.9 ms |
| garbage per apply | ⛔ **2.0 MB** | ⛔ **2.1 MB** |
| seek 25 / 75 / 99.9 % | 1.2 / 1.4 / 1.2 s | 1.4 / 1.3 / 1.5 s |
| step one frame | (not measured — `/sim/step` does not step a replay; the script then used `/replay/seek {frames:1}`) | 1.25 s (incl. ±0.4 s polling) |

**Frame (mean ms, fps) while playing 1× · paused at the same view:**

| view | editor playing | editor paused | Browser playing | Browser paused | the biggest costs (playing) |
|---|---|---|---|---|---|
| 3-D near 150 m | 28.5–31.2 (**~33 fps**) | 26.9 | 22.6–23.1 (**~44 fps**) | 21.2 | gizmos 4–5 ms · update 13 (editor) / 7 (Browser) |
| 3-D mid 900 m | 29.6–31.6 (**~33 fps**) | 28.1 | 26.2–27.6 (**~37 fps**) | 25.0 | gizmos 4–5 · update 13–15 / 8 |
| 3-D far 5 km | 61.0–64.4 (⛔ **~16 fps**) | 52.3 | 64.1–65.2 (⛔ **~16 fps**) | 55.4 | **unit bodies 14–19** · effects 7–11 · gizmos 8–9 · update 21–23 / 12 |
| 2-D whole 7 km | 33.6–34.0 (⛔ **~29 fps**) | 27.6 | 22.4–22.5 (**~45 fps**) | 23.6 | update 18 · gizmos 6.5 / 3.5 · cards 2 |

**Steady garbage per frame (editor, paused — the same while playing, plus the apply's):** update **~630 KB** · the 2-D gizmo draw **~650 KB** (2-D only) ·
mission summary **~235 KB** · message log **~172 KB** · effects **~147 KB** · event timeline **~139 KB** · cards **~99 KB** (2-D) ⇒ **~1.4 MB (3-D) / ~2.0 MB (2-D)** per frame.

### 1a. After the W-3 fixes (same recording, same script; Release; three runs)

**Steady garbage per frame (paused; FrameStats' update · draw · ui KB/frame):**

| run | 3-D views | 2-D whole |
|---|---|---|
| editor BEFORE (capture on — §1) | 630 · 165 · 607 ≈ **1.4 MB** | 634 · 798 · 604 ≈ **2.0 MB** |
| editor AFTER, capture on (like for like) | 626 · 19 · 252 ≈ 0.9 MB (the update half is the capture's own models) | 566 · 41 · 254 ≈ 0.86 MB |
| ⭐ **editor AFTER, capture off (= the user's run)** | 9–14 · 19–21 · 58 ≈ **90 KB** | 14 · 41 · 58 ≈ **113 KB** |
| Browser AFTER, capture off | 3–7 · 18–20 · 425 ≈ 450 KB (`rb_fdp_inspector` 362 KB) | 7 · 41 · 425; 3-D far adds `Entities (3-D)` 400 KB |

Per apply: **2.0 → 1.7 MB** (editor), 2.1 → 1.8 MB (Browser) — the rest is CE-1952 (managed chunks restored whole).

**Frame (mean ms) playing 1× — capture off:** editor 3-D near 30.4–30.8 · mid 29.3–29.7 · far 55.4–63.9 · 2-D whole 30.8–33.3; Browser 3-D near
26.1–28.5 · mid 24.2–25.7 · far 63.1–66.3 · 2-D whole 22.9–25.6. ⇒ the garbage fixes do not move frame TIME much (the GC was not the bottleneck on
this machine: gen-0 collections stayed in the tens over a run); the time is in the draw — §2.

Seeks 1.2–1.7 s, open 2.0 s (editor) / 4.6 s (Browser), 1× keeps pace — unchanged. ⚠ The editor's one-frame step (`/replay/seek {frames:1}`)
landed 2.4–4.2 s of recorded time later (Browser: one frame, 0.07 s) — not chased here; noted for CE-1478.

## 2. Against the budget (R-4192: ≥ 30 fps playing 1×, seek ≤ 2 s, 0 B garbage per steady frame)

| | verdict |
|---|---|
| fps | ✅ 3-D near / mid both hosts (32–41 fps), 2-D Browser (~40 fps) · ⚠ editor 2-D whole 30–32 fps (at the line) · ⛔ **3-D far both hosts (~16–18 fps)** — a STATIC cost (paused 48–56 ms): unit bodies 13–19 ms, effects 7–10, gizmos 7–10, update 19–24 (editor) at ~6 000 visible units. Owner: the render-under-load work (CE-4389 / CE-4390 — bodies need a far LOD / impostor) — not replay's; named, not fixed here |
| seek | ✅ ≤ 1.7 s |
| garbage | ⚠ steady frame **~90–113 KB** in the editor (was 1.4–2.0 MB); Browser ~450 KB (its inspector panel); ⛔ per apply 1.7 MB — CE-1952 |

## 3. Fixes (W-3)

| # | what | measured before → after | rail |
|---|---|---|---|
| CE-1951 | the review windows re-opened a recording that FAILS to load every frame (`RecordingFileReader.Of`) — a schema check + a FATAL line per frame | ~90 ms UI frames with nothing shown → one attempt | `TheFileReviewTests.AFileThatFailsToLoad_IsTriedOnce_NotEveryFrame_UntilItIsRewritten` |

**Where the garbage came from — an allocation trace, not a guess.** `dotnet-trace --profile gc-verbose` (10 s per view, paused and playing) on the
editor's replay, aggregated by allocating stack (a scratchpad TraceEvent tool). Paused at 3-D mid the editor allocated **48 MB/s**; at 2-D whole
**77 MB/s**; playing **80 MB/s**. ⚠ **A finding about the measuring itself:** `HROT_DEBUG_API_PORT` — how every run here is driven — also turns on
`PanelSnapshot.CaptureEnabled` (`EditorSubsystem.cs` 8b, `Program.cs` cluster path), and then every panel builds and registers its snapshot model
every frame. A user's run has it off. ⇒ the §1 numbers include work the user does not pay; `HROT_PANEL_CAPTURE=0` (new, a measuring switch like
`HROT_REPLAY_*`) keeps the API without the capture.

| source (per 10 s, sampled) | fix | rail (each red-proved by a compiling mutation) |
|---|---|---|
| `GizmoFramePanel.Publish` built the map feed's JSON model (≤ 500 primitives) EVERY frame, registered it only when capturing — **~240 MB** | not built unless capture is on (PanelSnapshot's own contract: "the argument is not even built when off"); the editor's feed address built once | `GizmoFramePanelTests.WithCaptureOff_ASteadyPublish_Allocates0B` (and `WithCaptureOff_…` now asserts no model) |
| `DebugPrimitiveRenderer2D.Render` (2-D gizmos): a new anchor dictionary, sort list, order array and capturing comparer per frame — **~245 MB** (2-D) | reused fields; a span sort over ONE cached delegate (`Array.Sort(…, IComparer)` wraps it in a new delegate per call) | `DebugPrimitiveRenderer2DEntityLocalTests.ASteadyFrame_OfAnchoredPrimitives_Allocates0B_InRender` |
| `MissionSummaryWindow` → `RulesReader.Read` → `AuthoredRules.Parse` JSON-parsed EVERY started behaviour's params each frame (~1 000 units) to find no `common` block — **~105 MB** | text without `common` is no rule ⇒ null without parsing | `RulesModelTests.AUnitsBehaviourParams_AreNoRule_DecidedWithoutParsing_0B` |
| `EffectLayer.Resolve` / `ResolveClouds` / `Puffs` / `CloudPuffs` new lists per frame, `ParseRgba` two strings per effect — **~66 MB** | `…Into(list)` fills reused buffers (the public list-returning API stays for the rails); the colour parsed from a span | covered by `EffectTests` (37 green); ⚠ no 0 B rail — `Draw` needs a GL context |
| `TimelineLayout.Density`: two `int[bins]` per row and an interface enumerator per non-empty bin — **~48 MB** | per-thread scratch, index loops | `TimelineLayoutTests.ACrowdedRow_IntoTheCallersList_Allocates0B` |
| `StyleResolutionSystem.BuildStyle` (playing): fixed strings → strings per entity, the id label `ToString` — **~48 MB** (CE-4389's "next lean") | stack buffers: `FixedString32/64.CopyTo(Span<char>)` (new, 0 B), `ResolvedStyle.SetTextureName/SetLabelText(ReadOnlySpan<char>)`, the id `TryFormat` | `StyleResolutionSystemTests.StyleResolutionSystem_AResolvingPass_Allocates0B` |
| `PlaybackSystem.ChunkBuffer` fell back to `ReadBytes` past 64 pooled lengths (managed blobs vary) — **~32 MB** playing | a VIEW of the frame's buffer (`ArraySegment`), no copy at all; `EntityIndex.Restore*ChunkFromBuffer` take a span | correctness: the recorder / playback rails (`Fdp.Core.Tests` 245 + 87 green); ⚠ the view's 0 B cannot be red-proved (the length pool hides it for unmanaged chunks) |
| `FramePrefetcher.Fill` grew each slot to the EXACT frame size — a fight's frames grow ⇒ a re-allocation nearly every fill — **~121 MB** (worker thread) | grown with a quarter's headroom (and the inline path's buffers) | ⚠ worker thread — no per-thread rail; the trace re-measures it |
| `EntityRotationGizmo.Draw` an interpolated string per unit — **~26 MB** | formatted on the stack | `EntityRotationGizmoTests.SC_GZ021_ROT_2b_Draw_Allocates0B` (+ `ROT_2` now asserts `90*`) |
| `DebugGizmoLayer.CardAnchor2D/3D` a dictionary + closure per frame | one dictionary, one delegate each | — (draw path) |

Not fixed here, named: `PlaybackSystem.RestoreManagedTable` — a touched MANAGED chunk is deserialised whole into new objects (strings,
`ActiveMissionPlan[]`, …) for every entity in it, changed or not — **~170 MB per 10 s playing**, the bulk of the ~2 MB per apply. That is a format
question (managed deltas per entity, like P6 did for unmanaged blocks) and a component one (R-2084's rule: a recorded managed component changes
RARELY) — filed as **CE-1952**. `WindowManager.WindowsMenuDto` / `GlobalMenuRegistry.BuildViewModel` (~11 MB) — the menu bar rebuilt per frame.

Filed, not fixed here: **CE-1950** (backend, orchestrator) — a replay that fails to load still reaches `OperatingReplay`, and `/replay {waitForReady}` answers `ok`.

## 4. CE-4407 (W-4)

The user's < 1 fps of `2026-11-10` (the Replay Browser on a 1 000-man scale-1 recording) → today **15–44 fps** in the Browser and **16–34 fps** in the
editor on the same kind of recording, 1× keeping pace. Lean: **close CE-4407** — the "something eating all the time in AAR" is gone (apply 2.5–3 ms,
prefetched; repair < 1 ms); what remains is the far-view draw cost, which is the same live and paused (CE-4389 / CE-4390) and CE-1952's per-apply garbage. ⚠ No "before" recording could be replayed on this build (none older than v8 here; and the
Browser's old frame-count pacing, CE-1479, has no switch), so the before is the user's own number.

## 5. Gates (all hidden, `HROT_HIDDEN_WINDOW=1`; each test project BUILT before `--no-build`)

| gate | result |
|---|---|
| `Fdp.Diagnostics.Contracts.Tests` `GizmoFramePanelTests` | 7 / 7 (incl. the new 0 B rail) |
| `Fdp.Presentation.Tests` `TimelineLayoutTests` · `DebugPrimitiveRenderer2D*` · `GizmoLayer*` | 7 / 7 · 66 / 66 |
| `Hrot.Presentation.Tests` `EffectTests` · `Map3DGizmoTests` · `DeclaredGizmoInputsTests` · `MissionSummary*` · `RulesModelTests` | 138 / 141 — ⚠ the 3 reds are the decimal comma (this machine's Czech locale: `0,14` vs `0.14` in `RulesModel.OverviewOf` / the mission summary's attention line), in code this batch did not touch — the same class as CE-1475 |
| `Hrot.Player.Tests` `StyleResolution*` · `EntityRotationGizmoTests` · `LayerRendering*` · `DebugPanelState*` | 44 / 44 |
| `Hrot.Editor.AiShared.Tests` `TheFileReviewTests` | 7 / 7 |
| `Fdp.Core.Tests` recorder / playback / prefetch / block-delta / partial-repair / entity-index / managed / fixed-string classes | 245 + 87 green, 6 skipped (pre-existing) — one red was this batch's own (`CE4050_…` calls the private `ApplyChunkData` by reflection with a `byte[]`; updated to the `ArraySegment`) |
| red-proofs | all 7 new rails RED under a compiling mutation (the old code put back), then restored |
| `tracker-counts.py --check` | OK |

Ids allocated: **CE-1950** (backend: a failed replay load still reaches `OperatingReplay`), **CE-1951** (done), **CE-1952** (managed chunks restored
whole). This session's ui ids below `CE-1480` are used up; it takes `CE-1950`+ (the other ui session runs `CE-1480`+), noted in the tracker's id table.
