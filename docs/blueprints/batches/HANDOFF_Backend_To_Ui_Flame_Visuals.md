<!--STATUS
state: LIVE — dispatched (backend → ui) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §6 THE LOOK CONTRACT this handoff builds against (R-3111, approved); §0 the picture
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §3c FireState's three holders and their frames, §4b the BurnedArea ground mark
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §7 / §7a Hrot.Look's effect rules and categories (the flame jet is one more kind)
-->
# HANDOFF — `backend` → `ui`: the flame visuals (CE-4635 flamethrower, CE-4637 napalm)

**Dispatched at** `b24416363` · ⭐ **Your scope is FROZEN at that sha; documents that change after it are FYI.**

🔒 User, `2026-11-19`: *"F-9 lean approved, numbers fine."* (the design, R-3111) · *"what do we need to define for the ui lane to work on flame visuals?"* ·
*"maybe after you first extend the components so they know exactly from code what fields to read"* — ⭐ done: the data below is BUILT and green
(FL-0), so you read real code names, not a plan.

## Goal

Draw fire from the SIMULATION's state, the same on every node and every renderer: a flamethrower's jet, flames on every burning spot (vehicle,
wagon, building, ground), a man on fire, a napalm field and its scorch.

## What to read — ⭐ the contract is [DESIGN_Flame_Weapons §6](../../designs/weapons/DESIGN_Flame_Weapons.md) (by code name; do not re-derive)

| item | read |
|---|---|
| U-1 the **flame jet** | `Fdp.Toolkit.Combat.FlameJets.TryGet(world, unit, now, out Jet)` — mount, aim point / target, reach, half-angle, intensity, burst start; lit ≤ 0.25 s after the last fuel shot |
| U-2 **every burning spot** | `FireState` (`Count`, `Get(i)` → `FireSpot`, `IsBurning(now)`) on ANY holder; a vehicle's spots in its own frame, a record entity's in its `SimTransform` frame. ⛔ replaces today's one-flame-per-wreck rule (`EffectSystems.cs:422`) |
| U-3 a **burning man** | `Effects.IsBurning(view, man, now)` (`EffectState.BurningUntil`) — exposed through `PersonLook` |
| U-4 **scorched ground** | `GroundHeatMarks` items of kind `BurnedArea` — a dark ground decal (length × width) |
| U-5 the **effect data** | `EffectKind.FlameJet` (8): its default visual in `EffectTkbCatalog` + the `EffectRules` row; a spot's size class from its radius |
| — napalm's big smoke | nothing new: the strike's `AreaEffectCloud` (drawn today) |

## Fences

| ⛔ | |
|---|---|
| no simulation | the cone law, ignition, damage, napalm's ellipse are backend's (FL-1…FL-3); you draw what the state says |
| no new component, no new field | if a picture needs data §6 does not give, STOP and send backend a NOTE (it is the contract's gap, not yours to add) |
| U-1 draws the full reach | a wall shortens the jet in the simulation (FL-1); v1 does not ask the renderer to clip it |
| R-4143 | a mockup before the build: a jet into a window, a burning truck, a napalm field, a burning man, a scorch |

## Acceptance

| # | done when |
|---|---|
| A-1 | frame rails (`Map3DFrameRail` family, `xvfb-run -a`) with hand-placed state: a `FireState` with 3 spots on a vehicle, on a building record and on a cell record; a unit whose `FlameJets.TryGet` is lit; a man with `BurningUntil` ahead; a `BurnedArea` mark — each drawn where §6 says, and gone when out |
| A-2 | 0 B per steady frame in the fire / jet draw (CLAUDE.md hot path) |
| A-3 | the wreck scenario `th-burning-wreck` still passes (U-2 replaced its rule) |
| A-4 | the FL scenarios (`fl-clear-the-bunker`, `fl-napalm-treeline`) — when backend's FL-1…FL-3 land; not a gate for this handoff |

## Order

You can start U-2 and U-4 at once (their components exist and are replicated). U-1, U-3 and U-5 read code that landed with this handoff.
