# The interaction API — tools, actions and long-running work

> **Consolidated design, 2026-08-10.** Supersedes the API fragments scattered across
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) and [UXI-07](UX_Feature_Tool_Model.md); those keep the
> *evidence*, this holds the *contract*.
>
> **User brief:** *"Design for the future, to be ready for a much more complex editor and cases than we
> have now, including long-running actions. Integrate the old knowledge, design the proper API."*
>
> 🔒 **Built from established models, not invented** — see [§7](#7-where-each-piece-comes-from).
>
> ✅ **Acceptance:** [UX_Interaction_UseCases.md](UX_Interaction_UseCases.md) — **50 cases, 41 headless /
> 9 integration / 0 visual-only**, with a coverage check mapping every API element to a case.

## 1. ⚠ Retracted — and what the engine actually mandates

> **User, 2026-08-10:** *"No, two async handlers should not write to ECS. There are command buffers for
> that. All ECS access is well documented. Please, no assumptions, no surprise discoveries — do your
> homework first."*

**An earlier revision of this document claimed a data race.** It was wrong, and the retraction is
[Corrections 21](UX_Tasks_Detail.md#corrections).

| My claim | Reality, from the docs and the code |
|---|---|
| *"`FdpEventBus` has zero synchronization"* | ❌ **False.** `NativeEventStream<T>.Write` is documented *"Thread-safe: multiple threads can write concurrently"* and implemented with `Interlocked.Increment` atomic reservation, locking only to resize (`NativeEventStream.cs:83-101`). README §2.6 states it outright. ⚠ *I grepped `FdpEventBus.cs` — the dispatcher — not the stream one call deeper* |
| *"⇒ publishing from a continuation is a race"* | ❌ **False.** Lock-free concurrent publish is the **designed** path, and the bus is double-buffered so writes and tick-reads target different buffers |

### 🔒 The documented rules — which existed all along

| # | Rule | Source |
|--:|---|---|
| **7** | **Background ≠ main thread.** Background/async code runs on a **read-only snapshot** (`ISimulationView`); it **never touches the live `EntityRepository`**. Structural changes go through an **`IEntityCommandBuffer`**, played back on the main thread. `GetComponentRW` is *intentionally absent* from `ISimulationView` | [Programmer's Guide](../HROT-PROGRAMMERS-GUIDE.md) §Part 0, rule 7 |
| **8.1** | All ImGui/Raylib draw calls on the main thread; **cross-thread notification only via `volatile` flags** | ibid. §8.1 |
| **2.4 / 2.6** | ECB for structural mutation from parallel threads; Tier-1 events are lock-free | README §2.4, §2.6 |

### What is actually left, stated as a question rather than a finding

The two handlers (`EditorSubsystem.cs:1462`, `:1479`) resume off-tick and then:

| Statement | Against the rules |
|---|---|
| `_world.Bus.Publish(new SeedTargetCommand{…})` | ✅ **correct** — the thread-safe, designed channel |
| `_world.IsAlive(target)`, `FindEntityByNetworkId(…)` | ⚠ reads the **live `EntityRepository`** (`_world` is `EntityRepository?`, `:184`) — not an `ISimulationView`. Rule 7 addresses background/async **modules**; whether it binds a UI async continuation is **a question for the architect, not my call** |

🔒 **And the user has already given the design answer regardless:** *async handlers should not write to
ECS; command buffers exist for that.* So the API's job is to make the documented path the **only**
reachable one — see §6.

## 2. The shape: split declarations, unified runtime

🔒 **The user ruled tools ≠ actions** — *"an action is what activates a tool"*. That ruling is about
**declaration**, and it stands. But at **runtime** a modal tool session and a long-running action are the
same thing: *something with a lifetime, a label, progress, and a cancel*.

```
DECLARATION  (split, per the ruling)      RUNTIME  (unified)
  ToolDescriptor          ─┐
  EntityActionDescriptor  ─┴──────────►     IActivity
```

⇒ One status bar, one Escape rule, one exclusivity check, one place to look for *"what is going on?"* —
without collapsing two vocabularies the user deliberately separated.

## 3. Declarations

```csharp
// ── Tools ────────────────────────────────────────────────────────────────
public enum ToolModality { Modal, Modeless }

public sealed record ToolDescriptor(
    string       Id,
    string       Label,
    ToolModality Modality,
    bool         ShowOnToolbar      = false,   // toolbar presence is opt-in (user ruling)
    bool         ToggleOnReactivate = false,   // re-activating does NOT cancel unless set
    bool         ProducesUndoEntry  = false);  // 🔷 hook only — see §8

// ── Actions ──────────────────────────────────────────────────────────────
public enum EntityActionGroup     { View, Edit, Destructive }
public enum EntityActionExecution { PerEntity, Selection }
public enum ActionConcurrency     { Concurrent, Restart, Drop, Queue }   // re-dispatch of the SAME action
public enum ActionExclusivity     { None, Exclusive }                    // blocks OTHER activities
public enum ActivityVisibility    { Auto, Always, Never }                // status-bar presence

public sealed record EntityActionDescriptor(
    int                   Id,                                  // a GlobalActionIds value
    string                Label,
    EntityActionGroup     Group,
    EntityActionExecution Execution        = EntityActionExecution.PerEntity,
    bool                  CancelsModalTool = false,             // false ⇒ *transparent* (AutoCAD)
    ActionConcurrency     Concurrency      = ActionConcurrency.Concurrent,
    ActionExclusivity     Exclusivity      = ActionExclusivity.None,
    ActivityVisibility    Visibility       = ActivityVisibility.Auto,
    bool                  ProducesUndoEntry = false);           // 🔷 hook only

// ── Tool ids are their own vocabulary, NOT GlobalActionIds ───────────────
public static class ToolIds { public const string Select = "select"; /* … */ }
```

⚠ **Every new field defaults to today's behaviour**, so adopting the API is additive and no existing
registration changes meaning.

## 4. The runtime — `IActivity`

```csharp
public enum ActivityState { Running, Suspended, Completed, Cancelled, Faulted }

public readonly record struct ActivityProgress(float? Fraction, string? Message);
//  Fraction == null ⇒ indeterminate. Message is what the status bar shows.

public interface IActivity
{
    string             Id              { get; }   // tool id or action id, for diagnostics
    string             Label           { get; }   // dynamic-capable, shown to the user
    ActivityState      State           { get; }
    bool               HoldsInputFocus { get; }   // a modal tool session or a picker
    ActivityProgress   Progress        { get; }
    CancellationToken  Cancellation    { get; }
    Task               Completion      { get; }   // faults surface here — never `async void`
    void Cancel();
}
```

> ### 🔒 The tool owns its own suspended presentation — user ruling, 2026-08-10
>
> *"Hiding a suspended tool could be considered an internal property of the tool itself — the tool should
> be able to read its own suspension state and suppress drawing if it wants to."*
>
> ⇒ **The controller never suppresses drawing.** A suspended tool keeps receiving `Draw` and simply reads
> `Activity.State == Suspended` to decide for itself. Default: **keep drawing** (today's behaviour).
>
> ⚠ **Why the activity handle rather than a new gizmo method:** `IGizmoInteractionHandler` lives in
> `GizmoMap.Contracts`, shared by every subsystem — adding `OnSuspended(bool)` there is a wide blast
> radius for a presentation choice. Handing the tool its `IActivity` at activation costs no shared
> interface change. ⇒ [UC-16](UX_Interaction_UseCases.md#3-the-modal-stack--interrupt-and-return).

**What this buys, each traceable to an existing defect or a stated need:**

| | |
|---|---|
| `Completion` is a `Task` | 🔴 kills `async void` — the two live handlers currently swallow exceptions ([UXI-17](UX_Issues.md#uxi-17)) |
| `Progress` + `Label` | the status-bar surface long-running work needs (the VS Code obligation) |
| `Cancellation` + `Cancel()` | one Escape rule for tools **and** long work |
| `HoldsInputFocus` | the arbitration input — see §5 |
| `State.Suspended` | the tool stack's suspend/resume, generalised |

## 5. The host — one per subsystem

```csharp
public interface IInteractionHost
{
    // ── Tools ────────────────────────────────────────────────────────────
    ToolDescriptor?               ActiveModal { get; }        // = top of the focus stack
    IReadOnlyList<ToolDescriptor> ModalStack  { get; }
    void        Activate (string toolId, Entity? target = null);   // replaces the top
    IDisposable PushModal(string toolId, Entity? target = null);   // suspends the top; dispose resumes

    // ── Actions ──────────────────────────────────────────────────────────
    IActivity Dispatch(int actionId, EntityActionContext ctx);     // policy applied here

    // ── Everything with a lifetime ───────────────────────────────────────
    IReadOnlyList<IActivity> Activities { get; }                   // status bar binds here
    void CancelTop();                                              // Escape
    event Action<IActivity> ActivityChanged;                       // toolbar/status refresh

    // ── Thread affinity — §6 ─────────────────────────────────────────────
    void          Post(Action work);        // run on the next tick
    TaskScheduler TickScheduler { get; }
}
```

### Arbitration, in one place

Evaluated by `Dispatch` **before** the handler runs:

| Order | Rule |
|--:|---|
| 1 | **Exclusivity gate** — if any running activity is `Exclusive`, reject and surface *why*. If **this** action is `Exclusive` and anything else is running, reject or queue |
| 2 | **Concurrency policy** for the same action id — `Concurrent` / `Restart` (cancel the previous) / `Drop` (ignore) / `Queue` |
| 3 | **Modal-tool interaction** — if `CancelsModalTool`, clear the modal stack. Otherwise the action is **transparent** and the stack is untouched |
| 4 | Run the handler, which may `PushModal(...)` for a scoped interruption |

🔒 **Focus is the only currency.** A modal tool holds it; whatever takes it displaces it; a transparent
action never asks for it.

## 6. ⭐ Make the documented path the only reachable one

**⚠ The `SynchronizationContext` proposal is withdrawn.** It would have invented a parallel threading
mechanism alongside a documented one (snapshot + ECB + volatile flags) — precisely the mistake this
programme keeps warning itself about. **The engine already has an answer; the API's job is to make it
unbypassable.**

### The root cause is *reachability*, not threading

Today's handler is a closure over `EditorSubsystem`'s fields, so `_world` — the **live repository** — is
simply in scope. Nothing had to go wrong for the wrong thing to be easy.

```csharp
// today: the handler can reach anything the subsystem can
async void () => { … _world.IsAlive(target) … }        // live EntityRepository, in scope by accident
```

⇒ **Give the handler a context that exposes only what rule 7 permits, and the whole error class
disappears by construction:**

```csharp
public sealed class EntityActionContext
{
    public Entity                Entity        { get; }   // clicked / fan-out target
    public IReadOnlyList<Entity> Selection     { get; }
    public ISimulationView       View          { get; }   // 🔒 READ-ONLY snapshot — no GetComponentRW
    public IEntityCommandBuffer  Commands      { get; }   // 🔒 structural writes, played back on the tick
    public FdpEventBus           Bus           { get; }   // ✅ lock-free event publish
    public IInteractionHost      Tools         { get; }   // PushModal etc.
    public string                CurrentPerspective { get; }
    public CancellationToken     Cancellation  { get; }
}
```

| Exposed | Why |
|---|---|
| `ISimulationView` | rule 7's read side; `GetComponentRW` is *intentionally absent*, so a handler **cannot** write components even by mistake |
| `IEntityCommandBuffer` | rule 7's write side — structural changes recorded, played back on the main thread |
| `FdpEventBus` | documented thread-safe; `SeedTargetCommand` already uses it correctly |
| ❌ **not** `EntityRepository` | the live repo is never handed to a handler. **This is the entire fix** |

### ✅ Verified 2026-08-10 — the path exists end to end and needs **no new machinery**

The previously-unverified ECB lifecycle, traced:

| # | Link | Evidence |
|--:|---|---|
| 1 | `ISimulationView.GetCommandBuffer()` returns a **per-thread** ECB | `EntityRepository.View.cs:33-35`, backed by `ThreadLocal<EntityCommandBuffer>` with **`trackAllValues: true`** (`:13`) |
| 2 | An ECB records **events as well as structural ops** | `OpCode.PublishUnmanagedEvent = 8`, `PublishManagedEvent = 9` (`EntityCommandBuffer.cs:24-25`) |
| 3 | The kernel flushes **every** thread's buffer, **on the main thread**, each frame | `ModuleHostKernel.cs:519-532` — BeforeSync phase iterates `_liveWorld._perThreadCommandBuffer.Values` and plays back any with `HasCommands` |
| 4 | The Editor runs that kernel | `EditorSubsystem.cs:585` — `new ModuleHostKernel(_world, accumulator)`; `Kernel.Update()` drives it (`:1617`) |

> ⇒ **A continuation on *any* thread can call `view.GetCommandBuffer()`, record, and be flushed correctly
> on the main thread at the next BeforeSync.** `EntityActionContext.Commands` is simply that buffer.
> **Nothing new is built** — this is the mechanism rule 7 points at, already wired.

⚠ **Three notes from the trace, none blocking:**

| | |
|---|---|
| `EntityRepository.FlushCommandBuffers()` is **dead public API** | **0 production callers**, yet its doc says *"In production the scheduler calls this"*. The scheduler in fact **inlines the same loop** (`ModuleHostKernel.cs:523-531`). A doc/API inconsistency worth a one-line fix, not a defect |
| **`ThreadLocal` + thread-pool threads** | a continuation may land on any pool thread and gets its own tracked buffer. Correct for flush; buffers accumulate per *distinct* pool thread over a long session. Worth watching, not fixing blind |
| **Timing** | ECB ops land at the next BeforeSync. ⚠ But a direct `Bus.Publish` is *also* only readable after `SwapBuffers`, so deferring to the ECB is **no regression** in latency |

## 6b. 🔒 Threading, the ECB, and progress — ruled 2026-08-10

> **User:** *"Running an action on different threads is problematic. The command buffer already has a
> full API we would duplicate in the context — that does not seem elegant. Actions are likely not heavy
> on command-buffer usage; creating a new one per action is fine **providing the action is always kept on
> the same thread**. Alternatively assess pros/cons of keeping async but restricted to a single thread and
> letting handlers yield."*

### ⭐ The two options are not alternatives — one is the other's precondition

**A per-action ECB is safe only if the handler never hops threads**, and with free-threaded `async` that
cannot be guaranteed — a continuation resumes wherever the pool puts it. ⇒ **single-threaded async is not
the alternative to the per-action buffer; it is what makes it legal.**

### Assessment

| | **Free-threaded async** *(today)* | **Single-threaded async — handlers yield** ✅ |
|---|---|---|
| Handler may read the live world | ❌ rule 7 exposure; **today's code violates it** (`_world.IsAlive` off-tick) | ✅ it *is* main-thread code — no exposure at all |
| Per-action ECB | ❌ unsafe — `EntityCommandBuffer` requires one buffer per thread | ✅ **safe**, which is the shape you asked for |
| Duplicating the ECB API in the context | forced, to hide the thread problem | ✅ **not needed** — hand the handler the real `IEntityCommandBuffer` |
| Determinism / headless tests | ❌ timing-dependent | ✅ pump N times and assert |
| Reasoning burden per handler | every author must know rule 7 | ~none |
| CPU-heavy work | ✅ never blocks the frame | ⚠ **blocks the frame** unless the handler explicitly `Task.Run`s and marshals back |

⚠ **The one real cost is the last row**, and it is narrow: heavy work becomes an explicit opt-out
(`await Task.Run(...)`, then resume on the tick) instead of the implicit default. The discipline still
exists — it just applies to the rare case rather than every handler.

### 🔒 Implement it **narrowly** — not as an ambient process-wide context

⚠ [Correction 21](UX_Tasks_Detail.md#corrections) withdrew a process-wide `SynchronizationContext`, and
that withdrawal stands. **This is a different, narrower thing:**

| | Withdrawn | Ruled |
|---|---|---|
| Scope | **ambient, whole process** — changes every `await` in the app, including DDS, file IO, orchestrator `Task.Run` paths | **only the handler invocation** — set around the call so the state machine captures it, then restored |
| Motive | to "fix" a race that did not exist | to keep handlers on the tick thread **by choice** |
| Relation to the docs | invented a mechanism parallel to snapshot + ECB | **uses no background mechanism at all** — main-thread code is the most conservative option |

The host pumps queued continuations once per `Update()`. `RunContinuationsAsynchronously` on the pick
`TaskCompletionSource` then works *for* us: the continuation is posted to the host's queue rather than
running inline.

### ⇒ The ECB decision

**One `EntityCommandBuffer` per action dispatch**, handed to the handler as-is — the full existing API,
nothing duplicated.

| | |
|---|---|
| ✅ **No API duplication** | the context carries `IEntityCommandBuffer`, not forwarding methods |
| ⭐ **Cancellation becomes atomic** | a cancelled action's buffer is **dropped, never played back** ⇒ no partial commit ([UC-27](UX_Interaction_UseCases.md)). The shared per-thread buffer cannot do this — you cannot un-record |
| Playback | the host plays it back on the main thread at a known point; single-threading makes that trivially legal |

## 6d. ✅ Where the host's playback sits — resolved 2026-08-10

### The frame, as it actually runs

```
EditorSubsystem.Update()                                    EditorSubsystem.cs
  … canvas.Update()          ← tool/UI input, menu clicks → handler STARTS here
  _gizmoBuffer.EndFrame()                                   :1615
  ┌─────────────────────────────────────────────────────┐
  │ ⭐ HOST PUMP + PLAYBACK GOES HERE                    │   ← the ruling
  └─────────────────────────────────────────────────────┘
  _kernel.Update()                                          :1618
      ├ ExecutePhase(BeforeSync)                            ModuleHostKernel.cs:520
      ├ FLUSH all per-thread ECBs   ← main thread           :523-531
      └ Bus.SwapBuffers()           ← events become visible :534
  _aiCoordinator.DrainPendingCallbacks()   ← ⭐ precedent    :1624
  _regenerationScheduler.Tick() … _orchestrationBus.SwapBuffers() …
```

### 🔒 Ruling: drain the pump and play back **immediately before `_kernel.Update()`**

| Why | |
|---|---|
| ⭐ **Same-frame commit** | the kernel flush is **before** `SwapBuffers()` (`:523-534`), so ops played back just before `Update()` are visible to systems **in the same frame's Simulation** |
| ⭐ **A handler that never awaits commits in the frame it was clicked** | it starts during `canvas.Update()`, which is earlier in the same method |
| **Safest window** | no ECS phase is executing — the kernel's own flush likewise runs *after* `ExecutePhase` returns, so playback outside a phase is the established pattern, not a novelty |
| **Deterministic ordering** | multiple actions completing in one frame play back in completion order |

### ⭐ The precedent to copy, including its comment style

`_aiCoordinator?.DrainPendingCallbacks()` (`:1620-1624`) is **exactly this pattern already in production** —
a main-thread drain of work queued by a background worker, placed deliberately in the frame with a
documented reason:

> *"Any `BTreeInterpreter` pointer swaps queued by the background ALC worker are applied here, between
> kernel ticks, so no active simulation tick can observe a half-swapped pointer."*

⇒ **The interaction host's pump is the same shape** and belongs beside it in the frame narrative, with an
equally explicit comment.

⚠ **Placed *before* `Kernel.Update()` rather than beside `DrainPendingCallbacks` (after it)** — the
after-position would push every action's commands to the **next** frame, for no gain.

### ⚠ One thing not pinned

Exactly where **ImGui panel drawing** sits relative to `EditorSubsystem.Update()` — i.e. whether a menu
click is dispatched before or after this method runs in the same frame. It changes only *which* frame a
synchronous handler commits in, never correctness. **Confirm during implementation** by logging the frame
index at dispatch and at playback.

## 6c. Progress reporting — part of the design, per the ruling

> **User:** *"A long-living cancellable **must** have a visible indication and cancellation. If it is an
> exclusive modal operation it should be a **modal dialog with a progress bar**. If non-exclusive, it must
> have a **progress bar and a cancel icon in the status bar**. The progress-reporting API must be part of
> the design."*

```csharp
// Handed to the handler; the only way to report progress.
public interface IProgressSink
{
    void Report(float? fraction, string? message);   // null fraction ⇒ indeterminate
}
// ctx.Progress.Report(0.4f, "Resolving 120 entities…");
```

**Surface is chosen by exclusivity, not by the handler:**

| Exclusivity | Surface | Cancel affordance |
|---|---|---|
| `Exclusive` | 🔒 **modal dialog with a progress bar** — it blocks everything, so it must say so | button in the dialog |
| `None` | 🔒 **status bar: progress bar + cancel icon** | the icon |

🔒 **Registration throws** when a long-running action declares `Visibility: Never`, or when `Exclusive`
has no progress surface — the same *fail-at-composition* stance `GlobalActionRegistry.Register` already
takes on duplicate ids.

⚠ **The status-bar surface is deferred to its own design** ([UXI-27](UX_Issues.md#uxi-27)) — it is a
shared shell component with its own concerns (ordering, multiple concurrent activities, overflow), and
`StatusBar.RegisterSection` already exists (`LocalWindowController.cs:70-75`). **This design owns the
`IProgressSink` contract and the two-surface rule; that one owns the widget.**

## 7. Where each piece comes from

| Piece | Borrowed from |
|---|---|
| **Transparent vs interrupting** actions | **AutoCAD** transparent commands (`'zoom`, `'pan`), 1980s — a registration-time flag on the command |
| **Modal stack**, operators that stay live until finished/cancelled, registration flags | **Blender** modal operators + modal handler stack |
| **Modality as an enum**, not a bool | **Qt** window-modality levels |
| **Concurrency policies** `Concurrent`/`Restart`/`Drop`/`Queue` | reactive streams: merge / switch / exhaust / concat |
| **Progress + cancel instead of a silent lock** | **VS Code** long-operation UX |
| **`SynchronizationContext` on the UI thread** | **WinForms / WPF / Unity** |

⭐ **The only genuinely new part is the composition** — and even that recovers `MapCanvas.PushTool`/
`PopTool`, which this codebase had and deleted.

## 8. Ready for later, deliberately not built now

| Hook | Why it is a hook |
|---|---|
| `ProducesUndoEntry` on both descriptors | [OQ-3](UX_Requirements.md#answered-questions) ruled general undo out of scope. Blender's operators carry exactly this flag; declaring it now costs a field and avoids a later signature break |
| `ActionExclusivity.Exclusive` | no consumer today. 🔒 **Must not ship without a status-bar presence** — a silent lock reads as a hang |
| `ActionConcurrency.Restart` / `Queue` | no consumer today; one `switch` arm each over machinery `Drop` needs anyway |
| `ActivityVisibility.Always/Never` | `Auto` (show if it outlives ~1 frame) covers everything current |

⚠ **Each is a field or an enum member, not a subsystem.** They exist so the *shape* survives contact with
a more complex editor — not because anything needs them this month.

## 9. What is actually needed now

⚠ **Re-ranked after [Correction 21](UX_Tasks_Detail.md#corrections)** — the previous #1 (a
`SynchronizationContext`) is withdrawn, and the "live data race" that justified it did not exist.

| Priority | Item | Justification | Status of the claim |
|---|---|---|---|
| 🔴 **1** | `IActivity` with a `Completion` **task** | the two live handlers are `async void`, so faults are unobserved ([UXI-17](UX_Issues.md#uxi-17)) | ✅ verified — `EditorSubsystem.cs:1462`, `:1479` |
| **2** | `EntityActionContext` exposing **only** `ISimulationView` + `IEntityCommandBuffer` + `FdpEventBus` | makes the [documented ECS path](../HROT-PROGRAMMERS-GUIDE.md) the only reachable one; today `_world` is in scope by closure accident | ✅ verified — `_world` is `EntityRepository?` (`:184`) |
| **3** | `IInteractionHost` modal focus stack | two `_focusedGizmo` arbiters on one bus, each guarding only itself | ✅ verified in code; ⚠ **and no documented invariant covers cross-engine gizmo focus** — §7.2 of the guide documents gizmos extensively but not this, so it reads as an unnoticed gap rather than a design |
| **4** | `Drop` on the two pick actions | double-invoking *Mark Target* starts two concurrent picks | ✅ verified — no guard exists |
| **5** | Status-bar activity list | makes 1-4 visible; `StatusBar.RegisterSection` already exists (`LocalWindowController.cs:70-75`) | ✅ seam verified |

🔒 **Nothing on this list now rests on an unread invariant.** Each row states whether the claim is
code-verified, and #3 explicitly records that the documentation is *silent* rather than supportive.
