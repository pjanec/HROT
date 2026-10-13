<!--STATUS
state: LIVE
updated: 2026-10-13
doc-type: HANDOFF — behaviors lane → the ui lane: P5b, the modeless order form (a FRAME — the ui lane designs the details). Dispatched at 737cd9e14 (branch `behaviors`).
current-answer: §1 goal · §2 the seam you implement · §3 what to build · §4 fences · §5 acceptance and the report
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design: §13.1 F6 (the order form, the fast path), §13.11 (P5 as built: OrderDraft, IOrderForm, the completeness rule, GET /orders), §13.3 (designations, P6 — will prefill the same draft)
  - docs/blueprints/DESIGN_Map_Picking_Unification.md — the map pick service and its presets
  - docs/DESIGN_Entity_Control.md — the unit menu, the RTS tool, EntityOrders as the one order path (§3.1, R-270)
-->

# Handoff — the order form (ui lane, P5b)

RELEARN

⭐ This is a FRAME: goal, the seam, fences and acceptance. ⭐ **Your step 1 is the design**: extend
[DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md) §13.11 with the form's own `classDiagram` +
`sequenceDiagram` (after an `INVENTORY` of what exists), then build, then fold the as-built back in.

🔒 **User, 2026-10-13:** *"Not all params are just picking, some are numbers (various distances etc.) and ui should allow editing them -
some kind of parameter filling modeless floating form allowing for picking from 2d/3d map (as attempted in the mission plan editor panel)
and/or list picker with incremental search, but avoidable for simpler orders where RTS style is desired (pre-filling params from
previously clicked entity/place etc.)"* · *"P5b to ui lane if definable and implementable separately"*.

## 1. Goal

An order the gesture cannot complete (two things to pick, or a number to type) opens a **modeless floating form** prefilled from the
gesture; the operator picks on the 2D / 3D map or from a searchable entity list, types values, and presses Send. Simple orders (one
pick, or nothing) keep the RTS fast path and never see the form.

## 2. The seam you implement *(built by behaviors, P5 / `CE-2148`, `Hrot.Common/Orders/OrderMenu.cs`)*

| type | what it gives you |
|---|---|
| `IOrderForm.Open(OrderDraft draft)` | ⭐ implement it; register ONE instance per station world: `world.SetSingletonManaged<IOrderForm>(form)` |
| `OrderDraft` | `Units` (network ids) · `Order` (`OrderInfo`: name, DTO type, `Fields`) · `Params` (a `JsonObject` already filled from the gesture — explicit values win) · `Missing` (the `[Required]` fields still unset) · `Send(json)` — call it on Send; it orders every unit through `EntityOrders` and returns one `OrderResult` per unit (show refusals) |
| `OrderField` | `Name` (JSON) · `Type` · `Kind` (`MapPoint` / `MapEntity` / `Value`) · `Required` · `Presets` (`[MapPickableEntity]` filters for an entity pick) |
| `OrderMenu.MissingIn(order, json)` | the ONE completeness rule — enable Send only when it returns nothing (`EntityOrders` refuses otherwise anyway) |
| `GET /orders` | the same fields and schema over HTTP, if useful for a check |

Who calls `Open` today: `MenuOrders.OpenForm` (the unit menu) and `RtsCommands` (an RTS gesture whose order it cannot complete). Without
a registered form they log *"needs the order form"* and send nothing.

## 3. What to build *(leans — your design may argue otherwise, say so in the report)*

| # | item | lean |
|---|---|---|
| U1 | the form | reuse `BehaviorParamsForm` (the mission panel and the AI details view host it) over `draft.Order.Dto`, seeded from `draft.Params`; modeless, stays open while the operator picks |
| U2 | a `Point` field | a "pick on map" button → `IMapPickService.PickLocationAsync` (2D exists; 3D: verify, build if absent) |
| U3 | an `Entity` field | a map pick (`PickEntityAsync(field.Presets)`) AND a searchable list — the node editor's fuzzy `PickerWindow` (favourites / recents, `NodeEditor.UI/Picker/PickerWindow.cs`) with a NEW entity source narrowed by the presets |
| U4 | the menu | ⭐ flip `ContextMenuProjectorGizmo`: today it hides an order whose `Pick` is `Several`; show it as `Name…` when the world has an `IOrderForm` |
| U5 | an "Order…" entry | optional: open the form for ANY order (prefilled), for an operator who wants to set optional fields |

## 4. Fences

- ⛔ Do not change `OrderDraft`, `IOrderForm`, `OrderMenu.MissingIn` or `EntityOrders` — they are behaviors' (P5). Need a change? Say so
  in the report or ask; a cross-lane edit is allowed but must be named.
- ⛔ No second order path: the form sends ONLY through `draft.Send`.
- ⭐ Designations (P6, behaviors, §13.3) will later prefill the same draft — nothing for you to do now.

## 5. Acceptance and the report

1. A rail in the menu suite (`MenuOrdersTests`) or the form's own suite: an order with two required picks opens the form prefilled
   with the gesture's pick; Send issues it once per unit; a form left incomplete sends nothing.
2. The menu shows a `Several` order as `Name…` only where a form is registered (a headless node shows nothing new).
3. The report: the design section you added, the gate table (per CLAUDE.md, rule 8 contract), ids allocated from the ui block.
