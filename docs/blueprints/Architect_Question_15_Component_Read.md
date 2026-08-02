# Architect question #15 — multi-pin ECS component READ (self + any entity, all field/collection kinds)

**Status: ✅ APPROVED (architect) — A1 · B1 · C1 · D1 · E1 · F2 · G1**, plus a managed-component
**immutability / snapshot-aliasing** rule from the architect's follow-up (see that section below). Reads only —
writes stay **action-gated**. Cleared to build; **Slice 1a needs no gate.**

> **Correction (user):** ECS components **can be managed** — fully supported, including replay. Used scarcely
> but **cannot be ignored**. An earlier draft wrongly assumed components are unmanaged-only; the model below is
> corrected and the managed path is now a first-class question (Q15-F/G).

## The need

A designer-facing node to **read ECS component data** in a blueprint graph, generalizing the single-field
`GetComponent` into a **multi-pin reader** (the shipped `GetShared` "Expand to field pins" model), covering
every field kind a component can hold — **managed and unmanaged**.

**User decisions (fixed — listed so the architect sees the target):**
- **Self *and* any entity** — an optional `Target` Entity pin (unwired ⇒ self).
- **All public fields** exposable (managed fields included — not skipped).
- **Nested structs** — expose as a struct pin → decompose with the existing **`Break`** node (no recursive
  split-pin this slice).
- **All collection kinds we use**, with **both** access modes: **random-access** (`Get[i]` / `Length`) **and**
  **iteration** (`ForEach`) — unmanaged *and* managed collections.
- **Maps/Sets** in scope (if such component fields exist — Q15-E).
- **Managed components fully supported** (incl. replay) — the reader must handle them.

## The real constraint model (corrected)

Component reads are **typed member access**, not a byte-offset read: `GetComponentRO<T>(entity).FieldName`.
So — unlike a `GetShared` byte-buffer read — **there is no blittable/offset requirement at the read site**; any
field type is technically readable, managed included. The one hard rule is **downstream persistence**:

| Field value | Read into a pin | Pass to a consumer (e.g. `FunctionCall`) | **Persist** in Variable/WorkingState/Shared |
|---|---|---|---|
| Unmanaged (primitive, enum, `FixedString`, `Entity`, blittable struct, unmanaged collection) | ✅ | ✅ | ✅ |
| **Managed** (`string`, `List<T>`, class ref, managed struct) | ✅ | ✅ | ❌ **BP1503** — state structs are unmanaged-only |

So "all public fields" is honored: **managed fields are readable and exposed**; the editor must just surface
the **persistence caveat** (a managed value can be read and passed along, but can't be dropped into a Variable /
WorkingState / Shared slot). Note `FixedString32/64` / `FixedList<T>` / `[InlineArray]` / `DynamicBuffer<T>` are
the *unmanaged* string/list forms and remain fully persistable; managed `string`/`List<T>` are read-and-pass.

## What exists today (reuse map)

| Piece | Mechanism | Reuse for this node |
|---|---|---|
| Single-field component read | `GetComponentNode` → `IrOp_GetComponentRO<T>(entity)` + `IrOp_FieldRead(.Field)` | multi-field = read-once + **N× `IrOp_FieldRead`** (member access — works for managed fields too) |
| Cross-entity read | `GetShared` optional **`Target` Entity pin** (Slice 2b) | copy verbatim → `GetComponentRO<T>(target ?? self)` |
| Fail-safe / existence | `GetShared` **`Found`** pin + `TryGetShared` (default + `false`, never throws) | mirror for absent component on a Target |
| Field reflection | `SharedStructFieldReflector` reflects a value type's fields → (Name, TypeId, byte **`Offset`**) | ⚠ **offset-based → shared-slot only.** Component reads need a *new* reflector: (Name, TypeId) **without offset**, and it must **not reject** managed fields (the current one bails on non-blittable). |
| Collection **iteration** | `FlowForEach` bakes `Count`/`Item[i]` accessor FQNs → `IrOp_GetComponentRO` + `IrOp_ForEach` | the reflection-free iteration pattern for **unmanaged** collections |
| Collection **random-access** | `ArrayGetNode` / `ArrayMakeNode` — **empty stubs, not lowered** | build (Q15-A) |
| Unmanaged collection kinds | `FixedString`, `FixedList<T>`, `[InlineArray]`, ECS `DynamicBuffer<T>` (433 hits / 88 files) | the unmanaged set to cover |
| State unmanaged rule | `BP1503` (`CheckUnmanagedConstraint`) — **state fields only** (Variables / WorkingState) | the persistence boundary; transient managed pins are fine |

**Unmanaged scalar/enum/string/entity + nested-struct-via-Break multi-pin read is essentially free** (reuse the
GetShared machinery + Target pin + a name/type reflector). Managed fields and collections are the design work.

---

## Q15-A — Reflection-free collection ACCESS model (unmanaged) — the crux

netstandard2.0 can't reflect, so it can't read an *unmanaged* collection generically — it needs a **baked
accessor**. `FlowForEach` proves one shape: baked static `Count(component)` / `Item(component, i)` FQNs. We need
**count + index + iterate** across `FixedList<T>`, `[InlineArray]`, `DynamicBuffer<T>` (+ maps/sets, Q15-E).

- **A1 — Generalize FlowForEach's baked-static-accessor pattern** to all kinds + a random-access node
  (`component.array[i]` + `Length`). *(Reuse-max; one mechanism.)*
- **A2 — Target an existing engine unmanaged-collection abstraction** (uniform `Length`/indexer), if one exists.
- **A3 — Source-gen per-kind accessors.** *(Most machinery; last resort.)*

**Claude's lean: A1** unless an A2 abstraction already exists. **Ask:** is there a sanctioned unmanaged-collection
access convention to bake against, or do we generalize the FlowForEach accessor pattern?

## Q15-B — `DynamicBuffer<T>` RO read authority/consistency mid-tick

Reading an ECS `DynamicBuffer<T>` **RO** during `BlueprintTickSystem` — any ordering/consistency concern if
another system resizes it the same frame?

- **B1 — RO read is safe as-is** (snapshot-consistent within the tick phase).
- **B2 — Needs a defined read barrier / phase ordering.**

**Claude's lean: B1** — needs an **engine-authority confirm** only the architect can give.

## Q15-C — Cross-entity Target read: absent-component semantics

When `Target` lacks the component: mirror `GetShared`'s fail-safe — a **`Found` (bool)** pin, values `default`,
never throw?

- **C1 — `Found` pin + defaults (mirrors `TryGetShared`).** · **C2 — validation-time guarantee (assume present).**

**Claude's lean: C1** — likely just a nod (mirrors an approved pattern).

## Q15-D — Discovery vocabulary: which component TYPES appear in the picker

User chose **all public fields**; open question is which **component types** the picker offers. Reads are RO
(low risk), but "every component in every assembly" is a UX/coupling concern.

- **D1 — Every component type** (managed + unmanaged) in loaded game assemblies; picker shows all fields with the
  persistence caveat on managed ones. *(Maximum reach; matches "all fields".)*
- **D2 — Opt-in marker attribute** (like `[BlackboardDtoStruct]`). · **D3 — Editor allowlist/catalog.**

**Claude's lean: D1** (reads are safe; least friction) — **confirm the architect is comfortable exposing the full
component surface**, vs a curated set.

## Q15-E — Maps/Sets

Do **unmanaged** map/set types exist as component fields (and/or managed `Dictionary`/`HashSet`)? If yes, name
them + the access convention (`Keys`/`Values`/`Contains`/`TryGet`).

- **E1 — none unmanaged on components** → out of scope for the unmanaged path. · **E2 — they exist** → architect
  names type(s) + accessor convention.

**Claude's lean:** we found only `FixedList`/`InlineArray`/`DynamicBuffer` — likely **E1** for unmanaged; managed
`Dictionary`/`HashSet` on a managed component would fall under Q15-G's read-and-pass rules.

## Q15-F — Read API for MANAGED components *(new)*

The single-field node emits `world.GetComponentRO<T>(entity).Field` — a `ref readonly` path that suits unmanaged
structs. **What is the sanctioned read API for a *managed* component?** (`GetComponentRO<T>` likely can't return a
managed `ref readonly`; is it `GetComponent<T>` / a managed-store accessor?) The emitter must pick the right
accessor per component kind (managed vs unmanaged), and the editor must know which a given component is.

- **F1 — one unified accessor** the engine already exposes for both. · **F2 — two accessors**, editor bakes a
  `IsManaged` flag onto the node so the emitter picks correctly.

**Claude's lean: needs an architect fact** (the managed-component read API), then likely **F2**.

## Q15-G — Managed-value flow + replay rules *(new)*

Given managed components are replay-supported: what are the **rules for a blueprint reading + using a managed
value**? Read + pass-to-consumer is clearly fine and can't be persisted (BP1503). But:

- May a managed pin feed **any** downstream node, or only managed consumers (`FunctionCall` to a managed library
  method)? *(An unmanaged consumer of a managed value is a category error we should reject at validate-time.)*
- **Replay/determinism:** does a blueprint merely *reading* a managed field (and passing it to a library call)
  stay within the replay guarantees the engine already provides for managed components, or are there constraints
  (e.g. no mutation, no identity-dependent branching)?

- **G1 — read + pass-to-managed-consumer only; no persistence; reject managed→unmanaged wiring at validate-time.**
- **G2 — additional replay constraints the architect specifies.**

**Claude's lean: G1** as the safe default — **confirm** whether the architect wants extra replay guardrails.

---

## Robustness — picker lifecycle & stale references (design requirement, not an architect ask)

**Picker fill = reflection at editor startup** over loaded game assemblies (same as `[BlueprintCallable]` /
`[BlueprintEvent]` / `[BlackboardDtoStruct]`, built in `BlueprintEditorBootstrap.CreatePaletteRegistry`). Adding a
component → rebuild → **restart the editor** → it appears automatically; no manual registry. (Refresh is at
startup, not live; the component's assembly must be loaded; *which* types qualify = Q15-D.)

**Removed/renamed component (or field)** reuses the shipped `FunctionCall`-unresolved pattern verbatim:

| Requirement | Mechanism (existing precedent) |
|---|---|
| No crash | reflector returns `null` gracefully; node **keeps** its baked `ComponentTypeFqn`/fields (never silently dropped); a stale per-field pin is marked, not removed |
| Warn visually | node drawn in `NodeState.Error` (red outline + tooltip "Component `X` removed/renamed") — same path as `BlueprintNodeModel.IsUnresolvedClrCall` |
| Must not run | (1) editor refuses Quick Reload / Full Rebuild while any node is in error state; (2) backstop — compiler bakes `GetComponentRO<global::X>()` → final **Roslyn compile fails (CS0246)** → no runnable assembly |

**Asymmetry (architect note):** the netstandard2.0 compiler can't reflect, so it **cannot** flag a removed
component at validate-time — it trusts the baked string. The **editor is the primary guard** (reflects → visual
error); the compiler's only backstop is the Stage8 Roslyn failure. Same division `FunctionCall` already relies on.

## Managed component immutability — snapshot aliasing (architect follow-up — MUST enforce)

Managed components are **shallow-copied by reference** into ECS snapshots (`ManagedComponentTable<T>.SyncDirtyChunks`
does `Array.Copy` of references; only `[DataPolicy(SnapshotViaClone)]` deep-clones). A snapshot therefore **shares
the same heap object** as the live world — **mutating a live managed component's fields retroactively corrupts
historical snapshots, Flight-Recorder buffers, and background (SoD) copies.** The fast path relies on managed
components being immutable/safe-by-design; a legitimate update replaces the **whole** component with a fresh
instance via the ECB (a write — out of scope here). Singletons share table references under an immutable-read
contract (also not our concern — reads only).

**Rule for this READ node — a produced managed value is a strictly read-only view; never mutate it, never hand it
to a consumer that might:**
- **Value / immutable fields** (primitive, enum, `string`, unmanaged struct) → read as copies/immutable →
  **always safe**; expose freely.
- **Mutable managed-reference fields** (nested class, `List<T>`, `Dictionary`, …) and the **whole managed
  component** → passing the live reference to a mutating consumer would corrupt snapshots. **Deferred:** not
  exposed as pass-through pins until a **read-only-consumer contract** exists (e.g. a `[BlueprintReadOnly]` marker
  on the consuming method/param), enforced at validate-time.

Unmanaged components are value-copied into snapshots, so none of this touches the Slice 1a / Slice 2 unmanaged
paths — it constrains only the managed field surface (Slice 1b).

## Proposed build order (approved)

1. **Slice 1a (no gate — pure reuse):** multi-pin read of **unmanaged** scalar/enum/`FixedString`/`Entity`/
   nested-struct-via-`Break`, self + `Target`, `Found` pin, "Expand to field pins" + a component/field picker
   (new name/type reflector; discovery = **all component types**, Q15-D/D1). Emit `view.GetComponentRO<T>(…)`.
2. **Slice 1b:** **managed** field read — editor bakes an `IsManaged` flag; emitter emits
   `view.GetManagedComponentRO<T>(…)`. **Only value/immutable fields** exposed (snapshot-aliasing rule above);
   strict read + pass-to-managed-consumer only; **reject managed→unmanaged wiring at validate-time**; uphold
   `BP1503` (no persisting managed into Variable/WorkingState/Shared). Mutable-managed-reference pass-through
   deferred pending the read-only-consumer contract.
3. **Slice 2:** collection read — iteration (generalize `FlowForEach`'s baked `Count`/`Item[i]` accessors) +
   random-access (`Get[i]`/`Length`) over `FixedList<T>` / `[InlineArray]` / `DynamicBuffer<T>` (RO reads
   snapshot-consistent mid-tick per B1). Managed collections via direct C# `foreach`/indexer under the Q15-G +
   immutability rules. No unmanaged maps/sets exist (E1).

## Architect answers (received)

- **Q15-A — A1 APPROVED.** Generalize the `FlowForEach` baked-static-accessor pattern (`Count` + `Item[i]` FQNs)
  for `FixedList<T>` / `[InlineArray]` / `DynamicBuffer<T>`. No universal engine collection abstraction exists (A2
  out).
- **Q15-B — B1 CONFIRMED.** `DynamicBuffer<T>` RO reads are safe mid-tick: `BlueprintTickSystem` runs in the
  Simulation phase and structural changes (resize) must go through the ECB, so the read is snapshot-consistent for
  the tick.
- **Q15-C — C1 APPROVED.** Mirror `TryGetShared`: `Found` (bool) pin + defaults; never throw on a missing
  component on a remote entity.
- **Q15-D — D1 APPROVED.** Expose every component type (managed + unmanaged) in loaded assemblies; RO so risk is
  low. Editor must present the persistence caveat on managed fields.
- **Q15-E — E1 CONFIRMED.** No unmanaged maps/sets as component fields — cover only `FixedList` / `[InlineArray]` /
  `DynamicBuffer`. Managed `Dictionary`/`HashSet` fall under the managed read-and-pass rules.
- **Q15-F — F2 APPROVED. Architect fact:** the sanctioned managed read API is **`view.GetManagedComponentRO<T>(entity)`**
  (does not bump version/dirty). Editor bakes an **`IsManaged`** flag; emitter picks `GetComponentRO<T>` (unmanaged)
  vs `GetManagedComponentRO<T>` (managed).
- **Q15-G — G1 APPROVED.** Read + pass-to-managed-consumer only; **reject managed→unmanaged wiring at
  validate-time**; uphold `BP1503`. No extra *replay* guardrails needed (engine serialization handles managed
  component replay) — **but** the separate **snapshot-aliasing / no-mutation** rule (above) MUST be enforced.
- **Build order APPROVED** (Slice 1a → 1b → 2).
