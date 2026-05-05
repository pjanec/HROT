# BATCH-02 Instructions: Phase 2 — Gizmo Contracts and Data-Driven Orchestration

**Tasks:** TASK-GZ004, TASK-GZ005, TASK-GZ006
**Design reference:** [DESIGN.md](../DESIGN.md) §2.1–2.5
**Task detail reference:** [TASK-DETAIL.md](../TASK-DETAIL.md) — sections for TASK-GZ004, GZ005, GZ006

---

## Prerequisites

BATCH-01 has been merged. All Phase 1 types are available in `Fdp.Toolkit.Diagnostics.Gizmos`:
`Rgba32`, `DebugPrimitive`, `IDebugDrawBuilder`, `DebugPrimitiveBuffer`, `StringInternMap`.

---

## Mandatory Workflow

1. Read [TASK-DETAIL.md](../TASK-DETAIL.md) sections for GZ004, GZ005, GZ006 in full before writing any code.
2. Implement all three tasks in order (GZ004 first — others depend on it).
3. Build: `dotnet build IOS-IG-SimHost.sln --nologo` — zero errors required.
4. Test: `dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --nologo` — all pass.
5. Write a BATCH-02-REPORT.md in `.dev/gizmos-1/reports/`.

---

## ECS API Reference (for accurate code)

### Namespaces
- `Fdp.Core` — `Entity`, `BitMask256`, `EntityRepository`, `EntityHeader`, `ComponentTypeRegistry`
- `Fdp.ModuleHost.Abstractions` — `ISimulationView`, `IEcsModuleSystem`, `SystemPhase`, `UpdateInPhase`, `QueryBuilder`, `EntityQuery`
- `Fdp.Toolkit.Lifecycle.Events` — `ConstructionOrder` (struct), `DestructionOrder` (struct)
- `Fdp.Toolkit.Behavior.Events` — `AssignBehaviorEvent` (sealed class), `ClearBehaviorEvent` (struct)
- `Hrot.IG.Components` — `SelectionState` (struct with `bool IsSelected`, `bool IsPrimarySelection`)
- `Fdp.Toolkit.Diagnostics.Gizmos` — all Phase 1 types

### Key API patterns
```csharp
// System declaration
[UpdateInPhase(SystemPhase.PostSimulation)]
public class MySys : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime) { ... }
}

// Reading unmanaged events
ReadOnlySpan<ConstructionOrder> orders = view.ReadEvents<ConstructionOrder>();
ReadOnlySpan<DestructionOrder>  deaths = view.ReadEvents<DestructionOrder>();
ReadOnlySpan<ClearBehaviorEvent> clears = view.ReadEvents<ClearBehaviorEvent>();

// Reading managed events (class events)
IReadOnlyList<AssignBehaviorEvent> assigns = view.ReadManagedEvents<AssignBehaviorEvent>();

// Getting entity component mask (requires EntityRepository cast)
if (view is EntityRepository repo)
{
    ref EntityHeader header = ref repo.GetHeader(entity.Index);
    // header.ComponentMask : BitMask256
}

// Querying for entities with SelectionState
EntityQuery selQuery = view.Query().With<SelectionState>().Build();
foreach (Entity e in selQuery) { ... }

// Component type ID lookup
int id = ComponentTypeRegistry.GetId(typeof(MyComponent));
// Returns -1 if not registered. GizmoRegistry.Register must throw if any ID == -1.

// BitMask256 match check
bool matches = BitMask256.HasAll(header.ComponentMask, rule.RequiredMask);

// Entity liveness
bool alive = view.IsAlive(entity);
```

---

## TASK-GZ004: Gizmo Contracts

See TASK-DETAIL.md §TASK-GZ004. Implement all four files as specified:
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoDefinition.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IGizmoVisibilityPolicy.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/GizmoRegistry.cs`

Key implementation notes:
- `AlwaysVisiblePolicy.Instance` must be `static readonly AlwaysVisiblePolicy`.
- `GizmoRegistry.Register` uses `ComponentTypeRegistry.GetId(type)` (returns -1 if not registered). If any required component ID is -1, throw `InvalidOperationException` naming the offending type.
- `CompiledGizmoRule` is an `internal struct` inside `GizmoRegistry.cs` (or a separate file in the same namespace).
- `BitMask256` is a value type; call `mask.SetBit(id)` for each component ID to build `RequiredMask`.

---

## TASK-GZ005: DataDrivenGizmoSystem

See TASK-DETAIL.md §TASK-GZ005. Create:
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`

Key implementation notes:
- Constructor: `DataDrivenGizmoSystem(GizmoRegistry registry, IDebugDrawBuilder drawBuilder)`.
- Pre-allocate `_globalVisibilityCache = new bool[registry.Rules.Count]` in the constructor.
- `GlobalDebugSettings` singleton does NOT exist in the codebase yet (defined in Phase 6 / GZ015). For BATCH-02, skip the singleton check entirely and always use selection-only mode. Add a comment: `// GlobalDebugSettings integration deferred to GZ015 (Phase 6).`
- `_activeGizmos` is `Dictionary<Entity, List<CompiledGizmoInstance>>`.
- `CompiledGizmoInstance` is a local private struct: `IStatefulGizmo Instance`, `IGizmoDefinition Definition`, `int RuleIndex`.
- On `ConstructionOrder`: cast view to `EntityRepository` to read `GetHeader(evt.Entity.Index).ComponentMask`. Evaluate all `Rules`. For each matching rule: call `rule.Definition.CreateInstance()`, call `instance.OnInitialize(view, evt.Entity)`, add to `_activeGizmos`.
- On `DestructionOrder`: if entity is in `_activeGizmos`, call `OnTeardown()` on each instance, remove.
- Execute (selection-only): build a query `view.Query().With<SelectionState>().Build()` each frame, iterate, check `view.GetComponentRO<SelectionState>(entity).IsSelected`, then call `UpdateAndDraw` for matching active gizmos.
- Call `view.IsAlive(entity)` before each `UpdateAndDraw`.
- Pre-evaluate `_globalVisibilityCache[i] = rule.Definition.VisibilityPolicy.IsGloballyEnabled(view)` once per frame before the entity loop.
- Skip draw if `!_globalVisibilityCache[ruleIndex]` or `!rule.Definition.VisibilityPolicy.IsEntityVisible(view, entity)`.

**IMPORTANT for tests**: `SelectionState` is in `Hrot.IG.Components` which references `Hrot.Map.Common`. Check if the Fdp.Toolkits project already references `Hrot.Map.Common` or if you need to add it to the project reference. If Hrot types are not accessible from `Fdp.Toolkits`, use a query on a component type that IS accessible (e.g., use `ISimulationView.HasComponent<SelectionState>` is not available without the reference). **Preferred approach**: define an interface `ISelectableComponent` or simply use `view.HasComponent<SelectionState>(entity)` after calling `view.Query().With<SelectionState>().Build()`. Check the existing project references in `Fdp.Toolkits.csproj` before deciding.

---

## TASK-GZ006: BehaviorGizmoManagerSystem

See TASK-DETAIL.md §TASK-GZ006. Create:
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/IBehaviorGizmoFactory.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoRegistry.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/BehaviorGizmoManagerSystem.cs`

Key implementation notes:
- `BehaviorGizmoRegistry`: `Dictionary<string, IBehaviorGizmoFactory>` keyed by `BehaviorName`.
- `BehaviorGizmoManagerSystem` constructor: `(BehaviorGizmoRegistry behaviorRegistry, IDebugDrawBuilder drawBuilder)`.
- `_activeBehaviorGizmos` is `Dictionary<Entity, (IStatefulGizmo Instance, IBehaviorGizmoFactory Factory)>`.
- `AssignBehaviorEvent` is a `sealed class` — read via `view.ReadManagedEvents<AssignBehaviorEvent>()`.
- `ClearBehaviorEvent` is a `struct` — read via `view.ReadEvents<ClearBehaviorEvent>()`.
- On `AssignBehaviorEvent`: if entity already has an active behavior gizmo, call `OnTeardown()` and `Factory.Return(instance)` first. Then rent new instance, call `OnInitialize`, store.
- On `ClearBehaviorEvent` and `DestructionOrder`: call `OnTeardown()`, `Factory.Return(instance)`, remove from map.
- `AssignBehaviorEvent` for unknown behavior name: silently ignore (no exception).
- Same visibility mode comment as GZ005 (GlobalDebugSettings deferred to GZ015).
- For execute: iterate `_activeBehaviorGizmos`, check `view.IsAlive(entity)`, check `IsSelected` via `view.HasComponent<SelectionState>(entity) && view.GetComponentRO<SelectionState>(entity).IsSelected`, call `UpdateAndDraw`.

---

## Test File

Create: `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs`

Test classes required:
- `GizmoRegistryTests` — covers all SC-GZ004-x
- `DataDrivenGizmoSystemTests` — covers all SC-GZ005-x
- `BehaviorGizmoManagerSystemTests` — covers all SC-GZ006-x

**Test harness notes:**
- All tests need `EntityRepository` directly (not through `ISimulationView`) so they can call `repo.RegisterComponent<T>()`, `repo.RegisterEvent<T>()`, `repo.PublishEvent<T>(evt)`, `repo.SwapBuffers()` (or equivalent).
- For tests to work with component type IDs, components need `[ComponentId]` attributes. In tests, use stub components from existing registered types (like `SelectionState`) or declare test-only components in the test file with unique IDs in the test range (50000+). Register them on the `EntityRepository` before the test system runs.
- Mock `IStatefulGizmo` — track calls to `OnInitialize`, `UpdateAndDraw`, `OnTeardown` via counters.
- Mock `IGizmoVisibilityPolicy` — track calls to `IsGloballyEnabled`, `IsEntityVisible`, control return values.
- Mock `IBehaviorGizmoFactory` — track `Rent()`/`Return()` call counts.
- To publish events in tests: `((EntityRepository)repo).Bus.Publish(new ConstructionOrder { Entity = e })` then swap buffers. Check the existing test files in `Fdp.Toolkits.Tests` for the exact pattern (e.g., `SubEntityTests.cs`, `NetworkGatewaySystemTests.cs`).
- SC-GZ005-8 (global visibility cache once per frame): mock `IsGloballyEnabled` to count invocations; assert it equals the number of registered rules (not the number of entities).

**Test component declarations example** (in test file, after looking at how existing tests declare test components):
```csharp
// Use an existing registered component type for tests, or declare test-only stubs.
// Do NOT redeclare SelectionState - it already has [ComponentId(GlobalComponentIds.SelectionState)].
// For a "required component" in GizmoRegistry tests, use a component type that is
// already registered OR declare a test stub with a unique [ComponentId] in the 55000+ range.
```

---

## Report Format

See `.dev/gizmos-1/reports/BATCH-01-REPORT.md` for the report format. Include:
- Files delivered per task
- Any design deviations
- Known issues / gaps
