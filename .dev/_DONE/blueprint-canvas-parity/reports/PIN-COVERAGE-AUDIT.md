# Blueprint Canvas — Pin-Coverage Audit (BCP-BATCH-04 Task 3)

**Scope:** every Blueprint node kind the editor can place, and whether it currently
**projects data pins** on the canvas. Where a kind is exec-only (or has no pins), the
reason is classified as one of:

- **by-design** — the compiler defines no node data pins for this kind; exec-only is correct.
- **config-needed** — data pins appear only once the node is configured (e.g. a target
  type/method is chosen); an unconfigured node is intentionally exec-only.
- **data-limited** — a data gap, not a code gap: the underlying catalog/runtime metadata is
  too thin to surface rich per-arg pins yet (needs content/runtime enrichment).
- **deferred** — the schema needed to type the pins is not yet available; explicitly punted.

Pins are **projection-only** (DESIGN.md "PINS ARE PROJECTION-ONLY"): they are hydrated by
`NodePinSchema.GetCanonicalPins` (`Hrot.Blueprints.Editor/Host/NodePinSchema.cs`) at render
time and never persisted. The registry-backed kinds (`When`, `ReadEqsResult`,
`SpawnEqsSensor`) carry hand-authored pins via
`Hrot.Blueprints.Editor/NodeDrawers/WhenNodePaletteEntries.cs`; all other kinds fall through
to the built-in table in `NodePinSchema.GetCanonicalPins` (Pass 2, lines 91–126).

## Sources cited

- Pin schema (authoritative for projection): `NodePinSchema.cs` lines 91–388.
- Palette (the kinds the picker offers): `BlueprintNodePaletteEntries.All()` lines 42–136 and
  `WhenNodePaletteEntries.cs` lines 12–82.
- Node types: `Hrot.Blueprints.Compiler/Assets/Nodes.cs`.
- Channel-command params: `Hrot.Blueprints.Compiler/Compiler/Catalogs/BuiltInChannelCommandCatalog.cs`
  (all five entries use a **single placeholder** `ParamsTypeFqn = "System.Int32"`).
- Compiler consumption (proves a projected data pin is meaningful, not decorative): pin-helper
  XML-doc cross-references in `NodePinSchema.cs` to `Stage2_Validate`, `Stage4_TypeResolve`,
  `Stage5_Schedule`.

## Coverage table

| Kind (palette id) | Node type | Data pins now? | Pins projected | Classification | Source |
|---|---|---|---|---|---|
| `Branch` | `BranchNode` | **Yes** | exec In/True/False + data-IN `Condition:System.Boolean` | — | `NodePinSchema.BranchPins` 178–185 (Stage5 reads the condition data-IN) |
| `Sequence` | `SequenceNode` | No (exec-only) | exec In + Then0/Then1 | **by-design** (flow node; no data ports) | `NodePinSchema.SequencePins` 187–193 |
| `Return` | `ReturnNode` | No (exec-only) | exec In | **by-design** (function/graph exit; no data) | `NodePinSchema` 94 `ReturnNode => ExecOnly("In")` |
| `EventEntry` | `EventEntryNode` | No (exec-only) | exec Out | **by-design** (entry point; engine event has no node data ports) | `NodePinSchema` 93 `EventEntryNode => ExecOnly("Out")` |
| `CallCustomEvent` | `CallCustomEventNode` | No (exec-only) | exec In/Out | **config-needed** (params come from the referenced `CustomEventDecl`; the dynamic catalog entry `MakeCustomEventEntry` adds per-param data-IN pins once a custom event with parameters is bound — see `BlueprintNodeCatalog.cs` 223–254) | `NodePinSchema` 106 `CallCustomEventNode => ExecInOut()` |
| `CallDispatcher` | `CallEventDispatcherNode` | No (exec-only) | exec In/Out | **config-needed** (dispatcher signature not modeled as node pins yet) | `NodePinSchema` 108 |
| `BindDispatcher` | `BindEventDispatcherNode` | No (exec-only) | exec In/Out | **by-design** (bind is a control op; no data) | `NodePinSchema` 109 |
| `WaitForEvent` | `WaitForEventNode` | No (exec-only) | exec In/Out | **by-design** (latent suspend; no node data) | `NodePinSchema` 105 |
| `GetVariable` | `GetVariableNode` | **Yes** | data-OUT `Value:<var type>` (pure; no exec) | — | `NodePinSchema.GetVariablePins` 293–297; type from `ResolveVariableTypeId` |
| `SetVariable` | `SetVariableNode` | **Yes** | exec In/Out + data-IN `Value` + data-OUT `Value` (typed from variable) | — | `NodePinSchema.SetVariablePins` 299–306 |
| `FunctionCall` | `FunctionCallNode` | **Conditional** | exec In/Out (+ one data-IN per method param + `Return` data-OUT **when configured**) | **config-needed** (needs `TargetTypeId` + `MethodName`; unresolved → exec-only) | `NodePinSchema.FunctionCallPins` 266–291 (resolves the method via reflection; graceful exec-only fallback) |
| `Literal` | `LiteralNode` | **Yes** | data-OUT `Value:<TypeId or System.Object>` | — | `NodePinSchema.LiteralPins` 308–312 |
| `Cast` | `CastNode` | **Yes** | exec In/Out + data-IN `In:System.Object` + data-OUT `Out:<TargetTypeId>` | — | `NodePinSchema.CastPins` 314–321 |
| `ArrayMake` | `ArrayMakeNode` | **Yes** | exec In/Out + data-IN `0`,`1` (`ElementTypeId`) + data-OUT `Array` | — | `NodePinSchema.ArrayMakePins` 245–256 |
| `ArrayGet` | `ArrayGetNode` | **Yes** | exec In/Out + data-IN `Array`,`Index:System.Int32` + data-OUT `Element` | — | `NodePinSchema.ArrayGetPins` 228–236 |
| `Delay` | `LatentDelayNode` | **Yes** | exec In/Out + data-IN `Duration:System.Single` | — | `NodePinSchema.LatentDelayPins` 200–206 (Stage5 reads the delay data-IN) |
| `CallPeerBlueprint` | `CallPeerBlueprintNode` | No (exec-only) | exec In/Out | **config-needed** (peer fn signature → pins not modeled yet; the dynamic `MakeCallablePeerEntry` is exec-only too, `BlueprintNodeCatalog.cs` 256–271) | `NodePinSchema` 107 |
| `ChannelCommand` | `ChannelCommandNode` | **Limited** | exec In/Out + (when the catalog resolves the action) **one** placeholder param data-IN | **data-limited** (`BuiltInChannelCommandCatalog` exposes a single `ParamsTypeFqn = System.Int32` per action; rich per-arg pins need catalog enrichment — **DEBT-BCP-006**) | `NodePinSchema.ChannelCommandPins` 341–388; catalog 12–20 |
| `WaitForChannel` | `WaitForChannelNode` | No (exec-only) | exec In/Out | **by-design** (latent suspend on a channel; no node data) | `NodePinSchema` 104 |
| `ScoreDecision` | `ScoreDecisionNode` | **Yes** | exec In/Out + data-OUT `WinningOptionId:System.Byte` | — | `NodePinSchema.ScoreDecisionPins` 212–218 |
| `ReadRankedResult` | `ReadRankedResultNode` | **No (no pins)** | none | **deferred** (needs the `UtilityDecisionDef` result schema to type the ranked-entry data-OUT pins) | `NodePinSchema` 119 `ReadRankedResultNode => Array.Empty<Pin>()` |
| `PartitionElements` | `PartitionElementsNode` | No (exec-only) | exec In/Out | **by-design** (squad coordination primitive; no node data ports per compiler) | `NodePinSchema` 120 |
| `AssignRoles` | `AssignRolesNode` | No (exec-only) | exec In/Out | **by-design** (squad primitive) | `NodePinSchema` 121 |
| `AdvancePhase` | `AdvancePhaseNode` | No (exec-only) | exec In/Out | **by-design** (squad primitive) | `NodePinSchema` 122 |
| `AcquireSlot` | `AcquireSlotNode` | No (exec-only) | exec In/Out | **by-design** (squad primitive) | `NodePinSchema` 123 |
| `When` | `WhenNode` | **Yes** (authored) | exec In/Out/OnFired (exec); reactive guard config drives behavior | — | `WhenNodePaletteEntries.WhenNode` 12–31 (hand-authored pins; registry Pass 1) |
| `ReadEqsResult` | `ReadEqsResultNode` | **Yes** (authored) | data-IN `Handle`,`ResultIndex` + data-OUT `IsReady`,`ResultCount`,`Entity`,`Position`,`Score` | — | `WhenNodePaletteEntries.ReadEqsResult` 33–56 |
| `SpawnEqsSensor` | `SpawnEqsSensorNode` | **Yes** (authored) | exec In/Out + 5 data-IN params + data-OUT `Handle` | — | `WhenNodePaletteEntries.SpawnEqsSensor` 58–82 |

### Dynamic (non-palette) kinds

| Kind | When created | Data pins? | Classification | Source |
|---|---|---|---|---|
| `CustomEvent.<name>` | a `CustomEventDecl` exists on the asset | exec In/Out + one data-IN per declared parameter | **config-needed** (driven by the decl's parameters) | `BlueprintNodeCatalog.MakeCustomEventEntry` 223–254 |
| `CallPeer.<guid>` | a callable peer is registered | exec In/Out only | **data-limited** (peer fn args not modeled) | `BlueprintNodeCatalog.MakeCallablePeerEntry` 256–271 |

## Summary by classification

- **Has data pins today (10 + 3 authored):** Branch, GetVariable, SetVariable, Literal, Cast,
  ArrayMake, ArrayGet, Delay, ScoreDecision (+ When/ReadEqsResult/SpawnEqsSensor authored,
  ReadEqsResult/SpawnEqsSensor being the rich ones). FunctionCall joins this group **once
  configured** with a resolvable target type+method.
- **by-design exec-only (no data ports by compiler design):** Sequence, Return, EventEntry,
  BindDispatcher, WaitForEvent, WaitForChannel, PartitionElements, AssignRoles, AdvancePhase,
  AcquireSlot.
- **config-needed:** FunctionCall (TargetTypeId+MethodName), CallCustomEvent / CallDispatcher /
  CallPeerBlueprint (referenced signature not yet modeled as node pins),
  `CustomEvent.<name>` (driven by decl parameters).
- **data-limited (data gap, not code gap):** ChannelCommand — params are a **single placeholder
  type** (`System.Int32`) per action in `BuiltInChannelCommandCatalog`; the projection already
  decomposes a struct params type into per-field pins (`NodePinSchema.ReflectDataMembers`
  395–419), so enriching the catalog with real params types would automatically surface rich
  pins with **no code change**. → **DEBT-BCP-006**. `CallPeer.<guid>` likewise.
- **deferred:** ReadRankedResult — needs the `UtilityDecisionDef` result schema to type the
  ranked-entry data-OUT pins.

## What is a code gap vs a data gap (for the user)

- **Code gap (would need editor/compiler work):** ReadRankedResult (deferred schema);
  CallDispatcher/CallPeerBlueprint pin modeling.
- **Data gap (content/runtime, no editor code change):** ChannelCommand rich params
  (DEBT-BCP-006) — the projection path is ready; it is starved of catalog metadata.
- **Not a gap (correct as-is):** the ten by-design exec-only kinds; FunctionCall and
  CustomEvent which intentionally surface pins only after configuration.
