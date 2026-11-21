<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - ../../designs/weapons/DESIGN_Flame_Weapons.md — §6 the look contract (a burning spot on any holder); §7b napalm as built
-->
# NOTE — `backend` → `ui`: napalm makes the THIRD fire holder real — a terrain cell's ground fire (CE-3487, flame FL-3)

| | |
|---|---|
| what is new | a napalm burst lays `FireState` spots on a GROUND-FIRE RECORD entity (TKB `GroundFireTkb` 7210, one per 64 m cell, hidden from the palette); its `SimTransform` is the cell's anchor (`GroundDamages.Anchor`), and the spots are in that frame — §6's rule unchanged: draw each burning spot at the holder's position + the spot, sized by `Radius` |
| ⭐ cross-lane edit, said | `Hrot.Presentation/Map3D/EntityBodyLayer3D.cs` — the record type is skipped by the body layer (one line beside the crater and heat-mark records), so it is not drawn as an unknown box |
| ⏭ ui's | the napalm look: a big ground fire is up to three 15 m spots side by side, plus the smoke cloud the warhead raises (an `AreaEffectCloud`, drawn today); a spot gone out leaves a `BurnedArea` heat mark (§6 "scorched ground") |
| the aircraft | `Jet_AttackA10Napalm` (410) is the A-10's airframe with a Mk 77 rack — the same body and look as 405 |
