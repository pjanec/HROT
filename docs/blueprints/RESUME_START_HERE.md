# ⭐ START HERE — coordinator session, blueprint gaps & QoL programme

> **Point a fresh session at this file. It is self-contained.** Last updated **2026-08-13**.
> ⭐⭐ **Batch 43 verified and merged at `3583acd4` (§7p) — `BP-57` is CLOSED.** The Local Variables
> section landed; a designer can declare, rename, delete, duplicate and undo a graph local from the
> editor, and it follows the canvas.
> ⭐⭐ **Batch 47 verified and merged at `d98b98bf` (§7t) — `BP-228` CLOSED.** `BP1671` refuses a
> made-up type id, naming the variable and the type. ⭐⭐ **The plan's open oracle question is RETIRED,
> not answered — measured: exactly ONE production site supplies a resolver, and the editor's
> `CompileOptions` site has NO production caller at all**, so there is no editor compile path to attach
> one to; `U-8` makes the picker safe **by construction** instead. 🔴 **`BP-87`'s restored lock found a
> live defect on its first run:** `System.String` was offered and can never compile as a variable.
> ⏭ **Batch 48 dispatched (`U-9` — the tagged declaration, alone).**
>
> ⚠⚠ **`U-6`/`U-13`/`U-16` are now UNSCHEDULED** — they hard-require the visual check (twelve batches).
> ⛔ **The plan's "coherent stop point" is SUSPENDED with them:** until `U-16` runs, a designer meets
> **two editors for one concept.** ⭐ **Nothing else waits on them; the sequence continues.**
>
> ⭐⭐ **Batch 46 verified and merged at `ea53e7e0` (§7s) — `BP-230` + `BP-231` CLOSED.** `isParams`
> is gone (the editor now uses the compiler's own `VariableKind`), the reference count is real and
> resolves **exactly as `Stage5.FindVariableRef` does**, and ⭐⭐ **`BP-230`'s eight-batch-old open
> question was answered from the panel code, not a screenshot: the Role combo was DRAWN, LIVE, and its
> result DISCARDED.** ⚠ **AiShared 1213 → 1216** — the one gate that was meant to move.
> ⏭ **Batch 47 dispatched (`U-7` + `U-8`) ⚠ SWAPPED AHEAD of the visual-check batch.**
>
> ⭐⭐ **Batch 45 verified and merged at `74526bf0` (§7r) — `BP-226` is CLOSED.** `VariableRef(kind,
> index)` travels Stage 5 → IR → Stage 7; `VarFieldName(int)` **no longer exists**, so the wrong call is
> unwritable. ⭐ **Golden 42/42 both tiers with no regeneration** ⇒ behaviour-preserving, measured.
> ⚠ **A coordinator finding was REFUTED and correctly** — the index was always list-relative; my
> "rebase it" would have broken every shipped AiPrimitive. ⏭ **Batch 46 dispatched (`U-4` + `U-5`).**
>
> ⭐⭐ **Batch 44 verified and merged at `ba337568` (§7q) — THE GOLDEN NET IS BUILT AND IT BITES.**
> 42 assets × two tiers, three proof-it-bites tests one per tier, and ⭐ **the in-process vs
> semantic-model resolver parity question is ANSWERED: 42/42 byte-identical.** ⭐ **`BP-229` closed.**
> ⇒ ⭐⭐ **Every later `U-` task's *"the output did not change"* is now falsifiable.**
> ⚠ **Two prerequisites the plan did not have, both measured:** the corpus compiles **as a set**
> (sibling catalog, not just the preload — 40/42 without it), and the generator **hardcodes
> `CompilerMode.Release`** ⇒ 📌 **Debug-mode emit is NOT covered by the baseline.**
> ⚠ **[PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md)'s batch table was
> RENUMBERED (+2)** — `BP-57`'s authoring half took three batches, not two. ⭐ **Only 47 hard-requires
> the visual check; 44 · 45 · 46 · 48 all run headless, and 48 may be pulled ahead of 47.**
> ⚠⚠ **The VISUAL CHECK has not run for NINE batches**, and Batch 43's whole deliverable is a panel
> surface no headless test can see drawn. ⭐ **Ask the user for it before the `U-` sequence buries it.**
> ⭐ **Macros are DONE end to end and proven by execution** — authored, called, expanded, compiled
> through real Roslyn, **ticked across frames**, and debuggable.
> ⭐⭐ **`BP-74` is CLOSED — collapse a selection into a Function or Macro works end to end**: reachable
> from the canvas, one undo entry that restores identity, refuses out loud, round-trip test-locked.
> ⭐⭐ **A macro can now be AUTHORED by hand** — created from "Macros +", exec entries/exits declared in
> the signature window, dragged from the palette. **`BP-74`/`BP-75`/`BP-77`/`BP-80`/`BP-81`/`BP-83` all closed.**
> ⭐⭐⭐ **THE MACRO PROGRAMME IS COMPLETE** — `BP-74`…`BP-83` **all closed**. Authored, collapsed,
> expanded (both directions, round-trip locked), run across frames, debuggable, navigable.
> ✅ **Batch 37 VERIFIED AND MERGED at `68cff233`** (§7i) — **`BP-57`'s compiler half**: locals are
> plain C# locals in a **per-graph** index space, id-only resolution with **no name fallback**,
> `BP1664` finally built and **`BP1669`** allocated. ✅ **Corrected and completed Batch 39** (Q27-A3
> storage; **`BP1670`** for the dangling rail); ✅ **authoring UI Batches 41–43 ⇒ `BP-57` CLOSED (§7p).**
> ⚠ **[FINDING_Variable_Index_Space.md](FINDING_Variable_Index_Space.md) corrected Q27 and Batch 37 §6:**
> the variable index space is an **unenforced invariant**, *not* a latent defect.
> ⭐⭐ **The implementation session then raised its severity by measuring what I asserted** — my
> *"not authorable"* leg was wrong: **57 live `WorkingState` references** ride the invariant today.
> Filed as **`BP-226`**; **`BP-227`** records the four numeric-`Dispatch` assets.
> ⭐ **`BP-226` is now expected to DISSOLVE into the unification** rather than be patched — stage C.
>
> ✅ **Batch 38 VERIFIED AND MERGED at `27ebe8dc`** (§7j) — ⭐⭐ **the design review, and it changed
> the design.** 📄 **[REVIEW_Unified_Variable_Design.md](REVIEW_Unified_Variable_Design.md)** —
> verdict **build it, with four named changes and a re-ordered plan**.
> ⭐⭐ **`C` moves to FIRST** (4 call sites, needs nothing from D, closes `BP-226`).
> 🔴 **`BP-228`** any dotted string compiles ⇒ **stage B′ blocked** · 🔴 **`BP-230`** the shared table's
> role/scope/reference-count members are **stubs** ⇒ stage B would ship an inert editor ·
> 🔴 **`BP-229`** `Compile` writes through into the caller's `Graph` · **`D` is a programme, not a stage**.
> ⚠ **Read the review before touching [model](Variable_Model_Unification.md) /
> [UI](Variable_Editing_UI.md)** — several of their claims are now known wrong.
>
> ✅ **The lost work is RECOVERED** — on **`claude/batch39-locals-preserved`** (`7e0b11b0`), pushed by
> the coordinator from the patches the implementation session checked in. ⭐ **Coordinator-gated on
> that branch: build 0 errors · Blueprints 3269 total / 3259 passed / 0 failed / 10 skipped** (+16).
>
> ⏭ **Batch 39 dispatched (`ade79865`) — RE-SCOPED:** merge and close out that work, then build the
> authoring half. ⛔ **Not a rebuild.**
> ⏭ **Batch 40 dispatched — [review the unification TASK PLAN](HANDOFF_Batch40_Unification_Plan_Review.md).**
> 📄 **[PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md)** — **14 tasks with
> headless gates**, batches **41–49**. ⭐ **`U-1` builds a golden-corpus harness FIRST**, because every
> later task's success condition is *"the output did not change"* and that is unfalsifiable without
> a recorded baseline. ⚠ **Nothing starts until the plan review returns.**
> ⭐⭐ **Three blockers RESOLVED as architect rulings** —
> **[`Q-j`](Variable_Editing_UI.md)** the struct validator is an existing seam (the generator already
> holds Roslyn's `Compilation`) · **[`Q-k`](Variable_Editing_UI.md)** `Role`/`Scope` are **read-only**
> for blueprints, a move not a toggle, so `R3` dissolves ·
> **[`Q-i`](Variable_Model_Unification.md)** shared state is **another document's storage**, excluded.
> ⭐ **And a MEMBERSHIP RULE that replaces the case-by-case answers:** *a declaration belongs in the
> model iff it has a byte offset in a struct this asset emits.*
> ⭐ **The merge stops at the IR boundary** — `IrAsset` keeps three lists, so `TickCore`'s signature,
> the emitted structs and the blackboard allocation are **unchanged**; `D`'s acceptance test is
> *"`StructureHash` byte-identical across the whole corpus."*
>
> ✅ **`BP-57` is DONE — nothing of it remains.** Batch 39 landed the suspension storage and the
> dangling rail; Batches 41–43 landed the authoring UI. ⭐ **The two 🔴🔴 defects listed here for six
> batches — a local reverting to its default across a suspension, and a dangling reference emitting
> `s.__var_-1` — are BOTH FIXED** (Q27-A3 entry-block reset; `BP1670`).
> ⚠ **One residue, recorded not patched:** `BlueprintLocalVariableSchemaSource.AddVariable` does not
> reject a duplicate name; the guard sits in the window. ⭐ **`U-6` absorbs the source — put it there.**
> ⭐⭐ **[Q27-A was REVISED A1 → A3 by user ruling `2026-08-13`](Architect_Question_27_Local_Variables.md):**
> a suspendable graph's locals are **blackboard-allocated and reset in the entry block**, because a C#
> stack local cannot cross a suspension and ⛔ **the designer must never see which storage they got.**
> ⚠ **My first draft asked for a REFUSAL rail — the wrong way round**, and argued for a separate
> "Locals" section *to make storage visible*, which is the opposite of the ruling.
> ⛔ **Q26-A supersedes Q25-D3:** a macro now has **N exec-ins**, not one.
>
> 📌 Supersedes [RESUME_Coordinator.md](RESUME_Coordinator.md), which is now the **historical log**
> (Batches 22-28, plus the §0z process root-cause). Read it only for backstory.

---

## 1 · Who you are

**The coordinator.** You own the tracker, write handoffs, review returned diffs, re-run gates, and pick
the next batch. ⛔ **You do not write feature code** — a separate *implementation* session does.

| Lane | Branch |
|---|---|
| **You** (push here, always) | ⭐ **`claude/blueprint-authoring-status-gm0akp`** |
| Implementation session | ⭐ **`claude/hrot-implementation-j1jvin`** (was `…-sdmspn`; they moved, Batch 29) |

⚠ **Changed 2026-08-10 by the user.** The coordinator lane used to be
`claude/blueprint-authoring-status-6sr5ld` — that was a **different, now-retired session**, and the
programme continues here. ⛔ **Anything still naming `6sr5ld` as the coordinator branch is stale.**

⭐ **The implementation session branches from — and re-syncs from — YOUR branch**, at the start of every
run (`.claude/CLAUDE.md` rule 7) and again before its final commit (rule 4). Say so in every handoff.

⚠ **Both sessions share this repo and both load `.claude/CLAUDE.md`** — that file is the only memory
between you. Its *Two-session protocol* table is binding; **re-read it before writing any handoff.**

⛔ **No PR unless the user explicitly asks.** ⛔ Never put a model identifier in anything pushed.

---

## 2 · First actions on resume

```bash
git fetch origin                       # ⚠ they have changed branch once; do not assume the name
git log --oneline HEAD..origin/claude/hrot-implementation-j1jvin
```
⚠ **If that branch is empty, find theirs** — any `claude/*` branch whose first commit's parent is one
of your commits:
```bash
for b in $(git ls-remote --heads origin | awk '{print $2}' | sed 's|refs/heads/||' | grep claude); do
  n=$(git log --oneline HEAD..origin/$b 2>/dev/null | wc -l); [ "$n" -gt 0 ] && echo "$b +$n"; done
```

| Situation | Do |
|---|---|
| **No batch in flight** | pick the next batch — see §4 |
| **Implementation reported done** | run **all eight gates** (§3), review the diff, reconcile the tracker three ways, **then** merge `--ff-only` and record it |
| A batch **is** in flight (⚠ **not today — Batch 43 is merged and nothing is dispatched**) | ⛔ **rule 6: the tracker and detail docs are theirs.** Put findings in the *next* handoff, never in a live one |

⭐ **Never say "they never saw X."** It is a property of one commit, not the session. Test against what
they *branched from*:
```bash
git log -1 --format='%p' <their-first-commit-of-that-run>
git merge-base --is-ancestor <my-commit> <that-parent>
```
Report *"not in the commit they built from (run starting `<sha>`)"*.

---

## 3 · The eight gates — commands and current baseline

Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj      --no-build -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj           --no-build -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj          --no-build -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj                       --no-build -v q --nologo
# ⚠⚠ the two NodeEdit gates take NO --no-build -- see the warning below
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj                -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj                    -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj                         --no-build -v q --nologo
```

### ⚠⚠ The two NodeEdit gates silently do not run under `--no-build`

Corrected 2026-08-10 after actually running them. Those projects are **not in `IOS-IG-SimHost.sln`**,
so the solution build never produces their assemblies and the runner exits with *"The argument
…`NodeEditor.Core.Tests.dll` is invalid"* — ⭐ **no test output at all**, which reads as *"nothing to
report"* rather than *"the gate did not run."* Trap #5, in the gate script itself.

⭐ **Run the five `--no-build` suites in PARALLEL** (`&` + `wait`, one log file each) — measured
**3m40s → 2m05s**. ⚠ **And always include `\[FAIL\]` in the result grep**: a `Passed!`/`Failed!`-only
grep is why Batch 42's flake could not be named.

**Baseline at `d98b98bf` — ⭐ all eight gates coordinator-RUN 2026-08-14, post-Batch-47:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** ⚠ *(an incremental build under-reports — record honestly)* |
| BP diagnostics | **10 distinct** — all `BP3010`, all **authored** orphans in 2 assets |
| Blueprints | **3474 total / 3464 passed / 0 failed / 10 skipped** ⚠ *(BP-111 filters 7 host-timing tests out of the default run — `Category=HostTimingSensitive` runs them)* |
| ⭐⭐ **Golden corpus** *(new, Batch 44)* | **42 assets × Tier 1 + Tier 2**, `Snapshots/Golden/`. ⛔ **Tier 1 moving is a FAILURE, not a rebase.** Regenerate Tier 2 with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` and **review the diff** |
| ⭐ **AiShared 1216** *(+3, Batch 46)* · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### ⚠⚠ Measuring blueprint warnings — the one that has been wrong all along

```bash
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -t:Rebuild -v n --nologo \
  | grep -oE "warning BP[0-9]+: [^[]*" | sort -u          # ⭐ sort -u is mandatory
```

**MSBuild prints every warning twice** — once in the build, once in the end-of-build summary block. A
plain `grep -c` doubles it. *Every count in this programme's history (34, then 36) was the doubled
figure.* ⭐ **Current: 10, all `BP3010`.** (Batch 29 fixed the 6 compiler-synthesized orphans and
retired `BP3011`; the 10 that remain are authored and deliberate.)

⚠ `.Succeeded` **never invokes Roslyn.** Only the real generator path proves a blueprint compiles.

---

## 4 · Where the programme stands

**Tracker: open 58 · done 111** (+1 refuted) ([Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md)), reconciling
three ways — checkbox tally, per-complexity columns (⚠ take the **first** tag on a row), and the total
row. ⚠⚠ **Reconcile all three EVERY time.** The columns can agree with the Total and both still be
wrong: a missed tick is invisible to arithmetic and only shows against the checkbox tally.
⭐⭐ **STOP HAND-MAINTAINING THIS TABLE — derive it:**

```bash
python3 scripts/tracker-counts.py            # print the correct table
python3 scripts/tracker-counts.py --check    # exit 1 if the file disagrees with its rows
```

**The done column has been wrong in three consecutive batches, by a different mechanism each time,
and the open column was right every time** — 29 inherited drift · 30 a tick never added to its column ·
31 open decremented by 2 while done rose by 1. ⇒ **the error is hand-maintaining two representations of
one fact**, so the script exists. Run `--check` as part of verifying any returned batch.
⚠ The *refuted* row sits **outside** the Total, so the done tally is one higher. The script knows.

| Batch | State |
|---|---|
| **27** | ✅ verified — authoring seams, the three matrix axes, diagnostic identity |
| **28** | ✅ verified — the silent `default:` arm family + `GraphKind.Macro` and both fail-loud nets |
| **29** | ✅ **verified and merged** (`da13a6a`, ff-only) — **BP-80** macro surface · the **warning triage** (`BP-217`/`BP-218`, `BP-219` open) · **BP-131** `Return.Success`. See §7 |
| **30** | ✅ **verified and merged** (`4fe3538a`, ff-only) — ⭐ **macros work end to end.** `Stage2_5_ExpandMacros` + **all four** Stage 2 rails + `BP-219`; `BP-220` opened. See §7b |
| **31** | ✅ **verified and merged** (`119305e7`, ff-only) — ⭐ **the macro payoff is executed, and building it exposed a real defect in Batch 30's `BP1661`.** Plus **BP-83** · **BP-220** · **BP-111**. See §7c |
| **32** | ✅ **verified and merged** (`fbc100cd`, ff-only) — **Q26-A3 N exec-ins** landed clean; ⭐ **first batch where the tracker counts were right on arrival**. See §7d |
| **33** | ✅ **verified and merged** (`a8deb89f`, ff-only) — ⭐ **collapse works headlessly and the round-trip property holds.** ⚠ **PARTIAL by design**: the sink, undo and menu are **not** done, so it is not reachable from the canvas. `BP-221`/`BP-222` opened. See §7e |
| **34** | ✅ **verified and merged** (`53c407f1`, ff-only) — ⭐ **`BP-74` CLOSED: collapse is reachable, undoable, and refuses out loud.** `BP-221`/`BP-222` fixed; **`BP-223`** found and fixed. See §7f |
| **35** | ✅ **verified and merged** (`8b56367b`, ff-only) — ⭐ **`BP-75`/`BP-77`/`BP-80` all CLOSED.** A macro can now be authored by hand end to end. `BP-224`/`BP-225` filed. See §7g |
| **36** | ✅ **verified and merged** (`4242f304`, ff-only) — ⭐⭐ **THE MACRO PROGRAMME IS COMPLETE.** `BP-76` + `BP-82` closed. See §7h |

### The macro capability

⭐ **Design closed; the compiler half is DONE.** A macro can be authored, called, expanded, and run.

| | |
|---|---|
| [Architect_Question_25_Macros.md](Architect_Question_25_Macros.md) | *what* a macro is — **A1**, **B1**, **C1 now**, **D3** (1 exec-in, **N ≥ 0** exec-out), **E** six rails |
| [Macro_Implementation_Design.md](Macro_Implementation_Design.md) | *how each slice is built* — findings **F1-F5**, the splice algorithm, diagnostics, ⭐ **§7: all three restrictions ACCEPTED by the user** |
| ✅ **BP-79 landed** (as BP-216) | `GraphKind.Macro` + the Stage 5 skip + `MapGraphKind` now **throws** |
| ✅ **BP-80 landed** (Batch 29) | `ExecOutDecl`, `Graph.ExecOutputs`, `MacroCallNode`, all four projection halves, `BP1668`. ⚠ Row stays **open** for the two visual gestures (palette drag, `BP-77`'s *"Macros +"*) |
| ✅ **BP-81 landed** (Batch 30) | `Stage2_5_ExpandMacros` + `GraphFragmentCloner` + **four** rails (`BP1660`-`BP1663`), `BP1665`/`BP1667`, `[JsonIgnore] Node.OriginNodeId` |
| 📤 **BP-83 dispatched** (Batch 31) | debug provenance. ⭐ **Its "shape decision" is answered**: `CSharpEmitter:43-56` drops `OriginNodeId`, and `DebugMapEntry` has nowhere to put it |
| ⏭ Remaining | ⚠ **`BP1664` is UNBUILDABLE — do not attempt it.** `Graph` has no `LocalVariables` field (**BP-57**), so a macro cannot declare a local · BP-82's two library rails · **BP-80's visual half** (palette drag, `BP-77`) — the only part needing the user's eyes |

---

## 5 · Verified facts — do NOT re-derive these

⚠ Checked in Batches 27-28 against `a0961ae1`. **Coordinates drift** — Batch 30 found the design's `ComputeMergePoints:4269` had moved to `:4624`. **Re-grep any line number before trusting it.**

| Fact | Where |
|---|---|
| **Pin identity** = `SHA-256("pin:{nodeId:N}:{name}:{direction}")`, first 16 bytes, v5 bits | `DeterministicIds.PinId` |
| **Data pins are pull-based** — `ResolveDataPin`/`ResolveNodeOutput` walk *back* from the consumer and emit there | `Stage5_Schedule` |
| **Two caches, and the difference is load-bearing** — `_pinValueCache` is **per-block** (cleared at each boundary); `_statementPinCache` is **never cleared**, for values materialised once as a real local | `Stage5_Schedule:176`, `:178-190` |
| ⚠ The never-cleared cache's premise (*flat goto body, no nested scopes*) **fails inside inline bodies** — `IrOp_ForEach` emits inside braces, so a local there dies at the closing brace. Save/restore isolation exists at all three inline sites | BP-214 |
| **`BP4005`** — a data-out pull with no `ResolveNodeOutput` case is now an **Error**, and fires **zero** times on the real corpus | `DiagnosticCodes`, `Stage5:~3662` |
| **`GraphKind`** = `{ Function, Event, Construction, Macro }`, serialised as a **string** | `GraphTypes.cs:24`, `BlueprintJsonServices.cs:26` |
| Stage 5 **skips** macro graphs (⚠ *skipped, not errored* — a future macro library legitimately declares uncalled macros); `MapGraphKind`'s catch-all now **throws** | `Stage5_Schedule:~46`, `:~4610` |
| **`BP1650`** (no latent node in a called Function graph) is a **Stage 2** validator ⇒ ⚠ expansion at Stage 2.5 would bypass it — that is design finding **F1** | `Stage2_Validate:2167-2186` |
| Editor and compiler pin projections are **two halves that must move together** | `NodePinSchema` ⇄ `Stage0_Rehydrate` |
| `Hrot.AI.Behaviors` sets **`TreatWarningsAsErrors`** | its `.csproj:4` |

### ⭐ The pin-GUID recipe — use it on any report naming a pin GUID

This is what cracked BP-209 after three sessions guessed wrong. One minute, and it turns *"probably a
stale link"* into yes/no.

```python
import hashlib, uuid
def pinid(node, name, d):
    h = hashlib.sha256(f"pin:{uuid.UUID(node).hex}:{name}:{d}".encode()).digest()
    b = bytearray(h[:16]); b[6] = (b[6] & 0x0F) | 0x50; b[8] = (b[8] & 0x3F) | 0x80
    g = bytes(b[0:4][::-1]) + bytes(b[4:6][::-1]) + bytes(b[6:8][::-1]) + bytes(b[8:16])
    return str(uuid.UUID(bytes=g))
```

---

## 6 · Your own recurring failure modes — read before writing a handoff

⭐ **The pre-dispatch re-read has caught a real defect in three consecutive handoffs.** It is not
ceremony. Do it every time, against the rules *and* against the code.

| Failure | Instances |
|---|---|
| 🔴 **Asserting a mechanism you inferred rather than measured** | the `ArgTypes`→printed-`0` theory (refuted); `TreatWarningsAsErrors` claimed absent (it is set); BP3010 "should be downgraded" (already was); "click empty canvas shows graph properties is Unreal parity" (it is not) |
| 🔴 **Amending a handoff after dispatch** | three ID collisions ⇒ **rule 3: you allocate NO ids at all** |
| 🔴 **Rewriting a handoff header and silently dropping the standing-rules section** | Batch 27; caught on the second review |
| 🔴 **Carrying a baseline number forward instead of measuring it** | the warning count, doubled for the entire programme |
| 🔴 **Self-contradicting instructions** | Batch 28 said *"use `_statementPinCache`"* **and** *"mirror `SetShared`"* — which uses the other cache |
| ⚠ **Instructions that are wrong in substance** | told them to allocate a `ResultValue`; correct answer was none, because the written value already exists as a local. **They were right, I was wrong** |
| 🔴🔴 **Verifying that a thing EXISTS without verifying anything USES it** | ⭐ **`BP-223`.** Batch 34's handoff said the refusal surface was *"wired at `BlueprintDocumentFactory:346`"*. The enqueue half was real; **nothing read the queue** — the only reader in the repo was the NodeEdit demo shell's, so every notification since BP-24 was discarded. ⚠ **Written into a handoff that warns about trap #5** |
| ⚠ **Reading an emitter instead of running it** | `BP-221` was **two** holes. Reading found the missing helper loop; only *reproduction* found the call site passing Instance-shaped context args into an AiPrimitive `TickCore` — **five `CS0103`s, not one** |

### ⭐⭐ The rule that would have caught most of the above — ask the REVERSE question

**Existence is not wiring.** For every claim of the form *"X exists / is already wired / is handled"*,
**verify the consumer side before it goes in a handoff**:

| Claim | ⛔ Not enough | ✅ Also check |
|---|---|---|
| *"the notification surface exists"* | the `Enqueue` | ⭐ **who dequeues it** |
| *"the helper is emitted"* | the emit site | **what the call site passes** |
| *"the command is handled"* | the `case` | **that it mutates, not just returns `true`** |
| *"the pin is projected"* | one half | **both halves, and who reads the projection** |

### ⚠ Use the Codebase Memory graph — and know what it is bad at

`.claude/CLAUDE.md` makes the graph tools **mandatory before reading files**. Use them.
⚠ **But measured on this repo, 2026-08-11: they are a supplement, not a substitute.** An inbound
`trace_path` on `ToastQueue.TryDequeue` returned the true reader **plus six false positives** from
plain **method-name collision** across unrelated queue types (`BatchListPool.Get`,
`HitFlashSystem.OnUpdate`), **and missed a real reader** that a `git grep` finds instantly.

⇒ ⭐ **Graph first for discovery and call chains; `grep` to confirm before asserting anything in a
handoff.** ⛔ Never state a coordinate in a handoff on the graph's word alone.

⭐ **The implementation session has repeatedly corrected you and been right.** Read their pushback as
evidence, not noise.

---

## 7 · Batch 29 — ✅ VERIFIED AND MERGED at `da13a6a`

Implementation ran on **`claude/hrot-implementation-j1jvin`** (⚠ *not* the `sdmspn` branch the lane
table names — the user moved the lane). ✅ **Rule 7 followed exactly:** they branched from `1af9bea`,
this branch's head, so the handoff, its stamp and the lane correction were all in view.

**Gates, coordinator-run on their tree:** build **0 errors**, warnings **77 → 69** · BP diagnostics
⭐ **18 → 10 distinct**, and the *right* 10 remain (all 6 synthesized + both `BP3011` gone, the 10
authored orphans correctly untouched) · Blueprints **3128**/0/10 · AiShared 1213 · BTree 612 ·
Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131.

⚠ **One red on the first run, and it is *not* theirs:**
`WhenNodePerfTests.ReadEqsResultNode_Under80ns_perInvocation` — a wall-clock perf assertion that
measured **25 µs against an 80 ns budget** on this shared cloud VM. Green on three subsequent runs.
⭐ **This is exactly [BP-111](Blueprint_Issues_Tracker.md)** (*"wall-clock perf assertions flake under
full-suite load, and the known-flake list is incomplete"*) — **and BP-111 predicted the wrong sibling**:
its row names `WhenNode_EqsResult_Under150ns_perTick`, but the one that actually flaked here is
`ReadEqsResultNode_Under80ns_perInvocation`. 📌 **Feed that to Batch 30** — it is evidence for BP-111,
not a Batch 29 defect.

### What they got right that is worth keeping

| ⭐ | |
|---|---|
| **`BP-217` is a one-line reorder with a real proof** | `EliminateOrphanNodes` now runs **before** `MaterializeDefaultPinLiterals`. The compiler was synthesizing literals for nodes it was about to delete, then warning about its own scaffolding. They argue — correctly — that reordering **cannot change which authored nodes are eliminated**, because a synthesized literal's only link is into the pin it was made for and `CollectReachable` walks both directions, so it can never bridge two components |
| **They caught a miss I did not flag** | `EnrichReturnPins`' early return would have silently **stopped `BP1655` firing** on an AiPrimitive that declares Outputs. They made the Success pin additive-and-last instead. *"Losing a diagnostic is the same class of defect as emitting a wrong value."* |
| **H2 done as defence in depth** | projection gate **and** name exclusion, with the right reason: the two mechanisms fail independently, and a **hand-authored** asset can carry pins no projection produced |
| **`BP1668` added as asked** | an unexpanded `MacroCallNode` is now an **Error**, not `BP4004`'s warning-and-walk-on |
| **`IrPrinter` updated too** | so an IR dump distinguishes the constant form from the runtime-condition form. Not asked for |
| **§1.4 ruled** | they split BP-80 from BP-81 and said so — BP-80's row stays **open** for the two visual gestures |

### ⚠ The one thing the coordinator got wrong, and fixed

The tracker's **`RW-L` done column was 43 and the Total 88; both were one short** — and the drift
**predates Batch 29** (present at `1af9bea`, and back at the 41/85 figures). Batch 29's own delta was
exactly right. Corrected to **44 / 89** after merge. It reconciles three ways now; the note in the
tracker records the method, including that the *refuted* row sits **outside** the Total.

---

> ⏭ **Batch 42 dispatched — [finish `BP-57`, wiring what Batch 41 built](HANDOFF_Batch42_Local_Variables_Wiring.md).**
> ⭐⭐ **`BlueprintLocalVariableSchemaSource` is complete and ORPHANED** — `grep` finds nothing that
> constructs it outside its tests. ⇒ **this batch is mostly WIRING**: the section that projects it,
> a delete that uses the reference count Batch 41 built and left unused, and ⛔ **undo, which no
> locals gesture has at all today.** §4 (the badge) moves the two NodeEdit gates and is the stop point.

> ⏭ **Batch 43 dispatched — [ONE ITEM: the Local Variables section](HANDOFF_Batch43_Local_Variables_Section.md).**
> ⛔⛔ **Asked for twice, skipped twice — and the common factor is mine:** I marked it *"🟢 Sonnet takes
> the section wiring"* both times. ⇒ ⭐ **one item, on Opus, delegated to nobody, nothing else in the
> batch.** ⭐⭐ **It is the last thing between `BP-57` and closed** — source, count, refusal and undo
> are all built; there is simply nowhere to declare a local.

> ⏭ **Batch 44 dispatched — [`U-1` the golden harness, then `U-2` the first thing it protects](HANDOFF_Batch44_Golden_Harness_And_Compiler_Ownership.md).**
> ⭐⭐ **The `U-` sequence opens.** `U-1` ships **no product change**: it records `StructureHash`, every
> emitted struct field and the diagnostic multiset across the 42-asset corpus, plus the generated source
> **as files**, because *"a hash names the asset; a stored file names the LINE."* ⭐ **Every later `U-`
> task's success condition is "the output did not change" and is unfalsifiable without it.**
> ⭐ **`U-2` is the smallest real change in the programme** and its second gate is *"golden unchanged"* ⇒
> **it is how we learn whether the net holds a fish.** ✅ **Both compiler-only — chosen because the
> visual check is unavailable.** ⭐ **Reuses `TestData.ReadOrRegenerateSnapshot`; the three existing
> `*EmitGoldenTests` are the precedent, so `U-1` is the sweep they imply, not a new concept.**

> ⏭ **Batch 45 dispatched — [`U-3`: `(kind, index)`, and it closes `BP-226`](HANDOFF_Batch45_Kind_Index.md).**
> ⭐⭐ **The first task the net was built for** — its Pass 1 is *"golden unchanged"*, which only became
> a real assertion yesterday. ⭐ **Coordinator finding added to the handoff and NOT in `BP-226`'s row:**
> `VarFieldName`'s `WorkingState` branch tests `index < ws.Count` and reads **`ws[index]`** — ⛔ **the
> index is never rebased**, so even reaching that branch resolves the wrong field; and **`Parameters`
> is never consulted at all.** ⭐ **The entrenchment worry is dead** — `BP1024`/`BP1031` mean no shipped
> asset has both lists populated, so the corpus cannot depend on the broken behaviour ⚠ **and therefore
> "golden unchanged" alone would also pass a refactor that fixed nothing.** ⇒ ⭐ **Pass 2 and Pass 3
> must be asserted RED before the change.**

> ⏭ **Batch 46 dispatched — [`U-4` + `U-5`: the editor's turn at the same defect](HANDOFF_Batch46_Third_Source_And_Honesty.md).**
> ⭐⭐ **`U-3` killed an untagged `int` in the compiler; `U-4` kills a two-valued `bool` over the same
> three-list model in the editor** — `BlueprintVariableSchemaSource(asset, bool isParams, …)`, with ten
> branches riding it and ⛔ **`Variables` not representable at all.**
> ⭐ **`U-5`'s `BP-230` is trap #5 built into an INTERFACE:** `UpdateVariableRole`/`UpdateVariableScope`
> have **default bodies `{ }`** ⇒ a source that never implements them compiles silently and does
> nothing. ⭐ **`Q-k` already ruled the semantics — read-only, a move not a toggle** — so "honest" means
> the surface must **say so**, not implement a setter.
> ⚠ **This is the one batch since 38 that SHOULD move the AiShared gate (1213).**

> ⏭ **Batch 47 dispatched — [`U-7` + `U-8`: the type-existence rail, then the picker](HANDOFF_Batch47_Type_Existence_Rail.md).**
> ⚠⚠ **ORDER SWAP:** these are the plan's *"batch 48"* tasks, **pulled ahead of `U-6`/`U-13`/`U-16`**,
> which hard-require the visual check. ⭐ **Nothing depends on the order** — `U-8` needs `U-7`; `U-6`
> needs `U-4`/`U-5`, which are done.
> ⭐ **`BP-228`: the dot is doing the work of a type check** — contains a dot ⇒ trusted verbatim.
> ⭐ **`Q-j`'s seam already exists** (`IClrSignatureResolver` on `CompileOptions`), and ⭐⭐ **Batch 44
> measured the in-process and semantic-model paths 42/42 byte-identical** ⇒ same oracle at both ends.
> 📐 **Still open and handed to them: does the EDITOR get an oracle at all?** ⭐ The review's lean is
> yes; ⛔ **`U-7` alone is shippable if wiring it reaches past `CompileOptions`.**

> ⏭ **Batch 48 dispatched — [`U-9`: the tagged declaration](HANDOFF_Batch48_Tagged_Declaration.md).**
> ⛔⛔ **The one rule: the tag must NOT reach JSON.** The serializer keeps writing the old three-list
> shape byte for byte, or `U-9` and `U-10` collapse and the migrator loses its own revert.
> ⭐ **Coordinator finding, from `Declarations.cs` and not in the plan:** `ParameterDecl` and
> `VariableDecl` are **not the same shape** — the parameter type lacks `IsEditable`,
> `IsExposedOnSpawn` and `Category` ⇒ **the down-projection DROPS three members**, and the drop must be
> enumerated in code rather than implicit in a mapping that forgot a line.
> ⚠ **Pass 2 (reflection over both projections) is the gate that matters** — ⛔ **a forgotten member
> reddens NOTHING**: not the golden corpus, not the round-trip, not the build. Same reason `BP-226`
> hid behind `BP1024`/`BP1031`.

## 7t · Batch 47 — ✅ VERIFIED AND MERGED at `d98b98bf` — ⭐⭐ **`BP-228` CLOSED, and the oracle question was settled by MEASUREMENT**

**Gates — all eight, coordinator-run on the merged tree:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** — unmoved |
| Blueprints | **3474 total / 3464 passed / 0 failed / 10 skipped** (**+9**) |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit **208 / 131** | unmoved |
| ⭐⭐ **Golden 42/42 both tiers** | ✅ **no `Snapshots/Golden/` file changed** |
| `tracker-counts.py --check` | **clean — sixteen batches.** open **58** / done **111** ⇒ ⭐ **`BP-228` moved across** |

✅ **Rule 7 verified mechanically:** their first commit's parent **is** the dispatch commit `31189dbaa`.
➕ **`BP1671` allocated** — the rail's diagnostic. ⇒ `BP1672+` is next free.

### ⚠ They corrected my §1.2, and the correction is the interesting part

⛔ **I wrote *"the seam already exists — do not build a resolver."*** ⭐ **True for METHODS, not for
type existence:** `TryResolve` takes a **type AND a method** and returns **one bool** ⇒ a `false`
cannot distinguish *"no such type"* from *"no such method."*

⇒ **One member added, `TypeExists`, with ⭐⭐ NO default body** — and their reason cites Batch 46 by
name: *"a default returning `true` would be the interface asserting a type exists on an implementer's
behalf, which is the exact shape of the defect this rail closes."* ⭐ **Two batches after the
`SupportsRoleScopeEditing` ruling, the same principle applied unprompted to a different interface.**

### ⭐⭐ The oracle question — answered by counting call sites, not by arguing

⭐ **Measured: exactly ONE production site supplies a resolver**, and of the three `CompileOptions`
sites ⛔ **the editor's has NO production caller at all.**

⇒ ⭐⭐ **There is no editor compile path to attach an oracle to** — which retires the plan's §4 open
question rather than answering it. ⇒ `U-8` makes the picker **safe by construction** instead:
`SelectableTypeIds` is the primitives **plus every discovered `[BlackboardDtoStruct]` FQN**, and
⭐ **discovery is itself the existence proof.**

⭐ **The rail therefore guards the BUILD, which is where the defect bit** — and the fallback contract
(no oracle ⇒ no opinion) is as load-bearing as the rail itself. ⚠ **Exactly what §5 asked them to
report: not that Pass 2 passes, but how many call sites supply one.**

### 🔴 `BP-87`'s restored lock found a LIVE defect on its first run

⛔ **`System.String` was offered by the picker and can never compile as a variable** (`BP1503`).
Removed — **the `FixedString` types were always the supported ones.**

⭐ **And a second one behind it:** the picker was **13 hardcoded primitives with no structs**, while
the Variables panel **did** offer structs ⇒ 🔴 **whether a struct variable could be declared depended
on which window was open.**

📌 **One more, unprompted:** the list is now `Lazy` rather than a static initializer — ⭐ **reflecting
over loaded assemblies at type-load time freezes whatever happened to be loaded.**

---

## 7s · Batch 46 — ✅ VERIFIED AND MERGED at `ea53e7e0` — ⭐⭐ **`BP-230` + `BP-231` CLOSED, and the visual question was answered WITHOUT the visual check**

**Gates — all eight, coordinator-run on the merged tree:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** — unmoved |
| Blueprints | **3465 total / 3455 passed / 0 failed / 10 skipped** (**+14**) |
| ⭐ **AiShared 1213 → 1216** (**+3**) | ✅ **the one gate this batch was expected to move, and it moved for the stated reason** |
| BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | unmoved |
| ⭐⭐ **Golden 42/42 both tiers** | ✅ **no `Snapshots/Golden/` file changed** — editor-only, as declared |
| `tracker-counts.py --check` | **clean — fifteen batches.** open **59** / done **110** ⇒ ⭐ **`BP-230` and `BP-231` both moved across** |

✅ **Rule 7 verified mechanically:** their first commit's parent **is** the dispatch commit `61fd40b44`.

### ⭐⭐ The finding of the batch — and it did NOT need a screen

⛔ **`BP-230` has carried an open question since Batch 38: are the `Role`/`Scope` columns
drawn-but-dead, or hidden?** ⭐⭐ **Answered from `VariablesPanelControl` rather than a screenshot:**
the Role combo is gated on **`!IsReadOnly` alone**, and the blueprint source returns `false` ⇒
🔴🔴 **the combo was DRAWN, LIVE, and its result DISCARDED.**

⭐ **That is the worst of the three possibilities** — a designer could set a Role, watch it take, and
have nothing happen — ⚠ **and it was headlessly answerable for eight batches.** 📌 **Lesson worth
keeping: "needs the visual check" was true of the RENDERING, not of the QUESTION.**

### ⭐ The fix is a capability, not a setter — and the interface stops volunteering to lie

| | |
|---|---|
| ⭐⭐ **`SupportsRoleScopeEditing` has NO default body** | ⇒ **every implementer must answer.** The panel gates on it and falls back to read-only **text** rather than a dead control |
| ⭐ **The two setters keep default bodies — but now THROW** | *"a default body is the interface volunteering to lie on an implementer's behalf."* ⇒ **honest in both directions:** a source that says it cannot edit is never called; one that says it can and forgot **fails loudly** |
| ✅ **`Q-k` respected exactly** | read-only for blueprints is **a move, not a toggle** — so the surface says so instead of implementing a setter |

### 🔴🔴 They caught a GATE that did not move when it should have

⛔ **The first full run left AiShared at 1213 after changing that very interface** — ⭐ **because the
contract change had no coverage in the assembly it landed in.** ⇒ three tests added there, **1216**.

⚠⚠ **This is trap #5 at the GATE level, and it is the subtlest instance the programme has hit:** the
handoff said *"expect 1213 to move"*, the number did not move, ⛔ **and a green suite would have read
as proof rather than as the absence of one.** ⭐ **They noticed the silence.**

### ⚠ They corrected my §2.1 advice, and were right

⛔ **I wrote *"do not re-derive the count; mirror the locals source."*** ⭐ **It could not.** The locals
source counts **by id only** — correct there, because `FindLocalIndex` has **no name fallback** —
⚠ **wrong for asset variables, because the compiler DOES match them by name.** ⇒ the new count
resolves **exactly as `Stage5.FindVariableRef` does**: id first, then name, both in list-priority
order. 📐 **Mirroring the shape would have produced a count that disagrees with the compiler.**

✅ **`BP-231`:** remove drops ids from the order list; ⭐ **rename correctly leaves it alone, and that
is test-locked** so a later name-keyed rewrite cannot creep in.
✅ **The `COMBINED index` comment is fixed** — and they confirm it was still in the tree, as flagged.

---

## 7r · Batch 45 — ✅ VERIFIED AND MERGED at `74526bf0` — ⭐⭐ **`BP-226` CLOSED, and my finding was REFUTED**

**Gates — all eight, coordinator-run on the merged tree** *(NodeEdit measured, not inferred, this time)*:

| | |
|---|---|
| Solution build | **0 errors** · BP diagnostics **10 distinct**, all `BP3010` — ⛔ **unmoved** *(`-t:Rebuild`, `sort -u`)* |
| Blueprints | **3451 total / 3441 passed / 0 failed / 10 skipped** (**+3**) |
| ⭐ **AiShared 1213** · BTree **612** · Breakpoints **130** · Generators **193** | unmoved |
| NodeEdit Core **208** · UI **131** | unmoved |
| `tracker-counts.py --check` | **clean — fourteen batches.** open **61** / done **108** ⇒ ⭐ **`BP-226` moved across** |
| ⭐⭐ **Golden: 42/42, BOTH tiers, no regeneration** | ⇒ **the refactor is behaviour-preserving for every shipped asset, and that is now a measurement rather than a hope** |

✅ **Rule 7 verified mechanically:** their first commit's parent **is** the dispatch commit `fdd2e90b9`.

### 🔴🔴 My §1 finding was WRONG, and they were right to refuse it

⛔ **I claimed `VarFieldName`'s `WorkingState` arm "never rebases the index" and should read
`ws[index - fields.Count]`.** ⭐⭐ **False — and building it would have introduced a defect in the one
case that worked.**

⭐ **The mechanic, from the code I quoted myself:** `FindVariableIndex` returns `i` from **inside**
whichever loop matched ⇒ **the index is LIST-RELATIVE, never combined.** ⇒ `ws[index]` is correct for a
WorkingState-sourced index; subtracting would have broken **every shipped AiPrimitive**, where
`Variables` is empty. ⛔ **The bug was only ever "which list", never "which offset."**

📐 **I imposed a combined-index model the producer never used.** ⭐ **They named the likely source:**
`FindParameterIndex`'s doc comment describes `FindVariableIndex` as returning *"a **COMBINED**
index"* — ⚠ **a wrong comment that misled a reader within one batch of being written down.**

### 📌⚠ The one thing still outstanding — that comment is NOT gone

⛔ **Their commit says the comment *"is gone with the method."* It is not.**
✅ **Coordinator-verified on the merged tree: `Stage5_Schedule.cs:4619` still says it**, because
`FindParameterIndex` **survives** (`IrOp_ReadParam` still needs it) — only `FindVariableIndex`'s
**shape** changed. ⇒ ⚠ **the comment is now doubly wrong**: the combined-index claim was always false,
and the return type it describes no longer exists.
⇒ 📌 **Carried into Batch 46 as a one-line nit.** ⭐ **It is the single highest-value comment in the
file to fix — it has a demonstrated victim.**

### ⭐ Three decisions they made that the handoff did not ask for

| | |
|---|---|
| ⭐⭐ **`VariableKind.Unresolved = 0`, deliberately the default** | ⇒ a zero-initialised `VariableRef` means *"nobody set this"* **and throws.** ⛔ **Had `Variable` been 0, a forgotten assignment would have silently meant `Variables[0]`** — the exact defect class the task exists to remove, re-created in the fix |
| ⭐ **The emitter now picks the CONTAINER, not just the field** | forced by carrying the kind: **a `Parameter` lives on a different struct**, and the bare `int` could not say so |
| ⭐ **Out-of-range now THROWS** | the old `__var_{index}` fall-through is gone: ⭐ **with the kind carried there is no legitimate way to reach it**, so silence would only hide a Stage 5 / declaration-list disagreement until Roslyn named a generated file |

✅ **`VarFieldName(int)` no longer exists** ⇒ ⭐ **the wrong call is UNWRITABLE, not merely unwritten** —
which is what the handoff asked for and is the difference between a fix and a patch.
✅ **`BP1670`'s assertion survives, restated** from *"index < 0"* to *"no kind resolved"*.

### ⭐⭐ And they caught their own vacuous test — which is exactly why §3 asked for red-first

⛔ **The first draft of the new tests PASSED before the fix** — an `Event`-graph fixture is eliminated
whole, so `TickCore` emits an **empty body** and the assertions had nothing to be wrong about.
⇒ ⭐ **A test that cannot fail is `BP-230`'s shape in test form.** ⚠ **Only running it red-first found
it.** *(Batch 44 found two defects the same way. Third batch running.)*

---

## 7q · Batch 44 — ✅ VERIFIED AND MERGED at `ba337568` — ⭐⭐ **THE NET IS BUILT, AND IT BITES**

**Gates — all eight, coordinator-run on the merged tree:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** — unmoved |
| Blueprints | **3448 total / 3438 passed / 0 failed / 10 skipped** (**+135**) |
| ⭐ **AiShared 1213** · BTree **612** · Breakpoints **130** · Generators **193** | unmoved |
| NodeEdit Core **208** · UI **131** *(no `--no-build`)* | unmoved |
| `tracker-counts.py --check` | **clean — thirteen batches running.** open **62** / done **107** ⇒ ⭐ **`BP-229` moved across** |

✅ **Rule 7 verified mechanically:** their first commit's parent **is** the dispatch commit `3b1dba8e6`.

### ⭐⭐ Three corrections to the plan — all measured, all mine to have missed

| | |
|---|---|
| 🔴⭐ **The corpus compiles as a SET** | ⛔ `SmokeGuard` and `SmokePatrol` fail `BP1301` with `SiblingSignatures: Array.Empty<>` — **they call each other.** ⭐ **Production has always built a catalog**: `BlueprintIncrementalGenerator` parses **every** `AdditionalFiles` entry through `BlueprintSignatureParser` and hands the whole thing to every compile. ⇒ **the preload and the catalog are two independent prerequisites, and the plan had only the first** — ⚠ **so "one `typeof` touch ⇒ 42/42" was wrong; it gives 40/42** |
| 🔴⭐ **`CompilerMode.Release`, hardcoded** | ⛔ `CompileOneAsset` pins Release ⚠ **regardless of MSBuild configuration** ⇒ a Debug-mode harness would have baselined **~40 extra `DebugProbe.NodeEnter` lines per asset — output that never ships.** 📌 **Not a defect:** `EditorMetadata.CompilerMode` + `QuickReloadService` are the debugger's live re-instrumentation path. 📌 **Known gap recorded: Debug-mode emit is NOT covered by the baseline** |
| ✅ **Cost** | **849 ms** for 131 tests / ~170 compilations — **~4× the work the 634 ms figure covered.** ⭐ A gate, comfortably |

### ⭐⭐ §1.5 — the parity question is ANSWERED, and the answer is clean

**42/42 byte-identical**, in-process reflection resolver vs production's `RoslynClrSignatureResolver`,
compared through `EmitCompilerGeneratedFiles`. ⭐⭐ **The two things that differed on the first attempt
were the MODE and the SIBLING CATALOG — my two errors above, not the resolver.** ⇒ **the harness
measures what production ships**, which is the assumption every later `U-` task rests on.

### 🔴🔴 Two test-infrastructure defects this batch paid for — both are trap #5

| | |
|---|---|
| ⛔⛔ **`ResolveSnapshotsDir` walked up from `bin/`** | ⇒ regeneration wrote baselines **into `bin/` and never into git.** ⚠ **Harmless for the existing snapshots** — `PreserveNewest` kept them in step — ⭐ **silently fatal for a NEW one**: the baseline appears to exist, the suite is green, and nothing is committed. Now anchored on the test project directory |
| ⛔⛔ **A bite test pointed at a committed baseline path** | under `BLUEPRINT_REGENERATE_SNAPSHOTS=1` the helper **writes** ⇒ the first run **overwrote `ManagedCollectionDemo`'s Tier 1 with the MUTATED layout.** ⭐⭐ **The proof-it-bites test corrupted the very net it exists to prove.** Bite tests now compare against a scratch copy |

⭐ **Both were found by doing §1.4 rather than by reasoning about it.** ⚠ **Neither is visible from a
green suite** — which is exactly why *"a harness that has never failed is not a harness"* was the item
that mattered.

### ⭐ The bites, one per tier, exactly as asked

| mutation | reddened |
|---|---|
| swap two of `ManagedCollectionDemo`'s six variables | ⭐ **Tier 1 + Tier 2 + `StructureHash`** — and the report **names the moved field** |
| change emitted text without moving a field | ⭐ **Tier 2 only** — which is the whole reason for two tiers |
| introduce one extra diagnostic | **Tier 1** (multiset) |

📌 **The 250 KB failure-message wart (§1.3) was fixed, not waved through:** the message now leads with
the **first differing line and context**, inlining both files only under a **4 KB budget**.

### ⭐ `U-2` — the placement decision is the whole task, and they reasoned it out

| | |
|---|---|
| ⭐ **Copy taken immediately after Stage 0** | Stage 0's pin rehydration is **contractually visible** (`Compile`'s own comment: *"intentional rehydration"*) ⇒ **an earlier copy would have silently changed documented behaviour.** Stage 2 in between is a pure validator |
| ⭐⭐ **Fresh `Link` OBJECTS, not just fresh lists** | ⚠ **and this is not belt-and-braces:** `MacroExpander` assigns `link.ToNodeId`/`ToPinId` **in place** (`:205`, `:258`) ⇒ **a list-only copy leaves the caller's own wires rewired while passing a node-count test** |
| ⭐ **Nodes stay SHARED, deliberately** | nothing mutates a node after Stage 0 (checked across 2.5/3/4), and cloning would have to preserve node ids — **the `DebugMap` and every diagnostic are keyed by them** |
| ⭐ **Built on `Graph.WithNodesAndLinks`** (`BP-220`) | ⇒ **`LocalVariables` comes across without anyone having to remember it** — the exact hazard the handoff flagged |
| ⭐ **Four gates, including the anti-vacuity one** | the macro **still expands** in the compiler's copy — ⛔ otherwise *"nothing changed"* is also satisfied by a compiler that skipped the splice |
| 🔴 **Revert-goes-red** | removing the copy reddens exactly the ownership test ⭐ **and the golden corpus stays green with and without it — which IS `U-2`'s Pass 2** |

### 📌 Coordinator's own correction to §7p

⚠ **§7p said "all eight, coordinator-run"; I ran six.** The two NodeEdit gates were **inferred** from
Batch 43's diffstat (it touches no `FDP/` file), not measured. ✅ **Measured now at `ba337568`:
208 / 131 — the recorded values were right, the method claimed was not.**

## 7p · Batch 43 — ✅ VERIFIED AND MERGED at `3583acd4` — ⭐⭐ **THE SECTION LANDED. `BP-57` is CLOSED**

**Gates — coordinator-run on the merged tree** ⚠ *(six measured here; see the correction at the end of §7q)*:

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** — unmoved |
| Blueprints | **3313 total / 3303 passed / 0 failed / 10 skipped** (**+15**) |
| ⭐ **AiShared 1213 — unmoved**, third batch running | BTree **612** · Breakpoints **130** · Generators **193** |
| ⚠ NodeEdit Core **208** · UI **131** | ⚠ **inferred from the diffstat here, MEASURED at Batch 44** — unmoved either way (the badge was correctly not built) |
| `tracker-counts.py --check` | **clean — twelve batches running.** open **63** / done **106** (+1 refuted) |

⭐ **`BlueprintMyBlueprintModel.cs` is touched for the first time in three batches**, and the tracker
was updated — both of the things 41 and 42 skipped.

### ⭐ What landed, and the four decisions inside it

| | |
|---|---|
| ⭐⭐ **The section follows the canvas through `Func<Guid>`** — `AiCanvasContext.CurrentGraphId` | 📐 **shape (a), and they justified it structurally, not by preference:** the switcher is built **per document** by the factory; the model is owned by a **perspective-bound** window. Neither holds a reference to the other, so shape (b) would have been a **new document-factory → perspective-window edge.** ⭐ `BP-72` met this exact wall and chose a polled provider — **they followed it instead of inventing a second mechanism**, which is what the handoff asked |
| ⭐ **`Changed` fires on switch — and the section does not depend on it** | `SyncCurrentGraph()` is polled from the window's draw (`:339`), idempotent, and `Retarget` resets the snap so a reopened document is not swallowed as *"same as last time."* ⚠ **But they measured that `MyBlueprintPanel.DrawSections` calls `GetItems` every frame** ⇒ the panel follows the canvas through the **delegate**; the event exists because `IMyBlueprintModel`'s contract has it and **a consumer that caches would show the previous graph's locals.** ⭐ Correct for the reason, not for the ritual |
| ⭐ **`Macro`: section AND `[+]` present, refusing out loud** through `IEditorIndicators` | 📐 their call, and ⭐ **they reported that it was FORCED anyway** — `_sections` is `static readonly`, so `CanCreateItems` cannot vary per graph. ⚠ **Naming a constraint as a constraint rather than dressing it as a choice** is the honest report the handoff wanted |
| ⭐⭐ **`local:{id}` routes rename/delete/duplicate to the SOURCE, not to `RenameItem`/`DeleteItem`** | because `RecordItemEdit`'s snapshot covers the **asset's** declaration lists only ⇒ routing locals through it would have produced ⛔ **an undo that restores nothing** — trap #5, arrived at from the code rather than from the handoff |
| ⭐ **Duplicate was not optional, and they found that themselves** | `MyBlueprintContextMenu` offers duplicate for **every `IsRenamable` item** ⇒ without an arm the entry appears and does nothing. ⭐ Exactly `BP-12b`'s shape, **not asked for in the handoff** |
| ✅ **`BP1671+` untouched** — no new diagnostic was needed | ➕ **`BP-234`** is the only id allocated |

### ⭐ The inert-button rail was asserted, not assumed

`BP-12c` shipped twice (Custom Events, Macros). ⭐ **`TheCreateCommandIsRegisteredByTheProductionRetarget`
drives the real `BlueprintDocumentFactory` path** (`:1683`) rather than the descriptor's string.
✅ **Coordinator-verified independently:** the command exists at exactly one production site, and
`EditorSubsystem:2264` feeds both `currentGraphId` and `indicators` from `ctx`.

### ⚠ One gap they found in their own work and RECORDED rather than patched

⛔ **`BlueprintLocalVariableSchemaSource.AddVariable` appends unconditionally** — right for the
generated-name blackboard surface it was written against, ⚠ **wrong for a modal**, which can now create
two locals of one name. ⭐ **The guard lives in the window's confirm path** (`:245`) so the **source's
contract stays as `U-6` will find it.** 📌 **Flag for `U-6`:** the unification absorbs this source, and
the duplicate-name rule belongs in the absorbed one — not left in a window.

### 📌 `BP-234` — filed, and the handoff's framing of it was wrong

I asked for a **reorder** warning. ⭐ **They refused it and were right:** add and delete change
`StructureHash` by the **same mechanism**, so a warning on the drag gesture would **imply the other two
are safe** — the misleading half of a half-truth. ⚖️ Ruling: one statement in the **hot-reload** story,
covering add/remove/reorder alike. **`RW-L`, open.**

### ⛔ Not covered headlessly — and it is the thing this batch is

⛔ **That the panel actually DRAWS the section.** ⚠ **The visual check has not run for NINE batches**,
and *"present and empty"* / *"follows the canvas"* are precisely what a headless test passes while the
panel shows nothing. ⭐ **This needs the user at a screen before the `U-` sequence buries it.**

📌 **Housekeeping:** `claude/batch39-locals-preserved` is fully merged and can be deleted.

---

## 7o · Batch 42 — ✅ VERIFIED AND MERGED at `57cd6161` — 🔴 **§1 SKIPPED AGAIN. `BP-57` still not closed**

**Gates:** build **0 errors** · Blueprints **3298 total / 3288 passed / 0 failed / 10 skipped** (**+9**) ·
⭐ **AiShared 1213 — unmoved** · counts clean.

### 🔴🔴 The pattern worth naming — **two batches, same omission**

⛔ **`BlueprintMyBlueprintModel` is STILL untouched.** ⇒ ⭐⭐ **A designer still cannot declare a local
from the editor.** `BP-57` cannot be ticked.

| batch | asked for | delivered |
|---|---|---|
| **41** | §1 source · **§2 section** · §3 picker · §4 delete · §5 badge · §6 nit | §1 · §3 |
| **42** | **§1 section** · §2 delete · §3 undo · §4 badge · §5 nit | §2 · §3 |

⚠ **The section was item §2 in one handoff and item §1 in the next — listed FIRST both times — and was
skipped both times.** ⭐ **In both handoffs I marked it *"🟢 Sonnet takes the section wiring."***
⇒ 📐 **That is the common factor, and it is mine: the one item I delegated is the one that never
lands.** ⛔ **Batch 43 must keep it on Opus and make it the ONLY item.**

📌 **Also skipped twice:** the tracker (`BP-57`'s row records **none** of 41 or 42), the doc-comment nit,
and the badge. ⚠ **Neither batch said where it stopped**, which both handoffs asked for.

### ⭐ But the model layer is now genuinely finished, and finished well

| | |
|---|---|
| ⭐⭐ **Delete: ruling (b), refuse while referenced** — ⭐ **and they found the repo had already ruled this way** | `DeleteItem`'s own comment says deleting a designer's nodes because a declaration went away *"is not recoverable."* **They matched existing policy instead of inventing one** |
| ⭐ **And diverged from it deliberately, in one direction, with a reason** | an asset variable's references are visible where it is declared; ⚠ **a LOCAL's can sit in another graph the designer cannot see from the current canvas.** ⇒ refusing **with a count** tells them something they could not otherwise learn; `BP1670` tells them only after a build |
| ⭐ **Refusals gathered BEFORE any mutation** | a batch containing one referenced entry **deletes nothing**, rather than half-deleting and then complaining |
| ⭐⭐ **The ruling makes `BP-225`'s trap UNREACHABLE rather than merely avoided** | because no nodes are ever removed, the undo entry has only declarations to restore ⇒ *"it cannot restore a declaration and forget its references"* |
| ⭐ **Undo: snapshot, never prediction** | mirrors `RecordItemEdit` · **all graphs, not the current one**, so a graph switch between edit and undo cannot silently restore nothing · **deep copies, because rename mutates in place** |
| ⭐ **A gesture that changes nothing records no entry** | `BP-204`'s degenerate case — *"an undo stack full of no-ops is its own defect."* **Not asked for** |
| ✅ **Revert-goes-red** | restoring the naive `RemoveAll` and bypassing the record seam reddens **5 of 9** |

⇒ ⭐ **Everything behind the surface is done: source · honest count · refusal · undo.** ⛔ **The surface
is the whole of what remains.**

---

## 7n · Batch 41 — ✅ VERIFIED AND MERGED at `748f1f79` — ⚠ **PARTIAL: §1 and §3 only. `BP-57` is NOT closed**

**Gates:** build **0 errors** · Blueprints **3289 total / 3279 passed / 0 failed / 10 skipped** (**+20**) ·
⭐ **AiShared 1213 — UNMOVED**, which was §1's explicit gate · counts clean.

### ⛔ What is NOT built — and this is the headline

| | |
|---|---|
| ⛔ **§2 — the Local Variables SECTION** | `BlueprintMyBlueprintModel` is **untouched**. ⭐ **There is still nowhere to DECLARE a local from the editor** |
| ⛔ **§4 — rename / delete** | no command, no undo entry, no delete-while-referenced gesture |
| ⛔ **§5 — the node badge** · ⛔ **§6 — the doc comment** | `INodeModel` has no badge; `GraphTypes.cs:64-82` still misplaced |
| ⛔ **The tracker was not touched** | rule 6 gave it to them for the batch; `BP-57`'s row **does not record §1 or §3** |

⚠ **They stopped EARLIER than the sanctioned boundary** (*"stop cleanly before §5"*) and **did not say
where they stopped**, which §8 asked for. ⇒ ⭐ **`BP-57` needs one more batch: §2 + §4 (+ §5/§6).**

### ⭐ But what landed is the load-bearing half, and it landed well

| | |
|---|---|
| ⭐⭐ **§1 obeyed the hard constraint exactly** | `IVariablesSchemaSource` **implemented, not changed** — `UpdateVariableRole`/`Scope` left as the default-bodied no-ops `Q-k` intends. ✅ **AiShared stayed at 1213**, so `U-5`'s `V2` is untouched |
| ⭐ **`CountNodesReferencingVariable` is REAL** | ⛔ not the hardcoded `0` (`BP-230`). ⭐ **And they reasoned past the handoff:** counted **by id, never by name** (`FindLocalIndex` has no name fallback) and **across the whole asset, not the owning graph** — because a node in another graph carrying the id is exactly `BP1670`'s dangling case, and *"a delete that could not see it would leave the asset uncompilable while reporting itself clean"* |
| ⭐ **`IsUnused` now follows that count** | it was hardcoded `false`. **Not asked for** |
| ⭐ **The graph is read through a DELEGATE, never captured** | `BP-72`'s lesson applied unprompted, so the projection follows the canvas rather than the graph open at construction |
| ⭐ **A `Macro` graph projects READ-ONLY rather than vanishing** | ⛔ *"a surface that disappears teaches nothing"* — the `Q26-B2` ruling, applied at the source layer without being told |
| ⭐ **Shadowed picker rows are labelled `(local)`** | ⚠ **and membership is decided by IDENTITY, not name** — *"a name test would mislabel exactly the shadowed pair the suffix exists to disambiguate"* |
| ✅ **Picker widened to locals and nothing else** | with a test asserting it — `WorkingState`/`Parameters` stay out (`BP-226`), struct FQNs stay out (`BP-228`) |
| ✅ **Revert-goes-red** | restoring the hardcoded `0` reddens **3**; removing the locals branch + picker reddens **7 of 9** |

📌 **The raw-GUID bug is fixed** — `ResolveVariableName` now searches every graph's `LocalVariables`,
and **the deliberate as-is fallback is preserved** so a dangling reference stays visible.

---

## 7m · Batch 40 — ✅ VERIFIED AND MERGED at `58123d2e` — ⭐⭐ **the plan review killed one gate and added two tasks**

**Docs only** (`REVIEW_Unification_Plan.md` + the tracker) ⇒ gates cannot have moved. Confirmed: build
**0 errors** · Blueprints **3259** / 0 / 10 · counts clean on arrival (**ninth** batch running).
**Verdict: run it, with five named changes. No re-cut — the task boundaries are right.**

### 🔴 V1 — my `U-10` gate was UNWRITABLE, and I verified it myself

*"v1 → v2 → v1 is the identity, byte-for-byte"* ⛔ **cannot pass.** ✅ **Coordinator-confirmed
independently: 41 of 42 shipped assets are hand-authored 2-space-indented, and
`BlueprintJsonServices` sets `WriteIndented = false`.** ⇒ **the gate would fail on indentation before
the migration logic ran once.**

⭐ **Their fix is better than a weaker gate:** a **canonicalisation pre-step** (new **`U-15`**) that
re-serializes the corpus once as a semantic no-op **the golden harness proves** — after which
byte-identity is meaningful *and* is the strongest possible gate. 📌 **It also settles `BP-227`
deliberately** rather than as a migration side effect.

### 🟠 V3 — a task I had missed entirely

⛔ **Nothing retired the standalone `BlueprintVariablesWindow`.** After `U-6` puts the same table in
Details, **both live** ⇒ my *"stop after 45 and everything is coherent"* claim was **false**: it would
ship **two ways to edit a variable — the exact sprawl the programme exists to remove.** New **`U-16`**.

### ⭐ What the review cleared, and it cleared the two things I feared most

| | |
|---|---|
| ⭐ **The harness is cheap** | **634 ms for all 42**, ~5 ms warm, against a Blueprints gate already ~95 s ⇒ **a gate, not a nightly** |
| ⭐ **Stage `C`'s seam exists** | shipped tests already drive Stage 3→7 directly, bypassing Stage 2. **No `InternalsVisibleTo` change** |
| ⭐⭐ **The entrenchment worry is DEAD** | I feared the golden set would entrench `BP-226`'s bug. ⛔ **It cannot: `BP1024`/`BP1031` mean no shipped asset has both lists populated, so the wrong resolution never fires inside the corpus.** `U-3` declares **no** golden change |
| ✅ **`U-11` is one batch, two sub-steps** | the buckets separate because the views survive to `U-12` |

### ⚠ Two corrections to my numbers, both mine to own

| | |
|---|---|
| **"42 shipped assets"** | ✅ **right number, wrong definition.** The corpus is **the generator's `AdditionalFiles`** — `Assets/Blueprints` only. ⛔ **Globbing `Recipes/` too THROWS**: assets exist in both roots sharing an `AssetId` |
| **`BP-227`: "four" numeric-`Dispatch`** | **7 files** — 4 golden + 3 recipes |

### 🔴 V2 — and this one changes a batch's shape

`UpdateVariableRole`/`UpdateVariableScope` are **default-bodied members of the SHARED interface** — the
silent no-op is the *interface's* contract, not the blueprint override's. ⇒ ⭐ **`U-5`'s capability flag
is an `Hrot.Editor.AiShared` addition: the AiShared gate moves and BTree/HSM implementers are touched.**
My *"one file, one lane"* description of that batch was wrong.

### ⏭ Batch 41 is dispatched — and it is NOT a `U-` task

📄 **[HANDOFF_Batch41_Local_Variables_Authoring.md](HANDOFF_Batch41_Local_Variables_Authoring.md)** —
⭐⭐ **its §1 is the load-bearing instruction: build the locals model as an `IVariablesSchemaSource`
so the unification ABSORBS it instead of undoing it**, while ⛔ **NOT adding a member to that
interface** (that is `U-5`'s `V2`, and it would move the AiShared gate).
⚠ **§5 (the node badge) moves the two NodeEdit gates** and is the clean stop point if it runs long.

### ⭐ The plan is updated

⛔ **`BP-57`'s authoring half is still unbuilt** (Batch 39 stopped after §0b) and was **not** in the
plan. ⇒ **it takes batch 41**, before the `U-` sequence, because it is `BP-57`'s last mile **and it sits
on the very surfaces `U-4`…`U-6` then change.** Everything else shifts by one: **42–46** independent,
**47–50** the model half.

📌 **Still not established, third batch running:** whether anything **outside this repository** reads
`.bp.json`. ⛔ **And the visual check has now not run for SIX batches** — `U-6`/`U-13`/`U-16` are
exactly what it would catch, and it needs the user.

---

## 7l · Batch 39 — ✅ VERIFIED AND MERGED at `8a687c0e` — ⭐ **the compiler half is COMPLETE.** §3 is NOT built

**All eight gates, coordinator-run on the merged tree:** build **0 errors** · Blueprints **3269 total /
3259 passed / 0 failed / 10 skipped** (**+16**) · AiShared **1213** · BTree **612** · Breakpoints
**130** · Generators **193** · NodeEdit Core **208** · UI **131** — **zero failures** · counts clean on
arrival (**eighth** batch running).

### ⛔ What was NOT delivered — and it matters for sequencing

⚠ **Only §0b.** The recovered work is merged, re-gated on the merged tree and finally documented.
⛔ **§3 — the authoring half — was not built**, and their own `BP-57` row says so: *"STILL NOT done —
a local is declarable in JSON only."* ⇒ ⭐ **`BP-57` stays open and the authoring UI needs a batch.**
📌 The handoff sanctioned stopping there (*"if the batch must stop early, stop after §0b"*), so this is
a clean boundary rather than a miss — **but it is not the whole batch, and the plan must make room.**

### ⭐ What they did beyond the merge

| | |
|---|---|
| ⭐⭐ **`StructureHash` done right** | the handoff warned a blackboard-resident local must enter the hash. They put the slots in **their own `IrAsset.GraphLocalSlots` list**, appended **after** the existing three ⇒ ⭐ **assets without locals hash identically**, and `FindVariableIndex`/`VarFieldName`'s positional space is untouched. **Exactly the "separate storage" lean, correctly executed** |
| ⭐ **Revert-goes-red, attributable** | disabling the promotion reddens **three** suspension tests; unregistering `BP1670` and restoring the `__var_-1` fall-through reddens **both** dangling tests. **Five red, restored green** |
| ⭐ **`BP-226`'s wording corrected** | as asked — *"an invariant nothing enforces"* was wrong and inherited from my finding |
| **`BP-233` half-closed** | `MacroLatency.IsLatent` fixed **with a test asserting the node-level and op-level predicates agree**; `BP1650` still carries its own copy, left open deliberately because *"fixing it widens a refusal, which wants its own slice"* |

### ⚠⚠ They corrected my corpus numbers — again, and the correction is load-bearing

| | mine (§7j / Batch 39 §2) | theirs |
|---|---|---|
| shipped assets | **42** | ⭐ **58** |
| `Get`/`SetVariable` references | 152 *"`VariableId` refs"* | ⭐ **103** — and **all resolve**, **0 dangling** |
| the *"63 unresolved, mostly `state`"* | I warned a naive rail *"would fail 6+ shipped assets"* | ⭐ **those are `GetShared`/`SetShared` — a DIFFERENT node kind**, name-keyed and runtime-resolved, never passed to `FindVariableIndex` |

⇒ ⭐ **My blast-radius warning was right for the wrong reason**, and they measured the right one before
building: scoping `BP1670` to `Get`/`SetVariableNode` refuses nothing that ships, where the generalised
*"any node with a `VariableId`"* rail I implied **would have rejected six assets.**

### 📌 Two carry-forwards

| | |
|---|---|
| ⚠ **`claude/batch39-locals-preserved` still exists** | the handoff asked for it to be deleted once merged. **Harmless; the content is in the mainline.** Delete when convenient |
| 🔴 **The task plan is now stale in one place** | ⭐ **`IrAsset` has a FOURTH list — `GraphLocalSlots` — and it is in `StructureHash`.** [`PLAN`](PLAN_Variable_Unification_Tasks.md)'s `U-9`/`U-10`/`U-11` are scoped against **three**. ⛔ **Not amended: the plan is the artifact [Batch 40](HANDOFF_Batch40_Unification_Plan_Review.md) reviews, and that handoff is dispatched and seen.** Its §3.6 points straight at it |

---

## 7k · Batch 38 follow-up — ⭐ **the lost work is recovered, and the design docs are updated**

**Merged at `406fbb3e`** (a `--no-ff` merge: their fix and my §7j record landed on the same base, an
ordinary divergence — merged rather than cherry-picked so their commit identity survives the
protocol's *branched-from* ancestry checks).

### ✅ The lost work is on the remote

| | |
|---|---|
| **What they did** | checked the two commits in as `git format-patch` output, because ⛔ **pushing a preservation TAG was refused with HTTP 403** — that session's credentials push branches, not tags |
| ⭐ **What the coordinator did** | `git am`'d them onto `de742e6e`, verified they apply **clean**, and pushed them as ⭐ **`claude/batch39-locals-preserved`** (`7e0b11b0`). ✅ **Confirmed on the remote via `ls-remote`** |
| **Then** | removed `docs/blueprints/patches/` — their README's own condition (*"delete it once the work is on a branch"*) is now met |

**What was recovered** — 21 files, **+1182 / −46**: `LocalStorage.cs` (159), `V_VariableReferenceRules.cs`
(110), `FieldLayout`/`StructureHash`/`MacroLatency` changes, and **715 lines of tests** across three
files. **`BP1670`** allocated; **no tracker rows**, so nothing collides on re-application.

⭐ **Their judgement call — checking in patches without asking — was right.** The container being
reclaimed would have destroyed it irreversibly; a checked-in patch is trivially reversible. **That is
the correct trade, and the escalation was the right shape: act, then flag it.**

### ⭐ The design documents are updated — what changed

| | |
|---|---|
| ⭐⭐ **Staging re-ordered** | **`C` first**, a new **stage 0** (`BP-229`), **`D` split into D1–D4**. The old A→B→C→D plan is kept in a collapsed `<details>` block **with the reason it was wrong**, not deleted |
| 🔴 **`CountNodesReferencingVariable`** | struck in **both** documents — it returns a hardcoded `0` (`BP-230`), and both had told Batch 39 to *reuse* it for delete-while-referenced |
| 🔴 **`Q-h` OVERTURNED** | my *"struct variables already work"* ruling. They compile — **so does `a.b`.** `B′` is blocked until a compiler-side rail exists |
| ⭐ **Shared state recorded** | `GetShared`/`SetShared` fits no cell — **61 references, 8 shipped assets** — now an **explicit exclusion to be decided**, not an omission |
| ⭐ **The bijection caveat** | `Variables`↔`WorkingState` is a function of `Dispatch`, so the down-migration is writable — ⚠ **but the tag carries no information `Dispatch` did not.** D's benefit is *one list*, not *the tag tells you the storage*; the document had implied the second |
| ✅ **Cleared** | round-trip is idempotence-only, the inspector is insulated, comparison fixtures are generic, and **nothing outside `Hrot.Blueprints.*` reads these lists** |
| 🔴 **Newly flagged** | order → `FieldLayout` → **`StructureHash`** → the tick **wipes the blackboard on mismatch** ⇒ **a migration that reorders fields resets every deployed entity's state** |
| ⚠ **`R3` recorded** | `UpdateVariableScope(string, WorkingStateScope)` **cannot carry a blueprint two-valued scope** — the two documents assumed both sides of that |

📌 The mapping SVG's footer now says the staging it shows was superseded, rather than contradicting §4.

---

## 7j · Batch 38 — ✅ VERIFIED AND MERGED at `27ebe8dc` — ⭐⭐ **a design review that changed the design**

**Docs only** — `git diff --name-only` returns the review, its diagram and the tracker. ⇒ the eight
gates **cannot** have moved. Confirmed: build **0 errors** · Blueprints **3243** / 0 / 10 skipped ·
counts clean on arrival (**seventh** batch running) · **6 rows filed** (`BP-228`…`BP-233`), 57 → 63 open.

📄 **[REVIEW_Unified_Variable_Design.md](REVIEW_Unified_Variable_Design.md)** · verdict:
⭐ **build it — with four named changes and a re-ordered plan.**

### ⭐⭐ The two findings that change the plan

| | |
|---|---|
| 🔴🔴 **`BP-228` — my `C1` was a half-truth, and the false half is a blocker** | I probed a struct FQN, saw it compile, and concluded *"resolved by fully-qualified name."* ⛔ **The real rule is purely syntactic: contains a dot ⇒ trusted verbatim.** ✅ **Coordinator re-verified, and worse than their example:** `Totally.Made.Up.Type` **and `a.b`** both compile with **zero diagnostics**, emitting `public global::a.b Threat;`. ⇒ **`BP-87`'s *"every offered type is guaranteed resolvable"* has nothing to check against**, and the end-to-end-compilation lock I specified **would pass on a fabricated type**. Only Roslyn catches it, as `CS0246` naming no variable — ⭐ **the `__var_-1` shape again**. **Stage B′ is blocked until something validates a type id** |
| 🔴 **`BP-230` — stage B would ship an inert editor** | `BlueprintVariableSchemaSource` implements `UpdateVariableRole`/`UpdateVariableScope` as **empty bodies** and `CountNodesReferencingVariable(name) => 0`, commented *"Blueprint variables do not use role/scope; no-op implementations."* ⭐ **Trap #5 in the surface both design docs told Batch 39 to reuse for delete-while-referenced** — it would report *"0 references"* for every variable and delete anyway. ⚠ **My `C5` said the columns render; I never checked whether anything was behind them** |

### ⭐ Where they beat the handoff

| | |
|---|---|
| ⭐⭐ **`C` moves to FIRST** | I put the compiler fix third. They measured its blast radius — **`FindVariableIndex` 2 real callers, `VarFieldName` 2** — and observed the kind needs no tag because **the search already knows which list matched**. ⇒ C is the *smallest* stage, needs nothing from D, and closes `BP-226` — the live ambiguity that makes D dangerous. **Leaving it last carries that ambiguity under every other stage** |
| ⭐ **`BP-229` — my `C7` was understated** | I said the compiler *aliases* the caller's lists. They proved it **writes through**: after `Compile`, the caller's own `Graph` no longer contains its `MacroCallNode`. ⭐ **And they closed the gap I could not** — no production path hands `Compile` a live document, because `QuickReloadService.TriggerAsync` **has no production caller at all**. A loaded gun, not a live defect |
| ⭐ **`BP-233` — a FOURTH latency predicate** | `BP1650` omits `ChannelCommandNode`-with-`ActionFqn` too ⇒ a called Function graph with an inline action reaches Emit with an unlowered `IrTerm_Suspend` and **`TerminatorEmitter` throws** — a compiler crash where a diagnostic was intended |
| ⭐ **`R5` — shared state fits no cell** | `GetShared`/`SetShared` is entity-scoped, **name-keyed, resolved at runtime, declared nowhere** — **61 references across 8 shipped assets.** ⛔ **Neither design document mentions it once.** To a designer it is a variable |
| ⭐ **Round-trip is NOT a barrier** | all seven tests assert `Serialize(Deserialize(j1)) == j1` — **serializer idempotence, not identity with any file on disk.** ⇒ **D2 can be scheduled on its merits rather than as a test-fixing exercise** |
| ⭐ **`D` is not a stage** | it is D1–D4, and **only D1 reverts cheaply** — once D2 has written v2 files, *the down-migrator IS the revert* |

### ⚠ What they could not establish, and said so

Whether the shared table's `Role`/`Scope` columns are **drawn-but-dead or hidden** for a blueprint
asset — *"it needs the UI on screen, and there is no ImGui in this container."*
⛔ **The visual check has now not been done for FIVE batches.** 📌 That is a standing gap, not theirs.

### 🔴🔴 The one thing that went wrong — **work was lost, and the recipe to recover it does not work**

Batch 39's §1/§2 (suspension storage + the dangling rail) were **built and gate-green**, then reset off
the branch by the force-push. §11 says *"recoverable — `git cherry-pick 2c1638b bec149d`."*
⛔ **Coordinator-verified: they are not.** `cat-file` ✗ · `rev-parse` ✗ · `fetch origin <sha>` ✗ ·
`ls-remote` shows **one** implementation ref and **zero tags** · `fsck`'s only dangling commit is
`02fb66db`, neither of them.

⇒ ⭐ **The only surviving copy is in the implementation session's own local clone.** It must push them
to a ref **before that container is reclaimed**. ⚠ **Until then, Batch 39 §1/§2 are UNBUILT.**
📌 `BP-233` came out of that work and is recorded, so the finding survived even though the code did not.

---

## 7i · Batch 37 — ✅ VERIFIED AND MERGED at `68cff233` — ⭐ **`BP-57`'s compiler half**

**Gates, coordinator-run on the merged tree — all eight, all at baseline:** build **0 errors / 69
warnings** · BP diagnostics **10 distinct**, all `BP3010`, all authored · Blueprints **3243** / 0 / 10
skipped *(3234 → 3243, **+9**: 8 `LocalVariableTests` + 1 execution test — every new test accounted
for)* · AiShared 1213 · BTree 612 · Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131.
**Zero failures.** `tracker-counts.py --check` **clean on arrival — sixth batch running.**

✅ **Rule 7 followed:** they branched from `cf26c24d` ⇒ ⭐ **the §6 amendment WAS in view**, and their
row acts on it rather than on the superseded text. The amend-at-no-risk call was correct.

### ⭐⭐ They raised the severity of my own finding — by measuring what I asserted

| | |
|---|---|
| **My finding claimed** | the picker offers only `Variables` ⇒ *"a designer cannot produce a `GetVariable` aimed at a `WorkingState` field"* — one of the **two legs** holding the invariant up |
| ⚠ **They measured** | **57** such references exist in the shipped corpus |
| ✅ **Coordinator re-measured independently** | 42 assets · **152** `VariableId` refs · **0 → Parameters** *(my one empirical claim, confirmed)* · **57 → WorkingState** · 32 → `Variables` · 63 → none *(their split was 34/61 — a different extraction, immaterial)* |

⇒ ⭐ **The leg I leaned on was the weaker of the two.** The invariant is not guarding a theoretical
case: **57 live references resolve correctly only because those AiPrimitive assets happen to have
`Variables.Count == 0`.** Filed as **`BP-226`** at raised severity, correctly.

📌 **Two workarounds exist, not one.** Their row cites `AiPrimitiveLowering:42-66` (append `__phase`,
never prepend). ⭐ **Coordinator found a second while verifying:** `Stage5.FindParameterIndex`'s doc
says using the combined index *"would silently emit the wrong field … whenever Variables/WorkingState
are non-empty"* — a **params-only** lookup written to dodge the same space. **Two independent authors
routed around this. Feed both to Batch 38.**

### ⭐ Where they diverged from the handoff, and were right

| | |
|---|---|
| **§5 rail placement** | My lean: fire at the **call site**, like `BP1661`. ⭐ **They fire on the macro's own node** — and the reason is better than mine: `BP1661`'s body is *fine* and only the call is wrong, so the call is the only actionable thing; here the body is wrong **in every host**, so reporting once at the macro beats once per call site for a defect that is not the call's fault |
| **§5 refuse-vs-resolve** | ⭐ Refused, as the handoff hoped but did not require. The argument is the one that matters: cross-host reference can only work by **name**, which is exactly the fallback the resolution design exists to refuse — *"building a cross-host name resolver would re-open, as a feature, the hazard the design closes"* |
| **Emit site** | ⭐ **`LibraryEmitter.EmitGraphBody`, not the four per-emitter methods** the handoff pointed at. Every graph passes through it; four copies is how `BP-221`'s helper loop came to be missed. **Better than what I asked for** |

### ⭐⭐ The revert-goes-red that did NOT go red — and what it exposed

They report the name-fallback revert **passed** on the first attempt. ⚠ **The shadowing test targets the
local by id, so it is green whether or not local lookup also matches on name.** The hazard runs the
other way: a node carrying a **NAME** must not be captured by a same-named local. That test
(`ANodeNamingAnAssetVariable_IsNotCapturedByASameNamedLocal`) was **missing**, and only a failed
revert found it. ⭐ **This is the discipline working as designed** — the gap was in the test, and the
revert is what named it.

### ✅ The two things they did not report, verified by the coordinator

| | |
|---|---|
| ⭐ **The `Graph` reflection guard** | §8 asked whether it went red; the report is silent. **Probed it directly** — removed `LocalVariables = LocalVariables` from `WithNodesAndLinks`, ran the single test: `Graph.WithNodesAndLinks dropped these members` ⇒ **red**, restored. ⭐ **The guard bites, and it bites on an UNPOPULATED member** (the fixture never sets `LocalVariables`) — so it will catch the next member too |
| **Round-trip** | No serializer change, and `BlueprintJsonServices` sets no `DefaultIgnoreCondition` ⇒ `LocalVariables:[]` is written for every graph. ⚠ **Not a regression:** shipped assets carry no `ExecInputs`/`ExecOutputs`/`Comments` either — **the exact `ExecInputs` precedent from Batch 32.** "Byte-identical" here means load→save stability, not file-on-disk equality |

### 📌 One real nit for Batch 38 — a misplaced doc comment

`GraphTypes.cs:64-82` — the **`BP-220` doc block explaining `WithNodesAndLinks` and the reflection
guard** is now attached to **`LocalVariables`**, because the new field was inserted between the comment
and the method it documents. ⇒ `LocalVariables` carries **two consecutive `<summary>` blocks**, and
`WithNodesAndLinks` — the method whose contract is load-bearing — is **undocumented**. ⚠ Silent (doc
generation is off), cosmetic, one-line fix. **Not worth a row; put it in the next handoff.**

---

## 7h · Batch 36 — ✅ VERIFIED AND MERGED at `4242f304` — ⭐⭐ **the macro programme is COMPLETE**

**Gates:** build **0 / 69** · BP diagnostics **10** · Blueprints **3234** (+17) · rest unchanged ·
**zero failures** · counts clean on arrival (fifth batch running).

### ✅ `BP-76` was what the handoff said, and they said so in the row

The greyed gate was the only thing keeping a corrupting path unreachable. `MacroExpander` is now public
in `Compiler/Transform/` beside `CollapseEmitter`, its exact inverse; **`Stage2_5_ExpandMacros` keeps
the pass and calls the shared splice** — one algorithm, two callers, which is the reuse the handoff
asked for. ⭐ **They checked the premise rather than assuming it**: the rules turned out identical
because every call addresses pins through `MacroCallPinView` and the boundary clones. The one real
difference is *what may be missing* — the editor can be handed an unresolvable target where `BP1660`
guarantees the pass one, so the entry point **returns a refusal rather than assuming**.

### ⭐ The bug they found doing it — worth remembering beyond macros

**`Link` is a mutable class and the splice rewrites endpoints in place**, so the "before" snapshot was
made of *the very objects the splice then rewrote* — quietly corrupting the state undo restores.
⇒ **Links are copied into the probe; nodes deliberately are not**, since sharing them is exactly what
preserves node and pin identity across an expand/undo cycle. ⭐ **A snapshot of mutable objects is not
a snapshot.**

### ⚠ `BP-82` — only ONE of the two rails was real, and my handoff implied both

I wrote *"`BP-82`'s last two library rails."* ⭐ **Same fact killed one and fixed the other: macro
graphs never reach the IR** (Stage 5 skips them; `IrGraphKind` has no `Macro`).

| | |
|---|---|
| **`BP5001`** ✅ real | it was rejecting **exactly the Q25-C2 shape** — a macro library declares macros and no functions, which by lowering time is indistinguishable from an *empty* library. `IrAsset` now carries the declared macro count, the smallest thing that tells them apart |
| **`BP9001`** ❌ needed no narrowing | the library-latency loop **cannot see** a latent node inside a macro declaration, because that declaration reaches no IR graph. The latent node is flagged where it actually lands — spliced into the calling function graph. ⭐ **They wrote a test saying why, so nobody later adds a filter that silences a real error** |

📌 **`BP1664` stays reserved** — `Graph` has no `LocalVariables` (**`BP-57`**), so a macro cannot declare
a local and the rail has nothing to check.

---

## 7g · Batch 35 — ✅ VERIFIED AND MERGED at `8b56367b` — ⭐ **a macro can be authored by hand**

`BP-75`, `BP-77` and `BP-80` all closed. Created from *"Macros +"*, exec entries and exits declared in
the signature window, dragged from the palette. **Gates:** build **0 / 69** · BP diagnostics **10** ·
Blueprints **3217** (+25) · rest unchanged · **zero failures** · counts clean on arrival (fourth batch).

### ⚠⚠ The handoff's central premise was WRONG — and the correction is better than the fear

I wrote that **reordering** an exec declaration silently re-targets wires, called it *"the one way this
feature can corrupt a working graph"*, and said not to ship without a test proving otherwise.

⭐ **They proved the opposite, and I verified it:** `DeterministicIds.PinId(nodeId, name, direction)` has
**no index component** (§5). A wire follows the **name**; both the boundary node's pins and every call
site's pins project from the **same list in the same order**, so index *k* names the same declaration on
both sides and a permutation moves both together. **Reorder is safe.**

⭐⭐ **And the genuinely corrupting edit was one nobody had named — including me:** **two declarations
sharing a name project to the same pin id**, so the second silently collapses onto the first. That falls
straight out of the same formula I have quoted in §5 all along. **Refused now on add and on rename.**

The two edits that *do* destroy are **rename** and **delete** — `BP-202`'s shape one level up. A rename
destroys one pin and creates another, dangling every incident link and breaking the solution build with
`BP1602` **from a graph that looks fine on screen**; rename now repoints the wires, which is possible
here because it hands over the old→new mapping outright where BP-202's Format edit could only prune.

📌 ⭐ **The instruction was still worth giving.** *"Do not ship without a test proving reorder is safe"*
is what produced the investigation that refuted it. **Demanding the proof was right even though my
reason was wrong.**

### What else they found that the handoff missed

| | |
|---|---|
| **The signature window excluded `Macro` from its graph picker entirely** | ⇒ a macro's **data** `Inputs`/`Outputs` were editable nowhere either. I only looked for exec sections |
| ⭐ **A separate rows view, for a better reason than the missing type** | `ParameterRowsView` renames **on every keystroke** — for an exec declaration that is a **pin migration per character**. The new view commits on deactivate |
| **The palette guid form** | entries mint the guid in `"N"` form while every consumer compares `Graph.Id.ToString()` ⇒ a dropped node whose target **resolved nowhere** and reported `BP1660` — right-looking and non-functional |
| **Preview vs bake** | the entry previews the target's pins for palette filtering, but **re-projects every rebuild** rather than baking — staying out of the `CallablePeers`/`ArgTypes` trap (F4) |

### The two rows filed

| | |
|---|---|
| **`BP-224`** | the section-filter boolean (coordinator-found). ⭐ Recorded as a **shape**: *a discriminator that is correct only because one of its cases never occurs* — it had been wrong since it was written and became reachable the moment collapse shipped |
| **`BP-225`** | records the destructive-edit reasoning **so nobody re-derives it wrongly** — including the refutation above |

---

## 7f · Batch 34 — ✅ VERIFIED AND MERGED at `53c407f1` — ⭐ **`BP-74` CLOSED**

**Collapse works end to end.** Reachable from the canvas, **one undo entry that restores identity**,
refuses out loud naming the offending nodes, and the round-trip property still holds.

**Gates:** build **0 errors / 69 warnings** · BP diagnostics **10** · Blueprints **3192** (+14) ·
AiShared 1213 · BTree 612 · Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131. **Zero
failures.** `tracker-counts.py --check` clean on arrival — third batch running. **60 open / 98 done.**

### ⚠⚠ `BP-223` — **the coordinator's handoff was wrong, and this is the correction**

Batch 34's handoff said the refusal surface *"exists … wired at `BlueprintDocumentFactory:346`"* and
asked them to confirm its shape. ⭐ **Confirming it is what found the defect.**

✅ **Coordinator-verified at the dispatch commit `5e347f67`:** the **only** `EditorNotification`
`TryDequeue` in the entire repository was **`NodeEditor.Demo/DemoShell.cs:456`**, against a queue *that
shell builds for itself*. **The Hrot editor's queue had no reader at all.** ⇒ every notification since
bookmarks landed (BP-24) was enqueued and silently discarded, and **a collapse refusal would have gone
the same way** — B2 implemented right up to the point where it says nothing.

⭐ **The coordinator checked that the enqueue half existed and never checked that anything read it.**
That is **trap #5 exactly** — a mechanism that looks present and does nothing — walked into while
writing a handoff that warns about trap #5. ⇒ ⭐ **"Confirm its shape" earned its keep; keep asking for
it.** `NotificationOverlay` supplies the missing consumer, and bookmarks get their notifications back.

### `BP-221` was two holes, not one — the coordinator found one of them

I verified the missing helper loop (`InstanceEmitter:83` has it, `AiPrimitiveEmitter` does not) and
stopped there. ⚠ **Reproduction found a second:** the call site emitted the **Instance-shaped** context
args (`view, ecb, deltaTime, instanceVersion`) into an AiPrimitive `TickCore` whose signature is
`(ref Params, ref WorkingState, self, world, time)` ⇒ **five `CS0103`s, not one.**
⭐ **Reading the emitter showed the missing loop; running it showed the wrong call shape.**

**`BP-222`** reproduced on the **Instance** path from a zero-output function graph ⇒ **never a collapse
artefact**, and the row's description was right. Cause is `BP-221`'s class: two emitters deciding
independently whether a call yields a value, disagreeing **in both directions** (`CS0815` void
assigned; later `CS0127` value returned from void). ⭐ **One shared predicate**
(`LibraryEmitter.HelperReturnType`) now answers it for the declaration and the call site together.

⇒ **This unblocked the proof Batch 33 could not write**: `CollapsingToAFunction_CompilesThroughRoslynAndRuns`.
⚠ A Function has **no inverse** — nothing expands a `FunctionCall` back — so the round-trip property is
unavailable to it and **execution is the only evidence**.

### The tests that lock the rulings

| | |
|---|---|
| `Command_IsEnabled_ForAnIllegalButNonEmptySelection` | ⭐ **locks Q26-B2** against a future "helpful" `isEnabled` reintroducing greying |
| `Command_IllegalSelection_NotifiesNamingTheOffendingNode_AndMutatesNothing` | asserts the **notification**, not merely the absence of change — the test `BP-223` would have failed |
| `Sink_CollapseToMacro_ActuallyMutatesTheGraph` | ⭐ asserts **the graph, not the result flag** — asserting `Success` alone would have been **green on the silent-success defect** |
| `Command_Undo_IsOneEntry_AndRestoresHostAndDropsTheCreatedGraph` | identity, not shape |
| `Instance_CallingItsOwnFunctionGraph_StillCompiles` | the other dispatch, guarded |

📌 **Process note they recorded, worth keeping:** restoring a revert patch with `mv` **back-dates the
file**, MSBuild skips the recompile, and the reverted binary survives into every measurement after it.

---

## 7e · Batch 33 — ✅ VERIFIED AND MERGED at `a8deb89f` — ⚠ **partial by design**

⭐ **Collapse works, and the property the user asked for holds:** `collapse → expand → structurally
equivalent`, across **five shapes** (linear · two entries · two exits · shared input · fan-out output).
⭐ **Q26-F executed**: a latent selection collapsed to a macro, compiled through **real Roslyn**, loaded
and **ticked** — Running/Ammo 0 → Success/Ammo 42. **Unreal refuses that collapse; we do it and run it.**

**Gates:** build **0 errors / 69 warnings** · BP diagnostics **10** · Blueprints **3178** (+17) ·
AiShared 1213 · BTree 612 · Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131. **Zero
failures.** `tracker-counts.py --check` clean on arrival again — **63 open / 94 done**.

### ⛔ What is NOT done — stated by them, up front

**Items 3 and 4: the sink cases, undo, and the context menu.** ⇒ **collapse is not reachable from the
canvas**, and `BlueprintCommandSink`'s `default:` arm still reports success while doing nothing.
⭐ **They said so in the commit subject, the body and the `BP-74` row** rather than letting it be
discovered. That is a clean stop at a boundary, which the handoff permits. **Batch 34 finishes it.**

### ⭐ What they did beyond the handoff

| | |
|---|---|
| **A refusal I did not specify** | a **Function** target now also refuses **≥ 2 exec ENTRIES** — a `FunctionCallNode` has one exec-in, so a Function built from a two-entry selection would silently lose every path but the first. Same class of hole as the exits rule I *did* specify |
| **Latent detection de-duplicated** | moved to a shared `MacroLatency` used by both collapse and `BP1661`, rather than copied — the **BP-69 lesson** applied unprompted |
| ⭐ **The comparator is proven non-vacuous** | `CanonicalGraphShape_DistinguishesADifferentTopology` + `..._IgnoresIdsAndPositions`. **Without those the five round-trip tests could all pass on a comparator that says yes to everything** |
| **Per-set dedup keys** | entries/exits keyed by the **interior** pin, inputs by the **outside producer**, outputs by the **interior producer** — §1.3's (a) and (b) are exactly getting these wrong |
| **The cycle check is structural** | contract the selection to one node, ask if it lies on a cycle. ⭐ The four-set table describes a cyclic boundary *happily* — as one input and one output — which is why it needs its own check |

### 🔴 Two pre-existing defects collapse walked into

| | |
|---|---|
| **`BP-221`** 🔴 | an **AiPrimitive** asset never emits `Func_*` helpers for its non-tick Function graphs, while the **call site is emitted regardless** ⇒ `CS0103` against a method that does not exist. ✅ **Coordinator-verified:** `InstanceEmitter:83` has the loop; `AiPrimitiveEmitter:157` picks a tick graph the same way but has **no equivalent loop**. ⚠ **Pre-existing and reachable by ordinary hand-authoring** — collapse merely walked into it |
| **`BP-222`** | a zero-output Function-graph call assigns a **void** helper (`CS0815`). ⭐ **Deliberately filed UNATTRIBUTED** — it may be the BP-104 family or the call-site emitter, and they said it should be reproduced from a hand-authored graph before anyone fixes the half collapse happened to reach |

⭐ **This is Batch 31's pattern again**: building the *proof* is what found the defects. The Function
path has no compile proof yet **because these two block it**, and that is stated rather than papered over.

---

## 7d · Batch 32 — ✅ VERIFIED AND MERGED at `fbc100cd`

**Q26-A3 landed: macros now take `N` execution inputs.** The prerequisite for collapse; the gesture
itself is Batch 33. Rule 7 followed again (branched from `df2bf437`).

**Gates:** build **0 errors / 69 warnings** · BP diagnostics **10**, unchanged · Blueprints **3161**
(+16) · AiShared 1213 · BTree 612 · Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131.
**Zero failures.** ⭐ The two-entry macro was run explicitly and passes.

⭐⭐ **First batch where the tracker counts were correct on arrival** — `tracker-counts.py --check`
passed with no coordinator fix. That is three consecutive wrong done-columns ended by deriving the
table instead of maintaining it.

| ⭐ Got right | |
|---|---|
| **The purity mirror, which was the whole risk** | walks the **host** graph from the `MacroCallNode`'s data-ins — not a copy of `BP1663`, which walks *inside* the macro. **Per call site**, names the **call node**, and gates on ⭐ **wired** entries, so `TwoDeclaredButOneWiredEntry_WithAnImpureProducer_IsAccepted` passes — a site using one door is provably safe and is not rejected |
| **`BP1667` generalised, not patched** | *"empty body"* now means **no exec-out of the boundary node is wired**, rather than *"entry 0 is unwired"*. An unwired entry is one unused door and is deliberately **not** a warning. The handoff only asked them to check the old test |
| **The mirror rebuilt wholesale** | `RebuildLinkedToIds(host)` once after splice, rather than patched at each rewire — simpler and harder to get wrong than what the design asked for |
| **The regression guard** | `SingleEntryMacro_WithAnImpureProducer_StillCompiles` — today's legal case is not swept up by the new rule |
| **Back-compat proven** | `ExecInputs_IsSemanticallyInert_AndOlderJsonWithoutItStillLoads`; `ExecInputs` carried in `Graph.WithNodesAndLinks:91` |

📌 **Nothing was found wrong in this batch.** First time in the run of batches this session.

---

## 7c · Batch 31 — ✅ VERIFIED AND MERGED at `119305e7`

⭐⭐ **The headline is not what was built — it is what building it found.**

### 🔴 `BP1661` was gated on the wrong thing, and it forbade the macro payoff

Batch 30 shipped `BP1661` as *"caller `Kind != GraphKind.Function` ⇒ skip"*. ⚠ **A tick graph is also
`GraphKind.Function`** — `InstanceEmitter:81-82` picks the tick graph from among the Function graphs
(coordinator-verified). ⇒ **the rail rejected a latent macro called from a tick graph: exactly where
latent macros are legal, and per BP-78 the single capability macros exist to provide.**

⭐ **It passed Batch 30's entire suite**, because every negative fixture built a Function caller that was
never a call target — so *"Function graph"* and *"synchronous method"* were indistinguishable in the
tests. **Only executing the payoff separated them.** The gate now mirrors how `BP1650` words its own
rule: membership in the set of `FunctionCall` targets, not graph kind.

⚠ **And the coordinator reviewed that rail in Batch 30 and called it good.** What was checked was that
the diagnostic *blamed the right node*; what was not checked was whether it *fired on the right
condition*. ⇒ **Diff review cannot see this class of defect. Running the feature can.** That is the
argument for item 1.2 in general, not just here.

### The payoff, executed

Coordinator-run explicitly — all five pass:

| Test | Proves |
|---|---|
| `LatentMacro_SuspendsAndResumesAcrossFrames_AndFiresOnlyAfterTheDelay` | aim → `Delay(0.4)` → fire, spliced into a tick graph, through **real Roslyn**, loaded and **ticked**: Running/0 → Running/0 → Success/**42**. ⭐ the *value* is the point — a splice that dropped the post-delay half would still read Running→Success |
| `TwoLatentCallSites_EachSuspendIndependently_AndBothBodiesRun` | 0 → 7 → 99. Batch 30 proved two *clones* exist; this proves two *suspensions* coexist |
| `..._CompilesThroughTheRealGenerator` | ⭐ **body fixed, name kept** — now actually invokes the generator and Roslyn |
| `OneAuthoredMacroNode_MapsToAnEntryPerExpansionSite` · `DebugMap_RoundTripsProvenance_AndStillReadsA10Map` | BP-83 |

### Gates

Build **0 errors / 69 warnings** · BP diagnostics **10**, unchanged · Blueprints **3145** · AiShared 1213 ·
BTree 612 · Breakpoints 130 · Generators 193 · NodeEdit Core 208 · UI 131. **Zero failures.**

⚠ **The suite total is unchanged at 3155 and that is correct, not suspicious**: BP-111 filtered **7**
host-timing tests out of the default run and the batch added **7**. They documented the drop.
📌 **`Category=HostTimingSensitive`** runs the perf family explicitly; the assertions were filtered, not
deleted.

⚠ **The ungated consumer** (`Hrot.ClusterRunner.Integration.Tests`) — they baselined it **before**
changing anything at 4 failed / 1 passed for `BlueprintObserveTests`, and identical after. The suite as
a whole is heavily red in this container with *"Failed to create RW mapping for RX memory"* (a JIT/W^X
sandbox limit). ⚠ Coordinator measured **45 failed / 135 total** against their **46 / 150** — run-to-run
variance from a fatal error killing test hosts non-deterministically. **Environmental, not the change.**

### `BP-220` — fixed as a shape, not a field

`Graph.WithNodesAndLinks` replaces both hand-rolled copies, **and a reflection test enumerates `Graph`'s
properties and fails when any member is not carried across.** ⇒ the next `Graph` member cannot be
silently dropped. That is the right reading of *"fix the shape, not the field."*

---

## 7b · Batch 30 — ✅ VERIFIED AND MERGED at `4fe3538a`

⭐ **Macros work end to end.** `Stage2_5_ExpandMacros` lands with **all four** Stage 2 rails, so the
item-3 gate was met by the real `BP1663` purity check rather than the fallback refusal.

Same branch as Batch 29 (`claude/hrot-implementation-j1jvin`); ✅ **rule 7 followed again** — branched
from `199d1298`, this branch's head, so the handoff and its stamp were both in view.

**Gates, coordinator-run:** build **0 errors / 69 warnings** · BP diagnostics **10**, unchanged and all
authored · Blueprints **3145** (+17) · AiShared 1213 · BTree 612 · Breakpoints 130 · Generators 193 ·
NodeEdit Core 208 · UI 131. **Zero failures**; the BP-111 perf flake did not fire this run.

| ⭐ Got right | |
|---|---|
| **Both design defects handled as flagged** | `BP1660`/`BP1662` pulled forward, and the pipeline comment states *why* the pass sits after Stage 2's error gate — so the splice may assume a resolvable, acyclic target instead of null-checking every rule |
| **Provenance ruled as recommended** | `[JsonIgnore] Guid? OriginNodeId` on `Node`, with the on-disk-invariance argument and the `NodeId ?? OriginNodeId` precedence both written down, plus a test locking that it never serialises |
| **The cloner was MOVED down, not copied** | `GraphFragmentCloner` in `.Compiler`; `BlueprintClipboard` delegates and keeps only its paste offset. ⭐ **`ClonedFragment` exposes `NodeMap`/`PinMap`** — the coordinator's finding that `Rehydrate` built the maps and threw them away |
| **`BP1665` names `BP1662`** | so a depth-cap error points at the cycle rail rather than leaving the designer chasing depth |
| **`BP1661` names the call site** | test asserts `bp1661.NodeId == call.Id` — the designer's node, not a latent node inside somebody else's macro |

### ⚠ Two carry-forwards for Batch 31 — **neither is a defect in the code**

| | |
|---|---|
| 🔴 **A test name overclaims** | `MacroExpansionTests.LatentMacro_SplicedIntoATickGraph_`**`CompilesThroughTheRealGenerator`** does **not** compile through the real generator. Its body calls `Expand(...)` and asserts splice shape only; the file contains **no `CSharpCompilation`** (real Roslyn = `CSharpCompilation.Create`, per `Stage8Tests:168` / `AuthoringPath:316,340`). ⭐ Its own doc-comment cites the *"`.Succeeded` never invokes Roslyn"* rule — **the intent was right, the body does not do it.** A green test whose name claims more than it checks is worse than no test |
| ⚠ **The payoff case is still unproven** | the handoff asked the latent macro be **ticked to completion across frames**. Splice shape is proven; *running* it is not. **BP-78's whole justification for macros is factoring out a reusable latent sequence** — that scenario should execute and assert, not just expand |

### ⚠ Bookkeeping — a missed tick, and this one WAS introduced here

`BP-219` was ticked done but **not added to the `RW-L` done column**: `RW-L` read 44 (should be 45) and
the Total 90 (should be 91). `BP-81`'s `RW-H` move was counted correctly; only the second item was
missed. Corrected by the coordinator after merge. ⭐ **This is exactly the case §4 warns the count check
cannot catch by arithmetic alone** — it only surfaces when you reconcile **three ways**, which is why
that step is not optional.

---

## 7a · Batch 29 as dispatched — the handoff

📄 **[HANDOFF_Batch29_Macro_Surface_Triage_ReturnStatus.md](HANDOFF_Batch29_Macro_Surface_Triage_ReturnStatus.md)**
— ⛔ **frozen** (rule 1). Three headless halves; every coordinate in it verified against this tree.

| | Item | Shape |
|---|---|---|
| **1** | **`BP-80`** macro surface | `ExecOutDecl` + `Graph.ExecOutputs` · `MacroCallNode` (one field) · admit `Macro` to **four** projection halves · `ReturnNode`'s **N exec-in** pins |
| **2** | the **warning triage** | 10 authored orphans · **6 synthesized** (🔴 the real item) · the `BP3011` rung |
| **3** | **`BP-131`** `Return.Success` | H1 (`IrTerm_ReturnStatus` gains a condition) is the work; H2/H3 are the traps |

### What this session added beyond §7's earlier proposal

| ⭐ | |
|---|---|
| **F5 is understated** | `Graph.Outputs.Count` is load-bearing at **20 executable sites across 8 files**, not "four" — four of them in `ReturnNodeDrawer` itself |
| 🔴 **`BP4004` is not a net** | `Stage5:2020-2025` is a **Warning that emits no IR and walks on**. So an unexpanded `MacroCallNode` would *silently vanish from the exec chain* in any consumer without `TreatWarningsAsErrors`. F4 calls it "a second diagnostic"; it is trap #5 again ⇒ the handoff requires an explicit **Error** |
| 📐 **A design-doc tension, flagged not ruled** | `Macro_Implementation_Design §5` says *"BP-80 and BP-81 must not be split"*, but its justification (two assemblies must agree) is entirely **inside** BP-80. Implementation session decides |
| 🔴 **`EnumDemo` is the T32 gate asset** | composed into `T32_ComposedGeneratedBlueprint`, named at `BTreeJsonGeneratorTests:2553`. Its 5 orphans are **not** a free 🟢 edit |
| ✅ **`InlineEd1` is referenced by no test** | genuinely safe — ⚠ but it is a divergent fork of the **test-locked** `EditorTypesDemo.bp.json` and shares node GUIDs. ⛔ do not sync the copies |
| ⚠ **H2 is sharper than written** | `AiPrimitive` is **unconditional** in `wantsStatusReturn`, so the zero-output-Library branch is only at risk if the pin is projected beyond AiPrimitive ⇒ the projection gate is the primary containment |
| ⚠ **The gate script itself had trap #5** | the two NodeEdit gates silently did not run under `--no-build` — corrected in §3 |

⭐ **Add a fifth question to the data-out audit check** (BP-213/214's lesson): *projects a data-out? ·
resolver case? · `_statementPinCache`? · only `_pinValueCache`? · ⭐ **inside an inline body?***

⚠ **Stale doc in the other lane:** `RESUME_Impl_Session.md:211` still lists BP-107 as *"architect round
required"* (verified still present). Not yours to edit — **flagged in the Batch 29 handoff §5.**

---

## 8 · User preferences that are binding

| | |
|---|---|
| ⛔ **Never** use the `AskUserQuestion` widget | ask in plain prose |
| **Delegate to Sonnet** | mirror-an-existing-pattern work, mechanical edits, broad searches. Keep novel scheduler/IR/compiler work hands-on |
| **Build general, not minimal** | ship the whole obvious set (every `ComparisonOperator`, not just `==`) |
| **Diagrams** | hand-authored **SVG** for anything non-trivial; Mermaid only for simple flowcharts, with short labels |
| **Prose** | ⭐ **short.** Lead with visuals and terse tables. Long walls go unread |
| **No non-trivial capability without a design** + an architect pass | the "architect" is the user's NotebookLM; you cannot reach it, the user relays. Q23/Q24/Q25 were legitimately self-researched against code — say so when you do that |

---

## 9 · Document map

| Doc | For |
|---|---|
| [Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md) | **the** backlog. Rows now carry the full analysis (the detail doc stopped at BP-122 and its dead links were removed in Batch 28) |
| [DECISIONS_Authoring_UX.md](DECISIONS_Authoring_UX.md) | **D1-D6** — settled authoring-UX rulings. Every architect question is closed |
| [Macro_Implementation_Design.md](Macro_Implementation_Design.md) · [Architect_Question_25_Macros.md](Architect_Question_25_Macros.md) | the macro capability, end to end |
| [FINDING_SetVariable_ValueOut.md](FINDING_SetVariable_ValueOut.md) | the printed-`0` root cause + the data-out audit method |
| [HANDOFF_Batch28_Silent_Defaults.md](HANDOFF_Batch28_Silent_Defaults.md) | the most recent handoff — **copy its shape**, including §0a's standing rules |
| [RESUME_Coordinator.md](RESUME_Coordinator.md) | historical log, Batches 22-28 |
