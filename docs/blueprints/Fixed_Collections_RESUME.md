# RESUME / HANDOFF — Fixed Collections (design phase)  ·  2026-08-03

Self-contained handoff written before a preventive compaction. **The Fixed Collections capability is fully
DESIGNED and documented; NOTHING is built yet.** A second adversarial design review was launched at compaction —
fold its findings into the docs when it returns, then build.

## Branch & state
- **Branch:** `claude/blueprint-ca07d` (off `origin/main`). Working tree clean.
- **Built earlier on this branch (shipped, gated):** CA-07d-1 (Contains/Find) + CA-07d-2 (managed collections) +
  the `ManagedCollectionDemo.bp.json` sample. Generators 184/184 byte-identical. See
  [[project_component_access_workstream]] memory + `Blueprint_Component_Access_TASK_TRACKER.md`.
- **PARKED:** PR `claude/blueprint-ca07d` → `main` (the CA-07d work + all the design docs below). User has not
  yet decided merge timing — ask before building so Fixed Collections can start on a clean branch.
- **Fixed Collections: design only, 0 code.**

## What Fixed Collections is (one capability, three homes)
One fixed-capacity blittable ordered collection — `[InlineArray(N)] Items + int Count`, capped, memcpy-safe —
that can live in three homes, with shared read/write machinery:
- **Component collection** — a field on an ECS component (shared on the entity). Reads shipped (the CA-07 consumer
  nodes); **component element-WRITE nodes are a verified omission** to add.
- **Blueprint variable collection** — a field in the blueprint's blackboard `State`/`WorkingState` (private to the
  instance). Fully designed (`Blueprint_List_Variables_Design.md`).
- **Action collection** — a field in a behavior action's params/working-state DTO. The C# action reads/writes it
  by ref for free; the only gap is the behavior editor **recognizing** the field (classifier has no array concept).

**Read unified** via the consumer nodes + `CollectionKind`. **Write unified** via one op family
(Add/Set/InsertAt/RemoveAt/Clear/Resize) over a `CollectionKind` write-backing (component vs blackboard field).

## The design docs (all committed, cross-linked)
- **`Blueprint_Fixed_Collections_Design.md`** — umbrella: three homes, verified gaps, reusable type (hand-written
  for component/action, compiler-generated for blueprint-variable; **no source generator v1**), the **BTree/HSM +
  action by-ref access mechanics**, the deferred first-class-shared future, and the FC-0→FC-3 build order.
- **`Blueprint_List_Variables_Design.md`** — the blueprint-variable home (= FC-2) in full: design + adversarial
  review (F1–F8 + deltas) + decided open points + the approved editor Declare-UX (Container dropdown, capacity,
  initial length, live budget line).
- **`Architect_Question_19_Fixed_Capacity_List_Variables.md`** — the blueprint-variable decisions (Q19-A…F).
- **`Blueprint_Fixed_Collections_Diagrams.md`** + `diagrams/*.svg` — 6 concept diagrams (map · dataflow · choose ·
  type anatomy · storage layout · authoring surfaces).
- **`Blueprint_Component_Access_*`** — component-collection reads (already shipped, CA-07).

## Decisions locked (condensed)
- Reframe to one Fixed Collections workstream; concept names (Component / Blueprint variable / Action collection).
- Reusable type = hand-written `[InlineArray(N)]`+Count+accessor for component/action homes; compiler-generated for
  the blueprint-variable home. **No source generator v1** (can't augment a hand-written non-partial struct; wrapper
  is ~3 lines). Homes share the *shape* + node machinery, not necessarily the same CLR type (cross-boundary
  passing deferred).
- Blueprint-variable home (Q#19 + review): storage `[InlineArray(N)]`+Count in State/WorkingState; **read reuses
  the existing consumer nodes** (surfaced via `GetVariable` collection out-pin, the 3 `ComponentTypeFqn` gates made
  Kind-aware); **writes in place, bound by variable id** (no whole-array copy); `ref`-bind read; overflow returns
  false + diagnostic; indexed r/w + preallocation (default-filled free); `SizeReliable=false` (runtime
  `Marshal.OffsetOf`); per-class wrapper; **defensive Count-clamp + init-on-all-attach-paths** (memory-safety
  blocker F2); no managed elements; no nested first-class lists; forbid list on generic pins except whole-list
  clone; require `IEquatable<T>` on struct elements for Contains/Find; forbid crossing graph/peer boundaries v1.
- Behavior actions reach a collection **by ref** (`p.field.Items[i]=x`), same as any blackboard field — no
  string-keyed API. Three blackboard components exist; the AiPrimitive `WorkingState` == the action's `ws`.

## Build order (when cleared to build)
- **FC-0** — foundation: canonical shape + accessor convention (reads exist; add **write accessors**) + the
  mutation-op **IR family** + `CollectionKind` write-backing. Extend `BpCollectionDemoOps` with mutators as the
  reference.
- **FC-1** — component-collection write nodes (cheapest, fills the felt gap, reuses shipped reads).
- **FC-2** — blueprint variable collection (the List_Variables design; heavy foundation F1–F4; reuses FC-1 writes).
- **FC-3** — action-collection recognition (behavior classifier learns the array kind; action access already free).
Each slice gated: clean build → **Generators 184/184 byte-identical** → new tests green. Opus does novel
compiler work; Sonnet mirrors editor; Opus reviews + gates + commits.

## Immediate next step (at resume)
1. **A second adversarial design review was launched** (3 reviewers: component-writes/FC-0 · action-DTO/classifier
   · unification-coherence/completeness) aimed at the NEW surface (the blueprint-variable F1–F8 is already done).
   **When it returns, synthesize its findings into the umbrella + a "Review" note, exactly as F1–F8 were folded
   into `Blueprint_List_Variables_Design.md`.**
2. Then: decide the parked PR timing, set up `Blueprint_Fixed_Collections_TASK_TRACKER.md`, and build FC-0 → FC-1.

Memory: [[project_fixed_capacity_list_variables]] (now the Fixed Collections workstream tracker),
[[project_component_access_workstream]] (CA-07 machinery reused).
