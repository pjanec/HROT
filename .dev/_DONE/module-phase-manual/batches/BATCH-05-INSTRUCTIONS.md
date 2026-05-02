# BATCH-05: Phase 5a - Behavior Contract Foundation

**Batch Number:** BATCH-05  
**Tasks:** MPM-P5-T01, MPM-P5-T02, MPM-P5-T03  
**Phase:** Phase 5 - Behavior Auto-Registration (Part A)  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 through BATCH-04 completed (Phase 5 is independent of Phases 3-4)

---

## Onboarding & Workflow

### Developer Instructions
Phase 5 eliminates all behavior behavior-ID magic strings from composition roots, domain catalogs, AI tree asset definitions, and unit tests. The strategy is to make each parameter DTO the Single Source of Truth for its behavior ID, integer ID, and tactical applicability category.

This batch (Phase 5a) builds the **foundation**: the attribute/enum types, DTO decorations, and the auto-registration utility. BATCH-06 (Phase 5b) will use these to replace the manual registrations and hardcoded string tables.

**Complete tasks in sequence.** Each task's output is needed by the next.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` - MPM-P5-T01, MPM-P5-T02, MPM-P5-T03
3. **Design Document:** `.dev/module-phase-manual/DESIGN.md` - Sections 5.1 through 5.3 (read all three)

### Key Locations to Explore Before Coding
- `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/` - where T01 new files go, where T02 new marker DTOs go
- `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/` - where existing DTOs like `FireAtTargetParamsJsonDto.cs` live (or find them - they may be directly under `Behavior/` or nearby subdirectory)
- `Hrot/Engine/Hrot.Core/` - look for `CgfBehaviorIds.cs` or similar file with behavior integer ID constants
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiSetup.cs` - to understand `BehaviorUiRegistry` and `Register<T>` signature
- `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiRegistry.cs` - to understand the registry type
- `Hrot/Subsystems/Hrot.CGF/Configuration/CgfBehaviorSetup.cs` - to understand `ScenarioBehaviorRemapper` and its `Register<T>` signature
- `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj` - to check its project references
- `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj` - to check if it references both Hrot.Core and Hrot.Presentation

### Report Submission
**Submit to:** `.dev/module-phase-manual/reports/BATCH-05-REPORT.md`  
**Questions to:** `.dev/module-phase-manual/questions/BATCH-05-QUESTIONS.md`

---

## Context

The current state has behavior behavior IDs scattered as magic strings in at least four places: `BehaviorUiSetup.cs`, `CgfBehaviorSetup.cs`, `BehaviorCatalog.cs`, and AI tree JSON in `CgfNodes.cs`. Each DTO has no self-description of its own behavior ID.

After BATCH-05:
- `BehaviorCategory` and `BehaviorContractAttribute` exist in `Hrot.Core`
- All 9 behavior DTOs (4 existing + 5 new marker DTOs) carry `[BehaviorContract]` + `const string BehaviorId`
- `BehaviorSchemaDiscovery.AutoRegister` exists and can replace any manual registration loop

BATCH-06 will then use these to actually replace the registrations.

---

## MANDATORY WORKFLOW

Build after EVERY task.

```
T01 → build ✅ → T02 → build ✅ → T03 → dependency check → build ✅ → full test sweep ✅
```

**DO NOT stop to ask for permission. Fix compile errors autonomously.**

---

## Tasks

### Task 1: Create BehaviorCategory and BehaviorContractAttribute (MPM-P5-T01)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.1  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T01

**New files to create** in `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/`:

**`BehaviorCategory.cs`:**
```csharp
namespace Hrot.Core.MapDefinitions.Behavior
{
    [Flags]
    public enum BehaviorCategory
    {
        None         = 0,
        Civilian     = 1 << 0,
        MilitaryApc  = 1 << 1,
        Infantry     = 1 << 2,
        Insurgent    = 1 << 3,
        AllMilitary  = MilitaryApc | Infantry | Insurgent
    }
}
```
(Use the exact namespace of the Behavior folder - verify by checking adjacent files.)

**`BehaviorContractAttribute.cs`:**
```csharp
namespace Hrot.Core.MapDefinitions.Behavior
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorContractAttribute : Attribute
    {
        public int BehaviorId { get; }
        public string BehaviorId { get; }
        public BehaviorCategory ValidCategories { get; }

        public BehaviorContractAttribute(int behaviorId, string behaviorId, BehaviorCategory categories)
        {
            BehaviorId      = behaviorId;
            BehaviorId      = behaviorId;
            ValidCategories = categories;
        }
    }
}
```

**Important:** Check the namespace of existing files in `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/` and match it exactly.

**Verify:**
- Both files compile.
- `BehaviorCategory.AllMilitary == 14` (MilitaryApc=2, Infantry=4, Insurgent=8).
- `dotnet build IOS-IG-SimHost.sln` passes.

---

### Task 2: Decorate Existing DTOs and Create Empty Marker DTOs (MPM-P5-T02)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.2  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T02

**Step A - Find behavior integer ID constants:**

Search for a file like `CgfBehaviorIds.cs` or a class with behavior ID constants such as `FireAtTarget_BT`, `MoveTo_BT`, etc. These are the integer IDs to use as the `behaviorId` argument of `[BehaviorContract]`. Note the exact constant names and their integer values.

**Step B - Modify existing DTOs:**

Find and modify 4 existing DTO files. Their locations may be under `Hrot/Engine/Hrot.Core/MapDefinitions/` or nearby. Search for them by name:
- `FireAtTargetParamsJsonDto`
- `MoveToLocationParamsJsonDto`
- `FollowRouteParamsJsonDto`
- `JoinFormationParamsJsonDto`

For each, add (inside the class, at the top):
```csharp
public const string BehaviorId = "<the-behavior-id-string>";
```
And add the attribute on the class:
```csharp
[BehaviorContract(CgfBehaviorIds.FireAtTarget_BT, BehaviorId, BehaviorCategory.AllMilitary)]
```

Use the exact string values already in use in `BehaviorUiSetup.cs` or `CgfBehaviorSetup.cs` — those are the ground truth for what the behavior ID strings must be.

**Step C - Create new empty marker DTOs:**

Create 5 new files in `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/`:
- `IdleParamsJsonDto.cs`
- `WanderMilitaryParamsJsonDto.cs`
- `ConvoyEscortParamsJsonDto.cs`
- `InfantryCombatParamsJsonDto.cs`
- `AmbushParamsJsonDto.cs`

Each has the pattern (example for Idle):
```csharp
namespace Hrot.Core.MapDefinitions.Behavior
{
    [BehaviorContract(CgfBehaviorIds.Idle_HSM, BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class IdleParamsJsonDto
    {
        public const string BehaviorId = "Idle";
    }
}
```

Match the integer ID constants and category flags from DESIGN.md § 5.2. Use the existing behavior ID strings as they appear in `BehaviorUiSetup.cs`/`CgfBehaviorSetup.cs`.

**Verify:**
- All 9 DTOs have `[BehaviorContract]` attribute and `const string BehaviorId`.
- `dotnet build IOS-IG-SimHost.sln` passes.

---

### Task 3: Create BehaviorSchemaDiscovery (MPM-P5-T03)

**Design Reference:** `.dev/module-phase-manual/DESIGN.md` § 5.3  
**Task Detail:** `.dev/module-phase-manual/TASK-DETAIL.md` § MPM-P5-T03

**CRITICAL FIRST STEP - Dependency check:**

Before writing any code, check which project can legally host `BehaviorSchemaDiscovery`:
1. Read `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj` - does it reference `Hrot.Core`?
2. Read `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj` - does it reference both `Hrot.Core` and `Hrot.Presentation`?
3. Read `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiRegistry.cs` - to understand the registry type
4. Find where `ScenarioBehaviorRemapper` is defined and which project owns it

`BehaviorSchemaDiscovery` must reference:
- `BehaviorContractAttribute` (from `Hrot.Core`)
- `BehaviorUiRegistry` and its `Register<T>` method
- `ScenarioBehaviorRemapper` and its `Register<T>` method

Choose the project that already references all three without creating a circular dependency. **Do NOT create new project references.** If `Hrot.Presentation` already references `Hrot.Core` and defines `BehaviorUiRegistry`, it is likely the right home.

**Implementation:**

Create `BehaviorSchemaDiscovery.cs` in the chosen project. Content as specified in DESIGN.md § 5.3:

```csharp
public static class BehaviorSchemaDiscovery
{
    public static void AutoRegister(BehaviorUiRegistry uiRegistry, ScenarioBehaviorRemapper remapper)
    {
        var uiRegMethod  = typeof(BehaviorUiRegistry).GetMethod("Register")!;
        var remapMethod  = typeof(ScenarioBehaviorRemapper).GetMethod("Register")!;

        var dtoTypes = typeof(BehaviorContractAttribute).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<BehaviorContractAttribute>() != null);

        foreach (var type in dtoTypes)
        {
            var attr = type.GetCustomAttribute<BehaviorContractAttribute>()!;
            uiRegMethod.MakeGenericMethod(type).Invoke(uiRegistry, [attr.BehaviorId]);
            remapMethod.MakeGenericMethod(type).Invoke(remapper, [attr.BehaviorId]);
        }
    }
}
```

**Important:** The `Register<T>` method signature on each registry type determines exactly how `MakeGenericMethod.Invoke` is called. Read the actual method signatures before writing the invocation code. Adjust the argument array accordingly.

**Verify:**
- `BehaviorSchemaDiscovery.cs` compiles in its chosen project.
- No new circular project dependencies introduced.
- `dotnet build IOS-IG-SimHost.sln` passes.
- `dotnet test IOS-IG-SimHost.sln --no-build` - same baseline as before (130 pass, 10 pre-existing integration failures).

---

## Testing Requirements

1. **After each task:** `dotnet build IOS-IG-SimHost.sln`
2. **After Task 3 (final):** `dotnet test IOS-IG-SimHost.sln --no-build`

No new unit tests required for T01 (the types are tested transitively). T02 and T03 tests are mentioned in TASK-DETAIL.md as "success conditions" but these will be verified via the build and integration smoke.

---

## Report Requirements

Submit to `.dev/module-phase-manual/reports/BATCH-05-REPORT.md`.

```markdown
# BATCH-05 Report

## Completion Status
- [ ] MPM-P5-T01: Create BehaviorCategory + BehaviorContractAttribute
- [ ] MPM-P5-T02: Decorate existing DTOs + create 5 marker DTOs
- [ ] MPM-P5-T03: Create BehaviorSchemaDiscovery

## Build Status
[Result of: dotnet build IOS-IG-SimHost.sln]

## Test Status
[Result of: dotnet test IOS-IG-SimHost.sln --no-build]

## Developer Insights

**Q1:** Where did you find the existing DTOs (exact file paths)? Were any in unexpected locations?

**Q2:** What are the exact behavior ID strings you found in BehaviorUiSetup.cs/CgfBehaviorSetup.cs?

**Q3:** Which project hosts BehaviorSchemaDiscovery, and why?

**Q4:** What is the exact signature of BehaviorUiRegistry.Register<T> and ScenarioBehaviorRemapper.Register<T>?

**Q5:** Did you find any other places where behavior-ID strings are hardcoded that BATCH-06 should be aware of?

## Suggested Commit Message
[Your commit message suggestion]
```

---

## Success Criteria

- [ ] `BehaviorCategory.cs` and `BehaviorContractAttribute.cs` exist in `Hrot.Core`
- [ ] 4 existing DTOs have `[BehaviorContract]` + `const string BehaviorId`
- [ ] 5 new marker DTO files created with `[BehaviorContract]` + `const string BehaviorId`
- [ ] `BehaviorSchemaDiscovery.cs` compiles without new project reference cycles
- [ ] `dotnet build IOS-IG-SimHost.sln` - 0 errors
- [ ] Test count unchanged from BATCH-04 baseline
- [ ] Report submitted

---

## Common Pitfalls

- **Namespace match:** Use the exact namespace of existing files in `Hrot.Core/MapDefinitions/Behavior/`. Do NOT invent a new namespace.
- **BehaviorId string values:** Copy behavior ID strings exactly from `BehaviorUiSetup.cs`/`CgfBehaviorSetup.cs`. These are the canonical values. If they differ between the two setup files, flag in the report but use the `BehaviorUiSetup.cs` version as canonical.
- **BehaviorSchemaDiscovery assembly scan:** `typeof(BehaviorContractAttribute).Assembly` scans the `Hrot.Core` assembly. If `BehaviorSchemaDiscovery` lives in `Hrot.Presentation`, it still scans `Hrot.Core`'s assembly, which is correct.
- **Register<T> method lookup:** `GetMethod("Register")` may fail if there are multiple overloads. Use `GetMethod("Register", BindingFlags...)` with specific parameters if needed. Read the actual method signature first.
- **No new project references:** If neither `Hrot.Presentation` nor `Hrot.CGF` can host it without a new reference, choose the one that minimizes changes and document in the report.

---

## Reference Materials
- **Design:** `.dev/module-phase-manual/DESIGN.md` §§ 5.1-5.3
- **Task Details:** `.dev/module-phase-manual/TASK-DETAIL.md` §§ MPM-P5-T01 through MPM-P5-T03
- **Previous Reviews:** `.dev/module-phase-manual/reviews/BATCH-04-REVIEW.md`
