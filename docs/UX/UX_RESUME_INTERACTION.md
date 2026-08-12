# RESUME — the interaction-design thread (2026-08-10)

> 🔴 **Read this first if context was compacted mid-session.** It is the smallest complete state of the
> interaction-design work. Branch: `claude/ux-session-resume-i2le7f`. Everything below is **committed**.

## 1. What this session did

Designed **7 of 27** issues, then consolidated three of them into one API and one acceptance catalogue.

| Doc | Holds |
|---|---|
| 📐 **[UX_Interaction_API.md](UX_Interaction_API.md)** | ⭐ **the contract** — types, arbitration order, threading, ECB, progress |
| ✅ **[UX_Interaction_UseCases.md](UX_Interaction_UseCases.md)** | **57 cases**, 46 headless / 11 integration / **0 visual-only** + coverage check |
| [UX_Issues.md](UX_Issues.md) | the register — 27 issues |
| [UX_Seam_Inventory.md](UX_Seam_Inventory.md) | prior-art table + `scripts/seam_inventory.py`, `scripts/type_index.py` |
| [UX_Tasks_Detail.md](UX_Tasks_Detail.md#corrections) | **23 corrections** — read before trusting any claim |

**Designed:** UXI-01, 02, 03, 04, 05, 06, 07, 08, **09**. **Refuted:** UXI-26. **Split out:** UXI-25 (ExCon ORBAT),
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

## 6. Next steps, in order

1. ✅ **RESOLVED — host pump + playback goes immediately before `_kernel.Update()`** (`EditorSubsystem.cs:1618`). The kernel flush is *before* `Bus.SwapBuffers()` (`ModuleHostKernel.cs:523-534`), so ops land visible **in the same frame**. Precedent: `_aiCoordinator.DrainPendingCallbacks()` (`:1620-1624`) is the same pattern already in production. ⚠ *Unpinned:* where ImGui panel drawing sits relative to `EditorSubsystem.Update()` — affects only which frame a synchronous handler commits in. See [API §6d](UX_Interaction_API.md#6d--where-the-hosts-playback-sits--resolved-2026-08-10)
2. ⏭ **Designing continues** — user, 2026-08-10: *"keep designing now rather than rewriting tasks later because we find new unexpected stuff"*. ✅ **UXI-09 designed** → [UX_Feature_Map_Viewport.md](UX_Feature_Map_Viewport.md). **Next: UXI-10** (symbology seam, zero hosts).
3. Cut `UXT` tasks — **none cut yet**, deliberately deferred.
4. Remaining undesigned: UXI-10..24 (symbology, duplication, robustness) + 23, 24, 25, 27.
5. The golden-path walk still needs a **Windows** session. ✅ **The UXI-09 ImGui question is closed** — verified against the real package: managed `ImGui.NET.dll` exposes **no** `DockBuilder*`, but `cimgui.dll` (already loaded) exports `igDockBuilderGetCentralNode` **and `ImGuiDockNode_Rect`** ⇒ tier T2 needs two `DllImport`s and no struct-offset arithmetic.

## 7. ⚠ Process rules earned the hard way

| | |
|---|---|
| **Rule 6** | every design opens with a **Prior art** section citing the [Seam Inventory](UX_Seam_Inventory.md) — the seam usually **already exists and is under-adopted**. ⭐ **9 instances so far**; the newest two are `MapCameraViewport` (a shared type stranded in `Hrot.IG`, reached by the Editor through a **project reference**) and `DockspaceLayout` (built and tested for the dockspace, never extended to the camera) |
| **Rule 6c** | ⚠ **never read a reference *count* as adoption — open the call sites.** [Correction 22](UX_Tasks_Detail.md#corrections): 8 tests vs 3 real calls, and I reported "zero consumers" from the ratio |
| **Rule 6b** | before claiming an **engine-level defect**, read `README.md` + the Programmer's Guide |
| ⚠ | **follow the call one level deeper** — I read a dispatcher and missed synchronization in the stream |
| ⚠ | subagent reports have failed verification **6+ times** — re-derive every delegated claim |
| ⚠ | resolve a duplicated type by **project reference**, never by namespace |
