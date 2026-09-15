# BATCH-02 — Task 3: node value pins for all node kinds (compiler-grounded)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, run implement→build→test→fix to green before reporting.
> **Codebase Memory MCP first** (`search_graph`/`get_code_snippet`/`trace_path`). Project:
> `D-Work-IOS-IG-SimHost-FDP-2`. Do **not** use `search_code`/grep the whole tree. Use Read for exact content.

## Mission

The user prioritized "node value pins for all kinds." A compiler-grounded audit (lead-verified) found
that **almost every node kind is already correct** — `NodePinSchema.GetCanonicalPins`
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Host/NodePinSchema.cs`) already projects the data
pins the compiler consumes, and the kinds that are exec-only are exec-only **because the compiler reads
their config from node fields, not pins** (verified: Stage5 has no pin reads for them). Adding data pins
to those would be misleading and violate the schema's invariant ("a projected data pin must be something
the compiler actually consumes").

**There are exactly THREE real gaps.** Fix only these. Do NOT add data pins to any other kind.

### The invariant (inviolable)
A projected data/value pin MUST correspond to something the compiler reads from a pin (verified against
Stage2_Validate / Stage4_TypeResolve / Stage5_Schedule). Pins are **projection-only**: never persisted to
the asset or `.bp.json`. The byte-stability test and compiler golden/snapshot tests must stay green
(NodePinSchema is editor-side projection; it is NOT on the compiler codegen path, so goldens must not move).

---

## Gap 1 — `ReadRankedResultNode` (currently `Array.Empty<Pin>()`)

**Compiler proof:** `Stage5_Schedule.cs:1049-1062` iterates the node's data-OUT pins **by name** and emits
`IrOp_FieldRead(helperResult2, outPin.Name, fieldType)` for each. With `Array.Empty` no field reads are
ever emitted → the node produces no usable output.

**The OUT pin NAMES must match the emitted struct field names**, because `IrOp_FieldRead` reads by name.
The emitter (`InstanceEmitter.cs:539-562`) declares the result struct as:
```
public bool  IsValid;
public long  Entity;
public float Score;
```
and assigns `result.IsValid = isValid; result.Entity = handle; result.Score = score;`.

So replace the `ReadRankedResultNode => Array.Empty<Pin>()` arm with a helper returning **three data-OUT pins**:
- `IsValid` — Out — `System.Boolean`
- `Entity`  — Out — `System.Int64`   ← the candidate handle is stored in the `Entity` field; the pin MUST be named `Entity`
- `Score`   — Out — `System.Single`

No data-IN pin (`Rank` is a node field, baked at compile time — `Stage5_Schedule.cs:1039`).
**Verify-first:** open `InstanceEmitter.cs` around lines 529-565 and confirm the exact struct field names
before naming the pins. If they differ from the above, use the actual field names and note it in the report.

## Gap 2 — `CallCustomEventNode` (currently `ExecInOut()`)

**Compiler proof:** `Stage5_Schedule.cs:695-703`: `ResolveAllDataInputs(node, stmts)` consumes **every**
non-exec data-IN pin positionally and maps them to the raised custom event's parameters
(`IrOp_RaiseCustomEvent(idx, inputVals)`). With `ExecInOut()` the event is always raised with zero args.

`FindCustomEventIndex(cce.EventId)` (`Stage5_Schedule.cs:1154+`) parses `EventId` as a Guid and matches
`asset.CustomEvents` by `Id`. The asset model (`Assets/Declarations.cs:33-38`): `CustomEventDecl { Guid Id;
string Name; List<ParameterDecl> Parameters }`; `ParameterDecl { string Name; BlueprintTypeRef Type; ... }`
(`Declarations.cs:16-24`); `BlueprintTypeRef.TypeId` is the CLR FQN string.

Add a `CallCustomEventPins(CallCustomEventNode cce, BlueprintAsset? asset)` helper. `GetCanonicalPins`
already receives `asset` — thread it in. Behavior:
- exec In + exec Out, then
- one data-IN pin per parameter **in declaration order**: `MakeData(param.Name, "In", typeId)` where
  `typeId = string.IsNullOrEmpty(param.Type?.TypeId) ? "System.Object" : param.Type.TypeId`.
- **Graceful fallback to exec-only** (`ExecInOut()`) when: `asset == null`, `cce.EventId` does not parse to
  a Guid, no matching `CustomEventDecl`, or it has zero parameters. (Mirror the FunctionCall graceful-degrade
  pattern at `NodePinSchema.cs:266-291`.)
- Verify the exact match key used by `FindCustomEventIndex` (Id vs Name) and mirror it; cite the line.

## Gap 3 — `CallPeerBlueprintNode` (currently `ExecInOut()`)

**Compiler proof:** `Stage5_Schedule.cs:656-673`: the compiler reads
`outPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out")` and caches the peer function's
**return value** on it; it also consumes all data-IN pins via `ResolveAllDataInputs` as the call arguments.

For THIS batch, add only the **static** part that needs no peer-signature resolution:
- a `CallPeerBlueprintPins()` helper returning exec In + exec Out + a single `Return` data-OUT pin
  (`MakeData("Return", "Out", "System.Object")`). The compiler always reads the first data-OUT pin as the
  return slot (Stage5:661-672); System.Object is a safe wildcard resolved by Stage4 from incident links.
- **DEFER** the dynamic data-IN argument pins (one per peer function parameter) to BATCH-03, because they
  require resolving the peer blueprint's exported function signature (the graph-signature work). Add a
  short `// TODO(BATCH-03): ...` comment citing Stage5:660 and explaining the deferral. Do NOT attempt
  signature resolution here.

---

## Implementation notes

- All edits are in `NodePinSchema.cs` only (plus tests). Use the existing `MakeExec`/`MakeData` factories.
- Update the Pass-2 `switch` arms:
  - `ReadRankedResultNode => ReadRankedResultPins(),`
  - `CallCustomEventNode cce => CallCustomEventPins(cce, asset),`
  - `CallPeerBlueprintNode => CallPeerBlueprintPins(),`
- Add XML-doc on each new helper citing the exact compiler line it is grounded in (match the style of the
  existing helpers, e.g. `BranchPins`/`LatentDelayPins`).
- Do NOT touch the other kinds (`CallEventDispatcher`, `BindEventDispatcher`, `WaitForEvent`,
  `WaitForChannel`, `PartitionElements`, `AssignRoles`, `AdvancePhase`, `AcquireSlot`, `When`,
  `ReadEqsResult`, `SpawnEqsSensor`, `ScoreDecision`) — they are verified correct.

## Tests

Find the existing NodePinSchema test file (search the test tree for `NodePinSchema` / `GetCanonicalPins`
usages; likely under `Hrot.Blueprints.Tests/Editor/` or `Host/`). Add `[Fact]`s:
- `ReadRankedResultNode` → exactly 3 data-OUT pins named `IsValid`/`Entity`/`Score` with the right types, 0 exec, 0 data-IN.
- `CallCustomEventNode` with an asset declaring a custom event with N typed parameters → exec In/Out + N data-IN
  pins in declaration order with the declared types; AND a graceful-fallback case (asset null or event not
  found → exec-only).
- `CallPeerBlueprintNode` → exec In/Out + one `Return` data-OUT (System.Object).
If no NodePinSchema test file exists, create `NodePinSchemaTests.cs` next to the other editor tests.
Build a `BlueprintAsset` with `CustomEvents` inline for the CallCustomEvent test (no disk fixture).

## Verification (reach green before reporting — paste real output)

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects
   (`Hrot.Blueprints.Editor`, `Hrot.Blueprints.Tests`).
2. New NodePinSchema tests → green.
3. Full `Hrot.Blueprints.Tests` → only the **10 pre-existing DEBT-006** golden/snapshot failures (0 new).
   **Critically confirm the golden/snapshot count did NOT change** — this proves the projection-only invariant
   held (NodePinSchema is not on the codegen path). If any golden newly fails, STOP and report — do not
   regenerate goldens in this batch.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report

Write `.dev/_DONE/blueprint-finalize/reports/BATCH-02-REPORT.md`: the 3 helpers added (file:line), the exact
compiler line each is grounded in, confirmation of the ReadRankedResult struct field names you verified,
the deferred CallPeerBlueprint args (BATCH-03), test names + real output, and confirmation that the
golden/snapshot failure count was unchanged (0 new). **Do not commit** — the lead reviews and commits.
