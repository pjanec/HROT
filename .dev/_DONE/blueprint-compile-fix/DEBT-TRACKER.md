# Blueprint compile-fix — Technical Debt Tracker

> Debt discovered during BP-1..BP-4. P1 never sits here (becomes a corrective task); P2/P3 tracked here.

| ID | Source | Description | Priority | Status |
|----|--------|-------------|----------|--------|
| BCF-D01 | BP-2 rework | **`EntityRepository.FlushCommandBuffers` allocates every call (real bug) — fix reverted, must be redone safely.** See detail below. | P2 | OPEN (fix reverted) |
| BCF-D02 | BP-2 | Pre-existing golden/snapshot failures surfaced as the only remaining Blueprints failures (DEBT-006): `AiPrimitiveEmitGoldenTests` (MoveToAndFire, HasVisibleTarget), `LibraryEmitGoldenTests`, `LibraryMathDemoTests`/`MoveToAndFireDemoTests` `*_GeneratedSource_Snapshot`, `ConditionSummaryAttachmentTests.Synthesize_EqsResult_ScoreCrossed_IncludesThreshold`. Not regenerated (out of scope); they predate this work. | P3 | OPEN (pre-existing) |
| BCF-D03 | BP-2 | CLR `FunctionCall` pin rehydration uses reflection (works in net8 editor/test); in the netstandard2.0 MSBuild generator host the target game assembly may be absent → exec-only fallback (logged, not silent). Registry-driven method signatures would remove the reflection dependency. | P3 | OPEN |

---

## BCF-D01 (detail) — `EntityRepository.FlushCommandBuffers` per-call allocation

### The real bug (confirmed)
`AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` (in `Hrot.Blueprints.Tests/Runtime/`) asserts a
ticked blueprint frame allocates **zero** managed bytes. It fails because of a **pre-existing** allocation in the
engine's command-buffer flush path (NOT caused by the blueprint work — it predates BP-1..BP-2; introduced when
`_repo.FlushCommandBuffers()` was added to `BlueprintTestFixture.TickFrame`):

- `FDP/Engine/Fdp.Core/EntityRepository.View.cs` — `FlushCommandBuffers()` iterates
  `_perThreadCommandBuffer.Values` where `_perThreadCommandBuffer` is a
  `ThreadLocal<EntityCommandBuffer>(trackAllValues: true)`.
- **`ThreadLocal<T>.Values` allocates a fresh `IList<T>` (a `List<T>`) on every access** (~32 bytes). Called once
  per frame → ~32 bytes/frame → the zero-allocation assertion fails (e.g. ~3200 bytes / 100 frames).

### The attempted fix (REVERTED — do not re-apply as-is)
A zero-allocation refactor in `EntityRepository.cs` + `EntityRepository.View.cs`:
- Added `private readonly List<EntityCommandBuffer> _knownBuffers` (+ `_knownBuffersLock`), appended by a
  factory `CreatePerThreadBuffer()` wired as the `ThreadLocal` value factory (so every thread's first
  `GetCommandBuffer()` registers its buffer once).
- `FlushCommandBuffers()` iterated `_knownBuffers` with a plain `for` loop (zero allocation) instead of `.Values`.
- Kept `trackAllValues: true` for `Dispose()` (which still uses `.Values`).

This **did** make `AllocationFreeTests` pass, but it **regressed**
`Fdp.Tests.RecorderSystemTests.DualStream_RecordableMaskFilter_NonRecordableBitIsCleared` (Fdp.Core.Tests):
baseline = that test passes; with the change it fails. (Verified by stash/unstash bisection.)

### Why it regressed (hypothesis to confirm when redoing)
`ThreadLocal.Values` returns buffers for **currently-live** threads; `_knownBuffers` is **append-only and
retains every buffer ever created** (including those of finished threads), and the unlocked forward-iteration
during flush changes set/ordering semantics. The recorder's dual-stream / recordable-mask logic appears to
depend on the previous flush set or order. A zero-alloc flush must preserve the exact playback semantics
`.Values` provided.

### How to redo it properly (separate, independently-reviewed change)
1. Reproduce both: `AllocationFreeTests.TickFrame_1000Frames_AllocatesZeroBytes` (must reach 0 bytes) AND
   `RecorderSystemTests.DualStream_RecordableMaskFilter_NonRecordableBitIsCleared` (must stay green).
2. Achieve zero-alloc flush WITHOUT changing the flushed-buffer set/order vs `.Values`. Options to evaluate:
   - Cache the `IList<EntityCommandBuffer>` returned by `.Values` and refresh it only when the live-thread set
     changes (invalidate on buffer creation), so steady-state frames don't allocate.
   - A custom non-allocating enumeration over the ThreadLocal's tracked values that matches `.Values` semantics.
   - If `_knownBuffers` is kept, reconcile dead-thread buffers + ordering with what the recorder expects (and
     add a test pinning the flush semantics).
3. Run the broader engine suites (Fdp.Core.Tests, and anything exercising `FlushCommandBuffers` / recording) to
   confirm no other regression — `FlushCommandBuffers` is core and used widely.

**Scope note:** this is an engine (`Fdp.Core`) concern, not blueprint-specific; it was only surfaced by the
blueprint AllocationFree test. Treat as its own small change with its own review.
