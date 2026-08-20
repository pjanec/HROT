# HSM Design Session — RESUME

> **Read this first when resuming.** Written to survive context compaction.
> **Branch:** `claude/hsm-visual-editing-9ngei4`, based on `claude/blueprint-authoring-status-gm0akp`.
> **Mode:** ⭐ **design session — NO CODE.** The user is asking questions, giving their view on how
> things should work, and we are settling the way forward together. Do not start implementing rows.
> ⭐ **Read [Hsm_Integration_Map.md](Hsm_Integration_Map.md) first** — the end-to-end chain, cited.
> It exists so this file does not have to re-derive the system from code.
> **Companions:** [Issues tracker](Hsm_Issues_Tracker.md) · [Concepts primer](Hsm_Concepts_For_Game_AI.md) ·
> [Params/variables opening prompt](Hsm_Parameters_And_Variables_OPENING_PROMPT.md)
> ⭐ **Scope widened 2026-08-14:** the tracker now covers **the whole HSM stack** — editor, codegen,
> runtime and kernel — not just visual editing, by user direction. Renamed accordingly.

---

## 1. Where we are

| | |
|---|---|
| Tracker | **18 open + 1 closed**, HSM-001…HSM-019 |
| Build | `IOS-IG-SimHost.sln` — 0 errors (69 pre-existing warnings) |
| Tests | `Hrot.Hsm.Editor.Tests` **554/554 green** (was 510; the merge added 44) |
| Committed | integration map, tracker, concepts primer, opening prompt, 3 hand-authored SVGs, this file |
| Environment | .NET 8.0.424 at `/root/.dotnet`; `codebase-memory-mcp` 0.10.3 at `/opt/codebase-memory-mcp`, project `home-user-HROT` indexed (166k nodes / 537k edges), warm daemon running |

⚠ **MCP graph tools flap in and out.** When they are disconnected use the CLI, which always works:
`/opt/codebase-memory-mcp/codebase-memory-mcp cli <tool> '<json>'`
(`trace_path` uses `direction: inbound|outbound|both`, **not** `callers`.)

---

## 1b. ⭐⭐ Merged the coordinator branch `2026-08-20` — Batch 46 → 98, 428 commits

The blueprint programme did **substantial HSM work** in that window (Batches 58, 59, 67, 71–78, 92).
**Every reproduced row was re-run on the merged tree**; only `HSM-016` had been fixed
(Batch 59 `W3`, railed by `BHU_020`) — the rest reproduce identically.

⭐ **Read these before re-deriving anything about parameters:**
`DESIGN_Parameter_Model.md` *(authoritative)* · `EXPLAINER_Where_Parameters_And_State_Live.md`
*(measurement record)* · `RULINGS.md` *(the ledger — `R-06`, `R-52`, `R-65`, `R-81`, `R-88`, `M-15`,
`M-16`, `M-24`)*.

📌 **Lesson:** the first audit ran on a base 428 commits stale, so one row was raised against a
defect the world had already closed. `.claude/CLAUDE.md` **rule 7** says re-sync from the coordinator
branch at the START of a run — do that before presenting findings, not after.

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

HSM-015 sits behind 013. ⭐ **Both are now UNIMPLEMENTED RULINGS, not open questions** — `R-88`
rules the key as `MethodFqn@offset` for BTree and HSM jointly, and the offset rides inside the
hashed identity so **no ROM change is needed**. 📐 `BTreeBridgeEmitCore` has **50** `MethodFqn`
references; `HsmBridgeEmitCore` has **0**.

---

## 5. Open rulings — waiting on the user

| # | Question | Row |
|---|---|---|
| 1 | Initial state: make `RegionNode.InitialChild` the single source of truth (composite = implicit single region), or keep `IsInitial` and make validator/renderer/persistence region-scoped? | HSM-003 |
| 2 | History: withdraw the pseudo-state palette entries and expose history as a checkbox on the composite (kernel-faithful), or keep UML sugar that lowers onto the parent flag at emit? | HSM-010 |
| 3 | Timers: add kernel arming (needs a ROM field + builder param), or stop offering `TimerAction` in the editor until it exists? | HSM-012 |
| 4 | What is the **canonical name** an HSM binds an action/guard by, and where do a binding's parameters live? | HSM-013, HSM-015, HSM-016 |
| 5 | Whose lane is HSM-013/015/016? They are *compiler / codegen* defects surfacing in HSM. | Q-G of the opening prompt |
| 6 | Does the missing runtime event surface matter now, or is authoring-first acceptable? | — |

---

## 5b. In flight — the parameters/variables consultation

[Hsm_Parameters_And_Variables_OPENING_PROMPT.md](Hsm_Parameters_And_Variables_OPENING_PROMPT.md) —
⭐ **REWRITTEN `2026-08-20` after the merge.** Four of its original seven questions were **already
ruled**, and two of our own premises were **wrong** (we said "whole-DTO binding"; the model moved to
*one params struct per behaviour, action binds a FIELD*. We said role `param`; the enum is
`{Input, State}` with no Param role).

⭐⭐ **The central proposal is no longer a proposal — `R-88` rules it:** the thunk key is
`MethodFqn@offset`, stated for BTree **and HSM** jointly. So it is an **unimplemented ruling**, not
an open design question. 📐 Measured: `BTreeBridgeEmitCore` has **50** `MethodFqn` references,
`HsmBridgeEmitCore` has **0** ⇒ **HSM has the params *supply* half and not the *binding* half.**

What genuinely remains: per-slot binding on a four-slot state (`DEBT-BF-04` — possibly already
discharged by the field-binding ruling), where a guard's params come from, guard side-effect safety
under speculative evaluation, `Scope=Node` across state re-entry and history restore, and whether
the packer's budget assumes one-live-binding-at-a-time.

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
