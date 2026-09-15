# Design idea: node-granular blueprint stepping via per-probe ECS snapshots

**Status:** captured idea + limitation analysis (2026-06-07). NOT scheduled. **Architect-consult candidate** (touches the
debug snapshot system, time controller, and probe instrumentation). Verified against the FDP clone
(`D:\Work\IOS-IG-SimHost-FDP`, `BlueprintDebugSession.cs`, `DataBreakpointManager.cs`).

## The limitation (why today's stepping is tick-granular)
A compiled blueprint **tick is atomic**: the generated goto-state-machine runs the **entire synchronous node chain**
in one tick (`EventEntry → Sequence → SetVariable → … → Delay-setup`), stopping only at a **latent suspend** or
Return. Probes (`OnNodeEnter`) are **non-blocking callbacks** — they can't halt the method mid-tick.

Pause/inspection uses `DataBreakpointManager`'s **triple-buffer, TICK-granular** rewind:
- `_preTickSnapshot` (start of tick), `_postTickSnapshot` (hit time); on a hit it rewinds the live repo to
  `_preTickSnapshot`, and while paused the view = pre-tick state. Exec/node breakpoints engage this via
  `HandleBreakpointHit → _dataBreakpointManager.OnExternalHit(...)`.

**Consequence:** any pause *inside* a multi-node tick shows the **same start-of-tick state**. So:
- Pause at SetVariable → pre-tick Counter (== pre-increment, ✓ — only because SetVariable is the tick's only Count
  mutation).
- Step to Delay (same tick) → **still** pre-tick Counter (✗ — not incremented). The increment only appears at the
  **next** tick's pre-tick snapshot.

The naive "one node per tick" intuition only holds at **latent boundaries** (each latent suspend ends a tick, so
latent nodes align with tick boundaries). Synchronous runs between latents collapse into one tick and defeat
node-granular inspection.

Zoo's CF-6 commit (`34748364`, gate `_onNodeExecuted` on `!_isPaused`) only fixes which node the **overlay
highlights** — it does NOT change the (tick-granular) **state** shown. Orthogonal.

## The proposed design (user's idea, worked through): per-probe snapshots + virtual execution pointer
Don't try to halt mid-tick. Instead, **record** state at each node during the atomic run, then let the user
**navigate the recordings** while the clock stays paused.

1. **Record at `OnNodeEnter` (node-entry).** When a breakpoint/step-temp is active for the debugged asset/entity,
   snapshot the ECS at each probe **as the node is entered** (before its effect). This yields, per tick, an ordered
   list of `(nodeId → snapshot-as-of-before-that-node)`.
   - Snapshot at *entry* gives exactly the wanted semantics: pointer at SetVariable → state before increment; pointer
     at Delay → state **after** SetVariable ran (Count incremented), before Delay. ✓ matches the expectation.
2. **Stepping = move a *virtual* execution pointer over the recordings; do NOT unpause the clock.** "Step" advances
   the pointer to the next recorded node and **restores that node's snapshot into the (read-only) inspected view**.
   The overlay highlights that node; the inspector reads that snapshot. No re-execution → no nondeterminism; the
   recordings are ground truth.
3. **Bridge to real time at the recording boundary.** Stepping *past* the last recorded node of the tick (or "resume")
   advances **one real tick**: unpause briefly, run the tick, record its probes, re-pause at the first node. Latent
   suspends naturally end a tick, so a Delay's "completion" lands in the next recorded tick.

## Key design decisions worked out (answers, not just questions)
- **Snapshot scope = the debugged entity's relevant components, not the whole world.** The triple-buffer does
  full-repo `SyncFrom`; doing that per-node × per-entity would be N× heavier. Scope per-node snapshots to the
  debugged entity's blueprint State/blackboard component(s) → cheap. (Full-repo only if cross-entity effects must be
  observed — make it opt-in.)
- **Only record during debug-active ticks** (a breakpoint or step-temp is live for that asset/entity). Zero overhead
  in normal runtime. Release the recording ring on resume/continue. Memory is bounded (≤ nodes-per-tick snapshots,
  only while paused).
- **Read-only navigation.** This gives inspection-stepping (see state at each node). It does NOT support
  edit-and-continue (modify a var mid-tick and re-run) — that would need re-execution from a snapshot and is a
  separate, much harder feature. Scope this idea to read-only first.
- **Reuse the existing probe→node mapping.** Keyed by probe nodeId via the debug map — inherits the node-ID
  correctness requirements (cf. the historical editor↔compiler node-ID drift / probe mis-attribution issue; verify
  the mapping is solid before relying on it).
- **Layer on the triple-buffer, don't replace it.** Keep tick-granular pre/post snapshots for clock pause/resume; ADD
  the per-node snapshot ring captured during the debug tick; have the inspector read from the virtual pointer's
  snapshot when paused (falling back to pre-tick when no per-node ring exists).

## Open questions for the architect
- Where to own the per-node ring (DBM vs BlueprintDebugSession vs a new recorder) and how it composes with
  `OnExternalHit`/the time controller's pause/resume.
- Snapshot mechanism for a single entity's components (is there a cheap per-entity copy, or only `EntityRepository.SyncFrom`?).
- Exact "step past end-of-recording → advance one tick → re-record → re-pause" handshake with the time controller
  (`RequestStepOneTick`/`RequestResume`/`RequestPause`).
- Whether to snapshot at node-entry only, or also node-exit (to show a node's *effect* in place without advancing).

## Bottom line
The idea is sound and the right shape: **record per-node at probe time, navigate recordings while paused** — it
delivers the expected "execution pointer moves between nodes, state updates accordingly" UX while respecting the
atomic compiled tick. It's a real feature (recorder + virtual-pointer stepping + time-controller handshake), not a
tweak — scope read-only first, run the architecture past the architect, and budget for visual iteration.
