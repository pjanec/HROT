# BATCH-04 Review

**Status: APPROVED**  
**Reviewer: Dev Lead**  
**Tasks reviewed:** TKB-011

---

## Implementation Quality

### Tkb.SourceGen.csproj

APPROVED. Matches the `Fbt.SourceGen.csproj` pattern exactly:
- `netstandard2.0`, `IsRoslynComponent=true`, `EnforceExtendedAnalyzerRules=true`
- `Microsoft.CodeAnalysis.CSharp 4.8.0` with `PrivateAssets="all"`
- `Microsoft.CodeAnalysis.Analyzers 3.3.4` with `PrivateAssets="all"`
- `NoWarn>CS8632` (nullable annotations netstandard2.0 warning)

### TkbDescriptorGenerator.cs

APPROVED. Key correctness points verified:
- `[Generator]` + `IIncrementalGenerator` — correct attribute.
- Uses `SyntaxProvider.CreateSyntaxProvider` with predicate on `TypeDeclarationSyntax` +
  non-empty `AttributeLists`.
- Semantic resolution uses `GetTypeByMetadataName(TkbDescriptorAttributeMetadataName)` —
  correct approach that avoids string-based attribute name matching after model resolution.
  Uses `SymbolEqualityComparer.Default.Equals` for attribute class comparison — correct.
- Extracts `HierarchicalName` from first constructor argument via `ConstructorArguments[0]`.
- `SanitizeIdentifier` replaces `.` and other non-identifier chars with `_`.
- Duplicate detection via case-insensitive dictionary — first registration wins, duplicate
  emits `TKB001` Warning diagnostic.
- `GenerateSource` uses `global::` prefix on all type references — correct for generated code.
- Generated file name `__TkbDescriptors_{name}.g.cs` matches spec.
- `internal static class` — correctly scoped.

**Minor deviation from spec (ACCEPTED):** Generated thunk uses
`JsonSerializer.Deserialize<T>(jsonElement, options)` instead of `jsonElement.Deserialize<T>(options)`.
Both are functionally equivalent in .NET 8 (the extension method calls `JsonSerializer.Deserialize`
internally). No behavior difference.

### Fdp.Toolkits.csproj changes

APPROVED. Analyzer reference correctly added with `OutputItemType="Analyzer"` and
`ReferenceOutputAssembly="false"`.

### Generated file verification

Generated file `__TkbDescriptors_Fdp_Toolkits.g.cs` confirmed at:
`Fdp.Toolkits/obj/Debug/net8.0/generated/Tkb.SourceGen/Fdp.Toolkit.SourceGen.TkbDescriptorGenerator/`

All 4 DTOs registered: `TkbMasterDto`, `VehicleParametersDto`, `WeaponCapabilitiesDto`,
`AmmoWeaponBallisticsDto`. All use `global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed`.

---

## Test Quality

### TkbDescriptorGeneratorTests.cs (5 tests)

APPROVED.

| Test | Coverage | Verdict |
|---|---|---|
| `Generator_SingleType_EmitsRegisterParserCall` | Verifies RegisterParser call, name, and type FQN | OK |
| `Generator_SingleType_EmitsModuleInitializer` | Verifies [ModuleInitializer] + Register method name | OK |
| `Generator_NoDescriptorTypes_EmitsNoFile` | Empty output for unannotated types | OK |
| `Generator_DuplicateHierarchicalName_EmitsWarning` | TKB001 diagnostic at Warning severity | OK |
| `Generator_MultipleTypes_AllRegistered` | Three distinct names all appear in output | OK |

Tests are pure Roslyn compilation tests — no dependency on static `TkbDescriptorRegistry`.
No `[Collection]` isolation needed. Clean, deterministic.

---

## Issues Found

None blocking.

---

## Decision

**APPROVED — build clean (0 errors), 99/99 TKB tests pass (5 new generator tests), generated file verified with 4 DTOs registered.**
