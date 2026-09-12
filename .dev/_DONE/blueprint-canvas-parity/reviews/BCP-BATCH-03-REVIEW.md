# BCP-BATCH-03 Review — node data/value pin enrichment
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
`NodePinSchema` now projects the real data pins the compiler consumes, so node kinds show value pins like the demo (was exec-only for most).

## Verification (ran myself)
- `dotnet build IOS-IG-SimHost.sln` **0 errors**; the touched projects (Blueprints.Editor, Hrot.Editor) build with **0 warnings** (the ~26 full-rebuild warnings are all pre-existing `Hrot.Utility.Editor.Tests`/`Fdp.Core.Tests`/etc., unrelated — DEBT-BCP-004).
- `Hrot.Blueprints.Tests` **1116 / 11 / 8** → 10 = DEBT-006, 11th = flaky sub-80ns perf (passes isolated; BATCH-03 touches no runtime path); golden + byte-stability unchanged. `Hrot.Editor.AiShared.Tests` **761/0**, `Hrot.BTree.Editor.Tests` **382/0**, `Hrot.Hsm.Editor.Tests` **333/0**, `EditorSubsystemBoot` **10/0**. +13 new pin tests.

## Code read (each pin cites its compiler source)
- **ChannelCommandNode (DYNAMIC):** coder correctly **rejected my `IActionSchemaExporter` hypothesis** (that exporter keys AI-action method FQNs + blackboard DTOs, no channel commands) and used `IChannelCommandCatalog` → `ChannelCommandCatalogEntry.ParamsTypeFqn`, matched exactly as `Stage2_Validate.cs:474-476` (LastSegment(ChannelTypeFqn)==ChannelType && Name==ActionId). Params DTO reflected → per-field data-IN pins (or single value pin for primitive params); exec-only fallback. `BuiltInChannelCommandCatalog.Instance` threaded from `EditorSubsystem` → `BlueprintDocumentFactory` → `BlueprintGraphModel` → `GetCanonicalPins` (additive/null-safe). Good correction.
- **FunctionCallNode (DYNAMIC):** Type resolved by FQN across loaded assemblies; params → data-IN, non-void return → `Return` data-OUT; exec In/Out only when `!IsPure`; graceful fallback.
- **Statics (verified compiler-consumed):** Branch `Condition`(Boolean, Stage5 ScheduleBranchNode reads first data-IN), LatentDelay `Duration`(Single), ScoreDecision `WinningOptionId`(Byte), ArrayGet `Array`/`Index`/`Element` (Array first per Stage4), ArrayMake `0`/`1` + `Array` out.
- Threading reuses the two-pass GUID binding (connected pins still bind from links). Projection-only (no persistence).

## Deferred (documented, not faked)
- `ReadRankedResultNode` (needs the referenced `UtilityDecisionDef` result-struct schema).
- Squad nodes (Partition/AssignRoles/AdvancePhase/AcquireSlot) — no node pins by compiler design (inputs from working-state).

## Test quality
Asserts real pin names/types/directions per kind (ChannelCommand params for a known action; FunctionCall params+Return, pure vs non-pure; Delay/ScoreDecision/ArrayGet/Branch). Unknown action/type → graceful exec-only. Real assertions.

## Verdict
APPROVED. Node kinds now project their real data/value pins (the user's priority). Deferred items are genuinely schema-dependent. Hand back for re-test.

## Commit Message
```
feat(blueprint-editor): enrich node data/value pins (BCP-BATCH-03)

NodePinSchema now projects the data pins the compiler consumes, so nodes show value pins like the demo:
- ChannelCommand: parameter data-in pins from IChannelCommandCatalog (ParamsTypeFqn), matched as
  Stage2_Validate does (channel registry, NOT IActionSchemaExporter); BuiltInChannelCommandCatalog
  threaded from EditorSubsystem -> BlueprintDocumentFactory -> BlueprintGraphModel -> NodePinSchema.
- FunctionCall: reflected params (data-in) + non-void Return (data-out); exec only when !IsPure.
- Static (compiler-verified): Branch Condition(bool), LatentDelay Duration(float),
  ScoreDecision WinningOptionId(byte), ArrayGet Array/Index/Element, ArrayMake 0/1 + Array.
Deferred (documented): ReadRankedResult (decision-schema), squad nodes (no node pins by design).

Projection-only (byte-stability + compiler golden unchanged). Build 0 errors; touched projects 0
warnings. Blueprints 1116/10 (DEBT-006), AiShared 761/0, BTree 382/0, Hsm 333/0, Boot 10/0; +13 tests.
```
