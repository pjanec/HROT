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
`CollectionKind` discriminator serve component and blueprint-variable collections identically. **Write is unified**
— one mutation-op family (`Add`/`Set`/`InsertAt`/`RemoveAt`/`Clear`/`Resize`) over a `CollectionKind`
*write-backing* (component vs blackboard field). **An action collection needs no graph work** — the action mutates
its struct field by ref in plain C#; the only editor gap is *recognizing* the collection field.

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
- **No source generator (v1):** a generator can't augment a hand-written non-`partial` struct; its only role would
  be emitting the reusable *type*, which is small enough to hand-write. Revisit only if hand-writing wrappers
  across many `(Elem,N)` proves annoying.
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
- **FC-0 — foundation (small, shared).** Canonical shape + accessor convention (reads exist; **add write
  accessors**) + the mutation-op **IR family** + the `CollectionKind` write-backing discriminator. A hand-written
  reference collection (extend `BpCollectionDemoOps` with mutators).
- **FC-1 — component-collection write nodes.** *First real slice* — fills the felt omission, reuses the shipped
  read consumers, exercises the FC-0 write family with **no new storage/layout/safety** work. Natural completion
  of the existing component-collection reads.
- **FC-2 — blueprint variable collection.** The `Blueprint_List_Variables_Design.md` design (its internal LV-1…LV-5
  steps). Heavy foundation (InlineArray-in-blackboard, `SizeReliable=false`, init-safety, `Marshal.OffsetOf` —
  review blockers F1–F4), but the write nodes already exist from FC-1.
- **FC-3 — action-collection recognition.** Teach the `BlackboardFieldClassifier` the array kind so the behavior
  editor recognizes/inspects a collection field; document the hand-written wrapper pattern. Action access is
  already free. Least blocking; pull forward if the behavior-DTO need is urgent.

Rationale: component writes are the cheapest, highest-felt-value slice and build the shared write machinery the
blueprint-variable home reuses; action-collection recognition is mostly editor work riding on the settled pattern.

## Detailed designs & references
- **Blueprint variable collection (full detail):** `Blueprint_List_Variables_Design.md` — design + adversarial
  review (F1–F8) + decided open points + Q#19 decisions. Its read-path (reuse the existing consumer nodes) and
  in-place write / `ref`-bind decisions carry over.
- **Component collection reads (already shipped):** `Blueprint_Component_Access_Design.md` /
  `Blueprint_Component_Access_TASK_TRACKER.md`.
- **Blueprint variable decisions:** `Architect_Question_19_Fixed_Capacity_List_Variables.md`.
- **Diagrams:** `Blueprint_Fixed_Collections_Diagrams.md`.
