# StructEdit.Sample

**Project path**: `FDP/ExtDeps/StructEdit/samples/StructEdit.Sample/StructEdit.Sample.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Output type**: Executable (console)
**Namespace root**: (top-level statements)

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the StructEdit
root.

---

## Executive Overview

`StructEdit.Sample` is a standalone console application that demonstrates the full
StructEdit library through seven self-contained scenarios. Each scenario is a contiguous
block in `Program.cs`, runs to completion, and prints results to standard output.

The sample covers:

| Scenario | Feature demonstrated |
|---|---|
| S1 | Basic struct editing with WholeComponent scope |
| S2 | Scoped editing (single field via `EditScope.ForField`) |
| S3 | Record editing and commit (immutable record reconstruction) |
| S4 | Validation failure, `EditValidationException`, and fix |
| S5 | Dynamic array resizing, `MarkStructuralChange`, `RebuildDocument` |
| S6 | JSON round-trip with `ToJson` / `LoadJson` |
| S7 | Custom field editors (`GuidFieldEditor`, `DateTimeFieldEditor`) |

The application requires no external data files, databases, or windowing libraries. It
runs in a standard `dotnet run` invocation and is suitable for CI smoke-testing.

**Dependencies**: `StructEdit.Core`, `StructEdit.Reflection`, `StructEdit.Json`
(all project references; no NuGet packages).

---

## Architecture

### Dependency Graph

```
+--------------------+
|  StructEdit.Sample |
|   (Program.cs)     |
+---------+----------+
          |
          |-- ProjectReference
          |
+---------+----------+  +-----------------+  +------------------+
| StructEdit.        |  | StructEdit.Json  |  | StructEdit.Core  |
| Reflection         |  |                  |  |                  |
|                    |  | ToJson()         |  | IEditSession     |
| ComponentEdit      |  | LoadJson()       |  | EditDocument     |
| ServiceBuilder     |  +--------+---------+  | EditNode         |
| IEditSession impl  |           |             | IValueBinding    |
+--------------------+           |             | IContainerBinding|
          |                      |             | IComponentValid. |
          +----------+-----------+             +------------------+
                     |
            ProjectReference
                     |
            +------------------+
            |  StructEdit.Core |
            +------------------+
```

### Scenario Layout

Each scenario follows the same pattern:

```
Console.WriteLine("--- Scenario N: Feature Name ---");
{
  var service = new ComponentEditServiceBuilder()
                    [.RegisterXxx(...)]
                    .Build();

  using var session = service.Open(new MyStruct(...), typeof(MyStruct)
                                   [, scope: EditScope.ForField("$.Field")]);

  // 1. Inspect document tree
  PrintDocument(session.Document);   (optional)

  // 2. Modify bindings
  var node = session.Document.Root.Children.First(n => n.Name == "Field");
  node.Binding!.SetBoxed(newValue);

  // 3. Commit / validate
  var result = (MyStruct)session.Commit();
  Console.WriteLine($"[SN] result={result.Field}");
}
```

---

## Source Structure

```
StructEdit.Sample/
+-- Program.cs                 -- all 7 scenarios + component types + validator
+-- StructEdit.Sample.csproj   -- references Core, Reflection, Json
```

All component types and the validator are defined as inner types at the bottom of
`Program.cs`:

| Type | Kind | Used in |
|---|---|---|
| `Bullet` | `struct` | S1 (basic editing) |
| `Character` | `struct` | S2 (scoped editing) |
| `PlayerStats` | `record` | S3 (record editing) |
| `WeaponConfig` | `struct` | S4 (validation) |
| `Inventory` | `class` | S5 (dynamic array) |
| `SpawnPoint` | `struct` | S6 (JSON) |
| `EventData` | `struct` | S7 (Guid + DateTime) |
| `WeaponValidator` | `class : IComponentValidator` | S4 |

---

## Public API Reference

`StructEdit.Sample` exposes no public API; it is an executable only. The relevant APIs
exercised are from the library projects:

### APIs exercised

```csharp
// Service construction
new ComponentEditServiceBuilder()
    .RegisterValidator<T>(validator)
    .RegisterFieldEditor<T>(editor)
    .Build()

// Session open
service.Open(component, typeof(T))
service.Open(component, typeof(T), scope: EditScope.ForField("$.FieldName"))

// Document traversal
session.Document.Root.Children
node.Name, node.Kind, node.Binding, node.Metadata

// Value read/write
node.Binding.GetBoxed()
node.Binding.SetBoxed(value)

// Dynamic array
var cb = (IContainerBinding)arrayNode.Binding!;
cb.Count
cb.Resize(newSize)
cb.GetElementBinding(i)

// Structural rebuild
session.MarkStructuralChange()
session.RebuildDocument()

// Validation
session.Validate()            // -> ValidationResult
session.Commit()              // throws EditValidationException on failure

// JSON
session.ToJson()              // -> string
session.LoadJson(json)        // deserialize into existing session

// Dirty + state
session.IsDirty
session.RebuildState
```

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `StructEdit.Core` | project ref | Core contracts used in scenarios |
| `StructEdit.Reflection` | project ref | `ComponentEditServiceBuilder`, reflection builder |
| `StructEdit.Json` | project ref | `ToJson` / `LoadJson` extension methods |

---

## Usage Examples

### Example 1: Run the sample

```
cd FDP/ExtDeps/StructEdit/samples/StructEdit.Sample
dotnet run
```

Expected output (abbreviated):

```
=== StructEdit Sample Application ===

--- Scenario 1: Basic struct editing ---
  Document: scope=WholeComponent, rootType=Bullet
  [0] root (Struct)
    [1] Speed (Scalar) = 100
    [2] Damage (Scalar) = 50
    [3] IsActive (Boolean) = True
[S1] Speed=200 Damage=50 Active=True

--- Scenario 2: Scoped editing ---
  [S2] Root children count: 1

--- Scenario 3: Record editing ---
  [S3] Score=999 Lives=3 GameOver=False

--- Scenario 4: Validation + error handling ---
  [S4] Validation failed: Damage cannot exceed 1000
  [S4] Fixed: Damage=500

--- Scenario 5: Dynamic array editing ---
  [S5] Initial count: 3
  [S5] After resize: 5 items
  [S5] Committed count: 5

--- Scenario 6: JSON round-trip ---
  [S6] JSON:
  { ... }
  [S6] Loaded: X=1.50 Y=0.00 Z=-3.20

--- Scenario 7: Custom field editors ---
  [S7] EventId node kind: Guid
  [S7] OccurredAt node kind: DateTime
  ...

=== All scenarios completed successfully ===
```

### Example 2: Porting scenario S5 (dynamic array) to a real host

The sample's dynamic array pattern is the reference for real array editing:

```csharp
// Open session for a component with a List<int> field
using var session = service.Open(inventory, typeof(Inventory));

// Find the array node
var arrayNode = session.Document.Root.Children.First(n => n.Name == "ItemIds");
var cb = (IContainerBinding)arrayNode.Binding!;

// User adds an item
cb.Resize(cb.Count + 1);
session.MarkStructuralChange();
session.RebuildDocument();

// Refresh tree reference after rebuild
arrayNode = session.Document.Root.Children.First(n => n.Name == "ItemIds");
cb = (IContainerBinding)arrayNode.Binding!;
cb.GetElementBinding(cb.Count - 1).SetBoxed(99);

// Commit
var updated = (Inventory)session.Commit();
```

### Example 3: Adding a new scenario

To add scenario S8, append to `Program.cs`:

```csharp
Console.WriteLine("--- Scenario 8: InlineArray ---");
{
    var service = new ComponentEditServiceBuilder().Build();

    // InlineArray needs [InlineArray] attribute (C# 12) or InlineArrayHint
    using var session = service.Open(new Matrix3x3(), typeof(Matrix3x3));
    PrintDocument(session.Document);

    // Edit element [1][1] (the diagonal center element)
    var matNode = session.Document.Root.Children.First(n => n.Name == "Data");
    var cb = (IContainerBinding)matNode.Binding!;
    cb.GetElementBinding(4).SetBoxed(2.0f); // row 1, col 1 in row-major order

    var result = (Matrix3x3)session.Commit();
    Console.WriteLine($"[S8] Element[4] = {result.Data[4]}");
}
```

---

## Best Practices

1. **Use the sample as a smoke test** -- running `dotnet run` against the sample is a
   quick way to verify that all three StructEdit projects build and integrate correctly
   after changes.

2. **All component types are in the same file** -- for a real application, split
   component type definitions into separate files per domain model. The sample keeps
   them co-located only for brevity.

3. **Study S4 before implementing validation** -- the validator pattern (register on
   builder, throw `EditValidationException` on commit, inspect `ex.Result.Errors`) is
   the same in all environments.

4. **Study S5 before editing any collection field** -- the `MarkStructuralChange` +
   `RebuildDocument` cycle is mandatory when `DynamicArray` size changes. Skipping it
   leaves stale `EditNode` references that point to incorrect element bindings.

5. **S7 shows the correct registration order for built-in editors** -- `GuidFieldEditor`
   and `DateTimeFieldEditor` are registered by `ComponentEditService` automatically when
   built via `ComponentEditServiceBuilder`. The sample registers them explicitly only to
   demonstrate the extension point; production code does not need to register them.

---

## Component Types Reference

All component types used in the sample are defined at the bottom of `Program.cs`:

```csharp
// S1: basic struct with three fields
struct Bullet { public float Speed; public int Damage; public bool IsActive; }

// S2: scoped editing
struct Character { public int Level; public float Health; public float Mana; }

// S3: immutable record
record PlayerStats(int Score, int Lives, bool GameOver);

// S4: validation
struct WeaponConfig { public int Damage; public float FireRate; }

// S5: dynamic array
class Inventory
{
    public List<int> ItemIds { get; set; } = new() { 1, 2, 3 };
}

// S6: JSON round-trip
struct SpawnPoint { public float X; public float Y; public float Z; }

// S7: Guid + DateTime
struct EventData { public Guid EventId; public DateTime OccurredAt; }
```

### WeaponValidator (used in S4)

```csharp
class WeaponValidator : IComponentValidator
{
    public ValidationResult Validate(EditValidationContext ctx)
    {
        var box = ctx.Buffer.Box();
        if (box is WeaponConfig wc && wc.Damage > 1000)
            return ValidationResult.Fail(new[]
            {
                new ValidationError("$.Damage", "Damage cannot exceed 1000")
            });
        return ValidationResult.Ok();
    }
}
```

---

## Diagram: Session Lifecycle (as exercised by the sample)

```
new ComponentEditServiceBuilder() [.RegisterXxx(...)] .Build()
  |
  v
IComponentEditService
  |
  +-- .Open(component, typeof(T) [, scope] [, context])
  |
  v
IEditSession
  |
  +-- .Document.Root
  |     |
  |     +-- .Children  (EditNode tree)
  |           |
  |           +-- .Binding.GetBoxed()   -> read current value
  |           +-- .Binding.SetBoxed(v)  -> write value to buffer
  |           +-- .Kind                 -> what renderer to use
  |           +-- .Metadata             -> min, max, unit, displayName
  |
  +-- .IsDirty          -> any write happened?
  +-- .RebuildState     -> Stable | RebuildRequired
  +-- .MarkStructuralChange()   -> set RebuildRequired
  +-- .RebuildDocument()        -> rebuild tree from current buffer
  +-- .Validate()               -> ValidationResult
  +-- .Commit()                 -> validate + box buffer -> updated component
  |                                throws EditValidationException if invalid
  +-- .Dispose()                -> free IEditBuffer
```

---

## Diagram: Dependency Graph

```
+--------------------+
|  StructEdit.Sample  |  (console exe, no NuGet deps)
+--------+----+-------+
         |    |
         |    +-- uses JSON: session.ToJson() / session.LoadJson()
         |    |
         |    v
         |  StructEdit.Json
         |    +-- ref -> StructEdit.Core
         |
         +-- uses service, sessions, documents
         |
         v
    StructEdit.Reflection
         +-- ref -> StructEdit.Core
         |
         v
    StructEdit.Core  (no deps)
```

---

## Output Reference

Full expected standard output (abbreviated to key lines):

```
=== StructEdit Sample Application ===

--- Scenario 1: Basic struct editing ---
  Document: scope=WholeComponent, rootType=Bullet
  [0] root (Struct)
    [1] Speed  (Scalar)  = 100
    [2] Damage (Scalar)  = 50
    [3] IsActive (Boolean) = True
[S1] Speed=200 Damage=50 Active=True

--- Scenario 2: Scoped editing ---
  Document: scope=ForField($.Health), rootType=Character
    [0] root (Struct)
      [1] Health (Scalar) = 100
[S2] Root children count: 1

--- Scenario 3: Record editing ---
[S3] Score=999 Lives=3 GameOver=False

--- Scenario 4: Validation + error handling ---
[S4] Validation failed: Damage cannot exceed 1000
[S4] Fixed: Damage=500

--- Scenario 5: Dynamic array editing ---
[S5] Initial count: 3
[S5] After resize: 5 items
[S5] Committed count: 5

--- Scenario 6: JSON round-trip ---
[S6] JSON:
{
  "structedit_version": "1.0",
  "rootTypeName": "NodeEditor.Demo.SpawnPoint, ...",
  "scope": "$",
  "nodes": [
    { "path": "$.X", "kind": "Scalar", "value": 1.5 },
    { "path": "$.Y", "kind": "Scalar", "value": 0.0 },
    { "path": "$.Z", "kind": "Scalar", "value": -3.2 }
  ]
}
[S6] Loaded: X=1.50 Y=0.00 Z=-3.20

--- Scenario 7: Custom field editors ---
[S7] EventId node kind: Guid
[S7] OccurredAt node kind: DateTime
[S7] EventId: <guid-value>
[S7] OccurredAt: <datetime-value>

=== All scenarios completed successfully ===
```

---

## Testing the Sample in CI

The sample exits with code 0 on success and code 1 (or throws) on failure.
A CI pipeline can validate it with:

```yaml
- name: Run StructEdit.Sample
  run: |
    dotnet run --project FDP/ExtDeps/StructEdit/samples/StructEdit.Sample/StructEdit.Sample.csproj
  shell: bash
```

Because all scenarios run synchronously and print to stdout, test failures are visible
in the CI log. The absence of "All scenarios completed successfully" in the output
indicates a regression.

---

## Notes on Non-Rendered Usage

Unlike `NodeEditor.Demo` (which uses Raylib and Dear ImGui), `StructEdit.Sample` is a
pure console application. It exercises only the data model and serialization layers. A
rendering layer (ImGui, WPF, MAUI, etc.) is a separate concern that consumers add
on top of `StructEdit.Core`'s `EditDocument` API.

---

## Related Projects

| Project | Relationship |
|---|---|
| `StructEdit.Core` | Direct dependency -- contracts used in all scenarios |
| `StructEdit.Reflection` | Direct dependency -- service builder and session |
| `StructEdit.Json` | Direct dependency -- JSON round-trip in S6 |
| `StructEdit.Reflection` (tests) | Sibling test project (not in sample folder) |
