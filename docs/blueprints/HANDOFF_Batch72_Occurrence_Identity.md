# HANDOFF — Batch 72: **occurrence identity, both hosts** — `E6`(A) · `E3` · multi-occurrence · BTree corpus

> 📌 **Dispatched at `844f81e93`.** Frozen per rule 1 *(rule 1a: re-dispatch only while this sha is NOT
> in your history)*. ✅ **Batch 71 MERGED at `bdd05a0dc`** — gates re-run by me; ⭐ **I confirmed no file
> under `Hrot.Blueprints.Tests` moved at all.**
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⭐⭐⭐ **The theme, stated once:** *N concurrent occurrences need N regions, keyed by occurrence.*
> **Items 1–2 fix the HSM half — where it is a LIVE defect. Item 3 buys the blueprint half as a
> CAPABILITY.** ⚠ **Do not let those two blur: one is broken, the other is merely absent.**

---

## 0. ⭐⭐⭐ Batch 71 — **your escalation was right, and here is the ruling**

| | |
|---|---|
| ⭐⭐⭐ **`E6` escalated instead of decided — CORRECT** | the key string reaches **outside Track E** *(`FDP/Examples` builds machines by hand)*, and that is plan-level by definition. ⭐ **You drew the line exactly where the standing ask puts it** |
| ⭐⭐ **and you landed the precondition anyway** | `HsmActionKey` — **seven** sites collapsed to one home — plus `HsmActionIdAgreementTests`, which encodes the measurement. ⇒ ⭐ **the decision below is made against a measurement, not a memory.** That is the best possible shape for a blocked item |
| ⭐⭐⭐ **`E0` asserts that it CAN FAIL** | ⛔ **a new green gate proves nothing**, and you proved the opposite before I had to ask twice |
| ⭐ **"generalising over asset kind cost nothing"** | ⇒ **BTree's 26 assets became a line item** — ⭐ **item 4 of this batch**, on your measurement |
| ⚠ **`DEBT-AIB-030`, confirmed both ways** | your red was a **gizmo** registry test, mine was a different one. ⭐ **Same conclusion from two independent runs** |

---

## 1. 🔴🔴 `E6` — **RULED (A): FQN everywhere.** ⭐ *A live defect, not a latent one*

> ⭐⭐⭐ **COORDINATOR RULING `2026-08-17`.** 📄 Plan §4A6.
> ⛔ **(B) is rejected**: it leaves `W9`/`E6` unfixed **and** would make the persisted asset store a
> simple name — ⚠ **reintroducing the exact collision `W9` named, in the FILE FORMAT**, which is the
> worst place to put it. ⭐ **(A)'s breakage is 4 call sites in EXAMPLE projects, visible at compile
> time — the cheapest breakage on the page.**

📐 **I verified the blast radius myself:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
**`:66`, `:70`** · `FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`
**`:631`, `:635`** — `.Activity("Activity_Cruise")` / `.OnEntry("OnEnter_Disabled")`. ⭐ **Four, as you
measured.**

| | |
|---|---|
| ⭐⭐ **one home, already yours** | the id is computed in `HsmActionKey` and nowhere else. ⛔ **Do not add a second spelling for "the FQN form"** |
| ⭐ **update the 4 example call sites** | to the fully-qualified method name |
| ⭐⭐ **invert `HsmActionIdAgreementTests`** | ⛔ **invert, do not delete** — Batch 70's rule. They assert the disagreement today; they must assert **agreement** after |
| 🔴🔴 **the rail that matters** | ⭐⭐⭐ **for every corpus asset, every id the REGISTRAR registers is addressed by the compiled BLOB** — ⛔ **asserted against the real blob, not a recomputation of the key**, which is how you found this |
| ⭐ **and `E6`'s own rail is still owed** | 📄 plan §4B: *"two actions with the same simple name in different types get **distinct ids**, and both re-bake sites agree."* ⭐ **Now seedable** — the dictionary-initializer problem you named disappears once the key is the FQN |

**expected:** ⭐⭐ **the HSM emit baseline MOVES** *(ids change)*. ⛔ **Regenerate deliberately and show
the diff is only ids** — the discipline you used on the 17 snapshots.
🔴 **STOP** if anything **outside `FDP/Examples` and the HSM path** addresses an action by name.

---

## 2. ⭐⭐⭐ `E3` — **occurrence in the HSM action key.** *The dangerous case*

> 📄 **`Architect_Question_34` §7** — ⭐⭐ **of the three occurrence cases, THIS is the one that
> silently corrupts.** Two orthogonal regions running one action write **the same bytes**.
> ⚠ **The blueprint case (item 3) is REFUSED today, not corrupted. This one is not refused.**

| | |
|---|---|
| ⭐ **the seam is cheap** | 📄 plan §4B: *"`r` (region) and `current` (state) are ALREADY IN SCOPE at the `ExecuteAction` call site"* ⇒ **a signature widening, not a data-flow redesign** |
| ⭐⭐ **adopt BTree's algorithm — do NOT invent one** | 📄 design §4.1: `ComputeStatefulSlotKey` at `StatefulSlotScope.Node` is `FNV(assetId ++ nodeVisualId)`. ⭐⭐ **The occurrence is already in that key** — HSM needs the same shape with region+state where BTree has the node |
| ⭐ **the corpus asset already exists** | **`HsmOrthogonalRegions`**, seeded last batch **for this** ⇒ ⭐ **the gate was built before the fix, which is the right order and rarely available** |
| ⚠ **a `FastHSM` `ExtDeps` change** | expected and budgeted |
| ⭐ **the params-base change folds into this same seam** | 📄 design §4.4 — ⚠ **but do NOT do the params half here** unless it is free; say which you did |

**rail — pre-written, 📄 plan §4B `E3`:** ⭐⭐⭐ *"two concurrently-active orthogonal regions running the
SAME action write DIFFERENT bytes."* ⛔ **It must FAIL before your change** — that is the whole point of
this row, and `HsmOrthogonalRegions` exists to carry it.

🔴 **STOP** if the region/state pair is not stable across ticks *(a re-entered region must reach the
same bytes)* — ⛔ **an occurrence key that changes per tick is worse than none**.

---

## 3. ⭐⭐ Blueprint multi-occurrence — ✅ **`Q34` RESOLVED; the user said BUILD IT NOW**

> 📄 **[`Architect_Question_34_Blueprint_Occurrence_Identity.md`](Architect_Question_34_Blueprint_Occurrence_Identity.md) §6** — all three answers, approved `2026-08-17`.
> ⛔⛔ **Do not re-derive them.** ⚠ **This buys a CAPABILITY.** Attaching the same asset twice is
> **refused** today (`AlreadyAttached`, an idempotent no-op) — ⛔ **not corrupted.** Nothing is broken;
> something is absent.

| ruled | build |
|---|---|
| **`Q34-A`** | ⭐ **widen `BlueprintSlotEntry` 16 → 20 B**: add `uint OccurrenceKey`, **`0` = the default/only occurrence** |
| **`Q34-B`** | ⭐ **caller-supplied `InstanceKey`** on `AttachInstanceBlueprintEvent` *(and `Replace`)*; **detach takes the same key** |
| **`Q34-C`** | ⭐ the 3-arg **`TryGetSlotOffset(memory, blueprintId, out offset)` keeps meaning "the occurrence with key `0`"**; a **4-arg overload** takes the key ⇒ ⭐⭐ **every existing call site stays correct by construction** |

### 📐 Measured for you — **the arithmetic and the two places it lands**

| | |
|---|---|
| ⭐ **the cost** | slot tables **64 / 128 / 256 → 80 / 160 / 320** ⇒ payload **928 / 3936 / 16096 → 912 / 3904 / 16032**. ⛔ **`SlotEntrySize`, `Initialize`, `Migrate` and the three tier `const`s + their doc comments all state 16 today** |
| ⭐⭐⭐ **`AlreadyAttached` must become PER KEY** | 📐 `TryFindExistingTier(world, entity, blueprintId, …)` is what makes a second attach a no-op ⇒ ⛔ **if you widen the entry and leave this, multi-occurrence is still refused and the rail passes vacuously.** ⚠ **This is the "ask the artefact" trap in its natural habitat** |
| ⚠ **`DetachFromEntity` scans three tiers by `blueprintId`** | ⇒ it must take the key too, or it detaches **an arbitrary occurrence** |
| ⭐ **the recorded frame grows** | tiers are `[DataPolicy(NoSave)]` = **snapshotted AND recorded**. ⭐ **Expected and accepted** — say the delta out loud |

**rails:** ⭐⭐ 📄 **`DESIGN_Parameter_Model.md` §8's *"params are occurrence-scoped"* rail is finally
buildable** — *"two occurrences of one asset on one entity ⇒ **distinct param bytes**"*, and §8 says
plainly this is **the test that stops the shared-region assumption returning**. ⭐ **Plus:** two
occurrences tick independently · detaching one leaves the other **intact and addressable** · ⛔ **key
`0` behaves exactly as today** *(every existing test is that rail)*.

🔴 **STOP conditions:** ⛔⛔ **`StructureHash` / `persistence-shape` / the 43 blueprint `Emit/*.cs.txt`
MUST NOT MOVE** — ⭐ **the slot entry is runtime storage, not compiled asset shape.** A move means you
touched emission. ⚠ **And if 20 bytes does not hold its alignment cleanly, report the real number
rather than padding silently.**

---

## 4. ⭐ Register BTree's 26 assets into `E0`'s harness

⭐⭐ **On your own measurement:** *"`AiAssetKind` = three delegates ⇒ BTree's 26 ungated assets are a
REGISTRATION, not a rewrite."* ⇒ **do it, and the golden hole closes completely.**

⚠ **Land it LAST**, so items 1–2's baseline movement is not tangled with 26 new files.
🔴 **STOP** if the BTree emitter turns out non-deterministic — ⭐ **the same finding you flagged for
HSM's `Dictionary.Values` ordering**, and it is a report, not a workaround.

📌 **While you are there:** that HSM ordering *(deterministic by implementation detail, not by
construction)* is now safe to fix — ⭐ **item 1 already moves the baseline**, so the objection that
blocked it in Batch 71 is gone. ⚠ **Optional; say if you did.**

---

## 5. ⛔ NOT in this batch

**`E5`** *(needs `-028`(a): `SubtreeAssetId` is not persisted — ⭐ and it now carries a RULING:
📄 `Q34` §7, **provision by KEY, not by attach**)* · **`E7a`** *(`IHostVariableAccess` stays
declared-only)* · **`E7b`'s runtime half** *(⭐ your finding: `ExpressionTargetField` is emitted
NOWHERE — that is a bigger piece and its own item)* · **`BP-281`** *(HSM has no `ParseParams`
counterpart)* · the `InspectorWindow` "STATIC PARAMETERS" retirement · the Track C **visual check**.

---

## 6. Gates

**Baseline — coordinator-verified at `bdd05a0dc`:** build **0 / 69** · Blueprints **3690 / 3680 / 0 / 10** ·
AiShared **1280** · BTree.Editor **615** · Breakpoints **134** · Generators **228** · Hsm.Editor **543** ·
AiEditor.Persistence **136** · Toolkits **1964** · NodeEdit **208 / 131** · tracker **open 61 / done 157**.

| | |
|---|---|
| ⭐⭐ **`FDP/Examples` must BUILD** | ⛔ **item 1 breaks it by design** — the solution build is that item's real gate |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash`. ⭐ **The HSM emit baseline SHOULD move in item 1** *(ids)* **and may move in item 2** — ⛔ **say which files moved in which commit, and why** |
| ⭐⭐ **`Fdp.Toolkits.Tests`** | ⛔ neither a red nor a green is evidence — `DEBT-AIB-030`. ⭐ **Item 3 lands squarely in this assembly**, so confirm any red with `--filter` and name the test |
| **per-item revert-goes-red** · `tracker-counts.py --check` · ⚠ **the two NodeEdit gates take NO `--no-build`** | |

---

## 7. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐ **item 1** — ⭐ **registrar ids and blob ids agree for every corpus asset**, asserted against the
**real blob** · the baseline diff is **only ids**, shown · anything outside `FDP/Examples` that
addressed an action by name.
⭐⭐⭐ **item 2** — ⭐ **did the two-regions rail FAIL before the change?** *(it must)* · **is the
region/state pair stable across ticks?** · whether the params-base half came free.
⭐⭐ **item 3** — ⭐⭐⭐ **`AlreadyAttached` is per-key, and the rail would FAIL if it were not**
*(say how you proved that, not that you did)* · the recorded-frame delta · **blueprint goldens
unchanged, stated FIRST**.
⭐ **item 4** — 26 registered · any BTree emitter non-determinism.
**Always:** ⭐ **every id you allocated** · **which `DEBT-AIB` rows this batch touched**.

⭐⭐⭐ **The standing ask, five batches running and it has paid every time:** when a premise of mine
fails, **STOP and report it.** ⭐ **Batch 71's escalation is the model** — you drew the line at *"this
reaches outside the track"*, landed the half that was unconditional, and left the decision with a
measurement attached to it.
