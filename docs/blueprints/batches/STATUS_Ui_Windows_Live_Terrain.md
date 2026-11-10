<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table "Items" (what was done on the cloud VM, what waits for the Windows machine), then "Gates"
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

## Items

| # | id | state | what |
|---|---|---|---|
| **L-0** | CE-4386 | ✅ built + proved on the VM (Xvfb) · ⏳ the Windows proof | `HROT_HIDDEN_WINDOW=1` — `Fdp.Presentation.Raylib.HiddenWindow`, used by ALL window creators (the frame harness, the editor / player shell, `FdpApplication`): raylib `HiddenWindow` + `UnfocusedWindow` before `InitWindow`; each frame drawn into an off-screen render target the window's size, which stays the GL READ framebuffer after the frame ⇒ every `LoadImageFromScreen` caller reads it unchanged (46 rail call sites, none touched); the film's own pass resumes onto it; on Windows a console window the process owns alone is hidden too (never the user's terminal). Default OFF |
| **L-1** | CE-4383 | ⏳ the live run waits for Windows · ✅ the opt-in rail written | `TheWizardRunsLiveTests` (`HROT_OSM_LIVE=1`): search → box → real Overpass preview → Import (DEM, netconvert, importer) → the New Scenario catalog lists it, each stage timed |
| **L-3** | CE-4385 | ⏳ needs the Windows GPU | software GL on the VM measures nothing about the user's frame rate |
| **L-2** | CE-4384 | ✅ built + rails · ⏳ the live proof (a dense city) waits for Windows | see the OSM design §7 as-built |
| **L-4** | CE-1449 | ⏳ needs Windows (Stride) | |
