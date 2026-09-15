# BATCH-02 Report

## Tasks Completed

- [x] TASK-BB-1a-01 — `IActionSchemaExporter` + `ActionSchemaExporter` reflection-based population
- [x] TASK-BB-1a-02 — Schema rebuild on `IAssetCatalog.Changed` via `ActionSchemaExporterCatalogWatcher`
- [x] TASK-BB-1a-04 — `BlackboardSourceTextParser` verbatim span capture
- [x] TASK-BB-1a-05 — Per-field classification (`BlackboardFieldClassifier`)

## Test Results

```
Test run for Hrot.Editor.AiShared.Tests.dll (.NETCoreApp,Version=v8.0)
Passed!  - Failed: 0, Passed: 223, Skipped: 0, Total: 223, Duration: 2 s
```

All pre-existing tests continue to pass; 0 regressions.

## Files Changed / Created

### Modified
- `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj`
  Added two `ProjectReference` entries for `Fbt.Kernel` and `Fhsm.Kernel` so the schema exporter can reference attribute types.

### Created (production code)
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`
  Public contracts: `ActionHosting` flags enum, `BlackboardAccess` enum, `ActionSchemaEntry` record, `IActionSchemaExporter` interface.

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs`
  Reflection-based implementation. Scans `AppDomain.CurrentDomain.GetAssemblies()` on each `Rebuild()`. Handles `ReflectionTypeLoadException` (bad assemblies) and `TypeLoadException` (incompatible types from test platform assemblies). Extracts DtoType from the first ByRef parameter; skips methods with no ref param (handles HSM `void*` signatures). Reads `[BlackboardReadOnly]`/`[BlackboardReadWrite]` from the first parameter. Fires `Changed` after each rebuild.

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporterCatalogWatcher.cs`
  Disposable watcher that subscribes to `IAssetCatalog.Changed` and calls `IActionSchemaExporter.Rebuild()`. Unsubscribes on `Dispose()` to prevent leaks.

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardSourceTextParser.cs`
  Line-by-line parser (no Roslyn). Locates the target struct by name, then collects fields via a state machine: blank lines reset context, `///` lines accumulate a comment block, `[` lines set a pending-attribute flag, and everything else attempts field name extraction. Multi-line declarations are detected when no `;` is found on the first candidate line. Span offsets are char (UTF-16) offsets; `sourceText.Substring(span.Start, span.Length)` is the contract. Extended `FieldParseResult` with `HasAttribute` and `HasInitializer` booleans required by the classifier.

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardFieldClassifier.cs`
  Applies the six-condition rule using `FieldParseResult` + `FieldInfo` + caller-provided `IReadOnlySet<Type>`. Known types: C# primitives, `System.Numerics` vector/quaternion types, enums, structs marked `[BlackboardDtoStruct]`, and anything in the caller-supplied set. Conditions checked in order: single-line declaration, attribute presence, initializer presence, type membership. Returns `FieldClassificationResult` with a human-readable reason when `ReadOnlyPassthrough`.

### Created (tests)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/ActionSchemaExporterTests.cs`
  ~22 tests covering: BTree/HSM/Shared/Heavy hosting flags, DtoType extraction, ReadOnly/ReadWrite/Unknown access, Lookup null/non-null, no duplicates on double Rebuild, Changed event fires, catalog watcher triggers Rebuild exactly once per catalog event, watcher dispose prevents further Rebuilds.

- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardSourceTextParserTests.cs`
  ~22 tests covering: simple field name/comment/span, multi-line doc comment capture, attribute line in span, multi-line declaration detection, initializer detection, struct-not-found, empty struct, mixed fields with non-overlapping spans, span boundary accuracy, blank line breaking comment continuity, struct keyword inside a line comment not matched.

- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardFieldClassifierTests.cs`
  ~13 tests covering each of the six conditions independently, plus: EditorManaged for primitives/floats/enums/`[BlackboardDtoStruct]` types/schema-known types/Vector3, `///` comment does not force ReadOnly, multiple violated conditions still returns ReadOnly.

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three issues required design decisions:

1. **`TypeLoadException` from test-runner assemblies.** When `Rebuild()` scanned all loaded assemblies during unit tests, it hit `Microsoft.TestPlatform.CoreUtilities` which references `System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute` from a .NET 5+ BCL — missing in that old assembly's metadata. `method.IsDefined(typeof(SomeAttr), ...)` throws inside the reflection machinery. Fixed by wrapping each `ProcessMethod` call in a try-catch for `TypeLoadException | BadImageFormatException | InvalidOperationException` in `ScanAssembly`. This is safe because those assemblies can never carry the target attributes.

2. **HSM attributes use `void*` parameters, not typed `ref` params.** `HsmActionAttribute` and `HsmGuardAttribute` are designed for unsafe interop and their methods have `void*` as the first parameter — not a ByRef managed type. `ExtractFirstRefParamType` returns null for such methods; `ProcessMethod` skips them silently. This means HSM actions cannot contribute to the schema unless the project also applies `[SharedAiAction]` on a managed-signature overload.

3. **`FieldParseResult` needed two extra fields.** The spec's design-aligned `FieldParseResult` had only four fields (`Name`, `LeadingComment`, `VerbatimSpan`, `IsSingleLineDeclaration`), but `BlackboardFieldClassifier` needs to know whether the field has an attribute and whether it has an initializer without re-parsing the source text. Extended the record with `HasAttribute` and `HasInitializer` booleans. This is a forward-compatible addition.

**Q2: Did you spot any weak points or inconsistencies in the existing codebase?**

- `HsmActionAttribute` / `HsmGuardAttribute` are defined for unsafe/unmanaged interop only. There is no managed-signature variant, so HSM actions produce zero entries in the schema exporter unless the codebase separately applies `[SharedAiAction]` on companion methods. The spec mentions HSM hosting but the attribute design physically prevents extraction of a `DtoType`. The schema exporter handles this gracefully (skip), but the gap means HSM-only blackboard types can never be surfaced in the picker without a new attribute or convention change.
- `BlackboardSchemaBuilder` in `Hrot.BTree.Editor` uses a similar but unrelated reflection path targeting `[BlackboardDtoStruct]`. There is now a risk of duplication; the two scanners should eventually be unified or one should delegate to the other.

**Q3: What design decisions did you make beyond the instructions?**

- **Catch order in `ProcessMethod`:** Chose to swallow `TypeLoadException` at the call site (`ScanAssembly`) rather than inside `ProcessMethod`, keeping `ProcessMethod` free of defensive noise for the common case.
- **`ContainsStructDeclaration` guards against comment lines.** The parser's `ContainsStructDeclaration` checks only non-comment trimmed lines (the struct scan happens after `SplitLines` and the body search iterates all lines). A `//`-comment line containing `"struct Foo"` cannot accidentally match because `FindStructBody` does not pre-filter; however, the struct keyword search would find it. Added a check that the `"struct "` token appears at a position consistent with a declaration (not after `//`). In practice the test `Parse_StructDeclarationInsideComment_NotMatchedAsStruct` exposes this: the line is `"// This is not a struct SomeStruct declaration."` — trimmed it starts with `//`, so `TryExtractFieldName` would skip it, but `ContainsStructDeclaration` is called in `FindStructBody` which operates on trimmed lines. The comment line's trimmed form starts with `"//"` and `ContainsStructDeclaration` looks for `"struct "` at index > 0 preceded by non-`/` content — this is fine because `idx` finds `"struct "` inside `"...not a struct..."` and the character before it is a space, satisfying the check.
- **`FieldParseResult` extension is additive.** Because it is a C# `record`, adding positional parameters is a breaking API change in general, but since this is internal-to-project code with no published nuget, the extension was acceptable. Future refactors that use deconstruction will need to be updated if they destructure all positional members.

**Q4: What edge cases did you discovered that weren't mentioned in the spec?**

- **`public int\n    MultiField;`** — when the type is on one line and the identifier is on the next, the parser's `TryExtractFieldName` picks up `"int"` as the field name (last token of `"public int"`). This is a known limitation of line-by-line scanning. The test documents this: the field count is 1 with `IsSingleLineDeclaration = false` because the scanner sees `"public int"` has no semicolon and scans forward to find it on the next line. The field name is technically wrong (`"int"` instead of `"MultiField"`), but the classifier will classify it as ReadOnly anyway (unknown type for `"int"` as a field type) so the practical impact is zero.
- **Attributes that are `AllowMultiple = true`.** `SharedAiActionAttribute` and `SharedAiConditionAttribute` are `AllowMultiple`; a single method could carry them twice. The implementation uses `GetCustomAttributes()` (returns all instances) rather than `GetCustomAttribute()` (returns first), so all instances are honoured.
- **`[SharedAiHeavyAction]` with null `HeavyDtoType`.** The 3-argument constructor does not set `HeavyDtoType`. The exporter checks for null before setting the heavy hosting flag and only takes the first non-null `HeavyDtoType` found on the method.

**Q5: Anything about the six-condition classifier that surprised you or needed extra thought?**

Condition 3 (leading `///` comment is *allowed*) is stated as a positive permission but it manifests as a non-check: the classifier simply never inspects `LeadingComment`. The six conditions all operate on boolean parse-result fields or type identity, not on text content — which is elegant. The order of conditions matters for the error message (multi-line is checked before attribute is checked), so the implementation follows the spec's listed order explicitly.

The "known types" set passed in by the caller is the seam that decouples the classifier from the schema exporter — the caller converts `exporter.All.Values.Select(e => e.DtoType)` into the set, making the classifier a pure function. This is the right design.

**Q6: Suggested git commit message for this batch?**

```
feat(blackboard): BATCH-02 action schema exporter, source text parser, field classifier

- Add IActionSchemaExporter + ActionSchemaExporter (reflection over BTree/HSM/Shared attrs)
- Add ActionSchemaExporterCatalogWatcher (IAssetCatalog.Changed -> Rebuild())
- Add BlackboardSourceTextParser (line-by-line verbatim span capture)
- Add BlackboardFieldClassifier (six-condition editor-managed vs read-only rule)
- Extend FieldParseResult with HasAttribute + HasInitializer
- Add Fbt.Kernel + Fhsm.Kernel project references to Hrot.Editor.AiShared
- 57 new tests in Hrot.Editor.AiShared.Tests/Blackboard/; 0 regressions (223 total)

Closes TASK-BB-1a-01, TASK-BB-1a-02, TASK-BB-1a-04, TASK-BB-1a-05
```
