<!--STATUS
state: LIVE
build-state: READY-TO-BUILD — AQ60 Slice A, revised to the user refinement `2026-08-26` (R1–R3).
  = Axis-C increment E1 (gap map §2c). Distinct File-menu items (no chameleons); toolbar-button
  selection REMOVED (its own toolbar-customization design). Class+sequence UML in §4/§5.
updated: 2026-08-26
current-answer: the whole file. Decisions + the user rulings: Architect_Question_60 (§3b, §4, §4b).
  ⛔ Checkpoint RESTORE, capability-gating, toolbar-customization, and the ASSET menu items (E2) are OUT (§8).
known-conflict: extends CgfSubsystem.cs + EditorSubsystem.cs + ScenarioMenuCommands.cs (UI/CGF lane) and
  moves EditorApplication's scenario half into Hrot.Editor.AiShared ⇒ UI/CGF lane; rule-4 re-pull.
-->
# DESIGN — **CGF scenario session: the shared facade + distinct File/Scenario menu** *(AQ60 Slice A = Axis-C E1)*

> 🎯 **The user's principle** *(2026-08-26)*: CGF ≡ editor bar distributed-vs-no-network; **most stuff
> shared, minimal duplication.** This is **increment E1 of the editor→shared move** *(gap map §2c Axis C)*:
> extract the scenario half of `EditorApplication` into a shared `IScenarioSession` both hosts instantiate,
> and give **both hosts distinct File-menu items — no chameleons, no per-host defaults in the menu (R2)**.

## 1. ⭐ INTENT BASIS *(cited — R-129)*
| source | binds this slice |
|---|---|
| `Architect_Question_60` §3b/§4/§4b | the user rulings — R1 *(whole editor→shared, nothing capability-level editor-only)*, **R2 *(distinct menu items, no chameleons)***, R3 *(toolbar selection is its own design)* |
| gap map **§2c Axis C** | this is **E1** of the editor→shared extraction *(host-agnostic → shared; the residual thin-host bootstrap is E5)* |
| rulings **58/59/65/66** | 58 all hosts open scenarios · **59 ② Open = cluster-wide request to the master** · 65 editing welcome, save machinery on CGF · 66 load fully possible from CGF |
| `DESIGN_Cgf_Menu_Follows_Focus_Slice.md` §10 *(CE-041..045)* | the shared menu list + `SUBSET-BY-DESIGN` verdict the new items ride on; ⛔ **the menu shows all serviceable items — ruling 49** |
| HN-029 | `/scenario/load/{edit,live}` — the two cluster load paths the menu items route to |

## 2. ⭐⭐ INVENTORY — feasibility *(scans `2026-08-26`; detail: AQ60 §4.1)*
- ⛔ `EditorApplication` can't be shared intact *(assembly wall `Hrot.Editor → Hrot.CGF`)*; ✅ **the scenario half is cleanly separable** — ctor deps engine/shared, **world is a ctor param**, `LoadScenarioByName` already routes through the cluster orchestrator; editor-local ride-along = `MigrationAlertManager` + the `ScenariosRoot` constant.
- ✅ **CGF already LOADS** *(HN-029 `CgfScenarioLoadHandler`)*; the SAVE serializer is on CGF *(`HrotScenarioSerializerFactory.Build`)*; **checkpoint SAVE** exists cluster-wide *(`TakeCheckpointIntent`)*. 🔴 checkpoint **restore** does NOT exist ⇒ §8.
- The `File` menu today: editor registers `File/*` + `File/Scenario/*` *(via `ScenarioMenuCommands`, takes `IEditorLogic`)*; CGF registers only the engine-default `Settings`. ⇒ the wall is `ScenarioMenuCommands` binding to the editor-only `IEditorLogic`.

## 3. ⭐⭐⭐ WHAT TO BUILD *(E1)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **Extract `IScenarioSession` + `EditorScenarioSession`** *(the scenario half of `EditorApplication`)* into **`Hrot.Editor.AiShared`** *(+ `MigrationAlertManager`, the `ScenariosRoot` constant)*. Members: `NewExercise()` · `LoadForLive(name)` · `OpenForEdit(name)` · `SaveCurrent()` · `SaveAs(name)` · `LoadedScenarioName` · `GetMigrationSidecars()` | ⚠⚠ **HN-037 lesson — measure the captures FIRST**; ⛔ do NOT drag the tool/view/mode half *(that is E3/E4)* |
| ⭐⭐ **②** | **Editor delegates** its scenario members to the shared session | ⛔⛔ **editor byte-identical** *(the gate)* — the editor's existing `File/Scenario/*` behaviour unchanged |
| ⭐⭐ **③** | **CGF instantiates** `EditorScenarioSession` over **CGF's own world + orchestration bus** | ⭐ same class, CGF's world — the parameterised-world finding makes this free |
| ⭐⭐⭐ **④** | **`ScenarioMenuCommands` takes `IScenarioSession`** and registers **DISTINCT File-menu items on BOTH hosts (R2)** — see §3a. ⛔ **No chameleon `New`/`Open`; no per-host "default" in the menu** | each item is a **logically distinct action**; all present where serviceable *(ruling 49)* |
| ⭐ **⑤** | **Conformance** — extend the `SUBSET-BY-DESIGN` **menu** verdict *(CE-045 `SubsetShape`)* to the new `File/*` items; a unit rail for `LoadForLive` vs `OpenForEdit` routing + the `NewExercise` confirm-branch | ⛔ NOT full-array identity |

### 3a. ⭐⭐⭐ THE DISTINCT MENU ITEMS *(R2 — no chameleons; both hosts, per serviceability)*
| menu path | action | routes to |
|---|---|---|
| `File/Live/New Exercise` | clear the running exercise + start fresh — **with a confirmation dialog** about finishing a running exercise | a cluster-wide clear/reset to the master |
| `File/Live/Load Scenario` | load a scenario and run it live | **`/scenario/load/live`** *(HN-029, confirmed-at-origin, ruling 59)* |
| `File/Edit/Open Scenario` | open a scenario for editing | **`/scenario/load/edit`** *(HN-029)* |
| `File/Save` | save the scenario open for editing | the editor's exact save via the session *(edit-mode)* |
| `File/Checkpoint/Take Checkpoint` | save the live running state | the existing **`TakeCheckpointIntent`** *(to the master)* |

⛔ **NOT in E1** *(they light up when their service is composed — ruling 49, the derived menu)*: `File/Edit/Open Asset` + `File/Edit/New Asset from Recipe` = **E2** *(the asset-picker/new-asset shell relocation, §6.1)*; `File/Checkpoint/Restore Checkpoint` = **Feature X**. ⭐ E1 establishes the `Live/`·`Edit/`·`Checkpoint/` submenu STRUCTURE; the later items slot in with **zero menu code**.

⛔⛔ **NO toolbar changes in E1 (R3).** Which distinct actions get a toolbar button per host/perspective is the **toolbar+menu customization system — its own AQ** *(gap map §5 FUTURE)*. The toolbar stays exactly as CE-037..045 shipped it.

## 4. ⭐⭐ CLASS DIAGRAM *(authoritative)*
```mermaid
classDiagram
    class IScenarioSession {
        <<NEW · Hrot.Editor.AiShared>>
        +NewExercise()
        +LoadForLive(name)
        +OpenForEdit(name)
        +SaveCurrent()
        +SaveAs(name)
        +LoadedScenarioName string
        +GetMigrationSidecars() IReadOnlyList
    }
    class EditorScenarioSession {
        <<NEW · Hrot.Editor.AiShared · impl>>
        +EditorScenarioSession(fileService, orchestrationBus, world, alerts, scenariosRoot)
    }
    class ScenarioFileService {
        <<EXISTS · Hrot.Presentation>>
    }
    class EditorApplication {
        <<MODIFIED · Hrot.Editor · scenario members delegate (byte-identical)>>
    }
    class CgfSubsystem {
        <<MODIFIED · constructs the session over CGF world>>
    }
    class ScenarioMenuCommands {
        <<MODIFIED · takes IScenarioSession; registers distinct Live/Edit/Checkpoint items>>
    }
    class TakeCheckpointIntent {
        <<EXISTS · Fdp.Toolkits · checkpoint SAVE>>
    }
    EditorScenarioSession ..|> IScenarioSession
    EditorScenarioSession ..> ScenarioFileService : save/new (load via TransitionStateIntent)
    EditorApplication ..> IScenarioSession : delegates
    CgfSubsystem ..> EditorScenarioSession : new(over CGF world)
    ScenarioMenuCommands ..> IScenarioSession : New/Load/Open/Save
    ScenarioMenuCommands ..> TakeCheckpointIntent : Take Checkpoint publishes
```

## 5. ⭐⭐ SEQUENCE DIAGRAM *(authoritative — the distinct items, both hosts)*
```mermaid
sequenceDiagram
    participant Host as Editor or CGF
    participant Session as EditorScenarioSession
    participant Bus as orchestration bus
    participant Master as ClusterMaster

    Note over Host: each host constructs the SAME session over its own world
    Host->>Session: new EditorScenarioSession(fileService, bus, world, ...)
    Note over Host: distinct File items bind to session methods
    Host->>Session: OpenForEdit(name) - from File/Edit/Open Scenario
    Session->>Bus: publish TransitionStateIntent edit
    Host->>Session: LoadForLive(name) - from File/Live/Load Scenario
    Session->>Bus: publish TransitionStateIntent live
    Bus->>Master: fan out to the roster
    Note over Master: CgfScenarioLoadHandler materialises the world, cluster-wide
    Host->>Session: SaveCurrent - from File/Save, edit-mode only
    Session->>Session: ScenarioFileService.SaveScenario(world, path)
```

## 6. ⭐ ACCEPTANCE / RAILS
- **editor byte-identical** — the editor's rendered `File` menu + `IEditorLogic` scenario behaviour unchanged after the extraction + the item rename to the distinct structure *(a `RenderGlobalMenu`/registry-dump diff before/after; ⚠ if the editor's existing item labels change, that IS a visible change — argue it in the report or keep the editor's labels and add the `Live/Edit/Checkpoint` grouping only where new)*.
- **CGF File menu** dumps the E1 items *(Live/New Exercise · Live/Load Scenario · Edit/Open Scenario · Save · Checkpoint/Take Checkpoint)*; the `SUBSET-BY-DESIGN` verdict holds.
- **routing rails** — `LoadForLive`/`OpenForEdit` publish the live/edit `TransitionStateIntent`; `NewExercise` confirms before clearing a running exercise.
- ⛔ **no toolbar entry changed** *(R3 — a rail asserting the toolbar set is unchanged from CE-037..045 is a cheap guard)*.
- affected-project builds; conformance suite named + run *(T3, background)*; reds proven pre-existing by `git diff`.

## 7. ⭐ LANE & GATES
⭐ **UI/CGF lane** *(owns `CgfSubsystem.cs`, `EditorSubsystem.cs`/`EditorApplication.cs`, `ScenarioMenuCommands.cs`; the extraction moves types into `Hrot.Editor.AiShared`)*. ⚠ **rule-4 re-pull** — hot files. Build the AFFECTED projects *(`Hrot.Editor.AiShared` · `Hrot.Editor` · `Hrot.CGF` · `Hrot.Presentation` · `Hrot.SystemTests`)*, ⛔ never the whole solution in the fix loop. Gates per the rule-8 contract; conformance suite **T3 — background it**. Obligation ⑤: fold any as-built deviation into THIS doc.

## 8. ⛔ EXPLICITLY OUT
- **The ASSET menu items** *(`Edit/Open Asset`, `Edit/New Asset from Recipe`)* — **Axis-C E2 / menu-report §6.1** *(relocate the asset-picker/new-asset shell to AiShared)*. They slot into E1's `Edit/` submenu with zero menu code when E2 lands.
- **Checkpoint RESTORE** *(+ its picker)* — Feature X *(the save exists, restore does not — dead enum slots, no `.fdp` read-back)*. Deferred by the user.
- **Toolbar-button / main-menu-item SELECTION per host/perspective** — the **toolbar+menu customization system**, its own AQ *(R3, gap map §5 FUTURE)*. ⛔ E1 changes no toolbar.
- **Capability-gating config layer** — unify fully-featured first *(R1)*.
- **Save-As modal browser** — rides E2's `PickerRegistry` composition.
