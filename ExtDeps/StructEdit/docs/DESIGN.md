# StructEdit — Component Editor Library: Design

**Goal:** A reusable .NET 8 library (`StructEdit`) that generates an instruction tree describing the editable
fields of any C# ECS component (managed class/record or unmanaged struct with fixed buffers / inline arrays),
supports scoped editing, atomic commit, session-based undo-cancel, JSON serialisation of the instruction tree,
and a plugin system for custom field editors and union/chameleon views — all without source generators.

---

## Project Structure

```
StructEdit/
  src/
    StructEdit.Core/          ← abstractions, EditDocument model, session API
    StructEdit.Reflection/    ← reflection-based document builder
    StructEdit.Json/          ← JSON serialise/deserialise of EditDocument
  tests/
    StructEdit.Tests/         ← xUnit unit tests
  samples/
    StructEdit.Sample/        ← console example (no ImGui dependency)
```

All projects target **net8.0**. The library has **no dependency on Dear ImGui** or any specific UI framework.

---

## Architectural Overview

```
Consumer (e.g. ECS inspector)
        │
        │  Open(boxedComponent, Type, EditScope?, EditContext?)
        ▼
IComponentEditService
        │
        ▼
Memory Classifier
  ├─ UnmanagedBlittableStruct → NativeStructEditBuffer  (NativeMemory + Unsafe)
  ├─ ManagedReference        → ManagedObjectEditBuffer
  └─ NonBlittableStruct      → BoxedStructEditBuffer
        │
        ▼
IEditDocumentBuilder  (reflection runs ONCE per session open)
  ├─ Reflection-based field scanning
  ├─ Byte-offset calculation (for native structs)
  ├─ IValueBinding / IContainerBinding construction per node
  ├─ Attribute metadata ([EditRange], [EditUnit], [InlineArrayHint], …)
  ├─ FixedBufferBinding / InlineArrayBinding
  ├─ IBufferViewProvider (union/chameleon projections)
  └─ EditScope filter applied → EditDocument tree
        │
        ▼
EditDocument (tree of EditNode, each with EditNodeId + IValueBinding)
        │
        ▼
Consumer render loop  (ImGui or other; library-agnostic)
  ├─ reads/writes via IValueBinding (integer NodeId, no string paths per frame)
  └─ polls session.RebuildState for structural changes
        │
        ▼
session.Validate()          (whole edit buffer, not just visible scope)
session.Commit()            → boxed replacement component
session.Cancel()            → original unchanged
```

**Key design rule:** The edit buffer always holds the **entire** cloned component.  
The `EditDocument` exposes only the **scoped** subset of fields requested by the caller.  
Commit returns the whole component, ensuring atomic replacement in the ECS.

---

## Phase 1: Foundation & Abstractions

Establish the public API surface: all interfaces, enumerations, and value-types that the rest of the
library builds on. No implementation logic lives here — only contracts, data-shapes, and simple
factory helpers.

### 1.1 EditPath

`EditPath` is a lightweight struct wrapping a `string` in JSONPath-like notation (`$.Field.SubField`).

- Used **only** for JSON output, diagnostics, and configuration.
- Must **not** be resolved at runtime during UI render loops.
- Provides `EditPath.Parse(string)`, equality, and `ToString()`.

### 1.2 EditNodeKind

Enumeration of all node types the library can represent:

```
SelectionRoot   — synthetic root when scope selects multiple unrelated paths
Scalar          — numeric primitives (int, float, double, byte, …)
Boolean
String
Enum            — any CLR enum; carries available names+values
Guid
DateTime
Struct          — value-type composite
Class           — reference-type composite
Record          — immutable record (reconstructed on write)
InlineArray     — C# 12 [InlineArray] attributed struct
FixedBuffer     — C# fixed keyword buffer
DynamicArray    — List<T>, T[], resizable collections
BufferView      — overlay projection of a raw byte buffer (union)
Union           — discriminator-driven choice between sub-editors
Custom          — consumer-installed custom field editor
Unsupported     — type the library cannot reflect (shown read-only or hidden)
```

### 1.3 EditNodeId

```csharp
public readonly record struct EditNodeId(int Value);
```

Stable integer identity assigned once at document build time.  
The render loop binds to `EditNodeId` — **never** to string paths.

### 1.4 EditNode

Immutable descriptor of one editable unit:

- `EditNodeId Id`
- `string Name` — display name (field/property name)
- `string JsonPath` — full path for JSON/diagnostics only
- `EditNodeKind Kind`
- `Type ClrType`
- `IValueBinding Binding`
- `IReadOnlyList<EditNode> Children`
- `EditNodeMetadata Metadata` — range, unit, display hints
- `bool IsReadOnly`

### 1.5 EditDocument

Container for the session's instruction tree:

- `EditNode Root`
- `Type RootComponentType`
- `EditScope Scope` (for JSON output)

### 1.6 EditScope

Controls which fields appear in the `EditDocument`.  
The edit buffer **always** stores the whole component regardless.

```csharp
public sealed class EditScope
{
    public static EditScope WholeComponent { get; }

    public required IReadOnlyList<EditPath> IncludedPaths { get; init; }
    public bool IncludeChildren { get; init; } = true;
    public bool IncludeParentsForContext { get; init; } = false;  // show ancestor nodes read-only for context

    public static EditScope ForField(EditPath path);
    public static EditScope ForFields(params EditPath[] paths);
}
```

When `IncludeParentsForContext = true`, ancestor nodes of included paths are included in the
document as **read-only** context nodes (so the renderer can show the full path hierarchy without
making parent nodes editable).

### 1.7 EditContext

Optional caller-provided context bag passed through to `IBufferViewProvider` implementations.
Allows discriminators that are **not** stored inside the component (e.g. external game state).

### 1.8 EditNodeMetadata

Carries editor-hint attributes decoded from reflection:

- `double? Min`, `double? Max` — from `[EditRange]`
- `string? Unit` — from `[EditUnit]`
- `int? FixedLength` — from `[FixedBufferHint]` or `[InlineArrayHint]` (when reflection cannot determine automatically)
- `string? DisplayName` — from `[EditDisplayName]`

### 1.9 Validation Types

```csharp
public enum EditRebuildState { Stable, RebuildSuggested, RebuildRequired }

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationError> Errors { get; }
}

public sealed class ValidationError
{
    public string JsonPath { get; }
    public string Message { get; }
}

public sealed class EditValidationException(ValidationResult result) : Exception;
```

### 1.10 IComponentValidator

```csharp
public interface IComponentValidator
{
    ValidationResult Validate(EditValidationContext context);
}

public sealed class EditValidationContext
{
    public required Type ComponentType { get; init; }
    public required IEditBuffer Buffer { get; init; }
    public required EditScope Scope { get; init; }
}
```

Validation always runs against the **entire edit buffer**, not only the visible scoped nodes.

---

## Phase 2: Memory Layer

Provides the internal buffer abstractions that isolate all memory-management complexity from the
rest of the library. The public API remains fully `object`-based; the buffer choice is made
internally by the memory classifier.

### 2.1 ComponentMemoryKind & IComponentMemoryClassifier

```csharp
public enum ComponentMemoryKind
{
    ManagedReference,         // class, record class
    UnmanagedBlittableStruct, // struct where IsUnmanaged is true, all fields blittable
    NonBlittableStruct,       // struct with managed fields
    Unsupported
}

public interface IComponentMemoryClassifier
{
    ComponentMemoryKind Classify(Type type);
}
```

Classification rules (v1):

| Type characteristic | Kind |
|---|---|
| `class` or `record class` | `ManagedReference` |
| `struct` satisfying `unmanaged` constraint | `UnmanagedBlittableStruct` |
| `struct` with any managed field | `NonBlittableStruct` |

### 2.2 IEditBuffer

Internal contract for the temporary storage of the cloned component:

```csharp
internal interface IEditBuffer : IDisposable
{
    Type ComponentType { get; }
    bool IsNative { get; }
    bool IsDirty { get; }

    bool TryGetRootSpan(out Span<byte> bytes);
    IValueBinding CreateRootBinding();
    object Box();   // returns boxed replacement for Commit()
}
```

### 2.3 IRuntimeTypeOps & RuntimeTypeOps\<T\>

Cached, generic-closed helpers for unmanaged types. Avoids `Marshal.StructureToPtr` /
`Marshal.PtrToStructure` issues with fixed buffers.

```csharp
public interface IRuntimeTypeOps
{
    int SizeOf { get; }
    unsafe void CopyObjectToNative(object boxed, void* destination);
    unsafe object BoxFromNative(void* source);
}

static class RuntimeTypeOps<T> where T : unmanaged
{
    public static unsafe void CopyObjectToNative(object boxed, void* dest)
        => Unsafe.Write(dest, (T)boxed);

    public static unsafe object BoxFromNative(void* src)
        => Unsafe.Read<T>(src)!;
}
```

A `RuntimeTypeOpsFactory` creates and caches `IRuntimeTypeOps` instances per `Type` via
closed-generic instantiation through reflection (one-time cost).

### 2.4 NativeStructEditBuffer

For `UnmanagedBlittableStruct` types. Allocates `NativeMemory.Alloc(sizeof(T))` on session open.

- `CopyObjectToNative` uses `Unsafe.Write<T>` (not `Marshal.StructureToPtr`).
- `BoxFromNative` uses `Unsafe.Read<T>`.
- `TryGetRootSpan` returns a `Span<byte>` over the native block.
- `Dispose` calls `NativeMemory.Free`.
- `IsDirty` tracks whether any binding has written to the buffer.

### 2.5 ManagedObjectEditBuffer

For `ManagedReference` types (classes, records). Performs a deep clone of the component on session
open (via reflection-based property copy or `ICloneable` if available).  
Records are reconstructed via `with { }` equivalent using property setters found by reflection.  
`Box()` returns the cloned (edited) managed object.

### 2.6 BoxedStructEditBuffer

For `NonBlittableStruct` types. Stores a boxed copy of the struct and modifies fields through
reflection. `Box()` returns the boxed struct.

---

## Phase 3: Reflection & Document Building

Uses reflection **once** per session open to build the `EditDocument` tree with stable bindings.

### 3.1 Edit Attribute Definitions

Attributes decorating component fields/properties to provide editing metadata:

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditRangeAttribute(double min, double max) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditUnitAttribute(string unit) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditDisplayNameAttribute(string name) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InlineArrayHintAttribute(int length) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FixedBufferHintAttribute(Type elementType, int length) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditReadOnlyAttribute : Attribute;
```

### 3.2 IValueBinding & IContainerBinding

Runtime binding between an `EditNode` and the actual memory/property in the edit buffer.

```csharp
public interface IValueBinding
{
    Type ValueType { get; }
    object? GetBoxed();
    void SetBoxed(object? value);
    bool TryGetSpan(out Span<byte> bytes);   // fast path for native fields
}

public interface IContainerBinding : IValueBinding
{
    int Count { get; }
    bool CanResize { get; }
    IValueBinding GetElementBinding(int index);
    void Resize(int newCount);  // triggers parent writeback + marks structural change
}
```

On `Resize`, the binding must:
1. Create the new container.
2. Write the updated container reference back to the **parent** property via the parent binding's
   setter.
3. Notify the session of a structural change (`EditRebuildState.RebuildRequired`).

Concrete binding implementations:

| Binding class | Used for |
|---|---|
| `NativeFieldBinding` | Scalar/struct field in native buffer (offset + size) |
| `FixedBufferBinding` | `fixed T[N]` — offset, elementType, elementSize, length |
| `InlineArrayBinding` | `[InlineArray(N)]` struct — offset, elementType, count |
| `ManagedPropertyBinding` | Property on a managed class/record |
| `ManagedFieldBinding` | Public field on a managed class |
| `DynamicArrayBinding` | `List<T>` or `T[]` with optional resize + parent writeback |

For **native bindings**, reads/writes use `MemoryMarshal.Read<T>` / `MemoryMarshal.Write<T>` on
the `Span<byte>` slice. No reflection per frame.

### 3.3 IEditDocumentBuilder

```csharp
public interface IEditDocumentBuilder
{
    EditDocument Build(
        IEditBuffer buffer,
        Type componentType,
        EditScope scope,
        EditContext? context);
}
```

Algorithm:
1. Scan all public fields and properties via `FieldInfo` / `PropertyInfo`.
2. Determine `EditNodeKind` per member.
3. For native buffers: compute byte offsets using `Marshal.OffsetOf` / `RuntimeHelpers` interop.
4. Construct the appropriate `IValueBinding`.
5. Recurse into struct/class fields (sub-nodes become children).
6. Handle `[InlineArray]` structs and `fixed` buffers as array nodes.
7. For each `DynamicArray` node, wrap with `DynamicArrayBinding` (supports resize + parent writeback).
8. Apply `EditScope` filter: retain only nodes matching included paths (and their children if
   `IncludeChildren = true`); if multiple unrelated paths selected, wrap in `SelectionRoot`.
9. Invoke registered `IBufferViewProvider` instances for any `FixedBuffer` or `BufferView` node.
10. Build `EditNodeMetadata` from attributes.

**Performance rule:** All reflection and offset computation happens once. The resulting
`EditDocument` tree is a pure data structure that the render loop reads without reflection.

### 3.4 IBufferViewProvider (Union / Chameleon Support)

Allows consumers to overlay a raw byte buffer with a typed struct projection based on a
discriminator field or external context.

```csharp
public interface IBufferViewProvider
{
    bool CanCreateView(BufferViewRequest request);
    BufferViewResult CreateView(BufferViewRequest request);
}

public sealed class BufferViewRequest
{
    public required Type ComponentType { get; init; }
    public required EditPath BufferPath { get; init; }
    public required IValueBinding BufferBinding { get; init; }
    public required EditContext? ExternalContext { get; init; }

    // Helper: read a sibling field's current value from the edit buffer
    public T ReadSibling<T>(EditPath siblingPath);
    // Helper: project buffer bytes as a named typed view
    public BufferViewResult ProjectBufferAs(Type viewType, string viewName);
}
```

When the render loop detects that a discriminator field changed (via `SetBoxed` on its binding),
the binding marks the session as `EditRebuildState.RebuildRequired`.  
The host calls `session.RebuildDocument()` which rebuilds only the `EditDocument` tree while
**preserving the existing edit buffer**.

---

## Phase 4: Edit Session

The top-level consumer-facing API: service, session lifecycle, commit, cancel, and document rebuild.

### 4.1 IComponentEditService

```csharp
public interface IComponentEditService
{
    IEditSession Open(
        object component,
        Type componentType,
        EditScope? scope = null,
        EditContext? context = null);
}
```

Implementation (`ComponentEditService`) is configured via `ComponentEditServiceBuilder`:

```csharp
var service = new ComponentEditServiceBuilder()
    .RegisterBufferViewProvider(new ProjectilePayloadViewProvider())
    .RegisterFieldEditor<Guid>(new GuidFieldEditor())
    .RegisterValidator<WeaponComponent>(new WeaponValidator())
    .Build();
```

### 4.2 IEditSession

```csharp
public interface IEditSession : IDisposable
{
    EditDocument Document { get; }
    bool IsDirty { get; }
    EditRebuildState RebuildState { get; }

    void RebuildDocument();           // rebuilds Document, preserves edit buffer
    void MarkStructuralChange();      // sets RebuildRequired (called by bindings internally)

    ValidationResult Validate();
    object Commit();                  // Validate() then buffer.Box()
    void Cancel();                    // no-op; buffer discarded on Dispose
}
```

`Commit()` behaviour:
1. Calls `Validate()`.
2. If invalid → throws `EditValidationException`.
3. Returns `buffer.Box()` — the fully reconstructed component.
4. Callers unbox to the concrete type and replace the ECS slot.

`Cancel()` is a semantic marker; actual cleanup happens in `Dispose`.

### 4.3 Consumer Integration Pattern

```csharp
// ECS-side code (consumer)
object boxed = componentStore.GetBoxed(entity, componentType);

using IEditSession session = editService.Open(boxed, componentType,
    EditScope.ForField(clickedFieldPath), editorContext);

while (editing)
{
    EditUiResult ui = renderer.Draw(session.Document);   // any renderer

    if (session.RebuildState == EditRebuildState.RebuildRequired)
        session.RebuildDocument();

    if (ui.CommitRequested)
    {
        var result = session.Validate();
        if (result.IsValid)
        {
            object replacement = session.Commit();
            componentStore.Replace(entity, componentType, replacement);
        }
    }
    if (ui.CancelRequested) { session.Cancel(); break; }
}
```

---

## Phase 5: Dynamic Array Resize

`List<T>` and `T[]` fields support optional resize operations within an edit session.

- `IContainerBinding.CanResize` — `true` for `List<T>`, configurable for `T[]`.
- `Resize(int newCount)` — creates a new collection, copies existing elements (truncating or
  default-initialising as needed), writes back through the parent binding's setter, then marks
  the session as `RebuildRequired` so the document subtree for that array node is rebuilt.
- Elements removed by shrink are simply dropped (no undo beyond cancel).

---

## Phase 6: JSON Support

Allows the `EditDocument` (instruction tree with current field values) to be serialised to and
from JSON. Enables tooling, clipboard operations, and value presets.

### 6.1 JSON Schema

```json
{
  "schemaVersion": "1.0",
  "rootTypeName": "MyComponent",
  "scope": {
    "includedPaths": ["$.Damage"],
    "includeChildren": true
  },
  "root": {
    "kind": "Scalar",
    "name": "Damage",
    "path": "$.Damage",
    "clrTypeName": "System.Int32",
    "value": 100
  }
}
```

Rules:
- `SelectionRoot` node emits `"kind": "SelectionRoot"` with `children` array.
- Enum values serialised as string name + integer value pair.
- `Guid` / `DateTime` serialised as standard ISO strings.
- `FixedBuffer` / `InlineArray` serialised as JSON arrays.
- `BufferView` serialised with `"viewType"` + children.

### 6.2 Session API

```csharp
string json = session.ToJson();          // serialise current edit document + values
session.LoadJson(string json);           // apply values from JSON into the edit buffer
```

`LoadJson` validates that the JSON `rootTypeName` and `scope` match the current session before
applying values. Mismatches throw `EditJsonMismatchException`.

---

## Phase 7: Plugin System & Custom Editors

### 7.1 Custom Field Editor Interface

Allows consumers to install a specialised editor for any CLR type:

```csharp
public interface ICustomFieldEditor
{
    Type TargetType { get; }
    // Returns replacement EditNode(s) to use instead of the default reflection result
    EditNode CreateNode(EditNodeId id, string name, string jsonPath,
                        IValueBinding binding, EditNodeMetadata metadata);
}
```

Registered via `ComponentEditServiceBuilder.RegisterFieldEditor<T>(ICustomFieldEditor editor)`.

Built-in plugins provided in `StructEdit.Core`:
- `GuidFieldEditor` — `EditNodeKind.Guid`
- `DateTimeFieldEditor` — `EditNodeKind.DateTime`

### 7.2 Custom Component Editor (Whole-Component Override)

```csharp
public interface ICustomComponentEditor
{
    Type ComponentType { get; }
    EditDocument BuildDocument(IEditBuffer buffer, EditScope scope, EditContext? context);
}
```

Registered via `ComponentEditServiceBuilder.RegisterComponentEditor(ICustomComponentEditor editor)`.
Takes priority over the default reflection-based builder for the matching component type.
Works for both managed and unmanaged components.

---

## Phase 8: Unit Tests

Tests live in `StructEdit.Tests` (xUnit + FluentAssertions).

Coverage areas:
- Memory classifier correctness for managed, unmanaged, non-blittable types
- `NativeStructEditBuffer` round-trip (copy in → mutate → box out)
- `ManagedObjectEditBuffer` clone isolation (editing clone does not affect original)
- `EditDocumentBuilder` node tree shape for all `EditNodeKind` variants
- Scope filtering (whole, single field, multi-field, include-children)
- `IContainerBinding.Resize` with parent writeback
- `IBufferViewProvider` union document rebuild
- `EditRebuildState` transitions
- `Validate()` runs against full buffer, not scope
- `Commit()` returns correct replacement value
- `Cancel()` / `Dispose()` leaves original unchanged
- JSON round-trip (ToJson → LoadJson → Commit equals original-mutated value)

---

## Phase 9: Example Project

`StructEdit.Sample` — a console application (no UI framework dependency) demonstrating:

1. Defining managed (`WeaponComponent` record) and unmanaged (`ProjectileComponent` struct) data.
2. Opening full-component sessions and editing fields via `IValueBinding`.
3. Scoped sessions (single field, multi-field).
4. `RebuildDocument` after discriminator change (union chameleon).
5. `ToJson()` and `LoadJson()` round-trip.
6. Registering and using `IBufferViewProvider`.
7. `Commit()` and writing the replacement back.
8. `Cancel()` leaving originals unchanged.

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| Public API is fully `object`-based | ECS integration stays simple regardless of managed/unmanaged |
| Edit buffer always holds whole component | Atomic commit; no partial overwrites |
| `EditNodeId` (int) for render loop | Zero string allocation per frame in immediate-mode UI |
| `Unsafe.Read/Write<T>` not `Marshal.StructureToPtr` | Correct with fixed buffers; blittable-only |
| Reflection runs once at session open | Performance: no reflection in the render loop |
| `IBufferViewProvider` for unions | Chameleon views are consumer-defined, library stays generic |
| `EditScope` separate from buffer scope | Small focused UI while preserving atomic commit semantics |
| No source generators | Purely reflection + attributes; works with any assembly at runtime |
| `session.RebuildDocument()` preserves buffer | Discriminator change re-renders UI without losing edits |
| Validation against whole buffer | Cross-field constraints cannot be bypassed by partial scope |
