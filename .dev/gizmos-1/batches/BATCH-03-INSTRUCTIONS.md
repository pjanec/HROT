# BATCH-03 Instructions — Gizmo Settings Store

**Tasks:** GZ007, GZ008
**Phase:** Phase 3 — Settings Store
**Design references:** TASK-DETAIL.md §TASK-GZ007, §TASK-GZ008; DESIGN.md §3.1–3.3

---

## Context

This batch builds the gizmo settings store. It adds no ECS systems. All new code lives in
`FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/`.

The test file is `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs`.

Previously, BATCH-01 delivered the primitive layer (Rgba32, DebugPrimitive, etc.) and BATCH-02
delivered the gizmo lifecycle contracts and orchestration systems.

---

## Codebase conventions to follow

- Namespace: `Fdp.Toolkit.Diagnostics.Gizmos.Settings` (source), `Fdp.Toolkit.Diagnostics.Gizmos.Tests` (tests)
- `[assembly: InternalsVisibleTo("Fdp.Toolkits.Tests")]` is already present.
- Test framework: **xUnit** with Fact/Theory. No NUnit, no MSTest.
- Test project path: `FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj`
- ECS event publishing: use `IEntityCommandBuffer.PublishEvent<T>` (from `Fdp.Interfaces`).
- FNV-1a 32-bit hash: `uint h = 2166136261; foreach(char c in name) { h ^= c; h *= 16777619; } return h;`
  This is the same algorithm used by `StringInternMap.Fnv1a32(string)`.
- Do NOT add XML doc comments to code you did not create. Do add concise summaries to all new types.

---

## Task GZ007 — GizmoSettingValue and GizmoSettingsRegistry

Full spec in TASK-DETAIL.md §TASK-GZ007.

### File: `GizmoSettingValue.cs`

```csharp
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    public enum SettingType : byte { Bool = 0, Int32 = 1, Float32 = 2 }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct GizmoSettingValue : IEquatable<GizmoSettingValue>
    {
        [FieldOffset(0)] public SettingType Type;
        [FieldOffset(4)] public bool BoolValue;
        [FieldOffset(4)] public int IntValue;
        [FieldOffset(4)] public float FloatValue;

        public static GizmoSettingValue From(bool v)  => new() { Type = SettingType.Bool,    BoolValue = v };
        public static GizmoSettingValue From(int v)   => new() { Type = SettingType.Int32,   IntValue  = v };
        public static GizmoSettingValue From(float v) => new() { Type = SettingType.Float32, FloatValue = v };

        public bool Equals(GizmoSettingValue other) ...
        public override bool Equals(object? obj) ...
        public override int GetHashCode() ...
        public static bool operator ==(GizmoSettingValue l, GizmoSettingValue r) => l.Equals(r);
        public static bool operator !=(GizmoSettingValue l, GizmoSettingValue r) => !l.Equals(r);
    }
}
```

The Equals implementation must compare `Type` and the 4-byte payload. Since `bool`, `int`, and `float`
all occupy the same 4 bytes at offset 4, comparing `IntValue` after checking `Type` is sufficient.

### File: `GizmoSettingsRegistry.cs`

```csharp
namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    public sealed class GizmoSettingsRegistry
    {
        private readonly Dictionary<uint, GizmoSettingValue> _active  = new();
        private readonly Dictionary<uint, GizmoSettingValue> _defaults = new();
        private readonly Dictionary<uint, string>            _keyNames = new();
        private bool _isDirty;

        public bool IsDirty => _isDirty;

        // Optional cold-path notification; not called on the hot Execute path.
        public event Action<uint>? OnSettingChanged;

        public void RegisterSetting(string keyName, GizmoSettingValue defaultValue)
        public GizmoSettingValue Read(uint keyHash)
        public void Write(uint keyHash, GizmoSettingValue value, IEntityCommandBuffer? cmd = null)
        public void ResetToDefault(uint keyHash)
        public static uint ComputeHash(string name)
        public IEnumerable<(string Key, GizmoSettingValue Active, GizmoSettingValue Default)> EnumerateAll()
    }
}
```

**RegisterSetting:** If `keyHash` is not in `_active`, add to both `_active` and `_defaults` with
`defaultValue`. Store `keyName` in `_keyNames`. If already present, only update `_defaults`
if `defaultValue` differs (migration support). Do not overwrite an existing active value.

**Write:** Update `_active[keyHash] = value`. Set `_isDirty = true`. If `cmd != null`, call
`cmd.PublishEvent(new GizmoSettingChangedEvent { KeyHash = keyHash })`. Fire `OnSettingChanged?.Invoke(keyHash)`.

**ResetToDefault:** If `_defaults.TryGetValue(keyHash, out var def)`, set `_active[keyHash] = def`.
Set `_isDirty = false` (only if no other key is dirty — or simply track dirty per-key if you prefer;
a simple bool flag is acceptable for this phase).

**Read:** Return `_active.TryGetValue(keyHash, out var v) ? v : default`.

**ComputeHash:** FNV-1a 32-bit as specified.

**EnumerateAll:** Yield `(keyName, active, defaultValue)` for all registered keys.

---

## Task GZ008 — Settings Persistence and GizmoSettingChangedEvent

Full spec in TASK-DETAIL.md §TASK-GZ008.

### File: `GizmoSettingChangedEvent.cs`

EventId 8050 is confirmed available (no collision in the codebase).

```csharp
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    [EventId(8050)]
    public struct GizmoSettingChangedEvent
    {
        public uint KeyHash;
    }
}
```

### File: `GizmoSettingsPersistence.cs`

```csharp
using System.Text.Json;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    public static class GizmoSettingsPersistence
    {
        public static void SaveOverrides(GizmoSettingsRegistry registry, string filePath)
        public static void LoadOverrides(GizmoSettingsRegistry registry, string filePath)
    }
}
```

**SaveOverrides:** Enumerate `registry.EnumerateAll()`. Include only entries where
`active != defaultValue` (user-changed settings). Write a JSON array of objects:
```json
[
  { "key": "NavMesh.ShowGrid", "type": "Bool", "value": "True" },
  { "key": "PathfindingGizmo.Thickness", "type": "Float32", "value": "2.5" }
]
```
Use `System.Text.Json.Utf8JsonWriter` or `JsonSerializer.Serialize`. After writing, reset the
registry's `IsDirty` flag (call a new `void ClearDirty()` method on `GizmoSettingsRegistry`,
or just set the field if persistence is in the same assembly).

**LoadOverrides:** If `!File.Exists(filePath)` return silently. Read JSON. For each entry:
1. Compute `hash = GizmoSettingsRegistry.ComputeHash(key)`.
2. If the registry does not have a registered default for this key (forward-compat scenario),
   call `registry.RegisterSetting(key, default(GizmoSettingValue))`.
3. Parse the value back to `GizmoSettingValue` using `type` field.
4. Call `registry.Write(hash, parsedValue)` (no cmd — no event on load).

**JSON format notes:** Use `System.Text.Json`. Do not add Newtonsoft.Json. Keep it simple:
serialize as an array of `{ "key": ..., "type": ..., "value": ... }` records.

---

## Tests (`GizmosSettingsTests.cs`)

Test class layout:

```
GizmoSettingValueTests        -- SC-GZ007-6, SC-GZ007-7, value equality
GizmoSettingsRegistryTests    -- SC-GZ007-1 through SC-GZ007-5
GizmoSettingsPersistenceTests -- SC-GZ008-1 through SC-GZ008-5
GizmoSettingChangedEventTests -- SC-GZ008-4 (event publish via cmd)
```

The existing gizmo test infrastructure (`GizmoTestRepo`) registers `ConstructionOrder`,
`DestructionOrder`, and `ClearBehaviorEvent`. You must also register `GizmoSettingChangedEvent`
before testing it:

```csharp
repo.RegisterEvent<GizmoSettingChangedEvent>();
```

**Minimum success conditions to test (all must pass):**

- **SC-GZ007-1:** Register a `bool` setting. `Read(ComputeHash(key))` returns `BoolValue == false`.
- **SC-GZ007-2:** `Write(hash, From(true))` → `Read(hash).BoolValue == true`.
- **SC-GZ007-3:** `ResetToDefault(hash)` after write restores original default.
- **SC-GZ007-4:** `Read` for unregistered hash returns `default(GizmoSettingValue)`, no throw.
- **SC-GZ007-5:** Register two distinct string keys. Writes to one do not affect the other
  (verifies hash isolation; distinct keys, no need to manufacture a real collision).
- **SC-GZ007-6:** `GizmoSettingValue.From(3.14f)` → `FloatValue` reads back as exactly `3.14f`;
  `Type == SettingType.Float32`.
- **SC-GZ007-7:** `Marshal.SizeOf<GizmoSettingValue>() == 8`.
- **SC-GZ008-1:** SaveOverrides to a temp file, LoadOverrides into a fresh registry, verify
  restored value equals saved value.
- **SC-GZ008-2:** Default-value settings are NOT written to disk (verify file does not contain
  the key whose value equals the registered default).
- **SC-GZ008-3:** LoadOverrides with a non-existent file path does not throw.
- **SC-GZ008-4:** `registry.Write(hash, value, cmd)` where `cmd` is a real (or mock)
  `IEntityCommandBuffer`; draining events yields `GizmoSettingChangedEvent { KeyHash == hash }`.
- **SC-GZ008-5:** `ResetToDefault(hash)`, then `SaveOverrides` — file does NOT contain that key.

For SC-GZ008-4, use a real `EntityRepository` with `GizmoSettingChangedEvent` registered, obtain
`view.GetCommandBuffer()` as the `IEntityCommandBuffer`, call `registry.Write(hash, value, cmd)`,
then flush (swap buffers), and assert `view.ReadEvents<GizmoSettingChangedEvent>()` contains one
event with the correct `KeyHash`.

**Additional quality tests (beyond the SC list):**

- Equality: `From(true) == From(true)`, `From(true) != From(false)`, `From(1) != From(1.0f)`.
- `OnSettingChanged` fires exactly once per `Write`, with the correct hash.
- `IsDirty` is false after `RegisterSetting`; becomes true after `Write`; becomes false after
  `ResetToDefault` (or `SaveOverrides` clears it).
- Int32 round-trip: `From(42)` → `IntValue == 42`.

---

## Deliverables

1. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingValue.cs`
2. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`
3. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingChangedEvent.cs`
4. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsPersistence.cs`
5. `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs`

## Verification command

```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --nologo
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmos"
```

All gizmo tests (74 prior + new settings tests) must pass. Report total count in BATCH-03-REPORT.md.

## Report

Write `d:\Work\IOS-IG-SimHost-FDP-2\.dev\gizmos-1\reports\BATCH-03-REPORT.md` listing:
- Files created
- Test results (pass count, fail count)
- Any design deviations from this spec and the reason for each
