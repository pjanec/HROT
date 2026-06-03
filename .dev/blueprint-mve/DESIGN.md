# Blueprint Full-Lifecycle MVE — Design

**Goal:** a thin, proven vertical slice of the whole blueprint loop — **load/author → compile → run/debug → save → hot-reload** — demonstrated **headlessly** in the ClusterRunner integration-test harness (so it's runnable in CI / by the dev loop), then surfaced as an editor **"spawn entity + run opened blueprint"** button for manual testing.

## Verified run pipeline (Instance Blueprints)
(Confirmed against code; the editor architect's summary holds.)
- **State tiers:** `BlueprintBlackboard1024/4096/16384` (header 32B + 16B slot entries + free-list payload). Multiple blueprints per entity via slots. (`FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/*`.)
- **Attach:** `BlueprintBlackboardPartitions.TryAttach(byte* mem, int blueprintId, int stateSize, ulong structureHash, out int payloadOffset)` (`…/Partitioning/BlueprintBlackboardPartitions.cs:83`). Overflow → ECB-add next tier; `BlueprintMaintenanceSystem` (BeforeSync) byte-copies up + removes old (`…/Systems/BlueprintMaintenanceSystem.cs`).
- **Tick:** `BlueprintTickSystem` (SystemPhase.Simulation) queries tier components, per slot resolves `BlueprintDefinition` from `BlueprintRegistry.TryGetById`, invokes the generated `Tick(span, view, ecb, entity, time, dt, instanceVersion)`; reload-reconciles on `StructureHash` mismatch (`…/Systems/BlueprintTickSystem.cs:46`).
- **World singletons:** `IsWorldSingleton` blueprints live in `EntityRepository.GetSingleton<TBB>()`, lazy-init/attached/ticked on first frame post-commit (`BlueprintTickSystem.TickWorldSingletons`).
- **Registry routes:** (i) build-time source generator emits `[BlueprintRegistrar]` per `*.bp.json` AdditionalFile → registers at startup; (ii) runtime `QuickReloadService` compiles a `.bp.json` (`BlueprintCompiler` Stages 1–8 + `InMemoryRoslynCompiler`) → ALC load → registrar scan → `AiHotReloadCoordinator` stages/commits into `BlueprintRegistry` (`BeginStaging`/`Add`/`CommitStaging`).

## Stage readiness (for the *current* AiShared editor)
| Stage | Infra | Wired to current editor | MVE action |
|---|---|---|---|
| Author | ✅ AiGraphCanvasWindow | ✅ | done |
| Compile | ✅ BlueprintCompiler/Roslyn/QuickReloadService | ❌ `_blueprintQuickReloadTrigger = null` (EditorSubsystem.cs:1873) | wire trigger |
| Run | ✅ BlueprintTickSystem + tiers + registry | ❓ confirm blueprint systems are in the ClusterRunner kernel schedule | MVE test + (if needed) ensure module loaded |
| Debug | ✅ BlueprintDebugSession (EditorSubsystem.cs:776), probes, LiveSessionRegistry (1604) | ⚠️ session exists; connect running instance → session → Watch/Breakpoints | wire/verify |
| Save | serialize exists (`BlueprintJsonServices.Serialize`) | ❌ no save command | implement (P0) |
| Hot-reload | ✅ AiHotReloadCoordinator (EditorSubsystem.cs:522) + staging/swap | ⚠️ file-watch path likely; editor-triggered depends on compile wiring | verify end-to-end |

## Existing headless harnesses (reuse)
- `Hrot.Blueprints.Tests/BlueprintTestFixture` — `CompileAndLoad`, `CreateEntity`, `AttachBlueprint`, `TickFrame(dt)`, `GetBlueprintState(asset,entity).TryGetField<T>` (minimal world + registry + BlueprintTick/Maintenance systems). Proven by `SingleSlotTickTests`.
- `Hrot.ClusterRunner.Integration.Tests/EditorHarness` — full `ModuleHostKernel` (SimHost + CGF + ScenarioEditor), `PumpFrames(n)`, `PumpUntil(cond)`, exposes `Repo`/`Bus`/`Kernel`/`Editor`/`Preview`. Headless, no DDS/display.
- Simplest observable instance asset: **InstanceCounter** (`Variables: Count:int`, tick increments) — assert `Count` 0→1→2.

## MVE plan (thin slice, ordered)
- **MVE-01 (run, this batch):** headless integration test in the **ClusterRunner harness** that creates an entity, attaches + runs an instance blueprint in the *real* sim (verify `BlueprintTickSystem`/`BlueprintMaintenanceSystem`/tier components are in the kernel schedule; if not, ensure the blueprint toolkit module is loaded), pumps frames, and asserts an observable state change. Extract a reusable **`BlueprintRunHarness`/helper** (“attach blueprint X to a new entity, run”) that the editor button will later call. Also a world-singleton variant.
- **MVE-02 (compile-on-demand):** drive `QuickReloadService` to compile a `.bp.json` at test time → register → run (proves compile→run without `dotnet build`).
- **MVE-03 (save):** implement editor Save (`BlueprintJsonServices.Serialize` → disk; validate pin round-trip / DEBT-BCP-005); headless test: load → mutate → save → reload-from-disk identical-or-expected.
- **MVE-04 (hot-reload):** running instance + recompile changed `.bp.json` → `AiHotReloadCoordinator` commit → assert the live instance picks up the change (soft-reload preserves state where hash unchanged).
- **MVE-05 (debug):** breakpoint/watch on a running instance via `BlueprintDebugSession` observed headlessly.
- **MVE-06 (editor button):** "Run Opened Blueprint on a Test Entity" in the editor, reusing the MVE-01 helper.

## Constraints
Projection-only for loaded assets stays (byte-stability). GizmoMap.Contracts 0.2.2; don't touch Hrot.IG/DDS. Reuse existing harnesses; don't reimplement runtime.
