# HANDOFF — Batch 81: **the surfaces reach the user**

> 📌 **Dispatched at `8f145e40e`** *(coordinator head; branch from it — rule 7)*.
> ⭐⭐ **Source: the user's FIRST VISUAL CHECK**, run `2026-08-17` against Batch 80. 📄 [`GUIDE_Track_C_Visual_Check.md`](GUIDE_Track_C_Visual_Check.md)
> ⛔ **Rule 3: allocate your own ids.** Every `BP-`/`DEBT-` number below is a placeholder.
> ⭐ **Rule 1b: push the `chore: started batch 81 at <sha>` marker before writing any code.**

---

## 0. ⭐⭐⭐ What the visual check found — **the pattern has a TENTH instance, one level down**

⭐ **Batch 80 fixed *"nobody CONSTRUCTS the outline."*** ⛔⛔ **This batch fixes *"nobody FEEDS it."***

> 🔴 **User, verbatim:** *"for btree/hsm MyBlueprint panel is present, but reads **Editor host service
> not available for this perspective yet**. same for hsm and btree."*

📐 **Measured — and it is a two-line dead end:**

```csharp
// AiMyBlueprintWindow.cs:79 — SyncToSelection passes the FIELDS…
Retarget(() => asset.BlackboardVariables, _hostServices, _commands);

// …and AiMyBlueprintWindow.cs:122-126 — the fields are ONLY EVER SET BY Retarget ITSELF.
if (!ReferenceEquals(_hostServices, hostServices) || …) { _hostServices = hostServices; … }

// ⇒ AiMyBlueprintWindow.cs:128 — so _panel is null forever, and :161 draws the placeholder.
_panel = _hostServices != null && _commands != null ? new MyBlueprintPanel(…) : null;
```

⇒ ⭐ **`SyncToSelection` feeds `Retarget` its own nulls.** ⛔ **The window is a closed loop: nothing
outside it can ever supply the services**, because the only setter is the method that reads them.

### 0a. ⭐⭐⭐ The good news — **both pieces already exist and are already reachable**

| what the panel needs | ⭐ where it ALREADY is | measured |
|---|---|---|
| `IEditorHostServices` | ⭐⭐ **`GraphView.Host` — already `public`** | `NodeEdit/src/NodeEditor.Core/View/GraphView.cs:32` |
| `IEditorCommands` | ⭐⭐ **`AiCanvasContext.Commands`** — the document factory already sets it | `AiGraphCanvasWindow.cs:52`; `BTreeDocumentFactory.cs:164` |
| the ACTIVE document's context | ⭐ **`doc.ViewState as AiCanvasContext`** — ⛔ **the established idiom, used ~10× in `EditorSubsystem`** | `EditorSubsystem.cs:2264, 2338, 2380, 2426, 2696, 3263, 3279, 3302` |

⇒ ⛔⛔ **Nothing new needs threading out of the document factories.** ⭐ **The services are per-DOCUMENT**
*(built in `BTreeDocumentFactory` / `HsmDocumentFactory`)*, which is exactly why the registrar cannot
hold them at boot — ⭐ **the window's own doc comment says so and is correct.**

---

## 1. 🔴 **Item 1 — feed the outline** *(this unblocks C, D and F; everything else is behind it)*

### ⭐ The design — **a RESOLVER, not an argument**

⛔ **Do not add "another thing `EditorSubsystem` must pass at construction"** — that is the ninth
instance verbatim, and the services do not exist at construction time anyway.

⭐⭐ **Give the window a `Func<AiCanvasContext?>`** *(or a pair of `Func<…>`)* resolving **the active
document's** context, installed by the registrar exactly as the selection store now is. Then
`SyncToSelection` re-reads services **alongside** the asset, and there is nothing to remember.

| ⚠ the residual seam | ⭐ the control |
|---|---|
| the registrar lives in `Hrot.Editor.AiShared` and **cannot reach the document manager** ⇒ the resolver must come from `EditorSubsystem` | ⭐⭐ **`2026-08-16`'s rule applies: a production caller that HAS a dependency must PASS it — with a forwarding rail PER DEPENDENCY, asserted on the CONSTRUCTED OBJECT.** ⛔ **Not on the registrar's source** |

⭐⭐⭐ **The rail that would have caught this**: drive `SyncToSelection()` with a resolver returning a
real context and assert **`HasPanel == true`**. ⛔ **`HasPanel` already exists for exactly this** *(it is
documented as "also a rail surface")* — ⚠ **and no test asserts it is ever true on the default path.**

⭐ **Also assert the negative:** with the resolver returning `null` the window still draws its reason
and does not throw.

### ⚠ Watch for
- ⭐ the services change **per document**, so a stale `_panel` after a document switch is the obvious
  second bug — ⛔ **re-evaluate on change, not once.**
- ⭐ `HsmDocumentFactory.cs:103` builds `HsmEditorHostServices` the same way; **both AI hosts must work**,
  and the user reported the placeholder on **both**.

---

## 2. 🔴 **Item 2 — two windows are titled "Variables" on the Blueprint perspective**

> 🔴 **User, verbatim:** *"for Blueprint the variables window **still show the old version** with
> Parameters section and Working State section."*

📐 **Measured — it is a TITLE COLLISION, not a missing registration:**

| window | title | registered on |
|---|---|---|
| ⭐ **`AiVariablesWindow`** *(Track C, the new table)* | **`"Variables"`** | ⭐ **all three** — `RegisterCore(windowManager, Variables)` is unconditional *(`PerspectiveWorkspaceRegistrar.cs:363`)* |
| **`BlueprintVariablesManagedWindow`** *(the old control)* | **`"Variables"`** | Blueprint — `BlueprintVariablesManagedWindow.cs:33` |
| `BlueprintVariablesWindow` | **`"Variables"`** | `BlueprintVariablesWindow.cs:378` |

⇒ ⛔⛔ **THREE windows claim the same title**, so the designer opens whichever the dock offers and has
**no way to tell which surface they are looking at.** ⭐ **The new table IS hosted on Blueprint** — the
user simply could not reach it.

⭐⭐ **This is NOT a removal.** 📌 **User ruling, `2026-08-17`:** *"`VariablesPanelControl` — keep for
now."* ⇒ ⭐ **duplicate SURFACE, and the merge is `Q38`.** ⛔ **Coexistence is deliberate; the
INDISTINGUISHABILITY is the defect.**

⇒ ⭐ **Retitle so a designer can name what they are looking at.** ⚠ **Your call which** — the constraint
is that after this batch **the user can open the Track C table on Blueprint on purpose.** ⭐ **A rail on
title distinctness per perspective** is the durable half *(the existing distinctness rails cover window
IDs, ⛔ not titles — that is the hole)*.

---

## 3. ⚠ **Item 3 — the Details panel never responds to an outline click**

> 🔴 **User, verbatim:** *"Details panel keep showing **'No node selected'** no matter if i click
> variable record in My Blueprint."*

⭐ **MEASURE FIRST, then fix — I did not measure this one**, and the guide's part D expects
`Double-click a NAME cell → the properties dialog`. ⚠ **Two different surfaces may be involved**
*(`BlueprintDetailsWindow` vs the row's own dialog)*; ⛔ **do not assume which the user meant** — they
were on the **Blueprint** perspective clicking a **My Blueprint variable row**.

⭐ **Report what you find even if the fix lands in Batch 82** — ⛔ **a STOP with a measurement is worth
more than a guess that builds.**

---

## 4. ⭐ Two QUESTIONS the user asked — **already answered from code, no work needed**

> ⭐ **Recorded here so the answers are not re-derived.** ⛔ **Neither is a defect to fix in this batch.**

### 4a. *"How does **Variables** differ from **Working State**?"* — **they are different `DeclarationKind`s**

| section | kind | what it is |
|---|---|---|
| **Variables** | `DeclarationKind.Variable` | the ordinary blueprint variable |
| **Inputs** | `DeclarationKind.Parameter` | ⭐ written **once at behavior assignment** |
| **Working State** | `DeclarationKind.WorkingState` | ⭐ the **AiPrimitive's** mutable per-instance state |

📌 **Why they exist** *(`BlueprintMyBlueprintModel.cs:74-80`)*: **32 shipped assets are
`(Parameter, WorkingState)`** and were showing an **empty Variables section with no way to see, rename
or delete anything they declare.**

⚠⚠ **BUT the user's confusion is itself a finding:** ⛔ **the two AiPrimitive-only sections render on
EVERY blueprint**, including ones that are not AiPrimitives — ⭐ **which is why they read as duplicates
of "Variables".** 📌 **Filed as an open point, not scheduled** *(see §6)*.

### 4b. *"the **Graphs** section is empty for all blueprints; only **Functions** shows Tick/Main"*

📐 `new(SectionGraphs, "Graphs", 0, null, **false, false**, null)` — ⭐ **non-creatable by design**, and
every blueprint graph is classified as a **Function**. ⇒ ⛔ **"Graphs" is structurally always empty.**
⚠ **Do not delete it in this batch** — *"no rush removals"*: this is a **duplicate SURFACE** question and
belongs with `BP-128`/`Q38`. 📌 **Filed as an open point.**

---

## 5. ⭐ Gates — **the rule 8 contract, all seven rows** *(unchanged from Batch 80)*

⭐⭐ **Your report substitutes for my run.** ⛔ **A missing row is the one thing that sends me to the
terminal.**

| # | report |
|---|---|
| **1** | one row per gate: **verbatim command · pass/fail/skip · Δ vs baseline** |
| **2** | ⭐⭐ **a `--no-build` COLUMN.** ⛔ **`NodeEditor.Core`, `NodeEditor.UI`, `Fhsm.Tests` take NO `--no-build`** *(out of solution ⇒ stale bin)* |
| **3** | ⭐⭐⭐ **golden movement as a DIFF SHAPE**, not a yes/no |
| **4** | ⭐ **every RED confirmed pre-existing against the base sha**, named |
| **5** | ⭐ **working tree CLEAN after every suite run** |
| **6** | ⭐ **both quarantine counts** — ⛔ **a new skip is a finding, not a fix** |
| **7** | ⭐ **`tracker-counts.py --check`** and **every id you allocated** |

⭐ **Baseline:** build **0/69** · AiShared **1318** · Blueprints **3691/3681/0/10** · BTree.Editor **615**
· Hsm.Editor **551** · Generators **270** · Breakpoints **134** · Persistence **136** · Scenarios
**56/68 (12 skipped)** · UrbanCombat **29** · Toolkits **1964** · NodeEdit **208/131** · FastHSM **300**
· tracker **open 64 / done 180**.
⛔ **`Fdp.Toolkits.Tests` = `DEBT-AIB-030`** — seven tests, identity **rotates**; confirm by `--filter`.

⭐⭐ **Revert-goes-red is expected for items 1 and 2.** ⛔ **Item 3 may return a measurement instead of a
fix** — that is an acceptable outcome, **stated**.

---

## 6. 📌 Queued behind this batch — ⛔ **do NOT build here**

| batch | scope |
|---|---|
| ⭐ **82** | the **C-sections' row commands** — 📄 §4 of [`PLAN_Remaining_Work.md`](PLAN_Remaining_Work.md) rev 26. ⛔ **Rename and Delete are silent no-ops on Inputs/Working State rows**, root cause measured |
| ⭐ **83** | **the Watch has no entry points** *(nothing pins a variable, nothing adds a breakpoint)* · ⭐ **author an asset with an `ExpressionTargetField`** so guide part **B** becomes testable |
| ⛔ **open points** | the AiPrimitive-only sections showing on every blueprint *(§4a)* · the always-empty **Graphs** section *(§4b)* — ⭐ **both belong with `Q38`** |
