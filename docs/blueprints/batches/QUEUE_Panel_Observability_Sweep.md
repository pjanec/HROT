<!--STATUS
state: LIVE
build-state: BUILDING (U-obs-5, the full sweep; the user ordered it 2026-08-22)
updated: 2026-08-22
current-answer: this file is the WORK QUEUE for the panel sweep — the recipe, the ordered list, the
  exclusions, and the tick-off state. The DESIGN is docs/DESIGN_UI_Observability_Snapshot.md; this
  file holds no design content, only the queue and the accumulated gotchas.
known-conflict: none.
-->
# QUEUE — **the panel observability sweep** *(`U-obs-5`)*

> 🔒 **User, `2026-08-22`:** *"pls work autonomously overnight, take it as far as possible, migrate all
> panels"* · *"you do not need to fan many agents at once, you can use one to convert all panels."*
>
> 📄 **The design — read it, do not re-derive it:**
> [`DESIGN_UI_Observability_Snapshot.md`](../../DESIGN_UI_Observability_Snapshot.md).
> ⛔ **This file is the QUEUE. Design content belongs in the design.**

## ⭐⭐⭐ THE RECIPE — **mirror it; do not redesign per panel**

| step | |
|---|---|
| **①** | **BUILD** the whole view-model — a projection of current state, implementing `IPanelViewModel` |
| **②** | **CAPTURE** — `if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);` ⛔⛔ **BEFORE anything ImGui-dependent**, so a headless run still observes it |
| **③** | **RENDER** — only from the view-model. ⛔ **Any drawn state-derived value that did not come from the VM is a defect** *(the INVARIANT)* |
| **④** | **DECLARE** in the constructor — `PanelSnapshot.DeclareInstrumented(<address>)`, **always**, ungated by `CaptureEnabled` |

### ⭐ Identity — **two fields, opposite requirements** *(📄 the design's AS-BUILT ②)*

| | |
|---|---|
| **`PanelId`** = the **ADDRESS** | unique among **live** panels ⇒ a per-perspective window uses **its own registration id**; a singleton declares a literal |
| **`PanelKind`** = the **LOGICAL NAME** | identical across hosts ⇒ what conformance groups by. Cross-host kinds are constants on `PanelIds`; single-host kinds stay local literals |

### ⛔⛔ GOTCHAS — **every one of these cost a real retry. Read before starting.**

| ⚠ | |
|---|---|
| **`ManagedWindow.DrawClientArea` is UNREACHABLE HEADLESS** | `ManagedWindow.Render` calls `Gui.Begin` **before** it ⇒ ⭐ extract a **build-and-publish seam** *(`BuildAndPublish()` / `DrawContent()`)* called first from the override and directly by tests. 📌 Precedents already in the tree: `AiGraphCanvasWindow.SimulateDrawClientArea`, `GraphSignatureWindow.DrawContent` — **mirror, do not invent** |
| **A plain `*Panel` has NO id** | ⇒ ⭐ **the HOST window registers**, using its own `Id`, and the panel just builds the model. ⛔ Do not give a panel its own address |
| **⛔⛔ RESTORE THE REVERT PROBE** | 📌 **Measured twice this run**: an agent commented out `Register`, probed, and left it commented ⇒ **a panel that declares itself instrumented and publishes nothing**, with a green build. ⭐ **After every probe: `grep` the file and confirm `Register` is LIVE** |
| **Do not reflect over a model carrying delegates or `System.Type`** | ⇒ project the **displayed** shape by hand. 📌 Precedent: `VariableTablePanelViewModel.Dump` |
| **Static chrome is NOT converted** | a constant caption, a button that only invokes a command ⇒ ⛔ a view-model that can never fail. 📄 the design's own *"never refactor a static label"*, which survives the full-sweep override |
| **`Fdp.Presentation.Tests` cannot run whole** | pre-existing test-host crash *(`BP-419`)* ⇒ ⭐ **gate by `--filter` only**, and say so |
| **`quick-check.sh` refuses to test a failed build** | ⛔ never report a pass off a failed build — `--no-build` would test a stale binary |

## ⛔ EXCLUSIONS — **not silently dropped; each has a reason**

| bucket | why |
|---|---|
| ⛔ **`Hrot.Orchestrator`** — `ClusterScenarioPanel` · `ClusterDiagnosticsPanel` · `ClusterControlWindow` · `OrchestratorWindow` · `DiagnosticsWindow` *(orchestrator's)* | 🔒 **the TIME lane owns this assembly** *(`CLAUDE.md` lane table)*. ⚠ **A cross-lane edit is a STOP-and-report**, not a judgement call. **6 rows** |
| ⛔ **`FDP/Examples/`** · `FDP/ExtDeps/FastBTree/demos/` | sample/demo apps, not editor panels. **4 rows** |
| ⭐ **`FDP/ExtDeps/NodeEdit/` — NOT excluded, INVERTED** | 🔒 **User:** *"nodeEdit itself does not need conversion, it is editing given structure reference, but its caller can register the model (the struct) in the singleton snapshot registry."* ⇒ ⭐⭐ **the CALLER registers**; the generic panel needs no contracts reference at all. 📄 the design's *"caller-registers rule"*. **5 panels, converted at their call sites** |
| ⚠ **`ManagedWindow.cs` itself** | infrastructure, not a panel — ⛔ but it is where `Id`/`Title` live, so a base-class change is a DESIGN question, not a per-panel conversion. **Leave it; flag if a conversion seems to need it** |

## ⚠ UNRESOLVED — **do not bucket these silently; report instead**

| file | the question |
|---|---|
| `Hrot.Blueprints.Editor/Debug/HotReloadLogWindow.cs` | has a live `HotReloadLogModel` but `DrawUI()` renders **nothing** — mid-implementation, or inert? |
| `Hrot.Editor.AiShared/Windows/TraceTimelineWindow.cs` | always draws the fixed string *"No trace data."* while `RegisteredProviderCount` goes **unread** |
| `Hrot.Blueprints.Editor/Variables/BlueprintVariablesWindow.cs` | ⛔ **the window class was RETIRED**; the filename is stale and only adapters remain. Nothing to convert |

## ⭐⭐ THE QUEUE

⭐ **Order: prove the cheap ones, then the big ones.** ⚠ Tick a row only when its rails are **green** and its
probe **restored**.

### ✅ DONE

| panel | address | kind |
|---|---|---|
| `EntityBlueprintsPanel` *(pilot)* | `entity-blueprints` | `entity-blueprints` |
| `AiVariablesWindow` | its `Id` | `variables` |
| `AiWatchWindow` | its `Id` | `watch` |
| `WatchPanelWindow` *(Blueprints)* | `blueprints-watch` | `watch` |

### 🔄 IN FLIGHT

`BlackboardAuthoringWindow` · `DiagnosticsWindow` *(AiShared)* · `MessageLogPanel`+`MessageLogWindow`

### ⏳ TODO — **by assembly, so one worker never re-reads the same context twice**

| # | assembly | panels |
|---|---|---|
| **1** | **`Hrot.Editor.AiShared`** | `DetailsWindow` · `DetailsViewWindow` · `RuntimeInspectorWindow` · `FindResultsWindow` · `AiMyBlueprintWindow` · `AiGraphCanvasWindow` · `AiBreakpointsWindow` · `ComparisonSummaryPanel` · `AssetBrowserPanel`+`AssetBrowserDockedWindow` |
| **2** | ⭐⭐ **`Hrot.Editor.AiShared/Shell` — THE `*DetailsView` FAMILY** *(the glob missed these, and they hold the Details panel's real state)* | `BlackboardDetailsView` · `NodePropertiesDetailsView` · `ParameterSyncDetailsView` · `RuntimeDetailsView` · `UtilityConsiderationDetailsView` · `VariablesDetailsView` |
| **3** | **`Hrot.Blueprints.Editor`** | `BlueprintMyBlueprintWindow` · `GraphSignatureWindow` · `CallstackWindow` · `DebugPanelWindow` · `PreferencesWindow` · `BlueprintBookmarksWindow` · `EntityBlueprintsManagedWindow` · `BlueprintNodeDetailsView` · `GraphSignatureDetailsView` · `ExecRowsView` · `ParameterRowsView` |
| **4** | **`Fdp.Presentation`** | `EntityInspectorPanel`+`FdpEntityInspectorWindow` · `EventBrowserPanel`+`FdpEventBrowserWindow` · `DerEntityInspectorPanel` · `EntityWatchPanel` · `ArchitectureDiagnosticsPanel` · `SystemProfilerPanel` · `ComponentEditWindow` · the 4 ReplayBrowser panels + their hosts |
| **5** | **`Hrot.Presentation` + `Hrot.UI.Common`** ⚠ **twin pairs — near-identical duplicates** | `ConfigPanel` · `PreviewPanel` · `SharedOrbatPanel` · `SpawnerPanel` · `ZoneEditorPanel` *(×2 assemblies)* · `MissionPanel` · `DataBreakpointManagerPanel`+`Window` · `ArchitectureDiagnosticsWindow` |
| **6** | **the rest** | `Hrot.ExCon` ×5 · `Hrot.IG` ×4 · `Hrot.Editor` ×3 · `Hsm.Editor` `HsmEventsWindow` · `Hrot.Utility.Editor` `UtilityDecisionWindow` · `Hrot.SimHost` `FakeNavigationInspectorWindow` · `Hrot.MuscleCharacter.Animation.Fake` · `Hrot.Diagnostics.Breakpoints` `TemporalStatusBannerPanel` |

⚠⚠ **Group 5's twins are a RULING-9 question, not a copy-paste job:** 📐 several are **byte-identical**
across the two assemblies. ⛔ **Do not convert the same panel twice** — ⭐ report the duplication and
convert once if they can share, or state why they cannot.

## ⭐ PER-PANEL DEFINITION OF DONE

1. rails: **instrumented-before-draw** *(with an anti-vacuity assert)* · **the dump carries a real field** · **capture-off publishes nothing but stays registered**
2. the touched project's suite **green** *(filtered where a suite cannot run whole)*
3. the revert probe **reddened**, and the `Register` call **verified live afterwards**
4. rendering behaviour **identical**
