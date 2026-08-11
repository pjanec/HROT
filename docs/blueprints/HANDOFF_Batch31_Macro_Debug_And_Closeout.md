# HANDOFF — Batch 31: prove the macro payoff, then give it a debugger

> 📌 **Dispatched at `<pending>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7 is yours:** branch from this branch, and re-sync from it at the **start** of your run.
> ⭐ **Rule 4 is yours:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** **No new `BP-2xx` number appears below.** `BP-83`,
> `BP-220`, `BP-111`, `BP-82`, `BP-57` are *referenced* existing rows. Number every new finding
> yourself and say what you chose (rule 5).
>
> ⭐ **Batch 30 was very strong** — both design defects handled as flagged, the cloner moved down rather
> than copied, `ClonedFragment` exposing the maps, `BP1665` naming `BP1662`, `BP1661` blaming the call
> site. **This batch is mostly closing out what that batch opened.**
>
> ⚠ **Everything here is headless.** BP-80's two visual gestures remain deliberately out of scope.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** the two macro tests | ⭐ **the tick-across-frames harness** — it is the one genuinely new thing | the rename + wiring the real-Roslyn assert |
| **2** `BP-83` debug provenance | the serializer-compat decision (§2.3) | the field, the threading, the arming test |
| **3** `BP-220` | the `CanonicalAsset` ruling (§3.2) | the copy-shape fix itself |
| **4** `BP-111` | — | ⭐ **entirely** |

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

---

## 1. 🔴 Finish proving the macro capability — the two carry-forwards

⭐ **Neither is a defect in Batch 30's code.** The pass is right; what is missing is the evidence for the
one scenario macros exist to serve.

### 1.1 A test name overclaims — fix the body, not the name

`MacroExpansionTests.LatentMacro_SplicedIntoATickGraph_`**`CompilesThroughTheRealGenerator`**
**does not compile through the real generator.** Verified: its body calls `Expand(...)` and asserts
splice shape (cloned `LatentDelayNode`, provenance, no `MacroCallNode` left, the mirror) — and the
**whole file contains no `CSharpCompilation`.**

⚠ Real Roslyn in this repo means **`CSharpCompilation.Create`** — `Stage8Tests.cs:168`,
`Integration/AuthoringPath.cs:316,340`. ⭐ **The test's own doc-comment cites the *"`.Succeeded` alone
never invokes Roslyn"* rule**, so the intent was right and the body does not carry it out.

⇒ **Make the body match the name.** ⛔ **Do not rename it to match the body** — that would keep a green
test whose subject is uncovered. A test claiming more than it checks is worse than no test, because it
retires the question.

### 1.2 ⭐ The payoff case — *run* it, do not just expand it

Design §6 asks for it and it is still missing:

> *aim → `Delay 0.4` → fire* in a macro, expanded into a tick graph, **ticked to completion across
> frames.**

**Why this one and not another integration test:** `BP-78` is on record that **a macro is the only
construct that can factor out a reusable *latent* sequence** — `BP1650` forbids latent nodes in a
Function graph, which is the whole reason macros were built. **Splice shape is proven; the thing the
feature exists for has never been executed.**

⇒ Expand, compile **through Roslyn**, execute, and **assert a value across ticks** — the sequence
suspends and resumes, and finishes. `Integration/AuthoringPath.cs` already has the harness shape.

⚠ **Then do it at two call sites in one graph**, because that is where a shared `Cursor` would show up
if the splice got resume-state wrong. Batch 30 proved two *clones* exist; it did not prove two
*suspensions* coexist.

---

## 2. 🟠 `BP-83` — macro debug provenance. **The blocker is smaller than the design thinks.**

Design §5 parked this as *"needs the debug-map shape decided first."* ⭐ **Coordinator-researched
against code — the shape question is now answered, and it is one field.**

### 2.1 Where the provenance stops

Batch 30 threaded it correctly as far as the IR:

```csharp
// Stage5_Schedule.cs:4671-4672 — ✅ carries it
private static IrDebugAnnotation DebugOf(Node node) =>
    new IrDebugAnnotation { GraphId = default, NodeId = node.Id, OriginNodeId = node.OriginNodeId };
```

**Then `CSharpEmitter` drops it** (`:43-49`, and `:51-56` for the end):

```csharp
var effectiveNodeId = debug?.NodeId ?? debug?.OriginNodeId;   // NodeId always wins
_debugMap.RecordNodeStart(effectiveNodeId.Value, debug!.GraphId, _currentLine, …);
//                        ^^^ only ONE id is passed on — OriginNodeId ends here
```

And `DebugMapEntry` (`DebugMapBuilder.cs:52-57`) has nowhere to put it:
`(Guid NodeId, Guid GraphId, int StartLine, int EndLine)` + `NodeKind` · `DisplayName` · `PhaseIndex`.

⇒ **The debug map — the thing the debugger actually consumes — cannot answer *"which lines belong to
authored node X?"* for anything inside a macro body.** A breakpoint set on a node **in the macro
graph** (which is where the designer sees it) matches **no** entry, because every entry carries a
*clone* id that exists in no asset file.

### 2.2 The work

| | |
|---|---|
| **Model** | add `OriginNodeId` to `DebugMapEntry` — ⚠ **and `OriginGraphId`**, because the authored node lives in the **macro** graph while `GraphId` is the **host**. Without both, an authored id is ambiguous across two macros |
| **Thread** | `CSharpEmitter:43-56` pass both ids · `DebugMapBuilder.RecordNodeStart`/`RecordNodeEnd`/`_openNodes` carry them · `DebugMapSerializer:89` round-trip |
| **Arm** | breakpoint resolution maps **authored node → N entries**. ⭐ This is the payoff: one breakpoint in a macro body arms **every** expansion site |

⚠ **Do not change the `NodeId ?? OriginNodeId` precedence.** The clone's id winning is what keeps
**line→node 1:1** while making **node→line one-to-many**; that asymmetry is the feature. You are
*adding* a back-reference, not redirecting the existing one.

### 2.3 📐 The one decision — **`DebugMapSerializer` has `SchemaVersion = "1.0"` (`:21`)**

Adding fields changes the on-disk debug map. **Your call:** bump to `1.1` and tolerate `1.0` on read
(absent origin ⇒ null, which is exactly right for a pre-macro map), or something else. **State the
reasoning.** ⚠ Whatever you choose, a `1.0` map must still load — old maps are not wrong, just older.

📌 **No architect round needed, and this is deliberate:** BP-83's *subject* was settled by Q25/the macro
design; what was open was a code-shape question, answered above against the code in the Q23/Q24/Q25
pattern. ⭐ **Say so in the row**, per the standing preference.

---

## 3. 🟢 `BP-220` — the field-by-field `Graph` copy, and a bigger thing behind it

### 3.1 The defect as you filed it — confirmed

`Graph` has **10** members. Both reconstruction sites copy **9**, dropping **`Comments`**:
`Stage3_Normalize.cs:137` (`MaterializeDefaultPinLiteralsInGraph`) and **`:431`**
(`EliminateOrphanNodesInGraph`). ✅ **`Stage2_5_ExpandMacros` does *not* rebuild `Graph`** — it mutates
in place, so Batch 30 added no third site. Good.

⇒ **Fix the shape, not the field.** A copy that must be remembered is a copy that will be forgotten —
`ExecOutputs` had to be hand-added to both sites in Batch 29, and nothing would have failed if it had
been missed at one.

### 3.2 ⭐ What makes it worth more than a tidy-up — `CanonicalAsset`

`CompileResult.CanonicalAsset` is assigned at `BlueprintCompiler.cs:113` (`typed.Asset`) and `:144` —
i.e. **the post-Stage-3 asset**. ⚠ **Coordinator-measured: `.CanonicalAsset` is read in NO file in the
repo.** Set twice, consumed nowhere.

⭐ **And after Batch 30 it changed meaning.** It now carries a **macro-expanded** graph: call nodes
replaced by inlined clones, comments stripped, literals and casts synthesized. **The first consumer to
treat it as "the asset" would see a designer's macros silently inlined.**

📐 **Your call, and it is the Opus part:** either **delete it** (nothing reads it) or **document it
loudly** as *"post-normalization, post-expansion — NOT the authored asset; never persist this."*
⚖️ My lean is **document over delete** — it is plausibly the right output for a hot-reload or
round-trip path — but an unread field whose meaning silently widened is exactly the shape that bites
later. **State which and why.**

---

## 4. 🟢 `BP-111` — implement your own recommendation

You wrote it and it is right:

> *a wall-clock nanosecond budget and a zero-allocation assertion are both meaningless on a shared
> cloud VM — they measure the host, not the code. Put the whole `WhenNodePerfTests` perf/allocation
> family behind a trait the normal suite filters out.*

⭐ **Three names now**, across two sessions: `WhenNode_EqsResult_Under150ns_perTick` (the row's original),
`ReadEqsResultNode_Under80ns_perInvocation` (**25 µs vs an 80 ns budget** — coordinator-observed twice),
`Spawn_ZeroAllocation` (**7 696 bytes vs 0** — yours). ⇒ **Naming them one at a time is chasing
symptoms.** Do the trait.

⚠ **Do not delete the assertions** — they are meaningful where timing is controlled. Filter them, and
say in the row how they are meant to be run.

---

## 5. 📌 `BP1664` stays reserved — **do not build it**

Design §4 lists `BP1664` = *"macro declares a local variable"*. ⚠ **Coordinator-verified: `Graph` has
no `LocalVariables` field at all** — per-graph locals are **`BP-57`, still open** (tracker: *"`Graph`
has no `LocalVariables` field"*). ⇒ **A macro cannot declare a local, so the rail has nothing to
check.** It becomes real only when BP-57 ships.

⭐ **Recorded so a future session does not spend a morning discovering this.** Note it on the `BP-82`
row rather than implementing it.

---

## 6. Gates

The eight, `--logger "console;verbosity=normal"`. Solution is **`IOS-IG-SimHost.sln`** (⚠ not `Hrot.sln`).
⚠⚠ **The two NodeEdit gates take NO `--no-build`** (see `RESUME_START_HERE.md` §3).

**Baseline — coordinator-RUN on this tree, all eight:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| BP diagnostics | **10 distinct**, all `BP3010`, all **authored** orphans in 2 assets |
| Blueprints | **3145** / 0 / 10 skipped |
| AiShared **1213** · BTree **612** · Breakpoints **130** | 0 failed |
| NodeEdit Core **208** · UI **131** · Generators **193** | 0 failed |

### ⚠⚠ Item 2 has a consumer OUTSIDE the eight gates — run it by hand

Coordinator-measured. The suites whose **source** touches `DebugMap`:

| Where | Gated? |
|---|---|
| `Hrot.Blueprints.Tests` — `Stage7_EmitTests/FIX2_002_DebugMapEmitTests.cs`, `Stage8Tests`, `CapturingDebugSession` | ✅ yes (gate 2) |
| 🔴 **`Hrot.ClusterRunner.Integration.Tests/BlueprintObserveTests.cs`** | ⛔ **NO — not one of the eight** |

`BlueprintObserveTests` *"manually registers a `DebugMap` that mirrors the compiler's"* to prove the
live debug-observe loop. ⇒ **A `DebugMapEntry` shape change can break it with every gate green.**

```bash
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/*.csproj -v q --nologo
```

⭐ **Run it for item 2 and report it.** ⚠ **I have not measured its baseline** — establish one before
you change anything, and say whether it was already red (`BP-123` records that
`Fdp.Core.Tests` is red on a clean tree in this environment, so a pre-existing red is plausible).

📌 `Hrot.Diagnostics.Breakpoints.Tests` is **not** the relevant suite despite the name — its only
`DebugMap` matches are compiled artifacts under `bin/`.

⚠ The 10 `BP3010`s are expected; `EnumDemo` is the **T32 committed gate asset**.

---

## 7. Reporting

Per-suite numbers · the **BP-warning count and composition** · revert-goes-red per item · ⭐ **every BP
id you allocated** (rule 5) · your rulings on **§2.3** (schema version) and **§3.2** (`CanonicalAsset`) ·
⭐ **whether the latent macro actually ran across frames, and what value you asserted** · anything here
**wrong against the code**.

⭐ **You have corrected the coordinator in every batch so far and been right each time.** If something
above does not match the tree, say so plainly.
