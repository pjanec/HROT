<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: HANDOFF — behaviors lane → a third session (Track C of the orders programme). Dispatched at 7dca4d807 (branch `behaviors`).
current-answer: §1 the scope · §2 the tasks in order · §3 traps · §4 gates
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Orders_Params_Entity_Classes.md — THE design (rules K1/K2, classes §2, modules §4, task list §6)
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — why (R-278..R-281); read §2.3k ⑤, §2.3l, §2.3m A, §2.3n–§2.3p
  - docs/DESIGN_Map_3D_Mode.md — VisualFamilies (§3.5), which C2 grows
  - docs/DESIGN_Add_Entity_Picker.md — D2b superseded; C2 rebuilds its folder path
-->

# Handoff — orders programme, Track C: entity classes and DIS

⭐ This file points at the design; it is not one. Extend
[DESIGN_Orders_Params_Entity_Classes.md](../DESIGN_Orders_Params_Entity_Classes.md).
⛔ Your scope is FROZEN at the dispatch sha; a later document that invalidates an item ⇒ STOP that item and report it.

## 1. Scope

`DisTypeMask`, the hard-coded entity class table and country table (🔒 R-279/R-280: hard-coded C#, NO data files), its use by
the 3-D families, the 2-D symbol and the Add-Entity picker, and the units' DIS layout. NOT the behaviour lookup (Track D's
CE-2142 consumes your table).

## 2. Tasks, in order *(design §6)*

| id | task | note |
|---|---|---|
| **CE-2137** C1 | `DisTypeMask` in `Fdp.Core`: per segment `*`, `n`, `a-b`, `a,b,c` (lists of ranges); `Matches`; `Specificity` (segment by segment from Kind: exact > list/range > `*`) | prior art: `QueryBuilder.WithDisType` (value + byte mask, used by nothing in production) stays; the Terrain Reference Library's unbuilt "most specific first" DIS lookup (DESIGN_Terrain_Combat_Tuning.md:90) can reuse it |
| **CE-2138** C2 | `EntityClassTable` (named masks, general → specific) grown from `VisualFamilies.Of` (`Map3D/VisualFamily.cs:30-56`) + `CountryTable`; `VisualFamilies.Of`, `DisNameTable.Path` / `CountryName` rebuilt on it; `Hrot.Core/Tkb/DisNames.json` + its `EmbeddedResource` deleted; fix CE-2127 (`DefaultEntityShapeLibrary.cs:30` reads Category from `>> 40`; it is bits 24–31) | the composite special cases (`VisualFamily.cs:35`, `EntityTypeCatalog.cs:88-89`) stay until C3 |
| **CE-2139** C3 | units → Kind 11 (Domain and Country kept; Category = arm; Subcategory = echelon, from `TkbCompositionDef.Echelon`; Specific = variant) in `NedTkbCatalog.cs:236,257,277`; the composite special cases become DIS matches; scenario load prefers the template's DIS type for a composite (`DisEntityTypeTranslator.cs:70-81`, `StagingEntityExtractor.cs:319,376`); `NedTkbBuilderVisualTests.cs:163-176` updated | units leave the "vehicles" map layer, stay in "units_ground" (`MapLayerRegistry.cs:77-89`); 2-D avatar → none (`DefaultEntityShapeLibrary` `_fallback`) — check whether unit commanders pass `EntityPresentationGizmo`'s filter at all (not measured) |

## 3. Traps

- A golden of every built-in type's 3-D family and picker folder BEFORE C2, compared after: nothing may move except the
  intended (units in C3).
- Build the TEST project; `--no-build` after. Report golden movement as a diff shape.
- Name the cross-lane edits (Presentation / GizmoMap are not behaviors-lane files) in each commit.

## 4. Gates

`python3 scripts/tracker-counts.py --check` · `python3 scripts/design-digest.py --check` · `python3 scripts/rulings-check.py` ·
`Hrot.Core.Tests`, `Hrot.Presentation.Tests` (the 3-D / picker / map-layer suites), `Fdp.Core.Tests` (DIS) · gate-report contract.
