# ⭐ START HERE — coordinator session, blueprint gaps & QoL programme

> **Point a fresh session at this file. It is self-contained.** Last updated **2026-08-10** at
> coordinator head **`0bef2f2`**; both branches in sync at Batch 28. **Batch 29 is dispatched** (§7).
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
| Implementation session | `claude/blueprint-macro-feature-sdmspn` |

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
git fetch origin claude/blueprint-macro-feature-sdmspn
git log --oneline <last-dispatch-sha>..origin/claude/blueprint-macro-feature-sdmspn
```

| Situation | Do |
|---|---|
| **No batch in flight** (today's state) | pick the next batch — see §7 |
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

**Baseline at `0bef2f2` — ⭐ all eight gates re-RUN 2026-08-10; every figure reproduces Batch 28's:**

| | |
|---|---|
| Solution build | **0 errors**, 77 warnings |
| BP diagnostics | **18 distinct** — 16×`BP3010` + 2×`BP3011` |
| Blueprints | **3101** / 0 failed / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### ⚠⚠ Measuring blueprint warnings — the one that has been wrong all along

```bash
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj -t:Rebuild -v n --nologo \
  | grep -oE "warning BP[0-9]+: [^[]*" | sort -u          # ⭐ sort -u is mandatory
```

**MSBuild prints every warning twice** — once in the build, once in the end-of-build summary block. A
plain `grep -c` doubles it. *Every count in this programme's history (34, then 36) was the doubled
figure.* **The true current figure is 18: 16×`BP3010` + 2×`BP3011`.**

⚠ `.Succeeded` **never invokes Roslyn.** Only the real generator path proves a blueprint compiles.

---

## 4 · Where the programme stands

**Tracker: open 65 · done 85** ([Blueprint_Issues_Tracker.md](Blueprint_Issues_Tracker.md)), reconciling
three ways — checkbox tally, per-complexity columns (⚠ take the **first** tag on a row), and the total
row. ⚠ **The count check verifies arithmetic, not semantics** — it cannot catch a duplicate row or a
missed tick, which is exactly what went wrong in Batch 28 bookkeeping.

| Batch | State |
|---|---|
| **27** | ✅ verified — authoring seams, the three matrix axes, diagnostic identity |
| **28** | ✅ verified — the silent `default:` arm family + `GraphKind.Macro` and both fail-loud nets |
| **29** | 📤 **written and dispatched** — [HANDOFF_Batch29_Macro_Surface_Triage_ReturnStatus.md](HANDOFF_Batch29_Macro_Surface_Triage_ReturnStatus.md). Three headless halves: **BP-80** macro surface · the **warning triage** · **BP-131** `Return.Success`. ⛔ Frozen (rule 1) — new findings go in Batch 30 |

### The macro capability

Design is **closed and complete**; implementation has just started.

| | |
|---|---|
| [Architect_Question_25_Macros.md](Architect_Question_25_Macros.md) | *what* a macro is — **A1**, **B1**, **C1 now**, **D3** (1 exec-in, **N ≥ 0** exec-out), **E** six rails |
| [Macro_Implementation_Design.md](Macro_Implementation_Design.md) | *how each slice is built* — findings **F1-F5**, the splice algorithm, diagnostics, ⭐ **§7: all three restrictions ACCEPTED by the user** |
| ✅ **BP-79 landed** (as BP-216) | `GraphKind.Macro` + the Stage 5 skip + `MapGraphKind` now **throws** |
| ⏭ **BP-80 is next** | `ExecOutDecl`, `Graph.ExecOutputs`, `MacroCallNode`, the boundary projections |
| Then | **BP-81** expansion pass (🔴 Opus, hands-on) · **BP-82** rails · **BP-83** debug provenance |

---

## 5 · Verified facts — do NOT re-derive these

Every line below was checked against code in Batches 27-28. Coordinates are current as of `a0961ae1`.

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

## 7 · Batch 29 — ✅ written and dispatched

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
