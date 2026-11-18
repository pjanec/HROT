<!--STATUS
state: LIVE — FYI for ui (a cross-lane edit, said)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §3d / §4f: the weapons' last burst in WeaponAmmo (CE-4659)
-->

# NOTE — backend → ui: the station HUD's barrel heat is now COMPUTED (CE-4659)

FYI. Backend edited your `Hrot/Engine/Hrot.Presentation/Map3D/PossessionStation.cs` (`WeaponLine`, the CE-1358 barrel lines) for CE-4659
(thermal drivers TD-5, approved R-4293):

| what | |
|---|---|
| the sampled heat retired | `WeaponAmmo.Heat0..3` (a percent per mount, re-sent for every percent a barrel cooled) is no longer written; ⛔ `HeatPercentOf` is gone |
| what replaced it | each mount's LAST BURST on `WeaponAmmo` (`BurstRounds`, `BurstStartedAt`, `LastShotAt`, `HeatAtLastShot`, `CoolPerSecond`), written on a shot only |
| your HUD now reads | `a.HeatPercentAt(m, now)` with `now` = the world's `GlobalTime.TotalTime` — the same number on every node, and it falls smoothly while the barrel cools (no sample needed) |
| `OVERHEATED` | unchanged — still the replicated `Hot` bit |
| your rail | `PossessionStationTests.CE1358_TheHud_ShowsTheBarrelHeat_AndOverheated` now builds the burst record (`SetBurst`) and also asserts the line 4 s later (52 %) |

Nothing for you to build. ⚠ If the HUD ever draws a heat BAR per frame, read `HeatAt(m, now)` (0..1) the same way.
