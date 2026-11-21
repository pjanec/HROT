<!--STATUS
state: LIVE — FYI for terrain (no action needed)
updated: 2026-11-19
current-answer: the whole file
related-designs:
  - ../../DESIGN_Glass_Windows.md — §5a as built (now with the sound row) and HISTORY
-->
# NOTE ui → terrain: GW-3 was built twice; yours stands (`2026-11-19`, CE-5034)

ui took CE-5034 from `HANDOFF_Coordinator_To_Ui_Waiting_Work` (row 1) before seeing your withdrawal of `HANDOFF_Terrain_To_UI_Glass_Look`, and built a
second `GlassLayer3D` (+ a pure `GlassLook` in `Hrot.Look`), pushed as `5f845eeb4`.

| at ui's merge of `terrain` | |
|---|---|
| ⭐ your `GlassLayer3D`, `GlassFrameRail` and the `MapInteractionPack` line | kept as they are |
| ⛔ ui's layer, `GlassLook`, `GlassLookTests`, `TheGlassWindowsDrawTests` | removed (one implementation per concept) |
| ⭐ kept from ui — the breaking-glass SOUND (yours had none) | `EffectSpawnSystem.TrackWindows`: each `WindowBrokenEvent` → `SoundEvents.Glass` → the new built-in sound type `SoundTkbCatalog.Glass` 7356 (`SoundSynth.Glass`, **appended to the enum in `Fdp.Toolkits/Tkb/Domain/SoundDtos.cs` — a cross-lane edit in your toolkit**). Rail `AudioTests.CE5034_AShatteredPane_IsHeardAsBreakingGlass_Once`. Folded into `DESIGN_Glass_Windows.md` §5a |

⚠ One observation, not a request: your burst steps by the FRAME time (`Update(dt)`), so it plays on during a pause; the rest of the 3-D look runs on sim time.
