# How each new blueprint addition is authored (worked, FOR REVIEW — nothing built)

> Answers "what does the designer actually drop, and how does a specific C# thing (an event struct, a
> component) get *exposed* as a node with real pins?" Notation: `A ─►B` exec flow; `(pin←src)` a wired
> data pin; `[var]` a blackboard variable.

## The one pattern that answers most of it: **catalog + picker + reified pins**

You never hardcode one node per C# type. There is **one generic node + a curated catalog**. The
designer picks a catalog entry from a dropdown; the node then **reifies concrete, typed pins by
reflecting the underlying struct's fields**. Adding a new C# thing = **one catalog line** (Slice 1,
hand-curated) or **one attribute** (Slice 2, auto-discovered). No editor code per type.

**This already works today** for `ChannelCommand`: its `BuiltInChannelCommandCatalog` entry
`new("MoveTo", "…LocomotionChannel", 1, "…MoveToParams")` makes the node, when you pick "MoveTo",
show one input pin per field of `MoveToParams`. Every addition below is the same idea.

---

## 1. Sending an FDP event — the `PublishEvent` node  *(your question)*

There are many event structs, so we do NOT make a node per event. **The Engine Event Catalog already
exists** — `BuiltInEngineEventCatalog` lists ~20 events today (`HitEvent`, `BehaviorFinishedEvent`,
`TargetVisibleEvent`, animation + navigation lifecycle events), each an entry:
```csharp
new(Name, EventTypeFqn, DisplayName, Category, TargetFieldName, FilterableFields[], QoS, Propagates…)
```

**Engine programmer, once — expose an event by adding one catalog line:**
```csharp
new("ClearBehavior",        "Fdp.Toolkit.Behavior.Events.ClearBehaviorEvent"),
new("AssignTacticalIntent", "Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent"),
```
(Slice 2 replaces this with a `[BlueprintExposedEvent]` attribute on the struct → auto-listed.)

**Designer — drop a `PublishEvent` node:**
- pick the event from a **Category-grouped dropdown** (like the GetShared type picker / ChannelCommand action picker);
- the node **reifies input pins for that event's fields** (same NodePinSchema-from-FQN mechanism ChannelCommand uses) — e.g. `AssignTacticalIntent` → pins `Target` (Entity), `IntentId` (int), `Location` (Vector3);
- the designer wires values into those pins.

```
EventEntry ─► PublishEvent "Clear Behavior" (Target←self) ─► Return(Success)
EventEntry ─► ForEach([roster]) └► PublishEvent "Assign Tactical Intent" (Target←item, IntentId←…, Location←…)
```

**Same-entity vs cross-entity is automatic:** if `Target` is `self`, it publishes to `world.Bus` this
frame; if `Target` is another entity, the node emits the deferred `BlueprintDeferredEvent` (≤1-frame,
per the ECS write rule). The designer just wires `Target` — no manual choice.

> The **respond** side (`WhenNode` / `WaitForEvent`) uses the *same* catalog — you pick the event to
> react to, and `FilterableFields` become optional filter pins. (`WaitForEvent` has a known FQN bug to
> fix first — logged.)

---

## 2. Reading a component — the `Read <Component>` catalog (P2)

**Engine programmer, once:** tag the component `[BlueprintReadable]` and mark exposed fields (a
`BlueprintReadableComponentCatalog` entry, same shape as the event/channel catalogs).

**Designer:** drop e.g. **`Read NavigationStatus`** → one `Entity` in-pin (defaults to `self`; wire a
different entity for a foreign read) + one out-pin per exposed field.
```
Read NavigationStatus (Entity←subordinate) ─out► Result   ─► Branch(Result == Arrived)
```
Array/complex fields aren't single pins — they surface as a `ForEach` source or a curated query node
(e.g. `TargetMemory.Contains(entity) → bool`).

## 3. Reading a world singleton — `GetSingleton` (P3)
Same catalog idea, **no Entity pin** (it's world-global): `GetSingleton NetworkEntityMap` → out-pins
for its exposed accessors.

## 4. Iterating — `FlowForEach` (P1)
Editor scaffolding already exists. Pins: **exec-in**; data-in `Collection`; **exec-out `Loop Body`**
(runs once per item) + **exec-out `Completed`**; data-out `Item` + `Index`.
```
… ─► ForEach (Collection←[roster])
        ├─Loop Body─► Read BehaviorState (Entity←Item) ─► … per-member work …
        └─Completed─► … after the loop …
```
Validator forbids latent nodes (Wait/Delay) inside `Loop Body`; compiler emits a synchronous C# `foreach`.

## 5. Calling a C# helper with engine context — context-aware `FunctionCall` (P7)
**Engine programmer:** end the helper signature with the context params:
```csharp
public static bool HasTarget(uint targetNetworkId, Entity self, ISimulationView view) { … }
```
**Designer:** the node shows **only** the real pins (`targetNetworkId`); `self`/`view` are recognized
by their trailing types, **hidden from the pins**, and auto-appended by the compiler. Read-only
(`ISimulationView`). (RW / mutation → a `[SharedAiAction]` node instead, which gets `self`+`world`.)

## 6. Slots — `AcquireSlot` / `ReleaseSlot` / `BurnSlot` (reuse existing `SlotRotation`)
**Designer:** declare a `SlotRotationState` **WorkingState variable** (from the struct-type picker) —
one for firing slots, one for baseline. The nodes operate on it:
```
AcquireSlot ([firingSlots], TotalSlots←N) ─out► slotIndex        // -1 if none free
BurnSlot    ([firingSlots], slot←idx)                            // on death
ReleaseSlot ([baselineSlots], slot←idx)                          // on return
```

## 7. Active-runner list — `MemberSlotList` nodes (the one new construct)
**Designer:** declare a `MemberSlotList` WorkingState variable; verbs:
```
Add          ([runners], entity←t, firingSlot←f, baselineSlot←b)   // HasStarted=0
Count        ([runners]) ─► int
ForEach      (Collection←[runners]) └► Item = (entity, firingSlot, baselineSlot, hasStarted)
SetHasStarted([runners], index←i, value←1)
SwapRemoveAt ([runners], index←i)                                 // O(1) compaction
```
Fixed named SoA columns (Entity + 3 bytes), cap 16. The `[InlineArray]`/`GetSpanRW()` write-loss
hazard is handled inside the primitive — invisible to the designer.

---

## Putting it together — `Condition_HasTarget`, fully authored
```
[Param] TargetNetworkId : uint
EventEntry ─► FunctionCall "HasTarget" (targetNetworkId←[TargetNetworkId])   // self/view hidden
           ─► Branch ┬─true─► Return(Success)
                     └─false─► Return(Failure)
```
The designer picks a catalog helper, wires one pin, branches, returns. Everything ECS-heavy is behind
the vetted `HasTarget` catalog entry — authored once by the engine team, reused by any behavior.
