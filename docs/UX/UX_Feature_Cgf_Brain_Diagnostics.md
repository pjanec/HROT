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

> ⛔⛔ **SUPERSEDED `2026-08-25` by the slice-4 build** *(`CE-028`; see
> [`DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md`](../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md)
> §10.3)*. **The paragraph below named the wrong class and undercounted the systems.** 📐 Measured:
> **`CycloneIngressSystem` has ZERO production registrations** — CGF's ingress is
> **`CycloneNetworkIngressSystem`**, constructed at **12 production sites across 9 files in 6
> assemblies**, of which **five** are registrations on CGF. ⭐⭐ And **one of those five is purely
> CONTROL PLANE** *(`SlaveTimeTranslatorRegistration` registers its own ingress system holding only the
> three time translators)*. ⇒ ⭐⭐⭐ **the conclusion below is right and its reason is now sharper**: the
> freeze gate is handed to EVERY ingress system on the node, control-plane one included, and **only the
> per-translator `Category` stops that being [DQ30 A](Design_Question_30_Debug_Pause_Resume.md)'s
> deadlock.** ⚠ The closing *"verify at implementation time"* instruction was carried out; its answer is
> §10.3.

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

> ✅✅ **BUILT `2026-08-25`** — `CE-025`…`CE-027`, in `Hrot/Subsystems/Hrot.CGF/Debug/`. ⭐⭐ **This
> section's framing was the correct one and the slice design's `classDiagram` was not:** it drew the
> controller calling `MasterSyncController` directly, which is not buildable on a slave. 📄 The as-built
> is [`DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md`](../DESIGN_Cgf_Editor_Sharing_Slice4_Debug_PauseStep.md)
> §10, whose diagrams supersede it. ⭐ The one ADDITION beyond the table below: **with no participant,
> `RequestResume` applies locally and at once** — the mirror of §3c, without which an offline node could
> never be un-frozen.

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

### 5b. What CGF must construct for authoring — the verified diff

⭐ **No assembly work**: `Hrot.Editor.AiShared` is already on CGF's graph (`Hrot.CGF.csproj:43` →
`Hrot.Blueprints.Editor.csproj:33`), as is `Hrot.Network.Orchestration` (`:37`) and `Hrot.Presentation`.
**Everything below is a constructor call at CGF's composition root.**

| Service | Editor builds it at | CGF |
|---|---|:--:|
| `WindowManagerPerspectiveSwitcher` (`IPerspectiveSwitcher`) | `EditorSubsystem.cs:2008` | ❌ |
| `AiDocumentManager` | `:2161` — **one argument** | ❌ |
| `PerspectiveWorkspaceRegistrar` ×3 (BTree/HSM/Blueprint) | `:2121, 2137, 2152` | ❌ |
| `AssetCatalog` | `:2011` | ❌ |
| `ScenarioFileService` | `EditorApplication.cs:34` | ❌ |
| `ScenarioNewAssetService` (`INewAssetService`) | `Hrot.Editor/ScenarioNewAssetService.cs` | ❌ |
| `ComponentEditServiceBuilder().Build()` | `:2120` | ⭐ **already** (`CgfSubsystem.cs:554`) |
| `WindowManager` | received, not constructed | ⭐ same in CGF (`:665`) |
| `PreviewClusterOpHandler` | `EditorSubsystem.cs:447` | ❌ (§5d) |

⚠ **`ScenarioNewAssetService` and the Editor's asset services live in `Hrot.Editor`** — a project CGF does
**not** reference. ⇒ either those services move to a shared project, or CGF references `Hrot.Editor`.
**This is the only genuine packaging question left**, and it is smaller than it looks: the *windows* and the
*document manager* are already reachable; it is the **catalog/save service layer** that is Editor-housed.

### 5d. Behavior hot reload on CGF

| Piece | Where | Note |
|---|---|---|
| `QuickReloadService`, `QuickReloadResult` | `Hrot.Blueprints.Editor/Reload/` | ⭐ CGF already references this assembly |
| `HotReloadLogWindow` / `Model` | `Hrot.Blueprints.Editor/Debug/` | same |
| `BlueprintDebugSession.OnHotReloadBegin` / `OnHotReloadCompleted` (`:1507-1515`) | the reload↔debug handshake | reusable |

🔒 Behaviors are **CGF-only**, so authoring, reload and execution share one node — the same single-process
shape the Editor relies on. ⚠ **A reload while frozen must reload and STAY frozen** — route it through the
halt toggle (§3b) or it silently resumes the exercise.

### 5c. 🔒 Asset roots come from CONFIG — shared by CGF and the Editor ([ruling 67](UX_RESUME_INTERACTION.md))

> **User, 2026-08-14:** *"We need a **config file provided asset path** for the CGF as well as the Editor
> (**same shared code**), with **fallback to the repo source** as of now."*

**The problem, verified.** `EditorSubsystem.ResolveAiBehaviorsDir` (`:693-708`) walks up from
`CurrentDirectory` **and** `BaseDirectory` looking for **`Hrot.AI.Behaviors.csproj`**, then points the asset
write roots at the **source** tree (`:710-721`):

| `.csproj` found? | `_bpRootDir` | `_btreeJsonRootDir` / `_hsmJsonRootDir` |
|---|---|---|
| yes | `<sourceDir>/Assets/…` | `<sourceDir>/Assets/…` |
| **no** (a deployed node) | `BaseDirectory/Assets/Blueprints` | 🔴 **`null`** |

⇒ on a deployed node **BTree/HSM authoring has no root at all** — and this already binds the **Editor** to
running beside its checkout. **A shared defect, not a CGF-specific cost.**

#### 🔴 And there are already two competing path authorities

| | |
|---|---|
| `AssetRoots` (`Hrot.Editor.AiShared/Identity/AssetRoots.cs`) | doc: ***"Single authority for the two root families"***; resolves from `AppContext.BaseDirectory` (`:94-121`). **30 call sites across 12 production files** |
| `EditorSubsystem`'s private walk-up (`:693-721`) | **bypasses it entirely** for the write paths |

⇒ ⭐ **The "single authority" is already contradicted next door** — the seam law again. **Adding a config
file as a third mechanism would make it worse.** 🔒 **Put the config INTO `AssetRoots` and delete the walk-up.**

#### The resolution order — one implementation, both hosts

```
1. config file value            ← new; authoritative when present
2. source .csproj walk-up       ← today's behaviour, kept as the dev fallback (user: "as of now")
3. AppContext.BaseDirectory     ← today's AssetRoots default, last resort
```

| | |
|---|---|
| ⭐ **The config mechanism already exists — do not invent one** | `ClusterConfiguration.LoadFrom(filePath)` (`Hrot.Orchestrator/ClusterConfiguration.cs:50-77`) is **already a JSON config file loader**: returns `Default` when the file is absent, **throws** when it exists but cannot be read or deserialized. It already carries a path — `NasBasePath` (`:29`) — and `EditorBootstrap.ScenariosRoot` already reads it |
| ⚠ **Placement is the one open call** | `ClusterConfiguration` lives in **`Hrot.Orchestrator`**. Either CGF takes that reference (the Editor already does), or the config type moves somewhere neutral. **Decide at implementation; do not add a second config file** |
| ⚠ **`AssetRoots` is a `static class`** | so config must arrive via an explicit `Configure(...)` at composition, or the type becomes an injected provider. ⭐ **30 call sites / 12 files** — the static-with-`Configure` form keeps all of them compiling; the provider form is cleaner but ripples. 🔒 **Lean: `Configure` now, provider only if a second root set ever coexists in one process** |
| 🔒 **Fail loud on a configured-but-missing root** | a configured path that does not exist must **throw at startup**, matching `LoadFrom`'s own stance. Silently falling through to the walk-up would reintroduce *"it worked on the dev box"* |
| ✅ **Scenario saving is unaffected** | `EditorBootstrap.ScenariosRoot` = `ClusterConfiguration.Default.NasBasePath` — already config-driven, no disk walk |

⇒ 🔒 **This is a shared-infrastructure change, not a CGF feature.** It fixes the Editor's checkout dependency
in the same stroke, and it is the **prerequisite** for authoring on any deployed node.

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
