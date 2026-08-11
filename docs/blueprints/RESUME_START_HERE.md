# ⭐ START HERE — coordinator session, blueprint gaps & QoL programme

> **Point a fresh session at this file. It is self-contained.** Last updated **2026-08-11**.
> Batches 29-31 are **merged**; **no batch is in flight** — pick the next one (§4).
> ⭐ **Macros are DONE end to end and proven by execution** — authored, called, expanded, compiled
> through real Roslyn, **ticked across frames**, and debuggable.
> ⏭ **Next: [Q26](Architect_Question_26_Collapse_Selection.md) — collapse a selection into a
> Function/Macro (BP-74).** Awaiting the architect; that gesture is how macros actually get made.
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
| **No batch in flight** (**today's state**) | pick the next batch — see §4 |
| **Implementation reported done** | run **all eight gates** (§3), review the diff, reconcile the tracker three ways, **then** merge `--ff-only` and record it |
| A batch **is** in flight | ⛔ **rule 6: the tracker and detail docs are theirs.** Put findings in the *next* handoff, never in a live one |

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

**Baseline at `119305e7` — ⭐ all eight gates coordinator-RUN 2026-08-11, post-Batch-31:**

| | |
|---|---|
| Solution build | **0 errors**, 69 warnings |
| BP diagnostics | **10 distinct** — all `BP3010`, all **authored** orphans in 2 assets |
| Blueprints | **3145** / 0 failed / 10 skipped ⚠ *(total 3155; BP-111 filters 7 host-timing tests out of the default run — `Category=HostTimingSensitive` runs them)* |
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

**Tracker: open 61 · done 94** ([Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md)), reconciling
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
| **32** | 📐 **gated on an architect round** — [Architect_Question_26_Collapse_Selection.md](Architect_Question_26_Collapse_Selection.md) (**BP-74**, *collapse a selection into a Function/Macro*), raised by the user 2026-08-11. ⛔ **Do not build before the answers are recorded** |

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
