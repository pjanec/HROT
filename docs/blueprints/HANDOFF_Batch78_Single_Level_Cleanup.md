# HANDOFF — Batch 78: **back to single level** — `BP-306` · `BP-307` · the `DEBT-AIB` pricing sweep

> 📌 **Dispatched at `f2b6ed471`.** Frozen per rule 1.
> ⭐⭐ **Rule 1b: push `chore: started batch 78 at <sha>` before writing any code.**
> ✅ **Batch 77 MERGED at `05317ff17`** — gates re-run by me; **FastHSM 300 / 300**.
> ⭐ **Rule 7 / Rule 4.** ⛔ **Rule 3: the coordinator allocates no ids.**
> ⭐ **One commit per item · per-item STOP conditions.**
>
> ⛔⛔ **USER RULING `2026-08-17` — MULTI-LEVEL IS PARKED.** ⭐ *"i would defer this idea for now and
> return to single level behaviors for a while in order to finish the planned work with variable
> unification and related ui changes."*
> ⇒ ⛔ **`E3` · `E5` · `E7a` · `Q36` · `Q37` · blueprint multi-occurrence are ALL OUT.**
> 📄 **`PLAN_Remaining_Work.md` §4C is the single-level queue.** ⭐ **This batch is three items from it.**

---

## 0. ⭐⭐⭐ Batch 77 — **your escalation was right, and the user's question broke my model**

| | |
|---|---|
| ⭐⭐⭐ **`E5`'s two blockers were upstream of my STOP** | *one brain per entity* and *resolve has no input for a BTree child*. ⭐ **`Q36` states both properly** — and the user has already approved `Q36-A` = **B** *(host ticks the child inline)* plus **a third `BrainTier` value for blueprints** |
| ⭐⭐ **the `E3` census correction** | *"zero **REGISTERED**, not zero **EMITTED**"* — `Fdp.Toolkits` **does** run the generator. ⭐ **And you built the tripwire better than I specified**: derived project set, Roslyn not grep, named baseline, **both directions**, a **set-non-empty guard**, and **shown failing on real code** |
| ⭐⭐⭐ **`BP-304`: both reds had ONE cause** | including the one I called unexplained — it failed at `Assert.True(traceData.Length > 0)` **one assertion before** it could speak. ⭐ **The RTC fail-safe always worked** ⇒ Batch 76 never implicated. ⛔ **And a third test was passing VACUOUSLY** against the same dead buffer — **seventh instance, never red, so nobody looked** |
| ⛔⛔ **and then the USER found what all of us missed** | with an HSM host ticking a BTree child, **both pack from offset `0` into the same 100-byte `BehaviorParameters`.** ⇒ 📄 **`Q37`**, parked with measurements. ⚠ **It corrected me a FOURTH time on `E3`** — rev 20's *"`E5`'s `E3` dependency was stale"* was reasoning about **state** storage and missed the **params base** |

---

## 1. 🔴🔴 `BP-306` — **`BTreeActionGenerator` emits non-compiling code.** ⭐ *And I found the lead*

> 📐 **Your measurement:** `FbtActionRegistrar.g.cs(162,54): error CS1666` the moment
> `Hrot.AI.Behaviors` gains its first `[SharedAiAction]` — while **`Fdp.Toolkits` compiles the same
> shape fine.** ⇒ **assembly-dependent.**
> ⭐⭐ **This matters beyond the error: it means the ONE generator-bearing assembly this programme owns
> cannot host a shared AI action today.**

### ⭐⭐ The lead — **two homes for one expression, and they disagree**

```csharp
// BTreeActionGenerator.cs:693, :706, :723   ← the ANALYZER
ref Unsafe.AddByteOffset(ref bb.BehaviorParameters,     (nint)<offset>)

// BTreeBridgeEmitCore.cs:513, :568, :666, :742   ← the PERSISTENCE emitter
ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0],  (nint)<offset>)
```

⭐ **`BehaviorParameters` is `fixed byte[100]`, and `CS1666` is exactly *"you cannot use fixed size
buffers contained in unfixed expressions"*.** ⇒ **the analyzer's copy is missing the `[0]` its sibling
has.** ⚠ **This is a LEAD, not a diagnosis — confirm it.**

📌 **A plausible explanation for "assembly-dependent", also to confirm:** the emitted lambda is generic
in the blackboard type `tb`. ⭐ **If `tb` is a GENERATED managed blackboard struct the member may not be
a fixed buffer at all; if it is `BrainBlackboard`, it is.** ⇒ the same text compiles in one assembly and
not the other **because the type substituted differs**, not because the assembly does.

### 🔴 STOP conditions

| | |
|---|---|
| ⭐⭐ **the rail must FAIL first** | ⛔ **a green test proves nothing here.** ⭐ **Add a `[SharedAiAction]` to a generator-bearing assembly and show the BUILD breaks before your fix** — the probe you already used for the tripwire |
| ⭐⭐⭐ **ONE home for the expression afterwards** | ⛔ **do not fix the analyzer's copy and leave two spellings.** ⚠ **The two-homes shape is what produced `E6`'s compound key AND `HsmActionKey`'s two spellings** — ⭐ **the third instance in this programme, so treat the duplication as the defect, not the character** |
| ⚠ **it touches the netstandard2.0 wall** | `Fdp.Toolkits.Analyzers` deliberately references nothing ⇒ ⭐ **a shared helper may not be reachable.** 🔴 **If the only honest answer is a MIRRORED constant with an agreement test** *(as `HsmActionKey.CompoundKeyName` did)*, ⭐ **say so — that is an accepted outcome and the precedent exists** |
| ⚠ **the tripwire baseline** | ⭐ **if you add a `[SharedAiAction]` anywhere, the Batch-77 tripwire fires by design.** ⛔ **Do not weaken it** — either keep the probe out of the committed tree, or move the entry into its baseline **with the reason** |

**rails:** ⭐ **the solution builds with a `[SharedAiAction]` present in a generator-bearing assembly**
· ⭐⭐ **the two emitters produce the same base expression, asserted against each other** *(⛔ not each
restated in its own test — that is how they drifted)*.

---

## 2. ⭐ `BP-307` — **a gate that tests a hand-written stub**

📐 **Coordinator-verified:**

```xml
<!-- FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj:25 -->
<ProjectReference Include="..\..\src\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

⛔ **`FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen` DOES NOT EXIST.** MSBuild prints *"Skipping project …
because it was not found"* and **succeeds** ⇒ ⭐⭐ **the suite's `SourceGen/*` tests exercise
`Helpers/GeneratedRegistrarStub.cs`, a HAND-WRITTEN file.**

⚠ **Same family as `BP-304` and the `--no-build` finding: a gate that reports on something other than
what it names.**

| | the decision, and it is yours to make with a measurement |
|---|---|
| **(a)** | **the generator was never built** ⇒ ⭐ **delete the dangling reference and RENAME the tests to say they cover a stub** — honest, and the stub keeps its value as a contract example |
| **(b)** | **it existed and was removed** ⇒ ⭐ **say what replaced it** *(the analyzer in `Fdp.Toolkits.Analyzers`?)* and point the tests there |

⭐⭐ **Check `.dev/` before deciding** — `2026-08-15`'s rule. ⛔ **Do not just delete the line**: if a
design record says the generator is planned, the reference is a **placeholder**, and the honest fix is
to say so in the csproj rather than remove the intent.

🔴 **STOP** if the `SourceGen/*` tests turn out to assert something the real analyzer contradicts —
⭐ **that is a finding, not a cleanup.**

---

## 3. ⭐⭐ The `DEBT-AIB` **pricing sweep** — ⛔ *price them, do not fix them*

⭐ **Four have closed since the partition was drawn** — `009` *(Batch 69)* · `021` *(70)* · `028` *(75/76)*
· `029` *(76)*. ⚠ **Thirteen have never been re-measured**, and some were filed many batches ago against
code that has since moved.

| bucket | rows |
|---|---|
| **parameter seam** | `001` · `002` · `008` · `011` |
| **parameter model** | `003` · `004` · `005` · `025` |
| **Track E** | `022` · `031` |
| **neither** | `010` · `023` · `024` |
| ⛔ **known, skip it** | `030` — the `Fdp.Toolkits.Tests` race, **seven tests, identity rotates**; ⭐ **nothing to price** |

### ⭐ What "pricing" means — **one line each, and a verdict**

| verdict | meaning |
|---|---|
| ⭐ **STILL REAL** | reproduced, with the file:line that shows it |
| ⭐⭐ **ALREADY FIXED** | ⚠ **say by which batch** — ⭐ **this is the likely majority and the most valuable outcome**, because a stale debt row costs a re-measurement every time someone reads it |
| **SUPERSEDED** | a later design changed the question ⇒ **name the design** |
| ⛔ **CANNOT REPRODUCE** | ⚠ **not the same as fixed** — say what you looked at |

⛔⛔ **DO NOT FIX ANY OF THEM in this batch**, however tempting. ⭐ **The deliverable is a table**, and
a fix hidden inside a sweep is a diff nobody reviewed for its own sake.
🔴 **STOP and report separately** if any row turns out to be a **live correctness defect** — ⭐ that
earns its own item in Batch 79 rather than a line in a table.

⭐ **Where they are filed:** `.dev/` reports and this repo's docs — ⚠ **`DEBT-AIB-012` is the cautionary
one**: it was *described and never filed*, and that id belongs to a **different, resolved** row.
⛔ **Do not assume a row exists just because an id is cited.**

---

## 4. ⛔ NOT in this batch

⛔⛔ **everything multi-level** — `E3` · `E5` · `E7a` · `E7b`'s bytes · `Q36`'s build · `Q37` ·
blueprint multi-occurrence · the producer picker's runtime *(⭐ single-level, but its own item)* ·
the 12 quarantined scenario tests · **the Track C visual check** *(⭐ the user's, not a batch's)*.

---

## 5. Gates

**Baseline — coordinator-verified at `05317ff17`:** build **0 / 69** · ⭐ **FastHSM 300 / 300** *(NO
`--no-build`)* · Blueprints **3691 / 3681 / 0 / 10** · AiShared **1289** · BTree.Editor **615** ·
Breakpoints **134** · Generators **268** · Hsm.Editor **551** · AiEditor.Persistence **136** ·
Examples.Scenarios **56 / 68 (12 skipped)** · Examples.UrbanCombat **29** · Toolkits **1964** ·
NodeEdit **208 / 131** · tracker **open 65 / done 178**.

| | |
|---|---|
| ⭐⭐⭐ **three gates take NO `--no-build`** | **NodeEdit ×2 and FastHSM** — ⛔ out of solution ⇒ the gate must build, or it reports a stale bin |
| ⭐⭐ **item 1 is a BUILD item** | ⇒ **the solution build IS its gate**, and ⚠ **`Fdp.Toolkits.Analyzers` changes affect every consuming assembly** — ⭐ **watch the generated-code golden tiers** |
| 🔴🔴 **the BLUEPRINT golden set MUST NOT MOVE** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` |
| ⚠ **expected movement** | ⭐ **I am not predicting.** Item 1 plausibly moves the **generated-code (BTree)** tier. **Report what moved and why** |
| ⚠ **the quarantine counts** | **12** scenario + **0** FastHSM *(Batch 77 fixed both as named gaps, not skips)*. ⛔ **A new skip is a finding** |
| **per-item revert-goes-red** · `tracker-counts.py --check` | |

---

## 6. Reporting

⭐⭐ **The gate table — one row per gate, verbatim command, result.**

**Per item:**
⭐⭐⭐ **item 1** — ⭐ **was the missing `[0]` the cause?** *(and if not, what was)* · ⭐ **why it was
assembly-dependent** · ⭐⭐ **how the two emitters now agree, asserted against each other** · what you
did about the tripwire baseline · ⭐ **did the build break BEFORE the fix?**
⭐ **item 2** — **(a) or (b)**, and ⭐ **what `.dev/` says** · whether the stub's assertions survive
contact with the real analyzer.
⭐⭐ **item 3** — ⭐⭐⭐ **the table: 13 rows, one verdict each**, and ⛔ **nothing fixed** · anything that
turned out live, escalated separately.
**Always:** ⭐ **the started-marker sha** · **every id you allocated** · **both quarantine counts**.

⭐⭐⭐ **Eleven batches. The last five each corrected a premise of mine, and Batch 77's came from the
USER** — the host/child params collision none of us had connected. ⭐ **That is the protocol working at
every level.** ⛔ **Keep stopping when a premise fails.**
