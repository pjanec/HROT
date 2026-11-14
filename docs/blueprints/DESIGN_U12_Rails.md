# `U-12` — the three rails, restated · **and the store flip**

> 📌 **Batches 52 (§0–§3) and 53 (§4).** Implementation-session design note. ⭐ The four passes are
> the plan's; ⛔ **the fifth rail (§3) is not in the plan** and is Batch 52's measurement, as §4.4's
> unguarded invariant is Batch 53's.

---

## 0. What moves, in one table

| | today | after |
|---|---|---|
| **`BP1024`** | AiPrimitive with **any** `Variable` ⇒ error | ⛔ **retired** — the code stays defined so the number is never reused |
| **`BP1031`** | Instance with **`Parameter` OR `WorkingState`** ⇒ error | ⭐ **`Parameter` only** — the `(Input, Asset)` half. `WorkingState` becomes legal |
| **`BP1011`** | Library with any **`Variable`** ⇒ error | ⭐ **any asset-scope declaration** — i.e. `Declarations.Count > 0` |
| 🆕 **`BP1673`** | *(did not exist — it could not be reached)* | ⛔ **two declarations sharing a name across kinds ⇒ error** |
| **the store** | three lists on `BlueprintAsset` | ⭐⭐ **FLIPPED (Batch 53)** — one tagged list; the three are live windows. See §4 |

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

## 4. ✅ The store flip — Batch 53

> ⚠ §4 was written in Batch 52 as *"why the flip is not in this batch"*. It landed in Batch 53; the
> original reasoning is kept below it because it is still the correct statement of the constraint.

### 4.1 ⭐⭐ The design turned on one measurement

⛔ **The obvious flip is a lie.** Three `List<T>` snapshots rebuilt on every `get` would satisfy the
serializer and break nothing that compiles — and `asset.Variables.Add(v)` would report success and
write to a list nobody reads. **Trap #5, on the model type this whole programme is about.**

⇒ ⭐ **The three properties must stay LIVE**, which means their type cannot be `List<T>`. So the
question became: what type keeps ~431 existing call sites compiling?

📌 **Measured with the compiler as the oracle** (`[Obsolete]` on all three, one full solution build —
⚠ and the `.Compiler` project's `TreatWarningsAsErrors` had to be relaxed first, or the build stops
there and every downstream site stays invisible):

| | |
|---|---|
| **431** distinct sites, ~100 files | **~400 of them in the test tree** |
| **172** object-initializer sites (`Parameters = …`) | ⇒ the property must be **settable** |
| **112** of those are `= new()` | ⛔ **rules out `IList<T>`** — you cannot `new()` an interface |
| **83** mutation sites (`.Add`, `.Remove`, …) | ⇒ the getter must be **live** |
| **~7** `= new List<VariableDecl> { … }` | ⇒ needs an implicit conversion |
| **3** `List<T>`-only calls — all `AddRange` | ⇒ the view provides it |
| **0** sites assigning the property to a `List<T>` local | ⭐ nothing pins the concrete type |

⇒ 📐 **`DeclarationView<T>`**: a concrete `IList<T>` with a parameterless constructor, an implicit
conversion from `List<T>`, and `AddRange`. ⭐⭐ **The flip landed with ZERO call-site churn.**

### 4.2 ⚖️ §1's ruling — the three properties SURVIVE as public members

⛔ **The handoff's premise — *"`ViewsAreUnreadTests` says nothing reads them, so deleting them is
possible"* — is true only of the two directories that test scans** (`Hrot.Blueprints.Editor` and
`Compiler/Stages`). ⭐ **~400 test-tree sites read them.**

⭐⭐ **And keeping them is what makes the flip verifiable.** Those ~400 assertions become the strongest
regression suite the change could have — written by earlier batches, against the old storage, and
untouched by this one. ⛔ Rewriting ~100 test files in the same commit as the store flip would have
destroyed exactly the independence that makes them evidence.

### 4.3 ⛔⛔ What the old arrangement was silently holding shut (§3.1's question)

| candidate | measured |
|---|---|
| **separately assignable lists** | ✅ 172 + 5 sites — handled by the setter, no churn |
| ⭐⭐ **reference identity of a list** | ⛔ **This one was real.** `BlueprintCompiler`'s copy shared the caller's actual `List` objects, so a stage that added a declaration would have written into the designer's asset. ⭐ The flip copies the store's *entries* instead ⇒ **`U-2`/`BP-229`'s guarantee extends from graphs to declarations for free.** ⚠ Verified safe first: **no compiler stage structurally mutates declarations** — every `Add`/`Remove`/`ReplaceAll` is in the editor |
| **null vs empty** | ✅ never null before or after; the windows are non-null for the asset's lifetime |
| **insertion order within a kind** | ⭐ preserved by the **grouping invariant** — see 4.4 |
| 📌 **fresh facade per read** | ⚠ `AtLocal` used to allocate a new `BlueprintDeclaration` per read; it now returns the stored one. `TaggedDeclarationTests` asserted `NotSame` on two reads — a test of the *mechanism*, not the rule — and was restated against the rule's one live production caller (`BlueprintDocumentFactory` removing by a facade it built itself) |

### 4.4 🔴🔴 The revert probe that mattered — and the one that lied

⭐ **The handoff's question: which gate catches the mistake you are most likely to make?**

| probe | Pass 1 `persistence-shape` | Pass 2 golden |
|---|---|---|
| ⭐⭐ **the store made `public`** *(the likely mistake: "it's the model now")* | 🔴 **RED** — 2 of 3 | ✅ **green, 131/131** |
| ⛔ **grouping invariant broken** (`ReplaceWith` appends instead of inserting at the kind's run) | ✅ **green** | ✅ **green** |

⭐ **Row 1 is the handoff's point proved:** golden cannot see a persistence-only regression; the
baseline can, and it is the only thing that can.

⛔⛔ **Row 2 is the finding.** The invariant the entire design rests on was **unguarded** — because
deserialization happens to set the three properties in the order `Parameters, WorkingState,
Variables`, which is already `KindOrder`. ⇒ appending and inserting agree on exactly the path the
42-asset corpus exercises, **and on no other**. ⭐ `StoreFlipTests` now drives the paths the corpus
cannot (reverse-order assignment, interleaved `Add`), and reddens under that probe.

📌 **A green revert probe is a finding about the tests. Never evidence the code was fine.**

---

## 4′. *(Batch 52)* ⛔ Why the store flip was not in that batch

⭐ **The handoff's own stop points:** *"§1 alone is a complete, valuable batch. §1 + the rails, without
the store flip, is another. ⛔ The store flip is the last thing in, never the only thing."*

⚠ **And the flip has a constraint that makes it its own piece of work:** `Pass 5` requires
`persistence-shape.txt` **unchanged** — the on-disk bytes must not move, because writing v2 is `U-10`'s
wiring. ⇒ the three properties must stop being **storage** while remaining **the serialized shape**,
i.e. they become serialization-only projections over the tagged store. That is a different kind of
change from three one-line predicate edits, with a different revert story, and it is the one gate in
this programme whose failure mode is *"every deployed entity's blackboard is re-initialised."*
