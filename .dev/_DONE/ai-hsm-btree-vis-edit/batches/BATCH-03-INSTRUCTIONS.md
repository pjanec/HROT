# BATCH-03: Phase 1.5b Core — Bin Packer, DTO Emitter, Round-Trip Tests + DEBT-01 Fix

**Batch Number:** BATCH-03
**Tasks:** DEBT-01 fix, TASK-BB-1b-04, TASK-BB-1b-01, TASK-BB-1b-06
**Phase:** Phase 1.5b — Editor-managed DTO emit (core infrastructure)
**Estimated Effort:** 12–16 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (kernel attrs), BATCH-02 (parser + classifier + schema exporter)

---

## Onboarding & Workflow

### Developer Instructions

This batch delivers three things:
1. **DEBT-01 fix** — add `DtoType` to `HsmActionAttribute`/`HsmGuardAttribute` so real HSM actions appear in the schema exporter
2. **Bin Packer** — compute inline byte offsets with C# alignment, enforce the 100-byte ceiling
3. **DTO Emitter** — emit `{AssetName}.Blackboard.cs` deterministically (editor-managed fields regenerated, read-only spans verbatim)
4. **Round-trip CI tests** — RT-1 (no-edit byte-identical) and RT-2 (single-edit confined diff)

No UI changes in this batch. Pure logic + tests in shared project.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/_DONE/ai-hsm-btree-vis-edit/ONBOARDING.md`
3. **Task Details:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — TASK-BB-1b-04, TASK-BB-1b-01, TASK-BB-1b-06
4. **Design:**
   - §2.1–2.2 (two categories, marker block)
   - §3.1–3.5, §3.7 (load/save lifecycle, field classification, round-trip guarantees)
   - §6.1–6.2 (bin-packing algorithm, alignment)
   - §6.6 (tail registers off-limits)
5. **Previous reviews:**
   - `.dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-01-REVIEW.md`
   - `.dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-02-REVIEW.md`
6. **Existing code to read before coding:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — base class you extend
   - `Hrot/Editor/Hrot.Editor.AiShared/Emit/UsingDirectiveSet.cs` — deterministic usings
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardSourceTextParser.cs` — from Batch 02
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardFieldClassifier.cs` — from Batch 02
   - `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` — BrainBlackboard tail layout (for bin-packer off-limits range)
   - `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` — for DEBT-01 fix

### Source Code Locations

- **DEBT-01 fix:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` and `HsmGuardAttribute.cs`; update `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs`
- **Bin packer:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs` (NEW)
- **DTO emitter:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` (NEW)
- **Tests:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/` (add new test files there)

### Report Submission

**When done, submit your report to:**
`.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/_DONE/ai-hsm-btree-vis-edit/questions/BATCH-03-QUESTIONS.md`

---

## Tasks

### DEBT-01 Fix — Add `DtoType` to `HsmActionAttribute` / `HsmGuardAttribute`

**Background:** (see DEBT-TRACKER.md DEBT-01). Real HSM action methods use `void*` parameters for unsafe interop. The schema exporter cannot infer `DtoType` from void pointers. The fix is an additive opt-in property.

**Changes in FastHSM submodule:**
- Add `Type? DtoType { get; set; }` property (default `null`) to `HsmActionAttribute` (`FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs`)
- Add the same property to `HsmGuardAttribute` (find it in the same folder)

**Changes in ActionSchemaExporter:**
- When a method's first parameter is `void*` (i.e., `ExtractFirstRefParamType` returns null), fall back to reading `DtoType` from the `HsmActionAttribute` or `HsmGuardAttribute` on the method
- If `DtoType` is non-null, use it as the entry's `DtoType` with `ActionHosting.Hsm` hosting

**Tests** (add to `ActionSchemaExporterTests.cs`):
- A fixture method decorated `[HsmAction(DtoType = typeof(MyDto))]` with void* params appears in `All` with correct DtoType and `ActionHosting.Hsm`
- A method with `[HsmAction]` but null `DtoType` and void* params still produces no entry (graceful skip)
- `[HsmGuard(DtoType = typeof(MyDto))]` with void* works the same way

**Note:** Commit the FastHSM submodule separately (it's a submodule).

---

### TASK-BB-1b-04 — `BlackboardBinPacker` (inline-only)

**Spec:** TASK-DETAIL.md §TASK-BB-1b-04; design BB §6.1–6.2, §6.6.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs`

**What it does:** Given a list of `BlackboardVariableDescriptor` (name + type, one per variable), compute sequential byte offsets with correct C# struct alignment, return the packed result.

**Public surface:**

```csharp
// Input: a variable's name and its CLR type (used for Marshal.SizeOf + alignment)
public record BlackboardVariableDescriptor(string Name, Type FieldType);

// Output per packed variable
public record PackedVariable(
    string Name,
    Type FieldType,
    int ByteOffset,
    int ByteSize,
    PackTier Tier            // Inline in this batch; Heavy in TASK-BB-1c-04
);

public enum PackTier { Inline, Heavy }

public enum PackWarning
{
    None,
    InlineMemoryExceeded,    // master vars > 100 bytes
}

public record PackResult(
    IReadOnlyList<PackedVariable> Variables,
    int TotalInlineBytes,
    bool RequiresHeavyComponent,  // always false in this slice
    PackWarning Warning
);

public static class BlackboardBinPacker
{
    // MaxInlineBytes = 100 (from BehaviorConstants.MaxBehaviorParamByteSize)
    public const int MaxInlineBytes = 100;

    // Pack the given variables into the inline tier.
    // masterVars: variables that belong to the master asset's own blackboard
    // aggregatedVars: sub-tree-required DTOs (empty in this slice, used in 1.5c)
    public static PackResult Pack(
        IReadOnlyList<BlackboardVariableDescriptor> masterVars,
        IReadOnlyList<BlackboardVariableDescriptor>? aggregatedVars = null);
}
```

**Algorithm (design BB §6.2):**
1. For each variable in order, compute alignment = `min(Marshal.SizeOf(type), 8)` — capped at 8 (matches C# default struct alignment rules).
2. Round current offset up to the next multiple of alignment.
3. Assign that as the variable's `ByteOffset`.
4. Advance offset by `Marshal.SizeOf(type)`.
5. After all variables, if `TotalInlineBytes > MaxInlineBytes`, set `Warning = InlineMemoryExceeded` and `RequiresHeavyComponent = false` (heavy spill is TASK-BB-1c-04).

**Critical — tail bytes off-limits (BB §6.6):** The last 28 bytes of `BrainBlackboard` (offsets 100–127) are reserved for the tail registers (`ExpectedThreatLevel`, `Interrupt_MobilityLost`, `Interrupt_Reserved`). The bin-packer must NEVER allocate offsets >= 100 — this is enforced by the 100-byte ceiling. The packer should assert or surface a warning if the ceiling is breached, not silently overflow.

**Tests** (`BlackboardBinPackerTests.cs`):
- Single `bool` (1 byte) → offset 0, size 1, total 1 byte
- Single `int` (4 bytes) → offset 0, size 4
- `bool` then `int` → bool at 0, int at 4 (aligned to 4), total 8
- `byte` then `long` → byte at 0, long at 8 (aligned to 8), total 16
- Multiple primitives that exactly fill 100 bytes → `Warning = None`
- Total > 100 bytes → `Warning = InlineMemoryExceeded`; `TotalInlineBytes` > 100; `RequiresHeavyComponent = false` (not yet)
- `Vector3` (12 bytes, natural align 4 on x86 but 4 on SIMD layout) — use `Marshal.SizeOf` to determine; verify offset matches expectation for concrete types in tests
- Empty list → offset 0, empty result, no warning
- `aggregatedVars = null` treated same as empty
- 8-byte cap on alignment: a type larger than 8 bytes aligned to 8 (not its size)

---

### TASK-BB-1b-01 — `BlackboardDtoEmitter` (HROT_EDITOR_GENERATED file)

**Spec:** TASK-DETAIL.md §TASK-BB-1b-01; design BB §2.2, §3.3, §3.4, §3.5, §4.6, §4a.2.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs`

This class emits the `{AssetName}.Blackboard.cs` file. It is NOT an asset emitter (doesn't need to extend `FluentCSharpEmitterBase` with `EmitCore(IEditableAsset)`) — it is a standalone stateless emitter that takes an explicit model and returns the file content string.

**Model types needed (define in `BlackboardDtoEmitter.cs` or a companion `BlackboardDtoModel.cs`):**

```csharp
// Represents a single field in the editor's model
public abstract record BlackboardFieldEntry(string Name, Type FieldType);

// Editor-managed field: regenerated from the editor model
public record EditorManagedFieldEntry(
    string Name,
    Type FieldType,
    string? Comment    // null = no /// comment; non-null = emitted as /// <summary>...</summary>
) : BlackboardFieldEntry(Name, FieldType);

// Read-only passthrough: emit verbatim from captured span
public record ReadOnlyFieldEntry(
    string Name,
    Type FieldType,
    string VerbatimText   // the exact text to emit (from the source span)
) : BlackboardFieldEntry(Name, FieldType);
```

**Model for the whole struct:**

```csharp
public record BlackboardDtoModel(
    Guid AssetId,
    string AssetName,
    string StructName,        // e.g. "OrcGuard_BT_Blackboard" or "OrcGuard_Blackboard"
    string Namespace,         // C# namespace for the emitted file
    IReadOnlyList<BlackboardFieldEntry> Fields   // in canonical order
);
```

**Public surface:**

```csharp
public static class BlackboardDtoEmitter
{
    // Returns the full .cs file content for the given model. Deterministic.
    public static string Emit(BlackboardDtoModel model);

    // Convenience: emit and write atomically, returning true if file changed.
    public static bool EmitAndWrite(BlackboardDtoModel model, string filePath);
}
```

**Emitted file format (design §2.2, §3.3):**

```csharp
// HROT_EDITOR_GENERATED -- managed by the AI editor; manual edits will be overwritten on next save.
// Hand-introduced fields with attributes or non-standard types are preserved verbatim.
// OwningAssetId: {AssetId:D}
// OwningAssetName: {AssetName}

using System.Runtime.InteropServices;
// ...other usings in sorted order (System.* first, then others)...

namespace {Namespace};

[StructLayout(LayoutKind.Sequential)]
public partial struct {StructName}
{
    {fields...}
}
```

For **editor-managed fields:**
```csharp
    /// <summary>{Comment}</summary>
    public {TypeName} {Name};
```
(if Comment is null, omit the `///` block entirely)

For **read-only passthrough fields:** emit `VerbatimText` exactly as captured, with the correct indentation (4 spaces inside the struct).

**Using directives:** Collect all namespaces required by the field types (use `FluentCSharpEmitterBase.SortUsings` for deterministic ordering). Always include `System.Runtime.InteropServices` for `[StructLayout]`.

**TypeName resolution:** Use the short C# type alias when available (`int` not `System.Int32`, `bool` not `System.Boolean`, etc.). For other types use `Type.Name` (not FullName, to avoid long qualified names in the output).

**Tests** (`BlackboardDtoEmitterTests.cs`):

1. **Marker block** — emitted file starts with the 4-line marker block (check first 4 lines)
2. **Correct OwningAssetId** — `OwningAssetId: {guid:D}` present
3. **StructLayout attribute** — `[StructLayout(LayoutKind.Sequential)]` present
4. **`partial struct`** — declaration uses `public partial struct`
5. **Editor-managed field with comment** — `/// <summary>...` block above the field
6. **Editor-managed field without comment** — no `///` lines above the field
7. **Read-only passthrough** — verbatim text emitted exactly (byte-identical substring)
8. **Using directives sorted** — `System.Runtime.InteropServices` present; additional usings in sorted order
9. **Determinism** — calling `Emit()` twice with the same model returns the same string
10. **Mixed model** — editor-managed and read-only fields in order, output correct for both

---

### TASK-BB-1b-06 — Round-trip determinism property tests (RT-1, RT-2)

**Spec:** TASK-DETAIL.md §TASK-BB-1b-06; design BB §3.7.

**Tests** (`BlackboardDtoEmitterTests.cs`, round-trip section):

**RT-1 — No-edit round-trip is byte-identical:**
- Build a `BlackboardDtoModel`, call `Emit()` → string S1
- Parse S1 with `BlackboardSourceTextParser`, build a new `BlackboardDtoModel` from the parse result, call `Emit()` again → string S2
- Assert `S1 == S2`
- Test this for: all-editor-managed model, all-read-only model (where `VerbatimText` is the raw field text from S1), and mixed model

**RT-2 — Single-edit round-trip produces confined diff:**
- Build a model with N editor-managed fields, emit → S1
- Add one new field to the model, emit → S2
- Assert S2 contains all original fields (check by substring), the new field is present, and only the new field and its position differ
- Also test: remove one field → the removed field is absent in S2, all others unchanged
- Also test: change a comment on one field → only that field's `///` block changes in S2; all other lines are unchanged (compare line-by-line)
- **Read-only fields must be byte-identical in S2** when they were not touched: for a mixed model, the read-only field's text in S2 must equal its text in S1 exactly

**Critical:** The round-trip tests must verify the real contract (byte-identical no-edit, confined-diff single-edit), not just "the output compiles" or "contains some keyword".

---

## Mandatory Workflow: Test-Driven Task Progression

For each task in order:
1. Read the spec in TASK-DETAIL.md and the referenced design sections
2. Explore existing code (emitter base, parser, classifier) to understand patterns
3. Write tests first (failing)
4. Implement to make tests pass
5. Run: `dotnet build IOS-IG-SimHost.sln` + specific test project
6. Move to next task only when current task's tests pass

---

## Testing Requirements

- All new tests in `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/`
- Bin packer tests: verify exact byte offsets, not just "no crash"
- Emitter tests: assert on actual file content (line-by-line or substring), not just "non-empty string"
- Round-trip tests: must compare full strings (S1 == S2 for RT-1) or differing lines only (RT-2)
- Minimum 30 new tests across the new test classes

---

## Notes

- `FluentCSharpEmitterBase` has `SortUsings` and `WriteAtomic` — use them
- `FluentCSharpEmitterBase` has `EditorGeneratedMarker` — use this constant for the first line
- The emitter is NOT an abstract subclass of `FluentCSharpEmitterBase` (there's no `IEditableAsset` here); call the static helpers directly from `BlackboardDtoEmitter`
- For type name resolution: `int` → `"int"`, `float` → `"float"`, etc. Build a small dictionary for the primitives. For others use `Type.Name`.
- The DEBT-01 fix touches `FDP/ExtDeps/FastHSM` (a git submodule) — commit the submodule first, then the top-level repo

---

## Report Requirements

Submit `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-03-REPORT.md`:

```markdown
# BATCH-03 Report

## Tasks Completed
- [ ] DEBT-01 fix (HsmAction/HsmGuard DtoType property)
- [ ] TASK-BB-1b-04 (BlackboardBinPacker)
- [ ] TASK-BB-1b-01 (BlackboardDtoEmitter)
- [ ] TASK-BB-1b-06 (Round-trip tests)

## Test Results
[dotnet test summary for Hrot.Editor.AiShared.Tests]

## Files Changed / Created

## Developer Insights

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Anything surprising about C# alignment rules for the bin packer?

**Q3:** What design decisions did you make beyond the instructions?

**Q4:** Were there edge cases in the round-trip tests you had to handle carefully?

**Q5:** Suggested git commit message?
```

---

## Success Criteria

This batch is DONE when:
- [ ] DEBT-01: `HsmActionAttribute.DtoType` + `HsmGuardAttribute.DtoType` exist, exporter reads them, tests pass
- [ ] TASK-BB-1b-04: `BlackboardBinPacker.Pack()` returns correct offsets for all test fixtures; `InlineMemoryExceeded` fires correctly; >100B ceiling enforced
- [ ] TASK-BB-1b-01: `BlackboardDtoEmitter.Emit()` produces correct 4-line header, StructLayout, fields, usings; tests pass
- [ ] TASK-BB-1b-06: RT-1 (byte-identical no-edit) and RT-2 (confined-diff single-edit) tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` all pass
- [ ] Report submitted
