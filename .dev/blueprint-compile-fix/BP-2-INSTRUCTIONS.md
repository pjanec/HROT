# BP-2: Compiler-side pin rehydration (pins-empty path) — the keystone
**Goal:** Make the blueprint COMPILER self-sufficient when nodes are saved projection-only (`"Pins": []`).
A new `Stage0_Rehydrate` pre-pass populates every node's `Pins` from an authoritative node registry +
the asset's authored state + the incident links, so the graph compiles **connected** and executes. This
fixes "Count stays 0" for Quick Reload (and, once BP-3 lands, for MSBuild) — both use this compiler.

**Architect-confirmed design (authoritative):**
- `BuiltInNodeRegistry` (`INodeRegistry`) is the single authoritative source of **static** node pin shapes.
- Projection-only stripping stays a strict invariant — never leak render pins into the `.bp.json`.
- **Dynamic** pins are computed from authored state: `EventEntry`←`Graph.Inputs`, `Return`←`Graph.Outputs`,
  `Get/SetVariable` type ← `asset.Variables`, `FunctionGraphCall`←`asset.Graphs`, `CallCustomEvent`←
  `asset.CustomEvents`, `CallPeerBlueprint`←`SiblingSignatures`.
- (NodePinSchema unification is a SEPARATE later batch BP-4 — do NOT touch the editor's NodePinSchema here.)

## Lead-verified seams (re-verify, cite). All file:line confirmed via research.
- **Insertion point:** `BlueprintCompiler.Compile` (`Hrot.Blueprints.Compiler/Compiler/BlueprintCompiler.cs`)
  invokes Stage2 at `:28`, Stage3 `:32`, Stage4 `:36`, Stage5 `:40`, Stage6 `:44`, Stage7 `:48` on a
  pre-parsed `BlueprintAsset`. Insert **`Stage0_Rehydrate.Run(asset, options)` as the FIRST line of `Compile`,
  before `Stage2_Validate.Run` (`:28`)**. The `BlueprintAsset`/`Node.Pins` are mutable (`List<Pin>`), so
  populate in place.
- **Connection resolution is 100% pin-ID-driven** (NO name/role fallback): Stage5 resolves every link by
  `link.FromPinId == pin.Id` / `link.ToPinId == pin.Id` (`Stage5_Schedule.cs:908,930,1231,1240`; Stage4
  `Stage4_TypeResolve.cs:176-177`). **Therefore rehydration MUST assign the LINK GUIDs to the corresponding
  pins** — fresh GUIDs would leave every wire dangling (→ empty Tick again).
- **Link-GUID assignment algorithm to replicate** (from `Hrot.Blueprints.Editor/Host/BlueprintGraphModel.cs`
  ~`:181-228`, the slow/`Pins:[]` path): per node, split canonical pins into Out and In buckets (in
  declaration order); collect distinct `FromPinId` GUIDs from outgoing links (in first-occurrence order) and
  distinct `ToPinId` from incoming links; assign the i-th distinct GUID to the i-th pin in that direction
  bucket; pins with no link get a **deterministic** synthetic GUID (e.g. `IdGenerator.Deterministic($"pin:{node.Id:N}:{pin.Name}:{dir}")`
  or the `Stage3_Normalize.SynthesizedGuid` helper). Fan-out shares one `FromPinId` (dedupe handles it).
  **The canonical pin order MUST be stable + match authoring order** (same order NodePinSchema produces) or
  the positional assignment mismaps — keep the exact pin order from the NodePinSchema cases below.
- **`INodeRegistry` is an empty stub** (`Compiler/Catalogs/INodeRegistry.cs` — `// Populated in TASK-CP-005`);
  `BuiltInNodeRegistry` likewise. `options.NodeRegistry` is threaded into `ValidationContext` but **never read
  today** (dead) — you are wiring its first real consumer.
- **Per-kind pin shapes** are currently in `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` (the reference to
  port — do NOT reference it from the compiler; the editor is net8-only). The static shapes to encode in
  `BuiltInNodeRegistry.GetStaticPins`:
  - `Branch`: exec-In "In", exec-Out "True", exec-Out "False", data-In "Condition"/System.Boolean (`BranchPins` L331)
  - `Sequence`: exec-In "In", exec-Out "Then0", exec-Out "Then1" (`SequencePins` L340)
  - `Literal`: data-Out "Value" / `LiteralNode.TypeId` (L584)
  - `Cast`: exec-In/Out + data-In "In"/System.Object + data-Out "Out"/`CastNode.TargetTypeId` (L590)
  - `Get/SetVariable`, `EventEntry`, `Return`, `FunctionCall` = **dynamic** (see below)
  - exec-In/Out-only kinds (WaitForChannel/WaitForEvent/etc.) via an `ExecInOut()` equivalent.
- **Dynamic pin derivation** (compiler has all inputs already — `asset.Variables`, `asset.CustomEvents`,
  `asset.Graphs`, `graph.Inputs/Outputs`, `options.SiblingSignatures`):
  - `EventEntryNode`: exec-Out "Out" + one data-Out per `graph.Inputs[i]` (Name, Type.TypeId|System.Object). Ref `EventEntryNodePins` L205.
  - `ReturnNode`: exec-In "In" + (if `graph.Outputs.Count>0`) data-Out from `graph.Outputs[0]`. Ref `ReturnNodePins` L241.
  - `GetVariableNode`: data-Out "Value" typed via `ResolveVariableTypeId(VariableId, asset)` (look up `asset.Variables` by Guid; strip `var:` prefix). Ref L155-175,569.
  - `SetVariableNode`: exec-In/Out + data-In "Value" + data-Out "Value", both typed from the variable. Ref L575.
  - `FunctionCallNode`: **CLR path** (`TargetTypeId`+`MethodName`) → exec-In/Out (unless `IsPure`) + data-In per method param + data-Out "Return" if non-void (reflection — `FunctionCallPins` L542). **Graph path** (`TargetGraphId`) → from the target graph's Inputs/Outputs (`FunctionGraphCallPins` L299).
  - (Other kinds — ChannelCommand/CallCustomEvent/CallPeerBlueprint/WhenNode/etc.: port their shapes too; **WhenNode exec pin NAMES are load-bearing** — `Stage5_Schedule.cs:610-617` matches `OnFired` etc. by name.)

## Tasks (sequence; build+test after each)

### Task 1 — `INodeRegistry` contract + `BuiltInNodeRegistry` static shapes
Extend `INodeRegistry` with a method returning the canonical ordered **static** pin shapes for a node:
```csharp
public interface INodeRegistry
{
    // Canonical, ordered, GUID-less pin shapes for a node's STATIC structure.
    // Dynamic kinds (EventEntry/Return/variable/FunctionCall) return what is statically known
    // (often empty/exec-only); the rehydration pass enriches them from authored state.
    IReadOnlyList<PinSchema> GetStaticPins(Node node);
}
public readonly record struct PinSchema(string Name, string Direction, bool IsExec, string TypeId);
```
Implement `BuiltInNodeRegistry.GetStaticPins` with the static shapes listed above. Keep pin order identical
to the NodePinSchema cases (order is load-bearing for the link-GUID positional assignment).

### Task 2 — `Stage0_Rehydrate` (NEW) — `Hrot.Blueprints.Compiler/Compiler/Stages/Stage0_Rehydrate.cs`
`public static void Run(BlueprintAsset asset, CompileOptions options)`. For each graph, for each node whose
`Pins` is **empty** (skip nodes that already have pins — e.g. test fixtures with authored pins, and Stage3's
cast nodes are added later so N/A here):
1. Compute the **canonical ordered pin list** = static shapes (`options.NodeRegistry.GetStaticPins(node)`)
   merged with **dynamic** enrichment per the rules above (EventEntry/Return/variable/FunctionCall/graph-call/
   custom-event/peer using `asset` + `graph` + `options.SiblingSignatures`). Produce real `Pin` objects
   (Name/Direction/IsExec/TypeRef) **without ids yet**, in stable order.
2. **Assign link GUIDs** using the BlueprintGraphModel positional-within-direction-bucket algorithm
   (Out pins ← distinct `FromPinId` from this node's outgoing links in order; In pins ← distinct `ToPinId`
   from incoming links; leftover pins ← deterministic synthetic GUID). Set `pin.Id` accordingly.
3. Assign to `node.Pins`.
**No-swallow rule:** if a node's pins cannot be determined (e.g. CLR `FunctionCall` whose target type/method
can't be resolved — possible in the netstandard2.0 MSBuild host where the game assembly isn't loaded), do
NOT silently leave it pinless. Emit a diagnostic (a new `BP00xx` info/warning via the sink, or log) naming the
node + reason, and fall back to exec-only. (Reflection WILL resolve in the net8 editor/test; the MSBuild
gap is acceptable for now and tracked for a later registry-driven FunctionCall.)

### Task 3 — wire into `BlueprintCompiler.Compile`
Add `Stage0_Rehydrate.Run(asset, options);` as the first statement of `Compile`, before `Stage2_Validate.Run`.
(Confirm `Validate()` and the generator's compile path both flow through `Compile`, so both benefit.)

## Success Criteria
- [ ] **Keystone test:** take CountingDemo, **strip all node Pins to `[]`** (mimic a saved/reloaded blueprint),
      run it through the FULL compiler, load + tick — assert it behaves identically to the pins-populated
      CountingDemo (Count climbs to 5 after 5 ticks). Mirror `CountingDemo_ProofTests`. This is the proof
      that rehydration produces a CONNECTED graph. + a focused Stage0_Rehydrate unit test (a small 2-node
      linked graph with `Pins:[]` → after Run, both pins carry the link's GUIDs and `node.Pins` populated).
- [ ] `INodeRegistry.GetStaticPins` returns correct ordered shapes for Branch/Sequence/Literal/Cast (+ exec-only kinds).
- [ ] Existing suites stay green: `Hrot.Blueprints.Tests` (only the 7 pre-existing DEBT-006 fail; 0 new),
      `Hrot.Blueprints.Compiler` tests, `CountingDemo_ProofTests` 2/2.
- [ ] Build `IOS-IG-SimHost.sln` 0 errors / 0 new warnings. Report exact counts.
- [ ] Report → `.dev/blueprint-compile-fix/BP-2-REPORT.md`.

## Report Requirements
The PinSchema/INodeRegistry contract; the Stage0_Rehydrate algorithm (static+dynamic+link-GUID assignment) and
how it mirrors BlueprintGraphModel; how each dynamic kind is derived; the no-swallow fallback for unresolvable
FunctionCall (+ the MSBuild-reflection caveat); confirmation it's wired before Stage2; the pins-stripped
CountingDemo proof result; exact test/build counts; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. Do NOT modify the editor's `NodePinSchema` (that's BP-4). Do NOT change the
projection-only save (stripping stays). Do NOT touch the generator deserialization (that's BP-3). Compiler must
stay `netstandard2.0`-compatible (no net8-only APIs in the rehydration path). Do NOT commit (the lead commits).
The user has uncommitted New-from-Recipe WIP (RecipeCreateModal, AssetBrowserWindow, EditorSubsystem) — do NOT
touch or revert those files.
