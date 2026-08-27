<!--STATUS
state: LIVE
build-state: READY-TO-BUILD
updated: 2026-08-27
current-answer: the whole file. Axis-C E5 — the SCENARIO-PERSPECTIVE WINDOWS (spawner, mission, orbat,
  map config, preview, zone) on CGF. §2 is the inventory, §5 the classDiagram, §6 the sequenceDiagram,
  §7 the item breakdown, §8 the two decisions that needed a call and got one.
stale-below: nothing.
known-rot: nothing.
known-conflict: none. ⚠ PROGRAMME_Cgf_Equals_Editor_Gap_Map.md §2c.2 lists E5 as "the thin-host
  bootstrap divergence, NOT a gap". 📐 That framing is INCOMPLETE, not wrong — see §1: it enumerated
  CAPABILITIES (scenario/asset/tool/inspector) and never enumerated WINDOWS, so the per-host window
  wrappers and their adapters fell between the rows. This design adds that row; the bootstrap
  divergence it names stays exactly as ruled.
-->
# ⭐⭐⭐ AXIS-C **E5** — the Scenario-perspective WINDOWS on CGF

> 🔒 **User, `2026-08-27`, `--mode all`:** *"the editor has many windows in its Scenario perspective like
> mission editor, orbat, entity placement, entity spawner, cgf offers just Entity inspector, Event
> Browser, architecture diagnostic, System profiler."*

## 1. ⚠⚠ THE PREMISE THE GAP MAP MISSED — **E1–E4 shared CAPABILITIES, not WINDOWS**

📐 **Measured, and it explains why four consecutive green slices left this visible:** `--mode all` expands
to `orchestrator,simhost,ig,excon,cgf` — ⛔ **no editor** *(`HrotRunnerConfiguration.Validate`, which also
REJECTS `editor` together with `cgf`)*. ⇒ on that path **CGF *is* the editor**, and every window the
editor registers and CGF does not is a missing feature, not a cosmetic gap.

| what E1–E4 shared | what nobody enumerated |
|---|---|
| the scenario **session** · the asset **picker/create** shell · **tool/selection/camera/rename** · the **inspector** interceptor | ⛔⛔ the **seven `ManagedWindow` wrappers** in `Hrot.Editor/Windows/EditorWindows.cs` and the **five adapters** they need |

⭐⭐ **And the shape is the seam law again, at 2× not 1×:** the PANELS and the FACADE INTERFACES are
**already shared** in `Hrot.Presentation`; what is duplicated is the *wrapper* — `EditorWindows.cs` **and**
`ExConWindows.cs` wrap the same panels with the same body, differing only in id/title/perspective/colour.

## 2. ⭐⭐ INVENTORY — the enumeration, with the queries

```
grep -rn "class Editor[A-Za-z]*Window" Hrot/Subsystems/Hrot.Editor/Windows/EditorWindows.cs   # 7 wrappers
grep -rn "class ExCon[A-Za-z]*Window"  Hrot/Subsystems/Hrot.ExCon/Windows/ExConWindows.cs      # 7 wrappers
grep -rln ": *I{SpawnController|MissionEditorService|MapPickService|OrbatController|MapConfigController}"
grep -c  "IEditorLogic\|_logic\|EditorApplication" Hrot/Subsystems/Hrot.Editor/Adapters/*.cs
grep -n  "ProjectReference" <each csproj>                                                      # the cycle check
```

### 2.1 The panels and facades — ✅ **already shared, in `Hrot.Presentation`**

| kind | types | home |
|---|---|---|
| panels | `MissionPanel` · `SpawnerPanel` · `SharedOrbatPanel` | `Hrot.Presentation/Panels/` |
| facades | `ISpawnController` · `IMissionEditorService` · `IMapPickService` · `IOrbatController` · `IMapConfigController` | `Hrot.Presentation/Facades/` |
| ⭐ a shared map-pick **implementation** | `CanvasMapPickAdapter : IMapPickService` | `Hrot.Presentation/Facades/` — ⭐⭐ **CGF already constructs it** *(`CgfSubsystem` `RegisterWindows`)* |
| ⛔ editor-only panel | `EditorToolbarPanel` *(takes `IEditorLogic`)* | `Hrot.Editor/UI/` |

### 2.2 The five adapters — ⭐ **four are host-agnostic ALREADY; they are in the wrong assembly**

📐 `grep -c "IEditorLogic|_logic|EditorApplication"`:

| adapter | LOC | editor-facade hits | blocker to moving into `Hrot.Presentation` |
|---|--:|--:|---|
| `EditorSpawnAdapter` | 271 | **0** | ⭐ `using Hrot.IG.Components` — 📐 **nothing from it is referenced. A stale using.** ⇒ delete it and the move is clean |
| `EditorMissionService` | 217 | **0** | ⭐ none |
| `EditorMapConfigAdapter` | 71 | **0** | ⭐ none |
| `EditorOrbatAdapter` | 217 | **4** | ⭐⭐ 📐 the hits are **ONE call**: `_logic.ActivateTool(EditorTool.Select)`. ⭐ `E3` already moved `EditorTool` to `Hrot.Common` and built the shared `ToolActivationDrainSystem` ⇒ **publish `ActivateEditorToolEvent`** and `IEditorLogic` drops out entirely |
| `EditorMapPickAdapter` | 134 | **0** | ⛔⛔ **DO NOT MOVE — it DUPLICATES `CanvasMapPickAdapter`** *(§8 D2)* |
| `EditorZoneAdapter` | — | — | ⚠ uses `Hrot.Editor.Gizmos.LocationPickerGizmo` ⇒ needs the same gizmo relocation `E3` did for `EntityRotatorGizmo`. **Deferred to E5b** |

### 2.3 ⛔ The cycle constraint — **measured, and it rules out one obvious home**

```
Hrot.IG  →  Hrot.Presentation          ⇒  ⛔ Hrot.Presentation may NOT reference Hrot.IG
Hrot.Editor → Hrot.CGF                 ⇒  ⛔ CGF may NOT reference Hrot.Editor  (the whole slice's reason)
Hrot.Editor.AiShared → Hrot.Presentation, Hrot.Core   ⇒  ✅ reachable from CGF
```

⇒ ⭐ **`Hrot.Presentation` is the home** for the four clean adapters and the shared wrappers: it already
holds the panels, the facades and `CanvasMapPickAdapter`, and needs **no new project reference**.
⛔ The only thing that wanted `Hrot.IG` was `HrotEntityFilterFactory`, reached solely by the adapter §8 D2
does not move.

## 3. ⭐ WHAT CGF ALREADY HAS for the adapters' arguments

| adapter needs | on CGF |
|---|---|
| `FdpEventBus` | ✅ `_context.World.Bus` / `_context.EventBus` |
| `EntityRepository` | ✅ `_context.World` |
| `BehaviorRegistry` | ✅ `_behaviorRegistry` |
| `ITkbDatabase` | ✅ used by `BuildAssetCatalog` |
| `GlobalGizmoManager` | ✅ `_cgfGizmoManager` |
| `MapCanvas` / `MapViewConfig` | ✅ `_canvas` / its view config |
| `IMapPickService` | ✅⭐ `CanvasMapPickAdapter`, **already constructed** |
| `ScenarioEntityCreationRequestSource` | ⚠ **the one genuine unknown** — resolve at build time; `null` is a legal argument *(optional)*, and its absence must be STATED, not defaulted silently |

## 4. ⛔ NOT IN E5

| out | why |
|---|---|
| `EditorToolbarWindow` / `EditorToolbarPanel` *(the user's "entity placement" palette)* | 📐 takes `IEditorLogic` directly and is a **tool palette**, not a panel over a facade ⇒ its own item once §8 D1's tool seam is exercised |
| `EditorPreviewWindow` · `EditorZoneEditorWindow` | ⚠ `IPreviewController` is the editor's **planning-vs-running** state, which CGF does not have *(a ruled divergence, recorded in `CgfSubsystem`'s `isSimUpSignal` note)*; the zone adapter needs the gizmo move ⇒ **E5b** |
| deleting `ExConWindows.cs`'s wrappers | ⭐ desirable *(ruling 9)* and **separable** — ExCon is the BACKEND lane's file. ⚠ E5 makes the shared set; adopting it on ExCon is a follow-up so this slice does not straddle two lanes |
| reconciling `EditorMapPickAdapter` with `CanvasMapPickAdapter` | §8 D2 — a **decision with regression risk to the editor's map picking**; filed, not smuggled |

## 5. ⭐⭐⭐ CLASS DIAGRAM — existing boxes marked, so a duplicate is visible on the page

```mermaid
classDiagram
    class ISpawnController {
        <<interface>>
    }
    class IMissionEditorService {
        <<interface>>
    }
    class IOrbatController {
        <<interface>>
    }
    class IMapConfigController {
        <<interface>>
    }
    class IMapPickService {
        <<interface>>
    }
    note for IMapPickService "EXISTS Hrot.Presentation/Facades"

    class SpawnerPanel
    class MissionPanel
    class SharedOrbatPanel
    note for SpawnerPanel "EXISTS Hrot.Presentation/Panels"

    class CanvasMapPickAdapter
    note for CanvasMapPickAdapter "EXISTS - already built on CGF"

    class EditorMapPickAdapter
    note for EditorMapPickAdapter "EXISTS Hrot.Editor - DUPLICATE of CanvasMapPickAdapter, see D2"

    class SharedPanelWindows {
        +Spawner(panel, spawn, id, perspective, color) ManagedWindow
        +Mission(panel, svc, pick, id, perspective, color) ManagedWindow
        +Orbat(panel, orbat, id, perspective, color) ManagedWindow
        +MapConfig(panel, cfg, id, perspective, color) ManagedWindow
    }
    note for SharedPanelWindows "NEW Hrot.Presentation/Windows - replaces the duplicated wrappers"

    class ScenarioSpawnAdapter
    class ScenarioMissionService
    class ScenarioOrbatAdapter
    class ScenarioMapConfigAdapter
    note for ScenarioSpawnAdapter "MOVED from Hrot.Editor/Adapters - body unchanged"

    ISpawnController <|.. ScenarioSpawnAdapter
    IMissionEditorService <|.. ScenarioMissionService
    IOrbatController <|.. ScenarioOrbatAdapter
    IMapConfigController <|.. ScenarioMapConfigAdapter
    IMapPickService <|.. CanvasMapPickAdapter
    IMapPickService <|.. EditorMapPickAdapter

    SharedPanelWindows ..> SpawnerPanel
    SharedPanelWindows ..> MissionPanel
    SharedPanelWindows ..> SharedOrbatPanel
    SharedPanelWindows ..> ISpawnController
    SharedPanelWindows ..> IMissionEditorService
    SharedPanelWindows ..> IOrbatController
    SharedPanelWindows ..> IMapPickService

    class EditorSubsystem
    class CgfSubsystem
    EditorSubsystem ..> SharedPanelWindows
    CgfSubsystem ..> SharedPanelWindows
    ScenarioOrbatAdapter ..> ActivateEditorToolEvent
    note for ScenarioOrbatAdapter "IEditorLogic.ActivateTool replaced by the E3 event"
```

## 6. ⭐⭐ SEQUENCE — how CGF gets a spawner window, and where the old wall stood

```mermaid
sequenceDiagram
    participant Cgf as CgfSubsystem.RegisterWindows
    participant Adapt as ScenarioSpawnAdapter
    participant Win as SharedPanelWindows
    participant WM as WindowManager
    participant Panel as SpawnerPanel

    Note over Cgf: before E5 the wrapper lived in Hrot.Editor, which CGF cannot reference
    Cgf->>Adapt: new(bus, jsonCompiler, tkbDb, requestSource, gizmoManager)
    Cgf->>Win: Spawner(panel, adapter, "cgf_spawner", "Scenario", TitleBarColor)
    Win-->>Cgf: ManagedWindow
    Cgf->>WM: RegisterWindow(window)
    WM->>Win: DrawClientArea per frame
    Win->>Panel: BuildViewModel then DrawContent(spawn)
    Panel->>Adapt: Spawn requested
    Note over Adapt: ruling 22 unchanged - an unowned write still travels as a request
```

## 7. ⭐ ITEMS

| # | item | risk |
|---|---|---|
| **①** | `SharedPanelWindows` in `Hrot.Presentation/Windows/` — the four wrappers, id/title/perspective/colour as **arguments**. ⚠ Keep `PanelSnapshot.DeclareInstrumented`/`Register` — the `ui-probe` rails read those | low |
| **②** | `git mv` the four clean adapters → `Hrot.Presentation/Adapters/`, renamed `Scenario*`. ⛔ Bodies unchanged except: drop `EditorSpawnAdapter`'s stale `using Hrot.IG.Components`, and swap `EditorOrbatAdapter`'s one `_logic.ActivateTool` for the `E3` event | ⚠ compile cascade — build the AFFECTED project per §Gates, ⛔ never the solution |
| **③** | `EditorSubsystem` registers through `SharedPanelWindows` — ⭐ **same ids, same titles**, so layout files and every id-keyed rail still resolve | ⚠ **the byte-identical gate**: the editor's window set must not change |
| **④** | `CgfSubsystem` constructs the four adapters + registers the four windows in `"Scenario"`, passing its **existing** `CanvasMapPickAdapter` for mission | low |
| **⑤** | Rails: the two hosts' `Scenario` window sets **agree on the panel KINDS** *(⛔ not on ids — those are per host)*; the adapters are constructed on both; an inverse-edit red-proof per item | ⭐ the layer §1 shows nothing asserted |

## 8. ⭐⭐⭐ TWO DECISIONS

| # | decision | call |
|---|---|---|
| **D1** | `EditorOrbatAdapter`'s `_logic.ActivateTool(EditorTool.Select)` | ⭐⭐ **Publish `ActivateEditorToolEvent`.** `E3` built exactly this seam *(`ToolActivationDrainSystem`, `PostSimulation`)* and both hosts register it. ⛔ Adding an `Action?` tool delegate instead would be a second tool-activation path — ruling 9 |
| **D2** | `EditorMapPickAdapter` **vs** the already-shared `CanvasMapPickAdapter` — both `: IMapPickService`, both ~130 LOC, **same three members** | ⛔⛔ **NOT reconciled in E5, and that is deliberate, not an oversight.** ⚠ The editor's version drives `LocationPickerGizmo` and a real `HrotEntityFilterFactory`; `CanvasMapPickAdapter` carries a private `MatchAllFilterFactory`. ⇒ ⭐ they may be **the same concept at two capability levels**, and collapsing them the wrong way silently degrades the editor's map picking — the user's core workflow. ⭐⭐ E5 needs neither: **CGF passes the shared one it already builds.** 📌 Filed as `CE-063`, with the capability comparison as its first item |

## 9. ⭐ GATES

| gate | command |
|---|---|
| build, **affected project only** | `dotnet build Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj --no-restore` *(📐 8 s vs 115 s for the solution)* |
| T0 per edit | `bash scripts/quick-check.sh Hrot/Subsystems/Hrot.Editor.Tests <filter>` |
| T1 before push | `dotnet test Hrot/Subsystems/Hrot.Editor.Tests --no-build` |
| the byte-identical gate | the editor's `Scenario` window **id set** is unchanged — assert it, do not eyeball it |
| T3 async | `bash scripts/run-system-tests.sh --no-build` — ⛔ backgrounded, never a foreground blocker |
