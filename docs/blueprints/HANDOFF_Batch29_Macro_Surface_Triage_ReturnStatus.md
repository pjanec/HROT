# HANDOFF — Batch 29: the macro authoring surface · the warning triage · `Return.Success`

> 📌 **Dispatched at `<pending>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 4 is yours:** pull the coordinator branch again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** **No new `BP-2xx` number appears below.** Existing
> rows are *referenced* (`BP-80`, `BP-131`, `BP-77`, `BP-200`); number every new finding yourself and
> say what you chose (rule 5).
>
> ⭐ **Everything here is headless.** The two visual gestures (palette drag, the *"Macros +"* button)
> are deliberately **out of scope** — the user's visual-testing capacity is limited. If an item cannot
> be proven by a test, say so rather than shipping it unproven.
>
> 📄 Read [Macro_Implementation_Design.md](Macro_Implementation_Design.md) (F1–F5, §5, §7) for item 1
> and [Return_Status_As_Pin_Design.md](Return_Status_As_Pin_Design.md) (§7–§9) for item 3. Both designs
> are **closed**; nothing below reopens them.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** macro model + projections | the **N exec-in** projection on `ReturnNode`; the unexpanded-call **Error** (§1.3) | `ExecOutDecl`/`Graph.ExecOutputs`/`MacroCallNode`; the round-trip + parity tests |
| **2** warning triage | ⭐ **the 6 synthesized orphans — expect a real defect** | the 10 authored orphans; the `BP3011` rung |
| **3** `Return.Success` | **H1** (`IrTerm_ReturnStatus` gains a condition) + **H2**'s exclusion | the pin projection in both halves, the drawer, the tests |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | `claude/blueprint-macro-feature-sdmspn`, built on the coordinator branch as usual |
| **Rule 6** | The **tracker + detail docs are yours** for this batch; the coordinator will not touch them |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

⚠ **Order matters:** item 3 edits `BuildReturnTerminator`, item 1 edits `ReturnNodePins`/`EnrichReturnPins`.
They meet on the Return node. **Land item 1 before item 3**, or expect a merge on the same lines.

---

## 1. 🟠 `BP-80` — the macro authoring surface, model + projection half

Design: [Macro_Implementation_Design.md](Macro_Implementation_Design.md) §2 (model), **F3** (reuse the
boundary nodes), **F4** (one field), **F5** (a new list). All three ACCEPTED (§7).

### 1.1 Model — the entire on-disk delta

```csharp
public enum GraphKind { Function, Event, Construction, Macro }   // ✅ already landed (BP-216)
public sealed class ExecOutDecl { public Guid Id; public string Name = ""; public string? Tooltip; }
public sealed class Graph      { /* … */ public List<ExecOutDecl> ExecOutputs { get; set; } = new(); }
public sealed class MacroCallNode : Node { public string TargetGraphId { get; set; } = ""; }
```

⭐ **F5 is understated in the design, and the correction strengthens it.** It says `Graph.Outputs.Count`
is load-bearing *"in at least four places"*. **Coordinator-measured on this tree: 20 executable sites
across 8 files** (comments and the three *signature*-typed `funcSig`/`peerFuncSig` reads excluded):

| File | Lines |
|---|---|
| `Stage5_Schedule.cs` | 898 · 914 · 925 · 1640 · 1657 · 3433 · 3464 |
| `ReturnNodeDrawer.cs` | 100 · 188 · 199 · 202 |
| `CSharpEmitter.cs` · `GraphSignatureWindow.cs` · `NodePinSchema.cs` | 287,300 · 240,243 · 347,349 |
| `LibraryEmitter.cs` · `Stage0_Rehydrate.cs` · `Stage2_Validate.cs` | 47 · 292 · 2327 |

⇒ **Do not put exec entries in `Graph.Outputs`.** A new list is not tidiness; it is the only option
that does not silently move 20 arithmetic sites — four of them in the **Return node's own drawer**.

⚠ **F4 — `MacroCallNode` carries `TargetGraphId` and nothing else.** Do not bake pin names, types,
counts or arity onto it. That is literally `CallablePeers` (BP-116) and `ArgTypes` (BP-201), which have
now bitten twice.

### 1.2 Projections — ⭐ **four halves, not two**

The rule everyone states as *"editor and compiler must move together"* is really **two independent
pairs**, and each pair has its own gate. Coordinates verified on this tree:

| Boundary | Editor half | Compiler half | Gate today |
|---|---|---|---|
| **Entry** | `NodePinSchema.EventEntryNodePins` **:268** | `Stage0_Rehydrate.EnrichEventEntryPins` | `Kind == Function \|\| Kind == Event` |
| **Return** | `NodePinSchema.ReturnNodePins` **:347** | `Stage0_Rehydrate.EnrichReturnPins` **:292** | `Kind == Function && Outputs.Count > 0` |

**Delta:** admit `Macro` to all four. Entry projects one data-out per `Graph.Inputs` (unchanged shape).
Return gains ⭐ **N exec-in pins from `Graph.ExecOutputs`** — the only genuinely new projection in the
batch, and the 🔴 Opus part.

⚠ `ReturnNode.Status` must be **hidden for `Macro`** — `ReturnNodeDrawer:92-100` (`ShowStatus`/
`ShowOutputs`), the BP-105 precedent.

⚠ F3's cost is real and greppable: `BP1601` (no return), `BP1602` (no entry), `BP1655` (declared output
unwired), `BP1657` must each **explicitly** decide about `Macro`. Bounded, and each fails loud.

### 1.3 ⛔ The one thing you must add that the design does not say

**BP-80 without BP-81 leaves a `MacroCallNode` that nothing expands.** F4 says such a node "lands in
the *unknown impure node kind → `BP4004`* arm, a second diagnostic beneath BP-79's". ⚠ **That arm is
verified, and it is not a net.** `Stage5_Schedule.cs:2020-2025`:

```csharp
default:
    // Unknown impure node kind -- emit BP4004 and skip.
    _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4004, …));
    break;                                    // ⇒ no IR emitted, exec chain continues
```

⇒ A **Warning** that emits nothing and walks on — **trap #5, one more floor up.** Under
`Hrot.AI.Behaviors`' `TreatWarningsAsErrors` it breaks the build; in **any consumer without that flag
the macro call silently vanishes from the exec chain.**

⭐ **So: give an unexpanded `MacroCallNode` reaching Stage 5 an explicit `Error`** (the design's
`BP166x` family — **confirm nothing has claimed those codes since** before using them). Word it the way
`BP-216` was worded — *reached Stage 5 **as a compilation target***. That single diagnostic is what
makes shipping BP-80 without BP-81 safe.

### 1.4 📐 A design-doc tension — **your call, and I am flagging rather than ruling**

`Macro_Implementation_Design.md §5` says: *"⚠ **BP-80 and BP-81 must not be split across sessions.**
Pin projection lives in two assemblies that must agree."*

⚠ **The justification only supports keeping the four projection halves together — which are all inside
BP-80.** BP-81 (the expansion pass) touches no pin projection. My reading is that the claim is broader
than its stated reason, and that §1.3's explicit Error is what actually makes the split safe.

**I am not ruling on this.** If you agree, land BP-80 alone and say so. If you find a reason the split
is genuinely unsafe, **stop at the boundary and report it** — do not start BP-81 to compensate; it is
🔴 Opus hands-on work that deserves its own batch.

⛔ **Not in this batch:** the expansion pass (BP-81), the rails (BP-82), debug provenance (BP-83), the
palette/My Blueprint gestures (**BP-77** stays open).

---

## 2. 🟠 The warning triage — D6's blockers are cleared

⭐ **All numbers below are coordinator-measured on this tree today, not carried forward.** Recipe —
`sort -u` is mandatory, MSBuild prints every warning twice:

```bash
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -t:Rebuild -v n --nologo \
  | grep -oE "warning BP[0-9]+: [^[]*" | sort -u
```

**18 distinct = 16 × `BP3010` + 2 × `BP3011`.** Reproduced independently of the Batch-28 figure.

### 2.1 ✅ D6's premise re-verified from scratch

The `Asset ▸ Graph ▸ NodeKind` prefix's **absent third segment** marks a compiler-synthesized node.
I re-checked every GUID against the asset corpus rather than trusting the claim:

| | Count | Appears in an asset file? |
|---|---:|---|
| **kind-bearing** (3 segments) ⇒ authored | **10** | ✅ all 10 do |
| **kindless** (2 segments) ⇒ synthesized | **6** | ✅ all 6 appear in **zero** files |

| Asset | authored | synthesized | total |
|---|---:|---:|---:|
| `InlineEd1 ▸ Tick` | 8 (1 Branch, 7 Function Call) | 3 | **11** |
| `EnumDemo ▸ Main` | 2 (Channel Command) | 3 | **5** |

### 2.2 ⭐ Two things about these assets that change the plan

**(a) `InlineEd1.bp.json` is referenced by no test — it is genuinely 🟢.** Only generated output
mentions it. ⚠ **But it is a renamed, divergent fork of `EditorTypesDemo.bp.json`**, with which it
shares node GUIDs. `EditorTypesDemo.bp.json` exists as **two byte-identical copies** — 
`Hrot.Blueprints.Tests/TestAssets/Recipes/` and `Hrot.AI.Behaviors/Recipes/Blueprints/` — and **is**
test-locked (`RecipeIntegrityTests:100,120,140,223` · `FixedStringPinTests:88-110`).
⇒ Editing `InlineEd1.bp.json` is safe **because it is a different file**. ⛔ **Do not "helpfully" sync
the three copies** — that is how a shared GUID turns one fix into three broken fixtures.

**(b) 🔴 `EnumDemo.bp.json` is the T32 committed gate asset — it is *not* a free edit.** It is composed
into `T32_ComposedGeneratedBlueprint` and named as such at `BTreeJsonGeneratorTests.cs:2553` (*"the
real EnumDemo committed gate asset (T32)"*). Its 5 orphans sit under the **AiEditor.Generators** gate.
⇒ That is the concrete name behind *"deleting a node can change what the fixture asserts"*. Re-run
that gate specifically, and if the composed output moves, **stop and report rather than re-baselining.**

**Only `Assets\Blueprints\**\*.bp.json` is `AdditionalFiles`** (`Hrot.AI.Behaviors.csproj:100`) ⇒ only
that tree warns. `Recipes\Blueprints\*.bp.json` is `Content` (`:116`) and is never compiled.

### 2.3 🔴 The 6 synthesized orphans — this is the item, not the 10

`BP3010` is emitted by `Stage3_Normalize.EliminateOrphanNodesInGraph` (**:360-381**); *orphan* = not
reachable from the entry node via **exec or data** wires.

⇒ **The compiler creates nodes, eliminates them, and warns about its own work.** No content edit can
fix it. **Expect a real defect under it** — find which pass synthesizes them and why they are
unreachable at Stage 3. Report the root cause even if the fix does not fit this batch.

### 2.4 🟠 `BP3011` — a judgement call, one rung not two assets

Two messages, `HillAssault2I_DispatchWaveWithTargets ▸ Main` and `HillAssault2_… ▸ Main`, both
*"Implicit cast inserted from `System.Byte` to `System.Int32`"* — an **always-safe widening**.
📐 **Your call:** arguably it should not warn at all. Decide at the rung (`Stage3_Normalize:325`), not
by editing two assets, and state the reasoning in the row.

⚠ **Report the new distinct count and its composition** after your changes. A total-warning count is
not a substitute — BP-211 proved that measure hides merges, and the summary echo inflates it.

---

## 3. 🔴 `BP-131` — `Return.Success : bool`, AiPrimitive only

Design **settled**: [Return_Status_As_Pin_Design.md](Return_Status_As_Pin_Design.md) §7 — one
`Success : bool` data-in pin, **AiPrimitive only**, no status surface anywhere else, no `NodeStatus`
literal node, **ABI unchanged** (the method still returns `NodeStatus`; the bool maps at the return
statement). ⭐ **`BP-107` dissolved into this row** — it is the only live item of the pair.

All three hazards re-verified on this tree, and **H2 is sharper than written**:

| | Verified | Note |
|---|---|---|
| **H1** | `IrBlock.cs:33` — `IrTerm_ReturnStatus(NodeStatus Status)`, a **constant**; `TerminatorEmitter.cs:34-36` renders `return global::Fbt.NodeStatus.{t.Status};` | ⭐ **This is the actual work.** It needs an optional `IrValue` condition and `return cond ? Success : Failure;` |
| **H2** | `BuildReturnTerminator:2114-2116` takes **every non-exec pin** (`Direction == "In" \|\| "Out"`); `wantsStatusReturn` **:2133-2135** = `AiPrimitive` **unconditional** `\|\|` (`Library && valuePins.Count == 0`) | see below |
| **H3** | `Stage0_Rehydrate.EnrichReturnPins(pins, graph, staticShapes)` **:276-277** has **no asset parameter** ⇒ cannot see `Dispatch` | ⚠ `NodePinSchema.ReturnNodePins(containingGraph)` **:339** does not take it either — but `asset` **is** in scope at the dispatch site (`:134`), so the editor half is a pass-through, the compiler half is a signature change |

⭐ **H2, precisely.** Because `AiPrimitive` is **unconditional**, and the pin is AiPrimitive-only, the
zero-output-`Library` branch is at risk **only if the pin is projected beyond AiPrimitive**.
⇒ **Primary containment is the projection gate**; excluding `Success` by name at `:2114` is defence in
depth. Do **both** — `valuePins.Count > 1` at `:2153` also drives tuple packing.

⚠ **Unwired-pin ruling (design §8, take it):** an unwired `Success` is `BP4001` + `default(bool)` =
`false` = **Failure**, which would flip every shipped AiPrimitive Return. ⇒ **Fall back to `rn.Status`
when the pin is unwired** — back-compatible with every shipped asset, no migration. (Preferred over an
inline `true` default.) ⚠ This interacts with **BP-200**'s open unwired-pin question; note it, do not
resolve BP-200 here.

📌 **Leave alone:** `SealFallThrough:898-900` emits `IrTerm_ReturnStatus(NodeStatus.Success)` on the
*implicit* path (no Return node ⇒ no pin). Correct as-is — do not "fix" it.
📌 **Stays separate:** D3's zero-output-`Library`-returns-`void` change. Test-locked by
`BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn` and it has no user-visible
benefit.

**Test:** ⭐ **run it.** An AiPrimitive whose `Success` pin is driven by a `Compare`, ticked, asserting
`NodeStatus.Failure` **then** `Success` across two ticks — through the **real Roslyn generator**
(`.Succeeded` never invokes Roslyn).

---

## 4. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).

**Baseline — ⭐ all eight gates coordinator-RUN on THIS tree (`0bef2f2`) today, not carried forward:**

| | |
|---|---|
| Solution build | **0 errors**, **77 warnings** |
| ⚠ of which **BP diagnostics** | ⭐ **18 distinct** — **16 × `BP3010`**, **2 × `BP3011`** |
| Blueprints | **3101** passed / **0** failed / **10** skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

✅ Every figure reproduces Batch 28's recorded baseline exactly. The tree is clean; anything red after
your changes is yours.

### ⚠⚠ A defect in the documented gate command — found running it

The two **NodeEdit** gates as written in `RESUME_START_HERE.md` §3 use `--no-build`:

```
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj --no-build …
```

⇒ On a clean tree this **does not run the tests**. Those projects are **not in `IOS-IG-SimHost.sln`**,
so the solution build never produces their assemblies, and the runner exits with
*"The argument …NodeEditor.Core.Tests.dll is invalid"* — ⭐ **no test output, and easy to read as
"nothing to report" rather than "the gate did not run."** Trap #5 in the gate script itself.

⇒ **Run those two WITHOUT `--no-build`.** Done that way they pass 208 / 131 as recorded. The
coordinator has corrected §3.

⚠ Items here touch the compiler, the emitter **and** the editor projections — run all eight. Item 2(b)
additionally means **`Hrot.AiEditor.Generators.Tests` is load-bearing**, not incidental.

---

## 5. Reporting

Per-suite numbers · **the BP-warning count and its composition** · revert-goes-red per item ·
⭐ **every BP id you allocated** (rule 5) · your ruling on **§1.4** (split or not, and why) · the
root cause of the 6 synthesized orphans · anything here **wrong against the code**.

⭐ **You have corrected the coordinator repeatedly and been right.** If something above does not match
the tree, say so plainly — that is the most valuable line in your report.

📌 **Stale doc, not yours to edit, flagged per rule 6:** `RESUME_Impl_Session.md:211` still lists
`BP-107` as *"architect round required"*. It was dissolved into `BP-131` on 2026-08-10.
