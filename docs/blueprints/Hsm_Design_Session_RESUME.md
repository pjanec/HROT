# HSM Visual Editing — Design Session RESUME

> **Read this first when resuming.** Written to survive context compaction.
> **Branch:** `claude/hsm-visual-editing-9ngei4`, based on `claude/blueprint-authoring-status-gm0akp`.
> **Mode:** ⭐ **design session — NO CODE.** The user is asking questions, giving their view on how
> things should work, and we are settling the way forward together. Do not start implementing rows.
> **Companions:** [Issues tracker](Hsm_Visual_Editing_Issues_Tracker.md) ·
> [Concepts primer](Hsm_Concepts_For_Game_AI.md)

---

## 1. Where we are

| | |
|---|---|
| Tracker | **15 open rows**, HSM-001…HSM-015, none done |
| Build | `IOS-IG-SimHost.sln` — 0 errors (69 pre-existing warnings) |
| Tests | `Hrot.Hsm.Editor.Tests` **510/510 green** |
| Committed | tracker, concepts primer, 2 hand-authored SVGs, this file |
| Environment | .NET 8.0.424 at `/root/.dotnet`; `codebase-memory-mcp` 0.10.3 at `/opt/codebase-memory-mcp`, project `home-user-HROT` indexed (166k nodes / 537k edges), warm daemon running |

⚠ **MCP graph tools flap in and out.** When they are disconnected use the CLI, which always works:
`/opt/codebase-memory-mcp/codebase-memory-mcp cli <tool> '<json>'`
(`trace_path` uses `direction: inbound|outbound|both`, **not** `callers`.)

---

## 2. ⭐ Verification discipline — adopted after getting one wrong

A claim of the form *"nothing calls X"* was made from a single grep scoped to `Hrot/` while the code
lived in `FDP/Toolkits/`, additionally narrowed by `grep -v Editor`, whose non-empty output was
misread as empty. **It was wrong and the user caught it.**

**Rules now in force:**
1. Every negative claim needs **(a)** a repo-wide search with no directory filter **and**
   **(b)** a `codebase-memory-mcp` graph query over `CALLS` edges.
2. Prefer **reproducing** a defect with a throwaway xUnit probe in
   `Hrot.Hsm.Editor.Tests`, quoting the real output into the row, then **deleting the probe** and
   confirming the suite is green again. Rows carrying ✅ *reproduced* were established this way.
3. Never let an `echo` of an assumed conclusion sit next to command output — read the output.

---

## 3. Established facts (verified, cite these rather than re-deriving)

### The kernel (`FDP/ExtDeps/FastHSM`)
- **Event-driven, always.** `HsmKernelCore.cs:108-117` — an instance stays in `Idle` while its event
  queue is empty ⇒ RTC never runs ⇒ **no guard is ever evaluated.** A guard is a filter on an event,
  never a poller.
- Phase cycle: `Idle → Entry (dequeue) → RTC (match+guard, exit/effect/entry) → Activity → Idle`.
  Max 10 events drained per tick; RTC loops to 100 iterations, then fail-safes.
- After a transition fires, RTC continues with `currentEventId = 0`, so **epsilon/completion
  transitions run as follow-ups** — but only inside a burst some real event started.
- Action ids and guard ids are `ComputeHash(name)` = **FNV-1a-32 over the name's UTF-16 chars,
  truncated to 16 bits** (`HsmFlattener.cs:385-394`).
- **History is a flag on the composite that owns the children** (`.History()` on the parent,
  `StateDef.HistorySlotIndex`). There is **no history pseudo-state** in this kernel.
- **Timers are a hollow shell** — `TimerDeadlines` is only ever written to `0` in production
  (exit cancellation, hot-reload reset); `StateDef.TimerActionId` is never read by the kernel.
- Parallel-region lane conflicts resolve **first-wins, silently**; priority arbitration is P4/future.
- `StateDef` is a full 32 bytes (1 spare byte, `Reserved29`); `TransitionDef` a full 16.
  **Neither has a parameter-slot field** — see HSM-015.

### The runtime (this exists — do not claim otherwise)
- `HsmTickSystem<T>` — `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`,
  `[UpdateInPhase(SystemPhase.Simulation)]`, registered in `CognitiveRuntimeModule` for `BrainHsm64`
  and `BrainHsm128`, plus two scenarios. Steps `HsmKernel.Update` per entity per tick; also handles
  trace buffers, NLog emission and terminal `BehaviorFinishedEvent`.
- **Exactly one external event pump exists.** Graph query over `CALLS` for production callers of
  `HsmEventQueue.TryEnqueue` (excluding tests/demos/benchmarks) returns four; only
  `HsmTickSystem.Execute` is external, posting `MobilityLost`:
  ```
  ActorCapabilities.CanMove set→cleared
    → CognitiveInterruptSystem writes BrainBlackboard.Interrupt_MobilityLost = 1
    → HsmTickSystem enqueues HsmEvent{ EventId_MobilityLost }
    → CognitiveCleanupSystem clears it
  ```
  `BrainBlackboard` reserves **two** interrupt slots (`Interrupt_MobilityLost` @126,
  `Interrupt_Reserved` @127). The only shipped brain, `ApcHsmSetup`, uses that one event.
- There is **no** general "post event X to entity E" API and **no** periodic tick.

### The editor
- EH-01…EH-05 **have landed** — the plan doc claiming otherwise is stale (HSM-011).
- Hand-written HSM actions ignore the `instance` pointer entirely and reach the world through
  `context` → `HsmKernelBridge` → `GCHandle.FromIntPtr(WorldHandle)`. Ignoring
  `HsmCommandWriter* writer` and writing channel components directly is the **house pattern**
  (`ApcHsmActions`), not a defect.

---

## 4. The through-line: what actually blocks the user's stated goal

> *"Easily define states and transitions with transition conditions."*

States, transitions and hierarchy already work. Three things stand in the way, in order:

| Blocker | Row | Why it is on the critical path |
|---|---|---|
| No event can be authored | **HSM-009** | a transition needs an event; guards only run when one arrives |
| A bound action/guard cannot resolve | **HSM-013** | GUID-hash registration vs name-hash lookup — never match |
| Nothing can be picked | **HSM-014** | the picker only offers names the asset already contains |

HSM-015 sits behind 013: even once naming is fixed, a **parameterised** primitive has nowhere to
store its params on the HSM side.

---

## 5. Open rulings — waiting on the user

| # | Question | Row |
|---|---|---|
| 1 | Initial state: make `RegionNode.InitialChild` the single source of truth (composite = implicit single region), or keep `IsInitial` and make validator/renderer/persistence region-scoped? | HSM-003 |
| 2 | History: withdraw the pseudo-state palette entries and expose history as a checkbox on the composite (kernel-faithful), or keep UML sugar that lowers onto the parent flag at emit? | HSM-010 |
| 3 | Timers: add kernel arming (needs a ROM field + builder param), or stop offering `TimerAction` in the editor until it exists? | HSM-012 |
| 4 | What is the **canonical name** an HSM binds an action/guard by, and where do a binding's parameters live? | HSM-013, HSM-015 |
| 5 | Whose lane is HSM-013/015? They are *blueprint compiler* defects surfacing in HSM. | — |
| 6 | Does the missing runtime event surface matter now, or is authoring-first acceptable? | — |

---

## 6. Not yet audited

BTree editor · Phase-2 debug/trace surface · JSON round-trip byte-stability and the
migration-equivalence gate · anything visual (renderer geometry, dividers, label placement,
internal-transition dashed loops) · `DEBT-BF-04`.

---

## 7. User preferences observed this session

- Wants **trustworthy, code-grounded** answers — *"no useless quick conclusions"*. Push back is
  earned by sloppy verification, not by bad news.
- Wants findings **recorded in the tracker as they are found**, not held in conversation.
- Is **learning the HSM domain** — explanations should teach the concept and then ground it in this
  kernel's actual behaviour, flagging where the two diverge.
- Per `.claude/CLAUDE.md`: plain-text questions (**never** the `AskUserQuestion` widget); terse docs
  led by visuals and tables; hand-authored SVG over Mermaid for anything non-trivial.

---

## Change log

| Date | Change |
|---|---|
| 2026-08-14 | Created mid-session, before compaction. Covers HSM-001…HSM-015. |
