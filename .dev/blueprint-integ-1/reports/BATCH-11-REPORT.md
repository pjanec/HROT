# BATCH-11 Report
**Blueprint data-flow host adapters (AIE-040..043)**

---

## Implementation Summary

Four new files in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/`:

| File | Task | Interface |
|------|------|-----------|
| `BlueprintTypeSystem.cs` | AIE-041 | `ITypeSystem` |
| `BlueprintGraphModel.cs` | AIE-040 | `IGraphModel` |
| `BlueprintLinkValidator.cs` | AIE-042 | `ILinkValidator` |
| `BlueprintNodeCatalog.cs` | AIE-043 | `INodeCatalog` |

Three supporting adapter files (not interfaces, internal to Host/):

- `BlueprintNodeModel.cs` — `INodeModel` adapter over `Node`
- `BlueprintPinModel.cs` — `IPinModel` adapter over `Pin`
- `BlueprintLinkModel.cs` — `ILinkModel` adapter over `Link`

Four test files in `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Host/` (81 tests total):

- `BlueprintTypeSystemTests.cs` (31 tests)
- `BlueprintGraphModelTests.cs` (22 tests)
- `BlueprintLinkValidatorTests.cs` (14 tests)
- `BlueprintNodeCatalogTests.cs` (14 tests)
- `NullPinDefaultValueEditorRegistry.cs` (headless test helper)

---

## The Real Blueprint Pin/Type Model

Verified from `Hrot.Blueprints.Compiler/Assets/GraphTypes.cs` and `Nodes.cs`:

**`Pin` class** (in `Graph.Nodes[i].Pins`):
```csharp
public sealed class Pin {
    public Guid   Id          { get; set; }
    public string Name        { get; set; } = "";
    public string Direction   { get; set; } = "";  // "In" or "Out"
    public BlueprintTypeRef TypeRef { get; set; } = new();
    public bool   IsExec      { get; set; }         // exec vs data
    public List<Guid> LinkedToIds { get; set; } = new();  // informational; links canonical source is Graph.Links
}

public sealed class BlueprintTypeRef {
    public string TypeId  { get; set; } = "";   // CLR-style full name, e.g. "System.Single"
    public bool IsArray   { get; set; }
    public List<BlueprintTypeRef> GenericArgs { get; set; } = new();
}
```

**`Link` class** (in `Graph.Links`):
```csharp
public sealed class Link {
    public Guid FromNodeId { get; set; }
    public Guid FromPinId  { get; set; }
    public Guid ToNodeId   { get; set; }
    public Guid ToPinId    { get; set; }
}
```

**Exec vs Data:** Distinguished by `Pin.IsExec` (bool). Exec pins have empty/ignored `TypeRef.TypeId`.

**Type key:** `Pin.TypeRef.TypeId` — full CLR-style name string, e.g. `"System.Boolean"`, `"System.Int32"`, `"System.Single"`, `"System.Numerics.Vector3"`, `"Fdp.Core.Entity"`, `"FDP.Eqs.EqsSensorHandle"`. Arrays use `TypeRef.IsArray = true`.

**Pin direction:** String `"In"` or `"Out"` (not an enum).

---

## How Each Adapter Mirrors FakeBlueprint vs Deviates

### `BlueprintTypeSystem` (AIE-041)
**Mirrors FakeTypeSystem exactly** — same `Dictionary<string, (Vector4, string)>` type table, same `GetPinColor`/`GetPinShape`/`AreCompatible`/`IsImplicitCast` signature. 

**Deviations:**
- Exec pins use `PinShape.Triangle` (not Circle) — appropriate for Blueprint's visual language.
- Exec pins return white (1,1,1,1) from `GetPinColor` (matches Unreal convention).
- Added Blueprint-specific types not in FakeTypeSystem: `Double`, `Byte`, `UInt32`, `Entity`, `EqsSensorHandle`.
- **Cast rules:** `int → float` is `AreCompatible` *and* `IsImplicitCast` = true. This is the only implicit cast Blueprint supports (widening); all others are false.

### `BlueprintGraphModel` (AIE-040)
**Mirrors FakeGraphModel + HsmGraphModel.** FakeGraphModel is mutable (build + mutate in-place); `BlueprintGraphModel` is read-only projection like `HsmGraphModel`.

**Deviations:**
- `Rebuild()` / `RebuildAndNotify()` public mutation API (matches HsmGraphModel's `OnAssetChanged` pattern, but made public for the host to call after edits until the full command-sink + asset-mutation event is wired in BATCH-12).
- `MakeLinkId(Guid, Guid)` is public+static: derives a deterministic `LinkId` from `(FromPinId, ToPinId)` via `IdGenerator.Deterministic`. This is needed by tests to verify link identity without enumerating.
- No `Changed` subscription to an asset event (asset doesn't have a `Changed` event — that's added in BATCH-12's BlueprintCommandSink).

### `BlueprintLinkValidator` (AIE-042)
**Mirrors FakeLinkValidator closely** — same `Validate(PinId from, PinId to)` pattern, same early-return on null pins, exec/data kind check, type check.

**Deviations:**
- Type compatibility delegated to `BlueprintTypeSystem.AreCompatible` (FakeLinkValidator does an inline `fromPin.Type != toPin.Type` comparison — we use the type system).
- **Single-data-input rule:** FakeLinkValidator doesn't enforce this; `BlueprintLinkValidator` checks `_graph.Links.Any(link => link.ToPin == inputPin.Id)` and returns `Invalid` if an existing link is present. This signals to the caller to do a replace (BATCH-12's CommandSink handles the actual replacement).
- Added same-node rejection (self-loop on same node, not just same pin).

### `BlueprintNodeCatalog` (AIE-043)
**Mirrors FakeNodeCatalog for Query/QueryForPinContext; mirrors BTreeNodeCatalog for dynamic registry wrapping.**

**Deviations:**
- Wraps `NodeKindRegistry` (the production palette, not a hard-coded list). Converts `NodeKindDescriptor` to `NodeCatalogEntry` by calling `d.CreateInstance()` to introspect pin signatures — this is how the catalog projects the production node shapes into NodeEdit's catalog contract.
- Dynamic entries: `CustomEventDecl` → `CustomEvent.{name}` entries; `CallablePeers` (Guids) → `CallPeer.{guidN}` entries.
- `CatalogChanged` event + `Refresh()` public API.
- `Asset` property setter triggers `Refresh()`.

---

## Cast Rules Implemented

| Rule | `AreCompatible` | `IsImplicitCast` |
|------|-----------------|-----------------|
| Same type | true | false |
| `int` → `float` | true | true |
| Everything else | false | false |

Rationale: Blueprint's `CastNode` handles explicit casts (string→int, etc.); the only truly implicit widening in the asset model is int→float (evidenced by the FakeTypeSystem comment and the Blueprint compiler's Stage4_TypeResolve).

---

## Catalog Wrapping NodeKindRegistry + Dynamic Peers/Events

`BlueprintEditorBootstrap.CreatePaletteRegistry()` populates a `NodeKindRegistry` with 3 entries (WhenNode, ReadEqsResult, SpawnEqsSensor). `BlueprintNodeCatalog` wraps this registry via `DescriptorToEntry()` — it calls `d.CreateInstance()` on each descriptor to get a default node instance, then reads its `Pins` to build `PinSignature` lists. Dynamic entries are appended per asset: one `CustomEvent.*` entry per `CustomEventDecl`, one `CallPeer.*` entry per callable peer Guid.

---

## Test Results

### `Hrot.Blueprints.Tests` (full suite)
```
Failed:    10  (pre-existing DEBT-006 snapshot failures — unchanged)
Passed:   970  (889 existing + 81 new)
Skipped:    8
Total:    988
```

### New tests by class
| Class | Tests | All Pass |
|-------|-------|---------|
| `BlueprintTypeSystemTests` | 31 | yes |
| `BlueprintGraphModelTests` | 22 | yes |
| `BlueprintLinkValidatorTests` | 14 | yes |
| `BlueprintNodeCatalogTests` | 14 | yes |
| **Total new** | **81** | **yes** |

### `Hrot.Editor.AiShared.Tests`
```
Passed: 702, Failed: 0
```

### `EditorSubsystemBoot` filter (Hrot.ClusterRunner.Integration.Tests)
```
Passed: 10, Failed: 0
```

### Full solution build
```
dotnet build IOS-IG-SimHost.sln -p:CycloneDdsDisableCodeGen=true
Build succeeded. 0 Warnings, 0 Errors.
```

Note: `-p:CycloneDdsDisableCodeGen=true` is required to work around a pre-existing CycloneDDS IDL scope resolution bug (`Fdp::Toolkit::Diagnostics::Gizmos::PipelineTarget` cannot be resolved by the codegen tool in Debug build). The `Hrot.IG.csproj` codegen failure existed before this batch (commit `8e197569` documents "fix(build): unblock solution compile + unify CycloneDDS.NET version"). The test binaries were cleared by a full `--no-incremental` rebuild; subsequent builds with the flag succeed.

---

## Developer Insights

1. **Link ID scheme:** Blueprint's `Graph.Links` has no stable ID per link — the canonical identity is `(FromPinId, ToPinId)`. We derive `LinkId` deterministically via SHA-256 over the pair. This is the same approach as HSM uses for `VisualId`.

2. **Pin.LinkedToIds is informational:** The authoritative connection list is `Graph.Links`. `Pin.LinkedToIds` appears to be a redundant cache stored by some serializers but is not the source of truth for the graph model.

3. **Single-data-input signal:** The validator returns `Invalid` with a descriptive message when a data input already has a connection. BATCH-12's `BlueprintCommandSink.ApplyAddLink` should interpret this to replace the existing link rather than reject entirely (matching Unreal's UE Blueprint behavior).

4. **NodeKindDescriptor.CreateInstance** may throw — the catalog wraps this in a try/catch and leaves pins empty on failure, so a malformed descriptor doesn't crash the catalog.

5. **CycloneDDS IDL Debug build:** The pre-existing issue is that `Fdp.Diagnostics.Contracts` only has Release IDL artifacts in its obj folder. A one-time copy of Release IDL → Debug IDL dir is a workaround, but the proper fix is to build `Fdp.Diagnostics.Contracts` in Debug configuration first, which should regenerate the IDL. The `CycloneDdsDisableCodeGen` flag is the cleaner workaround.

---

## Known Issues

- `BlueprintGraphModel` does not auto-subscribe to asset mutations (no `BlueprintAsset.Changed` event exists yet). Callers must call `RebuildAndNotify()` manually after edits. This is by design for this batch — mutation wiring is BATCH-12's `BlueprintCommandSink`.
- `BlueprintLinkValidator` returns `Invalid` (not `ValidWithCast`) for compatible-with-cast types other than int→float. The `ValidWithCast` verdict is designed for auto-inserting a cast node; the cast-node auto-insert is not in scope for this batch.
- `BlueprintNodeCatalog.DescriptorToEntry` calls `CreateInstance()` at catalog-build time. For descriptors with expensive constructors, this could be slow. In practice, all three current descriptors are cheap.

---

## Suggested Commit Message

feat(blueprints): AIE-040..043 — Blueprint data-flow host adapters (graph model, type system, link validator, node catalog)
