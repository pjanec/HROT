# Behavior Diagnostics — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions
**Design:** See [DESIGN.md](./DESIGN.md)
**Debt:** See [DEBT-TRACKER.md](./DEBT-TRACKER.md)

---

## Phase 1 — Foundation: Memory & Kernel Contracts

**Goal:** Establish unmanaged data structures and kernel-side contracts (interface + unmanaged context) without touching any tick system or UI yet.

- [ ] **BHD-T1.1** `BTreeTraceOpCode` enum and `ITreeTracer` interface in `Fbt.Kernel` [details](./TASK-DETAIL.md#t11--btreetraceopcode-and-itreetracer-in-fbtkernel)
- [ ] **BHD-T1.2** `HsmTraceContext` unmanaged struct in `Fhsm.Kernel.Data` [details](./TASK-DETAIL.md#t12--hsmtracecontext-in-fhsmkerneldata)
- [ ] **BHD-T1.3** `BTreeTraceRecord` + `BTreeTraceWorkingMemory1024` + write APIs [details](./TASK-DETAIL.md#t13--btreetracerecord--btreetraceworkingmemory1024--write-apis)
- [ ] **BHD-T1.4** `HsmTraceWorkingMemory1024` [details](./TASK-DETAIL.md#t14--hsmtraceworkingmemory1024)
- [ ] **BHD-T1.5** Component-ID assignment and `CognitiveComponentRegistry` registration [details](./TASK-DETAIL.md#t15--component-ids--cognitivecomponentregistry-registration)

---

## Phase 2 — Kernel Refactoring

**Goal:** Wire the kernels to the new contracts; eradicate the static global HSM trace buffer.

- [ ] **BHD-T2.1** `Interpreter<TB,TC>` adds `ITreeTracer` constraint and emit hooks [details](./TASK-DETAIL.md#t21--interpreter-itreetracer-constraint--emit-hooks)
- [ ] **BHD-T2.2** `BTreeContext` implements `ITreeTracer` + holds `TraceBuffer` pointer [details](./TASK-DETAIL.md#t22--btreecontext-implements-itreetracer--holds-tracebuffer-pointer)
- [ ] **BHD-T2.3** Delete `HsmTraceBuffer` + `SetTraceBuffer` (lands atomically with T2.4) [details](./TASK-DETAIL.md#t23--delete-hsmtracebuffer-and-settracebuffer)
- [ ] **BHD-T2.4** Thread `HsmTraceContext*` through kernel pipeline [details](./TASK-DETAIL.md#t24--thread-hsmtracecontext-through-kernel-pipeline)
- [ ] **BHD-T2.5** `HsmKernelBridge` adds `TraceContext` field [details](./TASK-DETAIL.md#t25--hsmkernelbridgetracecontext-field)

---

## Phase 3 — Generic Debug-State Plumbing

**Goal:** Generic `DebugState` transient component, JSON-patch command, and ingress system.

- [ ] **BHD-T3.1** Move `GlobalDebugSettings` into `Hrot.Common`, add `AutoEnableAiTracing` [details](./TASK-DETAIL.md#t31--move-globaldebugsettings-into-hrotcommon)
- [ ] **BHD-T3.2** `DebugState`, `BehaviorDebugFlags`, `HrotComponentIds.DebugState` [details](./TASK-DETAIL.md#t32--debugstate-behaviordebugflags-hrotcomponentidsdebugstate)
- [ ] **BHD-T3.3** `PatchDebugStateCommand` managed event [details](./TASK-DETAIL.md#t33--patchdebugstatecommand-managed-event)
- [ ] **BHD-T3.4** `DebugStatePatchCompiler` (expression-tree based) [details](./TASK-DETAIL.md#t34--debugstatepatchcompiler)
- [ ] **BHD-T3.5** `DebugStatePatchSystem` in `SystemPhase.Input` [details](./TASK-DETAIL.md#t35--debugstatepatchsystem)

---

## Phase 4 — Runtime Wiring

**Goal:** Connect debug state, lifecycle, tick systems, and behavior registry so tracing actually happens during execution.

- [ ] **BHD-T4.1** `TraceBufferLifecycleSystem` (adds/removes 1KB buffers on flag change) [details](./TASK-DETAIL.md#t41--tracebufferlifecyclesystem)
- [ ] **BHD-T4.2** `BTreeTickSystem` resolves `DebugState`, injects `TraceBuffer` pointer [details](./TASK-DETAIL.md#t42--btreeticksystem-wiring)
- [ ] **BHD-T4.3** `HsmTickSystem<T>` builds `HsmTraceContext`, updates `InstanceFlags.DebugTrace` [details](./TASK-DETAIL.md#t43--hsmticksystem-wiring)
- [ ] **BHD-T4.4** `BehaviorDefinition.HsmMetadata` + `AiBehaviorFactory` populates it [details](./TASK-DETAIL.md#t44--behaviordefinitionhsmmetadata--aibehaviorfactory)

---

## Phase 5 — UI, Translators, Auto-Enable, NLog

**Goal:** Surface the traces to humans (inspector, JSON dumps, NLog), enable per-entity UI toggle, and system-wide auto-enable for entity genesis.

- [ ] **BHD-T5.1** `BTreeTraceWorkingMemoryRenderer` and `HsmTraceWorkingMemoryRenderer` [details](./TASK-DETAIL.md#t51--imgui-renderers)
- [ ] **BHD-T5.2** `BTreeTraceWorkingMemoryTranslator` + `HsmTraceWorkingMemoryTranslator`; register in `HrotScenarioSerializerFactory` [details](./TASK-DETAIL.md#t52--json-translators)
- [ ] **BHD-T5.3** `GlobalActionIds.ToggleAiTrace`/`ToggleAiTraceLog` + context-menu items + action handlers [details](./TASK-DETAIL.md#t53--context-menu--globalactionids)
- [ ] **BHD-T5.4** `AiDiagnosticsTkbTranslator` registered in `SimHostNodeBootstrapper` [details](./TASK-DETAIL.md#t54--aidiagnosticstkbtranslator)
- [ ] **BHD-T5.5** `BehaviorLog` emission from `BTreeTickSystem` / `HsmTickSystem` (delta extraction) [details](./TASK-DETAIL.md#t55--behaviorlog-emission)

---

## Phase 6 — Out-of-Solution Examples & Unit Tests

**Goal:** Repair FastBTree/FastHSM examples and unit-test projects that live outside `IOS-IG-SimHost.sln` and would otherwise silently break.

- [ ] **BHD-T6.1** Fix `Fbt.Tests` for new `ITreeTracer` constraint [details](./TASK-DETAIL.md#t61--fix-fbttests-for-itreetracer-constraint)
- [ ] **BHD-T6.2** Fix `Fbt.Benchmarks`, `Fbt.Examples.*`, `Fbt.Demo.Visual.Tests` [details](./TASK-DETAIL.md#t62--fix-fbtbenchmarks-fbtexamples-fbtdemovisualtests)
- [ ] **BHD-T6.3** Fix `Fhsm.Tests` (rewrite `TraceTests`, `TraceSymbolicationTests`, `OrthogonalRegionTests`, `FailSafeTests`) [details](./TASK-DETAIL.md#t63--fix-fhsmtests-after-kernel-refactor)
- [ ] **BHD-T6.4** Fix `Fhsm.Benchmarks`, `Fhsm.Examples.Console`, `Fhsm.Demo.Visual`, `Fhsm.Demo.Visual.Tests` [details](./TASK-DETAIL.md#t64--fix-fhsmbenchmarks-fhsmexamplesconsole-fhsmdemovisual-fhsmdemovisualtests)

---

## Verification Checklist (run after Phase 6)

See [TASK-DETAIL.md → Verification Checklist](./TASK-DETAIL.md#verification-checklist) for the exact commands and manual smoke tests.
