<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — ⭐ THE DESIGN (approved R-4296): §7 the shared look library, LK-1…LK-7, §7a effect categories
  - ../../designs/person-looks/DESIGN_Person_Outfits.md — what the person resolver resolves
  - HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md — backend builds `VehicleLook` into the same assembly after your LK-S1
-->
# HANDOFF — coordinator → `ui`: THE SHARED LOOK LIBRARY (CE-4671: LK-S1, LK-S2)

🔒 User, `2026-11-18`: *"approved, fold both into the design"* · *"write the handoffs for ui and backend"*. Why: external renderers (a gateway on a node)
must show the SAME effects and people as our map — the rules move out of the presentation into one assembly with no presentation dependency.

## Priority
After CE-3468 and CE-2438 (the editor bugs moved to you), BEFORE the test-and-tool rows (CE-1473, CE-3451, CE-1475, CE-1046, CE-1038). ⭐ LK-S1 FIRST
of the two — backend's `VehicleLook` (VA-S1) lands in the assembly LK-S1 creates; leave backend a NOTE when it exists.

## The rows

| order | slice | builds (design §7 is the spec) | done when |
|---|---|---|---|
| 1 | **LK-S1** | ⭐ the assembly **`Hrot.Look`** (refs `Fdp.Toolkits` + `Hrot.Core`; ⛔ no Raylib, Stride, ImGui, `Hrot.UI`, `Hrot.Presentation`) in the root solution; `EffectRules.For(event, view, tkb) → EffectSpec[]` extracted from `EffectSpawnSystem` (`Hrot.Presentation/Effects/EffectSystems.cs`) — muzzle flash (none for silent weapons and bombs), explosion + ground mark, impact puffs / marks per surface, dust for inert thrown items and broken barriers, backblast / gun blast / launch; `EffectSpec` = `Kind` · `SizeClass` · `Surface` · `MunitionDis` (the munition's TKB DIS type) · `EffectType` (the per-ammo override) · anchor · lifetime · size (§7a); `EffectSpawnSystem` spawns the map's effect entities FROM the specs; the two HUD feeds (player messages, the kill feed) move OUT of it (they are not effects). ⛔ The wreck burn (`WreckSpawnSystem`) is NOT yours — backend's thermal CE-4660 re-bases it on `FireState` | the map's effect rails UNCHANGED and green (`EffectTests`, the 3-D frame rails under `xvfb-run -a`, the HUD-feed rails); ⭐ a NEW rail calls `EffectRules` from a test project that references `Hrot.Look` and NO presentation assembly; 0 B on the steady path (`EffectRules` on a frame with no events) |
| 2 | **LK-S2** | `PersonLook.Of(view, entity, tkb) → PersonAppearance` extracted from `Map3D/Outfits.cs`, `Wardrobe.cs`, `KitFromTkb.cs` — colours as a plain RGBA struct (⛔ no `Raylib.Color`); the map converts when drawing; the entity-seeded variety unchanged (LK-5) | the outfit / wardrobe / kit rails UNCHANGED and green; the same no-presentation rail for `PersonLook` |

⛔ A pure REFACTOR: no look changes. A golden or a frame that moves is a defect of the move, not a new look. §6a: no scenario run is needed for a
refactor whose rails are unchanged — run one map-heavy scenario (e.g. `ua-posture`) once at the end, in the background.

## Gates (rule 8)
The touched presentation test classes + the new `Hrot.Look` rails, every new rail red-proved; display rails under `xvfb-run -a` (counts, never a
skip read as a pass); the full-solution build once at the end (a new assembly). Fold any deviation back into the design §7 (obligation ⑤).
