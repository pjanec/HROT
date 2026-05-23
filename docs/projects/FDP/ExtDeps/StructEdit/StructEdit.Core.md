# StructEdit.Core

**Project path**: `FDP/ExtDeps/StructEdit/src/StructEdit.Core/StructEdit.Core.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `StructEdit.Core`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the StructEdit root.
All public types carry XML doc comments.

---

## Executive Overview

`StructEdit.Core` is the rendering-agnostic foundation of the StructEdit property editor
library. It defines:

- The **session model**: `IComponentEditService` (factory), `IEditSession` (editing
  handle), `EditDocument` (immutable instruction tree), `EditNode` (tree node).
- The **binding contract**: `IValueBinding` (read/write access to a field/property),
  `IEditBuffer` (raw memory backing for the edited copy of a component).
- The **node kind vocabulary**: `EditNodeKind` (20-value enum covering all C# type
  categories the library understands).
- **Attributes** for editor hints: `[EditRange]`, `[EditUnit]`, `[EditDisplayName]`,
  `[EditReadOnly]`, `[InlineArrayHint]`, `[FixedBufferHint]`.
- **Scoping**: `EditScope` controls which fields appear in the `EditDocument`.
- **Validation**: `IComponentValidator`, `ValidationResult`, `ValidationError`.
- **Union/buffer-view support**: `IBufferViewProvider` for projecting fixed-size byte
  buffers as discriminator-driven typed overlays.
- **Memory management**: three `IEditBuffer` implementations (native blittable, boxed
  struct, managed reference) plus `IComponentMemoryClassifier` to choose between them.
- **Plugin points**: `ICustomFieldEditor`, `ICustomComponentEditor` for bypassing
  reflection-based discovery for specific types.

The project has **no NuGet dependencies** and no project references. It can be used
independently by renderer layers (IMGUI, WPF, etc.) through any implementation of
`IEditDocumentBuilder`.

---

## Architecture

### Layered View

```
+------------------------------------------------------+
|         StructEdit.Reflection (consumer)             |
|  ComponentEditServiceBuilder -> ComponentEditService  |
|  ReflectionEditDocumentBuilder -> EditSession         |
+------------------------------------------------------+
                        |
            ProjectReference
                        |
+------------------------------------------------------+
|                 StructEdit.Core                      |
|                                                      |
|  +-------------------+  +------------------------+  |
|  | Session Layer      |  | Document Layer         |  |
|  |                    |  |                        |  |
|  | IComponentEdit     |  | EditDocument           |  |
|  |   Service          |  |   .Root: EditNode      |  |
|  | IEditSession       |  |   .RootComponentType   |  |
|  | EditRebuildState   |  |   .Scope               |  |
|  +-------------------+  |                        |  |
|                         | EditNode               |  |
|  +-------------------+  |   .Id / .Name          |  |
|  | Memory Layer       |  |   .JsonPath            |  |
|  |                    |  |   .Kind (EditNodeKind) |  |
|  | IEditBuffer        |  |   .ClrType             |  |
|  | NativeStructEdit   |  |   .Binding             |  |
|  |   Buffer           |  |   .Children            |  |
|  | ManagedObjectEdit  |  |   .Metadata            |  |
|  |   Buffer           |  |   .IsReadOnly          |  |
|  | BoxedStructEdit    |  +------------------------+  |
|  |   Buffer           |                             |
|  | IComponentMemory   |  +------------------------+  |
|  |   Classifier       |  | Binding Layer          |  |
|  +-------------------+  |                        |  |
|                         | IValueBinding          |  |
|  +-------------------+  | NativeFieldBinding     |  |
|  | Validation         |  | ManagedFieldBinding    |  |
|  |                    |  | ManagedPropertyBinding |  |
|  | IComponentValidator|  | NestedMemberBinding    |  |
|  | ValidationResult   |  | InlineArrayBinding     |  |
|  | ValidationError    |  | DynamicArrayBinding    |  |
|  | EditValidationCtx  |  | FixedBufferBinding     |  |
|  +-------------------+  +------------------------+  |
|                                                      |
|  +-------------------+  +------------------------+  |
|  | Scope              |  | Plugins / UnionSupport |  |
|  |                    |  |                        |  |
|  | EditScope          |  | ICustomFieldEditor     |  |
|  | EditPath           |  | ICustomComponentEditor |  |
|  | EditContext         |  | IBufferViewProvider    |  |
|  +-------------------+  | BufferViewRequest      |  |
|                         | BufferViewResult        |  |
|  +-------------------+  +------------------------+  |
|  | Attributes         |                             |
|  | [EditRange]        |                             |
|  | [EditUnit]         |                             |
|  | [EditDisplayName]  |                             |
|  | [EditReadOnly]     |                             |
|  | [InlineArrayHint]  |                             |
|  | [FixedBufferHint]  |                             |
|  +-------------------+                             |
+------------------------------------------------------+
            NO project / NuGet dependencies
```

### Session Lifecycle

```
IComponentEditService.Open(component, type, scope, context)
  |
  +-- Classify memory kind -> create IEditBuffer
  |     (NativeStructEditBuffer | ManagedObjectEditBuffer | BoxedStructEditBuffer)
  |
  +-- IEditDocumentBuilder.Build(buffer, type, scope, context)
  |     -> EditDocument (immutable tree of EditNode)
  |
  +-- new EditSession(buffer, builder, scope, context, validator, document)
  |
  v
IEditSession
  |
  +-- .Document.Root  -- read nodes and invoke .Binding.SetBoxed(value)
  |
  +-- .IsDirty        -- any binding has been written
  |
  +-- .Validate()     -- run IComponentValidator against buffer
  |
  +-- .Commit()       -- validate + box buffer -> returns updated component
  |
  +-- .Dispose()      -- frees IEditBuffer; session is dead
```

---

## Source Structure

```
StructEdit.Core/
+-- Attributes/
|   +-- EditAttributes.cs        -- [EditRange], [EditUnit], [EditDisplayName],
|                                    [EditReadOnly], [InlineArrayHint], [FixedBufferHint]
+-- Bindings/
|   +-- DynamicArrayBinding.cs   -- binding for List<T> / T[] resizable collections
|   +-- FieldReadWriterCache.cs  -- caches FieldInfo read/write delegates
|   +-- FixedBufferBinding.cs    -- binding for 'fixed' keyword buffers
|   +-- InlineArrayBinding.cs    -- binding for [InlineArray] structs (C# 12)
|   +-- ManagedFieldBinding.cs   -- field-info based read/write for managed types
|   +-- ManagedPropertyBinding.cs-- property-info based read/write
|   +-- NativeFieldBinding.cs    -- unsafe pointer offset binding for blittable structs
|   +-- NestedMemberBinding.cs   -- wraps a parent binding + field to reach nested field
+-- Memory/
|   +-- BoxedStructEditBuffer.cs       -- backing for non-blittable value types
|   +-- ComponentMemoryKind.cs         -- enum: UnmanagedBlittableStruct | ManagedReference | NonBlittableStruct
|   +-- DefaultComponentMemoryClassifier.cs -- classifies types using Marshal.IsBlittable
|   +-- IComponentMemoryClassifier.cs  -- pluggable classifier
|   +-- IRuntimeTypeOps.cs             -- SizeOf, CopyObjectToNative, BoxFromNative
|   +-- ManagedObjectEditBuffer.cs     -- backing for reference type components
|   +-- NativeStructEditBuffer.cs      -- unsafe NativeMemory backing for blittable structs
|   +-- RuntimeTypeOps.cs              -- cached IL-emitted implementations of IRuntimeTypeOps
+-- Plugins/
|   +-- ICustomComponentEditor.cs      -- override whole-component document building
|   +-- ICustomFieldEditor.cs          -- override per-field node creation
+-- UnionSupport/
|   +-- BufferViewRequest.cs     -- context for IBufferViewProvider.CanCreateView
|   +-- BufferViewResult.cs      -- replacement EditNode returned by a provider
|   +-- IBufferViewProvider.cs   -- plugin: projects a fixed-buffer as a typed overlay
|   +-- IdAllocator.cs           -- monotonic int sequence for EditNodeId allocation
+-- EditAttributes.cs        (in Attributes/)
+-- EditContext.cs            -- opaque external context passed through session
+-- EditDocument.cs           -- session document: Root + RootComponentType + Scope
+-- EditJsonMismatchException.cs -- thrown by StructEdit.Json on schema mismatch
+-- EditNode.cs               -- immutable tree node (id, name, path, kind, binding, children)
+-- EditNodeId.cs             -- int-backed node identity (monotonic per session)
+-- EditNodeKind.cs           -- 20-value enum of node types
+-- EditNodeMetadata.cs       -- decoded attribute hints (min, max, unit, displayName)
+-- EditPath.cs               -- JSON-path expression for scope targeting (e.g. "$.Speed")
+-- EditRebuildState.cs       -- enum: Stable | RebuildRequired
+-- EditScope.cs              -- scope descriptor: which fields appear in the document
+-- EditValidationException.cs-- thrown by IEditSession.Commit when validation fails
+-- IComponentEditService.cs  -- factory: Open(component, type, ...) -> IEditSession
+-- IContainerBinding.cs      -- base for bindings that own child bindings
+-- IEditBuffer.cs            -- raw byte-buffer contract (infrastructure; not for consumers)
+-- IEditDocumentBuilder.cs   -- Build(buffer, type, scope, ctx) -> EditDocument
+-- IEditSession.cs           -- session handle: Document, IsDirty, Validate, Commit
+-- IValueBinding.cs          -- GetBoxed / SetBoxed / TryGetSpan
+-- Validation.cs             -- IComponentValidator + EditValidationContext
+-- ValidationError.cs        -- single error record (path + message)
+-- ValidationResult.cs       -- Ok() or Fail(errors)
+-- StructEdit.Core.csproj
```

---

## Public API Reference

### IComponentEditService

Top-level factory. Typically obtained from `ComponentEditServiceBuilder.Build()` in
`StructEdit.Reflection`.

```csharp
public interface IComponentEditService
{
    IEditSession Open(
        object      component,
        Type        componentType,
        EditScope?  scope   = null,
        EditContext? context = null);
}
```

### IEditSession

```csharp
public interface IEditSession : IDisposable
{
    EditDocument    Document     { get; }
    bool            IsDirty      { get; }
    EditRebuildState RebuildState { get; }

    void             MarkStructuralChange();  // triggers RebuildRequired
    void             RebuildDocument();       // rebuilds tree from current buffer
    ValidationResult Validate();
    object           Commit();               // validate + return updated component
    void             Cancel();              // semantic no-op; Dispose discards buffer
}
```

### EditDocument

```csharp
public sealed class EditDocument
{
    public EditNode  Root              { get; }
    public Type      RootComponentType { get; }
    public EditScope Scope             { get; }
}
```

### EditNode

```csharp
public sealed class EditNode
{
    public EditNodeId                 Id       { get; }
    public string                     Name     { get; }
    public string                     JsonPath { get; }  // e.g. "$.Weapon.Damage"
    public EditNodeKind               Kind     { get; }
    public Type                       ClrType  { get; }
    public IValueBinding?             Binding  { get; }  // null for container nodes
    public IReadOnlyList<EditNode>    Children { get; }
    public EditNodeMetadata           Metadata { get; }
    public bool                       IsReadOnly { get; }
}
```

### EditNodeKind

```csharp
public enum EditNodeKind
{
    SelectionRoot,   // synthetic root for multi-path scopes
    Scalar,          // int, float, double, byte, ...
    Boolean,
    String,
    Enum,
    Guid,
    DateTime,
    Struct,          // value-type composite
    Class,           // reference-type composite
    Record,          // immutable record (reconstructed on write)
    InlineArray,     // C# 12 [InlineArray] struct
    FixedBuffer,     // C# 'fixed' keyword buffer
    DynamicArray,    // List<T>, T[]
    BufferView,      // discriminator-driven overlay
    Union,           // discriminator-driven choice
    Custom,          // consumer-installed editor
    Unsupported,     // type the library cannot reflect
}
```

### IValueBinding

```csharp
public interface IValueBinding
{
    Type    ValueType          { get; }
    object? GetBoxed();
    void    SetBoxed(object? value);
    bool    TryGetSpan(out Span<byte> bytes);  // only succeeds for native/blittable
}
```

### EditScope

```csharp
public sealed class EditScope
{
    public static EditScope WholeComponent { get; }  // all fields, no filtering

    public static EditScope ForField(EditPath path);
    public static EditScope ForFields(params EditPath[] paths);

    public IReadOnlyList<EditPath> IncludedPaths   { get; init; }
    public bool                    IncludeChildren  { get; init; }
    public bool                    IncludeParentsForContext { get; init; }
}
```

### Attributes

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditRangeAttribute(double min, double max) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditUnitAttribute(string unit) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditDisplayNameAttribute(string name) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class EditReadOnlyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InlineArrayHintAttribute(int length) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class FixedBufferHintAttribute(Type elementType, int length) : Attribute;
```

### Validation

```csharp
public interface IComponentValidator
{
    ValidationResult Validate(EditValidationContext context);
}

public sealed class EditValidationContext
{
    public required Type        ComponentType { get; init; }
    public required IEditBuffer Buffer        { get; init; }  // use Box() to read value
    public required EditScope   Scope         { get; init; }
}

public sealed class ValidationResult
{
    public bool IsValid                            { get; }
    public IReadOnlyList<ValidationError> Errors   { get; }

    public static ValidationResult Ok();
    public static ValidationResult Fail(IEnumerable<ValidationError> errors);
}
```

### IBufferViewProvider (union/discriminator support)

```csharp
public interface IBufferViewProvider
{
    bool             CanCreateView(BufferViewRequest request);
    BufferViewResult CreateView(BufferViewRequest request);
}
```

### Plugin interfaces

```csharp
public interface ICustomFieldEditor
{
    EditNode? CreateNode(EditNodeId id, string name, string jsonPath,
                         IValueBinding binding, EditNodeMetadata metadata);
}

public interface ICustomComponentEditor
{
    Type ComponentType { get; }
    EditDocument BuildDocument(IEditBuffer buffer, EditScope scope, EditContext? context);
}
```

---

## Dependencies

**None.** The project targets `net8.0` with no NuGet or project references.
`AllowUnsafeBlocks = true` is enabled for native buffer operations.

---

## Usage Examples

### Example 1: Traversing an EditDocument tree

```csharp
// Assume session was opened by StructEdit.Reflection
using var session = service.Open(myComponent, typeof(MyComponent));

void PrintTree(EditNode node, int depth = 0)
{
    var indent  = new string(' ', depth * 2);
    var value   = node.Binding?.GetBoxed();
    var display = node.Metadata.DisplayName ?? node.Name;
    Console.WriteLine($"{indent}[{node.Kind}] {display} = {value ?? "(container)"}");
    foreach (var child in node.Children)
        PrintTree(child, depth + 1);
}

PrintTree(session.Document.Root);
```

### Example 2: Writing a custom renderer (ImGui pattern)

```csharp
// A minimal ImGui renderer that walks the EditDocument
void RenderDocument(EditDocument doc)
{
    RenderNode(doc.Root);
}

void RenderNode(EditNode node)
{
    switch (node.Kind)
    {
        case EditNodeKind.Scalar:
        {
            var value = (float)node.Binding!.GetBoxed()!;
            var meta  = node.Metadata;
            float min = (float)(meta.Min ?? float.MinValue);
            float max = (float)(meta.Max ?? float.MaxValue);
            if (ImGui.SliderFloat(node.Name, ref value, min, max))
                node.Binding.SetBoxed(value);
            break;
        }
        case EditNodeKind.Boolean:
        {
            var value = (bool)node.Binding!.GetBoxed()!;
            if (ImGui.Checkbox(node.Name, ref value))
                node.Binding.SetBoxed(value);
            break;
        }
        case EditNodeKind.Struct:
        case EditNodeKind.Class:
        case EditNodeKind.Record:
            if (ImGui.TreeNode(node.Name))
            {
                foreach (var child in node.Children) RenderNode(child);
                ImGui.TreePop();
            }
            break;
        default:
            ImGui.Text($"{node.Name}: (unsupported kind {node.Kind})");
            break;
    }
}
```

### Example 3: Registering attributes and validating

```csharp
public struct WeaponConfig
{
    [EditRange(0, 1000)]
    [EditUnit("HP")]
    [EditDisplayName("Base Damage")]
    public float Damage;

    [EditRange(0.1, 30)]
    [EditUnit("rounds/s")]
    public float FireRate;

    [EditReadOnly]
    public Guid AssetId;
}

// Validation
public sealed class WeaponValidator : IComponentValidator
{
    public ValidationResult Validate(EditValidationContext ctx)
    {
        var weapon = (WeaponConfig)ctx.Buffer.Box();
        var errors = new List<ValidationError>();
        if (weapon.Damage <= 0)
            errors.Add(new ValidationError("$.Damage", "Damage must be positive."));
        if (weapon.FireRate <= 0)
            errors.Add(new ValidationError("$.FireRate", "FireRate must be positive."));
        return errors.Count > 0 ? ValidationResult.Fail(errors) : ValidationResult.Ok();
    }
}
```

---

## Best Practices

1. **Always dispose sessions** -- `IEditSession` implements `IDisposable`. Blittable
   struct sessions allocate `NativeMemory`; failing to dispose leaks unmanaged memory.

2. **Call `MarkStructuralChange` when array lengths change** -- if a `DynamicArray`
   element is added or removed, call `session.MarkStructuralChange()` then
   `session.RebuildDocument()` before rendering the next frame. Stale `EditNode` tree
   references will have wrong child counts.

3. **Check `IsReadOnly` before calling `SetBoxed`** -- the renderer should disable
   editing for nodes where `IsReadOnly == true` (driven by `[EditReadOnly]` or scope).

4. **Use `EditScope.ForField` for focused editing** -- when only one field needs to be
   edited (e.g. inline editing a single value in a table row), scoping reduces tree
   size and improves clarity:
   ```csharp
   using var session = service.Open(row, typeof(RowType),
       EditScope.ForField(new EditPath("$.Priority")));
   ```

5. **Use `IBufferViewProvider` for discriminated unions** -- the standard pattern for
   C-style tagged unions or variant payloads is a fixed-size byte buffer with a
   discriminator field. Register a provider that inspects the discriminator and returns
   a typed `BufferViewResult`.

6. **Metadata attributes are compile-time hints, not runtime enforcement** -- the
   library reads `[EditRange]` into `EditNodeMetadata.Min/Max` but does not clamp
   values. The renderer or validator is responsible for enforcement.

---

## Related Projects

| Project | Relationship |
|---|---|
| `StructEdit.Reflection` | Consumer -- builds `EditDocument` via reflection |
| `StructEdit.Json` | Consumer -- serializes `EditDocument` to JSON |
| `StructEdit.Sample` | Consumer -- exercises all session patterns |
| `Fdp.Engine` (FDP) | Production consumer -- edits component data in inspector panel |
