# BATCH-03 Instructions — Phase 4+5: Deserializer and Descriptor Registry

**Workstream:** tkb-1  
**Batch:** 03  
**Tasks covered:** TKB-010 (first, prerequisite), TKB-009  
**Design reference:** `.dev/tkb-1/DESIGN.md`  
**Task detail reference:** `.dev/tkb-1/TASK-DETAIL.md` (see TKB-009, TKB-010 sections)

---

## Overview

This batch implements the streaming deserialization pipeline:

- **TKB-010** (`TkbDescriptorRegistry`): static registry mapping descriptor names to parser
  thunks. Implement this first since `TkbDeserializer` depends on it.
- **TKB-009** (`TkbDeserializer` + `TkbFormatException`): streams a JSON entity file, builds
  a `TkbTemplate` via the registry, and registers it in `ITkbDatabase`.

**Target framework:** `net8.0`. The `Dictionary<K,V>.GetAlternateLookup<ReadOnlySpan<char>>()`
method was added in .NET 9, NOT .NET 8. See Task 1 for the .NET 8-compatible approach.

Read `TASK-DETAIL.md` sections TKB-009 and TKB-010 for authoritative specifications and success
conditions.

---

## Task 1 — TKB-010: `TkbDescriptorRegistry`

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDescriptorRegistry.cs`  
**Namespace:** `Fdp.Toolkit.Tkb`

### Design

This is a static, thread-unsafe registry that maps a `HierarchicalName` string to a parser
delegate. It is populated once at startup (by source-generated `[ModuleInitializer]` code in
Phase 5) and then read-only for the lifetime of the process.

### Delegate type

```csharp
/// <summary>
/// Parses a JSON sub-element into a descriptor DTO and stores it on the template.
/// </summary>
public delegate void TkbDescriptorParserThunk(
    TkbTemplate template, int partId, System.Text.Json.JsonElement jsonElement);
```

### Implementation

```csharp
public static class TkbDescriptorRegistry
{
    private static readonly Dictionary<string, TkbDescriptorParserThunk> _parsers
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a parser for the given hierarchical name.
    /// Last registration wins (typically called once per type from ModuleInitializer).
    /// </summary>
    public static void RegisterParser(
        string hierarchicalName, TkbDescriptorParserThunk parser)
    {
        _parsers[hierarchicalName] = parser;
    }

    /// <summary>
    /// Returns a copy of the current name-to-thunk map for use by TkbDeserializer.
    /// The returned dictionary uses OrdinalIgnoreCase and is a snapshot (not live).
    /// </summary>
    /// <remarks>
    /// NOTE: Dictionary.GetAlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;() requires .NET 9+.
    /// This project targets net8.0, so the deserializer must call .TryGetValue with
    /// key.ToString() on the hot path. When this project is upgraded to .NET 9+,
    /// replace this method with GetAlternateLookup and update TkbDeserializer accordingly.
    /// </remarks>
    public static bool TryGetParser(
        ReadOnlySpan<char> hierarchicalName,
        out TkbDescriptorParserThunk? thunk)
    {
        // net8.0: must allocate a string for the lookup key.
        // This is acceptable since parsing is a startup-time operation.
        return _parsers.TryGetValue(hierarchicalName.ToString(), out thunk);
    }

    /// <summary>
    /// Clears all registered parsers. For testing only.
    /// </summary>
    internal static void Clear() => _parsers.Clear();
}
```

---

## Task 2 — TKB-009: `TkbFormatException`

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbFormatException.cs`  
**Namespace:** `Fdp.Toolkit.Tkb`

Simple exception class:

```csharp
using System;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Thrown when a TKB entity file has structural problems that prevent parsing.
    /// </summary>
    public sealed class TkbFormatException : Exception
    {
        public TkbFormatException(string message) : base(message) { }
        public TkbFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
```

---

## Task 3 — TKB-009: `TkbDeserializer`

**File to create:** `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDeserializer.cs`  
**Namespace:** `Fdp.Toolkit.Tkb`

### Specification

```csharp
using System;
using System.Text.Json;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Vfs;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Parses a TKB entity JSON file and registers the resulting TkbTemplate
    /// in an ITkbDatabase.
    /// </summary>
    public sealed class TkbDeserializer
    {
        /// <summary>
        /// Parses the JSON content from <paramref name="file"/>, builds a
        /// <see cref="TkbTemplate"/>, and registers it in <paramref name="db"/>.
        /// </summary>
        /// <exception cref="TkbFormatException">
        /// Thrown if the JSON root element is missing the required <c>$guid</c> field.
        /// </exception>
        public void ParseAndRegister(TkbEntityFile file, ITkbDatabase db)
        {
            using var doc = JsonDocument.Parse(file.JsonStream);
            var root = doc.RootElement;

            // $guid is mandatory — fail fast.
            if (!root.TryGetProperty("$guid", out var guidProp))
                throw new TkbFormatException(
                    $"Entity '{file.FileName}' in '{file.CategoryPath}' is missing $guid.");
            long tkbId = guidProp.GetInt64();

            var template = new TkbTemplate(file.FileName, tkbId, file.CategoryPath);

            foreach (var prop in root.EnumerateObject())
            {
                ReadOnlySpan<char> name = prop.Name;

                // Skip reserved metadata fields: anything starting with a non-letter
                // (covers $guid, $schema, _EditorMetadata, etc.)
                if (name.IsEmpty || !char.IsLetter(name[0])) continue;

                // Split "Gen.AmmoWeaponBallistics#2" into key="Gen.AmmoWeaponBallistics" + partId=2.
                int hashIdx = name.IndexOf('#');
                ReadOnlySpan<char> key = hashIdx < 0 ? name : name[..hashIdx];
                int partId = 0;
                if (hashIdx >= 0 && hashIdx + 1 < name.Length)
                    int.TryParse(name[(hashIdx + 1)..], out partId);

                // Dispatch to the registered parser thunk; silently skip unknown keys.
                if (TkbDescriptorRegistry.TryGetParser(key, out var thunk) && thunk != null)
                    thunk(template, partId, prop.Value);
            }

            db.Register(template);
        }
    }
}
```

**Key rules:**
- One `JsonDocument` per entity file — disposed via `using` before the method returns.
- No `string.Substring` — use `ReadOnlySpan<char>` slicing for the `#PartId` split.
- Unknown descriptor keys are silently skipped (no exception, no warning).
- Fields where `name[0]` is not a letter are skipped (handles `$guid`, `_EditorMetadata`, etc.).

---

## Task 4 — Tests

**New test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDeserializerTests.cs`  
**Namespace:** `Fdp.Toolkit.Tkb.Tests`

### Test setup

Before tests, register parser thunks for the 3 standard DTOs using `TkbDescriptorRegistry`.
Use `[Collection("TkbDeserializerTests")]` with the `[CollectionDefinition]` / fixture pattern
OR use a static constructor or an `IAsyncLifetime` to register parsers. The simplest approach:
a shared fixture class that registers parsers once and clears after use.

Because `TkbDescriptorRegistry` is a static singleton, tests that manipulate it should run
serially. Use `[Collection("SerialTests")]` if that collection is already defined in the test
project, OR create a dedicated `[CollectionDefinition("TkbDeserializerTests")]`.

### JSON test fixtures

Create the following JSON strings as inline string constants in the test class (no separate
files needed — use `Encoding.UTF8.GetBytes(...)` to create a `MemoryStream` for `TkbEntityFile`).

**`M1_Abrams.json` (inline constant `AbramsJson`):**
```json
{
  "$guid": 100,
  "TkbMaster": {
    "CustomName": "M1 Abrams"
  },
  "Gen.VehicleParameters": {
    "Mass": 61000.0,
    "Length": 7.93,
    "Width": 3.66,
    "MaxSpeedFwd": 20.0,
    "MaxSpeedRev": 12.0,
    "MaxAccel": 2.5
  },
  "Gen.WeaponCapabilities": {
    "EffectiveRange": 3000.0,
    "RateOfFire": 6.0,
    "MagazineCapacity": 42
  },
  "_EditorMetadata": { "Author": "test" }
}
```

**`Missing_Guid.json` (inline constant `MissingGuidJson`):**
```json
{
  "Gen.VehicleParameters": {
    "Mass": 1000.0
  }
}
```

**`Unknown_Descriptors.json` (inline constant `UnknownDescJson`):**
```json
{
  "$guid": 200,
  "CGFX.ABSTRACT_ENTITY": { "foo": "bar" },
  "Future.NotYetRegistered": { "x": 1 }
}
```

**`Ammo_Apfsds.json` (inline constant `AmmoJson`):**
```json
{
  "$guid": 300,
  "Gen.AmmoWeaponBallistics#1": {
    "WeaponGuid": 10,
    "MuzzleSpeed": 1700.0,
    "Damage": 500.0
  },
  "Gen.AmmoWeaponBallistics#2": {
    "WeaponGuid": 11,
    "MuzzleSpeed": 1600.0,
    "Damage": 450.0
  }
}
```

### Helper method

Add a private helper that creates a `TkbEntityFile` from a JSON string:

```csharp
private static TkbEntityFile MakeFile(string fileName, string json,
    string categoryPath = "Test/Category")
{
    var stream = new System.IO.MemoryStream(
        System.Text.Encoding.UTF8.GetBytes(json));
    return new TkbEntityFile(categoryPath, fileName, stream);
}
```

### Tests to write (minimum 8)

**Registration/parsing:**

1. `ParseAndRegister_ValidEntity_TemplateHasCorrectTkbType`  
   Parse `AbramsJson` → verify `template.TkbType == 100`.

2. `ParseAndRegister_ValidEntity_TemplateHasCorrectCategoryPath`  
   Parse `AbramsJson` with categoryPath `"Platform/Vehicle"` → verify `template.CategoryPath == "Platform/Vehicle"`.

3. `ParseAndRegister_ValidEntity_HasVehicleParametersDto`  
   Parse `AbramsJson` → verify `template.HasDescriptor<VehicleParametersDto>()` is true and `dto.Mass == 61000f`.

4. `ParseAndRegister_ValidEntity_HasTkbMasterDto`  
   Parse `AbramsJson` → verify `template.GetDescriptor<TkbMasterDto>()!.CustomName == "M1 Abrams"`.

5. `ParseAndRegister_ValidEntity_HasWeaponCapabilitiesDto`  
   Parse `AbramsJson` → verify `dto.EffectiveRange == 3000f` and `dto.MagazineCapacity == 42`.

**Error handling:**

6. `ParseAndRegister_MissingGuid_ThrowsTkbFormatException`  
   Parse `MissingGuidJson` → verify `throws TkbFormatException`.

**Skip logic:**

7. `ParseAndRegister_UnknownDescriptors_ParsesWithoutThrowing`  
   Parse `UnknownDescJson` → verify no exception; template has `TkbType == 200`; template has NO
   `VehicleParametersDto` or `WeaponCapabilitiesDto`.

8. `ParseAndRegister_MetadataKey_IsSkipped`  
   Parse `AbramsJson` (which contains `_EditorMetadata`) → verify parsing succeeds and the
   template does NOT have any descriptor for the metadata key (cannot call `HasDescriptor` for
   `_EditorMetadata` since it has no DTO type; just verify the overall parse succeeds and
   standard descriptors are present).

**Multi-part:**

9. `ParseAndRegister_MultiplePartIds_BothAmmoBallisticsStored`  
   Parse `AmmoJson` → verify:
   - `template.HasDescriptor<AmmoWeaponBallisticsDto>(partId: 1)` is true with `WeaponGuid == 10`
   - `template.HasDescriptor<AmmoWeaponBallisticsDto>(partId: 2)` is true with `WeaponGuid == 11`

**LOH / hot-path:**

10. `ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap`  
    Parse 10,000 entity files (same small JSON reused), each with a unique `$guid`.
    After parsing, use `GC.GetAllocatedBytesForCurrentThread()` before and after the loop;
    assert that no individual entity parse allocates >= 85,000 bytes.

    Implementation sketch:
    ```csharp
    long before = GC.GetAllocatedBytesForCurrentThread();
    const int count = 10_000;
    for (int i = 0; i < count; i++)
    {
        var file = MakeFile($"Entity_{i}", SmallEntityJson(i), "Platform");
        _deserializer.ParseAndRegister(file, _db);
    }
    long after = GC.GetAllocatedBytesForCurrentThread();
    long perEntity = (after - before) / count;
    Assert.True(perEntity < 85_000,
        $"Average allocation per entity ({perEntity} bytes) must be below LOH threshold (85,000 bytes).");
    ```
    
    Where `SmallEntityJson(int i)` returns a minimal JSON string with a unique `$guid`:
    ```csharp
    private static string SmallEntityJson(int i) =>
        $"{{\"$guid\":{1000 + i},\"TkbMaster\":{{\"CustomName\":\"Entity{i}\"}}}}";
    ```

### Parser registration

The tests need parsers registered in `TkbDescriptorRegistry`. Register them in a shared
fixture or `[ClassInitialize]`-equivalent:

```csharp
// Register parser thunks for the 4 standard DTOs.
// Use FdpJsonOptionsRegistry.DefaultRelaxed for deserialization.
TkbDescriptorRegistry.RegisterParser("TkbMaster", (template, partId, elem) =>
{
    var dto = elem.Deserialize<TkbMasterDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
    template.AddDescriptor(dto, partId);
});
TkbDescriptorRegistry.RegisterParser("Gen.VehicleParameters", (template, partId, elem) =>
{
    var dto = elem.Deserialize<VehicleParametersDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
    template.AddDescriptor(dto, partId);
});
TkbDescriptorRegistry.RegisterParser("Gen.WeaponCapabilities", (template, partId, elem) =>
{
    var dto = elem.Deserialize<WeaponCapabilitiesDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
    template.AddDescriptor(dto, partId);
});
TkbDescriptorRegistry.RegisterParser("Gen.AmmoWeaponBallistics", (template, partId, elem) =>
{
    var dto = elem.Deserialize<AmmoWeaponBallisticsDto>(FdpJsonOptionsRegistry.DefaultRelaxed)!;
    template.AddDescriptor(dto, partId);
});
```

Because `TkbDescriptorRegistry` is static and shared across tests, use a fixture class:

```csharp
[CollectionDefinition("TkbDeserializerTests")]
public class TkbDeserializerCollection { }

public class TkbDeserializerFixture : IDisposable
{
    public TkbDeserializerFixture()
    {
        TkbDescriptorRegistry.Clear();
        TkbDescriptorRegistry.RegisterParser("TkbMaster", ...);
        TkbDescriptorRegistry.RegisterParser("Gen.VehicleParameters", ...);
        TkbDescriptorRegistry.RegisterParser("Gen.WeaponCapabilities", ...);
        TkbDescriptorRegistry.RegisterParser("Gen.AmmoWeaponBallistics", ...);
    }

    public void Dispose()
    {
        TkbDescriptorRegistry.Clear();
    }
}

[Collection("TkbDeserializerTests")]
public class TkbDeserializerTests : IClassFixture<TkbDeserializerFixture>
{
    private readonly TkbDeserializer _deserializer = new();
    private readonly TkbDatabase _db = new();

    public TkbDeserializerTests(TkbDeserializerFixture _) { }
    // ...
}
```

Note: If the test project already has a `[CollectionDefinition("SerialTests")]` defined,
reuse it. Check `FDP/Toolkits/Fdp.Toolkits.Tests/` for existing collection definitions.

Also add `using Fdp.Core.Serialization;` for `FdpJsonOptionsRegistry`.

---

## Task 5 — Tests for `TkbDescriptorRegistry`

**New test class or file:** Add to `FDP/Toolkits/Fdp.Toolkits.Tests/Tkb/TkbDescriptorRegistryTests.cs`

Tests (minimum 4, within a separate class that also clears the registry):

1. `RegisterParser_ThenTryGetParser_ReturnsTrueAndThunk`  
   Register a no-op thunk for `"Test.Foo"`, then `TryGetParser("Test.Foo".AsSpan(), out var t)` → true.

2. `TryGetParser_UnregisteredName_ReturnsFalse`  
   `TryGetParser("NonExistent".AsSpan(), out _)` → false.

3. `RegisterParser_CaseInsensitive_FoundWithDifferentCase`  
   Register `"gen.vehicleparameters"`, look up `"Gen.VehicleParameters"` → found.

4. `RegisterParser_Overwrite_ReturnsLatestThunk`  
   Register `"Test.Bar"` twice with different thunks; look up → second thunk returned.

---

## Build and test verification

```powershell
# From workspace root d:\Work\IOS-IG-SimHost-FDP-2

# 1. Build FDP
cd FDP
dotnet build FDP.sln
cd ..

# 2. Run all TKB tests
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --filter "FullyQualifiedName~Tkb"

# 3. Run full Toolkits test suite
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj
```

Zero build errors. All TKB-scoped tests pass. No regression in existing tests.

---

## Report

Write report to `.dev/tkb-1/reports/BATCH-03-REPORT.md`:
- Files created/modified
- Test counts (new tests added)
- Build and test output
- Any deviations and justification
- P2/P3 issues found

---

## Notes

- Do NOT add `using System.Runtime.CompilerServices;` module initializers to `TkbDescriptorRegistry`
  itself — those are emitted by the source generator (TKB-011, Phase 5, next batch).
- `FdpJsonOptionsRegistry.DefaultRelaxed` is in namespace `Fdp.Core.Serialization`.
- The `_parsers` dictionary in `TkbDescriptorRegistry` is accessed from tests via the
  `internal static void Clear()` method. The test project must be able to see `internal`
  members — check for `[assembly: InternalsVisibleTo("Fdp.Toolkits.Tests")]` in
  `FDP/Toolkits/Fdp.Toolkits/` and add it if missing.
- The LOH test (`ParseAndRegister_LargeVolume_DoesNotAllocateOnLargeObjectHeap`) is a
  best-effort regression guard. If the test is flaky (GC behavior can vary), wrap the
  assertion with a comment noting this is a heuristic, not a strict guarantee.
- `AmmoWeaponBallisticsDto.WeaponGuid` is a `long`; verify the JSON `10` and `11` parse
  correctly.
- After registering templates in the LOH test, call `_db.Clear()` in a finally block to
  prevent registration conflicts between test runs (since `TkbDatabase` throws on duplicate
  `TkbType` registration).
