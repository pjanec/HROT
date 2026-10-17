# HANDOFF backend → behaviors — how people react to gas (a FRAME: you design the details)

<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: whole frame
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.3b/§7.3c the cloud (built), §7.3d gas as a danger area + the expected behaviours (THIS frame's basis)
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — §10 the danger-area sensor and its kinds (the machinery gas joins)
-->

🔒 **User, `2026-10-17`:** *"avoidance via brain yes if sensor can find a cover point and the person can still see (usually not if hit by
tear gas) so the gas is something like a dangerous area to be avoided by not stepping into and rather taking cover farther from its current
or future expected location"* · *"Behaviors accepted, handoff to behavior lane should refer to the cloud design and expected behaviors.
Danger area kind for gas seems okay."*

## Goal

People treat tear gas as a danger area: they do not walk into it, they take cover away from where it is and where the wind will carry it,
and someone caught in it without a mask escapes blind before doing anything else.

## Design basis — read these, they are the contract

- **The cloud** — [§7.3b / §7.3c](../../DESIGN_Civilians_And_Crowd_Control.md): a replicated `AreaEffectCloud` entity (radius, height,
  kind, born, seconds), asked for with the entity-creation request on the bus (`CE-3228`) and so created and owned by the default
  processor (CGF), drifting once a second on the ground wind. Every node can read every cloud: `AreaEffectClouds.Of(view)`.
- **The expected behaviours** — [§7.3d](../../DESIGN_Civilians_And_Crowd_Control.md) table: avoid · escape blind · mask drill · crowd
  scatter · smoke is cover, not danger.
- **The effect on a person** — already built: a tear-gas cloud sends an Irritant hit once a second to each person inside, which sets
  `EffectState` Irritated (shown as GASSED) for ~2 s, so it holds while he stays in and clears ~2 s after he leaves.

## What backend provides (`CE-3227`, building next)

| | |
|---|---|
| `DangerAreaKind.GasCloud` | reported by the EXISTING danger-area sensor (`DangerAreaSensorSystem` → `DangerAreaResultEvent`), ask for it with `DangerAreaKinds.GasCloud` |
| its footprint | the cloud now ∪ where the wind takes it within ~15 s, plus a margin; `NearSideHandle` / `FarSideHandle` are the detour points either side |
| ⏳ later | a gas-mask TKB entry (immune to the irritant, narrower vision) — say if you need it in this round |

## Fences

- ⛔ No navmesh change and no second danger mechanism: gas is a kind in the existing danger-area machinery.
- ⛔ A gassed person (Irritated) does no cover search — he cannot see (user).
- ⭐ Smoke stays sight-only; it is not a danger area.

## Leans (yours to change, say why)

| question | lean |
|---|---|
| where the reactions live | a reaction in the brain's reaction slot (the `Demo_TakeCover` family) for "gassed", and the danger-area reaction (your `DangerAreaNodes`) choosing detour / wait for `GasCloud` instead of the crossing manoeuvre |
| escape direction | upwind and away from the cloud's centre, a few metres past its edge |
| civilians | the CivilianHsm's flee, away from the cloud and the launcher |

## Acceptance

- A unit ordered to move through a gas cloud detours or waits; it never enters it.
- A unit caught inside without a mask leaves it within a few seconds, without first running a cover search.
- Rails in your behaviour suites; ⭐ name the backend sha you built against.
