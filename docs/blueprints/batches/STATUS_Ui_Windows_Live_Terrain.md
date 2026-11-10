<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: "## On Windows (2026-11-10)" — the Windows run (L-0 proof, L-1 live, L-3 numbers, L-4); "Items" / "Gates" below it are the cloud-VM run
related-designs:
  - docs/DESIGN_Terrain_From_OSM.md — OWNS the wizard and the library; §7 (failures) holds L-2's as-built
  - docs/DESIGN_Map_3D_Mode.md — OWNS the chunked 3-D terrain (§6ah) where L-3's "measured on Windows" block goes
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — how to run the editor; holds the hidden-window switch (L-0)
-->

# STATUS — the OSM wizard live and the first real frame rates (ui; CE-4386, CE-4383, CE-4384, CE-4385, CE-1449)

Handoff [HANDOFF_Coordinator_To_Ui_Windows_Live_Terrain.md](HANDOFF_Coordinator_To_Ui_Windows_Live_Terrain.md), dispatched at `894ca0d98`; started at `bc6b5d8f0`.
⚠ **This run is on the CLOUD VM (Linux, Xvfb, Mesa software GL), not the Windows machine the handoff is written for** — 🔒 user, `2026-11-10`: *"so please do what you
can in VM"*. Measured here: `overpass-api.de`, `overpass.kumi.systems` time out and `maps.mail.ru`'s Overpass answers 504 ⇒ no live preview from this VM.
Cross-lane edits, stated in the commits: `Hrot/Engine/Hrot.Terrain.Osm` (the coordinator's library — allowed), `FDP/Engine/Fdp.Presentation` (the hidden-window switch),
`Hrot/Editor/Hrot.Editor.UiFrameRail` (the frame harness), `Hrot/Runner/Hrot.ClusterRunner` (the editor's window and frame loop).

## On Windows (2026-11-10) — the user's machine, every run HIDDEN

⭐ **No window appeared at any point**: every frame-rail run and every editor ran with `HROT_HIDDEN_WINDOW=1` (the editor logs `hidden=True focused=False`; no
`dotnet` process had a visible main window). Merged nothing new (ui at `14194f2cc`); commits `51aa42476`, `08086bf14`, `e01dec527` + the L-3 commit.

| # | id | state | what happened on Windows |
|---|---|---|---|
| **L-0** | CE-4386 | ✅ proved on Windows | ⛔ **every frame rail SKIPPED on Windows** (the harness required `DISPLAY`) ⇒ now they run with the switch, skip without it. ⛔ at 150 % scaling raylib reads the screen × the DPI scale ⇒ every probe read black (`TheHiddenWindowModeTests` red: `0,0,0`) ⇒ a DPI-unaware hidden process (red → green). ⛔ the first full hidden run had ~190 reds from ONE race: test classes declaring into the process' `Keymap` in parallel corrupted its dictionary ⇒ declarations locked. Suite: **2 009 passed / 24 failed / 9 skipped** — 20 fail at base too (rails all skipped there: 1 809 / 20 / 212), the other 4 are the known `CE1380` / `CE1227` reds and two Debrief / Mission-summary frame rails in the same "strings differ" family as the base Debrief model reds |
| **L-1** | CE-4383 | ✅ live end to end | two defects found and fixed: the search said "no result" with Nominatim answering (`Searching` false before the answer landed) · an Overpass answer carried a way TWICE ⇒ "duplicate id" and the import refused (the reader now keeps each element once — coordinator's library, stated). Then: **Köln 1.5 km** (town; "Kolin" without the accent finds Köln first — a UX finding, below) search 0.3 s · preview 50.1 s cold (1 943 enterable, 108 km roads, verdict Ok) · import 15.3 s; **Velim 2 km** (village) search 0.2 s · preview 26.9 s (567 enterable, 19.6 km roads) · import 8.0 s; New Scenario on it: play-ready < 10 s, 774 entities, **ran 15 s of sim** in preview (69 fps, 3.4 MB / frame whole process), stopped clean. Overpass answered 502 once — the wizard said *"Overpass (overpass-api.de) answered 502 BadGateway after 4 attempts — try again in a minute or two"* (L-2 ③ seen live) |
| **L-3** | CE-4385 | ✅ measured · ⏳ under 30 fps in most views — the costs named | the table + the frame split in [Map 3D §6ah "Measured on Windows"](../../DESIGN_Map_3D_Mode.md). ⭐ the chunked terrain's draw is zero-garbage and cheap; the budget goes to the **update** (`StatelessGizmoSystem` 11.4 ms on scale-1) and the **2-D draw** (6.3 MB / frame). Fixed now: the gizmo family policy hashed a freshly built string per entity per frame (540 KB / frame over 6 912 doors — first blamed on `MissionPresentationGizmo`'s JSON parse, which was fixed too but measured not to be it). New tools: `--target-fps 0`, `/diagnostics/render` terrain + memory + garbage, `/film/shot {twoD}`, `HROT_FRAME_STATS` per gizmo rule. The rest → the render-under-load handoff (R-2 / R-3), which measures the same systems at 1 000 men |
| **L-2** | CE-4384 | ✅ ③ seen live · ⏳ ① (a too-large box) not triggered | the Köln box (2 090 footprints) came back whole; ⭐ so did **central Prague, 4 × 4 km** — search 1.6 s, preview 51.7 s, 10 868 enterable / 11 595 footprints, 1 023 km roads, 5 866 trees; the wizard's verdict **Refused** (over the budget) and Import stayed blocked, as designed. ⇒ overpass-api.de answers a box this size whole; the split path is proved only by its rails until a box Overpass itself refuses |
| **L-4** | CE-1449 | ✅ | Stride's run controls through `NodeRunControls.For` (SimHost's profile); `HrotStrideApp.Game` builds on Windows; a source-scan rail keeps every ECS host on the one factory (Stride included). ⏳ its window does not compose the loading screen yet |

**Findings to file:** "Kolin" (no accent) → Köln first: the wizard's search could bias to the user's region or show the country first · the ~2 MB / frame the
idle 3-D editor allocates (update 1.55 MB + UI 0.7 MB — the inspector 437 KB, the message log 171 KB) · two `WriteTests` in `Hrot.Terrain.Osm.Tests` red once in a
full run, green alone and in two full runs after.

| gate (Windows) | result |
|---|---|
| `HROT_HIDDEN_WINDOW=1 dotnet test Hrot.Presentation.Tests --no-build` | 2 009 / 24 failed / 9 skipped (see L-0) |
| `dotnet test Hrot.Terrain.Osm.Tests --no-build` | 126 / 0, 1 skipped (twice) |
| red-proofs (each compiles) | `MakeDpiUnaware` absent ⇒ `TheHiddenWindowModeTests` red · `Searching` the old line ⇒ `Search_IsStillSearching_UntilAPumpLandsTheAnswer` red · no `TryAdd` guard ⇒ `AnElementTwiceInTheAnswer_IsReadOnce` red · the per-draw parse back ⇒ `CE4385_ALegacyTargetRoute_DrawsWithNoGarbage_OnceWarm` red · `SettingHash` per call again ⇒ `CE4385_TheFamilyPolicy_AnswersWithNoGarbage_OnceWarm` red (⚠ the first mutation did not compile and its 'pass' ran the old binary — redone with one that compiles) · Stride's line back to `new ClusterTimeTransportAdapter(` ⇒ `CE1449_NoEcsHostBuildsItsRunControlsByHand_StrideIncluded` red, naming `StrideNodeShell.cs` |
| `dotnet build Stride/HrotStrideApp.Game` | 0 errors |
| `tracker-counts.py --check` | OK |

## Items (the cloud-VM run)

| # | id | state | what |
|---|---|---|---|
| **L-0** | CE-4386 | ✅ built + proved on the VM (Xvfb) · ⏳ the Windows proof | `HROT_HIDDEN_WINDOW=1` — `Fdp.Presentation.Raylib.HiddenWindow`, used by ALL window creators (the frame harness, the editor / player shell, `FdpApplication`): raylib `HiddenWindow` + `UnfocusedWindow` before `InitWindow`; each frame drawn into an off-screen render target the window's size, which stays the GL READ framebuffer after the frame ⇒ every `LoadImageFromScreen` caller reads it unchanged (46 rail call sites, none touched); the film's own pass resumes onto it; on Windows a console window the process owns alone is hidden too (never the user's terminal). Default OFF |
| **L-1** | CE-4383 | ⏳ the live run waits for Windows · ✅ the opt-in rail written | `TheWizardRunsLiveTests` (`HROT_OSM_LIVE=1`): search → box → real Overpass preview → Import (DEM, netconvert, importer) → the New Scenario catalog lists it, each stage timed |
| **L-3** | CE-4385 | ⏳ needs the Windows GPU | software GL on the VM measures nothing about the user's frame rate |
| **L-2** | CE-4384 | ✅ built + rails · ⏳ the live proof (a dense city) waits for Windows | see the OSM design §7 as-built |
| **L-4** | CE-1449 | ⏳ needs Windows (Stride) | |

## Gates

| gate | result |
|---|---|
| `HROT_HIDDEN_WINDOW=1 xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build` | 2 030 / 3 failed, 7 skipped (one more test than visible: `TheHiddenWindowModeTests` runs only with the switch) |
| the same, visible | 2 029 / 3 failed, 8 skipped — the SAME three |
| base `72aaefa03` (before L-0), clean worktree, visible | 2 029 / 3 failed — the SAME three ⇒ all pre-existing: `OneWindowPerProcessTests.CE1039` (order-dependent: passes alone; a rail asking for another window size makes a second window), `FilmFrameRail.CE1380`, `Map3DFrameRail.CE1227` |
| the editor, hidden, `--mode editor` with `HROT_DEBUG_API_PORT` | answered `/diagnostics/architecture` `ok:true` |
| `dotnet test Hrot.Terrain.Osm.Tests --no-build` | 125 / 0, 1 skipped (the opt-in live rail) |
| red-proofs (each compiles) | `HiddenWindow.BeginFrame` a no-op ⇒ `TheHiddenWindowModeTests` red · `IsTooLarge` always false ⇒ two `OverpassRobustnessTests` red |
