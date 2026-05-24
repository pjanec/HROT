# BATCH-05 Instructions — Phase 6: ECS Translators and Wiring

**Workstream:** tkb-1  
**Batch:** 05  
**Tasks covered:** TKB-012, TKB-013, TKB-014  
**Design reference:** `.dev/tkb-1/DESIGN.md`  
**Task detail reference:** `.dev/tkb-1/TASK-DETAIL.md` (TKB-012, TKB-013, TKB-014 sections)

---

## Overview

This batch implements Phase 6: the ECS projection / translator layer. Components are applied
to entities by injecting descriptor DTOs into ECS via `ITkbEntityTranslator` implementations
rather than the deleted `ApplyTo` method.

**Debt items addressed:**
- D-002 (P2): `WithHeavyMemory` no-op — `Blackboard1024` restoration is OUT OF SCOPE for this
  batch (requires a managed-component translator not yet designed). Remains in debt tracker.
- D-003 (P2): `UrbanAmbushIntegrationTests` fail — these require additional translators for
  combat/HSM/AI components not part of TKB-013. Remain in debt tracker; the translator loop
  wiring in TKB-014 is the prerequisite but not sufficient on its own.

---

## Task 1 — TKB-012: `ITkbEntityTranslator` interface

**File to create:** `FDP/Engine/Fdp.Core/Abstractions/ITkbEntityTranslator.cs`  
**Namespace:** `Fdp.Interfaces` (matches all other Abstractions files in that folder)

```csharp
using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Projects N TKB descriptor DTOs into M ECS components on a live entity.
    /// Mirrors IDescriptorTranslator for TKB content; same N:M projection mechanics.
    /// </summary>
    public interface ITkbEntityTranslator
    {
        /// <summary>
        /// Returns the CLR types of TKB descriptor DTOs this translator consumes.
        /// The pipeline uses this to track which descriptors have been projected.
        /// </summary>
        IEnumerable<Type> GetConsumedDescriptors();

        /// <summary>
        /// Projects data from <paramref name="template"/> into ECS components on
        /// <paramref name="entity"/>. Implementations MUST call
        /// <c>repo.IsComponentTypeRegistered&lt;T&gt;()</c> before every
        /// <c>repo.AddComponent&lt;T&gt;()</c> call.
        /// </summary>
        void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
    }
}
```

No tests needed for the interface itself — it is verified by TKB-013 and TKB-014.

---

## Task 2 — TKB-013: `VehicleKinematicsTkbTranslator`

**File to create:**  
`FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs`  
**Namespace:** `CarKinem.Tkb`

**Implementation:**

```csharp
using System;
using System.Collections.Generic;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace CarKinem.Tkb
{
    /// <summary>
    /// Reference ITkbEntityTranslator implementation.
    /// Projects VehicleParametersDto into four ECS components:
    ///   VehicleParams, VehicleState, NavState, PhysicsCollider.
    /// Each AddComponent call is guarded by IsComponentTypeRegistered.
    /// </summary>
    public sealed class VehicleKinematicsTkbTranslator : ITkbEntityTranslator
    {
        public IEnumerable<Type> GetConsumedDescriptors()
        {
            yield return typeof(VehicleParametersDto);
        }

        public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
        {
            var dto = template.GetDescriptor<VehicleParametersDto>();
            if (dto == null) return;

            if (repo.IsComponentTypeRegistered<VehicleParams>())
                repo.AddComponent(entity, new VehicleParams
                {
                    Length     = dto.Length,
                    Width      = dto.Width,
                    WheelBase  = dto.Length * 0.6f,
                    MaxSpeedFwd = dto.MaxSpeedFwd,
                    MaxAccel   = dto.MaxAccel
                });

            if (repo.IsComponentTypeRegistered<VehicleState>())
                repo.AddComponent(entity, new VehicleState { Speed = 0, SteerAngle = 0 });

            if (repo.IsComponentTypeRegistered<NavState>())
                repo.AddComponent(entity, new NavState { Mode = KinematicsMode.None });

            if (repo.IsComponentTypeRegistered<PhysicsCollider>())
                repo.AddComponent(entity, new PhysicsCollider
                {
                    Radius         = System.Math.Max(dto.Length, dto.Width) / 2f,
                    CollisionLayer = 1
                });
        }
    }
}
```

**Important:** `IsComponentTypeRegistered<T>` only works for unmanaged types (blittable structs).
All four target types (`VehicleParams`, `VehicleState`, `NavState`, `PhysicsCollider`) are
unmanaged structs — confirmed.

---

## Task 3 — Tests for `VehicleKinematicsTkbTranslator`

**File to create:**  
`FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Tkb/VehicleKinematicsTkbTranslatorTests.cs`  
**Namespace:** `CarKinem.Tkb.Tests`

**Required usings:**
```csharp
using CarKinem.Core;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;
```

**Helper:** Build a `TkbTemplate` with a `VehicleParametersDto` descriptor.

```csharp
private static TkbTemplate MakeTemplate(float length = 6f, float width = 2.5f,
    float maxSpeedFwd = 20f, float maxAccel = 2.5f)
{
    var t = new TkbTemplate("TestVehicle", 999L);
    t.AddDescriptor(new VehicleParametersDto
    {
        Length = length, Width = width,
        MaxSpeedFwd = maxSpeedFwd, MaxAccel = maxAccel
    });
    return t;
}

private static EntityRepository MakeWorldWithComponents()
{
    var repo = new EntityRepository();
    repo.RegisterComponent<VehicleParams>();
    repo.RegisterComponent<VehicleState>();
    repo.RegisterComponent<NavState>();
    repo.RegisterComponent<PhysicsCollider>();
    return repo;
}
```

**Tests (minimum 7):**

1. `GetConsumedDescriptors_ReturnsVehicleParametersDto`  
   Verify `new VehicleKinematicsTkbTranslator().GetConsumedDescriptors()` contains `typeof(VehicleParametersDto)`.

2. `Inject_WithAllComponentsRegistered_AddsVehicleParams`  
   Full world + template → entity has `VehicleParams`;  
   `Length == 6f`, `Width == 2.5f`, `WheelBase == 6f * 0.6f`, `MaxSpeedFwd == 20f`.

3. `Inject_WithAllComponentsRegistered_AddsVehicleState`  
   Full world + template → entity has `VehicleState` with `Speed == 0f` and `SteerAngle == 0f`.

4. `Inject_WithAllComponentsRegistered_AddsNavState`  
   Full world + template → entity has `NavState` with `Mode == KinematicsMode.None`.

5. `Inject_WithAllComponentsRegistered_AddsPhysicsCollider`  
   Full world + template → entity has `PhysicsCollider` with `Radius == Math.Max(6f, 2.5f) / 2f == 3f`.

6. `Inject_TemplateWithoutVehicleParametersDto_AddsNoComponents`  
   Template with NO `VehicleParametersDto` → entity has none of the four components.  
   Verify by checking `repo.HasComponent<VehicleParams>(entity)` is false.

7. `Inject_WorldWithoutVehicleParamsRegistered_DoesNotThrow`  
   World with NO registered components → `Inject` completes without exception.  
   Entity has no components.

---

## Task 4 — TKB-014: Replace stub comments with translator loops

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/BlueprintApplicationSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Lifecycle/EntityLifecycleModule.cs`
- `FDP/Toolkits/Fdp.Toolkits/Replication/ReplicationLogicModule.cs`

### 4a — `BlueprintApplicationSystem`

Add optional `translators` parameter (default: empty). Replace the stub comment with the
translator loop.

```csharp
// Current constructor:
public BlueprintApplicationSystem(ITkbDatabase tkb)

// New constructor:
public BlueprintApplicationSystem(
    ITkbDatabase tkb,
    System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator>? translators = null)
{
    _tkb = tkb;
    _translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();
}
```

Add field:
```csharp
private readonly System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator> _translators;
```

Replace the stub comment in `Execute`:
```csharp
// Before:
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(repo, order.Entity, template);

// After:
foreach (var t in _translators)
    t.Inject(repo, order.Entity, template);
```

Add `using Fdp.Interfaces;` if not present.

### 4b — `GhostPromotionSystem`

```csharp
// Current constructor:
public GhostPromotionSystem(ITkbDatabase tkbDatabase, EntityLifecycleModule lifecycleModule)

// New constructor:
public GhostPromotionSystem(
    ITkbDatabase tkbDatabase,
    EntityLifecycleModule lifecycleModule,
    System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator>? translators = null)
{
    _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
    _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
    _translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();
}
```

Add field:
```csharp
private readonly System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator> _translators;
```

Replace the stub comment in `PromoteGhost`:
```csharp
// Before:
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(_world!, entity, template);

// After:
foreach (var t in _translators)
    t.Inject(_world!, entity, template);
```

### 4c — `NetworkSpawningSystem`

Insert `translators` before the optional `onEntitySpawned` parameter:

```csharp
// Current constructor signature:
public NetworkSpawningSystem(
    ITkbDatabase tkbDb,
    EntityLifecycleModule elm,
    NetworkEntityMap networkMap,
    INetworkIdAllocator idAllocator,
    int localNodeId,
    Action<EntityRepository, Entity, bool>? onEntitySpawned = null)

// New constructor signature:
public NetworkSpawningSystem(
    ITkbDatabase tkbDb,
    EntityLifecycleModule elm,
    NetworkEntityMap networkMap,
    INetworkIdAllocator idAllocator,
    int localNodeId,
    System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator>? translators = null,
    Action<EntityRepository, Entity, bool>? onEntitySpawned = null)
```

Add field:
```csharp
private readonly System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator> _translators;
```

Initialize in constructor body:
```csharp
_translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();
```

Replace the stub comment in `ProcessSpawn`:
```csharp
// Before:
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(world, entity, template);

// After:
foreach (var t in _translators)
    t.Inject(world, entity, template);
```

### 4d — `EntityLifecycleModule`

Add `translators` optional parameter and thread it through to `BlueprintApplicationSystem`:

```csharp
// Add to constructor parameters (after existing params):
System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator>? translators = null

// Add field:
private readonly System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator> _translators;

// Initialize in constructor body:
_translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();

// In RegisterSystems:
// Before:
registry.RegisterSystem(new BlueprintApplicationSystem(_tkb));
// After:
registry.RegisterSystem(new BlueprintApplicationSystem(_tkb, _translators));
```

### 4e — `ReplicationLogicModule`

Add `translators` optional parameter and thread to `GhostPromotionSystem`:

```csharp
// Add to constructor parameters:
System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator>? translators = null

// Add field:
private readonly System.Collections.Generic.IReadOnlyList<ITkbEntityTranslator> _translators;

// Initialize in constructor body:
_translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();

// In RegisterSystems:
// Before:
registry.RegisterSystem(new GhostPromotionSystem(_tkbDatabase, _lifecycleModule));
// After:
registry.RegisterSystem(new GhostPromotionSystem(_tkbDatabase, _lifecycleModule, _translators));
```

---

## Task 5 — Tests for TKB-014 translator wiring

### Verify existing tests still pass (backward compat)

The existing tests that construct `GhostPromotionSystem`, `BlueprintApplicationSystem`, and
`NetworkSpawningSystem` WITHOUT a translator list must still pass (empty list = no-op).
Do NOT modify those test files — they verify backward compatibility.

### New tests

**File to create:**  
`FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TranslatorWiringTests.cs`

Tests (minimum 3):

1. `BlueprintApplicationSystem_WithTranslator_CallsInjectOnKnownTkbType`  
   Create a `BlueprintApplicationSystem` with ONE translator that records how many times
   `Inject` was called. Fire a `ConstructionOrder` event for a known TKB type. Verify
   `Inject` was called exactly once on the correct entity.

2. `NetworkSpawningSystem_WithTranslator_CallsInjectOnSpawn`  
   Create a `NetworkSpawningSystem` with ONE translator. Fire a `SpawnEntityCommand` for a
   known TKB type. Verify `Inject` was called once.

3. `GhostPromotionSystem_WithEmptyTranslators_PromotesWithoutException`  
   Create a `GhostPromotionSystem` with an empty translator list. Execute it with a ghost
   entity that has all mandatory components satisfied. Verify the entity is promoted to
   `EntityLifecycle.Constructing` without exception.

**Translator stub for tests:**

```csharp
private sealed class RecordingTranslator : ITkbEntityTranslator
{
    public int InjectCount { get; private set; }
    public Entity LastEntity { get; private set; }

    public System.Collections.Generic.IEnumerable<System.Type> GetConsumedDescriptors()
        => System.Array.Empty<System.Type>();

    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        InjectCount++;
        LastEntity = entity;
    }
}
```

---

## Build and test verification

```powershell
# From workspace root d:\Work\IOS-IG-SimHost-FDP-2

# 1. Build FDP
cd FDP ; dotnet build FDP.sln -v m 2>&1 | Select-String -Pattern "error|Build succeeded|Build FAILED" | Select-Object -Last 5

# 2. Run all Tkb tests (must have >= 99)
cd .. ; dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj `
    --filter "FullyQualifiedName~Tkb" -v n 2>&1 | `
    Select-String -Pattern "Passed|Failed|Test Run" | Select-Object -Last 10

# 3. Run CarKinem Tkb tests
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj `
    --filter "FullyQualifiedName~CarKinem" -v n 2>&1 | `
    Select-String -Pattern "Passed|Failed|Test Run" | Select-Object -Last 10

# 4. Run all Toolkits tests (regression)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj -v n 2>&1 | `
    Select-String -Pattern "Passed|Failed|Test Run" | Select-Object -Last 5
```

Zero build errors. All existing tests pass. New tests: >= 10 (7 translator tests + 3 wiring tests).

---

## Report

Write report to `.dev/tkb-1/reports/BATCH-05-REPORT.md`:
- Files created/modified
- Test counts (new tests + total)
- Build and test output
- Deviations and justification
- P2/P3 issues

---

## Notes

- `ITkbEntityTranslator.Inject` receives `EntityRepository repo` (not `ISimulationView`) — the
  systems already have a reference to the `EntityRepository` at the point of the translator call.
- All translator parameters are `IReadOnlyList<ITkbEntityTranslator>?` with `null` defaulting to
  `Array.Empty<ITkbEntityTranslator>()`. This ensures 100% backward compatibility with existing
  test code that does not supply translators.
- Do NOT modify the existing test files for `GhostProtocolTests.cs`, `SubEntityTests.cs`,
  `BlueprintApplicationSystemTests.cs`, or `SpawnSystemTests.cs` — they test the existing
  no-translator behavior and should pass unchanged.
- `VehicleState { Speed = 0, SteerAngle = 0 }` — both fields have default value 0 but set
  them explicitly for clarity (matches the TASK-DETAIL spec).
- `NavState { Mode = KinematicsMode.None }` — sets only Mode; other fields remain at default.
- `PhysicsCollider.CollisionLayer = 1` — standard entity collision layer (matches
  `PhysicsConstants.EntityCollisionLayer = 1` used elsewhere in the codebase).
- In `VehicleKinematicsTkbTranslatorTests`, create a fresh `EntityRepository` and fresh entity
  per test (no shared ECS world between tests).
- The AGENTS.md invariants apply: no unicode in code/comments for things expressible in ASCII,
  preserve existing comments, minimize diff.
