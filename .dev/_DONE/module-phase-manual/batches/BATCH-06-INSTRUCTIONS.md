# BATCH-06: Phase 5b - Behavior Auto-Registration Completion

**Batch Number:** BATCH-06  
**Tasks:** MPM-P5-T04, MPM-P5-T05, MPM-P5-T06, MPM-P5-T07  
**Phase:** Phase 5 - Behavior Auto-Registration (Part B)  
**Estimated Effort:** 7-9 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-05 completed (BehaviorContractAttribute, DTOs, BehaviorSchemaDiscovery all exist)

---

## Onboarding & Workflow

### Developer Instructions
This batch completes Phase 5. The foundation from BATCH-05 is in place. This batch uses it to:
1. Replace manual behavior-ID string registrations with reflection-based auto-registration
2. Rebuild `BehaviorCatalog` from `[BehaviorContract]` attributes instead of hardcoded string arrays
3. Replace behavior-ID string literals in `CgfNodes.cs` AI tree JSON with DTO constants
4. Create `BehaviorTestHelper` and remove magic behavior-ID strings from test files

**Read the relevant source files BEFORE modifying them.** The registrations and DTO structures are complex.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - MPM-P5-T04 through MPM-P5-T07
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 5.4 through 5.8
4. **BATCH-05 Review:** `.dev/module-phase-manual/reviews/BATCH-05-REVIEW.md` - especially the notes for BATCH-06

### Key Source Files to Read First
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiSetup.cs` - current manual registrations
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorSchemaDiscovery.cs` - the new auto-registration utility
- `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` - current manual registrations
- `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BehaviorCatalog.cs` - current hardcoded string arrays
- `Hrot/Subsystems/Hrot.CGF/Brains/CgfNodes.cs` - contains TreeName string literals (lines ~239, 333, 357, 378, 598)
- `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/` - all new DTOs and BehaviorIds.cs

### Report Submission
**Submit to:** `.dev/module-phase-manual/reports/BATCH-06-REPORT.md`  
**Questions to:** `.dev/module-phase-manual/questions/BATCH-06-QUESTIONS.md`

---

## Context

BATCH-05 created:
- `BehaviorCategory`, `BehaviorContractAttribute`, `BehaviorIds` in `Hrot.Core`
- 9 DTOs in `Hrot.Core/MapDefinitions/Behavior/` (4 full + 5 marker), all with `[BehaviorContract]`
- `BehaviorSchemaDiscovery.AutoRegister(BehaviorUiRegistry, ScenarioBehaviorRemapper)` in `Hrot.Presentation`

**Important context from BATCH-05 report:**
- The 3 existing param DTOs in `Fdp.Toolkits` (`FireAtTargetParamsJsonDto`, `MoveToLocationParamsJsonDto`, `FollowRouteParamsJsonDto`) cannot reference `Hrot.Core` types. The new Hrot.Core versions in `Hrot.Core/MapDefinitions/Behavior/` are the decorated ones.
- `BehaviorIds.cs` in Hrot.Core has internal integer constants mirroring `CgfBehaviorIds.cs` in Hrot.CGF.
- `BehaviorCatalog.cs` has civilian behaviors `"WanderCivil"` and `"PanicFlee"` that have NO `[BehaviorContract]` DTO - these must be preserved in TASK 2 (BehaviorCatalog rebuild).

---

## MANDATORY WORKFLOW

Build after EVERY task. Tests after T07 (final).

```
T04 → build ✅ → T05 → build ✅ → T06 → build ✅ → T07 → build + all tests ✅
```

**DO NOT stop to ask for permission. Fix compile errors autonomously.**

---

## Tasks

### Task 1: Replace BehaviorUiSetup and CgfBehaviorSetup Behavior-ID Strings (MPM-P5-T04)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.4, 5.5  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T04

This task has two distinct parts. Read each file carefully first.

#### Part A: BehaviorUiSetup.cs

**File:** `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiSetup.cs`

Currently calls:
```csharp
registry.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
registry.Register<FollowRouteParamsJsonDto>("FollowRoute");
registry.Register<MoveToLocationParamsJsonDto>("MoveToLocation");
```

Replace `CreateRegistry()` to use `BehaviorSchemaDiscovery`. Two approaches - pick the one that compiles cleanly:

Option A (preferred): Replace the body with `BehaviorSchemaDiscovery.AutoRegister(registry, remapper)` where a fresh `ScenarioBehaviorRemapper` is created and discarded. Return the registry.

Option B: Split `BehaviorSchemaDiscovery` to have a `RegisterUi(BehaviorUiRegistry)` overload that only does the UI part.

**Note:** `BehaviorUiSetup.CreateRegistry()` returns only the `BehaviorUiRegistry`. The `ScenarioBehaviorRemapper` part of `AutoRegister` may need to go somewhere - either use a throwaway remapper or restructure. The goal is: no magic behavior-ID strings in `BehaviorUiSetup.cs`.

Also check: does `BehaviorUiSetup.cs` still need the `using Fdp.Toolkit.Behavior.Params` import after the change? If not, remove it.

#### Part B: CgfBehaviorSetup.cs

**File:** `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs`

Two methods to update:

1. `RegisterAll()` - each `registry.Register(id, "BehaviorId", ...)` call has a raw string. Replace the string with the corresponding DTO's `BehaviorId` constant. 

   **Check first:** Does `Hrot.CGF` project reference `Hrot.Core`? (Check `Hrot.CGF.csproj`). If yes, use `Hrot.Core.MapDefinitions.Behavior.MoveToLocationParamsJsonDto.BehaviorId` etc. If no, use `BehaviorIds` class constants from Hrot.Core or add the project reference only if it doesn't create a cycle.

   If `Hrot.CGF` cannot reference `Hrot.Core`, the alternative is to use local string constants (e.g., `private const string BehaviorId_MoveToLocation = "MoveToLocation"`) inside `CgfBehaviorSetup`. This is not ideal but avoids a bad dependency. Document your choice in the report.

2. `CreateBehaviorRemapper()` - currently calls `remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget")` using Fdp.Toolkits DTOs. Check if `BehaviorSchemaDiscovery` can be called from `Hrot.CGF` (it lives in `Hrot.Presentation` - check if Hrot.CGF references Hrot.Presentation). If yes, replace with `BehaviorSchemaDiscovery.AutoRegister(new BehaviorUiRegistry(), remapper)` or a dedicated overload. If no, replace the string literal with a constant as above.

**Verify after Task 1:**
- No magic behavior-ID string literals (like `"FireAtTarget"`, `"MoveToLocation"`, etc.) remain in `BehaviorUiSetup.cs` or `CgfBehaviorSetup.cs`.
- `dotnet build IOS-IG-SimHost.sln` passes.

---

### Task 2: Rebuild BehaviorCatalog Using Reflection (MPM-P5-T05)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.6  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T05

**File to modify:** `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BehaviorCatalog.cs`

**IMPORTANT - Read BehaviorCatalog.cs first!**

The current file has:
- `s_civilianBehaviors = ["WanderCivil", "PanicFlee"]` - these have NO `[BehaviorContract]` DTO
- `s_militaryApcBehaviors = ["ConvoyEscort", "MoveToLocation", "FollowRoute", "FireAtTarget"]`
- `s_infantryBehaviors = ["InfantryCombat", "MoveToLocation", "JoinFormation", "FireAtTarget"]`
- `s_insurgentBehaviors = ["Ambush", "MoveToLocation", "FireAtTarget"]`
- `s_defaultBehaviors = ["MoveToLocation", "FollowRoute", "JoinFormation", "Idle", "FireAtTarget"]`

The DESIGN.md approach (DESIGN.md § 5.6) builds lists dynamically from `[BehaviorContract]` attributes, then also handles `GetValidBehaviors(tkbType)` via a type-to-category mapping.

**Two-part implementation:**

1. **Reflection-based list for military/insurgent categories:** Build the four military/insurgent behavior lists dynamically from assembly scanning using the `BehaviorCategory` flags. This replaces `s_militaryApcBehaviors`, `s_infantryBehaviors`, `s_insurgentBehaviors`.

2. **Preserve civilian list manually:** The civilian behaviors `"WanderCivil"` and `"PanicFlee"` do not have `[BehaviorContract]` DTOs and so cannot appear in reflection results. Keep `s_civilianBehaviors` as a hardcoded list for now, since adding DTOs for those civilian-only behaviors is out of scope. The `GetValidBehaviors` switch still handles `TkbEntityTypes.CivilianPedestrian` and `TkbEntityTypes.CivilianCar` with the preserved hardcoded list.

3. **Replace s_defaultBehaviors:** The default list can be built via reflection using `BehaviorCategory.None` or a special approach, OR keep it as a hardcoded fallback - your choice. Document in report.

Implement `BuildMap()` per DESIGN.md § 5.6, then wire `GetValidBehaviors` to use the reflection-built map for military/insurgent types. The switch case for civilian types uses the preserved hardcoded list.

**Important:** `BehaviorCatalog` is in `Hrot.Core` which already contains `BehaviorContractAttribute` - so no new project references needed.

**Verify:**
- `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc)` still returns list containing `"FireAtTarget"`, `"MoveToLocation"`, `"ConvoyEscort"`, `"FollowRoute"`.
- `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianPedestrian)` returns `["WanderCivil", "PanicFlee"]` unchanged.
- No magic behavior-ID strings remain for military/insurgent categories in the new implementation.
- `dotnet build IOS-IG-SimHost.sln` passes.

---

### Task 3: Update CgfNodes.cs to Use DTO BehaviorId Constants (MPM-P5-T06)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.7  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T06

**File to modify:** `Hrot/Subsystems/Hrot.CGF/Brains/CgfNodes.cs`

5 occurrences of TreeName string literals to replace (at lines ~239, 333, 357, 378, 598):
- `"TreeName": "WanderMilitary"` → use `WanderMilitaryParamsJsonDto.BehaviorId`
- `"TreeName": "MoveToLocation"` → use `MoveToLocationParamsJsonDto.BehaviorId`
- `"TreeName": "FollowRoute"` → use `FollowRouteParamsJsonDto.BehaviorId`
- `"TreeName": "JoinFormation"` → use `JoinFormationParamsJsonDto.BehaviorId`
- `"TreeName": "FireAtTarget"` → use `FireAtTargetParamsJsonDto.BehaviorId`

These DTOs live in `Hrot.Core.MapDefinitions.Behavior`. **Check if `Hrot.CGF` references `Hrot.Core`** (see the `.csproj`). If yes, use `using Hrot.Core.MapDefinitions.Behavior;` and reference `MoveToLocationParamsJsonDto.BehaviorId` directly.

The JSON is in raw string literals (interpolated). Use C# interpolated raw strings (`$$"""..."""`) to inject the constant value. The approach from DESIGN.md § 5.7:
```csharp
// Before:
"TreeName": "FireAtTarget",
// After (inside $$"""..."""):
"TreeName": "{{FireAtTargetParamsJsonDto.BehaviorId}}",
```

**Read the surrounding raw string literals carefully** to understand their current interpolation level (single `$` or `$$` or no interpolation). Adjust the `$` count if needed to avoid collisions.

**Runtime values must be identical** - the produced JSON string values must be unchanged.

**Verify:**
- No raw TreeName behavior-ID string literals remain in `CgfNodes.cs`.
- All runtime JSON values are identical to what they were before.
- `dotnet build IOS-IG-SimHost.sln` passes.

---

### Task 4: Create BehaviorTestHelper and Update Test Files (MPM-P5-T07)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.8  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T07

**Step A - Create BehaviorTestHelper:**

**New file:** `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorTestHelper.cs`

Content (from DESIGN.md § 5.8):
```csharp
namespace Hrot.Core.MapDefinitions.Behavior
{
    public static class BehaviorTestHelper
    {
        public static string GetBehaviorId<TDto>()
        {
            var attr = typeof(TDto).GetCustomAttribute<BehaviorContractAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(TDto).Name} is missing [BehaviorContractAttribute]");
            return attr.BehaviorId;
        }
    }
}
```

**Step B - Update test files with magic behavior-ID strings:**

Search for test files under `Hrot/` that use behavior ID strings as test data. The following were found in the codebase:

**`Hrot/Engine/Hrot.Presentation.Tests/Behavior/MissionPanelRegistryTests.cs`** (line ~42):
Has `registry.Register<Fdp.Toolkit.Behavior.Params.FireAtTargetParamsJsonDto>("FireAtTarget")`. This test is registering a DTO directly - can replace `"FireAtTarget"` with `Hrot.Core.MapDefinitions.Behavior.FireAtTargetParamsJsonDto.BehaviorId`.

**`Hrot/Engine/Hrot.Presentation.Tests/Behavior/BehaviorUiCompilerTests.cs`** (lines ~103, 105):
Has `registry.Register<FireAtTargetParamsJsonDto>("FireAtTarget")` and `registry.TryGet("FireAtTarget", ...)`. Replace the string with `FireAtTargetParamsJsonDto.BehaviorId` (use appropriate DTO - either Hrot.Core version or Fdp.Toolkits version depending on project references).

**`Hrot/Network/Hrot.Network.NED.Tests/MissionControlMarshalRoundTripTests.cs`** (multiple lines):
Has many `BehaviorId = "WanderMilitary"` and `"MoveToLocation"` usages. These are protocol-level test data for network message round-trips. Check if this test project references `Hrot.Core`. If yes, replace with `WanderMilitaryParamsJsonDto.BehaviorId` etc. If no, leave them as-is and note in the report (these are network serialization tests, not behavior registration tests - the string values are correct by definition).

**`Hrot/Subsystems/Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs`** (lines ~46, 47, 85):
Has `registry.Register(42, "FollowRoute", ...)`. Replace `"FollowRoute"` with `Hrot.Core.MapDefinitions.Behavior.FollowRouteParamsJsonDto.BehaviorId` if the test project references Hrot.Core.

**`Hrot/Subsystems/Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs`** (line ~38):
Has `registry.Register(101, "MoveToLocation", ...)`. Same pattern - replace if project references allow it.

**Important guidance:** Only update test files where the project already references `Hrot.Core` (check `.csproj`). Do NOT add new project references just to update test strings. If a test project doesn't reference `Hrot.Core`, leave those magic strings as-is and note them in the report.

**Verify after Task 4:**
- `BehaviorTestHelper.cs` exists and compiles.
- Test files that were updated still pass.
- `dotnet build IOS-IG-SimHost.sln` - 0 errors.
- `dotnet test IOS-IG-SimHost.sln --no-build` - same baseline as BATCH-05 (10 pre-existing integration failures, all others pass).

---

## Testing Requirements

1. **After each task:** `dotnet build IOS-IG-SimHost.sln`
2. **Final after T07:** `dotnet test IOS-IG-SimHost.sln --no-build`

---

## Report Requirements

Submit to `.dev/module-phase-manual/reports/BATCH-06-REPORT.md`.

```markdown
# BATCH-06 Report

## Completion Status
- [ ] MPM-P5-T04: Replace BehaviorUiSetup + CgfBehaviorSetup behavior-ID strings
- [ ] MPM-P5-T05: Rebuild BehaviorCatalog using reflection
- [ ] MPM-P5-T06: Update CgfNodes.cs TreeName strings with DTO constants
- [ ] MPM-P5-T07: Create BehaviorTestHelper + update test files

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Result of: dotnet test IOS-IG-SimHost.sln --no-build]

## Developer Insights

**Q1:** How did you handle BehaviorUiSetup - which approach did you use (Option A vs B)?

**Q2:** Does Hrot.CGF reference Hrot.Core? How did you handle CgfBehaviorSetup string replacement?

**Q3:** How did you handle the civilian behaviors (WanderCivil, PanicFlee) in BehaviorCatalog?

**Q4:** For CgfNodes.cs - what interpolation format did the raw strings use? Any complications?

**Q5:** Which test files were updated? Which were left with magic strings, and why?

**Q6:** Are there any remaining magic behavior-ID strings elsewhere in the codebase?

## Suggested Commit Message
[Your commit message]
```

---

## Success Criteria

- [ ] No magic behavior-ID string literals in `BehaviorUiSetup.cs`
- [ ] No magic behavior-ID strings in `CgfBehaviorSetup.cs` (replaced with constants or DTO refs)
- [ ] `BehaviorCatalog.cs` military/insurgent lists built from reflection (no hardcoded strings for those)
- [ ] `BehaviorCatalog.cs` civilian list (`WanderCivil`, `PanicFlee`) preserved correctly
- [ ] No raw TreeName behavior-ID strings in `CgfNodes.cs`
- [ ] `BehaviorTestHelper.cs` created in Hrot.Core
- [ ] Test files updated where project references permit
- [ ] `dotnet build IOS-IG-SimHost.sln` - 0 errors
- [ ] Test count unchanged from BATCH-05 baseline
- [ ] Report submitted

---

## Common Pitfalls

- **Civilian behaviors:** `WanderCivil` and `PanicFlee` are NOT in the `BehaviorCategory` enum flags or any DTO. They MUST be kept as a hardcoded list. Do NOT remove them.
- **CgfNodes interpolation:** The raw string literals in CgfNodes.cs may already use `$"""..."""` format (single dollar). To add `{{ClassName.BehaviorId}}` interpolation, you need to change to `$$"""..."""` (double dollar). Read the actual string before editing.
- **BehaviorCatalog s_defaultBehaviors:** The design doesn't specify how to rebuild this. If it's complex, keep it as a hardcoded list (it's a fallback, not a category with a matching entity type).
- **Don't add new project references.** If a test project can't reference Hrot.Core, leave those strings and note them.
- **BehaviorUiSetup cleanup:** After using BehaviorSchemaDiscovery, remove the unused `using Fdp.Toolkit.Behavior.Params` if applicable.

---

## Reference Materials
- **Design:** `.dev/module-phase-manual/DESIGN.md` §§ 5.4-5.8
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` §§ MPM-P5-T04 through MPM-P5-T07
- **BATCH-05 Review:** `.dev/module-phase-manual/reviews/BATCH-05-REVIEW.md`
