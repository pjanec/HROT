# Reactive Guards

A **reactive guard** is a condition that is re-evaluated every simulation tick.
When the condition transitions from `false` to `true` (rising edge), the guard _fires_,
triggering associated behavior. Guards in Hrot are **level-triggered evaluation** with
**edge-triggered firing**.

---

## Why reactive guards?

Imperative AI (sequences, coroutines, channel commands) works well for _directed_ tasks:
move here, aim there, wait for result. Reactive guards complement this with _opportunistic_
responses to world state changes: "whenever health drops below 30%, switch to cover behavior."

---

## The three implementations

Each Hrot AI subsystem has its own reactive guard primitive. They are equivalent at the
concept level; pick the one that fits your execution model.

### BTree -- Observer Selector

An **Observer Selector** re-evaluates its guard children every tick from the root.
If a higher-priority guard child becomes true, it preempts any lower-priority running child.

- **When to use:** You're already authoring a BTree and need reactive branching.
- **Hosting rule:** Works in any BTree, including both `AiPrimitive` and full BTrees.
- **Note:** Non-observer selectors (plain Selectors) do NOT re-evaluate; they resume
  the currently running child.

### HSM -- Transition Guard

A **transition guard** is a predicate bound to an HSM transition.
While the source state is active, the guard is re-evaluated every tick.
When it becomes true, the transition fires (subject to event matching).

- **When to use:** You're already authoring an HSM and want a self-resetting condition
  to trigger a state change.
- **Hosting rule:** Guards are attributes of transitions, not nodes. Set the Guard field
  in the transition inspector.
- **Performance note:** Guards are polled every tick while the source state is active.
  Keep predicates O(1).

### Instance Blueprint -- When Node

A **When node** re-evaluates its condition every tick (or on rising/falling edges).
When the configured edge fires, the `OnFired` exec output triggers.

- **When to use:** You're already authoring an Instance Blueprint and need to respond
  to world state transitions.
- **Hosting rule:** Instance Blueprints only. `AiPrimitive` Blueprints stay imperative
  (use BTrees or HSMs for reactive behavior in primitives).
- **Modes:** Value Changed, Event Fired, Condition Met, EQS Result.
- **Cross-subsystem note:** If familiar with Observer Selectors or HSM transition guards,
  When nodes serve the same role in Instance Blueprints.

---

## Hosting rules summary

| Reactive guard type | AiPrimitive | Instance Blueprint | BTree | HSM |
|---|---|---|---|---|
| Observer Selector | Yes | -- | Yes | -- |
| Transition Guard | -- | -- | -- | Yes |
| When Node | -- | Yes | -- | -- |

---

## Performance characteristics

All three poll every tick. Keep guard predicates:
- Pure (no side effects)
- O(1) -- cache results, do not iterate collections inside a guard

---

## EQS helpers: not reactive guards themselves

`SpawnEqsSensorNode` and `ReadEqsResultNode` are **EQS-specific helpers**, not reactive
guards. They live in the **"EQS" palette category** for this reason.

`WhenNode` in **EQS Result** mode is the reactive guard; the EQS nodes supply its input:

```
SpawnEqsSensor --> [handle] --> WhenNode (EQS Result mode) --> OnFired --> behavior
                  [handle] --> ReadEqsResult --> Entity, Position, Score
```

---

## Canonical patterns and recipes

- `CoverAwarePatrol.bp.json` -- EQS pipeline with WhenNode (EQS Result mode)
- `HealthThresholdReaction.bp.json` -- WhenNode (Condition Met mode)
- `SquadAwareEngagement.bp.json` -- WhenNode (Value Changed, PeerBlueprintVariable source)

Recipe files live in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/`.
