# REPORT — Batch 78: `BP-306` · `BP-307` · the `DEBT-AIB` pricing sweep

> 📌 **Started at `b068b08`** *(rule 1b marker, pushed before any code; dispatch sha `f2b6ed4` was
> already an ancestor)*.
> ⭐ **Rule 4:** re-pulled before the final commit — the coordinator had pushed the **Track C
> checklist** and the **78 → 79 → visual-check sequencing**; merged, and **nothing in it changes this
> batch's items**.
> ⭐ **IDs allocated: `BP-309`** *(the only new one; `BP-306` and `BP-307` were yours from Batch 77)*.
> ⭐ **`DEBT-AIB` rows touched: all thirteen PRICED, none changed** — `-030` excluded as instructed.
> ⭐ **Quarantine: 12 scenario · 0 FastHSM** — unchanged.

---

## 0. ⭐⭐ The headline

| | |
|---|---|
| ⭐⭐⭐ **`BP-306` was TWO defects, and the first was masking the second** | your `[0]` lead was right — ⛔ **and fixing it produced `CS0214`**, because the emitted class is not `unsafe` either |
| ⭐⭐ **the "assembly-dependent" cause is simpler than the generic-`tb` guess, and it is NOT the type** | ⛔ **`Fdp.Toolkits` emits NO registrar at all** — no `[BTreeAction]` ⇒ no group ⇒ early return |
| ⭐⭐ **ONE HOME was reachable — the mirror fallback was not needed** | a **linked source file** crosses the netstandard2.0 wall an assembly reference cannot. **Eleven** call sites, **three** spellings collapsed into one |
| ⭐⭐⭐ **`BP-307`'s STOP FIRED** | the stub keys on the **short name**; the successor keys on the **FQN** — ⚠⚠ **and it does so because of `E6(A)`, Batch 72, OUR change**, made while that suite was outside our gate set |
| ⚠ **and the dangling reference was in THREE csprojs** | **two of those projects do not build at all** → **`BP-309`** |
| ⭐ **the sweep: 6 of 13 are no longer live as filed** | and **`023`'s scoping is factually wrong** — it calls a method DEAD that three shipped assets reach |

---

## 1. ✅ `BP-306` — one home, and two defects

### 🔴 Did the build break BEFORE the fix? — **yes, and twice**

| # | error | what it was |
|---|---|---|
| ① | `FbtActionRegistrar.g.cs(162,54): CS1666` | ⭐ **your lead, confirmed:** `ref bb.BehaviorParameters` **without the `[0]`**, and `BehaviorParameters` is a `fixed byte[100]` |
| ② | `FbtActionRegistrar.g.cs(162,54): CS0214` | ⭐⭐ **appeared only once ① was fixed** — the emitted class is **not `unsafe`**, which a fixed buffer needs regardless of indexing. `HsmActionRegistrar` is unconditionally `unsafe`; `BTreeBridgeEmitCore` reaches the same place with a per-lambda `unsafe { }` block |

⛔⛔ **This is why the rail COMPILES the generated source instead of grepping it** — ⭐ **a text rail
would have been written against whichever error happened to be in front at the time**, and would have
gone green on a still-broken generator.

### ⭐⭐⭐ Why it was assembly-dependent — **not the type substitution**

📐 **Measured:** `Fdp.Toolkits` emits **no `FbtActionRegistrar.g.cs` at all.**

```csharp
// BTreeActionGenerator.Execute — mergedGroups is built ONLY from `registrable`,
// i.e. 4-param [BTreeAction] methods.
if (sharedAiMethods.Count > 0 && mergedGroups.Count > 0) { … }
if (mergedGroups.Count == 0) return;          // ⇐ Fdp.Toolkits exits HERE
```

⭐ `Fdp.Toolkits` has **four `[SharedAiAction]` and zero `[BTreeAction]`** ⇒ early return.
⭐ `Hrot.AI.Behaviors` has **both** *(`HillAttackTankNodes`)* ⇒ it emits, and broke.

⭐⭐ **And that early return is DELIBERATE, not a third defect** — the code says so in place:
*"the HSM generator handles these entries on the HSM side when no BTree context claims them."*
⇒ **checked before proposing anything about it**, per the `.dev/` rule.

### 🛠 ONE HOME — ⛔ *the mirror fallback was not needed*

| | |
|---|---|
| **what** | `BlackboardParamsExpression` — `Base(bb)` and `At(bb, offset)` |
| ⭐⭐ **how it crosses the wall** | compiled into `Fdp.Toolkits.Analyzers`, **`<Compile Link>`-ed** into `Hrot.AiEditor.Persistence`. ⭐ **Both are `netstandard2.0`**, so a linked source file works where an assembly reference must not go — ⛔ the analyzer is a Roslyn component and has no place in a shipped emitter's dependency graph |
| ⭐ **why no `CS0436`** | `internal` on **both** sides ⇒ a project referencing both assemblies *(which `Hrot.AiEditor.Generators.Tests` does)* imports neither |
| ⭐ **reach** | **11** sites — analyzer ×3, `HsmActionGenerator` ×2, `BTreeBridgeEmitCore` ×6 — collapsing **three** spellings: `bb.BehaviorParameters` · `…[0], (nint)` · `…[0], (IntPtr)` |
| ⚠ **`unsafe` is CONDITIONAL** | on there being a SharedAi entry ⇒ **every assembly with none keeps byte-identical output**, which is why no golden moved |

### ⭐⭐ How the two emitters now agree — **asserted against each other**

`TheAnalyzerAndTheBridge_EmitTheSameParamsProjection` extracts the projection from the **analyzer's
generated registrar** and from **`BTreeBridgeEmitCore` over the whole real BTree corpus**, normalises
the offset, and compares the sets. ⛔ **No literal is restated in the test** — restating it in two
places is exactly how the two drifted.

### ⭐ The tripwire baseline — **untouched**

The probe lives in a **synthetic compilation**, so no `[SharedAiAction]` is committed and Batch 77's
tripwire neither fires nor needed weakening. ⚠ **One existing rail did follow the expression:**
`HsmOccurrenceCollisionTests` read the literal out of the analyzer's SOURCE; it now reads the one home,
plus a new assertion that the generator still reaches it — ⛔ **the claim is unchanged, only its
address**.

---

## 2. ✅ `BP-307` — **verdict (b), and the STOP fired**

### 📐 What `.dev/` says — *(the `2026-08-15` rule paying off again)*

| record | |
|---|---|
| **`FastHSM/.dev-workstream/batches/BATCH-11-INSTRUCTIONS.md`** | *"**New Project:** `src/Fhsm.SourceGen/` (NEW)"* … *"**Create:** `src/Fhsm.SourceGen/HsmActionGenerator.cs`"* ⇒ ⭐ **it existed** |
| **`BATCH-11-REVIEW.md`** | confirms it shipped *(netstandard2.0, Roslyn component)* |
| ⭐⭐ **`.dev/_DONE/fluent-btree/ONBOARDING.md:88,103`** | **names the successor** — `HsmActionGenerator` ↔ `Fhsm.SourceGen`, and today the only `class HsmActionGenerator` in the repo is **`Fdp.Toolkits.Analyzers/HsmActionGenerator.cs`** |
| ⭐⭐ **`.dev/_DONE/blueprints-2/reports/BATCH-01-REPORT.md:70`** | *"Rather than skipping the `SharedAiGeneratorTests`, a hand-written stub … was created."* ⇒ ⛔ **the stub is a RECORDED DECISION, not an accident** |

### 🔴🔴 The STOP condition — **hit**

📐 **The stub's `Hash(name)` and `ActionDispatchTests.ComputeHash(name)` key on the SHORT method name.
`HsmActionKey.ForActionName` keys on the FULLY QUALIFIED name.**

⚠⚠ **And it does so because of `E6(A)` — Batch 72, OUR change** — made while this suite was outside our
gate set *(it entered at Batch 76)*. ⭐ **The stub was correct when written; we made it stale and could
not see it.**

⭐ **They are self-consistent, so nothing goes red** — they simply do not describe production dispatch.
⛔ **Reconciling them is FastHSM's call, not a side effect of a csproj tidy** ⇒ **no assertion touched.**

### 🛠 Done — documentation only

the dangling `ProjectReference` → a comment naming the successor, the design record and the divergence ·
the stub's header states all three · `SharedAiHsmTests` says in its own doc that it is a **contract
example, not coverage of emitted code**.
⭐⭐ **`Fhsm.Tests` stays 300 / 300 — which IS the proof the reference was inert**: removing it changed
nothing.

### ⚠ `BP-309` — filed, NOT adopted

📐 **The dangling reference was in THREE csprojs**, and the other two have no stub:

```
Fhsm.Demo.Visual      DemoApp.cs(68,13)            error CS0234: … 'Generated' does not exist
Fhsm.Examples.Console TrafficLightExample.cs(65,13) error CS0234: … 'Generated' does not exist
```

⛔ **Neither is in `IOS-IG-SimHost.sln`**, so `-t:Rebuild` has never touched them.
⭐⭐ **Third instance of one family in three batches:** `BP-304` *(a gate asserting on a dead buffer)* ·
`BP-307` *(a gate measuring a stub)* · **`BP-309` (code that is not a gate at all)**.

---

## 3. ✅ The `DEBT-AIB` pricing sweep — ⛔ *nothing fixed*

📄 **The table is [`DEBT_AIB_Pricing_Sweep_2026_08_17.md`](DEBT_AIB_Pricing_Sweep_2026_08_17.md)** —
13 rows, one verdict each, every one carrying the `file:line` or design reference it was measured from.

| verdict | rows |
|---|---:|
| ⭐⭐ **ALREADY FIXED** | **2** — `001` *(BATCH-01)* · `005` *(I4)* |
| ⭐⭐ **SUPERSEDED** | **3** — `004` *(S3 scopes)* · `010` *(into `030`, by its own text)* · `025` *(I4)* |
| ⭐⭐ **FOLD** | **1** — `024` into `023` |
| ⭐ **STILL REAL** | **6** — `002` · `003` · `008` · `011` · `023` · `031` |
| ⛔ **USER-DEFERRED** | **1** — `022` *(a decision, not debt)* |
| **CANNOT REPRODUCE** | **0** |

⚠ **Your prediction was that ALREADY FIXED would be the majority** — ⭐ **it is not, but six of thirteen
are no longer live as filed**, and the six that survive are smaller than their text suggests.

### ⛔ Two things a verdict column cannot hold

| | |
|---|---|
| ⛔ **`023` is factually WRONG** | it calls `CgfNodes.Action_HoldPosition` **DEAD**. 📐 It is not — `CgfNodes.cs:603`, live, `ref BrainBlackboard` — and `Action_Wander` *(`:416`)* is still bound by **`CombatShowcase`, `BTreeRenderShowcase`, `T04`** among others. ⭐⭐ **The row would have sent whoever picked it up to delete a method three shipped assets reach** |
| ⛔ **`025` is BOTH superseded and blocking, in ONE document** | `…Design_Status.md` §I4 *"(Supersedes the `DEBT-AIB-025` deferral.)"* vs §E2 *"blocked by I4; tracked `DEBT-AIB-025`"* ⇒ ⚠ **one is stale and the reader cannot tell which** |

⛔ **Neither is a live correctness defect**, so neither earns its own Batch-79 item under your STOP —
⭐ **both are documentation that would mislead the next reader**, which is what pricing is for.

---

## 4. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| ⭐ **solution** *(item 1's own gate)* | `dotnet build IOS-IG-SimHost.sln -t:Rebuild` | ✅ **0 errors / 69 warnings** |
| ⭐ **Generators** | `dotnet test …/Hrot.AiEditor.Generators.Tests.csproj` | ✅ **270 / 270** *(was 268 — the two `BP-306` rails)* |
| ⭐ **FastHSM** *(NO `--no-build`)* | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Fhsm.Tests.csproj` | ✅ **300 / 300, 0 skipped** |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build` | ✅ **3681 / 3691, 0 failed, 10 skipped** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build` | ✅ **1289** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build` | ✅ **615** |
| Breakpoints | `dotnet test …/Hrot.Diagnostics.Breakpoints.Tests.csproj --no-build` | ✅ **134** |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build` | ✅ **551** |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build` | ✅ **136** |
| Examples.Scenarios | `dotnet test …/Fdp.Examples.Scenarios.Tests.csproj --no-build` | ✅ **56 / 68, 12 skipped** |
| Examples.UrbanCombat | `dotnet test …/Fdp.Examples.UrbanCombat.Tests.csproj --no-build` | ✅ **29** |
| Toolkits | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build` | ✅ **1964** |
| NodeEdit Core *(NO `--no-build`)* | `dotnet test …/NodeEditor.Core.Tests.csproj` | ✅ **208** |
| NodeEdit UI *(NO `--no-build`)* | `dotnet test …/NodeEditor.UI.Tests.csproj` | ✅ **131** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 64 / done 180 (+1 refuted)** |

⭐ **`Fdp.Toolkits.Tests` was green this time** *(`DEBT-AIB-030` did not surface; Batch 77 saw it on the
second of two runs)* — ⛔ **which is not evidence either way**, per the standing rule.

### Golden movement — ⭐ **NONE**

| | |
|---|---|
| ⭐⭐ **nothing under `Snapshots/` changed** | `git diff --name-only b068b08..HEAD` lists no baseline — the only `Golden/` path in the diff is `HsmOccurrenceCollisionTests.**cs**`, a test file |
| ⭐ **and that is by construction** | the `unsafe` marker is **conditional on a SharedAi entry**, and no assembly that emits a registrar has one ⇒ **byte-identical output** |
| 🔴🔴 **the blueprint golden set** | `persistence-shape` · the 43 `Emit/*.cs.txt` · `StructureHash` — ⭐ **untouched.** ⚠ Those use a **different** emitter (`CSharpEmitter`, `global::System.Runtime.CompilerServices.Unsafe.AddByteOffset`) and were never in this diff's path |

**Quarantine:** **12** scenario · **0** FastHSM — ⭐ **unchanged, no new skip.**

---

## 5. Carried forward

⭐⭐ **Batch 79 is the WIRING batch** *(your sequencing, merged under rule 4)* — five of seven Track C
surfaces hosted by nothing; the checklist §2 is its acceptance list · **then** the visual check ·
**`BP-309`** *(filed, not adopted)* · the six `STILL REAL` debt rows and the two documentation
corrections above · the producer picker's runtime · the 12 quarantined scenario tests.
⛔⛔ **Everything multi-level stays parked** — `E3` · `E5` · `E7a` · `Q36` · `Q37` · blueprint
multi-occurrence.
