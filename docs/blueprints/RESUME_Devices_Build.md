<!--STATUS
state: LIVE
updated: 2026-11-27 (overnight autonomous build started — user: "Go autonomously overnight")
current-answer: §1 (state) · §2 (the queue) — read §1 first after a compaction
stale-below: nothing
related-designs:
  - ../designs/devices/DESIGN_Simulated_Devices.md — THE design (READY-TO-BUILD; §3.14 what the capability cleanup built; §3.5 S1a as built; §5 the slices)
  - ../DESIGN_Capability_Components.md — the cleanup that ran first (C0–C4 built)
-->

# Resume — the simulated-devices build (coordinator lane)

🔒 User, `2026-11-27`: *"yes, go ahead autonomously"* (READY-TO-BUILD, measurements, mockups, S1a) · *"Go autonomously overnight"*.
Branch `coordinator` (no other lane active — the user, same day). ⛔ Per CLAUDE.md: feature unit tests suffice as gates (no scenario runs
except as a slice's own proof in S1e); red-prove the key rail; fold the as-built into the design; commit by path, push after green.

## 1. State

| step | state | commit / note |
|---|---|---|
| design READY-TO-BUILD, Q-10 + R7-13 measured | ✅ | `0082ed5a6` |
| HUD mockups (6 panels) | ✅ | `32d3f2432` — `docs/designs/devices/mockups/devices-hud.svg` |
| **S1a** device tick (`DeviceTickSystem` = a `BrainTickSystem` subclass, `DeviceInfo` id 219) | ✅ | `563651aa7` — 6 rails |
| **S1b-1** the operator: `PossessionMode.Operated`, `DeviceOperators` (the walk, run by the device pre-pass), possession's release paths and frame loop skip `Operated` | ✅ | `DeviceOperatorTests` (4, red-proved) |
| **S1b-2** device frames: `DeviceControl` (axes, press counters, trackers, body offset, Viewed) in its OWN `DeviceFrameEvent` (event id 5204) → `DeviceInput` (component id 228), ingested by the device tick; `PossessionSystem.ApplyFrame` the one body routine | ✅ | rail red-proved; ⚠ deviation: not in the body frame (CE-11027 pins its recorded layout) |
| **S1b-3** `MountKinds.Device = 32`; four KIT stations appended to `CarrierDefDto.PersonCarrier()` (seats 4–7, Accepts Device only); `DevicePose` (id 229) + `DevicePoses.TryWorld` | ✅ | rails red-proved; ⚠ every person type's seat data changed ⇒ goldens move hash-only (CarrierSeats / CarrierStations / PassengerBuffer) — like CE-4711 |
| S1c the rangefinder: `LaserPointing{Power}` / `LaserSpot` / `LaserSpotSystem` / `DevicePanel` / the device folder | ⏳ | — |
| S1d the HUD to the mockups: `HudScope`, `BeginScope`/`EndScope`, bound-view draw, binoculars device replaces CE-1219 | ⏳ | — |
| S1e the scenario grammar + SD-S1 scenarios | ⏳ | — |

## 2. Rules learned this run (do not re-learn)

- `[TickMode]` is `Inherited = false` — a subclass states its own mode.
- ⛔ A lambda capturing a LOCAL in a per-frame method allocates its closure on every entry (32 B/frame, S1a) — build such things in their own method.
- A refused start must not be retried every frame (string + allocation).
- `CgfLogicPackTests` counts the CGF pack's systems — adding a system updates three counts.
- ⛔ Never add a field to `OperatorControlFrameEvent` / `OperatorInput` / `PossessedBy` / the possession events: `PossessionReplicationTests` pins their recorded layout hashes (old recordings must replay). New data = a new event / component.
- A person now has 8 seats (`PersonCarrierTests.TheProfile_IsStampedFromTheType`).
- The composition snapshots (`TheHostCompositionIsFrozenTests`) were stale before this work — CE-4710; do not chase them.
