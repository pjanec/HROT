# ⭐ START HERE — coordinator session, blueprint gaps & QoL programme

> **Point a fresh session at this file. It is self-contained.** Last updated **2026-08-11**.
> Batches 29-31 are **merged**; **no batch is in flight** — pick the next one (§4).
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
> `BP1664` finally built and **`BP1669`** allocated. ⛔ **`BP-57` stays OPEN** — a local is declarable
> **in JSON only**; the authoring UI is **[Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md)**, postponed.
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
> ⛔ **`BP-57`'s remaining work was Batch 38 and is now
> [Batch 39](HANDOFF_Batch39_Finish_Local_Variables.md) — POSTPONED**, because its authoring half sits
> on the surfaces the unification changes. ⭐ **Its §1/§2 (suspension storage, dangling rail) are
> independent and may be pulled forward if the review says so.**
> 🔴🔴 Still true and unfixed: **a local silently reverts to its default across a suspension**
> (coordinator-probed, zero diagnostics) · 🔴 a dangling reference emits `s.__var_-1`.
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
| A batch **is** in flight (**today's state — Batch 38, the design review**) | ⛔ **rule 6: the tracker and detail docs are theirs.** Put findings in the *next* handoff, never in a live one |

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

**Baseline at `4242f304` — ⭐ all eight gates coordinator-RUN 2026-08-11, post-Batch-36:**

| | |
|---|---|
| Solution build | **0 errors**, 69 warnings |
| BP diagnostics | **10 distinct** — all `BP3010`, all **authored** orphans in 2 assets |
| Blueprints | **3234** / 0 failed / 10 skipped ⚠ *(total 3244; BP-111 filters 7 host-timing tests out of the default run — `Category=HostTimingSensitive` runs them)* |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
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

**Tracker: open 55 · done 105** ([Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md)), reconciling
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

### ⭐ The plan is updated; and Batch 41 is NOT a `U-` task

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
