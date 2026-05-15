# BATCH-03C Instructions — Corrective (Tasks Skipped in BATCH-03)

**Source batch:** BATCH-03 (review: CHANGES REQUIRED)
**Tasks in this batch:** 3 corrective items

---

## Context

Before starting, read these files:
- `.dev/replay-browser-2/DESIGN.md` §§3.4, 3.8 (translator dispatch, export service)
- `.dev/replay-browser-2/TASK-DETAILS.md` tasks RB-2.1 (correctives only)
- `.dev/replay-browser-2/DEBT-TRACKER.md` items RB02C-P2-001, RB02-P3-003, RB03-P2-001
- `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/RecordingExportService.cs` (full file)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Export/RecordingExportServiceTests.cs` (full file)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Diff/ComponentDiffServiceTests.cs` (DIF-T09 only)
- `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs` (full file)
- `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs`
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` (Translators property)
- `FDP/Toolkits/Fdp.Toolkits/Scenario/FdpAutoSerializer.cs` (_fieldAwareOptions, lines ~215-255)

---

## Task C1: Translator Dispatch in RecordingExportService (RB02C-P2-001)

### What to implement

`RecordingExportService.ExportToJson()` currently ignores `_serializer.Translators` entirely.
It always falls back to `autoSerializer.TryExtract()` for every component bit. Fix this:

**In the per-entity component-writing block** (inside the `foreach (Entity entity in query)` loop,
before the per-bit `for (int bit = 0; bit < 256; bit++)` loop), add:

```csharp
// Build a translator payload map for this entity.
// Translators claim component bits and return named payloads (keyed by component name).
var translatorPayloads = new System.Collections.Generic.Dictionary<string, JsonNode?>();
if (_serializer != null)
{
    foreach (var translator in _serializer.Translators)
    {
        if (!translator.CanTranslate(sandboxRepo, entity)) continue;
        var extracted = translator.Extract(sandboxRepo, entity, guidResolver);
        foreach (var kvp in extracted)
            translatorPayloads[kvp.Key] = kvp.Value as JsonNode;
    }
}
```

**In the per-bit loop**, replace:
```csharp
JsonObject? payload = autoSerializer.TryExtract(sandboxRepo, entity, bit, guidResolver);
```
with:
```csharp
JsonNode? payload;
if (translatorPayloads.TryGetValue(compName, out var translatorPayload))
    payload = translatorPayload;
else
    payload = autoSerializer.TryExtract(sandboxRepo, entity, bit, guidResolver);
```

Note: `WritePayloadNode` accepts `JsonNode?`, not `JsonObject?`. Adjust the method call
accordingly if the signature was `JsonObject?`. The signature should accept `JsonNode?`.

### The same fix applies to ExportChangelogToJson

`ExportChangelogToJson` also serializes entity components. Apply the same translator dispatch
there. Look for where it calls `autoSerializer.TryExtract` (or `BuildEntityStateNode`) and
add the same translator lookup before the per-component loop.

### Invariants

- Translators whose `CanTranslate()` returns `false` for an entity are NOT called.
- A translator's claimed component bits (from `GetConsumedComponentsMask()`) are NOT
  excluded from the per-bit loop — the translator's payload dict key (`"HarnessVelocity"`)
  is the lookup key; if the dict has that key, use the translator's payload, otherwise fall back.
  (This means translator payload takes priority over autoSerializer for the bits it covers.)

---

## Task C2: Strengthen EX-T22 (Translator Honored in ExportToJson)

### Current state

EX-T22 verifies translator in `ScenarioSerializer.Serialize()` context only. It also passes
`serializer` to `RecordingExportService` and checks the output file exists — but does NOT
assert the translator's payload appears in the exported JSON.

### What to change

Replace the second half of `EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto`:

```csharp
// Also verify: RecordingExportService actually invokes the translator for HarnessVelocity.
string fdpPath = BuildBasicRecordingWithVelocity(out var velEntity);
string outPath = Path.GetTempFileName() + ".json";
try
{
    new RecordingExportService(serializer: serializer)
        .ExportToJson(fdpPath, outPath, new JsonExportOptions());

    string text = File.ReadAllText(outPath);
    var root = JsonNode.Parse(text)!.AsObject();
    // Find the HarnessVelocity component in the first frame's entity list.
    bool found = false;
    foreach (var frame in root["Frames"]!.AsArray())
    {
        foreach (var entityNode in frame!["Entities"]!.AsArray())
        {
            foreach (var comp in entityNode!["Components"]!.AsArray())
            {
                if (comp!["ComponentType"]!.GetValue<string>() != "HarnessVelocity") continue;
                string payloadJson = comp["Payload"]!.ToJsonString();
                Assert.Contains("FooBlackboard", payloadJson);
                found = true;
            }
        }
    }
    Assert.True(found, "HarnessVelocity component entry with FooBlackboard payload not found in export.");
}
finally { TryDelete(outPath); }
```

Also add the helper `BuildBasicRecordingWithVelocity`:
```csharp
/// <summary>
/// Single-entity, 1-frame recording containing HarnessVelocity so EX-T22 can verify
/// translator dispatch in ExportToJson.
/// </summary>
private static string BuildBasicRecordingWithVelocity(out Entity entity)
{
    var h = new FdpRecordingHarness();
    h.SpawnEntity()
     .WithComponent(new HarnessPosition { X = 0f, Y = 0f, Z = 0f })
     .WithComponent(new HarnessVelocity { Vx = 1.5f, Vy = 2.5f });
    entity = h.LastSpawned;
    h.Tick().RecordKeyframe(100_000L);
    return h.BuildToTempFile();
}
```

Note: The `FdpRecordingHarness` registers both `HarnessPosition` and `HarnessVelocity` in the
`ComponentTypeRegistry`. After the harness builds the recording, `ScenarioSerializerBuilder.Build()`
will find both types (IDs 202 and 203) when it calls `autoSerializer.Build()`.

---

## Task C3: HarnessTransform + Strengthen EX-T20 (RB02-P3-003)

### HarnessTransform component

Add to `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Support/FdpRecordingHarness.cs`,
immediately after `HarnessVelocity`:

```csharp
// Component IDs 202-204 reserved for this file
[StructLayout(LayoutKind.Sequential)]
[ComponentId(204)]
public struct HarnessTransform
{
    public System.Numerics.Vector3 Position;
}
```

Also register it in `FdpRecordingHarness()` constructor:
```csharp
_repo.RegisterComponent<HarnessTransform>();
```

### Why Vector3 serializes as an array

`FdpAutoSerializer` uses `FdpJsonOptionsRegistry.DefaultRelaxed` (its `_fieldAwareOptions`)
which includes compact array converters for `Vector3`. So `Vector3 Position` will be serialized
by `FdpAutoSerializer` as `{"Position": [x, y, z]}` — a JSON array inside the component payload.
`FlattenNumericArrays` then collapses any multi-line numeric array to a single line.

### Strengthen EX-T20

Replace `EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine` with:

```csharp
[Fact]
public void EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine()
{
    // Build a recording with HarnessTransform (has Vector3 Position field).
    // FdpAutoSerializer serializes Vector3 as a JSON array [x, y, z].
    // FlattenNumericArrays must collapse the multi-line array to a single line.
    string fdpPath = BuildRecordingWithTransform(out _);
    string outPath = Path.GetTempFileName() + ".json";
    try
    {
        new RecordingExportService().ExportToJson(fdpPath, outPath, new JsonExportOptions());
        string text = File.ReadAllText(outPath);

        // Locate the HarnessTransform component payload and assert the Position array
        // is on a single line (no embedded newline inside [...]).
        var root = JsonNode.Parse(text)!.AsObject();
        bool foundTransform = false;
        foreach (var frame in root["Frames"]!.AsArray())
        {
            foreach (var entityNode in frame!["Entities"]!.AsArray())
            {
                foreach (var comp in entityNode!["Components"]!.AsArray())
                {
                    if (comp!["ComponentType"]!.GetValue<string>() != "HarnessTransform") continue;
                    string payloadJson = comp["Payload"]!.ToJsonString();
                    // The Position value is a JSON array [x, y, z].
                    // After FlattenNumericArrays it must be on one line: no \n inside [...]
                    Assert.DoesNotMatch(
                        new System.Text.RegularExpressions.Regex(@"\[\s*[0-9eE.+\-]+\s*,\s*\n"),
                        payloadJson);
                    // The array itself must exist in the payload.
                    Assert.Matches(
                        new System.Text.RegularExpressions.Regex(@"\["),
                        payloadJson);
                    foundTransform = true;
                }
            }
        }
        Assert.True(foundTransform, "HarnessTransform component entry not found in export output.");
    }
    finally { TryDelete(outPath); }
}
```

Add the helper:
```csharp
private static string BuildRecordingWithTransform(out Entity entity)
{
    var h = new FdpRecordingHarness();
    h.SpawnEntity()
     .WithComponent(new HarnessPosition { X = 1f, Y = 0f, Z = 0f })
     .WithComponent(new HarnessTransform { Position = new System.Numerics.Vector3(1f, 2f, 3f) });
    entity = h.LastSpawned;
    h.Tick().RecordKeyframe(100_000L);
    return h.BuildToTempFile();
}
```

**Note on `AddComponent`:** `FdpRecordingHarness.WithComponent<T>()` calls `_repo.AddComponent()`.
For `HarnessTransform`, if the entity was spawned without it initially, use `h.AddComponent(entity, ...)`.
The cleaner approach is to call `WithComponent` in the chain (shown above) which adds component
to `_lastSpawned` immediately after spawn.

---

## Task C4: Fix DIF-T09 Allocation Budget (RB03-P2-001)

### Problem

The current DIF-T09 creates new `JsonObject` instances inside the 1000-iteration loop,
measuring both input-data construction AND the diff algorithm. The spec budget was 1 MB;
the test used 512 MB. The test should measure algorithm allocation, not input construction.

### Fix

Replace the `DIF_T09_AllocationBudget_1000Calls_Under512MB` method with:

```csharp
// ── DIF-T09: Allocation budget ─────────────────────────────────────────

[Fact]
public void DIF_T09_AllocationBudget_1000Calls_Under100MB()
{
    // Pre-build the JSON string once — parse it per iteration to satisfy JsonNode's
    // ownership model (each JsonNode can belong to only one parent at a time).
    // This isolates the measurement to parse + diff + output, not to JsonObject
    // builder calls.
    var sb = new System.Text.StringBuilder();
    sb.Append("{");
    for (int i = 0; i < 200; i++)
    {
        if (i > 0) sb.Append(",");
        sb.Append($"\"Prop{i}\":{(double)i}");
    }
    sb.Append("}");
    string jsonStr = sb.ToString();

    // Warm up JIT
    {
        var a = JsonNode.Parse(jsonStr)!;
        var b = JsonNode.Parse(jsonStr)!;
        _svc.ComputeDiff("root", a, b, 0.001);
    }

    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
    long allocBefore = GC.GetTotalAllocatedBytes(true);

    for (int i = 0; i < 1000; i++)
    {
        var a = JsonNode.Parse(jsonStr)!;
        var b = JsonNode.Parse(jsonStr)!;
        _svc.ComputeDiff("root", a, b, 0.001);
    }

    long allocAfter = GC.GetTotalAllocatedBytes(true);
    long allocatedBytes = allocAfter - allocBefore;

    // 100 MB budget: each call parses two 200-field JSON objects + runs the diff.
    // This guards against algorithmic allocation regressions without being
    // sensitive to JsonNode's baseline overhead (~50-100 KB/call from JSON parsing).
    Assert.True(allocatedBytes < 100L * 1024 * 1024,
        $"Allocated {allocatedBytes / 1024} KB for 1000 calls; expected < 100 MB.");
}
```

**Rename the test method** (the old name included "512MB"). Only the method body and name
change; the surrounding test class, fixture, and IDisposable structure remain the same.

---

## Verification

After implementing all tasks, run:
```
dotnet test FDP/FDP.sln --filter "FullyQualifiedName~ReplayBrowser" --no-build
dotnet build FDP/FDP.sln
```

All 60+ tests must pass. The new EX-T22 assertion and EX-T20 component lookup must both
`Assert.True(found, ...)` / `Assert.True(foundTransform, ...)`.

Write `.dev/replay-browser-2/reports/BATCH-03C-REPORT.md` with:
- Summary of each task
- All modified files
- Test results (pass count)
- Any deviations from these instructions with rationale
