# BATCH-02: Phases 4 + 5 — Shared AI Attributes and Channel Safety

**Batch Number:** BATCH-02
**Tasks:** BHU-011, BHU-012, BHU-013, BHU-014
**Phase:** 4 (Shared AI Node Attributes) + 5 (Actuator Channel Safety)
**Estimated Effort:** 8-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 committed (all BHU-001 through BHU-016 done)

---

## Onboarding & Workflow

### Required Reading (IN ORDER)

1. **BATCH-01 Review:** `.dev/btree-hsm-unif/reviews/BATCH-01-REVIEW.md` — what was done in BATCH-01
2. **Task Detail:** `.dev/btree-hsm-unif/TASK-DETAIL.md` — sections BHU-011, BHU-012, BHU-013, BHU-014
3. **Design Document:** `.dev/btree-hsm-unif/DESIGN.md` — Phase 4 (§4) and Phase 5 (§5)

### Key source files you will touch

| File | Task |
|------|------|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/SharedAiAttributes.cs` (NEW) | BHU-011 |
| `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs` | BHU-012 |
| `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` | BHU-013 |
| `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs` | BHU-014 (BTree side) |
| `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` | BHU-014 (HSM side) |
| `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmGraphValidator.cs` | BHU-014 (validator) |

### Key files to READ before editing

Before touching any SourceGen files, study these existing source generators closely:
- `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs` — existing BTree generator
- `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` — existing HSM generator (already has `ClearAll()` from BHU-002)
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs` — find `HsmKernelBridge` struct here (field is `WorldHandle`, NOT `RepoHandle`)
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmGraphValidator.cs` — existing validator for BHU-014

### Test projects

- `dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj`
- `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj`
- `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`

### Report Submission

Submit your report to: `.dev/btree-hsm-unif/reports/BATCH-02-REPORT.md`

---

## MANDATORY WORKFLOW

Work tasks in order: BHU-011 → BHU-012 → BHU-013 → BHU-014. After each:
1. Build the relevant project(s)
2. Run the relevant test project(s)
3. Fix all errors before moving on

**Do NOT stop to ask permission.** If a build fails, fix it. If a test fails, find the root cause and fix it. Only write the report when ALL tests pass and the solution builds clean.

---

## Context

This batch adds the cross-paradigm "Shared AI Node" system, which lets a single C# method be callable as both a BTree condition/action AND an HSM guard/action through generated adapters with a unified compound-key naming scheme. It also adds actuator-channel safety wrappers that automatically clean up channels when actions fail or states exit.

After this batch:
- `[SharedAiCondition]` and `[SharedAiAction]` methods get adapters in both paradigms.
- Both generators use identical FNV-1a hash on the compound key `"MethodName@offset"`.
- `[WritesChannel]` methods get failure-cleanup wrappers (BTree) and exit-cleanup thunks (HSM).
- `HsmGraphValidator` enforces exit cleanup presence at compile time.

---

## Tasks

### BHU-011 — `SharedAiAttributes.cs` in `Fbt.Kernel`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-011".

New file: `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/SharedAiAttributes.cs`
Namespace: `Fbt.Kernel`

Create three attribute classes and one enum as specified in TASK-DETAIL.md BHU-011:
- `SharedAiConditionAttribute(int blackboardOffset)` — `AttributeTargets.Method`
- `SharedAiActionAttribute(int blackboardOffset)` — `AttributeTargets.Method`
- `WritesChannelAttribute(ChannelKind channel)` — `AttributeTargets.Method`, `AllowMultiple = true`
- `enum ChannelKind { Locomotion, Weapon, Interaction }`

**Important:** `ChannelKind` must live in `Fbt.Kernel` namespace (not `Fdp.Toolkits`), because both `Fbt.SourceGen` and `Fhsm.SourceGen` will reference it by fully qualified name when scanning attributes.

**Tests required:**
- `dotnet build FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Fbt.Kernel.csproj` — zero errors.
- Simple attribute construction tests: `new SharedAiConditionAttribute(offset: 16).BlackboardOffset == 16`, etc.

---

### BHU-012 — Extend `Fbt.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-012". Read all 8 success conditions carefully before coding.

File: `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs`

**What to implement:**

Extend `BTreeActionGenerator` (Roslyn `IIncrementalGenerator`) to also scan for `[SharedAiCondition]` and `[SharedAiAction]` attributes (by full name: `"Fbt.Kernel.SharedAiConditionAttribute"` and `"Fbt.Kernel.SharedAiActionAttribute"`).

For each such method:
1. Resolve the struct type `T` and field name from attribute arguments via Roslyn semantic model.
2. Compute `offset = offsetof(T, fieldName)` by:
   - For sequential layout: sum sizes of all preceding fields (use `Unsafe.SizeOf<FieldType>()` emitted inline in the thunk, or use `Marshal.OffsetOf` in the generator itself via reflection on the `ITypeSymbol`).
   - For explicit layout: read `[FieldOffset(N)]` from the field's attributes.
3. Emit a **compound key** `"{MethodName}@{offset}"` (e.g. `"IsEnemyNear@16"`).
4. Emit an adapter registered under that compound key via `actionRegistry.RegisterCondition(...)` (for condition) or `actionRegistry.RegisterAction(...)` (for action).

The adapter signature is:
```csharp
static (ref BrainBlackboard bb, BTreeContext ctx) =>
{
    ref var field = ref Unsafe.As<byte, TField>(
        ref Unsafe.AddByteOffset(ref bb.Memory[0], (IntPtr)offset));
    return {MethodName}(ref field, ctx.Entity, (EntityRepository)ctx.World);
}
```

Where `TField` is the type of the referenced struct field.

**Diagnostic rules** (emit via `context.ReportDiagnostic`):
- Non-static method with `[SharedAiCondition]` → `DiagnosticDescriptor` ID `BHU_002`, warning, skip.
- `typeof(T)` argument references a type with no matching field named `fieldName` → ID `BHU_003`, error, skip.
- `ref TValue` parameter type mismatches the resolved field type → ID `BHU_001`, error, skip.

**Hash function** (FNV-1a, must be identical to what `Fhsm.SourceGen` uses):
```csharp
private static ushort ComputeHash(string name)
{
    uint hash = 2166136261;
    foreach (char c in name) { hash ^= c; hash *= 16777619; }
    return (ushort)(hash & 0xFFFF);
}
```

**Tests required** (add to `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/`):
- Happy path: `[SharedAiCondition]` on a static method; verify generated key contains `@offset`, adapter callable, returns correct boolean.
- Negative: non-static method → diagnostic `BHU_002` emitted, no adapter generated.
- Negative: unknown field name → diagnostic `BHU_003` emitted, no adapter generated.
- Offset correctness: sequential struct field at offset 4 → key `"MethodName@4"`. Explicit-layout field at offset 12 → key `"MethodName@12"`.
- Hash cross-check: verify `ComputeHash("IsEnemyNear@16")` in `Fbt.SourceGen` == expected value (pre-compute and hard-assert).

---

### BHU-013 — Extend `Fhsm.SourceGen` for `[SharedAiCondition]` / `[SharedAiAction]`

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-013". Read all 9 success conditions.

File: `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`

**What to implement:**

Mirror the BHU-012 scan, but emit HSM guard/action thunks instead of BTree adapters.

For each `[SharedAiCondition(typeof(T), "Field")]` method:
1. Resolve offset same way as BHU-012.
2. Emit a private static unsafe guard thunk:
```csharp
private static unsafe bool Guard_{MethodName}_At{offset}(
    void* instancePtr, void* contextPtr, ushort eventId)
{
    // CONSTRAINT: Do NOT add or remove ECS components from this thunk.
    // Shared action thunks write directly to EntityRepository, bypassing FastHSM's
    // deferred HsmCommandWriter. Structural ECS mutations during chunk iteration
    // corrupt the chunk arrays. Only read/write fields of existing components.
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var bb = ref repo.GetComponentRW<BrainBlackboard>(bridge->Self);
    ref var field = ref Unsafe.As<byte, {TField}>(
        ref Unsafe.AddByteOffset(ref bb.Memory[0], (IntPtr){offset}));
    return {MethodName}(ref field, bridge->Self, repo);
}
```
3. In `HsmActionRegistrar.RegisterAll()`, add:
```csharp
HsmActionDispatcher.RegisterGuard(
    ComputeHash("{MethodName}@{offset}"),
    (IntPtr)(delegate* <void*, void*, ushort, bool>)&Guard_{MethodName}_At{offset});
```

**CRITICAL:** The bridge field accessing the world is `bridge->WorldHandle` (type `IntPtr`, a GCHandle). Do NOT write `bridge->RepoHandle` — that field does NOT exist. Verify against `HsmKernelBridge` in `HsmTickSystem.cs`.

**Hash function:** Use the exact same FNV-1a `ComputeHash` already present in `HsmActionGenerator.cs` — the same compound key `"{MethodName}@{offset}"`.

**Diagnostic rules:** Emit the same `BHU_001`, `BHU_002`, `BHU_003` diagnostic IDs as BHU-012 for the same error conditions. Consistency between generators is required.

**ECS mutation constraint comment:** Every generated thunk body must contain the comment verbatim:
```
// CONSTRAINT: Do NOT add or remove ECS components from this thunk.
// Shared action thunks write directly to EntityRepository, bypassing FastHSM's
// deferred HsmCommandWriter. Structural ECS mutations during chunk iteration
// corrupt the chunk arrays. Only read/write fields of existing components.
```

**Tests required** (add to `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/`):
- Happy path: `[SharedAiCondition]` annotated method; generated `HsmActionRegistrar.g.cs` contains `Guard_MethodName_At{N}`.
- Structural assertion: generated thunk uses `bridge->WorldHandle` (search generated source, not just assert compilation).
- Runtime call-through: invoke via `HsmActionDispatcher.EvaluateGuard(hash, ...)` with a real `HsmKernelBridge` and live `EntityRepository`. Assert it calls the correct method.
- Hash cross-check: `ComputeHash("SomeName@16")` in `Fhsm.SourceGen` must equal `ComputeHash("SomeName@16")` in `Fbt.SourceGen` — hard-assert the exact `ushort` value.
- Same three diagnostic negative tests as BHU-012.

---

### BHU-014 — Channel safety wrappers + `HsmGraphValidator` enforcement

Full spec: `.dev/btree-hsm-unif/TASK-DETAIL.md` — section "BHU-014".

Two files for generators, one for the compiler validator.

**BTree side (`BTreeActionGenerator.cs`):**

For each `[BTreeAction]` or `[SharedAiAction]` method that ALSO has `[WritesChannel(ChannelKind.Locomotion)]` (or Weapon/Interaction), wrap the generated delegate:

```csharp
actionRegistry.RegisterAction(
    "{key}",
    static (ref BrainBlackboard bb, BTreeContext ctx) =>
    {
        var status = {OriginalMethod}(ref bb, ctx);
        if (status == NodeStatus.Failure)
        {
            ref var ch = ref ctx.Entity.Get<LocomotionChannel>();
            ch.ActiveAction     = 0;
            ch.ActionInstanceId = unchecked((ushort)(ch.ActionInstanceId + 1));
        }
        return status;
    });
```

Apply analogously for `WeaponChannel` and `InteractionChannel`. Verify the exact channel component names in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/` before emitting.

**HSM side (`HsmActionGenerator.cs`):**

For each `[HsmAction]` or `[SharedAiAction]` method that ALSO has `[WritesChannel(...)]`, emit an exit-cleanup thunk:

```csharp
private static unsafe void ExitCleanup_{MethodName}(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;
    ref var ch = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
    ch.ActiveAction     = 0;
    ch.ActionInstanceId = unchecked((ushort)(ch.ActionInstanceId + 1));
}
```

Register it in `RegisterAll()` under key `"ExitCleanup_{MethodName}"`.

Also emit into `HsmActionRegistrar.g.cs`:
```csharp
public static readonly IReadOnlyDictionary<string, string> RequiredExitCleanups =
    new Dictionary<string, string> { ["MoveTo"] = "ExitCleanup_MoveTo", /* ... */ };
```

**Compiler validator (`HsmGraphValidator.cs`):**

Add a channel-safety validation pass called from `HsmCompiler.Compile()` after the graph is built. Accepts `RequiredExitCleanups`. For each state that uses a channel-writing action as `OnEntry` or `Activity`, verify that the corresponding `ExitCleanup_*` is set as `OnExit`. Throw a descriptive `HsmValidationException` (or existing exception type) naming the offending state and the missing cleanup key if validation fails.

**Tests required:**
- BTree: `[WritesChannel(Locomotion)]` action returns `NodeStatus.Failure` → `LocomotionChannel.ActiveAction == 0` and `ActionInstanceId` incremented.
- BTree: same action returns `NodeStatus.Running` → channel is unchanged.
- HSM: `ExitCleanup_MoveTo` thunk invoked via `HsmActionDispatcher` → `LocomotionChannel.ActiveAction == 0` and `ActionInstanceId` incremented.
- Validator: state with channel-writing `OnEntry` but no matching `OnExit` → `HsmCompiler.Compile()` throws with a message naming the state.
- Validator: compliant state (has both `OnEntry` and correct `OnExit`) → compiles without error.

---

## Quality Standards

- Tests must verify ACTUAL runtime behavior: flag values, returned NodeStatus, channel field values — not just that generated source contains a substring.
- For source generator tests where you check generated code content, ALSO run the generated code to verify it produces the right runtime result.
- Every generated thunk accessing `bridge->WorldHandle` must be verified (string match in generated source + runtime invocation).

---

## Success Criteria

- [ ] BHU-011: `Fbt.Kernel` builds; three attribute classes + enum present; attribute construction tests pass
- [ ] BHU-012: `Fbt.SourceGen` extended; compound-key adapters generated; offset resolution correct; three diagnostic IDs; hash cross-check passes
- [ ] BHU-013: `Fhsm.SourceGen` extended; HSM guard/action thunks generated; `bridge->WorldHandle` used; hash equals BHU-012; ECS constraint comment present
- [ ] BHU-014: BTree failure wrappers; HSM exit-cleanup thunks + `RequiredExitCleanups` dict; validator enforces exit cleanup; all runtime tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` — zero `error CS` lines

---

## Reference Materials

- **Task Specs:** `.dev/btree-hsm-unif/TASK-DETAIL.md` — sections BHU-011 through BHU-014
- **Design:** `.dev/btree-hsm-unif/DESIGN.md` — Phase 4 and Phase 5
- **Existing BTree generator:** `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs`
- **Existing HSM generator:** `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`
- **HsmKernelBridge (WorldHandle field):** `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`
- **HsmGraphValidator:** `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmGraphValidator.cs`
- **Channel components:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/` (find LocomotionChannel, WeaponChannel)
- **Fhsm.Tests:** `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/`
- **Fbt.Tests:** `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/`
