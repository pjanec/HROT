# StructEdit.Reflection

**Project path**: `FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/StructEdit.Reflection.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `StructEdit.Reflection`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the StructEdit root.

---

## Executive Overview

`StructEdit.Reflection` is the primary entry-point layer of the StructEdit library for
applications that need automatic `EditDocument` generation from C# type metadata. It
provides:

- **`ComponentEditServiceBuilder`** -- a fluent configuration builder for the editing
  pipeline. Hosts register validators, custom field editors, custom component editors,
  and buffer-view providers here.
- **`ComponentEditService`** (internal) -- implements `IComponentEditService`. Classifies
  each component's memory layout and creates the appropriate `IEditBuffer`, then
  delegates document building to `ReflectionEditDocumentBuilder` or a registered
  `ICustomComponentEditor`.
- **`ReflectionEditDocumentBuilder`** -- implements `IEditDocumentBuilder`. Uses
  `System.Reflection` to scan public fields and properties of a CLR type, recursively
  builds an immutable `EditNode` tree, and applies `EditScope` filtering.
- **`EditSession`** (internal) -- the `IEditSession` implementation. Owns the
  `IEditBuffer` for the session's lifetime and delegates to the builder on rebuild.
- **Built-in field editors**: `DateTimeFieldEditor` and `GuidFieldEditor` are registered
  automatically, adding proper `EditNodeKind.DateTime` and `EditNodeKind.Guid` nodes for
  those types.

The reflection layer has **no NuGet dependencies** and depends only on `StructEdit.Core`.

---

## Architecture

### Component Diagram

```
+----------------------------------------------------------+
|                   Consumer (application)                  |
|                                                          |
|   var service = new ComponentEditServiceBuilder()        |
|       .RegisterValidator<MyType>(validator)              |
|       .RegisterFieldEditor<DateTime>(new DateTimeEd())   |
|       .RegisterBufferViewProvider(unionProvider)         |
|       .Build();                                          |
|                                                          |
|   using var session = service.Open(component, type);     |
+----------------------------------------------------------+
                          |
                 uses IComponentEditService
                          |
+----------------------------------------------------------+
|                StructEdit.Reflection                     |
|                                                          |
|  ComponentEditServiceBuilder                             |
|    .RegisterBufferViewProvider(IBufferViewProvider)      |
|    .RegisterFieldEditor<T>(ICustomFieldEditor)           |
|    .RegisterComponentEditor(ICustomComponentEditor)      |
|    .RegisterValidator<T>(IComponentValidator)            |
|    .Build() -> IComponentEditService                     |
|                                                          |
|  ComponentEditService (internal)                         |
|    .Open(component, type, scope, ctx)                    |
|      |                                                   |
|      +-- DefaultComponentMemoryClassifier               |
|      |   .Classify(type)                                |
|      |   -> UnmanagedBlittableStruct                    |
|      |      -> NativeStructEditBuffer (unsafe alloc)    |
|      |   -> ManagedReference                            |
|      |      -> ManagedObjectEditBuffer                  |
|      |   -> NonBlittableStruct                         |
|      |      -> BoxedStructEditBuffer                    |
|      |                                                   |
|      +-- CustomComponentEditor? -> custom Build()       |
|      |   else ReflectionEditDocumentBuilder.Build()     |
|      |                                                   |
|      +-- new EditSession(buffer, builder, ...)          |
|                                                          |
|  ReflectionEditDocumentBuilder                           |
|    .Build(buffer, componentType, scope, context)         |
|      |                                                   |
|      +-- Recursive field/property scan                  |
|      |   (DetermineKind -> EditNodeKind)                |
|      |                                                   |
|      +-- CustomFieldEditor? for type -> custom node     |
|      |   else build default node                        |
|      |                                                   |
|      +-- CreateLeafBinding(buffer, offset, fi, pi)      |
|      |   -> NativeFieldBinding | ManagedFieldBinding |  |
|      |      ManagedPropertyBinding | NestedMemberBinding|
|      |                                                   |
|      +-- IBufferViewProvider? -> replace FixedBuffer    |
|      |                                                   |
|      +-- ApplyScope(root, scope) -> filter tree         |
|      |                                                   |
|      +-- return EditDocument(filteredRoot, type, scope) |
|                                                          |
|  EditSession (internal)                                  |
|    .Document / .IsDirty / .RebuildState                  |
|    .Validate() / .Commit() / .Dispose()                  |
|                                                          |
|  Editors/                                                |
|    DateTimeFieldEditor   (auto-registered)               |
|    GuidFieldEditor       (auto-registered)               |
+----------------------------------------------------------+
              |
     ProjectReference
              |
   +--------------------+
   |  StructEdit.Core   |
   |  (no NuGet deps)   |
   +--------------------+
```

### Type Classification Flow

```
Type T
  |
  +-- IsValueType? No  -> ManagedReference -> ManagedObjectEditBuffer
  |
  +-- Marshal.IsBlittable(T)? Yes -> UnmanagedBlittableStruct
  |      -> NativeStructEditBuffer (NativeMemory.Alloc, unsafe pointer bindings)
  |
  +-- Otherwise -> NonBlittableStruct -> BoxedStructEditBuffer
```

### Reflection Traversal

```
BuildNode(buffer, path, name, type, ...)
  |
  +-- DetermineKind(type):
  |     bool -> Boolean
  |     string -> String
  |     numeric -> Scalar
  |     Guid -> Guid (handled by GuidFieldEditor)
  |     DateTime -> DateTime (handled by DateTimeFieldEditor)
  |     enum -> Enum
  |     T[] / List<T> -> DynamicArray
  |     [InlineArray] struct -> InlineArray
  |     fixed buffer -> FixedBuffer
  |     struct -> Struct
  |     class -> Class
  |     record -> Record
  |     other -> Unsupported
  |
  +-- For each public field/property:
  |     recurse BuildNode(buffer, jsonPath+".Name", ...)
  |
  +-- FixedBuffer + IBufferViewProvider match?
  |     -> replace with BufferViewResult.Node
  |
  +-- Return EditNode(id, name, path, kind, type, binding, children, metadata)
```

---

## Source Structure

```
StructEdit.Reflection/
+-- Editors/
|   +-- DateTimeFieldEditor.cs   -- ICustomFieldEditor for System.DateTime
|   +-- GuidFieldEditor.cs       -- ICustomFieldEditor for System.Guid
+-- ComponentEditService.cs      -- IComponentEditService implementation (internal)
+-- ComponentEditServiceBuilder.cs -- fluent builder (public entry point)
+-- EditSession.cs               -- IEditSession implementation (internal)
+-- ReflectionEditDocumentBuilder.cs -- IEditDocumentBuilder via System.Reflection
+-- StructEdit.Reflection.csproj
```

---

## Public API Reference

### ComponentEditServiceBuilder

The primary entry point for all consumer code:

```csharp
public sealed class ComponentEditServiceBuilder
{
    /// <summary>
    /// Registers a buffer-view provider for union/chameleon buffer projections.
    /// </summary>
    public ComponentEditServiceBuilder RegisterBufferViewProvider(
        IBufferViewProvider provider);

    /// <summary>Registers a custom field editor for the given CLR type.</summary>
    public ComponentEditServiceBuilder RegisterFieldEditor<T>(ICustomFieldEditor editor);
    public ComponentEditServiceBuilder RegisterFieldEditor(
        Type type, ICustomFieldEditor editor);

    /// <summary>Registers a custom whole-component editor. Overrides reflection.</summary>
    public ComponentEditServiceBuilder RegisterComponentEditor(
        ICustomComponentEditor editor);

    /// <summary>Registers a validator for the given component type.</summary>
    public ComponentEditServiceBuilder RegisterValidator<T>(IComponentValidator validator);
    public ComponentEditServiceBuilder RegisterValidator(
        Type type, IComponentValidator validator);

    /// <summary>Builds and returns the IComponentEditService.</summary>
    public IComponentEditService Build();
}
```

### ReflectionEditDocumentBuilder

Available as a public class for cases where a consumer needs to build documents
independently of the full service (e.g. preview without a session):

```csharp
public sealed class ReflectionEditDocumentBuilder : IEditDocumentBuilder
{
    public ReflectionEditDocumentBuilder();
    public ReflectionEditDocumentBuilder(IReadOnlyList<IBufferViewProvider> providers);
    public ReflectionEditDocumentBuilder(
        IReadOnlyList<IBufferViewProvider> providers,
        IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors);

    public EditDocument Build(
        IEditBuffer   buffer,
        Type          componentType,
        EditScope     scope,
        EditContext?  context);
}
```

### Built-in Field Editors

```csharp
// Auto-registered by ComponentEditService; not usually needed directly.

public sealed class DateTimeFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(DateTime);
    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath,
                                IValueBinding binding, EditNodeMetadata metadata);
}

public sealed class GuidFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(Guid);
    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath,
                                IValueBinding binding, EditNodeMetadata metadata);
}
```

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `StructEdit.Core` | project ref | All contracts and data types |

No NuGet dependencies. Uses only BCL `System.Reflection`.

---

## Usage Examples

### Example 1: Basic setup and struct editing

```csharp
// Build a service (once per application lifetime)
var service = new ComponentEditServiceBuilder().Build();

// Edit a struct
public struct PlayerStats
{
    [EditRange(0, 1000)]
    [EditUnit("HP")]
    public float Health;

    [EditRange(1, 100)]
    public int Level;

    public bool IsAlive;
}

using var session = service.Open(
    new PlayerStats { Health = 100f, Level = 5, IsAlive = true },
    typeof(PlayerStats));

// Walk the tree
foreach (var node in session.Document.Root.Children)
    Console.WriteLine($"{node.Name} [{node.Kind}] = {node.Binding?.GetBoxed()}");
// Health [Scalar] = 100   (with Min=0, Max=1000, Unit="HP" in Metadata)
// Level  [Scalar] = 5
// IsAlive[Boolean]= True

// Modify a value
var healthNode = session.Document.Root.Children.First(n => n.Name == "Health");
healthNode.Binding!.SetBoxed(75f);

// Commit
var result = (PlayerStats)session.Commit();
Console.WriteLine(result.Health);  // 75
```

### Example 2: Custom field editor for a game-specific type

```csharp
// Custom type that would otherwise be Unsupported
public struct ItemId { public int Value; }

// Custom editor produces a Scalar node backed by ItemId.Value
public sealed class ItemIdEditor : ICustomFieldEditor
{
    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath,
                                IValueBinding binding, EditNodeMetadata metadata)
    {
        // Wrap the outer ItemId binding with an accessor for the inner int
        var intBinding = new DelegateBinding<int>(
            () => ((ItemId)binding.GetBoxed()!).Value,
            v  => binding.SetBoxed(new ItemId { Value = v }));
        return new EditNode(id, name, jsonPath, EditNodeKind.Scalar,
                            typeof(int), intBinding, null, metadata);
    }
}

// Register it
var service = new ComponentEditServiceBuilder()
    .RegisterFieldEditor<ItemId>(new ItemIdEditor())
    .Build();
```

### Example 3: Whole-component custom editor

```csharp
// Completely override the tree for a specific type
public sealed class TransformEditor : ICustomComponentEditor
{
    public Type ComponentType => typeof(Transform);

    public EditDocument BuildDocument(
        IEditBuffer buffer, EditScope scope, EditContext? context)
    {
        // Build a custom tree (e.g. position/rotation/scale groups)
        var idAlloc = new IdAllocator();
        var rootBinding = buffer.CreateRootBinding();
        var root = new EditNode(
            new EditNodeId(idAlloc.Next()), "Transform", "$",
            EditNodeKind.Struct, typeof(Transform),
            binding: null,
            children: new[] { BuildPositionNode(buffer, idAlloc) });
        return new EditDocument(root, typeof(Transform), scope);
    }
}

var service = new ComponentEditServiceBuilder()
    .RegisterComponentEditor(new TransformEditor())
    .Build();
```

### Example 4: Record editing with commit

```csharp
// Immutable record -- the builder reconstructs the instance on write
public sealed record WeaponRecord(int Damage, float FireRate, string Name);

var service = new ComponentEditServiceBuilder().Build();
using var session = service.Open(
    new WeaponRecord(50, 5f, "Rifle"),
    typeof(WeaponRecord));

var damageNode = session.Document.Root.Children.First(n => n.Name == "Damage");
damageNode.Binding!.SetBoxed(100);

var updated = (WeaponRecord)session.Commit();
Console.WriteLine(updated.Damage);   // 100
Console.WriteLine(updated.FireRate); // 5 (unchanged)
Console.WriteLine(updated.Name);     // "Rifle" (unchanged)
```

---

## Best Practices

1. **Build the service once** -- `ComponentEditServiceBuilder.Build()` produces a
   reusable `IComponentEditService`. Create it during application startup and inject
   it; do not rebuild per-session.

2. **Register all custom editors before `Build()`** -- registrations after `Build()`
   have no effect on the service instance.

3. **Custom field editors take priority over default kind detection** -- if a
   `ICustomFieldEditor` returns a non-null node, the default reflection path for that
   field type is skipped entirely.

4. **`ReflectionEditDocumentBuilder` is thread-safe** -- it creates new instances of
   all bindings per call. Multiple sessions for different components can be opened
   concurrently.

5. **For blittable structs, prefer direct NativeFieldBinding reads** -- the native path
   uses unsafe pointer arithmetic and is significantly faster than boxed access for
   hot inspection panels that render many fields per frame.

6. **Use `EditScope.ForField` with `IncludeParentsForContext = true` for breadcrumb UI**
   -- this makes ancestor container nodes visible in the document as read-only context
   nodes, enabling renderers to show the full field path without walking up through
   `null` bindings.

---

## Advanced Notes

### Memory Classification Details

`DefaultComponentMemoryClassifier` uses `Marshal.IsBlittable(type)` combined with
`type.IsValueType` to select the buffer strategy:

```
Type T
  |
  +-- IsValueType? No  -> ManagedReference
  |                         -> ManagedObjectEditBuffer
  |                            (stores object reference directly)
  |
  +-- IsBlittable? Yes -> UnmanagedBlittableStruct
  |                         -> NativeStructEditBuffer
  |                            (NativeMemory.Alloc + unsafe pointer bindings)
  |                            -> fastest: direct byte-level read/write
  |
  +-- IsValueType + !IsBlittable -> NonBlittableStruct
                        -> BoxedStructEditBuffer
                           (boxed object, reflected field access)
```

**Blittable** means the struct has no reference-type fields, no bool/char (which have
different sizes in managed vs native), and all nested structs are also blittable.
Typical blittable structs: `Vector3`, `Matrix4x4`, custom game ECS components that hold
only `float`/`int`/`byte` fields.

### Binding Selection

For each field or property, `ReflectionEditDocumentBuilder` creates the appropriate
binding type:

| Scenario | Binding Created |
|---|---|
| Blittable struct field | `NativeFieldBinding` (unsafe offset pointer) |
| Managed object field | `ManagedFieldBinding` (FieldInfo.GetValue/SetValue) |
| Property (get+set) | `ManagedPropertyBinding` (PropertyInfo.GetValue/SetValue) |
| Nested field in blittable parent | `NativeFieldBinding` with accumulated offset |
| Element of `List<T>` or `T[]` | `DynamicArrayBinding` element binding |
| Element of `[InlineArray]` struct | `InlineArrayBinding` element binding |
| Element of `fixed T[]` buffer | `FixedBufferBinding` element binding |

### Scope Filtering

After the full reflection tree is built, `ApplyScope(root, scope)` prunes the tree to
only the paths specified in `EditScope.IncludedPaths`:

```
Full tree (all fields):
  $ (Struct)
    $.Position (Struct)
      $.Position.X (Scalar)
      $.Position.Y (Scalar)
    $.Health (Scalar)
    $.Level (Scalar)

Scope: ForField("$.Health")
  $ (SelectionRoot, or single-child Struct)
    $.Health (Scalar)

Scope: ForField("$.Position") + IncludeParentsForContext=false
  $ (SelectionRoot)
    $.Position (Struct)
      $.Position.X (Scalar)
      $.Position.Y (Scalar)
```

### Performance: Reflection Cache

`ReflectionEditDocumentBuilder` does **not** cache reflection data between calls. Each
`Build` call scans the type tree fresh. For hot paths (inspector panels rendered at
60 fps), the builder is invoked only on session open or rebuild, not every frame.

For performance-critical hosts that open and close many sessions per second (e.g. a
table editor showing thousands of rows), consider caching `EditDocument` structures by
type key and cloning bindings rather than rebuilding from scratch.

---

## Diagram: Build Flow

```
ComponentEditServiceBuilder.Build()
  |
  +--> ComponentEditService (internal)
         |
         +-- .Open(component, type, scope, ctx)
               |
               +-- DefaultComponentMemoryClassifier.Classify(type)
               |     |
               |     +-- UnmanagedBlittableStruct -> NativeStructEditBuffer
               |     +-- ManagedReference         -> ManagedObjectEditBuffer
               |     +-- NonBlittableStruct        -> BoxedStructEditBuffer
               |
               +-- CustomComponentEditor? Build() OR
               |   ReflectionEditDocumentBuilder.Build(buffer, type, scope, ctx)
               |         |
               |         +-- BuildNode(buffer, "$", "root", type, ...)
               |         |     +-- DetermineKind(type)
               |         |     +-- CustomFieldEditor? CreateNode() OR default build
               |         |     +-- Create binding (NativeFieldBinding / etc.)
               |         |     +-- Recurse for children
               |         |     +-- IBufferViewProvider? -> replace FixedBuffer node
               |         |
               |         +-- ApplyScope(root, scope) -> prune tree
               |         +-- return EditDocument(filteredRoot, type, scope)
               |
               +-- new EditSession(buffer, builder, scope, ctx, validator, document)
               |
               v
         IEditSession (consumer)
```

---

## Related Projects

| Project | Relationship |
|---|---|
| `StructEdit.Core` | Direct dependency -- all contracts, `IEditBuffer`, `EditNode` |
| `StructEdit.Json` | Peer -- extends `IEditSession` with JSON; no project ref between them |
| `StructEdit.Sample` | Consumer -- exercises all builder patterns |
| `Fdp.Engine` (FDP) | Production consumer -- drives inspector panels for ECS components |
