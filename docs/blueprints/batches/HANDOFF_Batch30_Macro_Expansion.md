# HANDOFF — Batch 30: `Stage2_5_ExpandMacros` — the pass that makes macros real

> 📌 **Dispatched at `9af3c78e`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** **No new `BP-2xx` number appears below.** `BP-81`,
> `BP-82`, `BP-219`, `BP-111` are *referenced* existing rows. `BP166x` are **diagnostic codes** from the
> reserved block (`DiagnosticCodes.cs:98` — `BP1660`-`BP1667` reserved, `BP1668` taken by BP-80), not
> tracker ids. Number every new finding yourself and say what you chose (rule 5).
>
> 📄 **[Macro_Implementation_Design.md](Macro_Implementation_Design.md) §3 is the algorithm** — fixpoint,
> the five splice rules, clone reuse, provenance. Read it first. ⚠ **§0 below corrects three things in
> it**; the corrections are the reason this handoff exists rather than "go build §3".
>
> ⭐ **Batch 29 was excellent work.** The `BP-217` reorder-with-a-proof, catching the `BP1655` regression
> I did not flag, and doing H2 as defence in depth — that is the standard this batch is scoped against.

---

## 0. ⚠ Three things in the design that are wrong or stale on this tree

Verified by the coordinator at `1a63771`. **Do not build §3 without reading these.**

| # | Design says | Actually |
|---|---|---|
| **A** | *"Recursion is already excluded upstream by the BP1654-shaped cycle rail (BP-82), so the cap only catches pathological depth, never a loop"* | 🔴 **BP-82 has not landed.** That precondition does not exist. A macro cycle would spin the fixpoint to `MaxDepth` and report **`BP1665` "expansion exceeded 16 rounds"** — fail-loud, but **misattributed**: it names depth when the cause is a loop ⇒ **item 2 pulls the rail forward** |
| **B** | *"Every cloned node gets `OriginNodeId` = the authored node's id"* | 🔴 **There is nowhere to put it.** `Node` (`Nodes.cs:60`) has exactly `Id` · `Pins` · `EditorMetadata` · `PinDefaults` — **no provenance field** — and `OriginNodeId` lives on `IrDebugAnnotation` (`:15`), which `DebugOf(Node)` (`Stage5:4661-4662`) builds as `{ GraphId = default, NodeId = node.Id }`, **never setting it** ⇒ see item 1.4 |
| **C** | `ComputeMergePoints` at `Stage5:4269` | ⚠ now **`:4624`** (called from `:255`). The design's other coordinates drifted the same way — **re-grep every line number before you trust it** |

📌 **Where the pass goes:** `BlueprintCompiler.cs` — after `Stage2_Validate.Run` (`:57`) **and its
`if (sink.HasErrors) return FailResult` gate (`:58`)**, before `Stage3_Normalize.Run` (`:61`).
⭐ **That gate is load-bearing and it is free**: expansion never runs on a graph Stage 2 rejected, so
every rail you add in item 2 is a genuine **precondition** for the splice, not a redundant re-check.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** the expansion pass | ⭐ **all of it** — splice rules 1-4 are novel IR-adjacent work | the golden node/link-count test fixtures |
| **2** `BP1661` (F1, latent) | 🔴 **yes** — reachability reasoning | `BP1660`/`BP1662` mirror `BP1651`/`BP1654` exactly |
| **3** `BP1663` (F2, purity) — **or the refusal** | 🔴 the purity walk | the negative-asset tests |
| **4** `BP-219` `Info` arm · **5** `BP-111` flake list | — | ⭐ **entirely** |

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```
⚠ **Gate every commit on the fix being in the tree**, not on an agent reporting success.

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | The **tracker + detail docs are yours** for this batch; the coordinator will not touch them |
| **Revert-goes-red** | Every fix, **never delegated** |
| **Commit per item** · **stop cleanly at a boundary** · **no PR** | |

⭐ **Order:** items 2 and 3 are **preconditions** for item 1 being safe, but item 1 is the interesting
work. Build item 1 first if that suits you — **but do not land it alone.** See item 3's gate.

---

## 1. 🔴 `BP-81` — `Stage2_5_ExpandMacros`

### 1.1 The shape (design §3, unchanged)

**Fixpoint, not recursion.** Nested macros fall out on the next round; the depth cap *is* the round
counter, so there is no separate mechanism.

```
for round in 1..16:                             # then BP1665
    calls = host.Nodes.OfType<MacroCallNode>()
    if calls is empty: break
    for each call C: splice(C)
else: BP1665
```

### 1.2 The five splice rules — design §3, and rule 5 is the one that bites

Let **M′** = fresh clone of the macro body, **In′**/**Out′** its boundary nodes.

| # | Rule | ⚠ |
|---|---|---|
| 1 | `X.out → C.execIn` becomes `X.out → succ(In′.execOut)` | `In′.execOut` unwired ⇒ empty body, call is a no-op ⇒ **`BP1667` warning** |
| 2 | `Z.out → Out′.execIn[k]` **+** `C.execOut[k] → Y.in` become `Z.out → Y.in` | several `Z` may feed one `execIn[k]`; in-degree ≥ 2 at `Y` is fine — `ComputeMergePoints` (**`:4624`**) allocates one shared block |
| 3 | consumers of `In′.dataOut[p]` re-tie to `pred(C.dataIn[p])` | unwired ⇒ synthesise a `LiteralNode` from `Pin.DefaultValue`, else `ParameterDecl.DefaultValueJson`, else error |
| 4 | consumers of `C.dataOut[q]` re-tie to `pred(Out′.dataIn[q])` | unwired ⇒ **`BP1655` already covers it**, reused verbatim |
| 5 | delete `C`, `In′`, `Out′`; drop their pins | ⭐ **every rewire updates `Pin.LinkedToIds` on BOTH endpoints** — the BP-23a lesson: a stale mirror makes a node claim wires it does not have |

⭐ **Rule 5's mirror is exactly the class of defect this programme keeps finding**: a denormalised copy
that no test compares against its source. **Assert the mirror, not just the link list.**

### 1.3 Cloning — reuse `BlueprintClipboard`, and the factoring is better than the design says

`BlueprintClipboard.Rehydrate` (`BlueprintClipboard.cs:129-184`) already does the JSON deep-copy, fresh
**node and pin** GUIDs, internal link remap, and the `LinkedToIds` mirror (`:159-164`). Verified.

⚠ **Two known deltas, per design §3 — plus a third the design misses:**

1. **Boundary links must be rewired, not dropped.** `:171-174` `continue`s on any link with an endpoint
   outside the fragment — **precisely the links rules 1-4 need.**
2. **Assembly direction.** `BlueprintClipboard` is in **`.Editor`**; Stage 2.5 is in **`.Compiler`**, and
   the dependency runs Editor → Compiler. ⛔ **Move the shared core down, do not duplicate** — BP-69
   duplicated `ResolveCustomEventDecl` across this exact boundary and the two copies drifted.
3. ⭐ **New — the shared primitive must return the MAPS, not just the fragment.** `Rehydrate` builds
   `nodeMap`/`pinMap` (`:138-139`) and **throws them away** at `:183`. Those maps are the whole input to
   rules 1-4: without them you cannot say *"the clone of `Out′.dataIn[q]`"*. ⇒ factor out
   `(nodes, links, nodeMap, pinMap)` and let the clipboard keep its `Vector2 offset` step layered on top
   — the offset is an editor concern and should not go down with the core.

### 1.4 ⚠ Provenance — design finding B, and it needs a ruling

The design wants each cloned node to carry the authored node's id so BP-83 can arm a breakpoint at
**every** expansion site. **There is no field for it** (§0·B).

📐 **Your call. My recommendation, stated as a lean, not a ruling:**

> Add `[JsonIgnore] public Guid? OriginNodeId` to `Node`, and have `DebugOf` emit it.

⭐ **`[JsonIgnore]` is the point** — it is in-memory only, so **"that is the entire on-disk change"
(design §2) stays true**, no asset round-trip moves, and `DebugOf(Node)` stays `static` because it can
read the field straight off the node. The alternative — a side map in the compile context — forces
`DebugOf` to become an instance method or take an extra parameter at its **55** call sites in
`Stage5_Schedule.cs` alone.

⚠ **Whichever you pick, the precedence is already right and is deliberate:** `CSharpEmitter:45,53` read
`debug?.NodeId ?? debug?.OriginNodeId`, so **the clone's own `NodeId` wins**. That is what makes one
authored node yield **two `DebugMapEntry` rows at two call sites** — ⭐ **line→node stays 1:1,
node→line becomes one-to-many.** That is BP-83's whole subject; do not "fix" it.

---

## 2. 🟠 Pull `BP1660` + `BP1662` forward from `BP-82` — they are preconditions, not scope creep

§0·A: the design's algorithm **assumes** these exist. They do not.

| Code | Sev | Rule | Mirror |
|---|---|---|---|
| `BP1660` | Error | `MacroCallNode.TargetGraphId` does not resolve to a `GraphKind.Macro` graph | `BP1651` verbatim |
| `BP1662` | Error | macro call cycle, direct or mutual | `BP1654`'s DFS, over macro-call edges |

Both are Stage 2 validators — `V_FunctionGraphCallRules` (`Stage2_Validate.cs:2081`, registered at
`:49`) already builds `callEdges` and already walks called graphs. **The macro versions are the same two
passes over macro-call edges.** 🟢 Sonnet.

⭐ **Why they are not deferrable:** Stage 2's error gate (`BlueprintCompiler.cs:58`) runs *before*
expansion. With these rails in place, `splice()` may **assume** a resolvable, acyclic target — which is
what lets item 1 be written without defensive null-handling on every rule. Without them, `BP1665`
reports *"exceeded 16 rounds"* for what is actually a two-macro cycle, and the designer chases depth.

---

## 3. 🔴 `BP1663` (F2) — **or an explicit refusal.** One of the two must land.

**The rule (design F1/F2, ✅ ACCEPTED by the user, §7):** when a macro declares **≥ 2 exec-outs**, every
data output must be fed by a **pure** producer chain.

⚠ **Why it becomes live in THIS batch and not before:** until expansion works, a multi-exec-out macro
cannot be spliced, so the hazard is unreachable. **Item 1 makes it reachable.** The failure is
`CS0165` — *"use of unassigned local `__t5`"* — a **hard error in generated code**, pointing at the
**consumer**, not at the impure producer on the other path. Loud, and uninterpretable.

⭐ **The canonical case passes anyway**: Unreal's `ForEachLoop` is exactly this shape — one exec-in, two
exec-outs, plus data outputs — fed by **pure** array reads.

⛔ **The gate, and it is the one hard requirement in this batch:**

> **Either `BP1663` lands, or `Stage2_5_ExpandMacros` refuses a macro declaring ≥ 2 exec-outs with a
> clear diagnostic saying the check is not implemented yet.**

⛔ **What must NOT happen is shipping an expansion pass whose first two-exec-out user gets `CS0165`
about `__t5`.** A temporary refusal is honest and cheap; a silent path to an incomprehensible generated
-code error is the exact failure this programme exists to stop. **Say which you chose and why.**

📌 **Not in this batch:** `BP1664` (macro declares a local), BP-82's two library rails, **BP-83** debug
provenance (item 1.4 only lays its foundation), BP-80's two visual gestures.

---

## 4. 🟢 `BP-219` — the missing `Info` arm

`BlueprintIncrementalGenerator:186-188` maps `defaultSeverity: diag.IsError ? Error : Warning` — a
**two-way branch over a three-member enum**, the same missing-arm shape as `BP-215`/`BP-216`. Latent
today (nothing emits `Diagnostic.Info`), but the first person to reach for `Info` gets a **Warning**,
which under `TreatWarningsAsErrors` is a build break. ⭐ **You found it; you close it.**

## 5. 🟢 `BP-111` — the known-flake list names the wrong sibling

Coordinator-measured verifying Batch 29: the test that actually flaked is
**`WhenNodePerfTests.ReadEqsResultNode_Under80ns_perInvocation`** — **25 µs against an 80 ns budget**
on a shared cloud VM, green on three subsequent runs. **BP-111's row names
`WhenNode_EqsResult_Under150ns_perTick`** — a *different* sibling.

⇒ That is BP-111's own thesis (*"the known-flake list is incomplete"*) confirmed with a second name.
Add it. ⚠ **A wall-clock budget of 80 ns is not meaningful on a shared cloud VM at all** — consider
whether these belong behind a category the full-suite run skips, and say what you decided.

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** — those projects are outside the solution, and with
`--no-build` they silently do not run (see `RESUME_START_HERE.md` §3).

**Baseline — coordinator-RUN on this tree (`1a63771`), all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | ⭐ **10 distinct** — all `BP3010`, all **authored** orphans in 2 assets |
| Blueprints | **3128** / 0 / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

⚠ **Known flake, not yours:** `ReadEqsResultNode_Under80ns_perInvocation` (item 5). If it is your only
red, re-run before investigating — **and say that it flaked**, do not silently re-run until green.

⚠ **The 10 remaining `BP3010`s are EXPECTED.** They are the authored orphans in `InlineEd1 ▸ Tick` and
`EnumDemo ▸ Main`, deliberately left by Batch 29. ⚠ `EnumDemo` is the **T32 committed gate asset** —
do not edit it casually.

---

## 7. Testing — ⭐ this is fully headless (design §6)

| Layer | Test |
|---|---|
| **Splice** | golden: expanded node/link counts **+ the `LinkedToIds` mirror agrees with the link list** |
| **Compile** | through the **real Roslyn generator** ⚠ (`.Succeeded` never invokes Roslyn) |
| **Run** | ⭐ execute and **assert a value** |
| ⭐ **Latent** | **the payoff case** — *aim → `Delay 0.4` → fire* in a macro, expanded into a tick graph, **ticked to completion across frames**. This is the thing macros exist for (BP-78: a macro is the only construct that can factor out a reusable *latent* sequence) |
| **Nested** | a macro calling a macro — proves the fixpoint, not just one round |
| **Two-site** | same macro at two call sites ⇒ **two** `DebugMapEntry` rows, same authored id (item 1.4) |
| **Negative** | one asset per code — `BP1660`/`BP1662`/`BP1665`/`BP1667` (+`BP1661`, and `BP1663` or the refusal), asserting the **code**, not just failure |

---

## 8. Reporting

Per-suite numbers · the **BP-warning count and composition** · revert-goes-red per item · ⭐ **every BP
id you allocated** (rule 5) · **your ruling on item 1.4** (provenance) and **item 3** (`BP1663` or the
refusal) · anything here **wrong against the code**.

⭐ **You have corrected the coordinator repeatedly and been right** — twice in Batch 29 alone. If
something above does not match the tree, say so plainly; that is the most valuable line in your report.
