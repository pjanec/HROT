<!--STATUS
state: LIVE
updated: 2026-08-22
current-answer: this whole file — what W3 and W5 built, the half of W5 that is reported rather than
  built, and the three findings.
stale-below: nothing.
known-rot: none.
known-conflict: none.
design-basis: DESIGN_Staged_Live_Write.md §1 (the run-state table) · §6 (W3/W5 rows) · §8 ·
  DESIGN_Time_Architecture.md §10 · HANDOFF_UI_W3_W4_W5_The_Staged_Write.md §0/§2/§3 ·
  R-126 · R-130 · R-63 · R-128.
-->
# ⭐⭐⭐ REPORT — **`W3` + `W5`: uniform staging, and one drain**

> **Design:** 📄 [`DESIGN_Staged_Live_Write.md`](../DESIGN_Staged_Live_Write.md) §1 · §6 · §8 ·
> 📄 [`DESIGN_Time_Architecture.md`](../DESIGN_Time_Architecture.md) §10
> **started at** `3031a624` *(marker `643e461e`)* · **branch** `claude/hrot-implementation-j1jvin`
> ⭐ IDs allocated: **`BP-408`** *(`W3`)* · **`BP-409`** *(`W5`)* · **`BP-410`** *(a second host, fixed)* ·
> **`BP-411`** *(a finding, open)*
> ⛔ **No diagram in this report** — the design owns them.

| item | verdict |
|---|---|
| **`W3`** remove `MIN`'s `WriteFieldNow`; stage in every writable run state | ✅ **built** |
| **`W3`** keep `NoSelectedEntity` · `FieldNotResolvable` · `SizeMismatch` | ✅ **kept** — and `NoDebugSession`, a fourth |
| **`W5`** the resume path stops draining | ✅ **built** |
| **`W5`** *move the restore* out of `RequestStep`/`RequestContinue` | 🛑 **REPORTED, not built** — §3 |
| a second host that would have lost every staged edit | ✅ **found and fixed** — `BP-410` |

---

## 1. ⭐⭐ `W3` — **one path, every run state**

📌 **`R-126`, the user, verbatim:** *"I do not understand how comes that something can be unwritable…
we should be able to write anything anywhere"* ⇒ **running is not a reason to refuse, it is a reason to
STAGE**, and the ledger row names the deletions: *"`RefusedRunning` and `LiveWriteRefusal.NotFrozen` are
deleted; only DATA-shaped refusals survive."*

| what | before | after |
|---|---|---|
| `BlueprintDebugSession.TryWriteWorkingStateField` | `!IsClockHalted()` ⇒ refuse; then a three-way on `IsPaused` | ⭐ **one `StageFieldMutation` call** |
| `DataBreakpointManager.WriteFieldNow` + its interface member | `MIN`'s immediate scratch-buffer write | ⛔ **removed** |
| `LiveWriteRefusal.SimulationAdvancing` | *"The simulation is running — pause it"* | ⭐ **`StagingUnavailable`** — the host has no manager |
| `VariableEditCommit.TargetFor(Running)` | `Nowhere` | ⭐ **`LiveBlackboard`** |
| `VariableEditCommit.Outcome.RefusedRunning` | the running refusal | ⭐ **`RefusedRunState`** — `Replay` only |

### ⚠⚠ This SUPERSEDES ruling 15, and the supersession is argued rather than assumed

🔒 Ruling 15 narrowed ruling 7: *"the change of runtime var makes sense **ONLY if sim is paused on
breakpoint or deterministic time step**. at that time nothing else changes the blackboard."*
⭐⭐ **Its REASON is answered, not discarded:** the worry was that a running sim overwrites the designer's
bytes. 📐 It cannot — the write **stages**, and the kernel's `PreFrame` drain applies it at the **top of
a tick**, before `Input` and before any behaviour runs. ⇒ nothing races it inside that tick.

### ⭐ Two deletions I did NOT make, and why

| kept | why |
|---|---|
| **`IsClockHalted`** *(its only caller was the gate `W3` deleted)* | 📌 *"unreferenced is not unintentional."* It answers *"is the simulation advancing?"* — which `R-126` names as **the one source of "paused"** — and it is railed. ⛔ Losing a caller does not make a predicate wrong. |
| **`Target.Nowhere` / `RefusedRunState`** *(now `Replay` only)* | 📐 **`Replay` has NO production producer** — `RunStateSource.Resolve` yields only `Planning`/`Paused`/`Running`. ⭐ The arm is a second agreement with `VariableEditPolicy`, which already denies the dialog there; ⛔ it is **not** a claim that anyone can reach it, and the rails say so. |

⚠ **The behaviour visibly changes, on purpose** *(§1's table)*: a **toolbar-paused** edit no longer lands
instantly and invisibly — it stays 🟡 **staged** until the clock advances. ⭐ That is what makes `W4`'s
yellow **true** *(`R-130`)*.

---

## 2. ⭐⭐ `W5` — **the resume path is no longer a write path**

🔴 `RequestStep`/`RequestContinue` **restored the post-tick snapshot AND drained**. Once the coordinator
wired the kernel's `PreFrame` `ResumeAndDrainSystem`, that was a **second implementation of "apply the
staged bytes"** — ruling 9's exact shape, and the easy-to-miss kind, because both were correct alone.

| ⭐ | |
|---|---|
| **No observable timing changed** | 📐 Measured: `DrainPendingMutations` wrote into `((ISimulationView)repo).GetCommandBuffer()`, which the kernel plays back **during** the tick ⇒ the bytes already landed at the start of the next tick — exactly where the `PreFrame` drain puts them. **Same boundary, one implementation.** |
| **It is what makes `W3`'s toolbar arm work at all** | A toolbar pause never calls either request method, so a drain living only there could never apply that edit — 📌 `R-126`'s reason for making the drain a **PULL**: *"no path can forget to raise what is never raised."* |

---

## 3. 🛑 **THE HALF OF `W5` I DID NOT BUILD — "move the restore"**

📄 §6's `W5` row and the handoff both say *"move the restore out of `RequestStep`/`RequestContinue`"*.
⛔ **I did not, on three measured grounds, and I am reporting rather than inventing a way.**

| # | ground |
|---|---|
| **①** | 📐 **The seam it would move through no longer exists.** `DESIGN_Time_Architecture.md` §10 once drew `ResumeAndDrainSystem --> IStagedWrites : restore-then-drain` with a **`RestorePostTick()`** member. ⚠ That member was **deliberately trimmed** on `2026-08-21` while `W1`/`W2` were built — §10 carries the correction in its own words, and the shipped interface is `HasPending · IsRewound · DrainInto · TryGetPending`. |
| **②** | ⛔ **Putting it back is a CROSS-LANE edit.** It means changing `Fdp.Core`'s `IStagedWrites` **and** `Fdp.ModuleHost/Time/ResumeAndDrainSystem.cs` — the TIME lane's. 📌 `R-128`: *"a cross-lane edit is a STOP-and-report, not a judgement call."* |
| **③** | ⭐⭐ **And it is not a staged-write concern.** The restore undoes **this class's own rewind** *(`OnHit` rewound `_liveRepo ← _preTickSnapshot`)*. 📌 `R-63` reads *"the resume path restores the post-tick snapshot **AND DRAINS ITSELF**"* — ⭐ **the duplicate is the second half**, and that is the half this batch removed. |

⇒ ⭐ **What a future batch would need**, stated so it is not re-derived: either the TIME lane adds
`RestorePostTick()` back to the seam and `ResumeAndDrainSystem` calls it before `DrainInto`, **or** the
design accepts that the restore is the manager's private bookkeeping and closes the `W5` row. ⚠ **The
second is my lean** — the drain skipping while `IsRewound` already encodes the ordering the move was
meant to guarantee.

---

## 4. ⭐⭐⭐ THREE FINDINGS

| # | finding |
|---|---|
| **`BP-410`** ✅ fixed | ⚠⚠ **A SECOND HOST WOULD HAVE SILENTLY LOST EVERY STAGED EDIT.** 📐 `CgfSubsystem:588` is the **only other production site constructing a `DataBreakpointManager`**, and it registered `DebugSnapshotProvider` + `DataBreakpointSystem` but **not** `ResumeAndDrainSystem`. ⇒ once `W5` took the drain out of the resume path, a staged edit there would queue and never apply — 📌 *"accepted and silently discarded"*, the failure `MIN` existed to end, relocated. ⭐ Fixed with the same one-line wire, and **railed as a NEGATIVE**: `WithNoDrainRegistered_AStagedEditNeverLands` drives a rig built **without** the wire. ⇒ a third host reddens instead of losing edits. |
| **`BP-411`** ⛔ open | ⚠⚠ **`W3` made the greyed-OK-with-tooltip path UNREACHABLE.** 📐 `CommitRefusalReason` needs an active session **and** `TargetFor == Nowhere`; `W3` left `Nowhere` to `Replay`, and `VariableEditPolicy` answers `Denied` there ⇒ no session ⇒ no tooltip, ever. ⛔ **Not deleted** — the affordance exists because of a **user ruling** *(`2026-08-17`)* that nothing retracts, and 📌 *"unreferenced is not unintentional."* ⭐ Railed as a tripwire over **every** `VariableRunState`. |
| **`R-63`** ✅ corrected | ⚠ **The ledger carried a state claim my own change made FALSE.** `R-63`'s row said *"`RequestStep`/`RequestContinue` restore … **and THEN drain**"*. 📐 Its machine probe *(`_liveRepo.SyncFrom(_postTickSnapshot);`)* **still matched**, because the restore stayed — 📌 exactly §M's warning: *"the quote still exists in a document; the CODE changed."* ⭐ Corrected in the same commit, with the DECISION half left intact. |

---

## 5. ⭐⭐ THE RAILS — **21 re-expressed, 0 deleted; 3 new; 4 revert probes, all red**

| # | probe *(un-applied by its INVERSE edit, never `git checkout`)* | result |
|---|---|---|
| **1** | re-add `if (!IsClockHalted()) return false;` to the session | 🔴 **3** |
| **2** | re-add `DrainPendingMutations` to `RequestContinue` | 🔴 **1** *(the resume must leave the queue alone)* |
| **3** | `TargetFor`: `Running` → `Nowhere` | 🔴 **4** |
| **4** | `TryWrite` returns `Succeeded` instead of `StagingUnavailable` | 🔴 **1** |

⚠⚠ **A FIFTH PROBE I DID NOT RUN, and why — stated rather than glossed.** 📌 `BP-402` ①: *a probe that
reddens nothing is a finding about the RAIL.* 📐 Removing the `CgfSubsystem` drain line would redden
**nothing**: the suite that could reach that composition root is
`Hrot.ClusterRunner.Integration.Tests` *(`BreakpointSubsystemWiringTests` is exactly that shape)*, and
📌 **`BP-378` — it cannot be gated.** ⭐ What is railed is the **consequence**
*(`WithNoDrainRegistered_AStagedEditNeverLands`)*; ⛔ the line itself is not, and I am saying so.

⚠ **Every red this batch produced was a rail asserting the OLD design.** ⭐ None was deleted.

| where | how many | what changed |
|---|---|---|
| `TheToolbarPauseWriteLandsTests` | **4** re-expressed, **2** new | ⭐⭐ `UnderAToolbarPause_TheWriteLands…` **inverted** to `…TheEditStages_AndLandsOnTheFirstAdvancingFrame` — ⚠ and the mirrored claim still bites: the old rail checked the value did not **drift**, the new one checks it does not **land early**. `TheImmediateWriteDoesNotQueue…` replaced by `WithNoDrainRegistered_AStagedEditNeverLands`. ⭐ New: `WhileABreakpointHoldsARewoundView_TheDrainWaits` *(`R-63`, so `IsRewound` cannot be deleted as "defensive")*. |
| `PendingMutationTests` · `StagedFieldWriteEntryPointTests` · `SurgicalFieldWriteTests` · `IntegrationTests` | **7** re-expressed | ⭐ their CLAIMS are untouched *(last-write-wins · the N+1 boundary · the surgical byte range · managed routing)* — only the step that drains moved, so each names two steps via `ResumeThenDrain`. ⭐⭐ **One got STRONGER**: `Drain_AppliesAtN_Plus_1_BoundaryNotN` can now assert that the restore **alone** leaves the mutation queued. |
| `TheEditDialogIsDrawnTests` · `TheWriteWhilePausedTests` | **2** re-expressed, **1** split, **1** new | `AFreeRunningRefusalStillGreysOk…` → `WhileFreeRunning_TheDialogIsAnEditorWithALiveOk`; the target matrix's `Running` row moves to `LiveBlackboard`; `WhileFreeRunning_TheLiveWriteRefuses…` **split** so the still-true `Replay` half survives. ⭐ New: `AfterW3_NoRunStateProducesAGreyedOkTooltip` *(`BP-411`'s tripwire)*. |

---

## 6. ⭐ GATES — **run ONCE, at the end** *(`M-37`)*

⭐ Baseline = **`W4`'s table**; base sha **`3031a624`**.

| gate | env | `--no-build` | result | Δ |
|---|---|---|---|---|
| **solution build** | — | ⛔ builds | ⭐ **0 errors, 0 compiler warnings** *(the 12 warnings are the pre-existing `MessagePack` NuGet advisories)* | — |
| `Hrot.Editor.AiShared.Tests` | **Xvfb** | ✅ | **1846 / 0 / 0** | ⭐ **+2 — mine** |
| `Hrot.Blueprints.Tests` | **Xvfb** | ✅ | **3887 / 0 / 10** | ⭐ **+1 — mine** *(a `[Fact]` became a 2-case `[Theory]`)* |
| `Hrot.Diagnostics.Breakpoints.Tests` | **Xvfb** | ✅ | **152 / 0 / 0** | ⭐ **+1 — mine** |
| `Hrot.BTree.Editor.Tests` | **Xvfb** | ✅ | **622 / 0 / 0** | **0** |
| `Hrot.Hsm.Editor.Tests` | **Xvfb** | ✅ | **555 / 0 / 0** | **0** |
| `Hrot.Editor.Tests` | **Xvfb** | ✅ | **209 / 0 / 0** | **0** |
| `Hrot.Smoke.Tests` | **Xvfb** | ✅ | **4 / 0 / 0** | **0** |
| `Hrot.ClusterRunner.Tests` | **Xvfb** | ✅ | ⚠ **262 / 2 / 0** | **0** — the `D003_*` pair, unchanged |
| ⚠⚠ `Fdp.ModuleHost.Tests` | **Xvfb** | ✅ | 🔴 **192 / 6 / 0** | **0** — ⛔ **CROSS-LANE, unchanged, not fixed** *(`R-128`; the same six as `W4`'s table — Convoy · SoD · provider assignment. 📐 My diff touches zero files under `Fdp.ModuleHost`)* |
| **tracker** | — | — | ⭐ **OK — open 87 / done 259 (+1 refuted)** | +1 open, +3 done |
| **goldens** | — | — | ⭐⭐⭐ **ZERO moved** — 📐 22 files, +752 / −293; ⛔ no `.approved.`/golden/snapshot/`.verified.` file in the diff, checked by name | — |
| **rulings** | — | — | ⭐ **22/22 verified** *(incl. the corrected `R-63`)* | — |
| **design digest** | — | — | ⭐ **OK** | — |
| **working tree** | — | — | ⭐ **CLEAN after every suite run** | — |

---

## 7. ⭐ LANE CHECK

⭐ Files touched: `Hrot.Blueprints.Editor` · `Hrot.Diagnostics.Breakpoints` + tests ·
`Hrot.Editor.AiShared` + tests · `Hrot.Editor` · **`Hrot.CGF`** *(the `BP-410` wire)*.
⛔ **Nothing under `Fdp.Toolkits/Time/`, `Fdp.ModuleHost`, `Hrot.Orchestrator` or `ModuleHostKernel`**
*(`R-128`)* — ⚠ **and §3 is exactly the item that would have required it, which is why it is reported.**
⭐ ids are **`BP-`**.

## 8. ⭐ WHAT IS OPEN

| | |
|---|---|
| 🛑 **`W5`'s restore half** | §3 — needs a TIME-lane seam change or a design decision |
| ⛔ **`BP-411`** | the unreachable greying, awaiting the user's call |
| ⛔ **`BP-407`** | a row with no `ClrType` yellows but shows its applied value |
| ⛔ carried | **`BP-399`** · **`BP-403`** · **`BP-405`** *(unblocks on `Q44-B`)* · **`L6`** |
