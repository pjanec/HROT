# BATCH-03: Engine Integration — Deactivators for Channel and EQS Cleanup (Phase 3)

**Batch Number:** BATCH-03
**Tasks:** TASK-EQL-005, TASK-EQL-006, TASK-EQL-007, TASK-EQL-008
**Phase:** Phase 3 — Engine Integration
**Priority:** HIGH
**Dependencies:** BATCH-01 (Phase 1 complete), BATCH-02 (Phase 2 complete — generator active)

---

## Onboarding & Workflow

### Developer Instructions

This batch wires up the four deactivator methods that replace manual channel-cleanup code
in the engine integration layer. For each action, you add a companion static `Deactivate_*`
method annotated with `[BTreeDeactivator(...)]`. The source generator (already active after
BATCH-02) automatically emits the `registry.RegisterDeactivator(...)` call — **do not write
manual registration code**.

Each deactivator tests are pure unit tests that invoke the deactivator method directly,
without running a BTree interpreter.

### Required Reading

1. **Design Document:** `.dev/ai-btree-deactivator-1/DESIGN.md` — read §3.1 through §3.4
   in full before writing any code. The guard conditions, action ID checks, and "what NOT to
   do" constraints are specified per task.
2. **Task Specifications:** `.dev/ai-btree-deactivator-1/TASK-DETAIL.md` — Phase 3 section
   (TASK-EQL-005 through TASK-EQL-008) for precise success conditions.
3. **Existing node files** — read each target file fully before adding the deactivator:
   - `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` (EQL-005)
   - `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` (EQL-006, EQL-007)
   - `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` (EQL-008)
4. **Test pattern reference:**
   - `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HillAttackGizmoTests.cs` — minimal
     `EntityRepository` setup without `HeadlessDemoApp` (register only needed components,
     create entity, add component, call method, assert).
   - `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/ApcBrainTests.cs` — for context on how
     `BTreeContext { Self = entity, World = world }` is constructed in this codebase.

### Source Code Locations

**Implementation files:**
- `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` — add `Deactivate_AimAndFire`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` — add `Deactivate_CreepToAndBeyondSlot`, `Deactivate_AimAndFireSpecific`
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` — add `Deactivate_RequestAreaQuery`

**Test files (add new test classes):**
- `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/InsurgentNodesDeactivatorTests.cs` (EQL-005)
- `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackTankNodesDeactivatorTests.cs` (EQL-006, EQL-007 — both tasks in same file)
- `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackCommanderNodesDeactivatorTests.cs` (EQL-008)

**Test projects:**
- `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/Fdp.Examples.UrbanCombat.Tests.csproj`
- `Hrot/Subsystems/Hrot.IG.Tests/Hrot.IG.Tests.csproj`

### Build & Test Commands

Run from the solution root `D:\WORK\IOS-IG-SimHost-FDP`:

```powershell
# Test EQL-005 (InsurgentNodes deactivator)
dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj --filter "InsurgentNodesDeactivator"

# Test EQL-006, EQL-007, EQL-008 (Hrot behavior deactivators)
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --filter "Deactivator"

# Full baseline verification (after all tasks complete)
dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj
```

### Report Submission

When done, submit your report to:
`.dev/ai-btree-deactivator-1/reports/BATCH-03-REPORT.md`

---

## Context

After BATCH-01 (Phase 1 library) and BATCH-02 (Phase 2 generator), the framework is fully
operational. The BTreeActionGenerator already detects `[BTreeDeactivator]` attributes and
emits `registry.RegisterDeactivator(...)` calls into `FbtActionRegistrar.g.cs`. This means
adding the attribute to a method is the entire wiring step — no manual `ActionRegistry`
calls needed.

**Key generator behavior to remember:**
- For 4-param action methods: `[BTreeDeactivator("Namespace.Class.Action_Name")]`
- For 3-param bridge actions: `[BTreeDeactivator("Namespace.Class.Action_Name@0")]`
  (the `@0` suffix is the bridge method compound key — it is NOT inferred automatically;
  you must include it verbatim in the attribute string)
- The generator validates that the TargetAction matches a known action in the compilation;
  BHU-017 warning if not found. Use the full qualified method name including namespace.

**No existing code should be removed.** Specifically:
- Do NOT remove the explicit `ActiveAction = 0` guards already inside
  `Action_CreepToAndBeyondSlot` body — they remain as belt-and-suspenders.
- Do NOT remove `ClearWeaponActionIfActive` call in `Action_AimAndFireSpecific` for the
  MaxRounds path — the deactivator covers the abort path only.
- Do NOT modify `AiBehaviorFactory` or any behavior wiring code.

---

## Task Specifications

### TASK-EQL-005 — WeaponChannel deactivator for InsurgentNodes.Action_AimAndFire

See TASK-DETAIL.md §TASK-EQL-005 for full specification.

**Target file:** `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs`

**Method to add:** `Deactivate_AimAndFire` — a `public static void` method with 4-param
signature matching `NodeDeactivatorDelegate<BrainBlackboard, BTreeContext>`:
```csharp
[BTreeDeactivator("Fdp.Examples.UrbanCombat.Brains.InsurgentNodes.Action_AimAndFire")]
public static void Deactivate_AimAndFire(
    ref BrainBlackboard blackboard,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
```

**Logic:**
1. Guard: `if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self)) return;`
2. `ref var channel = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);`
3. Guard: `if (channel.ActiveAction != CombatConstants.ActionIdAimAndFire) return;`
4. `channel.ActiveAction = 0;`
5. `unchecked { channel.ActionInstanceId++; }`

**Test file:** `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/InsurgentNodesDeactivatorTests.cs`

**Test setup pattern:**
```csharp
var world = new EntityRepository();
world.RegisterComponent<WeaponChannel>();
var entity = world.CreateEntity();
world.AddComponent(entity, new WeaponChannel { ActiveAction = CombatConstants.ActionIdAimAndFire, ActionInstanceId = 0 });
var ctx = new BTreeContext { Self = entity, World = world };
var state = new BehaviorTreeState();
var bb = new BrainBlackboard();
InsurgentNodes.Deactivate_AimAndFire(ref bb, ref state, ref ctx, 0);
```

**Success conditions** (per TASK-DETAIL.md §TASK-EQL-005):
- T1: Entity WITH `WeaponChannel`, `ActiveAction == ActionIdAimAndFire` → assert `ActiveAction == 0`, `ActionInstanceId` incremented by 1.
- T2: Entity WITHOUT `WeaponChannel` → invoke; assert no exception.
- T3: Entity WITH `WeaponChannel`, `ActiveAction == 0` → invoke; assert `ActionInstanceId` unchanged.
- T4: Entity WITH `WeaponChannel`, `ActiveAction` set to a DIFFERENT action ID → invoke; assert `WeaponChannel` unchanged.

---

### TASK-EQL-006 — LocomotionChannel deactivator for HillAttackTankNodes.Action_CreepToAndBeyondSlot

See TASK-DETAIL.md §TASK-EQL-006 for full specification.

**Target file:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`

**Important:** `Action_CreepToAndBeyondSlot` is a **3-param bridge method** (takes
`ref HullDownAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx`). Its
registered key in the generator is `"Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0"`.

The deactivator must use 4-param signature. Add it to the `HillAttackTankNodes` class. The
`TargetAction` attribute string must include the `@0` suffix:

```csharp
[BTreeDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0")]
public static void Deactivate_CreepToAndBeyondSlot(
    ref BrainBlackboard blackboard,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
```

**Important:** The first parameter is `ref BrainBlackboard` (the group TBlackboard), NOT
`ref HullDownAttackBlackboard`. All methods in `Hrot.AI.Behaviors` belong to a single group
keyed on `BrainBlackboard + BTreeContext`. The bridge closures in the generator use
`Unsafe.As<BrainBlackboard, HullDownAttackParams>` to reinterpret the blackboard — the
deactivator does not need to do this projection.

**Logic:**
1. Guard: `if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self)) return;`
2. `ref var loco = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);`
3. Guard: `if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo) return;`
4. `loco.ActiveAction = 0;`
5. `unchecked { loco.ActionInstanceId++; }`

**Test file:** `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackTankNodesDeactivatorTests.cs`

**Success conditions** (per TASK-DETAIL.md §TASK-EQL-006):
- T1: Entity WITH `LocomotionChannel`, `ActiveAction == NavigationConstants.ActionIdMoveTo` → assert `ActiveAction == 0`, `ActionInstanceId` incremented.
- T2: Entity WITHOUT `LocomotionChannel` → invoke; assert no exception.
- T3: Entity WITH `LocomotionChannel`, `ActiveAction` set to a DIFFERENT action ID → invoke; assert unchanged.

---

### TASK-EQL-007 — WeaponChannel deactivator for HillAttackTankNodes.Action_AimAndFireSpecific

See TASK-DETAIL.md §TASK-EQL-007 for full specification.

**Target file:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`
(same file as EQL-006; add the second deactivator in the same class)

**Important:** `Action_AimAndFireSpecific` is also a **3-param bridge method** (takes
`ref HullDownAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx`).

```csharp
[BTreeDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_AimAndFireSpecific@0")]
public static void Deactivate_AimAndFireSpecific(
    ref BrainBlackboard blackboard,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
```

**Important:** Same as EQL-006 — first parameter is `ref BrainBlackboard`.

**Logic:**
1. Guard: `if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self)) return;`
2. `ref var weapon = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);`
3. Guard: `if (weapon.ActiveAction != CombatConstants.ActionIdAimAndFire) return;`
4. `weapon.ActiveAction = 0;`
5. `unchecked { weapon.ActionInstanceId++; }`

**Test file:** `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackTankNodesDeactivatorTests.cs`
(same file as EQL-006 tests; add a separate test class for the weapon deactivator)

**Success conditions** (per TASK-DETAIL.md §TASK-EQL-007, same structure as EQL-005):
- T1: Entity WITH `WeaponChannel`, `ActiveAction == CombatConstants.ActionIdAimAndFire` → assert `ActiveAction == 0`, `ActionInstanceId` incremented.
- T2: Entity WITHOUT `WeaponChannel` → invoke; assert no exception.
- T3: Entity WITH `WeaponChannel`, `ActiveAction == 0` → invoke; assert `ActionInstanceId` unchanged.
- T4: Entity WITH `WeaponChannel`, `ActiveAction` set to a DIFFERENT action ID → invoke; assert unchanged.

---

### TASK-EQL-008 — EqsRequestId deactivator for HillAttackCommanderNodes.Action_RequestAreaQuery

See TASK-DETAIL.md §TASK-EQL-008 for full specification.

**Target file:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`

**Important:** `Action_RequestAreaQuery` is a **3-param bridge method** (takes
`ref PlatoonHillAttackParams p, ref BehaviorTreeState state, ref BTreeContext ctx`).

```csharp
[BTreeDeactivator("Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0")]
public static void Deactivate_RequestAreaQuery(
    ref BrainBlackboard blackboard,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
```

**Important:** The first parameter is `ref BrainBlackboard` (the group TBlackboard for all
methods in `Hrot.AI.Behaviors`), NOT `ref PlatoonHillAttackBlackboard`. Verified by reading
the existing `FbtActionRegistrar.g.cs` which shows a single `RegisterAll` method keyed on
`ActionRegistry<BrainBlackboard, BTreeContext>`.

**Logic (matching the `Unsafe.As` pattern used throughout this file):**
1. Guard: `if (!ctx.World.HasComponent<Blackboard1024>(ctx.Self)) return;`
2. `ref var heavyComp = ref ctx.World.GetComponentRW<Blackboard1024>(ctx.Self);`
3. `ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref heavyComp);`
4. `s.CachedEqsRequestId = -1;`

**Test file:** `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackCommanderNodesDeactivatorTests.cs`

**Test setup:** You need a `Blackboard1024` component (which is a large struct projected via
`Unsafe.As` to `HillAttackMutableState`). Set up the world with:
```csharp
world.RegisterComponent<Blackboard1024>();
```
Then add the component and set `CachedEqsRequestId` on the projected `HillAttackMutableState`:
```csharp
var bb1024 = new Blackboard1024();
ref var s = ref Unsafe.As<Blackboard1024, HillAttackMutableState>(ref bb1024);
s.CachedEqsRequestId = 42;
world.AddComponent(entity, bb1024);
```

**Success conditions** (per TASK-DETAIL.md §TASK-EQL-008):
- T1: Entity WITH `Blackboard1024`, `CachedEqsRequestId == 42` → invoke; assert `CachedEqsRequestId == -1`.
- T2: Entity WITHOUT `Blackboard1024` → invoke; assert no exception.
- T3: Entity WITH `Blackboard1024`, `CachedEqsRequestId == -1` already → invoke; assert no exception and value is still -1.

---

## Success Criteria for the Whole Batch

Before submitting the report, verify:

1. **Build passes:** The FDP solution and Hrot subsystems compile without errors.
   ```powershell
   dotnet build FDP\FDP.sln
   dotnet build Hrot\Subsystems\Hrot.AI.Behaviors\Hrot.AI.Behaviors.csproj
   dotnet build Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj
   ```
2. **Generator emits deactivators:** After build, inspect the generated
   `FbtActionRegistrar.g.cs` in the build output for `Hrot.AI.Behaviors` and
   `Fdp.Examples.UrbanCombat` and confirm `registry.RegisterDeactivator(...)` lines appear
   for each new deactivator.
3. **All new deactivator tests pass:**
   ```powershell
   dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj --filter "Deactivator"
   dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --filter "Deactivator"
   ```
4. **No regressions:** Full test suite passes with the same or better pass counts vs the
   BATCH-02 baseline:
   - `Fdp.Examples.UrbanCombat.Tests`: establish new baseline (note total count before and after)
   - `Hrot.IG.Tests`: no new failures vs pre-BATCH-03 baseline
