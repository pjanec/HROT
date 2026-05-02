# BATCH-05 Report

## Completion Status
- [x] MPM-P5-T01: Create BehaviorCategory + BehaviorContractAttribute
- [x] MPM-P5-T02: Decorate existing DTOs + create 5 marker DTOs
- [x] MPM-P5-T03: Create BehaviorSchemaDiscovery

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

These could not be decorated with `[BehaviorContract]` in-place because:
1. `BehaviorContractAttribute` (Hrot.Core) cannot be applied in `Fdp.Toolkits` without
   creating a circular dependency (Fdp.Toolkits has no Hrot dependencies).
2. `CgfBehaviorIds` (Hrot.CGF) cannot be referenced from Fdp.Toolkits for the same reason.

Resolution: new Hrot.Core versions of all 4 DTOs were created in
`Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/`, carrying the full JSON property
definitions plus `[BehaviorContract]` + `BehaviorId`. The Fdp.Toolkits originals remain
untouched (they are still used by BehaviorUiSetup and ScenarioBehaviorRemapper until
BATCH-06 switches to AutoRegister).

**Q2: What are the exact behavior ID strings found in BehaviorUiSetup.cs/CgfBehaviorSetup.cs?**

From `BehaviorUiSetup.cs`: `"FireAtTarget"`, `"FollowRoute"`, `"MoveToLocation"`
From `CgfBehaviorSetup.cs`: same three, plus `"JoinFormation"`, `"Idle"`, `"WanderMilitary"`
From `BehaviorCatalog.cs` (string literals): `"ConvoyEscort"`, `"InfantryCombat"`, `"Ambush"`

The three strings for ConvoyEscort, InfantryCombat, Ambush do not yet appear in either
setup file - they are referenced only in BehaviorCatalog. BATCH-06 will need to add their
BehaviorDefinition registrations to CgfBehaviorSetup.

**Q3: Which project hosts BehaviorSchemaDiscovery, and why?**

`Hrot.Presentation` (`Hrot/Engine/Hrot.Presentation/Behavior/BehaviorSchemaDiscovery.cs`).

Dependency check:
- `Hrot.Presentation` already references `Hrot.Core` (BehaviorContractAttribute) v
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

1. `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BehaviorCatalog.cs` - has hardcoded lists of
   behavior-ID strings per entity category. BATCH-06 / MPM-P5-T05 should replace these
   with the reflection-based BuildMap approach described in DESIGN.md 5.6.

2. `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` - each
   `registry.Register(id, "BehaviorId", ...)` call still uses a raw string. BATCH-06
   should derive the string from the DTO's `[BehaviorContract].BehaviorId` or call
   `BehaviorSchemaDiscovery.AutoRegister`.

3. `CgfBehaviorIds.cs` does not yet have constants for ConvoyEscort, InfantryCombat, or
   Ambush (they are assigned IDs 3013-3015 in the new local `BehaviorIds.cs`). BATCH-06
   should add those constants to `CgfBehaviorIds.cs` to keep it as the single source for
   Hrot.CGF consumers, and optionally remove the duplication with `BehaviorIds.cs`.

4. The assembly scan in `BehaviorSchemaDiscovery` covers only `Hrot.Core` (the assembly
   that contains `BehaviorContractAttribute`). The 3 original Fdp.Toolkits DTOs remain
   undecorated; after BATCH-06 switches callers to AutoRegister those Fdp.Toolkits classes
   can be removed or left as legacy types.

---

## New Files Created

**Hrot.Core (`Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/`):**
- `BehaviorCategory.cs` - [Flags] enum; AllMilitary = 14
- `BehaviorContractAttribute.cs` - sealed attribute with BehaviorId, BehaviorId, ValidCategories
- `BehaviorIds.cs` - internal constants mirroring CgfBehaviorIds (3001-3015)
- `FireAtTargetParamsJsonDto.cs` - full JSON props + [BehaviorContract] + BehaviorId
- `MoveToLocationParamsJsonDto.cs` - full JSON props + [BehaviorContract] + BehaviorId
- `FollowRouteParamsJsonDto.cs` - full JSON props + [BehaviorContract] + BehaviorId
- `JoinFormationParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId
- `IdleParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId
- `WanderMilitaryParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId
- `ConvoyEscortParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId
- `InfantryCombatParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId
- `AmbushParamsJsonDto.cs` - marker + [BehaviorContract] + BehaviorId

**Hrot.Presentation (`Hrot/Engine/Hrot.Presentation/Behavior/`):**
- `BehaviorSchemaDiscovery.cs` - AutoRegister scans Hrot.Core assembly

---

## Suggested Commit Message

```
MPM Phase 5a: Behavior contract foundation (BATCH-05)

- Hrot.Core: BehaviorCategory [Flags] enum (AllMilitary=14)
- Hrot.Core: BehaviorContractAttribute with BehaviorId, BehaviorId, ValidCategories
- Hrot.Core: BehaviorIds internal constants (3001-3015), mirrors CgfBehaviorIds
- Hrot.Core: 4 contract-bearing DTOs in MapDefinitions/Behavior/ (FireAtTarget,
  MoveToLocation, FollowRoute, JoinFormation) with full JSON properties
- Hrot.Core: 5 empty marker DTOs (Idle, WanderMilitary, ConvoyEscort,
  InfantryCombat, Ambush) with [BehaviorContract] + BehaviorId
- Hrot.Presentation: BehaviorSchemaDiscovery.AutoRegister scans Hrot.Core assembly
  and registers all 9 DTOs with BehaviorUiRegistry and ScenarioBehaviorRemapper
- Build: 0 errors, no new project references, test baseline unchanged (10 pre-existing
  integration failures)
```
