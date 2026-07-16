# Blueprint Authoring — worked examples per use case (FOR REVIEW, nothing built yet)

> **Purpose:** make the architect's "orchestration over C# leaf-helpers" model concrete, so we can
> judge *purity vs practical need* before committing. Each example shows the **C# helper** a
> programmer writes and the **blueprint graph** a designer wires, plus an honest **verdict** on how
> much is actually visual. Graph sketches are `A ─► B` flow; `[param]` = a value from the blackboard.

## Conventions (from the architect's answers)

- **Read/condition** → a `FunctionCall` to a C# helper whose signature *ends* with `Entity self,
  ISimulationView view`. The editor hides those trailing params from the visual pins; the compiler
  auto-appends them. **Read-only** (`ISimulationView`, never `EntityRepository`).
- **Write / command / event** → a `[SharedAiAction]` node; its helper receives `Entity self,
  EntityRepository world` and may publish to `world.Bus` mid-tick (publishing is not a structural
  mutation).
- **Cross-entity event** → publish a deferred `BlueprintDeferredEvent{target, eventNameHash, payload}`;
  consumed next frame's Input phase (**one-frame latency**, accepted).

---

## 1. Read an ECS component (condition) — `Condition_HasTarget`

```csharp
// Trailing (self, view) are auto-bound + hidden from pins. Read-only.
public static bool HasTarget(uint targetNetworkId, Entity self, ISimulationView view)
{
    // singleton lookup + TargetMemory array scan — stays in C#
    ...
    return found && threatScore > 0f;
}
```
```
EventEntry ─► FunctionCall "HasTarget"     ─► Branch ┬─true─► Return(Success)
              (visible pin: targetNetworkId←[param])       └─false─► Return(Failure)
```
**Verdict:** the *decision* (call → branch → return) is visible; the *scan* is C#. Reasonable — the
scan isn't graph-worthy. **Blueprint value: medium.** Needs GAP-7 (context-aware FunctionCall).

## 2. Issue a movement command (action) — `Action_ReverseToBaseline`

```
EventEntry ─► ChannelCommand(Locomotion · MoveTo, pos←[param Baseline], ReverseAllowed=1)
           ─► WaitForChannel ─► Return(Running → Success)
```
**No C# helper needed** — locomotion is a first-class `ChannelCommand`. **Blueprint value: high.**
This is where blueprints genuinely shine — the whole action is visual today. (Its extra
"publish ClearBehavior on terminal" is example 3.)

## 3. Publish an FDP event (action) — `ClearBehavior`, and cross-entity order dispatch

```csharp
[SharedAiAction]                                   // gets self + world; world.Bus is the FdpEventBus
public static NodeStatus ClearOwnBehavior(Entity self, EntityRepository world)
{
    world.Bus.Publish(new ClearBehaviorEvent { Entity = self });   // same-entity, this frame
    return NodeStatus.Success;
}
```
```
EventEntry ─► Action "ClearOwnBehavior" ─► Return(Success)
```
Cross-entity (commander → subordinate order) — deferred, one-frame latency:
```csharp
[SharedAiAction]
public static NodeStatus DispatchOrder(Entity target, /*…scalars…*/ Entity self, EntityRepository world)
{
    world.CommandBuffer.PublishEvent(new BlueprintDeferredEvent {
        Target = target, EventNameHash = Hash("AssignTacticalIntent"), /*payload*/ });
    return NodeStatus.Success;
}
```
**Verdict:** publishing one event is a clean visual action. **Blueprint value: medium-high.** Needs
GAP-3 (a catalog-gated `[SharedAiAction]` publish node + the deferred-event plumbing).

## 4. Respond to an event — an **Event node** per event (handler entry, Unreal-style)

```
Event "Move Completed" (─out Reason) ─► Branch(Reason == Arrived) ─► …
Event "Hit"            (─out Damage)  ─► …                                // a 2nd handler in the same graph
```
An `EventEntry` with a specific `EventTypeId` = "when this event fires, run this chain." Same
`EngineEventCatalog` dropdown as `PublishEvent`, mirrored: the event's fields are **output** pins (the
payload). Drop several — multiple handlers per graph. `When` (edge/threshold) and `WaitForEvent`
(inline mid-sequence pause) are *different, narrower* tools — see the New-Node Authoring Guide §1b.
**Verdict:** the intuitive, correct shape for reacting to events. **Blueprint value: high.**
*(Caveat: multi-handler scheduling to be verified/completed; `WaitForEvent`'s FQN bug is only relevant
to that niche pause tool, not this handler path.)*

## 5. Loop over a roster/array — `Action_DispatchAllToBaseline`  ← the contentious one

**Architect's model (no loop node):** the whole loop is one C# action.
```csharp
[SharedAiAction]
public static NodeStatus DispatchAllToBaseline(Entity self, EntityRepository world)
{
    ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
    for (int i = 0; i < roster.Count; i++) { /* compute baseline; world.Bus.Publish(order) */ }
    return NodeStatus.Success;
}
```
```
EventEntry ─► Action "DispatchAllToBaseline" ─► Return(Success)
```
**Verdict:** the blueprint adds **nothing** — 100% of the logic is C#. This is exactly the
"complexity just moved to a different part" case. For *this* node it's arguably fine (the loop body is
genuinely complex domain math). But note the blueprint is a fig leaf here.

**If we had a bounded `ForEach`** (see discussion — feasible, deferred by choice):
```
EventEntry ─► ForEach(over [UnitRoster])
                 └body► FunctionCall(ComputeBaseline, i, [params]) ─► Action(PublishOrder, target←roster[i], pos)
           ─► Return(Success)
```
Now the *iterate + per-member dispatch* is visible; only the small compute/publish are helpers.
**Blueprint value: none (helper) vs medium (ForEach).** This is the trade to weigh.

---

## Summary — where blueprints earn their keep

| Use case | Blueprint value under orchestration-model | Needs |
|----------|-------------------------------------------|-------|
| Movement / channel command (ex. 2) | **High** — fully visual today | — |
| Publish one event (ex. 3) | **Medium-high** | GAP-3 |
| Read-based condition (ex. 1) | **Medium** — decision visible, scan in C# | GAP-7 |
| Respond to event (ex. 4) | **High** — Event-node handler (not WaitForEvent) | verify multi-handler scheduling |
| Loop + per-item work (ex. 5) | **None (helper) / Medium (ForEach)** | GAP-1 (contested) |

**Takeaway:** blueprints add the most for **command/event/decision orchestration**; they add the
least for **data-processing leaves** (loops, scans), which the model pushes to C#. The Platoon
Hill-attack is unusually leaf-heavy, so a faithful "orchestration-model" rebuild leaves much of it in
C# helpers — an honest finding to weigh (see the discussion note).
