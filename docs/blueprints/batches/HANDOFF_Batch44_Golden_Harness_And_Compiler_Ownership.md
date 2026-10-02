# HANDOFF — Batch 44: ⭐⭐ **`U-1` the golden harness, then `U-2` the first thing it protects**

> 📌 **Dispatched at `c9866b37`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1671+` is the next free diagnostic *(coordinator-verified: `BP1670` is the highest in the tree)*.
>
> ⭐⭐ **`BP-57` is CLOSED — the locals programme is over.** This batch opens the **`U-` sequence**:
> 📄 **[PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md)** §2 holds both tasks' gates in full.
> ⚠ **The plan's batch table was RENUMBERED (+2) `2026-08-13`** — `U-1`/`U-2` were "Batch 42" when it
> was written. **Groupings unchanged; only the numbers moved.**

---

## 0. Why these two, in this order

| | |
|---|---|
| ⭐⭐ **`U-1` first, always** | ⛔ **Every later `U-` task's success condition is *"the output did not change."*** That is **unfalsifiable** without a recorded baseline. `U-1` ships **no product change at all** |
| ⭐ **`U-2` second** | it is the **smallest real change in the programme** and its Pass 2 is *"golden unchanged"* ⇒ ⭐ **it is how you find out whether the net you just built actually holds a fish** |
| ✅ **Both compiler-only** | ⛔ **no editor surface, no panel, nothing that needs a human at a screen.** ⚠ **The visual check has not run for NINE batches and is unavailable right now** — this batch was chosen because it does not need it |

---

## 1. ⭐⭐ `U-1` — the golden-corpus harness

### 1.1 The corpus — ⛔ **not "all shipped `.bp.json`"**

```xml
<!-- Hrot.AI.Behaviors.csproj:100 — THIS is the corpus, and nothing else -->
<AdditionalFiles Include="Assets\Blueprints\**\*.bp.json" />
```

✅ **Coordinator-verified today: 42 files** under that glob.
⛔ **`Recipes/Blueprints` is `Content`** — production never compiles it, and globbing **both roots
throws**, because assets exist in each sharing an `AssetId`. ⭐ **42 is the right number and, on its
own, the wrong definition — take the glob, not the count.**

⚠ **The preload:** three `HillAssault2I_*` assets fail `BP1602` under a bare `Compile`, because a null
`ClrSignatureResolver` makes Stage 0 reflect over **loaded** assemblies. ⭐ **One `typeof(...).Assembly`
touch on `Hrot.AI.Behaviors` ⇒ 42/42.** *(Your own measurement, Batch 40 — restated, not re-derived.)*

### 1.2 ⭐ What it records — the reviewed two-tier invariant

| tier | contents | moves |
|---|---|---|
| **1 — never moves undeclared** | `StructureHash` · **every emitted struct field** (name · type · offset · size) · the **diagnostic multiset** (code × count) | ⛔ a change here is a **failure**, not a rebase |
| **2 — moves with a regenerated baseline** | ⭐ **the full generated source, stored as FILES** (~250 KB total), ⛔ **not hashed** | ✅ regenerating is a **reviewable diff** |

⭐ **The reason Tier 2 is files and not a hash:** *"a hash names the asset; a stored file names the
LINE."* ⚠ **This programme's recurring defect shape is a denormalised copy no test compares against its
source** — Stage 2.5's own comment says so. A hash is that shape again.

### 1.3 ⭐⭐ **Reuse the snapshot mechanism — do not invent a second one**

✅ **It already exists and already does exactly this**, `TestData.cs:49`:

```csharp
public static void ReadOrRegenerateSnapshot(string relativePath, string actual)
// BLUEPRINT_REGENERATE_SNAPSHOTS=1 rewrites; otherwise compares, LF-normalised on both sides
```

⭐ **`AiPrimitiveEmitGoldenTests` / `InstanceEmitGoldenTests` / `LibraryEmitGoldenTests` are the
precedent** — same idea, hand-picked assets instead of the corpus. ⇒ **`U-1` is the sweep those three
imply**, not a new concept.

⚠ **One measured wart to decide about, and say which you chose:** on mismatch the helper throws with
**both whole files inlined in the message**. ⭐ **Fine for a 3 KB snapshot; for a 250 KB corpus sweep a
single failure floods the test output and buries the asset name.** 📐 **Your call** — a first-differing-
line/context report, a cap, or leave it and say why. ⛔ **A failure message nobody can read is a harness
that reports its finding to nobody.**

### 1.4 🔴 **Prove it BITES** — the item that makes this task real

⭐ **Mutate one field's declaration order in a scratch run ⇒ the test MUST fail, naming the asset and
the field.** ⛔ **A harness that has never failed is not a harness** — it is 42 green checkmarks that
would stay green through `U-9`.

⭐ **Do the same for each tier separately**, and report which mutation reddened which tier:

| mutation | should redden |
|---|---|
| reorder a field | Tier 1 (offsets) **and** Tier 2 |
| rename a local/temp in emitted code without moving a field | ⭐ **Tier 2 ONLY** — which is the whole point of keeping two tiers |
| introduce one extra diagnostic | Tier 1 (multiset) |

### 1.5 📌 Close one gap inside this task

⚠ **The harness runs the IN-PROCESS path** (reflection resolver); ⭐ **production runs the
SEMANTIC-MODEL resolver** (`RoslynClrSignatureResolver`, handed in by
`BlueprintIncrementalGenerator`). **Diagnostic sets are known to match. Byte parity has never been
compared.** ⇒ ⭐ **Compare it ONCE, via `EmitCompilerGeneratedFiles`, and record the answer.**

⛔ **If they differ, do not "fix" it in this batch** — record what differs and where. ⭐ **That finding
is worth more than a patched harness**, and it changes what `U-7`/`U-8` can assume about the resolver.

### 1.6 ✅ Cost — already measured, so this is a budget not a question

**634 ms for all 42**, ~5 ms/asset warm, against a Blueprints gate already ~95 s.
⇒ ⭐ **a gate, not a nightly.** ⚠ **If your build comes in materially above that, say so** — it means
the corpus or the path is not the one that was measured.

---

## 2. `U-2` — the compiler must own its graphs (`BP-229`)

### 2.1 The defect, coordinator-re-verified today

```csharp
// BlueprintCompiler.cs:50 — the shallow copy, and its own comment admits the shape
Graphs = new List<Graph>(asset.Graphs),  // new list, same graph objects
```

⇒ ⛔ **`Stage2_5_ExpandMacros` then mutates those shared objects in place**, `:68` and `:83`:

```csharp
var calls = host.Nodes.OfType<MacroCallNode>().ToList();   // host IS the caller's Graph
…
MacroExpander.Expand(asset, host, call, macro, ctx.Diagnostics.Add);
```

⇒ ⭐⭐ **Compiling an asset EDITS it.** The caller's macro call node is gone and the macro body is
spliced into the graph the designer is looking at.

### 2.2 Gates

| | |
|---|---|
| ✅ **Pass 1** | after `Compile(asset)` on an asset whose graph holds a `MacroCallNode`: the **caller's** graph still holds it · same node count · same link count |
| ✅ **Pass 2** | ⭐ **golden unchanged** — `U-1`'s corpus, run against the baseline `U-1` just committed |
| 🔴 **Revert-goes-red** | remove the copy ⇒ **Pass 1 fails** |

### 2.3 ⚠ The one thing to preserve, and it is in the existing comment

⭐ **Stage 0's pin rehydration is INTENTIONALLY visible to the caller** — the comment at `:28-31` says
so. ⛔ **A deep copy that also hides Stage 0 changes behaviour.** 📐 **Decide where the copy goes and
say which:** deepen the copy and re-expose Stage 0's result, or copy **between** Stage 0 and Stage 2.5.
⭐ **Either is defensible; silently dropping the rehydration is not.**

📌 **`LocalVariables` rides on `Graph` too.** ⚠ Batch 43 just made locals editable, so a graph object
now carries designer-owned state that a write-through can corrupt. ⭐ **Whatever copy you choose must
cover it — and a test that a compile does not disturb the caller's `LocalVariables` is worth having.**

---

## 3. ⚡ How to work

**You are on Opus.** 🟢 **Sonnet is a reasonable fit for `U-1`'s mechanical sweep** — 42 assets through
an existing snapshot helper. ⭐ **Opus keeps §1.4 (prove-it-bites), §1.5 (the parity finding) and all of
`U-2`.**

⚠⚠ **Coordinator's standing correction to itself:** *"🟢 Sonnet takes it"* on the load-bearing item is
what lost the Local Variables section **twice**. ⇒ ⛔ **Delegate the sweep, never the proof.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the **tracker is yours** — ⭐ **`BP-229`'s row closes with `U-2`** |
| **Revert-goes-red** | both tasks, **never delegated** |
| ⚠ **Stop point** | ⭐ **`U-1` alone is a complete, shippable batch.** If §1.5's parity check turns up a real difference, **stop there and report it** — `U-2` is worth less than that finding |

---

## 4. Gates

The eight, `--logger "console;verbosity=normal"`. Solution **`IOS-IG-SimHost.sln`**.
⚠⚠ **The two NodeEdit gates take NO `--no-build`.** ⛔ **Neither task should move them.**
⭐ **Run the five `--no-build` suites in PARALLEL** — your own measurement, 3m40s → 2m05s — **and keep
`\[FAIL\]` in the result grep.**
⭐ **`python3 scripts/tracker-counts.py --check`** — clean **twelve** batches running.

**Baseline — coordinator-run on the merged Batch-43 tree (`3583acd4`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** *(⚠ an incremental build under-reports — record honestly)* |
| Blueprints | **3313 total / 3303 passed / 0 failed / 10 skipped** ⚠ **`U-1` adds ~42** |
| ⭐ **AiShared 1213** | ⛔ **must NOT move** — no editor surface is in this batch |
| BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |

⚠ **`U-1` adds ~250 KB of committed baseline files.** ⭐ **Say where you put them** and confirm
`ResolveSnapshotsDir()` finds them from both `bin/` and the repo root — the helper walks up, and a
baseline the gate cannot locate fails as *"snapshot not found"*, which reads like a missing file rather
than a broken path.

---

## 5. Reporting

Per-suite numbers · `tracker-counts.py --check` · ⭐ **every id you allocated** ·
⭐⭐ **§1.4: which mutation reddened which tier** · ⭐⭐ **§1.5: the in-process vs semantic-model parity
answer, whatever it is** · **your §1.3 failure-message choice** · **your §2.3 copy placement** ·
⭐ **where you stopped** · anything here **wrong against the code**.

⛔ **Say plainly what is NOT covered headlessly.** ⚠ **Nothing in this batch should need the visual
check — if you find something that does, that is a finding, and it changes which batch runs next.**

⭐ **Batch 43's best instinct was refusing the handoff's framing of the reorder warning and giving the
reason.** ⚠ **§1.3 and §2.3 here are both deliberately left open for the same treatment: if the shape
this handoff assumes is wrong against the code, say so rather than building it.**
