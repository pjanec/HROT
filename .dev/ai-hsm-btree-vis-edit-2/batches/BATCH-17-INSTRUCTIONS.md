# BATCH-17 — Generator symbol-check: incompatible bound method → diagnostic, never build break (CRITICAL GUARANTEE)

**Task:** TASK-BT-17. **One objective.** Implementer: **sonnet, end-to-end** (implement + build + test to green + report; do NOT commit — the lead reviews & commits).

## 🔒 Working agreement (MANDATORY)
- **NO cheating:** never exclude a file from compilation, suppress diagnostics, disable `TreatWarningsAsErrors` globally, weaken assertions, or stub a feature to dodge an error. If genuinely blocked, STOP and write the blocker in the report.
- **Finish end-to-end without asking:** build, run the named test projects, diagnose, fix, repeat **until `Failed: 0`** and the build is clean; then write the report.
- **Soundness is paramount:** a *false-pass* (treating an incompatible binding as valid) re-introduces the catastrophic build break. When in doubt, treat a binding as **invalid** (skip+warn) — never as valid.
- Tests must compile real/stub method symbols and assert actual behavior (a CSharpGeneratorDriver run's diagnostics + emitted sources). Litter-free. Report = diffs.

## 📋 Onboarding / context
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-17-REPORT.md`.
- The BTree generator (`Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs`) compiles every `*.btree.json` into `Hrot.AI.Behaviors`. Prior batches made **unbound** leaves (BT-12) and **cyclic** trees (BT-14) non-fatal (emit core throws → generator catches → `BTREE0002` Warning → asset skipped → build survives; `Hrot.AI.Behaviors.csproj` has `WarningsNotAsErrors=BTREE0002`).
- **Remaining hole:** a leaf bound to a method whose signature can't bind to the tree's blackboard/context (e.g. `Condition_TargetAliveAndVisible(ref FireAtTargetParams,…)` on a `BrainBlackboard` tree) still emits `.Action(Method,…)` / `.Condition(Method,…)` that the C# compiler rejects → **breaks the whole assembly build** (catastrophic; reachable via the Inspector binding picker, which is NOT filtered). The emit core (netstandard2.0) has no symbol info, so it can't detect this.
- **Fix:** the generator (which HAS the `Compilation`) validates each bound method's signature BEFORE emitting; an asset with any incompatible/unresolved bound leaf is **skipped + reported as BTREE0002** (mirroring BT-12/14), so the build NEVER breaks.

## 🎯 Required behavior
For each editor-owned BTree asset, **before emitting** its topology, validate every Action/Condition leaf **that is reachable from the entry** (use the same entry/child walk the emitter uses; reuse/mirror `BTreeEmitCore`'s `CheckNoCycles` traversal — do NOT re-introduce a cycle-overflow risk, the cycle guard already runs). For each such leaf **with a non-empty `MethodFqn`**:
- It is **VALID** iff `MethodFqn` resolves (in the `Compilation`) to a **public static method** that exactly matches the BTree builder's single-delegate `NodeLogicDelegate<TBB,TCtx>` shape, where `TBB` = the asset's `BlackboardTypeName` and `TCtx` = its `ContextTypeName`:
  - returns `Fbt.NodeStatus`,
  - has exactly 4 parameters: `(ref TBB, ref Fbt.BehaviorTreeState, ref TCtx, int)` — params 0,1,2 are `RefKind.Ref`; param types resolved via `SymbolEqualityComparer` against the types named by `BlackboardTypeName`/`ContextTypeName`/the FastBTree `BehaviorTreeState`; param 3 is `System.Int32`.
- **Everything else is INVALID** (skip+warn): method unresolved; wrong arity/param types/ref-kinds/return; DTO-param methods (param0 ≠ blackboard); `DelegateShape == ThreeParamReusable` / expression-target bindings (the reusable path is unsupported today — VE-DEBT-002 — so treat as invalid for now; this is the *safe* choice and prevents build breaks; add a one-line `// TODO VE-DEBT-002: support reusable/expression-target binding validation` note).
- **VERIFY the real signature first:** read the actual `NodeLogicDelegate<,>` delegate declaration in `FDP/ExtDeps/FastBTree/src/**` and the `BTreeBuilder.Action`/`.Condition` overloads, and match the REAL shape. If the real single-delegate overload differs from the above, follow the real code and note it. (Optionally, the most robust check: resolve the `NodeLogicDelegate`2`` delegate symbol, substitute TBB/TCtx, and compare the method against its `Invoke` signature — use this if straightforward.)

If **any** reachable bound leaf is invalid → the asset is invalid: **do not emit** its `.g.cs`/`.Registrar.g.cs`, and report a **`BTREE0002` Warning** (reuse `MakeCodegenWarningDiagnostic`) naming the asset + the offending method + reason. A valid asset emits normally. One invalid asset must NOT affect sibling assets.

## Implementation notes (generator wiring)
- Add `context.CompilationProvider` and `Combine` it with the existing AdditionalTexts provider, then `RegisterSourceOutput` over the combined value so `GenerateOneAsset` receives the `Compilation`. (Incrementality caveat: combining with the full compilation re-runs generation on code changes; acceptable for the small asset set — note it in the report. Do NOT attempt a fancy incremental-symbol extraction unless trivial.)
- Put the validation in the generator (it has the `Compilation`); the netstandard2.0 emit core stays symbol-free. You may add a small validator class in `Hrot.AiEditor.Generators`.
- Reuse the existing `catch`/diagnostic skip path where natural; the new check should produce the same "skip + BTREE0002" outcome as BT-12/14.

## 🧪 Tests (`Hrot.AiEditor.Generators.Tests`, mirror BATCH-12/14's `CSharpGeneratorDriver` tests)
Build test compilations that DEFINE the methods being referenced (either reference the real behavior types, or add stub `[BTreeAction]`/`[BTreeCondition]` methods + a stub blackboard/context type to the in-memory compilation — whatever the existing generator tests do).
- `Generator_IncompatibleBoundMethod_DtoParam_SkipsAndWarns_NoErrors`: an asset whose Action binds a method with a NON-blackboard first ref param (DTO-param) → generator emits **no source** for it, reports **BTREE0002 Warning**, and the run has **zero `Error` diagnostics** (build survives). Condition variant too.
- `Generator_UnresolvedMethod_SkipsAndWarns`: `MethodFqn` that doesn't resolve → BTREE0002 + skip + no Error.
- `Generator_CompatibleBoundMethod_EmitsNormally`: an asset binding a method matching `NodeLogicDelegate<TBB,TCtx>` (4-param, ref blackboard …) → emits `.g.cs` + `.Registrar.g.cs`, **no** BTREE0002.
- `Generator_IncompatibleAsset_DoesNotSuppressValidSibling`: one invalid + one valid asset → valid one still emits (fault isolation).
- `Generator_WrongArityOrReturn_IsInvalid`: a method with the blackboard first param but wrong arity/return → invalid (proves it's a real signature check, not just first-param).

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings for the committed (valid) assets (no BTREE0002 fires for them — they bind `Action_Wander`-style compatible methods).
- [ ] **`Failed: 0`** in `Hrot.AiEditor.Generators.Tests`, `Hrot.AiEditor.Persistence.Tests`, `Hrot.BTree.Editor.Tests`. (Pre-existing `Generators.Tests` MigrationEquivalence ×2 may remain — list them explicitly and confirm they are unchanged/pre-existing by stashing if unsure.)
- [ ] A bound-but-incompatible (or unresolved) method → asset skipped + BTREE0002 + **zero Error diagnostics** in the generator output (the guarantee). Compatible methods emit normally. Sibling isolation holds.
- [ ] No global TWAE change; no excluded files; no suppressed diagnostics.
- [ ] Report written: the exact compatibility rule implemented, the `NodeLogicDelegate` signature you matched against (cite the file), per-project test counts, and the incrementality note.

## ⚠️ Pitfalls / soundness
- The committed `CombatShowcase.btree.json` binds `Action_Wander` (a real 4-param `NodeLogicDelegate` method) → MUST remain VALID (emit normally), else the showcase stops working. Verify this specific asset still emits after your change.
- A sandbox MSBuild quirk (MSB1025 in `ClearCacheDirectory`) can appear when a *new* `.btree.json` is added to the working tree during a full build; recover with `dotnet build-server shutdown`. It is unrelated to code — do not chase it; rely on the `CSharpGeneratorDriver` unit tests for the "no Error diagnostics" proof.
- Do NOT touch the Inspector picker (BB1), the emit core's emit logic, or BT-13's palette filter.
