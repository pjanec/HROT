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

## 6. Selection is fragmented three ways

| Representation | Used by | Kind |
|---|---|---|
| ECS `SelectionState` component + `SelectionInteractionSystem` | Editor **and** IG | genuinely shared state |
| `ISelectionState` instance | CGF, SimHost, Editor — **one object each** | same interface, separate objects |
| entity-id list over the wire | ExCon | not ECS at all; its ORBAT menu captures the clicked row directly |

⇒ **A shared panel cannot act on a consistent "what is selected" across modes today.** Any target
architecture that shares panels must fix this first — it is the precondition, not a nicety.

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
