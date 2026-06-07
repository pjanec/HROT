-----------
Suggested implementation order that ensures that everything is perfectly testable.
----------
The answer isn't just "follow the Roadmap milestone numbers." The Roadmap orders things by *dependency*, but for **testability** you want a slightly different sequence: get the verification harness running before anything it will verify exists.

## The core principle

**Every line of production code should be born already testable.** That means the test harness — and specifically the mocks that pin the engine contract — must precede everything else. If you write the compiler first and the test harness second, you have no way to know whether the compiler's output is right until you can actually run it. That gap is where bugs hide.

## Suggested implementation order

### Phase 0 — Infrastructure (M0-M1, ~1 week)

**Read order: Architecture v1.2 → its Inline Patches → Final Resolutions → Roadmap v1.1**

Before any code: the whole team reads the architecture documents end-to-end. Not the DDs yet — those are too granular for orientation. The architecture establishes the three dispatch kinds, the channel/intent CQRS split, the storage tiers, the hot-reload model. Without that vocabulary, the DDs read as alphabet soup.

**Then build:**
- Project skeleton: the four projects (`Hrot.Blueprints.Core`, `.Generators`, `.Editor`, `.Tests`) with correct target frameworks (`netstandard2.0` for Generators, `net8.0` for the rest) and the `Fdp.Toolkits.Blueprints` directory under engine.
- `BlueprintAsset` JSON schema types (the data model from Compiler DD §3, minimal — just what's needed to deserialize a `.bp.json`).
- `BlueprintJsonServices` (serialize/deserialize with the `FdpJsonOptionsRegistry.DefaultRelaxed` options).
- A trivial end-to-end smoke test: parse a hand-written `MathLib.bp.json`, assert the deserialized object has the expected name.

This phase establishes the build hygiene. If `dotnet build` doesn't pass, nothing else matters.

### Phase 1 — Test Harness FIRST (M2, ~1 week)

**Read order: Test Harness DD → its Inline Patches**

This is the architecturally consequential reorder. The Roadmap puts M2 right after M0-M1, and that's correct, but the *significance* deserves emphasis: **nothing else gets built until the harness exists.**

**Build:**
- `MockSimulationView` wrapping a real `EntityRepository`.
- `MockEntityCommandBuffer` with `EcbOp` discriminated union, playback at end of frame.
- `BlueprintTestFixture` with the full `CompileAndLoad`/`SimulateReload`/`TickFrame` API.
- `BlueprintAssetBuilder` fluent API.
- ALC unload + GC reclaim verification in `Dispose`.
- `MockTimeController`.
- `MockDispatcherSystem<TChannel>` base class + the three concrete dispatchers.

**Acceptance gate** (don't move to Phase 2 until this passes):
- All Test Harness DD §8.3 mock-contract tests pass.
- `MockEntityCommandBuffer` playback-ordering test passes.
- ALC unload + GC reclaim verification fires correctly on a deliberately-leaked test.

The harness has nothing to harness yet — that's fine. You're proving the engine contract enforcement works against synthetic inputs.

### Phase 2 — Runtime FIRST, before Compiler (M8-M10, ~2 weeks)

This is the second reorder, and the more controversial one. The Roadmap lists Compiler (M3-M7) before Runtime (M8-M10). My recommendation: **flip them.**

**Why:** the runtime can be tested with hand-written generated-style code. The compiler needs the runtime to be testable. If you build the compiler first, you have to read its output and trust it; if you build the runtime first, you can write hand-crafted "fake generated" code that exercises every runtime path, and *then* the compiler's job becomes "produce output that matches the hand-crafted shape."

**Read order: Runtime DD → its Inline Patches**

**Build:**
- `BlueprintRegistry` (snapshot + atomic swap, with the pre-materialized `WorldSingletonList`).
- `BlueprintDefinition` record with the `uint instanceVersion`-aware `TickDelegate` and `float deltaTime`-aware `EventHandlerDelegate`.
- The three `BlueprintBlackboard{1024,4096,16384}` components.
- `BlueprintBlackboardPartitions` — full allocator (Initialize / TryGetSlotOffset / TryAttach / TryDetach / coalescing / ResetSlot / CopyToLargerTier).
- `BlueprintTickSystem` with lazy query init, `[UpdateBefore]` declarations, per-slot reload reconciliation.
- `BlueprintMaintenanceSystem` with the two-frame tier upgrade.

**Test against hand-written fake generated code:**

```csharp
// In test code only:
public static class FakeInstanceBp
{
    public const int BlueprintId = unchecked((int)0xDEADBEEF);
    public const ulong StructureHash = 0x0123456789ABCDEF;

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public BlueprintLatentCursor Cursor;
        public int TickCount;
    }

    public static int StateSize => Unsafe.SizeOf<State>();

    public static void InitDefault(Span<byte> bytes) { /* zero State */ }

    public static void Tick(Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s.TickCount++;
    }
}
```

Write tests that register this fake into `BlueprintRegistry`, attach to entities, tick, verify state. **Every Runtime DD §11 test scenario can be exercised this way without the compiler existing.**

**Acceptance gate:**
- All 14 partition allocator test scenarios pass.
- Phase-ordering test (BlueprintTickSystem runs before LocomotionDispatcherSystem) passes.
- Multi-slot-per-entity test passes.
- Tier upgrade two-frame test passes.
- Replay determinism test passes.
- Allocation budget test confirms 0 bytes/frame in steady state.

### Phase 3 — Compiler with golden tests (M3-M7, ~3 weeks)

**Read order: Compiler DD → Inline Patches v1 → Inline Patches v2**

Now the compiler has something to compile *toward* — the hand-crafted code shapes that the Runtime tests proved correct. The compiler's golden-output tests (Compiler DD §17.5, §17.6) snapshot the generated source; the snapshots are checked against what the Runtime knows how to consume.

**Build in stage order:** Stage 1 (Parse) → Stage 2 (Validate, one validator at a time per Compiler DD §17.4) → Stage 3-4 (Normalize + TypeResolve) → Stage 5 (Schedule) → Stage 6 (Lower) → Stage 7 (Emit) → Stage 8 (Roslyn finalize).

**Key sub-ordering inside Phase 3:**

1. **Stage 1-2 first** — get parsing and validation working before any IR. Validation tests are the easiest to write and the most numerous (~30 validators × 2 tests each = ~60 tests). They build confidence in the pipeline shape.
2. **Stage 7 (Emit) before Stage 6 (Lower)** for Library dispatch. Library has no lowering; emitting its registrar tests Stage 7's mechanics in isolation.
3. **Stage 6 + 7 together for Instance** dispatch. Add the cursor-based wait lowering. Instance is simpler than AiPrimitive because no phase-byte working state.
4. **Stage 6 + 7 for AiPrimitive last.** The phase-byte state machine + inline `Blackboard1024` projection is the densest emit logic.
5. **Stage 8 (Roslyn) at the end.** Once Stages 1-7 produce valid C# source, Stage 8 is mechanical.

**Acceptance gate per stage:**
- Stage 1: round-trip every Slice 1 demo asset (deserialize → serialize → bytes identical).
- Stage 2: every diagnostic code BP0xxx-BP1xxx has a positive + negative test.
- Stage 5: golden IR snapshots match for the five Slice 1 demos.
- Stage 7: golden source snapshots compile under Roslyn without errors.
- Stage 8: PE+PDB load into a collectible ALC; reflection finds `[BlueprintRegistrar]` classes; the Phase 2 runtime tests pass when the compiled code replaces the hand-crafted fakes.

The end-of-phase milestone: **run every Phase 2 runtime test, but this time use compiler-generated code instead of hand-written fakes.** All tests pass identically. The compiler is now provably substitutable for hand-crafted code.

### Phase 4 — Hot Reload (M11, ~1 week)

**Read order: Hot Reload DD → its Inline Patches**

By now the compiler can produce ALC-loadable assemblies, and the Test Harness's `SimulateReload` was already exercising the reload semantics. Hot Reload adds the engine-side coordinator that automates what the harness did manually.

**Build:**
- `[BlueprintRegistrar]` attribute discovery (already partially exists in test harness's `InvokeAllRegistrars`; lift it to `AiHotReloadCoordinator`).
- Background-thread `LoadAndScan` with file-watcher debounce.
- Main-thread `DrainPendingCallbacks` and `ApplyReload` with the strict ordering invariants from Hot Reload DD Patch 1.
- `ApplyQuickReload` public method per Hot Reload DD Patch 3 (with the 3-parameter signature from Editor DD Inline Patches Patch 3).
- `OnReloadCompleted` with the `ReloadCompletedInfo` payload from Editor DD Patch 2.
- PDB loading option.

**Acceptance gate:** all Hot Reload DD §10 test categories pass, including the production-parity test that proves `SimulateReload` (test) and `AiHotReloadCoordinator.ApplyReload` (production) produce identical registry state.

### Phase 5 — Debug Protocol (M12, ~1 week)

**Read order: Debug Protocol DD → its Inline Patches**

The debug protocol mostly observes, so it builds on top of everything that exists. The soft-pause mechanism via `IBlueprintTimeController` is the only delicate part.

**Build:**
- `IBlueprintDebugSession` + concrete `BlueprintDebugSession`.
- `DebugProbe` static dispatcher + `IBlueprintProbeSink` with the `where T : unmanaged` constraint.
- `DebugMapIndex` loader + `BlueprintDebugMap` JSON schema.
- Structure-hash-aware breakpoint matching.
- Soft pause via `RequestPause` (no `WaitOne`).
- Step semantics with `PeerCallEnter`/`PeerCallExit` probes.
- `IBlueprintTimeController` interface; `MockTimeController` for tests.

**Acceptance gate:** all Debug Protocol DD §12 test categories pass, including the probe-overhead benchmark with the 200ns budget.

### Phase 6 — Editor (M13, ~3 weeks)

**Read order: Editor DD → its Inline Patches**

The editor consumes everything above. It can be tested in pieces (logic tests for services, manual tests for ImGui), so the build order within Phase 6 is:

1. **`EditorServices` + DI wiring** — bring everything from earlier phases into one façade.
2. **`AssetBrowserWindow` + `IAssetCatalog`** — read-only, no compile flow. Verify catalog discovery.
3. **`InspectorWindow` + `DrawerRegistry` + the standard drawers** — read+edit, but no compile. Verify dirty tracking.
4. **`GraphEditorWindow`** — the visual surface. Manual testing dominant here; logic tests cover `LinkValidator` and the create-node palette.
5. **`QuickReloadService`** — the integration crown jewel. Per Editor DD Inline Patches Patch 3, this is where the sibling-signature build, registrar invocation, and coordinator handoff converge. Test extensively against the harness.
6. **`FullRebuildService`** — file save + MSBuild invocation + wait-for-coordinator.
7. **Debug-related windows** — `DebugPanelWindow`, `WatchPanelWindow`, `CallstackWindow`. Exercise the debug session.
8. **`HotReloadLogWindow`** — passive observer; trivially testable.
9. **Engine time-controller adapter** — final glue. Per Editor DD §13, this is the one piece that requires an engine-side modification.

**Acceptance gate:** the Roadmap §5 five demos run end-to-end via the editor UI. Quick Reload under 100ms for all five. Full Rebuild + reload completes successfully.

### Phase 7 — Demos & polish (M14-M16, ~1 week)

The five Roadmap §5 demos become the acceptance suite. Each is implemented as an asset + a test that exercises it end-to-end through the harness. If any demo fails, the corresponding sub-phase has a regression.

## The reorder summarized

```
Roadmap order:        M0 M1 M2 │ M3 M4 M5 M6 M7 │ M8 M9 M10 │ M11 │ M12 │ M13 │ M14-16
                      ─────────┴────────────────┴───────────┴─────┴─────┴─────┴───────
                       infra    │     compiler    │  runtime  │ HR  │ dbg │ ed  │ demos

Testability order:    M0 M1 M2 │ M8 M9 M10       │ M3 M4-M7  │ M11 │ M12 │ M13 │ M14-16
                      ─────────┼─────────────────┼───────────┼─────┼─────┼─────┼───────
                       infra    │     runtime     │  compiler │ HR  │ dbg │ ed  │ demos
                              ↑                 ↑
                       harness first      compiler last among M3-M10
```

Two swaps: (1) Test Harness gets explicit gate-before-anything status, and (2) Runtime moves before Compiler.

## Why the Runtime-before-Compiler swap matters in practice

Without the swap, the compiler is built against a hypothesis about what the runtime needs. With the swap, the compiler is built against a *proven* contract — the hand-crafted Phase 2 code is the spec the compiler must match. This is the same principle that makes integration tests more valuable than unit tests: you're testing the *contract*, not the *implementation*.

In your specific case there's also a concrete payoff. The Phase 2 hand-crafted code is small (~200 lines per dispatch kind) but exercises every runtime feature: latent cursors, reload reconciliation, multi-slot-per-entity, channel commands, world singletons. When you eventually have compiler-generated code passing the same tests, you have very high confidence the runtime is correct *and* that the compiler emits the right shape. If you'd done compiler-first, you'd be debugging "is the runtime wrong or is the compiler wrong?" every time a test failed.

## Per-developer parallelism

Within Phase 3 (Compiler), multiple developers can work on different stages in parallel once Stage 1-2 is done — Stage 5's IR shape is the only cross-cut. Within Phase 6 (Editor), multiple developers can work on different windows in parallel; only `QuickReloadService` is a critical-path item.

Phases 4 (Hot Reload) and 5 (Debug Protocol) are nearly independent — each consumes Runtime + Compiler but neither consumes the other. A two-person team could parallelize them.

## One non-obvious recommendation

**Have one person own the test harness end-to-end across all phases.** The test harness is the constant — it's used by Compiler tests, Runtime tests, Hot Reload tests, Debug tests, Editor tests. If one person owns it, the test patterns stay consistent and the harness evolves to meet each phase's needs without drift. If different people add tests in different styles, the harness accretes inconsistencies and becomes harder to extend.

That person also becomes the natural reviewer for "is this testable?" architectural questions during implementation — the human equivalent of the role the architect played during design.

## What success looks like at each gate

End of Phase 1: empty `BlueprintTestFixture` runs, passes its own mock-contract tests, leaks nothing.

End of Phase 2: hand-crafted Instance + AiPrimitive Blueprints work end-to-end through real systems. The five demos could conceptually run, if anyone wanted to write them by hand.

End of Phase 3: the same five demos run with compiler-generated code, byte-for-byte identical results to the hand-crafted versions.

End of Phase 4: hot reload works in tests (it already did via `SimulateReload`) AND in a manually-driven engine instance.

End of Phase 5: breakpoints in the debug panel halt simulation correctly via soft pause; watches show live values.

End of Phase 6: all five demos can be authored end-to-end in the editor, Quick Reloaded, debugged, and saved.

End of Phase 7: the demos run continuously, hot reload chained, no leaks.

Good luck. The design phase did its job; now the implementation phase tests whether the design *was* what we thought it was.