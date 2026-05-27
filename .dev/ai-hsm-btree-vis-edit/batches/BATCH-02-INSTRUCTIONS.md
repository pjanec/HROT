# BATCH-02: Phase 1.5a — Action Schema + Source Text Parser + Field Classifier

**Batch Number:** BATCH-02
**Tasks:** TASK-BB-1a-01, TASK-BB-1a-02, TASK-BB-1a-04, TASK-BB-1a-05
**Phase:** Phase 1.5a — Action schema and read-only Variables panel (core infrastructure)
**Estimated Effort:** 12–16 hours
**Priority:** HIGH — unblocks all subsequent phases that need action-DTO reflection and file parsing
**Dependencies:** BATCH-01 (Phase 0 attribute work, now committed)

---

## Onboarding & Workflow

### Developer Instructions

This batch builds the foundational services for blackboard authoring: the action-DTO schema exporter (reflects all registered actions/conditions/guards to discover their DTO types), the catalog-change wiring that keeps the schema fresh on hot reload, the companion-file source text parser (verbatim span capture), and the per-field classification rule. No editor windows or picker changes yet — pure services and tests.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/ai-hsm-btree-vis-edit/ONBOARDING.md` — especially section 3 (code locations) and section 4 (building)
3. **Task Details:** `.dev/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — §0 (Reconciliation) then Phase 1.5a entries for TASK-BB-1a-01, 02, 04, 05
4. **Design:** `.dev/ai-hsm-btree-vis-edit/Blackboard_Authoring_Detailed_Design.md`:
   - §3.2 (load pipeline), §3.4 (editor-managed rule), §3.5 (read-only-passthrough), §3.7 (round-trip guarantees)
   - §10.1–10.4 (action schema exporter), §10.7 (rebuild on catalog change)
5. **Previous review:** `.dev/ai-hsm-btree-vis-edit/reviews/BATCH-01-REVIEW.md`
6. **Existing anchors to read before coding:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` — the `Changed` event you wire to
   - `Hrot/Editor/Hrot.Editor.AiShared/Emit/FluentCSharpEmitterBase.cs` — the emitter pattern (context for §6 of the design)
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Blackboard/BlackboardSchemaBuilder.cs` — existing reflection path to understand and extend
   - `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/` — attribute classes you'll reflect over
   - `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` — HSM attribute classes
   - `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BlackboardAnnotations.cs` — K-03/K-04 attributes from Batch 01

### Source Code Locations

- **New shared services:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/` (create this folder)
- **New tests:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/` (create this folder)
- **Existing test project:** `Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj`
- **Existing shared project:** `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj`

### Report Submission

**When done, submit your report to:**
`.dev/ai-hsm-btree-vis-edit/reports/BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev/ai-hsm-btree-vis-edit/questions/BATCH-02-QUESTIONS.md`

---

## Context

Phase 1.5a's acceptance gate (BB §15): "opening an asset shows its reflected blackboard fields; BTree/HSM action pickers filter to compatible variables; no editor-side authoring yet." This batch delivers the backend services. The picker filtering (TASK-BB-1a-06) and window shell (TASK-BB-1a-03) come in the next batch.

**Dependency graph for this batch:**
```
TASK-BB-K-03 (BlackboardDtoStructAttribute) -- already done
TASK-BB-K-04 (BlackboardReadOnly/Write)      -- already done
    |
    v
TASK-BB-1a-01  IActionSchemaExporter (reflection-based)
    |
    v
TASK-BB-1a-02  Schema rebuild on IAssetCatalog.Changed
    |
TASK-BB-1a-04  BlackboardSourceTextParser (verbatim span capture)
    |
TASK-BB-1a-05  Per-field classification (uses 1a-01 + 1a-04)
```

---

## Tasks

### TASK-BB-1a-01 — `IActionSchemaExporter` with reflection-based population

**Spec:** See TASK-DETAIL.md §TASK-BB-1a-01; design BB §10.1–§10.4.

**New files to create:**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs` (production implementation)
- Supporting types (can be in the same files or separate): `ActionSchemaEntry`, `ActionHosting` (flags enum), `BlackboardAccess` enum

**Public surface (must match exactly):**

```csharp
// Flag enum — can be combined with | operator
[Flags]
public enum ActionHosting
{
    None      = 0,
    BTree     = 1 << 0,
    Hsm       = 1 << 1,
    Shared    = 1 << 2,
    Heavy     = 1 << 3,   // action has a heavy DTO parameter
}

public enum BlackboardAccess
{
    Unknown   = 0,  // unannotated → treated as ReadWrite by caller
    ReadOnly,
    ReadWrite,
}

public record ActionSchemaEntry(
    string Fqn,               // "{DeclaringType.FullName}.{MethodName}" format
    Type DtoType,             // type of first ref parameter
    ActionHosting Hosting,
    BlackboardAccess Access,  // from [BlackboardReadOnly]/[BlackboardReadWrite] on first param
    Type? HeavyDtoType        // non-null for [SharedAiHeavyAction] with unmanaged heavy param
);

public interface IActionSchemaExporter
{
    IReadOnlyDictionary<string, ActionSchemaEntry> All { get; }  // keyed by Fqn
    ActionSchemaEntry? Lookup(string fqn);
    void Rebuild();
    event Action? Changed;
}
```

**Reflection targets** — enumerate ALL loaded assemblies and scan methods bearing any of:
- `[BTreeAction]` (`Fbt.BTreeActionAttribute`) → `ActionHosting.BTree`
- `[BTreeCondition]` (`Fbt.BTreeConditionAttribute`) → `ActionHosting.BTree`
- `[BTreeObserver]` (if it exists in `Fbt.Kernel`) → `ActionHosting.BTree`
- `[HsmAction]` (`Fhsm.Kernel.Attributes.HsmActionAttribute`) → `ActionHosting.Hsm`
- `[HsmGuard]` (`Fhsm.Kernel.Attributes.HsmGuardAttribute`) → `ActionHosting.Hsm`
- `[SharedAiAction]` (`Fbt.Kernel.SharedAiActionAttribute`) → `ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared`
- `[SharedAiCondition]` (`Fbt.Kernel.SharedAiConditionAttribute`) → same
- `[SharedAiHeavyAction]` (`Fbt.Kernel.SharedAiHeavyActionAttribute`) → `| ActionHosting.Heavy`; extract `HeavyDtoType` from the attribute's `HeavyComponentType` (check actual attribute properties)

For each method found:
- **FQN** = `{method.DeclaringType.FullName}.{method.Name}`
- **DtoType** = the type of the first `ref` parameter
- **Access** = check the first parameter for `[BlackboardReadOnly]` → `ReadOnly`; `[BlackboardReadWrite]` → `ReadWrite`; neither → `Unknown`
- **Hosting** = union of all attribute kinds found on the method (a single method may carry multiple attributes)

**Note on attribute discovery:** Before reflecting, read the existing attribute files in `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/` and `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` to find the exact class names. Some may not exist yet — only scan for what actually exists. Do not crash if an attribute type is not found in loaded assemblies.

**Tests** (`Hrot.Editor.AiShared.Tests/Blackboard/ActionSchemaExporterTests.cs`):
- Define a fixture assembly-local class with methods bearing the various attributes; call `Rebuild()` against those assemblies; verify each FQN is discoverable
- `DtoType` extracted from first `ref` param of BTree action
- `Hosting` flags composed for a method carrying `[SharedAiAction]` multiple times with different DTO types (same hosting flags; multiple entries if different FQNs)
- `[BlackboardReadOnly]` on first param → `Access = ReadOnly`
- `[BlackboardReadWrite]` → `Access = ReadWrite`
- Unannotated first param → `Access = Unknown`
- Method with `[SharedAiHeavyAction]` → `HeavyDtoType` non-null; correct type
- `Lookup(fqn)` returns null for unknown FQN
- After `Rebuild()` with no new types, `All` remains consistent (no duplicates)

### TASK-BB-1a-02 — Schema rebuild on `IAssetCatalog.Changed`

**Spec:** See TASK-DETAIL.md §TASK-BB-1a-02; design BB §10.7.

Wire `IActionSchemaExporter.Rebuild()` to fire when `IAssetCatalog.Changed` fires.

**Where:** Create `ActionSchemaExporterCatalogWatcher` (or integrate into `ActionSchemaExporter`) inside `Hrot.Editor.AiShared/Blackboard/`. This should be registered via DI alongside the exporter so the watcher is active for the editor's lifetime.

**Behavior:**
- When `IAssetCatalog.Changed` fires, call `Rebuild()` then raise the exporter's own `Changed` event so downstream consumers (pickers, aggregation) also refresh.
- The subscription must not leak (unsubscribe when disposed / use a stable weak reference or lifecycle-managed DI scope).

**Tests** (`ActionSchemaExporterTests.cs` or separate `ActionSchemaExporterCatalogWatcherTests.cs`):
- Catalog `Changed` event → `Rebuild()` called exactly once per event (verify via a mock or a counter on a test double)
- After `Rebuild()`, the exporter's own `Changed` event fires exactly once
- Subscribing multiple times to catalog does not cause duplicate `Rebuild()` calls (idempotency via proper wiring)

### TASK-BB-1a-04 — `BlackboardSourceTextParser` (verbatim span capture)

**Spec:** See TASK-DETAIL.md §TASK-BB-1a-04; design BB §3.2 (load steps 3,6), §3.5.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardSourceTextParser.cs`

**Responsibilities:**
1. Given the full text of a `.cs` companion file, locate the struct declaration (by name).
2. For each field in the struct (derived from the text, NOT from reflection here — you parse the text), extract:
   - Field name
   - Leading `///` doc comment block (consecutive lines starting with `///` immediately above the field declaration, no blank line between comment and field)
   - Verbatim source span: from the start of the leading comment/attributes (or start of the declaration line if no comment/attributes) through the trailing `;` inclusive
   - Whether the field declaration is a "single-line declaration" (fits on one line with no embedded newlines between the type and the semicolon)
3. Return a result with: per-field entries plus a struct-locate result (success/failure with reason).

**Public surface (design-aligned):**

```csharp
public record FieldParseResult(
    string Name,
    string? LeadingComment,       // null if none; include the /// lines verbatim
    (int Start, int Length) VerbatimSpan,  // byte offsets into the source text
    bool IsSingleLineDeclaration
);

public record StructLocateResult(bool Found, string? Reason);

public record SourceParseResult(
    StructLocateResult LocateResult,
    IReadOnlyList<FieldParseResult> Fields  // empty if StructLocateResult.Found == false
);

public static class BlackboardSourceTextParser
{
    // Returns parse result for the named struct in the source text.
    public static SourceParseResult Parse(string sourceText, string structName);
}
```

**Implementation notes:**
- Use simple line-by-line text scanning; no Roslyn dependency required for this parser. The struct is always `public partial struct {Name}` or `public struct {Name}`. Field declarations are always simple enough to parse with line scanning given the six-condition rule's guarantee that malformed fields are at most verbatim-captured, not parsed.
- For multi-line fields (those that span multiple lines), capture the whole span from the first line's start through the line containing the `;`.
- The "leading comment" for a field means consecutive `///` lines with NO blank line separating the last `///` line from the field declaration line.
- Struct-not-found case: return `StructLocateResult(false, reason)` with empty Fields.

**Tests** (`Hrot.Editor.AiShared.Tests/Blackboard/BlackboardSourceTextParserTests.cs`):

Use inline string literals as fixture source text in the tests. Test each scenario:

1. **Clean simple field** — `public int AmmoCount;` → correct name, null comment, span covers only the declaration line, `IsSingleLineDeclaration = true`
2. **Field with `///` comment** — multi-line `///` block above the field → `LeadingComment` contains all `///` lines; span starts at the first `///` line
3. **Field with attributes** (`[SomeAttr]`) — verbatim span includes the attribute line; `IsSingleLineDeclaration = true` (the field itself is one line, but combined with the attribute it spans 2 lines — the span captures both; the `IsSingleLineDeclaration` flag reflects only the declaration line itself, not the attribute)
4. **Multi-line field declaration** (e.g. `public \n int field;`) — span covers all lines; `IsSingleLineDeclaration = false`
5. **Field with initializer** (`public int Count = 0;`) — captured as verbatim span; `IsSingleLineDeclaration = true` (it's a single line)
6. **Struct-not-found** — source with no matching struct name → `LocateResult.Found = false`
7. **Empty struct** — struct with no fields → `Fields` is empty list, `Found = true`
8. **Mixed: editor-managed field followed by read-only field** — both appear in order; spans are non-overlapping and contiguous

**Critical:** Span boundaries must be byte-accurate (byte offsets into the UTF-8/UTF-16 source string, or char offsets — be consistent and document which). Test that `sourceText.Substring(span.Start, span.Length)` returns the exact text from `///` through `;`.

### TASK-BB-1a-05 — Per-field classification (editor-managed vs read-only-passthrough)

**Spec:** See TASK-DETAIL.md §TASK-BB-1a-05; design BB §3.4 (the six conditions), §3.5.

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardFieldClassifier.cs`

**Input:**
- A `FieldParseResult` from the source text parser (1a-04)
- A `FieldInfo` from reflection (to know the type)
- The set of "known types" (see below)

**Output:** `FieldClassification` — either `EditorManaged` or `ReadOnlyPassthrough`, with a reason for read-only.

**The six conditions** (from design §3.4) — a field is `EditorManaged` ONLY if ALL six hold:

1. Declaration shape is exactly `public {Type} {Name};` — check `IsSingleLineDeclaration` from parse result AND that the parse reveals no attributes and no initializer.
2. The field's `FieldInfo.FieldType` is in the "known type set":
   - C# primitives: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`
   - `Vector2`, `Vector3`, `Vector4`, `Quaternion` (from `System.Numerics`)
   - Any `enum` type
   - Any struct marked `[BlackboardDtoStruct]` (from Batch 01)
   - Any `Type` that appears as `DtoType` in any `ActionSchemaEntry` from the schema exporter
3. A `///` comment block is acceptable (allowed), but only if it is immediately above the declaration with no blank line.
4. No attributes on the field declaration line itself (check parse result for attribute lines in the span).
5. No initializer.
6. Single-line declaration (`IsSingleLineDeclaration == true`).

**Public surface:**

```csharp
public enum FieldClassification
{
    EditorManaged,
    ReadOnlyPassthrough,
}

public record FieldClassificationResult(
    FieldClassification Classification,
    string? ReadOnlyReason  // null when EditorManaged; human-readable rule description when ReadOnly
);

public static class BlackboardFieldClassifier
{
    public static FieldClassificationResult Classify(
        FieldParseResult parseResult,
        FieldInfo fieldInfo,
        IReadOnlySet<Type> knownTypes);
}
```

**Tests** (`BlackboardFieldClassifierTests.cs` — or in the same test class as the parser tests if compact):

Each of the six conditions independently forces `ReadOnlyPassthrough` when violated:

1. Multi-line declaration → ReadOnly (reason mentions "multi-line")
2. Unknown type (`SomeExoticType` not in known set) → ReadOnly (reason mentions "unknown type")
3. Has attribute in parse span → ReadOnly (reason mentions "has attribute")
4. Has initializer in parse span → ReadOnly (reason mentions "initializer")
5. Non-public visibility (e.g., `private int x;`) → ReadOnly (though parser may not capture private fields from the struct scan — handle gracefully)
6. Struct type marked `[BlackboardDtoStruct]` in known set → EditorManaged
7. Type appears in schema exporter `DtoType` set → EditorManaged
8. Enum type → EditorManaged
9. Primitive type + single-line + no attributes + no initializer → EditorManaged
10. `///` comment allowed (does NOT force ReadOnly) → EditorManaged

**Critical:** the test for "has attributes" must use a parse result where the span contains attribute lines (e.g., `[SomeAttr]\npublic int Field;`). The test for "no initializer" must parse a span containing `= 0`.

---

## Mandatory Workflow: Test-Driven Task Progression

For each task:

1. **Read the task spec** in TASK-DETAIL.md and the referenced design sections before coding
2. **Explore the existing code** — read the attribute files, the existing `BlackboardSchemaBuilder`, the `IAssetCatalog`, the `FluentCSharpEmitterBase`, to understand patterns
3. **Write the tests first** — they define the contract
4. **Implement** the minimal code to make tests pass
5. **Run:** `dotnet build IOS-IG-SimHost.sln` + the specific test project
6. **Move to next task** only when current task's tests pass

---

## Testing Requirements

- All tests live in `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/`
- Tests use inline fixture data (fixture `.cs` strings, fixture test attribute-bearing methods) — no file I/O needed
- Every assertion checks specific values, not just "no exception" or "not null" alone
- Schema exporter tests use local test fixture methods decorated with the kernel attributes (note: the fixture assembly must reference `Fbt.Kernel` so the attributes are available)
- Source parser tests must verify byte-exact span boundaries via `sourceText.Substring(span.Start, span.Length)` assertions
- Classifier tests must independently cover each of the six conditions
- Minimum 25 tests across the three new test classes

---

## Notes on Project References

- `Hrot.Editor.AiShared` already references the BTree and HSM kernel assemblies (check the `.csproj` to confirm). The action schema exporter will need to reference the attribute types — confirm the project reference chain rather than assuming.
- If a `[BTreeObserver]` attribute does not exist, skip it without error.
- Do not add a reference from `Hrot.Editor.AiShared` to `Hrot.BTree.Editor` or `Hrot.Hsm.Editor` — the reference direction is one-way (subsystem editors reference shared; never the reverse).

---

## Report Requirements

Submit `.dev/ai-hsm-btree-vis-edit/reports/BATCH-02-REPORT.md` with:

```markdown
# BATCH-02 Report

## Tasks Completed
- [ ] TASK-BB-1a-01
- [ ] TASK-BB-1a-02
- [ ] TASK-BB-1a-04
- [ ] TASK-BB-1a-05

## Test Results
[Paste dotnet test summary for Hrot.Editor.AiShared.Tests]

## Files Changed / Created
[List each file with a brief description of what changed]

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or inconsistencies in the existing codebase (attribute classes, schema builder, etc.)?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Anything about the six-condition classifier that surprised you or needed extra thought?

**Q6:** Suggested git commit message for this batch?
```

---

## Success Criteria

This batch is DONE when:
- [ ] TASK-BB-1a-01: `IActionSchemaExporter` + `ActionSchemaExporter` exist, all attribute kinds reflected, FQNs correct, access annotations read, tests pass
- [ ] TASK-BB-1a-02: `IAssetCatalog.Changed` → `Rebuild()` → exporter's `Changed` fires; tests verify the chain
- [ ] TASK-BB-1a-04: `BlackboardSourceTextParser.Parse()` returns correct spans for all fixture scenarios; byte-exact span boundary tests pass
- [ ] TASK-BB-1a-05: `BlackboardFieldClassifier.Classify()` returns `EditorManaged`/`ReadOnlyPassthrough` correctly for each of the six conditions; all ten classifier tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with no errors
- [ ] `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj` — new tests all pass, no regressions
- [ ] Report submitted to `.dev/ai-hsm-btree-vis-edit/reports/BATCH-02-REPORT.md`
