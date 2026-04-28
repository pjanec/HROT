# BATCH-01 Report: StructEdit Core Extensions

**Batch Number:** BATCH-01
**Tasks:** TASK-CE01, TASK-CE02, TASK-CE03
**Date:** 2026-04-24
**Status:** COMPLETE

---

## Implementation Summary

### Files Created

| File | Description |
|---|---|
| `FDP/ExtDeps/StructEdit/src/StructEdit.Core/Bindings/NestedMemberBinding.cs` | New binding type (CE01) |
| `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/NestedMemberBindingTests.cs` | CE01 tests (3 tests) |
| `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/MetadataTests.cs` | CE02 tests (4 tests) |
| `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/Reflection/ArrayElementNodeTests.cs` | CE03 tests (6 tests) |

### Files Modified

| File | Change |
|---|---|
| `FDP/ExtDeps/StructEdit/src/StructEdit.Core/EditNodeMetadata.cs` | Added `CustomAttributes` property |
| `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/ReflectionEditDocumentBuilder.cs` | Extended `BuildNode`, `BuildChildren`, `CreateLeafBinding`; added `BuildArrayElements` |
| `FDP/ExtDeps/StructEdit/tests/StructEdit.Tests/StructEdit.Tests.csproj` | Fixed pre-existing missing `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` |

---

## Q1: Issues Encountered and Resolutions

**Pre-existing broken test project.** The test project was missing `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in its `.csproj`. Every build command without explicit restore failed with `CS0227: Unsafe code may only appear if compiling with /unsafe` for all test files that use `unsafe` structs (fixed buffers, `NativeStructEditBuffer`, etc.). The fix was a one-line addition to `StructEdit.Tests.csproj`. Without this, no test runs were possible via `dotnet test --no-restore`.

**`DynamicArray` case naming clash.** The original `DynamicArray` switch arm used a local variable named `parentBinding` (for the binding of the array field itself). After adding the `parentBinding` optional parameter to `BuildNode`, this created a shadowing ambiguity. Renamed the local to `fieldBinding` to clearly distinguish the two concepts.

**`BuildArrayElements` calling convention.** The spec says each element node is built with `nativeOffset: -1, fi: null, pi: null`. The existing `CreateLeafBinding` already has `if (fi == null && pi == null) return null;` at the top, so for element nodes the `default` branch returns null. The fix: after the switch, `if (binding == null && explicitBinding != null) binding = explicitBinding;` ensures the element binding is not lost.

---

## Q2: Ambiguities and Interpretations

**What is `parentBinding` in `BuildArrayElements`'s call to `BuildNode`?**
The spec says "pass the result as `explicitBinding`" and separately says "the recursive `BuildChildren` call must receive the element binding as `parentBinding`". Inside `BuildNode`, for `Struct` kind with `explicitBinding != null`, I pass `parentBinding: explicitBinding` to `BuildChildren` so that leaf fields inside the struct element are backed by `NestedMemberBinding`. I pass `parentBinding: null` from `BuildArrayElements` to `BuildNode` (the element-node call) since the element's own fields are the only level that needs `parentBinding`.

**Should struct element nodes carry `binding = explicitBinding`?**
The spec success conditions (`T-CE03b`) only verify the children's bindings for struct elements, not the element node's binding itself. However, setting `binding = explicitBinding` on the struct element node is harmless and consistent (primitive element nodes require it for correct `GetBoxed`). I apply it to all element kinds uniformly.

**`ReadMetadata` for `EditReadOnlyAttribute`.** The spec says to filter out "the ones already handled" (EditRange, EditUnit, EditDisplayName, InlineArrayHint, FixedBufferHint). `EditReadOnlyAttribute` is also a StructEdit internal attribute (defined in `EditAttributes.cs`) and is not a domain-level custom attribute, so I included it in the filter list to avoid leaking it into `CustomAttributes`.

---

## Q3: Edge Cases Discovered

**`null` array container.** When `DynamicArrayBinding`'s container is null (field is a null array reference), the existing `parentBinding.GetBoxed()` returns null and the `if (container != null)` guard prevents `DynamicArrayBinding` construction. This means `binding = null` and `children = null` (empty). `T-CE03f` covers this path and confirms zero children, no exception.

**Property-based array fields.** `BuildChildren` propagates `parentBinding` to both field-based and property-based `BuildNode` calls. `CreateLeafBinding` with `parentBinding != null && nativeOffset < 0` creates `NestedMemberBinding` for `PropertyInfo` members as well as `FieldInfo` members via the `MemberInfo member = fi ?? (MemberInfo)pi!` pattern.

**`nativeOffset < 0` sentinel and native buffers.** For `InlineArray` and `FixedBuffer` element nodes, the element bindings are `NativeFieldBinding` (already compute correct absolute offsets). These are passed as `explicitBinding` and the `nativeOffset: -1` sentinel only affects `CreateLeafBinding`. For InlineArray/FixedBuffer, the element type is typically a primitive (float, byte), so `kind = Scalar` and the `default` branch runs; `CreateLeafBinding` is called with `parentBinding: null` (passed from `BuildArrayElements`), so the `!buffer.IsNative && parentBinding != null && nativeOffset < 0` condition is false, and the `NativeFieldBinding` path would normally apply — but since `fi == null && pi == null`, the existing guard returns null, and the `explicitBinding` fallback provides the correct native binding. No incorrect `NestedMemberBinding` is ever constructed for native element types.

---

## Q4: Weak Points and Design Smells

1. **`nativeOffset = -1` as a sentinel.** Using a magic value (-1) as a "managed element path" signal is fragile. A dedicated `BuildNode` overload (or an enum discriminator) would be clearer. The current approach works because native buffer offsets are always non-negative, but it is an implicit contract rather than an explicit one.

2. **`BuildNode` parameter list is long.** The method already has 13 parameters before the new optional ones. This is a smell that the builder is doing too much in one method and would benefit from a context object (`BuildContext`) grouping the stable parameters (`buffer`, `idAlloc`, `visited`, `providers`, `fieldEditors`, `context`).

3. **`IContainerBinding.GetElementBinding` returns a fresh binding on every call.** For `DynamicArrayBinding`, each call to `GetElementBinding(i)` allocates a new `ArrayElementBinding` that captures the container reference at that moment. This is correct but means that after `Resize`, the old `ArrayElementBinding` objects captured before the resize still point to the old container. `RebuildDocument` is the contract for refreshing, but there's no enforcement. A test that mutates via a stale `ArrayElementBinding` after `Resize` would silently write to the pre-resize array.

4. **`EditNodeMetadata` is a `sealed record`.** Using `record` syntax for a value object with optional properties is reasonable, but the `CustomAttributes` property cannot use the primary-constructor parameter shorthand. The current approach (explicit property with init setter) works correctly.

---

## Q5: Commit Message

```
feat(struct-edit-1): Phase 1 StructEdit core extensions (BATCH-01)

CE01 - NestedMemberBinding: new internal sealed class in StructEdit.Core.Bindings.
  Wraps an existing IValueBinding and exposes one public field or property of its
  parent value. SetBoxed writes back to the parent when the parent holds a value
  type (struct copy-on-box correctness). Handles both FieldInfo and PropertyInfo.

CE02 - EditNodeMetadata.CustomAttributes: new IReadOnlyList<Attribute> property
  defaulting to Array.Empty<Attribute>(). ReadMetadata now harvests all non-StructEdit
  attributes and stores them in CustomAttributes, enabling opaque flow of domain
  attributes ([MapPickableEntity], etc.) to the UI renderer.

CE03 - Array element node generation in ReflectionEditDocumentBuilder:
  BuildNode gains explicitBinding/parentBinding optional parameters.
  New BuildArrayElements helper generates one EditNode per IContainerBinding element.
  DynamicArray, InlineArray, FixedBuffer cases now call BuildArrayElements and
  store the result as children. BuildChildren propagates parentBinding so leaf fields
  inside managed struct array elements are backed by NestedMemberBinding.
  CreateLeafBinding detects the managed-element path (parentBinding != null &&
  nativeOffset < 0) and returns NestedMemberBinding instead of ManagedFieldBinding.

Also fixed: StructEdit.Tests.csproj was missing <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  (pre-existing; tests could not build without a full restore).

Tests added (13 new, all pass):
  NestedMemberBindingTests: T-CE01a, T-CE01b, T-CE01c
  MetadataTests:            T-CE02a, T-CE02b, T-CE02c, T-CE02d
  ArrayElementNodeTests:    T-CE03a, T-CE03b, T-CE03c, T-CE03d, T-CE03e, T-CE03f

Total StructEdit.Tests: 184 passed, 0 failed.
```

---

## Test Results

| Task | Tests | Result |
|---|---|---|
| CE01 (NestedMemberBinding) | T-CE01a, T-CE01b, T-CE01c | 3/3 PASS |
| CE02 (CustomAttributes) | T-CE02a, T-CE02b, T-CE02c, T-CE02d | 4/4 PASS |
| CE03 (Array Element Nodes) | T-CE03a, T-CE03b, T-CE03c, T-CE03d, T-CE03e, T-CE03f | 6/6 PASS |
| Pre-existing StructEdit.Tests | 171 tests | 171/171 PASS |
| **Total** | **184 tests** | **184/184 PASS** |

`dotnet test IOS-IG-SimHost.sln` — running at time of report submission (StructEdit.Tests is not part of the main solution; the Hrot/FDP integration projects are unaffected by StructEdit changes since no files outside `FDP/ExtDeps/StructEdit/` were modified).
