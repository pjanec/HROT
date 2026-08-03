# Blueprint Fixed Collections — umbrella design

One capability, three homes. A **fixed-capacity, blittable, ordered collection**
(`[InlineArray(N)] Items + int Count`, capped at N, memcpy-safe) usable uniformly across the blueprint /
behavior stack. Supersedes the standalone "list variables" framing — the blackboard list variable is now just
*one home* of this capability. Visual companion: `Blueprint_Fixed_Collections_Diagrams.md` (6 views).

## The three homes

| Home | Where it lives | Blueprint access | C# action (BTree/HSM) access | Scope |
|---|---|---|---|---|
| **Component collection** | field on an ECS component | read via `GetComponent` collection out-pin; write via mutation nodes | `ctx.World.GetComponentRW<C>()` → `c.Items[i]=x` by ref | shared on the entity, persists as component data |
| **Blueprint variable collection** | field in the blueprint's `State`/`WorkingState` blackboard slot | read + write via the same nodes (`GetVariable` collection out-pin) | only when the blueprint *is* an action (its `WorkingState`) | private to one blueprint instance |
| **Action collection** | field in a behavior action's params / working-state struct | — (not a blueprint variable) | `ref p.field` → `p.field.Items[i]=x` by ref (plain C#) | the action's own data |

**Read is unified** — the consumer nodes (`ForEach`/`ItemGet`/`ItemCount`/`Contains`/`Find`) + the
`CollectionKind` discriminator serve component and blueprint-variable collections identically. **Write shares a
verb vocabulary, not machinery** (corrected by the 2nd review, §R1) — `Add`/`Set`/`InsertAt`/`RemoveAt`/`Clear`/
`Resize` exist for both, but component writes are pin-bound (entity → `GetComponentRW` + accessor) and
blueprint-variable writes are variable-id-bound (`SetVariable` lvalue) — different node shapes and emit. **An action
collection's runtime read/write is free by ref**, but its editor/authoring support is *not* trivial (2nd review §R4).
See **"Second review"** below for the corrected picture — read this section's claims through those deltas.

## Verified gaps (2026-08-03, against the code)
- **Component write:** `Nodes.cs` has only component-collection **read** nodes — no write node exists; a blueprint
  can't modify a component array element today (only whole `SetComponent`). Real omission.
- **Action-collection recognition:** `BlackboardFieldClassifier`'s known-type set has **no array concept**; a
  collection field in a DTO / blackboard struct falls to read-only passthrough — the behavior editor won't
  manage or inspect it.
- **Blueprint variable collection:** fully designed (see `Blueprint_List_Variables_Design.md`), not built.

## The reusable collection type — hand-written; no generator (v1)
Shape: `[InlineArray(N)] struct Buffer { Elem _e0; }` + `struct FixedList { int Count; Buffer Items; }`, capped at
N, default-fill free (a zeroed blob + `default(blittableElem)` == zero bytes). This is the
`UnitRoster`/`BpCollectionDemo` fixed-buffer pattern generalized to any blittable element (incl. `Entity`,
blittable `[BlackboardDtoStruct]`).

- **Component & action homes** — the dev **hand-writes** the wrapper (~3 lines). For a component the blueprint
  graph uses it, so it also gets an **accessor class**: read accessors (`[BlueprintCollection]`/
  `[BlueprintCollectionItem]`, already supported) + **new write-accessor statics** (`Add`/`Set`/`RemoveAt`/`Clear`
  that mutate `ref C`). An action needs no accessors — it touches the field directly by ref.
- **Blueprint-variable home** — the **compiler** generates the wrapper + storage (the designer declares
  element × capacity in the editor and writes no C#).
- **No source generator for the TYPE (v1)** — a generator can't augment a hand-written non-`partial` struct; the
  wrapper is ~3 lines. **AMENDED 2026-08-04: the *accessor ops class* IS generated** (FC-1b) — a free-standing
  `static class {Component}CollectionOps` in its own file has no `partial` problem and is ~60 lines of trap-prone
  code (Span write-through, Count/zeroing invariants) per collection. Trigger is **explicitly opt-in per field**
  (`[BlueprintCollectionField(nameof(Count))]`, with `Access`/`Ops` knobs); hand-written accessors win over
  generated (bespoke-semantics escape hatch). Full convention + generator design:
  `Architect_Question_20_Component_Collection_Write.md` §"G1 resolution".
- The three homes share the *shape* + the read/write node machinery, **not** necessarily the same CLR type in v1
  (a single canonical `FixedList_{Elem}_{N}` usable across boundaries is the deferred *first-class-shared* path —
  see below and `Blueprint_List_Variables_Design.md` open point #1).

## How the behavior system (BTree/HSM) and its actions reach it
Investigated 2026-08-03 — captured so the relationship isn't re-derived later.

- **No string-keyed blackboard API.** A behavior action/condition is a C# method taking its data **by ref**:
  `static NodeStatus Act(ref TParams p, ref TWorkingState ws, ref BTreeContext ctx)`. A "variable" *is* a field on
  that struct; the tree-builder binds the struct to a byte offset once (`Marshal.OffsetOf`) and the runtime hands
  the method a live reinterpret-cast `ref`. So an array field is read/written exactly like any field —
  `p.field.Items[i] = x;` mutates in place, zero copy. **A component collection is reachable the same way** via
  `ctx.World.GetComponentRW<C>()`.
- **There isn't one blackboard — there are three components.** `BrainBlackboard` (an action's active param block),
  a heavy-DTO `Blackboard1024`, and the tiered, slot-partitioned `BlueprintBlackboard1024/4096/16384` (which holds
  blueprint `State`/`WorkingState` **and** BTree *stateful* working-state **and** `GetShared`/`SetShared`, each in
  its own keyed slot). So a **blueprint variable collection** lives in a `BlueprintBlackboard*` slot; an **action
  collection** lives in the action's `BrainBlackboard`/partition block; a **component collection** lives on the
  ECS component. They are separate homes, not one shared pool.
- **The one coincidence:** an *AiPrimitive* (a blueprint compiled **into** an action) has its `WorkingState` == the
  action's `ws` — there a blueprint-variable collection and the action's collection are the same bytes.
- **Two type systems.** Blueprints use the structured `BlueprintTypeRef`→`IrTypeRef` path; the behavior editor uses
  a **separate textual classifier** (`BlackboardFieldClassifier`) with no array concept — which is exactly why the
  action-collection home needs that classifier taught the array kind (nothing at the runtime/by-ref level is
  missing).
- **First-class-shared (deferred).** For a hand-authored BTree action to bind to a *blueprint-declared* collection
  variable **by name** (same bytes), two things are needed beyond v1: (1) a **shared** canonical
  `FixedList_{Elem}_{N}` type (so both compilation units reference one CLR type), and (2) a **name→offset binding
  ABI**. v1 keeps this open — the per-asset generated wrapper can migrate to a shared type without asset
  migration, and the call ABI can gain by-ref passing later.

## Sequencing (build slices)
- **FC-0 — foundation (small, shared).** Canonical shape + the **write-accessor convention**
  (`[BlueprintCollectionWrite]`, pinned `ref C` signatures — Q#20 §"G1 resolution") + the mutation-op **IR
  family** + the **tail-always-default zeroing invariant** (all homes: slots ≥ `Count` are always `default(T)`;
  mutators zero vacated slots, grow never fills) + the real `DebugProbe` overflow hook. Hand-written reference
  ops on a **new `[InlineArray]`-backed demo component** (Q#20 G7: `BpCollectionDemo`'s `fixed` buffer cannot
  exercise the R3 write-loss trap) + the InlineArray write round-trip test.
- **FC-1 — component-collection write nodes.** Per Q#20: collection **in-pin** binding with validate-time
  producer self-check (source `GetComponent.Target` unwired; emit binds `self` regardless), single `Ok` out-pin,
  `CuratedStatic`-only, iteration-mutation validator warning (G3), and the **composition-order test** for
  `BlueprintTickSystem` vs the dispatchers (G2 — or fix the `bpTick` splice).
- **FC-1b — the `[BlueprintCollectionField]` source generator** emitting the ops class from the FC-0 template
  (see the amended generator note above).
- **FC-2 — blueprint variable collection.** The `Blueprint_List_Variables_Design.md` design (its internal LV-1…LV-5
  steps). Heavy foundation (InlineArray-in-blackboard, `SizeReliable=false`, init-safety, `Marshal.OffsetOf` —
  review blockers F1–F4), but the write nodes already exist from FC-1.
- **FC-3 — action-collection recognition.** Teach the `BlackboardFieldClassifier` the array kind so the behavior
  editor recognizes/inspects a collection field; document the hand-written wrapper pattern. Action access is
  already free. Least blocking; pull forward if the behavior-DTO need is urgent.

> **Ordering corrected by the 2nd review (§R1, §R2):** FC-2 does **not** reuse FC-1's write machinery (they share
> only the verb names), so the slices are **independent**, not a dependency chain. And component writes (FC-1) are
> **architect-gated** — they collide with the Q#16 write rulings (self-only, `[BlueprintWritable]` set, no managed
> per-field writes), so a `Architect_Question_Component_Collection_Write` doc must land **before** FC-0/FC-1. The
> "cheapest, no new safety work" framing was wrong.

## Second review — adversarial pass on the new surface (2026-08-03)
Three independent reviewers (component-writes · action-DTO · unification) audited the *new* surface against the
code (the blueprint-variable home already carries its F1–F8 review). The **read side held**; the **write side and
the action-DTO home were materially under-designed**. Findings → deltas:

**R1 (blocker) — "write is unified" is false.** Blueprint-variable writes (Q19-D) are **variable-id-bound** (the
`SetVariable` lvalue pattern), a fresh `IrOp_ListWrite*` family with **no `CollectionKind`**. Component writes must
be **pin-bound** (entity-resolved `Collection`/`Target` → `GetComponentRW` + accessor call) — a different node shape
*and* emit. They share only the verb vocabulary. **DELTA:** reframe "one op family over a write-backing" → "shared
verbs, per-home binding/emit"; **decouple FC-1↔FC-2** (independent slices, not a dependency chain).

**R2 (blocker ×4) — component writes collide with the Q#16 write rulings.** The read side is deliberately permissive
(any component, any entity via the GetComponent `Target` pin); the write side has architect constraints the new
nodes bypass: (a) **cross-entity write** — a write consumer inheriting a wired `Target` entity violates Q#16
"self-only"; BP2062 is pinned to `SetComponentNode` and won't catch a new node. (b) **`[BlueprintWritable]` gate
bypassed** — write nodes bake `ComponentTypeFqn` off GetComponent's *unfiltered* read picker, so any component with
a collection becomes writable (Q#16-A). (c) **ManagedMember writes forbidden** — `List<T>.Add/RemoveAt` on a managed
component field is a per-field managed mutation → snapshot-aliasing corruption (Q#16-C / BP2064). **DELTA:** component
writes need their **own architect question** (mirror Q#16) *before* FC-0 — self-only enforcement for a wire-derived
entity, `[BlueprintWritable]` gating, and **scope writes to CuratedStatic + unmanaged only** (reject ManagedMember).

**R3 (blocker, cross-cutting correctness) — the `[InlineArray]` silent-mutation-loss trap.** `GetComponentRW`'s own
doc warns: `ref var q = ref GetComponentRW<T>(e); q.Buf[0] = x;` copies the buffer to a temp → **the write is lost**.
The naive accessor `c.Items[c.Count++] = v` is exactly this shape; the safe form casts to `Span<T>`
(`((Span<Elem>)c.Items)[i] = v` / `MemoryMarshal.CreateSpan`). Not verifiable from a signature. **DELTA (all homes):**
mandate the `Span<T>` access pattern for every `[InlineArray]` element write, gated by an `[InlineArray]`-based write
test. Supersedes the softer review F8 note.

**R4 (blocker/major) — the action-DTO home is not "just recognition."** Runtime read/write by ref *is* free
(verified end-to-end). But: (a) `BlackboardFieldClassifier` has **no live production caller** — the recognition
pipeline must be stood up, not just extended; (b) the **F2 reused-slot zero-init OOB hazard applies to a stateful
action's working-state** collection too (same `AttachSlotsToMemory` path); (c) authoring an initial value needs a
**custom JSON converter** (`ParseParams` STJ can't populate `[InlineArray]`'s private backing field); (d) the
behavior inspector (`LiveBlackboardPanel`) is **composite-blind** → needs marshal work. **DELTA:** reframe from "no
graph work" to "runtime free by ref; needs a recognition pipeline + F2 safety + a JSON converter + inspector marshal."

**R5 (major) — a third blueprint-authored home is unaddressed: Shared (`GetShared`/`SetShared`).** These already
give a blueprint a cross-entity, id-keyed shared value — the natural place a designer reaches for a "shared list,"
absent from Q#19's scope. Also unaddressed: `asset.Parameters` (exposed-on-spawn) as a collection. **DELTA:** add
both to the scope table (in, or out + diagnostic).

**R6 (minor).** The AiPrimitive "WorkingState == action ws" coincidence's usability is **unverified** given F4's
per-asset private-nested wrapper — spike it or mark non-actionable. The `Component*Node` class names become misnomers
once they iterate a blackboard variable — rename (`CollectionForEachNode`…) at build time. The `DebugProbe` overflow
hook is aspirational (only `NodeEnter`/`PinValue` exist) — FC-0 must build it.

**Confirmed sound (no gap):** cross-entity *reads* (GetComponent `Target` pin works), different-CLR-types coherence,
capacity/element-mismatch, and the action by-ref write safety (true `ref` all the way, no value-copy trap in the
delegate chain).

**Revised approach:** `Architect_Question_Component_Collection_Write` (self-only / writable-gate / managed-exclusion)
→ **FC-0** foundation (incl. the `Span<T>` write pattern + a real `DebugProbe` overflow hook) → blueprint-variable
writes **and** component writes as **independent** slices → action-DTO (recognition pipeline + F2 safety + JSON
converter + inspector marshal). Scope table gains Shared + Parameters.

## Decisions folded from the Q#20 architect-role review pass (2026-08-04)

Full detail + evidence: `Architect_Question_20_Component_Collection_Write.md` §"Architect-role review pass" +
§"G1 resolution". The load-bearing ones for ALL homes:

| Decision | Rule |
|---|---|
| **Zeroing invariant (G6)** | ONE rule for all homes: **slots ≥ `Count` are always `default(T)`** — `RemoveAt`/`Clear`/`Resize`-shrink zero vacated slots; grow never fills (blob already zero). Supersedes Q#19's "grow-after-shrink re-zeroes" (that lazy-grow rule is retired; the List-Variables emits change accordingly). `Contains`/`Find` can never match stale-tail garbage even under a `Count` bug; byte image is canonical for snapshots. |
| **`Span<T>` mandate lives in the accessors (G1)** | Generated graph code never touches raw buffers (Q#5-C); writes lower to curated `[BlueprintCollectionWrite]` statics (`Ops.Add(ref __wc, v)`). The R3 pattern is enforced by the accessor recipe + FC-0 round-trip test (+ the FC-1b generator template). Blackboard-home emits (`s.f.Items[i]=…`) must ALSO use the Span form — same trap shape. |
| **Write binding (G4)** | Writes keep the Unreal **collection in-pin** (UX symmetry with reads); validate-time enforces the producer is self (`GetComponent.Target` unwired); emit binds `self` regardless (defense-in-depth). |
| **Out-pin contract (G5)** | One `Ok` bool = component present AND op applied; `DebugProbe` diagnostic distinguishes absent/full/OOB. Failed-op chunk-version bump accepted v1 (documented). |
| **Ordering fact (G2)** | The Q#16-B `[UpdateBefore]` guarantee is **not delivered** by the current editor compositions (`EditorSubsystem.cs:889` appends `bpTick` after the dispatchers; group order is array position). Actual contract today: write-visible-next-tick. FC-1 gate: fix the splice or land a composition-order test. |
| **Mutation-during-iteration (G3)** | Designer rule "a collection is read-only while being iterated"; validator warning when a write to the same baked (component, field) sits inside a `ForEach` over it (wire-dependent semantics otherwise — hoisted vs re-evaluated bound). |
| **R5 scope** | `GetShared`/`SetShared` list slots + list-typed `asset.Parameters`: **OUT v1**, explicit validate-time rejection + diagnostic naming the supported homes. |

> **FC-0 empirical correction to R3 (2026-08-04):** the `[InlineArray]` "ldobj → temp → write lost" mechanism
> from `GetComponentRW`'s doc **does not reproduce** on the current toolchain — the naive `ref`-local element
> write lands; the only reproducible loss is the missing-`ref` **value copy** (generic to all component writes).
> The accessor + `Span<T>` mandate stands on curation (Q#5-C), readonly-read copies, and burying the value-copy
> hazard — not on the indexer trap. Compiler behavior is pinned by two FC-0 tests. See the tracker's FC-0 note.

## Detailed designs & references
- **Blueprint variable collection (full detail):** `Blueprint_List_Variables_Design.md` — design + adversarial
  review (F1–F8) + decided open points + Q#19 decisions. Its read-path (reuse the existing consumer nodes) and
  in-place write / `ref`-bind decisions carry over.
- **Component collection reads (already shipped):** `Blueprint_Component_Access_Design.md` /
  `Blueprint_Component_Access_TASK_TRACKER.md`.
- **Blueprint variable decisions:** `Architect_Question_19_Fixed_Capacity_List_Variables.md`.
- **Diagrams:** `Blueprint_Fixed_Collections_Diagrams.md`.
