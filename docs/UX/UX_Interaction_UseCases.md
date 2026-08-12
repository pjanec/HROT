# Interaction use cases — the acceptance catalogue

> **Drafted 2026-08-10** for [UX_Interaction_API.md](UX_Interaction_API.md), covering
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) · [UXI-04](UX_Feature_Cross_Surface_Actions.md) ·
> [UXI-07](UX_Feature_Tool_Model.md).
>
> **Two audiences:** the user, to review that the design solves the right things; and the implementation
> session, to cut straight into tests. **[§10](#10-coverage-check) proves every design element is exercised.**

## 1. How to read this

| Class | Meaning | Who runs it |
|:--:|---|---|
| **H** | **Headless unit test** — controller/registry logic, no ImGui, no window | CI, every commit |
| **I** | **Integration** — needs a kernel tick (ECB playback, event swap) | CI, headless kernel |
| **V** | **Visual** — needs the running editor on Windows | the golden-path walk |

🔒 **Design target: every rule is H or I.** A rule only reachable by **V** is a rule that will rot —
if a case below is V-only, that is a signal the API is missing a seam, not that the test is hard.

**🛡 marks a regression guard** for a defect this programme actually found, with the citation.

## 2. Tools — modality and lifecycle

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-01** | Activate a modal tool | no tool → activate `Rotate` → `ActiveModal == Rotate`, `ModalStack.Count == 1` | H |
| **UC-02** | Switching modal tools cancels the first | `Rotate` active → activate `Measure` → `Rotate` disposed, `ActiveModal == Measure`, stack still 1 | H |
| **UC-03** | 🔒 **Re-activating does NOT cancel** | `Rotate` active on A → activate `Rotate` on A → still active, **not** cancelled | H |
| **UC-04** | …unless `ToggleOnReactivate` | tool declared `ToggleOnReactivate: true`, active → activate again → cancelled, `ActiveModal == null` | H |
| **UC-05** | Re-activate with a different target re-targets | `Rotate` on A → activate `Rotate` on B → one instance, now bound to B | H |
| **UC-06** | 🛡 **Two entities do not keep two tools** | `Rotate` on A → activate `Edit` on B → **only `Edit` alive**. *Guards `_injectedGizmos` per-`Entity` leak (`DataDrivenGizmoSystem.cs:74`)* | H |
| **UC-07** | Modeless tools coexist | activate 2 modeless + 1 modal → all three live; `ActiveModeless.Count == 2` | H |
| **UC-08** | Stateless gizmos are untouched by tools | health-bar gizmos drawn for 5 entities → activate/cancel a modal tool → still 5. *`gizmo ≠ tool`* | H |
| **UC-09** | Toolbar presence is opt-in | tool with `ShowOnToolbar: false` → toolbar has no button; `Activate(id)` still works | H |
| **UC-10** | `Select` is the null modal tool | `Rotate` active → activate `Select` → `ActiveModal == Select`, `Rotate` disposed. *Replaces `case Select: break;`* | H |
| **UC-11** | 🛡 **One arbiter across both engines** | activate a `DataDrivenGizmoSystem` tool **and** a `GlobalGizmoManager` tool → the second displaces the first; **one** gizmo receives the drag. *Guards the two-`_focusedGizmo` defect (`EditorSubsystem.cs:1122-1134`)* | H |

## 3. The modal stack — interrupt and return

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-12** | Push suspends, does not destroy | route editor active with 3 waypoints placed → `PushModal(picker)` → route editor `State == Suspended`, **its 3 waypoints intact**, picker on top | H |
| **UC-13** | Dispose pops and resumes | as above → dispose the scope → route editor `Running` again, **still 3 waypoints**, `ModalStack.Count == 1` | H |
| **UC-14** | 🔒 Escape pops **one** level | stack = [route editor, picker] → Escape → picker cancelled, **route editor resumes**; stack == 1 | H |
| **UC-15** | Escape again cancels the base | continue UC-14 → Escape → route editor cancelled, `ActiveModal == null` | H |
| **UC-16** | 🔒 **Suspension is visible to the tool; drawing is the tool's own call** | route editor suspended → it **still receives `Draw`**, `Activity.State == Suspended`, and it receives **no input**. A tool that opts to suppress emits nothing; one that does not keeps drawing. *The controller never decides this* | H |
| **UC-17** | Async handler interrupt round-trip | *Mark Target* handler runs → pushes picker → `await` completes → pops → whatever was active beneath is restored | I |
| **UC-18** | Cancelling the picker cancels the awaiting handler | picker pushed → Escape → the handler's `await` observes cancellation; **no `SeedTargetCommand` recorded** | I |

## 4. Actions — transparency, concurrency, faults

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-19** | 🔒 **Transparent action leaves the tool armed** | route editor active → fire *recenter map* (`CancelsModalTool: false`) → **route editor still active, waypoints intact**. *The AutoCAD case* | H |
| **UC-20** | `CancelsModalTool` clears the **whole** stack | stack = [route editor, picker] → fire *Delete entity* (`CancelsModalTool: true`) → stack empty | H |
| **UC-21** | 🛡 `Drop` refuses re-entry | *Mark Target* (`Drop`) running → dispatch again → **second returns the same activity**, no second picker. *Guards two concurrent picks today* | H |
| **UC-22** | `Restart` cancels the previous | action declared `Restart`, running → dispatch again → first `Cancelled`, second `Running` | H |
| **UC-23** | `Queue` serialises | action declared `Queue`, running → dispatch ×2 → they run in order, never overlapping | H |
| **UC-24** | `Concurrent` allows overlap | default action → dispatch ×2 → two activities `Running` | H |
| **UC-25** | `Exclusive` blocks others, **and is visible** | `Exclusive` action running → dispatch any other → rejected **with a reason**, and the status bar shows the blocker + a cancel | H |
| **UC-26** | 🛡 **A faulting handler surfaces** | handler throws → `activity.Completion` is `Faulted`, exception observable, app alive. *Guards the two `async void` handlers (`:1462`, `:1479`)* | H |
| **UC-27** | Cancellation is observable | long action → `Cancel()` → `Completion` reports cancelled; no partial commit | I |

## 5. Cross-surface consistency

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-28** | Same entity, same set on every surface | one entity → collect items from map, inspector and ORBAT → sets are equal **except** map-only exclusions | H |
| **UC-29** | 🔒 **Map exclusions are explainable by the id rule** — assert the invariant, never a list | for one entity, take `inspectorSet` and `mapSet` → **every item in `inspectorSet \ mapSet` has no `GlobalActionRegistry` binding**. Catches both directions: an id-bound action silently dropped from the map, **and** an action reaching the map *without* a binding (⇒ an **inert item**, the CGF failure mode of [UXI-23](UX_Issues.md#uxi-23)) | H |
| **UC-30** | `Selection`-mode items never appear on the map | *Mark Target* (`Selection`) → absent from the map menu, present in the inspector | H |
| **UC-31** | Multi-select: **AND over the selection** | select a unit + a tac-graphic → `Delete` shown, `Edit Route` **hidden** | H |
| **UC-32** | Multi-select: `PerEntity` fans out | 3 selected → invoke `Delete` → handler runs **3 times**, once per entity | H |
| **UC-33** | Multi-select: `Selection` runs once | 3 selected → invoke *Mark Target* → handler runs **once**, receiving all 3 | H |
| **UC-34** | Ordering is group-then-call-order | register out of order across 2 providers → rendered order is View → Edit → Destructive | H |
| **UC-35** | 🔒 ExCon binds without an `Entity` | ExCon ORBAT (DDS-only, `int EntityId`) → the shared menu renders and dispatches through `IOrbatController` | H |

## 6. Focus, perspective and co-running subsystems

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-36** | 🔒 Unfocused subsystem's tool stays armed but deaf | SimHost `Measure` active → switch perspective to CGF → `Measure` still `ActiveModal` for SimHost, **receives no input** | H |
| **UC-37** | Regaining focus resumes input | continue UC-36 → switch back → `Measure` receives input again, state intact | H |
| **UC-38** | Tool state is per subsystem | SimHost `Measure` + CGF `Rotate` → each host reports its own `ActiveModal`; neither sees the other | H |
| **UC-39** | Menu items follow focus | `--mode all` → switch perspective → subsystem-bound items change; File/Settings/Help stay | I |
| **UC-40** | 🛡 First run picks a real perspective | delete `fdp_windows.json`, `--mode all` → lands on a perspective **that has windows**; the 22 bound windows are visible | I |

## 7. ECS discipline — the rules made unbypassable

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-41** | 🔒 **A handler cannot reach the live world** | `EntityActionContext` exposes `ISimulationView` + `IEntityCommandBuffer` + `FdpEventBus` only → **compile-time**: no member returns `EntityRepository` | H |
| **UC-42** | 🔒 A handler cannot write components | `ISimulationView` has **no** `GetComponentRW` → compile-time | H |
| **UC-43** | Background recording is flushed on the tick | continuation on a **non-main thread** records into `ctx.Commands` → after one `Kernel.Update()`, the ops are applied. *Verified path: `EntityRepository.View.cs:13,33-35` → `ModuleHostKernel.cs:519-532`* | I |
| **UC-44** | Deferred events arrive | handler records `PublishUnmanagedEvent(SeedTargetCommand)` → readable by systems after the next swap | I |
| **UC-45** | Architecture test: no UI handler touches `EntityRepository` | scan registered action/tool handlers → none references `EntityRepository`. *Keeps UC-41 true as code is added* | H |

## 8. Long-running work

| # | Case | Given → When → Then | Cls |
|---|---|---|:--:|
| **UC-46** | Progress is reported and surfaced | long action reports 0.0→1.0 → status bar shows label + fraction; `Auto` visibility appears only if it outlives a frame | H |
| **UC-47** | Indeterminate work still shows | action reports `Fraction: null` with a message → status bar shows the message, no bar | H |
| **UC-48** | Cancel from the status bar | long action running → click cancel → `Cancellation` fires, `Completion` cancelled, activity leaves the list | H |
| **UC-49** | Activities list reflects reality | start 2 actions + 1 modal tool → `Activities` has 3; complete one → 2 | H |
| **UC-50** | A tool and a long action coexist | modal tool armed + a `Concurrent` long action → both listed; the action does not steal focus | H |

## 9. What is deliberately **not** covered

| | Why |
|---|---|
| Undo/redo of actions | `ProducesUndoEntry` is a declared **hook**; [OQ-3](UX_Requirements.md#answered-questions) ruled general undo out of scope |
| IG's DDS-authored menu | separate pipeline (Q26-A2) |
| Unifying the three `Delete` handlers | user ruling — divergence is structural |
| Rendering/symbology | [UXI-10](UX_Issues.md#uxi-10), unrelated seam |

## 10. Coverage check

Every element of the API mapped to at least one case. **A design element with no case is a gap.**

| Design element | Covered by |
|---|---|
| `ToolModality.Modal` / `.Modeless` | UC-01, 02, 07 |
| `ShowOnToolbar` | UC-09 |
| `ToggleOnReactivate` | UC-03, 04 |
| `Activate` replace semantics | UC-02, 05, 06, 10 |
| `PushModal` / dispose | UC-12, 13, 17 |
| `ModalStack` depth | UC-12, 14, 20 |
| `Cancel()` / Escape | UC-14, 15, 18 |
| Suspend keeps state; tool owns its drawing | UC-12, 13, 16 |
| Single arbiter across engines | **UC-11** |
| `CancelsModalTool` (transparency) | UC-19, 20 |
| `ActionConcurrency` ×4 | UC-21, 22, 23, 24 |
| `ActionExclusivity` | UC-25 |
| `IActivity.Completion` (faults) | UC-26, 27 |
| `ActivityProgress` | UC-46, 47 |
| `ActivityVisibility.Auto` | UC-46 |
| `Activities` list | UC-49, 50 |
| `EntityActionExecution` ×2 | UC-32, 33 |
| `EntityActionGroup` ordering | UC-34 |
| AND-over-selection | UC-31 |
| Cross-surface parity + id rule | UC-28, 29, 30 |
| Non-ECS binding (ExCon) | UC-35 |
| Focus follows perspective | UC-36, 37, 38, 39 |
| Per-subsystem host | UC-38 |
| Context exposes only the legal surface | UC-41, 42, 45 |
| ECB record → tick playback | UC-43, 44 |

**Counts:** 50 cases — **41 H · 9 I · 0 V.**

> ⭐ **Zero V is the headline.** Every rule in this design is assertable without the Windows editor, which
> matters because this programme cannot run it. ⚠ *Visual confirmation is still required for the
> golden-path walk* — but no **rule** depends on a human looking at it.

## 11. Regression guards — defects found by this programme

| Case | Guards | Found |
|---|---|---|
| **UC-11** | two `_focusedGizmo` arbiters on one bus | `EditorSubsystem.cs:1122-1134` |
| **UC-06** | per-`Entity` injection leaves two tools alive | `DataDrivenGizmoSystem.cs:74` |
| **UC-21** | double *Mark Target* starts two concurrent picks | no guard exists today |
| **UC-26** | `async void` swallows handler faults | `:1462`, `:1479` ([UXI-17](UX_Issues.md#uxi-17)) |
| **UC-40** | `--mode all` first run hides 22 windows | [UXI-06](UX_Feature_Perspective_Restore.md) |
| **UC-03** | inconsistent repeat-click (`Edit`/`Route` toggle, `Measure`/`Rotate` do not) | `:3823-3893` |
