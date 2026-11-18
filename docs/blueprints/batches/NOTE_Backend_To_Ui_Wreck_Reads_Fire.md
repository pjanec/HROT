<!--STATUS
state: LIVE — FYI for ui (a cross-lane edit, said)
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md — §3b / §4g: fire as simulation state (CE-4660)
  - ../../DESIGN_Visual_Effects.md — W3 / VE-P, the wreck's flames (yours)
-->

# NOTE — backend → ui: the W3 wreck flames now READ the fire (CE-4660)

FYI. Backend edited your `Hrot/Engine/Hrot.Presentation/Effects/EffectSystems.cs` (`WreckSpawnSystem`) and three of your rails for CE-4660
(thermal drivers TD-16, approved R-4293: *"the presentation's W3 burn READS it (one truth, ruling 9)"*):

| what | |
|---|---|
| the trigger | flames + smoke when the hull's replicated `FireState` STARTS burning — or is FIRST SEEN burning (a late-joining host now shows the fire it joined) |
| ⛔ was | this host seeing `Health` cross 0, for a vehicle family (`Burns(type)` — retired) |
| who decides a hull burns | the simulation: `BurnSystem` on the Health owner, once at the death crossing, for a type whose thermal data has `FireSeconds` > 0 (by default every wheeled / tracked / boat / aircraft body; a person, a weapon system, an obstacle never) |
| VE-P kept | a wreck loaded already dead has no fire and shows none; off during a replay |
| your rails | `EffectTests` W3 ×3, `AirCombatLooksRail.ADeadJet_*`, `Map3DFrameRail.W_DestroyedEntities_*` now run `BurningWrecks` (Presentation.Tests/Effects) — the real chain: the kill → `BurnSystem` lights the hull → `WreckSpawnSystem` shows it |

⚠ Your **CE-4671** Look library (LK) moves this system: take the FireState trigger with it. Nothing else for you to build.
