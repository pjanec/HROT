# AN1 Report — Stage-3 Default-Literal Materialization

**Branch:** `blueprint-integ-1`  
**Anchor commit (AN2):** `176b329c`  
**Date:** 2026-06-06

---

## STEP 1 — Investigation Findings

### BP4001 location

`Stage5_Schedule.cs`, `GraphScheduler.ResolveDataPin` (lines 958–982):

```csharp
if (link == null)
{
    // Unconnected -- emit BP4001 and return a dummy value.
    _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4001, ...));
    var dummy = AllocValue(Stage5_Schedule.UnknownType);
    _pinValueCache[pinId] = dummy;
    return dummy;
}
```

The check is a single `if (link == null)` — no awareness of defaults. The test
`Schedule_UnconnectedDataPin_EmitsBP4001` builds a `FunctionCallNode` with a data-IN pin that
has no `PinDefaults` entry and no `Pin.DefaultValue`, and asserts BP4001 is present in diagnostics.

### How a connected pin flows to IR

1. Stage5 `ResolveDataPin` finds the incoming `Link.FromNodeId` / `FromPinId`.
2. Calls `ResolveNodeOutput(fromNodeId, fromPinId, stmts)`.
3. For a `LiteralNode`: emits `IrStatement { Operation = new IrOp_Const(ln.ValueJson, pinType) }`.
4. `StatementEmitter.EmitOp` renders `var __tN = <CSharpLiteral>;` verbatim.

`IrOp_Const.CSharpLiteral` is emitted word-for-word — so the value must already be valid C#
when it reaches Stage5.

### Chosen injection mechanism: synthesize LiteralNode in Stage3

Two options were considered:

| Option | Pros | Cons |
|--------|------|------|
| Synthesize `LiteralNode` + `Link` in Stage3 (chosen) | Uses the existing well-tested path (`LiteralNode` → `IrOp_Const`); no Stage5 modification; LiteralNode already survives EliminateOrphanNodes (CollectReachable follows data wires in both directions); deterministic GUID synthesis already exists in Stage3. | Adds nodes to the graph (projection-only: they are never persisted). |
| Emit `IrOp_Const` directly in Stage5 `ResolveDataPin` | Fewer nodes in graph | Requires Stage5 to know about `PinDefaults`/`DefaultValue` and perform type-dispatch formatting; breaks the separation of concerns where Stage3 = normalize and Stage5 = schedule. |

**Chosen: Stage3 LiteralNode synthesis.** Stage3 already synthesizes nodes for implicit casts
(`InsertImplicitCasts`); adding the same pattern for default literals is fully consistent.
Stage5 needs zero changes.

### Where default values live

Both fields exist in the model:
- `Node.PinDefaults` (`Dictionary<string,string>?`, keyed by pin name) — the **live editor path**:
  set by `BlueprintPinModel` when the user types a value in the inline editor. Serialized to JSON,
  survives save/load. Primary source per the task specification.
- `Pin.DefaultValue` (`string?`) — an older per-pin field also present in the model.

**Priority:** `PinDefaults[pin.Name]` first; fall back to `Pin.DefaultValue`.  A pin with neither
(and neither empty-string) is left unchanged → BP4001 still fires.

---

## STEP 2 — Implementation

### File changed: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage3_Normalize.cs`

**Lines 22–28 (old stub) replaced with** ~150 lines implementing:

1. `MaterializeDefaultPinLiterals(asset, ctx)` — iterates graphs, delegates to
   `MaterializeDefaultPinLiteralsInGraph`.

2. `MaterializeDefaultPinLiteralsInGraph(graph, asset)`:
   - Builds `connectedPinIds` from existing links (skips pins that already have a source).
   - For each node pin that is `!IsExec && Direction == "In"` and not already connected:
     - Resolves `rawDefault` from `PinDefaults[pin.Name]` then `Pin.DefaultValue`.
     - If no default → `continue` (pin stays unconnected → BP4001 later).
     - Calls `FormatDefaultLiteral(typeId, rawDefault)` → C# literal string.
     - If null (unsupported type) → `continue`.
     - Synthesizes `LiteralNode` (deterministic GUID via `SynthesizedGuid("default-literal", ...)`)
       and a `Link` from that node to the pin.
   - Returns original graph unchanged when `extraNodes.Count == 0` (preserves
     `Normalize_PreservesNodeCountForCleanAsset` test).
   - Otherwise returns a new `Graph` with nodes/links appended (same pattern as
     `EliminateOrphanNodes`).

3. `FormatDefaultLiteral(typeId, rawValue)`:
   - **Enum** (`typeId.StartsWith("global::", ...)`) → `(global::Ns.MyEnum)N`.
     The TypeId already carries the `global::` prefix per the AN2 convention, so no second prefix
     is added. Result: `(global::SomeNs.SomeEnum)2`. No `global::global::`.
   - `System.Int32` → `rawValue` (e.g. `42`).
   - `System.Single` → appends `f` if not already present (e.g. `3.14` → `3.14f`; `3.14f` → `3.14f`).
   - `System.Boolean` → `"true"` or `"false"` (case-normalized).
   - `System.String` → `"\"<escaped>\""`.
   - `Fdp.Core.FixedString32` / `Fdp.Core.FixedString64` → `new global::Fdp.Core.FixedString32("<text>")`.
   - `System.Int64/UInt32/UInt64/Int16/UInt16/Byte/SByte/Double/Decimal` — typed literals.
   - Unknown type → `null` (pin left for BP4001).

### File added: test
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Compiler/Stage3_NormalizationTests/MaterializeDefaultPinLiteralsTests.cs`

5 tests:
1. `DefaultPins_IntFloatFixedStringEnum_GenerateCorrectLiterals` — core AN1 e2e test: 4 pins
   (int via PinDefaults, float via Pin.DefaultValue, FixedString32 via PinDefaults, enum via
   PinDefaults with `global::SomeNs.SomeEnum`). Asserts:
   - `Assert.Contains("42", src)`
   - `Assert.Contains("3.14f", src)`
   - `Assert.Contains("new global::Fdp.Core.FixedString32(", src)`
   - `Assert.Contains("(global::SomeNs.SomeEnum)2", src)`
   - `Assert.DoesNotContain("global::global::", src)`
   - `Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.BP4001)`
2. `DefaultPins_PinDefaultsPreferredOver_PinDefaultValue` — PinDefaults wins over Pin.DefaultValue.
3. `DefaultPins_NoDefault_StillEmitsBP4001` — regression guard: no default → BP4001 fires.
4. `DefaultPins_FloatWithoutSuffix_GetsFSuffix` — `"1.5"` → `"1.5f"`.
5. `DefaultPins_Bool_EmitsTrueFalse` — `"true"` → `"true"` in generated source.

---

## VERIFY results

### New tests: 5/5 pass

```
Passed  DefaultPins_IntFloatFixedStringEnum_GenerateCorrectLiterals
Passed  DefaultPins_PinDefaultsPreferredOver_PinDefaultValue
Passed  DefaultPins_NoDefault_StillEmitsBP4001
Passed  DefaultPins_FloatWithoutSuffix_GetsFSuffix
Passed  DefaultPins_Bool_EmitsTrueFalse
```

### Regression: `Schedule_UnconnectedDataPin_EmitsBP4001` — PASS (unchanged)

### `dotnet build` — 0 CS errors

- `Hrot.Blueprints.Compiler`: 0 warnings, 0 errors.
- `Hrot.Blueprints.Tests`: 0 errors (8 pre-existing CS0618/CS8601 warnings, unchanged).

### Full Blueprints suite

**Before AN1 (clean state):** 4 failures  
**After AN1:** 4 failures — identical set.

| Test | Status | Pre-existing? |
|------|--------|---------------|
| `Library_EmitMatchesGoldenSource` | FAIL | Yes — CRLF snapshot flake (confirmed pre-existing) |
| `LibraryMath_GeneratedSource_Snapshot` | FAIL | Yes — CRLF snapshot flake (confirmed pre-existing) |
| `Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` | FAIL | Yes — known ScoreCrossed pre-existing |
| `TickFrame_1000Frames_AllocatesZeroBytes` | FAIL | Yes — known AllocatesZeroBytes pre-existing |

Pre-existing status confirmed by stashing AN1 changes and re-running those 2 golden tests — they
failed identically on the clean branch state.

**0 new failures.**

---

## Goldens

No golden regeneration required. My change only adds synthesized `LiteralNode` nodes to graphs
that have pins with defaults. The existing test assets (`LibraryMath`, `InstanceCounter`,
`MoveToAndFire`) have no `PinDefaults`/`Pin.DefaultValue` on their nodes, so their IR and emitted
output is byte-for-byte identical.

---

## Deviations

None. Implementation follows the plan exactly:
- Stage3 LiteralNode synthesis (not Stage5 inline const emit).
- Projection-only: no new JSON fields; synthesized nodes never persisted.
- `PinDefaults` primary, `Pin.DefaultValue` fallback.
- `global::` prefix for enums comes from `TypeId` as-is; no double-prefix.
- Unknown types silently skipped → BP4001 preserved.
