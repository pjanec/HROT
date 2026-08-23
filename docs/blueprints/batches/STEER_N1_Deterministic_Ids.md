<!--STATUS
state: LIVE
build-state: STEER
updated: 2026-08-23
current-answer: the whole file — a rule-1c steer for N1 ONLY, issued while part B is running. N1 gains
  an owning design it did not have; ⛔ every other item is UNCHANGED.
known-conflict: none. ⛔ This is NOT an amendment to HANDOFF_Regression_Net_Part_B.md — that file is
  untouched (rule 1). The steer is additive: N1 now has a design to build against.
-->
# STEER — **`N1` has a design now** *(rule 1c, additive)*

> 📌 **Part B was dispatched at `7677478f4` and IS RUNNING** *(started-marker `fd0eb02b9`)*. ⇒ ⛔ **rule 1
> forbids amending the handoff and rule 1a does not apply.** ⭐ This is the legal form: a separate file,
> the user relays it, ⭐⭐ **and it changes ONE item.**

## 0. ⭐⭐⭐ WHAT IS UNCHANGED — **read this first**

| ⭐ | |
|---|---|
| ⭐⭐⭐ **`N2` · `N3` · `N4` · `N5` · `N6` and ALL of §0b's doc debt: UNCHANGED.** | ⛔ **Nothing here stops the batch** *(`R-106`)*. If `N1` is already done and green, ⭐ **read §2 and say whether what you built matches** — ⛔ do not redo it |
| ⭐ **`N1`'s TASK is unchanged** | ⭐ the reset, two fresh processes, a byte-identical diff. ⛔ **No scope is added** |
| ⛔ **nothing is withdrawn** | ⭐ this is not a correction of you |

## 1. ⛔⛔ WHY THIS EXISTS — **the coordinator dispatched `N1` without its design**

🔒 **User, `2026-08-23`:** *"where is the design for the deterministic network id allocation and reset"*
⇒ 📐 **There was none.** Only charter `D6` *(a decision row)* and `DESIGN_Regression_Net.md` §7 `N1`
*(one line)*. ⛔ **My own UML obligation ④ says a design with no UML is not dispatchable** — the net's
diagrams cover the **golden harness**, not id determinism.

⭐⭐⭐ **And a `search_graph` pass then found the design that OWNS the allocator, which `D6` never cited:**
📄 **`docs/designs/mgmt-1/DESIGN.md` §5.7** — ⭐ the reset is **already designed AND BUILT** as a
**master-owned, DDS-broadcast** operation. ⛔ **`D6` reads it as a dormant local call. That is wrong**, and
it is wrong in a way that would have cost you time.

📄 **The design now exists: [`DESIGN_Deterministic_Network_Ids.md`](../../DESIGN_Deterministic_Network_Ids.md)** —
`READY-TO-BUILD`, with the inventory, the hazards, a `classDiagram` and a `sequenceDiagram` *(both parse)*.
⭐ **§4 is the five rules; §6 is what `N1` must assert.**

## 2. 🔴🔴 THE ONE THING THAT WILL COST YOU A RED — **hazard ①, measured**

⭐⭐⭐ **`Reset()`'s DEFAULT IS NOT THE CONSTRUCTION VALUE — on both allocators, differently:**

| allocator | construction | allocate | first id fresh | first id after `Reset()` |
|---|---|---|---|---|
| `Hrot.Core.Network.SequentialIdAllocator` | `_next = 1` | `Interlocked.Increment` *(**pre**)* | **2** | 🔴 **1** |
| `EditorSubsystem`'s **private nested** one | `_next = 1000` | `_next++` *(**post**)* | **1000** | 🔴 **0** |

⇒ ⭐⭐⭐ **ALWAYS `Reset(explicitStart)` matching that allocator's construction value. NEVER `Reset()`.**
⛔ Otherwise a reset run and a fresh-process run differ, and **`N1` goes red for a reason that is the
fix's own fault** — ⚠ which is exactly the situation in which someone reaches for an ignore-list.

⚠ **And note WHICH allocator the harness runs:** 🔴 **there are THREE types named
`SequentialIdAllocator`** *(`Hrot.Core.Network` · `EditorSubsystem` private nested · `EditorHarness` test
nested)*. ⛔ **Fixing the `Hrot.Core.Network` one does not touch the editor's.**

## 3. ⭐⭐ THE OTHER THREE, briefly *(full text in the design)*

| ⭐ | |
|---|---|
| ⭐⭐ **the CLUSTER reset is a BROADCAST** *(§5.7)* | clients **pool** ids; the server's `Resp_Reset` is what flushes them. ⛔ A per-node local reset strands the old range. ⚠ **charter `D6` caveat ② called this "a hazard" — 📐 it is a DESIGNED, BUILT PROTOCOL** |
| 🔒 **`LoadingReplay` is UNCHANGED** | §5.7 resets **forward** to a high-water mark for collision avoidance — ⛔ **the opposite direction from determinism.** ⭐ Same method, contradictory intents ⇒ **do not touch the replay path** |
| ⭐ **subscribe ONCE** | `WorldResetEvent` has **three** publish sites; `RegisterWorldResetObserver` *(`ScenarioFileService:84`)* is the seam |

## 4. ⭐ SCOPE, and what I recommend you NOT do

| | |
|---|---|
| ⭐⭐ **BUILD design rule ①** *(one-node: reset on the seam, explicit start)* | that is what the harness needs |
| ⛔ **do NOT build rules ②/③** *(the cluster paths)* | ⭐ **designed, deliberately unbuilt** — the harness is one-node. 📄 Design §7 |
| ⛔ **do NOT collapse the duplicate nested allocator** | ⭐ ruling 9 applies to it, ⚠ **but not under a running batch.** ⭐ **File it and move on** |
| ⭐⭐ **obligation ③ / ⑤ apply as normal** | report whether what you built matches the design's 2 diagrams, and ⭐ **fold any deviation back into `DESIGN_Deterministic_Network_Ids.md`**, not into the report |
