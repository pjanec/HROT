<!--STATUS
state: LIVE — ✅ APPROVED 2026-11-02 (user: "then approved", with MA-1's always-visible button); P1 building
updated: 2026-11-03 (§9 P4 as built)
build-state: BUILT — (all phases) P1 (MA-4, MA-3) built §6 · P2 (MA-1, MA-2, MA-5) built §7 · P3 (MA-6, MA-7, MA-8) built §8 · P4 (MA-9) built §9 · W-M6 blob references closed §9
current-answer: §9 the P4 AS BUILT · §8 the P3 AS BUILT · §7 the P2 AS BUILT · §6 the P1 AS BUILT · §3 (decisions) · §4 (phases)
stale-below: nothing
known-rot: none
related-designs:
  - docs/DESIGN_Scenario_Macros.md — OWNS the macro model: §5.1 the three binding homes (SR-159…SR-166), §5.1.3 the binder as built, SR-39a the instance in Details, §14 SR-173 the macros/ layout; this doc owns only the EDITOR surfaces over it
  - docs/blueprints/DESIGN_Map_Picking_Unification.md — §6 CE-4206: the reference leaves (EntityLink/PointLink/AreaLink, Exact · Key); MA-2 adds the Param form to the same leaf drawer
  - docs/DESIGN_Entity_Hierarchy_Views.md — the Scenario view (macro entities under their instance); the instance is selected there and edited here
  - docs/DESIGN_Add_Entity_Picker.md — §2g/§2h the gallery groups; MA-8 adds a Macros group
  - docs/DESIGN_Node_Features.md — not affected
-->

# DESIGN — **authoring macros in the editor** *(CE-4210 + the editor half of SR-39a, SR-104 Z1, §14 "Save selection as macro")*

[CE-4210](blueprints/Blueprint_Issues_Tracker.md) (feed a field from a parameter) · dispatched by
[HANDOFF_Coordinator_To_Ui_Scenario_Authoring](blueprints/batches/HANDOFF_Coordinator_To_Ui_Scenario_Authoring.md) ⑤ and ⑧.
The model is [`DESIGN_Scenario_Macros.md`](DESIGN_Scenario_Macros.md); nothing here changes it.

## 1. INVENTORY *(`2026-11-02`)*

**Queries:**
- graph (⚠ the index pre-dated today's merges; re-indexed in the background, so the gaps were filled by grep):
  - `.*Param(s)?(Form|Table|Editor|Panel|Window|Drawer)$` Class → 2 (`BehaviorParamsForm`, `BehaviorParamsEditor`);
  - `.*Predicate.*(Builder|Editor|Panel|Window|Composer)$` → 1 (`PredicateValueFieldEditor`);
  - `.*(Header|Scenario)(Params|Properties|Settings)(Panel|Window|Form|Editor)$` → **0**;
  - after the re-index: `.*(Header|Scenario)(Params|Parameters|Properties|Settings|Header)(Panel|Window|Form|Editor|Resident)?$` → 1 (`ScenarioHeader`, the record — no editor of it) · `.*Macro.*` Class → 19, all the BLUEPRINT graph macros (`MacroCallNode`, `Stage2_5_ExpandMacros`, …), a different concept with no scenario UI.
- grep: `new ComponentEditDrawer` (8 production sites) · `ScenarioHeader` in the editor (2) · `EnumerateRelPaths` (2) · `TypeId` mapping (none) · the save handler · `ScenarioMergeCore` · `EntityWriteRouter`.
- ⚠ Coverage was not checked: `check_index_coverage` is not in the CLI.

| measured | where | so |
|---|---|---|
| ⭐ **ONE field drawer** draws every editable value in the editor: the inspector (`ComponentReflector`), the ONE params form (`BehaviorParamsForm.DrawFields`), the params defaults, the Replay Search predicate editor, the node properties, the variable modal | `FDP/Engine/Fdp.Presentation/ImGui/Editing/ComponentEditDrawer.cs` (`DrawLeafNode` :253, `DrawContainerNode` :134); 8 `new ComponentEditDrawer` sites | ⭐ the bind affordance is ONE change here, and every editor gets it by construction |
| the reference leaves (CE-4206) are drawn by ONE class through the same drawer; each leaf knows its `JsonPath` | `ReferenceLeafDrawers.cs` :166, :352 | the `Param` form is one more arm of an existing form switch |
| the drawer's optional host services come in through ONE context (`IMapPickContext`), and a host that has none passes null | `ComponentEditDrawer` ctor | a bind context comes in the same way (⚠ the silent-default rule: every editor host that HAS a scope must pass it) |
| the binder, its three homes and `ParamBindings` (469) are built; `BindableNode.Bind` is settable; `SearchPredicateDto : BindableNode` | `Fdp.Toolkits/Scenario/ParamBinding.cs`; DESIGN_Scenario_Macros §5.1.3 | the editor only WRITES the homes; it never applies them (SR-168: the binder is non-destructive) |
| a param is `ScenarioParamDecl(Name, TypeId, Default, Comment)`; `TypeId` is a free string (`"AreaLink"`, `"float"`, `"ForceId"`); ⛔ **no table maps a TypeId to a CLR type** — the binder converts to the TARGET field's type | `ScenarioHeader.cs:73`; grep | the bind menu cannot filter params by type, and an args form cannot draw a typed row ⇒ MA-3 |
| 🔴 **an editor SAVE drops the macro header**: the save handler builds `new ScenarioHeader(docType, TerrainName, Description)` — no `Kind`, `Params`, `Anchor`, `MaxShift`, `Relocatable` — and the slice merge's canonical header keeps only `TerrainName`, `World`, `Description` | `HrotScenarioSaveHandler.cs:98-100`; `ScenarioMergeCore.cs:98-188` | ⛔ opening a macro and saving it turns it into a plain scenario with no params; a scenario's own params (SR-41) are lost the same way ⇒ MA-4. Note: the serializer CAN write the macro header (`ScenarioSerializer.cs:273-279`, `ScenarioHeaderJson.Write`); nothing passes it |
| ⚠ the save handler's slice path is `scenarios/<name>/.slices`, while the merge reads `ResolveScenarioDirectory(name)` | `HrotScenarioSaveHandler.cs:102`; `StorageProcessManager.cs:217` | for `macros/x` the slices land under `scenarios/macros/x/.slices` and are not cleaned up; the canonical file is right ⇒ fixed with MA-4 |
| the load stamps the scenario's text and args resident (`ScenarioDescriptionResident {Text, Name, Hash, Args}`); the save reads `Description` back from it | `ScenarioDescription.cs:16-29`; save handler :100 | ⭐ the ONE owner of "the header of the scenario being edited" already exists — it just holds too little |
| the resolver opens any `macros/<path>` name (`ResolveScenarioDirectory` arm); `ContentLayout.MacroName` / `IsMacroName` / `Macros(root)` exist | `Fdp.Toolkits/Orchestration/ContentLayout.cs:58-164` | Open / New / Save of a macro needs no new path code |
| the scenario listing enumerates `scenarios/` only | `ScenarioEnumeration.EnumerateRelPaths`; `EditorSubsystem.cs:1376`; `CgfSubsystem.cs:2620` | a Macros group in the picker = the same enumeration over `ContentLayout.Macros(root)` |
| the instance is TKB 8813 (`TkbEntityTypes.ScenarioMacro`), drawn today with the cultural-feature outline; the worker gives it its own marker (W-M1, SR-178) | `EntityClassShapeLibrary.cs:28` | Add Entity ▸ Macro creates an 8813 + `MacroInstance` |
| ⚠ an args edit over the debug API (`POST /entities/{id}/component {"Args":…}`) answers ok and changes nothing | DESIGN_Scenario_Macros §15.4 (live) | the args form writes a NEW `MacroInstance` record (immutable, SR-175), never a patch |
| the attribute write router is numeric (`Write(entity, attributeId, double)`); a managed record has no attribute route | `EntityWriteRouter.cs`, `IEntityComponentWriter.cs:40-55` | ⛔ unmeasured: how the editor's inspector commits a MANAGED component on an entity another node owns. MA-9 measures it first (§5) |

## 2. The shape

```mermaid
classDiagram
    direction LR
    class ComponentEditDrawer { <<existing, the ONE field drawer>> DrawLeafNode · DrawContainerNode }
    class IParamBindContext { <<NEW, optional, beside IMapPickContext>> ParamsInScope · BoundTo(path) · Bind(path, name) · Unbind(path) · Problem(path) }
    class ReferenceLeafDrawers { <<existing CE-4206>> Exact · Key · gains Param }
    class BindableNode { <<existing S3a>> Bind : name → param }
    class ParamBindings { <<existing S3a, id 469>> path → param }
    class ParamTypes { <<NEW, Fdp.Toolkits/Scenario>> TypeId ↔ CLR type · Fits(TypeId, Type) }
    class ScenarioDescriptionResident { <<existing, grows>> Text · Name · Args · + Header (kind, params, anchor, placement) }
    class HeaderParamsRowSource { <<NEW, a row source of the EXISTING Variables table>> the header params: add · rename · type · default }
    class RenameParamCommand { <<NEW, undoable>> rewrites every binding of a name }
    class MacroArgsForm { <<NEW>> the instance's args: one typed row per header param }
    class EntityBindContext { <<NEW>> the inspector's context: one entity's ParamBindings }
    class HrotScenarioSaveHandler { <<existing>> writes the resident header }
    class ScenarioMergeCore { <<existing, backend's file>> keeps the header from the first slice that has it }
    ComponentEditDrawer ..> IParamBindContext : optional
    ComponentEditDrawer ..> ReferenceLeafDrawers : Param form for a reference leaf
    ComponentEditDrawer ..> BindableNode : Bind for a node property
    IParamBindContext <|.. EntityBindContext
    EntityBindContext ..> ParamBindings : any other field
    IParamBindContext ..> ParamTypes : filters by type
    IParamBindContext ..> ScenarioDescriptionResident : params in scope
    HeaderParamsRowSource ..> ScenarioDescriptionResident : edits Header.Params
    HeaderParamsRowSource ..> RenameParamCommand
    MacroArgsForm ..> ParamTypes : a typed row per param
    MacroArgsForm ..> ComponentEditDrawer : draws each row
    HrotScenarioSaveHandler ..> ScenarioDescriptionResident : reads Header
    ScenarioMergeCore ..> HrotScenarioSaveHandler : slices
```

*What the picture shows that prose hid:* the drawer picks the binding's HOME from the node it is drawing (a reference leaf, a property
of a `BindableNode`, or neither). So the host passes only the params in scope and a place to write `ParamBindings`; no host can put a
binding in the wrong home. And the header has ONE owner, the resident the load already fills, which the save writes back.

```mermaid
sequenceDiagram
    participant U as author
    participant D as ComponentEditDrawer
    participant C as IParamBindContext (the host's)
    participant H as the home
    U->>D: right-click a field's label
    D->>C: ParamsInScope (filtered by ParamTypes.Fits(param, node.ClrType))
    C-->>D: area : AreaLink, zone : AreaLink
    U->>D: Bind to parameter ▸ area
    alt the leaf is a reference type (EntityLink · PointLink · AreaLink)
        D->>H: the value's RefSpec Form = Param, Key = "area" (the binding.SetBoxed the form switch already does)
    else the leaf's owner is a BindableNode
        D->>H: owner.Bind[propertyName] = "area" (the same node, then MarkStructuralChange)
    else any other field
        D->>C: Bind(node.JsonPath, "area")
        C->>H: ParamBindings on the entity, an undoable component edit
    end
    D-->>U: the chip "⛓ area" (the literal stays editable, the value used when the macro is opened alone)
```

```mermaid
graph TD
    LOAD["ScenarioLoadStep (every node)"] -->|"stamps (exists: Text, Args)"| RES["ScenarioDescriptionResident (+ Header)"]
    PANEL["Variables table, header-params source (editor)"] -->|edits| RES
    BIND["IParamBindContext (inspector · params form · predicate editor)"] -->|"reads params in scope"| RES
    RES -->|"read by"| SAVE["HrotScenarioSaveHandler (every node's slice)"]
    SAVE --> MERGE["ScenarioMergeCore (orchestrator): keeps Header from the first slice carrying it"]
    MERGE --> FILE["macros/PATH/scenario.json or scenarios/NAME/scenario.json"]
    PICK["scenario picker: Scenarios · Macros groups"] -->|"edit load of macros/PATH"| LOAD
    GALLERY["Add Entity gallery: Macros group"] -->|"creation request: TKB 8813 + MacroInstance"| WORLD["the edit world"]
    ARGS["MacroArgsForm (Details of a selected instance)"] -->|"a NEW MacroInstance record, to its owner"| WORLD
    style MERGE stroke:#c00,stroke-width:2px
    style SAVE stroke:#c00,stroke-width:2px
```

*What the picture shows that prose hid:* the two red boxes are where the header is dropped today (§1). Every editor surface reaches
the header through the resident, so no surface can keep a copy of its own.

## 3. Decisions

| # | decision | ⭐ lean | blast radius |
|---|---|---|---|
| **MA-1** *(amended — user, `2026-11-02`: "right-clicking a field name … doesn't sound like something intuitive. How the user gets feedback …? There needs to be either some icon … or the field needs to respond to hovering")* | the ONE affordance — VISIBLE | ⭐ in `ComponentEditDrawer`, every BINDABLE row (a type that fits at least one param in scope) gets a small **⛓ button at the row's right edge**: ① **always drawn, faint**, on every bindable row — so the user sees which fields can be fed without hovering any of them (user, `2026-11-02`: *"If the field will indicate, even without hovering, that there is something to be clicked … then approved"*); hovering the row brightens it and shows the tooltip *"Feed from a parameter"*; ② **always shown, solid**, when the field is bound, as the chip *⛓ name* in front of the value; ③ red, with the validator's reason as its tooltip, when the name is not declared or the type does not fit (SR-149). Clicking it opens *Bind to parameter ▸ (the params whose type fits)* · *New parameter…* · *Unbind*. ⭐ Hovering a bindable row also tints its label, so the row says "something is here" before the button is found. Right-click on the label opens the same menu as a SHORTCUT, never as the only way. The literal stays editable (the value used when the macro is opened alone, SR-168). ⭐ Only when the host passes an `IParamBindContext` with params in scope ⇒ a field outside any macro or scenario-with-params shows nothing new | one class (the drawer) + one optional ctor argument; every drawer host keeps compiling. ⚠ The row's right edge already holds the array *X* (delete) button — the ⛓ sits left of it |
| **MA-2** | where the write goes | ⭐ decided by the DRAWER from the node, never by the host: a reference leaf ⇒ the value's `Param` form (the CE-4206 form switch gains *Param*); a property of a `BindableNode` (the nearest container's value is one) ⇒ that node's `Bind`; anything else ⇒ `context.Bind(path)`, which the inspector's context writes to the entity's `ParamBindings` (`"<Component>.<field path>"`) as an undoable component edit | ⛔ never a key inside params JSON. ⭐ The params form's field paths step into a params blob (`Behavior.Params.Speed`, the binder's own path form) |
| **MA-3** | params by type | ⭐ ONE `ParamTypes` table in `Fdp.Toolkits/Scenario`: `TypeId ↔ CLR type` (`EntityLink`, `PointLink`, `AreaLink`, `Bearing`, `WorldRotation`, `float`, `int`, `bool`, `string`, and enums by their registered name, e.g. `ForceId`), plus `Fits(typeId, Type)`. Used by the bind menu, the args form and the declaration editor's type combo — ⭐ and offered to the validator for SR-162's type half (backend's file; said in the note, not done here). ⚠ Measured: the three declaration lists spell types differently — scenario params short names (`"float"`, `"AreaLink"`), `.params.json` CLR names (`"System.Single"`). ⇒ the table reads BOTH spellings and writes each list's own, so no file changes | a new small table; an unknown TypeId fits nothing and is shown as such |
| **MA-4** | 🔴 the header is dropped on save | ⭐ the resident is the ONE owner: `ScenarioDescriptionResident` gains `Header` (the macro fields: kind, params, anchor, placement, relocatable), stamped at load from the file. The save handler passes it to the serializer (which already writes it). `ScenarioMergeCore` keeps it from the first slice that carries it (the `Description` rule, CE-3288). The save handler's slice path goes through `ContentLayout` (the `macros/` arm). ⭐ Rail: load a macro → save → `Kind`, `Params`, `Anchor` are still in the file | ⚠ cross-lane: `ScenarioMergeCore` is in `Fdp.Toolkits` (backend). Small, symmetric with `Description`, said in the commit and the note |
| **MA-5** *(amended — user, `2026-11-02`: "similar to not yet existing parameter DTO editor or a generic structure editor … this editor should be shared for both purposes" · "there is already a blackboard variables editor. Maybe this is just another instance of a generic editor")* | editing the params — ONE declaration editor, three lists | ⭐ the scenario/macro params are NOT a new panel: they are one more ROW SOURCE of the existing Variables table (`VariableTableControl` + `VariableEditModal` + `VariableRenameCommit`, over `IVariableRowSource`) — the editor the blackboard already uses. ⭐ Three lists of the SAME shape (name · type · default as JSON · comment), measured: blackboard variables (exists) · `ScenarioParamDecl` (`ScenarioHeader.cs:73`) · `ParamsDtoFieldDto` (`*.params.json`, CE-2136 — ⛔ **no editor today**: only the generator and `ParamsDtoStaleness` read it). ⇒ one source per list (`HeaderParamsRowSource` over the resident's `Header.Params`; `ParamsDtoAssetRowSource` over an opened `.params.json`), each with its write-back. ⭐ **Rename** of a scenario param is the shared rename commit plus one step: it rewrites every binding of the name in the edit world (the walker's `Param` leaves, every `BindableNode.Bind`, every `ParamBindings` entry, `ParamBinder.BindingsOf`). Delete is refused while bound, naming the bindings | ✅ **measured `2026-11-02`**: the Variables panel (`VariablesPanelControl`) draws any `IVariablesSchemaSource` (`Hrot.Editor.AiShared/Blackboard/VariablesPanelControl.cs`) — Add · Remove · Rename · Move · default JSON · properties · a reference count — implemented today by `BTreeHsmSchemaSource`, `BlueprintVariablesWindow`'s source and `BlueprintLocalVariableSchemaSource`; `SectionVariableRowSource` already runs with no entity at authoring time (U-6). ⇒ the scenario params are a FOURTH `IVariablesSchemaSource` (`HeaderParamsSchemaSource`), and the reference count is the binding count. ⭐ The `.params.json` editor comes for free with the same source; it is the asset CE-2136 left without a UI |
| **MA-6** | Open / New macro | ⭐ the scenario picker (CE-4141) gains a **Macros** group = `ScenarioEnumeration` over `ContentLayout.Macros(root)`; a pick is an edit load of `ContentLayout.MacroName(path)` (the resolver arm exists). The title says *Macro: forces/RedQRF*. *New macro…* = a new scenario under `macros/` with `kind = macro` and the anchor at the origin, heading north | the picker's group list + one enumeration root; the load and save paths are unchanged |
| **MA-7** | Add Entity ▸ Macro (SR-104 Z1) | ⭐ the Add Entity gallery gains a **Macros** group (`GalleryGroups` data, the same enumeration as MA-6). Placing one is the ordinary placement tool and creation request: TKB 8813 + `MacroInstance {Macro = path, ArgsJson = "{}"}` at the click, the heading from the tool. The edit world expands it (S3a), and the worker's member lines and dimmed members show what it put in | no new creation path; `MacroInstance` is saved by its translator |
| **MA-8** | the instance in Details (SR-39a) | ⭐ the inspector draws a **Macro** section for an entity with `MacroInstance`: the macro (the same picker, Macros group) + `MacroArgsForm` — one row per header param of the picked macro (read through the content source): its default greyed until overridden, *reset to default*, each row drawn by the ONE drawer over a one-field typed holder (`ParamTypes` gives the type). A change builds a NEW `MacroInstance` record and writes it to the owner (S3a reconciles). The expansion status (`Done` / `Failed` + reason) is shown at the top | needs MA-3. ⚠ the owner write route for a managed record is measured first (§1 last row) |
| **MA-9** | Save selection as macro (§14) | ⭐ one SAVE path, not a second serializer: the save payload gains `OnlyGuids` + a target name `macros/<path>` + a header (`kind = macro`, anchor = the selection's centroid, heading north). Each node writes only the selected entities it owns. ⭐ Before saving, the editor walks the selection's references (the S0 walker) and lists every reference to an entity OUTSIDE the selection. For each, either turn it into a param (default = its current key or GUID, type from the field) or cancel — a macro is closed (SR-58). v1 leaves the selection in place; *replace with an instance* is a later item | the save payload grows by optional fields; a node that ignores them would save everything ⇒ a rail per host (the silent-default rule) |
| **MA-10** | Preview of an `InsertMacro` (SR-189) | ⭐ **designed `2026-11-03` in §10** (MP-1 … MP-4): ghosts from the pure expander, nothing created until *Insert*. ⛔ Superseded lean: *"the button calls MA-7's creation with `IsTransient`"* | §10 |

**Rejected — one line each:**
- *A bind button in each editor (inspector, params form, predicate builder)* — three copies; the drawer is the one place every editor passes through.
- *The host decides the home* — a host can then write `ParamBindings` for a reference leaf, which the next typed save keeps but the walker never reads.
- *Keeping the header in the editor session* — a second owner beside the resident the load already fills; it also leaves CGF's and IG's slices without it.
- *Patching `MacroInstance.ArgsJson` through the attribute route* — measured: the patch is accepted and changes nothing; the record is immutable by design.
- *Save selection by serializing the editor's replica* — the editor does not own every selected entity (split authority); the ONE save path asks each owner.

## 4. Phases

| phase | items | gate |
|---|---|---|
| **P1** | MA-4 (the header owner + merge + slice path) · MA-3 (`ParamTypes`) | rail: macro load → save keeps the header; merge keeps it from the first slice; `ParamTypes` round-trips every shipped TypeId |
| **P2** | MA-1 · MA-2 · MA-5 | rails: each home written by its node kind; the chip; rename follows all three homes; a frame rail of the bind menu under `xvfb-run -a` |
| **P3** | MA-6 · MA-7 · MA-8 | rails: the Macros group lists `macros/`; placing creates 8813 + `MacroInstance`; the args form writes a new record; live check on `test/fire-team` |
| **P4** | MA-9 | rails: an outside reference is listed; turned into a param; the file holds only the selection |

## 5. Claim table *(the leans rest on)*

| claim | code | design basis |
|---|---|---|
| every editor field goes through `ComponentEditDrawer` | ✅ 8 `new ComponentEditDrawer` sites | ✅ CE-1023 "the ONE params form" |
| the header is dropped on save | ✅ `HrotScenarioSaveHandler.cs:98`, `ScenarioMergeCore.cs:98-188` | ✅ SR-163 says the save writes bindings "with no extra step" ⇒ the header must survive |
| no TypeId → CLR table exists | ✅ grep, none | ⛔ searched `docs/` for one, none found |
| the binder is non-destructive, so the editor may hold bound values | ✅ §5.1.3 | ✅ SR-168 |
| a managed record can be written to its owner from the inspector | ⛔ **unmeasured** — MA-8 only; P3 measures it before building | ✅ SR-175 (routes an args edit to the owner, `EntityWriteRouter`) |

## 6. AS BUILT — P1 *(ui, `2026-11-03`; MA-4 the header owner, MA-3 `ParamTypes`)*

| decision | as built | deviation |
|---|---|---|
| MA-4 the ONE owner | `ScenarioDescriptionResident.HeaderJson` (a flat string: exactly the object `ScenarioHeaderJson.Write` produces for Kind · Params · Anchor · Placement · Relocatable; empty = none). `ScenarioDescription.Stamp(…, headerJson)` · `HeaderJsonOf(ScenarioHeader / json)` · `HeaderOf(world)` (a `ScenarioHeader` with only those fields). `ScenarioLoadStep` stamps it from the loaded file's header (`_pendingHeaderJson`, with the description). A later stamp replaces the whole resident (`SetSingletonManaged`) | the design's `Header` property is a STRING (`HeaderJson`), not a typed record: the resident stays flat (CLAUDE.md "NEW COMPONENTS"); the typed view is `HeaderOf` |
| MA-4 the save writes it | ONE helper, `ScenarioSaveCore.HeaderOf(world, docType)` (`Hrot.Core/Scenario/Map`): terrain + description + the resident's macro fields. Its two callers are the only production `new ScenarioHeader(` save sites — `HrotScenarioSaveHandler` (every host registers it: editor, CGF, SimHost, IG) and `ScenarioFileService.SaveScenario` (the editor's shim) | the other `new ScenarioHeader(` in production is `ScenarioHeaderJson.Read` (a reader) |
| MA-4 the merge keeps it | `ScenarioMergeCore` (⚠ backend's, `Fdp.Toolkits`): the macro fields of the first slice that has any are written through `ScenarioHeaderJson.Write`; a differing later slice WARNS and the first is kept (the CE-3288 Description rule). No macro fields ⇒ the output is byte-for-byte today's | — |
| MA-4 the slice path | `HrotScenarioSaveHandler.SliceRelativePath` builds `<folder>/.slices/node_N.json` from the new `ContentLayout.ScenarioRelativeFolder(name)` (the resolver against a virtual root: `scenarios/x`, `macros/x`, `recipes/scenarios/x`, `opened/…`) — the folder the merge reads and cleans. `StorageProcessManager.MergeScenarioSlices` is now `internal` for its rail | — |
| MA-3 `ParamTypes` | `Fdp.Toolkits/Scenario/ParamTypes.cs`: `TryResolve(typeId, out Type)` (either spelling, case-insensitive) · `IdOf(Type, ParamTypeSpelling.Short \| Clr)` · `Fits(typeId, Type)` (the SAME type, or `Nullable<>` of it — nothing is converted) · `RegisterEnum`. Short: `EntityLink PointLink AreaLink Bearing WorldRotation float int bool string double` · CLR: their `FullName`s (`System.Single` …). Unknown id ⇒ resolves to nothing, fits nothing, never throws | ➕ `Vector3` / `System.Numerics.Vector3` (a `.params.json` field — `T41Params.Home` — has no other spelling). **Enums: ONE rule** — an enum is a param type when REGISTERED (`RegisterEnum`), found by name (`ForceId`) or full name; `Fdp.Core.ForceId` (what the macro samples and §5.1.1 spell) is registered; another enum is registered once by the feature that declares it. ⛔ not a scan of loaded assemblies (two enums of one name would resolve by load order). The validator's SR-162 type half (backend's file) is NOT changed — offered, not done |

Rails (each in its feature's suite): `ScenarioMergeCoreTests` +3 · `HrotScenarioSaveHandlerTests` +2 · `ScenarioLoadStepTests` +1 · `StorageProcessManagerTests` +1 · `ContentLayoutTests` +1 · `ParamTypesTests` (new: no suite existed). Red-proofs: the merge not writing the fields · `HeaderOf` ignoring the resident · the load not passing `headerJson` — each fails its rail.

## 7. AS BUILT — P2 *(ui, `2026-11-03`; MA-1 the affordance, MA-2 the three homes, MA-5 the params source)*

```mermaid
graph TD
  D[ComponentEditDrawer.DrawLeafNode] -->|BindView| M[ParamBindModel - pure]
  M -->|ctx.Params / BoundTo / Bind| C[IParamBindContext]
  C --> E[EntityBindContext - ParamBindings under prefix.path]
  H1[FdpEntityInspectorHelper.BindContextFactory] --> E
  H2[EntityAiDetailsView.BindsFor] --> E
  E -->|params in scope| P[HeaderParams.Of - resident header]
  S[HeaderParamsSchemaSource] -->|Stamp new resident| P
  S -->|rename / count / find| U[ParamUsage - walker + Bind maps + ParamBindings]
  W[ParametersWindow] --> S
  POP[bind popup New parameter] -->|DeclareParam| S
```
*Caption: who feeds the drawer its context (every host that holds the world), and the ONE scan (`ParamUsage`) behind the count, the remove refusal and the rename.*

**As built, and where it differs from §2/§3:**
- `IParamBindContext` + `ParamBindModel` (pure: bindable, home, chip/button/red, bind/unbind) + `EntityBindContext` live in `Fdp.Presentation/ImGui/Editing/`. The drawer takes the context as the **4th** optional ctor argument (not the 3rd — a 3rd would break positional `customDrawers` callers); `ComponentEditWindow` and `BehaviorParamsForm.DrawFields` forward it. Unbound bindable row: a faint drawn chain button at the right edge (the font has no ⛓); bound: solid chip in front of the value; red chip with the reason on a problem; click / label right-click opens *Bind to ▸ · New parameter… · Unbind*. A field whose path has an index is not bindable (SR-161).
- Homes decided by the drawer: link leaves (`EntityLink`/`PointLink`/`AreaLink`) → Param form via new `BindToParam/UnbindParam` on the link types (`Links.cs`); nearest container a `BindableNode` → its `Bind` map (+ `MarkStructuralChange`); else `ctx.Bind(path)`.
- Hosts: the diagnostics bundle (`FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu` now takes the world getter, required) wires the panel's and every watch window's reflector, so IG/CGF/SimHost/Editor agree by construction; `EntityAiDetailsView` gives the task / SOP params forms prefixes `Behavior.Params` / `Sop.Params` (instance rows and mission/order forms have no entity ⇒ none).
- ⚠ **Deviation — undo of an inspector bind:** no component-edit undo seam exists in the inspector (`IInspectableSession.SetComponent` is the one write path), so a bind/unbind there is one `SetComponent` of a NEW `ParamBindings` (not on an undo stack). The Parameters window has its own history (`ParamsEditHistory`): add/remove/move/edit/rename are undoable, a rename (declaration + every binding in all three homes) is ONE step.
- MA-5: `HeaderParamsSchemaSource : IVariablesSchemaSource` over the resident header's `Params` (each edit stamps a NEW resident); Remove is refused while bound, naming the bindings; the reference count is the binding count (cached ≤ 1 s). Opened from *Scenario ▸ Parameters…* and by the popup's *New parameter…* (`DiagnosticsHostServices.DeclareParam`, only the editor passes it). `ParamTypes.All()` supplies the type choices. ⛔ The `.params.json` source is not built.
- Live check (real editor under Xvfb, `macros/test/fire-team`): the Scenario menu shows *Parameters…*, the window lists `side : ForceId` from the loaded macro, the entities carry `EntityInfo.ForceId → side`, and a save to a copy wrote the 4 bindings back with `Header.Params`. ⚠ The popup click and the in-table rename could not be driven by xdotool (ImGui double-click timing); both are railed instead.

## 8. AS BUILT — P3 *(ui, `2026-11-03`; MA-6 Open / New macro, MA-7 Add Entity ▸ Macros, MA-8 the instance in Details)*

```mermaid
graph TD
  L[MacroPickerEntries.Paths - ScenarioEnumeration over ContentLayout.Macros NAS] --> P[AssetPickerLauncher - Macros group, OpenMacros]
  L --> G[EntityAuthoringInputs.Macros -> AddEntityAction -> EntityTypeCatalog.Build macros]
  P -->|pick = LoadScenario macros/path = OpenForEdit| T[EditorWindowTitle - Macro: path]
  P -->|Change...| V[MacroInstanceDetailsView]
  G -->|ArmMacro: point tool on 8813, path in the placement json| A[ScenarioSpawnAdapter.ArmPlacement - every host]
  A -->|MacroBirth.Split / BornWith| I[MacroInstance in the new entity]
  N[Scenario / New Macro...] --> C[ScenarioNewAssetService.CreateNew - name under macros/]
  C -->|ScenarioDescription.StampNewMacro| R[resident header: Kind, Anchor] --> S[the ONE save path, MA-4]
  I --> V
  V -->|MacroArgs.With - NEW record| W[MacroArgs.Write - only where this node owns it] --> X[MacroExpansionSystem reconcile SR-150/157]
```
*Caption: one listing (the NAS `macros/` root the load resolves against) feeds both the picker and the gallery; the placement path-carrier and the birth component live in the one adapter every host's point commit passes; the args form writes only where the component is owned.*

**As built:**
- **MA-6.** The scenario picker (`AssetPickerLauncher`, new optional `macroPaths`/`macrosRoot`) gains a **Macros** group (`MacroPickerEntries`: folders → category `Macros/<folder>`, tag = a scenario-kind asset named `macros/<path>`, so a pick is the ordinary `LoadScenario` = `OpenForEdit`). The root is `ContentLayout.Macros(NasRoot)`, ⛔ never the repo; an empty root shows the disabled hint *No macros on the server — see macros/ seeding*. Only the editor passes it (macro authoring is the editor's; other hosts' pickers are unchanged). Title: `EditorWindowTitle.Detail` → *Macro: <path>*. **New macro…** is *Scenario ▸ New Macro…* → the same recipe picker (`NewAssetLauncher.OpenMacro`) → a macro naming dialog → the SAME `ScenarioNewAssetService.CreateNew`: a name under `macros/` (`IsMacroName`) stamps `Kind = macro` + anchor at the origin (`ScenarioDescription.StampNewMacro`) into the resident header after the seed loads, then the same save-as — the first save writes them (MA-4). A macro with no world to stamp is refused, never saved as a scenario. (Put in the *Scenario* menu, not *File*, to leave the File-menu layout golden alone.)
- **MA-7.** `groups.json` gains `{ "name": "Macros", "macros": true }` (a group with no masks/tools; `GalleryGroups.MacrosName`). `EntityTypeCatalog.Build(…, macros)` lists each macro file as an entry on the instance type (`MacroPath`), group *Macros*, chip = its folder; the bare 8813 is **excluded from the catalogue** (it was in *Props*, and in the Spawner panel's list). Arming = `PlacementToolRegistry.ArmMacro` → the ordinary point tool on 8813 with the path riding in the placement JSON under `$macro`; `ScenarioSpawnAdapter.ArmPlacement` — the one point commit every host passes — splits it out (`MacroBirth.Split`, the attribute compiler never sees it) and `MacroBirth.BornWith` adds `MacroInstance { Macro = path, ArgsJson = "{}" }` (the sibling of `AreaAuthoringArm.BornWith`). `EntityAuthoringInputs.Macros` is passed by the editor only (the only host that expands macros in an edit world).
- **MA-8.** A **Macro** tab in Details (`MacroInstanceDetailsView`, applies to one entity with a `MacroInstance`): the `MacroExpansionStatus` line (*Done* / *Failed: reason*), the macro + *Change…* (the picker's Macros group, `OpenMacros`; the args reset), and one row per header param (`ScenarioHeaderJson.Read` on `{macros root}/{path}/scenario.json`) drawn by the ONE drawer over `DefaultValueAuthoring`'s one-field holder (`VariableEditModal.ScalarRowOrRoot`, now public); default greyed with a *default* tag until overridden, then a *reset* button. `MacroArgs.With` builds a NEW record whose `ArgsJson` is merged key by key; a value equal to the default is no override.
- **MEASURED — who may write the instance.** In the editor's edit world the node creates and owns what it spawns: `NetworkAuthority { PrimaryOwnerId = LocalNodeId = 1 }`, and the `MacroInstance` authority bit is set — so `MacroArgs.Write` (`IInspectableSession.SetComponent` → `SetManagedComponent` of the new record) lands, and the expander's reconcile re-expanded the members with the **same network ids** (live: `side` Hostile → Friend, the four members' `ForceId` followed). A component this node does NOT own (authority bit clear — a plain local entity in a rail, or another node's) is refused with *Edit on the owning node: this node does not own this instance*, the form read-only; nothing is written to a foreign instance. ⚠ Not measured: an instance owned by ANOTHER node of a multi-node edit cluster (no second node in the live run) — the refusal rail covers the mechanism, the cluster case is unobserved. ⛔ `POST /entities/{id}/component {"Args":…}` is not used (§15.4).
- Live check (real editor, Xvfb, `macros/test/fire-team` seeded into the NAS): *File ▸ Load Scenario…* lists **Macros ▸ test ▸ fire-team** (with its description pane); *Open* loaded `macros/test/fire-team`, window title *HROT Cluster Runner — Macro: test/fire-team*; with a scenario loaded, a macro instance created by `POST /entities/spawn` (8813 + `MacroInstance`) expanded (`Done`, members 1011–1014); the **Macro** tab showed *Expansion: Done*, the macro and `side = Hostile (default)`; choosing *Friend* wrote `Args {"side":"Friend"}` and the members re-expanded with `ForceId Friend`; a save to a copy name wrote `"MacroInstance":{"Macro":"test/fire-team","Args":{"side":"Friend"}}` and no members. ⚠ **Not driven live:** the *Add Entity ▸ Macros* click (the canvas menu would not open under xdotool) — the placement is railed through the real adapter instead; and *New Macro…* through its dialogs.
- ⚠ Findings: `Hrot.Editor.Tests` gains the `Hrot.Editor.UiFrameRail` reference and `Xunit.SkippableFact` (the Macro section's frame rail); the font draws `—`/`…` as `?`, so the new drawn strings use ASCII (the picker hint keeps the literal text the handoff specified).

## 9. AS BUILT — P4 *(ui, `2026-11-03`; MA-9 Save selection as macro)*

```mermaid
sequenceDiagram
  participant U as editor gesture (Scenario menu / unit menu)
  participant F as SaveSelectionAsMacroFlow + MacroClosure (S0 walker)
  participant S as EditorScenarioSession.SaveSelectionAs
  participant M as ClusterMaster (SaveScenario fan-out)
  participant N as every node: HrotScenarioSaveHandler
  participant C as ScenarioSaveCore (filter · header · rebind)
  U->>F: Begin (selection) + name
  F->>F: walk the selection's leaves, outside = not in the selection
  alt closed
    F->>S: macros/path, guids, header (Kind=macro, centroid anchor)
  else open: dialog
    F-->>U: entity . field . target list
    U->>F: Make parameters (or Cancel = nothing saved)
    F->>S: same, header + a param per outside target (default = its key, else its GUID)
  end
  S->>M: ExecuteStorageOpIntent {OnlyGuids, HeaderJson}
  M->>N: SerializeLocal ScenarioSaveHandlerPayload (node-op JSON: OnlyGuids, MacroHeaderJson)
  N->>C: HeaderOf(world, docType, HeaderJson) · Write(.., OnlyGuids)
  C->>C: Serialize writes only OnlyGuids, RebindOutsideReferences rewrites ONLY the DOM
```
*Caption: the one save path with two optional inputs; the closure walk and the rebinding are the only new logic, and the rebinding never reaches the world.*

**As built:**
- **The payload (no wire struct changes).** `ScenarioSaveHandlerPayload(ScenarioName, OnlyGuids = null, HeaderJson = null)`; `ExecuteStorageOpIntent` gains `OnlyGuids` / `HeaderJson`; the request chain (`ClusterOpEgressTranslator` → `ClusterOpMasterTranslator` / `ClusterOpRequestAdapter`) reads and writes ONE encoding, `SaveScenarioRequestJson` (a plain save encodes byte-for-byte as before); the node-op DTO `NodeTransitionPayloadDto` gains `OnlyGuids` / `MacroHeaderJson`, **written only when set** (`WhenWritingNull`). Both are JSON strings inside the existing DDS `PayloadJson` — `ned-wire-inventory.py --check`: *OK, 123 wire types == 123 rows*. `POST /scenario/save` takes optional `onlyGuids` / `header`. ⚠ A selection save is NOT a save of the scenario: `ClusterMaster` does not start an unsaved-check round for it, and `EditorScenarioSession.SaveSelectionAs` changes neither the loaded name nor the takes.
- **The handler, in ONE place.** `ScenarioSerializer.Serialize(repo, header, onlyGuids)` writes only the saveable entities in the set (after the ownership gate; the save resolver still knows every saveable entity, so a reference to an outside entity is written, not thrown on; no terrain-objects section); `ScenarioSaveCore.HeaderOf(world, docType, macroHeaderJson)` takes the macro fields from the override (and does not carry the source scenario's description); `BuildDom` / `Write` take `onlyGuids`.
- **Rebinding, in the saved DOM only** *(the lean, built as measured)*. The auto-serializer writes an unauthored `EntityLink` as the target's **network id number**, not a GUID — so a text match would have missed it. `ScenarioSaveCore.RebindOutsideReferences` therefore walks each saved component's JSON with the S0 walker (`EntityRefRemap.WalkJson`, typed: only `EntityLink` / `AreaLink` leaves) and rewrites a leaf to `"^param"` when it names the param's target — by its authored key / GUID, or by the network id of the entity the param's default (`@Key` via `RefKey`, or a GUID via `EntityGuid`) names in the saving node's world. The saved file holds only the binding (no stale network id). The live world is untouched (rail).
- **The gesture.** *Scenario ▸ Save Selection as Macro…* and the map's unit menu item (new `GlobalActionIds.SaveSelectionAsMacro` = 544; only the editor registers its handler) open `SaveSelectionAsMacroWindow` over `SaveSelectionAsMacroFlow`: a name → the closure walk (`MacroClosure.Walk`: the S0 walker with a new no-op `LeafApplier.Visiting(member)` hook so the dialog can name the FIELD) → a closed selection saves; an open one shows `entity · field · target` and offers **Make parameters** or **Cancel** (Cancel saves nothing). Header: `Kind = macro`, anchor = the selection's centroid (x, y mean, z mean, heading 0), params one per distinct outside target (type `EntityLink` / `AreaLink`; default = `@Key` when the target has a `RefKey`, else its GUID). v1 leaves the selection in place.
- **Not a parameter ⇒ not savable:** a plain `EntityRef` or a `PointLink` to an outside entity has no Param form; the dialog lists it with the reason and **Make parameters is disabled** — ⛔ a macro is never saved with a dangling outside reference.
- **Live check** (real editor, Xvfb, `ua-fire-and-movement`): two men selected (`SelectionChangeRequest`), *Scenario ▸ Save Selection as Macro…*, a name, *Save* → the selection was closed (no outside reference), so it saved at once: `macros/<name>/scenario.json` has `Kind: macro`, `Anchor: [200, 285, 0] heading 0` and exactly the two men; the scenario on screen (name, entity count) is unchanged; copies deleted. ⚠ **Not live:** the *Make parameters* branch — no registered, saved component of a unit holds an `EntityLink` / `AreaLink` leaf (measured: the only such leaves are in params DTOs and JSON blobs), so there is no real unit to reference an outside entity; that branch is railed through the REAL save handler + serializer with a link-bearing component. (The unit-menu item and an in-field ctrl-select could not be driven under xdotool; the menu route was.)
- ✅ **CLOSED `2026-11-03` (W-M6) — references inside JSON blobs.** *Prior state (SUPERSEDED): the closure walk saw typed leaves only, a blob reference was invisible.* SR-159 rules it: a reference inside params is bound as its Param form, so a blob reference is treated exactly like a typed leaf — listed, and *Make parameters* rebinds it to `^name`; no refusal. As built: ① the per-component pass that `StagingEntityExtractor` had privately (`ApplyPass`) moved to ONE public static `ScenarioEntityWalk.Apply(comps, behaviorRemapper, pass)` in `Hrot.Core` (`Scenario/Map/`, cross-lane edit: backend's CGF extractor now calls it, no copy left); it walks typed leaves, `ActiveMissionPlan` tasks' and `InitialBrainIntent`'s params blobs through `ScenarioBehaviorRemapper.ApplyJson` (the one blob parser) and route waypoints. ② `MacroClosure.Walk(world, selected, behaviorRemapper)` feeds it the unit's brain slots as the save writes them (`BrainIntentReader.Read(..., Ordered)`, needs the remapper's registry) and its `ActiveMissionPlan`; the walker says which slot (`LeafApplier.Entering`: `Behavior.Params`, `Sop.Params`, `task n (name) params`) and which field (`Visiting` now also fires in the JSON walk). ③ The saved DOM: `ScenarioSaveCore.RebindOutsideReferences(..., behaviorRemapper)` runs the same `ApplyJson` with the rebinding applier over `Behavior` / `Sop` `Params` and `MissionPlan.PlanData.tasks[].behaviorParams` (links there are GUID / key strings). ④ `ParamBindings` strings are bindings, not references: skipped, saved as they are. ⑤ The remapper (hence the behaviour registry) is handed through: the editor's flow gets one built from its registry; `HrotScenarioSaveHandler` takes the node's own (`NodeBootstrapper` passes it) — a forwarding rail asserts it on the constructed handler. Without a remapper blobs are left alone (a host with no contracts). ⚠ **Known gap (not ui's): a trigger's `do`-list is NOT walked.** *Superseded claim: "trigger `do`-lists are typed `BindableNode`s, reached by the typed walk" — unmeasured, and wrong:* `DESIGN_Scenario_Macros.md` §16.2 measured that the trigger's params contract carries `do` as a free-form `JsonNode`, so the contract-typed blob walk never types a step and a reference inside a `do` step (an `Order` target) is neither listed nor rebound. It closes by construction when behaviors types the step (`{Act, Args, Bind}` : `BindableNode`, B1b, `NOTE_Backend_To_Behaviors_Step_Bind.md`) — no ui change then. Rails: `SelectionMacroTests.W_M6_*` (3), `HrotScenarioSaveHandlerTests.CE4210_W_M6_*` (2); red-proof: disabling the mission-plan arm in the shared walk reddens all 3 closure rails.


## 10. MA-10 — preview a macro before committing *(DESIGN, `2026-11-03`; `HANDOFF_Coordinator_To_Ui_Rules_Runnable.md` ⑦; SR-189; leans MP-1 … MP-4)*

### 10.1 INVENTORY *(`2026-11-03`)*
**Queries:** grep `class MacroExpander` → `Hrot.Common/Macros/MacroExpander.cs:29` (THE expander; a second same-named one in the Blueprints compiler is unrelated) · read its header: *"turns ONE macro instance into the ordinary creation requests of every entity it makes … from the FILES — before anything exists … ⛔ Pure: it publishes nothing; a failure comes back as the reason"*; `Result(Children: EntityCreationRequest[], Failure, MaxShift, Relocatable)` · MA-7 as built (§8: `ScenarioSpawnAdapter.ArmPlacement` places TKB 8813 + `MacroInstance`; `MacroBirth`) · the `InsertMacro` step (R5b's Do tab). ⚠ coverage not checked (no `check_index_coverage` in the CLI).

| measured | so |
|---|---|
| the expander is PURE and returns every child's creation request (type, position, heading, name …) before anything exists | ⭐ a preview needs NO entity: expand, draw the requests dimmed, create nothing |
| a transient INSTANCE (the handoff's lean, MA-10's note) in a LIVE run spawns members that start their brains at once — they move, see and shoot, and the recording keeps it | ⛔ *Cancel* could not undo what they did; a preview must not run |

```mermaid
classDiagram
    direction LR
    class MacroPreview { <<NEW, pure>> Of(macro path, args, anchor, heading) → ghosts or the failure }
    class MacroExpander { <<existing, pure>> Expand(instance) → Result(Children, Failure) }
    class MacroPreviewLayer { <<NEW map layer>> draws the ghosts dimmed (icon + name), the anchor, a footprint }
    class PreviewBar { <<NEW>> Insert · Cancel · the failure text }
    class ArmPlacement { <<existing MA-7>> the placement tool, TKB 8813 + MacroInstance }
    class InsertMacroStep { <<existing R5b>> the Do tab's step row }
    InsertMacroStep ..> MacroPreview : Preview button
    ArmPlacement ..> MacroPreview : while the tool hovers
    MacroPreview ..> MacroExpander
    MacroPreviewLayer ..> MacroPreview
    PreviewBar ..> ArmPlacement : Insert = the ordinary placement / creation
```
*What the picture shows that prose hid:* the preview and the real insert read the SAME expander, so the ghosts are exactly what *Insert* will
create, and nothing exists until *Insert* (MA-7's creation, or the step's own `InsertMacro`).

```mermaid
sequenceDiagram
    participant U as author / instructor
    participant P as MacroPreview
    participant X as MacroExpander
    participant L as MacroPreviewLayer
    U->>P: Add ▸ Macros ▸ forces/red-qrf (hover) — or the Do tab's Preview on an InsertMacro step
    P->>X: Expand(a throw-away MacroInstance at the anchor, the args)
    X-->>P: Children (requests) or Failure
    P->>L: ghosts (dimmed) + the anchor
    alt Insert
        U->>P: click / Insert
        P->>P: the ordinary path — MA-7's creation (edit or live) / the step stays as authored
    else Cancel / Esc
        P->>L: clear — nothing was ever created
    end
```

### 10.2 Decisions

| # | decision | ⭐ lean |
|---|---|---|
| **MP-1** | how a preview is made | ⭐ **ghosts from the pure expander**, never a transient instance: `MacroPreview.Of(path, args, anchor, heading)` builds a throw-away `MacroInstance` in memory, calls the ONE `MacroExpander`, and returns each child's type / name / position / heading (or the expander's failure text). ⚠ **Deviates from the handoff's lean** (*"a transient instance … Cancel deletes it"*): measured, a live transient instance's members start acting at once, so a cancel cannot undo it. The ghosts give the same picture with nothing to clean up |
| **MP-2** | where it shows | ⭐ (a) **Add ▸ Macros** placement: the ghosts follow the cursor (anchor = the hover point, heading from the tool), a click inserts (MA-7, unchanged), Esc cancels. (b) the Do tab's **Preview** on an `InsertMacro` step: the ghosts at the step's anchor and args with an *Insert now* (live only: the step's macro inserted once via the same creation) / *Close* bar |
| **MP-3** | drawing | ⭐ a map layer: each ghost as its type icon at 50 % alpha with its name, a dashed line to the anchor, the expander's failure in red on the bar. 2-D first; 3-D only if the existing placement ghost already has a 3-D path (reuse it, never a second one) |
| **MP-4** | a failure | ⭐ shown as text, nothing drawn, *Insert* disabled with the reason (the expander's own words) |

**Rejected — one line each:**
- *A transient instance, deleted on Cancel* — in a live run its members act and are recorded before you cancel.
- *A frozen transient instance* (brains paused) — a new pause state on spawned entities for one screen; the ghosts need none.

| claim | code | design basis |
|---|---|---|
| the expander is pure and returns the children's requests | ✅ `MacroExpander.cs:1-29` (header), `Result` | ✅ macros §15 (S3a ④) |
| a live transient member acts at once | ⛔ **inferred** (members are ordinary entities with their brain intents; nothing pauses them) — measure at build | — |
| MA-7's placement is the one insert path | ✅ §8 as built | ✅ MA-7 |
