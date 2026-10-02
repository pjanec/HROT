# HANDOFF — Batch 81: **visual check round 1 — every finding**

> 📌 **RE-STAMPED — dispatched at `438143ee7`.** ⭐ **User confirmed `2026-08-17`: *"last executed is
> 80"*** ⇒ ⛔ **no run was in progress; rule 1b's blind window is closed by the user's own statement**,
> not by my ancestry check.
>
> ⭐⭐⭐ **THIS BATCH NOW ABSORBS BATCH 82** *(user: "can i run batch 81+82 together?")*.
> 📄 [`HANDOFF_Batch82`](HANDOFF_Batch82_The_Row_Commands_Work.md) is a **pointer stub** — ⛔ **do not
> work from it.** ⭐ **Its three items are §§3a–3c below, unchanged.**
>
> ⭐⭐ **Source: the user's FIRST VISUAL CHECK**, run `2026-08-17` against Batch 80.
> 📄 [`GUIDE_Track_C_Visual_Check.md`](GUIDE_Track_C_Visual_Check.md)
> ⛔ **Rule 3: allocate your own ids.** Every `BP-`/`DEBT-` number below is a placeholder.
> ⭐ **Rule 1b: push `chore: started batch 81 at <sha>` before writing any code.**

---

## ⭐⭐⭐ ORDER OF WORK — **and the ONE item you may drop**

⛔⛔ **Land these in order.** ⭐ **Item 1 is BLOCKING** — the user cannot re-run guide parts **C, D or
F** until it works, so ⛔ **nothing else in this batch is worth delaying it for.**

| # | item | weight | |
|---|---|---|---|
| **1** | ⭐⭐⭐ **feed the outline its host services** *(§1)* | **design** | 🔴 **BLOCKING — do this first** |
| **2** | **rename the duplicate window titles** *(§2)* | small–medium | ⭐ a sweep |
| **3a** | ⭐⭐ **the row commands: rename / delete / duplicate** *(§3a)* | **one-line root cause** | ⭐ highest value per line in the batch |
| **3b** | **every section's `[+]` opens the same dialog** *(§3b)* | small | ⭐ depends on **3a** being right |
| **3c** | ⚠ **grey the `[+]` with a reason** *(§3c)* | ⛔ **UNBOUNDED — crosses into `NodeEditor.Core` + `NodeEditor.UI`** | ⭐⭐⭐ **THE DROP ITEM** |
| **4** | **measure the Details panel** *(§4)* | measurement | ⭐ a report is an acceptable outcome |

### ⛔⛔ The split rule — **stated so you do not have to ask**

> ⭐⭐⭐ **If §3c grows beyond a contained change, STOP IT AND SHIP THE REST.** ⛔ **Do not hold items
> 1–3b hostage to the NodeEdit descriptor work.** ⭐ **Say in your report that you split it, and it
> becomes Batch 82 for real.**

⚠ **Everything else here is independent** — ⭐ **items 1/2 touch `AiShared` + `EditorSubsystem`, items
3a/3b touch `BlueprintDocumentFactory` + `BlueprintMyBlueprintModel`.** ⛔ **Near-disjoint; conflicts
should be rare.**

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

### ⭐⭐⭐ The user has ruled on the resolution — ⛔ **RENAME, and not only these three**

> ⭐⭐⭐ **USER RULING, `2026-08-17`, verbatim:** *"**If many different windows and title 'Variables',
> rename them to unique names pls.**"*

⇒ ⛔ **Not "your call which" any more** *(that was the original stamp)*. ⭐ **Give each a name that says
what it IS**, so a designer can ask for one by name.

| ⭐ | |
|---|---|
| ⭐⭐ **SWEEP, do not spot-fix** | ⛔ **The user said *"many different windows"* — ⚠ I measured only the `"Variables"` collision.** ⭐⭐ **Find every duplicate title across the three perspectives and report the full list**, then rename. 📐 **There are 50 `ManagedWindow` subclasses** *(`Q38` §1)* |
| ⭐⭐ **name by ROLE, not by assembly** | ⛔ **"AiVariablesWindow" is an implementation name.** ⭐ A designer distinguishes *what it shows*, not which DLL it came from |
| ⚠ **titles are USER-FACING strings** | ⭐ **They are also what layout persistence and the dock may key on — CHECK before renaming**, and say in your report whether any saved layout is affected |
| ⭐ **the durable half** | ⭐⭐ **a rail on TITLE distinctness per perspective.** ⛔ **The existing distinctness rails cover window IDs, not titles — that is the hole this fell through** |

⛔ **Still not a removal** — all three surfaces keep drawing. ⭐ **Renaming is what makes the deliberate
coexistence usable until `Q38` decides the merge.**

---

## 3a. 🔴🔴 **Item 3a — Rename and Delete are SILENT NO-OPS on Inputs and Working State rows**

> ⭐⭐ **User, verbatim:** *"variable record in Working State shows just **'Delete' (does nothing)** and
> **'Rename' (shows rename dialog but does not cause name to change)** items in the context menu (while
> in Variables section there are many more items)."*

### ⭐⭐⭐ Root cause — measured, and it is **one line**

📐 **`BuildDeclarationItems` emits the SAME `var:` prefix for all THREE declaration kinds:**

```csharp
// BlueprintMyBlueprintModel.cs:383 — called for Variable, Parameter AND WorkingState
ItemId: $"var:{v.Id}",
```

📐 **…but the lookup every row command goes through is KIND-SCOPED to `Variable`:**

```csharp
// BlueprintDocumentFactory.cs:1030-1033
private static VariableDecl? FindVariable(BlueprintAsset asset, string itemId)
    => TryItemGuid(itemId, "var:", out var id)
        ? asset.Declarations.Of(DeclarationKind.Variable).FirstOrDefault(d => d.Id == id)?.AsVariableDecl
        : null;                    // ⛔⛔ ONLY DeclarationKind.Variable
```

⇒ ⭐⭐ **For an Input or Working-State row the id parses, the lookup returns `null`, and every command
falls through to `return false`:**

| gesture | what happens | ⭐ matches the user exactly |
|---|---|---|
| **Rename** | `ItemDisplayName` → `null` ⇒ the dialog opens **with an empty current name**, `RenameItem` returns `false` | ✅ *"shows rename dialog but does not cause name to change"* |
| **Delete** | `DeleteItem` finds no variable, no custom event ⇒ `return false` | ✅ *"does nothing"* |
| ⚠ **Duplicate** | ⛔ **same fall-through — the user did not test it, and it is broken too** | — |

⛔⛔ **And `DeleteItem` would still be wrong even if the lookup found it:**

```csharp
// BlueprintDocumentFactory.cs:965 — hard-codes the kind when building the removal key
return asset.Declarations.Remove(BlueprintDeclaration.For(DeclarationKind.Variable, variable));
```

### ⭐⭐ The rule that already existed, and that the C-sections broke

> 📌 **`BuildLocalVariableItems`' own doc comment, verbatim:** *"the `local:{id}` id form is what
> `editor.rename-item` / `editor.delete-item` **dispatch on**… the declarations live in different lists
> and have different delete rules, so **one prefix per kind is what keeps them apart**."*

⇒ ⭐ **The locals section obeyed it. The C-sections did not.**

---

### ⭐ The fix — **kind-agnostic lookup, NOT a third prefix** *(and here is why)*

⚠ **The stated rule says "one prefix per kind".** ⛔ **Do not apply it here** — read *why* it exists:
*"the declarations live in **different lists** and have **different delete rules**."*

📐 **That premise is TRUE for locals** *(they live on `graph.LocalVariables`)* and ⛔ **FALSE for these
three** — `Variable`, `Parameter` and `WorkingState` all live in **one list, `asset.Declarations`**, with
**one delete rule**, and their ids are already unique GUIDs.

⇒ ⭐⭐ **Resolve across all declarations and carry the FOUND declaration's own kind into the mutation.**
⛔ **A third and fourth prefix would be ceremony that buys nothing and adds two more places that know
the mapping** — the exact objection `AddDeclaration`'s own comment raises about `BlueprintDeclaration.Create`.

⚠ **If you find a delete rule that genuinely differs by kind, STOP and report it** — that would
invalidate this reasoning, and ⭐ **it is a design question, not an implementation detail.**

### ⭐ Rails
| | |
|---|---|
| ⭐⭐ **one per kind × per gesture** — rename · delete · duplicate, on **Parameter** and **WorkingState** | ⛔ **not one test with a loop that could pass on `Variable` alone** |
| ⭐ **assert the OBSERVABLE outcome** *(the declaration's name changed / it is gone from `asset.Declarations`)* | ⛔ **not that the command was registered** — the whole defect is a registered command that returns `false` |
| ⭐ **`ItemDisplayName` returns the real name** for all three kinds | ⚠ that is what made the dialog open empty |

---

## 3b. ⛔⛔ **Item 3b** — **the quick-add is OVERRULED: every section's `[+]` opens the SAME dialog**

> ⭐⭐⭐ **USER RULING, `2026-08-17`, verbatim:** *"working state `[+]` opening no dialog is **wrong,
> inconsistent**. **Must open new variable dialog same as any other variable section.**"*

⚠ **I initially filed this as *not a defect*, because the design note calls it a deliberate choice:**

> 📌 **`BlueprintDocumentFactory.cs:1693-1698`:** *"⭐ Quick-add, not a modal — deliberately unlike
> `editor.create-variable`… the created declaration is **renamable and retypable in place**."*

⇒ ⛔⛔ **The user has overruled it, and the record supports them twice over:**

| | |
|---|---|
| ⭐⭐ **its premise is FALSE** | *"renamable in place"* is exactly what §3a proves does not work |
| ⭐⭐⭐ **and CONSISTENCY outranks the saving** | ⛔ **the note weighed "a second modal" against nothing.** ⭐ **The real cost it skipped is a designer learning TWO different meanings for one button** |

⇒ ⭐ **`editor.create-parameter` and `editor.create-working-state` open the SAME modal
`editor.create-variable` does**, taking a **name and a TYPE**, and create a declaration of **that
section's kind**. ⛔ **Not a third modal — the same one, parameterised by kind.**

⚠ **Update the design note in place.** ⛔ **Do not leave a comment asserting a choice that was
reversed** — *"a doc asserting an unbuilt feature is worse than the gap"* *(plan §4C)*, and this is the
same failure pointed the other way.

📌 **`AddDeclaration` stays** — it is the one path that knows the kind→concrete-type mapping. ⭐ **The
modal supplies name and type; `AddDeclaration` still builds the declaration.**

---

## 3c. ⭐⭐⭐ **Item 3c — THE DROP ITEM** — **DISABLE the `[+]`, do not let it be clicked and then refuse**

> ⭐⭐⭐ **USER RULING, `2026-08-17`, verbatim:** *"**Disabling/graying a `[+]`** on variable section but
> **showing explanatory tooltip** (same as the info window now) **would be better than allowing user to
> click the button and then saying that it is not possible** — same information value, **no false
> expectations**."*

### ⭐⭐ This does NOT contradict `Q26-B2` — read the ruling carefully

📌 **`Q26-B2`, restated at `BlueprintMyBlueprintModel.cs:116`:** *"the '+' **stays** and REFUSES OUT
LOUD, naming the reason, rather than **vanishing** and teaching nothing."*

⇒ ⭐⭐⭐ **The ruling forbids VANISHING. Greying is not vanishing.**

| | `Q26-B2` demands | ⭐ greyed + tooltip |
|---|---|---|
| the `[+]` stays visible | ✅ | ✅ |
| the reason is taught | ✅ | ✅ **in the tooltip, BEFORE the work** |
| ⭐ **no wasted authoring** | ⛔ **not addressed — the ruling never considered the order** | ✅ |

⇒ ⭐ **This is a REFINEMENT of `Q26-B2`, not a reversal.** ⛔ **Record it as such** in the design note,
citing the user and the date.

### ⚠⚠ The cost — **measured, and it crosses the NodeEdit boundary**

📐 **`MyBlueprintSectionDescriptor` lives in `NodeEditor.Core`** *(`FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IMyBlueprintModel.cs:24`)*:

```csharp
public sealed record MyBlueprintSectionDescriptor(
    …, bool CanCreateItems, …, string? CreateCommandId);   // ⛔ no reason, no disabled state
```

| ⚠ what this needs | where |
|---|---|
| a **reason** field *(e.g. `string? CreateDisabledReason`)* | ⛔ **`NodeEditor.Core`** |
| the panel drawing it **greyed with a tooltip** | ⛔ **`NodeEditor.UI`** |
| ⭐⭐ **descriptors that vary PER GRAPH** | ⚠ **`BlueprintMyBlueprintModel._sections` is `static readonly` — ONE instance for every asset and every graph.** 📌 **That is the stated reason `CanCreateItems` could not vary**, and it is now the thing that must change |

⛔⛔ **Both NodeEdit assemblies are OUT OF SOLUTION ⇒ their gates take NO `--no-build`.** ⭐ **This is
the "two NodeEdit gates" cost the existing comments cite twice — pay it deliberately, and say in your
report that you did.**

⭐ **Make it GENERAL, not macro-specific** *(round-out preference)*: any section that cannot currently
create says **why**. ⛔ **Do not special-case the macro-locals arm** — the same mechanism should serve
every future refusal.

⚠ **If the per-graph descriptor change turns out to be large, STOP and report** — ⭐ **splitting item 3
into its own batch is a legitimate outcome; items 1–3b stand alone.**

---

---

## 4. ⚠ **Item 4 — the Details panel never responds to an outline click**

> 🔴 **User, verbatim:** *"Details panel keep showing **'No node selected'** no matter if i click
> variable record in My Blueprint."*

⭐ **MEASURE FIRST, then fix — I did not measure this one**, and the guide's part D expects
`Double-click a NAME cell → the properties dialog`. ⚠ **Two different surfaces may be involved**
*(`BlueprintDetailsWindow` vs the row's own dialog)*; ⛔ **do not assume which the user meant** — they
were on the **Blueprint** perspective clicking a **My Blueprint variable row**.

⭐ **Report what you find even if the fix lands in Batch 82** — ⛔ **a STOP with a measurement is worth
more than a guess that builds.**

---

## 5. ⭐ Two QUESTIONS the user asked — **already answered from code, no work needed**

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

## 6. ⭐ Gates — **the rule 8 contract, all seven rows** *(unchanged from Batch 80)*

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

## 7. 📌 Queued behind this batch — ⛔ **do NOT build here**

| batch | scope |
|---|---|
| ⭐ **83** | **the Watch has no entry points** *(nothing pins a variable, nothing adds a breakpoint)* · ⭐ **author an asset with an `ExpressionTargetField`** so guide part **B** becomes testable |
| ⛔ **open points** | the AiPrimitive-only sections showing on every blueprint *(§4a)* · the always-empty **Graphs** section *(§4b)* — ⭐ **both belong with `Q38`** |
