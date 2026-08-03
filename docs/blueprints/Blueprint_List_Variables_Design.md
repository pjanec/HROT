# Blueprint List Variables — Design

> **Scope note:** this is the **blueprint-variable-collection** home of the broader *Fixed Collections* capability
> (`Blueprint_Fixed_Collections_Design.md`) — i.e. build slice **FC-2**. The LV-1…LV-5 steps below are FC-2's
> internal sub-steps. The umbrella covers the other two homes (component collections, action-DTO collections) and
> the shared read/write machinery this reuses.

Fixed-capacity, typed, ordered **list variables** stored inline in the unmanaged blackboard blob. Decisions
locked in `Architect_Question_19_Fixed_Capacity_List_Variables.md` (A1/B1/C/D-in-place/E/F1). This is the
implementation design; a companion **Review** section (end) records the adversarial pass and any deltas.

## 1. Goal & non-goals

**Goal.** A designer declares a variable like `Squad : List<Entity>[16]` (element type + capacity, optional
initial length). It persists in the blueprint's blackboard state, is read/iterated/searched via the existing
CA-07 collection consumer nodes, and mutated via new in-place write nodes — with **zero heap, zero GC, plain
`memcpy` for record/playback/snapshot**.

**Non-goals (v1).** No managed/reference/nullable elements (would break memcpy snapshot). No growable capacity.
No maps/sets. No `IEnumerable` semantics. No dead-`Entity` auto-compaction. No `Nullable<T>` slots.

## 2. Storage & memory layout

A list variable becomes **one field** in the emitted per-blueprint `State` (Instance) / `WorkingState`
(AiPrimitive) struct — same mechanism as a scalar variable today (`InstanceEmitter.EmitStateStruct`,
`AiPrimitiveEmitter.EmitWorkingStateStruct`), reinterpret-cast onto the raw `fixed byte` blackboard payload via
`Unsafe.As`.

The field's type is a **compiler-generated blittable wrapper**:

```
[InlineArray(N)] struct __Buf_{Elem}_{N} { private {Elem} _e0; }      // N contiguous elements, no header
struct __List_{Elem}_{N} { public int Count; public __Buf_{Elem}_{N} Items; }
```

`State` layout for `Squad : List<Entity>[16]` (Entity = 8B assumed), following the existing 16-byte cursor:

```
 offset  size  field
 ------  ----  --------------------------------------
   0     16    BlueprintLatentCursor  Cursor
  16      4    int   Squad.Count
  20      4    (pad to 8-byte Entity alignment)
  24    128    __Buf_Entity_16 Squad.Items   (16 × 8B)
 ------  ----
 total  152    (counts against the tier budget: 1024/4096/16384)
```

- **Size:** `sizeof(__List) = alignUp(4, align(Elem)) + N*sizeof(Elem)`, struct-aligned to `align(Elem)`.
  `FieldLayout` must compute this so tier-budget accounting (`PackWarning`) and `Unsafe.SizeOf<State>()` agree.
- **Default-fill is free:** the blackboard blob is zero-initialized and `default(blittableElem)` is all-zero
  bytes, so a preallocated list = `Count = InitialLength` over already-zeroed `Items`.
  **AMENDED 2026-08-04 (Q#20 review G6, all homes):** the canonical rule is the **tail-always-default
  invariant** — `RemoveAt`/`Clear`/`Resize`-shrink zero the vacated slots at mutation time, so grow (including
  the old "grow-after-shrink") **never** fills. Supersedes the lazy re-zero-on-grow rule previously recorded
  here and in Q#19. Rationale + evidence: `Blueprint_Fixed_Collections_Design.md` §"Decisions folded".
- **Snapshot/record/playback:** unchanged — the field is part of the `State` blob, copied by the existing
  byte-copy path. No special handling.

## 3. Type model

- **`BlueprintTypeRef`** (asset) gains a list shape. Proposal: reuse `IsArray=true` + `GenericArgs=[elementType]`
  and a **new `Capacity` int** (0/absent ⇒ not a fixed list). `TypeId` names the element or a synthetic list id.
- **`IrTypeRef`** (IR) gains `Capacity` (and keeps `ElementType`). A fixed-list resolves
  `IsUnmanaged = elementIsUnmanaged`, `SizeBytes = §2 formula`, `SizeReliable = true`.
- **`StaticTypeRegistry.TryResolve`**: new branch — when `Capacity > 0` and the element resolves unmanaged,
  return the list `IrTypeRef` (unmanaged, real size). This is what lets it **pass BP1503** (unlike a plain `T[]`,
  which stays `IsUnmanaged=false, SizeBytes=0`). Element must itself be unmanaged, else BP1503 fires on the element.
- **`TypeRefToCSharp`**: a fixed-list type emits the generated wrapper name `__List_{Elem}_{N}`; the compiler
  emits the two wrapper structs once per distinct `(Elem, N)` used.

## 4. Read path (Q19-A — reuse CA-07 consumers)

> **SUPERSEDED by Review §F1 — see below.** The reuse is at the IR/emit layer only; the graph-level nodes are
> **dedicated list nodes bound by variable id (A3)**, not the component consumers. Keep §4 for the mechanism
> (`CollectionKind=BlackboardFixedList`, `RenderCollectionAccessors` case) but ignore the "consume the CA-07 nodes
> unchanged" framing.

A list variable surfaces to the graph as a **collection out-pin** on a `GetVariable`/list-source node (`IsArray`,
element-typed), baked with **`CollectionKind = BlackboardFixedList`** + the variable id/field name. The five
existing consumers (`ComponentForEach`/`ItemGet`/`ItemCount`/`Contains`/`Find`) consume it unchanged at the pin
level; the compiler grows:

- One new `StatementEmitter.RenderCollectionAccessors` case: `BlackboardFixedList` renders count `s.{f}.Count`
  and item `s.{f}.Items[i]` (state local `s`/`ws`), instead of curated `global::Fqn(...)` or managed `__ml`.
- **⚠ Source resolution (the non-obvious part).** The CA-07 consumers' Stage5 cases resolve their "Collection"
  in-pin to an **entity**, then re-read a **component** (`GetComponentRO`/`GetManagedComponentRO`). A list
  *variable* has no entity/component — its backing is `s.{field}`. So each consumer's Stage5 case needs a **new
  source branch**: when the wired source is a list-variable (CollectionKind=BlackboardFixedList), bind the
  "component ref" to the state field `s.{f}` instead of emitting a component re-read. This is *moderate* new
  lowering, not free — see Review §R1.

## 5. Write path (Q19-D — in-place, no array copy)

New nodes, each **exec** and bound to the target list **by variable id** (the `SetVariableNode` lvalue pattern —
they do NOT take the list through a by-value data pin):

> **Emit form AMENDED 2026-08-04 (Q#20 review R3/G6):** `s` is a `ref` local over the blackboard bytes, so the
> naïve `s.f.Items[i] = item` is exactly the `[InlineArray]` ldobj silent-write-loss shape — every element
> access below goes through the **`Span<T>` cast** (`var __sp = (Span<Elem>)s.f.Items;`). And zeroing follows
> the **tail-always-default invariant** (vacated slots zeroed at mutation time; grow never fills).

| Node | Signature | Emit (in place on `s.{f}`, via `__sp = (Span<Elem>)s.f.Items`) |
|---|---|---|
| `ListAdd` | `(var, item) → bool` | `if (s.f.Count < N) { __sp[s.f.Count++] = item; ok } else { diag; false }` |
| `ListInsertAt` | `(var, index, item) → bool` | bounds+full check; shift `[i,Count)` up one (span copy); assign; `Count++` |
| `ListSet` | `(var, index, item) → bool` | `if ((uint)i < (uint)Count) __sp[i]=item; else diag+false` |
| `ListRemoveAt` | `(var, index) → bool` | bounds check; shift `[i+1,Count)` down one; `__sp[--Count] = default` (G6) |
| `ListClear` | `(var)` | `__sp[..Count].Clear(); s.f.Count = 0` (G6 — no lazy re-zero anywhere) |
| `ListResize` | `(var, length) → bool` | `length ≤ N` ? (shrink: `__sp[length..Count].Clear()`) set `Count` : diag+false — grow needs NO fill |

- **IR:** a new `IrOp_ListWrite` family (one op per verb, or one op keyed by an enum). Carries the variable index
  + operands. Emit reuses the `EmissionContext.VarFieldName` + state-local convention from `IrOp_WriteVariable`.
- **Overflow contract:** every `→ bool` returns `false` on full/OOB and emits a runtime diagnostic **in
  Debug/Trace mode** (a probe/log; no-op/return-only in Release). Never silent, never overwrite/throw.
- **Reads stay read-only** (Q19-A pins); only these write nodes mutate. Editing a struct element's sub-field =
  `ItemGet[i]` (copies one element) → edit → `ListSet[i]` (writes it back). One-element copy, never the array.

## 6. Editor

- **Declare UX** (Add-Variable dialog in the Variables panel; approved mockup 2026-08-03). A **Container** dropdown
  next to the element type — **Single** (today's behaviour, unchanged) | **Fixed list**. Choosing *Fixed list*
  reveals **Capacity** (≥1) and **Initial length** (0…capacity, default 0) fields, plus a **live budget line**
  (element size × capacity + overhead vs the current tier, red past the limit — surfaces the over-budget footgun
  at creation, not as a later compile error). Element combo is unchanged (primitives + `[BlackboardDtoStruct]` +
  `Entity`). Persists `BlueprintTypeRef { element, Capacity=N, InitialLength=L }`.
  - **Capacity is fixed at creation** (v1) — changing it would reallocate/migrate instance state; to change,
    delete + re-add. The Variables **table** renders a fixed list as e.g. `Entity[16]`.
  - **Injected, not widened** (review §F7): the shared `VariablesPanelControl` gains the container/capacity rows
    only when the Blueprint host passes a "list support" capability; HSM passes nothing → identical old UI. The
    capacity/length ride a Blueprint-local carry into `BlueprintTypeRef` — the shared `BlackboardVariableEntry`
    isn't widened. Discriminator is `Capacity`, **not `IsArray`** (§F7).
- **Read pin + wire-bake:** the list-source out-pin projects `IsArray`/element-typed; `TryBakeCollectionConsumer`
  stamps `CollectionKind=BlackboardFixedList` + the variable ref (mirror of the CA-07d-2 managed bake).
- **Write nodes + palette:** the 6 write nodes get drawers (variable picker like `SetVariable`) + palette entries.
- **NodePinSchema ⇄ Stage0 parity** maintained for every new pin shape.

## 7. Validation (new diagnostics)

- **Element must be unmanaged** — reuse BP1503 on the element (list of managed element rejected).
- **Capacity bounds** — `1 ≤ N ≤ tierMax/elemSize`; a new diagnostic (e.g. `BP15xx`) when a single list can't
  fit the largest tier, or when `InitialLength > Capacity`.
- **Tier budget** — existing `FieldLayout` sum vs `TierHint`; list size participates.
- Write-node target must reference a declared fixed-list variable (else a new diagnostic).

## 8. Testing & gates

- Compiler: type resolution (unmanaged + size), wrapper emit (per `(Elem,N)`), each read consumer over a list var
  (assert `s.f.Items[i]`/`s.f.Count`, NO component re-read), each write op (assert in-place emit, overflow bool),
  layout/size math, initial-length seeding, grow-after-shrink zero-fill.
- Editor: type-picker list option, capacity/initial-length persistence, tier warning, wire-bake, pin parity.
- **Gate every slice:** clean build → **Generators 184/184 byte-identical** (feature is additive) → new tests
  green. `Hrot.AI.Behaviors` builds.
- Demo: `ListVariableDemo.bp.json` (declare → Add/Set/Resize → ForEach/Contains/Find), generator-compiled.

## 9. Sequencing (initial — SUPERSEDED by Review "Revised sequencing")

1. **LV-1 storage/type foundation** — type model + resolve-unmanaged + `[InlineArray]` wrapper emit + State field
   + FieldLayout size + initial-length. Gate: declares, compiles, correct bytes, 184 intact.
2. **LV-2 read side** — list-source out-pin + `CollectionKind=BlackboardFixedList` + `RenderCollectionAccessors`
   case + the consumer source-resolution branch (§4 ⚠). Editor pin + wire-bake (Sonnet mirror).
3. **LV-3 write side** — `IrOp_ListWrite*` + 6 nodes + in-place emit + overflow diagnostics. Editor nodes/palette.
4. **LV-4 editor declare-UX** — type picker + capacity/initial-length + tier warning + drawers.
5. **LV-5 demo + docs**.

## 10. Open risks (pre-review)

- `[InlineArray]` emit + indexer syntax requires the **consuming** project's C# LangVersion ≥ 12 (the generator is
  netstandard2.0 but only emits *source*); if not, fall back to `MemoryMarshal.CreateSpan`/`Unsafe.Add`.
- Exact struct padding/alignment of the wrapper must match `FieldLayout`'s computed `SizeBytes` or the blob
  reinterpret desyncs.
- Reusing the CA-07 consumers is **not** free (§4 ⚠) — needs a variable-source lowering branch in each.
- Blackboard partition/tier allocation must tolerate the larger field; per-blueprint slot sizing.

---

## Review — adversarial multi-perspective pass (2026-08-03)

Three independent reviewers (codegen/type-system · runtime/persistence/snapshot · graph-semantics/completeness)
audited this design against the real code. **The storage idea is sound and `[InlineArray]` is proven in-repo
(`UtilityResultBuffer`, EQS), but four load-bearing claims broke.** Findings → deltas:

### F1 (BLOCKER, read path) — "reuse the CA-07 consumers" (A1) is the wrong shape
The entity-rooted assumption is baked into **three** layers, none Kind-aware for an empty `ComponentTypeFqn`:
editor `BlueprintNodeModel` (red-node + `IsCollectionConsumerBakeIncomplete`), Stage2 **BP2066**
(`ComponentTypeFqn` ANDed regardless of kind — hard error), and all 5 Stage5 unwired-guards. Worse, the tempting
render shortcut `{comp}?.{field}` (managed path, `StatementEmitter.cs:650`) **doesn't compile** for a value-type
state local `s` (`?.` on a struct). And `GetVariablePins` has no `IsCollection` branch to even project a collection
out-pin (`NodePinSchema.cs:625`). → A1 is ~8 files of moderate work, not "one emit case."
- **Reviewer proposed A3 (dedicated list nodes bound by variable id).** Structurally cleaner (sidesteps the 3
  gates), BUT it gives a DIFFERENT node-level UX (pick-a-variable vs wire-a-collection-pin).
- **DECISION (user, 2026-08-03) — keep the A1 UX, pay the compiler cost.** Requirement: *working with an array
  variable must be identical, from the designer's point of view, to working with a component collection field.*
  So the list surfaces a **collection out-pin** (off `GetVariableNode`, exactly like `GetComponent`'s collection
  out-pin) that wires into the SAME `ComponentForEach/ItemGet/ItemCount/Contains/Find` consumer nodes. The extra
  work the reviewer flagged is accepted as LV-2 scope:
  1. **`GetVariablePins` gets an `IsCollection` branch** (mirror `GetComponentPins`) + its `Stage0_Rehydrate`
     mirror — projects the element-typed `IsArray` collection out-pin for a list variable.
  2. **Wire-bake** (`TryBakeCollectionConsumer`) also accepts a `GetVariableNode`-list source, stamping
     `CollectionKind=BlackboardFixedList` + the **variable ref** (field name) onto the consumer, `ComponentTypeFqn`
     left empty.
  3. **All 3 `ComponentTypeFqn` gates become Kind-aware** — `BlueprintNodeModel` (red-node + bake-incomplete),
     Stage2 **BP2066**, and the 5 Stage5 unwired-guards require the *variable ref* (not `ComponentTypeFqn`) when
     `Kind=BlackboardFixedList`. (Also covers the Minor-10 element-type-change staleness.)
  4. **Stage5 source-resolution branch** — when the wired source is a list variable, bind the "component ref" slot
     to a **`ref`/`ref readonly` local aliasing `s.{field}`** (NOT an entity+`GetComponentRO`); delegate into the
     SAME `IrOp_ForEach`/`IrOp_ComponentAccessorCall`/`IrOp_ComponentCollectionSearch` ops with a new
     `Kind=BlackboardFixedList`.
  5. **`RenderCollectionAccessors`** gains the `BlackboardFixedList` case — `s.{f}.Items[i]` / `s.{f}.Count`, a NEW
     branch (no `?.`, no host indirection — the reviewer's non-compiling-`?.` blocker is avoided by not reusing the
     managed render).
  Reuse still lands at the IR/emit layer (same three ops); the *added* cost vs A3 is the 3 Kind-aware gates + the
  GetVariable collection projection — the price of identical UX, which is the requirement.

### F2 (BLOCKER, memory safety) — "zeroed blob = free defaults" is FALSE for WorkingState
Instance state is safe (`BlueprintInstanceService`/genesis/singleton all call `InitDefault`, which is `s = default`
then non-zero defaults). But **WorkingState** slots reached via the manifest/partition rail
(`BehaviorIngressSystem.AttachSlotsToMemory`) and the inline `BlueprintCall` path (`InlineActionLowering`) **never
run init and never re-zero on slot reuse** — a reused slot holds a *previous occupant's* bytes. A garbage `Count`
feeding an `[InlineArray]` indexer (**no bounds check**) is an unbounded OOB read/corruption — a memory-safety bug,
not a wrong number. Save/replication is a non-issue (`NoSave`; only the assignment persists). Record/playback is
genuine field-agnostic memcpy (confirmed). The *causal story* ("blob is zeroed") is wrong; the guarantee comes
from the `InitDefault` hook.
- **DELTA (belt + braces):** (a) **defensive clamp** — every list op uses effective length `min(Count, N)` and
  bounds-checks every index, so a garbage `Count` can never OOB (self-contained in the list ops → memory-safe
  unconditionally). (b) **init on all attach paths** — make `BehaviorIngressSystem.AttachSlotsToMemory` +
  `InlineActionLowering` run the definition's default-init/zero on fresh **and** reused slots. `InitialLength`
  seeding rides the same hook via a NEW partial-init emission (`s.{f}.Count = L`), which today's whole-field
  `DefaultValueCSharp` can't express.

### F3 (BLOCKER, layout) — baked field offsets are already unreliable; `SizeReliable=true` disables the safety net
`FieldLayout.TypeAlignment` guesses alignment from `SizeBytes` (`Entity` is declared 8B but truly 4-aligned →
already over-pads). `CSharpEmitter` emits **baked** offsets when a field is `SizeReliable=true`, but falls back to
runtime `Marshal.OffsetOf<State>("field")` when `false` — and `BlueprintDebugSession` slices raw bytes at those
offsets. My plan (`SizeReliable=true`) would trust the bad heuristic for exactly the composite type where it's
least trustworthy.
- **DELTA:** list fields resolve **`SizeReliable=false`** → runtime `Marshal.OffsetOf` layout, sidestepping the
  heuristic entirely. LV-1 gate: a `Marshal.OffsetOf`↔`FieldLayout` round-trip proof with a **non-8-aligned
  element** (not just Entity). (A real `IrTypeRef.Alignment` is the fuller fix; `SizeReliable=false` is the robust
  minimum.)

### F4 (BLOCKER, emit) — per-file generator + "emit wrapper once per (Elem,N)" = `CS0101`
The generator emits source **per `.bp.json`**; two blueprints sharing `List<Entity>[16]` would each emit a
top-level `struct __List_Entity_16` → duplicate-type error (the common case).
- **DELTA:** **nest `__List_{Elem}_{N}`/`__Buf` privately inside each per-blueprint class** (precedent:
  `EmitEqsResultPrevStateStructs`); drop cross-file dedup. Consequence: a list type is **per-asset**, so it
  **cannot cross a graph/peer/AiPrimitive boundary** (structurally-identical distinct CLR types). → **DELTA:
  forbid a list variable on any call-node arg/return pin** (Stage2 diagnostic); crossing-boundary is out of scope v1.

### F5 (MAJOR, write loopholes) — generic `Set/GetVariable` on a list var bypasses the contract
Nothing blocks a generic `SetVariableNode` (whole-struct overwrite, no overflow check) or `GetVariableNode` (O(N)
value copy) at a list variable, silently bypassing Q19-D.
- **DELTA (v1 rule):** a list variable may only be — read via a list-read node, mutated via a list-write node, or
  **whole-assigned via `SetVariable(listVar ← listVar of same shape)`** (a legitimate clone/reset = one flat struct
  copy; **allowed + tested**). **Forbid a list variable on any other generic data pin** (Stage2 diagnostic).

### F6 (MAJOR, debugger) — a list variable is invisible in the state inspector/watch
`BlueprintDebugSession` marshals only scalar types; the generated wrapper type can't be resolved by name → the
field is silently skipped (no crash, but never shown).
- **DELTA:** new slice **LV-5** — marshal a list field (`Count` + first `min(Count,N)` elements), render
  `List<T>[N] Count=k {…}`.

### F7 (MAJOR, editor) — declare UX is CLR-`Type`-keyed and lives in a shared library
`AddVariable` persists a single `TypeId` off a `System.Type`; there is no CLR type for "list of Entity × 16", and
`BlackboardTypeChoiceBuilder` is shared with HSM (no compiler backing there).
- **DELTA:** a **Blueprint-local** composite "element × capacity × initial-length" control; `BlueprintTypeRef`
  gains **`Capacity`** as the list discriminator (**not `IsArray`** — else `TypeRefToCSharp` emits a plain
  `Elem[]`; keep "field-type-is-list" and "pin-is-array" separate). Do **not** widen the shared builder.

### F8 (decisions / minor)
Overflow diagnostic → extend the existing `DebugProbe` (`IrOp_DebugProbe_*`), not a parallel mechanism.
`[InlineArray]` value-copy footgun → indexer writes are safe only inside `ref State`/`ref WorkingState` methods
(true today) — document it; prefer a generated span helper as defense. `EqualityComparer<T>.Default` boxes a
`[BlackboardDtoStruct]` element lacking `IEquatable<T>` → recommend requiring `IEquatable<T>` on struct elements
used with Contains/Find. Alignment-slack + capacity-0/1 + InitialLength-0 edge tests.

### Read binding — DECIDED (user, 2026-08-03)
**`ref`-bind + loop bound snapshotted at entry + per-iteration index clamped to live `min(Count,N)`.** Matches the
component path (`GetComponentRO` aliasing, zero-copy, sees same-tick writes), avoids the O(N) whole-struct copy,
and is never OOB (a mid-loop resize may skip/repeat a slot — documented contract). Value-copy rejected.

### Revised sequencing (was 5 slices / "read near-free" → 6 slices / heavier LV-1)
1. **LV-1 — foundation + safety (Opus).** Capacity-carrying type (discriminator `Capacity`; `SizeReliable=false`);
   resolve unmanaged w/ real size; **per-class nested** `[InlineArray(N)]`+`Count` wrapper; State field; FieldLayout;
   `InitDefault` partial-init (InitialLength); **defensive Count-clamp**; **init-on-all-attach-paths fix**; Stage2
   diagnostics (element-unmanaged; capacity bounds via `V_VariablesAndState`; forbid-list-on-generic-pins;
   forbid-cross-boundary). **Gate:** declares + compiles + `Marshal.OffsetOf` round-trip proof (non-8-aligned
   element) + 184 byte-identical.
2. **LV-2 — read via the SAME consumer nodes (A1 UX) (Opus + Sonnet).** `GetVariablePins` `IsCollection` branch
   (+ Stage0 mirror); wire-bake accepts a list-variable source; **3 `ComponentTypeFqn` gates made Kind-aware**
   (BlueprintNodeModel, Stage2 BP2066, 5 Stage5 guards); Stage5 variable-source branch binding a `ref` to
   `s.{field}` → same IR ops with `Kind=BlackboardFixedList`; `RenderCollectionAccessors` case; ref-bind +
   snapshotted-bound + per-iter clamp.
3. **LV-3 — write nodes (Opus).** Add/InsertAt/Set/RemoveAt/Clear/Resize + whole-list clone; in-place emit w/
   clamp; overflow via `DebugProbe`.
4. **LV-4 — editor declare UX (Sonnet + Opus review).** Blueprint-local element×capacity×initial-length; tier warn.
5. **LV-5 — debugger/watch visibility (Sonnet + Opus review).**
6. **LV-6 — demo + docs.**

**Verdict:** no blocker is unresolvable; the design is sound *with these deltas*. Structural changes: (i) read
path stays **A1 UX** (same consumer nodes — user requirement) but with the **3 gates made Kind-aware + GetVariable
collection projection** (the reviewer's A3 was cleaner but changed UX → rejected); (ii) **memory-safety hardening**
(Count-clamp + init-on-all-paths) promoted into LV-1; (iii) **`ref`-bind read** adopted.

### Open points — RESOLVED (user, 2026-08-03). Design fully pinned.
1. **Cross-boundary passing — OUT OF SCOPE v1**, but must stay doable later (incl. **ref-param** passing — the
   need is expected). Enforcement: a Stage2 diagnostic rejects a list variable wired into any function-graph /
   peer / AiPrimitive arg or return pin. **Forward-compat constraint:** that diagnostic is trivially liftable, and
   the per-asset nested wrapper (F4) can later migrate to a **shared** `(Elem,N)` wrapper type (a cross-file
   `.Collect()` emission pass) WITHOUT asset migration (assets never name the wrapper — it's all generated). LV-1
   must not bake an assumption that hard-blocks a future shared-type + by-ref call ABI.
2. **List on generic pins — FORBID.** Usable only via its collection out-pin (→ consumer nodes), the write nodes,
   and the whole-list clone (#3). Stage2 diagnostic on any other data-pin wiring (closes the O(N)-copy footgun).
3. **Whole-list clone — ALLOW.** `SetVariable(listA ← listB)` of identical shape = one flat struct assignment
   (clone/reset). Compiler test asserts a single flat assignment, not a loop.
4. **Struct-element equality — REQUIRE `IEquatable<T>`** on a `[BlackboardDtoStruct]` element used with
   Contains/Find (validation diagnostic if missing) — avoids `EqualityComparer<T>.Default` boxing/reflection.
5. **Nested lists — FORBID v1.** A first-class fixed-list element (list-of-lists) is rejected (size × tier footgun
   + layout/graph-UX complexity). An **opaque** blittable struct element that internally has a raw `fixed T[N]`
   buffer stays allowed (it's element bytes, not a second-level surfaced list).
6. **Debugger visibility — IN SCOPE (LV-5).** Render a list var as `List<T>[N] Count=k {…first min(Count,N)…}`.
