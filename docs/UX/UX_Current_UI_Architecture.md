# Current UI architecture — what is shared, what is forked, and why

> **Assessment, 2026-08-10.** Five parallel code scans across `Hrot.Editor`, `Hrot.IG`, `Hrot.ExCon`,
> `Hrot.CGF`, `Hrot.SimHost`, `Hrot.Presentation`, `Hrot.UI.Common` and `Fdp.Presentation`.
> Every claim is `file:line`-cited. ⚠ Two scan claims were **wrong** and are corrected here — see
> [Corrections](#corrections-to-the-scans).
>
> Answers the user's question of 2026-08-10: *"how much is the UI shared across modes, where are we
> sharing too much, where too little, and how is customization possible?"*

## The finding, in one line

**Sharing in this codebase is not governed — it is incidental.** Every surface that exposes a
**contribution seam** is shared successfully across modes. Every surface that does not has been
**forked**. Across five scans there is **no counter-example**.

⇒ The question is not *"share or duplicate?"* It is *"does this surface have a seam?"* That is the unit
of analysis, and it is what the target architecture must make mandatory rather than optional.

## 1. The layers today

| Project | LOC | Classes | Role | Referenced by |
|---|--:|--:|---|---|
| `Fdp.Presentation` | 15,648 | 100 | Genuine toolkit — `WindowManager`, inspectors, event browser, `Vis2D` map machinery | all 7 window-registering subsystems |
| `Hrot.Presentation` | 8,052 | 60 | HROT shared panels — Mission, Spawner, Config, SharedOrbat, time controls, `ScenarioEditor/` gizmos | Editor, IG, ExCon, CGF, SimHost |
| `Hrot.UI.Common` | 1,171 | 20 | 🔴 **DEAD** — near-1:1 fork of the above | **nothing.** In no `.csproj`, in no `.sln` |
| per-subsystem UI | — | — | Editor / IG / ExCon / CGF / SimHost each carry their own | themselves |

**Composition:** `LocalWindowController.cs:55-57` loops the composed subsystems calling
`RegisterWindows(wm)` into **one** `WindowManager`. `--mode` selects which subsystems compose, and that
is the *only* granularity of UI control that exists today.

⚠ **`ios` is a legacy alias for `excon`** (`HrotRunnerConfiguration.cs:85`) — five real UI modes, not six.
And **ExCon has no map at all** (`ExConSubsystem.cs:44`: *"no 3-D world visuals; all UI is ImGui"*).

## 2. ⭐ The seam inventory — the core table

| Surface | Seam | State | Consequence |
|---|---|:--:|---|
| Entity context menu | `IEntityContextMenuHandler` + `RegisterContextMenuHandler` | ✅ | Editor 5 handlers, CGF 1, SimHost 1, StrideMock 0, ExCon opts out. **Same panel, different menu per mode — already works** |
| Map draw layers | `MapCanvas.AddLayer(IMapLayer)` | ✅ | SimHost adds road+trajectory layers, Editor adds a grid layer, nobody else has either |
| Entity inspector | `ExtractionService`, `Serializer`, `Reflector`, `ChainToMap`, `OnEntitySelected` | ✅ | Richest seam in the repo; reused by 4 modes |
| Time / transport | `ITimeTransportFacade` | ✅ | Editor-local and cluster-bus impls interchangeable by design |
| Diagnostics window | injected service factory | ✅ | 5 modes, different data domains |
| Toolbar items | `RegisterEntry(…, perspective:)` | ✅ | Per-perspective filtering **exists here** |
| Graph-canvas node menus | `INodeContextMenuProvider` + 2 siblings | ✅ | BTree/HSM/Blueprint each supply different items |
| Map symbology | `IEntityShapeLibrary` on `DebugGizmoLayer` | ⚠ | Seam exists, **no host uses it** — all pass `DefaultEntityShapeLibrary` |
| **Main menu** | — | ❌ | Flat union of whatever composes; no perspective filter, no ordering, last-write-wins |
| **ORBAT rows** | — | ❌ | One hardcoded item (`Disembark`) ⇒ **ExCon forked a 434-line replacement** |
| **Map camera** | — | ❌ | 4 hand-coded literals, all stale |
| **Spawn UI** | catalog injection only | ❌ | **4 independent implementations** |
| **Selection** | — | ❌ | **3 incompatible representations** |

## 3. Sharing too little — duplication

| # | What | Evidence |
|--:|---|---|
| 1 | **Spawn UI ×4** — `SpawnerPanel` (250L, Editor+ExCon), `MiniExConPanel`+state (394L, IG), `SimHostSpawnPanel` (62L), plus an inline combo in ExCon's ORBAT (`OrbatPanel.cs:332-351`) | no shared code between them |
| 2 | **ORBAT ×3** — `SharedOrbatPanel` (183L), ExCon's `OrbatPanel` (434L), `EditorOrbatPanel` (27L stub, dead) | ExCon reimplements the Editor's job |
| 3 | **IG runs two entity inspectors at once** — local 78L (`IgApplication.cs:673`) *and* the shared 593L (`:416`) | both instantiated in one mode |
| 4 | **Gizmo main-menu-bar block copy-pasted ×4** — Editor, IG, SimHost, ReplayBrowser each open their own `BeginMainMenuBar`, bypassing the `Render(gizmoMenuItems,…)` overload built for it | `EditorSubsystem.cs:1911-1926`; `IgApplication.cs:1259-1279`; `SimHostVisualization.cs:369-388`; `ReplayBrowserSubsystem.cs:414-434` |
| 5 | **Two un-merged map context-menu pipelines** — gizmo-projected vs ExCon↔IG networked JSON | `ContextMenuProjectorGizmo.cs` vs `Hrot.IG/Systems/ContextMenuSystem.cs` |
| 6 | **`PanelConstants` copied** — ExCon re-declares all 10 shared constants verbatim rather than referencing them | `Hrot.ExCon/Panels/PanelConstants.cs:15-122` vs `Hrot.Presentation/Panels/PanelConstants.cs:11-48` |
| 7 | **Map camera setup ×4, all stale** — IG `1600×900` consts, CGF and SimHost hardcode `1280×720`, Editor/ReplayBrowser never set `Offset`. Real default window is **2200×1200** | `IgApplication.cs:617`; `CgfSubsystem.cs:577`; `SimHostVisualization.cs:226`; `RunnerOptions.cs:18,21` |
| 8 | **`MapLayerBits` hand-synced** — constants re-declared with a comment admitting they *"must match `Hrot.IG.Systems.MapLayerRegistry` exactly"* | `Hrot.Core/Config/MapLayerBits.cs:1-25` |

## 4. Sharing too much — rigidity without a seam

| What | Why it hurts |
|---|---|
| `SharedOrbatPanel` — parameterless ctor, **zero** extension point, one hardcoded `Disembark` item | ⭐ **This is why ExCon forked.** A host needing Select/Center/Delete/Edit Route/Abort had no way to add them |
| `ConfigPanel`, `MissionPanel` — take a node id used **only in log strings** | Look parameterised, are not. Any real divergence must break the shared class |
| Map is drawn **full-OS-window** by every host; `GridMapLayer` uses raw `GetScreenWidth/Height` | No mode can inset or window the map. `DockspaceLayout.CentralSize` exists and **no camera code reads it** |
| Menu is the union of composed subsystems, un-filterable | A host cannot present a curated menu without editing subsystem code |

## 5. Dead weight inflating the apparent shared surface

| Item | Size | Status |
|---|--:|---|
| `Hrot.UI.Common` | 1,171 LOC | 🔴 In **no** `.csproj` and **no** `.sln`. Never builds |
| ExCon `InspectorPanel` + `DataMonitorPanel` | 435 L | `[Obsolete]`, zero non-test instantiations |
| `EditorOrbatPanel` + `EditorOrbatWindow` | 27 L + wrapper | Constructed at `EditorSubsystem.cs:1559`, **never registered** |
| `EntityPropertyInspector` (Editor) | 48 L | Never instantiated |
| `WorkspaceMenuBuilder` | 126 L | Model built, **no renderer** |

> ### 🔴 The namespace lies — the trap this creates
>
> Panels that actually compile live in `Hrot.Presentation/Panels/` but declare
> **`namespace Hrot.UI.Common.Panels`**. Navigating by namespace lands you in the dead project. The
> copies have **drifted** (`SharedOrbatPanel` differs by a `vehicleId` local and reworded docs).
>
> ⇒ *"Fix the shared ORBAT panel"* has even odds of editing a file that compiles into nothing.
> **Delete `Hrot.UI.Common` before any shared-panel work starts.**

## 5b. How perspective switching actually works

*Added 2026-08-10, answering: how does the original cluster-role meaning coexist with the editor's
internal Scenario / BTree / HSM / Blueprint layouts?*

**There are two independent mechanisms keyed by the same string.**

### Mechanism 1 — a window visibility filter (pure UI, general)

`ManagedWindow.Render(currentPerspective, atlas)` (`ManagedWindow.cs:154-165`):

```csharp
var isVisible = Scope == WindowScope.Global      // always visible
             || _isPinned                         // user pinned it across perspectives
             || OwningPerspective == currentPerspective;
if (!isVisible) return;
```

That is the whole concept: **a perspective is a tag, and switching filters the registered windows by
it.** `WindowManager` knows nothing about subsystems, modes or the cluster. `GetPerspectives()` simply
returns the distinct `OwningPerspective` values of the registered `PerspectiveBound` windows
(`WindowManager.cs:178-186`), so **the perspective list is emergent from what got registered** — never
declared.

### Mechanism 2 — a map-ownership handover (cluster-specific side effect)

`WindowManager.OnPerspectiveChanged` → `LocalWindowController.cs:61-65` enqueues a
`TogglePerspectiveEvent` → drained every frame by `PerspectiveUpdateSubsystem.Update`
(`PerspectiveUpdateSubsystem.cs:28`, deliberately the **first** subsystem so it runs before any other)
→ `PerspectiveCoordinatorSystem.ProcessPendingEvents` (`:69-86`):

```csharp
if (_perspectiveToSubsystemName.TryGetValue(evt.NewPerspective, out var subsystemName))
{
    outgoing.GizmoController?.RemoveListener();   // hand off gizmo input
    incoming.GizmoController?.AddListener();
    _orchestrator.SwitchMapOwner(subsystemName);
}
_currentPerspective = evt.NewPerspective;         // ← outside the if, always runs
```

`SwitchMapOwner` (`SubsystemOrchestrator.cs:164-179`) swaps `_activeMapOwner` and **copies the camera
view across** so the operator does not jump. It matters because only the owner draws the world:

```csharp
private bool IsMapOwner(ISubsystem s)
    => !(s is IMapCameraProvider)      // non-map subsystems always draw
       || s == _activeMapOwner;        // map-capable ones only when they own it
```

### ⭐ The bridge is a 5-entry hardcoded allow-list

`Program.cs:244-251`:

| In the map | Not in the map |
|---|---|
| `IG`, `SimHost`, `ExCon`, `CGF`, `StrideMock` | `Editor`, `BTree`, `HSM`, `Blueprint`, `ReplayBrowser` |

⇒ **Cluster-role perspectives** fire *both* mechanisms — filter the windows **and** hand over the map,
the gizmo listener and the camera.
⇒ **The editor's internal perspectives are absent from the table, so the `if` falls through.** Only
mechanism 1 runs. `_currentPerspective` still updates, because that assignment sits outside the branch.

**So the editor's use is not a second concept bolted on — it is mechanism 1 alone.** The cluster use is
*mechanism 1 plus a side effect*. One is a superset of the other, which is why they do not fight. The
coordinator's own doc comment states the design intent: *"Unknown perspective names are silently ignored
by the orchestrator."*

In `--mode editor` the map owner is fixed for the process: `Initialize` sets
`_activeMapOwner = _subsystems.FirstOrDefault(s => s is IMapCameraProvider)`
(`SubsystemOrchestrator.cs:78`) — the `EditorSubsystem` — and no switch ever fires afterwards. Toggling
Scenario → Blueprint therefore cannot disturb map ownership. Correct behaviour, reached by a lookup miss.

### 🔑 …and the two vocabularies never actually meet

**`editor` is validated as standalone** — it cannot be combined with `ig`, `excon`, `orchestrator` or
`cgf` (`HrotRunnerConfiguration.cs:127-134`), and `replaybrowser` likewise (`:136-141`). So a process
**never** contains both cluster-role perspectives and editor-internal ones.

⇒ The coexistence is safe **because of a config constraint in a different file**, not because the
mechanism distinguishes the two kinds. Nothing in `WindowManager` or the coordinator knows there are two
kinds at all.

### 🔴 Where the ambiguity does bite — the two places that speak the wrong vocabulary

Both live in `LocalWindowController.OpenLocalWindow()`, and both use **subsystem names** where the
persisted value is a **perspective id**:

| Line | Code | Problem |
|---|---|---|
| `:83` | `_subsystems.Any(s => s.Name == persisted)` | Restore validation. `BTree`/`HSM`/`Blueprint` are not subsystem names ⇒ **silently discarded**, back to the default |
| `:81-82` | `defaultPersp = _subsystems.Skip(1).FirstOrDefault()?.Name` | Default pick. A *subsystem name* used as a *perspective name* — works only because cluster subsystems name their perspective after themselves |

⚠ **`EditorSubsystem.Name == "Editor"` is load-bearing three times over** — it is the mode token, the
subsystem name, **and** the main perspective id (`:172`, `:3446`). The display name is already decoupled
(`RegisterPerspectiveLabel("Editor", "Scenario")`, `:3449`), but renaming the **id** to `Scenario` would
break the restore check, the default-perspective pick, and the `isScenarioContext` gate at once.

> ### ⚠ Scope corrected 2026-08-10 — the user reports no loss, and both can be true
>
> *"In editor mode I did not experience any perspective settings being lost (layout, windows opened); it
> is remembered and correctly switched on perspective change."*
>
> **Each thing in that sentence is restored by a different mechanism, and none of them is this one:**
>
> | What is remembered | By what | Affected? |
> |---|---|:--:|
> | Docking layout | `%LocalAppData%\HROT\imgui.ini` | ❌ |
> | Which windows are open / pinned, UI scale | the `Windows` dict of `fdp_windows.json` — applied **regardless** of the perspective check | ❌ |
> | Runtime perspective switching | mechanism 1, a pure window filter | ❌ |
> | **Which perspective is active at startup** | `LocalWindowController.cs:83-84` | ✅ **only this** |
>
> ⇒ **It shows only if you quit from a graph perspective.** Quitting from Scenario restores Scenario,
> because `"Editor"` *is* a subsystem name and passes the check — so normal use never reveals it.
>
> **Verified there is no second restore path:** `LocalWindowController.cs:84` is the only startup
> `SwitchPerspective` call in the repo, and nothing reopens documents at launch (`AiDocumentManager`
> switches perspective on document *activation*, not at startup).
>
> ⚠ **Severity downgraded** 🔴 → minor: you lose your *place*, nothing else. The earlier wording
> *"silently drops your saved perspective"* overstated it.
>
> **One-step test:** switch to Blueprint → quit normally → relaunch. Blueprint (claim wrong) or Scenario
> (claim right)?

⇒ **The fix for the seam work is still one line of vocabulary**: validate the restored perspective against
`GetPerspectives()` — the registry that already exists — instead of against the subsystem list. That is
also the [Q25-F-ii](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-ii-perspective-restore) `G2`
argument: a shell that owns an explicit perspective set can validate against *that set*.

## 6. Selection — one shared mechanism, two hosts outside it

> ⚠ **CORRECTED 2026-08-10.** An earlier revision of this section said selection was *"fragmented three
> ways"* and put SimHost outside the shared mechanism. **That overstated it** — SimHost runs the same
> `SelectionInteractionSystem` as the Editor and IG. Logged as [Corrections row 8](UX_Tasks_Detail.md#corrections).

| Host | Mechanism | Verdict |
|---|---|---|
| Editor · SimHost · IG | **One shared class** — `SelectionInteractionSystem` mutating the ECS `SelectionState` component (`EditorSubsystem.cs:1287`, `SimHostVisualization.cs:250`, `IgApplication.cs:767`) | ✅ genuinely shared, hit-testing included |
| CGF | **None** — no `SelectionInteractionSystem`; selection is a manual *"Select entity"* context-menu item (`CgfSubsystem.cs:593-597`) | ❌ outside the mechanism |
| ExCon | entity-id list over the wire; its ORBAT menu captures the clicked row directly | ❌ no ECS at all (and no map) |

**The per-host mirrors are not competing mechanisms.** `DefaultSelectionState` (Editor) and
`SimHostSelectionManager` (a `HashSet<Entity>` synced from `_selectionSystem.OnSelectionChanged`,
`SimHostVisualization.cs:253-260`) are read-side caches for local UI panels, layered *on top of* the
shared ECS selection.

⭐ **And the seam is already there:** `SelectionInteractionSystem`'s optional `RubberBandState` ctor
parameter. The Editor passes one and gets a visible rubber-band rectangle; SimHost and IG use the 2-arg
overload, so box-select logic runs identically with no visual. **That is exactly the shape UXR-80 asks
for** — one implementation, an opt-in difference — and it already works.

⇒ The remaining gap is narrower than previously stated: **CGF and ExCon sit outside**, not "three
competing models".

## 6b. Map tools and the Editor ↔ SimHost relationship

*Added 2026-08-10, answering: how do Editor and SimHost share map tooling, and how customizable is it?*

### The relationship, corrected

**Editor ≈ SimHost ∪ IG, plus authoring extras.** It is not "the Editor's real sibling is IG" — an
earlier framing of mine that was **directionally right but overstated** (logged as
[Corrections row 9](UX_Tasks_Detail.md#corrections)). The truth splits by layer:

| Layer | Editor | SimHost | IG |
|---|:--:|:--:|:--:|
| **Interaction core** — `SelectionInteractionSystem`, `EntityDragGizmoDefinition`, `DataDrivenGizmoSystem`, `GlobalGizmoManager`, `StatelessGizmoSystem`, `DebugGizmoLayer` | ✅ | ✅ | ✅ |
| **Authoring gizmos** — Mission / Route / Vertex / Waypoint / Placement / Label | ✅ | ❌ | ✅ |
| **Domain layers** | grid | road + trajectory | — |

The Editor explicitly imports **SimHost's whole gizmo registrar** (`EditorSubsystem.cs:1097-1098`)
alongside IG's, so it composes both worlds. The **interaction core is shared three ways**; only the
*authoring* slice is Editor+IG, which is correct — SimHost runs an exercise and has no authoring
operator UI.

⇒ **Your premise holds for the core and correctly fails for the tools.** Grid vs road/trajectory layers
are a *legitimate* difference — a navigation aid vs live physics data. ⚠ **Not** prep-vs-live: the
Editor runs too (user, 2026-08-10), so it needs both kinds.

### 🔴 …but there is no tool abstraction at all

**No `ITool` / `IMapTool` interface, no tool registry, no "current tool" state.** "Which tool is active"
is inferred from *whichever gizmo instance happens to be registered* in `GlobalGizmoManager` /
`DataDrivenGizmoSystem`. Four uncoordinated activation idioms coexist:

| # | Idiom | Where |
|--:|---|---|
| 1 | **enum + switch** — `EditorTool` → `ActivateEditorToolEvent` → `DrainToolActivationEvents` switch → `new SomeGizmo(...)` | Editor toolbar |
| 2 | **int action-id dictionary** — `GlobalActionRegistry.Register(id, handler)` | all hosts — ⭐ **the one real seam** |
| 3 | **polled boolean setting** | IG's Measure (`MeasureToolGizmoAdapter.Update`) |
| 4 | **direct network-command dispatch** | IG placement (`MapCommandController`) |

⚠ **The Editor implements idioms 1 and 2 for the same tools, in the same class** — the
`DrainToolActivationEvents` switch *and* `actionRegistry.Register(...)` handlers at
`EditorSubsystem.cs:1152-1197` both activate Measure / PlaceEntity / EditOverlay / EditRoute.

**Per-host tool sets** — the answer to *"can each mode have its own tools?"* is *yes, but only by not
writing the wiring*:

| Tool | Editor | SimHost | IG | CGF |
|---|:--:|:--:|:--:|:--:|
| Select · Drag · Pan/zoom | ✅ | ✅ | ✅ | drag ❌, select via menu |
| Rotate | ✅ | ✅ | via DDS → SimHost | ✅ |
| Spawn · Area/Route authoring · Vertex edit · Waypoint edit · Measure | ✅ | ❌ | ✅ | ❌ |
| Rubber-band **visual** | ✅ | ❌ | ❌ | ❌ |

⇒ **SimHost has three tools: Select, Drag, Rotate.** Every other tool is absent because nobody wrote its
activation code — not because a seam let it opt out.

### What is and is not customizable today

| Concern | State |
|---|---|
| `GlobalActionRegistry` — per-host `Register(id, handler)`, unregistered ids silently unsupported | ✅ **a real seam**, but only for *discrete menu-triggered actions*; the handler still `new`s a gizmo by hand |
| Rendering-only gizmos — `[GizmoProjector]` + a Roslyn source generator auto-discovers them into per-namespace registrars | ✅ **declarative**, and the best mechanism in the area — **but not used for interactive tools** |
| Layer ordering — a host adding an `IMapLayer` above `DebugGizmoLayer` consumes clicks first | ✅ real |
| **Defining a new continuous-interaction tool** | ❌ hardcoded in each host's composition root |
| **A toolbar that shows which tool is active** | ❌ **not implementable today** — `EditorToolbarPanel` is stateless `ImGui.Button` calls and `IEditorLogic` exposes no current-tool property |

🔴 **`EditorTool.Select` is a no-op** — `case EditorTool.Select: break;` (`EditorSubsystem.cs:3814-3816`).
The toolbar's "Select" button therefore does nothing; selection works because the ECS gizmo path is
always on. **A dead control in the literal sense** ([UXR-X1](UX_Requirements.md#uxr-x1)).

### Context menus: the seam is used, the content is not shared

Editor registers **4** `IEntityContextMenuHandler`s, SimHost **1**, IG **1** — each a hand-written
lambda. *"Center on entity"* and *"Delete"* exist in all three and are **reimplemented three times with
different behaviour**: the Editor publishes `DestroyEntityCommand`; SimHost branches on `NetworkIdentity`,
falls back to `_repo.DestroyEntity`, and clears its selection and inspector state.

⇒ **Exactly [UXR-82](UX_Requirements.md#uxr-82).** The mechanism is shared; the *common items* are not.
Having a seam is not the same as using it well — and three copies of "Delete" is where behaviour drifts.

### Two more findings

- ⚠ **Two presentation gizmos may both match one entity.** `IgEntityPresentationGizmo` is keyed
  `[GizmoProjector(SimTransform, NetworkIdentity, CullingState)]`; `SimHostEntityPresentationGizmo` is
  keyed `[GizmoProjector(SimTransform, NetworkIdentity)]` — and **the Editor registers both**. An Editor
  entity carrying `CullingState` satisfies both projectors. *Whether the dispatcher then draws it twice
  is unverified* — confirm before treating it as a defect.
- `SelectionRenderSystem` + `SelectionRenderConstants` sit in the shared `ScenarioEditor/Rendering/`
  subtree and are **instantiated by no host** — only by a test. More dead weight in a "shared" home.
  `ScenarioEditorModule` is likewise a stub with empty `RegisterSystems`/`Tick`.

### ⇒ What UXR-81 actually costs

The map is **not** the ORBAT situation. The interaction core is genuinely shared three ways and the
domain differences are legitimate. What is missing is one level up: **a tool is not a thing in this
codebase**, so "one pool of tools, each mode drawing its own set" has no pool to draw from.
⚠ The Editor is **not** preparation-only — it runs too, and composes the largest tool set of any host.

**The Tier-1 shape:** introduce a tool descriptor (id, label, icon, activation, the gizmo it installs)
plus a per-host tool set, and route all four idioms through it. `GlobalActionRegistry` is the closest
existing pattern and `[GizmoProjector]` proves declarative registration already works here. That also
delivers the active-tool state the toolbar needs, and kills the duplicated activation paths.

## 7. What an ideal looks like — for the stated requirement

*Share whole panels; differ per mode in layout, main menu, map composition, and context menus.*

| # | Rule |
|--:|---|
| 1 | **One implementation per UI role.** No second ORBAT, no fourth spawner |
| 2 | **Every shared surface exposes a contribution seam.** Hosts *register items*; panels never hardcode a mode's item list |
| 3 | **Panel = content, window wrapper = per-mode framing.** ⭐ Already proven in-house: `EditorSpawnerWindow`/`ExConSpawnerWindow` wrap one `SpawnerPanel` |
| 4 | **The host declares a profile** — layout + menu set + map layer set + context-menu handlers — as *data*, not by recompiling panels |
| 5 | **One selection model** |
| 6 | **The map is a viewport, not the screen** — camera reads the effective (unoccluded) rect |

## 8. The gap — what it takes

**Tier 1 — mirror a pattern that already exists in-house.** Low risk, high yield.

| Work | Pattern to copy | Fixes |
|---|---|---|
| Perspective filter on `GlobalMenuRegistry.RegisterItem` | `MainToolbarManager.RegisterEntry(…, perspective:)` | per-mode main menu |
| Item-provider seam on `SharedOrbatPanel` | `IEntityContextMenuHandler` | lets ExCon's 434 L collapse into the shared panel |
| One camera-setup path reading the effective viewport | `MapCamera.Offset` already *is* the mechanism | 4 stale copies **and** the occlusion defect, together |
| Delete `Hrot.UI.Common` + the 4 dead panels | — | removes the namespace trap |

**Tier 2 — real design work.**

- Unify selection (3 → 1).
- Merge the two map context-menu pipelines.
- Collapse the 4 spawn UIs behind one seam.
- A host-declared **menu profile** layered over the union.

**Tier 3 — structural.** Layout-as-data, and the perspective model (10 `WindowManager` perspectives vs
the 5 cluster roles in `Program.cs:244-251` — the split that silently drops a restored
`BTree`/`HSM`/`Blueprint` perspective).

## 9. ⚠ What this means for the dedicated-exe question

**It largely dissolves it.** The premise of a new shell was that a curated editor UI needs its own host.
But every difference the requirement names — layout, menu, map layers, context menus — is a **seam
problem inside shared code**, not a hosting problem. Seams are exercised by whoever composes the panels;
a second executable adds nothing a profile could not express.

⇒ Do the Tier-1 seam work first. It is smaller than the exe, it benefits **all five modes** rather than
one, it needs no second test path, and afterwards the exe is a packaging decision rather than an
architectural one. [Q25-F′](Architect_Question_25_Scenario_Authoring_Golden_Path.md#f-prime-measured)
should not be relayed until this is folded in.

## Corrections to the scans

⚠ Two agent claims were wrong and are **not** carried into this document.

| Claim | Reality |
|---|---|
| *"`Hrot.UI.Common` is listed in `IOS-IG-SimHost.sln`"* | It is in **no** solution — `grep` across all `.sln` returns empty |
| *"`MessageLogPanel` (713 L) has no consumer"* | It **is** used — `MessageLogWindow.cs:30`, which `LocalWindowController.cs:50` registers for **every** mode. The scan checked only subsystems, not the host |

Also corrected against the programme's own docs:

| Programme claim | Reality |
|---|---|
| *"no right-click affordances on objects"* ([RESUME §0](UX_RESUME.md)) | **False as stated.** ~26 production context-menu sites; the Editor alone has 5 registered handlers plus state-varying map menus. True only of `EditorOrbatPanel`, the 27-line stub. **The menus exist and are attached to the wrong surfaces** |
| *"IOS/SimHost likely use different map rendering"* (user, 2026-08-10) | Symbol rendering is **shared and identical** — one `DebugGizmoLayer → DebugPrimitiveRenderer2D → DefaultEntityShapeLibrary` chain, data-driven by DIS enumeration. And **IOS = ExCon has no map at all** |
