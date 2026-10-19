<!--STATUS
state: LIVE — backend → behaviors: BR-14…BR-16 built (rows CE-3273…CE-3275); the hearing cap ruled and built (CE-3276, R-3023): a soldier now hears a launch at 1.2 km.
updated: 2026-10-19
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — §4 ② / ⑧ (the need)
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — §6 B (a weapon's report), §8.1 (a listener's ear range)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5 (the weapon types), R-1001 (vehicle weapons are TKB presets)
-->
# Note — backend → behaviors: the urban battle's data (BR-14…BR-16)

Answers [HANDOFF_Behaviors_To_Backend_Urban_Battle_Data](HANDOFF_Behaviors_To_Backend_Urban_Battle_Data.md). Rows in backend's block:

| # | row | built | rail |
|---|---|---|---|
| **BR-14** | `CE-3273` | every weapon system that fires carries its own report (`WeaponSystemType(…, report:)`): mortars 60 / 81 / 120 mm → 1.5 / 2 / 2.5 km, 105 mm towed → 4 km, M109 → 5 km (+ its tracks, 300 m), all `HeavyWeapon`; HMG / minigun 1 km, ZU-23 1.5 km, launchers 0.5 km, tripod ATGMs 0.8 km, pick-up rifle / pistol 400 / 200 m `SmallArms`; the charge none | `EqsModuleTests.BR14_…` (a 81 mm launch heard at 1.2 km, not at 3 km, at the tube) · `NedTkbBuilderCombatTests.BR14_…` |
| **BR-15** | `CE-3274` | `Truck_HMMWV` is a light platform: armour 6 / 5 / 5 / 3 mm, 350 hp, blast 0.9, mobility kill below 60 % | `NedTkbBuilderCombatTests.BR15_…` (rifle: half damage on the side; RPG: kills on every face) · roof gun: SimHost `P1_…` (HMMWV row) |
| **BR-16** | `CE-3275` | `MilitaryAPC` 2001: gunner seat **8** (enclosed, starts with an `InfantrySoldier`) + RWS station **9** (pintle, `NeedsGunner`, starts with an M2); seats 0–7 unchanged | SimHost `P1_…` (APC row: both spawn, board, the vehicle controls the M2, `CanFire`) |

⚠ **Found on the way (BR-14):** a crewed weapon's round names the WEAPON as its shooter (WS-3), so the sound sits on the weapon's
emitter. The technicals' own `FiringAudibleRange` (on the pickup) never sounded — today's change gives the M2 / DShK / ZU-23 their own.

✅ **Ruled and built `2026-10-19` — `CE-3276` (R-3023): a `HeavyWeapon` / `Explosion` sound now carries past the ear range** ([Thermal & Acoustic §8.2](../../DESIGN_Thermal_And_Acoustic_Sensing.md)); the original question: a listener's ear range caps every sound (`AcousticPerception.Hear`: `d > carries || d > range`).
An urban soldier hears 200 m and a BDC rifleman 750 m ⇒ **a soldier does not hear the 81 mm at 1.2 km** even now. The rail uses a 5 km
listening post. Lean: a loud class (`HeavyWeapon`, `Explosion`) is heard within its OWN carry, whatever the ear range. Until it is
ruled, counter-battery (IF-6) and "finding a mortar" (§4 ②) only hear tubes inside the soldier's ear range. ⇒ Now: a soldier
hears the 81 mm at 1.2 km (`EqsModuleTests.CE3276_…`), so §4 ② and IF-6 can build on a heard launch.

⛔ Fences kept: no behaviour changed.

---
_backend lane, 2026-10-19_
