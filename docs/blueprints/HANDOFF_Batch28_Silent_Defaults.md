# HANDOFF — Batch 28: silent `default:` arms, and the macro net that closes the same trap

> 📌 **Dispatched at `a7051eea`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 4 is yours:** pull the coordinator branch again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids** — **no new `BP-2xx` numbers appear below.** The only
> one cited (`BP-211`) is an existing Batch 27 row, referenced not allocated. Number every finding
> yourself when you create the rows, and say what you chose (rule 5).
>
> 📄 **Read [FINDING_SetVariable_ValueOut.md](FINDING_SetVariable_ValueOut.md) first** — items 1–3 are
> all one root cause, already traced end to end. Then
> [Macro_Implementation_Design.md](Macro_Implementation_Design.md) for item 4.
>
> ⭐ **One theme, two floors.** Every item here is the same defect: **a `default:` arm that returns a
> plausible value instead of reporting.** Trap #5. Items 1–3 are the data-source switch; item 4 is the
> graph-kind switch. Fix them together and the pattern gets internalised once.
>
> ⭐ **The user still has very limited visual-testing capacity. Everything here is provable
> headlessly.** If an item cannot be proven by a test, say so rather than shipping it unproven.

---

## 0a. ⚡ How to work — the standing rules

**You are on Opus. Delegate to Sonnet everything that does not need Opus-level reasoning.**

| Item | 🔴 Opus keeps | 🟢 Sonnet takes |
|---|---|---|
| **1** `SetVariable.Value`-Out | the `_statementPinCache` placement — get it wrong and the value goes stale across blocks | the emit + the run-value test |
| **2** make `default:` report | ⭐ **the diagnostic's severity and wording — not delegable.** It decides whether item 3 is a build break or a warning sweep | threading it to call sites |
| **3** the audit | the per-row verdict (a projected pin may be legitimately served elsewhere) | running the three-step check and writing it up |
| **4** `GraphKind.Macro` + net | the guard's **wording** (see item 4) | the enum member, the map arm, the tick-eligibility test |
| **5** dead anchors | — | ⭐ **entirely** |
| **6** *(only if room)* macro model + projections | the N-exec-in projection | the model delta + the parity test |

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

---

## 1. 🔴 The printed `0` — `SetVariable`'s `Value`-Out pin is a promise nothing implements

**Closes the one item Batch 27 left open.** Root cause is traced; do not re-derive it.

The user's `Count4.bp.json` wires `SetVariable.Value`-Out → `Print String.{threat}`. **Every pin id
resolves** (recomputed from `DeterministicIds.PinId`), **both projection halves emit the pin** typed
`System.Int32` (`NodePinSchema:792-799`, `Stage0_Rehydrate:333-345`) — which is why the build is
clean. But `SetVariableNode` appears **exactly once** in `Stage5_Schedule.cs`, at `:1186`, and emits
`IrOp_WriteVariable` with **no `ResultValue` and no `_statementPinCache` entry.**

⇒ Pulling that pin finds no case in `ResolveNodeOutput` and lands in `:3589`'s `default:` arm →
`IrOp_Const("default", Int32)` → **`0`, silently, every tick.**

**Work:** allocate a `ResultValue` for the `Value`-Out pin, a pass-through of the written value, and
record it in ⭐ **`_statementPinCache` — the cross-block one.** Unreal parity: `Set` ships exactly this
pass-through output.

⚠⚠ **Do NOT copy `SetSharedNode` blindly, even though it is the nearest precedent.** It builds the
`writtenResult` correctly (`:1238-1250`) but then caches it in **`_pinValueCache`** (`:1252-1253`) —
the **per-block** cache, *"cleared when starting each new block"* (`:176`). A `SetVariable` whose
consumer sits in the same block would work; put a `Branch` between them and the pull silently falls
back through `default:` to `0` again. `SetVariable` is **statement-scheduled**, so its value is
materialised once as a real local — which is precisely what `_statementPinCache` exists for
(`:178-190`), and why it is *"never cleared"*.

⇒ Take `SetSharedNode`'s **shape** (allocate a `ResultValue`, guard on the pin existing) but **not its
cache choice.** ⭐ **And `SetSharedNode.Written` is therefore defective in the same family** — see
item 3.

⚠ **Do not "fix" this by removing the pin.** The pin is right; the missing implementation is the bug.

**Test (headless, and it would have caught this):** `AuthoringPathRunValueTests` already has the
harness — wire `SetVariable.Value` → `Print String`, tick twice, assert the log reads **11** then
**22**, not `0`.

---

## 2. 🔴 Make `ResolveNodeOutput`'s `default:` arm **report**

`Stage5_Schedule.cs:3589`:

```csharp
default:
{
    // Unknown pure source -- dummy value.
    result = AllocValue(pinType);
    stmts.Add(new IrStatement {
        ResultValue = result,
        Operation   = new IrOp_Const("default", pinType),
        Debug       = new IrDebugAnnotation {
            Synthesized = $"unknown-source-{sourceNode.GetType().Name}",   // ⭐ it KNOWS
        },
    });
    break;
}
```

⭐ **It already computes the message and throws it away.** A diagnostic naming **node + pin + source
type** turns this whole family from silent-wrong-value into something a designer can act on.

⚠ **Sequencing matters — this is the one real trap in the batch.** Turning it on will fire for every
row item 3 finds. ⇒ **Land item 2 and item 3's fixes in the same commit**, or land item 2 last. Do
**not** land item 2 alone and leave the tree red.

📐 **Your call, and it is the Opus part:** `Error` or `Warning`. Consider that `Hrot.AI.Behaviors` sets
`TreatWarningsAsErrors`, so a `Warning` there is an `Error` anyway — **verify that before choosing**
(the coordinator got this exact fact wrong once). State the reasoning in the row.

---

## 3. 🟠 Complete the data-out audit

`ResolveNodeOutput` (`:2253-3610`) has **24** node cases. Only `FunctionCallNode` (impure, `:1642`),
`CallPeerBlueprintNode` (`:1711`) and `ScoreDecisionNode` (`:2158`) populate `_statementPinCache`,
which is the only other way a pull can be served across blocks.

**Six node types project a data-Out with no resolver case.** ⚠ **Only the first two are traced** — the
rest share the *static signature* and each needs the same **four**-step check before being called a
bug: projects a data-out? · resolver case? · `_statementPinCache` entry? · ⭐ **or a `_pinValueCache`
entry, which only works within the declaring block?**

| Node type | Status |
|---|---|
| `SetVariableNode` | 🔴 **confirmed — item 1.** No cache entry at all ⇒ always `default` |
| `SetSharedNode` (`Written`) | 🔴 **confirmed, and subtler.** It *does* cache (`:1252-1253`) — but into **`_pinValueCache`**, the **per-block** one. ⇒ correct in the declaring block, silently `false` across any `Branch`. **Same fix as item 1** |
| `SetComponentNode` · `CollectionWriteNode` · `ListWriteNode` | 🟠 same signature |
| `ComponentForEachNode` | ⚠ **may be served by loop lowering — check before claiming** |

⭐ **The `SetShared` row is why the four-step check exists.** A three-step check ("is it cached?")
passes it. The per-block/cross-block distinction is the whole defect, and it is invisible to any test
whose producer and consumer sit in one block — **so every test here needs a `Branch` between them.**

⚠ **Do not mass-fix on the signature alone.** A projected pin can be legitimately served elsewhere;
that is exactly why `ComponentForEach` is flagged. **Verdict per row, with the evidence.**

---

## 4. 🔴 `GraphKind.Macro` + the fail-loud net — **the same trap, one floor up**

📄 [Macro_Implementation_Design.md](Macro_Implementation_Design.md) · design closed by
[Q25](Architect_Question_25_Macros.md) · ⭐ **all three open restrictions ACCEPTED by the user
2026-08-10** (§7) — nothing is blocked.

Add the enum member to `{ Function, Event, Construction }` (`Assets/GraphTypes.cs:24`). It serialises
as a **string** (`BlueprintJsonServices.cs:26`), so it is additive on disk. **Then close the two
silent holes, before any expansion code exists** — that is the whole point of doing this first.

| Hole | Where | Why it is silent |
|---|---|---|
| `GraphKind → IrGraphKind` catch-all | ⚠ **`Stage5_Schedule.cs:4492-4498`** — `_ => IrGraphKind.Function` | a macro surviving expansion is emitted as a `Func_X` with **no diagnostic** — literally item 2's trap, in the graph-kind switch |
| tick-graph fallback | `InstanceEmitter.cs:81-82` — `?? FirstOrDefault(Kind == Function)` | a macro must never be eligible. Free under a separate enum member — but it needs an **explicit test** so a refactor cannot quietly undo it |

⚠ **The coordinator's earlier docs said `4311-4314`. That is stale** — the file moved under Batches
20–26. Verified `4492-4498` on this tree.

⚠⚠ **Wording is load-bearing, and it is the Opus part.** Phrase the guard as *"a macro graph reached
Stage 5 **as a compilation target**"*, **not** *"a Macro graph exists"*. A future macro-library asset
([Q25-C2](Architect_Question_25_Macros.md)) only *declares* macros; with no call sites they
legitimately reach Stage 5 unexpanded and must be **skipped, not errored**. Free to word right today,
expensive to unpick later.

📌 Diagnostic codes: `BP1650…BP1657` are taken. The design proposes `BP1660+`; **confirm nothing has
claimed them since** before using them.

---

## 5. 🧹 Dead anchors in the tracker

`Blueprint_Issues_Detail.md` stops at **BP-122**. Every row from **BP-123 onward** (Batches 26–27,
~20 rows) links to an anchor that does not exist. The tracker rows have absorbed the analysis in
practice — they now run 1000+ chars each.

⇒ **Either backfill the anchors or drop the links.** Your call which; say which and why. It is a live
wrong link in a document the user reads.

---

## 6. *(Only if there is room)* — the macro model delta, headless half only

**Model:** `ExecOutDecl` + `Graph.ExecOutputs` (⚠ a **new** list — `Graph.Outputs.Count` is
load-bearing arithmetic in four places, see the design's F5) · `MacroCallNode` carrying **exactly one
field**, `TargetGraphId` (F4 — everything else derived by projection, which is what makes it immune to
the `CallablePeers`/`ArgTypes` shape that has now bitten twice).

**Projections:** extend `EventEntryNodePins` and `ReturnNodePins` to accept `Macro` (F3 — reuse, do
**not** build new boundary nodes), with `ReturnNode`'s **N exec-in** pins being the only genuinely new
projection. ⚠ **Editor `NodePinSchema` and compiler `Stage0_Rehydrate` move together** — every batch
that touched one and not the other produced a silent shape mismatch.

⛔ **Not in this batch:** the expansion pass, the guard rails, the palette/My Blueprint gestures.
**Stop cleanly at a boundary** rather than half-landing any of them.

---

## 7. Gates

Same eight, `--logger "console;verbosity=normal"`.

**Baseline — coordinator-measured on the merged Batch-27 tree, 2026-08-10:**

| | |
|---|---|
| Solution build | **0 errors**, **77 total warnings** |
| ⚠ of which **BP diagnostics** | ⭐ **18 distinct** — **16×`BP3010`**, **2×`BP3011`** |
| Blueprints | **3091 / 0 / 10 skipped** |
| AiShared · BTree · Breakpoints | **1213 / 0** · **612 / 0** · **130 / 0** |
| NodeEdit Core · UI · Generators | **208 / 0** · **131 / 0** · **193 / 0** |

⚠⚠ ⭐ **Count them with `sort -u`, or you will double every number.** MSBuild prints each warning
**twice** — once in the build (`1>CSC : warning …`) and once in the end-of-build **summary block**. A
plain `grep -c` over the log therefore reports **36** where there are **18**. *Every warning count in
this programme's history — 34, then 36 — has been the doubled figure.* The relative movement was
still real; the absolute number was not.

⚠ **The 18 are EXPECTED, not a regression.** ✅ **BP-211's finding survives this correction**: before
BP-206 both `BP3011` messages were byte-identical so MSBuild merged them to one (×2 echo = the old
"2"); afterwards they name two different assets, so two distinct (×2 echo = the reported "4"). The
merge was real; the echo is separate and was hiding inside both figures.

**Do not fix or silence them** — triage is **Batch 29's**, deliberately not this batch's (D6, and see
below).

⚠ ⭐ **Item 2 will move this count.** That is the point — **report the new distinct number and its
composition**, do not silence it. A total-warning count is *not* a substitute: BP-211 proved that
measure hides merges, and the summary echo proves it also inflates.

📌 **Context for Batch 29, not work for this batch.** D6's two blockers are now both **cleared**, so
the triage is unblocked but deliberately out of scope here:
> `BP-206`'s `Asset ▸ Graph ▸ NodeKind` prefix fixed attribution — **and its *absent* third segment
> turned out to be the synthesized-node marker D6 was waiting for.** Verified: every kindless
> `BP3010` GUID appears in **no** asset file; every kind-bearing one appears in its asset.
> ⇒ **10 authored** orphans (2 assets: `InlineEd1 ▸ Tick`, `EnumDemo ▸ Main`) · **6 compiler-synthesized**.

⚠ Items here touch the compiler, the emitter **and** the editor projections — run all eight.

## 8. Reporting

Per-suite numbers · **the BP-warning count and its composition** · revert-goes-red per item ·
**every BP id you allocated** · the audit table with a verdict + evidence per row · anything here
wrong against the code.

⭐ **Item 1's method matters more than item 1.** Three sessions diagnosed the printed `0` wrong before
the asset arrived; what closed it was **computing the pin GUIDs**, about a minute of work that turns
*"probably a stale link"* into a yes/no. Use it on any report that names a pin GUID.
