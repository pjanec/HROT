# The interaction API — tools, actions and long-running work

> **Consolidated design, 2026-08-10.** Supersedes the API fragments scattered across
> [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) and [UXI-07](UX_Feature_Tool_Model.md); those keep the
> *evidence*, this holds the *contract*.
>
> **User brief:** *"Design for the future, to be ready for a much more complex editor and cases than we
> have now, including long-running actions. Integrate the old knowledge, design the proper API."*
>
> 🔒 **Built from established models, not invented** — see [§7](#7-where-each-piece-comes-from).

## 1. 🔴 The finding that sets the centre of gravity

**The two async handlers that exist today already race the simulation tick.** Verified chain:

| Link | Evidence |
|---|---|
| The pick `TaskCompletionSource` is created `RunContinuationsAsynchronously` | `EditorMapPickAdapter` — the continuation is *forbidden* from running inline on the completing thread |
| **No `SynchronizationContext` is installed anywhere in the repo** | 2 mentions total, both `useSynchronizationContext: false` (`ExConLogic.cs:447,499`) |
| ⇒ `await` resumes on the **thread pool** | default `TaskScheduler`, no captured context |
| The continuation reads and writes the ECS world | `EditorSubsystem.cs:1463-1471` — `_world.IsAlive(...)`, `_world.Bus.Publish(...)` |
| `FdpEventBus` has **no synchronization at all** | zero `lock` / `Interlocked` / `ConcurrentQueue` / `volatile`; `Dictionary` lookup + stream write |

> ⇒ ***Mark Target* and *Mark Area Targets* mutate a single-threaded ECS bus from a thread-pool thread
> while the tick is running.** 🔴 A data race, live today, in **100 %** of the async handlers that exist.

**This is why "long-running actions" is a threading question before it is a UI question.** Progress bars
and cancel buttons are the visible part; **thread affinity is the part that corrupts state if it is
wrong.**

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

## 6. ⭐ Thread affinity — the proven fix, and it is small

**Install a `SynchronizationContext` bound to the subsystem's tick.** This is exactly what WinForms, WPF
and Unity do; it is the reason `await` "just works" on a UI thread in those frameworks.

```csharp
// pumped once per frame in the subsystem's Update
sealed class TickSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback, object?)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    public void Pump() { while (_queue.TryDequeue(out var w)) w.Item1(w.Item2); }
}
```

| | |
|---|---|
| **Where** | set on the UI/tick thread at subsystem start; `Pump()` once per `Update` |
| **Effect** | every `await` in a handler resumes **on the tick** — 🔴 **the race in §1 disappears with no handler edited** |
| **CPU-bound work** | `await Task.Run(...)` explicitly leaves the tick, and the continuation comes back to it automatically |
| **Cost** | one class, one `Pump()` call, one `SetSynchronizationContext` |

⚠ **This is the single highest-value item in this document.** It is small, it is proven, and without it
*every* long-running action added in future inherits a live data race.

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

| Priority | Item | Justification |
|---|---|---|
| 🔴 **1** | `TickSynchronizationContext` | fixes a **live data race** in 100 % of existing async handlers |
| 🔴 **2** | `IActivity` + `Completion` task | kills `async void`; closes [UXI-17](UX_Issues.md#uxi-17) |
| **3** | `IInteractionHost` focus stack | closes the [two-arbiter defect](UX_Feature_Tool_Model.md#-the-defect-that-changes-this-issues-severity) |
| **4** | `Drop` on the two pick actions | double-invoking *Mark Target* today starts **two** concurrent picks |
| **5** | Status-bar activity list | makes 1-4 visible; the `StatusBar.RegisterSection` seam already exists (`LocalWindowController.cs:70-75`) |

⚠ **Items 1 and 2 are worth doing even if the tool model is deferred** — they are correctness, not
architecture.
