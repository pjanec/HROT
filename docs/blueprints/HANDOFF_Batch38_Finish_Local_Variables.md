# HANDOFF — Batch 38: finish `BP-57` — the rail Batch 37 was never asked for, and the authoring half

> 📌 **Dispatched at `eb89ebaf`** · ⚠⚠ **§1.3 and §3.1 REWRITTEN and RE-DISPATCHED at `PENDING`.**
> ⭐ **Q27-A was revised from A1 to A3 by user ruling on `2026-08-13`** — verified not present in the
> tree your last run built from (run starting `02fb66db`), so rule 1 does not bite.
> ⛔⛔ **The earlier §1.3 asked for a REFUSAL RAIL. There is none — build the storage.**
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-57` is *referenced*; `BP1670+` is the next free
> diagnostic. **You allocate everything new** (rule 5).
>
> 📄 **[Q27](Architect_Question_27_Local_Variables.md) is SETTLED and NOT reopened.** ⚠⚠ **Read its new
> `2026-08-13` correction banner first** — it records two coordinator errors, and **§1 below is Q27-B's
> own ruling, which Batch 37 was never asked to build.**
>
> ⭐ **After this batch `BP-57` closes.**

---

## 0. Scope — and an apology that is load-bearing

| | |
|---|---|
| **§1** 🔴🔴 | ⭐ **suspension-surviving storage** — a local silently reverts to its default across a suspension **today, on the merged tree**. ⚠ **Q27-A was revised to A3 on `2026-08-13`: implement, do not refuse** |
| **§2** 🔴 | a **dangling** local/variable reference emits `s.__var_-1` — **invalid C#**, no diagnostic |
| **§3** ⭐ | the **authoring half** — declare, target, rename, delete. Without it a local is JSON-only |
| **§4** 📌 | one misplaced doc comment |

⛔ **`BP-226`/`BP-227` are NOT in this batch** — they are about the *asset* index space, which locals
never enter. **Batch 39.** Folding them in would muddy revert-goes-red on both, exactly as Batch 37
correctly refused to.

⚠ **§1 and §2 are correctness; §3 is volume.** ⭐ **If the batch has to stop early, stop after §2** —
a silent wrong value outranks an authoring gesture. **Say where you stopped.**

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** suspension storage | ⭐ **all of it** — the predicate (§1.2) **and** the entry-block reset (§1.3) | the fixtures |
| **2** dangling ref | ⭐ **where it fires** | the tests |
| **3** authoring UI | the delete/rename maintenance (§3.3) | ⭐ **declare + picker — mirror-pattern, entirely Sonnet** |
| **4** doc comment | — | 🟢 trivial |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | The **tracker + detail docs are yours** for this batch |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

---

## 1. 🔴🔴 A local does not survive a suspension — ⭐ **make it survive**

### 1.1 The evidence — coordinator-probed on the merged tree, not reasoned

`Set(local=7) → Delay(0.4) → Get(local) → SetComponent(Ammo)`, AiPrimitive, real emit:

```csharp
int __loc_Carry = 0;              // ← re-run on EVERY entry, ABOVE the phase dispatch
byte __t8 = ws.__phase;
__block_entry:
  __loc_Carry = 7;
  ws.__phase = 1;
  return NodeStatus.Running;      // ← suspend: the C# frame dies, and the local with it
__block_wait_resume_0:
  var __t3 = __loc_Carry;         // ← next frame: reads 0, NOT 7
  __wc5.Ammo = __t3;
```

⭐ **`__phase` and `__waitUntilTime` live in `ws` precisely because they must cross that boundary. The
local does not.** ⚠⚠ **Diagnostics on that compile: none. It succeeds and produces a wrong value.**

### 1.2 ⛔⛔ The trap — **the shared latency predicate is incomplete, and its own doc denies it**

`MacroLatency` (`Compiler/Transform/MacroLatency.cs`) says *"⛔ Do not write a second latent-detection
rule. There is exactly one question — can this suspend?"* ⚠ **There are already two, and they
disagree:**

| | |
|---|---|
| `MacroLatency.IsLatent` | `LatentDelayNode or WaitForChannelNode or WaitForEventNode` — **three node kinds** |
| `InstanceLowering:16-21` *(and `AiPrimitiveLowering:91`, `WaitLowering_*`)* | `IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent` ⭐ **or `IrOp_InlineActionCall`** |

⭐ **`IrOp_InlineActionCall` comes from a `ChannelCommandNode` with `ActionFqn` set**
(`Stage5.ScheduleInlineActionNode`, whose own doc says *"Running suspends"* and *"produce the
suspend/resume block split"*). ⇒ **that node suspends and `MacroLatency` does not know it.**

⚠⚠ **So this is not only your problem.** If confirmed, `BP1661` and collapse legality (Q26-F) **miss the
same case today** — a macro whose only latent node is an inline action reads as synchronous.

📐 **Yours to decide, and say which:**

| | |
|---|---|
| **A** | Fix `MacroLatency` to match the lowering, and dispatch §1.3's storage choice on it ⇒ ⭐ **one definition, three call sites corrected at once.** ⚠ **May turn existing `BP1661`/collapse tests red** — if it does, **that is the finding**, not a regression to paper over |
| **B** | Dispatch on a check of your own and file the divergence ⇒ ⛔ **this is writing the second rule the file forbids.** Do not choose it without saying why |

⭐ **The lean is A.** ⚠ **Confirm the `ChannelCommandNode` claim with a test before acting on it** — it
is coordinator reasoning from doc comments and one grep, and **five consecutive batches have corrected
this coordinator's reasoning about the code.** If it is wrong, say so and A shrinks to a no-op.

### 1.3 ⭐⭐ The rule to build — **implement, do NOT refuse**

⚠⚠ **This section was rewritten `2026-08-13` before you read it.** ⭐ **[Q27-A was revised from A1 to
A3 by user ruling](Architect_Question_27_Local_Variables.md).** An earlier draft of this handoff asked
for a refusal rail. ⛔ **There is no refusal rail. Build the storage.**

> *"In a suspendable graph there can't be true C# stack locals, so all must be blackboard-allocated
> vars. From the user's perspective these both are just graph vars, aren't they?"*

⇒ **Two storage classes, one designer-visible meaning:**

| graph | storage | initialised |
|---|---|---|
| **cannot suspend** | a C# local | top of the body — ⭐ **today's behaviour, do not touch it** |
| ⭐ **can suspend** | a **graph-scoped blackboard slot** | ⭐⭐ **in the ENTRY BLOCK, not at the top of the method** |

⭐⭐ **The entry block is the correct reset point and this is the crux of the whole item.** It is
reached **only when `__phase == 0`** — a fresh logical invocation — and the phase is cleared before the
final block, so the next invocation lands there again. ⇒ *"reset once per invocation, survives
suspension within it"* — **the same rule as the stack case**, where an invocation happens to be one
frame. ⭐ **"Per-invocation" was never "per-frame"; A1 conflated them because in a non-suspending graph
they coincide.**

⚠ **Verify that claim against the emitted code before building on it** — it is coordinator reading of
one probe's output (§1.1), and the phase-clear in particular (`ws.__phase = 0` before the resume
block) is what makes the reset repeat. **If the phase is not cleared on every exit path, the reset is
wrong on the second invocation and you must say so.**

⛔⛔ **The trap: do NOT append the slot to `WorkingState` and index it through the old path.**
`FindVariableIndex`/`VarFieldName` read that list **positionally** (`BP-226`), and
`AiPrimitiveLowering:42-66` already appends `__phase` rather than prepending **for exactly this
reason**. 📐 **Yours: separate storage, or an append that is never positionally indexed by
`VarFieldName`.** ⚖️ **Separate is the lean** — `BP-226` is unfixed, and adding a fourth source to a
space that cannot express three is the direction Q27-D ruled against.

⚠ **Two graphs may each declare a local named `Scratch`.** The slot names must be graph-qualified;
`__loc_` alone is not enough once they share storage.

📌 **The latency predicate still decides which branch a graph takes** — so §1.2 remains live and is
now the *dispatch* question rather than the *refusal* question. **Getting it wrong now silently picks
stack storage for a suspending graph, which is the bug this item exists to fix.**

### 1.4 📌 Then answer the question this raises

⚠ A `Function` graph **that is the asset's own body** can contain a latent node today — `BP1650` only
guards functions reached through `FunctionCallNode.TargetGraphId` (`Stage2_Validate:2169`).
**Coordinator-probed: zero diagnostics.** ⇒ 📐 **Is that intended?** Q27 assumed it was impossible and
built a ruling on it. **Report what you find; file it if it is a hole. Do not fix it here.**

---

## 2. 🔴 A dangling reference emits invalid C#

Coordinator-probed — a `SetVariable` whose `VariableId` matches nothing:

```
DIAGS  = (none)
OUTPUT = s.__var_-1 = __t0;      ← not a valid C# identifier
```

`FindLocalIndex` → −1 → `FindVariableIndex` → −1 → `VarFieldName(-1)` → `__var_{-1}`. ⇒ **the solution
build breaks with an unintelligible `CS` error and no BP diagnostic.**

⚠ **Pre-existing, but locals make it REACHABLE** — §3.3 adds a delete gesture, and `BP-225` showed
delete/rename maintenance is exactly where this lands.

📐 **Yours: a Stage 2 rail refusing an unresolvable `Get`/`SetVariable`, or making `VarFieldName(-1)`
throw, or both.** ⚖️ **The rail is the lean** — it names the node; a throw names nothing. ⭐ **Both is
defensible**: the rail is the diagnostic, the throw is the assertion that the rail is complete.

⚠⚠ **Measure the blast radius before you build it.** Coordinator-measured: **63 of 152** `VariableId`
references in the shipped corpus resolve to **no** list — but most are the literal string `"state"` on
`HillAssault2I_*` assets, i.e. **a different mechanism, not dangling references.** ⇒ **a naive rail
would fail 6+ shipped assets.** **Find out what those actually are first**; if they are legitimate,
the rail must exclude them and the row must say why. 📌 `Count5.bp.json` also carries a
`var:<guid>`-prefixed id — the prefix is handled, the guid may still be stale.

---

## 3. ⭐ The authoring half — three gaps, not one

⭐ **Coordinator-verified: `grep LocalVariables` outside `Compiler/` and `Tests/` returns ZERO hits.**
Nothing in the editor touches the field.

### 3.1 🟢 Declare — ⭐ **a SCOPE on the existing Variables surface, not a new section**

⚠⚠ **Rewritten `2026-08-13` with §1.3 — the earlier lean was the opposite and was wrong.** It argued
for a separate "Locals" section *"because it makes this is different storage visible"*. ⛔ **Q27-A3
rules that storage must NOT be visible.** Keep these two apart:

| | designer-visible? |
|---|---|
| **stack local vs blackboard slot** | ⛔ **No.** Pure compiler choice (§1.3). Hide it completely |
| ⭐ **graph-scoped vs asset-scoped** | ✅ **Yes** — one resets per invocation, one persists. **That difference IS the feature** |

⇒ ⭐ **No new concept and no fifth section: a scope on a variable**, in the surface that already
exists. `BlueprintMyBlueprintModel:46-61` has `Graphs · Functions · Macros · Variables`, with
`editor.create-variable`.

⚠ **The one hard constraint:** a graph-scoped variable belongs to the **CURRENT GRAPH**, so the panel
must retarget with the canvas — `BP-24`'s graph switch, and `BP-72`'s lesson that *a panel editing the
graph you are not looking at is a defect*.

📐 **Yours:** a scope column/dropdown on the row, a graph-owned group inside the Variables section, or
another shape that reads as *one concept with a scope*. ⛔ **Not a parallel section that advertises a
storage class.** **Say which and why.**

### 3.2 🟢 Target — ⛔ **the blocker**

`BlueprintPickerSources:148-152`:
```csharp
if (string.IsNullOrEmpty(text)) return _asset.Variables;
return _asset.Variables.Where(v => v.Name.Contains(text, ...)).ToList();
```
⇒ ⭐ **Even a JSON-declared local cannot be aimed at from the editor.** The picker must offer the
current graph's locals **as well as** the asset's variables.

⚠ **Distinguish them in the list.** Q27-C1 lets a local **shadow** an asset variable of the same name,
and the compiler resolves it silently and correctly — ⛔ **a picker showing two identical rows named
`Scratch` is unusable.** ⭐ **The shadowing is invisible today and that is a real gap in its own right.**

⛔⛔ **Do NOT add `WorkingState` or `Parameters` to this picker.** They are `BP-226`'s space and it is
unfixed; widening the picker is precisely what makes that row live. **One line in your report
confirming you did not.**

### 3.3 🔴 Rename and delete — `BP-225`'s shape, one level along

| | |
|---|---|
| **Rename** | ⭐ **Safe, and say so with a test rather than a comment.** A local resolves by **id** (`FindLocalIndex` is id-only), so a rename cannot re-target anything. ⚠ **This is the opposite of `BP-225`'s pins**, where identity is the *name* — do not carry that fear across |
| **Delete** | 🔴 **Leaves every `Get`/`SetVariable` dangling ⇒ §2's `__var_-1`.** Take the references with it, or refuse while references exist, and **hand them back for the undo** — `BP-225`: an undo that restored only the declaration would recreate the dangling state |
| **Duplicate names** | 📐 Two locals sharing a name in one graph. `BP-225` refused this for exec declarations because two decls collapsed onto **one pin id**. ⚠ **Here ids are distinct, so it is not corrupting — only confusing.** ⭐ **Different problem, different answer. Do not copy `BP-225`'s refusal reflexively; decide and say which** |

---

## 4. 📌 One misplaced doc comment

`GraphTypes.cs:64-82` — the **`BP-220` block explaining `WithNodesAndLinks` and the reflection guard**
is attached to **`LocalVariables`**, because Batch 37 inserted the field between the comment and its
method. ⇒ `LocalVariables` carries **two consecutive `<summary>` blocks** and `WithNodesAndLinks` is
undocumented. Silent (doc generation is off). **One-line fix, no row.**

---

## 5. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (`RESUME_START_HERE.md` §3).
⭐ **Run `python3 scripts/tracker-counts.py --check`** — clean on arrival six batches running.

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** |
| Blueprints | **3243** / 0 / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### Tests

| | |
|---|---|
| ⭐⭐ **§1, executed** | ⭐ **through real Roslyn** (`.Succeeded` never invokes it): a local written **before** a suspension reads **the value it was given** after resume — the case that returns `0` today · ⚠⚠ **and the invocation AFTER that one sees the default again**, which is the half a single call would pass on · ⭐ **plus the non-suspending twin still emits a plain C# local and still resets per call** |
| ⭐ **§1.3 storage** | a suspending graph's slot is **not** positionally reachable through `VarFieldName`, and **two graphs' same-named locals do not collide** |
| ⭐ **§1.2** | whichever way you rule — **a test proving `ChannelCommandNode`-with-`ActionFqn` does or does not suspend.** That claim is the batch's one piece of unverified coordinator reasoning |
| **§2** | a dangling reference ⇒ a diagnostic, **not** `__var_-1` in the output · ⭐ **and the shipped corpus still compiles** — the `"state"` references must not be caught |
| **§3.2** | a `Get` node targeting a local, authored **through the picker path**, compiles to `__loc_` ⚠ *(pin-level, if the picker is not directly testable — say which)* |
| **§3.3** | delete a referenced local ⇒ your ruling, **and undo restores decl AND references** · rename ⇒ references still resolve |
| **Round-trip** | existing assets unchanged; a graph with locals survives save/load |

---

## 6. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) · ⭐ **your §1.2
ruling and whether `MacroLatency` was incomplete** · ⭐ **what the 63 unresolved corpus references
actually are** (§2) · **your §3.1 and §3.3 rulings** · ⭐ **confirmation you did not widen the picker to
`WorkingState`/`Parameters`** · **where you stopped if you stopped** · anything here **wrong against the
code**.

⭐ **Two of this batch's four items exist because the coordinator's Batch 37 handoff did not ask for
them**, and ⛔ **§1 was then written the WRONG WAY ROUND** — as a refusal, until the user pointed out
that a suspendable graph simply has different storage and the designer should never see the
difference. ⚠ **§1.2's `ChannelCommandNode` claim, §1.3's entry-block reset and §2's blast-radius
estimate are coordinator reasoning from one probe, not measurement. Treat all three accordingly.**
