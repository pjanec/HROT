# BATCH-06 Report

**Batch:** BATCH-06
**Date:** 2026-04-22
**Status:** Complete

---

## Completion Status

- [x] MPM-P5-T04: Replace BehaviorUiSetup + CgfDoctrineSetup behavior-ID strings
- [x] MPM-P5-T05: Rebuild DoctrineCatalog using reflection
- [x] MPM-P5-T06: Update CgfNodes.cs TreeName strings with DTO constants
- [x] MPM-P5-T07: Create DoctrineTestHelper + update test files

---

## Build Status

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

Build verified after every task (T04, T05, T06, T07). All green.

---

## Test Status

```
dotnet test IOS-IG-SimHost.sln --no-build

Passed!  - Failed: 0, Passed:  94, Skipped: 0, Total:  94  - Hrot.Orchestrator.Tests.dll
Passed!  - Failed: 0, Passed: 219, Skipped: 0, Total: 219  - Hrot.ClusterRunner.Tests.dll
Passed!  - Failed: 0, Passed: 180, Skipped: 0, Total: 180  - Fdp.ModuleHost.Tests.dll
Passed!  - Failed: 0, Passed: 403, Skipped: 3, Total: 406  - Hrot.SimHost.Tests.dll
Passed!  - Failed: 0, Passed: 718, Skipped: 2, Total: 720  - Fdp.Core.Tests.dll
Failed!  - Failed:10, Passed: 130, Skipped: 4, Total: 144  - Hrot.ClusterRunner.Integration.Tests.dll
```

10 pre-existing integration test failures, identical to the BATCH-05 baseline.
All non-integration tests pass.

---

## Developer Insights

**Q1: How did you handle BehaviorUiSetup - which approach did you use (Option A vs B)?**

Used Option A: replaced the body of `CreateRegistry()` with a single call to
`DoctrineSchemaDiscovery.AutoRegister(registry, new ScenarioBehaviorRemapper())`.
A throwaway `ScenarioBehaviorRemapper` is created and discarded - it is never used for
anything because `CreateRegistry()` only returns the `BehaviorUiRegistry`. This keeps the
method body to a minimal two lines. The old `using Fdp.Toolkit.Behavior.Params` was
removed (no longer needed); replaced with `using Fdp.Toolkit.Behavior` for
`ScenarioBehaviorRemapper`.

**Q2: Does Hrot.CGF reference Hrot.Core? How did you handle CgfDoctrineSetup string replacement?**

Hrot.CGF does NOT have a direct `<ProjectReference>` to Hrot.Core in its .csproj.
However, Hrot.Core types ARE accessible transitively: Hrot.CGF directly references
Hrot.Common, which directly references Hrot.Core. In .NET SDK projects, transitive
project references flow to the compiler by default, and Hrot.CGF already uses
`Hrot.Map.Common` types from Hrot.Common (which itself imports Hrot.Core). This
establishes that Hrot.Core is on the reference path.

For `RegisterAll()`: added `using Hrot.Map.Definitions.Doctrine;` and replaced each
string literal in `registry.Register(id, "BehaviorId", ...)` with the corresponding
DTO's `BehaviorId` constant (e.g. `MoveToLocationParamsJsonDto.BehaviorId`). The
`DoctrineDefinition.Name` property values were left as hardcoded strings per the
minimize-diff rule - the task only specifies replacing the `registry.Register()` argument.

For `CreateBehaviorRemapper()`: Hrot.CGF directly references Hrot.Presentation which
contains `DoctrineSchemaDiscovery`. Replaced the two manual `remapper.Register<T>()` calls
with `DoctrineSchemaDiscovery.AutoRegister(new BehaviorUiRegistry(), remapper)`. This
now registers all 9 Hrot.Core DTOs with the remapper (not just the 2 with
`[RemapNetworkId]` properties), which is harmless - registering a DTO with no
`[RemapNetworkId]` properties is a no-op at remapping time. Removed the now-unused
`using Fdp.Toolkit.Behavior.Params`.

**Q3: How did you handle the civilian doctrines (WanderCivil, PanicFlee) in DoctrineCatalog?**

`s_civilianDoctrines = ["WanderCivil", "PanicFlee"]` is preserved exactly as a
hardcoded field. The `GetValidDoctrines()` switch still routes `CivilianPedestrian` and
`CivilianCar` to this hardcoded list. The civilian doctrines have no
`[DoctrineContract]` DTO and would not appear in the reflection scan.

The `s_defaultDoctrines` fallback list is also preserved as hardcoded (the instructions
explicitly say this is acceptable). Only the three military/insurgent lists are rebuilt
dynamically via a static constructor that calls `BuildMap()`.

`BuildMap()` scans `typeof(DoctrineContractAttribute).Assembly` for all types with
`[DoctrineContract]`, then for each of the three non-civilian categories checks
`attr.ValidCategories.HasFlag(cat)` to bucket the `BehaviorId` into the right list.

Note: the dynamically-built lists include `Idle` and `WanderMilitary` in the MilitaryApc
list (both have `DoctrineCategory.AllMilitary` or `DoctrineCategory.MilitaryApc`). The
original hardcoded lists did not include these. This is correct - the DTO category
annotations are the source of truth. The task verification says the list "still contains"
the original entries, which it does.

**Q4: For CgfNodes.cs - what interpolation format did the raw strings use? Any complications?**

All 5 JSON fields were `private const string` with un-interpolated raw string literals
`"""..."""`. Since `$$"""..."""` is an interpolated string (not a compile-time constant),
each field was changed from `const string` to `static readonly string`. The
`$$"""..."""` format uses `{{expression}}` for interpolation while leaving bare `{` and
`}` in the JSON body as literals, so no JSON brace escaping was needed.

There is a naming conflict: CgfNodes.cs contains a private inner class
`FireAtTargetParamsJsonDto` (a local serialization struct) and `MoveToLocationParamsJsonDto`
(also a private inner class). To avoid ambiguity without adding a general
`using Hrot.Map.Definitions.Doctrine;`, fully-qualified names were used for all 5 DTO
references in the interpolations (e.g.
`Hrot.Map.Definitions.Doctrine.WanderMilitaryParamsJsonDto.BehaviorId`). This is verbose
but unambiguous and requires no change to the existing using directives.

**Q5: Which test files were updated? Which were left with magic strings, and why?**

Updated (Hrot.Presentation.Tests - directly references Hrot.Core):
- `Hrot.Presentation.Tests/Behavior/MissionPanelRegistryTests.cs` (line ~42):
  `"FireAtTarget"` replaced with `Hrot.Map.Definitions.Doctrine.FireAtTargetParamsJsonDto.BehaviorId`
- `Hrot.Presentation.Tests/Behavior/BehaviorUiCompilerTests.cs` (lines ~103, 105):
  Both `"FireAtTarget"` occurrences replaced with fully-qualified `BehaviorId` constant.

Left unchanged (no direct Hrot.Core project reference in .csproj):
- `Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs`:
  `"FollowRoute"` strings left as-is. `Hrot.SimHost.Tests.csproj` has no direct
  `<ProjectReference>` to `Hrot.Core`. These are network-level test data strings.
- `Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs`:
  `"MoveToLocation"` strings left as-is. Same reason.
- `Hrot.Network.NED.Tests/MissionControlMarshalRoundTripTests.cs`:
  No reference to Hrot.Core at all. Network serialization round-trip tests - the string
  values are correct by definition.

Created:
- `Hrot.Core/MapDefinitions/Doctrine/DoctrineTestHelper.cs` - new helper with
  `GetBehaviorId<TDto>()` that reads `[DoctrineContractAttribute]` via reflection.

**Q6: Are there any remaining magic behavior-ID strings elsewhere in the codebase?**

- `CgfDoctrineSetup.RegisterAll()`: the `DoctrineDefinition.Name = "MoveToLocation"`
  property values are still hardcoded strings. These are display names, not behavior IDs
  used for lookup - they were not targeted by the task specification.
- `Hrot.SimHost.Tests` and `Hrot.Network.NED.Tests`: as noted above, left unchanged due
  to missing direct Hrot.Core project references.
- `DoctrineCatalog.s_defaultDoctrines`: kept as hardcoded fallback by design.

---

## Suggested Commit Message

```
MPM Phase 5b: Doctrine auto-registration completion (BATCH-06)

- BehaviorUiSetup.CreateRegistry() now uses DoctrineSchemaDiscovery.AutoRegister
  (removes FireAtTarget/FollowRoute/MoveToLocation magic strings)
- CgfDoctrineSetup.RegisterAll() uses DTO BehaviorId constants for all 6 doctrines
- CgfDoctrineSetup.CreateBehaviorRemapper() uses DoctrineSchemaDiscovery.AutoRegister
- DoctrineCatalog: military/insurgent lists rebuilt via [DoctrineContract] reflection;
  civilian list (WanderCivil, PanicFlee) preserved as hardcoded
- CgfNodes.cs: 5 TreeName JSON strings use $$""" interpolation with DTO BehaviorId
  constants; const -> static readonly to allow interpolation
- DoctrineTestHelper added to Hrot.Core for test use
- MissionPanelRegistryTests and BehaviorUiCompilerTests updated to use
  FireAtTargetParamsJsonDto.BehaviorId constant
```
