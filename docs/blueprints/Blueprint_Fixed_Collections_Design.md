# Blueprint Fixed Collections — umbrella design

One capability, three backings. A **fixed-capacity, blittable, ordered collection** (`[InlineArray(N)] Items +
int Count`, capped at N, memcpy-safe) usable uniformly across the blueprint / behavior stack. Supersedes the
standalone "list variables" framing — the blackboard list variable is now just *one backing* of this capability.

## The three backings

| | Backing storage | Blueprint reads | Blueprint writes | C# action (BTree/HSM) | Scope |
|---|---|---|---|---|---|
| **(X) ECS component collection** | `GetComponentRW<C>().field` | ✅ CA-07 consumers (shipped) | **FC-1 — new write nodes** | ✅ `ctx.World.GetComponentRW<C>` by ref | shared on the entity, persists as component data |
| **(Y) Blueprint blackboard list variable** | blackboard `State`/`WorkingState` field `s.field` | CA-07 consumers (via `CollectionKind`) | list-write nodes | AiPrimitive only (its `ws`) | private to a blueprint instance |
| **(Z) Action param / working-state DTO collection** | `ref p.field` / `ref ws.field` (C#) | n/a (not a blueprint var) | n/a | ✅ **free** — `p.field.Items[i] = x` by ref | the action's DTO block |

**Read is already unified** — the CA-07 consumers (`ComponentForEach/ItemGet/ItemCount/Contains/Find`) + the
`CollectionKind` discriminator. **Write unifies** — one write-op family (Add/Set/InsertAt/RemoveAt/Clear/Resize)
over a `CollectionKind` *write-backing* (component vs blackboard field). **(Z) needs no runtime/graph work** — the
action mutates its DTO field by ref in plain C#; the only gap is the editor recognizing the collection field.

## Verified gaps (2026-08-03)
- **(X)** `Nodes.cs` has only component-collection **read** nodes — no write node exists; blueprints can't modify
  a component array element today (only whole `SetComponent`). Real omission.
- **(Z)** `BlackboardFieldClassifier`'s known-type set has **no array concept**; a collection field in a DTO/
  blackboard struct falls to read-only passthrough — the editor won't manage/inspect it. Not supported.
- **(Y)** designed (see `Blueprint_List_Variables_Design.md`), not built.

## The reusable collection type — hand-written, no generator (v1)
Shape: `[InlineArray(N)] struct __Buf { Elem _e0; } ; struct FixedList { int Count; __Buf Items; }`, capped at N,
default-fill free (zeroed blob / `default(Elem)` == zero bytes). This is the `UnitRoster`/`BpCollectionDemo`
pattern generalized to any blittable element (incl. `Entity`, blittable `[BlackboardDtoStruct]`).

- **(X)/(Z)** — the dev **hand-writes** the wrapper (~3 lines) + the accessor class for blueprint use
  (`[BlueprintCollection]`/`[BlueprintCollectionItem]` reads exist; add write-accessor statics). No source
  generator: a generator can't augment a hand-written non-`partial` struct anyway; its only role would be emitting
  the reusable *type*, which is small enough to hand-write. Matches existing precedent.
- **(Y)** — the blueprint **compiler** generates the wrapper (the author declares the list in the editor, writes
  no C#). Per-asset for v1 (F4); a shared `(Elem,N)` type is the deferred cross-boundary/first-class path.
- The three backings share the *shape* + the read/write node machinery, **not** necessarily the same CLR type in
  v1 (cross-boundary passing / a single canonical `FixedList_{Elem}_{N}` is the deferred B-full case — see
  `Blueprint_List_Variables_Design.md` open point #1).

## Sequencing
- **FC-0 — foundation (small, shared).** Canonical shape + accessor convention (reads exist; **add write
  accessors** Add/Set/RemoveAt/Clear) + the write-op **IR family** + `CollectionKind` write-backing discriminator.
  A hand-written reference collection (extend `BpCollectionDemoOps` with mutators).
- **FC-1 = (X) component-collection write nodes.** *First real slice* — fills the felt omission, reuses shipped
  CA-07 reads, exercises the FC-0 write family with **no new storage/layout/safety** work. Natural CA-07 completion.
- **FC-2 = (Y) blackboard list variable.** The `Blueprint_List_Variables_Design.md` design — the "blackboard-field
  backing." Heavy foundation (InlineArray-in-blackboard, `SizeReliable=false`, init-safety, `Marshal.OffsetOf` —
  blockers F1–F4), but the write nodes already exist from FC-1.
- **FC-3 = (Z) action-DTO collections.** Teach the `BlackboardFieldClassifier` the array kind so the editor
  recognizes/inspects a collection field; document the hand-written wrapper pattern. Action access already free.
  Least blocking; pull forward if the BTree DTO need is urgent.

Rationale: (X) is the cheapest, highest-felt-value slice and builds the shared write machinery (Y) reuses; (Z) is
mostly editor recognition riding on the settled pattern.

## Detailed designs
- **(Y) backing:** `Blueprint_List_Variables_Design.md` (full design + adversarial review + F1–F4 deltas + decided
  open points). Its read-path A1 (reuse CA-07 consumer nodes) and in-place write decision carry over.
- **CA-07 (reads, shipped):** `Blueprint_Component_Access_Design.md` / `..._TASK_TRACKER.md`.
- **Architect Q#19** (list-variable decisions): `Architect_Question_19_Fixed_Capacity_List_Variables.md`.
