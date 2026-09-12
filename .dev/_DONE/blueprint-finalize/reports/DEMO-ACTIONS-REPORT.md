# DEMO-ACTIONS Batch — Report

## Attribute + Canonical Signature

**Attribute:** `[SharedAiAction(Type dtoType, string fieldName)]` from namespace `Fbt.Kernel`  
(assembly `Fbt.Kernel.csproj`, `AllowMultiple = true`).

**Canonical method signature** (production context — `Entity`/`EntityRepository` from `Fdp.Core`):

```csharp
[SharedAiAction(typeof(ParentDto), nameof(ParentDto.Field))]
public static NodeStatus MethodName(
    ref TFieldType dto,    // first ref param — field type, NOT parent DTO type
    Entity         self,
    EntityRepository world)
    => NodeStatus.Success;
```

The key subtlety: `ActionSchemaExporter.ExtractFirstRefParamType` strips the `&` from the first
`ByRef` parameter to get the `DtoType` used in `ActionSchemaEntry`. This means the `ref` param
type (not the attribute's `DtoType`) is what becomes `ParamsTypeFqn` in the catalog, and what
`NodePinSchema.ReflectDataMembers` reflects to produce data-IN pins.

The Roslyn source generator (`HsmActionGenerator`) validates at compile time that the `ref`
parameter type matches the declared field type in the parent DTO. Using test-fixture `int`/`int`
params (as in `SharedAiTestFixtures`) works only in assemblies outside the production context.
Production code in `Fdp.Toolkits` must use `Entity` and `EntityRepository`.

---

## Demo Implementation

### Location and Discovery

File: `FDP/Toolkits/Fdp.Toolkits/Behavior/Demo/DemoEnumAction.cs`

**Why it's discovered:** `ActionSchemaExporter.Rebuild()` calls
`AppDomain.CurrentDomain.GetAssemblies()` and reflects every type in every loaded assembly.
`Fdp.Toolkits` is loaded in the editor process via the dependency chain
`Hrot.AI.Behaviors` → `Fdp.Toolkits`, and in the test process via
`Hrot.Blueprints.Tests` → `Hrot.Blueprints.Editor` → `Fdp.Toolkits`.

No new project references were needed.

### New Types Added (all in namespace `Fdp.Toolkit.Behavior.Demo`)

#### `DemoSharedActionParams` (blittable params struct — the `ref` param type / catalog DTO)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct DemoSharedActionParams
{
    public float     AlertRadius;   // plain float pin
    public DemoStance PostureHint;  // enum field → TypeId stamped "global::" per AN6
    public int       MaxUnits;      // plain int pin
}
```

Layout: `float(4) + int/DemoStance(4) + int(4) = 12 bytes` — within the 32-byte limit.
Reuses the existing `DemoStance` enum (`Standing/Crouching/Prone`) to exercise AN6 enum pins
on a non-channel action.

#### `DemoBlackboardSlot` (parent DTO container — used as attribute argument only)

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct DemoBlackboardSlot
{
    public DemoSharedActionParams Params;  // field type must match ref param type
}
```

Required because the `[SharedAiAction]` attribute takes a parent DTO type + field name, and
the Roslyn generator validates that the field type matches the method's `ref` param type.
`DemoBlackboardSlot` is never used at runtime.

#### `DemoSharedActions.AlertNearbyUnits` (the demo action method)

```csharp
[SharedAiAction(typeof(DemoBlackboardSlot), nameof(DemoBlackboardSlot.Params))]
public static NodeStatus AlertNearbyUnits(
    ref DemoSharedActionParams p,
    Entity self,
    EntityRepository world)
{
    _ = p; _ = self; _ = world;
    return NodeStatus.Success;
}
```

**No-op body** — exists solely for palette + pin projection testing.

---

## Catalog / Palette Flow

| Step | What happens |
|------|-------------|
| `ActionSchemaExporter.Rebuild()` | Finds `AlertNearbyUnits` via `[SharedAiAction]`; extracts `DtoType = typeof(DemoSharedActionParams)` from first `ref` param; sets `Hosting = BTree \| Hsm \| Shared` |
| `BehaviorActionCatalog.Rebuild()` | `MapHosting(Shared)` → `BehaviorActionHosts.Blueprint`; adds entry with `Id = "Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits"`, `ParamsTypeFqn = "Fdp.Toolkit.Behavior.Demo.DemoSharedActionParams"` |
| `BlueprintNodePaletteEntries.NonChannelActionEntries()` | Emits descriptor with `Kind = "Action:Fdp.Toolkit.Behavior.Demo.DemoSharedActions.AlertNearbyUnits"`, `Category = "Action/DemoSharedActions"` |
| `NodePinSchema.GetCanonicalPins()` for a node with `ActionFqn` set | Calls `NonChannelActionPins()` → `AppendParamPins()` → `ReflectDataMembers(typeof(DemoSharedActionParams))` → 3 data-IN pins: `AlertRadius:float`, `PostureHint:global::Fdp.Toolkit.Behavior.Demo.DemoStance`, `MaxUnits:int` |

---

## Verification (Headless Tests)

### New test file
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN8b_DemoSharedActionTests.cs`

All 10 tests in `AN8b_DemoSharedActionTests` pass:

| Test | Assertion |
|------|-----------|
| `ActionSchemaExporter_Rebuild_FindsDemoSharedAction_AN8b` | Demo FQN appears in `exporter.All` |
| `ActionSchemaExporter_DemoEntry_HasSharedHosting_AN8b` | Hosting has `BTree \| Hsm \| Shared` flags |
| `ActionSchemaExporter_DemoEntry_DtoTypeIsDemoSharedActionParams_AN8b` | `DtoType == typeof(DemoSharedActionParams)` |
| `BehaviorActionCatalog_WithRealExporter_ContainsDemoAction_AsBlueprint_AN8b` | Entry found with `BehaviorActionHosts.Blueprint` |
| `BehaviorActionCatalog_DemoEntry_ParamsTypeFqn_IsDemoSharedActionParams_AN8b` | `ParamsTypeFqn == typeof(DemoSharedActionParams).FullName` |
| `PaletteRegistry_WithRealCatalog_ContainsDemoActionKind_AN8b` | Registry slot `"Action:{FQN}"` present |
| `PaletteRegistry_DemoDescriptor_CreateInstance_BakesActionFqn_AN8b` | Node has `ActionFqn` set, `ChannelType`/`ActionId` empty |
| `NodePinSchema_DemoSharedNode_ProjectsThreeDataInPins_AN8b` | 3 data-IN pins: `AlertRadius`, `PostureHint`, `MaxUnits` |
| `NodePinSchema_DemoSharedNode_PostureHintPin_HasGlobalColonColonTypeId_AN8b` | `PostureHint` TypeId = `"global::Fdp.Toolkit.Behavior.Demo.DemoStance"` |
| `NodePinSchema_DemoSharedNode_HasExecInAndExecOut_AN8b` | Exec In + Out present |

### Build results
- `dotnet build IOS-IG-SimHost.sln`: **0 CS errors, 18 pre-existing warnings**
- `Fdp.Toolkits.csproj`: 0 errors (BHU_001 analyzer + HSM generator pass with Entity/EntityRepository signature)
- `Hrot.Blueprints.Tests.csproj`: 0 errors

### Test results
- `Hrot.Blueprints.Tests`: **1603 passed, 4 failed (pre-existing), 8 skipped**
  - Pre-existing failures (not caused by this batch):
    - `Library_EmitMatchesGoldenSource` (CRLF flake)
    - `LibraryMath_GeneratedSource_Snapshot` (CRLF flake)
    - `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` (ScoreCrossed pre-existing)
    - `TickFrame_1000Frames_AllocatesZeroBytes` (AllocatesZeroBytes pre-existing)
- `Hrot.Editor.AiShared.Tests`: **856 passed, 0 failed**
- `AN8b_DemoSharedActionTests` (10 new tests): **all passed**

---

## AiPrimitive Demo Blueprint

**Not added.** A `AiPrimitive(BlueprintCall)` demo blueprint asset was not included because:
1. The AN8 commit `c93eccf0` currently emits a compile-time `#error` for
   `SharedAiAction` lowering (`AiPrimitive(BlueprintCall)` path).
2. Adding a `.bp.json` containing the demo action to `Hrot.AI.Behaviors` would break
   the MSBuild generator until AN8b implements the lowering.
3. The palette + pin projection objectives are fully satisfied by the headless tests
   using `NodePinSchema.GetCanonicalPins` directly (no blueprint compilation required).

AN8b should add the blueprint test asset once the lowering path is implemented.

---

## AN8b Compile Note

**DO NOT** add `DemoSharedActions.AlertNearbyUnits` to any committed `.bp.json` blueprint until
AN8b implements the non-channel `AiPrimitive(BlueprintCall)` lowering in Stage5. The method
will cause a `#error` in the MSBuild generator. The demo is safe for reflection-only discovery
(palette + pins) as demonstrated by all 10 passing headless tests.

---

## Files Changed

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Demo/DemoEnumAction.cs` | Added `DemoSharedActionParams`, `DemoBlackboardSlot`, `DemoSharedActions.AlertNearbyUnits` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN8b_DemoSharedActionTests.cs` | **New** — 10 headless verification tests |
| `.dev/_DONE/blueprint-finalize/reports/DEMO-ACTIONS-REPORT.md` | This report |
