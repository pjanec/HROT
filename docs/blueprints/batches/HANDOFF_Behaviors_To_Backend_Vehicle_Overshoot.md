<!--STATUS
state: LIVE
updated: 2026-10-15
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: the cycle's short hide ↔ firing-point drives (where it was measured)
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — OWNS the ground domain's wheeled / tracked motion models (where the fix lives)
-->

# HANDOFF — behaviors → backend: a ground vehicle overshoots a short move by its braking distance, into a building (CE-2198)

**Dispatched at `4d692dc11`** (behaviors). Your scope is frozen at that sha; nothing here stops an item.

## The measurement (live `ua-tank-corner`, test-town, `2026-10-15`)

An M1 (100) on `VehicleFightFromCover` was sent to a hide at **(251.25, 107.67)**, 37 m north of its start, `ArrivalRadius` 2, speed 0
(⇒ the type's own, 20 m/s):

| sim s | position | what |
|---|---|---|
| 8.1 | (251.3, 105.3, 0) | at the hide |
| 10.8 | (251.2, 116.7, **15**) | 9 m PAST it — inside the 4-storey block [230..290 × 110..170], lifted to its 15 m roof |
| 13.5 | (245.1, 134.1, 15) | 26 m past — still inside the building |
| 18.9 | (251.9, 106.4, 0) | back at the hide (the follower turned round) |

26 m is the M1's braking distance from its top speed (v² / 2a = 20² / (2 × 4) ≈ 28 m with `MaxSpeedFwd` 20, `MaxDecel` 4): the
follower runs the short leg at full speed and only brakes once the point is passed. And nothing stopped the hull at the building's wall —
it drove inside the footprint and the height sampler put it on the roof.

## What we ask (two items, your design)

1. ⭐ **A short move arrives**: the speed profile brakes for the destination (no overshoot past `ArrivalRadius`), at any requested speed.
2. ⭐ **A hull never enters a building footprint**: either the follower is blocked by static colliders, or the sampled height of a footprint
   never lifts a ground vehicle onto a roof (the ground domain cannot climb a wall).

## What behaviors did meanwhile (no dependency on you)

`VehicleFightFromCover` now creeps at **5 m/s** by default between hide and firing point (a tactical move; braking ≈ 3 m) — this hides
item 1 for the cycle, not for other movers (`MoveToLocation` at a vehicle's own speed, convoys).

**Acceptance:** ★ a tank sent 37 m at its top speed stops within `ArrivalRadius` · ★ a tank driven at a building's wall stops at it.

---

# ② (CE-2199, added `2026-10-15` at `5c6c3f7f1`, a new item — ① above unchanged) — an aimed burst with cooldown 0 fires EVERY FRAME

📐 Live `ua-tank-corner`: an M1 fired **8 rounds of its 120 mm in ~1.3 s** (the muzzle moved ~0.8 m per shot at a 5 m/s creep ⇒ ~6 rounds/s)
against a catalogue `RateOfFire` of 6 per minute. `AimAndFireExecutor.cs:275` sets `CooldownSecondsRemaining = p.CooldownSeconds`, and every
caller that means "the weapon's own" passes **0** — `VehicleFightFromCoverNodes` (`FireCooldownSeconds` 0 ⇒ documented "the weapon's own"),
`AirSense.FireAt` (`PostureNodes.Fire(…, 0f, 0, air)`). Nothing resolves the 0, so the mount fires each frame until its ammunition runs out.

⭐ **Ask:** in the executor, a cooldown ≤ 0 ⇒ the FIRED mount's own interval (60 / `RateOfFire`, and its reload when the magazine empties) —
only the executor knows which mount `MountAuto` picked (a tank's 120 mm 10 s vs its coax 0.1 s). ⚠ Blast radius: every caller passing 0 slows
to its weapon's real rate (the vehicle cycle, the air defence; the infantry nodes pass 0.25–0.3 s and are unchanged).

**Acceptance:** ★ a tank on `VehicleFightFromCover` fires its main gun at most once per 60 / `RateOfFire` s · ★ a cooldown > 0 is kept as given.
