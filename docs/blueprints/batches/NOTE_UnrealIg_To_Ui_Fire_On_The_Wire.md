# Note: fire on the DPS wire — what Unreal draws today, and what is missing (a burning building?)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT) and the gateway |
| **User, verbatim** | *"we also need a flamethrower guy … and burning guy and burning ground etc, HROT supports that, ask"* |
| **Asks** | §2 |

## 1. On the wire and drawn by Unreal (contract at gateway `86b363f`)

`FlameJet` (per mount: aim point, range, half angle, intensity, burst start, burn seconds) → the flame from the muzzle; `PersonState.Burning` → a
body-sized fire on the man; `FireSpots` (per holder: position, radius, intensity, since, seconds) → a fire per spot; `GroundMarks` → scorch.
To check live next: `fl-flamethrower-cone`, `fl-timber-house`, `fl-napalm-strike` (basic-desert).

## 2. Questions

1. **A BURNING BUILDING** (HROT: `FireState` on the building damage entity, `BuildingDamageSystem.cs:157-183`; `fl-timber-house`): DPS
   `BuildingDamage` has no fire field. Is the building's fire sent as `FireSpots` (on the damage entity / the building), or not at all? Lean:
   appended to `BuildingDamage` — burning, intensity, since (and burnt-out), so the client can light the house, smoke it and char it.
2. **Anything else that burns and is not on the wire** — a burning vehicle beyond `WreckFlame` effects, burning trees / vegetation, a napalm
   field's area (as `FireSpots`?), smoke columns? Please list what HROT models and how each travels.
3. **The flamethrower man's look**: is the flame tank / gun a `HeldLook` (T-076) or must the client infer it from his type's mount
   (category Flamethrower)? Today the client infers it from the jet.
