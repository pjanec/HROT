# HANDOFF — Batch 82: **the C-sections' row commands actually work**

> ⛔⛔ **QUEUED — NOT DISPATCHED.** ⭐ **Batch 81 goes first.** ⚠ **This document is editable until it
> carries a `Dispatched at <sha>` stamp** *(rule 1 / 1a)*.
> ⭐ **Source: the user's first visual check, `2026-08-17`** — steps **A6 / A7**.
> ⛔ **Rule 3: allocate your own ids.**

---

## 1. 🔴🔴 The finding — **Rename and Delete are SILENT NO-OPS on Inputs and Working State rows**

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

## 2. ⭐ The fix — **kind-agnostic lookup, NOT a third prefix** *(and here is why)*

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

## 3. ⛔⛔ Item 2 — **the quick-add is OVERRULED: every section's `[+]` opens the SAME dialog**

> ⭐⭐⭐ **USER RULING, `2026-08-17`, verbatim:** *"working state `[+]` opening no dialog is **wrong,
> inconsistent**. **Must open new variable dialog same as any other variable section.**"*

⚠ **I initially filed this as *not a defect*, because the design note calls it a deliberate choice:**

> 📌 **`BlueprintDocumentFactory.cs:1693-1698`:** *"⭐ Quick-add, not a modal — deliberately unlike
> `editor.create-variable`… the created declaration is **renamable and retypable in place**."*

⇒ ⛔⛔ **The user has overruled it, and the record supports them twice over:**

| | |
|---|---|
| ⭐⭐ **its premise is FALSE** | *"renamable in place"* is exactly what §1 proves does not work |
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

## 4. ⭐⭐⭐ Item 3 — **DISABLE the `[+]`, do not let it be clicked and then refuse**

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
into its own batch is a legitimate outcome; items 1 and 2 stand alone.**

---

## 5. ⭐ Gates — **the rule 8 contract, all seven rows**

⭐ **Identical to Batch 81 §5.** ⛔ **`NodeEditor.Core` / `NodeEditor.UI` / `Fhsm.Tests` take NO
`--no-build`.** ⭐ **Baseline moves with whatever Batch 81 lands — state the base sha you measured
against.**

⚠ **Golden movement is plausible here** — ⛔ **item 1 touches declaration mutation**, so a `.blueprint.json`
round-trip could shift. ⭐ **Report it as a diff shape either way.**
