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

## ⚠ CODEBASE REALITY CHECK (2026-08-01, during CA-07 build) — invalidates the auto-resolve premise

Before building CA-07a I audited what ECS component collections actually ARE in this repo. The Q#17 draft
assumed three uniform kinds (`FixedList<T>`, `[InlineArray]`, `DynamicBuffer<T>`) with clean `Length` + indexer.
**That premise is false for this codebase:**

1. **`FixedList<T>` and `DynamicBuffer<T>` do not exist here** (0 occurrences repo-wide). The ONLY collection
   shape in ECS components is **`[InlineArray(N)]`** (and raw C# `fixed` buffers, e.g. `UnitRoster.fixed long
   SubordinateEntities[16]`).
2. **Capacity ≠ logical count.** These buffers carry a fixed capacity `N`, but the *logical* element count is a
   **separate sibling field** (`UnitRoster.Count`, `UtilityResultBuffer.Count`). Auto-using the buffer's `N` as
   `Length` would iterate garbage tail slots — semantically wrong.
3. **`[InlineArray]` has a read footgun + no `.Length`.** `GetComponentRO<T>` returns `ref readonly T`; indexing
   an inline array through a readonly ref triggers the C# 12 `ldobj` defensive-copy (correct for reads, but the
   *discouraged* path). The blessed pattern is a hand-written `GetSpanRO()` per wrapper — which not every buffer
   defines. Inline-array types have **no `.Length`** property at all (length = the attribute constant).
4. **The architect already ruled on exactly this (Q#5-C):** raw fixed/inline-array access is kept **OUT of the
   visual graph**, confined to a tiny curated helper (`UnitRosterOps.Count(in T)` / `Subordinate(in T,i)`, marked
   `[BlueprintCallable]`), whose FQNs `FlowForEach` bakes. So the ESTABLISHED, architect-approved answer to
   "iterate a component collection from a blueprint" is **hand-written curated accessors + baked FQNs** — i.e. the
   A1 mechanism, not raw auto-resolution.

**Consequence:** pure-A2 (auto-resolve `.Length`/indexer off any collection field and emit it inline in the
graph's generated code) **contradicts Q#5-C** and is unsafe/incorrect for the Count-carrying inline-array idiom.
The A2 *UX* (a collection out-pin feeding generic `ForEach`/`Get[i]`/`Length`) is still achievable — but it must
be backed by **curated baked accessor FQNs** (the `FlowForEach`/Q#5-C mechanism), discovered editor-side via
`[BlueprintCallable]`/convention, NOT by auto-reflecting the raw buffer. **This is a genuine fork that needs a
user/architect call before CA-07 proceeds — see the RECONCILED OPTIONS below.**

### Reconciled options (post reality-check)

- **R1 — A2 UX + curated-accessor mechanism (RECOMMENDED).** Full Unreal collection-pin UX (collection out-pin →
  generic `ForEach`/`Get[i]`/`Length`), but the editor bakes **hand-written accessor FQNs** per collection field
  (the `UnitRoster`/`FlowForEach` model: `Count(in T)` + `Item(in T,i)`), discovered by convention
  (`[BlueprintCallable]` ops class, or a `[BlueprintCollection]` field marker naming its accessors). Respects
  Q#5-C, reflection-free, safe. Cost: only *curated* collection fields are exposed (a component must ship the
  accessors) — not every raw inline array is auto-iterable. This is A2-UX + A1-mechanism = the honest best fit.
- **R2 — pure auto-resolve raw inline array.** Bake element type + capacity `N`, emit `GetSpanRO()`-or-unsafe-span
  reads, `Length = N`. **Rejected:** contradicts Q#5-C, ignores the logical `Count`, emits unsafe span code.
- **R3 — span-pin.** Recognize wrappers exposing `GetSpanRO()` → project a `ReadOnlySpan<T>` pin. Cleaner but
  ties to that one convention and still needs the logical-count question answered.

**Claude's lean: R1.** It delivers the collection-pin UX the user committed to while honoring the architect's
Q#5-C ruling and the ECS's actual collection idiom. Awaiting user/architect confirmation before CA-07a.

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

*(recorded 2026-08-01 — user decision, direct)*
- **Q17-A: A2 — the full Unreal collection-pin UX, committed up front** (not the A1-first-then-evolve
  interim). A collection component field projects a **collection out-pin**; generic `ForEach` / `Get[i]` /
  `Length` (and later `Contains`/`Find`) nodes consume a **collection in-pin**. The pin carries only the entity
  at runtime; on wiring, the editor **bakes the connected field's `(ComponentFqn, field, CollectionKind, Count/Item
  accessor FQNs)` onto the consuming op** (author-time resolution — stays reflection-free, no runtime collection
  value). This supersedes the "A1 source-baked ops" interim in Q17-A above.
- Q17-B: first slice = **iterate (`ForEach`, all kinds) + `Length` + `Get[i]`**, all consuming the collection
  pin; `Contains`/`Find` follow. Generalize `FlowForEach`'s baked-accessor mechanism to feed the pin-driven ops.
- Q17-C: small editor-side **per-`CollectionKind` accessor resolver** (`FixedList<T>`, `[InlineArray]`,
  `DynamicBuffer<T>`) — no common interface to key off; mirror how `FlowForEach` obtained its accessors.
- Q17-D: managed collections handled as a thin variant (direct `foreach`/`[i]`, element copies) under the
  existing managed flow rules — later sub-slice.
- Q17-E: iterated/indexed element = **value copy** → struct element decomposed with `Break` (shipped), scalar
  element → scalar pin. No new machinery.
