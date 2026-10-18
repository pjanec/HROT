<!--STATUS
state: LIVE — behaviors → backend: CE-2273 (DA-A, proficiency) has landed; BR-6 / BR-7 may start.
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §8a DA-A as designed and built (the component, its sources, the reader)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Defend_And_Attack.md — the frame this note unblocks (BR-6 spread, BR-7 acquisition)
-->

# NOTE — behaviors → backend: proficiency landed, BR-6 / BR-7 are unblocked

**On `behaviors`, in the commit that carries this note.** Row: [`CE-2273`](../Blueprint_Issues_Tracker.md) — built.
Design: [Defend & Attack §8a](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md).

| for you | what |
|---|---|
| ⭐⭐ **the one call for BR-6** | `Fdp.Toolkit.Behavior.Components.ProficiencyOf.SpreadFactor(view, shooter)` — Untrained 2.5 · Militia 1.6 · Regular 1.0 · Veteran 0.8 · Elite 0.65, linear between; **exactly `1.0f`** with no component, a zero-filled one, Regular, or an unregistered world (railed bit-exact), so your "× 1.0 is today" rail holds |
| ⭐ **for BR-7** | `ProficiencyOf.Value(view, observer)` (0–1, Regular 0.6) or `ProficiencyOf.Tier(...)`; scale the acquisition delay as you designed it; keep 0 at full exposure within 100 m |
| ⭐ **what it reads** | the man's OWN `CombatProficiency` (component 415, registered on every node by `HrotSharedComponentRegistry`) — never his commander. A unit's value is WRITTEN onto its men by `ProficiencyFillSystem` on their brain node, so the reader needs only the man |
| ⚠ **the wire is yours to decide** | the component is NOT replicated today. If the shot is resolved on a node other than the shooter's brain node (SimHost), either replicate `CombatProficiency` (three floats + a flag byte, no handles) or carry the factor with the shot. Without that, a shot resolved on SimHost reads Regular (neutral — safe, just not skilled) |
| ⚠ no insurgent TKB entry exists | `BehaviorProfileDto.DefaultProficiency` is built and projected at spawn; type 505 has no catalog entry to set Militia on. Scenarios set it with the key `"Proficiency"` (a number or a tier name) on a man or a unit |

---
_behaviors lane, 2026-10-19_
