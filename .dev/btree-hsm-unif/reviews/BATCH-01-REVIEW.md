# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-01
**Status:** APPROVED

---

## Summary

All 12 tasks (BHU-001 through BHU-016) implemented correctly. Solution builds with 0 errors.
Test results independently verified:
- `Fhsm.Tests`: 241/241 pass
- `Fdp.Toolkits.Tests`: 769/782 (13 pre-existing failures, none from this batch)
- `Hrot.Editor.Tests`: 90/90 pass

---

## Issues Found

No issues found.

---

## Code Quality Notes (no action needed)

**CgfHsmNodes.cs** — correct `[HsmAction]` stub with proper unmanaged signature. SourceGen
will emit `HsmActionRegistrar.g.cs` as required by BHU-001.

**HsmActionGenerator.cs** — `ClearAll()` emitted in `GenerateKernelDispatcher()` after
`RegisterAction`/`RegisterGuard`. FNV-1a hash matches the spec. Correct.

**HsmTickSystem.cs** — dedup dict, `_seenThisFrame`, `_staleKeys` all present. Terminal
latch cleared after publish (clears `InstanceFlags.Terminated`, resets `Phase = Idle`).
Early-exit path correctly calls `_publishedTerminalForInstanceId.Clear()` instead of
skipping stale pruning. Correct.

**CognitiveInterruptSystem.cs** — two-pass design (Pass A: init `PreviousCapabilities`
for new entities; Pass B: edge detection). Correct edge-triggered semantics.

**CognitiveRuntimeModule.cs** — exactly 6 systems, correct order, correct types.
`HsmDamageBridgeSystem` deleted from codebase and all 3 referencing example files
updated. Correct.

**AiHotReloadCoordinator.cs** — `DrainPendingCallbacks` step order: `ClearAll()` BEFORE
`RegisterAll()` BEFORE staging apply BEFORE hot-reload. Correct.

**DoctrineIngressSystem.cs** — `ResetHsmComponents` private static helper called in both
`AssignDoctrineEvent` and `AssignDoctrineHashEvent` handlers. Resets `Terminated`,
`Phase`, queue state, and `ActiveLeafIds`. Correct.

**Test quality** — all new tests verify actual runtime values (flag bits, phase enum,
byte values, event counts). No shallow assertions or string-presence-only tests.

---

## Commit Message

```
feat(bhu-01): Phases 1-3 + BHU-016 -- HSM hot-reload, terminal routing, cognitive interrupts

BHU-001: Add Fhsm.Kernel/Compiler/SourceGen refs to Hrot.AI.Doctrines.csproj.
         Create CgfHsmNodes.cs with stub [HsmAction].

BHU-002: HsmActionGenerator emits ClearAll() in GenerateKernelDispatcher.
         Tests: ClearAll empties ActionTable + GuardTable; round-trip re-register.

BHU-003: New AiHotReloadCoordinator -- unified BTree+HSM reload coordinator.
         Background load + main-thread DrainPendingCallbacks with mandatory
         ClearAll -> RegisterAll -> apply -> TryReload -> release ALC order.
         PreviousAlcRef WeakReference exposed for GC-unload verification.

BHU-004: EditorSubsystem uses AiHotReloadCoordinator (replaces FbtAssemblyHotReloader).
         AiDoctrineFactory.BuildRegistrationAction builds real HsmDefinitionBlob
         for Idle_HSM using HsmBuilder + HsmCompiler.Compile.

BHU-005: StateNode.IsFinal, StateBuilder.Final(), HsmFlattener emits StateFlags.IsFinal.
         Tests: IsFinal set in compiled blob; regression for non-final graphs.

BHU-006: HsmKernelCore sets InstanceFlags.Terminated on final-state entry.
         Guard at top of event-dispatch: early-return when Terminated.
         Tests: flag set after entering final; second Update is no-op.

BHU-007: HsmTickSystem<T> detects Terminated, publishes DoctrineFinishedEvent once
         per doctrine instance (dedup via _publishedTerminalForInstanceId).
         Terminal latch cleared after publish. Stale-key pruning each frame.
         Tests: single event; dedup; new doctrine fires new event; entity removal.

BHU-008: New CognitiveInterruptSystem -- edge-triggered CanMove->false writes bb[126].
         Two-pass: init PreviousCapabilities for new entities; then edge detection.
         Tests: edge fires; no re-trigger on steady state; always-CanMove stays 0.

BHU-009: HsmTickSystem reads bb[126] -> enqueues EventId_MobilityLost before Update.
         Does NOT clear byte 126 (that is CognitiveCleanupSystem's job).
         Tests: inject path active; no inject when byte=0; byte preserved after tick.

BHU-010: CognitiveRuntimeModule updated to 6-system order:
         [ChannelArbitration, CognitiveInterrupt, BTreeTick,
          HsmTick<128>, HsmTick<64>, CognitiveCleanup].
         HsmDamageBridgeSystem deleted; 3 referencing example files updated.

BHU-015: New CognitiveCleanupSystem -- zeroes bb[126] and bb[127] each frame.
         Tests: cleanup zeros byte; does not affect unrelated bytes.

BHU-016: DoctrineIngressSystem.ResetHsmComponents resets BrainHsm64 and BrainHsm128
         (Terminated, Phase, queue, ActiveLeafIds) on both AssignDoctrine event paths.
         Tests: Terminated cleared + Phase=Idle after reassign; ActiveLeafIds=0xFFFF.

Build: 0 errors. Fhsm.Tests 241/241. Fdp.Toolkits.Tests 769/782 (13 pre-existing).
Hrot.Editor.Tests 90/90.
```
