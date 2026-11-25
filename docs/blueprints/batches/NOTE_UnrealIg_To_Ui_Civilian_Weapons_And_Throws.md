# Note: what a civilian holds (pistol / rifle) and throws (stones, grenades) — two questions

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT); FYI the gateway |
| **User, verbatim** | *"I need the civilians to be able to hold a pistol or even a rifle if that is what HROT is sending. And they can also throw some stones … do we have grenades throwing? Is that what Gateway can send? If not, please make a note for the HROT … Stone throwing should work for civilians as well."* |
| **Asks** | §2 |

## 1. What we found (measured, HROT `ui` `a85340796`, gateway `ec4bd26`)

- **Throws are on the wire.** A thrown stone (`TkbStone` 6038) and the grenades (M67, stun, tear gas, smoke) have a `Munition.Visual`, so with
  CE-11050 each throw is a `Round` (shape Grenade, size, colour) and a `Fire` (category `Grenade`). HROT's map draws the throw arm for
  `ThrowSeconds` 0.6 s after the shot (`ShapeKit.ThrowArms`, CE-1195) — the client will do the same from `Fire`, soldier or civilian; the
  `Gesture.Throwing` the client plays today stays for the wind-up.
- **A weapon's look is on its mount** (`WeaponMount.Look`, e.g. the pick-up `9mm_pistol` with `ItemLook.Pistol`, CE-3233); the client reads a
  type's mount CATEGORIES from `TkbType` (`smallarms` for both a pistol and a rifle) and draws one rifle for `Held = Weapon`.

## 2. Asks

1. **What tells the client which weapon a CIVILIAN holds right now** — after he picks one up (CE-3233)? Is it his type's mount + `ActiveSlot`
   (then we read the mount's `Look` from the `TkbType` JSON), or a carried item entity, or nothing on the wire yet? Lean: `PersonState`
   (or an appended field) names the held weapon's `ItemLook` (Pistol / Rifle / …) whenever `Held = Weapon`.
2. **Confirm a throw's `Fire.Origin` is the hand** (the client starts the arm and the round there) and that a civilian's stone throw sends a
   `Fire` like a soldier's grenade.
