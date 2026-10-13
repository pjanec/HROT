<!--STATUS
state: LIVE
updated: 2026-10-13
doc-type: HANDOFF — backend lane → the ui lane: the editor panels of the params programme (a FRAME — the ui lane designs the details). Dispatched at DISPATCH_SHA (branch `backend`; re-stamped from 388b4c7d2 — unstarted, U5 ruled by the user, R-3001).
current-answer: §1 goal · §2 what exists · §3 what to build · §4 fences · §5 acceptance and the report
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design: §1 P1–P5 (one params DTO per behaviour), §13.10 ⑦ (no variable kinds; the "Not yet" list this handoff dispatches), F2 (the one registry the editor picks from)
  - docs/blueprints/Variables_Designer_Quickstart.md — what a designer is told the panels show (one list: Params first, then variables)
  - docs/blueprints/Blueprint_Issues_Tracker.md — CE-3194, the backend half of U5b (DefaultOf, override bake, the blueprint field)
  - docs/blueprints/HANDOFF_Order_Form_Ui.md — the sibling ui handoff (P5b, the order form, CE-1059); it already reuses BehaviorParamsForm
-->

# Handoff — the params panels (ui lane)

RELEARN

⭐ This is a FRAME: goal, what exists, fences, acceptance. ⭐ **Your step 1 is the design**: extend
[DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md) §13.10 ⑦ with the panels' `classDiagram` +
`sequenceDiagram` (after an `INVENTORY`), then build, then fold the as-built back in.

⛔ **Your scope is FROZEN at `DISPATCH_SHA`.** Documents that change after it are FYI only; one that invalidates an item ⇒ stop that
item and report it.

**Merge first:** `backend` at `DISPATCH_SHA` or later (it carries `behaviors`; `ui@b68d82693` is an ancestor, so the merge is clean).

🔒 **User, 2026-10-13:** *"the programme was largely about forcing single-dto params and removing the role from the variable table …
why would we show use those variable kinds? what would he do with that?"* ⇒ the panels show **`Params` (the DTO's fields) and the
variables, nothing about kinds.**

## 1. Goal

Every behaviour names ONE params DTO (corpus, measured 2026-10-13: 18 / 18 `.bp.json` name a `ParamsDtoTypeId`, **0** `Parameter`
declarations left) and a variable has no authored kind (CE-3193). The engine is done; **the panels have not caught up**: an author
cannot see the fields of the DTO their behaviour takes.

## 2. What exists *(measured at `388b4c7d2`)*

🔒 **User, 2026-10-13 (R-3001):** *"editor should allow choosing the csharp hardcoded dto OR json asset dto and editing the default values of the fields per asset (using structEdit - already existing) and save those defaults in the asset."* — design [§13.10a](../DESIGN_Orders_Params_Entity_Classes.md).


| surface | today | file |
|---|---|---|
| blueprint "Inputs" section | lists DECLARED Parameters only ⇒ **empty for every blueprint** | `BlueprintMyBlueprintModel.cs:128` (section), `:335` (`BuildDeclarationItems(DeclarationKind.Parameter, …)`) |
| blueprint canvas pins | ✅ correct — from the named DTO | `ParamsDtoFields.ParametersOf(asset, source)` (`Hrot.Blueprints.Compiler/Compiler/ParamsDtoFields.cs:161`), used by `NodePinSchema.cs:961` |
| "Create parameter" | still authors a `ParameterDecl` — a kind the compiler only honours when the asset names NO DTO (`Stage2_Validate.cs:2840`), which no asset does | `BlueprintMyBlueprintModel.CommandCreateParameter = "editor.create-parameter"` (`:98`) |
| BTree / HSM `Params` row | ONE read-only entry; the DTO's fields are not listed (the panel never expands a struct) | `BlackboardAuthoringWindow.cs:323` (`IsReadOnly: v.IsReservedParams`), `VariablesPanelControl.cs:408` (tooltip) |
| BTree / HSM default values of `Params` | `ParamsDefaultJson` on the asset — **no editor surface writes it** (hand-edited JSON; e.g. DangerCrossing's six tuning defaults) | `BehaviorTreeAsset.cs:245`, `HsmAsset.cs:47` |
| naming the asset's DTO | `ParamsDtoTypeId` — **no editor surface writes it** on any of the three asset kinds | `BehaviorTreeAsset.cs:241`, `HsmAsset.cs:43`, `BlueprintAsset.cs:36` |
| a form over a DTO | ✅ `BehaviorParamsForm` (`Hrot.Presentation/Behavior`) — the AI details view and the mission panel host it; the order form (CE-1059) too | `EntityAiDetailsView.cs:320-344` |
| the DTO catalogue | ✅ `ParamsDtoRegistry` (`Hrot.Core/MapDefinitions/Behavior/ParamsDtoRegistry.cs:65`); `BlackboardTypeChoiceBuilder` lists `[ParamsDto]` types (F2) | |
| CE-3193 surfaces (backend built, **nobody has looked**) | one "Variables" outline section; no Scope column (main table 5 columns, Node-Owned 3); the working-state picker lists any variable of the node's working-state type | `BlackboardMyBlueprintModel`, `VariablesPanelControl`, `ActionBindingSources.GetWorkingStateVariables` |

## 3. What to build *(leans — your design may argue otherwise; say so in the report)*

| # | item | lean |
|---|---|---|
| U1 | blueprint "Inputs" shows the DTO's fields | read-only rows from **`ParamsDtoFields.ParametersOf`** — the pins' own source, so panel and pins cannot disagree; the section header names the DTO (simple name) |
| U2 | retire "Create parameter" | delete the command and the section's `[+]` (⛔ an author can no longer make a parameter the compiler ignores). ⚠ "add a field to the DTO" is NOT the replacement — that is B4b (the editor-made DTO), postponed by the user |
| U3 | BTree / HSM `Params` row lists its fields | expand the reserved row into read-only child rows (name, type, default) — the same field source as U1 where it reaches (a `Type` from `ParamsDtoRegistry`); `Params.<field>` binding already works, only the listing is missing |
| U4 | visual check of the CE-3193 surfaces | one pass in the real editor (a Windows session or the headless smoke harness): the three rows of §2's last line, plus U1–U3 |
| U5a | ⭐ **ruled (R-3001)** — choose the asset's params DTO | a picker over `ParamsDtoRegistry` on BTree, HSM and blueprint assets: hand-written C# DTOs AND generated `.params.json` DTOs, the origin shown; writes `ParamsDtoTypeId`. Needs nothing from backend |
| U5b | ⭐ **ruled (R-3001)** — edit the defaults per asset | StructEdit over the DTO (`BehaviorParamsForm`) showing the EFFECTIVE value = `ParamsDefaults.DefaultOf(T)` overlaid with the asset's `ParamsDefaultJson`; an overridden field is marked and resettable; the asset saves **only the overridden keys** in `ParamsDefaultJson` (BTree / HSM today, blueprint after CE-3194 ③). ⚠ **Depends on backend `CE-3194`** (the `DefaultOf` function and the blueprint field) — build U5a first |

## 4. Fences

- ⛔ Do not change `WorkingStateVariables`, the generators, `ParamsDtoFields`' semantics or `Stage2_Validate` — backend's (CE-3193, P4).
  Need a change? Say so in the report; a cross-lane edit is allowed but must be named.
- ⛔ No second field source: U1 and U3 read what the pins / bindings read.
- ⛔ No variable kind reappears in any panel (the user's ruling above).

## 5. Acceptance and the report

1. U1: a rail in the blueprint editor's host suite (e.g. beside `ListVariableDeclareUxTests`): a DTO-named blueprint's "Inputs"
   lists exactly `ParametersOf`'s fields; no `editor.create-parameter` command is registered.
2. U3: a rail in `Hrot.Editor.AiShared.Tests/Blackboard` (beside `NodeOwnedVariableTests`): the `Params` row of a DTO-named BTree
   lists the DTO's fields, read-only.
3. U4: what you saw, per surface, with a screenshot path or "not looked at, because …".
4. U5: a rail — picking a DTO writes `ParamsDtoTypeId`; editing one field writes ONLY that key to `ParamsDefaultJson`; reset removes it.
5. The report: the design section you added, the gate table (CLAUDE.md rule 8 contract), ids allocated from the ui block.
