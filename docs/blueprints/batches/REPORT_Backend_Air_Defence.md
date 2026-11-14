<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file. It is the report for HANDOFF_Behaviors_To_Backend_Air_Defence.md (CE-2187…CE-2190).
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §3c owns the IR seeker, proximity fuze, countermeasures and warning (as-built there)
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — §5c owns the MANPADS launchers, the gunner presets and the ZU-23-2
  - docs/designs/air-attack/DESIGN_Air_Operations.md — §7d owns the frame (AD1–AD6)
-->

# REPORT — backend: air defence, the engine half (CE-2187…CE-2190)

**For:** the behaviours lane. **From:** backend, `2026-10-15`, started at `525562886` (merged `behaviors@278de2fef`).
🔒 User: *"approved, start air defence autonomously"* — the leans AD1–AD5 as sent, plus moving `SimRng` to `Fdp.Toolkits`.

## 1. What you can build against (CE-2191)

| you want | read / raise | where |
|---|---|---|
| a missile is coming | `SensorChange.MissileWarning` (17; HSM `Sensor.MissileWarning`, id 0xFF11) on the aircraft's brain, and its `MissileWarning` component: `At`, `From`, `BearingDeg` (0 = north, clockwise), `Seeker` (`GuidanceKind`), `TimeToImpactS` | `MissileWarningSensingSystem` (CgfLogicPack) |
| throw flares / chaff | publish `DispenseCountermeasureIntent { Unit, Kind = DecoyKind.Flare / Chaff }` on the brain — it crosses to the SimHost by itself (DDS `DispenseCountermeasure`) | `CountermeasureSystem` (SimHost) |
| what is left | `CountermeasureState` on the SimHost only (⚠ not replicated to the brain — tell me if a behaviour needs the count) | §3c |
| a MANPADS team | type **202** (Stinger) / **203** (Igla): a rifleman with the launcher (613 / 614) on his back; his weapon brain picks it only at an aircraft and holds it (`WrongDomain`) at anything else | Weapon Systems §5c |
| an AA technical | type **110**: the pickup with a ZU-23-2 (615) in the bed, −10…+90° | Weapon Systems §5c |

⚠ **The IR lock is the launcher's `AimSeconds` (4 s)** — the existing aim clock; a soldier must SEE the aircraft for it, as for every aimed shot.
⚠ **A homing missile leads** its target (`TargetLead.Point`) — a break turn works by pulling the aircraft off the predicted point
faster than the 30°/s missile can follow, and a flare works only while the missile sees it in its 20° cone.

## 2. Built

| row | commit | what |
|---|---|---|
| CE-2187 | `7104ea35c` | IR seeker + cone + lead, `TargetDomain` (one gate + a hold; every ATGM Ground-only), proximity fuze (4 m) through the existing area effect, Stinger 6030 / Igla 6031, launchers 613 / 614 (`WeaponCategory.Sam`), gunners 202 / 203; ZU-23 data |
| CE-2188 + CE-2189 | `51cb95da4` | `CountermeasuresDto` on every crewed aircraft, `CountermeasureSystem`, the seduction draw (`SimRng`, moved); the warning event, DDS dt 133 / 134, `MissileWarningSensingSystem` |
| CE-2190 | (this commit) | the ZU-23's rails: placed in 110's bed and controlled by the pickup; elevates overhead (the M2 cannot); a round at a helicopter crossing at 60 m/s, 1 km out, passes within its body radius |

## 3. Deviations (each argued in the design's as-built)

① a homing seeker LEADS — pure pursuit passed 6.8 m behind a crossing helicopter (measured) · ② `TargetDomain.Either`, not `Any` (IDL
keyword) · ③ a helicopter on its pad is still Air (the locomotion-class rule) · ④ a decoy integrates its own velocity (Stride's
integrator is projectile-only) · ⑤ the ZU-23's muzzle speed is the BDC derivation (range × 0.5 = 1250 m/s, real ≈ 970) like every BDC
gun — the lead and the flight use the same value, so it hits; tuning starts there.

## 4. Gates *(CLAUDE.md rule 8)*

| gate | command | result | delta / note |
|---|---|---|---|
| Toolkits | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | **3554 / 0** (+1 skip) | +6 CE-2187, +4 CE-2188, +1 CE-2189, +1 CE-2190 |
| SimHost | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build` | **1417 / 1** (+6 skip) | the 1 = `SopParamsRoundTripTests.CE2079` (yours), red at base `6fb06b88c` 3/3 |
| Core | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build` | **261 / 0** | +1 catalogue rail |
| red-proofs | `TickCount64 < 0` bypasses | each reddened its rail | no lead · no proximity fuze · no domain gate · no seduction · no lead (ZU-23) |
| tracker / designs / diagrams | `tracker-counts.py --check` · `design-digest.py --check` · `mermaid-check.mjs` | OK · OK · 12/12 | |
| ⭐ row 8 (cross-node) | `AirDefenceTranslatorsTests` (two worlds, two entity maps) | 2 / 0 | ⚠ **no cluster run**: the DDS transport is the shared machinery; your `ua-air-defence` is the live check |
| clean tree | `git status --short` after every suite | clean | |

Cross-lane edits, named in the commits: `Hrot.AI.Behaviors` (one `using` in 6 files — `SimRng` moved) · `Hrot.CGF/CgfLogicPack` (+1
system; `CgfLogicPackTests` counts 21 / 26) · `Hrot.Presentation` (`Sam` = a launcher; HUD label) · your tracker rows CE-2187…CE-2190.
