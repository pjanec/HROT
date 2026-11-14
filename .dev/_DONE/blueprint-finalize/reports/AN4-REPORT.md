# AN4 Report — Per-action palette generation

Branch: `blueprint-integ-1`  
Date: 2026-06-06  

---

## STEP 1 — Palette → Node creation mechanism

### NodeKindDescriptor / NodeKindRegistry

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/NodeKindDescriptor.cs`  
File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/NodeKindRegistry.cs`

`NodeKindDescriptor` carries:
- `Kind` — string key, **must be unique** in the registry (`NodeKindRegistry` stores `_map[descriptor.Kind] = descriptor`; last write wins, so duplicate keys silently drop the earlier entry)
- `CreateInstance` — `Func<Node>` factory called at placement time
- `DisplayName`, `Category`, `Tooltip`, `Icon`

`NodeKindRegistry.TryGet(string kind)` returns `NodeKindDescriptor?`.

### BlueprintEditorBootstrap.CreatePaletteRegistry

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`

Populates the registry in this order:
1. `WhenNodePaletteEntries` — three hand-authored When/EQS entries
2. `BlueprintNodePaletteEntries.All()` — 24 built-in node kinds (ChannelCommandNode excluded per AN4)
3. `BlueprintNodePaletteEntries.ChannelCommandEntries(channelCatalog)` — N per-action entries (AN4)
4. `BlueprintMathPaletteEntries.All()` — math function-call presets

### BlueprintCommandSink.CreateAssetNode flow

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs`

On `Apply(AddNode(id, kind, pos, props))`:
1. `_catalog.TryGet(kind.Id)` — resolves the descriptor
2. `descriptor.CreateInstance()` — creates the node (AN4: bakes ChannelType+ActionId)
3. `ApplyInitialProperties(node, props)` — still handles ChannelCommandNode fields from props dict; with baked factory this is redundant but harmless
4. `ApplyPinIds(node, props)` — stamps canonical pin ids from `["PinIds"]` prop key

### NodePinSchema.ChannelCommandPins

Matches by `LastSegment(e.ChannelTypeFqn) == cc.ChannelType && e.Name == cc.ActionId` using the channel catalog. Returns exec-In + exec-Out + per-field data-IN pins from the action's `ParamsTypeFqn` struct.  
With baked `ChannelType`/`ActionId` on the node, `GetCanonicalPins` correctly projects param pins without any additional props.

---

## STEP 2 — Implementation

### Design decision (D-B)

One palette entry per channel-command action. Each entry's `CreateInstance` lambda closes over the `bakedChannelType` (short class name, e.g. `"LocomotionChannel"`) and `bakedActionId` (action name, e.g. `"MoveTo"`), writing both directly onto the new `ChannelCommandNode` at construction time. The generic single `"ChannelCommand"` entry was removed (no chameleon hazard; no mutable "pick action later" dropdown).

### Kind ID format

`"ChannelCommand:{ChannelShortName}:{ActionId}"`  
Example: `"ChannelCommand:LocomotionChannel:MoveTo"`

This gives each per-action entry a unique registry slot. `LastSegment(ChannelTypeFqn)` is used to extract the short class name (`"LocomotionChannel"` from the FQN).

### Files changed

| File | Change |
|------|--------|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` | Added `ChannelCommandEntries(IChannelCommandCatalog?)` + `LastSegment` + `StripChannelSuffix` helpers; removed generic `ChannelCommandNode` entry from `All()` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` | `CreatePaletteRegistry` accepts optional `IChannelCommandCatalog?`; calls `ChannelCommandEntries(channelCatalog)` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BcpBatch04WireDropTests.cs` | Added `using Hrot.Blueprints.Core.Compiler.Catalogs`; `MakeSink` passes `BuiltInChannelCommandCatalog.Instance` to `CreatePaletteRegistry` and `BlueprintGraphModel`; kind references updated to `"ChannelCommand:LocomotionChannel:MoveTo"` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BlueprintCommandSinkTests.cs` | `MakeSutWithChannelCatalog` passes catalog to `CreatePaletteRegistry`; test uses per-action kind; props no longer include `ChannelType`/`ActionId` (baked by CreateInstance); added bake-verification assertions |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BlueprintNodeCatalogTests.cs` | Added `using Hrot.Blueprints.Core.Compiler.Catalogs`; `QueryForPinContext_ExecOutputSource_ReturnsFullFlowSet_WithCompatibleExecInput` updated to pass `BuiltInChannelCommandCatalog.Instance` and check per-action kind `"ChannelCommand:LocomotionChannel:MoveTo"` |

### Catalog threading

`IChannelCommandCatalog` (not `IBehaviorActionCatalog`) is threaded into `CreatePaletteRegistry` as an optional parameter. Only channel commands are valid in Blueprint graphs; `IBehaviorActionCatalog` may not be available at palette-build time and is not needed here.

---

## Tests

### New test file

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN4_PerActionPaletteTests.cs` — 20 tests covering:
1. `ChannelCommandEntries(catalog)` with N actions → N descriptors (null/empty catalog → empty)
2. Kind format `"ChannelCommand:{Channel}:{Action}"`, all unique, all start with prefix
3. `CreateInstance` bakes `ChannelType` and `ActionId`; each call produces a new `Id`
4. `CreatePaletteRegistry` registers all N entries; no generic `"ChannelCommand"` kind
5. `sink.Apply(AddNode(...))` via per-action kind bakes `ChannelType` + `ActionId` on the node
6. `NodePinSchema` projects param pins for baked action (> 2 pins total)
7. Display name `"Locomotion / MoveTo"` and category `"Channel/Locomotion"` format (StripChannelSuffix)
8. BuiltIn catalog: 5 entries registered as 5 distinct kinds; no generic kind

### Test results

```
AN4_PerActionPaletteTests:                20/20 passed
BcpBatch04WireDropTests:                   3/3  passed
BlueprintCommandSinkTests:                21/21 passed
BlueprintNodeCatalogTests:               17/17  passed
Host namespace (total):                 364/364  passed
Blueprints full suite:               1541/1545  passed (4 pre-existing failures)
Hrot.Editor.AiShared.Tests:            831/832  passed (1 pre-existing flaky failure)
```

Pre-existing failures (unchanged):
- `Library_EmitMatchesGoldenSource` — CRLF flake
- `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — ScoreCrossed
- `TickFrame_1000Frames_AllocatesZeroBytes` — allocation counter
- `LibraryMath_GeneratedSource_Snapshot` — CRLF flake
- `Write_to_invalid_path_does_not_leave_temp_files_behind` — flaky filesystem race (passes in isolation)

Zero new failures introduced by AN4.

---

## Deviations

None. Implementation matches D-B decision exactly: one action = one node, action baked at creation, no chameleon hazard.
