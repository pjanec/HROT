# BATCH-05 Report

## Completion Status
- [x] MPM-P5-T01: Create DoctrineCategory + DoctrineContractAttribute
- [x] MPM-P5-T02: Decorate existing DTOs + create 5 marker DTOs
- [x] MPM-P5-T03: Create DoctrineSchemaDiscovery

## Build Status
`dotnet build IOS-IG-SimHost.sln` - 0 errors, 0 warnings introduced by this batch.

## Test Status
`dotnet test IOS-IG-SimHost.sln --no-build`

- Fdp.Core.Tests: Passed 718, Skipped 2, Failed 0
- Hrot.ClusterRunner.Integration.Tests: Passed 130, Skipped 4, Failed 10

Failure count matches the pre-existing BATCH-04 baseline (10 timing-sensitive integration
failures unrelated to this batch).

---

## Developer Insights

**Q1: Where did you find the existing DTOs (exact file paths)?**

The 3 DTOs with JSON properties were found in Fdp.Toolkits, not in Hrot.Core:
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/FireAtTargetParamsJsonDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/MoveToLocationParamsJsonDto.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/FollowRouteParamsJsonDto.cs`

`JoinFormationParamsJsonDto` did not exist anywhere in the codebase.

These could not be decorated with `[DoctrineContract]` in-place because:
1. `DoctrineContractAttribute` (Hrot.Core) cannot be applied in `Fdp.Toolkits` without
   creating a circular dependency (Fdp.Toolkits has no Hrot dependencies).
2. `CgfDoctrineIds` (Hrot.CGF) cannot be referenced from Fdp.Toolkits for the same reason.

Resolution: new Hrot.Core versions of all 4 DTOs were created in
`Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/`, carrying the full JSON property
definitions plus `[DoctrineContract]` + `BehaviorId`. The Fdp.Toolkits originals remain
untouched (they are still used by BehaviorUiSetup and ScenarioBehaviorRemapper until
BATCH-06 switches to AutoRegister).

**Q2: What are the exact behavior ID strings found in BehaviorUiSetup.cs/CgfDoctrineSetup.cs?**

From `BehaviorUiSetup.cs`: `"FireAtTarget"`, `"FollowRoute"`, `"MoveToLocation"`
From `CgfDoctrineSetup.cs`: same three, plus `"JoinFormation"`, `"Idle"`, `"WanderMilitary"`
From `DoctrineCatalog.cs` (string literals): `"ConvoyEscort"`, `"InfantryCombat"`, `"Ambush"`

The three strings for ConvoyEscort, InfantryCombat, Ambush do not yet appear in either
setup file - they are referenced only in DoctrineCatalog. BATCH-06 will need to add their
DoctrineDefinition registrations to CgfDoctrineSetup.

**Q3: Which project hosts DoctrineSchemaDiscovery, and why?**

`Hrot.Presentation` (`Hrot/Engine/Hrot.Presentation/Behavior/DoctrineSchemaDiscovery.cs`).

Dependency check:
- `Hrot.Presentation` already references `Hrot.Core` (DoctrineContractAttribute) v
- `Hrot.Presentation` already references `Fdp.Toolkits` (ScenarioBehaviorRemapper) v
- `BehaviorUiRegistry` is defined in `Hrot.Presentation` itself v
- No new project references needed v

`Hrot.CGF` was also a candidate but was rejected: it references `Hrot.Presentation`
transitively, so placing the class there would work too, but `Hrot.Presentation` is the
lower layer (fewer dependencies) and co-locates the class with `BehaviorUiSetup.cs`.

**Q4: What is the exact signature of BehaviorUiRegistry.Register<T> and
ScenarioBehaviorRemapper.Register<T>?**

```csharp
// Hrot.Presentation.Behavior.BehaviorUiRegistry (BehaviorUiCompiler.cs)
public void Register<TDto>(string behaviorId) where TDto : class, new()

// Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper
public void Register<TDto>(string behaviorId) where TDto : class, new()
```

Both are instance methods with a single string parameter. `GetMethod("Register")` returns
a unique match on each type; no overload disambiguation was needed.

The `Invoke` call passes `new object[] { attr.BehaviorId }` as the argument array.

**Q5: Other places where behavior-ID strings are hardcoded that BATCH-06 should know:**

1. `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/DoctrineCatalog.cs` - has hardcoded lists of
   behavior-ID strings per entity category. BATCH-06 / MPM-P5-T05 should replace these
   with the reflection-based BuildMap approach described in DESIGN.md 5.6.

2. `Hrot/Subsystems/Hrot.CGF/Configuration/CgfDoctrineSetup.cs` - each
   `registry.Register(id, "BehaviorId", ...)` call still uses a raw string. BATCH-06
   should derive the string from the DTO's `[DoctrineContract].BehaviorId` or call
   `DoctrineSchemaDiscovery.AutoRegister`.

3. `CgfDoctrineIds.cs` does not yet have constants for ConvoyEscort, InfantryCombat, or
   Ambush (they are assigned IDs 3013-3015 in the new local `DoctrineIds.cs`). BATCH-06
   should add those constants to `CgfDoctrineIds.cs` to keep it as the single source for
   Hrot.CGF consumers, and optionally remove the duplication with `DoctrineIds.cs`.

4. The assembly scan in `DoctrineSchemaDiscovery` covers only `Hrot.Core` (the assembly
   that contains `DoctrineContractAttribute`). The 3 original Fdp.Toolkits DTOs remain
   undecorated; after BATCH-06 switches callers to AutoRegister those Fdp.Toolkits classes
   can be removed or left as legacy types.

---

## New Files Created

**Hrot.Core (`Hrot/Engine/Hrot.Core/MapDefinitions/Doctrine/`):**
- `DoctrineCategory.cs` - [Flags] enum; AllMilitary = 14
- `DoctrineContractAttribute.cs` - sealed attribute with DoctrineId, BehaviorId, ValidCategories
- `DoctrineIds.cs` - internal constants mirroring CgfDoctrineIds (3001-3015)
- `FireAtTargetParamsJsonDto.cs` - full JSON props + [DoctrineContract] + BehaviorId
- `MoveToLocationParamsJsonDto.cs` - full JSON props + [DoctrineContract] + BehaviorId
- `FollowRouteParamsJsonDto.cs` - full JSON props + [DoctrineContract] + BehaviorId
- `JoinFormationParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId
- `IdleParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId
- `WanderMilitaryParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId
- `ConvoyEscortParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId
- `InfantryCombatParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId
- `AmbushParamsJsonDto.cs` - marker + [DoctrineContract] + BehaviorId

**Hrot.Presentation (`Hrot/Engine/Hrot.Presentation/Behavior/`):**
- `DoctrineSchemaDiscovery.cs` - AutoRegister scans Hrot.Core assembly

---

## Suggested Commit Message

```
MPM Phase 5a: Doctrine contract foundation (BATCH-05)

- Hrot.Core: DoctrineCategory [Flags] enum (AllMilitary=14)
- Hrot.Core: DoctrineContractAttribute with DoctrineId, BehaviorId, ValidCategories
- Hrot.Core: DoctrineIds internal constants (3001-3015), mirrors CgfDoctrineIds
- Hrot.Core: 4 contract-bearing DTOs in MapDefinitions/Doctrine/ (FireAtTarget,
  MoveToLocation, FollowRoute, JoinFormation) with full JSON properties
- Hrot.Core: 5 empty marker DTOs (Idle, WanderMilitary, ConvoyEscort,
  InfantryCombat, Ambush) with [DoctrineContract] + BehaviorId
- Hrot.Presentation: DoctrineSchemaDiscovery.AutoRegister scans Hrot.Core assembly
  and registers all 9 DTOs with BehaviorUiRegistry and ScenarioBehaviorRemapper
- Build: 0 errors, no new project references, test baseline unchanged (10 pre-existing
  integration failures)
```
