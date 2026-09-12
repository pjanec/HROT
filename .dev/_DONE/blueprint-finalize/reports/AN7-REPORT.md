# AN7 Report — Generalize node + palette to non-channel actions

**Branch:** `blueprint-integ-1`
**Batch:** AN7 (editor only; AN8 = compile lowering, deferred)
**Status:** DONE (lead commits pending per convention)

---

## 1. Investigation: Which catalog actions are Blueprint-valid?

### ActionHosting / MapHosting analysis

`ActionSchemaExporter` assigns `ActionHosting` flags per attribute:

| Attribute(s) | Resulting `ActionHosting` |
|---|---|
| `[BTreeAction]` / `[BTreeCondition]` | `BTree` |
| `[HsmAction]` / `[HsmGuard]` | `Hsm` |
| `[SharedAiAction]` / `[SharedAiCondition]` | `BTree \| Hsm \| Shared` |
| `[SharedAiHeavyAction]` | `BTree \| Hsm \| Shared \| Heavy` |

Before AN7, `BehaviorActionCatalog.MapHosting()` mapped only `BTree→BTree` and `Hsm→Hsm`. The `Shared` flag was consumed implicitly (BTree + Hsm set simultaneously) but did **not** add `Blueprint` hosting. This meant:

- `[SharedAiAction]` entries: **not** Blueprint-valid pre-AN7.
- `[BTreeAction]`-only or `[HsmAction]`-only entries: not Blueprint-valid.
- Channel commands: Blueprint-valid (unchanged; `BehaviorActionSource.ChannelCommand` path).

**AN7 decision (per ROUND-3):** `[SharedAiAction]` methods are explicitly the non-channel actions that Blueprints can invoke. Their `ActionHosting.Shared` flag is the discriminator. AiPrimitive(BlueprintCall) entries also appear via `IActionSchemaExporter` after build+reload and would carry `Shared` once the generator stamps that flag.

### Conclusion

**Blueprint-valid non-channel actions** = entries with `ActionHosting.Shared` set (which always includes `BTree | Hsm | Shared`). BTree-only or Hsm-only entries are NOT Blueprint-valid. Channel commands remain Blueprint-valid via the unchanged ChannelCommand source path.

---

## 2. Node model change: `ActionFqn` on `ChannelCommandNode`

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs`

Added to `ChannelCommandNode`:

```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? ActionFqn { get; set; }
```

- `null` → channel-command node (existing `ChannelType` / `ActionId` path, unchanged).
- Non-null → non-channel behavior action identified by FQN (`{Namespace}.{Type}.{Method}`).
- `JsonIgnore(WhenWritingNull)` → omitted from JSON for all existing channel-command assets → **byte-stable**.
- JSON kind discriminator remains `"ChannelCommand"` (no new `JsonDerivedType`).
- Compile lowering of nodes with `ActionFqn` set is **deferred to AN8** (AN7 is editor-only).

---

## 3. Catalog change: `MapHosting` includes `Blueprint` for `Shared` entries

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ActionCatalog/BehaviorActionCatalog.cs`

```csharp
// AN7: Shared actions are also valid in Blueprint graphs.
if ((hosting & ActionHosting.Shared) != 0) result |= BehaviorActionHosts.Blueprint;
```

This is the only change to `BehaviorActionCatalog`. No change to `BehaviorActionEntry` record or `IActionSchemaExporter`.

---

## 4. Palette: `NonChannelActionEntries` + Bootstrap wiring

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/BlueprintNodePaletteEntries.cs`

- Added `Categories.Action = "Action"` constant.
- Added `NonChannelActionEntries(IBehaviorActionCatalog? catalog)` method:
  - Iterates `catalog.GetActions(BehaviorActionHosts.Blueprint)`.
  - Skips `BehaviorActionSource.ChannelCommand` entries (those are handled by `ChannelCommandEntries`).
  - For each remaining entry: kind = `"Action:{FQN}"`, category = `"Action/{Category}"`, display name = `"{Category} / {MethodName}"`.
  - `CreateInstance` bakes `ActionFqn = entry.Id`, `ChannelType = ""`, `ActionId = ""` (D-B: action immutable after creation).
  - Tooltip notes that compile lowering is pending AN8.

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`

- `CreatePaletteRegistry` gains `IBehaviorActionCatalog? behaviorActionCatalog = null` parameter.
- Calls `BlueprintNodePaletteEntries.NonChannelActionEntries(behaviorActionCatalog)` after channel entries.
- All existing call sites (pass 0–1 positional args) are backward-compatible.

---

## 5. `NodePinSchema` changes

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`

- `GetCanonicalPins` gains `IBehaviorActionCatalog? behaviorActions = null` (last param, default null — all existing call sites unaffected).
- `ChannelCommandPins(cc, channelCommands)` → `ChannelCommandPins(cc, channelCommands, behaviorActions)`.
- `ChannelCommandPins` now dispatches:
  - `!string.IsNullOrEmpty(cc.ActionFqn)` → `NonChannelActionPins(cc.ActionFqn, behaviorActions)`.
  - Otherwise → `ChannelCommandPinsFromCatalog(cc, channelCommands)` (old logic, unchanged).
- `NonChannelActionPins`: looks up the FQN in `behaviorActions.GetActions(BehaviorActionHosts.Blueprint)` (source ≠ ChannelCommand), resolves `ParamsTypeFqn` via `AppendParamPins`.
- `AppendParamPins`: extracted shared helper (used by both channel and non-channel paths) — resolves the type, reflects `ReflectDataMembers`, appends data-IN pins. Enum fields stamped `"global::{FQN}"` per AN6.
- Non-channel nodes with a null catalog or unknown FQN fall back to exec-only (no throw).

---

## 6. Drawer changes

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ChannelCommandNodeDrawer.cs`

`ChannelCommandNodeSession.Draw()` now dispatches on `ActionFqn`:

- Non-null `ActionFqn` → `DrawNonChannelAction()`: shows "Behavior Action" header, then read-only labels for `ActionName` (last segment of FQN), `Type` (type portion), and `FQN` (full string). Also shows a disabled note "(compile lowering via AN8 — not yet emittable)". No mutation path (AN5 pattern).
- Null `ActionFqn` → `DrawChannelCommand()`: original read-only channel/action labels (unchanged).

---

## 7. Tests added / updated

### Updated existing tests

**`BehaviorActionCatalogTests.cs`:**

- `GetActionsByHost_Blueprint_ReturnsOnlyChannelCommands` renamed to `GetActionsByHost_Blueprint_ReturnsChannelCommandsOnly_WhenNoSharedEntries` (semantics unchanged: BTree-only schema entries are excluded).
- **Added** `GetActions_SharedEntry_ValidHostsIncludesBlueprint_AN7` — confirms `ActionHosting.Shared` → `BehaviorActionHosts.Blueprint`.
- **Added** `GetActions_BTreeOnlyEntry_ValidHostsDoesNotIncludeBlueprint_AN7` — confirms BTree-only entries are not Blueprint-valid.
- **Added** `GetActionsByHost_Blueprint_ReturnsChannelCommandsAndSharedEntries_AN7` — Blueprint filter returns channel commands + shared entries, excludes BTree-only.

### New AN7 tests in `NodePinSchemaEnrichmentTests.cs`

- `NonChannelAction_KnownFqn_MultiFieldParams_ProjectsOnePinPerField_PlusExec` — verifies exec In/Out + data-IN per DTO field, enum field stamped `"global::"`.
- `NonChannelAction_NullCatalog_ExecOnly` — graceful fallback when catalog is null.
- `NonChannelAction_UnknownFqn_ExecOnly` — graceful fallback when FQN not in catalog.
- `NonChannelAction_NullActionFqn_FallsThroughToChannelCommandPath` — confirms null `ActionFqn` uses the channel-command path (no regression).
- `ChannelCommandNode_ActionFqn_JsonRoundTrip_OmittedWhenNull_PresentWhenSet` — confirms byte-stability: `ActionFqn` absent in JSON when null; present and round-trips via `BlueprintJsonServices.Deserialize` when set.

### New AN7 tests in `AN4_PerActionPaletteTests.cs`

- `NonChannelActionEntries_NActions_YieldsNDescriptors_AN7`
- `NonChannelActionEntries_KindFormat_IsActionColonFqn_AN7`
- `NonChannelActionEntries_CreateInstance_BakesActionFqn_AN7`
- `NonChannelActionEntries_SkipsChannelCommandEntries_AN7`
- `CreatePaletteRegistry_WithBehaviorActionCatalog_RegistersNonChannelKinds_AN7`
- `NonChannelActionEntries_NullCatalog_YieldsEmpty_AN7`

---

## 8. Build and test results

**Whole-solution build:** 0 CS errors, 0 new warnings (pre-existing warnings unchanged).

**Targeted suite (BehaviorActionCatalogTests + NodePinSchemaEnrichmentTests + AN4_PerActionPaletteTests):** 105 / 105 pass.

**Full blueprint test suite (Hrot.Blueprints.Tests):** 1577 pass, 4 fail, 8 skip.

**Failing tests (all pre-existing):**
- `Hrot.Blueprints.Tests.Editor.ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` (ScoreCrossed pre-existing)
- `Hrot.Blueprints.Tests.Compiler.LibraryEmitGoldenTests.Library_EmitMatchesGoldenSource` (CRLF flake)
- `Hrot.Blueprints.Tests.Runtime.AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` (pre-existing)
- `Hrot.Blueprints.Tests.Demos.LibraryMathDemoTests.LibraryMath_GeneratedSource_Snapshot` (CRLF flake)

**0 new failures.**

---

## 9. Compile lowering note (AN8)

Non-channel action nodes (`ActionFqn` set) placed in a canvas-authored blueprint will **not compile** until AN8 implements the lowering path in `Stage5_Schedule`. The lowering must:

- Invoke `(self, ctx, paramsDTO) -> NodeStatus` via `BehaviorRegistry` routing.
- Suspend on `NodeStatus.Running` using an inline latent `BlueprintLatentCursor` switch (mirror of the WaitForChannel path).
- Route exec-Out on Success / Failure.

Do NOT include non-channel action nodes in any committed `.bp.json` asset that passes through the generator in tests — they will produce a compiler error until AN8. Headless tests that exercise only the editor-side projection (palette, NodePinSchema, JSON round-trip) are safe.

---

## 10. Composition-root wiring (live editor) — AN7 completion

The earlier passes added optional `IBehaviorActionCatalog?` params (defaulting null) to the palette and `NodePinSchema` paths, but the **live** call sites still passed null, so the running editor never showed non-channel actions or projected their pins. This pass completes the wiring, mirroring exactly how `channelCatalog` / `_channelCommands` is already threaded.

### 10.1 `EditorSubsystem.cs` (composition root)

**Field** (after `_blueprintPaletteEntries`, ~line 255):
```csharp
// AN7: unified behavior-action catalog ... constructed once, reused by palette + factory.
private Hrot.Blueprints.Editor.ActionCatalog.BehaviorActionCatalog? _behaviorActionCatalog;
```

**Construct catalog + rebuild palette** (immediately after `var sharedSchemaExporter = new ActionSchemaExporter();`, ~line 1825):
```csharp
var bpChannelCatalog = Hrot.Blueprints.Core.Compiler.Catalogs.BuiltInChannelCommandCatalog.Instance;
_behaviorActionCatalog = new Hrot.Blueprints.Editor.ActionCatalog.BehaviorActionCatalog(
    bpChannelCatalog, sharedSchemaExporter);
// Rebuild the palette now that the behavior-action catalog exists so non-channel actions
// appear alongside the channel-command entries built earlier (line ~917).
_blueprintPaletteEntries = Hrot.Blueprints.Editor.BlueprintEditorBootstrap.CreatePaletteRegistry(
    bpChannelCatalog, behaviorActionCatalog: _behaviorActionCatalog);
```
Notes:
- The catalog is the same singleton source (`BuiltInChannelCommandCatalog.Instance`) the palette at line ~917 already uses; `bpChannelCatalog` is a fresh local because the `channelCatalog` local from line ~901 is out of the schema-exporter block's scope.
- The palette built at line ~917 (channel-only) is **reassigned** here once the catalog exists. `_blueprintPaletteEntries` is only consumed later (document open, ~line 2367), so the reassignment is safe and the channel path is unchanged.
- **Construct-once + reused:** `BehaviorActionCatalog` subscribes to `sharedSchemaExporter.Changed` in its ctor and rebuilds its snapshot on hot-reload (AN3). It is built once here and reused for both palette and factory; no per-document or per-reload reconstruction.

**Forward to factory** (in the `DocumentOpened` Blueprint case, ~line 2367):
```csharp
doc.ViewState = Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.Build(
    doc.Asset, adapterBundle, _blueprintEditService,
    _blueprintPaletteEntries,
    channelCommands: ...BuiltInChannelCommandCatalog.Instance,
    peerAssetCatalog: blueprintPeerCatalog,
    behaviorActions: _behaviorActionCatalog);   // ← AN7 added
```

### 10.2 `BlueprintDocumentFactory.cs`

**Signature** — added last param (mirrors `channelCommands`):
```csharp
IAssetCatalog?          peerAssetCatalog = null,
ActionCatalog.IBehaviorActionCatalog? behaviorActions = null)
```

**Thread to graph model** (~line 135):
```csharp
var graphModel = new BlueprintGraphModel(bpAsset, graph, kindRegistry, channelCommands, peerLookup,
    editorRegistry, enumProvider, behaviorActions);   // ← behaviorActions added
```

**Thread to command sink** (~line 158):
```csharp
var commandSink = new BlueprintCommandSink(
    bpAsset, graph, graphModel, nodeCatalog, validator, history,
    localEditService, markDirty, channelCommands: channelCommands,
    enumProvider: enumProvider, behaviorActions: behaviorActions);   // ← behaviorActions added
```

### 10.3 `BlueprintGraphModel.cs`

- New field `_behaviorActions` (`ActionCatalog.IBehaviorActionCatalog?`).
- New ctor param `behaviorActions = null` (last param), assigned to `_behaviorActions`.
- `GetCanonicalPins` call (~line 175) now passes `_behaviorActions` as the trailing arg:
  ```csharp
  var canonicalPins = NodePinSchema.GetCanonicalPins(assetNode, _kindRegistry, _asset, _channelCommands, _graph, _peerSignatureLookup, _behaviorActions);
  ```

### 10.4 `BlueprintCommandSink.cs`

- New field `_behaviorActions`.
- New ctor param `behaviorActions = null` (last param), assigned to `_behaviorActions`.
- `ApplyPinIds`'s `GetCanonicalPins` call (~line 235) now passes `behaviorActions: _behaviorActions`.

The channel-command path (`_channelCommands` → `ChannelCommandPinsFromCatalog`) is unchanged throughout; the catalog reference type uses the relative `ActionCatalog.IBehaviorActionCatalog` qualifier (no new `using` needed in the `Hrot.Blueprints.Editor.Host` files).

### 10.5 Verification (live-style wiring)

**Build:** `dotnet build IOS-IG-SimHost.sln` → **0 errors**, 8 warnings (all pre-existing, none in changed files).

**New tests** — `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN7_LiveWiringTests.cs` (3, all pass), exercising the production composition path (real `BehaviorActionCatalog` from a channel catalog + `IActionSchemaExporter` with a `Shared` entry):
- `CreatePaletteRegistry_WithLiveCatalog_ContainsNonChannelActionEntry_AN7` — palette registry built with the real catalog contains the `Action:{FQN}` kind and bakes `ActionFqn`.
- `BlueprintGraphModel_WithLiveCatalog_ProjectsNonChannelParamPins_AN7` — a non-channel `ChannelCommandNode` projected through the real `BlueprintGraphModel` (catalog threaded as in the factory) yields exec In/Out + one data-IN pin per params-DTO field.
- `BlueprintGraphModel_WithoutCatalog_NonChannelNode_IsExecOnly_AN7` — control: same node without catalog collapses to exec-only (proves the pins are a consequence of the threaded catalog).

**Suites (0 new failures):**
- AN7/AN4/catalog/graph-model/command-sink targeted run: **154 / 154 pass**.
- Full `Hrot.Blueprints.Tests`: **1580 pass, 4 fail, 8 skip** — the 4 failures are the same pre-existing ones (ConditionSummary ScoreCrossed, LibraryEmitGolden CRLF, AllocationFree, LibraryMath CRLF). Was 1577 pass pre-AN7-wiring; +3 = the new live-wiring tests.
- `Hrot.Editor.AiShared.Tests`: 855/856; the single failure was the known fs-race flake (`AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind`), which **passed on re-run** (9/9).

---

## Files changed

| File | Change |
|---|---|
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/Nodes.cs` | Add `ActionFqn` (string?, JsonIgnore when null) to `ChannelCommandNode` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/ActionCatalog/BehaviorActionCatalog.cs` | `MapHosting`: add `Blueprint` for `Shared` flag |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/BlueprintNodePaletteEntries.cs` | Add `Categories.Action`, `NonChannelActionEntries()` method |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs` | Add `behaviorActionCatalog` param + `NonChannelActionEntries` call in `CreatePaletteRegistry` |
| `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` | **(AN7 wiring)** Add `_behaviorActionCatalog` field; construct `BehaviorActionCatalog` from `BuiltInChannelCommandCatalog.Instance` + `sharedSchemaExporter`; rebuild palette with it; forward `behaviorActions:` to `BlueprintDocumentFactory.Build` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintDocumentFactory.cs` | **(AN7 wiring)** Add `behaviorActions` param; thread into `BlueprintGraphModel` + `BlueprintCommandSink` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs` | **(AN7 wiring)** Add `_behaviorActions` field + ctor param; pass to `GetCanonicalPins` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/BlueprintCommandSink.cs` | **(AN7 wiring)** Add `_behaviorActions` field + ctor param; pass to `GetCanonicalPins` in `ApplyPinIds` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN7_LiveWiringTests.cs` | **(AN7 wiring)** New: 3 live-style wiring tests (real catalog → palette + graph-model projection) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | Add `behaviorActions` param; dispatch to `NonChannelActionPins`; extract `AppendParamPins` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/NodeDrawers/ChannelCommandNodeDrawer.cs` | `Draw()` dispatches to `DrawNonChannelAction()` or `DrawChannelCommand()` |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/BehaviorActionCatalogTests.cs` | Rename + add 3 AN7 catalog tests |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/NodePinSchemaEnrichmentTests.cs` | Add 5 AN7 pin/JSON tests |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/AN4_PerActionPaletteTests.cs` | Add 6 AN7 palette tests |
