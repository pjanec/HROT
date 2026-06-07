# WHEN-BATCH-02 — Stage 2 Validators for WhenNode, ReadEqsResultNode, SpawnEqsSensorNode

## Tasks Covered

- **WHEN-M1-T3** — `WhenNode` validator (Stage 2 diagnostics BP2001–BP2015)
- **WHEN-M1-T4** — `ReadEqsResultNode` validator (BP2020, BP2021)
- **WHEN-M1-T5** — `SpawnEqsSensorNode` validator (BP2030, BP2031)

**Design reference (authoritative):** [When_Reactivity_Iteration_Design_v2_2.md](../When_Reactivity_Iteration_Design_v2_2.md) §4, §4.1, §4.2, §4.3, §4.4  
**Task detail (authoritative):** [TASK-DETAIL.md](../TASK-DETAIL.md)

---

## Context

WHEN-BATCH-01 added the three new node classes to the schema. This batch adds the Stage 2
validators that enforce all constraints on those nodes.

**CRITICAL prerequisite:** `DiagnosticCodes.cs` currently defines `BP2001`, `BP2002`, `BP2003`
as Stage 3 — Normalize codes. The new WhenNode validators (DESIGN §4.1) need BP2001–BP2015.
Before adding any new BP20xx constants, **rename the three Stage 3 codes** to free up the
BP20xx range:

| Old constant | New constant | Used in |
|---|---|---|
| `BP2001` (orphan node warning) | `BP3010` | `Stage3_Normalize.cs` line 172 |
| `BP2002` (implicit cast warning) | `BP3011` | `Stage3_Normalize.cs` line 118 |
| `BP2003` (unused placeholder) | `BP3012` | not used — keep or remove |

Update the `// Stage 3 -- Normalize` comment block in `DiagnosticCodes.cs` to reflect
the new constants. Update all references in `Stage3_Normalize.cs`.

Also update the comment on `BP1600`:
```
public const string BP1600 = "BP1600";  // OrphanedNode (Stage 2 graph-structure)
```
(Remove the "(unused, alias of BP2001)" note — BP2001 is now repurposed.)

---

## Step 1: New diagnostic codes

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs`

Add a new section after the Stage 2 graph-structure codes (after BP1602) and before the
renamed Stage 3 section:

```csharp
// Stage 2 -- Validate (WhenNode rules)
public const string BP2001 = "BP2001";  // WhenNode in unsupported dispatch
public const string BP2002 = "BP2002";  // WhenNode missing required payload
public const string BP2003 = "BP2003";  // WhenNode Value Changed: invalid property path
public const string BP2004 = "BP2004";  // WhenNode Value Changed: peer BP variable not declared
public const string BP2005 = "BP2005";  // WhenNode Event Fired: event type not in catalog
public const string BP2006 = "BP2006";  // WhenNode Event Fired: Self filter without target field
public const string BP2007 = "BP2007";  // WhenNode Event Fired: payload condition invalid
public const string BP2008 = "BP2008";  // WhenNode Condition Met: predicate tree null or empty
public const string BP2009 = "BP2009";  // WhenNode Condition Met: predicate DTO references unknown type
public const string BP2010 = "BP2010";  // WhenNode EQS Result: sensor variable not declared
public const string BP2011 = "BP2011";  // WhenNode EQS Result: trigger requires threshold/max-age
public const string BP2012 = "BP2012";  // WhenNode Edges set to None
public const string BP2013 = "BP2013";  // WhenNode Event Fired falling edge meaningless (warning)
public const string BP2014 = "BP2014";  // WhenNode Value Changed epsilon on non-float field (warning)
public const string BP2015 = "BP2015";  // WhenNode downstream of a Branch (warning)

// Stage 2 -- Validate (ReadEqsResultNode rules)
public const string BP2020 = "BP2020";  // ReadEqsResultNode in unsupported dispatch
public const string BP2021 = "BP2021";  // ReadEqsResultNode sensor variable not declared

// Stage 2 -- Validate (SpawnEqsSensorNode rules)
public const string BP2030 = "BP2030";  // SpawnEqsSensorNode in unsupported dispatch
public const string BP2031 = "BP2031";  // SpawnEqsSensorNode template not found
```

---

## Step 2: EQS template catalog interface

For BP2031, the validator needs to check whether a `TemplateAssetId` (Guid) is a known
EQS template. Add a minimal catalog interface:

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Catalogs/IEqsTemplateCatalog.cs`

```csharp
namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Provides a compile-time set of known EQS template asset IDs.
/// Used by Stage 2 validators to check SpawnEqsSensorNode.TemplateAssetId.
/// </summary>
public interface IEqsTemplateCatalog
{
    /// <summary>Returns true if <paramref name="assetId"/> is a registered EQS template.</summary>
    bool Contains(Guid assetId);
}
```

Add `IEqsTemplateCatalog? EqsTemplates = null` as an optional parameter to `CompileOptions`:

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/CompileOptions.cs`

```csharp
public sealed record CompileOptions(
    CompilerMode Mode,
    INodeRegistry NodeRegistry,
    ITypeRegistry TypeRegistry,
    IEngineEventCatalog EngineEvents,
    IChannelCommandCatalog ChannelCommands,
    IWaitPrimitiveCatalog WaitPrimitives,
    IReadOnlyList<BlueprintSignature> SiblingSignatures,
    bool EmitPdbWithEmbeddedSource = false,
    string? VirtualSourcePath = null,
    IEqsTemplateCatalog? EqsTemplates = null);  // NEW — optional; null = no templates registered
```

Thread into `ValidationContext`:

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/ValidationContext.cs`

Add:
```csharp
public IEqsTemplateCatalog? EqsTemplates { get; }
```

And in the constructor:
```csharp
EqsTemplates = options.EqsTemplates;
```

---

## Step 3: Implement validators in Stage2_Validate.cs

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs`

Register the three new validators at the **end** of the `Validators` list:

```csharp
new V_WhenNodeRules(),
new V_ReadEqsResultNodeRules(),
new V_SpawnEqsSensorNodeRules(),
```

### V_WhenNodeRules

Iterates all `WhenNode` instances across all graphs and enforces BP2001–BP2015:

```csharp
internal sealed class V_WhenNodeRules : IValidator
{
    // Dispatch contexts where WhenNode is forbidden:
    //   - Library dispatch
    //   - AiPrimitive (any intent)
    //   - Instance + pure-function graph (GraphKind.Function where IsPureFunction flag)
    // Note: GraphKind.Function in Instance dispatch may be pure or impure.
    // The "pure-function graph" check: a Function graph in an Instance blueprint is
    // considered pure if it contains no EventEntryNode (i.e., it is a user-defined
    // pure helper function). WhenNode is forbidden there.
    // Use the same definition the existing V_LatentRules uses for "latent-in-pure-function".
}
```

#### Enforcement rules

**BP2001 — unsupported dispatch (Error):**
```
For each graph in the asset:
  For each WhenNode in graph.Nodes:
    If asset.Dispatch == Library → emit BP2001
    If asset.Dispatch == AiPrimitive → emit BP2001
    If asset.Dispatch == Instance AND graph.Kind == GraphKind.Function
       AND graph contains no EventEntryNode (i.e. it is a pure helper function) → emit BP2001
```

**BP2002 — missing required payload (Error):**
```
For each WhenNode:
  If Mode == ValueChanged  AND ValueChanged == null → emit BP2002
  If Mode == EventFired    AND EventFired == null   → emit BP2002
  If Mode == ConditionMet  AND ConditionMet == null → emit BP2002
  If Mode == EqsResult     AND EqsResult == null    → emit BP2002
```

**BP2012 — Edges set to None (Error):**
```
For each WhenNode:
  If Edges == WhenEdge.None → emit BP2012
```

Emit BP2012 **before** mode-specific checks (so BP2002+BP2012 both fire when both are wrong).

**BP2003 — Value Changed: invalid property path (Error):**
```
For each WhenNode where Mode == ValueChanged AND ValueChanged != null:
  If string.IsNullOrEmpty(ValueChanged.ComponentTypeId) OR
     string.IsNullOrEmpty(ValueChanged.PropertyPath) → emit BP2003
```

**BP2004 — Value Changed: peer BP variable not declared (Error):**
```
For each WhenNode where Mode == ValueChanged AND ValueChanged != null:
  If Source == PeerBlueprintVariable:
    If ValueChanged.PeerBlueprintAssetId == null → emit BP2004
    Else if !ctx.SiblingSignaturesById.ContainsKey(ValueChanged.PeerBlueprintAssetId.Value) → emit BP2004
```

**BP2014 — epsilon on non-float field (Warning):**
```
For each WhenNode where Mode == ValueChanged AND ValueChanged != null:
  If ValueChanged.Epsilon != 0:
    // Only warn if the ComponentTypeId resolves and the PropertyPath ends with a known
    // integer or boolean field. Since full field-type resolution happens in Stage 4,
    // this validator does a best-effort check:
    // Emit BP2014 warning ONLY if the ComponentTypeId resolves in the TypeRegistry to
    // a type where the field can be determined to be non-float.
    // If the TypeRegistry cannot resolve or determine the field type, skip the warning
    // (conservative: do not warn when uncertain).
    // Implementation note: for now, emit BP2014 if ValueChanged.PropertyPath is non-empty
    // and the type resolves but is NOT System.Single or System.Double. A future pass
    // (Stage 4) can emit this more precisely.
```

For simplicity, in this batch: **emit BP2014 only when** `Epsilon != 0.0` **AND** the
`ComponentTypeId` is a known non-float type (i.e., `ctx.TypeRegistry.TryResolve(
new BlueprintTypeRef { TypeId = propertyType }, out var ir)` where `ir.FullName` is not
`System.Single` or `System.Double`). If the component type can't be resolved at this stage,
skip the warning. (The full resolution requires knowing the field type within the component,
which is Stage 4 work; this is a best-effort warning.)

**Simpler implementation for BP2014:** Just check `ValueChanged.Epsilon != 0` and leave
the actual type check as a no-op (always warn when epsilon != 0 and mode = ValueChanged
and source = SelfComponent). The test for BP2014 will use a case where epsilon is set.

**BP2005 — Event Fired: event type not in catalog (Error):**
```
For each WhenNode where Mode == EventFired AND EventFired != null:
  If string.IsNullOrEmpty(EventFired.EventTypeId) OR
     !ctx.EngineEvents.TryGet(EventFired.EventTypeId, out _) → emit BP2005
```

Look at how the existing `V_EventGraphReferences` and `V_WaitNodeReferences` validators
check engine event catalog membership to understand the correct `IEngineEventCatalog` API.

**BP2006 — Self filter without target field (Error):**
```
For each WhenNode where Mode == EventFired AND EventFired != null:
  If EventFired.TargetFilter == EventTargetFilter.Self AND
     string.IsNullOrEmpty(EventFired.TargetFieldName) → emit BP2006
```

**BP2007 — payload condition invalid (Error):**
```
For each WhenNode where Mode == EventFired AND EventFired != null:
  If EventFired.PayloadCheck != null:
    If string.IsNullOrEmpty(EventFired.PayloadCheck.PropertyPath) OR
       string.IsNullOrEmpty(EventFired.PayloadCheck.TargetValueText) → emit BP2007
```

**BP2013 — EventFired with FallingEdge (Warning):**
```
For each WhenNode where Mode == EventFired:
  If (Edges & WhenEdge.FallingEdge) != 0 → emit BP2013 (Warning)
```

**BP2008 — Condition Met: predicate null or empty (Error):**
```
For each WhenNode where Mode == ConditionMet AND ConditionMet != null:
  #if NET8_0_OR_GREATER
  If ConditionMet.Condition == null → emit BP2008
  Else if ConditionMet.Condition is CompoundPredicateDto compound
       AND compound.Conditions.Count == 0 → emit BP2008
  #endif
  // (In netstandard2.0 build, Condition is typed as object — skip predicate checks)
```

**BP2009 — predicate DTO references unknown type (Error):**
```
#if NET8_0_OR_GREATER
For each WhenNode where Mode == ConditionMet AND ConditionMet?.Condition != null:
  Recursively walk the predicate tree.
  For each PropertyMatchDto where ComponentType == null → emit BP2009
  (ComponentType is null when TypeNameJsonConverter failed to resolve the type name.)
#endif
```

Implement a recursive helper:
```csharp
private static bool HasUnresolvableComponentType(SearchPredicateDto? predicate)
{
    return predicate switch
    {
        null => false,
        PropertyMatchDto p => p.ComponentType == null,
        CompoundPredicateDto c => c.Conditions.Any(HasUnresolvableComponentType),
        _ => false,
    };
}
```

**BP2010 — EQS Result: sensor variable not declared (Error):**
```
For each WhenNode where Mode == EqsResult AND EqsResult != null:
  If no variable in asset.Variables has name == EqsResult.SensorVariableName
     AND TypeId == "FDP.Eqs.EqsSensorHandle" → emit BP2010
  Note: also check Variables (for Instance dispatch); if asset.Dispatch != Instance,
  BP2001 already fired, so just skip further mode checks.
```

**BP2011 — trigger requires threshold/max-age (Error):**
```
For each WhenNode where Mode == EqsResult AND EqsResult != null:
  If EqsResult.Trigger == EqsTrigger.ScoreCrossed AND EqsResult.ScoreThreshold == 0 → emit BP2011
  If EqsResult.Trigger == EqsTrigger.BecomesStale AND EqsResult.MaxAgeSeconds <= 0 → emit BP2011
```

**BP2015 — WhenNode downstream of a Branch (Warning):**
```
For each graph:
  Build a set of "branch-successor" nodes: all nodes reachable from any BranchNode
  via exec-flow links (follow the Out/True/False output exec pins forward).
  For each WhenNode in the graph:
    If WhenNode.Id is in the branch-successor set → emit BP2015 (Warning)
```

Implementation hint: An exec pin has `PinDirection == Output` and `PinKind == Exec`
(check the Pin model to confirm exact field names). Walk the graph's Links where
`FromNodeId` is a BranchNode and follow forward. Use BFS.

If exec pin detection is unclear from the schema (pins may not always be materialized
at Stage 2 — they're added in Stage 3 Normalize), then **skip BP2015 in this batch**
and add a TODO comment. BP2015 is a warning, not an error, and the graph's pin list
may be empty before Stage 3. Note any skip in the batch report.

---

### V_ReadEqsResultNodeRules

**BP2020 — unsupported dispatch (Error):**  
Same as BP2001 logic but for `ReadEqsResultNode`. ReadEqsResultNode is Instance-only.
```
If asset.Dispatch != Instance → emit BP2020 for each ReadEqsResultNode
If asset.Dispatch == Instance AND graph.Kind == Function AND no EventEntryNode → emit BP2020
```

**BP2021 — sensor variable not declared (Error):**
```
For each ReadEqsResultNode:
  If no variable in asset.Variables has Name == node.SensorVariableName
     AND TypeId == "FDP.Eqs.EqsSensorHandle" → emit BP2021
```

---

### V_SpawnEqsSensorNodeRules

**BP2030 — unsupported dispatch (Error):**  
Same pattern as BP2001/BP2020. SpawnEqsSensorNode is Instance-only (Instance Tick or Event
graph; NOT Library, NOT AiPrimitive, NOT pure-function).

**BP2031 — template not found (Error):**
```
For each SpawnEqsSensorNode:
  If node.TemplateAssetId == Guid.Empty → emit BP2031
  Else if ctx.EqsTemplates != null AND !ctx.EqsTemplates.Contains(node.TemplateAssetId) → emit BP2031
  Else if ctx.EqsTemplates == null → skip (no catalog = no validation; templates are
    validated at runtime by the generated code's static reference)
```

The rationale: when `ctx.EqsTemplates == null` (no catalog provided), the validator
assumes the caller didn't wire up template discovery (e.g., command-line quick compile),
so BP2031 is suppressed. When a catalog is provided and the template isn't in it, BP2031
fires. This allows test code to provide a small stub catalog with only known templates.

---

## Step 4: Test files

Create three new test files. Each test file follows the pattern in `Stage1To5Tests.cs`:
build a `BlueprintAsset`, create `DiagnosticSink` + `ValidationContext(sink, options)`,
call `Stage2_Validate.Run(asset, ctx)`, assert on `sink.All`.

Use `BlueprintAssetBuilder` to construct assets. Locate it at
`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs`
and understand its API before writing tests.

### File 1: `Hrot.Blueprints.Tests/Compiler/WhenNodeValidatorTests.cs`

Minimum required test methods (name must match exactly for DESIGN §15.2 compliance):

| Test method | What it asserts |
|---|---|
| `Validate_LibraryDispatch_BP2001` | Library asset with a WhenNode emits BP2001 |
| `Validate_AiPrimitiveDispatch_BP2001` | AiPrimitive asset with a WhenNode emits BP2001 |
| `Validate_MissingPayload_ValueChanged_BP2002` | WhenNode Mode=ValueChanged, ValueChanged=null → BP2002 |
| `Validate_MissingPayload_EventFired_BP2002` | WhenNode Mode=EventFired, EventFired=null → BP2002 |
| `Validate_MissingPayload_ConditionMet_BP2002` | WhenNode Mode=ConditionMet, ConditionMet=null → BP2002 |
| `Validate_MissingPayload_EqsResult_BP2002` | WhenNode Mode=EqsResult, EqsResult=null → BP2002 |
| `Validate_InvalidPropertyPath_BP2003` | ValueChanged with empty ComponentTypeId → BP2003 |
| `Validate_PeerVariableNotDeclared_BP2004` | Source=PeerBlueprintVariable, no sibling signature → BP2004 |
| `Validate_EventTypeNotInCatalog_BP2005` | EventFired with unknown EventTypeId → BP2005 |
| `Validate_SelfFilterWithoutTargetField_BP2006` | EventFired TargetFilter=Self, TargetFieldName null → BP2006 |
| `Validate_PayloadConditionInvalid_BP2007` | PayloadCondition with empty PropertyPath → BP2007 |
| `Validate_ConditionNull_BP2008` | ConditionMet.Condition == null → BP2008 |
| `Validate_ConditionEmptyCompound_BP2008` | ConditionMet.Condition == CompoundPredicate with 0 children → BP2008 |
| `Validate_SensorVariableNotDeclared_EqsResult_BP2010` | EqsResult.SensorVariableName not in Variables → BP2010 |
| `Validate_ScoreCrossedWithZeroThreshold_BP2011` | Trigger=ScoreCrossed, ScoreThreshold==0 → BP2011 |
| `Validate_BecomesStaleWithZeroMaxAge_BP2011` | Trigger=BecomesStale, MaxAgeSeconds==0 → BP2011 |
| `Validate_EdgesNone_BP2012` | WhenEdge.None → BP2012 |
| `Validate_EventFiredFallingEdge_BP2013Warning` | EventFired + FallingEdge → BP2013 Warning (not error) |
| `Validate_EpsilonNonZero_ValueChanged_BP2014Warning` | Epsilon != 0 → BP2014 Warning |
| `Validate_ValidInstance_NoErrors` | A correct Instance WhenNode emits zero errors |

**Important**: BP2013 and BP2014 and BP2015 are **warnings**, not errors. Assert `sink.HasErrors == false` but `sink.All.Any(d => d.Code == "BP2013" && d.Severity == DiagnosticSeverity.Warning)`.

### File 2: `Hrot.Blueprints.Tests/Compiler/ReadEqsResultValidatorTests.cs`

| Test method | What it asserts |
|---|---|
| `Validate_LibraryDispatch_BP2020` | Library asset with ReadEqsResultNode emits BP2020 |
| `Validate_AiPrimitiveDispatch_BP2020` | AiPrimitive asset with ReadEqsResultNode emits BP2020 |
| `Validate_SensorVariableNotDeclared_BP2021` | ReadEqsResultNode with unknown SensorVariableName emits BP2021 |
| `Validate_ValidInstance_NoErrors` | Instance asset with ReadEqsResultNode and matching EqsSensorHandle variable emits zero errors |

### File 3: `Hrot.Blueprints.Tests/Compiler/SpawnEqsSensorValidatorTests.cs`

| Test method | What it asserts |
|---|---|
| `Validate_UnsupportedDispatch_BP2030` | Library/AiPrimitive/pure-function emits BP2030 |
| `Validate_TemplateNotFound_BP2031` | Known catalog provided; TemplateAssetId not in catalog → BP2031 |
| `Validate_NoCatalog_NoTemplateError` | EqsTemplates=null in options; any TemplateAssetId → NO BP2031 |
| `Validate_ValidInstance_WithCatalog_NoErrors` | TemplateAssetId in catalog; Instance dispatch → zero errors |

**For BP2031 tests**, construct a stub `IEqsTemplateCatalog` inline:
```csharp
private sealed class StubEqsTemplateCatalog(params Guid[] knownIds) : IEqsTemplateCatalog
{
    private readonly HashSet<Guid> _known = new(knownIds);
    public bool Contains(Guid assetId) => _known.Contains(assetId);
}
```
Pass it via `new CompileOptions(..., EqsTemplates: new StubEqsTemplateCatalog(knownTemplateGuid))`.

---

## Constraints

1. **Do NOT change lowering stages, IR types, or editor code.**
2. **Do NOT modify existing tests** except to update any that assert on BP2001/BP2002/BP2003 as Stage 3 codes (check if any test references these string codes directly — if so, update to BP3010/BP3011/BP3012).
3. The validator registration order in `Stage2_Validate.Validators` matters: add the three new validators **last** (after `V_DeterminismOrdering`). All dispatch-context checks should be independent of other validators.
4. BP2015 (WhenNode downstream of a Branch): if pin materialization happens in Stage 3 and pins are empty at Stage 2, **skip this diagnostic and add a TODO**. Note in the batch report.
5. BP2009 predicate tree check: only compile-guarded by `#if NET8_0_OR_GREATER` since `SearchPredicateDto` is net8.0-only. In the netstandard2.0 build, skip the check.

---

## Success Criteria

1. ✅ Stage 3 codes renamed: BP2001→BP3010, BP2002→BP3011 in both `DiagnosticCodes.cs` and `Stage3_Normalize.cs`
2. ✅ New diagnostic codes BP2001–BP2015, BP2020–BP2021, BP2030–BP2031 in `DiagnosticCodes.cs`
3. ✅ `IEqsTemplateCatalog` interface created; threaded through `CompileOptions` + `ValidationContext`
4. ✅ Three validators implemented and registered
5. ✅ All named test methods in the three test files pass
6. ✅ Pre-existing test suite passes (including Stage 3 tests that assert on old BP2001/BP2002 codes if any exist — update them)
7. ✅ Solution builds

---

## Files to Create or Modify

| File | Change |
|---|---|
| `Hrot.Blueprints.Compiler/Compiler/Diagnostics/DiagnosticCodes.cs` | Rename BP2001→BP3010, BP2002→BP3011, BP2003→BP3012; add BP2001–BP2031 |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage3_Normalize.cs` | Update BP2001→BP3010, BP2002→BP3011 references |
| `Hrot.Blueprints.Compiler/Compiler/Catalogs/IEqsTemplateCatalog.cs` | New file |
| `Hrot.Blueprints.Compiler/Compiler/CompileOptions.cs` | Add optional `EqsTemplates` parameter |
| `Hrot.Blueprints.Compiler/Compiler/Stages/ValidationContext.cs` | Add `EqsTemplates` property |
| `Hrot.Blueprints.Compiler/Compiler/Stages/Stage2_Validate.cs` | Add V_WhenNodeRules, V_ReadEqsResultNodeRules, V_SpawnEqsSensorNodeRules + register |
| `Hrot.Blueprints.Tests/Compiler/WhenNodeValidatorTests.cs` | New file |
| `Hrot.Blueprints.Tests/Compiler/ReadEqsResultValidatorTests.cs` | New file |
| `Hrot.Blueprints.Tests/Compiler/SpawnEqsSensorValidatorTests.cs` | New file |

---

## Run Tests

```
dotnet test "Hrot\Subsystems\Blueprints\Hrot.Blueprints.Tests" -c Debug --no-build
```

All pre-existing tests must still pass. Check if any test directly asserts the string
`"BP2001"`, `"BP2002"`, or `"BP2003"` as Stage 3 Normalize codes — search the test project
for these string literals and update to `"BP3010"`, `"BP3011"`, `"BP3012"` as needed.

---

## Batch Report

Write a file `.dev/blueprints-3-when-node/batches/WHEN-BATCH-02-REPORT.md` containing:

1. Summary of files changed
2. Test results (before/after counts)
3. List of which BP20xx diagnostics were implemented vs skipped with justification
4. Any deviations from the instructions with justification
