# `U-12` — the three rails, restated · **the store flip is NOT here**

> 📌 **Batch 52 §2.** Implementation-session design note. ⭐ The four passes are the plan's; ⛔ **the
> fifth rail below is not in the plan** and is this batch's measurement.

---

## 0. What moves, in one table

| | today | after |
|---|---|---|
| **`BP1024`** | AiPrimitive with **any** `Variable` ⇒ error | ⛔ **retired** — the code stays defined so the number is never reused |
| **`BP1031`** | Instance with **`Parameter` OR `WorkingState`** ⇒ error | ⭐ **`Parameter` only** — the `(Input, Asset)` half. `WorkingState` becomes legal |
| **`BP1011`** | Library with any **`Variable`** ⇒ error | ⭐ **any asset-scope declaration** — i.e. `Declarations.Count > 0` |
| 🆕 **`BP1673`** | *(did not exist — it could not be reached)* | ⛔ **two declarations sharing a name across kinds ⇒ error** |
| **the store** | three lists on `BlueprintAsset` | ⛔ **unchanged this batch** — see §4 |

---

## 1. ⭐ "Asset scope" needs no new vocabulary

The design's cell table is `(lifetime, scope)`:

| declaration | cell |
|---|---|
| `Parameters` | `(Input, Asset)` |
| `WorkingState` | `(State, Asset)` |
| `Variables` | `(State, Asset)` — **same cell, deliberately** |
| `Graph.LocalVariables` | `(State, Graph)` |

⇒ ⭐ **All three of `BlueprintAsset`'s lists are `Asset`-scope, and nothing else on the asset is.**
Graph locals live on `Graph`, a different type. So the two restatements translate directly onto the
`U-9` vocabulary already in the tree, with **no new enum**:

| plan wording | code |
|---|---|
| *"a Library with **any** `Asset`-scope entry"* | `asset.Declarations.Count > 0` |
| *"an Instance with an **`Input`** entry"* | `asset.Declarations.CountIn(DeclarationKind.Parameter) > 0` |

⚠ **`BP1031` "split" is read as one surviving arm, not two codes.** The gate names a single condition
(*an Instance with an `Input` entry*), and a code whose only other arm is deleted is not a split, it is
a narrowing. **Say so if that reading is wrong** — it is the one place the wording is ambiguous.

---

## 2. ⭐⭐ Measured: all three restatements are corpus-neutral

⛔ Not assumed — counted over **all 58 shipped assets** (42 corpus + 16 recipes):

| dispatch | count | asset-scope declarations |
|---|---|---|
| `AiPrimitive` | 32 | ⭐ **0** carry a `Variable` (what `BP1024` refuses) |
| `Instance` | 23 | ⭐ **0** carry a `Parameter` **or** a `WorkingState` |
| `Library` | 3 | ⭐ **0** carry **anything** — `SmokeMathLib` ×2, `LibraryFunctionsDemo` |

⇒ ⭐ **`BP1011`'s widening is the only one that can refuse something new, and it refuses nothing that
ships.** Golden Tier 1 + Tier 2 must therefore be **unchanged**, and that is a prediction the harness
checks rather than a hope.

---

## 3. ⛔⛔ The rail the plan does not name — and why it becomes reachable

### 3.1 What `BP1024`/`BP1031` were silently also doing

`Stage5.FindVariableRef` resolves a reference **by priority across kinds**:

```
GUID  : Variables → WorkingState → Parameters
name  : Variables → WorkingState → Parameters      ← the fallback hand-authored assets use
```

⭐ The **GUID** path is unambiguous — ids are unique per declaration. ⛔ **The NAME fallback is not.**
Two declarations of the same name in different kinds resolve to whichever the priority order reaches
first, **silently**, with no diagnostic.

⇒ 📌 **That has never been reachable**, because `BP1024` and `BP1031` made the mixture itself illegal:
an AiPrimitive had `Parameters` + `WorkingState` and no `Variables`; an Instance had `Variables` and
neither of the others. **Removing `BP1024` and half of `BP1031` opens exactly that door.**

### 3.2 ⚠ What does NOT save it

| | |
|---|---|
| ⛔ **`U-3` / `VariableRef`** | ✅ verified: `EmissionContext.VarFieldName` now switches on all three kinds, so a resolved ref emits against the right struct. ⛔ **But that fixes the wrong half** — the ambiguity here is in *which* declaration Stage 5 picks, before a `VariableRef` exists |
| ⛔ **`U-14` (Batch 50)** | closes `BlueprintDocumentFactory.MakeUniqueName` — the **editor's auto-namer**. ⚠ A hand-authored `.bp.json` never goes near it |
| ⛔ **Stage 2 today** | ✅ verified: **no duplicate-declaration-name rule exists at all.** Grepped — the only `duplicate` rules cover pin ids and links |

### 3.3 📐 The rail

```
BP1673 — two asset-scope declarations share a name (case-insensitive), across any kinds.
```

⭐ **Case-insensitive**, matching `U-14`'s `OrdinalIgnoreCase` comparison, so the compiler refuses
exactly what the editor's namer refuses.
⭐ **Across any kinds, including within one kind** — a duplicate inside a single list was equally
undiagnosed and is the same defect.

### 3.4 ✅ Built — `V_DeclarationNameUniqueness`, Batch 52

⭐ **Revert-goes-red, measured:** reverting all three rail edits *and* unregistering the validator
reddens exactly **seven** tests — one per rail change, plus the three `BP1673` positives — while the
two `BP1673` negatives (`DistinctNamesAcrossKindsAreFine`, `AGraphLocalMayShadowAnAssetDeclaration`),
`Library_DeclaringNothing` and `NoShippedAssetCarriesACollision` correctly stay green.

⚖️ **Flagged for an architect nod, built rather than deferred.** Building it is the conservative move:
`U-12` knowingly widens what compiles, and shipping the widening without the rail leaves a silent
mis-resolution reachable from any hand-authored asset. ⚠ **What is worth a ruling is the severity** —
error (chosen) versus warning — and whether the same-kind case deserves its own code.

---

## 4. ⛔ Why the store flip is not in this batch

⭐ **The handoff's own stop points:** *"§1 alone is a complete, valuable batch. §1 + the rails, without
the store flip, is another. ⛔ The store flip is the last thing in, never the only thing."*

⚠ **And the flip has a constraint that makes it its own piece of work:** `Pass 5` requires
`persistence-shape.txt` **unchanged** — the on-disk bytes must not move, because writing v2 is `U-10`'s
wiring. ⇒ the three properties must stop being **storage** while remaining **the serialized shape**,
i.e. they become serialization-only projections over the tagged store. That is a different kind of
change from three one-line predicate edits, with a different revert story, and it is the one gate in
this programme whose failure mode is *"every deployed entity's blackboard is re-initialised."*
