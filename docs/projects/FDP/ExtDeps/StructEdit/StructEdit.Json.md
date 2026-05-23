# StructEdit.Json

**Project path**: `FDP/ExtDeps/StructEdit/src/StructEdit.Json/StructEdit.Json.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Namespace root**: `StructEdit.Json`

---

## README Validation

Status: **Missing** -- no `README.md` exists in the project folder or the StructEdit root.

---

## Executive Overview

`StructEdit.Json` adds JSON persistence to the StructEdit library. It provides two
public surfaces:

1. **`EditDocumentJsonSerializer`** (static class) -- serializes an `EditDocument`'s
   current binding state to an indented JSON string, and deserializes a JSON string back
   into the bindings of an existing `EditDocument` in place.

2. **`EditSessionJsonExtensions`** -- extension methods `ToJson()` and `LoadJson(string)`
   on `IEditSession`, providing a convenience API so callers do not need to interact with
   the serializer directly.

The JSON format is defined internally at schema version `"1.0"`. The format is a flat
array of leaf/array nodes keyed by JSON path, not a deep nested tree mirroring the
component structure. This design makes the format compact, human-readable, and robust to
field reordering in the C# types.

The project is intentionally small (two source files). It is a peer-level dependency of
`StructEdit.Reflection`: neither project references the other, keeping the dependency
graph acyclic.

---

## Architecture

### Component Position

```
+------------------+     +---------------------+
| StructEdit.      |     | StructEdit.         |
| Reflection       |     | Json                |
|                  |     |                     |
| ComponentEdit    |     | EditDocumentJson    |
| ServiceBuilder   |     | Serializer          |
| ComponentEdit    |     |                     |
| Service          |     | EditSessionJson     |
| EditSession      |     | Extensions          |
|   .ToJson()  <---+-----+-- extension method  |
|   .LoadJson()<---+-----+-- extension method  |
+------------------+     +---------------------+
        |                         |
        | ProjectReference        | ProjectReference
        |                         |
        +--------+--------+-------+
                          |
               +--------------------+
               |  StructEdit.Core   |
               |  EditDocument      |
               |  EditNode          |
               |  IEditSession      |
               |  IValueBinding     |
               |  IContainerBinding |
               +--------------------+
```

### JSON Schema

```
{
  "structedit_version": "1.0",
  "rootTypeName": "My.Namespace.MyType, MyAssembly, ...",
  "scope": "$",
  "nodes": [
    { "path": "$.Speed",          "kind": "Scalar",  "value": 42.5 },
    { "path": "$.Name",           "kind": "String",  "value": "Alice" },
    { "path": "$.IsActive",       "kind": "Boolean", "value": true },
    { "path": "$.DamageType",     "kind": "Enum",    "value": "Fire" },
    { "path": "$.Tags",           "kind": "DynamicArray",
      "count": 2,
      "children": [
        { "index": 0, "value": "hero" },
        { "index": 1, "value": "player" }
      ]
    },
    { "path": "$.Matrix",         "kind": "InlineArray", "values": [1,0,0, 0,1,0] }
  ]
}
```

Node layout rules:
- `Struct`, `Class`, `Record`, `SelectionRoot`, `BufferView` containers are **not**
  emitted as entries; only their serializable leaf descendants are.
- `DynamicArray` emits a `count` and a `children` array.
- `InlineArray` and `FixedBuffer` emit a `values` array.
- `Custom`, `Union`, `Unsupported` nodes are silently skipped.

### Deserialization Strategy

```
Deserialize(json, document):
  |
  +-- Validate version + rootTypeName matches document
  |   (throws EditJsonMismatchException on mismatch)
  |
  +-- Build path -> EditNode map from document.Root (recursive walk)
  |
  +-- For each "nodes" entry:
  |     find EditNode by "path"
  |     |
  |     +-- DynamicArray: resize via IContainerBinding, set each element
  |     +-- InlineArray/FixedBuffer: set each element via IContainerBinding
  |     +-- Leaf: parse JSON value to CLR type, call SetBoxed
  |
  +-- Unrecognized paths are silently skipped (forward compatibility)
```

---

## Source Structure

```
StructEdit.Json/
+-- EditDocumentJsonSerializer.cs   -- serialize + deserialize (static)
+-- EditSessionJsonExtensions.cs    -- ToJson / LoadJson extension methods
+-- StructEdit.Json.csproj
```

---

## Public API Reference

### EditDocumentJsonSerializer

```csharp
public static class EditDocumentJsonSerializer
{
    /// <summary>
    /// Serializes the current binding state of <paramref name="document"/> to a JSON string.
    /// </summary>
    public static string Serialize(EditDocument document);

    /// <summary>
    /// Deserializes <paramref name="json"/> into the existing document's bindings in place.
    /// Validates schema version and root type name.
    /// </summary>
    /// <exception cref="EditJsonMismatchException">
    /// When schema version or root type name do not match.
    /// </exception>
    public static void Deserialize(string json, EditDocument document);
}
```

### EditSessionJsonExtensions

```csharp
public static class EditSessionJsonExtensions
{
    /// <summary>
    /// Serializes the current binding state of the session's document.
    /// Returns indented JSON conforming to StructEdit 1.0 schema.
    /// </summary>
    public static string ToJson(this IEditSession session);

    /// <summary>
    /// Deserializes <paramref name="json"/> into the current session's bindings.
    /// Does NOT open a new session or discard unserialized fields.
    /// Call session.MarkStructuralChange() + session.RebuildDocument() afterwards
    /// when DynamicArray sizes changed.
    /// </summary>
    public static void LoadJson(this IEditSession session, string json);
}
```

### EditJsonMismatchException

```csharp
// Thrown by Deserialize / LoadJson when the JSON envelope does not match the session.
public sealed class EditJsonMismatchException : Exception
{
    public string ExpectedTypeName { get; }
    public string ActualTypeName   { get; }
}
```

---

## Dependencies

| Dependency | Version | Why |
|---|---|---|
| `StructEdit.Core` | project ref | `EditDocument`, `EditNode`, `IValueBinding` |

No NuGet dependencies. Uses only BCL `System.Text.Json`.

---

## Usage Examples

### Example 1: Save and restore a struct via JSON

```csharp
var service = new ComponentEditServiceBuilder().Build();

// Open and edit
using var session = service.Open(new WeaponConfig { Damage = 100f, FireRate = 10f },
                                 typeof(WeaponConfig));

var node = session.Document.Root.Children.First(n => n.Name == "Damage");
node.Binding!.SetBoxed(250f);

// Serialize to JSON
string json = session.ToJson();
Console.WriteLine(json);
// {
//   "structedit_version": "1.0",
//   "rootTypeName": "...",
//   "scope": "$",
//   "nodes": [
//     { "path": "$.Damage",   "kind": "Scalar", "value": 250.0 },
//     { "path": "$.FireRate", "kind": "Scalar", "value": 10.0  }
//   ]
// }

// In a later session, restore
using var session2 = service.Open(new WeaponConfig(), typeof(WeaponConfig));
session2.LoadJson(json);
var restored = (WeaponConfig)session2.Commit();
Console.WriteLine(restored.Damage);   // 250
Console.WriteLine(restored.FireRate); // 10
```

### Example 2: Persist struct data to file

```csharp
// Save
string json = session.ToJson();
File.WriteAllText("weapon_defaults.json", json, Encoding.UTF8);

// Load
string saved = File.ReadAllText("weapon_defaults.json", Encoding.UTF8);
using var restoreSession = service.Open(
    new WeaponConfig(), typeof(WeaponConfig));
restoreSession.LoadJson(saved);

// Check for structural changes after load (e.g. array size changed)
if (restoreSession.RebuildState == EditRebuildState.RebuildRequired)
    restoreSession.RebuildDocument();

var final = (WeaponConfig)restoreSession.Commit();
```

### Example 3: Detect schema mismatch

```csharp
// A JSON saved from a WeaponConfig session; now try loading into a BulletConfig session
string weaponJson = "...";
using var session = service.Open(new BulletConfig(), typeof(BulletConfig));
try
{
    session.LoadJson(weaponJson);
}
catch (EditJsonMismatchException ex)
{
    Console.WriteLine($"Type mismatch: expected {ex.ExpectedTypeName}, " +
                      $"got {ex.ActualTypeName}");
}
```

### Example 4: Forward compatibility -- unknown fields are silently ignored

```csharp
// A struct had a "LegacyBonus" field that was removed in v2.
// JSON from v1 that contains $.LegacyBonus will be silently skipped when
// loaded into the current v2 session. No exception is thrown.

string v1Json = """
{
  "structedit_version": "1.0",
  "rootTypeName": "My.WeaponConfig, MyAssembly",
  "scope": "$",
  "nodes": [
    { "path": "$.Damage",      "kind": "Scalar", "value": 50.0 },
    { "path": "$.LegacyBonus", "kind": "Scalar", "value": 10.0 }
  ]
}
""";

// WeaponConfig v2 no longer has LegacyBonus; that entry is skipped.
session.LoadJson(v1Json);
var result = (WeaponConfig)session.Commit();
Console.WriteLine(result.Damage); // 50
```

---

## Best Practices

1. **Call `RebuildDocument` after `LoadJson` when array sizes may change** -- if the
   JSON contained a `DynamicArray` with a different `count` than the current session, the
   tree is stale. Always check `RebuildState`:
   ```csharp
   session.LoadJson(json);
   if (session.RebuildState == EditRebuildState.RebuildRequired)
       session.RebuildDocument();
   ```

2. **The JSON is a snapshot of binding values, not a full component serializer** --
   do not use `StructEdit.Json` as a primary persistence format for complex object
   graphs with references, cycles, or polymorphism. It is designed for editor preset
   round-trips.

3. **Schema version mismatch is fatal** -- the deserializer checks `structedit_version`
   and the exact assembly-qualified root type name. Both must match. Build a migration
   layer if evolving persisted files across type renames.

4. **`Custom`, `Union`, `Unsupported` nodes are not serialized** -- if your component
   type has fields of these kinds, they will be absent from the JSON. Either ensure these
   fields have custom editors that produce leaf nodes, or handle them out of band.

5. **Thread safety** -- `EditDocumentJsonSerializer` is a static class using local
   `MemoryStream`/`Utf8JsonWriter` instances. It is safe to call concurrently from
   different threads on different documents, but not on the same session/document
   simultaneously.

---

## Advanced Notes

### Serialization Coverage by Node Kind

| EditNodeKind | Serialized? | Notes |
|---|---|---|
| `Scalar` | Yes | value as JSON number |
| `Boolean` | Yes | value as JSON bool |
| `String` | Yes | value as JSON string |
| `Enum` | Yes | value as string (enum name) |
| `Guid` | Yes | value as string (UUID format) |
| `DateTime` | Yes | value as string (ISO 8601, round-trip format `"O"`) |
| `DynamicArray` | Yes | `count` + `children` array of indexed values |
| `InlineArray` | Yes | `values` flat array |
| `FixedBuffer` | Yes | `values` flat array |
| `Struct` | Container only | no entry emitted; children serialized |
| `Class` | Container only | no entry emitted; children serialized |
| `Record` | Container only | no entry emitted; children serialized |
| `SelectionRoot` | Container only | no entry emitted |
| `BufferView` | Container only | no entry emitted |
| `Union` | Skipped | discriminator-driven nodes not serialized |
| `Custom` | Skipped | renderer-specific; skipped |
| `Unsupported` | Skipped | silently ignored |

### DateTime Serialization

`DateTime` values are serialized as ISO 8601 round-trip strings using the `"O"` format
specifier. This preserves `DateTimeKind` (`Local`, `Utc`, `Unspecified`) and
sub-second precision:

```json
{ "path": "$.OccurredAt", "kind": "DateTime", "value": "2026-05-23T10:30:00.0000000Z" }
```

Deserialization parses using `DateTime.Parse(s, null, DateTimeStyles.RoundtripKind)`.

### Enum Serialization

Enums are serialized as their name string, not as an integer:

```json
{ "path": "$.DamageType", "kind": "Enum", "value": "Fire" }
```

If the enum value is not defined (e.g. a flags combination), the numeric form is used.
Deserialization calls `Enum.Parse` with `ignoreCase: false`.

### Forward Compatibility Rules

The deserializer is designed to be forward-compatible:

- Unknown `path` values in the `nodes` array are silently skipped.
- Unknown `kind` values in entries cause that entry to be skipped.
- Extra JSON properties on entries (beyond `path`, `kind`, `value`/`count`/`children`)
  are ignored by `System.Text.Json` with the `PropertyNameCaseInsensitive` option.

Backward compatibility (reading new JSON with an old binary) is not guaranteed when:

- A field is removed from the component type (path lookup fails -> silently skipped).
- The `rootTypeName` changes due to assembly rename or namespace move (mismatch exception).

### Performance Notes

- Serialization uses `Utf8JsonWriter` with a `MemoryStream` backend. For components
  with up to ~200 leaf nodes, this is effectively zero-cost from a user perspective.
- Deserialization builds a `Dictionary<string, EditNode>` path map by walking the tree
  once (`O(n)` build). Subsequent path lookups are `O(1)`.
- Both operations are synchronous. For very large components (thousands of dynamic array
  elements), consider running serialization on a background task.

---

## Diagram: JSON Round-Trip

```
Component (struct/class/record)
  |
  +-- service.Open(component, type)
  |
  v
IEditSession
  |
  +-- session.ToJson()  ---> JSON string (StructEdit 1.0 schema)
  |                               |
  |                               | (store to file / clipboard / network)
  |                               |
  +-- new session                 |
  |   service.Open(new T(), type) |
  |                               v
  +-- session.LoadJson(json) <--- JSON string
  |
  +-- [optional] session.RebuildDocument()  (if DynamicArray count changed)
  |
  +-- session.Commit()
  |
  v
Updated component
```

---

## Diagram: Schema Format

```
{
  "structedit_version": "1.0",          -- must match SchemaVersion constant
  "rootTypeName":  "..., ...",          -- must match session's RootComponentType
  "scope": "$",                          -- EditDocument.Root.JsonPath
  "nodes": [
    {
      "path": "$.Field",                 -- matches EditNode.JsonPath
      "kind": "Scalar",                  -- matches EditNode.Kind.ToString()
      "value": 42                        -- CLR value in JSON representation
    },
    {
      "path": "$.Tags",
      "kind": "DynamicArray",
      "count": 2,
      "children": [
        { "index": 0, "value": "hero"   },
        { "index": 1, "value": "player" }
      ]
    }
  ]
}
```

### Error Handling Summary

| Situation | Behavior |
|---|---|
| `structedit_version` mismatch | `EditJsonMismatchException` |
| `rootTypeName` mismatch | `EditJsonMismatchException` |
| Unknown `path` in nodes array | Silently skipped (forward compat) |
| Unknown `kind` in entry | Silently skipped |
| Value parse failure (bad number) | `JsonException` from `System.Text.Json` |
| Null json argument | `ArgumentNullException` |
| Null document argument | `ArgumentNullException` |

### Encoding

Both `Serialize` and `Deserialize` work with UTF-8 strings. `Serialize` uses a
`MemoryStream` + `Utf8JsonWriter`, then decodes the result as UTF-8. All node name and
path strings are expected to be ASCII-compatible; field names containing non-ASCII
characters are written as UTF-8 JSON string escapes automatically by
`System.Text.Json`.

### Null Values

Null reference values in bindings are serialized as JSON `null`:

```json
{ "path": "$.Description", "kind": "String", "value": null }
```

On deserialize, `null` is passed to `SetBoxed(null)`. Whether the binding accepts null
depends on the field type (nullable reference types accept it; value types do not and
will receive the default value).

---

## Related Projects

| Project | Relationship |
|---|---|
| `StructEdit.Core` | Direct dependency -- `EditDocument`, `IEditSession` |
| `StructEdit.Reflection` | Peer -- provides `IEditSession`; no project ref in either direction |
| `StructEdit.Sample` | Consumer -- demonstrates JSON save/load |
| `Fdp.Engine` (FDP) | Production consumer -- persists inspector presets |
