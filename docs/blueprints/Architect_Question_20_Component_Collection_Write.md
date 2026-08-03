# Architect question #20 — component COLLECTION element WRITE (fixed-capacity, self, unmanaged only)

**Status: 🟡 PENDING architect.** The write counterpart to Q#17/#18 (component collection READ, shipped) and the
collection-shaped sibling of Q#16 (scalar component WRITE, approved). Gates the Fixed Collections build (FC-0/FC-1).
See `Blueprint_Fixed_Collections_Design.md` (umbrella) + `Fixed_Collections_RESUME.md`.

## The need

A designer-facing node family to **mutate the elements of a fixed-capacity collection field on the entity's own
ECS component** — `SetAt` / `Add` / `InsertAt` / `RemoveAt` / `Clear` / `Resize` over an `[InlineArray(N)] Items +
int Count` field. Today blueprints can **read** such a field (CA-07 consumer nodes: `ForEach` / `ItemGet` /
`ItemCount` / `Contains` / `Find`) but there is **no write op** — the emitter only writes *scalar* component fields
(`__wc.Field = value`, `StatementEmitter.cs:374`). Writing "my component's list" still requires a hand-authored C#
action. This is the verified omission FC-1 fills.

**Scope of THIS question — the *component* home only.** The blueprint-variable list home (Q#19, private to the
instance) and the action-DTO home (by-ref in C#) are **independent** slices with their own decisions; they do not
inherit these rulings. This doc is only about writing a collection field that lives on a *shared ECS component*.

## What is already settled (inherited, not re-asked)

These are approved and carry over verbatim — the collection case must stay inside them:

| Q#16 ruling (scalar write) | Applies to collection element write as |
|---|---|
| **Self only** — no cross-entity write; RW binds to `self` | element writes bind to `self`; the node exposes **no `Target`/`Entity` pin** |
| **`[BlueprintWritable]` opt-in** gate on the component type | a collection field is writable only if its component is `[BlueprintWritable]` (plus Q20-A below) |
| **Unmanaged → direct `GetComponentRW<T>(self)`**, write-if-present, in-place | same fetch; element mutation through the same `ref` |
| **Managed → whole-replace via ECB; per-field managed FORBIDDEN** (snapshot aliasing) | **managed (`ManagedMember`, e.g. `List<T>`) collections are NOT element-writable** — see Q20-C |
| **Tick order safe:** `BlueprintTickSystem` `[UpdateBefore]` the dispatchers, system outputs excluded by policy | unchanged — element writes are still in-phase, self, before readers |

**The read side introduced a hazard the write side must NOT inherit.** CA-07 reads may target **any** component on
**any** entity (a `Target` pin). Writes are self-only. So the collection-write node must **not** reuse the read
picker's cross-entity Target surface — it is a self-write node that happens to address a collection field.

## What exists today (reuse map)

| Piece | Mechanism | Reuse for collection write |
|---|---|---|
| Scalar self-write | `IrOp_SetComponent` → `ref var __wc = ref GetComponentRW<T>(self); __wc.F = v;` (`StatementEmitter.cs:372`) | same `GetComponentRW` fetch + HasComponent guard; the **assignment target** changes from a scalar field to a collection element |
| Collection READ accessors | `RenderCollectionAccessors` (CA-07d): `CuratedStatic` → `global::{Fqn}(comp[,i])`; `ManagedMember` → `IReadOnlyList<T>` local | the **write** accessor is new (`SetAt`/`Add`/…); `CollectionKind` already discriminates curated vs managed |
| Overflow-returns-false + diagnostic | designed for the blueprint-variable list (Q#19) | same contract: `Add`/`InsertAt` on a full list return `false` + `DebugProbe` diagnostic, never silent |
| `[InlineArray]` element access | `.NET 8` inline-array indexer / `AsSpan` | **mandate the `Span<T>` write pattern** (see Robustness) — the naïve `ref`+indexer path can silently lose writes |

**Most machinery is reuse** (self fetch, `[BlueprintWritable]` gate, `CollectionKind`, overflow contract). The
genuinely new decisions are the *element-write* gate granularity (Q20-A), the managed exclusion (Q20-C), and the
capacity/existence contract (Q20-D).

---

## Q20-A — Is `[BlueprintWritable]` on the component enough, or does a *collection field* need its own opt-in?

A scalar `[BlueprintWritable]` write assigns one field. An element write mutates **shared, sequence-valued** state
whose *contents/ordering* a system may own even when the struct is nominally "behavior-writable" (e.g. a roster a
system re-sorts, a ring buffer a system compacts). Two curation granularities:

- **A1 — Component-level gate only.** If the component is `[BlueprintWritable]`, all its fields — scalar *and*
  collection — are writable. Simplest; matches the scalar rule exactly; author opts the whole component in.
- **A2 — Field-level opt-in for collections** (`[BlueprintWritable]` on the component **plus** a per-field marker,
  or a `[BlueprintWritableCollection]` on the field). Lets an author expose scalar intent while keeping a
  system-managed sequence read-only on the same component.

**Claude's lean: A1** (component-level, matching Q#16) for v1 — a component author who marks a type writable is
already declaring "behaviors may mutate this"; collection fields are not special enough to warrant a second marker,
and A2 can be added later without breaking A1. **Ask the architect:** is there a component whose scalar fields are
safe-to-write but whose collection field is system-owned sequence state — i.e. a real case that demands A2 now?

## Q20-B — Tick-ordering for *sequence* mutation (confirm Q16-B still holds)

Q16-B established `BlueprintTickSystem` runs in Simulation, `[UpdateBefore]` the dispatchers, so writing
behavior-owned intent is race-free. Element writes are still in-phase, in-place, on self.

- **B1 — The Q16-B guarantee covers element writes unchanged** (a mutated `Items`/`Count` is read after the
  behavior, same as a mutated scalar).

**Claude's lean: B1.** **Confirm** there is no additional hazard specific to *structural* mutation of a shared
component's sequence mid-tick (e.g. another Simulation-phase system iterating the same `Items` between the write
and the dispatcher). If such a reader exists for a writable collection, that component simply stays out of the
writable set — same policy lever as scalar.

## Q20-C — Managed (`ManagedMember`) collections: read-only, confirming the per-field-managed forbiddance

CA-07d added `ManagedMember` collections — a `List<T>`/`IReadOnlyList<T>`/`T[]` on a **class** component, read via
an `IReadOnlyList<T>` local. Q#16 **forbids per-field managed writes** (a managed component is shallow-copied by
reference into snapshots → field-mutation corrupts recorded history / Flight Recorder / background threads).

- **C1 — Managed collections are READ-ONLY from blueprints.** Element writes are emitted only for **`CuratedStatic`
  (unmanaged `[InlineArray]`) collections**; a write node bound to a `ManagedMember` collection is rejected at
  validate-time (a new `BPxxxx`, the collection analog of Q#16's per-field-managed rejection). A managed collection
  is mutated only by whole-component ECB replace (Q#16 W2) — never element-wise.

**Claude's lean: C1** — it is the exact collection projection of the approved aliasing rule; anything else
reintroduces the snapshot-aliasing corruption Q#16 closed. **Confirm** no exception is wanted (e.g. a managed
component explicitly marked "not snapshotted").

## Q20-D — Write mechanics, capacity, and existence

Direct `GetComponentRW<T>(self)` (per Q16-D), then mutate the `[InlineArray]` element(s):

- **Op family:** `SetAt(i,v)` (indexed write over `[0,Count)`), `Add(v)`, `InsertAt(i,v)` (shift up), `RemoveAt(i)`
  (shift down), `Clear()`, `Resize(n)` (grow default-fills, shrink drops tail). Matches the blueprint-variable list
  ops so the designer's node-level UX is identical across homes (the standing UX requirement).
- **Overflow:** `Add`/`InsertAt` past capacity `N` → **return `false` + `DebugProbe` diagnostic, drop the write**;
  never silent, never throw. (Same contract as Q#19.)
- **Existence:** write-if-present (Q16-D). Component absent on self → graceful fail (`Found`-style / `NodeState`),
  **no implicit ECB add**.
- **`Count` coherence:** all ops keep `Count` in `[0,N]`; `SetAt` is valid only within `[0,Count)` (does not grow
  `Count` — use `Add`/`Resize` to extend), OOB index → false + diagnostic.

**Claude's lean:** direct RW + the full op family + false-on-overflow + write-if-present, exactly mirroring Q16-D
and Q#19 so all three homes share one vocabulary. **Confirm** the op set and the "`SetAt` does not grow `Count`"
rule (vs. a Godot-style auto-extend), and that `Resize` shrink need not re-zero dropped tail bytes for correctness
(it must for snapshot determinism — see Robustness).

---

## Robustness & correctness (design notes — build mandates, not architect asks)

- **`[InlineArray]` silent-mutation-loss trap (MANDATORY).** `GetComponentRW`'s own doc warns that
  `ref var q = ref GetComponentRW<T>(...); q.Buf[0] = x;` can copy the inline buffer to a temp and **lose the
  write**. All element writes MUST go through the `Span<T>` pattern (`MemoryMarshal.CreateSpan` / the inline-array
  `AsSpan`) that writes through the `ref` to real component storage. **Gate: an InlineArray write round-trip test**
  (write element → re-read component → value present) is required before FC-1 lands, for every home.
- **Determinism on shrink/remove.** `RemoveAt`/`Resize`-shrink/`Clear` must **re-zero** the vacated tail slots
  (not just lower `Count`) so byte-for-byte memcpy snapshots/record-playback stay identical regardless of prior
  contents. (`Count`-only shrink leaves stale bytes → nondeterministic hashes.)
- **Self-only, no Target pin.** The node binds `GetComponentRW<T>(self)`; it exposes **no** cross-entity Target pin,
  unlike the CA-07 read picker. Validate-time rejects any wired entity source (the collection-write analog of
  Q#16's cross-entity rejection).
- **Multi-op ordering.** Distinct write nodes on the same component each fetch their own `ref` inside a HasComponent
  guard (the `SetShared`/Q#16 model); a chain of `Add`s is exec-ordered by wires, each in-place — no whole-struct
  clobber.
- **Stale/removed field:** reuse the CA-07 reflector → null → `NodeState.Error` red node + build failure; baked
  data preserved.

## Proposed build order (gated on this question)

1. **FC-0 foundation** — canonical write-accessor convention (`Span<T>` pattern) + the mutation-op **IR family** +
   `CollectionKind` write-backing + the InlineArray write round-trip test + the real `DebugProbe` overflow hook.
   Extend `BpCollectionDemoOps` (the CA-07 demo accessor) with the mutators as the reference implementation.
2. **FC-1** — component-collection write nodes: palette + self-bound picker (writable set, **no Target**) + drawer,
   `CuratedStatic`-only (managed rejected, Q20-C), validate-time gates (non-writable, cross-entity, managed,
   existence, OOB), `NodeState.Error` stale-ref.
   Gate: clean build → **Generators 184/184 byte-identical** → new lowering + round-trip tests green.

Blueprint-variable writes (Q#19) and action-DTO recognition are **independent** slices — not chained off FC-1.

## Architect answers (received)

_(pending — Q20-A · Q20-B · Q20-C · Q20-D)_
