# Architect question #19 — Fixed-capacity typed **list variables** (blackboard-resident)

**Context.** Blueprint variables (`asset.Variables`) and AiPrimitive `WorkingState` live in a fixed-size
**unmanaged** blackboard blob (`BlueprintBlackboard1024/4096/16384`, tiered via `TierHint`), emitted as a
`[StructLayout(Sequential)] struct State`/`WorkingState` with one typed field per declared variable and
`Unsafe.As`-cast onto the raw `fixed byte` payload. `Stage4_TypeResolve.CheckUnmanagedConstraint` (BP1503)
rejects any variable whose type is not blittable, so **array/list variables are impossible today**. The
`ArrayMake`/`ArrayGet` nodes produce only transient managed `T[]` (no Stage5 lowering / no emit — dead ends);
they are **not** the vehicle.

Goal: a designer-declared **fixed-capacity, typed, ordered list** stored inline in the blackboard, so it is
pre-allocated (capacity known at author time) and — critically — stays **plain-`memcpy`** for
record/playback/snapshot (no GC tracking). Precedent for the storage shape already ships as ECS *components*:
`UnitRoster` (`fixed long[16] + Count`) and `BpCollectionDemo` (`fixed int[4] + Count`), surfaced to graphs via
the CA-07 curated-collection consumer nodes.

**Big reuse lever.** CA-07d-2 (just shipped) added a `CollectionKind` discriminator + a
`StatementEmitter.RenderCollectionAccessors` helper that already renders **native indexed access**
(`__ml![i]` / `.Count`). Adding one more kind lets a list variable feed **all five existing read/query
consumers** (`ComponentForEach` / `ItemGet` / `ItemCount` / `Contains` / `Find`) essentially unchanged — so only
the *write* side is net-new.

### Settled with the user (2026-08-03) — recorded, not open
| | Decision |
|---|---|
| Storage | `[InlineArray(N)]` blittable buffer + `int Count`, inline in the `State` struct — memcpy-safe for rec/playback/snapshot |
| Element types | primitives, blittable structs (`[BlackboardDtoStruct]` **or** hand-declared blittable), `Entity` handles |
| **Managed / reference / nullable-reference elements** | **DROPPED** — no managed blackboard (would break plain-memcpy snapshot); do not design for it |
| Semantics | **ordered list**, duplicates allowed, positional `InsertAt`/`RemoveAt` shift the tail |
| Overflow | `Add`/`InsertAt` return `false` + emit a runtime diagnostic and drop the element; **never silent**, never overwrite |
| Indexed r/w | direct `Get[i]` / `Set[i]` by index over the logical length `[0, Count)` |
| Preallocation | supports declaring/resizing to a given logical length, **default-filled**. *Free for blittable `T`*: `default(T)` is all-zero bytes and the blob is zero-initialized — so preallocation = setting `Count = N` over already-zeroed slots; only a **grow-after-shrink** must re-zero the reclaimed `[oldCount, N)` range |

---

## Q19-A — How does a list variable surface to the graph (read/query side)?

- **A1 — reuse the CA-07 consumers.** A list variable's `GetVariable` projects a **collection out-pin**
  (`IsArray`, element-typed) baked with a new `CollectionKind = BlackboardFixedList` + the field name; it fans
  into the existing `ComponentForEach` / `ItemGet` / `ItemCount` / `Contains` / `Find` nodes. Emit adds ONE case
  to `RenderCollectionAccessors` rendering `s.{field}[i]` / `s.{field}_Count` (the `s`/`ws` state local). *Reuse:*
  the entire read/iterate/search surface + its editor pins/wire-bake already exist; near-zero new read code.
- **A2 — dedicated new list-read nodes** (`ListForEach`/`ListGet`/`ListCount`/`ListContains`/`ListFind`).
  Conceptually "cleaner" separation, but duplicates five nodes + pins + wire-bake + tests already shipped for
  collections, and splits the designer's mental model.

**Claude's lean: A1.** The consumers are collection-shape-agnostic by construction (that's what `CollectionKind`
is for); a third kind is the whole point. Only the *source* differs (a state field vs a component re-read).

## Q19-B — Type-model representation + generated storage shape

Today neither `BlueprintTypeRef` (asset) nor `IrTypeRef` (IR) carries a capacity/length, and every array resolves
`IsUnmanaged=false, SizeBytes=0` → BP1503. A list type must resolve as **genuinely unmanaged with a real size**
so it passes BP1503 and counts against the tier budget (`FieldLayout` sums `SizeBytes`).

- **B1 — capacity-carrying list type + generated `[InlineArray(N)]` wrapper.** `BlueprintTypeRef` gains
  `(ElementTypeId, Capacity)` for a list kind; the compiler generates a per-(element,N) blittable wrapper
  `struct __List_{Elem}_{N} { int Count; ElemBuffer Items; }` where `ElemBuffer` is an
  `[InlineArray(N)] struct { Elem _e0; }`. Resolves `IsUnmanaged=true`, `SizeBytes = align(4 + N*sizeof(Elem))`.
  Get/Set/read all project `s.{field}` exactly like a scalar field. *Build:* new type-kind threaded through
  resolve + `FieldLayout` size math + `TypeRefToCSharp` + the wrapper emit.
- **B2 — a hand-written generic `FixedList<T,N>` value type** in `Fdp.Core`. Fewer moving parts in the compiler,
  BUT C# can't express `N` as a generic parameter (no const generics), so this degenerates into per-capacity
  types or an `[InlineArray]` that still needs codegen — no real simplification. Also loses the
  `[BlackboardDtoStruct]`-style auto-discovery the type picker already uses.

**Claude's lean: B1** — the compiler already owns struct emission and size math; generating the wrapper keeps the
declaration purely data (`element type + capacity [+ optional initial length]`), which the editor and tier-budget
UI extend naturally. A declared **initial length** `L ≤ N` seeds `Count = L` at instance init (slots already
`default` from the zeroed blob) — that IS the "preallocated array of N defaults" mode with zero extra machinery.
*(Editor UX, design-before-build: the Variables type picker gains a "fixed list of \<T\> × capacity \<N\> [initial
length \<L\>]" option; capacity feeds the existing tier-budget `PackWarning`. Elements are the same choice list the
picker already offers — primitives + `[BlackboardDtoStruct]` structs + `Entity`.)*

## Q19-C — Write-node vocabulary (round-out)

Read/query is reused (A1). The net-new **mutation** nodes:

- **Core (lean to ship):** `ListAdd(list, item) → bool` (append-if-room), `ListInsertAt(list, index, item) → bool`
  (shift; `false` if full or index out of `0..Count`), `ListSet(list, index, item)` (in-range overwrite),
  `ListRemoveAt(list, index)` (shift down), `ListClear(list)` (Count→0), and `ListResize(list, length) → bool`
  (a.k.a. `SetLength`: set `Count = length ≤ Capacity`, **zero-fill** any grown range so new slots are `default`;
  `false` if `length > Capacity`) — this is the runtime "preallocate to N defaults" op. All exec nodes mutating
  `s.{field}` in place; every `→ bool` follows the settled overflow contract (false + runtime diagnostic).
- **Reused from CA-07 (no new node):** `Contains` (membership), `Find` (→ Index + Found = the `IndexOf` need),
  `ItemGet[i]`, `ItemCount` (= `Count`), `ForEach`.
- **Deferred:** `RemoveValue(item)` (= `Find` then `RemoveAt` — composable), `Sort`, `Reverse`, `Contains`-based
  dedup-on-add (a per-`Add` flag, not a type change).

**Claude's lean:** ship the **core 5 write nodes**; reuse the 5 read/query nodes; defer `RemoveValue`/sort. This
is the smallest vocabulary that makes the list fully usable without gaps.

## Q19-D — Element mutation model (value semantics)

Fixed buffers can't cross the graph by-ref, and pins are by-value, so mutating the i-th struct element's field is
**get copy → modify → `ListSet[i]` write-back** (a value round-trip). There is no `ref`-to-element pin.

- **D1 — value round-trip only** (`ItemGet[i]` → edit → `ListSet[i]`). Consistent with how every other blittable
  value flows through the graph; no new concept.
- **D2 — dedicated in-place element-field write nodes** (e.g. `ListSetField(list, i, "FieldName", value)`).
  Ergonomic for struct elements, but a new reflective-ish node shape and bespoke emit.

**Claude's lean: D1** for v1 — round-trip is uniform with the rest of the graph and needs no new machinery;
revisit D2 only if struct-element editing proves painful in practice.

## Q19-E — Scope knobs

- **Applies to:** Instance `Variables` **and** AiPrimitive `WorkingState` (both are blittable tiered state) — same
  emit path. Lean: **both**.
- **`Nullable<T>` (blittable `T?`) slots:** dropped with the managed decision (a `T?` slot costs a per-slot flag
  and complicates memcpy semantics). Lean: **out** for v1; a sentinel value is the author's job.
- **Dead-`Entity` compaction:** a list of `Entity` handles does **not** auto-remove stale/dead entities (unlike
  `UnitRoster`'s derived semantics); staleness is the author's concern. Lean: **no auto-compaction**.

## Q19-F — Indexed write out of range: auto-extend vs bounded + explicit resize

`Set[i]`/`Get[i]` are valid over `[0, Count)`. When a designer writes `Set(list, i, x)` with `i >= Count` (but
`< Capacity`), what happens?

- **F1 — bounded + explicit resize.** Out-of-range `Get`/`Set` returns the overflow contract (`Set → false` +
  diagnostic; `Get` → `default`, never throws). To index past `Count`, the author first `ListResize(n)` /
  `ListAdd`. Predictable; one obvious way to grow; matches `List<T>` indexer semantics.
- **F2 — auto-extend on write.** `Set(list, i, x)` with `i >= Count` sets `Count = i+1` and default-fills the gap
  `[oldCount, i)`. Convenient "sparse fill" (Lua/JS-array feel), but implicit length changes are a foot-gun and
  muddy the overflow contract (when is a big `i` a bug vs. an intentional grow?).

**Claude's lean: F1** — bounded indexer + explicit `ListResize`/`ListAdd`, consistent with `List<T>` and with the
"never silent" overflow rule. Preallocation is served cleanly by declared initial length (Q19-B) and `ListResize`
(Q19-C), so F2's convenience isn't needed.

---

## Recommendation summary
| | Lean |
|---|---|
| Q19-A read surfacing | **A1** — reuse CA-07 consumers via `CollectionKind=BlackboardFixedList`; one emit case |
| Q19-B type + storage | **B1** — capacity-carrying list type + generated `[InlineArray(N)]`+`Count` wrapper; resolves unmanaged w/ real `SizeBytes` |
| Q19-C write nodes | core 6 (`Add`/`InsertAt`/`Set`/`RemoveAt`/`Clear`/`Resize`); reuse `Contains`/`Find`/`ItemGet`/`ItemCount`/`ForEach`; defer `RemoveValue`/sort |
| Q19-D mutation | **D1** — value round-trip (`Get[i]` → edit → `Set[i]`); no by-ref element pin |
| Q19-E scope | Instance Variables **+** WorkingState; no blittable `T?`; no dead-Entity auto-compaction |
| Q19-F OOB write | **F1** — bounded indexer; grow via declared initial length + `ListResize`; no auto-extend |

**Load-bearing constraint (do not relax):** blittable-only, memcpy-safe — no managed blackboard, ever, for this
feature. Record/playback/snapshot stay plain byte copies.

## Architect answers
*(pending — this is a full architect round, not a fast-track: it is a new capability + new type kind, larger than
Q#18's refinement of an existing mechanism.)*
- **Q19-A:** _TBD_
- **Q19-B:** _TBD_
- **Q19-C:** _TBD_
- **Q19-D:** _TBD_
- **Q19-E:** _TBD_
- **Q19-F:** _TBD_
