# RESUME — Coordinator session (2026-08-08)

> **Read this first on resume. Self-contained.** You are the **coordinator**: you own the tracker,
> write handoffs, review returned diffs, re-run gates, and pick the next batch. You do **not** write
> feature code.

---

## 0. ⚠ FIRST ACTION — an unverified merge is already committed

The Batch-22 merge **was committed** by this resume commit, but **its gates were never run**.
The user interrupted the build. **Run this before anything else:**

```bash
dotnet build IOS-IG-SimHost.sln -v q --nologo
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj -v q --nologo
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/Hrot.Editor.AiShared.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Hrot.BTree.Editor.Tests.csproj -v q --nologo
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/Hrot.Diagnostics.Breakpoints.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo
dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo
dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/Hrot.AiEditor.Generators.Tests.csproj -v q --nologo
```

**Baseline to beat (measured on the merged Batch-21 tree):** build **0 errors** · Blueprints
**2905 / 0 / 10 skipped** · AiShared **1213 / 0** · BTree **612 / 0**.
Known flakes: `PdbEmbeddedSourceTests`, `WhenNodePerfTests.WhenNode_ValueChanged_Under100ns_perTick`.

⚠ **Batch 22 reported two intermittent failures** and registered **BP-111** because `-v q` prints
counts but not the failing test's *name*. **If a suite is red, re-run it without `-v q`.**

⚠ Classify any failure with `git stash` → re-run → `git stash pop`.

---

## 1. Branches

| | |
|---|---|
| **Coordinator** | `claude/blueprint-authoring-status-6sr5ld` — docs/tracker; **you push here** |
| **Implementation** | `claude/blueprint-macro-feature-sdmspn` — merged in as of this commit |
| **Do not** | create a pull request. Not in any batch so far |

**Counts:** **63 open · 53 done**, reconciled three ways (checkbox tally · header total ·
complexity-column sum). Verify with the snippet in §6 after any tracker edit.

---

## 2. ⚠ Process failure this session — a numbering collision. Fix the rule.

**Both sessions independently allocated `BP-110` and `BP-111` for different defects.** Resolved by
renumbering **mine**, because theirs are shipped and their commit messages name them:

| ID | Owner | Subject |
|---|---|---|
| **BP-110** | theirs (`[x]` done) | a `CallPeerBlueprint` has **never compiled** |
| **BP-111** | theirs (open) | wall-clock perf flakes; incomplete flake list |
| **BP-112** | mine (open) | CS9191 `ref`/`in` in the generated Library adapter — *was BP-110* |
| **BP-113** | mine (open) | `CallPeerBlueprint` projects only `Outputs[0]` — *was BP-111* |

⭐ **Root cause: I violated my own shared-file protocol.** That protocol says the tracker and detail
files belong to the **implementation session for the duration of a batch**. I allocated new IDs while
Batch 22 was in flight.

**The rule to enforce from now on:** while a batch is in flight, the coordinator **records new findings
in conversation and in a scratch note — not as tracker rows**, and allocates IDs only after merging.
If a finding must be registered immediately, allocate from a **reserved high block (BP-200+)** and
renumber on merge.

---

## 3. Batch 22 — DELIVERED, NOT YET VERIFIED

Commits `e214c4dc`, `83f7b8a1`, `466ea11a`, `853ace7b`.

| Item | State |
|---|---|
| **BP-109** end-to-end smoke test | `[x]` — 3 recipe assets (`SmokePatrol`, `SmokeGuard`, `SmokeMathLib`) + `BP109_SmokeTestEndToEndTests.cs` |
| **BP-110** peer call never compiled | `[x]` — plus `CallPeerBlueprintRoslynTests.cs` |
| **BP-111** perf flakes / flake list | `[ ]` registered |

⭐ **Their headline finding: a `CallPeerBlueprint` has NEVER compiled.** They reproduced it with caller
and peer in the **same merged compilation**, which **disproves the comment in `NodeCoverageTests`**
claiming production works because siblings compile together. **That is the eleventh wrong audit claim.**

### ⚠ Two corrections they made to my handoff — I was wrong, they were right

1. **Recipes cannot break the solution build.** `Recipes/Blueprints` are `Content`; only
   `Assets/Blueprints` are generator-compiled `AdditionalFiles`. My handoff warned them otherwise.
   ⚠ **My BP-103 analysis is still correct** — the user's `FuncLib1` was in `Assets/`, which *does*
   break the build. Do not "correct" BP-103 on the strength of this.
2. **Two entities in one world needed no code changes** — the risk I flagged did not materialise.

### Still to do on this batch

- [ ] Run the gates (§0).
- [ ] Review the actual diff of `e214c4dc` — it touches `CSharpEmitter`, `EmissionContext`,
      `StatementEmitter`, `Stage7_Emit`, `BlueprintCompiler`. **That is compiler surface, not test
      scaffolding** — review it as such.
- [ ] Confirm **BP-112** (CS9191) is still live, or was incidentally fixed by their emitter changes.
      The user hit it on a library with **no peer call**, so it is probably distinct — but check.
- [ ] Check they reported: gate numbers, revert-goes-red, Sonnet split.

---

## 4. The user's visual check — stopped at section B

Guide: RESUME → *🎯 Batches 20+21 — DO THIS FIRST*. **Sections C–F, including the T-series, were never
reached.** The T-series is unverified for a **sixth** batch.

| Result | |
|---|---|
| ✅ | `Function Library` template exists; created + opened without throwing ⇒ BP-103 fixed, BP-92 re-tickable |
| ✅ | in-memory hot reload OK |
| 🔴 | **CS9191** on full build ⇒ **BP-112** |
| 🔴 | `ushort` → **BP1500** ⇒ **BP-87 confirmed live** |
| 🔴 | `CallPeerBlueprint` shows one output pin ⇒ **BP-113** |

⚠ **Tell the user to delete `FuncLib1.bp.json`** if still present — while it contains `ushort` it
breaks the solution build.

---

## 5. Next batch — my recommendation

**BP-112 + BP-113**, and they converge:

- **BP-112** (CS9191) needs a **`Dispatch: Library` fixture in `Hrot.AiEditor.Generators.Tests`** —
  every fixture there is an `AiPrimitive` HillAssault2 asset, so **no Library asset has ever gone
  through the generator**. That blind spot is why the user found it and the suite did not.
- **BP-113** (peer call, one output pin) is now **downstream of their BP-110 fix** — re-check whether
  multi-output across assets works before scoping it.

Also open and worth sequencing: **BP-108** 📐 (Print/Log node — design-note-first; no `ToString`/
`Format`/`Concat` node and no string coercion exists, so it needs a format-literal + typed-args pin
shape), **BP-85 + BP-100** (breadcrumb + kind icons, step 3 of the UX plan), **BP-101** (F2 rename).

---

## 6. Standing rules — do not re-derive

- **Model delegation:** implementation sessions are Opus and **must** delegate to Sonnet what does not
  need Opus. State the split **per item** in every handoff. ⚠ **Subagents share one working tree** —
  run them sequentially, wait for `ps aux | grep -c "[d]otnet build\|[d]otnet test"` to hit 0, and gate
  every commit on the fix being in the tree, not on an agent reporting success.
- **Never delegate verification.** Diff review, gates and revert-goes-red stay on Opus.
- **Ask in plain prose**, never the multiple-choice widget.
- **Verify claims against code** — the audit register has now been wrong **eleven** times.
- **Anything left behind gets a tracker row**, not a note inside a `DONE` block (the BP-102 lesson).

**Count check after any tracker edit:**

```bash
cd docs/blueprints && python3 -c "
import re; from collections import Counter
t=open('Blueprint_Issues_Tracker.md').read()
op=len(re.findall(r'^- \[ \] \*\*\[BP-',t,re.M)); dn=len(re.findall(r'^- \[x\] \*\*\[BP-',t,re.M))
h=re.search(r'\| \*\*Total\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|',t)
c=Counter()
for l in t.split('\n'):
    m=re.match(r'^- \[([ x])\] \*\*\[BP-',l); k=re.search(r'\`(WIRING|RW-L|RW-M|RW-H)\`',l)
    if m and k: c[(k.group(1),m.group(1))]+=1
print('tally',op,dn,'header',h.group(1),h.group(2))
print({k:c[k] for k in sorted(c)})"
```
