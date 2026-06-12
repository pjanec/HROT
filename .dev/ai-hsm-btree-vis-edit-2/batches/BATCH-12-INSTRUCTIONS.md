# BATCH-12 — Fault-tolerant codegen: invalid asset → diagnostic, not build break (CRITICAL)

**Task:** TASK-BT-12 (Fix A — the editor must always launch even when an asset is mid-edit/invalid). **One objective.**

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating** (no excluding files / suppressing diagnostics / weakening tests); **finish without asking** until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.

## 📋 Onboarding
- Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-12-REPORT.md`.
- Context: the BTree generator compiles every `*.btree.json` into the game assembly. An **unbound** Action/Condition leaf (no method — what dropping a generic "Action"/"Condition" from the palette produces) currently makes `BTreeEmitCore` emit `.Action(visualId: …)` / `.Condition(visualId: …)` — calls with **no matching builder overload** → CS compile error → the WHOLE `Hrot.AI.Behaviors` assembly fails → the editor can't launch from sources. This must become a per-asset **diagnostic**, never a build break (JSON DD §1: "compile failures become diagnostics, not lost work").

## 🎯 Objective
Make the BTree codegen **fault-tolerant per asset**: an asset that cannot emit valid topology is **skipped** (no `.g.cs`/`.Registrar.g.cs`) and reported as a **non-build-breaking diagnostic** — the assembly still builds, other assets are unaffected, and the editor launches.

## Files (exact)
1. **`Hrot/Subsystems/AI/Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs`** — in `EmitAction` and `EmitCondition`, replace the current "emit a call with no method" path for the unbound case with a **throw**:
   - `EmitAction` (currently lines ~439-443): when `node.Action == null` **OR** `string.IsNullOrEmpty(node.Action.MethodFqn)` → `throw new InvalidOperationException($"Action node {node.VisualId:D} is unbound (no method) — bind a method in the editor.");`
   - `EmitCondition` (currently lines ~465-469): symmetric for `node.Condition`.
   - (These methods are only invoked for nodes reachable from the entry, so a disconnected/in-progress unbound node that isn't wired in does NOT throw — only emitted ones do. Do not change the reachability/walk logic.)
   - Do NOT change `EmitWait`/`EmitSubtree` (they already emit valid calls for null payloads).
2. **`Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeJsonGenerator.cs`** — the `catch (Exception ex)` around `EmitTopologyCore` (lines ~67-76) already skips `AddSource` (so neither `.g.cs` nor the bridge is emitted for that asset — good). **Change the reported diagnostic for the emit/codegen-failure path to a NON-build-breaking severity (`DiagnosticSeverity.Warning`)** so a single invalid asset does not fail the build. Add a distinct descriptor, e.g. id `BTREE0002`, title "BTree asset skipped (codegen validation)", messageFormat "Skipped '{0}': {1}. Fix the asset in the editor.", category "BTreeJsonGenerator", severity Warning. Use it for the `EmitTopologyCore` and bridge codegen-failure catches. (Leave the deserialize-failure path as-is OR also make it Warning — your call, but the emit-failure path MUST be Warning.)
   - **Verify**: confirm `Hrot.AI.Behaviors.csproj` (and Directory.Build.props it inherits) does NOT have `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` that would escalate BTREE0002 back into a build break. If it does, report it (do NOT globally disable TWAE) — we'll decide; but first check, because the whole point is the build must survive.

## 🧪 Tests (`Hrot.AiEditor.Generators.Tests`, mirror existing `BTreeJsonGeneratorTests` which use `CSharpGeneratorDriver`)
- `Generator_UnboundActionAsset_DoesNotEmitSource_AndReportsWarning`: a `.btree.json` (as AdditionalText) whose **reachable** tree contains an Action leaf with **no Action payload** → after running the generator driver: (a) **no `Combat*`/asset `.g.cs` source is added** for it (or whatever the existing tests assert for "skipped"); (b) a diagnostic with id `BTREE0002` and **severity Warning** is reported.
- `Generator_UnboundActionAsset_OutputCompilation_HasNoErrors`: **the resulting compilation has zero `DiagnosticSeverity.Error` diagnostics** (the build survives — this is the core guarantee). Assert `driver.GetRunResult()` / the output compilation `.GetDiagnostics()` contains no `Error`.
- `Generator_UnboundConditionAsset_*`: symmetric for an unbound Condition.
- `Generator_ValidAsset_EmitsTopologyAndBridge_NoWarning`: a fully-bound valid asset (e.g. a Root→Wait tree, or Root→Action with a valid 4-param method) emits the normal `.g.cs` + `.Registrar.g.cs` and reports **no** BTREE0002.
- Also add an emit-core unit test (`Hrot.AiEditor.Persistence.Tests`): `BTreeEmitCore.EmitTopologyCore(dtoWithReachableUnboundAction)` **throws `InvalidOperationException`**; and with a disconnected (non-reachable) unbound node it does **not** throw.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings introduced into existing assets (the real committed assets are all valid, so no BTREE0002 fires for them).
- [ ] **Failed: 0** in `Hrot.AiEditor.Generators.Tests`, `Hrot.AiEditor.Persistence.Tests`, `Hrot.BTree.Editor.Tests`.
- [ ] An asset with a reachable unbound Action/Condition → generator skips it + emits a **Warning** (BTREE0002); the output compilation has **no errors** (verified by test).
- [ ] Valid assets unchanged (normal emission).
- [ ] Report written: include whether `TreatWarningsAsErrors` is set for `Hrot.AI.Behaviors` (and if so, flag it).

## Notes
- Do NOT attempt to fix the *DTO-param incompatible binding* case here (that's BT-13 / VE-DEBT-002) — BT-12 is specifically the unbound-leaf + diagnostic-not-error safety net.
- The generator catch already skips emission; the key changes are (1) emit core THROWS on unbound emitted leaf, (2) the diagnostic is Warning not Error.
- Keep the existing deserialize-failure behavior working (don't regress the existing generator tests; rebaseline severity assertions only if you intentionally change them, and say so).
