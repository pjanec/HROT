# BATCH-02 Instructions — Phase 3: In-Memory Registry Refactoring

**Workstream:** tkb-1  
**Batch:** 02  
**Tasks covered:** TKB-006, TKB-007, TKB-008  
**Design reference:** `.dev/tkb-1/DESIGN.md`  
**Task detail reference:** `.dev/tkb-1/TASK-DETAIL.md` (see TKB-006, TKB-007, TKB-008 sections)

---

## Overview

This batch removes the `ApplyTo` / `_applicators` mechanism from `TkbTemplate` and replaces it
with a descriptor bag API. It also extends `ITkbDatabase` with `Clear()`,
`GetEntitiesByCategory()`, and `ActiveTkbName`. **The build must compile and all existing tests
must pass (except those explicitly deleted) when the batch is complete.**

Because `ApplyTo` is a load-bearing API with many callers, this batch also updates every caller.
The production ECS systems (GhostPromotionSystem, BlueprintApplicationSystem,
NetworkSpawningSystem) get empty stubs (Phase 6 wires the real translator loop). The Hrot
catalog builder (NedTkbBuilder) is migrated to use `AddDescriptor`. Test files that tested the
old `ApplyTo` behavior are deleted and replaced with descriptor-bag tests.

Read `TASK-DETAIL.md` sections TKB-006, TKB-007, TKB-008 for authoritative specifications
and success conditions.

---

## Task 1 — TKB-006: Refactor `TkbTemplate` to pure descriptor bag

**File:** `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs`

### Changes to make

**ADD** a `CategoryPath` property and update the constructor:

```csharp
/// <summary>
/// File-system category derived from the VFS path when loading from TKB files.
/// Empty string for programmatically-registered templates.
/// </summary>
public string CategoryPath { get; }

// Update constructor signature:
public TkbTemplate(string name, long tkbType, string categoryPath = "")
{
    // ... existing validation ...
    CategoryPath = categoryPath ?? "";
}
```

**ADD** the descriptor bag field and methods:

```csharp
private readonly Dictionary<(Type, int), object> _descriptors = new();

/// <summary>
/// Stores a descriptor DTO in the bag. Uses (Type, partId) as the key.
/// Overwrites any previously stored descriptor with the same key.
/// </summary>
public void AddDescriptor<T>(T descriptor, int partId = 0) where T : notnull
{
    _descriptors[(typeof(T), partId)] = descriptor;
}

/// <summary>
/// Retrieves a descriptor of type T (for reference types).
/// Returns null if not found.
/// </summary>
public T? GetDescriptor<T>(int partId = 0) where T : class
{
    _descriptors.TryGetValue((typeof(T), partId), out var obj);
    return obj as T;
}

/// <summary>
/// Tries to retrieve a descriptor of type T (for value types).
/// Returns false if not found.
/// </summary>
public bool TryGetDescriptor<T>(out T descriptor, int partId = 0) where T : struct
{
    if (_descriptors.TryGetValue((typeof(T), partId), out var obj) && obj is T typed)
    {
        descriptor = typed;
        return true;
    }
    descriptor = default;
    return false;
}

/// <summary>
/// Returns true if a descriptor of type T (with the given partId) is present.
/// </summary>
public bool HasDescriptor<T>(int partId = 0)
{
    return _descriptors.ContainsKey((typeof(T), partId));
}

/// <summary>
/// Enumerates all stored descriptors as (Type, PartId, Data) tuples.
/// </summary>
public IEnumerable<(Type Type, int PartId, object Data)> GetAllDescriptors()
{
    foreach (var kv in _descriptors)
        yield return (kv.Key.Item1, kv.Key.Item2, kv.Value);
}
```

**REMOVE** the following (completely delete, do not deprecate):
- `private readonly List<Action<EntityRepository, Entity, bool>> _applicators = new();`
- `public void AddComponent<T>(T component) where T : unmanaged` (the entire method)
- `public void AddManagedComponent<T>(Func<T> factory) where T : class` (the entire method)
- `public void ApplyTo(EntityRepository repo, Entity entity, bool preserveExisting = false)` (the entire method)

**RETAIN** (do not touch):
- `public long TkbType { get; }`
- `public string Name { get; }`
- `public DISEntityType DisType { get; set; }`
- `public List<MandatoryComponent> MandatoryComponents { get; }`
- `public List<ChildBlueprintDefinition> ChildBlueprints { get; }`
- `public void AddMandatoryComponent<T>(bool isHard = true, uint softTimeoutFrames = 0)` - KEEP

---

## Task 2 — Fix all callers of deleted methods (keep the build compiling)

After removing `AddComponent`, `AddManagedComponent`, and `ApplyTo` from `TkbTemplate`, the
following files will fail to compile. Fix each one.

### 2a. Production ECS systems — remove ApplyTo call, add stub comment

**File:** `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs`

Find the line:
```csharp
template.ApplyTo(_world!, entity, preserveExisting: true);
```
Replace it with:
```csharp
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(_world!, entity, template);
```

**File:** `FDP/Toolkits/Fdp.Toolkits/Lifecycle/Systems/BlueprintApplicationSystem.cs`

Find the line:
```csharp
template.ApplyTo(repo, order.Entity, preserveExisting: true);
```
Replace it with:
```csharp
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(repo, order.Entity, template);
```

**File:** `FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Systems/NetworkSpawningSystem.cs`

Find the line:
```csharp
template.ApplyTo(world, entity);
```
Replace it with:
```csharp
// TKB-014 (Phase 6): translator loop will replace ApplyTo here.
// foreach (var t in _translators) t.Inject(world, entity, template);
```

### 2b. NedTkbBuilder — migrate from AddComponent to AddDescriptor

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbBuilder.cs`

The `NedTkbBuilder` class inside this file must be updated. Add the following using directives
at the top of the file:
```csharp
using Fdp.Toolkit.Tkb.Domain;
```

#### Method: `DefineVehicle(long tkbId, string name)`

Remove all `AddComponent` and `AddManagedComponent` calls in this method. Keep the two
`AddMandatoryComponent` calls and the `_db.Register(template)` call.

Add `AddDescriptor` for the master descriptor:
```csharp
template.AddDescriptor(new TkbMasterDto { CustomName = name });
```

After migration the method body should be:
```csharp
var template = new TkbTemplate(name, tkbId);
template.AddDescriptor(new TkbMasterDto { CustomName = name });
template.AddMandatoryComponent<EntityInfo>(isHard: true);
// SimTransform will be stamped by translator in Phase 6.
template.AddMandatoryComponent<SimTransform>(isHard: true);
_db.Register(template);
return this;
```

#### Method: `WithPhysics(long tkbId, Action<SimVehicleDef> configure)`

Replace the `AddComponent(BuildVehicleParams(physicsDef))` and `AddComponent(PhysicsCollider)`
calls with a single `AddDescriptor`:

```csharp
var physicsDef = new SimVehicleDef();
configure(physicsDef);

template.AddDescriptor(new VehicleParametersDto
{
    Mass        = physicsDef.Mass,
    Length      = physicsDef.Length,
    Width       = physicsDef.Width,
    MaxSpeedFwd = physicsDef.MaxSpeed,
    MaxSpeedRev = physicsDef.MaxSpeedRev,
    MaxAccel    = physicsDef.Acceleration,
});
// Height, TurnRate, Mobility mapped to VehicleParams by translator in Phase 6.
```

Note: `BuildVehicleParams` is still needed as a private helper in Phase 6, but for now it
can remain in the file (unused). However if the compiler warns about it, delete it to keep
the code clean.

#### Method: `WithCombat(long tkbId, Action<SimCombatDef> configure)`

Replace the managed component factory and all the `AddComponent` calls with descriptor storage.
The `SimCombatDef` is stored directly in the descriptor bag (it is a reference type and the
method can store the concrete instance):

```csharp
var combatDef = new SimCombatDef();
configure(combatDef);

// Store the combat definition as a descriptor for inspector / ORBAT display.
template.AddDescriptor(combatDef);

// Derived capability DTO for the general TKB pipeline.
if (combatDef.Weapons.Count > 0)
{
    var primary = combatDef.Weapons[0];
    template.AddDescriptor(new WeaponCapabilitiesDto
    {
        EffectiveRange   = primary.Range,
        RateOfFire       = primary.RateOfFire,
        MagazineCapacity = primary.Ammunition,
    });
}
// ECS components (PerceptionReceptor, WeaponState, Health, PhysicsCollider)
// will be stamped by translators in Phase 6.
```

#### Method: `WithVisual(long tkbId, Action<IgVisualDef> configure)`

Remove the `AddComponent(new VisualData { ... })` call. Leave the method body returning `this`
with a comment:
```csharp
// VisualData ECS component will be applied by IG-side translator in Phase 6.
return this;
```

#### Method: `WithFaction(long tkbId, byte factionId)`

Remove the `AddComponent(new EntityInfo { ForceId = forceId })` call. Leave the method body
returning `this` with a comment:
```csharp
// EntityInfo.ForceId will be stamped by translator in Phase 6.
return this;
```

#### Method: `WithBehavior(long tkbId)`

Remove ALL `AddComponent` calls. Leave the method body returning `this` with a comment:
```csharp
// Behavior and navigation ECS components will be applied by translators in Phase 6.
return this;
```

#### Method: `WithHeavyMemory(long tkbId)`

Remove `AddComponent(new Blackboard1024())`. Leave the method body returning `this` with a
comment:
```csharp
// Blackboard1024 ECS component will be applied by translator in Phase 6.
return this;
```

#### Method: `AsComposite(long tkbId, Action<TkbCompositionDef> configure)`

Replace the `AddManagedComponent(factory)` call with `AddDescriptor`:

```csharp
// ... existing ChildBlueprints population code stays ...

// Store as descriptor instead of managed component.
var compositionDef = new TkbCompositionDef();
configure(compositionDef);
template.AddDescriptor(compositionDef);
return this;
```

(The variable `freshDef` was previously a factory-constructed instance — now we create one
concrete instance and store it. This is semantically equivalent since templates are registered
once at startup.)

### 2c. DemoTkbSetup — migrate FDP example catalog

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs`

This file registers 5 entity templates using `AddComponent` / `AddManagedComponent`. All such
calls must be removed. Add `AddDescriptor` calls where a suitable DTO exists.

Add using at top:
```csharp
using Fdp.Toolkit.Tkb.Domain;
```

For each `RegisterXxx` private method:
- **Remove** every `t.AddComponent(...)` and `t.AddManagedComponent(...)` call.
- If the entity has a meaningful name, **add** `t.AddDescriptor(new TkbMasterDto { CustomName = "<name>" })`.
- If the entity has vehicle physics (VehicleParams / VehiclePresets used), **add** a
  `VehicleParametersDto` based on the same numeric values.
- Keep `tkb.Register(t)` at the end of each method.
- Keep `AddMandatoryComponent` calls if present.

ECS components (SimTransform, SimVelocity, BehaviorState, WeaponState, etc.) will be applied by
translators in Phase 6. Removing these `AddComponent` calls is intentional and correct.

---

## Task 3 — TKB-007: Extend `ITkbDatabase`

**File:** `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs`

Add the following three members to the interface (see exact documentation in TASK-DETAIL.md):

```csharp
void Clear();

IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath);

string? ActiveTkbName { get; set; }
```

### 3a. Fix all concrete implementations

There are 6 concrete implementations of `ITkbDatabase`. After adding the new interface members,
each must be updated:

1. `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs` — full implementation (Task 4 below).

2. `FDP/Toolkits/Fdp.Toolkits.Tests/Replication/GhostProtocolTests.cs`  
   Classes: `MockTkbDatabase`, `SlowMockTkbDatabase`  
   Add stubs:
   ```csharp
   public string? ActiveTkbName { get; set; }
   public void Clear() { }
   public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
       => throw new NotImplementedException();
   ```

3. `FDP/Toolkits/Fdp.Toolkits.Tests/Replication/SubEntityTests.cs`  
   Class: `TestTkbDatabase`  
   Add same stubs as above.

4. `FDP/Toolkits/Fdp.Toolkits.Tests/Replication/NetworkGatewaySystemTests.cs`  
   Class: `GatewayTestTkbDb`  
   Add same stubs as above.

5. `FDP/Toolkits/Fdp.Toolkits.Tests/Lifecycle/EntityLifecycleIntegrationTests.cs`  
   Class: `MockTkbDatabase` (private inner class)  
   Add same stubs as above.

---

## Task 4 — TKB-008: Update `TkbDatabase`

**File:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs`

Add the three new members (see authoritative spec in TASK-DETAIL.md):

```csharp
public string? ActiveTkbName { get; set; }

public void Clear()
{
    _byName.Clear();
    _byType.Clear();
}

public IEnumerable<TkbTemplate> GetEntitiesByCategory(string categoryPath)
{
    if (string.IsNullOrEmpty(categoryPath))
        return _byType.Values;
    return _byType.Values.Where(t =>
        t.CategoryPath.Equals(categoryPath, StringComparison.OrdinalIgnoreCase) ||
        t.CategoryPath.StartsWith(categoryPath + "/", StringComparison.OrdinalIgnoreCase));
}
```

Add `using System.Linq;` if not already present.

---

## Task 5 — Update / delete test files

### 5a. Delete ApplyTo-based tests in `BlueprintApplicationSystemTests.cs`

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Lifecycle/Systems/BlueprintApplicationSystemTests.cs`

The two existing tests (`Execute_PreservesExistingComponents`, `Execute_AppliesMissingComponents`)
use `template.AddComponent` (deleted) and test `ApplyTo` behavior (deleted). Delete both tests
and their helper struct `TestComponentA`.

Replace with these two new tests:

```csharp
[Fact]
public void Execute_UnknownTkbType_DoesNotThrow()
{
    var mockTkb = new Mock<ITkbDatabase>();
    TkbTemplate? outTemplate = null;
    mockTkb.Setup(x => x.TryGetByType(It.IsAny<long>(), out outTemplate)).Returns(false);

    var repo = new EntityRepository();
    var system = new BlueprintApplicationSystem(mockTkb.Object);

    repo.Bus.Publish(new ConstructionOrder { Entity = default, BlueprintId = 99 });
    repo.Bus.SwapBuffers();

    // Must not throw even when template is not found.
    system.Execute(repo, 0.1f);
}

[Fact]
public void Execute_KnownTkbType_ConsumesOrderWithoutThrowing()
{
    var template = new TkbTemplate("TestTemplate", 1);
    template.AddDescriptor(new TkbMasterDto { CustomName = "TestTemplate" });

    var mockTkb = new Mock<ITkbDatabase>();
    TkbTemplate outTemplate = template;
    mockTkb.Setup(x => x.TryGetByType(1, out outTemplate)).Returns(true);

    var repo = new EntityRepository();
    repo.RegisterEvent<ConstructionOrder>();
    var system = new BlueprintApplicationSystem(mockTkb.Object);
    var entity = repo.CreateEntity();

    repo.Bus.Publish(new ConstructionOrder { Entity = entity, BlueprintId = 1 });
    repo.Bus.SwapBuffers();

    // No exception — template found, no translators yet (Phase 6).
    system.Execute(repo, 0.1f);
}
```

Note: You will need `using Fdp.Toolkit.Tkb.Domain;` at the top of the file.

### 5b. Delete ApplyTo-based tests in `BlueprintTests.cs` (FDP examples)

**File:** `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/BlueprintTests.cs`

Delete these four test methods entirely (they tested `ApplyTo` behavior which no longer exists):
- `APC_Template_HasPassengerBuffer`
- `APC_Template_HasHsmBrainTier`
- `Soldier_Template_HasWeaponState`
- `Insurgent_Template_HasWeaponState_WithExpectedAmmo`

Keep all other test methods (TkbSetup_RegistersAllFiveTemplates, T4 TrafficBrainSystem tests,
T5 InsurgentNodes tests, T6 APC ConvoyEscort_HSM tests).

### 5c. Rewrite `NedTkbBuilderCombatTests.cs`

**File:** `Hrot/Engine/Hrot.Core.Tests/NedTkbBuilderCombatTests.cs`

Delete all 4 existing test methods (they called `template.ApplyTo` to verify ECS components).

Replace with 4 new descriptor-bag tests. The M1 Abrams combat configuration is:
- `c.Weapons[0].Range = 3000`, `c.Weapons[0].RateOfFire = 6`, `c.Weapons[0].Ammunition = 42`

```csharp
[Fact]
public void WithCombat_StoresWeaponCapabilitiesDescriptor()
{
    var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
    Assert.True(template.HasDescriptor<WeaponCapabilitiesDto>());
}

[Fact]
public void WithCombat_WeaponCapabilities_HasExpectedEffectiveRange()
{
    var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
    var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
    Assert.Equal(3000f, dto.EffectiveRange);
}

[Fact]
public void WithCombat_WeaponCapabilities_HasExpectedRateOfFire()
{
    var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
    var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
    Assert.Equal(6f, dto.RateOfFire);
}

[Fact]
public void WithCombat_WeaponCapabilities_HasExpectedMagazineCapacity()
{
    var template = BuildDatabase().GetByType(TkbEntityTypes.Tank_M1Abrams)!;
    var dto = template.GetDescriptor<WeaponCapabilitiesDto>()!;
    Assert.Equal(42, dto.MagazineCapacity);
}
```

Update the `BuildDatabase()` helper to return a `TkbDatabase` (it probably already calls
`NedTkbCatalog.RegisterAll` so keep that). Add the required `using` for
`Fdp.Toolkit.Tkb.Domain`.

Remove all World/entity creation helpers (`CreateWorld()`) since they are no longer needed.

### 5d. Rewrite `BdcTkbBuilderPhysicsTests.cs`

**File:** `Hrot/Engine/Hrot.Core.Tests/BdcTkbBuilderPhysicsTests.cs`

Delete all 4 existing test methods (they called `template.ApplyTo` to verify ECS components).

Replace with 4 new descriptor-bag tests. The test builder configures `def.Length = 6f`,
`def.Width = 2.5f`. MaxSpeedFwd defaults to 0 unless set.

```csharp
[Fact]
public void WithPhysics_StoresVehicleParametersDescriptor()
{
    var template = BuildDatabase().GetByType(TestTkbId)!;
    Assert.True(template.HasDescriptor<VehicleParametersDto>());
}

[Fact]
public void WithPhysics_VehicleParameters_HasExpectedLength()
{
    var template = BuildDatabase().GetByType(TestTkbId)!;
    var dto = template.GetDescriptor<VehicleParametersDto>()!;
    Assert.Equal(6f, dto.Length);
}

[Fact]
public void WithPhysics_VehicleParameters_HasExpectedWidth()
{
    var template = BuildDatabase().GetByType(TestTkbId)!;
    var dto = template.GetDescriptor<VehicleParametersDto>()!;
    Assert.Equal(2.5f, dto.Width);
}

[Fact]
public void WithPhysics_VehicleParameters_HasExpectedMaxSpeedFwd()
{
    var db = BuildDatabase(length: 6f, width: 2.5f, maxSpeed: 20f);
    var template = db.GetByType(TestTkbId)!;
    var dto = template.GetDescriptor<VehicleParametersDto>()!;
    Assert.Equal(20f, dto.MaxSpeedFwd);
}
```

The last test requires adding a `maxSpeed` parameter to the private `BuildDatabase(float, float)` helper (add a 3rd optional parameter). Update the `BuildDatabase(float length, float width)` overload to pass `0f` for maxSpeed.

Remove `CreateWorld()` and related ECS helpers.

Add `using Fdp.Toolkit.Tkb.Domain;`.

### 5e. Delete SC-HA014 tests from `TkbRegistrationTests.cs`

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/TkbRegistrationTests.cs`

Delete the entire `SC-HA014` section (the 3 test methods):
- `SC_HA014_1_Unit_TankPlatoon_Blueprint_HasBlackboard1024`
- `SC_HA014_2_Unit_TankPlatoon_Auto_Blueprint_HasBlackboard1024`
- `SC_HA014_3_Tank_M1Abrams_Blueprint_HasTargetMemoryAndBehaviorState`

Also remove the `using` directives that become unused after this deletion
(e.g., `SimHostComponentRegistry`, `Blackboard1024`, `TargetMemory`, `BehaviorState`,
`EntityRepository`, `Entity`).

Keep the `P1-001` registration tests (TryGetByType tests) — they are still valid.

### 5f. Delete ApplyTo tests from `TacGraphicRouteBlueprintTests.cs`

**File:** `Hrot/Subsystems/Hrot.SimHost.Tests/TacGraphicRouteBlueprintTests.cs`

Delete all test methods that call `template.ApplyTo(...)`. These tested the old
delegate-applicator mechanism. Keep only the two registration tests:
- `TacGraphicRoute_IsRegisteredInTkbCatalog`
- `TkbType_TacGraphicRoute_Is8802`

Remove any `using` directives that become unused after deletions (e.g., `EntityRepository`,
`Entity`, and component-specific usings for RoutePlan, SimTransform, NetworkIdentity,
NetworkTransform, TkbIdentity if they are only used in deleted tests).

---

## Task 6 — Write new TKB-006/007/008 unit tests

Add a new test file for the descriptor bag and TkbDatabase extensions.

**File to create:**
`FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/DescriptorBagTests.cs`

```
namespace Fdp.Toolkit.Tkb.Tests
```

Tests to write (minimum — add more if gaps exist):

**Descriptor bag (TKB-006):**
1. `AddDescriptor_ThenGetDescriptor_ReturnsMatchingInstance` — add `VehicleParametersDto`,
   retrieve it, compare `Mass`, `Length`, `Width`, `MaxSpeedFwd`.
2. `AddDescriptor_Overwrite_ReturnsLatestValue` — add same type twice, verify second value returned.
3. `HasDescriptor_ReturnsFalse_WhenNotAdded` — check a type that was never added.
4. `HasDescriptor_ReturnsTrue_AfterAdd` — add, then check.
5. `TryGetDescriptor_ValueType_ReturnsFalse_WhenMissing` — test value-type overload.
6. `GetAllDescriptors_ReturnsAll` — add 2 descriptors, verify both in enumeration.
7. `CategoryPath_DefaultsToEmpty` — `new TkbTemplate("T", 1).CategoryPath == ""`
8. `CategoryPath_SetViaConstructor` — `new TkbTemplate("T", 1, "Platform/Vehicle").CategoryPath`

**TkbDatabase.Clear (TKB-008):**
9. `Clear_RemovesAllTemplates` — register, clear, verify `GetAll()` is empty.
10. `Clear_ThenReRegister_FindsNewTemplate` — clear then register fresh, verify found by type.

**TkbDatabase.GetEntitiesByCategory (TKB-008):**
11. `GetEntitiesByCategory_EmptyPrefix_ReturnsAll` — register 2 templates with different paths,
    empty prefix returns both.
12. `GetEntitiesByCategory_ExactMatch_ReturnsMatch` — path `"A/B"` finds template with
    `CategoryPath == "A/B"`.
13. `GetEntitiesByCategory_ChildPath_ReturnsChild` — path `"A/B"` also finds template with
    `CategoryPath == "A/B/C"`.
14. `GetEntitiesByCategory_DoesNotMatchPartialSuffix` — path `"A/B"` does NOT match `"A/BC"`.

**ActiveTkbName (TKB-008):**
15. `ActiveTkbName_DefaultsToNull` — fresh `TkbDatabase().ActiveTkbName` is null.
16. `ActiveTkbName_CanBeSetAndRead` — set and read back.

---

## Build and test verification

After all changes, run these commands (from the workspace root `d:\Work\IOS-IG-SimHost-FDP-2`):

```powershell
# 1. Build FDP submodule
cd FDP
dotnet build FDP.sln
cd ..

# 2. Run Tkb-specific tests (existing BATCH-01 + new BATCH-02)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb"

# 3. Run all FDP tests (must not regress)
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj

# 4. Build and test the Hrot projects
dotnet build Hrot\Engine\Hrot.Core\Hrot.Core.csproj
dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj

# 5. Build and test FDP examples
dotnet build FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj
dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj
```

Zero build errors and all remaining tests pass.

---

## Report

When done, write a report to `.dev/tkb-1/reports/BATCH-02-REPORT.md` covering:
- Files modified / created / deleted
- Test counts: how many tests deleted, how many added, net change
- Any deviations from these instructions (with justification)
- Build and test results (copy the terminal output summary)
- Any P2/P3 issues discovered

---

## Notes

- Do NOT add back `AddComponent`, `AddManagedComponent`, or `ApplyTo` in any form — not even
  with `[Obsolete]`. They must be completely deleted.
- Do NOT skip the `DemoTkbSetup.cs` migration — the FDP example project references it and
  the build will fail if `AddComponent` is called after it is deleted from `TkbTemplate`.
- The `BuildVehicleParams` private static method in `BdcTkbBuilder.cs` becomes unused. Either
  delete it or leave it (the Phase 6 VehicleKinematicsTkbTranslator may reuse its logic). If
  the C# compiler produces an "unused private member" warning, delete the method.
- The `SimCombatDef` class is not annotated with `[TkbDescriptor]`. Storing it via
  `AddDescriptor(combatDef)` is correct; the descriptor bag stores any object, not just
  `[TkbDescriptor]`-annotated DTOs.
- When updating using directives in test files after deleting tests, only remove usings that
  are TRULY unused. Do not blindly remove all component-type usings if they are still
  referenced elsewhere in the file.
