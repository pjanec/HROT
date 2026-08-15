# Feature design — CGF brain diagnostics and runtime debugging

> **Design for [UXI-37](UX_Issues.md#uxi-37) · drafted 2026-08-14.**
> Scope from [rulings 59-64](UX_RESUME_INTERACTION.md); pause/resume semantics settled in
> **[Design Question 30](Design_Question_30_Debug_Pause_Resume.md)** (A-E all decided).
> **Status: ✅ designed — [Q1 closed by ruling 65](UX_RESUME_INTERACTION.md); §5b adds the optional authoring tier.**

## 0. Prior art — nearly everything exists; almost nothing is wired

🔒 Index-first, then grep to confirm call sites ([rule 6e](UX_RESUME_INTERACTION.md)).

| Exists? | What | On CGF today |
|:--:|---|---|
| ✅ | `DataBreakpointManager` + `DataBreakpointSystem` + **`DebugSnapshotProvider`** (pre-tick snapshots), predicate + event-scanner compilers wired to the blueprint registry | ⭐ **already constructed and registered** (`CgfSubsystem.cs:555-568`) |
| ✅ | `IDataBreakpointManager` accessor | ⭐ **already exposed** (`:153`) |
| ✅ | `BehaviorDiagnosticsModule` · `BehaviorTraceLog` · blackboard renderers (`BrainBlackboard`, `Blackboard1024`, `BlueprintBlackboard{1024,4096,16384}`) | ⭐ **already registered** (`:277-326`) |
| ✅ | `Hrot.Diagnostics.Breakpoints` — **neutral** assembly, incl. `WatchPersistence` | ⭐ **already referenced** by CGF |
| ✅ | `AiWatchWindow`, `AiBreakpointsWindow`, `AiGraphCanvasWindow`, `InspectorWindow`, `DiagnosticsWindow` | 🔴 in `Hrot.Editor.AiShared` — **not referenced by CGF** |
| ✅ | `IEngineDebugTimeController` — **neutral** (`Hrot.Blueprints.Core`); Editor drives it via `MasterSyncTimeControllerAdapter` → `MasterSyncController` | 🔴 **`CgfNoOpTimeController` — all three request methods EMPTY** (`:825-834`) |
| ✅ | Cluster pause/step protocol: `SwitchToDeterministic(roster)` · `Step(dt)` · `SwitchToContinuous()`; slave `BarrierPending`/`Stepping`, `SteppedSlaveController`, lockstep translator | 🔴 unused by CGF's debugger |
| ✅ | `IWindowRegistrar` | ⭐ **CGF already implements it** |
| ✅ | Brain systems in `TogglableInputGroup` / `TogglableSimulationGroup` | ⭐ **already composed** (`:330-334`) — the halt/step actuator |

⇒ ⭐ **This is a wiring design, not a capability design.** The one genuinely new artefact is a translator
category (§4); everything else is an adapter, a registration, or an assembly reference.

## 1. ⭐ Two findings that shape the design

### 1a. The control-plane / world-state split **already exists physically**

⚠ [DQ30 C](Design_Question_30_Debug_Pause_Resume.md) asked for the ingress translators to be categorized.
**Verification shows CGF already pumps them in two different places:**

| Class | Pumped by | While the sim is halted |
|---|---|:--:|
| **Time mode + lockstep** | ⭐ **by hand in the app loop** — `CgfApplication.cs:259-262`, `PollIngress(null!, null!)` (null ECS args: they touch no ECS) | ✅ **keeps running** — outside the ECS pipeline entirely |
| **World state** | `CycloneIngressSystem` — an `[UpdateInPhase(SystemPhase.Input)]` **ECS system** holding `INetworkTranslator[]` | gated with the pipeline |

> ⇒ 🔒 **Resume is safe by construction.** The `SwitchTimeModeEvent` that un-freezes CGF arrives through a
> hand-pumped translator, so [DQ30 A](Design_Question_30_Debug_Pause_Resume.md)'s deadlock **cannot** occur
> through the ingress path — the two classes are already on different pumps.

🔴 **But `CycloneIngressSystem` is all-or-nothing**: one system, one array, one `Execute`. It is **not** part of
`CgfLogicPack`'s togglable groups, so freezing the brain does **not** stop it — and gating the whole system
would also stall any control-plane traffic registered inside it (orchestration commands, lifecycle acks).
⇒ **that is precisely why the per-translator category is still needed** (§4). ⚠ **Verify at implementation
time which translators CGF actually registers into `CycloneIngressSystem`** — the mix decides how much the
category buys.

### 1b. 🔴 "Show the asset graph" is coupled to the authoring stack

| Window | Needs | Verdict |
|---|---|---|
| ⭐ **`AiWatchWindow`** | `(id, perspective, IDataBreakpointManager)` — **nothing else** | ✅ **nearly free** — CGF already exposes the manager |
| `AiBreakpointsWindow` | breakpoint manager family | ✅ cheap |
| 🔴 **`AiGraphCanvasWindow`** | **`AiDocumentManager`** (`:230-232`) — open documents, tab bar, activate/close, dirty tracking | 🔴 **drags in the document/authoring stack** |

⚠ **[Ruling 59](UX_RESUME_INTERACTION.md) called authoring an optional bonus and graph viewing required** —
but as built, **the graph canvas cannot be had without the document manager**. See [Q1](#q1--the-graph-canvas-drags-in-the-authoring-stack).

## 2. The three capability layers

| Layer | Ruling | Content |
|---|---|---|
| **① Inspect** (required) | 59 | asset graphs + runtime activity overlays, runtime inspectors, **watch windows**, behavior trace |
| **② Break** (bonus) | 59, 62 | breakpoint hit ⇒ **freeze the whole cluster**; inspect a coherent snapshot |
| **③ Step** (bonus) | 63 | within-tick: walk the node recording, **no cluster**. Tick crossing: `Step(dt)` on the master, **whole cluster** |

🔒 **They ship in this order.** ① is registration-only and useful alone; ② needs §3; ③ needs ② plus the
step actuator (§3b).

## 3. The freeze — replace the no-op with a master-driven adapter

### 3a. `CgfClusterDebugTimeController : IEngineDebugTimeController`

Mirrors `MasterSyncTimeControllerAdapter`, but CGF is a **slave**: it cannot switch modes, only **request**.

| Method | Behaviour |
|---|---|
| `RequestPause()` | ① **halt CGF's sim groups immediately** (exact — [ruling 61](UX_RESUME_INTERACTION.md)) ② **request cluster deterministic mode** via the orchestration/control path to the master |
| `RequestResume()` | request `SwitchToContinuous`; CGF's own resume arrives as `SwitchTimeModeEvent` → `ApplyResume` → **`ApplyTimeSnap`** (zero-dt snap, [DQ30 B](Design_Question_30_Debug_Pause_Resume.md)) and re-enables the sim groups |
| `RequestStepOneTick()` | request `Step(dt)` on the master; on the granted step, **re-enable the sim groups for exactly one tick** (§3b) |
| `IsPausedByDebugger` | ⚠ **already live today** (`_bpManager?.IsPaused`) — do not assume it means the clock stopped |

🔒 **The halt scope is the simulation systems only, never the kernel** ([DQ30 A](Design_Question_30_Debug_Pause_Resume.md)) —
the kernel must keep ticking so `SlaveSyncController.Update()` drains the mode switch. ⭐ `TogglableInputGroup`
/ `TogglableSimulationGroup` are exactly that switch and are **already composed**.

⚠ **Verify the toggles do not gate `SlaveSyncController` itself.** If the slave controller sits inside a
togglable group, [DQ30 A](Design_Question_30_Debug_Pause_Resume.md)'s deadlock returns through the back door.
**This is the single highest-risk check in the design.**

### 3b. The toggle is the **step actuator**, not just the halt

```
frozen: sim groups OFF ──▶ step granted ──▶ groups ON for 1 tick ──▶ OFF again
```

⚠ It must be **exactly one** tick. A latched re-enable that survives a frame boundary silently turns a step
into a resume — and the operator would read the resulting state as "one step".

### 3c. When the freeze request is unanswered

🔒 [Ruling 64](UX_RESUME_INTERACTION.md): **halt CGF locally anyway and say the cluster is still running.**
⭐ **Except in the documented no-DDS mode** (`CgfApplication.cs:107`) — there no cluster exists, the local halt
**is** complete and correct, and **no warning is shown**; a permanent warning in a supported mode is
[ruling 49](UX_RESUME_INTERACTION.md)'s dead affordance in another costume.

## 4. Ingress categorization

```csharp
public enum TranslatorClass { WorldState = 0, ControlPlane = 1 }   // default = WorldState
```

| | |
|---|---|
| **Where** | on `IDescriptorTranslator` / `INetworkTranslator`, beside `TopicName`, `DescriptorOrdinal`, `Direction` |
| **Who reads it** | `CycloneIngressSystem.Execute` skips `WorldState` translators while the debugger holds a freeze |
| 🔒 **Fail-safe default = `WorldState`** | a miscategorised control-plane translator fails **loudly** (*"resume/abort does not work"*); the opposite default **silently** leaks live world data into a frozen snapshot |
| ⭐ **Resume never depends on this** | it arrives through the hand-pumped time translators (§1a) — the category protects **orchestration**, not the un-freeze |
| ⚠ **UI consequence** | while frozen, CGF's remote-entity view is **stale by up to k ticks**; the paused view must say so rather than imply live data |

## 5. Windows on CGF

| Step | Work |
|--:|---|
| 1 | CGF references `Hrot.Editor.AiShared` (it already pulls that project's heavy deps — `Fdp.Presentation`, `Hrot.Presentation` — and **already references `Hrot.Blueprints.Editor`**, `CgfSubsystem.cs:54`) |
| 2 | Register `AiWatchWindow` + `AiBreakpointsWindow` through the existing `IWindowRegistrar`, perspective `"CGF"` |
| 3 | Menu + toolbar entries via [UXI-35](UX_Feature_Shell_Parity.md)'s shared registries — **CGF has neither today**, so UXI-35 is a soft prerequisite for discoverability |
| 4 | The AI-debug transport icons already exist in the Editor toolbar; ⚠ they are **permanently dark** because `IDebugSessionRegistry.ActiveSession` is never set in production (`.dev/toolbar-debug-activate`) — [ruling 49](UX_RESUME_INTERACTION.md) applies: **fix or omit**, never ship them grey |

### ✅ Q1 CLOSED — the authoring stack is welcome on CGF ([ruling 65](UX_RESUME_INTERACTION.md))

> **User, 2026-08-14:** *"Bringing editing machinery onto a runtime node is perfectly OK. It would not hurt at
> all if also the **behavior hot reload and save** features could be optionally enabled as well. The behaviors
> are **CGF only**, so all that happens on a single node, similarly to the editor."*

🔒 **Option (a): reference `AiDocumentManager` and take the graph canvas as built.** No read-only viewer, no
parallel implementation. ⚠ **And read-only is no longer a constraint** — editing is explicitly welcome.

## 5b. The optional authoring tier on CGF

### Behavior hot reload + save — ⭐ cheaper than it sounds

| Piece | Where | On CGF |
|---|---|---|
| `QuickReloadService` · `QuickReloadResult` | `Hrot.Blueprints.Editor/Reload/` | ⭐ **CGF already references this assembly** (`CgfSubsystem.cs:54`) |
| `HotReloadLogWindow` · `HotReloadLogModel` | `Hrot.Blueprints.Editor/Debug/` | same assembly |
| `BlueprintDebugSession.OnHotReloadBegin` / `OnHotReloadCompleted` (`:1507-1515`) | already the reload↔debug handshake | reusable as-is |

🔒 **The user's reasoning holds and the code agrees**: behaviors are **CGF-only**, so authoring, reload and
execution are all on **one node** — the same single-process shape the Editor already relies on. ⇒ **enabling
this is a registration, not a distributed problem.**

⚠ **One interaction to honour**: a hot reload during a **freeze** must not silently resume the sim. Route it
through the same toggle that owns the halt (§3b), and treat reload-while-paused as **reload, stay paused**.

### ✅ Scenario editing from CGF — cleared ([ruling 66](UX_RESUME_INTERACTION.md))

> **User, 2026-08-14:** *"The components that are missing on the CGF node and present on SimHost are usually
> **not those carrying the scenario information**. At runtime these are filled from commands coming from CGF.
> They are important only for taking and restoring a snapshot of current immediate simulation state
> (**checkpoint**), which is **a different category from scenario authoring**. The system supports also
> **sending initial state of mandatory ECS components on entity creation** to SimHost, so the scenario loading
> process is fully covered and possible from the CGF node only."*

⚠ **An earlier draft demanded a save-time completeness check and a SimHost-vs-CGF serializer diff as a gate.
Both are withdrawn** ([Correction 47](UX_Tasks_Detail.md#corrections)) — the premise was wrong twice over:

| The claim | Why it fails |
|---|---|
| *"a naive save emits `VehicleState`/`VehicleParams`/`NavState` as defaults"* | 🔴 **`SerializeEntity(repo, entity, resolver, BitMask512 componentMask)` is MASK-DRIVEN** — the caller states which components are in scope — and `AutoSerializer.TryExtract(repo, entity, bit, resolver)` is **per entity, per bit**. `RegisterComponent<T>()` registers a **type with the world**; it is neither entity-level presence nor serializer scope |
| *"CGF-unowned components threaten scenario fidelity"* | 🔒 **wrong category.** Those components carry **immediate simulation state**, which matters for **checkpoint** (snapshot/restore), not for scenario authoring. At runtime they are filled from commands originating on **CGF** |

⭐ **And the load path is already covered**: `CreateEntityRequestSystem` compiles `InitialAttributesJson` into
components, splitting `SimTransform`/`SimVelocity` into typed spawn fields and the rest into a fallback list
(`:225-245`) ⇒ **initial ECS component state travels with entity creation**, so a scenario can be loaded
**from CGF alone**.

⇒ 🔒 **Scenario editing on CGF is NOT a gated third tier.** It joins the authoring tier. ⚠ **What remains is a
one-line check, not a programme**: confirm the scenario save passes the **same component mask** on CGF as on
SimHost — the mask is the contract, and it is chosen by the caller.

### ⚠ Preview — the one genuinely open item

> **User:** *"I am not so sure about the **preview** feature, which in the editor is not the same as full
> scenario loading, but might be also doable if we use **distributed snapshot taking and restoring**. Now in
> the editor this is probably made just in-process locally, but doing that across a cluster should be very
> feasible."*

⭐ **Better than expected — preview is ALREADY a cluster operation.** `EditorPreviewController`
(`EditorSubsystem.cs:439-467`) drives **`PreviewClusterOpHandler`** with `TriggerLoadingPreview()` /
`TriggerUnloadingPreview()`, and brackets it with `MasterSyncController.SwitchToContinuous()` on enter and
**`SwitchToDeterministic(new HashSet<int>())`** on exit.

> ### ⭐ The regularity worth naming: **the Editor is a one-node cluster**
>
> That **empty slave roster** is the same trick `MasterSyncTimeControllerAdapter` uses for the debug pause
> ([§3a](#3a-cgfclusterdebugtimecontroller--iengine­debugtimecontroller)). **Two independent features already
> run the cluster protocol with a roster of zero.** ⇒ making either work distributed is **supplying the real
> roster**, not building a distributed version.

| ⚠ Still open for preview | |
|---|---|
| What `PreviewClusterOpHandler` does to the world on enter/exit | if it clears and repopulates, a distributed preview needs the same on every node — that **is** the snapshot/restore the user names |
| Whether exit **restores** prior state or just unloads | `TriggerUnloadingPreview` suggests unload; if there is no restore, "preview" is destructive today and distributing it inherits that |

🔒 **Not designed here** — preview is a scenario-authoring feature, not a diagnostics one. Recorded so the
`PreviewClusterOpHandler` finding is not lost, and left to the [UXI-33](UX_Issues.md#uxi-33) authoring session.

## 6. Risks## 6. Risks## 6. Risks

| | |
|---|---|
| 🔴 **`SlaveSyncController` inside a togglable group** | would re-introduce [DQ30 A](Design_Question_30_Debug_Pause_Resume.md)'s deadlock. **Check first, before anything else** |
| 🔴 **Step latch** | a step that re-enables the sim groups for more than one tick is a silent resume |
| ⚠ **k is expected small but unmeasured** ([ruling 64](UX_RESUME_INTERACTION.md)) | the zero-dt timer discontinuity and the stale-view window both rest on it — **measure it once** |
| ⚠ **DATA breakpoints ≠ NODE breakpoints** | what runs on CGF today fires on component data change; breaking on a BTree/HSM/blueprint **node** is the other kind and is the ② work |
| ⚠ **Breakpoint UI must not offer unobservable components** | [ruling 61](UX_RESUME_INTERACTION.md) limits CGF to owned-or-replicated components ⇒ [ruling 49](UX_RESUME_INTERACTION.md): **absent, not greyed** |
| ⚠ **Checkpoint is a separate feature from scenario save** | the CGF-unowned components matter for **snapshot/restore of immediate simulation state**, not for authoring ([ruling 66](UX_RESUME_INTERACTION.md)). Do not let a checkpoint requirement leak into the scenario-save design, or the reverse |
| ⚠ **Hot reload during a freeze** | must reload and **stay paused** — routing it around the halt toggle would silently resume the exercise |
| ⚠ **A destructive freeze during a live exercise** | [ruling 59](UX_RESUME_INTERACTION.md) accepts it, but the operator must know: arming a breakpoint on a live cluster is a **cluster-wide stop**. ⇒ confirm at arm time via [UXI-16](UX_Feature_Modal_Surfaces.md), resolved at the origin ([ruling 53](UX_RESUME_INTERACTION.md)) |

## 7. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 37.1 | 🔒 A breakpoint hit halts CGF's **sim groups** while the **kernel keeps ticking** — `SlaveSyncController.Update` still runs | H |
| 37.2 | 🔴 With the sim halted, a master `SwitchTimeModeEvent` is **still drained** — the deadlock guard | H |
| 37.3 | A breakpoint hit **requests cluster deterministic mode**; all nodes stop at the **same barrier tick** | I |
| 37.4 | 🔒 On resume, CGF **snaps** its sim baseline to the master snapshot — **no ticks replayed**, breakpoint does **not** re-fire | H |
| 37.5 | 🔒 While frozen, **world-state** translators are not polled and **control-plane** ones are | H |
| 37.6 | 🔒 An uncategorised translator is treated as **world state** (fail-safe default) | H |
| 37.7 | 🔒 A granted step advances the sim groups by **exactly one tick**, then re-freezes | H |
| 37.8 | Within-tick stepping walks the node recording with **no cluster round trip** and **no re-execution** | H |
| 37.9 | 🔒 Freeze request unanswered ⇒ CGF halts locally **and says the cluster is still running** | H |
| 37.10 | 🔒 In **no-DDS** mode the local halt raises **no warning** — it is normal operation | H |
| 37.11 | The Watch window on CGF reads the **live** `IDataBreakpointManager` and survives a freeze/resume cycle | I |
| 37.12 | 🔒 The breakpoint UI offers **only** components CGF owns or receives — others are **absent** | H |
| 37.13 | Arming a breakpoint on a live cluster **confirms first**, resolved at the origin | H |
| 37.14 | 🔒 CGF's paused remote-entity view is **labelled stale**, not presented as live | I |
| 37.15 | Behavior **hot reload on CGF** succeeds and the running brains pick up the new asset | I |
| 37.16 | 🔒 A hot reload **while frozen** leaves the sim **still frozen** — reload must not act as a resume | H |
| 37.17 | A scenario saved on CGF and one saved on SimHost use the **same component mask** | H |
| 37.18 | 🔒 A scenario authored on CGF **loads correctly** — initial component state travels with entity creation | I |
