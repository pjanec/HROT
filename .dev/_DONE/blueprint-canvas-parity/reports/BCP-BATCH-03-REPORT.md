# BCP-BATCH-03 Report — Node pin enrichment (data/value pins)

## Implementation Summary

Enriched `NodePinSchema.GetCanonicalPins` so blueprint node kinds project the **real data
pins the compiler consumes**, not just exec. All pins remain editor projection-only (no
`.bp.json` / `Pin`-schema / `BlueprintJsonServices` change). Threading is additive and
null-safe (null resolver → prior exec-only behavior).

### Task 1 — ChannelCommandNode parameter pins (DYNAMIC)
- Threaded a resolver additively: `EditorSubsystem` → `BlueprintDocumentFactory.Build(..., channelCommands)`
  → `BlueprintGraphModel(..., channelCommands)` → new optional 4th param on
  `NodePinSchema.GetCanonicalPins(node, registry, asset, channelCommands)`.
- **Deviation (documented below): I threaded `IChannelCommandCatalog`, NOT `IActionSchemaExporter`.**
  Verification (see "Resolver APIs verified") proved the exporter cannot resolve channel-command
  params — its keys are AI-action method FQNs with *blackboard* DTOs, and channel params DTOs
  are plain structs that are never in the exporter. The channel catalog is the compiler's actual
  source of truth.
- For a `ChannelCommandNode`, the matching `ChannelCommandCatalogEntry` is found exactly the way
  `Stage2_Validate.V_ChannelCommandReferences` matches it:
  `LastSegment(entry.ChannelTypeFqn) == node.ChannelType && entry.Name == node.ActionId`.
  Its `ParamsTypeFqn` is resolved to a CLR `Type` across loaded assemblies and projected as:
  - one data-IN pin per public instance field/property when the params type is a decomposable
    struct/class (e.g. `AimAndFireParams` → `Target`, `CooldownSeconds`);
  - a single data-IN pin (named after the type's last segment) typed as the params FQN when the
    params type is a primitive/enum (e.g. `System.Int32` → one `Int32` pin) — Stage5 consumes
    channel-command data-IN pins by `(Name, value)`, so one value pin is meaningful.
  - Unknown action / null catalog / unresolvable type → exec-only, no throw.
- Wired the production singleton `BuiltInChannelCommandCatalog.Instance` at the `EditorSubsystem`
  Blueprint document-open call site (the same instance already used for node-drawer bootstrap).

### Task 2 — FunctionCallNode params + return (DYNAMIC, reflection)
- Resolve `TargetTypeId` by FQN across loaded assemblies (`Type.GetType` then an AppDomain scan),
  reflect the method named `MethodName`:
  - each parameter → a data-IN pin `(param.Name, param.Type.FullName)`, in **declaration order**
    (matches `Stage5_Schedule.ResolveAllDataInputs`, which is order-dependent);
  - non-void return → a single data-OUT pin named `Return`;
  - exec In/Out only when `!IsPure` (pure pins are pure data, per `Stage5` PureCall path).
- Not-found type/method → graceful fallback to the prior behavior (exec-only for non-pure, empty
  for pure). `ref` params are unwrapped via `GetElementType()`.

### Task 3 — static data pins the compiler consumes
- **LatentDelayNode:** `+ Duration` (System.Single) data-IN; keep exec In/Out.
  (`Stage5.BuildLatentDelayOp` reads the first non-exec data-IN as delay seconds.)
- **ScoreDecisionNode:** `+ WinningOptionId` (System.Byte) data-OUT; keep exec In/Out.
  (`Stage5` caches the score result on the out pin literally named `WinningOptionId`.)
- **ArrayGetNode:** `Array` (System.Object) data-IN **first**, `Index` (System.Int32) data-IN,
  `Element` (System.Object) data-OUT, + exec In/Out. Array is first because
  `Stage4_TypeResolve` uses the *first* non-exec data-IN as the array.
- **ArrayMakeNode:** element data-IN pins `"0"`,`"1"` typed from `ElementTypeId` (or System.Object),
  + `Array` data-OUT typed `ElementTypeId + "[]"`, + exec In/Out. Two element slots is a fixed
  default (dynamic element-count tracking is out of scope, as the spec notes).
- **BranchNode:** `+ Condition` (System.Boolean) data-IN. **Verified the compiler DOES consume it**
  — `Stage5.ScheduleBranchNode` reads the first non-exec data-IN as the branch condition (falls
  back to a `false` const when unconnected). Kept In/True/False exec.
- Cast / Literal / Get/SetVariable: left unchanged (already had data pins).

## Resolver APIs verified (cited findings)

- **`IActionSchemaExporter`** (`Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs`,
  `ActionSchemaExporter.cs`): `Lookup(fqn)` / `All` keyed by FQN built as
  `$"{method.DeclaringType.FullName}.{method.Name}"` (ActionSchemaExporter.cs:160-164). It only
  catalogs methods decorated with `[BTreeAction]`/`[HsmAction]`/`[SharedAiAction]` etc., and its
  `DtoType` is the method's first `ref` (blackboard) parameter. **It has no channel-command entries**,
  so `Lookup("{ChannelType}.{ActionId}")` always returns null — the hypothesised key format does
  not exist. Channel-command params DTOs (e.g. `AimAndFireParams`) are plain structs with no AI
  attributes and never appear in the exporter. → exporter is the wrong resolver here.
- **Channel command source of truth:** `ChannelCommandCatalogEntry(Name, ChannelTypeFqn, ActionId, ParamsTypeFqn)`
  (`Compiler/Catalogs/CatalogInterfaces.cs:51-52`); `IChannelCommandCatalog.GetEntries()`. Match rule
  from `Stage2_Validate.cs:474-476`: `LastSegment(e.ChannelTypeFqn)==node.ChannelType && e.Name==node.ActionId`.
  `Stage5_Schedule.cs:676-693` consumes channel-command data-IN pins by `(Name, value)`. The built-in
  catalog (`BuiltInChannelCommandCatalog`) currently lists 5 actions all with `ParamsTypeFqn="System.Int32"`
  → with that catalog ChannelCommand nodes get one `Int32` value pin each (primitive path).
- **FunctionCall:** `Stage5_Schedule.cs:635-653` (non-pure) / `:921-931` (pure) call
  `ResolveAllDataInputs` (`:1090-1096`, pin-declaration-order) and a single non-exec out pin for the
  return value. Confirms param-order pins + one `Return` out pin.
- **Delay/ScoreDecision/Array/Branch:** verified at `Stage5_Schedule.cs:801-821` (Delay first data-IN),
  `:765-785` (ScoreDecision out pin "WinningOptionId"), `Stage4_TypeResolve.cs:134-167` (Array first
  data-IN → array, first data-OUT → element/array), `Stage5_Schedule.cs:335-354` (Branch first data-IN
  → condition).

## Design Decisions
- ChannelCommand primitive-params path emits ONE typed value pin rather than zero, because the
  compiler treats each non-exec data-IN as a `(Name, value)` param and the built-in catalog's params
  are primitives — a single value pin is the faithful, wireable projection.
- `Array` placed before `Index` on ArrayGet and `"0"` before `Array` on ArrayMake because Stage4
  selects the *first* data-IN/OUT pin; order is load-bearing.

## Deviations
- **WHAT:** Threaded `IChannelCommandCatalog` instead of `IActionSchemaExporter` for ChannelCommand
  param resolution. **WHY:** verification proved the exporter cannot resolve channel-command params
  (different keying + different DTO domain); the channel catalog's `ParamsTypeFqn` is the compiler's
  actual source. **BENEFIT:** ChannelCommand pins are real and compiler-faithful instead of always
  empty. **RISK:** minimal — the param is optional/null-safe, the resolver is the same singleton the
  rest of the blueprint editor already uses, and nothing is persisted.

## Test Results
New tests: `Hrot.Blueprints.Tests/Host/NodePinSchemaEnrichmentTests.cs` — **13 passed / 0 failed**.
Each asserts real pin name + direction + type (not non-null):
- ChannelCommand: multi-field DTO → `Target`/`CooldownSeconds` data-IN + exec; primitive params →
  single `Int32` value pin; unknown action → exec-only no-throw; null catalog → exec-only.
- FunctionCall: non-pure → `value`(Int32)+`scale`(Single) data-IN in order, `Return`(Int32) data-OUT,
  exec In/Out; pure → no exec, params+Return present; void return → no Return pin; unknown type →
  graceful exec-only fallback.
- Delay → `Duration`(Single) IN; ScoreDecision → `WinningOptionId`(Byte) OUT; ArrayGet → `Array`/
  `Index`(Int32) IN (Array first) + `Element` OUT; ArrayMake → `0`/`1`(elem) IN + `Array`(elem[]) OUT;
  Branch → `Condition`(Boolean) IN + exec In/True/False.

Required suites:
| Suite | Result |
|---|---|
| `Hrot.Blueprints.Tests` (full) | 1117 passed / 10 failed / 8 skipped |
| — the 10 failures | exactly the pre-existing **DEBT-006** golden/snapshot/allocation set; **verified identical on a stash baseline** (stashed my 4 prod files → same 10 fail). Projection-only held. |
| — flaky perf | `WhenNode_ConditionMet_Under200ns` passed when re-run isolated (load flake). |
| Byte-stability + pin hydration | 38 passed / 0 failed (projection-only proven). |
| `Hrot.Editor.AiShared.Tests` | 761 / 0 |
| `Hrot.BTree.Editor.Tests` | 382 / 0 |
| `Hrot.Hsm.Editor.Tests` | 333 / 0 |
| `EditorSubsystemBoot` (ClusterRunner.Integration) | 10 / 0 |

Build: `dotnet build IOS-IG-SimHost.sln` → **0 compile errors**. The two touched projects
(`Hrot.Blueprints.Editor`, `Hrot.Editor`) build with **0 warnings**. (A transient
`apphost.exe`-locked `UnauthorizedAccessException` on the unrelated `Fdp.Core.Benchmarks` project
appeared once under parallel build IO contention and built cleanly on retry — not a code issue.)
Byte-stability + compiler golden unchanged. GizmoMap.Contracts untouched.

## Deferred (not faked)
- **ReadRankedResultNode:** output pins come from the referenced `UtilityDecisionDef` result schema;
  resolving them needs the decision asset loaded (load decision → result struct fields). Left as-is
  (`Array.Empty<Pin>()`).
- **Squad nodes** (PartitionElements / AssignRoles / AdvancePhase / AcquireSlot): by compiler design
  they have **no node pins** — inputs/outputs flow from working-state vars, not pins. Left exec-only.
- **(Branch Condition was NOT deferred** — verified compiler-consumed and added.)

## Developer Insights
- The batch's stated hypothesis (`IActionSchemaExporter.Lookup("{ChannelType}.{ActionId}")`) is
  factually wrong against the code; the instruction to "verify first" was the right call. The channel
  catalog is the correct, already-available resolver.
- With the current `BuiltInChannelCommandCatalog` (all `ParamsTypeFqn="System.Int32"`), ChannelCommand
  nodes get a single primitive value pin — the multi-field decomposition only lights up once the
  catalog points at richer DTO structs (e.g. `AimAndFireParams`). The decomposition path is fully
  implemented and tested via a stub catalog, so it works the moment the catalog is enriched (a
  follow-up data change, not a code change).
- ArrayMake's element-count is fixed at 2; a future enhancement could grow slots from the connected
  link count.

## Known Issues
- None introduced. The 10 DEBT-006 golden/snapshot failures are pre-existing and orthogonal.

## Suggested Commit Message
feat(blueprint-editor): project real data pins per node kind (ChannelCommand params, FunctionCall params/return, Delay/ScoreDecision/Array/Branch) — projection-only (BCP-BATCH-03)
