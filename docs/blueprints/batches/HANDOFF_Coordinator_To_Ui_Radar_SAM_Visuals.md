<!--STATUS
state: LIVE — RE-DISPATCHED (rule 1a: the first stamp 87fc746f3 was never started), not started
updated: 2026-11-15
current-answer: the whole file
related-designs:
  - ../../designs/air-attack/DESIGN_Radar_SAM.md — ⭐ THE design (RS-1 the vehicle, RS-9 who builds; §5 U-1…U-3)
  - ../../DESIGN_Visual_Effects.md — the launch / motor effects this extends
-->

# HANDOFF — coordinator → `ui`: the radar SAM's visuals (CE-4618)

**For:** the `ui` lane. **From:** coordinator. **Dispatched at `9b6ed8318`** *(re-stamped again, rule 1a — unstarted; supersedes `d287b3380` and `87fc746f3`: adds the fixed ids)*; ⛔ scope FROZEN there.

🔒 User, `2026-11-15`: *"The UI lane can provide the visuals for the carrier and launcher and the missile and the effect of launch."*

## ⭐ The entity to bind — [design §2a](../../designs/air-attack/DESIGN_Radar_SAM.md)

| what | the value |
|---|---|
| vehicle type | `TkbEntityTypes.AirDefence_RadarSam = 114` ("Radar SAM vehicle") — declared by backend |
| DIS / class | DIS `1.1.*.28.1` · entity class `"AirDefenceVehicle"` = `1.1.*.28` — declared by backend in `EntityClassTable.cs` |
| ⭐ YOUR key | add `VisualFamily.AirDefenceVehicle` and map `["AirDefenceVehicle"]` to it in `FamilyOfClass` (`VisualFamily.cs`, beside `["Bulldozer"]`), plus its `ShapeKit` |
| missile | `MunitionTkbCatalog.TkbRadarSamMissile = 6047` — backend adds a placeholder look row; you tune it |
| launcher | the vehicle's own turret 0 — no separate weapon-system entity |

⚠ If backend's type is not merged when you start, declare nothing of theirs: build the family and kit against a test template carrying that DIS
type and class, and bind once `114` lands. If backend reports different values (its batch was running before these were fixed), follow its as-built.

| # | builds | done when |
|---|---|---|
| **U-1** | the vehicle's 3-D body — ⚠ chosen by its DIS type / class → `VisualFamily` → a `ShapeKit` (`VisualFamily.cs:85-99`, `ShapeKit.cs:559`), NOT an asset key: a new family / kit for the SAM truck; the launcher (4 canisters) on **turret 0** so it elevates through the replicated `TurretPose` (`TurretPoseSource.cs:24-44`); a SECOND, time-driven part — the radar mast — spinning while the replicated `RadarEmission.On` (backend B-1), still while Off | drawn in 2-D and 3-D, ⭐ also on a separate display node (a cluster run); a frame rail under `xvfb-run -a` |
| **U-2** | the missile look (`MunitionVisualDto` in `MunitionTkbCatalog`, its smoke trail) + the launch flash (an ammunition `Effect.Set`, VE-D) — backend's B-2 adds placeholder rows; you tune them (cross-lane into `MunitionTkbCatalog`, say so in the commit) | the launch shows on the frame of the shot |
| **U-3** | ⏸ optional: the radar's coverage on the map when selected, On / Off colour | — |

⚠ If backend's B-1 / B-2 are not merged when you start, build against a test type with a turret and a stand-in `On` flag, and rebind. ⛔ No screenshots in git — side-by-sides go to the report and chat. Report: `STATUS_Ui_Radar_SAM_Visuals.md`.
