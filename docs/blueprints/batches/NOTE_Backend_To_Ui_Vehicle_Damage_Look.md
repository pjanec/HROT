<!--STATUS
state: LIVE — FYI for ui (a cross-lane edit in your map files)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6c VA-S1 as built
-->
# NOTE backend → ui: VA-S1 draws partial damage on our 3D map (`2026-11-18`, CE-4670)

**Cross-lane edit, in your files** (the handoff assigned it to backend and asked to say it):

| file | change |
|---|---|
| `Hrot/Engine/Hrot.Presentation/Map3D/EntityBodyLayer3D.cs` | around `DrawParts`: two fields (`_damageMask`, `_turretThrown`) set once per vehicle from `VehicleDamageDraw`; a destroyed part is drawn with `PartColour(…, wreck: true)`; a Catastrophic wreck's turret and gun are drawn through `VehicleDamageDraw.ThrownTransform(size)` |
| `Hrot/Engine/Hrot.Presentation/Map3D/VehicleDamageDraw.cs` *(new)* | the kit-part → simulation-part map (turret / gun by role; a running-gear part — Hull role, low on one side — by position), the destroyed mask from `Hrot.Look.Vehicles.VehicleLook`, the thrown pose |
| `Hrot/Engine/Hrot.Presentation.Tests/Map3D/VehicleDamageDrawTests.cs` *(new)* | 3 rails (the Tank kit's mapping, a left-track kill darkens only the left track, the thrown turret lands beside the hull) |

⚠ Yours to tune: the thrown pose is a starter (tipped onto the left side, beside the hull). A kit part carries no simulation part id, so the
running-gear mapping is by position (`Centre.Z < 0.3`, `|Centre.Y| ≥ 0.3`); a part id on `KitPart` would make it exact.
`VehicleLook` (in `Hrot.Look/Vehicles/`) is the reader for `PersonLook`'s sibling; `PartState(part, centre, now)` answers Burning when the renderer
gives the part's centre in the vehicle's frame.
