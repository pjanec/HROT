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

## 3. ⭐⭐ Item 2 — **the quick-add's OWN JUSTIFICATION depends on item 1**

> ⭐ **User, A6:** *"Clicking [+] in Working State section adds immediately 'NewState' in that section,
> **no dialog** for selecting data type etc."*

⛔ **This is NOT a defect — it is documented, deliberate, and the design note says so:**

> 📌 **`BlueprintDocumentFactory.cs:1693-1698`, verbatim:** *"⭐ **Quick-add, not a modal** — deliberately
> unlike `editor.create-variable`… ⚠ Stated so it reads as a choice rather than an omission: **the
> created declaration is renamable and retypable in place**, exactly like `AddVariable`'s `NewVar`."*

⇒ ⭐⭐⭐ **The choice was sound and its PREMISE IS FALSE.** ⛔ **"Renamable in place" is precisely what
§1 proves does not work** — so the quick-add currently produces a declaration named `NewState` that the
designer **cannot rename, cannot retype and cannot delete.**

| ⇒ | |
|---|---|
| ⭐⭐ **Fixing §1 restores the premise** — ⛔ **do not add a modal**; that would discard a deliberate design choice to work around a bug | |
| ⚠ **BUT verify the OTHER half: "retypable in place."** ⭐ **If there is no type editor on those rows either, the premise is still false after §1** — ⛔ **and then it IS a finding.** 📌 **Report it; do not silently build a type picker** | |

---

## 4. ⚠ Item 3 — **the Macro refusal fires too late**

> ⭐ **User, A7:** *"Clicking on Local variable section's [+] **showed New variable dialog** and **only
> once confirmed** is shown the refusal indicator."*

⭐ **Refusing OUT LOUD is correct and is a ruling** — 📌 `Q26-B2`, restated at
`BlueprintMyBlueprintModel.cs:116`: *"the '+' stays and REFUSES OUT LOUD, naming the reason, rather than
vanishing and teaching nothing."*

⛔ **What is wrong is the ORDER.** ⚠ **Making a designer name and type a variable before telling them it
cannot exist is worse than a disabled button** — it teaches the reason *after* wasting the work.

⇒ ⭐ **Refuse BEFORE the modal opens.** ⛔ **Do not make the "+" vanish** — that is the thing the ruling
forbids. ⭐ **Keep the indicator; move it earlier.**

---

## 5. ⭐ Gates — **the rule 8 contract, all seven rows**

⭐ **Identical to Batch 81 §5.** ⛔ **`NodeEditor.Core` / `NodeEditor.UI` / `Fhsm.Tests` take NO
`--no-build`.** ⭐ **Baseline moves with whatever Batch 81 lands — state the base sha you measured
against.**

⚠ **Golden movement is plausible here** — ⛔ **item 1 touches declaration mutation**, so a `.blueprint.json`
round-trip could shift. ⭐ **Report it as a diff shape either way.**
