# HANDOFF — Batch 38: finish `BP-57` — suspension-surviving storage, and the authoring half

> 📌 **Dispatched at `eb89ebaf`** · ⚠⚠ **§1.3 and §3.1 REWRITTEN and RE-DISPATCHED at `d5899d9e`.**
> ⭐ **Q27-A was revised from A1 to A3 by user ruling on `2026-08-13`** — verified not present in the
> tree your last run built from (run starting `02fb66db`), so rule 1 does not bite.
> ⛔⛔ **The earlier §1.3 asked for a REFUSAL RAIL. There is none — build the storage.**
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP-57` is *referenced*; `BP1670+` is the next free
> diagnostic. **You allocate everything new** (rule 5).
>
> 📄 **[Q27](Architect_Question_27_Local_Variables.md) — ⚠⚠ **read its `2026-08-13` sections FIRST**:
> a correction banner recording two coordinator errors, **and A's revision from A1 to A3**, which is
> what §1.3 builds. ⛔ **B, C, D, E are unchanged and NOT reopened.**
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
reason**.

⚠⚠ **And the precise reason is worse than "an unguarded gap" — corrected `2026-08-13`.**
`Stage2_Validate` **already enforces** the disjointness: **`BP1024`** (*"AiPrimitive uses parameters
and workingState, not variables"*), **`BP1031`** (*"Instance uses variables, not
parameters/workingState"*), **`BP1011`** (Library). ⭐ **But those are STAGE 2 rules, and lowering
runs at Stage 6 — after they have passed.** ⇒ ⛔ **Parking locals in `WorkingState` during lowering
does not exploit a missing rail, it goes AROUND an existing one**, producing a storage combination
Stage 2 exists to refuse and would have refused if authored.

📐 **Yours: separate storage, or an append that is never positionally indexed by `VarFieldName`.**
⚖️ **Separate is the lean** — `BP-226` is unfixed, and adding a fourth source to a space that cannot
express three is the direction Q27-D ruled against.

📌 **`BP-226`'s row says *"nothing enforces the invariant"* — that is wrong and is the coordinator's
error, inherited from the finding.** ⭐ **Correct the row when you touch it in Batch 39.** What
survives is sharper: **`WorkingState` + `Parameters` coexist legally in every AiPrimitive**, no rail
separates *that* pair, and `Parameters` has no branch in `VarFieldName` at all.

⚠ **Two graphs may each declare a local named `Scratch`.** The slot names must be graph-qualified;
`__loc_` alone is not enough once they share storage.

⚠⚠ **And the one that will bite silently: `StructureHash`.** `StructureHashComputation.Compute`
(`Stage6_Lower:27`) hashes **`Parameters` + `WorkingState` + `Variables`** — name, type, offset, size
— and **nothing else.** The emitted `BTreeTick` wipes and re-initialises the blackboard **only** when
`storedHash != StructureHash`. ⇒ ⛔ **A blackboard-resident local that is not in that hash means
changing its type or layout leaves the old bytes in place and reinterprets them** — stale memory read
as a new type, with nothing reporting it. **If the slot lives in blackboard memory, it belongs in the
hash. Say explicitly what you did about this.**

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

### 3.1 ⭐⭐ Declare — **a "Local Variables" section that follows the canvas.** USER-RULED, not open

⚠⚠ **Rewritten TWICE on `2026-08-13`. Read this version only.** The first draft asked for a separate
section *"because it makes this is different storage visible"* — ⛔ wrong reason, `Q27-A3` rules
storage invisible. The second over-corrected into *"a scope column, no new section"* — ⛔ **also
wrong**, and the user named exactly why:

> *"Adding a scope cannot express to what graph they are local to."*

⭐ **Correct. The section is right; my justification for it was the wrong one.** These are two
different things and only the first is hidden:

| | designer-visible? |
|---|---|
| **stack local vs blackboard slot** | ⛔ **No.** Pure compiler choice (§1.3) |
| ⭐ **which GRAPH a variable belongs to** | ✅ **Yes**, and a flat scope column **cannot say it** |

#### ⭐ The ruling — build this

| | |
|---|---|
| **A new `Local Variables` section** | in `BlueprintMyBlueprintModel`, alongside `Graphs · Functions · Macros · Custom Events · Variables` |
| ⭐ **Filled from the CURRENT GRAPH** | ⇒ *"which graph"* is answered by **which graph is open**, not by a column. This is Unreal's model and it is the only thing that answers the user's objection |
| ⭐ **Always present, empty when the graph has none** | ⛔ **Do not hide it.** A section that appears and disappears reads as a broken feature |
| **`[+]` where applicable** | see the note below — ⚠ *applicable* must not become *silently absent* |

#### ⚠ What this costs, measured — the model has no idea what graph is open

| | |
|---|---|
| ⛔ `_sections` is `static readonly` | a fixed list of five descriptors. **A sixth is trivial; a CONTEXT-SENSITIVE one is the actual work** |
| ⛔ `Retarget(IEditableAsset?, BlueprintAsset?)` | **asset only — the model has no current-graph concept at all** |
| ✅ ⭐ **But the wiring already exists** | `AiCanvasContext.CurrentGraphId` (fed by `BlueprintGraphSwitcher.CurrentGraphId`), and **`GraphSignatureWindow` already consumes it** — that is `BP-72`, whose whole lesson was *a panel editing the graph you are not looking at is a defect*. **Follow that precedent; do not invent a second mechanism** |

#### ⚠⚠ On "where applicable" — ⛔ silence is not an option

`MyBlueprintSectionDescriptor.CanCreateItems` is a **static bool**, so a per-graph `[+]` needs either a
dynamic `Sections` or a create command that refuses. ⭐ **A `Macro` graph cannot declare a local
(`BP1664`)** — that is the case "where applicable" excludes.

⛔ **Whichever you pick, the designer must learn WHY**, per the user's standing ruling that decided
Q26-B2 — *"grey out does not educate the user"* — and `BP-76`/`BP-77`, both filed **because** something
was greyed with no explanation. ⇒ either `[+]` is absent **and the empty section says why**, or it is
present **and refuses out loud** through the `IEditorIndicators` surface `BP-223` repaired. 📐 **Your
call which; ⛔ a silently missing button is not one of the options.**

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

### 3.2a ⭐⭐ The node itself — **a BADGE. USER-RULED, not open**

⛔ **First, a bug that exists regardless of styling.** `BlueprintNodeModel.ResolveVariableName`
(`:425-448`) resolves a Get/SetVariable's title through **`Variables` then `WorkingState`** and
**nothing else** ⇒ ⚠ **a local-targeting node displays a RAW GUID as its title.** Add the
`LocalVariables` branch. 📌 Keep the existing fallback shape — an unresolvable id is returned as-is
*"so a dangling reference stays visible on the node rather than reading as a valid"* reference.

**Then the distinction.** ⚠ `Q27-C1` permits shadowing, so a local `Scratch` and an asset `Scratch`
produce **two pixel-identical nodes that read different storage**. Unreal has this ambiguity and it is
a known annoyance; ⭐ **we are asked to do better.**

⇒ ⭐ **A badge on the node. User-ruled.** ⛔ **NOT colour** — colour is already spent on **type**
(`BlueprintTypeSystem.GetAccentColorForTypeId`), Unreal's convention, and overloading it would make
two meanings share one channel.

⚠⚠ **Where this lands, measured — it is NOT purely Hrot-side:**

| | |
|---|---|
| ✅ `MyBlueprintItem` | **already has `BadgeText` and `IconKey`** ⇒ the **panel** side is free |
| ⛔ `INodeModel` | **has no badge.** `Title` · `Subtitle` · `Category` · `StatusTooltip` — ⇒ the **canvas** side needs a new member on `NodeEditor.Core` **and** rendering in `NodeEditor.UI`'s `CanvasRenderer` |
| ⛔ **`Subtitle` is NOT free** | `BP-17` owns it: when a node carries a custom title, the generated title becomes the subtitle. **Putting the badge there would collide with every renamed node** |
| ⚠ **Two gates move** | **NodeEdit Core 208** and **UI 131** are shared-library suites. Adding to `INodeModel` touches both — ⭐ **and they take NO `--no-build`** (§5) |

📐 **Yours: the badge's shape** (glyph, short text, tooltip) and whether the panel item carries the
matching badge too. ⚖️ **Both surfaces is the lean** — the panel already supports it for free, and the
two views disagreeing is its own defect.

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
| **§3.2** | a `Get` node targeting a local, authored **through the picker path**, compiles to a local ⚠ *(pin-level, if the picker is not directly testable — say which)* |
| ⭐ **§3.1** | the Local Variables section **follows the canvas** — switch graphs, the contents change · and it is **present when the graph has none** |
| ⭐ **§3.2a** | a local-targeting Get/Set shows **its NAME, not a GUID** (the `ResolveVariableName` bug) · and a local `Scratch` beside an asset `Scratch` is **distinguishable** |
| **§3.3** | delete a referenced local ⇒ your ruling, **and undo restores decl AND references** · rename ⇒ references still resolve |
| **Round-trip** | existing assets unchanged; a graph with locals survives save/load |

---

## 6. Reporting

Per-suite numbers · **BP-warning count and composition** · `tracker-counts.py --check` clean ·
revert-goes-red per item · ⭐ **every id and diagnostic code you allocated** (rule 5) · ⭐ **your §1.2
ruling and whether `MacroLatency` was incomplete** · ⭐ **what the 63 unresolved corpus references
actually are** (§2) · **your §3.1 and §3.3 rulings** · ⭐ **confirmation you did not widen the picker to
`WorkingState`/`Parameters`** · ⭐ **your `[+]`-where-applicable choice and how the designer learns why**
(§3.1) · ⭐ **the badge's shape, and whether `INodeModel` changed** (§3.2a) · **where you stopped if you
stopped** · anything here **wrong against the code**.

⭐ **Two of this batch's four items exist because the coordinator's Batch 37 handoff did not ask for
them**, and ⛔ **§1 was then written the WRONG WAY ROUND** — as a refusal, until the user pointed out
that a suspendable graph simply has different storage and the designer should never see the
difference. ⚠ **§1.2's `ChannelCommandNode` claim, §1.3's entry-block reset and §2's blast-radius
estimate are coordinator reasoning from one probe, not measurement. Treat all three accordingly.**
