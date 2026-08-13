# RESUME — the interaction-design thread (2026-08-10)

> 🔴 **Read this first if context was compacted mid-session.** It is the smallest complete state of the
> interaction-design work. Branch: `claude/ux-session-resume-i2le7f`. Everything below is **committed**.

## 1. What this session did

Designed **7 of 27** issues, then consolidated three of them into one API and one acceptance catalogue.

| Doc | Holds |
|---|---|
| 📐 **[UX_Interaction_API.md](UX_Interaction_API.md)** | ⭐ **the contract** — types, arbitration order, threading, ECB, progress |
| ✅ **[UX_Interaction_UseCases.md](UX_Interaction_UseCases.md)** | **57 cases**, 46 headless / 11 integration / **0 visual-only** + coverage check |
| [UX_Issues.md](UX_Issues.md) | the register — **30 issues** |
| ✅ **[Architect_Question_28_Map_Layers.md](Architect_Question_28_Map_Layers.md)** | **UXI-28 decisions — ALL SETTLED** 2026-08-12 |
| ✅ **[UX_Feature_Map_Layers.md](UX_Feature_Map_Layers.md)** | **UXI-28 design** — 17 cases. 🔴 **Gated on a Windows check**: the layer panel round-trip is complete in source but reported non-working |
| ✅ **[UX_Feature_Selection.md](UX_Feature_Selection.md)** | **UXI-11 design** — 22 cases. One store, `ISelectionState` becomes a view; CGF gets the full chain |
| ✅ **[UX_Feature_Authority_Aware_Writes.md](UX_Feature_Authority_Aware_Writes.md)** | **UXI-29 design** — 13 cases. Gizmos publish intent; complementary authority gates route local-vs-remote |
| [UX_Seam_Inventory.md](UX_Seam_Inventory.md) | prior-art table + `scripts/seam_inventory.py`, `scripts/type_index.py` |
| [UX_Tasks_Detail.md](UX_Tasks_Detail.md#corrections) | **29 corrections** — read before trusting any claim |

**Designed:** UXI-01..**11**, **28**, **29** (+ **19**, verified and absorbed into 10). **Refuted:** UXI-26. **Split out:** UXI-25 (ExCon ORBAT),
UXI-27 (progress surface).

## 2. 🔒 Every user ruling, in force

| # | Ruling |
|--:|---|
| 1 | **Tools ≠ actions.** *"An action is what activates a tool."* Two vocabularies, one relationship |
| 2 | **`gizmo ≠ tool`** — stateless gizmos (health bars) are not tools; many per entity |
| 3 | **Modal** = ≤1 active per subsystem; **modeless** = permanent until off, several coexist |
| 4 | **Re-activating a modal tool does NOT cancel it** — toggle only via `ToggleOnReactivate` |
| 5 | Cancellation is **focus-driven**. `CancelsModalTool` on the **action** (not the tool); `false` = *transparent* (AutoCAD) |
| 6 | **No suspend flag can exist** — suspension needs a resume point only the handler knows ⇒ scoped (`PushModal` + dispose) |
| 7 | **Toolbar presence is opt-in** (`ShowOnToolbar`) |
| 8 | **A suspended tool decides its own drawing** — controller never suppresses; tool reads `Activity.State` |
| 9 | **Modal tool stack** required — interrupt and return. It existed once (`MapCanvas.PushTool/PopTool`) and was deleted |
| 10 | **Tool state per subsystem** (B1); an unfocused subsystem's tool stays armed but consumes no input |
| 11 | **`EditorTool`**: no tool classes remain; the enum is a fossil. Tool ids are their own vocabulary |
| 12 | **Escape centralised for modal tools**, gizmo-local cleanup stays |
| 13 | **Long-running cancellables MUST be visible**: `Exclusive` → **modal dialog + progress bar**; else → **status-bar progress + cancel icon** |
| 14 | **Progress API is part of the design**; the widget is its own issue (UXI-27) |
| 15 | 🔒 **Single-threaded async** — handlers stay on the tick thread and yield; **per-action ECB**, real API, no wrapper |
| 16 | **ExCon is DDS-only, no ECS** — reuses the ORBAT UI with its own data model. `int EntityId` is correct, not a defect |
| 17 | **No assumptions.** Read `README.md` + `docs/HROT-PROGRAMMERS-GUIDE.md` before claiming an engine defect |
| 18 | **Layout = one directory.** `fdp_windows.json` lives **next to `imgui.ini`** in *both* places (user `%LocalAppData%\HROT\` and the shipped default). Reset = a directory copy |
| 36 | 🔒 **Drag is unified — one registration, no callbacks** (user, 2026-08-12): *"drag handling needs unification, sharing same implem in all map subsystems."* Today it is wired **5 different ways**; the `onDragCommitted` hook has **exactly one caller** (IG). ⇒ `new EntityDragGizmoDefinition()` everywhere; IG's `onDragCommitted`/`OnEntityDragEnded`/`SendGeoSpatialUpdate` **retire**. ⭐ **Continuous-drag and shift-immediate are reachable only from a TEST HOOK** (`IgApplication.cs:2291-2305`); production is already commit-only, so unification is behaviour-preserving. **Seam-law instance 14**: `MapUserConfig.ContinuousDragUpdates` has no production reader |
| 35 | 🔒 **Two classes of inbound writer, with INVERSE gates** (user, 2026-08-12): *"writing to unowned components is reserved for network replication ingress translators, never for change requests like attrib/descriptors — these must be received by any node and applied only to owned ECS components."* ⇒ **replication ingress**: `if (HasAuthority) skip` — writes **unowned** ghosts **by design**. **Change-request appliers**: `if (!HasAuthority) skip` — **never** write unowned. 🔒 The notion is **native component ownership**, so the descriptor `FIXME` was right. 🔴 **UXI-30's sweep must classify each site first** — inverting a replication translator breaks ghost updates repo-wide |
| 34 | 🔒 **Wire form is economics; the ownership gate is the invariant** (user, 2026-08-12): *"whether to update whole descriptor or just single attrib is the network bandwidth/performance decision, otherwise both should be applied with the ownership gate on the receiver."* ⇒ **one architecture, two encodings** — attribute records for scalars, whole descriptor for structure-changing lists. 🔴 **The invariant does not hold today**: of five appliers, **2 gate on nothing**, **2 gate on the descriptor key**, **1 on native component authority**. [UXI-30](UX_Issues.md#uxi-30) widened to cover all five |
| 33 | 🔒 **The binary path's missing authority check is a SEVERE ERROR** (user, 2026-08-12): *"no reason why the binary path should be different from the json path in this aspect."* ⇒ filed as **[UXI-30](UX_Issues.md#uxi-30)**, a **prerequisite** for UXI-29. ⭐ **Census: zero production senders of `AttributeRecords`** — the fix breaks nothing; the binary path is receive-only with no producers (**seam-law instance 13**). ✅ **`GeoHeading` approved** — one constant + one handler, mirroring `SimTransformAttributeInstaller:81-86` |
| 32 | 🔒 **Intents are adopted, and their wire form is the BINARY attribute change request** (user, 2026-08-12): *"if intent is a local FDP event translated to a network binary attrib change request message, then ok, lets use intents in entity attribute changing gizmos."* ⇒ local `Fdp` bus event → `UpdateEntityAttributeRequest.AttributeRecords` (ATTR2), not JSON, not `dtWorldPos`. 🔴 **Two verified gaps block it**: the **binary path has no authority gate** (`CanWrite` is called only from `JsonAttributeCompiler`), and **no rotation attribute ID exists** (`AttributeIds` has only Name/Affiliation/GeoLat/Lon/Alt) |
| 31 | 🔒 **Pick box and selection ring are shared and identical in every map subsystem** (user, 2026-08-12) — *"should be the same in all map subsystems, shared and reused"*. ⭐ Both **already are single implementations** (`EmitPickBox`, `SelectionHighlightGizmo`); the gap is **adoption only** — CGF has neither, SimHost lacks the ring. 🔒 No per-host variants or parameters. ⚠ **Mismatch found:** pick box **8 px** vs ring **20 px** radius — the visible affordance exceeds the clickable one, identically everywhere |
| 30 | 🔒 **Structural ECS changes go through the command buffer** (user, 2026-08-12): *"why not modify ECS using command buffers, which is safe always?"* ⇒ the `ISelectionState` view records on the ECB rather than writing the repo. ⭐ Already consistent with [ruling 15](#) — a menu item that changes selection **is** an action handler, so it already has a per-action ECB. ⚠ **`EntityQuery` iteration was never at risk** ([Correction 29](UX_Tasks_Detail.md#corrections)) — it is an index scan, not a chunk walk |
| 29 | 🔒 **Multi-delete requires a modal confirmation** (user, 2026-08-12) naming what will be removed; Cancel deletes nothing. ⇒ [UXI-16](UX_Issues.md#uxi-16) gains a hard requirement, delivered via [ruling 13](#)'s modal machinery |
| 28 | 🔒 **Click semantics** (user, 2026-08-12). **Right-click selects.** On an **already-selected** entity ⇒ selection **unchanged**, menu shows items applicable to **all** selected. On a **non-selected** entity ⇒ **clear, then select just it**. **Empty click clears; reload clears.** ⇒ file-manager convention. [UXI-11](UX_Feature_Selection.md) §2.3 |
| 27 | 🔒 **Selection: CGF is identical to SimHost/Editor; ExCon is correctly different** (user, 2026-08-12). *"CGF should be no different from SimHost nor even Editor regarding selection — same visuals, same interaction, same ECS component. **Selection is subsystem-local.** ExCon has no map, it is an ImGui-panel-only subsystem whose role is to control the IG map and CGF and SimHost over DDS; selection is controlled remotely and stored on ExCon in a completely separated way."* ⇒ [UXI-11](UX_Issues.md#uxi-11)'s ExCon half is **refuted** ([Correction 27](UX_Tasks_Detail.md#corrections)); the CGF half stands |
| 26 | 🔒 **Dim is style, tag is filter** (user, 2026-08-12), verbatim: *"dimming is style, tag is layer filter"*. ⇒ **search dims and never becomes a tag**; my open question *"should search also get a hide mode?"* is **withdrawn as ill-posed** — it conflated the two. If *hide-non-matches* is ever wanted it is simply a new tag, no redesign. **`MapCanvas.ActiveLayerMask` survives** as the *"which renderers run"* switch. **Tag width stays `uint`/32** — no change needed |
| 25 | 🔒 **`ZIndex` becomes the draw order and pick priority; dim ≠ hide** (user, 2026-08-12). *"Is there a dedicated sorter field? If yes then of course it should be made working."* ⭐ **There is — and it is dead**: `ZIndex` is set by **no production code** (only 3 lines in `GizmoMap.Example`), so every production primitive is `ZIndex = 0` and sorting collapses to `DebugLayer`. ⇒ the migration is cheap — reproduce only `Entities`→`Perception`→`AiHelpers` as `ZIndex` 0/1/2. **Seam-law instance 12.** ⭐ **Dimming is a *colour* concern**, applied at emit via an `IStyleSource` — no mask, no renderer change, works on the remote terminal |
| 24 | 🔒 **Layer visibility is ALL, and overflow degrades to visible** (user, 2026-08-12). A multi-tagged primitive is **hidden if *any* of its tags is hidden** ⇒ *"hide hostiles"* works and the two axes compose; an untagged primitive is always visible. On the **257th** distinct combination: **degrade to always-visible and log** — never silently drop. ⭐ The rule lives in one place — the backend's per-frame evaluation — so it can change without touching wire, renderer or gizmo |
| 23 | 🔒 **Map layers are TAGS, not a partition** (user, 2026-08-12). *"I would like one entity **or gizmo** to belong to more than one layer."* ⭐ **And the byte survives**: *"maybe the `DebugLayer` byte needs to be dynamically calculated out of the bitmasks"* ⇒ it becomes an **interned combination id**, so `DebugPrimitive` needs **no wire change** and the renderer's filter is unchanged. 🔴 **Blocker found:** `DebugLayer` is also the **sort key** (`DebugPrimitiveRenderer2D.cs:178`) and the **hit-test priority** (`DebugGizmoLayer.cs:447`) — both must move to `ZIndex`. [Q28](Architect_Question_28_Map_Layers.md) §B |
| 22 | 🔒 **Mutate ECS only where you own it** (user, 2026-08-12). *"CGF does not own `SimTransform`, so it needs to send a request to SimHost, not change ECS directly — similar to Delete. **Editor owns all.**"* ⇒ CGF's *Rotate* must mirror `DeleteEntity`'s `DestroyEntityCommand` publish-by-`NetworkId` (`CgfSubsystem.cs:777-785`). 🔴 **No pose-change command exists** — Spawn and Destroy have network paths, pose does not; drag and rotate both `GetComponentRW<SimTransform>` directly ⇒ [UXI-29](UX_Issues.md#uxi-29) |
| 21 | 🔒 **One pose source, one symbol path** (user, 2026-08-12). *"CGF is not different from the others — all should use `SimTransform`, the same gizmo, the same DIS-type/TKB-derived shape; maybe just IG can override via DDS."* CGF's `NetworkTransform` preference is **deleted, not migrated** ([Correction 26](UX_Tasks_Detail.md#corrections)). ⭐ **Colour overrides may still be subsystem-specific** — via the same modification-layer mechanism as IG's DDS layer (`IStyleSource`) |
| 20 | 🔒 **Two classes of map** (user, 2026-08-12). **IG = the production 2D map, remotely controlled via the DDS API**; `StyleResolutionSystem` was written *for it*, DDS-provided styles being its point. **Editor · CGF · SimHost · ReplayBrowser = service-level maps** — sources are **local/user input + ECS, no remote DDS control**. ⇒ **Share the infrastructure where it is generic, reusable and helpful; never make a service map depend on the DDS layer.** [UXI-10](UX_Feature_Entity_Symbology.md) §2.5 |
| 19 | ✅ **The CGF / SimHost initial-view shift is approved** (user, 2026-08-12). Removing the hardcoded `(640, 360)` offset changes what those two subsystems show on first launch — accepted as the correct behaviour arriving. [UXI-09](UX_Feature_Map_Viewport.md) §5 |

## 3. The threading/ECB solution (ruling 15) — the shape

```csharp
var ecb  = new EntityCommandBuffer();                       // per dispatch
var prev = SynchronizationContext.Current;
SynchronizationContext.SetSynchronizationContext(_pump);    // ONLY around the call
var task = descriptor.Execute(ctx);                          // state machine captures _pump
SynchronizationContext.SetSynchronizationContext(prev);
// once per Update(), on the tick thread:  _pump.Drain();
```

| Outcome | Buffer |
|---|---|
| completes | **played back** on the main thread |
| cancelled / faulted | **dropped, never played back** ⇒ atomic commit |

⚠ **`ConfigureAwait(false)` anywhere in the awaited chain breaks it silently** — needs an analyzer or
review rule (UC-44c).

🔒 **This is NOT the withdrawn proposal.** That was an *ambient, process-wide* `SynchronizationContext`
([Correction 21](UX_Tasks_Detail.md#corrections)); this is scoped to the handler invocation only.

## 4. Verified engine facts — do not re-derive

| Fact | Evidence |
|---|---|
| `NativeEventStream<T>.Write` is **thread-safe**, `Interlocked`-reserved, double-buffered | `NativeEventStream.cs:83-101`, README §2.6 |
| `ISimulationView.GetCommandBuffer()` → **per-thread** ECB, `trackAllValues: true` | `EntityRepository.View.cs:13,33-35` |
| ECB records **events too** | `OpCode.PublishUnmanagedEvent = 8`, `PublishManagedEvent = 9` |
| Kernel flushes **all** thread buffers on the main thread at **BeforeSync** | `ModuleHostKernel.cs:519-532` |
| The Editor runs that kernel | `EditorSubsystem.cs:585`, driven `:1617` |
| `EntityRepository.FlushCommandBuffers()` is **dead API** (0 production callers) | `EntityRepository.View.cs:43` |
| Rule 7: background code uses `ISimulationView` + ECB, never the live repo | `HROT-PROGRAMMERS-GUIDE.md` Part 0 |
| 🔒 **Replication ingress writes UNOWNED components on purpose** — `if (!isLocallyOwned) SetComponent(...)`. Its gate is the **inverse** of a change-request applier's, and both are correct | `GeoSpatialIngressTranslator.cs:85-89` |
| **`HasAuthority<T>` is PER-COMPONENT** — a bit in a per-entity `AuthorityMask`; a Brain can own `BehaviorState` while a Muscle owns `SimTransform` on the same entity | `EntityRepository.cs:1060-1063,1120-1127` |
| ⭐ **The authority-routing precedent**: two systems read the same event with **complementary** gates — `if (!HasAuthority) continue` applies locally, `if (HasAuthority) continue` forwards to the owner | `TacticalIntentResolutionSystem.cs:95`, `TacticalIntentEgressTranslator.cs:72` |
| **A generic inbound patch channel exists** — `UpdateEntityAttributeRequest`, authority-gated **per field** inside the compiler | `JsonAttributeCompiler.cs:37-41` (`CanWrite<T>()` → `reader.Skip()`) |
| ⚠ **`EntityQuery` iteration is an INDEX SCAN, not a chunk walk** — `_maxIndex` snapshotted at construction ⇒ Add/RemoveComponent mid-iteration does **not** invalidate it. The guide's chunk-iteration ban is scoped to **HSM/BTree `SharedAi` thunks** | `EntityQuery.cs:106-123`, guide `:483-484` |

## 5. 🔴 Verified defects the design closes

| | Evidence |
|---|---|
| Two `_focusedGizmo` arbiters on one bus, no arbitration | `EditorSubsystem.cs:1122-1134`, `:65`/`:31` |
| Per-`Entity` injection leaves two tools alive | `DataDrivenGizmoSystem.cs:74` |
| Double *Mark Target* → two concurrent picks | no guard exists |
| Two `async void` handlers swallow faults | `:1462`, `:1479` |
| `--mode all` first run hides **22** perspective-bound windows | [UXI-06](UX_Feature_Perspective_Restore.md) |
| SimHost/CGF emit **no** per-entity map menu | `SimHostApp.cs:337-345`, `CgfSubsystem.cs:497-500` |
| Editor + ReplayBrowser never set `MapCamera.Offset` ⇒ *Center on Entity* lands at the **top-left pixel** | `EditorSubsystem.cs:1395`, `ReplayBrowserSubsystem.cs:134` · [UXI-09](UX_Feature_Map_Viewport.md) |
| `Offset` is written **once at construction** in all 5 hosts ⇒ **resize decentres every map** | one `Offset` write per site; none in any resize path |
| **Perspective switch transports `Offset` between subsystems** — a screen-space value treated as portable camera state | `MapCameraView.cs:10-18` → `MapCamera.cs:253` → `SubsystemOrchestrator.cs:175-177` |
| 🔴 **`ResolvedStyle` (tint/affiliation/damage, 3-layer merge) is read by no renderer** — the map hardcodes `Rgba32(100,220,255)`, so friend and hostile look identical | `EntityPresentationGizmoShared.cs:92` · [UXI-10](UX_Feature_Entity_Symbology.md) |
| **CGF's semantic shapes are `alpha 0`** (bypasses the shared helper) and it emits **no pick box** ⇒ unselectable | `CgfEntityPresentationGizmo.cs:45-49` |
| **`VisualData.MapShapeName`** is authored, translated, and **read nowhere**; `GetShape`'s `shapeName` is always `null` | `VisualData.cs:33`, `PresentationTkbTranslator.cs:41`, `DebugPrimitiveRenderer2D.cs:410` |
| ✅ **UXI-19 verified** — the Editor emits **two** of every presentation primitive, and the second ignores culling | `StatelessGizmoSystem.cs:104`, `EditorSubsystem.cs:1094-1097` |
| **Selection: two stores, THREE writers, hand-synced** — ⚠ *not* desynced ([Correction 28](UX_Tasks_Detail.md#corrections)); `EditorSubsystem.cs:1226-1230` re-implements `SetSelected` and writes both | `SelectionInteractionSystem:170-172`, `EditorSubsystem.cs:1226-1230,1292-1297` |
| **CGF runs no `SelectionInteractionSystem`** ⇒ no entity ever gets `SelectionState` ⇒ the ring gizmo cannot match | registered by IG/ReplayBrowser/SimHost/Editor only |
| 🔴 **Rotating an entity in CGF does not visibly rotate it** — rotator writes `SimTransform`, gizmo draws `NetworkTransform` | `EntityRotatorGizmo.cs:118-122`, `CgfSubsystem.cs:605` |
| **`SetLayerMask(ushort) { }` is empty** — `MapCanvas.ActiveLayerMask` has **zero** effect on individual primitives; only `LayerControlGizmo`'s 256-bit mask filters, and only 3 bits are used | `Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs:23`, called `DebugGizmoLayer.cs:100` |
| **`MapLayerAssignmentSystem` classifies every entity into 5 layers → `MapDisplayComponent.LayerMask`, and the symbol emitter never reads it** (always `layer: 0`) — ⭐ *the same resolve-then-discard failure as the tint* | `MapLayerAssignmentSystem.cs:97-127` vs `EntityPresentationGizmoShared.cs` |

## 6. Next steps, in order

1. ✅ **RESOLVED — host pump + playback goes immediately before `_kernel.Update()`** (`EditorSubsystem.cs:1618`). The kernel flush is *before* `Bus.SwapBuffers()` (`ModuleHostKernel.cs:523-534`), so ops land visible **in the same frame**. Precedent: `_aiCoordinator.DrainPendingCallbacks()` (`:1620-1624`) is the same pattern already in production. ⚠ *Unpinned:* where ImGui panel drawing sits relative to `EditorSubsystem.Update()` — affects only which frame a synchronous handler commits in. See [API §6d](UX_Interaction_API.md#6d--where-the-hosts-playback-sits--resolved-2026-08-10)
2. ⏭ **Designing continues** — user, 2026-08-10: *"keep designing now rather than rewriting tasks later because we find new unexpected stuff"*. ✅ **UXI-09** → [Map Viewport](UX_Feature_Map_Viewport.md); ✅ **UXI-10 + UXI-19** → [Entity Symbology](UX_Feature_Entity_Symbology.md). ✅ **UXI-28 designed** → [Map Layers](UX_Feature_Map_Layers.md). ✅ **UXI-11 designed** → [Selection](UX_Feature_Selection.md). **Next: UXI-12** (spawn UI ×4) or **UXI-23** (map parity, now the natural sequel).
3. Cut `UXT` tasks — **none cut yet**, deliberately deferred.
4. Remaining undesigned: UXI-12..18, 20..25, 27.
5. The golden-path walk still needs a **Windows** session. ✅ **The UXI-09 ImGui question is closed** — verified against the real package: managed `ImGui.NET.dll` exposes **no** `DockBuilder*`, but `cimgui.dll` (already loaded) exports `igDockBuilderGetCentralNode` **and `ImGuiDockNode_Rect`** ⇒ tier T2 needs two `DllImport`s and no struct-offset arithmetic.

## 7. ⚠ Process rules earned the hard way

| | |
|---|---|
| **Rule 6** | every design opens with a **Prior art** section citing the [Seam Inventory](UX_Seam_Inventory.md) — the seam usually **already exists and is under-adopted**. ⭐ **9 instances so far**; the newest two are `MapCameraViewport` (a shared type stranded in `Hrot.IG`, reached by the Editor through a **project reference**) and `DockspaceLayout` (built and tested for the dockspace, never extended to the camera) |
| **Rule 6c** | ⚠ **never read a reference *count* as adoption — open the call sites.** [Correction 22](UX_Tasks_Detail.md#corrections): 8 tests vs 3 real calls, and I reported "zero consumers" from the ratio. ⚠ **And counting *constructions* misses hosts that default silently** — CGF omits the shape-library argument entirely ([Correction 24](UX_Tasks_Detail.md#corrections)) |
| **Rule 6d** | ⚠ **"the seam is unused" has two very different meanings** — an interface nobody *calls*, vs one called every frame with a dead parameter and no second implementation. UXI-10 was the second kind; the fix differs completely |
| **Rule 6b** | before claiming an **engine-level defect**, read `README.md` + the Programmer's Guide |
| ⚠ | **follow the call one level deeper** — I read a dispatcher and missed synchronization in the stream |
| ⚠ | subagent reports have failed verification **6+ times** — re-derive every delegated claim |
| ⚠ | resolve a duplicated type by **project reference**, never by namespace |
