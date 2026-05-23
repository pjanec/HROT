# Fdp.Tools.RecordingDumper

**Project path:** `FDP/Tools/Fdp.Tools.RecordingDumper/`
**Assembly name:** `fdp-recording-dumper`
**Target framework:** net8.0
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` was found inside `FDP/Tools/Fdp.Tools.RecordingDumper/`. This document
serves as the authoritative reference in its absence.

---

## Executive Overview

`fdp-recording-dumper` is a command-line tool that converts binary FDP simulation
recordings (`.fdp` files) into human-readable JSON.

### What It Does

During a simulation run, `AsyncRecorder` + `RecorderSystem` continuously snapshot the
ECS (Entity-Component-System) world into a compact binary stream. Each recording
consists of a global binary header followed by a sequence of LZ4-compressed frames,
accompanied by a sidecar `.meta.json` schema manifest. The tool:

1. Opens the `.fdp` binary file and its `.meta.json` sidecar.
2. Validates the schema manifest to detect struct-layout changes since recording.
3. Seeks to the requested time/frame window.
4. Streams every requested frame through `RecordingExportService`, emitting a JSON
   document for each frame: entity states (component payloads), event payloads, and
   frame timing metadata.
5. Writes the result to an output `.json` file.

### Input / Output

| Item | Description |
|------|-------------|
| Input | `.fdp` binary recording + companion `.meta.json` sidecar |
| Output | `.json` file containing frame-by-frame ECS state and events |
| Exit codes | 0 = success, 1 = argument error, 2 = file not found, 3 = runtime error |

### Use Cases

- **Post-mortem analysis** - Inspect entity state at every tick after a scenario run.
- **Regression testing** - Compare recordings across code changes to detect behaviour
  drift.
- **Changelog auditing** - Use `--changelog` to produce a per-entity mutation log
  showing only what changed between frames.
- **Targeted debugging** - Filter by entity index (`--entity-id`) or time window
  (`--start-time`/`--end-time`) to narrow output to just the entities and frames of
  interest.
- **CI pipeline integration** - The tool exits with deterministic codes, making it
  suitable as a build pipeline step that post-processes test recordings.

---

## Architecture

### Layered Design

The tool is composed of three distinct layers:

```
+-----------------------------------------------------+
|             fdp-recording-dumper (CLI)              |
|  Program.cs        DumperOptions.cs                 |
|  - arg parsing     - CommandLine attribute model    |
+---------------------+-------------------------------+
                      | calls
                      v
+-----------------------------------------------------+
|           Fdp.Toolkit.ReplayBrowser                 |
|  RecordingExportService  JsonExportOptions          |
|  IRecordingExportService ChangelogEntryDto          |
|  ComponentDiffService    DiffNode / DiffObject      |
|                          DiffValue                  |
+---------------------+-------------------------------+
                      | reads via
                      v
+-----------------------------------------------------+
|           Fdp.Core.FlightRecorder                   |
|  PlaybackController  RecorderSystem                 |
|  PlaybackSystem      AsyncRecorder                  |
|  RecordingGlobalHeader  FrameOuterHeader            |
|  SchemaValidator     RecordingMetadata              |
+-----------------------------------------------------+
```

### Data Flow

```
  .fdp file (binary)
  .meta.json sidecar
        |
        v
+----------------+      schema OK?
| PlaybackController |---> SchemaValidator ---> throws if mismatch
|  BuildFrameIndex   |
|  SeekToFrame(...)  |
+-------+--------+
        | StepForward() per frame
        v
+----------------------------+
| RecordingExportService     |
|  AbsoluteState path:       |
|   - EntityRepository query |
|   - FdpAutoSerializer      |      +--------------------------+
|   - Reflection fallback    +----->|  Utf8JsonWriter          |
|  Changelog path:           |      |  (FileStream, 64KB buf)  |
|   - BuildEntityStateNode   |      +--------------------------+
|   - ComponentDiffService   |              |
|   - ChangelogEntryDto      |              v
+----------------------------+        output .json
```

### Export Format Modes

Three export format modes control the shape of the output JSON:

```
+-------------------------+--------------------------------------------+
| ExportFormatMode        | Output shape                               |
+-------------------------+--------------------------------------------+
| AbsoluteState (default) | { "Header": {...}, "Frames": [ { frame }   |
|                         |   per frame: FrameHeader, Entities,        |
|                         |              Events, DestroyedEntities ] } |
+-------------------------+--------------------------------------------+
| Changelog               | [ { ChangelogEntryDto per mutation } ]     |
|                         |   keyed by entity handle string            |
|                         |   uses epsilon diff tree                   |
+-------------------------+--------------------------------------------+
| Incremental             | same root array as Changelog but uses      |
|                         | CompactDiffListConverter for compact JSON   |
+-------------------------+--------------------------------------------+
```

### Windowing

```
+-------------------+     +-------------------+     +-------------------+
| WindowMode        |     | Trigger condition |     | Seek mechanism    |
+-------------------+     +-------------------+     +-------------------+
| FullFile          | --> | none              | --> | none              |
| ByFrame           | --> | --start-frame N   | --> | SeekToFrame(N-1)  |
|                   |     | --end-frame M     |     | break at frame M  |
| ByTime            | --> | --start-time T    | --> | SeekToWallClock() |
|                   |     | --end-time U      |     | break at tick U   |
+-------------------+     +-------------------+     +-------------------+
```

Frame-based windowing and time-based windowing are mutually exclusive. The `Program`
validates this before dispatching to the export service.

---

## Source Structure

### Project: `Fdp.Tools.RecordingDumper`

**Namespace:** `Fdp.Tools.RecordingDumper`

| File | Class / Type | Access | Role |
|------|-------------|--------|------|
| `Program.cs` | `Program` | `internal static` | Entry point; argument parsing; `Execute` dispatch |
| `DumperOptions.cs` | `DumperOptions` | `internal sealed` | CommandLine attribute model; maps CLI flags to export options |

### Toolkit: `Fdp.Toolkit.ReplayBrowser` (in `Fdp.Toolkits`)

**Namespace root:** `Fdp.Toolkit.ReplayBrowser`

| File | Type | Role |
|------|------|------|
| `IRecordingExportService.cs` | `IRecordingExportService` | Interface contract for export |
| `RecordingExportService.cs` | `RecordingExportService` | Main export implementation; both AbsoluteState and Changelog paths |
| `JsonExportOptions.cs` | `JsonExportOptions`, `ExportWindowMode`, `ExportFormatMode` | Options bag for the export service |
| `ChangelogEntryDto.cs` | `ChangelogEntryDto` | Record DTO for one changelog entry (per-entity mutation + timing) |
| `EntitySelectionHistory.cs` | `EntitySelectionHistory` | Tracks entity selection history for interactive replay UI |
| `PlaybackHistoryTracker.cs` | `PlaybackHistoryTracker` | Tracks playback position history |
| `ReplayBrowserContext.cs` | `ReplayBrowserContext` | Full interactive replay browser context (UI use) |
| `BoundingBoxPickerGizmo.cs` | `BoundingBoxPickerGizmo` | Gizmo for spatial entity selection in the UI |
| `Diff/IComponentDiffService.cs` | `IComponentDiffService` | Interface for diff computation |
| `Diff/ComponentDiffService.cs` | `ComponentDiffService` | Recursive JSON-tree diff with epsilon tolerance for numerics |
| `Diff/DiffNode.cs` | `DiffNode`, `DiffObject`, `DiffValue` | Polymorphic diff tree nodes |
| `Search/SearchPredicateDto.cs` | `SearchPredicateDto` | DTO for search predicate serialization |
| `Search/PredicateCompiler.cs` | `PredicateCompiler` | Compiles search predicates to Func delegates |
| `Search/PropertyEvaluator.cs` | `PropertyEvaluator` | Evaluates property paths on entity state JSON |
| `Search/RecordingSearchService.cs` | `RecordingSearchService` | Searches recordings for frames matching predicates |
| `Search/EventScannerCompiler.cs` | `EventScannerCompiler` | Compiles event scanners for search |
| `Search/TargetEntityFilter.cs` | `TargetEntityFilter` | Entity filter for search queries |

### Core: `Fdp.Core.FlightRecorder`

| File | Type | Role |
|------|------|------|
| `RecordingGlobalHeader.cs` | `RecordingGlobalHeader` | Binary struct: magic + format version + timestamp (18 bytes, Pack=1) |
| `FrameOuterHeader.cs` | `FrameOuterHeader` | Binary struct: per-frame outer header (25 bytes, Pack=1) |
| `RecorderSystem.cs` | `RecorderSystem` | Core delta/keyframe capture; raw memory copy strategy |
| `AsyncRecorder.cs` | `AsyncRecorder` | Async double-buffer wrapper around `RecorderSystem`; zero-alloc hot path |
| `PlaybackController.cs` | `PlaybackController` | Random-access playback with `SeekToFrame` / `SeekToWallClockTicks` |
| `PlaybackSystem.cs` | `PlaybackSystem` | Applies deserialized frames to an `EntityRepository` |
| `SchemaValidator.cs` | `SchemaValidator` | Validates `.meta.json` schema manifest before playback |
| `FdpAutoSerializer.cs` | `FdpAutoSerializer` | Source-generated component serializer (reflection-free hot path) |
| `FdpPolymorphicSerializer.cs` | `FdpPolymorphicSerializer` | Polymorphic event serializer |
| `ComponentSchemaInfo.cs` | `ComponentSchemaInfo` | Schema entry: component ID, size, FNV hash, type name |
| `ComponentLayoutHasher.cs` | `ComponentLayoutHasher` | FNV layout hash computation |
| `Metadata/RecordingMetadata.cs` | `RecordingMetadata` | Sidecar data: timestamps, frame count, schema manifests |
| `Metadata/MetadataSerializer.cs` | `MetadataSerializer` | JSON serializer for `RecordingMetadata` |

---

## Public API Reference

### Command-Line Interface

```
fdp-recording-dumper [options]
```

#### Required options

| Flag | Short | Type | Description |
|------|-------|------|-------------|
| `--input` | `-i` | string | Path to the input `.fdp` recording file |
| `--output` | `-o` | string | Path to the output `.json` file |

#### Windowing (mutually exclusive groups)

| Flag | Short | Type | Description |
|------|-------|------|-------------|
| `--start-frame` | `-s` | int? | First frame index to export (ByFrame windowing) |
| `--end-frame` | `-e` | int? | Last frame index to export, inclusive (ByFrame windowing) |
| `--start-time` | `-t` | float? | Start time in seconds from recording start (ByTime windowing) |
| `--end-time` | `-u` | float? | End time in seconds from recording start (ByTime windowing) |

Mixing `--start-frame`/`--end-frame` with `--start-time`/`--end-time` is an error
(exit code 1).

#### Filtering

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--entity-id` | int? | (all) | Export only the entity with this ECS index |

#### Output control

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--no-events` | bool | false | Omit the Events array from each frame |
| `--no-entities` | bool | false | Omit the Entities array from each frame |
| `--minified` | bool | false | Write non-indented (minified) JSON |
| `--changelog` | bool | false | Use Changelog export mode (per-entity mutation log) |
| `--epsilon` | double | 0.001 | Numeric epsilon for changelog diff comparison |

#### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Argument parse error or mutual-exclusion violation |
| 2 | Input file not found |
| 3 | Runtime error during export |

### Programmatic API

The tool's core logic lives in `RecordingExportService` (in `Fdp.Toolkits`). It can be
used directly in test harnesses or other tools without going through the CLI:

```csharp
var service = new RecordingExportService();

var opts = new JsonExportOptions
{
    IncludeEvents   = true,
    IncludeEntities = true,
    Minified        = false,
    FormatMode      = ExportFormatMode.AbsoluteState,
    WindowMode      = ExportWindowMode.FullFile,
};

service.ExportToJson("sim_run_001.fdp", "output.json", opts);
```

The testable CLI entry point is also accessible via `internal` visibility:

```csharp
// Inject custom TextWriter instances to capture stdout/stderr in tests.
int exitCode = Program.RunMain(
    args:   new[] { "-i", "rec.fdp", "-o", "out.json" },
    stdout: new StringWriter(),
    stderr: new StringWriter());
```

`Fdp.Tools.RecordingDumper.Tests` is granted `InternalsVisibleTo` access via the
`.csproj` assembly attribute.

### `IRecordingExportService`

```csharp
public interface IRecordingExportService
{
    void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options);
}
```

### `JsonExportOptions`

```csharp
public sealed class JsonExportOptions
{
    public ExportWindowMode WindowMode       = ExportWindowMode.FullFile;
    public ExportFormatMode FormatMode       = ExportFormatMode.Incremental;

    // ByFrame window
    public int   StartFrame                  = 0;
    public int   EndFrame                    = int.MaxValue;

    // ByTime window
    public float StartTimeSec                = 0f;
    public float EndTimeSec                  = float.PositiveInfinity;

    // Entity filter
    public bool          FilterBySelection   = false;
    public List<Entity>  TargetEntities      = new();
    public bool          FilterByEntityIndex = false;
    public int           TargetEntityIndex   = -1;

    // Output toggles
    public bool   IncludeEntities            = true;
    public bool   IncludeEvents              = true;
    public bool   Minified                   = false;
    public double EpsilonTolerance           = 0.001;
}
```

### `ChangelogEntryDto`

```csharp
public sealed record ChangelogEntryDto(
    int FrameIndex,
    long WallClockTicks,
    double RelativeWallTimeSec,
    double SimTimeSec,
    string EntityHandle,
    IReadOnlyList<DiffNode> Mutations);
```

### `IComponentDiffService`

```csharp
public interface IComponentDiffService
{
    DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode,
                          double epsilonTolerance);

    IReadOnlyList<DiffNode> ComputeEntityDiff(
        Entity entity,
        EntityRepository sandboxRepo,
        ScenarioSerializer serializer,
        Action applyStepFunc);

    IReadOnlyList<DiffNode> ComputeTreeDiff(
        JsonNode? before, JsonNode? after, double epsilonTolerance);
}
```

---

## .fdp Binary File Format

Every `.fdp` recording file has a two-part structure:

### Global Header (18 bytes, Pack=1)

```
+--------+--------+--------+--------+--------+--------+
| M      | A      | G      | I      | C      | \0     |  bytes 0-5  "FDPREC"
+--------+--------+--------+--------+--------+--------+
| FormatVersion (uint32, 4 bytes)                      |  bytes 6-9
+------------------------------------------------------+
| Timestamp (int64, 8 bytes)                           |  bytes 10-17
+------------------------------------------------------+
```

Magic string: `"FDPREC"` (ASCII, 6 bytes, no null terminator required).

### Per-Frame Outer Header (25 bytes, Pack=1)

```
+----------+----------+----------+----------+
| CompressedSize (int32, 4 bytes)            |  bytes 0-3
+----------+----------+----------+----------+
| UncompressedSize (int32, 4 bytes)          |  bytes 4-7
+--------------------+---------------------+
| Tick (ulong, 8 bytes)                     |  bytes 8-15
+----------+---------+---------------------+
| FrameType (byte)   |                      |  byte  16
+----------+---------+                      |
| WallClockTicks (int64, 8 bytes)           |  bytes 17-24
+-------------------------------------------+
```

`FrameType`: `0` = Delta, `1` = Keyframe.

After the outer header, `CompressedSize` bytes of LZ4-compressed frame payload follow.
The payload contains:
- Destruction log (count + entity index/generation pairs)
- Event payloads (if `eventBus` was provided at record time)
- Per-component-type chunk data (raw struct memory delta-encoded against `prevTick`)

### Sidecar: `.meta.json`

Each `.fdp` file is accompanied by a `<name>.fdp.meta.json` file containing
`RecordingMetadata`:

```json
{
  "ProtocolVersion": 1,
  "Timestamp": "2026-05-01T12:00:00Z",
  "AppVersion": "1.0.0",
  "Description": "",
  "TotalFrames": 3600,
  "Duration": "00:01:00",
  "CustomTags": {},
  "SchemaManifest": {
    "5": { "Name": "Transform", "Size": 48, "LayoutHash": 1234567890 },
    "12": { "Name": "Velocity", "Size": 12, "LayoutHash": 987654321 }
  },
  "EventManifest": { ... },
  "MaxNetworkId": 42
}
```

`SchemaValidator` cross-references each entry in `SchemaManifest` against the
currently-registered component types. If a struct's byte size or FNV layout hash
differs, playback is aborted before any data is read.

---

## Output JSON Schema

### AbsoluteState mode (default)

```json
{
  "Header": {
    "Magic": "FDPREC",
    "FormatVersion": 4,
    "Timestamp": 638500000000000000
  },
  "Frames": [
    {
      "FrameHeader": {
        "FileFrameOrdinal": 0,
        "SimFrameNumber": 1000,
        "Tick": 1000,
        "FrameType": "Keyframe",
        "WallClockTicks": 638500000000000000,
        "RelativeWallTimeSec": 0.0,
        "SimTimeSec": 16.666,
        "CompressedSize": 312,
        "UncompressedSize": 980
      },
      "DestroyedEntities": [],
      "Entities": [
        {
          "EntityId": [3, 1],
          "Components": [
            {
              "ComponentType": "Transform",
              "HasAuthority": true,
              "Payload": { "Position": [1.0, 0.0, 2.5], "Rotation": [0,0,0,1] }
            }
          ]
        }
      ],
      "Events": [
        {
          "EventType": "FireEvent",
          "IsManaged": false,
          "Payload": { "ShooterId": "[3, 1]", "TargetId": "[7, 2]" }
        }
      ]
    }
  ]
}
```

### Changelog mode (`--changelog`)

Root array of `ChangelogEntryDto` objects. Each entry describes one entity's mutation
at one frame:

```json
[
  {
    "FrameIndex": 120,
    "WallClockTicks": 638500002000000000,
    "RelativeWallTimeSec": 2.0,
    "SimTimeSec": 2.0,
    "EntityHandle": "e[3,1]",
    "Mutations": [
      {
        "Name": "Transform",
        "Children": [
          { "Name": "Position", "OldValue": "[1.0,0.0,2.5]", "NewValue": "[1.1,0.0,2.6]" }
        ]
      }
    ]
  }
]
```

---

## Dependencies

### Direct project dependency

| Reference | Role |
|-----------|------|
| `Fdp.Toolkits` | Provides `RecordingExportService`, `JsonExportOptions`, `IRecordingExportService`, and the full `Fdp.Toolkit.ReplayBrowser` namespace |

`Fdp.Toolkits` in turn pulls in:

| Reference | Role |
|-----------|------|
| `Fdp.ModuleHost` | ECS kernel (`EntityRepository`, `FdpEventBus`, component registration) |
| `Fdp.Core` (via ModuleHost) | `FlightRecorder` subsystem, `PlaybackController`, `RecordingMetadata` |
| `Fdp.Diagnostics.Contracts` | Diagnostic protocol types (`DiagnosticGuidResolver`) |
| `Fdp.Diagnostics.Network` | DDS schema types for gizmo network protocol |
| `GizmoMap.Contracts` | ECS-free gizmo interaction interfaces |
| `FastBTree` | B-tree kernel used by ECS indexes |
| `FastHSM` | Hierarchical state machine kernel |
| `StructEdit.Core` / `StructEdit.Json` | Struct editing support |

### NuGet packages

| Package | Version | Used by | Purpose |
|---------|---------|---------|---------|
| `CommandLineParser` | 2.9.1 | `Fdp.Tools.RecordingDumper` | CLI argument parsing (`[Option]` attributes) |
| `CommandLineParser` | 2.9.1 | `Fdp.Toolkits` | (shared version) |
| `CycloneDDS.NET` | 0.2.2 | `Fdp.Toolkits` | DDS networking |
| `Microsoft.Extensions.Logging` | 8.0.0 | `Fdp.Toolkits` | Logging abstractions |
| `Newtonsoft.Json` | 13.0.3 | `Fdp.Toolkits` | JSON serialization in legacy paths |
| `NLog` | 5.2.8 | `Fdp.Toolkits` | Structured logging |

All packages are resolved transitively; `Fdp.Tools.RecordingDumper.csproj` declares
only `CommandLineParser` directly.

---

## Usage Examples

### Example 1: Full dump of a recording

Export the entire recording to an indented JSON file:

```bash
fdp-recording-dumper \
  --input  sim_run_001.fdp \
  --output sim_run_001.json
```

Output: a `Frames` array covering all recorded frames, with full entity states and
events, indented for readability.

### Example 2: Time-windowed export, no events

Extract only frames between 5 and 10 seconds into the recording, omitting the Events
block to reduce output size:

```bash
fdp-recording-dumper \
  --input       sim_run_001.fdp \
  --output      window_5s_10s.json \
  --start-time  5.0 \
  --end-time    10.0 \
  --no-events
```

### Example 3: Single-entity changelog

Produce a compact changelog for entity index 7 only, covering frames 100 to 200:

```bash
fdp-recording-dumper \
  --input       sim_run_001.fdp \
  --output      entity7_changelog.json \
  --start-frame 100 \
  --end-frame   200 \
  --entity-id   7 \
  --changelog   \
  --epsilon     0.0001
```

Output: root JSON array listing only the frames where entity 7's component state
changed beyond the epsilon threshold.

### Example 4: Minified full dump for CI diffing

Generate a minified file that can be compared with a baseline in a CI pipeline:

```bash
fdp-recording-dumper \
  --input    regression_baseline.fdp \
  --output   regression_actual.json \
  --minified \
  --no-events
```

Then diff in CI:

```bash
diff regression_expected.json regression_actual.json
```

### Example 5: Programmatic use in a test harness

```csharp
using Fdp.Toolkit.ReplayBrowser;

// Arrange
var service = new RecordingExportService();
var opts = new JsonExportOptions
{
    WindowMode        = ExportWindowMode.ByFrame,
    StartFrame        = 0,
    EndFrame          = 59,          // first 60 frames (0-based, inclusive)
    FormatMode        = ExportFormatMode.AbsoluteState,
    IncludeEvents     = false,
    FilterByEntityIndex = true,
    TargetEntityIndex   = 3,
    Minified          = true,
};

// Act
service.ExportToJson("test_recording.fdp", "actual.json", opts);

// Assert
var json = File.ReadAllText("actual.json");
var doc  = System.Text.Json.JsonDocument.Parse(json);
Assert.True(doc.RootElement.TryGetProperty("Frames", out _));
```

### Example 6: Testable CLI entry point

```csharp
using System.IO;
using Fdp.Tools.RecordingDumper;   // internal via InternalsVisibleTo

var stdout = new StringWriter();
var stderr = new StringWriter();

int code = Program.RunMain(
    new[] { "-i", "rec.fdp", "-o", "out.json", "--minified" },
    stdout,
    stderr);

Assert.Equal(0, code);
Assert.Contains("Exported:", stdout.ToString());
```

---

## Internal Design Details

### Memory Strategy

`RecordingExportService` uses `Utf8JsonWriter` bound directly to a `FileStream`
(64 KB buffer). It never materializes a full in-memory DOM of the entire recording.
Each frame is written and flushed before the next frame is read, bounding heap
allocation to O(single-frame) regardless of recording length.

### Component Serialization Fallback Chain

For each component bit set in an entity's `ComponentMask`, serialization follows this
priority order:

```
1. ScenarioSerializer.Translators  (domain-specific custom translators)
         |
         v
2. FdpAutoSerializer.TryExtract()  (source-generated, reflection-free)
         |
         v
3. TrySerializeComponentByReflection()    (for unmanaged/struct types)
   TrySerializeManagedComponentByReflection()  (for class types)
```

Reflection fallback uses cached `MethodInfo` objects (`_tryGetComponentGenericDef`,
`_hasManagedComponentGeneric`, `_getManagedComponentGeneric`) to minimize overhead
on the hot path.

### Schema Validation Gate

`PlaybackController` always validates the `.meta.json` schema manifest **before**
opening the binary stream. This prevents silent memory corruption caused by playing
back a recording made with a different struct layout. Recordings without a sidecar
file are treated as legacy and emit a console warning but do not abort.

### Sandbox Isolation

The export service creates its own `EntityRepository` and `FdpEventBus` (`sandboxRepo`
/ `sandboxBus`). These sandbox objects are never shared with any running simulation.
All component types and event types are auto-registered into the sandbox before
playback begins, ensuring the playback system can apply all frames without throwing
on unknown type IDs.

### Diff Tree and Epsilon Tolerance

In Changelog mode, `ComponentDiffService.ComputeTreeDiff` produces a polymorphic
`DiffNode` tree:

- `DiffObject`: represents a JSON object node; is considered modified when any
  descendant is modified.
- `DiffValue`: represents a leaf value; compares `OldValue` / `NewValue` strings.
  For numeric leaves, the change is only reported when
  `|newVal - oldVal| >= epsilonTolerance`. This suppresses floating-point noise from
  physics integration in the output.

The `PruneUnchangedNodes` pass removes unmodified nodes from the tree before
serialization, keeping changelog entries compact.

---

## Best Practices

### Input files

- Always keep the `.fdp` and its `.meta.json` sidecar in the same directory. The
  playback controller derives the metadata path as `<fdp_path>.meta.json`.
- If a sidecar is absent (legacy recording), expect a console warning and possible
  deserialization failures if struct layouts have changed.

### Output files

- Prefer `--minified` when the output is consumed by code (CI diffs, unit tests). It
  is faster to write and smaller.
- Use indented output (default) when the file is inspected manually.

### Windowing

- Always prefer `--start-frame`/`--end-frame` over time-based windowing when you know
  exact frame indices; frame seeking uses the pre-built frame index and is O(1).
- Time-based windowing is appropriate when frame rate varies or when working from
  wall-clock timestamps in an event log.

### Changelog mode

- Set `--epsilon` appropriately for your component data. Physics positions typically
  require `0.001` (default); orientation quaternions or health integers may benefit
  from a much tighter `0.0001` or `0.0` respectively.
- Use `--entity-id` with `--changelog` to produce focused, small output files when
  only a single entity's history is needed.

### Integration testing

- Use `Program.RunMain` with injected `TextWriter` instances instead of running the
  process out-of-process. This avoids spawning a new process and gives direct access
  to exit codes and output text.
- Use `RecordingExportService` directly in unit tests to skip argument parsing
  altogether.

### Error handling

- Check the exit code before consuming the output file. Exit code 3 means a runtime
  exception was caught; the output file may be incomplete or absent.
- Pass `--no-entities` / `--no-events` to reduce output size when only one of the two
  data types is needed; this also reduces processing time.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Fdp.Toolkits` | Direct dependency; provides `RecordingExportService` and all `ReplayBrowser` types |
| `Fdp.Core` | Transitive dependency; provides `FlightRecorder` binary format, `PlaybackController`, `RecorderSystem`, `AsyncRecorder`, `SchemaValidator` |
| `Fdp.ModuleHost` | Transitive dependency; provides ECS kernel (`EntityRepository`, `FdpEventBus`, `ComponentTypeRegistry`) |
| `Fdp.Diagnostics.Contracts` | Transitive dependency; provides `DiagnosticGuidResolver` used to produce stable entity handle strings |
| `Fdp.Toolkits.Tests` | Test assembly; has `InternalsVisibleTo` access to `RecordingExportService` test helpers (`FdpRecordingHarness`) |
| `Fdp.Tools.RecordingDumper.Tests` | Test assembly for the CLI itself; has `InternalsVisibleTo` access to `Program.RunMain` and `DumperOptions` |
| `Fdp.Examples.Runner` | Uses `AsyncRecorder` at runtime to produce the `.fdp` files this tool consumes |
| `Hrot` subsystem | Also uses `AsyncRecorder`; recordings produced there are compatible with this dumper provided component types are registered |

---

## Appendix: Full CLI Help Output

```
fdp-recording-dumper 1.0.0
Copyright (C) 2025-2026 FDP Project

  -i, --input         Required. Path to the input .fdp recording file.
  -o, --output        Required. Path to the output .json file.
  -s, --start-frame   First frame to export (ByFrame windowing).
  -e, --end-frame     Last frame to export inclusive (ByFrame windowing).
  -t, --start-time    Start time in seconds (ByTime windowing).
  -u, --end-time      End time in seconds (ByTime windowing).
      --entity-id     Export only entities with this ECS index.
      --no-events     (Default: false) Omit the Events block from output.
      --no-entities   (Default: false) Omit the Entities block from output.
      --minified      (Default: false) Write minified (non-indented) JSON.
      --changelog     (Default: false) Use Changelog export mode.
      --epsilon       (Default: 0.001) Epsilon tolerance for changelog diff.
      --help          Display this help screen.
      --version       Display version information.
```

---

## Appendix: Schema Mismatch Error Reference

If the struct layout of any registered component has changed since the recording was
produced, `PlaybackController` throws:

```
System.InvalidOperationException:
  Schema mismatch: component 'Transform' (ID 5) layout has changed.
  Recorded size = 48 bytes, current size = 56 bytes.
  The recording cannot be played back safely.
```

Resolution: either replay with the binary version that matches the recording, or
re-record the scenario with the current binary.
