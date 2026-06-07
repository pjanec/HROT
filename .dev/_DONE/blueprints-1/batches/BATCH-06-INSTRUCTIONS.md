# BATCH-06: Phase 2 Runtime -- Foundation Types (RT-001, RT-002, RT-003)

**Batch Number:** BATCH-06
**Tasks:** TASK-RT-001, TASK-RT-002, TASK-RT-003
**Phase:** Phase 2 -- Runtime (Part 1 of 3)
**Estimated Effort:** 18-24 hours
**Priority:** HIGH
**Dependencies:** BATCH-05 committed (Phase 1 Test Harness complete)

---

## Onboarding & Workflow

### Your Role

You are the **Developer**. Your role description is in `.github\skills\developer\SKILL.md`.
Read it before starting.

### Required Reading (IN ORDER)

1. **TASK-DETAIL.md:** `.dev/blueprints-1/TASK-DETAIL.md`
   Read the following sections in full:
   - **TASK-RT-001** -- BlueprintRegistry
   - **TASK-RT-002** -- BlueprintDefinition, Delegate Types, BlueprintLatentCursor
   - **TASK-RT-003** -- BlueprintBlackboard Components and Slot-Table Types

2. **Runtime DD:** `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design.md`
   Read §1 (architecture), §2 (registry), §3 (definition + delegates), §4 (Blackboard layout).

3. **Runtime DD Inline Patches:** `.dev/blueprints-1/Blueprint_Subsystem_Runtime_Detailed_Design_InlinePatches.md`
   Read **Hot-path Correction 1** (materialized WorldSingletonList) and
   **Q-12.4** (world-singleton lazy init) -- both affect RT-001's `GetAllWorldSingletons` shape.

4. **DEBT-TRACKER:** `.dev/blueprints-1/DEBT-TRACKER.md`
   Note all OPEN items.

### Build & Test Commands

```powershell
# From repo root:
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

All 90 existing tests must continue to pass (plus new tests added in this batch).

### Report Submission

Submit report to: `.dev/blueprints-1/reports/BATCH-06-REPORT.md`
Questions: `.dev/blueprints-1/questions/BATCH-06-QUESTIONS.md`

---

## Test-Driven Task Progression (Mandatory Workflow)

For every Success Condition (SC) in each task:

1. **Write the test first** (it must fail or be skipped before you write production code).
2. **Write the minimum production code** to make the test pass.
3. **Verify** `dotnet test` shows the test passes.
4. **Move to the next SC.**

---

## TASK-RT-001 -- BlueprintRegistry (Full Implementation)

**Reference:** TASK-DETAIL.md section TASK-RT-001 (read it in full).

### Existing stub

`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs` contains a partial stub.
Read it before modifying. The stub already has `BeginStaging`, `CommitStaging`, `TryGetById`,
`TryGetByName`, `GetAll`, and basic `Snapshot` -- but these are incomplete per the full spec.

Also read `BlueprintRegistryStaging` if it exists separately.

### Key gaps to fill

1. **`RegisterDirect` collision guard:** The `CommitStaging` path (or `BlueprintRegistryStaging.Add`)
   must throw `InvalidOperationException` when a duplicate `blueprintId` is added. Include both
   asset names in the message.

2. **`RegisterWorldSingleton` validation:** Must throw if `blueprintId` is not already in `ById`
   (the Blueprint must be registered before being marked as a world-singleton).

3. **`WorldSingletonList` pre-materialization:** Per Hot-path Correction 1 of Runtime DD Inline
   Patches, `GetAllWorldSingletons()` must return an `IReadOnlyList<(int blueprintId, BlackboardTier)>`
   that is **pre-built inside `CommitStaging`** (not lazily on each call). The `Snapshot` must
   carry a `WorldSingletonList` field of type `IReadOnlyList<(int, BlackboardTier)>`.

4. **Blueprint ID hash:** The world-singleton registry key is `int` (not `Guid`). Read the
   Runtime DD §2.2 to understand how `int blueprintId` relates to `Guid assetId`. Look for
   `BlueprintIdHash` in the codebase -- if it doesn't exist, you must create a
   `public static class BlueprintIdHash` with `static int Compute(Guid assetId)` using the
   first 4 bytes of the GUID as an `int` (or as specified in the Runtime DD).

5. **`GetAll()` return type:** Must be `IReadOnlyCollection<BlueprintDefinition>`.

6. **`OnRegistryChanged`** must fire after every `CommitStaging`.

### Files to modify/create

```
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintIdHash.cs   (create if not present)
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintRegistryTests.cs  (create)
```

### Success Conditions (from TASK-DETAIL.md TASK-RT-001 SC1-SC7)

- SC1: Register 3 Blueprints. Assert TryGetById/TryGetByName each return true. GetAll().Count==3.
- SC2: CommitStaging with 2 Instance Blueprints -- TryGetById returns true; non-existent returns false.
- SC3: GetAllWorldSingletons() returns IReadOnlyList. Count==1 after CommitStaging with 1 world-singleton. Second call returns same list reference.
- SC4: Duplicate blueprintId throws InvalidOperationException with both asset names.
- SC5: RegisterWorldSingleton for unknown blueprintId throws.
- SC6: Two consecutive CommitStaging calls -- TryGetById returns ONLY second staging entries.
- SC7: OnRegistryChanged fires exactly once per CommitStaging, even if staging is empty.

---

## TASK-RT-002 -- BlueprintDefinition, Delegate Types, and BlueprintLatentCursor

**Reference:** TASK-DETAIL.md section TASK-RT-002 (read it in full).

### Existing files

- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs` -- minimal stub, not a record.
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs` -- has wrong fields (uses `Guid GraphId`; design requires `uint ResumeAt` + `float WaitUntilTime`).
- `FDP/Toolkits/Fdp.Toolkits/Blueprints/Attributes/BlueprintRegistrarAttribute.cs` -- may already exist.

### What to implement

**`BlueprintDefinition` (replace stub):**
- Must be `sealed record` (not class).
- Fields per Runtime DD §3.2: `Name (string)`, `Kind (BlueprintDispatchKind)`, `StructureHash (ulong)`,
  `StateSize (int)`, `InitDefault (InitDefaultDelegate?)`, `Tick (TickDelegate?)`,
  `EventHandlers (IReadOnlyDictionary<string, EventHandlerDelegate>)`, `StateClrType (Type?)`,
  `StateFields (IReadOnlyDictionary<string, BlueprintFieldDescriptor>)`.
- Also keep backward-compat: `AssetId (Guid)` may be needed by the fixture (check usages first).

**`BlueprintFieldDescriptor` (create):**
- `sealed record BlueprintFieldDescriptor(string Name, Type ClrType, int OffsetBytes, int SizeBytes, string CategoryOrEmpty)`.

**Delegate types (create new file or add to `BlueprintDefinition.cs`):**
Per Runtime DD §3.3:
- `InitDefaultDelegate` - see design for exact signature (takes `Span<byte>`)
- `TickDelegate` - see design for exact signature (includes `uint instanceVersion`)
- `EventHandlerDelegate` - see design for exact signature (includes `float deltaTime`)

Read Runtime DD §3.3 for the exact signatures.

**`BlueprintLatentCursor` (fix):**
- Replace `Guid GraphId` with `uint ResumeAt` and `float WaitUntilTime`.
- `[StructLayout(LayoutKind.Sequential, Size = 16)]` (8 bytes used + 8 reserved padding).
- Must remain `unmanaged`.

**`BlueprintRegistrarAttribute` (verify/fix):**
- `[AttributeUsage(AttributeTargets.Class, Inherited = false)] public sealed class BlueprintRegistrarAttribute : Attribute`.
- If already exists and correct, no change needed.

### WARNING: Check Existing Usages

`BlueprintDefinition` is referenced by `BlueprintRegistry`, `BlueprintTestFixture`,
`BlueprintBlackboardPartitions` stub, and potentially tests. Before changing its structure
to a `sealed record`, read all usages and update them. The key change: the stub has
`AssetId (Guid)` and `StateFields (IReadOnlyDictionary<string, int>)` -- the full spec
changes `StateFields` to `IReadOnlyDictionary<string, BlueprintFieldDescriptor>` and adds
many more fields.

Also check `InitDefault(byte*, int)` in the stub vs `InitDefaultDelegate` (span-based) in spec.

### Files to modify/create

```
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDefinition.cs     -- replace stub with sealed record
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs   -- fix fields
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintDelegates.cs      -- new: InitDefaultDelegate etc.
FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintFieldDescriptor.cs -- new
FDP/Toolkits/Fdp.Toolkits/Blueprints/Attributes/BlueprintRegistrarAttribute.cs -- verify
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintDefinitionTests.cs  -- create
```

### Success Conditions (from TASK-DETAIL.md TASK-RT-002)

- SC1: `typeof(BlueprintDefinition).IsSealed && typeof(BlueprintDefinition).IsValueType == false`
  (sealed record, not struct). Instance has all required fields as init-only.
- SC2: `sizeof(BlueprintLatentCursor) == 16` (checked via `Unsafe.SizeOf`).
- SC3: `BlueprintLatentCursor` is `unmanaged` -- verified by constraining with `where T : unmanaged`.
- SC4: `InitDefaultDelegate`, `TickDelegate`, `EventHandlerDelegate` can each be declared
  and assigned a lambda with matching signature (no compile error).
- SC5: `BlueprintRegistrarAttribute` applies to a class without error; applying to a struct throws
  `AttributeUsageException` at runtime (check the `AttributeTargets` enforcement).
- SC6: `new BlueprintDefinition { Name = "X", Kind = BlueprintDispatchKind.Library, ... }` compiles.
- SC7: `dotnet build` succeeds with zero errors.

---

## TASK-RT-003 -- BlueprintBlackboard Components and Slot-Table Types

**Reference:** TASK-DETAIL.md section TASK-RT-003 (read it in full).

### Existing files

The three component files already exist at:
```
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard4096.cs
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard16384.cs
```

They have the correct size and ComponentId attributes but are missing the layout constants
(`TotalSize`, `MaxSlots`, `SlotTableSize`, `PayloadStart`, `PayloadSize`) and the field
`Memory` is called `Data` (check the design to see which name is required).

The `Partitioning/` directory exists but is missing the new struct files:
```
FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/
```

### What to implement

**Update the three Blackboard components:**
Each must have these `public const int` fields:
- `TotalSize` (1024 / 4096 / 16384)
- `HeaderSize = 32`
- `MaxSlots` (4 / 8 / 16)
- `SlotTableSize` (= MaxSlots * 16, i.e., 64 / 128 / 256)
- `PayloadStart` (= HeaderSize + SlotTableSize, i.e., 96 / 160 / 288)
- `PayloadSize` (= TotalSize - PayloadStart, i.e., 928 / 3936 / 16096)

The memory field must be named `Memory` (check design) -- if the existing files name it `Data`,
rename it. Check all usages in `BlueprintBlackboardPartitions.cs` and `BlueprintTestFixture.cs`
before renaming.

**Create `BlueprintBlackboardHeader.cs`:**
```csharp
[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct BlueprintBlackboardHeader
{
    public uint  MagicAndVersion;
    public byte  SlotCount;
    public byte  MaxSlots;
    public ushort FreeListHead;
    public ushort PayloadStart;
    public ushort PayloadSize;
    public ushort PayloadFree;
    public ushort PayloadHighWater;
    public ulong  Reserved;
}
// Magic constant: 0x42504257u
```

**Create `BlueprintSlotEntry.cs`:**
```csharp
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintSlotEntry
{
    public int    BlueprintId;
    public uint   InstanceVersion;
    public ushort PayloadOffset;
    public ushort PayloadSize;
    public ulong  StructureHash;
}
public const int SlotEntrySize = 16;
```
Note: `SlotEntrySize` should be a constant on `BlueprintBlackboardPartitions` (per spec).
Create it there or in the same file -- read the spec carefully.

**Create `BlueprintFreeBlockHeader.cs`:**
```csharp
[StructLayout(LayoutKind.Sequential, Size = 4)]
public struct BlueprintFreeBlockHeader
{
    public ushort NextFreeOffset;
    public ushort Size;
}
```

All these structs go in the `Fdp.Toolkit.Blueprints` or `Fdp.Toolkit.Blueprints.Partitioning`
namespace (check the Runtime DD §1.3 module layout for exact placement).

**Check `GlobalComponentIds`:**
The ComponentId constants for the three Blackboard tiers must be registered in `GlobalComponentIds`.
Read `GlobalComponentIds.cs` to check if they already exist. If not, add them. If they exist
but are in a different file, do not duplicate them.

### Files to modify/create

```
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard1024.cs  -- add consts
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard4096.cs  -- add consts
FDP/Toolkits/Fdp.Toolkits/Blueprints/Components/BlueprintBlackboard16384.cs -- add consts
FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardHeader.cs -- create
FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintSlotEntry.cs        -- create
FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintFreeBlockHeader.cs  -- create
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlackboardLayoutTests.cs -- create
```

### Success Conditions (from TASK-DETAIL.md TASK-RT-003 SC1-SC7)

- SC1: `Unsafe.SizeOf<BlueprintBlackboard1024>() == 1024`, same for 4096 and 16384.
- SC2: `Unsafe.SizeOf<BlueprintBlackboardHeader>() == 32`, `BlueprintSlotEntry == 16`, `BlueprintFreeBlockHeader == 4`.
- SC3: Constants: `BlueprintBlackboard1024.PayloadStart == 96`, `PayloadSize == 928`. Same for other tiers.
- SC4: `MaxSlots * SlotEntrySize == SlotTableSize` for all three tiers.
- SC5: `typeof(BlueprintBlackboard1024).GetCustomAttribute<ComponentIdAttribute>()?.Id == GlobalComponentIds.BlueprintBlackboard1024`.
- SC6: `default(BlueprintBlackboard1024)` is zeroed (first 4 bytes == 0 -- magic not set).
- SC7: `dotnet build` succeeds.

---

## Developer Insights (Questions to Answer in Report)

1. What is the `int blueprintId` used in `GetAllWorldSingletons()`? How does it relate to `Guid assetId`? Does `BlueprintIdHash` already exist?
2. Did the `BlueprintDefinition` change to a `sealed record` require significant caller updates? Which callers needed changes?
3. Did the `BlueprintLatentCursor` field rename (`Guid GraphId` -> `uint ResumeAt + float WaitUntilTime`) affect any existing code?
4. Is the `Memory` vs `Data` field naming on Blackboard components consistent across the codebase? What was the impact?
5. Were there any build or test failures during development? How were they resolved?
6. What design decisions did you make that were not explicitly specified?

---

## Report Format

Submit `.dev/blueprints-1/reports/BATCH-06-REPORT.md`:

```
# BATCH-06-REPORT

## Tasks Completed
[table]

## 1. TASK-RT-001 -- BlueprintRegistry
[changes, tests added, deviations]

## 2. TASK-RT-002 -- BlueprintDefinition + Delegates + Cursor
[changes, callers updated, tests added]

## 3. TASK-RT-003 -- Blackboard Layout Types
[changes, new structs, tests added]

## 4. Build Status
[dotnet build output]

## 5. Test Summary
[pass/skip/fail counts, test breakdown table]

## 6. Developer Insights
[answers to 6 questions above]

## 7. Deviations from Instructions
[any deviation with reason]
```
