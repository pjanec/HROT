<!--STATUS
state: LIVE — FYI for behaviors (the decision half is yours)
updated: 2026-11-18
current-answer: the whole file (a short note)
related-designs:
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — §6a head out, §6b lights: the simulation half as built
-->
# NOTE backend → behaviors: a crewman's head out and a vehicle's light discipline can be ASKED for now (`2026-11-18`, CE-4670)

The simulation half is built; **who decides** is behaviors' (design VA-3 / VA-4: *"the crew decides"*):

| ask | the call (any node — the interaction transport carries it to the owner) | applied by |
|---|---|---|
| a crewman puts his head out / back in | `VehicleRequests.HeadOut(world, vehicle, man, out: true / false)` — kind 15 | the boarding arbiter (`VehicleOpeningsSystem`): the hatches of his seat's role open and stay open, `OpeningSlot.CrewExposed` set |
| the vehicle's light discipline (Normal · Convoy · Blackout · Off) | `VehicleRequests.SwitchLights(world, vehicle, discipline, actor)` — kind 16 | the lights owner (`LightRuleSystem`): the discipline and, at once, the mask by the rule |

**Cross-lane edit, in your files:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/Objectives/Verbs/VehicleVerbs.cs` *(new)* — two scenario verbs, `HeadOut`
{ target = the vehicle, `role`, `out` } and `SetVehicleLights` { target, `discipline` }, beside `SetObjectiveActive` (same shape: resolve in the phase, act in
`RuleAfterPhase`). They let a trigger ask (scenario `va-crew-switches`); an SOP / order can call `VehicleRequests` directly.

**Not built, and yours** (design §6a / §6b ⏳): an SOP / ROE / order that sets the discipline (a convoy blacked out at night, lights on in a safe area);
an observation behaviour or an order that puts the commander's head out (and takes it in under fire — `CrewExposed` is the hook a hit model can use);
the searchlight's target (`CE-3475`). ⭐ Safe from the parallel brain phase: the ask is an EVENT (the bus defers a publish made in the phase, `FdpEventBus.Publish` →
`ParallelReadCheck.DefersBus`; the owner reads it next frame) — no cross-entity write, the `CustodyRules.Ask` shape.
