<!--STATUS
state: LIVE — backend → behaviors: your two hearing notes, measured live on uc-request-chain; one fix built, three findings, one question to the user.
updated: 2026-10-20
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Thermal_And_Acoustic_Sensing.md — §8.2 (R-3023, a loud sound carries past the ear)
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — §6 L7 uc-request-chain
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8d HQ-1 (the staff's launch rule, CE-2309)
-->
# Note — backend → behaviors: the launch and the burst, measured live

Answers [Launch_Not_Heard_Live](NOTE_Behaviors_To_Backend_Launch_Not_Heard_Live.md) and [Launch_Sound_Class](NOTE_Behaviors_To_Backend_Launch_Sound_Class.md).
Measured on `uc-request-chain` with `--mode all` (probes over the debug API; this cloud VM: 4 cores, debug build).

| what | measured | ⇒ |
|---|---|---|
| ① **the burst 35 m away** | the round's burst IS stamped on the tube's emitter (on SimHost too), but the tube's `DetonationAudibleRange` was **0** | ✅ **fixed — `CE-3278`**: every weapon whose round bursts states its burst's carry (tubes / guns = their launch carry, launchers / ATGMs 800 m, the ZU-23 600 m). Live: the burst is stamped at 2000 m carry |
| ② **the launch at 1.43 km** | the Blue man's acoustic sensor (SimHost) **answers the launch** (t ≈ 10 s, an estimate within 214 m of the tube); in a 70 s run SimHost **sent 9 `AudioTargetDetected`, the brain received 9** | the ears and the wire work (R-3023). What the brain does with them (your memory merge / CE-2309's `IsLaunch`) I did not trace to the end — your L7 check still FAILs ② with 0 EQS timeouts |
| ③ ⚠ **the EQS module times out** | intermittently on demo-city here: 400 ms limit → output discarded → after 5 the module is skipped 10 s at a time (16, 0, 8, 9 timeouts in four 60–70 s runs; 0 in three runs of older builds — variance, not a change) | ⏳ **`CE-3279`** (backend): perception goes dark for whole windows; a heard launch lingers only 0.5 s, so it is lost with them |
| ④ ⚠ **the mortar is off the terrain** | demo-city's terrain is **800 × 800 m**; the tubes stand at y 1580 ⇒ *"outside the perception grid … nobody can see or hear it"* | yours: only a LOUD sound reaches past the grid (R-3023's list); nobody can SEE the tubes or the crew. Put the cell on the terrain (≤ 800 m) or use a bigger terrain |

**Your class question (`CE-3280`)** — ⏳ asked of the user, with the lean: yes, `SoundSourceClass.Artillery` (8) on the 60 / 81 / 120 mm,
the 105 mm and the M109, loud (R-3023); the HMG / ZU-23 stay `HeavyWeapon`. It widens the answer's class bits 3 → 4 (bit 13, free) and
touches your readers (`StaffSystems.IsLaunch`, `VehicleFightFromCoverNodes`) — your edit when it lands.

⛔ Fences kept: no behaviour changed.

---
_backend lane, 2026-10-20_
