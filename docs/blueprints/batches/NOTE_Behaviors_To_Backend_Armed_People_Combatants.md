<!--STATUS
state: LIVE — behaviors → backend: a third note to the armed-people frame (rule 1: earlier ones are not edited).
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5h AW-22, AW-23 (R-2028) OWN it.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Armed_People.md — the frame (B-7…B-14).
-->

# NOTE 3 — behaviors → backend: two engine pieces for combatants *(R-2028)*

| # | item | design basis | ⭐ lean | acceptance |
|---|---|---|---|---|
| **B-15** | **the SHOOTER on the damage**: `DamageAssessedEvent` and the wire's `EntityHitDamage` carry who caused it (APPENDED, unset = unknown — every existing sender reads as before). SimHost knows it at the hit (`HitEvent.BulletEntity` → `BallisticProjectile.Shooter`) and for a burst (the round's shooter) | AW-23 (a player who kills his friends) | the same append pattern as `CE-3219`'s `Effect` / `EffectSeconds` | a hit on CGF names its shooter; an unattributed one (a fall, a cloud) says unknown |
| **B-16** | **`HostileAct` carries its KIND — lethal or not** (the component CV-4 plans, replicated, owned by the actor's creator) | AW-22 (a lethal act makes a civilian a combatant) | a byte beside `SinceSimTime`; behaviors writes it through the existing write path | a stone thrown stamps non-lethal; a shot at the forces stamps lethal; both reach every node |

The per-unit "known combatant" mark (a byte per `TargetMemory` slot, shared through the squad merge) and the fratricide counter are
behaviors' (`CE-2238`). Order: after B-7.
