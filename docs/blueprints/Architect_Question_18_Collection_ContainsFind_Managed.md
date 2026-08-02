# Architect question #18 — CA-07d: `Contains`/`Find` + managed collections (Slice C3)

**Context.** CA-07a/b/c shipped the Unreal-style collection-pin UX for **unmanaged** component collections:
a `GetComponent` collection out-pin (baked from curated `[BlueprintCollection]`/`[BlueprintCollectionItem]`
accessor pairs) fanning into `ComponentForEach` / `ComponentItemGet[i]` / `ComponentItemCount`. Q#17 deferred
two follow-ons to CA-07d (Slice C3): the **`Contains`/`Find`** query nodes, and **managed collections**
(Q17-B "`Contains`/`Find` follow"; Q17-D "managed handled as a thin variant — direct `foreach`/`[i]`, element
copies"). The overall direction is set; this pins the remaining detail decisions before building.

Proposed slicing: **CA-07d-1 = `Contains`/`Find`** (pure extension of the shipped unmanaged mechanism, low
risk) first; **CA-07d-2 = managed collections** second.

---

## Q18-A — `Contains`/`Find` element comparison

The consumed element is either a **scalar** (int/float/bool/enum) or a **struct value-copy** (Q17-E). How do
`Contains`/`Find` compare a collection element to the query value?

- **A1 — scalar elements only.** `==`; struct elements unsupported (author uses `ForEach` + `Break` + manual
  `Compare`). Cheapest, but a capability hole for struct collections.
- **A2 — scalar `==` + struct `.Equals`.** Per-kind emit branch. More code; struct equality depends on the
  type overriding `Equals` (default `ValueType.Equals` is reflection-based/slow).
- **A3 — `EqualityComparer<T>.Default.Equals` uniformly.** One emit path for every element kind (scalars,
  enums, structs), reflection-free in the generated code
  (`global::System.Collections.Generic.EqualityComparer<TElem>.Default.Equals(item, query)`).

**Claude's lean: A3.** Single emit path, correct for all element kinds, no per-kind branching. The query pin is
typed to the element type (same as `CurrentItem`/`Element`). *Reuse:* the loop is the same `Count`/`Item`
accessor walk as `ComponentForEach`; only the body differs (compare + early-out).

## Q18-B — `Find` return shape, and which nodes to ship

- **B1 — `Find → int Index`** (0-based; `-1` if absent). Unreal-style; consumer must know the `-1` sentinel.
- **B2 — `Find → int Index + bool Found`** (two out-pins). Explicit; mirrors `GetComponent`'s existing `Found`
  out-pin convention; no sentinel foot-gun.

Also: ship **`Contains → bool`** as well (it is `Find` with only the bool)? Round-out says yes — cheap, and the
common "is it in there?" case shouldn't force reading an index.

**Claude's lean: B2 + ship both `Contains` and `Find`.** `Contains(collection, item) → bool`;
`Find(collection, item) → (int Index, bool Found)`. Consistent with the codebase's `Found` convention.

## Q18-C — Managed collection accessor path

A managed collection field (`List<T>`, `IReadOnlyList<T>`, `T[]`) already reflects as `IsManaged` and is
readable under the CA-05/06 managed read-and-pass rules. How does its collection pin resolve its accessors?

- **C1 — reuse curated `[BlueprintCollection]` accessors** (same as unmanaged). Uniform mechanism, but forces
  authors to hand-write `Count`/`Item` helpers even for a plain `List<T>` that already has `.Count`/`[i]`.
- **C2 — auto-resolve native `.Count`/`[i]`** for well-known managed shapes. The editor bakes
  `(field name, element type, CollectionKind=Managed)`; the compiler emits direct member access
  (`comp.Field.Count`, `comp.Field[i]`) under the managed rules — no curated helpers. Matches Q17-D
  "direct `foreach`/`[i]`". *Flag:* this is a NEW auto-resolution path. Q#5-C kept **unmanaged** raw
  fixed/inline arrays OFF-graph (unsafe, no bounds/count) — but managed member-access is a different regime
  (safe, native `Count`/indexer), so C2 does not reopen Q#5-C. Confirm that reading is correct.
- **C3 — both:** auto-resolve well-known shapes (C2), fall back to curated (C1) for exotic managed types.

**Claude's lean: C2** for `List<T>` / `IReadOnlyList<T>` / `T[]` — no curation for the common case, which is the
whole point of "managed is a thin variant." (C1 stays available implicitly: an exotic type simply isn't
auto-recognized and the author can still expose a curated virtual collection as today.)

## Q18-D — Managed collection scope

Which managed collection types are in scope for CA-07d-2?

- Indexable + countable → **all ops** (ForEach/Get[i]/Count/Contains/Find): `List<T>`, `IReadOnlyList<T>`,
  `T[]` (managed element).
- `IEnumerable<T>` (no index/count) → ForEach-only; **defer** (needs a different lowering).
- `Dictionary`/`HashSet`/maps/sets → **out** (Q#15-E: no unmanaged maps/sets; keep managed symmetric for now).

**Claude's lean:** scope = `List<T>` + `IReadOnlyList<T>` + `T[]` (all ops). Defer `IEnumerable<T>`; no maps/sets.

---

## Recommendation summary
| | Lean |
|---|---|
| Q18-A comparison | **A3** — `EqualityComparer<T>.Default.Equals`, one emit path |
| Q18-B Find shape | **B2** — `Index + Found`; ship both `Contains` and `Find` |
| Q18-C managed accessors | **C2** — auto-resolve native `.Count`/`[i]` for known shapes (curated stays for exotic) |
| Q18-D managed scope | `List<T>` / `IReadOnlyList<T>` / `T[]`; defer `IEnumerable<T>`; no maps/sets |
| Slicing | CA-07d-1 `Contains`/`Find` (unmanaged) first; CA-07d-2 managed second |

## Architect answers
*(2026-08-02 — FAST-TRACKED by user; Claude's leans adopted without a separate architect round, on the
basis that Q#17-B/C/D already set the direction and these are refinements.)*
- **Q18-A: A3** — `EqualityComparer<T>.Default.Equals`, one reflection-free emit path for all element kinds.
- **Q18-B: B2 + both nodes** — `Contains → bool`; `Find → (int Index, bool Found)`.
- **Q18-C: C2** — auto-resolve native `.Count`/`[i]` for known managed shapes; curated stays for exotic. (Does
  NOT reopen Q#5-C: managed native indexer is a safe regime, unlike unmanaged raw fixed/inline arrays.)
- **Q18-D:** scope = `List<T>` / `IReadOnlyList<T>` / `T[]` (all ops); defer `IEnumerable<T>`; no maps/sets.
- **Slicing:** CA-07d-1 = `Contains`/`Find` (unmanaged, extend shipped CA-07b) first; CA-07d-2 = managed second.
