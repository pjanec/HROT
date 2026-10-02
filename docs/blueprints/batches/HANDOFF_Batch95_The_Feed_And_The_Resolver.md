<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 95 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. It builds what FINDINGS_Visual_Check_2026_08_19.md root-caused,
  which is one layer BENEATH everything Batches 84, 89, 90 and 94 fixed. None of those
  is undone by this.
-->
# HANDOFF — Batch 95: **the feed and the resolver**

> 📌 **Dispatched at `c890cbda3`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 95 at c890cbda3` FIRST, before any code.**

> ## ⭐⭐⭐ THE SOURCE
> 📄 **[`FINDINGS_Visual_Check_2026_08_19.md`](FINDINGS_Visual_Check_2026_08_19.md)** — ⭐ **the user ran
> the visual check on `2026-08-19` and reported two failures.** Both are root-caused there and in the
> ledger *(`M-22` re-opened, `M-28` added)*.
> ⭐⭐ **Batch 94 is CORRECT and is not undone by this** — its report saw the finding and said so:
> *"the finding is one layer beneath what this batch built."* ⛔ **Nothing of 94 is to be reverted.**

---

## 1. ⛔⛔⛔ WHY THIS BATCH EXISTS — **read this before the items**

> ⭐⭐ **User, verbatim:** *"like if nothing was fixed."*

⭐⭐⭐ **They are right, and every previous fix was also real.** Four batches fixed four different nulls
on the same two paths, and **each fix was one layer above a deeper one**:

| batch | what it fixed | ⭐ still fixed |
|---|---|---|
| **84** | `facetEditService` never reached the Blueprint registrar | ✅ |
| **89** | `VariableEditModal.Draw` had **zero callers** | ✅ |
| **90** | the Details Value column had no live arm | ✅ |
| **94** | a pinned row was a snapshot; the monitor was inert | ✅ |
| ⛔⛔ **95 — THIS** | ⭐ **the resolver cannot express a blueprint asset · the feed reads a store nobody writes** | |

⇒ ⭐⭐⭐ **The lesson is in `M-22`'s correction and it governs this batch's rails:**
⛔ **"is it connected?" is not "does anything flow?"** ⭐⭐ **Every rail here must drive a VALUE through
the CONSTRUCTED production object.** ⛔ **A rail that asserts an argument was passed is what let all
four of these ship.**

---

## 2. 🛠 **`95a` — the Blueprint asset must be resolvable** *(finding ①)*

### 📐 The measurement, verbatim from the findings

```
VariableEditGestureBinder.Open:174    var entry = _entryResolver(row);
                                      if (entry is null) return;      ⛔ always, on Blueprint
PerspectiveWorkspaceRegistrar.ResolveEntry:721
     if (store.ActiveAsset is not IBlackboardManagedAsset asset) return null;

grep IBlackboardManagedAsset →  HsmAsset:17 · BehaviorTreeAsset:234
grep class BlueprintAsset    →  public sealed class BlueprintAsset      ⛔ implements NOTHING
```

⇒ ⛔⛔ **"Edit value…" and "Properties…" can never open on the Blueprint perspective.** ⭐ And `C`/`D`
of the visual check depend on that dialog, so **the user could run neither.**

### ⭐⭐ What to decide FIRST — ⛔ **and I am NOT ruling it for you**

⚠ **This is a design question, not a wiring one**, and it is why this item is first:

| ⭐ option | |
|---|---|
| **(a)** `BlueprintAsset` implements `IBlackboardManagedAsset` | ⛔ **Suspect.** `BlackboardVariables` is the **AI** blackboard vocabulary; blueprint declarations are `VariableDecl` with a **`Guid Id`** *(`M-16`)*. Making one type answer both shapes is how `R-24`-class trouble starts |
| ⭐⭐ **(b)** the resolver becomes **host-supplied** | ⭐ each perspective already supplies its own selection store and validators; the entry resolver is the same kind of thing. ⭐⭐ **My lean** — but ⚠ **I have NOT measured what the launcher does with the returned entry**, so it may not be substitutable |
| **(c)** the launcher stops needing a `BlackboardVariableEntry` | ⭐ the cleanest end state, ⛔ almost certainly the largest |

⭐⭐⭐ **MEASURE `VariableEditLauncher.Open` FIRST — what does it actually USE the entry for?**
⇒ ⭐ **then pick, state which you picked and why, and build it.** ⛔ **If the answer is (c) or the
measurement contradicts all three, STOP AND REPORT** — that is a design call and it is mine to make
with the user.

⚠ **Do NOT "fix" this by making the menu item greyed on Blueprint.** ⭐ That would be honest and
useless — 📌 the user's rule is *"same information value, no false expectations"*, and here the
expectation is **correct**: the dialog should open.

### ⭐ The rail

⛔ **Not** *"the resolver returns non-null."* ⭐⭐ **Drive the CONSTRUCTED Blueprint registrar: raise
`PropertiesRequested` on a Blueprint row and assert a SESSION OPENS.** ⚠ **BTree/HSM are UNVERIFIED**
*(the check covered Blueprint only)* — ⭐ **rail all three**, and if one already worked, say so.

---

## 3. 🛠 **`95b` — the live feed reads a store nobody writes** *(finding ②)*

### 📐 The measurement

| | |
|---|---|
| the three providers read | `_btreeSelectionStore` *(`EditorSubsystem:2121`)* · `_hsmSelectionStore` *(`:2125`)* · `_blueprintSelectionStore` *(`:2208`)* |
| ⛔⛔ **the ONLY `Connect(` in the codebase** | **`:1333` — `_selectionBridge.Connect(_aiEditorSelectionStore)`, a FOURTH store** |
| ⚠ **and this is why it looks wired** | **`ActiveAsset` IS set on all three** *(`:2252`–`:2254`)*. ⭐ Only the ENTITY half is orphaned |

⇒ ⛔⛔⛔ **`SelectedEntity` is `null` on all three perspective stores, always** ⇒ `GetLiveObjects`
returns `null` on its **second line** ⇒ ⭐ **every row on every host renders `(pending)` for ever** —
exactly what the user saw.

### ⭐⭐ What to build — ⭐ **and prefer the shape that cannot recur**

⛔ **The obvious fix is three more `Connect` calls. ⛔⛔ DO NOT DO THAT** — 📌 it is precisely the shape
`PerspectiveWorkspaceServices` was created to abolish: *"passing one more argument does not compose —
the next shared service is one more thing three call sites must remember, and the third one has now
forgotten three times."*

| ⭐ my lean | |
|---|---|
| ⭐⭐⭐ **the selected entity is ONE fact about the world, not three** | ⇒ **one source, read by all three perspectives.** Whether that is *"the bridge fans out to every registered store"* or *"the stores share one entity cell"* is yours to measure |
| ⚠ **but check the premise first** | ⛔ **I have NOT established that per-perspective entity selection is undesired.** ⭐ If the design intends three independent selections, the fix is instead *"each perspective's bridge connects its own store"* ⇒ **still not three ad-hoc calls** |

⭐ **Sweep for the design record before choosing** — 📌 `RULINGS.md` §4 gives the order. ⛔ **State the
basis in your report** *(rule: no item without a cited basis, or the explicit sentence that none was
found)*.

### ⭐⭐⭐ The rail — **this is the one that matters most in the batch**

> ⭐⭐ **Drive a value end to end through the CONSTRUCTED composition root:** select an entity the way
> production selects it, and assert a **Details cell renders the RUN'S VALUE, not `(pending)`.**
> ⛔ **A rail that asserts `SelectedEntity` was set is the same mistake one level down.**

⚠ **Batch 94's `TheWatchGoesLive` is the right shape and stops one step short** — its own report says
so: *"it does not, and cannot, prove the production HOST supplies a provider."* ⭐ **Close that step.**

---

## 4. 🛠 **`95c` — a rail class against the whole family** *(only if `95a`/`95b` are green)*

📌 **Five instances now, all the same shape:** a dependency **passed** but **wrong-instance** or
**unimplementable**, where a wiring grep says ✅ and nothing flows.

⭐ **Add ONE rail per live capability that asserts a VALUE ARRIVES through the constructed object.**
⛔ **Do not build a generic detector** — 📌 one was tried and thrown away on `2026-08-16`; a sweep over
optional parameters flags dozens of correctly-defaulted ones and gets switched off within a batch.
⭐ **Per capability, explicit, in the suite that is gated.**

⚠ **If `95a`/`95b` consume the batch, STOP** — ⭐ **`95c` is worth a batch of its own and is worth
nothing before the two failures are fixed.**

---

## 5. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **three more `Connect` calls** | 📌 the shape `PerspectiveWorkspaceServices` exists to abolish |
| **greying the menu item on Blueprint** | ⭐ the expectation is CORRECT; the dialog should open |
| **widening `FdpAutoSerializer`** | `R-104` — tooth ② is railed, not fixed |
| **reverting anything from Batch 94** | ⭐ it is correct and one layer above this |
| **routing `BlueprintAssetTickSource`** | ⭐ `BP-348` — deliberately dormant, its rails ungateable *(`DEBT-AIB-030`)* |
| **unifying the four `FindEntityByNetworkId`** | `BP-345` |

---

## 6. ⭐ GATES — the seven-row contract

⭐ **Baseline is Batch 94's table**, base sha **`c890cbda3`**: AiShared **1541** · BTree.Editor **622** ·
Hsm.Editor **554** · Generators **277** · Persistence **143** · Blueprints **3778/0/10 skip** ·
Hrot.Editor **201** · Breakpoints **143** · NodeEditor.Core **211** · NodeEditor.UI **135** ·
Fhsm **300** · Fdp.Presentation **146 filtered** · tracker **open 73 / done 211** · rulings **69/69**.

⭐ **Same seven rows as Batch 94, which reported them well** — ⭐⭐ **keep the `--no-build` column, the
`EXIT=` lines UNFILTERED, and the revert-goes-red probe per item.**
⛔ **`Fdp.Toolkits.Tests` not as a suite** *(`DEBT-AIB-030`)*; ⭐ `--filter` anything you land there.

### ⭐ Extra, this batch only

| ⭐ | |
|---|---|
| ⭐⭐⭐ **the end-to-end rail** | ⭐ **name it, and say exactly which production objects it constructs.** ⛔ **If it constructs a fake at any layer, say WHICH — that is the layer the defect could still hide in** |
| ⭐ **`95a`'s decision** | which of (a)/(b)/(c), **and the measurement of `VariableEditLauncher.Open` that decided it** |
| ⭐ **`95b`'s design basis** | the cited record, ⛔ or the explicit *"searched `<where>`, no design record found"* |

---

## 7. ⭐⭐ WHEN THIS LANDS

⭐ **The visual check gets re-run by the user** — 📌 `R-27` gates the whole `Q38`/`Q44` family on it, and
⭐⭐ **`C`, `D` and `E2`–`E7` all become runnable for the first time in the same pass.**
⚠ **A pin still does not survive a scenario reload** *(`94g`, not started)* — ⭐ **expected, not a
finding.**
