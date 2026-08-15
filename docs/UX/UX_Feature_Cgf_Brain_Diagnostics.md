# Feature design — CGF brain diagnostics **and authoring**

> **Design for [UXI-37](UX_Issues.md#uxi-37) · drafted 2026-08-14.**
> Scope from [rulings 59-64](UX_RESUME_INTERACTION.md); pause/resume semantics settled in
> **[Design Question 30](Design_Question_30_Debug_Pause_Resume.md)** (A-E all decided).
> **Status: ✅ designed — one scope: diagnostics AND authoring ([ruling 65](UX_RESUME_INTERACTION.md)).**
> ⚠ **Re-scoped 2026-08-14** ([Correction 48](UX_Tasks_Detail.md#corrections)): earlier drafts kept narrowing
> back to diagnostics and pushing authoring pieces to other issues. **They are the same code on one node.**

## 0. Prior art — nearly everything exists; almost nothing is wired

🔒 Index-first, then grep to confirm call sites ([rule 6e](UX_RESUME_INTERACTION.md)).

| Exists? | What | On CGF today |
|:--:|---|---|
| ✅ | `DataBreakpointManager` + `DataBreakpointSystem` + **`DebugSnapshotProvider`** (pre-tick snapshots), predicate + event-scanner compilers wired to the blueprint registry | ⭐ **already constructed and registered** (`CgfSubsystem.cs:555-568`) |
| ✅ | `IDataBreakpointManager` accessor | ⭐ **already exposed** (`:153`) |
| ✅ | `BehaviorDiagnosticsModule` · `BehaviorTraceLog` · blackboard renderers (`BrainBlackboard`, `Blackboard1024`, `BlueprintBlackboard{1024,4096,16384}`) | ⭐ **already registered** (`:277-326`) |
| ✅ | `Hrot.Diagnostics.Breakpoints` — **neutral** assembly, incl. `WatchPersistence` | ⭐ **already referenced** by CGF |
| ✅ | `AiWatchWindow`, `AiBreakpointsWindow`, `AiGraphCanvasWindow`, `InspectorWindow`, `DiagnosticsWindow` | ⭐ **`Hrot.Editor.AiShared` is ALREADY on CGF's build graph** — `Hrot.CGF.csproj:43` → `Hrot.Blueprints.Editor.csproj:33` → `Hrot.Editor.AiShared` ([Correction 49](UX_Tasks_Detail.md#corrections)). Nothing constructs them yet |
| ✅ | `IEngineDebugTimeController` — **neutral** (`Hrot.Blueprints.Core`); Editor drives it via `MasterSyncTimeControllerAdapter` → `MasterSyncController` | 🔴 **`CgfNoOpTimeController` — all three request methods EMPTY** (`:825-834`) |
| ✅ | Cluster pause/step protocol: `SwitchToDeterministic(roster)` · `Step(dt)` · `SwitchToContinuous()`; slave `BarrierPending`/`Stepping`, `SteppedSlaveController`, lockstep translator | 🔴 unused by CGF's debugger |
| ✅ | `IWindowRegistrar` | ⭐ **CGF already implements it**, and already builds `FdpEntityInspectorWindow`/`FdpEventBrowserWindow`/`ArchitectureDiagnosticsWindow` (`:685,719,724`) |
| ✅ | **`PreviewClusterOpHandler`** — snapshot/restore of the whole world | ⭐ **neutral assembly `Hrot.Network.Orchestration`, ALREADY referenced by CGF** (`Hrot.CGF.csproj:37`); nothing registers it |
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

### 1b. ⭐ The authoring stack is far smaller than "the authoring stack" sounds

| Window | Needs | Verdict |
|---|---|---|
| ⭐ **`AiWatchWindow`** | `(id, perspective, IDataBreakpointManager)` — **nothing else** | ✅ **nearly free** — CGF already exposes the manager (`:153`) |
| `AiBreakpointsWindow` | breakpoint manager family | ✅ cheap |
| **`AiGraphCanvasWindow`** | `AiDocumentManager` (`:230-232`) | ⇒ ⭐ **which needs only `IPerspectiveSwitcher`** — or even a plain `Action<string>` (`AiDocumentManager.cs:44-71`), plus an optional focus callback. **That is the whole dependency.** The Editor passes `WindowManagerPerspectiveSwitcher(windowManager)` (`EditorSubsystem.cs:2008,2161`), and CGF is already handed a `WindowManager` (`CgfSubsystem.cs:665`) |

🔒 **So "the document manager drags in the authoring stack" was overstated.** What authoring actually needs
beyond it is the **catalog + save** services (§5b) — and those are ordinary classes, not a subsystem.

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
> **not those carrying the scenario information**… they are important only for **checkpoint**, which is a
> different category from scenario authoring. The system supports also **sending initial state of mandatory
> ECS components on entity creation**, so scenario loading is fully covered and possible from the CGF node only."*

⚠ **Two checks I demanded are now BOTH withdrawn** ([Corrections 47](UX_Tasks_Detail.md#corrections),
[50](UX_Tasks_Detail.md#corrections)):

| Withdrawn | Why |
|---|---|
| *"a naive save emits `VehicleState`/`VehicleParams`/`NavState` as defaults"* | 🔴 wrong category — those are **checkpoint** state; and serialization is mask-driven, not registry-driven |
| *"confirm CGF passes the same component mask as SimHost"* | 🔴 **there is no caller-supplied mask on this path.** `ScenarioSerializer.Serialize(repo, header)` computes it itself: `repo.GetSaveableMask()` ∧ `repo.GetComponentMask(entity.Index)` (`:134,143-145`). `GetSaveableMask()` reads the **static `ComponentTypeRegistry` saveable tags** (`EntityRepository.Sync.cs:192-199`) ⇒ **the mask is global and identical on every node by construction** |

⭐ **And the save path is ordinary**: `ScenarioMenuCommands:126-133` → `IEditorLogic.SaveScenarioAs` →
`EditorApplication:179-186` → `ScenarioFileService.SaveScenario` (`:108-143`) → `Serialize` → `File.WriteAllText`.
`ScenarioFileService` lives in **`Hrot.Presentation`**, which CGF already references.

### 🔴 The ONE real obstacle to authoring on CGF: asset write paths need a source checkout

`EditorSubsystem.ResolveAiBehaviorsDir` (`:693-708`) walks up from `CurrentDirectory` **and**
`BaseDirectory` looking for **`Hrot.AI.Behaviors.csproj`**, and points the asset write roots at the **source**
tree when found (`:710-721`).

| Found | `_bpRootDir` | `_btreeJsonRootDir` / `_hsmJsonRootDir` |
|---|---|---|
| source `.csproj` present | `<sourceDir>/Assets/…` | `<sourceDir>/Assets/…` |
| **absent** (a deployed node) | falls back to `BaseDirectory/Assets/Blueprints` | 🔴 **`null`** |

⇒ 🔒 **On a deployed CGF node, blueprint authoring degrades to the output dir and BTree/HSM authoring has no
root at all.** ⚠ **This — not assemblies, not masks, not components — is what actually blocks authoring on a
production CGF node.** ⭐ Scenario saving is unaffected: it uses `EditorBootstrap.ScenariosRoot` =
`ClusterConfiguration.Default.NasBasePath` — **config-driven, not a disk walk**.

🔒 **Design consequence**: authoring on CGF needs an **explicit configured asset root**, not a walk-up
heuristic. ⚠ Note the same heuristic already makes the *Editor* dependent on running beside its checkout —
so this is a shared defect the CGF work would surface, not a CGF-specific cost.

### ✅ Preview — in scope, and it already restores

> **User:** *"I am not so sure about the preview feature… might be doable if we use distributed snapshot
> taking and restoring. Now in the editor this is probably made just in-process locally, but doing that
> across a cluster should be very feasible."*

⭐ **Verified, and better than the guess on every axis:**

| | |
|---|---|
| ✅ **It restores — it does not merely unload** | `PreviewClusterOpHandler.UnloadingPreviewCommit` calls **`_liveRepo.SyncFrom(_snap, includeTransient: true)`** and logs *"live repo rewound to snapshot"* (`:146-171`). The doc comment says *"effectively rewinding all changes made during the dry-run session"* |
| ✅ **The snapshot is a full in-memory world** | `new EntityRepository(); snap.SyncFrom(_liveRepo, includeTransient: true)` (`:138-140`) — RAM, not JSON. Loading preview **does not mutate the live world**; it only copies it |
| ⭐ **Neutral assembly, already referenced by CGF** | `Hrot.Network.Orchestration` — `Hrot.CGF.csproj:37`. **Nothing to move** |
| ✅ **It is already a cluster-op** | `IClusterOpHandler` with `PrepareAsync`/`Commit`/`Abort` (`:65-97`) — the two-phase shape the orchestrator already drives |
| 🔴 **But only the Editor registers it** | `EditorSubsystem.cs:447` inside `EditorPreviewController`. Other nodes register a **different** class, `ReferencePreviewHandler` (SimHost `NodeBootstrapper.cs:453`, Orchestrator `:235`, ExCon `:226`, IG `:254`); **CGF registers neither** |

> ⇒ 🔒 **Distributed preview = register `PreviewClusterOpHandler` on each participating node and let the
> master drive the existing cluster op.** ⭐ **The snapshot/restore the user worried about is already written
> and already neutral** — what is missing is registration and a roster, which is [ruling 66](UX_RESUME_INTERACTION.md)'s
> *"the Editor is a one-node cluster"* in yet another instance.

⚠ **Two things to settle when preview is built** (not blockers for this design): the live-repo snapshot is a
**full world copy in RAM** — cost scales with entity count, so measure before enabling it on a large exercise;
and `ReferencePreviewHandler` vs `PreviewClusterOpHandler` are two different preview notions on the same
cluster — **reconcile them rather than adding a third.**

## 6. Risks## 6. Risks## 6. Risks## 6. Risks

| | |
|---|---|
| 🔴 **`SlaveSyncController` inside a togglable group** | would re-introduce [DQ30 A](Design_Question_30_Debug_Pause_Resume.md)'s deadlock. **Check first, before anything else** |
| 🔴 **Step latch** | a step that re-enables the sim groups for more than one tick is a silent resume |
| ⚠ **k is expected small but unmeasured** ([ruling 64](UX_RESUME_INTERACTION.md)) | the zero-dt timer discontinuity and the stale-view window both rest on it — **measure it once** |
| ⚠ **DATA breakpoints ≠ NODE breakpoints** | what runs on CGF today fires on component data change; breaking on a BTree/HSM/blueprint **node** is the other kind and is the ② work |
| ⚠ **Breakpoint UI must not offer unobservable components** | [ruling 61](UX_RESUME_INTERACTION.md) limits CGF to owned-or-replicated components ⇒ [ruling 49](UX_RESUME_INTERACTION.md): **absent, not greyed** |
| 🔴 **Asset write roots resolve by walking up to a source `.csproj`** | `EditorSubsystem.cs:693-721`; on a deployed node BTree/HSM roots become **`null`**. The real blocker for authoring on CGF — and a **shared** defect the Editor already has |
| ⚠ **Preview snapshots the whole world into RAM** | `EntityRepository.SyncFrom(includeTransient: true)` — measure the cost before enabling on a large exercise |
| ⚠ **Two preview notions coexist** | `PreviewClusterOpHandler` (Editor) vs `ReferencePreviewHandler` (every other node) — reconcile, do not add a third |
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
| 37.17 | 🔴 Authoring on CGF uses an **explicitly configured asset root** — it does not depend on a source checkout being present | H |
| 37.18 | 🔒 A scenario authored on CGF **loads correctly** — initial component state travels with entity creation | I |
