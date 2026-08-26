<!--STATUS
state: LIVE
build-state: READY-TO-BUILD. Carries the INVENTORY (§2), a classDiagram (§4) and a sequenceDiagram (§5).
  Axis-C increment E2 (gap map §2c). Handoff references this; do not design in the handoff.
updated: 2026-08-26
current-answer: §3 = what to build (5 items). §4/§5 = the UML. §2 = the measured inventory that says E2 is
  the PICKER UI + wiring, NOT the service layer (CGF already has that).
design-basis: PROGRAMME_Cgf_Equals_Editor_Gap_Map.md §2c (Axis-C E2, the asset-picker/new-asset shell
  relocation, §6.1) · DESIGN_Cgf_Scenario_Session_Slice.md §9 (Slice A left Open/New greyed on CGF —
  "they light up the day E2 composes a picker") · DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md (the
  OPEN-by-MCP path, already built — the service layer) · Architect_Question_57 (the create/recipe registry,
  MA-019..023) · ruling 9 (one implementation) · ruling 49 / VC-3 (unavailable = absent/greyed-with-cause).
known-conflict: edits EditorSubsystem.cs + CgfSubsystem.cs + ScenarioMenuCommands.cs (AiShared) — the same
  hot files Slice A and the backend batch touched; rule-4 re-pull. ⛔ Disjoint from the MCP lane (DebugApi).
-->
# DESIGN — **CGF asset-picker / new-asset shell** *(Axis-C increment E2)*

> 🎯 Slice A registered `Open Scenario` / `New Scenario` on CGF but left them **greyed** ("no picker on this
> host"). E2 composes the picker on CGF so they light up — by **lifting the launcher glue to shared** and
> **factoring the duplicated create-core into one type** (ruling 9). ⛔ Not the service layer — CGF already has it.

## 1. ⭐ WHY THIS IS UI-ONLY *(measured `2026-08-26`)*
CGF already composes the whole **service/create** layer — per-kind `INewAssetService` registry
*(`CgfSubsystem.WireAssetCreation:1507`, exposed as `AssetShellNewAssetServices`)*, `AssetShellCreate`
*(:1530, a near-verbatim copy of the editor's create-core)*, `AssetCatalog`, `AiDocumentManager`, recipes
— all reachable over MCP *(MA-019..023)*. ⇒ **E2 is the interactive PICKER UI + menu/toolbar wiring**, the
one thing CGF lacks: it passes `openPicker: null` / `openSaveAsDialog: null` to `ScenarioMenuCommands.Register`
*(`CgfSubsystem.cs:1770-1782`)* and omits OpenAsset/NewAsset from the toolbar `HostServices` *(:1744)*.

## 2. ⭐⭐⭐ INVENTORY — the shell classes, measured
| class | home today | verdict for E2 |
|---|---|---|
| `AssetPickerLauncher` | `Hrot.Editor/Browser` | ⭐ **CLEAN LIFT** → `Hrot.Editor.AiShared/Browser` — pure delegate-seam glue *(`openPicker` is an injected `Action<PickerRequest,Action<PickerResult>>`; deps `IAssetCatalog`/`AssetPickerSource`/NodeEditor picker — all shared)*, no `IEditorLogic`/`EditorApplication` |
| `NewAssetLauncher` | `Hrot.Editor/Browser` | ⭐ **CLEAN LIFT** — deps `RecipePickerSource`, `IReadOnlyDictionary<AssetKind,INewAssetService>`, picker types — all shared |
| `AssetPickActionRouter` | `Hrot.Editor/Browser` | ⭐ **CLEAN LIFT** — `(Action<IEditableAsset> openDocument, Action<string> loadScenario)`, its own doc says it avoids concrete host types |
| `ShowNewAssetDialog` + `CreateAssetCore` | **inline in `EditorSubsystem.cs:3797/3831`** | 🔴 **EXTRACT + DEDUP** — a local fn closed over ~8 host fields; **CGF has an equivalent inline copy** *(`AssetShellCreate`, `CgfSubsystem.cs:1530`)*. ⇒ ⭐⭐ **factor the TWO copies into ONE shared `AssetCreateController`** (ruling 9) taking the deps as ctor delegates |
| `AssetPickerSource` · `RecipePickerSource` · `INewAssetService` · `NewAssetDialog` · `SaveAsDialog` | already `Hrot.Editor.AiShared` | ✅ already shared — E2 reuses |

⚠ **The one measured RISK — the modal itself.** `AssetPickerLauncher.openPicker` is backed in the editor by
`_shellPickers.OpenPicker` *(the actual modal browser over `WindowManager`)*. **Confirm CGF can compose an
equivalent `openPicker` / save-as modal** *(is the shell-picker infra shareable, or does CGF compose its own over
its `WindowManager`?)*. ⛔ If a host genuinely cannot host a modal *(a headless node)*, the items stay
greyed-with-cause per ruling 49 — that is the correct end state, not a bug.

## 3. ⭐ WHAT TO BUILD *(5 items)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Lift** `AssetPickerLauncher` · `NewAssetLauncher` · `AssetPickActionRouter` → `Hrot.Editor.AiShared/Browser` | ⚠ HN-037 lesson: measure the field captures — a lift, ⛔ not `s/old/new/`. Editor references the moved types byte-identically |
| ⭐⭐⭐ **②** | **Extract `AssetCreateController`** from `EditorSubsystem.ShowNewAssetDialog`/`CreateAssetCore` **and delete CGF's `AssetShellCreate` duplicate**, both composing the one shared type | ⛔⛔ **ruling 9** — the whole point is ONE create-core, not a third copy. The editor + CGF both construct it with their own dep set |
| ⭐⭐ **③** | **CGF composes** the launchers + create-controller over its own catalog/registry/`WindowManager`, and passes real `openPicker`/`openSaveAsDialog` to `ScenarioMenuCommands.Register` *(replacing the two `null`s)* | ⭐ Slice A's greyed items light up with **zero menu code** — the wiring is the deliverable |
| ⭐ **④** | **Toolbar** — add OpenAsset/NewAsset to CGF's `HostServices` via the shared list *(the same `CgfEditorShellToolbar` path)* | ⚠ if this collides with the toolbar-customization AQ, keep it to enabling what the editor already exposes; ⛔ no new toolbar model |
| ⭐ **⑤** | **Conformance** — extend the existing `SubsetShape`/menu verdict: on CGF the Open/New items are now **enabled** *(not greyed)*, and a host that composes no modal keeps them greyed-with-cause | reuse the equality-rail pattern from Slice A; ⛔ no new verdict type |

## 4. ⭐⭐⭐ CLASS DIAGRAM
```mermaid
classDiagram
    direction LR
    class AssetPickerLauncher {
        <<LIFT to AiShared.Browser · seam glue>>
        +Open(kinds, callback)
    }
    class NewAssetLauncher {
        <<LIFT to AiShared.Browser · seam glue>>
        +Open(kind)
    }
    class AssetPickActionRouter {
        <<LIFT to AiShared.Browser>>
        +Route(pick)
    }
    class AssetCreateController {
        <<NEW · AiShared.Browser · extracted from EditorSubsystem + CgfSubsystem duplicate>>
        +Create(kind, recipe, name) IEditableAsset
    }
    class INewAssetService {
        <<exists · AiShared.Recipes · per-kind, already shared>>
    }
    class AssetPickerSource {
        <<exists · AiShared.Browser>>
    }
    class ScenarioMenuCommands {
        <<exists · AiShared.Scenarios · Slice A>>
        +Register(commands, session, openPicker, openSaveAsDialog)
    }
    class EditorSubsystem {
        <<exists · composes for the editor>>
    }
    class CgfSubsystem {
        <<exists · composes for CGF · TODAY passes null>>
    }
    AssetPickerLauncher ..> AssetPickerSource : lists
    AssetPickerLauncher ..> AssetPickActionRouter : routes a pick
    NewAssetLauncher ..> AssetCreateController : create
    AssetCreateController ..> INewAssetService : per-kind mint
    EditorSubsystem ..> AssetPickerLauncher : composes
    EditorSubsystem ..> AssetCreateController : composes
    CgfSubsystem ..> AssetPickerLauncher : composes (E2 — was absent)
    CgfSubsystem ..> AssetCreateController : composes (E2 — replaces AssetShellCreate)
    EditorSubsystem ..> ScenarioMenuCommands : openPicker + openSaveAsDialog
    CgfSubsystem ..> ScenarioMenuCommands : openPicker + openSaveAsDialog (E2 — was null)
    note for AssetCreateController "ONE create-core. Deletes CGF's AssetShellCreate duplicate and the editor's inline CreateAssetCore (ruling 9)."
    note for CgfSubsystem "openPicker is backed by a modal over CGF's WindowManager; a host that cannot host a modal keeps the items greyed-with-cause (ruling 49)."
```

## 5. ⭐⭐⭐ SEQUENCE DIAGRAM *(CGF, Open Scenario — now enabled)*
```mermaid
sequenceDiagram
    autonumber
    participant U as User (CGF)
    participant M as ScenarioMenuCommands
    participant L as AssetPickerLauncher
    participant P as openPicker modal (CGF WindowManager)
    participant R as AssetPickActionRouter
    participant S as IScenarioSession / AiDocumentManager

    Note over M: E2 wired openPicker, so the item is ENABLED not greyed
    U->>M: click Open Scenario
    M->>L: Open(scenario kinds, callback)
    L->>P: request a pick
    P-->>L: PickerResult with the chosen asset
    L->>R: Route(pick)
    R->>S: openDocument or loadScenario for edit
    Note over S: same shared path the editor uses — CGF differs only by composition
```

## 6. ⭐ DONE — rails
- editor byte-identical after the lift+extract *(the delegation gate)*; CGF's Open/New items **enabled** and functional; the `AssetShellCreate` duplicate is **gone** *(one create-core)*; a no-modal host keeps them greyed-with-cause; conformance verdict holds. Affected-project builds; conformance suite named + backgrounded (T3); reds proven pre-existing by `git diff`.

## 7. ⛔ NOT IN E2
Open-Asset/New-Asset for **AI assets** beyond scenarios ride the same shell once composed — verify they light up too, but ⛔ no new asset-kind vocabulary. Tools/selection/camera = **E3**; view/inspector = **E4**. Checkpoint restore = Feature X.
