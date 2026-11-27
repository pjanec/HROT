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
| **S1c** the rangefinder: `LaserPointing` (231, + request `Seq`) / `LaserSpot` (232) / `LaserSpotSystem` (muscle, `CombatModule` PostSimulation) / `DevicePanel` (233) / `Hrot.AI.Behaviors/Devices/Rangefinder/` (`DeviceRangefinder`) | ✅ | `6cf30d6a0` (pushed) — `LaserSpotSystemTests` (8), `RangefinderBehaviorTests` (7, end to end + 0 B, parallel checks green); two red-proofs; deviations in the design's S1c AS BUILT (no TKB `DeviceDefDto` yet; `DevicePose` from the frame → S1d) |
| **S1d-1** `HudScope` meta-primitive, `BeginScope`/`EndScope`, the one run-skip helper, `FrameFor` keeps the bound source's run, reader rails, R7-13 contiguity | ✅ | the S1d-1 commit — `HudScopes` (the one reader), `BeginScope`/`EndScope`/`AppendHeld`, `DebugGizmoLayer.BoundSource` + `ViewFrame`; rails in `HudScopesTests`, `DebugPrimitiveBufferLayerTests`, `DebugGizmoLayerGizmoTests`, `GizmoFramePanelTests`; ⚠ R7-13's threaded-node rail not built — the detector `ForeignAppendsInScope` is, S1d-2 asserts it in a threaded run (design S1d-1 AS BUILT) |
| S1d-2 the rangefinder HUD projector (DeviceHud layer, visibility policy, screen pass) to the mockups + fidelity renders | ⏳ | — |
| S1d-3 the binoculars device replaces CE-1219 (`Viewed` raises the held item), `DevicePose` from the station look | ⏳ | — |
| S1e the scenario grammar + SD-S1 scenarios | ⏳ | — |
| **queued (user `2026-11-27`)** SD-S4b steer the round in flight (both modes, crosshair first) + the missile's-view toggle — design §3.13a (D-80–D-91), §5 row, §5a `dv-steer-*` (5), §9 Q-15 | ⏳ | after SD-S4 `FireControl`; real joystick = SD-S2 |

## 2. Rules learned this run (do not re-learn)

- `[TickMode]` is `Inherited = false` — a subclass states its own mode.
- ⛔ A lambda capturing a LOCAL in a per-frame method allocates its closure on every entry (32 B/frame, S1a) — build such things in their own method.
- A refused start must not be retried every frame (string + allocation).
- `CgfLogicPackTests` counts the CGF pack's systems — adding a system updates three counts.
- ⛔ Never add a field to `OperatorControlFrameEvent` / `OperatorInput` / `PossessedBy` / the possession events: `PossessionReplicationTests` pins their recorded layout hashes (old recordings must replay). New data = a new event / component.
- A person now has 8 seats (`PersonCarrierTests.TheProfile_IsStampedFromTheType`).
- The composition snapshots (`TheHostCompositionIsFrozenTests`) were stale before this work — CE-4710; do not chase them.
- ⭐ S1c: a request seq (`LaserPointing.Seq` echoed by `LaserSpot.PointingSeq`) is what lets a device tell THIS emission's spot from a stale one — reuse it for any device that asks the muscle.
- A curated device behaviour needs `[Behavior(Params = NoParams, Requires = new[]{ typeof(DeviceInfo) })]` (CE-1431 wants a contract; `Requires` keeps it off every unit's picker) and moves `Snapshots/BehaviourHosting/hosting.txt` by one line (`HROT_RECORD_HOSTING=1`).
- `StatefulAction` on a `BTreeBuilder` is an extension in `Hrot.AI.Behaviors.Brains` — a behaviour outside that namespace needs the `using`.
- `SpatialHashGrid` is a STRUCT: a test keeps it in a non-readonly field and re-publishes `SpatialGridData` after `Add` (origin via `Create(…, originX, originY)`).
- `SimHostCoreLogicPackTests` asserts `SimulationSystems.Count == 28` and the pack has 29 — stale before S1c (S1c adds only a PostSimulation system); not chased.
- `TheTaskPickerTests.H5_TheOpenPicker…` counts (14→18, 6→7) are stale since `0926ff85c` (2026-11-09) — one curated device behaviour behind `Requires` cannot move a folder by 4; not chased.
- ⭐ S1d-1: `Fdp.Diagnostics.Contracts` sees GizmoMap.Contracts only through `extern alias GizmoMapContracts` — a new GizmoMap type it uses needs a `global using` line in its `TypeForwards.cs`.
- A HUD-run reader takes `HudScopes.Keep` / `Unscoped` (a span filter, 0 B, the frame itself when no scope) or `After` (index loop); a buffer that RE-APPENDS someone else's primitives uses `AppendHeld`, never an `AppendRaw` loop (the count must be re-stamped for its own slots).
