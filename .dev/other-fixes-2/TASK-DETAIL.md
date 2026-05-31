# Design Conformance Fixes -- Round 2 (incomplete fixes from round 1)

Verification of the round-1 fixes (`blueprint-fixes-1` BPF-* and `other-fixes-1` OFX-*) found **22 items
that are not fully fixed**: 1 not-started, 20 partial, plus 1 (BPF-015) that introduced a new seam bug.
This document is the re-fix work list for an independent coding agent.

> Companion tracker: [TASK-TRACKER.md](./TASK-TRACKER.md). Original gap descriptions, design `§`
> references, and the intended fix are in the round-1 docs -- **open the cited source finding first**:
> `../blueprint-fixes-1/TASK-DETAIL.md` (BPF-*) and `../other-fixes-1/TASK-DETAIL.md` (OFX-*).

## How round-1 was verified
50/67 auto-checked fixes were confirmed genuinely fixed; the 5 Criticals were re-read by hand
(4 fixed, BPF-015 partial). Verification read the **live** source files (the codebase-memory graph
index predates the fixes). Each item below cites the exact current code.

## The two dominant failure modes (read this first)
1. **Scaffolding without wiring (dead code).** A type/method/record field was added correctly, but
   **no production code calls it**, so runtime behaviour is unchanged. Affects FIX2-001..007, 010, 013, 014.
   Always check callers (the original defect is only closed when the new code is reached in production).
2. **Vacuous "fix" tests.** A test was added but it bypasses the real path (calls an internal helper,
   pre-populates the data the production path was supposed to produce, sorts away the property under test,
   or never calls the rendering/round-trip method). Affects FIX2-006, 007, 011, 014, 018, 019, 020, 021.
   A fix is not done until a test drives the **production** path end-to-end.

Each item: **FIX2-NNN** | source finding | severity | what's DONE | what REMAINS | (NEW bug if any) | fix.

---

## A. Feature still non-functional at runtime (highest priority)

### FIX2-001 -- Debug probe node-id format mismatch -> breakpoints still never fire (source: BPF-015)
- **Severity:** Critical
- **Done:** `StatementEmitter.cs:300-308` now emits real `DebugProbe.NodeEnter(self, "...")` / `PinValueChanged(...)` calls (no longer comments).
- **Remains:** The probe formats the node-id with `:N` (32 hex, **no hyphens**): `$"...NodeEnter(self, \"{op.NodeId:N}\");"`. But the matcher keys breakpoints by `nodeId.ToString("D")` (hyphenated) -- `BlueprintDebugSession.SetBreakpoint` (`:240` `nodeId.ToString("D")`) stores into `_bpByNodeString`, and `OnNodeEnter` (`:91`) does a direct `TryGetValue(nodeId, ...)` with no normalization. `DebugMapIndex` also uses `ToString("D")` (`:54`). So the emitted string never matches a breakpoint key -> breakpoints/steps still don't fire despite the call now existing.
- **Fix:** change the probe emission to `{op.NodeId:D}` (or normalize node-id strings to one canonical format on both the emit and match sides). Add a test that compiles a blueprint, sets a breakpoint by the editor's node Guid, runs a tick, and asserts the probe hit matched.

### FIX2-002 -- DebugMap fields are emitted empty: builder API added but the emitter never calls it (source: BPF-002 + BPF-021)
- **Severity:** High
- **Done:** `DebugMap`/`DebugMapEntry`/`DebugMapBuilder`/`DebugMapSerializer`/`DebugMapIndex` now carry & round-trip `AssetName`, `GeneratedSourcePath`, `Graphs`, `Pins`, `StateLayout`, and `NodeKind`/`DisplayName` (DebugMapBuilder.cs:33-115; DebugMapSerializer.cs:19-113; DebugMapIndex.cs:20-101).
- **Remains (dead code):** `CSharpEmitter.cs:43-53,78` only calls `RecordNodeStart`/`RecordNodeEnd`/`Build()`. The new `SetAssetName`/`SetGeneratedSourcePath`/`AddGraph`/`AddPin`/`AddStateLayoutField` have **zero callers** -> every produced `DebugMap` has `AssetName=""`, `Graphs=[]`, `Pins=[]`, `StateLayout.Fields=[]`, `GeneratedSourcePath=""` at runtime (unchanged from pre-fix). `NodeKind`/`DisplayName` are always `string.Empty` because `IrDebugAnnotation` (IrDebugAnnotation.cs:3-9) carries no such fields, so `RecordNodeStart` never receives them. This is the root cause that also leaves FIX2-009 (Instance state inspection) and watch type-resolution non-functional.
- **Fix:** populate the map during emit -- add `NodeKind`/`DisplayName` to `IrDebugAnnotation` (or pass from `IrOp_DebugProbe_NodeEnter`) and thread into `RecordNodeStart`; call `AddPin`/`AddGraph`/`AddStateLayoutField`/`SetAssetName`/`SetGeneratedSourcePath` from the emitter/Stage7. Add a test asserting a compiled asset's `DebugMap.Pins`/`StateLayout` are non-empty.

### FIX2-003 -- breakpoint per-frame dedup never resets: `OnNewTick()` has no production caller (source: BPF-003)
- **Severity:** High
- **Done:** `Breakpoint` has `IsStale`/`AssetStructureHashAtSetTime`; `OnNodeEnter` gates on `Enabled && !IsStale` + hash-safety; `_firedBreakpointsThisTick` dedup + `IncrementHitCountOnly`; reload marks stale not clears (all in BlueprintDebugSession.cs / IBlueprintDebugSession.cs).
- **Remains (dead code):** `OnNewTick()` (IBlueprintDebugSession.cs:181; impl BlueprintDebugSession.cs:633) that resets `_firedBreakpointsThisTick` is **only called from tests** -- never from any coordinator/ECS system/frame loop. Per Debug DD §9.2 the dedup set must reset each tick. In production the set only resets on `Continue()`, so a node hit again in a later tick (without an intervening pause) is permanently suppressed.
- **Fix:** call `session.OnNewTick()` at the tick boundary from the production frame loop / debug coordinator (e.g. where the tick system advances). Add an integration test that ticks twice and asserts the breakpoint fires each tick.

### FIX2-004 -- `DebugProbe.Sink` never routed: `Attach()/Detach()` exist but `BlueprintEditorModule` never calls them (source: BPF-033)
- **Severity:** High
- **Done:** `BlueprintDebugSession.cs:208-234` -- `_isAttached` field, `IsAttached` returns it, `Attach()` sets `DebugProbe.Sink = this`, `Detach()` restores `NullProbeSink`.
- **Remains (dead code):** `BlueprintEditorModule.cs:35-55` `OnEditorActivated`/`OnEditorDeactivated` do **not** call `Attach()`/`Detach()` and the module holds no session reference. So `DebugProbe.Sink` is never set to the session in production -> probes never reach it (compounds FIX2-001). Tests call `Attach()` directly (SC-anchor: module wiring untested).
- **Fix:** wire the session into `BlueprintEditorModule` and call `Attach()` on activate / `Detach()` on deactivate. Add a test constructing the module and asserting `DebugProbe.Sink` becomes the session.

### FIX2-005 -- Blueprint editor windows never registered: registrar doesn't implement the engine interface and isn't in DI (source: BPF-035)
- **Severity:** High
- **Done:** `BlueprintWindowRegistrar.cs` + `IBlueprintWindowRegistry.cs` created; registrar registers all 7 windows (`:53-73`); isolated test passes.
- **Remains (dead code):** the registrar uses a new local `IBlueprintWindowRegistry`, NOT the engine's `IWindowRegistrar.RegisterWindows(WindowManager)`; it is **not added to DI** (`BlueprintEditorServiceCollectionExtensions.cs:7-18`); and has **zero production callers**. The engine orchestrator calls `IWindowRegistrar.RegisterWindows` on subsystems, which this never implements -> windows remain unregistered at runtime.
- **Fix:** implement the engine `IWindowRegistrar` (bridge to `WindowManager`) and register it in DI so the orchestrator invokes it. Add a test that drives the engine `IWindowRegistrar -> WindowManager` path.

### FIX2-006 -- Debug/Watch/Callstack panels fetch data then discard it (no rendering); Callstack uses the wrong API (source: BPF-034)
- **Severity:** High
- **Done:** `DebugPanelWindow.cs:16-24`, `WatchPanelWindow.cs:24-29`, `CallstackWindow.cs:18-23` now *call* `IsPaused`/`GetBreakpoints()`/`GetWatches()`/`GetRecentNodeHistory()` -- but immediately discard with `_ = x;` and render nothing ("Rendering requires ImGui runtime").
- **Remains:** no actual ImGui rendering of session state (Editor DD §8.2/8.5/8.7). Tests (DebugWindowsTests.cs) still assert only Title + subscription, never call `DrawUI()`.
- **NEW bug:** `CallstackWindow.cs:21` calls `GetRecentNodeHistory()` (flat execution history) instead of the design's `GetCurrentCallStack()` (peer-call frame stack, Editor DD §8.7) -- and `GetCurrentCallStack()` doesn't exist on `IBlueprintDebugSession`. When rendering is wired, the callstack panel will show node history, not a call-depth frame stack.
- **Fix:** implement the panel rendering per §8.2/8.5/8.7; add `GetCurrentCallStack()` (peer-call frames) to the interface and use it in `CallstackWindow`. Add a `DrawUI()`-driving test (headless ImGui or a render-command capture).

### FIX2-007 -- BTree runtime overlay still blank: `SetDebugMetadata()` has no production caller (source: BPF-026)
- **Severity:** High
- **Done:** `BTreeDebugSession.cs:29-30,80-94,135-142` -- metadata fields, `SetDebugMetadata()`, `GetVisualId()`, and `Update()` now symbolicates `RunningElementId`/`StackElementIds`.
- **Remains (dead code):** `SetDebugMetadata()` is **only in tests**; no editor module wires the asset's `DebugMetadata` into the session on load (`BTreeAssetContributor.cs:53` passes it to the projector, not the session). At runtime metadata stays null -> `RunningElementId` stays null -> overlay renders nothing (original symptom). SC-anchor: symbolication tests call only the internal `TrySymbolicateIndex`; `Update()` tests never call `SetDebugMetadata()` first nor assert `RunningElementId`.
- **Fix:** call `SetDebugMetadata(blob.DebugMetadata, assetId)` when the editor attaches an asset to the debug session. Add a test that calls `SetDebugMetadata` then `Update` and asserts a non-null `RunningElementId`.

### FIX2-008 -- `LookAtChannelIntentEgressTranslator` still omits the ActionParams blob compare (source: OFX-012)
- **Severity:** High
- **Done:** `AnimationChannelIntentEgressTranslator.cs:27,70-84` now compares the 4-ulong `Params` blob alongside `ActionInstanceId`.
- **Remains:** the **LookAt** translator (`LookAtChannelIntentEgressTranslator.cs:25,61-66`) was not touched -- still gates publication solely on `ActionInstanceId`. The original OFX-012 cited both files. In-place param mutations reusing the same `ActionInstanceId` are silently dropped for the LookAt channel.
- **Fix:** apply the same blob-comparison fix to `LookAtChannelIntentEgressTranslator` (DD-2 §2.4).

---

## B. Partial implementations (still diverge from design)

### FIX2-009 -- Instance-dispatch state inspection is still a stub (source: BPF-001)
- **Severity:** Medium
- **Done:** `BlueprintStateSnapshot` has the full shape; `GetCurrentStateSnapshot`->`CaptureStateSnapshot` with a dispatch switch; the **AiPrimitive** path reads slot bytes + hash header (§8.6) correctly.
- **Remains:** `CaptureInstanceStateFromDefinition` (BlueprintDebugSession.cs:522-528) is an empty stub (returns cursor=null, no fields) -- comment "Instance state byte access requires the partition allocator, not wired in here." Per Debug DD §8.5 it must read slot bytes via the partition allocator, read the `BlueprintLatentCursor` (first 16 bytes), and project all `stateLayout` fields. (Also blocked on FIX2-002 providing a non-empty `StateLayout`.)
- **Fix:** implement Instance slot-byte reading + cursor + field projection.

### FIX2-010 -- HSM snapshot: EventQueue / TimerSlots / HistorySlots still empty (source: BPF-010)
- **Severity:** Medium
- **Done:** `HsmDebugSession.cs:94-120` -- assetId populated; `ActiveLeafStableIds` decoded via `DecodeLeaves64/128` through `MachineMetadata.StateStableIds`. BTree counterpart fully populated.
- **Remains:** `EventQueue`/`TimerSlots`/`HistorySlots` are still `Array.Empty<...>()` (lines 96-98, 112-114). The raw data exists in `HsmInstance64` (TimerDeadlines[2], HistorySlots[2], EventCount, EventBuffer[24]) but no decode helpers were added.
- **Fix:** add decode helpers for the event queue, timer slots, and history slots (mirroring `DecodeLeaves*`).

### FIX2-011 -- HSM deferred events: projector never populates them; no blob storage; vacuous test (source: BPF-022)
- **Severity:** Medium
- **Done:** `HsmFluentEmitter.cs:284-286` emits `.DeferEvent(id)` for `s.DeferredEventIds` (write path).
- **Remains:** `HsmAssetProjector` never writes `StateNode.DeferredEventIds` (stays empty, HsmAsset.cs:462-463). Worse, the kernel `StateDef` has no deferred-event field and `MachineMetadata` no per-state mapping -> there is **no blob-side source** to project from. So a save+reload round-trip still drops all deferred events.
- **NEW (vacuous test):** `HsmFluentEmitterTests.cs:170-171` pre-populates `DeferredEventIds` directly and comments that the projector path isn't implemented -- it never exercises blob->projector->emit.
- **Fix:** add deferred-event storage to `StateDef`/blob + `MachineMetadata`; populate it in the flattener/emitter; read it in the projector; rewrite the test to do a real round-trip.

### FIX2-012 -- HSM projector: transitions & regions still use positional-sort identity (source: BPF-025)
- **Severity:** Medium
- **Done:** the **states** section (HsmAssetProjector.cs:88-109) now resolves `StableId` via `metadata.StateStableIds[FlatIndex]` (keyed lookup, no positional sort).
- **Remains:** the **transitions** (`:145-158`) and **regions** (`:181-192`) sections still sort layout Guid keys and assign by array index. `metadata.TransitionVisualIds` (already populated, keyed by flat transition index) is never consulted. A structural edit (add/remove a transition or region) shifts every subsequent Guid->layout mapping (wrong waypoints/colors/comments) -- the exact bug that was fixed for states.
- **Fix:** resolve transition `VisualId` via `metadata.TransitionVisualIds[index]` and regions analogously; remove the positional-sort fallbacks.

### FIX2-013 -- BTree async-badge overlay still missing (source: BPF-045)
- **Severity:** Medium
- **Done:** `BTreeDebugSession.cs:164-178` now symbolicates trace records' `NodeVisualId` via `GetVisualId(rec->NodeIndex)` (no longer `Guid.Empty`). (Depends on FIX2-007's `SetDebugMetadata` wiring to be non-null at runtime.)
- **Remains:** `BTreeRuntimeOverlayRenderer.cs` still has only 3 sections (running node, stack ancestry, status glyphs). The design §12.4 step-4 async-pending clock-icon block (`GetRecentAsyncHistory` -> `DrawAsyncBadge`) is absent. Nodes with pending async ops render no clock icon.
- **Fix:** add the step-4 async-badge render path per §12.4.

### FIX2-014 -- FakeAnimationBackend still runs off the managed Dictionary; only Generation is mirrored (source: OFX-003)
- **Severity:** Medium
- **Done:** `SetEntityRepository()` added (`:77-81`), registers `FakeAnimBackendState`; `RegisterEntity` writes `Generation`; `ResetWorld` removes the component.
- **Remains:** `Tick` (`:255-264`) still iterates `_entityStates.Values` (managed Dictionary); the per-tick state (Slots/Aim/Stance/TotalTicks/footstep/notifies) is never mirrored to `FakeAnimBackendState`. So AAR-recording + entity-inspector integration (DD-Fake §1/§3/§4) remain absent.
- **NEW bug:** `_entityIndexToEntity` is populated in `RegisterEntity` (`:98`) but never removed in `UnregisterEntity` (`:105-115`) -> dead-entity map leak when entities are unregistered without `ResetWorld`.
- **Fix:** move per-entity state into `FakeAnimBackendState` and make `Tick` query-driven (or formally re-scope OFX-003 as a documented deviation if the managed path is intentional); also clean up `_entityIndexToEntity` in `UnregisterEntity`.

---

## C. Bookkeeping, not-started, and test-quality gaps (lower priority)

### FIX2-015 -- blueprints-1 OPEN debt only partly addressed; rows not marked RESOLVED (source: BPF-011)
- **Severity:** Low
- **Done in code:** DEBT-004 source comment present; DEBT-023 comment present; DEBT-021 per-hit `OnBreakpointListChanged` removed (real fix).
- **Remains:** DEBT-018 (debug files folder placement) and DEBT-022 (`GetNodeHistory(Entity,int)` not on interface) are unaddressed (no code change, no comment). DEBT-003 has a tracker note but no source comment. **All six rows are still "OPEN"** in `.dev/blueprints-1/DEBT-TRACKER.md` despite three being addressed -- mark addressed ones RESOLVED; implement or explicitly defer DEBT-018/022.

### FIX2-016 -- blueprints-2 DEBT-TRACKER inconsistent with code (source: BPF-012)
- **Severity:** Low (bookkeeping)
- **Done:** D-02 (SubtreeAssetIds) fixed & RESOLVED; D-03/D-04 fixed in code (HsmEmitter.cs:38-61).
- **Remains:** `.dev/blueprints-2/DEBT-TRACKER.md:9-10` still mark D-03/D-04 as OPEN -> mark RESOLVED. (D-01 is intentionally deferred -- leave.)

### FIX2-017 -- breakpoints-1 debt not started (source: BPF-013) [only NOT-FIXED item]
- **Severity:** Low-Medium
- **Remains (all three still OPEN, no code change):**
  - D-BP-01: `CgfSubsystem.cs:795-801` `CgfNoOpTimeController.IsPausedByDebugger => false` still hardcoded.
  - D-BP-02: `CgfSubsystem.cs:522-523` `_bpPreTickSnapshot` still mirrors only `CgfComponentRegistry`.
  - D-BP-04: `GraphEditorWindow.cs` `DrawUI()` (`:63-111`) has no right-click popup; `BlueprintBreakpointMenuPopulator.PopulateNodeMenu` is never reached via UI (tests call it directly).
- **Fix:** implement per design, or (if still deferred) leave but confirm with the user; D-BP-04 is the user-facing one (Blueprint canvas breakpoint menu unreachable).

### FIX2-018 -- BTree composite emitter: add the Roslyn compile assertion (source: BPF-027)
- **Severity:** Low
- **Done:** `BTreeFluentEmitter.EmitComposite` (`:228-242`) switched to statement-lambda block form -- the invalid `;,` sequence is genuinely fixed; 5 regex/substring tests added.
- **Remains:** the fix direction asked for a **round-trip compile assertion** (Roslyn parse/compile the emitted C#); the tests only regex for absence of `;,`. A more complex tree (pills/decorators wrapping composites) could still emit invalid C# undetected.
- **Fix:** add a Roslyn `CSharpSyntaxTree.ParseText` + diagnostics assertion over representative trees.

### FIX2-019 -- AtomicMultiFileWriter: partial-batch `SuccessfullyWritten` still untested (source: BPF-037)
- **Severity:** Low
- **Done:** a mid-move-failure test was added (`AtomicMultiFileWriterTests.cs:131-152`) hitting the move-phase catch.
- **Remains:** the test uses a **single-file** dict, so `written` is always empty when the move fails -> the `SuccessfullyWritten` partial-accumulation arm (production line 34) is never exercised, and the test never asserts `result.SuccessfullyWritten`.
- **Fix:** add a two-file scenario (file-1 moves OK, file-2 fails) asserting `SuccessfullyWritten` contains file-1.

### FIX2-020 -- ChildOrderDeterminismTests still test a local stub, not a production model (source: BPF-047)
- **Severity:** Low
- **Done:** the stub was renamed `StubContainer` -> `FakeContainerModel` with a "mirrors Demo" comment.
- **Remains:** it's still a private, test-only `List<NodeId>`-backed class (no `using NodeEditor.Demo`); the production `IContainerNodeModel` types (`StateNode`, Demo `FakeContainerModel`) are never exercised. A Dictionary/HashSet-backed production impl would still pass. The SC-anchor vacuity is only cosmetically changed.
- **Fix:** assert child order on the real production `IContainerNodeModel` implementations.

### FIX2-021 -- Utility emitter round-trip test added but skips the reflect step (source: OFX-015)
- **Severity:** Low-Medium
- **Done:** `UtilityFluentEmitterTests.cs:285-357` now Roslyn-parses the emitted C# (real improvement over `Assert.Contains`).
- **Remains:** it never **reflects** a `UtilityDecisionAsset` back (Editor DD §8.2/§12) -- it only AST-extracts `.Consider(...)` `InputName` identifiers and sorts them alphabetically before comparing. So **zeroed weight** and **mistranslated context** still pass silently; dropped-consideration is caught only by the count. (`UtilityAssetLoader.Load` itself still defers options/considerations parsing -- the reflect path doesn't exist yet.)
- **NEW (vacuous sub-assertion):** the test sorts `InputName`s alphabetically (`:353`), so it cannot detect an ordering inversion even if the emitter's VisualId sort broke.
- **Fix:** implement `UtilityAssetLoader` consideration/option parsing and assert full structural equality (weights, contexts, curves, order) against the original model.

---

## Per-source-doc index
- **blueprint-fixes-1 (BPF):** FIX2-001 (BPF-015), 002 (BPF-002/021), 003 (BPF-003), 004 (BPF-033), 005 (BPF-035), 006 (BPF-034), 007 (BPF-026), 009 (BPF-001), 010 (BPF-010), 011 (BPF-022), 012 (BPF-025), 013 (BPF-045), 015 (BPF-011), 016 (BPF-012), 017 (BPF-013), 018 (BPF-027), 019 (BPF-037), 020 (BPF-047)
- **other-fixes-1 (OFX):** FIX2-008 (OFX-012), 014 (OFX-003), 021 (OFX-015)

## Note on the 50 confirmed-fixed
The other round-1 fixes verified clean, including the Criticals BPF-014/016/017/018 and the High OFX
algorithm fixes (OFX-001 nav backend, OFX-002 notify-kind, OFX-004/005 fake-anim stop/blendweight,
OFX-007 squad merge-contact, etc.). Only the 22 items above need another pass.
