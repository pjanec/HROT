<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: sections 2 and 3 — both reported failures, root-caused.
stale-below: nothing.
known-rot: none.
known-conflict: RULINGS.md M-22 said the live Value column works on all three hosts.
  That is FALSE and is corrected in the ledger by this document's §3.
-->
# ⛔⛔ FINDINGS — the visual check of `2026-08-19`

> ⭐ **User, verbatim:** *"like if nothing was fixed: c2: Edit nor Properties context menu items open
> anything · rest of C, D items not applicable, no edit/properties dialog · when simulation running all
> rows items show `(pending)`, not the real value."*

⭐⭐ **Both are REAL, both are measured below, and neither is the defect the previous batches fixed** —
⛔ **each is one layer beneath it.** ⚠ **Batch 94 is in flight and its scope is FROZEN** *(rule 1)* ⇒
these are the next batch, ⛔ **not an amendment.**

---

## 1. ⛔⛔⛔ FIRST — **my own gate was wrong, and this is how**

📌 **`M-22` read: *"the Details Value column shows LIVE values on ALL THREE hosts. Coordinator-verified
on the merged tree."*** ⛔⛔ **FALSE.**

⭐⭐⭐ **What I actually verified was that the ARGUMENT IS PASSED** — I grepped the call sites for
`readRaw:` and `GetLiveObjects` and found them wired. ⛔ **I never checked that a VALUE ARRIVES.**

⇒ ⭐⭐ **This is the failure mode the canon says nothing catches:** *"when a claim depends on what a
symbol MEANS, read its BODY."* ⚠ **A wiring grep answers *"is it connected?"* — ⛔ it cannot answer
*"does anything flow?"***

---

## 2. ⛔⛔ FAILURE ① — **"Edit value…" / "Properties…" do nothing** *(guide `C2`, and it takes `C` and `D` with it)*

### 📐 The chain, measured

| # | | |
|---|---|---|
| ✅ | `VariableTableControl:254` | the context menu opens; `:258`/`:261` fire `EditValueRequested` / `PropertiesRequested` |
| ✅ | `PerspectiveWorkspaceRegistrar:329/347` | `EditGestures` built, `AttachEditGestures` reaches every `IVariableTableHost` |
| ✅ | `:547` | `EditModal.Draw` **is** registered as a frame overlay *(Batch 89's fix, still good)* |
| ⛔⛔ | **`VariableEditGestureBinder.Open:174`** | **`var entry = _entryResolver(row); if (entry is null) return;`** |
| ⛔⛔ | **`PerspectiveWorkspaceRegistrar.ResolveEntry:721`** | **`if (store.ActiveAsset is not IBlackboardManagedAsset asset) return null;`** |

### ⭐⭐⭐ The root cause

```
grep -rn "IBlackboardManagedAsset" --include=*.cs Hrot/ | grep -v Tests
  → HsmAsset:17            : IEditableAsset, IBlackboardManagedAsset, IStitchableAsset
  → BehaviorTreeAsset:234  : IEditableAsset, IBlackboardManagedAsset, IBTreeSyncableAsset, …
grep -rn "class BlueprintAsset" …
  → BlueprintAsset:3       : public sealed class BlueprintAsset        ⛔ implements NOTHING
```

⇒ ⭐⭐⭐ **`ResolveEntry` returns `null` for EVERY Blueprint row, always.** ⇒ `Open()` returns on its
fifth line ⇒ ⛔ **the dialog can never open on the Blueprint perspective — by construction, not by
accident.**

| ⚠ | |
|---|---|
| **why no test caught it** | the resolver is exercised with **AI** assets, which DO implement the interface |
| **why it looks fixed** | ⭐ **Batch 84 fixed a DIFFERENT null** — `facetEditService` not reaching the Blueprint registrar. ⛔ **That was real and is still fixed.** This is one layer down: the service arrives, the gestures attach, the modal draws — **and `Open` bails before any of it matters** |
| ⛔ **the comment is misleading** | `// ⛔ the row's variable is gone — fail closed, never guess` ⇒ it reads as *"a stale row"*. ⭐ **The truth is that a blueprint variable was never EXPRESSIBLE in this resolver** |
| ⭐ **BTree/HSM** | the same gesture should WORK there — ⚠ **unverified; the check only covered Blueprint** |

---

## 3. ⛔⛔⛔ FAILURE ② — **every row reads `(pending)` while the sim runs** — ⭐ **and it is ALL THREE hosts, not just Blueprint**

### 📐 The chain, measured

⭐ The row logic is **correct** and is not the bug:

```
VariableRowSources.ToRow      : live == null && _readRaw == null  ⇒  written: false
VariableValueFormatter.Cell:88: if (!row.HasEverBeenWritten) return PendingFirstWrite;
```

⇒ ⭐⭐ **`(pending)` everywhere means the PROVIDER returned nothing** — so read the provider:

```csharp
// BlueprintLiveValueProvider.GetLiveObjects:97
if (asset is null) return null;
var entity = _store.SelectedEntity;
if (entity is null) return null;          // ⛔⛔ HERE
```

### ⭐⭐⭐ The root cause — **a fourth store**

| | |
|---|---|
| the three providers read | `_btreeSelectionStore` *(`:2121`)* · `_hsmSelectionStore` *(`:2125`)* · `_blueprintSelectionStore` *(`:2208`)* |
| ⭐ **the ONLY `Connect(` in the codebase** | **`EditorSubsystem:1333` — `_selectionBridge.Connect(_aiEditorSelectionStore);`** |
| ⛔⛔ | **a FOURTH store, which no live-value provider reads** |

📐 **And the three per-perspective stores DO get `ActiveAsset`** *(`:2252`–`:2254`)* — ⭐⭐ **`ActiveAsset`
is set and `SelectedEntity` never is.** ⚠ **That asymmetry is why it looks wired**: every other consumer
of these stores uses `ActiveAsset` and works fine.

⇒ ⛔⛔⛔ **`SelectedEntity` is `null` on all three perspective stores, always** ⇒ every provider returns
`null` on its second line ⇒ **every row on every host renders `(pending)` for ever.**

### ⚠ What this does NOT mean

⛔ **Batch 90 is not wasted.** ⭐ The arms, the object/byte split, the honest `(pending)` rule and the
per-name presence measurement are all correct and all still needed — ⭐⭐ **they are downstream of a feed
that never delivers.**

---

## 4. ⭐ WHAT THE TWO HAVE IN COMMON

⭐⭐⭐ **Both are `R-67` — *"a production caller that HAS a dependency must PASS it"* — in its nastiest
form: the dependency IS passed, and it is the WRONG INSTANCE or an UNIMPLEMENTABLE SHAPE.**

| | ① | ② |
|---|---|---|
| what is passed | a resolver that **cannot express** a blueprint asset | a store **nobody writes `SelectedEntity` to** |
| what a wiring grep sees | ✅ connected | ✅ connected |
| what flows | ⛔ nothing | ⛔ nothing |

⇒ ⭐⭐ **The rail that would have caught both is the same one, and it is not "assert the argument was
passed":** ⛔ **assert that a VALUE ARRIVES through the CONSTRUCTED object.**

---

## 5. ⭐ DISPOSITION

⛔⛔ **Batch 94 is in flight, frozen at `58bf7df4e` — these are NOT amendments** *(rule 1)*.
⭐ **They become the next batch**, and it should land **before** the visual check is re-run:
⛔ **Batch 94's pinned-row work sits downstream of failure ②**, so a green Batch 94 will still show
`(pending)` in the Watch until ② is fixed.

⚠ **Ids are the implementation session's to allocate** *(rule 3)* — ⛔ **this document deliberately
files none.**
