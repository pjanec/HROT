<!--STATUS
state: LIVE — dispatched to the ui lane
updated: 2026-11-28
current-answer: the whole file
related-designs:
  - ../../designs/devices/DESIGN_Simulated_Devices.md — OWNS this: §3.10a (SD-S5d — D-163–D-167, the class diagram), D-63 / D-64 (raised while viewed; drawn where it is), D-61 / D-120 (stacking)
  - ../../designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md — OWNS the shared look library (Hrot.Look) the device kind joins
-->

# HANDOFF — coordinator → ui: devices DRAWN in their forms (SD-S5d)

**Dispatched at:** the `coordinator` commit that adds this file. ⛔ **Your scope is FROZEN at that commit** — later documents are FYI only; a later document
that invalidates an item ⇒ STOP that item and report it. Merge `coordinator` first.

🔒 User, `2026-11-28`: *"If there are the device types already settled in tkb we could also ask ui to allow rendering them in their various forms"*.

## 1. Goal

A device entity is drawn as ITSELF, where it is: raised in the trainee's hands while his view shows it, mounted at a vehicle or weapon station, stacked on
another device — from ONE device-look rule in the shared look library.

## 2. Design basis — build THIS

[`DESIGN_Simulated_Devices.md`](../../designs/devices/DESIGN_Simulated_Devices.md) **§3.10a** (its class diagram is the build contract):

| decision | what |
|---|---|
| D-163 | `DeviceLooks.KindOf(type)` → `DeviceKind { Binoculars, Rangefinder, Designator, DroneController, Console, Box }` + size + colours, in `Hrot.Look` (the IG will share it); the kind is TKB data — `DeviceDefDto.Look`, defaulted by behaviour; ⛔ never from a name |
| D-164 | the forms NOW: ① raised in his hands while viewed (the device's own kit replaces the generic binoculars held item); ② mounted at a station (the observer HMMWV, type 120, station 7) — at the station place, turned by `DevicePose`, the mounted-weapon path; ③ stacked on a device — ⚠ waits for SD-S5b (building on `coordinator` now: the pose chain); a carried, unraised device is not drawn |
| D-165 | later: on a tripod (D-60), lying where dropped (SD-S5) — the kit is ready for them |
| D-166 | the place always from `DevicePoses.TryWorld` |
| D-167 | mockups first: the six kinds' block shapes at block-figure scale + the forms |

## 3. What exists (measured `2026-11-28`)

| | where |
|---|---|
| the device types 640 (rangefinder), 641 (binoculars), the observer HMMWV 120 (station 7 starts with a rangefinder) | `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/NedTkbCatalog.cs` (`RegisterDevices`, `ObserverSeats`), `DeviceDefDto` (`FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/`) |
| the generic held binoculars (drawn for every raised device today) | `Hrot/Engine/Hrot.Presentation/Map3D/BlockFigureHeldItems.cs:19-68`; the raise: `CarriedDevices.AnyViewed` → `HeldItems.BinocularsUp` (S1d-3) |
| the mounted-weapon drawing to copy | `WeaponKits` (`Mounted`), `EntityBodyLayer3D.TurnedWithStation` (`:1487`); the shared kind rule `FiringBlasts.KindOf` in `Hrot.Look` (CE-4671) |
| a device's world pose | `DevicePoses.TryWorld` (`FDP/Toolkits/Fdp.Toolkits/Behavior/Devices/DevicePose.cs`) |
| scenes with devices | `scenarios/demos/devices/` (dv-range-targets: a held rangefinder; dv-vehicle-station: the observer HMMWV; dv-switch-devices: binoculars + rangefinder) |

## 4. Fences

- ⛔ No change to the device components, the device tick or the device behaviours (the coordinator lane builds them; SD-S5b is in flight — touch nothing in `Behavior/Devices/` but read it).
- ⛔ No tripod device and no dropped device (D-165 — not in the model yet).
- ⭐ `DeviceDefDto.Look` is the ONE coordinator-owned field you may add (TKB data, defaulted) — say so in the report.
- ⭐ R-4143 / R-4146: mockups first, then build to them and check fidelity yourself.

## 5. Acceptance — frame rails under `xvfb-run -a` (a skip is not a pass)

1. `DeviceLooks` rails: every device type in the catalogue has a kind; the default by behaviour; 0 B.
2. Frame rails: a man viewing his rangefinder holds a RANGEFINDER (not binoculars), viewing binoculars holds binoculars; an unraised carried device is not drawn; the observer HMMWV's rangefinder is drawn at its station and turns with the device's pose; (after SD-S5b merges) a stacked device sits on its host and turns with it.
3. 0 B per steady frame for the added drawing.
4. Red-prove one rail with a compiling mutation.

## 6. Report

The gate table (exact command · pass / fail / skip, reds confirmed pre-existing at your base sha), the mockup path, the render paths (scratchpad, never git),
an "SD-S5d AS BUILT" paragraph in §3.10a of the design, the ids you allocated (none expected).
