# Unadopted-seam inventory — the prior-art table

> **Built 2026-08-10 in response to a process complaint from the user:** *"these surprising discoveries
> that something already exists happen way too often, meaning the features get designed before a proper
> scan of existing sources. How can we avoid these time-consuming iterations?"*
>
> **Reproduce:** `python3 scripts/seam_inventory.py` — ~1.5 s over 4,626 files.

## Why the earlier scans missed these

Five scans ran before this, and all of them searched the **call space** — `RegisterContextMenuHandler`,
`AddItem`, construction sites. A call-site scan surfaces a type **in proportion to how widely it is
adopted**. An unadopted seam is therefore *invisible by construction* — and unadopted seams are exactly
what this programme is looking for.

> ### 🔒 The standing prior, and the rule that follows
>
> [The seam law](UX_Current_UI_Architecture.md) says every UI surface with a contribution seam is shared
> and every one without is forked. Its operational half was never used:
>
> **For any *"we need a shared X"*, assume a shared X already exists and is under-adopted. The design's
> first job is to find it and explain why adoption stopped — not to propose one.**
>
> ⇒ Every `UX_Feature_*.md` carries a **"Prior art"** section, filled *before* the design, citing a row
> of this table — **including when the answer is "nothing"**. A section that must be written is a check
> that cannot be silently skipped. Same enforcement trick as the [Corrections table](UX_Tasks_Detail.md#corrections).

## Method, and the two ways it lies

Every `public` type declared in the six layers that subsystems *compose from*, counted by how many
production files elsewhere name it. `Hrot.UI.Common` (dead) and the NodeEdit vendor tree are excluded.

| Column | Meaning |
|---|---|
| **prod** | production files naming the type, anywhere outside its own file |
| **ext** | ⭐ of those, files **outside the shared layers** — i.e. actual adoption *by a subsystem* |
| **test** | test files naming it. *"Many tests, one caller"* is the `SharedContextMenuPopulator` fingerprint |

> ### ⚠ It matches identifiers textually. Both error directions are real.
>
> | | |
> |---|---|
> | **Under-counts** adoption reached through a wrapper | `IEntityContextMenuHandler` shows **ext 1**, yet **five** subsystems register handlers — through `LambdaEntityContextMenuHandler`, so the interface name never appears. 🔒 **Before calling an `IFoo` unadopted, check `LambdaFoo` / `DefaultFoo` / `SharedFoo` too** |
> | **Over-counts** types whose name is a common word | any `*Builder` / `*Adapter` |
>
> ⇒ **A low number is a *candidate*, not a finding.** Every row is verified by hand before it is cited.

## Distribution — 1,167 public types in the shared layers

| prod consumers | 0 | 1 | 2 | 3 | 4 | 5 | 6+ |
|---|--:|--:|--:|--:|--:|--:|--:|
| types | 126 | 167 | 153 | 124 | 101 | 76 | 420 |

Narrowed to **seam-shaped names** (`I*`, `*Registry`, `*Provider`, `*Descriptor`, `*Controller`,
`*Builder`, `*Factory`, `*Handler`, `*Library`, `Shared*`, `Default*`, `Lambda*`, `*Populator`,
`*Adapter`) in the **three UI layers**: **34 rows at ≤1 external adopter.**

## ✅ Verified — read and confirmed

| Type | prod / ext / test | What it is | Bearing |
|---|:--:|---|---|
| **`MapContextActionController`** `Hrot.Presentation/Menus/` | **0 / 0 / 0** | *"Minimal `IEntityActionController` for **inline map context menus**"* — centre / delete / rotate via caller callbacks; the other four deliberately no-op | ⭐ **the map half of [UXI-04](UX_Issues.md#uxi-04), already written, never wired** |
| **`IHierarchyAdapter`** `Fdp.Presentation/Vis2D/` | **0 / 0 / 0** | *"hierarchy traversal… generic hierarchy rendering without coupling to specific component types"*, zero-alloc child enumerator | ⭐ **the ORBAT half of UXI-04** |
| `SharedContextMenuPopulator` `Hrot.Presentation/Menus/` | 1 / 1 / 1 | the shared entity-menu declaration | [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) — **found the hard way; this table reproduces it** |
| `IEntityActionController` `Hrot.Presentation/Facades/` | 3 / 1 / 1 | its host-binding port | UXI-03 |
| `IEntityShapeLibrary` `GizmoMap` | 4 / **0** / 1 | map symbology seam; every host passes the default | [UXI-10](UX_Issues.md#uxi-10) — independently confirmed |
| `IEntityContextMenuHandler` `Fdp.Presentation` | 3 / 1 / 1 | 🔴 **false positive** — adopted by 5 subsystems via `LambdaEntityContextMenuHandler` | the under-count caveat, in the flesh |

## ⚠ Candidates — surfaced, **not yet verified**

Cited only after reading. Grouped by the issue they would touch.

| Bearing | Rows |
|---|---|
| **Menus / actions** ([UXI-04](UX_Issues.md#uxi-04), [UXI-05](UX_Issues.md#uxi-05)) | `GlobalMenuRegistry` (3/1/3, **Editor only**) · `MainMenuAdapter` (2/0/0) · `ToolbarCommandAdapter` (2/1/1) · `IDerContextMenuHandler` + `LambdaDerContextMenuHandler` (ExCon's parallel duplicate of the entity path) · `ContextMenuBuilder` (2/0/0) |
| **Map & symbology** ([UXI-10](UX_Issues.md#uxi-10), [UXI-09](UX_Issues.md#uxi-09)) | `ISemanticShapeProfileRegistry` (0/0/0) · `IIconAtlas` + `IconAtlasAdapter` (0/0/0) · `IGizmoUndoRecord` (3/0/1) · `IGizmoInteractionHandler` (11/1/3) |
| **Inspector / rendering** ([UXI-15](UX_Issues.md#uxi-15)) | `IEntityAwareImGuiRenderer` (9/0/1) · `ImGuiRendererRegistry` (5/0/1) · `IImGuiRenderer` (17/1/1) · `IInspectableSession` (23/1/7) · `ImGuiPropertyTreeAdapter` (6/1/1) |
| **Picking / input** ([UXI-07](UX_Issues.md#uxi-07)) | `IComponentPickerContext` (4/0/2) · `ISpatialPickerContext` (4/1/1) · `IPickInteractionContext` (2/0/2) · `IInputProvider` (4/0/5) |
| **Other** | `SimulationViewAdapter` (0/0/0) · `IResourceProvider` (2/0/0) · `IRouteWaypointEditorState` (2/1/1) · `BehaviorUiRegistry` (4/1/3) · `EditorFontRegistry` (2/1/1) |
| ⚠ **Odd — declared `class` with an `I` prefix** | `INetworkTopologyRenderer`, `ISerializationRegistryRenderer`, `ITkbDatabaseRenderer` — all in `SingletonRenderers.cs`, all 0/0/0. Naming defect or parse artefact; check before citing |

## ✅ Back-check — the three finished designs, run through this table

User, 2026-08-10: *"please run the previous designs through this. Let's be sure."* Done with
`scripts/type_index.py` (3,804 public types, repo-wide, **including** `Hrot.UI.Common`).

| Design | Result |
|---|---|
| **[UXI-01](UX_Feature_DeadUI_Removal.md)** delete dead UI | ✅ **clears.** All **20** public types in `Hrot.UI.Common` have a twin — 19 in `Hrot.Presentation`, `MissionCommitResult` in **`Hrot.Core`**. **0 would break on delete** |
| **[UXI-02](UX_Feature_HalfBuilt_Decisions.md)** four half-built items | ✅ **all four hold**, ⚠ **plus one new task.** `SelectionRenderSystem` read as 2 consumers vs the design's *"nothing instantiates it"* — verified as three `<see cref>` doc links in `Hrot.Core/…/SelectionState.cs:11,27,48`, which name a namespace the type **already left**. Dangling today; the delete must fix them |
| **[UXI-03](UX_Feature_Entity_Action_Vocabulary.md)** action vocabulary | ✅ **stands.** No `EntityActionDescriptor`/`Registry`/`Context` exists. ⚠ One refinement: the map already has a **shared rendering path** (`ContextMenuAdapter` 9p across CGF/IG/SimHost), so UXI-04's map half is *emit into the existing binding*, not *build a path* |
| **[UXI-07](UX_Issues.md#uxi-07)** tools *(not yet designed)* | ⭐ **prior art genuinely empty** — zero `ITool` / `ToolDescriptor` / `ToolRegistry` / `ToolState` / `ActiveTool` types repo-wide. Confirms *"a tool is not a thing in this codebase"*. **An empty result is a valid, recordable answer** |

⇒ **One correction and one new task across three designs** — the check pays for itself without
invalidating anything already agreed.

## What this does not fix

Only **one** of the recent misses was this failure mode. Logging them apart, so the checklist stays
aimed:

| Miss | Cause | Fix |
|---|---|---|
| `SharedContextMenuPopulator`, UXI-05's existing filter, UXI-22's registrar | searched the call space for an unadopted type | ✅ **this table** |
| [Correction 3](UX_Tasks_Detail.md#corrections) — the F3 lean | a lean argued from plausibility, never measured | measure before leaning |
| [Correction 5](UX_Tasks_Detail.md#corrections) — *"no right-click affordances"* | an **absence** claim generalised from one file | absence claims need a repo-wide search, not an example |
| [Correction 11](UX_Tasks_Detail.md#corrections) — PACK2-E002 | a stale code comment read as a live plan | check whether the work landed |
| [Correction 14](UX_Tasks_Detail.md#corrections) — the wrong twin | duplicate type resolved by **namespace** | resolve by **project reference** |

## ✅ The graph MCP is live — and it is **complementary**, not a replacement

Connected 2026-08-10: **163,526 nodes / 519,797 edges** on this branch. Tested against the findings above.

### Where it beats this script

Real semantic edges (`IMPLEMENTS` 2,065 · `CALLS` 110,955 · `USAGE` 127,153 · `INHERITS` · `OVERRIDE`),
so it does hops a textual count cannot:

| Question | Result |
|---|---|
| *Who implements `IEntityContextMenuHandler`?* | ✅ `LambdaEntityContextMenuHandler` + `JsonEntityContextMenuHandler` — **the wrapper hop this script gets wrong** |
| *Which `SharedContextMenuPopulator`?* | ✅ separates the twins **by `file_path`** — would have prevented [Correction 14](UX_Tasks_Detail.md#corrections) |
| *Unadopted interfaces?* | ✅ independently surfaced `IHierarchyAdapter` at 1 implementer |

### 🔴 TRAP — `is_test` is unreliable in this index

**`is_test` is set on `Module` nodes only.** Every `Class`/`Method` in a `*.Tests` project reports
`is_test = "false"` — verified: `StandaloneIosTests` in `Hrot.ExCon.Tests` returns `"false"`.

> **Consequence, measured:** ranking seams by raw inbound edges gives `SharedContextMenuPopulator`
> **12 inbound** — which reads as *well adopted*. Truth: **1 production caller + 11 tests.** Trusting it
> would have erased the entire [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) finding.
>
> 🔒 **Filter by `file_path`, never by `is_test`.**

### Division of labour from here

| Tool | Use for |
|---|---|
| **Graph** | *what connects to what* — implementers, callers, transitive reach. **Structure.** |
| **`seam_inventory.py` / `type_index.py`** | *how widely adopted* — a census with **production and test counted separately**, the distinction the graph gets wrong |

⚠ Also: the graph indexes `docs/**.md`, so name matches include documentation. And per its own tool
docs, **coverage is best-effort and never proof of completeness** — negative claims ("nothing uses
this") still need a second source. Cross-check before publishing.

## Next session gets a better tool than this

`scripts/cloud-bootstrap.sh` has been run: .NET and `codebase-memory-mcp 0.10.2` are installed and the
repo is indexed. MCP servers spawn at session start, so the graph tools are live **from the next session
on** — and *"nodes with low in-degree"* is a first-class query there, which is precisely what grep is
worst at. ⚠ Per `CLAUDE.md` these tools were mandated **first**, before reading files, and this programme
did not use them. That is the cheapest of all the fixes listed here.
