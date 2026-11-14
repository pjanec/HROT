# BATCH-03 Report

## Tasks Completed
- [x] DEBT-01 fix (HsmAction/HsmGuard DtoType property)
- [x] TASK-BB-1b-04 (BlackboardBinPacker)
- [x] TASK-BB-1b-01 (BlackboardDtoEmitter)
- [x] TASK-BB-1b-06 (Round-trip tests)

## Test Results

```
Passed!  - Failed:     0, Passed:   276, Skipped:     0, Total:   276, Duration: 2 s - Hrot.Editor.AiShared.Tests.dll (net8.0)
```

New tests added in BATCH-03: **53**
- DEBT-01 (ActionSchemaExporterTests additions): 7 tests
- BlackboardBinPackerTests: 19 tests
- BlackboardDtoEmitterTests (incl. RT-1 and RT-2): 27 tests

Prior test count (end of BATCH-02): 223
Final test count: 276
Build: succeeded (0 errors, 9 pre-existing warnings unrelated to our changes)

## Files Changed / Created

**Modified (FastHSM submodule):**
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs` -- added `Type? DtoType { get; set; }`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmGuardAttribute.cs` -- added `Type? DtoType { get; set; }`

**Modified (main repo):**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` -- DEBT-01 fallback: when `ExtractFirstRefParamType` returns null, try `ExtractHsmAttributeDtoType` before skipping
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` -- added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (required for void* fixture methods)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/ActionSchemaExporterTests.cs` -- added 3 DEBT-01 fixture methods + 7 tests

**Created:**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardBinPacker.cs` -- `BlackboardVariableDescriptor`, `PackedVariable`, `PackTier`, `PackWarning`, `PackResult`, `BlackboardBinPacker` static class
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` -- `BlackboardFieldEntry`, `EditorManagedFieldEntry`, `ReadOnlyFieldEntry`, `BlackboardDtoModel`, `BlackboardDtoEmitter` static class
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardBinPackerTests.cs` -- 19 tests
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardDtoEmitterTests.cs` -- 27 tests (TASK-BB-1b-01 + TASK-BB-1b-06)

## Developer Insights

**Q1: What issues did you encounter? How did you resolve them?**

Two issues:

1. `Marshal.SizeOf(typeof(bool))` returns 4 on Windows (Win32 BOOL = 4 bytes), but C# sequential struct layout uses 1 byte for `bool`. The bin packer tests correctly expected 1 byte. Fixed by adding a `PrimitiveSizes` lookup table that maps C# primitive types to their managed sizes; `Marshal.SizeOf` is used only for struct types not in the table (e.g., `Vector3`, `Quaternion`).

2. The FastHSM `HsmActionAttribute.cs` got its indentation corrupted by the first replacement attempt (a regex mismatch left the closing braces in the wrong place). Fixed by rewriting the file via PowerShell `[System.IO.File]::WriteAllText` with the correct content.

**Q2: Anything surprising about C# alignment rules for the bin packer?**

Yes -- `Marshal.SizeOf` is explicitly for P/Invoke/unmanaged interop and does NOT reflect the C# managed struct layout for primitive types. `bool` is the most obvious case (4 bytes for P/Invoke, 1 byte in managed). For the blackboard DTO use case (a C# sequential struct), the managed size is correct. The `PrimitiveSizes` lookup handles all C# built-ins; `Marshal.SizeOf` is the fallback for user-defined blittable structs.

The 8-byte alignment cap (`AlignmentCap = 8`) correctly mirrors C# default struct rules: types of 12+ bytes (like `Vector3`) still align to 8, not 12.

**Q3: What design decisions did you make beyond the instructions?**

- **`GetManagedSize` helper** in `BlackboardBinPacker` instead of a raw `Marshal.SizeOf` call. The instructions say "compute alignment = min(Marshal.SizeOf(type), 8)" but the intent is clearly to match C# struct layout, not P/Invoke sizes. Added a comment in the code and this report explaining the decision.

- **Fixed newline in `EmitReadOnlyField`** -- the spec says "emit `VerbatimText` exactly as captured", but verbatim text from the parser includes newlines. Added a trailing-newline guard so that if the verbatim text lacks a trailing `\n`, one is appended. This ensures the next field starts on a new line without corrupting the verbatim content.

- **`AllowUnsafeBlocks` on the test project** -- needed for the DEBT-01 fixture methods with `void*` parameters. This is the minimal change required; the production code itself does not use unsafe blocks.

**Q4: Were there edge cases in the round-trip tests you had to handle carefully?**

The RT-1 round-trip tests work by parsing the emitted string with `BlackboardSourceTextParser` and reconstructing the model using `ReadOnlyFieldEntry` (verbatim text from span). For this to be byte-identical on re-emit, the verbatim text must not be modified. The key insight: the parser captures the span including leading whitespace (indentation), so as long as the emitter emits verbatim text without re-indenting it, the round-trip holds.

For RT-2 (comment change), the test verifies line-by-line equality for all lines not belonging to the changed field. This works because the emitter emits each field independently, so changing one field's comment affects only that field's lines.

**Q5: Suggested git commit message?**

```
feat(blackboard): BATCH-03 bin packer, DTO emitter, round-trip tests + DEBT-01 fix

- DEBT-01: add DtoType property to HsmActionAttribute + HsmGuardAttribute (FastHSM submodule)
- ActionSchemaExporter: fall back to attribute DtoType for void* HSM method signatures
- BlackboardBinPacker: sequential byte-offset packing with C# alignment caps (AlignmentCap=8)
  - PrimitiveSizes lookup for managed sizes (Marshal.SizeOf gives wrong size for bool)
  - MaxInlineBytes=100 ceiling; InlineMemoryExceeded warning when exceeded
- BlackboardDtoEmitter: emit {AssetName}.Blackboard.cs deterministically
  - 4-line HROT_EDITOR_GENERATED marker block (AssetId, AssetName)
  - [StructLayout(LayoutKind.Sequential)] public partial struct
  - EditorManagedFieldEntry: optional summary comment + public field declaration
  - ReadOnlyFieldEntry: verbatim text preservation (byte-identical passthrough)
  - UsingDirectiveSet for deterministic sorted usings
- AllowUnsafeBlocks on test project for void* DEBT-01 fixture methods
- 53 new tests (7 DEBT-01, 19 BinPacker, 27 DtoEmitter+RT); 0 regressions

Closes DEBT-01, TASK-BB-1b-04, TASK-BB-1b-01, TASK-BB-1b-06
```
