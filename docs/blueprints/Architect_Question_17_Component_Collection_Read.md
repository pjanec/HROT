# Architect question #17 — collection component fields (read: iterate + index + length)

**Status: 🟡 DRAFT — for architect/user.** Slice 2 (collections) of the component-access workstream (CA-07),
after CA-01–06 shipped scalar/managed Get/Set Component. The read *mechanism* is already architect-approved
(Q#15-A **A1** baked `Count`/`Item[i]` accessors; Q#15-B `DynamicBuffer` RO-safe mid-tick; Q#15-E no unmanaged
maps/sets). What's open is the **designer-facing shape** for a component field that is a *collection*. User
steer: **follow Unreal.**

## The need

A component field can be a collection — `FixedList<T>`, `[InlineArray]`, or ECS `DynamicBuffer<T>`. A scalar
value-pin can't carry it, so the multi-field GetComponent read (CA-01) currently can't expose it. We need
**iterate + random-access `Get[i]` + `Length`** over such a field. (Writable container ops stay out — writes are
action-gated.)

## Unreal's model (the UX to follow)

Unreal never explodes a container into per-element pins. A container member surfaces as **one typed pin**:
- **`TArray` → an "Array of T" pin** consumed by generic nodes: `Get (a copy)`[i], `Length`/`Last Index`,
  **`For Each Loop`** (+ *with Break*), `Contains`, `Find`.
- `TMap`/`TSet` → their own pin + `Keys`/`Values`/`Contains`. (Out of scope — Q#15-E: no unmanaged maps/sets.)
- Nested *structs* get "Split Struct Pin"; **containers stay one pin**.

So Unreal's rule = **collection field → one collection pin; generic operation nodes consume it.** Composable
(read once, iterate + index + length off the same pin), designer-familiar.

## The reflection-free wrinkle (why we can't copy Unreal verbatim)

Unreal's `TArray` is one uniform heap type, so a generic array pin "just works." Ours are **unmanaged +
heterogeneous** (`FixedList<T>` fixed-cap, `[InlineArray]` fixed-size, `DynamicBuffer<T>` ECS-native) with **no
common runtime type/interface**, and the netstandard2.0 compiler **can't reflect**. So a pin can't carry "the
collection" by value or via a uniform type. The A1-approved answer: **baked `(Count, Item[i])` accessor FQNs per
collection kind** — exactly what `FlowForEach` already does.

## What exists today

| Piece | State |
|-------|-------|
| `FlowForEach` | **Already source-bakes iteration over a component collection**: baked `SourceComponentFqn` + `CountAccessorFqn` + `ItemAccessorFqn` → `IrOp_GetComponentRO` + `IrOp_ForEach` (body inlined). Confirm which collection kinds it currently supports. |
| `ArrayGetNode` / `ArrayMakeNode` | **Empty stubs, not lowered** — the random-access/length gap. |
| `IrOp_FieldRead` | reads a scalar field (CA-01); a collection field is not a scalar. |

**So iteration is ~half-built (FlowForEach); random-access + length are not.**

---

## Q17-A — Composition model (the crux)

How does a collection field connect to the operation nodes?

- **A1 — Source-baked ops (FlowForEach-style).** Each op is self-contained and bakes `(ComponentFqn, field, kind,
  Count/Item accessor FQNs)`. Nodes: **`ForEachComponentItem`** (generalize `FlowForEach`), **`GetComponentItem`**
  (`component.field[i]`), **`ComponentItemCount`** (`Length`). Designer picks the component+field on each op.
  *Pros:* maximal reuse (FlowForEach already is this), no new pin type, no IR handle machinery, cheapest. *Cons:*
  re-pick the collection per op; not Unreal-composable.
- **A2 — Collection pin (Unreal-style).** GetComponent projects a **collection out-pin** for a collection field;
  generic `ForEach`/`Get`/`Length`/`Contains` nodes take a **collection in-pin**. The pin carries only the entity
  at runtime; **on wiring, the editor bakes the connected field's `(ComponentFqn, field, kind, accessor FQNs)`
  onto the consuming op** (author-time resolution — no runtime collection value, stays reflection-free). *Pros:*
  Unreal-familiar, composable (read once → many ops), scales to future maps/sets. *Cons:* a collection-pin
  abstraction + per-connection accessor baking (editor complexity); the "collection value" is an author-time
  binding, not real runtime data.

**Claude's lean:** **A1 for the first slice** (fast, reuses `FlowForEach`, delivers iterate+index+length now),
**evolve to A2** for the composable Unreal UX once the primitive ops exist — *unless* the architect/user want to
commit to A2 up front (the user did say "follow Unreal"). **Question:** is the A2 collection-pin (an author-time
binding that bakes accessors onto the consumer, carrying only the entity at runtime) an acceptable IR/editor
shape, or do we start source-baked (A1)?

## Q17-B — Node set + first slice

- Iterate: **generalize `FlowForEach`** to all three kinds (mostly done — extend the accessor resolution).
- Random-access: **`Get[i]`** + **`Length`** (finish/replace the `ArrayGet`/`ArrayMake` stubs, or new
  component-scoped ops). `Contains`/`Find` are nice-to-have follow-ons.

**Claude's lean:** first slice = **iterate (FlowForEach, all kinds) + `Length` + `Get[i]`**; `Contains`/`Find`
later.

## Q17-C — Accessor resolution per collection kind

The editor (net8, reflects) must resolve the `(Count, Item[i])` accessors per kind and bake their FQNs:
`FixedList<T>` (`.Length` + indexer), `[InlineArray]` (fixed length + span indexer), `DynamicBuffer<T>`
(`.Length` + indexer). **Is there a uniform accessor convention** to key off, or do we hand-write a small per-kind
resolver (a `CollectionKind` enum + accessor lookup)? (Mirrors how `FlowForEach`'s accessors were obtained.)

**Claude's lean:** a small editor-side per-kind resolver (enum + known accessor shapes), since there's no common
interface. Confirm the three kinds are the complete set.

## Q17-D — Managed collections

A managed component may hold a managed collection (`List<T>`, array). Under the Q#15-G managed rules
(read-and-pass, no persist, no mutate): iterate/index via **direct C# `foreach`/indexer** (no baked accessor
needed — it's real net8 at runtime), producing **element copies** that flow only to managed consumers.

**Claude's lean:** handle managed collections as a thin variant (direct `foreach`/`[i]`), gated by the existing
managed flow rules (BP2063-style). Likely a later sub-slice.

## Q17-E — Element type flow

Each iterated/indexed element is a **value copy** (Unreal "Get a copy"). A struct element → a struct-typed pin →
decompose with **`Break`** (already shipped); a scalar element → a scalar pin. No new machinery.

---

## Proposed build order

1. **Slice C1 (A1, unmanaged):** generalize `FlowForEach` to all three kinds (Q17-C resolver) + `ComponentItemCount`
   (`Length`) + `GetComponentItem` (`[i]`), source-baked. Element via value-copy + `Break`.
2. **Slice C2 (if A2 chosen):** the collection out-pin on GetComponent + generic `ForEach`/`Get`/`Length` consuming
   it (author-time accessor baking).
3. **Slice C3:** managed collections (direct `foreach`/indexer under the managed rules); `Contains`/`Find`.

## Architect answers

*(record once relayed)*
- Q17-A:
- Q17-B:
- Q17-C:
- Q17-D:
- Q17-E:
