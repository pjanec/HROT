# BATCH-03C2 — Editor: CallPeerBlueprint arg pins via extended BlueprintSignature

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.

## Mission

Resolve the BATCH-02 deferral: project a `CallPeerBlueprintNode`'s **argument data-IN pins** (one per peer
function parameter) and a typed **Return** data-OUT pin by reading the peer blueprint's exported function
signature. Today `BlueprintSignature.ExportedFunctionNames` is **names-only**, and sibling signatures are
**not available** at editor projection time. This batch enriches the signature and threads a lookup to the
projection.

The compiler already consumes a `CallPeerBlueprintNode`'s data-IN pins **positionally** as call args and the
first data-OUT as the return (`Stage5_Schedule.cs:656-673`); BATCH-02 added a static `Return` data-OUT pin.
This batch makes the editor project the *correct* arg pins so the wires are meaningful.

## Read first (verify the exact shapes before changing)
- `Hrot.Blueprints.Compiler/Compiler/BlueprintSignature.cs` — the `BlueprintSignature` record
  (`ExportedFunctionNames: IReadOnlyList<string>`, positional ctor). Find EVERY construction site.
- `Hrot.Blueprints.Editor/Reload/BlueprintSignatureBuilder.cs` (`FromInMemoryAsset`, ~15-34) — projects
  `g.Name` for `GraphKind.Function` graphs.
- `Hrot.Blueprints.Compiler/Compiler/BlueprintSignatureParser.cs` (`ParseExportedFunctions`, ~75-87) —
  parses only `"name"` from each Function graph JSON element. Note the JSON field names for graph
  `inputs`/`outputs`/`type` (match the `.bp.json` schema — verify against a real fixture).
- All consumers of `ExportedFunctionNames` (compiler peer-ref validation, tests' `MakeSiblingSignature`).
- `Host/NodePinSchema.cs` `GetCanonicalPins` (now has `containingGraph` from BATCH-03C) and the
  `CallPeerBlueprintNode` arm (BATCH-02 added a static `Return` data-OUT via `CallPeerBlueprintPins`).
- `Host/BlueprintGraphModel.cs` ctor + `Rebuild` call site; `Host/BlueprintDocumentFactory.cs`
  (where `BlueprintGraphModel` is constructed — ~107). Determine what asset-catalog access the factory has
  to BUILD sibling signatures (mirror `QuickReloadService.BuildSiblingSignatures`).
- `Assets/Nodes.cs` `CallPeerBlueprintNode` (`PeerBlueprintId` GUID string, `FunctionRef` name).

## Changes

### 1. Enrich `BlueprintSignature` (shared compiler/editor contract — change carefully)
Add a per-exported-function signature carrying parameter + return types. Suggested shapes:
```
public sealed record BlueprintFunctionSig(string Name,
    IReadOnlyList<BlueprintParamSig> Inputs,
    IReadOnlyList<BlueprintParamSig> Outputs);
public sealed record BlueprintParamSig(string Name, string TypeId);
```
Add `IReadOnlyList<BlueprintFunctionSig> ExportedFunctions` to `BlueprintSignature`. **Keep
`ExportedFunctionNames` working** — make it a computed member derived from `ExportedFunctions`
(`ExportedFunctions.Select(f => f.Name)`), or update every consumer + construction site. Pick whichever
keeps the diff smallest and ALL existing callers/tests compiling. Update EVERY construction site
(builder, parser, test helpers like `MakeSiblingSignature`) — do not leave a broken positional ctor.

### 2. `BlueprintSignatureBuilder.FromInMemoryAsset`
Project each `GraphKind.Function` graph's `Inputs`/`Outputs` (`ParameterDecl` → `BlueprintParamSig`
using `Name` + `Type.TypeId`) into `ExportedFunctions`.

### 3. `BlueprintSignatureParser.ParseExportedFunctions`
Parse each Function graph's `inputs`/`outputs` arrays (name + type id) from the `.bp.json`. Verify the
exact JSON property names against a real fixture and the in-memory builder so disk-parse and in-memory
agree. Graceful: missing arrays → empty lists.

### 4. Thread a sibling-signature lookup to the projection
Add an optional lookup param to `NodePinSchema.GetCanonicalPins`, e.g.
`Func<Guid, BlueprintSignature?>? peerSignatureLookup = null` (a delegate is lighter than pre-building a
dict). Add the same to `BlueprintGraphModel` (ctor field) and pass it through at the `Rebuild` call site.
`BlueprintCommandSink` and `BlueprintNodeCatalog` pass `null` (graceful). In `BlueprintDocumentFactory`,
supply a lookup that resolves a peer asset's `BlueprintSignature` from the editor's asset catalog (build
via `BlueprintSignatureBuilder.FromInMemoryAsset`, or parse from disk for catalog assets — mirror
`QuickReloadService.BuildSiblingSignatures`). If wiring the live lookup requires more than a small
pass-through in the factory/EditorSubsystem, implement the projection + param + a catalog-backed lookup in
the factory, and if any deeper composition change is needed, STOP and report it rather than expanding scope.

### 5. Project `CallPeerBlueprintNode` pins
Replace the BATCH-02 static `CallPeerBlueprintPins()` with a signature-aware projection:
- if `peerSignatureLookup != null` and `PeerBlueprintId` parses and the peer sig is found and it has a
  `BlueprintFunctionSig` whose `Name == FunctionRef`:
  exec `In`/`Out` + one data-IN per the function's `Inputs` (named per input, typed from its TypeId,
  declaration order — positional contract) + one data-OUT `Return` typed from `Outputs[0]` (or
  `System.Object` if no outputs / unresolved). Remove the TODO(BATCH-03) comment.
- else: fall back to the current static `exec In/Out + Return:System.Object` (no throw). Keep this exact
  fallback so existing behavior holds when no lookup/peer is available.

## Tests
Extend `NodePinSchemaEnrichmentTests.cs`:
- CallPeerBlueprint with a stub `peerSignatureLookup` returning a `BlueprintSignature` whose
  `ExportedFunctions` has the matching `FunctionRef` with 2 typed inputs + 1 output → exec In/Out + 2
  data-IN (names/types/order) + `Return` data-OUT typed from the output.
- No lookup / unknown peer / unknown FunctionRef → static fallback (exec In/Out + `Return:System.Object`).
- `BlueprintSignatureBuilder.FromInMemoryAsset` on an asset with a Function graph (2 inputs, 1 output)
  → `ExportedFunctions` populated with correct names/types; `ExportedFunctionNames` still correct.
- If practical, a `BlueprintSignatureParser` round-trip test (parse a `.bp.json` snippet → same
  ExportedFunctions as the in-memory builder). If the parser test needs a real fixture, reuse an existing one.

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects. (The signature
   change touches the compiler too — confirm the whole solution builds.)
2. New + existing tests green (NodePinSchema + any BlueprintSignature/peer-ref tests).
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7**, 0 new, no golden changed.
   Run any `Fdp.Toolkits.Tests`/peer-ref suites that touch BlueprintSignature and keep them green.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/_DONE/blueprint-finalize/reports/BATCH-03C2-REPORT.md`: the signature change + every construction site
updated (file:line), builder/parser changes, the lookup threading (and exactly how the factory supplies
it), the CallPeerBlueprint projection, test names + output, full-suite classification, and any composition
wiring you had to touch (or flagged as needing discussion). **Do not commit** — lead reviews/commits.
