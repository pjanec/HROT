<!--STATUS
state: LIVE
updated: 2026-10-20
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — §6 L7 `uc-request-chain`, the rung this blocks
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — §8d HQ-1 (a heard launch is a SPOT), the consumer
-->

# NOTE — behaviors → backend: a mortar launch is not heard LIVE (CE-3273 / CE-3276)

**From:** behaviors (`behaviors@<this commit>`), 2026-10-20. **Ask:** measure on your side; ⛔ nothing here is changed by behaviors.

## What was run

`scripts/utility-demo-check.py --launch uc-request-chain` (Urban Battle §6 L7, demo-city): two insurgent 81 mm tubes (610, each crewed by
an Insurgent 2003 with `ManWeapon`) at (650/660, 1580); a covert spotter (`SpotForFire`) calls them on a Blue platoon on Avenue E at y 150
— **1430 m** from the tubes.

## Measured

| | |
|---|---|
| the tube's emitter (SimHost) | `FiringAudibleRange 2000`, `FiringClass 5` (HeavyWeapon), `DetonationAudibleRange 0`, `DetonationClass 6` |
| the tube's emitter (CGF) | the same ranges, `HasLast false` — never stamped there |
| rounds | **1 detonation in 90 s** (shooter = the tube, burst 35 m from the Blue men) |
| Blue hearing | two Blue men's memories sampled every 0.4 s from t 20 to 90: **no anonymous (heard) slot at all** — not the launch at 1.4 km, ⚠ **not even the burst 35 m away** |
| Blue's reports | PROGRESS / SITREP only — no SPOT, so the staff's `ComposeLaunch` (CE-2309) never had a heard launch to compose |

## Two things, I think

1. ⚠ **The burst is unheard because the round's detonation is stamped on its SHOOTER** (`SoundEmissionSystem`: `DetonationNotification.Shooter`
   → that entity's emitter) **and the tube's `DetonationAudibleRange` is 0.** A shell bursting beside a soldier should be the loudest thing in
   his day. Probably the burst's carry should come from the round's warhead (or `Explosion` class), not from the weapon that fired it.
2. **The launch at 1.4 km is unheard although CE-3276 says a loud class carries past the ear.** Not located. Candidates I could not settle from
   my side: the acoustic sensor's cadence vs the 0.5 s `SoundLingerSeconds` (a single launch, not a burst of fire); whether the sensor that hears
   runs where the emitter is stamped; whether an indirect `ManWeapon` round reaches `FireProcessingSystem`'s `WeaponFireNotification`.

## What behaviors does meanwhile

`uc-request-chain-dry` fakes the one missing report (a SPOT IndirectFire, as `pc-defend-dry` fakes its reports) so the rest of the chain —
the platoon's FireSupport request (CE-2310), the HQ's answer (CE-2312), the AH-64's armed search (CE-2311) — runs live now. The full rung
waits for this.
