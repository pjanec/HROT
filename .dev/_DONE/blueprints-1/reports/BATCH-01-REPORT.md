# BATCH-01-REPORT

## Tasks Completed

| Task | Title | Status |
|---|---|---|
| TASK-P0-001 | Project Skeleton | COMPLETE |
| TASK-P0-002 | Asset Schema Types | COMPLETE |
| TASK-P0-003 | Round-Trip Serialization Tests | COMPLETE |

---

## Test Results

```
Passed!  - Failed: 0, Passed: 29, Skipped: 0, Total: 29, Duration: 76 ms
```

### Test breakdown

**PlaceholderTests** (pre-existing, unchanged)
- `Placeholder_AlwaysPasses` -- PASS

**SchemaReflectionTests** (TASK-P0-002 SC2/SC3/SC4)
- `ConcreteNodeSubtypeCount_Is19` -- PASS
- `DiscriminatorRoundTrip_EachNodeKind` x 19 (one Theory per node type) -- PASS x 19
- `UnknownFieldsTolerance_DoesNotThrow` -- PASS
- `MissingFieldsDefaultToEmpty` -- PASS

**AssetJsonRoundTripTests** (TASK-P0-003 SC2-SC7)
- `LibraryDispatch_RoundTrip` -- PASS
- `AiPrimitive_RoundTrip` -- PASS
- `Instance_RoundTrip` -- PASS
- `AllNodeTypes_PolymorphicRoundTrip` -- PASS
- `UnknownField_Tolerated` -- PASS
- `MissingFields_DefaultToEmpty` -- PASS

---

## Developer Insights

**1. How was the `[JsonPolymorphic]` / frozen `DefaultRelaxed` conflict resolved?**

`FdpJsonOptionsRegistry.DefaultRelaxed` is sealed with `MakeReadOnly()`. Attempting to register a `DefaultJsonTypeInfoResolver` on it (required by `[JsonPolymorphic]`) throws at runtime. The resolution is the "CreateExtended workaround": in `BlueprintJsonServices`'s static constructor, a new `JsonSerializerOptions` instance is created by copying all six relevant scalar properties from `DefaultRelaxed` and re-adding all its converters. A fresh `new DefaultJsonTypeInfoResolver()` is then assigned to the copy. This unfrozen copy can handle `[JsonPolymorphic]`/`[JsonDerivedType]` attribute metadata without conflict.

**2. Why does `Hrot.Blueprints.Generators` not reference `Hrot.Blueprints.Core`?**

The Generators project targets `netstandard2.0` (required by the Roslyn analyzer pipeline), while `Hrot.Blueprints.Core` targets `net8.0`. A `netstandard2.0` project cannot directly reference a `net8.0` project via `ProjectReference` during NuGet restore. The generator performs structural validation of `.bp.json` files (checking the opening `{`) purely via file content; it does not need to instantiate or reflect over `BlueprintAsset`. Introducing a shared `netstandard2.0` contracts assembly was deferred to a future task.

**3. How were the 19 discriminator strings determined?**

All discriminator strings were taken verbatim from Architecture document §5.3 (Blueprint_Subsystem_Architecture_v1.2.md), which lists both the C# class name and the exact JSON discriminator string for each node type. They are declared using `[JsonDerivedType(typeof(XNode), "discriminator")]` on the `Node` abstract base class.

**4. Was `InternalsVisibleTo` needed between test and core projects?**

No. All types in `Hrot.Blueprints.Core` and `Hrot.Blueprints.Core.Assets` are declared `public`. The test project accesses them entirely through the public API. No internal cross-assembly access is needed.

**5. Why is `JsonAestheticFormatter.FlattenNumericArrays` not used in the round-trip tests?**

`JsonAestheticFormatter` is implemented in `Fdp.Toolkits` and uses Newtonsoft.Json internally. The `Hrot.Blueprints.Tests.csproj` references only `Hrot.Blueprints.Core` and `Fdp.Core`; adding `Fdp.Toolkits` would introduce an out-of-scope dependency. Since `BlueprintJsonServices` uses `WriteIndented = false` (inherited from `DefaultRelaxed`) and `System.Text.Json` produces deterministic compact output with properties written in declaration order, a direct `string == string` comparison of `Serialize(asset)` and `Serialize(Deserialize(Serialize(asset)))` is sufficient and fully verifies round-trip fidelity.

---

## Deviations from Spec

None. All three tasks were implemented per the BATCH-01-INSTRUCTIONS.md specification.

---

## Issues / Technical Debt

- **Generators do not reference Core**: The `BlueprintIncrementalGenerator` only checks that `.bp.json` files start with `{`. Full structural validation (schema version check, type discrimination) is deferred to a future task that will introduce a shared `netstandard2.0` contracts assembly.
- **`NodeStatus` enum**: Defined in `GraphTypes.cs` alongside the graph shape types. It is not yet consumed by any code. Intended for future AI behavior tree integration.
- **`BlueprintAsset` does not enforce `Dispatch`-specific invariants**: E.g., `Primitive` should only be non-null when `Dispatch == AiPrimitive`. These constraints are not enforced at the model level; enforcement is deferred to a validation pass in a later milestone.

---

## Build Verification

```
dotnet build IOS-IG-SimHost.sln
  Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All Blueprint projects:
- `Hrot.Blueprints.Core` -- 0 errors, 0 warnings
- `Hrot.Blueprints.Tests` -- 0 errors, 0 warnings
- `Hrot.Blueprints.Generators` -- 0 errors, 0 warnings
- `Hrot.AI.Behaviors` (consumer) -- 0 errors, 0 warnings
