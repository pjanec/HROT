# Blueprint Subsystem — Architecture Document (Slice 1) — v1.2

> **Status:** Architect-blessed v1.1 baseline + Q-OPEN-A/B/C resolutions + AI Primitives, channel-command authoring, and engine-direct interface model. All inline patches and Final Resolutions integrated; Q-OPEN-D and Q-OPEN-E resolved.
> **Audience:** Senior engineer (rationale paragraphs); implementation agent (spec subsections).
> **Scope:** Slice 1 architecture only. Detailed designs of compiler internals, codegen, runtime adapter, test harness, and editor follow as separate documents after this is approved.
> **Engine:** Hrot (game) on FDP (ECS toolkit).
> **Supersedes:** v1.1 + v1.1 addendum.

---

## Changelog vs v1.1

Three classes of change:

**A. Q-OPEN resolutions (from v1.1 addendum)**
- `AddEmptyComponent` ECB opcode confirmed (§13.5).
- Tier upgrade now via dedicated `BlueprintMaintenanceSystem` in `SystemPhase.BeforeSync` (§6.9).
- Generator output filenames include the `BlueprintId` hash: `{SanitizedName}_{BlueprintId:X8}_Bp.g.cs` (§7.4).

**B. AI primitives, channel commands, condition correctness**
- **NEW dispatch kind**: `AiPrimitive` (replaces `BehaviorAction`). One graph can host as BTree action, BTree condition, HSM action, HSM guard, and/or as a callable from other Blueprints, by declaring `hostings`.
- **`intent` flag** (`Action` | `Condition`) on AI primitives, with strict validator rules. **Conditions cannot be latent.**
- **Wait-node lowering is dispatch-aware**: AiPrimitive emits `return NodeStatus.Running`; Instance emits `BlueprintLatentCursor` switch.
- **AI primitive working state** lives in `Blackboard1024` (engine's existing component), not `BlueprintBlackboard*`.
- **Slice 1 constraint**: one AiPrimitive working-state Blueprint per entity (`Blackboard1024` partition allocator deferred to Slice 2).
- **Channel Command Catalog** + **Wait Primitive Catalog**: new authoring catalogs alongside the Engine Event Catalog. Slice 1 hand-curated; Slice 2 attribute-driven.

**C. Engine-direct interface model (substantial reshuffle)**
- **No `IBlueprint*` wrapper interfaces.** Generated code and Blueprint runtime systems use real Fdp.Core types directly: `ISimulationView`, `IEntityCommandBuffer`, `EntityRepository`, `Entity`, etc.
- **`Hrot.Blueprints.Core` references `Fdp.Core`** (was zero-engine-dependency in v1.1; that was the wrong boundary).
- **`Hrot.Blueprints.Engine` adapter assembly dropped.** Runtime systems live in `Fdp.Toolkits.Blueprints` directly.
- Mock implementations target the same Fdp.Core interfaces. The mock contract = the engine contract.

The net effect: simpler assembly graph, no impedance mismatch at integration, and the test harness enforces the engine's real phase/threading rules.

---

## 1. Goals and Non-Goals

### 1.1 Goals

A Blueprint-like visual scripting subsystem for the Hrot/FDP engine, with:

- **Graph-authored behavior** with semantics borrowed selectively from Unreal Blueprints (typed pins, exec wires, pure/impure nodes, events, custom events, latent operations).
- **C# code generation** via Roslyn incremental source generator, compiled into the existing hot-reloadable assembly.
- **Three dispatch kinds** supported uniformly:
  - **Library** — stateless utility functions.
  - **AiPrimitive** — single-method graph hosted by BTree and/or HSM (replacing v1.1's BehaviorAction with multi-host capability).
  - **Instance** — entity-bound (or world-singleton) script with state, events, optional tick, latent execution.
- **Multi-Blueprint per entity** for Instance dispatch via partition allocator on `BlueprintBlackboard*`. AI primitives use `Blackboard1024` directly (one per entity in Slice 1).
- **Cross-Blueprint composition** via declared `callablePeers` — synchronous in-frame calls between Blueprints on the same entity.
- **Channel command authoring** — visual "Command Channel" nodes (e.g., `Locomotion / MoveTo`) compile to the engine's CQRS pattern (`ActiveAction`, `Params`, `ActionInstanceId++`). Removes the most common BTree-authoring boilerplate.
- **Wait primitive authoring** — visual "Wait For Channel" / "Wait For Event" nodes, with dispatch-aware lowering.
- **Zero-allocation runtime hot path.** Generated code follows engine idioms exactly.
- **Hot reload** plugged into existing `AiHotReloadCoordinator`.
- **State preservation across reload** via per-slot structure-hash comparison.
- **Replay-safe by construction.**
- **Debug strategy B + C** from day one (native .NET debugger via PDB, Blueprint debug protocol via probes).
- **Test harness** with xUnit, mock world implementing real Fdp.Core interfaces, in-memory compile-load-run-reload cycle.
- **Minimal ImGui editor** via StructEdit and engine's `WindowManager`.

### 1.2 Non-Goals (Deferred to Slice 2+)

- Visual node-graph canvas editor — Slice 1 uses StructEdit forms over JSON.
- Macros, interfaces, animation graphs, UI graphs, timelines, RPC nodes.
- Cross-entity dispatcher calls (Slice 1: `Target != Self` is a validator error).
- Save/load authoring story for `BlueprintBlackboard*` state (the bytes save automatically via the scenario serializer; typed access from save files is Slice 2).
- Map/Set containers (Array only in Slice 1).
- Multi-thread/worker-thread Blueprint graphs.
- Refactoring API (promote-to-variable, collapse-to-function).
- Visual debugger UI (canvas-aware) — protocol exists; UI is Slice 3.
- Defragmentation pass for blackboard payloads.
- `[BlueprintExposedEvent]` / `[BlueprintExposedChannelCommand]` attribute-driven catalogs — Slice 1 uses hand-curated catalogs; Slice 2 evolves.
- **Partition allocator on `Blackboard1024`** for multi-AiPrimitive working-state per entity (Slice 1 = one AiPrimitive working-state Blueprint per entity).
- Multiple world-singleton Blueprints per tier.
- Integration test harness with `clusterop` scripts — Slice 1 ships unit-level tests only.

### 1.3 Slice 1 Done = Definition

Demonstrated by passing automated unit tests and a working ImGui session:

1. A `.bp.json` file compiles via Roslyn generator into `Hrot.AI.Behaviors.dll` alongside hand-written AI code.
2. Same `.bp.json` compiled in-memory via compiler library runs identically against mocks (test harness path).
3. Editing a `.bp.json` and rebuilding triggers `AiHotReloadCoordinator`; instances continue with preserved state when structure-hash unchanged, hard-reset when changed. Multi-slot Instance blackboards reconcile per-slot.
4. .NET debugger can step through generated C# (PDB + EmbeddedSource).
5. Blueprint debug protocol can breakpoint on node, pause, report pin values, resume.
6. All three dispatch kinds execute end-to-end: Library called from C#; AiPrimitive registered into `BehaviorRegistry` and tickable via BTree; Instance ticking via `BlueprintTickSystem`.
7. **AiPrimitive with `hostings: ["BTreeAction", "HsmAction"]`** runs identically in both subsystems from one authored graph.
8. **AiPrimitive with intent `Condition`** rejected by validator if it contains latent nodes.
9. **Channel Command node** (e.g., `Locomotion/MoveTo`) compiles to the correct CQRS write sequence; **Wait For Channel** node compiles to AiPrimitive-style `Running` return *or* Instance-style cursor switch depending on dispatch.
10. Two Instance Blueprints on the same entity call each other synchronously via `callablePeers`; state is isolated in distinct partition slots.
11. World-singleton Instance Blueprint runs against `SetSingletonUnmanaged<BlueprintBlackboard1024>` storage.
12. ImGui editor lists assets, edits via StructEdit, triggers compile-and-reload, displays diagnostics, shows runtime instance state per-slot, exposes step/breakpoint controls.
13. The MoveToAndFire AiPrimitive demo runs end-to-end under both BTree and HSM hostings from a single authored asset, demonstrating channel commands + latent waits + dual-hosting.

---

## 2. System Overview

### 2.1 The picture in one diagram

```mermaid
graph TB
    subgraph "Authoring"
        AUTH[.bp.json files]
        ED[ImGui Editor<br/>StructEdit forms]
    end

    subgraph "Hrot.AI.Behaviors.csproj — the single reloadable DLL"
        ADDFILES[AdditionalFiles: .bp.json]
        HANDCODE[Hand-written .cs<br/>BTree, HSM, [SharedAi*] methods]
        GEN[Roslyn generators:<br/>Hrot.Blueprints.Generators<br/>Fdp.Toolkits.Analyzers]
        ROSLYN[Roslyn compile<br/>PE + Portable PDB]
        DLL[Hrot.AI.Behaviors.dll]

        ADDFILES --> GEN
        HANDCODE --> ROSLYN
        GEN --> ROSLYN
        ROSLYN --> DLL
    end

    subgraph "Engine — runtime"
        AHRC[AiHotReloadCoordinator<br/>refactored: attribute-driven scan]
        BREG[BehaviorRegistry<br/>existing]
        BPREG[BlueprintRegistry<br/>NEW in Fdp.Toolkits.Blueprints]
        HSMD[HsmActionDispatcher<br/>existing]

        DLL -.file watch.-> AHRC
        AHRC --> BREG
        AHRC --> BPREG
        AHRC --> HSMD
    end

    subgraph "Tick"
        BTREE[BTreeTickSystem<br/>existing]
        HSMTICK[HsmTickSystem<br/>existing]
        BPTICK[BlueprintTickSystem<br/>NEW]
        BPMAINT[BlueprintMaintenanceSystem<br/>NEW, BeforeSync]

        BREG --> BTREE
        BREG --> HSMTICK
        BPREG --> BPTICK
    end

    subgraph "Data"
        BB[BrainBlackboard 100B params<br/>existing]
        BB1024[Blackboard1024<br/>existing - now used by AiPrimitive working state]
        BPBB[BlueprintBlackboard1024/4096/16384<br/>NEW partition-allocated]

        BTREE --> BB
        BTREE --> BB1024
        BPTICK --> BPBB
        BPMAINT --> BPBB
    end

    AUTH --> ED
    ED -.compile&reload.-> ROSLYN

    style ADDFILES fill:#fff4e1
    style BPREG fill:#fff4e1
    style BPTICK fill:#fff4e1
    style BPMAINT fill:#fff4e1
    style BPBB fill:#fff4e1
    style GEN fill:#fff4e1
```

Five new components total in `Fdp.Toolkits.Blueprints`: the registry, two systems, the three blackboard tiers, and the partition allocator helpers.

### 2.2 Key terms

- **Asset** — A `.bp.json` file. One Blueprint, one asset.
- **Dispatch kind** — `Library` | `AiPrimitive` | `Instance`.
- **AiPrimitive intent** — `Action` (returns Success/Failure/Running) | `Condition` (returns Success/Failure only, no latent).
- **Hosting** — Where an AiPrimitive can run: `BTreeAction`, `BTreeCondition`, `HsmAction`, `HsmGuard`, `BlueprintCall`. Asset declares which.
- **BlueprintId** — Runtime `int`. Deterministic FNV-1a 32-bit of asset Guid.
- **StructureHash** — Deterministic 64-bit hash of an asset's state layout.
- **Slot** — One Instance Blueprint's state region inside a `BlueprintBlackboard*` component.
- **Partition allocator** — Free-list memory manager inside `BlueprintBlackboard*` that assigns slots.
- **Callable peers** — Per-asset list of peer Blueprints this asset can synchronously call on the same entity.
- **Engine Event Catalog** — Registered engine event types exposed to Blueprint authoring.
- **Channel Command Catalog** — Registered (channel, ActionId) pairs exposed as commandable nodes.
- **Wait Primitive Catalog** — Registered kinds of latent waits (`WaitForChannel`, `WaitForEvent`, `WaitForRingBufferResult`).

---

## 3. Assembly Layout (revised for engine-direct model)

### 3.1 Design rationale

The Blueprint compiler generates C# code that targets **real Fdp.Core types directly** — `ISimulationView`, `IEntityCommandBuffer`, `Entity`, `EntityRepository`. There is no Blueprint-specific wrapper layer.

This means:

- `Hrot.Blueprints.Core` references `Fdp.Core` (for those interfaces and types).
- The test harness mocks implement the *same* Fdp.Core interfaces — there's a single contract between Blueprint code and the engine surface, regardless of mock-vs-real backing.
- No `Hrot.Blueprints.Engine` adapter assembly exists; runtime systems live in `Fdp.Toolkits.Blueprints`.

The "decoupling" we preserve is more precise: `Hrot.Blueprints.Core` references **Fdp.Core schema/interface types only**, never `Fdp.Toolkits` runtime systems. Tests can run the core (compiler, IR, asset model, validation) without spinning up the engine kernel.

### 3.2 Spec — projects and references

```
Hrot.Blueprints.Core                 — net8.0 library
  references:  Fdp.Core (interfaces, schema types, Entity, etc.)
               System.Text.Json
               Microsoft.CodeAnalysis.CSharp (for compiler library)
  contains:    asset schema (BlueprintAsset and friends)
               type registry, node registry, validator
               IR data model
               compiler pipeline
               debug map types
               BlueprintDefinition record
               BlueprintLatentCursor struct
               IBlueprintCompiler interface
               IBlueprintDebugSession interface
               JSON helpers wrapping FdpJsonOptionsRegistry

Hrot.Blueprints.Generators           — netstandard2.0 analyzer
  references:  Hrot.Blueprints.Core (PrivateAssets="all")
               Microsoft.CodeAnalysis.CSharp 4.8.0 (PrivateAssets="all")
               Microsoft.CodeAnalysis.Analyzers 3.3.4 (PrivateAssets="all")
  contains:    BlueprintIncrementalGenerator

Fdp.Toolkits.Blueprints              — net8.0 library  (consolidates the engine adapter)
  references:  Hrot.Blueprints.Core
               Fdp.Core
               Fdp.Toolkits
               Fdp.ModuleHost.Abstractions
  contains:    BlueprintRegistry (concrete class)
               BlueprintBlackboard{1024,4096,16384} component definitions
               BlueprintBlackboardPartitions (allocator static helpers)
               BlueprintTickSystem (IEcsModuleSystem, Simulation phase)
               BlueprintMaintenanceSystem (IEcsModuleSystem, BeforeSync phase)
               [BlueprintRegistrar] attribute
               [BlueprintExposedEvent] attribute (declared for Slice 2 use)
               [BlueprintExposedChannelCommand] attribute (declared for Slice 2 use)
               EngineEventCatalog, ChannelCommandCatalog, WaitPrimitiveCatalog
               (hand-curated entries for Slice 1)

Hrot.Blueprints.Editor               — net8.0 library
  references:  Hrot.Blueprints.Core
               Fdp.Toolkits.Blueprints
               Fdp.Core
               Fdp.Presentation (ImGui + StructEdit + WindowManager)
               Fdp.Toolkits
  contains:    ImGui ManagedWindow subclasses for each editor window
               StructEdit field drawers for Blueprint-specific types
               IWindowRegistrar implementation

Hrot.Blueprints.Tests                — net8.0 test project (xUnit)
  references:  Hrot.Blueprints.Core
               Fdp.Toolkits.Blueprints
               Fdp.Core (for the real interfaces the mocks implement)
               xUnit
               Microsoft.CodeAnalysis.CSharp (for in-memory compile)
  contains:    Mock implementations of Fdp.Core interfaces:
                 MockSimulationView : ISimulationView
                 MockEntityCommandBuffer : IEntityCommandBuffer
                 MockEntityRepository (in-memory ECS store + singletons)
               BlueprintAssetBuilder (fluent test asset builder)
               BlueprintTestFixture (per-test ALC, lifecycle)
               Capturing debug session
               Compile-load-run-reload helpers
```

**Existing engine projects modified:**

```
Hrot.AI.Behaviors.csproj
  • adds <ProjectReference Include="...Hrot.Blueprints.Generators.csproj"
                            OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  • adds <ProjectReference Include="...Fdp.Toolkits.Blueprints.csproj" />
  • adds <AdditionalFiles Include="Blueprints\**\*.bp.json" />
  • adds <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  • adds <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
  • adds <DebugType>portable</DebugType>
  • adds <DebugSymbols>true</DebugSymbols>

Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs
  • attribute-driven registrar discovery on background thread
    (scans for [HsmActionRegistrar], [FbtRegistrar], [BlueprintRegistrar])
  • optional PDB loading (constructor parameter)

Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs
  • recommended: CreateExtended(params JsonConverter[]) factory

Fdp.Core/IEntityCommandBuffer.cs (and EntityCommandBuffer impl)
  • adds AddEmptyComponent<T>(Entity) where T : unmanaged
    (engine team confirmed will add per v1.1 Q-OPEN-A)

GlobalComponentIds.cs
  • three new ComponentIds: BlueprintBlackboard1024, BlueprintBlackboard4096,
    BlueprintBlackboard16384
```

### 3.3 Dependency direction

```
Hrot.Blueprints.Tests       ──►  Hrot.Blueprints.Core
                            ──►  Fdp.Core            (for the real interfaces)

Hrot.Blueprints.Generators  ──►  Hrot.Blueprints.Core

Fdp.Toolkits.Blueprints     ──►  Hrot.Blueprints.Core
                            ──►  Fdp.Core, Fdp.Toolkits

Hrot.Blueprints.Editor      ──►  Hrot.Blueprints.Core
                            ──►  Fdp.Toolkits.Blueprints
                            ──►  Fdp.Presentation

Hrot.AI.Behaviors.dll       has analyzer ref to Hrot.Blueprints.Generators
                            has runtime ref to Fdp.Toolkits.Blueprints
                            its generated code uses Fdp.Core + Fdp.Toolkits types
                            its generated code uses Fdp.Toolkits.Blueprints types
```

The cycle "engine adapter references types defined in reloadable DLL" never occurs because **all types and interfaces** live in stable assemblies (Fdp.Core, Fdp.Toolkits.Blueprints), and the reloadable DLL contains only concrete implementations of them. Same pattern the engine already uses for hand-written BTree/HSM code.

---

## 4. The Three Dispatch Kinds

### 4.1 Design rationale

A unified Blueprint compiler with three lowering targets. Each lowering produces C# code that integrates with a different engine subsystem, but they share IR, validator base, debug probes, hot reload, and JSON asset model.

**AiPrimitive is the major v1.2 expansion over v1.1's BehaviorAction.** A single AiPrimitive asset can host as BTree action, BTree condition, HSM action, HSM guard, and/or as a callable from other Blueprints — all from one authored graph. The author declares `hostings` on the asset.

### 4.2 Spec — capability matrix

| Capability | Library | AiPrimitive | Instance |
|---|---|---|---|
| Authored function graphs | ✓ | ✓ (single main graph + helpers) | ✓ |
| Authored event graphs | ✗ | ✗ | ✓ (engine events + custom events) |
| Member variables (state) | ✗ | ✓ split into Params + WorkingState | ✓ (single State struct) |
| `Self` binding | ✗ | ✓ | ✓ (or world-singleton) |
| Latent nodes (Wait/Delay) | ✗ | ✓ if intent=Action; **✗ if intent=Condition** | ✓ |
| Pure nodes | ✓ | ✓ | ✓ |
| Impure nodes (read components, ECB writes) | ✓ | ✓ | ✓ |
| Channel command nodes | ✗ | ✓ | ✓ |
| Wait primitive nodes | ✗ | ✓ if Action; ✗ if Condition | ✓ |
| Engine-event subscription | ✗ | ✗ (BTree/HSM dispatch from outside) | ✓ |
| Custom events (declared in asset) | ✗ | ✗ | ✓ |
| Event dispatchers (self-bound) | ✗ | ✗ | ✓ |
| Call into Library | ✓ | ✓ | ✓ |
| Call into peer Instance (declared callablePeers) | ✗ | ✗ | ✓ |
| Call into AiPrimitive with `BlueprintCall` hosting | ✓ | ✓ | ✓ |
| Returns NodeStatus | ✗ | ✓ (Success/Failure/Running for Action; Success/Failure for Condition) | ✗ |
| State storage | none | `BrainBlackboard.BehaviorParameters` (params) + `Blackboard1024` (working state, if any) | `BlueprintBlackboard*` slot |
| Hot-reload soft/hard | n/a | per-slot in `Blackboard1024` | per-slot in `BlueprintBlackboard*` |

### 4.3 Spec — Library

A Library asset compiles to a `public static class` with `public static` methods. No state, no Self, no events.

```csharp
public static class MathLib_Bp
{
    public const int BlueprintId = unchecked((int)0xA3F7_91D2);

    public static float ClampAngle(float angle, float min, float max) { /* generated */ }
    public static Vector3 ProjectOnPlane(Vector3 v, Vector3 normal) { /* generated */ }
}

[BlueprintRegistrar]
public static class BlueprintRegistrar_MathLib_A3F791D2_Bp
{
    public static void Register(BlueprintRegistry registry)
    {
        registry.RegisterLibrary(MathLib_Bp.BlueprintId, "MathLib");
    }
}
```

**Validator constraints (Library):** No state, no events, no latent, no Self, no impure nodes (ECS read or ECB write).

### 4.4 Spec — AiPrimitive

An AiPrimitive asset has these declarations:

- `intent`: `Action` or `Condition`
- `hostings`: subset of `{BTreeAction, BTreeCondition, HsmAction, HsmGuard, BlueprintCall}`
- `parameters`: typed list (fit within `BrainBlackboard.BehaviorParameters` = 100 B)
- `workingState`: typed list (lives in `Blackboard1024`; optional)

The compiler emits one shared core method plus host-specific thunks.

```csharp
public static class HasVisibleTarget_Bp
{
    public const int   BlueprintId    = unchecked((int)0xC714_5A20);
    public const ulong StructureHash  = 0xB29F_4E18_0C7D_3194UL;

    [StructLayout(LayoutKind.Sequential)]
    public struct Params { public float MinVisibilityDuration; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WorkingState { public float VisibleSince; public byte WasVisibleLastTick; }

    // Shared core — same logic, regardless of host
    public static NodeStatus TickCore(
        ref Params p,
        ref WorkingState ws,
        Entity self,
        EntityRepository world,
        float time)
    {
        DebugProbe.NodeEnter(/* ... */);

        if (!world.HasComponent<TargetMemory>(self))
        {
            ws = default;
            return NodeStatus.Failure;
        }

        var tm = world.GetComponentRO<TargetMemory>(self);
        if (tm.Count > 0)
        {
            if (ws.WasVisibleLastTick == 0) ws.VisibleSince = time;
            ws.WasVisibleLastTick = 1;

            if (time - ws.VisibleSince >= p.MinVisibilityDuration)
                return NodeStatus.Success;
            return NodeStatus.Running;
        }

        ws = default;
        return NodeStatus.Failure;
    }

    // BTree thunk — exact NodeLogicDelegate signature
    public static NodeStatus BTreeTick(
        ref BrainBlackboard bb, ref BehaviorTreeState state,
        ref BTreeContext ctx, int paramIndex)
    {
        // Parameters: project from BrainBlackboard.BehaviorParameters slice
        ref var p = ref Unsafe.As<byte, Params>(
            ref bb.BehaviorParameters[paramIndex * sizeof(Params)]);

        // Working state: inline projection over Blackboard1024 with hash check
        ref var bb1024 = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);
        unsafe
        {
            fixed (byte* memory = bb1024.Memory)
            {
                // Header at first 8 bytes
                ulong storedHash = *(ulong*)memory;
                if (storedHash != StructureHash)
                {
                    // Hard reset: zero everything, write our hash, run init
                    Unsafe.InitBlock(memory, 0, (uint)sizeof(Blackboard1024));
                    *(ulong*)memory = StructureHash;
                    InitDefaultWorkingState((WorkingState*)(memory + 8));
                }

                ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
                return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.Time);
            }
        }
    }

    private static unsafe void InitDefaultWorkingState(WorkingState* dst)
    {
        *dst = default;  // or per-asset specific init
        // ... any non-zero default initialization
    }

    // HSM thunk — unmanaged pointers, void return
    public static unsafe void HsmActivity(void* instance, void* context, HsmCommandWriter* writer)
    {
        var bridge = (HsmKernelBridge*)context;
        var world = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
        ref var p = ref *(Params*)instance;

        ref var bb1024 = ref world.GetComponentRW<Blackboard1024>(bridge->Self);
        fixed (byte* memory = bb1024.Memory)
        {
            if (*(ulong*)memory != StructureHash)
            {
                Unsafe.InitBlock(memory, 0, (uint)sizeof(Blackboard1024));
                *(ulong*)memory = StructureHash;
                InitDefaultWorkingState((WorkingState*)(memory + 8));
            }
            ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
            TickCore(ref p, ref ws, bridge->Self, world, world.Time);  // status discarded
        }
    }

    // HSM guard thunk — bool return
    public static unsafe bool HsmGuard(void* instance, void* context, ushort eventId)
    {
        var bridge = (HsmKernelBridge*)context;
        var world = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
        ref var p = ref *(Params*)instance;

        ref var bb1024 = ref world.GetComponentRW<Blackboard1024>(bridge->Self);
        fixed (byte* memory = bb1024.Memory)
        {
            if (*(ulong*)memory != StructureHash)
            {
                Unsafe.InitBlock(memory, 0, (uint)sizeof(Blackboard1024));
                *(ulong*)memory = StructureHash;
                InitDefaultWorkingState((WorkingState*)(memory + 8));
            }
            ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
            return TickCore(ref p, ref ws, bridge->Self, world, world.Time) == NodeStatus.Success;
        }
    }

    // BlueprintCall — for direct invocation from other Blueprint code
    public static NodeStatus Call(ref Params p, ref WorkingState ws, Entity self, EntityRepository world, float time)
        => TickCore(ref p, ref ws, self, world, time);
}

[BlueprintRegistrar]
public static unsafe class BlueprintRegistrar_HasVisibleTarget_C7145A20_Bp
{
    public static void Register(BlueprintRegistry registry,
                                  BehaviorRegistry behaviorRegistry,
                                  HsmActionDispatcher hsmDispatcher)
    {
        // BTree side — for both Action and Condition hostings, the BTree
        // treats them as polled nodes; the difference is just how composites
        // interpret the result.
        behaviorRegistry.RegisterAction("HasVisibleTarget_Bp", HasVisibleTarget_Bp.BTreeTick);

        // HSM side — register both activity (for action hosting) and guard
        // (for guard hosting). Each is a separate unmanaged function pointer.
        hsmDispatcher.RegisterAction(HasVisibleTarget_Bp.BlueprintId,
            (IntPtr)(delegate* unmanaged<void*, void*, HsmCommandWriter*, void>)&HasVisibleTarget_Bp.HsmActivity);
        hsmDispatcher.RegisterGuard(HasVisibleTarget_Bp.BlueprintId,
            (IntPtr)(delegate* unmanaged<void*, void*, ushort, bool>)&HasVisibleTarget_Bp.HsmGuard);

        // Blueprint side — register metadata (no dispatch table needed for direct call)
        registry.RegisterAiPrimitive(HasVisibleTarget_Bp.BlueprintId, new BlueprintDefinition { /* ... */ });
    }
}
```

**Validator constraints (AiPrimitive):**
- `Params` total size ≤ 100 bytes (BTree's `BehaviorParameters` slice).
- `WorkingState` total size ≤ remaining `Blackboard1024` capacity (Slice 1: full 1024 minus header, but in practice a single asset).
- `intent: Action`: terminal nodes are `Return Success/Failure/Running`.
- `intent: Condition`:
  - Terminal nodes are `Return Success/Failure` **only**. `Running` is forbidden.
  - **No latent nodes** (Wait, Delay) anywhere in the graph.
  - Editor's palette filters out latent nodes when intent is Condition.
- Hostings must all be compatible with the declared intent:
  - `Action` intent + `{BTreeAction, HsmAction, BlueprintCall}`.
  - `Condition` intent + `{BTreeCondition, HsmGuard, BlueprintCall}`.
- ECS mutations only via `world.GetCommandBuffer()`; direct `AddComponent`/`RemoveComponent`/`CreateEntity`/`DestroyEntity` are validator errors.
- Channel command nodes are allowed (compile to direct channel writes inline — see §16).
- **`BlueprintTickSystem` does NOT tick AiPrimitives.** They are invoked by the BTree/HSM kernels exclusively (or directly via BlueprintCall).

### 4.5 Spec — Instance

An Instance asset compiles to a `public static class` with: a `State` struct projected into a partition slot in `BlueprintBlackboard*`, event-handler methods, an optional `Tick` method, and a registrar.

```csharp
public static class DoorActor_Bp
{
    public const int   BlueprintId   = unchecked((int)0xD0_1B_5A_3F);
    public const ulong StructureHash = 0xA3F7_91D2_4C0B_8E55UL;

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public bool IsOpen;
        public float OpenAngle;
        public Entity LastInteractor;
        public BlueprintLatentCursor Cursor;
    }

    public static class VarIds
    {
        public const string IsOpen         = "var-a91f-...";
        public const string OpenAngle      = "var-b733-...";
        public const string LastInteractor = "var-c104-...";
    }

    public static int StateSize => Unsafe.SizeOf<State>();

    public static void InitDefault(Span<byte> bytes)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s = default;
        s.OpenAngle = 0f;
    }

    public static void Event_BeginPlay(ref State s, ISimulationView view,
                                          IEntityCommandBuffer ecb,
                                          Entity self, float time)
    { /* generated */ }

    public static void Event_OnHit(ref State s, ISimulationView view, IEntityCommandBuffer ecb,
                                     Entity self, float time,
                                     Entity attacker, float damage, Vector3 direction)
    { /* generated */ }

    public static void Tick(ref State s, ISimulationView view, IEntityCommandBuffer ecb,
                              Entity self, float time, float deltaTime)
    {
        // Engine event polling generated from `OnHit` event graph:
        var hits = view.ReadEvents<HitEvent>();
        for (int i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            if (!view.IsAlive(hit.Target)) continue;
            if (hit.Target == self)
                Event_OnHit(ref s, view, ecb, self, time, hit.Attacker, hit.Damage, hit.Direction);
        }

        // ... user-authored Tick graph body ...
    }

    public static void RegisterAll(BlueprintRegistry registry) { /* ... */ }
}
```

**Note the signature shape:** Instance methods take `ISimulationView` and `IEntityCommandBuffer` directly as parameters. No `BlueprintContext` wrapper struct. The mock test harness provides `MockSimulationView : ISimulationView` and `MockEntityCommandBuffer : IEntityCommandBuffer`; production gets real engine implementations. Either way the generated code is identical.

**Validator constraints (Instance):**
- Variables → fields of the `State` struct in declared order.
- Total `State` size must fit declared `tierHint`; auto-tier picks smallest fitting tier.
- Event graphs become `Event_<Name>` methods.
- `Tick` is optional.
- Latent nodes lower via `BlueprintLatentCursor` switch (see §10).
- `Self` is the entity parameter; `Entity.Null` for world-singleton.
- ECS mutations only via the passed `IEntityCommandBuffer`.

### 4.6 Spec — runtime invocation paths

| Dispatch | Registered into | Invoked by |
|---|---|---|
| Library | `BlueprintRegistry` (metadata only) | Direct C# method calls |
| AiPrimitive | `BehaviorRegistry` (for BTree), `HsmActionDispatcher` (for HSM), `BlueprintRegistry` (metadata + `BlueprintCall` thunk) | BTree kernel / HSM kernel / direct call |
| Instance | `BlueprintRegistry` | `BlueprintTickSystem` |

### 4.7 Cross-dispatch invocations

- Library called from anywhere: direct C# call.
- AiPrimitive with `BlueprintCall` hosting called from anywhere: direct C# call.
- Instance calls peer Instance on same entity (declared `callablePeers`): synchronous via partition allocator lookup.
- Cross-entity calls of any kind: validator error in Slice 1; deferred to Slice 2 (via deferred events).

---

## 5. Asset Schema

### 5.1 Spec — top-level shape

```csharp
namespace Hrot.Blueprints.Core.Assets;

public sealed class BlueprintAsset
{
    public Header Header { get; set; } = new();
    public Guid AssetId { get; set; }
    public string Name { get; set; } = "";
    public BlueprintDispatchKind Dispatch { get; set; }
    public BlackboardTierHint TierHint { get; set; } = BlackboardTierHint.Auto;
    public bool IsWorldSingleton { get; set; }

    // For AiPrimitive only:
    public AiPrimitiveDecl? Primitive { get; set; }
    public List<ParameterDecl> Parameters { get; set; } = new();    // for AiPrimitive
    public List<VariableDecl> WorkingState { get; set; } = new();   // for AiPrimitive

    // For Instance only:
    public List<VariableDecl> Variables { get; set; } = new();
    public List<EventDispatcherDecl> EventDispatchers { get; set; } = new();
    public List<CustomEventDecl> CustomEvents { get; set; } = new();
    public List<Guid> CallablePeers { get; set; } = new();

    // Common:
    public List<Graph> Graphs { get; set; } = new();
    public AssetMetadata EditorMetadata { get; set; } = new();
}

public enum BlueprintDispatchKind { Library, AiPrimitive, Instance }

public sealed class AiPrimitiveDecl
{
    public AiPrimitiveIntent Intent { get; set; }              // Action | Condition
    public List<AiPrimitiveHosting> Hostings { get; set; } = new();
}

public enum AiPrimitiveIntent { Action, Condition }

public enum AiPrimitiveHosting
{
    BTreeAction,
    BTreeCondition,
    HsmAction,
    HsmGuard,
    BlueprintCall,
}

public enum BlackboardTierHint { Auto, Force1024, Force4096, Force16384 }
```

### 5.2 Spec — variables, parameters, dispatchers, custom events

Unchanged from v1.1 except `VariableDecl` is now used for both Instance `Variables` and AiPrimitive `WorkingState`:

```csharp
public sealed class VariableDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public BlueprintTypeRef Type { get; set; } = new();
    public string? DefaultValueJson { get; set; }
    public bool IsEditable { get; set; }
    public bool IsExposedOnSpawn { get; set; }
    public string? Category { get; set; }
    public string? Tooltip { get; set; }
}

public sealed class ParameterDecl  // for AiPrimitive only — author-time arguments
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public BlueprintTypeRef Type { get; set; } = new();
    public string? DefaultValueJson { get; set; }
    public string? Tooltip { get; set; }
}

public sealed class EventDispatcherDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<ParameterDecl> Parameters { get; set; } = new();
}

public sealed class CustomEventDecl
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<ParameterDecl> Parameters { get; set; } = new();
}

public sealed class BlueprintTypeRef
{
    public string TypeId { get; set; } = "";
    public bool IsArray { get; set; }
    public List<BlueprintTypeRef> GenericArgs { get; set; } = new();
}
```

### 5.3 Spec — graphs, nodes, pins, links

Same as v1.1, with additions for channel commands and waits:

```csharp
public sealed class Graph
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public GraphKind Kind { get; set; }
    public List<ParameterDecl> Inputs { get; set; } = new();
    public List<ParameterDecl> Outputs { get; set; } = new();
    public List<Node> Nodes { get; set; } = new();
    public List<Link> Links { get; set; } = new();
    public GraphMetadata EditorMetadata { get; set; } = new();
}

public enum GraphKind { Function, Event, Construction }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FunctionCallNode),         "FunctionCall")]
[JsonDerivedType(typeof(BranchNode),               "Branch")]
[JsonDerivedType(typeof(SequenceNode),             "Sequence")]
[JsonDerivedType(typeof(GetVariableNode),          "GetVariable")]
[JsonDerivedType(typeof(SetVariableNode),          "SetVariable")]
[JsonDerivedType(typeof(LiteralNode),              "Literal")]
[JsonDerivedType(typeof(EventEntryNode),           "EventEntry")]
[JsonDerivedType(typeof(ReturnNode),               "Return")]
[JsonDerivedType(typeof(CastNode),                 "Cast")]
[JsonDerivedType(typeof(ArrayMakeNode),            "ArrayMake")]
[JsonDerivedType(typeof(ArrayGetNode),             "ArrayGet")]
[JsonDerivedType(typeof(LatentDelayNode),          "Delay")]
[JsonDerivedType(typeof(CallEventDispatcherNode),  "CallDispatcher")]
[JsonDerivedType(typeof(BindEventDispatcherNode),  "BindDispatcher")]
[JsonDerivedType(typeof(CallCustomEventNode),      "CallCustomEvent")]
[JsonDerivedType(typeof(CallPeerBlueprintNode),    "CallPeerBlueprint")]
[JsonDerivedType(typeof(ChannelCommandNode),       "ChannelCommand")]      // NEW
[JsonDerivedType(typeof(WaitForChannelNode),       "WaitForChannel")]      // NEW
[JsonDerivedType(typeof(WaitForEventNode),         "WaitForEvent")]        // NEW
public abstract class Node
{
    public Guid Id { get; set; }
    public List<Pin> Pins { get; set; } = new();
    public NodeMetadata EditorMetadata { get; set; } = new();
}

public sealed class ChannelCommandNode : Node
{
    public string ChannelType { get; set; } = "";   // e.g., "LocomotionChannel"
    public string ActionId { get; set; } = "";       // e.g., "ActionIdMoveTo"
    // Pins carry the per-action params (Destination, Speed, ArrivalRadius, etc.),
    // determined from the Channel Command Catalog entry.
}

public sealed class WaitForChannelNode : Node
{
    public string ChannelType { get; set; } = "";   // which channel to poll
    // Output pins: Success exec, Failure exec, optional data outputs from the
    // channel's status component.
}

public sealed class WaitForEventNode : Node
{
    public string EventTypeId { get; set; } = "";   // FQTN of the event struct
    public string? FilterByField { get; set; }       // e.g., "Target" — match against ctx.Self
    public string? CorrelationField { get; set; }     // e.g., "RequestId" — match against captured value
}

// Pin, PinRef, Link, AssetMetadata, GraphMetadata, NodeMetadata — unchanged from v1.1
```

### 5.4 Sample `.bp.json` files

**An AiPrimitive (action with channel commands and waits):**

```json
{
  "header": { "subsystemType": "Hrot.Blueprints", "schemaVersion": "1.0" },
  "assetId": "ai-4f23-87b1-...",
  "name": "MoveToAndFire",
  "dispatch": "AiPrimitive",
  "primitive": {
    "intent": "Action",
    "hostings": ["BTreeAction", "HsmAction"]
  },
  "parameters": [
    { "id": "p-1", "name": "Target",        "type": { "typeId": "System.Numerics.Vector3" } },
    { "id": "p-2", "name": "ApproachSpeed", "type": { "typeId": "System.Single" } }
  ],
  "workingState": [
    { "id": "ws-1", "name": "Phase", "type": { "typeId": "System.Byte" } }
  ],
  "graphs": [
    {
      "id": "graph-main", "name": "Main", "kind": "Function",
      "nodes": [ /* entry, ChannelCommand(Loco/MoveTo), WaitForChannel(Loco),
                   Branch on Success/Failure, ChannelCommand(Weapon/Fire),
                   WaitForChannel(Weapon), Return */ ],
      "links": [ /* ... */ ]
    }
  ]
}
```

**An AiPrimitive condition:**

```json
{
  "name": "HasVisibleTargetFor",
  "dispatch": "AiPrimitive",
  "primitive": {
    "intent": "Condition",
    "hostings": ["BTreeCondition", "HsmGuard"]
  },
  "parameters": [
    { "id": "p-1", "name": "MinVisibilityDuration", "type": { "typeId": "System.Single" } }
  ],
  "workingState": [
    { "id": "ws-1", "name": "VisibleSince",         "type": { "typeId": "System.Single" } },
    { "id": "ws-2", "name": "WasVisibleLastTick",   "type": { "typeId": "System.Byte" } }
  ],
  "graphs": [ /* ... no Wait nodes allowed; validator enforces ... */ ]
}
```

**An Instance** (unchanged from v1.1):

```json
{
  "name": "Door",
  "dispatch": "Instance",
  "tierHint": "Auto",
  "variables": [ /* ... */ ],
  "graphs": [ /* event graphs + Tick + function graphs */ ]
}
```

---

## 6. Runtime: Components, Partition Allocator, Registry, Tick Systems

### 6.1 Design rationale

Two storage stories, one per dispatch kind:

- **Instance dispatch** uses the new `BlueprintBlackboard{1024,4096,16384}` components with partition allocator. Multi-Blueprint per entity supported from Slice 1.
- **AiPrimitive dispatch** uses the engine's existing `Blackboard1024` for working state. **Slice 1 constraint:** one AiPrimitive working-state Blueprint per entity (Slice 2 adds partition allocator to `Blackboard1024`).

The architect's clarification settled this: AiPrimitives reuse the existing `[SharedAiHeavy*]` projection pattern over `Blackboard1024`. They do not use `BlueprintBlackboard*`. This keeps storage stories cleanly separated and avoids retrofitting.

### 6.2 Spec — `BlueprintBlackboard*` components (Instance dispatch only)

Three new components in `Fdp.Toolkits.Blueprints`, registered in `GlobalComponentIds`. Layout: header + slot table + free-list-managed payload.

```csharp
namespace Fdp.Toolkit.Blueprints;

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]
public unsafe struct BlueprintBlackboard1024
{
    public const int TotalSize = 1024;
    public const int HeaderSize = 32;
    public const int MaxSlots = 4;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 64
    public const int PayloadSize = TotalSize - HeaderSize - SlotTableSize; // 928

    public fixed byte Memory[TotalSize];
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard4096)]
public unsafe struct BlueprintBlackboard4096
{
    public const int TotalSize = 4096;
    public const int HeaderSize = 32;
    public const int MaxSlots = 8;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 128
    public const int PayloadSize = TotalSize - HeaderSize - SlotTableSize; // 3936

    public fixed byte Memory[TotalSize];
}

[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard16384)]
public unsafe struct BlueprintBlackboard16384
{
    public const int TotalSize = 16384;
    public const int HeaderSize = 32;
    public const int MaxSlots = 16;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 256
    public const int PayloadSize = TotalSize - HeaderSize - SlotTableSize; // 16096

    public fixed byte Memory[TotalSize];
}
```

### 6.3 Spec — header, slot table, free-list

```csharp
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct BlueprintBlackboardHeader
{
    public uint   MagicAndVersion;
    public byte   SlotCount;
    public byte   MaxSlots;
    public ushort FreeListHead;
    public ushort PayloadStart;
    public ushort PayloadSize;
    public ushort PayloadFree;
    // 18 reserved bytes
}

[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintSlotEntry
{
    public int    BlueprintId;        // 0 = unused
    public uint   InstanceVersion;
    public ushort PayloadOffset;
    public ushort PayloadSize;
    public ulong  StructureHash;
}
```

Free block header (4 bytes, in-payload): `ushort NextFree; ushort Size;`

### 6.4 Spec — `BlueprintBlackboardPartitions` allocator helpers

```csharp
namespace Fdp.Toolkit.Blueprints;

public static unsafe class BlueprintBlackboardPartitions
{
    public const int SlotEntrySize = 16;
    public const int FreeBlockHeaderSize = 4;
    public const int Alignment = 8;

    public static void Initialize(byte* memory, int totalSize, byte maxSlots);

    // Lookup — used by generated code each tick.
    public static bool TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset);

    // Attach/detach — used by tick system on first encounter and explicit detach.
    public static bool TryAttach(byte* memory, int blueprintId, int requestedSize,
                                  ulong structureHash, out int payloadOffset);
    public static bool TryDetach(byte* memory, int blueprintId);

    // For tick system slot enumeration
    public static int GetSlotCount(byte* memory);
    public static ref BlueprintSlotEntry GetSlot(byte* memory, int slotIndex);

    // For per-slot reload reconciliation
    public static void ResetSlot(byte* memory, int slotIndex);

    // For tier upgrade in BlueprintMaintenanceSystem
    public static void CopyToLargerTier(byte* src, int srcSize, byte* dst, int dstSize, byte dstMaxSlots);
}
```

Fast path (per-tick lookup) is a linear scan over ≤16 slot entries — cache-friendly, sub-microsecond, JIT-inlined.

### 6.5 Spec — `Blackboard1024` memory layout for AiPrimitive working state

AiPrimitive working state is accessed via **inline projection** in each generated thunk — no separate helper class.

**Memory layout (compile-time documented):**

```
Blackboard1024.Memory layout when hosting an AiPrimitive working state:
  Offset 0..7   : ulong  StructureHash    (8 bytes)
  Offset 8..    : T      WorkingState     (struct of the asset's declared working-state fields)
```

The first 8 bytes are reserved for the StructureHash header. The working-state struct projects starting at offset 8. Each thunk checks and resets the header inline (see §4.4 for the generated thunk pattern).

**Implicit Slice 1 constraint:** Only one AiPrimitive working-state Blueprint can occupy an entity's `Blackboard1024` at a time, because the StructureHash header is at a fixed location. If two AiPrimitives with working state are attached to the same entity, the second one's first invocation will overwrite the first's hash and zero the working memory. **⚠ Lifted in Slice 2 via Option β** — AiPrimitive working state is partitioned into the existing `BlueprintBlackboard*` tiers (keyed per node by `FNV-1a(BehaviorAssetId, NodeVisualId)`), **not** a `Blackboard1024` allocator. See `BTree_AiActionParameterBinding_Detailed_Design.md` §4.

**Detection:** The compiler can detect static conflicts (one BTree references two AiPrimitives with `WorkingState != null` and both can target the same entity) and emit a warning diagnostic. Runtime detection is not free; documenting it as authoring discipline for Slice 1.

### 6.6 Spec — `BlueprintRegistry`

```csharp
public sealed class BlueprintRegistry
{
    public void RegisterLibrary(int blueprintId, string name);
    public void RegisterAiPrimitive(int blueprintId, BlueprintDefinition def);
    public void RegisterInstance(int blueprintId, BlueprintDefinition def);

    public bool TryGetById(int blueprintId, out BlueprintDefinition def);
    public bool TryGetByName(string name, out BlueprintDefinition def);
    public IEnumerable<(int Id, BlueprintDefinition Def)> GetAll();

    // Hot reload — atomic staging swap, called by AiHotReloadCoordinator
    internal void CommitStaging(BlueprintRegistryStaging staging);
}

public sealed class BlueprintDefinition
{
    public required string Name { get; init; }
    public required BlueprintDispatchKind Kind { get; init; }
    public required ulong StructureHash { get; init; }
    public required int StateSize { get; init; }   // 0 for Library and stateless AiPrimitive

    // For Instance:
    public InitDefaultDelegate? InitDefault { get; init; }
    public TickDelegate? Tick { get; init; }
    public IReadOnlyDictionary<string, EventHandlerDelegate> EventHandlers { get; init; }
        = new Dictionary<string, EventHandlerDelegate>();

    // For Inspector / debugger
    public Type? StateClrType { get; init; }
}

public delegate void InitDefaultDelegate(Span<byte> stateBytes);
public delegate void TickDelegate(Span<byte> stateBytes, ISimulationView view,
                                    IEntityCommandBuffer ecb, Entity self,
                                    float time, float deltaTime);
public delegate void EventHandlerDelegate(Span<byte> stateBytes, ISimulationView view,
                                            IEntityCommandBuffer ecb, Entity self,
                                            float time, ReadOnlySpan<byte> payload);
```

**Note the delegates take real Fdp.Core types** (`ISimulationView`, `IEntityCommandBuffer`, `Entity`) directly. Generated code calls these delegates; mock tests provide mock implementations of the interfaces.

### 6.7 Spec — `BlueprintTickSystem` (Instance dispatch only)

```csharp
namespace Fdp.Toolkit.Blueprints.Systems;

[UpdateInPhase(SystemPhase.Simulation)]
[UpdateBefore(typeof(LocomotionDispatcherSystem))]
[UpdateBefore(typeof(WeaponDispatcherSystem))]
[UpdateBefore(typeof(InteractionDispatcherSystem))]
public sealed class BlueprintTickSystem : IEcsModuleSystem, IProfiledSystem
{
    public string ProfileName => "BlueprintTickSystem";

    private readonly BlueprintRegistry _registry;

    public BlueprintTickSystem(BlueprintRegistry registry) => _registry = registry;

    public void Execute(ISimulationView view)
    {
        var ecb = view.GetCommandBuffer();   // engine-provided ECB for this frame

        TickTier<BlueprintBlackboard1024>(view, ecb);
        TickTier<BlueprintBlackboard4096>(view, ecb);
        TickTier<BlueprintBlackboard16384>(view, ecb);

        TickWorldSingletons(view, ecb);
    }

    private unsafe void TickTier<TBB>(ISimulationView view, IEntityCommandBuffer ecb)
        where TBB : unmanaged
    {
        // ... iterate entities with TBB, iterate slots, per-slot reload check,
        //     call def.Tick with (stateBytes, view, ecb, entity, time, deltaTime) ...
    }
}
```

Per-slot soft/hard reload reconciliation happens **inside** the tick: when a slot's `StructureHash` differs from `def.StructureHash`, the slot is zeroed and re-init'd before being ticked.

**AiPrimitives are NOT ticked by `BlueprintTickSystem`.** They are invoked by `BTreeTickSystem` and `HsmTickSystem<T>` (existing) through their registered thunks.

### 6.8 Spec — world-singleton Instance support

For Instance assets declared `IsWorldSingleton = true`, state lives in a singleton blackboard:

```csharp
// Engine adapter provides
public sealed partial class BlueprintRegistry
{
    public bool TryAttachWorldSingleton(EntityRepository world, int blueprintId, BlueprintDefinition def);
    public bool TryGetWorldSingletonSlotOffset(EntityRepository world, int blueprintId,
                                                  BlackboardTier tier, out int offset);

    public void EnsureWorldSingletonsInitialized(EntityRepository world);
    // ...
}

public enum BlackboardTier { B1024, B4096, B16384 }
```

State persists across `SoftClear` (per architect); scenario-load explicitly resets via `SetSingletonUnmanaged(default(...))` if a fresh start is wanted. One world-singleton Blueprint per tier in Slice 1.

### 6.9 Spec — `BlueprintMaintenanceSystem` (NEW — from v1.1 Q-OPEN-B resolution)

Tier upgrade is **never lazy in the tick**. A dedicated maintenance system runs in `SystemPhase.BeforeSync`:

```csharp
namespace Fdp.Toolkit.Blueprints.Systems;

[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class BlueprintMaintenanceSystem : IEcsModuleSystem, IProfiledSystem
{
    public string ProfileName => "BlueprintMaintenanceSystem";

    public void Execute(ISimulationView view)
    {
        var repo = (EntityRepository)view;
        UpgradeTier<BlueprintBlackboard1024, BlueprintBlackboard4096>(repo);
        UpgradeTier<BlueprintBlackboard4096, BlueprintBlackboard16384>(repo);
    }

    private unsafe void UpgradeTier<TOld, TNew>(EntityRepository repo)
        where TOld : unmanaged where TNew : unmanaged
    {
        var query = repo.Query().With<TOld>().With<TNew>().Build();
        foreach (var entity in query)
        {
            ref var oldBB = ref repo.GetComponentRW<TOld>(entity);
            ref var newBB = ref repo.GetComponentRW<TNew>(entity);

            BlueprintBlackboardPartitions.CopyToLargerTier(
                src: (byte*)Unsafe.AsPointer(ref oldBB),
                srcSize: sizeof(TOld),
                dst: (byte*)Unsafe.AsPointer(ref newBB),
                dstSize: sizeof(TNew),
                dstMaxSlots: GetMaxSlots<TNew>());

            repo.RemoveComponent<TOld>(entity);
        }
    }
}
```

Tier upgrade flow:
1. During Simulation, an `attach` request fails because the current tier is full. The runtime issues an ECB `AddEmptyComponent<NextTier>(entity)`. Does NOT remove the old tier yet.
2. End of frame: ECB plays back. Entity now has both old and new tier components.
3. Next frame `BeforeSync`: `BlueprintMaintenanceSystem` finds the dual-component entity, byte-copies, removes the old component synchronously.
4. Next phase `Simulation`: `BlueprintTickSystem` sees a clean single-tier blackboard, performs the attach.

### 6.10 Engine-direct interface usage in generated code

Generated Instance Tick:

```csharp
public static void Tick(ref State s, ISimulationView view, IEntityCommandBuffer ecb,
                          Entity self, float time, float deltaTime)
{
    // Read a component — engine returns ref readonly to chunk memory
    ref readonly var pos = ref view.GetComponentRO<Position>(self);

    // Query other entities
    var query = view.Query().With<Sensor>().Build();

    // Read events — engine returns IReadOnlyList<T>
    var hits = view.ReadEvents<HitEvent>();
    for (int i = 0; i < hits.Count; i++)
    {
        if (hits[i].Target == self) /* ... */
    }

    // Write via ECB
    ecb.AddComponent(self, new SomeFlag { Active = true });

    // Publish event
    ecb.PublishEvent(new DoorOpenedEvent { Door = self });
}
```

No `BlueprintContext` wrapper. No `IBlueprintWorldView` interface. Generated code is structurally identical to hand-written ECS code that uses these same engine types.

---

## 7. Compiler Pipeline

### 7.1 Spec — public compiler API

```csharp
namespace Hrot.Blueprints.Core.Compiler;

public interface IBlueprintCompiler
{
    CompileResult Compile(BlueprintAsset asset, CompileOptions options);
    ValidationResult Validate(BlueprintAsset asset);
}

public sealed record CompileOptions(
    CompilerMode Mode,                  // Release | Debug | Trace
    INodeRegistry NodeRegistry,
    ITypeRegistry TypeRegistry,
    IEngineEventCatalog EngineEvents,        // NEW
    IChannelCommandCatalog ChannelCommands,  // NEW
    IWaitPrimitiveCatalog WaitPrimitives,    // NEW
    IReadOnlyList<BlueprintAsset> SiblingAssets);

public enum CompilerMode { Release, Debug, Trace }

public sealed record CompileResult(
    bool Succeeded,
    string? GeneratedSource,
    int BlueprintId,
    ulong StructureHash,
    DebugMap? DebugMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    BlueprintAsset CanonicalAsset);
```

### 7.2 Pipeline stages

```
1. Parse        : JSON → BlueprintAsset
2. Validate     : structural + intent + hostings + size limits + node compatibility
3. Normalize    : default-value materialization, implicit cast insertion, dead-node elimination
4. Type-resolve : pin type unification, wildcard resolution
5. Schedule     : topological order per exec path; basic-block IR
6. Lower        : dispatch-aware lowering pass per declared hosting:
                  • Library lowering — pure functions, no state
                  • AiPrimitive lowering — TickCore + thunks (one set per declared hosting)
                  • Instance lowering — Event_X methods + Tick with BlueprintLatentCursor
7. Emit         : walk lowered IR, emit C# via templated string builder
                  emit debug map alongside
8. Validate emitted source : Roslyn CSharpSyntaxTree.ParseText sanity check
```

### 7.3 Determinism contract

Strict: same input → byte-identical output. Enforced by sorted iteration (`OrderBy(x => x.Id)`), no `Guid.NewGuid()` or `DateTime.Now` in output, sorted dictionaries during compile.

```csharp
public static int ComputeBlueprintId(Guid assetId)
{
    const uint OffsetBasis = 0x811C_9DC5, Prime = 0x0100_0193;
    Span<byte> bytes = stackalloc byte[16];
    assetId.TryWriteBytes(bytes);
    uint hash = OffsetBasis;
    for (int i = 0; i < 16; i++) hash = (hash ^ bytes[i]) * Prime;
    return unchecked((int)hash);
}
```

Collisions detected at registration time; user resolves by re-Guiding one asset.

### 7.4 Generator output topology

**One registrar per asset** for Roslyn caching health. For each `.bp.json`:

- `{SanitizedName}_{BlueprintId:X8}_Bp.g.cs` — the static class with `BlueprintId`, `StructureHash`, state types, methods.
- `BlueprintRegistrar_{SanitizedName}_{BlueprintId:X8}_Bp.g.cs` — the `[BlueprintRegistrar]`-attributed class.

`SanitizedName` is the asset's `name` field with non-alphanumeric characters replaced by `_`, prepended with `_` if it starts with a digit. The `BlueprintId:X8` suffix guarantees uniqueness regardless of folder layout.

### 7.5 Dispatch-aware lowering — the key new compiler concept

For an AiPrimitive asset with `hostings: ["BTreeAction", "HsmAction"]`, the compiler emits **one** `TickCore` method (shared logic) plus **two** thunks (one per hosting). The thunks differ only in how they marshal from the host kernel's calling convention to `TickCore`'s shared signature.

For Wait nodes specifically, the lowering depends on dispatch:

- **AiPrimitive Wait**: emit a phase-byte write in working state + `return NodeStatus.Running`. The host kernel re-enters next tick; `TickCore`'s switch on phase resumes at the right code.
- **Instance Wait**: emit a `BlueprintLatentCursor.ResumeAt` write + `return`. `BlueprintTickSystem` re-enters; the `Tick` method's switch on `Cursor.ResumeAt` jumps to the resume label.

Both lowerings share the same conceptual IR primitive ("Wait until condition"), but the emitted C# differs structurally. Detailed lowering rules live in the Compiler Detailed Design doc.

---

## 8. Hot-Reload Integration

### 8.1 Spec — coordinator changes (engine-side)

Per v1.1 Q-OPEN resolutions:

- Attribute-driven registrar discovery on background thread.
- Pre-resolved `MethodInfo` list dispatched on main thread during `DrainPendingCallbacks`.
- Optional PDB loading (constructor-gated).

```csharp
internal sealed class AiHotReloadCoordinator
{
    private void LoadAndReload(string dllPath)
    {
        // ... background thread ...
        var newAlc = new AssemblyLoadContext(/* ... */, isCollectible: true);
        Assembly newAssembly = LoadAssemblyInto(newAlc, dllPath);

        // Background-thread attribute scan
        var registrarMethods = new List<MethodInfo>();
        foreach (var type in newAssembly.GetTypes())
        {
            if (type.GetCustomAttribute<HsmActionRegistrarAttribute>() != null)
                registrarMethods.Add(type.GetMethod("RegisterAll")!);
            else if (type.GetCustomAttribute<FbtRegistrarAttribute>() != null)
                registrarMethods.Add(type.GetMethod("Register")!);
            else if (type.GetCustomAttribute<BlueprintRegistrarAttribute>() != null)
                registrarMethods.Add(type.GetMethod("Register")!);
        }

        _pendingReloads.Enqueue(new PendingReload { /* ... */ });
    }

    private void DrainPendingCallbacks(EntityRepository repo)
    {
        if (!_pendingReloads.TryDequeue(out var pending)) return;

        HsmActionDispatcher.ClearAll();   // existing — clears stale function pointers

        foreach (var method in pending.RegistrarMethods.OrderBy(m => m.DeclaringType!.FullName))
        {
            var paramType = method.GetParameters()[0].ParameterType;
            object registryArg = paramType switch
            {
                Type t when t == typeof(BehaviorRegistry)  => _behaviorRegistry,
                Type t when t == typeof(BlueprintRegistry) => _blueprintRegistry,
                Type t when t == typeof(HsmActionDispatcher) => HsmActionDispatcher.Instance,
                _ => throw new InvalidOperationException("Unknown registrar signature"),
            };
            method.Invoke(null, new object[] { registryArg });
        }

        if (pending.OldAlc != null)
        {
            PreviousAlcRef = new WeakReference<AssemblyLoadContext>(pending.OldAlc);
            pending.OldAlc.Unload();
        }

        OnReloadCompleted?.Invoke();
    }

    private Assembly LoadAssemblyInto(AssemblyLoadContext alc, string dllPath)
    {
        using var peStream = File.OpenRead(dllPath);
        if (_options.LoadPdbs)
        {
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                using var pdbStream = File.OpenRead(pdbPath);
                return alc.LoadFromStream(peStream, pdbStream);
            }
        }
        return alc.LoadFromStream(peStream);
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BlueprintRegistrarAttribute : Attribute { }
```

### 8.2 Per-slot soft/hard reload (Instance dispatch)

Reconciliation happens inside `BlueprintTickSystem.TickTier` — when the tick visits a slot whose `StructureHash` differs from the loaded `def.StructureHash`:

```
soft (same hash): slot Memory bytes survive verbatim; tick continues.
hard (changed hash):
  - zero the slot's payload bytes.
  - call def.InitDefault on the zeroed bytes.
  - slot.StructureHash = def.StructureHash.
  - slot.InstanceVersion++.  (invalidates any latent cursors)
```

For AiPrimitive working state: same mechanism, except the "slot" is the single entry inside `Blackboard1024` (Slice 1), with the StructureHash header at offset 0 and the working-state struct at offset 8. The thunk checks the hash inline on every invocation and performs a hard reset if it differs (see §4.4 for the generated thunk pattern; §6.5 for the memory layout).

### 8.3 Managed-delegates-only rule

All Blueprint dispatch goes through managed delegates (`Action<...>`, `Func<...>`, custom managed delegate types declared in the stable `Fdp.Toolkits.Blueprints` assembly). HSM thunks use `delegate*` only as the raw entry-point shape the HSM kernel needs — those entry-point pointers are re-registered after each reload from the new ALC's types, so no stale unmanaged pointer survives. `HsmActionDispatcher.ClearAll()` handles the clearing.

---

## 9. Debug Strategy (B + C from day one)

### 9.1 Modes

| Mode | Probes emitted | PDB | Source on disk | Use |
|---|---|---|---|---|
| Release | No | Yes (portable) | Yes (for symbol resolution) | Production |
| Debug | At each node enter | Yes | Yes | Default dev loop |
| Trace | At each node enter + each pin value | Yes | Yes | Deep tracing sessions |

Per-asset mode toggle in editor; most assets stay Release, a few under active debugging go to Debug.

### 9.2 Spec — debug protocol

```csharp
namespace Hrot.Blueprints.Core.Debug;

public interface IBlueprintDebugSession
{
    bool IsAnyBreakpointActive { get; }
    bool IsAnyWatchActive { get; }

    void OnNodeEnter(Entity self, string nodeId);
    void OnPinValue<T>(Entity self, string pinId, T value);

    void SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId);
    void ClearBreakpoint(Guid assetId, Guid graphId, Guid nodeId);
    void StepOver();
    void StepInto();
    void StepOut();
    void Continue();

    event Action<BreakpointHit>? OnBreakpointHit;
    event Action<NodeExecuted>? OnNodeExecuted;
    event Action<PinValueChanged>? OnPinValueChanged;
}

public sealed record BreakpointHit(Guid AssetId, Guid GraphId, Guid NodeId,
                                     Entity Self, IReadOnlyDictionary<string, object> Locals);
public sealed record NodeExecuted(Guid AssetId, Guid GraphId, Guid NodeId, Entity Self);
public sealed record PinValueChanged(Guid AssetId, Guid PinId, object? Value);
```

This is one of the few interfaces Blueprint *does* define — because it's a Blueprint-specific concept with no engine equivalent. The runtime implementation lives in `Fdp.Toolkits.Blueprints`; the test harness provides a capturing implementation for assertions.

### 9.3 Debug map sidecar

Per-asset JSON sidecar mapping `nodeId` ↔ `(generated file, line range)` and `pinId` ↔ value-access expression. Same as v1.1.

### 9.4 Compile modes and PDB emission (clarified from v1.1)

Two distinct compilation paths both require PDB and EmbeddedSource support:

- **Path A — MSBuild Full Rebuild**: `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` + `<DebugType>portable</DebugType>` produce on-disk `.g.cs` files and a portable PDB. Debuggers resolve source from the on-disk `.g.cs` files via PDB path references.
- **Path B — Quick Reload (in-memory)**: the in-process compiler library calls `CSharpCompilation.Emit` directly with `EmbeddedText.FromSource(virtualSourcePath, sourceText)` + `EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb)`. Debuggers find source embedded inside the PDB.

Updated compile modes table (both paths apply the same compile modes):

| Mode    | Probes emitted | PDB              | Source on disk (Full)   | Source embedded (Quick) |
|---------|----------------|------------------|-------------------------|-------------------------|
| Release | No             | Yes (Portable)   | Yes (for symbol resolve)| Yes                     |
| Debug   | At node enter  | Yes              | Yes                     | Yes                     |
| Trace   | + pin values   | Yes              | Yes                     | Yes                     |

The difference between paths is purely *where the source comes from when the debugger asks*: Full Rebuild — on-disk file via PDB path reference; Quick Reload — embedded text inside the PDB.

**File locks during rebuild**: an attached debugger can lock PDB files. PDB loading is option-gated; off by default in production, on in dev. Toggle off when actively rebuilding with debugger attached.

**Debugger diagnostic stack growth**: many reload cycles with PDB loading on can grow the debugger's diagnostic memory unboundedly. Same gating handles it.

---

## 10. Latent Execution

### 10.1 Spec — `BlueprintLatentCursor` (Instance only)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct BlueprintLatentCursor
{
    public uint  ResumeAt;
    public uint  InstanceVersion;
    public float WaitUntilTime;
    public uint  WaitEventMask;  // Slice 2
}
```

If `cursor.InstanceVersion != slot.InstanceVersion`, the continuation is stale and silently dropped.

### 10.2 Spec — Instance dispatch latent lowering

```csharp
public static void Tick(ref State s, ISimulationView view, IEntityCommandBuffer ecb,
                          Entity self, float time, float deltaTime)
{
    switch (s.Cursor.ResumeAt)
    {
        case 0: goto entry;
        case 1: goto resume_after_delay_1;
        default: return;
    }

entry:
    // ... nodes before Delay ...
    s.Cursor.ResumeAt = 1;
    s.Cursor.InstanceVersion = /* captured from slot.InstanceVersion */;
    s.Cursor.WaitUntilTime = time + 2.0f;
    return;

resume_after_delay_1:
    if (time < s.Cursor.WaitUntilTime) return;
    // ... nodes after Delay ...
    s.Cursor.ResumeAt = 0;
    return;
}
```

### 10.3 Spec — AiPrimitive dispatch latent lowering

```csharp
public static NodeStatus TickCore(ref Params p, ref WorkingState ws,
                                    Entity self, EntityRepository world, float time)
{
    if (ws.Phase == 0)
    {
        // ... pre-Wait logic ...
        ref var ch = ref world.GetComponentRW<LocomotionChannel>(self);
        ch.ActiveAction = NavigationConstants.ActionIdMoveTo;
        // ... write params ...
        ch.ActionInstanceId++;
        ws.Phase = 1;
        return NodeStatus.Running;   // Host kernel re-ticks us next frame
    }

    if (ws.Phase == 1)
    {
        var chStatus = world.GetComponentRO<LocomotionChannel>(self);
        if (chStatus.Status == NodeStatus.Running) return NodeStatus.Running;
        if (chStatus.Status == NodeStatus.Failure) { ws.Phase = 0; return NodeStatus.Failure; }
        ws.Phase = 2;
        return NodeStatus.Running;
    }

    // ... etc ...
}
```

Each Wait costs one tick (clean version; no within-tick fall-through). Predictable, debuggable.

### 10.4 Replay safety

- Cursor + working-state bytes live in unmanaged components → recorded and replayed verbatim.
- `BlueprintTickSystem` is suspended during replay (part of `TogglableSimulationGroup`).
- On replay→live branch, `GlobalTime` is restored alongside, so `WaitUntilTime` checks against the correct time. Resumes cleanly.

---

## 11. Test Harness Architecture

### 11.1 Design rationale

Slice 1 = unit-level only. Mocks implement real `Fdp.Core` interfaces (`ISimulationView`, `IEntityCommandBuffer`, etc.). Generated code is byte-identical between dev and production; only the implementation of the engine surface differs.

The mock must enforce the engine's real phase/threading/lifecycle rules so tests catch violations early. A mock that returns the right values but lets generated code do forbidden things gives false confidence.

### 11.2 Spec — mock implementation contract

```csharp
namespace Hrot.Blueprints.Tests.Mocks;

public sealed class MockEntityRepository
{
    // In-memory ECS store: unmanaged-component slabs keyed by (componentId, entity),
    // managed-component dictionary, generation counters, singleton store.

    public Entity CreateEntity();
    public bool IsAlive(Entity e);
    public ref T GetComponentRW<T>(Entity e) where T : unmanaged;
    public ref readonly T GetComponentRO<T>(Entity e) where T : unmanaged;
    public T GetManagedComponentRO<T>(Entity e) where T : class;
    public bool HasComponent<T>(Entity e) where T : unmanaged;
    // ... (matches Fdp.Core.EntityRepository public surface)

    public ref T GetSingleton<T>() where T : unmanaged;
    public void SetSingletonUnmanaged<T>(T value) where T : unmanaged;

    public IEntityQuery Query();  // (matches Fdp.Core's query interface)

    // Test-only: ECB integration
    public MockEntityCommandBuffer GetCommandBuffer();

    // Test-only: simulation control
    public float Time { get; set; }
    public uint Tick { get; set; }
    public void Advance(float dt);
}

public sealed class MockSimulationView : ISimulationView
{
    // Implements Fdp.Core.ISimulationView fully.
    // Delegates reads to MockEntityRepository; enforces "ReadOnly" contract.
    // ReadEvents<T> returns the same IReadOnlyList<T> for the whole tick.
}

public sealed class MockEntityCommandBuffer : IEntityCommandBuffer
{
    // Implements Fdp.Core.IEntityCommandBuffer fully.
    // Queue of deferred ops; Playback() applies them to MockEntityRepository.
    // CreateEntity returns real Entity immediately (matching QCB-1 architect ruling).
    // AddEmptyComponent supported.
    // Enforces phase-rule violations:
    //   * Direct AddComponent during Simulation - OK (queues)
    //   * Direct singleton write during Simulation - throws (test caught)
}
```

### 11.3 Spec — test fixture

```csharp
public sealed class BlueprintTestFixture : IDisposable
{
    public AssemblyLoadContext Alc { get; }
    public MockEntityRepository World { get; }
    public MockSimulationView View { get; }
    public IBlueprintCompiler Compiler { get; }
    public BlueprintRegistry Registry { get; }
    public CapturingDebugSession DebugSession { get; }

    public Assembly CompileAndLoad(BlueprintAsset asset, CompilerMode mode = CompilerMode.Debug);
    public Assembly CompileAndLoadMany(IReadOnlyList<BlueprintAsset> assets, CompilerMode mode = CompilerMode.Debug);
    public void SimulateReload(IReadOnlyList<BlueprintAsset> newVersions);

    public void TickFrame(float dt);  // advances World.Time, calls BlueprintTickSystem on real engine systems

    public void Dispose() { /* unload ALC, GC.Collect, verify reclaimed */ }
}
```

The fixture uses **real** `BlueprintTickSystem` and `BlueprintMaintenanceSystem` (from `Fdp.Toolkits.Blueprints`) against the mock world. Only the engine I/O surface (`ISimulationView`, `IEntityCommandBuffer`, `EntityRepository`) is mocked. Everything Blueprint-specific is real code under test.

### 11.4 Spec — project structure

```
Hrot.Blueprints.Tests/
  Compiler/
    GoldenOutputTests.cs       — given asset, generated source matches snapshot
    DeterminismTests.cs        — compile twice, byte-identical
    DiagnosticsTests.cs        — validator catches errors
    BlueprintIdHashTests.cs    — FNV-1a stable
    StructureHashTests.cs      — hash deterministic
    AiPrimitiveLoweringTests.cs — TickCore + thunks emitted correctly per hosting
    ConditionValidatorTests.cs — Running and latent forbidden in Conditions
  Runtime/
    LibraryDispatchTests.cs
    AiPrimitiveDispatchTests.cs    — invoke via registered BTree thunk
    InstanceDispatchTests.cs
    PartitionAllocatorTests.cs     — attach, detach, coalesce, full, upgrade
    MultiBlueprintTests.cs         — two Instances same entity, peer call
    WorldSingletonTests.cs
    Blackboard1024AccessTests.cs   — AiPrimitive working state attach
    TierUpgradeTests.cs            — BlueprintMaintenanceSystem upgrades 1024→4096
  HotReload/
    SoftReloadTests.cs
    HardResetTests.cs
    AlcUnloadTests.cs              — GC reclaims old ALC
    PerSlotIsolationTests.cs       — one slot's reset doesn't disturb others
  Debug/
    BreakpointTests.cs
    TraceTests.cs
    WatchTests.cs
  Latent/
    InstanceDelayTests.cs          — BlueprintLatentCursor pattern
    AiPrimitiveWaitTests.cs        — Running-return pattern
    StaleCursorTests.cs
  Channels/
    ChannelCommandLoweringTests.cs   — Command nodes emit correct CQRS
    WaitForChannelTests.cs           — Wait latent in both dispatch shapes
  Events/
    EngineEventPollingTests.cs       — Instance OnHit fires for hits.Target == self
    CustomEventSyncTests.cs          — CallCustomEventNode → direct call
    PeerCallTests.cs                 — CallPeerBlueprintNode → slot lookup
  EndToEnd/
    JsonRoundtripTests.cs
    EditorModelTests.cs
```

### 11.5 Mock contract enforcement table

The mock implementations must enforce these engine rules for tests to be trustworthy:

| Rule | Real engine | Mock enforces |
|---|---|---|
| `IsAlive` returns true mid-frame after ECB destroy | True until Playback | Defers entity removal until `Playback()` |
| `GetComponentRO<T>` returns ref to chunk memory | True | Returns ref to stable backing array slot |
| `ReadEvents<T>` stable for full tick | True | Same `IReadOnlyList<T>` instance for tick |
| Direct singleton write during Simulation forbidden | Throws | Throws |
| ECB writes deterministic playback order | True | Queue order = playback order |
| Tier upgrade only via BeforeSync system | True | Test ticks must run BlueprintMaintenanceSystem in BeforeSync phase |
| `AddEmptyComponent<T>` works for >1024-byte components | Will work after engine extension | Implements the API; default-initializes the component |

---

## 12. Minimal ImGui Editor

### 12.1 Integration with engine `WindowManager`

Per the engine's `WindowManager` pattern: each editor window is a `ManagedWindow` subclass, registered via an `IWindowRegistrar`-implementing subsystem.

```csharp
namespace Hrot.Blueprints.Editor;

public sealed class BlueprintEditorSubsystem : ISubsystem, IWindowRegistrar
{
    private readonly BlueprintAssetListPanel _assetList;
    private readonly BlueprintAssetEditorPanel _assetEditor;
    /* ... */

    public void RegisterWindows(WindowManager windowManager)
    {
        windowManager.RegisterWindow(new BlueprintAssetListWindow(_assetList));
        windowManager.RegisterWindow(new BlueprintAssetEditorWindow(_assetEditor));
        windowManager.RegisterWindow(new BlueprintDiagnosticsWindow(_diagnostics));
        windowManager.RegisterWindow(new BlueprintHotReloadLogWindow(_reloadLog));
        windowManager.RegisterWindow(new BlueprintRuntimeEntitiesWindow(_runtime));
        windowManager.RegisterWindow(new BlueprintInstanceInspectorWindow(_inspector));
        windowManager.RegisterWindow(new BlueprintDebugSessionWindow(_debug));
        windowManager.RegisterWindow(new BlueprintEngineEventCatalogWindow(_eventCatalog));
        windowManager.RegisterWindow(new BlueprintChannelCommandCatalogWindow(_channelCatalog));
    }
}
```

### 12.2 Windows

| Window | Purpose | Notes |
|---|---|---|
| Asset List | List loaded assets, open for editing | + new asset wizard |
| Asset Editor | Edit asset via StructEdit forms | Compile & Reload buttons (Quick / Full Rebuild — see §12.4) |
| Diagnostics | Validate/compile diagnostics, clickable | Jumps to offending node |
| Hot Reload Log | Per-slot soft/hard reload events | Time, asset, outcome |
| Runtime Entities | Entities with `BlueprintBlackboard*`, slot counts | Drives Instance Inspector |
| Instance Inspector | Per-slot StructEdit-rendered State | Read-write — can mutate live state |
| Debug Session | Breakpoints, step controls, locals | Pops on `OnBreakpointHit` |
| Engine Event Catalog | Read-only list of engine events available to Blueprints | Slide-out from Asset Editor (per your preference) |
| Channel Command Catalog | Read-only list of channel commands available to Blueprints | Slide-out from Asset Editor |

### 12.3 Compile & Reload — in-memory vs MSBuild

Per your decision (E-1 with in-memory live-game support):

- **Quick Reload (in-memory, default)**: Compile the open asset(s) via the in-process compiler library. Emit PE + PDB to in-memory streams. Load into a per-asset *patch ALC*. Register Blueprints into `BlueprintRegistry` via the same path the hot-reload coordinator uses. Sub-second turnaround. Live game continues uninterrupted.
- **Full Rebuild (MSBuild)**: Save `.bp.json` to disk, kick off `dotnet build`. File watcher fires existing `AiHotReloadCoordinator`. Multi-second turnaround. Pre-commit validation path.

Both modes produce identical runtime behavior; only the load mechanism differs. Patch ALCs from Quick Reload integrate cleanly because all Blueprint dispatch goes through managed delegates registered into `BlueprintRegistry` (not through cross-ALC type identity).

### 12.4 Slice 1 extras (per your confirmations)

- **Force Hard Reset button** per slot in Instance Inspector, gated behind a "developer mode" toggle.
- **Engine Event Catalog and Channel Command Catalog** rendered as slide-out panels inside Asset Editor (vs always-visible windows).

### 12.5 What we do NOT build in Slice 1

- No node canvas.
- No live-validation while typing (validation runs on button or `IEditSession.IsDirty` debounce).
- No refactoring operations from UI.

---

## 13. Engine-side Changes Summary

### 13.1 csproj changes to `Hrot.AI.Behaviors.csproj`

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>
  <DebugType>portable</DebugType>
  <DebugSymbols>true</DebugSymbols>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\..\Hrot.Blueprints.Generators\Hrot.Blueprints.Generators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\..\..\FDP\Toolkits\Fdp.Toolkits.Blueprints\Fdp.Toolkits.Blueprints.csproj" />
  <AdditionalFiles Include="Blueprints\**\*.bp.json" />
</ItemGroup>
```

### 13.2 `AiHotReloadCoordinator` refactor

- Attribute-driven registrar discovery on background thread (no hardcoded type names).
- Pre-resolved `MethodInfo` list dispatched on main thread.
- Optional PDB loading (constructor-gated).

### 13.3 `FdpJsonOptionsRegistry` addition

```csharp
public static JsonSerializerOptions CreateExtended(params JsonConverter[] customConverters);
```

For Blueprint-specific converters without polluting the global registry.

### 13.4 New: three `BlueprintBlackboard*` component types

Three new `ComponentId` values in `GlobalComponentIds`. All three live in `Fdp.Toolkits.Blueprints`.

### 13.5 New: `IEntityCommandBuffer.AddEmptyComponent<T>` (confirmed by engine team)

```csharp
public interface IEntityCommandBuffer
{
    // ... existing ...

    /// <summary>
    /// Adds a default-initialized component to the entity. Bypasses the
    /// 1024-byte ECB payload limit. Zero-initialized at playback time on
    /// the main thread.
    /// </summary>
    void AddEmptyComponent<T>(Entity entity) where T : unmanaged;
}
```

### 13.6 New: Slice-2-anticipated `Blackboard1024` partition allocator

The Slice 1 constraint "one AiPrimitive working-state Blueprint per entity" exists because `Blackboard1024` is currently a single-typed projection slot. **Slice 2 lifts this via Option β** — partitioning AiPrimitive working state into the existing `BlueprintBlackboard*` tiers (reusing the proven `BlueprintBlackboardPartitions` allocator), keyed per node by `FNV-1a(BehaviorAssetId, NodeVisualId)`. Retrofitting an allocator onto the engine `Blackboard1024` (or adding a new `BlueprintAiWorking1024`) was **rejected** — it would ripple through the FastHSM/BTree kernels. See `BTree_AiActionParameterBinding_Detailed_Design.md` §4 (incl. the three mandated fixes).

Not blocking Slice 1.

### 13.7 No engine changes that block Slice 1

If engine team pushes back on any of the above:
- csproj changes: non-negotiable but trivial.
- Coordinator refactor: small, isolated.
- `CreateExtended`: ergonomic only.
- `BlueprintBlackboard*` ComponentIds: three IDs of 256, non-negotiable.
- `AddEmptyComponent`: engine team has confirmed.
- Slice 2 `Blackboard1024` partition allocator: not in Slice 1 critical path.

---

## 14. Deferred to Slice 2+

| Feature | Why deferred | Slice |
|---|---|---|
| Visual node canvas editor | Big; StructEdit forms suffice for Slice 1 | 3+ |
| Macros, interfaces, animation, UI, RPC graphs | Each is its own surface | 2/3 |
| Cross-entity dispatcher calls | Slice 2 routes via deferred events | 2 |
| Save/load authoring story | Bytes save automatically; typed access needs design | 2 |
| Map/Set containers | Validator + emitter changes | 2 |
| Worker-thread Blueprint graphs | Deep ECB semantics work needed | 3+ |
| Refactoring API (promote/collapse/inline) | Authoring-time, additive | 2 |
| Visual debugger UI (canvas-aware) | Protocol exists; UI is canvas work | 3 |
| Defragmentation pass for blackboards | Free-list sufficient until fragmentation pain | 2 if needed |
| `[BlueprintExposedEvent]` / `[BlueprintExposedChannelCommand]` attribute-driven catalogs | Curated suffices for Slice 1 | 2 |
| **Partition allocator on `Blackboard1024`** | Single-slot per entity sufficient for Slice 1 | 2 |
| Multiple world-singleton Blueprints per tier | One per tier sufficient | 2 |
| Integration test harness with clusterop | Defer until real Hrot integration | post-Slice 1 |
| Latent execution in AiPrimitive graphs hosted as BTree Condition | Validator forbids in Slice 1 (correct per architect); reconsider if needed | n/a (architectural) |

---

## 15. Engine Authoring Catalogs (NEW)

### 15.1 Three catalogs

The Blueprint editor and compiler consult three catalogs of "things authors can wire into their graphs":

1. **Engine Event Catalog** — engine event types Blueprints can subscribe to (Instance dispatch only).
2. **Channel Command Catalog** — (Channel, ActionId) pairs Blueprints can write.
3. **Wait Primitive Catalog** — kinds of latent waits supported (`WaitForChannel`, `WaitForEvent`, `WaitForRingBufferResult`).

All three follow the same evolution path: Slice 1 hand-curated, Slice 2 attribute-driven via `[BlueprintExposedEvent]`, `[BlueprintExposedChannelCommand]`, etc.

### 15.2 Engine Event Catalog

```csharp
public sealed record EngineEventCatalogEntry(
    Type ClrType,                          // e.g. typeof(HitEvent)
    string EventName,                      // "OnHit" — matches event graph name
    string DisplayName,                    // editor combo label
    string Category,
    string? Tooltip,
    string? TargetFieldName,               // for Self-filtering
    IReadOnlyList<EngineEventField> Fields);

public sealed record EngineEventField(string FieldName, Type FieldType);

public static class EngineEventCatalog
{
    private static readonly List<EngineEventCatalogEntry> _entries = new()
    {
        new(typeof(HitEvent), "OnHit", "On Hit", "Combat",
            "Fired when this entity takes damage.",
            TargetFieldName: "Target",
            Fields: new[]
            {
                new EngineEventField("Attacker",  typeof(Entity)),
                new EngineEventField("Damage",    typeof(float)),
                new EngineEventField("Direction", typeof(Vector3)),
            }),
        // ... ~10-20 entries for Slice 1, curated by hand ...
    };

    public static IReadOnlyList<EngineEventCatalogEntry> All => _entries;
    public static bool TryGet(string eventName, out EngineEventCatalogEntry entry);
}
```

### 15.3 Channel Command Catalog

```csharp
public sealed record ChannelCommandCatalogEntry(
    string ChannelType,                    // "LocomotionChannel"
    string ActionId,                       // "ActionIdMoveTo"
    string DisplayName,                    // "Move To"
    string Category,                       // "Locomotion"
    string? Tooltip,
    Type ParamsType,                       // typeof(MoveToParams)
    IReadOnlyList<ChannelCommandField> Fields);  // for editor pin types

public sealed record ChannelCommandField(string FieldName, Type FieldType,
                                            string DisplayName, string? Tooltip);

public static class ChannelCommandCatalog
{
    private static readonly List<ChannelCommandCatalogEntry> _entries = new()
    {
        new("LocomotionChannel", "ActionIdMoveTo", "Move To", "Locomotion",
            "Commands the entity to navigate to a target position.",
            typeof(MoveToParams),
            Fields: new[]
            {
                new ChannelCommandField("Destination",   typeof(Vector3), "Destination", null),
                new ChannelCommandField("Speed",         typeof(float),   "Speed",       "Target speed in m/s"),
                new ChannelCommandField("ArrivalRadius", typeof(float),   "Arrival Radius", null),
            }),
        // ... entries for Weapon/Fire, Weapon/AimAndFire, Interaction/Use, etc. ...
    };

    public static IReadOnlyList<ChannelCommandCatalogEntry> All => _entries;
    public static bool TryGet(string channel, string action, out ChannelCommandCatalogEntry entry);
}
```

### 15.4 Wait Primitive Catalog

```csharp
public sealed record WaitPrimitiveCatalogEntry(
    string WaitKind,                       // "WaitForChannel", "WaitForEvent", "WaitForRingBufferResult"
    string DisplayName,
    string Category,
    string? Tooltip,
    Type? StatusComponentType,             // for WaitForChannel; null otherwise
    Type? EventOrResultType);              // for WaitForEvent / WaitForRingBufferResult

public static class WaitPrimitiveCatalog
{
    private static readonly List<WaitPrimitiveCatalogEntry> _entries = new()
    {
        new("WaitForChannel:LocomotionChannel", "Wait For Locomotion", "Locomotion",
            "Suspends until the LocomotionChannel.Status transitions to Success or Failure.",
            typeof(LocomotionChannel), null),
        new("WaitForChannel:WeaponChannel", "Wait For Weapon", "Combat",
            "Suspends until the WeaponChannel.Status transitions.",
            typeof(WeaponChannel), null),
        new("WaitForEvent:BehaviorFinishedEvent", "Wait For Behavior Finished", "AI",
            "Suspends until a BehaviorFinishedEvent matches this entity's behavior instance.",
            null, typeof(BehaviorFinishedEvent)),
        // ... more entries ...
    };

    public static IReadOnlyList<WaitPrimitiveCatalogEntry> All => _entries;
}
```

### 15.5 Compiler usage

For each event graph in an Instance asset, the compiler looks up `graph.Name` in `EngineEventCatalog`. If found: validates inputs, emits a `ReadEvents<TClrType>()` poll loop filtered by Self.

For each `ChannelCommandNode`, the compiler looks up the (channel, action) in `ChannelCommandCatalog`. The catalog tells it the param-struct type to use for unsafe write. Emission:

```csharp
ref var ch = ref world.GetComponentRW<LocomotionChannel>(self);
ch.ActiveAction = NavigationConstants.ActionIdMoveTo;
unsafe {
    fixed (byte* paramSlot = ch.Params) {
        *(MoveToParams*)paramSlot = new MoveToParams {
            Destination   = pin_destination,
            Speed         = pin_speed,
            ArrivalRadius = pin_arrival_radius,
        };
    }
}
ch.ActionInstanceId++;
```

For each `WaitForChannelNode` / `WaitForEventNode`, the compiler emits dispatch-aware lowering (AiPrimitive: `Running` return; Instance: `BlueprintLatentCursor` switch). The catalog provides the type to poll.

### 15.6 Slice 2 evolution

Replace hand-curated lists with assembly-scan discovery via:

```csharp
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class BlueprintExposedEventAttribute : Attribute { /* fields */ }

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, Inherited = false)]
public sealed class BlueprintExposedChannelCommandAttribute : Attribute { /* fields */ }
```

Engine team annotates the relevant events / channel actions; the catalog becomes a passive scan result. No Blueprint-side changes.

---

## 16. Cross-Blueprint Composition on the Same Entity

### 16.1 Spec — declaration

```json
{
  "name": "Door",
  "dispatch": "Instance",
  "callablePeers": [
    "00000000-7f3a-9e21-..."
  ]
}
```

### 16.2 Spec — call site

`CallPeerBlueprintNode` references peer by `AssetId` and method name. Editor's peer-picker combo is populated from this list.

### 16.3 Spec — generated code

For Instance-to-Instance peer calls on same entity:

```csharp
if (BlueprintBlackboardPartitions.TryGetSlotOffset(
        memory, MathLib_Bp.BlueprintId, out int peerOffset))
{
    ref var peerState = ref Unsafe.As<byte, MathLib_Bp.State>(ref memory[peerOffset]);
    int t0 = MathLib_Bp.SomeFunction(ref peerState, view, ecb, self, time, /* args */);
    // ... use t0 ...
}
```

For library peer calls: trivial static method invocation (no slot lookup).

### 16.4 Spec — validator rules

- `targetPeerAssetId` must be in `asset.callablePeers`.
- Target method must be public on the peer asset.
- Parameter pins match peer method signature.
- Peer must be Library or Instance (AiPrimitive not callable as a peer in Slice 1; only via its `BlueprintCall` hosting if declared).
- For Instance-to-Instance peer calls: caller and callee must both be Instance dispatch; cross-entity rejected in Slice 1.

### 16.5 Slice 2 enhancements

- "Shareable" Blueprint declaration (callable without peer declaration).
- Cross-entity peer calls via deferred events.
- Peer-interface contracts.

---

## 17. Open Questions

After three rounds of review + Q-OPEN-A/B/C resolutions + v1.2 review feedback, the open list is short:

**Q-OPEN-D**: (resolved — see Final Resolutions addendum)

**Q-OPEN-E**: (resolved — see Final Resolutions addendum)

That's it. No structural questions remain. The architecture is implementable.

---

## 18. Decisions Locked

**From v1.0/v1.1 (carried forward):**
- One reloadable DLL.
- Roslyn incremental generator; in-memory Roslyn only in tests.
- Asset format JSON; engine's `FdpJsonOptionsRegistry` + `JsonAestheticFormatter`.
- StructEdit for editor.
- State preservation via structure-hash + soft/hard reload.
- Stable Guids in JSON.
- Three dispatch kinds in Slice 1.
- Decoupled core (Fdp.Core schema only, no Fdp.Toolkits runtime).
- Debug B + C from day one.
- xUnit; per-test ALC isolation; GC verification.

**From v1.1 + addendum:**
- `BlueprintId` = FNV-1a 32-bit of asset Guid (compile-time constant).
- Three blackboard tiers (1024/4096/16384) with partition allocator for Instance dispatch.
- Multi-Blueprint per entity for Instance dispatch.
- `callablePeers` for cross-Blueprint sync calls (same entity).
- Cross-entity calls deferred to Slice 2.
- One `[BlueprintRegistrar]` per asset.
- Engine events via `ISimulationView.ReadEvents<T>()` polling.
- Hand-curated catalogs in Slice 1.
- BehaviorAction validator restricts to ECB-mediated mutations only.
- World-singleton via `SetSingletonUnmanaged<T>`.
- Tick system sequential.
- One-frame delay accepted for cross-entity events.
- Latent execution replay-safe.
- Hot-reload attribute-driven scan on background thread.
- Per-slot soft/hard reconciliation inside `BlueprintTickSystem`.
- Roslyn pinned: netstandard2.0, `Microsoft.CodeAnalysis.CSharp 4.8.0`, `Microsoft.CodeAnalysis.Analyzers 3.3.4` with `PrivateAssets="all"`.
- Engine adapter in `Fdp.Toolkits.Blueprints`.
- Blackboards not network-replicated; brain-role-only.
- `AddEmptyComponent` ECB opcode (engine team confirmed).
- Tier upgrade via dedicated `BlueprintMaintenanceSystem` in `SystemPhase.BeforeSync`.
- Generator filenames include `BlueprintId:X8`.

**Added in v1.2:**
- Dispatch kind renamed: `BehaviorAction` → `AiPrimitive`. `BlueprintBlackboard*` reserved for Instance dispatch.
- AiPrimitive `intent`: `Action` (Success/Failure/Running) or `Condition` (Success/Failure, no latent nodes).
- AiPrimitive `hostings`: `{BTreeAction, BTreeCondition, HsmAction, HsmGuard, BlueprintCall}` — declared subset.
- AiPrimitive parameters in `BrainBlackboard.BehaviorParameters` (100 B), working state in `Blackboard1024`.
- Slice 1 constraint: one AiPrimitive working-state Blueprint per entity (Slice 2 adds partition allocator to `Blackboard1024`).
- Three catalogs: Engine Event, Channel Command, Wait Primitive — hand-curated in Slice 1.
- `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode` node kinds.
- Wait lowering is dispatch-aware: AiPrimitive emits `Running` return; Instance emits `BlueprintLatentCursor` switch.
- Each AiPrimitive Wait costs one tick in the host kernel (no within-tick fall-through optimization in Slice 1).
- Q-OPEN-D resolved: one AiPrimitive working-state Blueprint per entity in Slice 1; `Blackboard1024` partition allocator deferred to Slice 2.
- Q-OPEN-E resolved: MoveToAndFire AiPrimitive scenario is a required Slice 1 acceptance demo.
- **No `IBlueprint*` wrapper interfaces.** Generated code uses Fdp.Core types directly.
- **`Hrot.Blueprints.Core` references `Fdp.Core`.** Decoupling rule: core uses Fdp.Core schema/interfaces only, never Fdp.Toolkits runtime.
- **`Hrot.Blueprints.Engine` adapter assembly dropped.** Runtime systems live in `Fdp.Toolkits.Blueprints`.
- Editor uses Fdp.Core + Fdp.Presentation directly via StructEdit.
- Test harness mocks implement Fdp.Core interfaces (`ISimulationView`, `IEntityCommandBuffer`, `EntityRepository`). Mock contract = engine contract.
- Mocks enforce real engine phase/threading rules.

---

## 19. Glossary

(Inherits v1.1 glossary; additions and changes below.)

- **AiPrimitive** *(replaces BehaviorAction)* — Dispatch kind for a single-method graph hostable by BTree action, BTree condition, HSM action, HSM guard, and/or as a BlueprintCall.
- **AiPrimitive intent** — `Action` or `Condition`. Conditions cannot be latent and cannot return `Running`.
- **AiPrimitive hosting** — Which engine subsystem invokes the graph: `BTreeAction`, `BTreeCondition`, `HsmAction`, `HsmGuard`, `BlueprintCall`. Asset declares a subset.
- **Channel Command Catalog** — Curated registry of (channel, ActionId) pairs exposed to Blueprint authoring as commandable nodes.
- **`ChannelCommandNode`** — Visual node lowering to a channel write (`ActiveAction`, `Params`, `ActionInstanceId++`).
- **Engine Event Catalog** — Curated registry of engine event types exposed to Instance Blueprint authoring as event-graph subscriptions.
- **`Fdp.Core`** — The engine's stable schema/interface assembly. `Hrot.Blueprints.Core` references this; runtime code targets the interfaces it defines.
- **`Fdp.Toolkits.Blueprints`** — The Blueprint runtime adapter assembly. Holds `BlueprintRegistry`, tick systems, partition allocator, blackboard components, attributes.
- **`MockEntityRepository` / `MockSimulationView` / `MockEntityCommandBuffer`** — Test-harness implementations of Fdp.Core types. Enforce real engine phase/threading rules.
- **Wait Primitive Catalog** — Curated registry of kinds of latent waits available to Blueprint authoring.
- **`WaitForChannelNode` / `WaitForEventNode`** — Latent visual nodes that compile differently depending on dispatch kind.

---

*End of v1.2. Detailed designs for compiler internals, runtime adapter, debug protocol, test harness, and editor follow as separate documents.*
