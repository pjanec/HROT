# AN3 Report — Unified behavior-action catalog

Branch: `blueprint-integ-1`  
Date: 2026-06-06  

---

## STEP 1 — Source catalog API shapes

### IChannelCommandCatalog
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/CatalogInterfaces.cs`

```csharp
public sealed record ChannelCommandCatalogEntry(
    string Name,           // short action name e.g. "MoveTo"
    string ChannelTypeFqn, // ECS channel FQN e.g. "Fdp.Toolkit.Behavior.Components.LocomotionChannel"
    ushort ActionId,       // numeric discriminator within the channel (e.g. 1)
    string ParamsTypeFqn); // FQN of executor-param struct e.g. "Fdp.Toolkit.Navigation.MoveToParams"

public interface IChannelCommandCatalog
{
    IReadOnlyList<ChannelCommandCatalogEntry> GetEntries();
}
```

`BuiltInChannelCommandCatalog.GetEntries()` returns 5 entries: MoveTo, FollowRoute, AimAndFire, OpenDoor, EjectPassengers.  
No Changed/watcher hook — it is a static list; re-read on every `BehaviorActionCatalog.Rebuild()`.

---

### IActionSchemaExporter
File: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`  
Implementation: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporter.cs`

```csharp
public record ActionSchemaEntry(
    string          Fqn,         // "{DeclaringType.FullName}.{MethodName}"
    Type            DtoType,     // CLR type of first ref param (the action DTO)
    ActionHosting   Hosting,     // flags: BTree | Hsm | Shared | Heavy
    BlackboardAccess Access,     // Unknown | ReadOnly | ReadWrite
    Type?           HeavyDtoType // non-null for SharedAiHeavyAction only
);

// ActionHosting flags:
// BTree=1, Hsm=2, Shared=4, Heavy=8

public interface IActionSchemaExporter
{
    IReadOnlyDictionary<string, ActionSchemaEntry> All { get; }
    ActionSchemaEntry? Lookup(string fqn);
    void Rebuild();
    event Action? Changed;  // ← rebuild/watcher hook EXISTS
}
```

Scans all loaded assemblies for `[BTreeAction]`, `[BTreeCondition]`, `[HsmAction]`, `[HsmGuard]`, `[SharedAiAction]`, `[SharedAiCondition]`, `[SharedAiHeavyAction]`.  
`ActionSchemaExporterCatalogWatcher` wires `IAssetCatalog.Changed → Rebuild()`.  
The `Changed` event fires after every successful `Rebuild()` — this is the hook used by `BehaviorActionCatalog`.

---

## STEP 2 — Implementation

### Entry record
`BehaviorActionEntry` record with fields:

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `string` | Canonical stable identity: `"{ChannelTypeFqn}::{ActionId}"` for channel commands; FQN for schema entries |
| `DisplayName` | `string` | Short name (action name or method name) |
| `Category` | `string?` | Channel type short name for CC; declaring type short name for schema entries |
| `ChannelTypeFqn` | `string?` | Non-null only for `ChannelCommand` entries |
| `ActionId` | `ushort` | Non-zero only for `ChannelCommand` entries |
| `ParamsTypeFqn` | `string` | FQN of param DTO type |
| `ValidHosts` | `BehaviorActionHosts` (flags) | Blueprint / BTree / Hsm |
| `Source` | `BehaviorActionSource` | ChannelCommand / Hardcoded / AiPrimitive |

Note on `Source=AiPrimitive`: The `ActionSchemaExporter` does not distinguish between hardcoded and blueprint-authored AiPrimitive entries at the interface level — both appear as schema entries after compilation+hot-reload. All schema entries are tagged `Hardcoded` in this implementation (correct for the current phase). A future extension could inspect declaring-type assembly origin to set `AiPrimitive` when the entry comes from a generated blueprint assembly.

### Location justification
Files placed in `Hrot.Blueprints.Editor` (new folder `ActionCatalog/`), **not** in `Hrot.Editor.AiShared`.

Reason: `IBehaviorActionCatalog` must import both `IChannelCommandCatalog` (from `Hrot.Blueprints.Compiler`, namespace `Hrot.Blueprints.Core.Compiler.Catalogs`) and `IActionSchemaExporter` (from `Hrot.Editor.AiShared`). The dependency graph is:

```
Hrot.Blueprints.Editor  ──► Hrot.Editor.AiShared     (already exists)
Hrot.Blueprints.Editor  ──► Hrot.Blueprints.Core     (already exists)
                              └──► Hrot.Blueprints.Compiler  (already exists)
```

Adding `Hrot.Blueprints.Compiler` as a dep of `Hrot.Editor.AiShared` would create a circular chain (`Hrot.Blueprints.Editor → Hrot.Editor.AiShared → Hrot.Blueprints.Compiler`... while `Hrot.Blueprints.Editor` → `Hrot.Editor.AiShared` is already there). Placing the catalog in `Hrot.Blueprints.Editor` avoids any new project references — both source catalogs are already reachable there.

No `.csproj` changes were required.

### New files

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ActionCatalog/IBehaviorActionCatalog.cs` | Enums (`BehaviorActionHosts`, `BehaviorActionSource`) + `BehaviorActionEntry` record + `IBehaviorActionCatalog` interface |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ActionCatalog/BehaviorActionCatalog.cs` | Composing implementation: subscribes to `IActionSchemaExporter.Changed`; rebuilds snapshot from both sources; `GetActions()` / `GetActions(host)` / `Changed` event; `IDisposable` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BehaviorActionCatalogTests.cs` | 30 headless unit tests using `FakeChannelCommandCatalog` + `FakeActionSchemaExporter` |

### Rebuild/watcher policy
`BehaviorActionCatalog` subscribes to `IActionSchemaExporter.Changed` and calls `Rebuild()` on every notification. Initial rebuild happens in the constructor. Channel-command catalog is re-read on each rebuild (it is a static list). `IDisposable.Dispose()` unsubscribes to prevent event-handler leaks.

---

## STEP 3 — Verification

### Build
```
dotnet build Hrot.Blueprints.Editor  →  0 CS errors, 0 warnings
dotnet build Hrot.Blueprints.Tests   →  0 CS errors, 8 pre-existing warnings (CS0618 obsolete + CS8601 nullable)
```

### AN3 tests
```
30 / 30 passed (BehaviorActionCatalogTests)
```

### Full Hrot.Blueprints.Tests suite
```
Passed: 1521, Failed: 4, Skipped: 8, Total: 1533
```

Failing set (all pre-existing, no new failures):
- `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — pre-existing flake (ScoreCrossed)
- `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` — pre-existing flake (AllocatesZeroBytes)
- `LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` — pre-existing CRLF flake (Library golden)
- `LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` — pre-existing CRLF flake (LibraryMath)

### Hrot.Editor.AiShared.Tests suite
```
Passed: 831, Failed: 1
```
The 1 failure (`AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind`) is pre-existing and unrelated to AN3.

---

## Deviations

None. All requirements from TASK-DETAIL.md §AN3 and ACTION-NODE-DESIGN.md §ROUND-2 satisfied:
- Entry record matches the spec shape.
- Canonical Id = FQN for schema entries, `(ChannelTypeFqn)::(ActionId)` for channel commands (architect AQ2).
- Channel-command entries: `Source=ChannelCommand`, `ValidHosts=Blueprint`, carry `ChannelTypeFqn`+`ActionId`+`ParamsTypeFqn`.
- Schema entries: `Source=Hardcoded`, `ValidHosts` from `ActionHosting` flags (BTree/Hsm).
- `GetActions()` and `GetActions(host)` both exposed.
- Rebuild on `IActionSchemaExporter.Changed`.
- No palette/node changes (AN4 scope).
