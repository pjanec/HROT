<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: this whole file - the Batch 88 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# HANDOFF — Batch 88: **Blueprint's live values, and a Details host for BTree / HSM**

> 📌 **Dispatched at `f7f57e79b`.** ⭐ **Branch from it** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⭐ Documents changing after it are **FYI ONLY**.
> ⚠ **If a later document INVALIDATES an item — STOP AND REPORT.** ⛔ **Do NOT adapt, do NOT revert.**
> ⭐ **Rule 3: allocate your own ids and state them.** ⭐ **Rule 1b: push
> `chore: started batch 88 at f7f57e79b` FIRST, before any code.**
>
> ⭐⭐ **User, `2026-08-18`:** *"fix the blueprint value provider. and if possible do it also for btree
> and hsm… pls make the detail panel working for btree and hsm infrastructure wise before we return to
> visual testing."*
>
> ⭐⭐⭐ **BTree and HSM ALREADY HAVE live-value providers** — 📐 `EditorSubsystem:2116/2120` construct
> two `LiveBlackboardValueProvider`s and pass them at `:2178`/`:2188`. ⛔ **There is nothing to do for
> them on `88a`.** ⇒ ⭐ **the user's second ask is already satisfied; their THIRD is `88b`.**

---

## 1. ⭐⭐⭐ BOTH ITEMS ARE ROUTING, NOT CONSTRUCTION — **measured, do not re-derive**

| | 📐 what already exists |
|---|---|
| ⭐⭐ **`88a`** | **`IBlueprintDebugSession.CaptureLiveState(Entity self, Guid assetId)`** → `BlueprintStateSnapshot?` — ⭐ **the live read, by entity and asset**, already consumed by `BlueprintRuntimeInspectorPane:195`. ⛔ **DO NOT build a byte reader.** `DebugMap.StateLayout` *(`StateLayoutField(Name, Type, OffsetBytes, SizeBytes)`)* is the name→offset map and it ships |
| ⭐⭐ **`88b`** | **`VariableDetailsSection` is ALREADY in `AiShared` and deliberately window-less** — *"the host draws it; it does not own a window… that is what lets one Details panel per perspective host the same list."* ⭐ **Batch 87's `SelectionOrigin` / `IDetailsSurfaceClaimant` / `FocusedSurface` are in `AiShared` too, and cross-host by `R-95`** |

⇒ ⚠⚠ **If either item starts looking like new machinery, you have taken a wrong turn — STOP and report.**

---

## 2. 🛠 **88a — Blueprint's live-value provider** *(row 58's unbuilt half)*

📄 **Design basis:** `Q32` §4 **row 58** — *"the Value column… **+ blueprint's `ILiveValueProvider`** and
`UpdateVariableDefaultValueJson`"* · `FINDINGS_VisualCheck_PostBatch86.md` §2 *(`C7`)*.

📐 **Measured:** `EditorSubsystem` passes a provider for BTree and HSM and ⛔ **`null` for Blueprint**;
⭐ **zero `ILiveBlackboardValueProvider` implementations exist under `Hrot.Blueprints.*`** ⇒ the Details
Value column renders **`(pending)`**, which is the **designed** output for a source with no byte reader.

| ⭐ | |
|---|---|
| **implement** | `ILiveBlackboardValueProvider` for Blueprint, over **`CaptureLiveState`** ⇒ `GetLiveVariableValues(asset)` returns the snapshot's field values keyed by **variable name** |
| ⛔ **do NOT reuse `LiveBlackboardValueProvider`** | 📐 it reads through **`BehaviorRegistry`** — ⚠ **BTree/HSM-shaped.** Blueprint state lives in the `BlueprintBlackboard{16384,4096,1024}` partitions. ⭐ **Same INTERFACE, different source** — that is one concept with two adapters, ⛔ not two implementations *(ruling 9)* |
| ⭐⭐ **wire it** | `PerspectiveWorkspaceServices.CreateRegistrar(…, liveValueProvider: …)` for **Blueprint**. 📌 **`R-67`: the omission is the defect** — ⚠ the Blueprint registrar is the one that has forgotten a service **four times** |
| ⭐ **the formatter is SHARED** | ⛔ **do not add a second one** — 📌 `C8`/`BP-01`: a hex string is a regression |
| ⭐⭐ **run-state honesty** | ⭐ **no entity / no live session ⇒ `(pending)`**, unchanged. ⛔ **Never a zero that looks like a value** |

⚠ **Rail:** ⭐⭐ **ask the ARTEFACT** *(Batch 87's lesson)* — assert the **cell text the control would
draw** with a fake live session, ⛔ **not that the provider returns a dictionary.**

---

## 3. 🛠 **88b — a Details host for BTree and HSM** *(`BP-317`)*

📄 **Design basis:** `Q32` **ruling 6** — *"The same Details panel is REUSED for every asset type — HSM,
BTree, Blueprint ⇒ **this is a cross-host deliverable, not a blueprint one**"* · `BP-317` · `R-60`/`R-62`.

📐 **`BP-317` measured it:** exactly **one** window is titled `"Details"` — `BlueprintDetailsWindow`,
registered on Blueprint only, **`sealed`**, in `Hrot.Blueprints.Editor`, blueprint-specific by
construction *(`BlueprintAsset`, `BlueprintNodeDrawerRegistry`, `BlueprintNodeSelection`)*.
⭐ **The AI perspectives have `InspectorWindow` instead.**

| ⭐ | |
|---|---|
| ⭐⭐⭐ **build the HOST in `AiShared`, register it for BTree and HSM** | ⛔ **Do NOT unseal or generalise `BlueprintDetailsWindow`** — ⚠ its node arm is blueprint-shaped. ⭐ **What is shared is `VariableDetailsSection`, and it already is** |
| ⭐⭐ **claim through Batch 87's seam** | the host and the AI outline participate in **`IDetailsSurfaceClaimant`** / `SelectionOrigin` — 📌 **`R-95` is cross-host by user ruling**, so ⛔ **do not invent a second routing rule for the AI hosts** |
| ⭐ **selection yields a SECTION** | 📌 design §1c, as on Blueprint. `BlackboardMyBlueprintModel` already sections by `Role × Scope` *(`Inputs` · `Working State` · `Asset Globals`)* |
| ⭐⭐ **the Value column comes free** | BTree/HSM providers already exist ⇒ ⭐ **`C7` should work on these hosts the day the host lands** |
| ⚠ **`InspectorWindow` STAYS** | ⛔ **This batch does NOT retire it** — 📌 `BP-295`, and `R-86`'s ruling is **not in scope** |

### ⛔ Scope fence — **infrastructure only, and the user said so**

⭐ *"make the detail panel working for btree and hsm **infrastructure wise**"* ⇒ ⭐⭐ **the host, the
routing and the table.** ⛔ **NOT** the shared outline *(row 61)*, ⛔ **NOT** `U-16` *(row 60)*, ⛔ **NOT**
`R-86`'s renamable/editable ruling, ⛔ **NOT** the `⋮` button.

---

## 4. ⚠ WHAT THIS UNLOCKS — **state it in the report**

📌 **`R-21`** suspends visual checks *"until the Details panel is implemented and the emitters and all
access infrastructure are unified"*, and **`R-62`** records that as **met for Blueprint only, because
`R-60`: BTree/HSM have no Details window at all.**

⇒ ⭐⭐⭐ **`88b` is the thing that lifts the suspension for BTree and HSM.** ⭐ **Say so explicitly** —
the coordinator will write the guide against it.

---

## 5. ⭐ GATES — **the rule-8 contract, plus the two this batch owns**

| # | report |
|---|---|
| **1–7** | the standard contract — verbatim commands · **`--no-build` column** *(⛔ `NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` report a STALE BIN)* · golden movement as a **diff shape** · every red confirmed **pre-existing vs `f7f57e79b`** · clean tree after every suite · both quarantine counts · `tracker-counts.py --check` + `rulings-check.py` + **every id you allocated** |
| ⭐⭐ **8** | ⭐ **The `88b` ENUMERATION: every window that hosts a `VariableDetailsSection` or is titled Details, by `search_graph`, with the query and its `total`.** 📌 **`R-74`** — ⚠ **Batch 87's gate 8 found a FOURTH table host my handoff did not know about** |
| ⭐⭐ **9** | ⭐ **What each rail ASKS.** ⛔ **A rail on the provider's return value proves nothing** — 📌 Batch 87: `IsSelected` returned `true` throughout the defect it was meant to catch |

⭐ **Baseline** *(Batch 87)*: AiShared **1424** · Blueprints **3767/3777/10** · BTree.Editor **615** ·
Hsm.Editor **551** · Hrot.Editor **194** · Breakpoints **143** · tracker **open 66 / done 201** ·
rulings **60/60**.

## 6. ⭐⭐ If you must stop

⭐ **Stopping is a good outcome** — 📌 Batch 85 and Batch 87 both did it and both were right.
⭐ **`88a` and `88b` are INDEPENDENT** — ⛔ **landing one and reporting the other is a clean end state.**
⚠ **If `88b` needs a routing decision the design does not settle, STOP** — ⛔ **do not invent one.**
📌 **Batch 87's `2d` is the model: the user supplied the frame in ONE exchange.**
