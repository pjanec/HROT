# REPORT — Batch 76: **orthogonal regions work now** — one defect, four instances

> **Branch** `claude/hrot-implementation-j1jvin` · **base** `67eaada` *(dispatch `f2a4fc88b`)*
> ⭐ **Rule 1b honoured** — started-marker **`60791f3`**, pushed before any code.
> **Rule 7** re-synced at start · **rule 4** re-fetched before the final commit.
> ✅ **All three items landed.** ⭐⭐ **Item 1's defect had FOUR instances, not one.**

---

## 1. Gates — one row per gate, verbatim command, result

| gate | command | result |
|---|---|---|
| solution build | `dotnet build IOS-IG-SimHost.sln -t:Rebuild -v q --nologo` | ✅ **0 errors / 69 warnings** |
| ⭐⭐ **FastHSM** *(NEW — item 1's home)* | `dotnet test FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/*.csproj -v q --nologo` ⚠ **NO `--no-build`** | ⚠ **298 / 300** — **2 PRE-EXISTING reds**, §1a |
| Blueprints | `dotnet test …/Hrot.Blueprints.Tests.csproj --no-build -v q --nologo` | ✅ **3691 / 3681 / 0 / 10** |
| AiShared | `dotnet test …/Hrot.Editor.AiShared.Tests.csproj --no-build -v q --nologo` | ✅ **1289 / 1289 / 0 / 0** |
| BTree.Editor | `dotnet test …/Hrot.BTree.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **615 / 615 / 0 / 0** |
| Breakpoints | `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/*.csproj --no-build -v q --nologo` | ✅ **134 / 134 / 0 / 0** |
| Generators | `dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests/*.csproj --no-build -v q --nologo` | ✅ **266 / 266 / 0 / 0** |
| Hsm.Editor | `dotnet test …/Hrot.Hsm.Editor.Tests.csproj --no-build -v q --nologo` | ✅ **551 / 551 / 0 / 0** *(**+2**)* |
| AiEditor.Persistence | `dotnet test …/Hrot.AiEditor.Persistence.Tests.csproj --no-build -v q --nologo` | ✅ **136 / 136 / 0 / 0** |
| Examples.Scenarios | `dotnet test FDP/Examples/Fdp.Examples.Scenarios.Tests/*.csproj --no-build -v q --nologo` | ✅ **68 / 56 / 0 / 12 skipped** ⭐ **quarantine UNCHANGED at 12** |
| Examples.UrbanCombat | `dotnet test FDP/Examples/Fdp.Examples.UrbanCombat.Tests/*.csproj --no-build -v q --nologo` | ✅ **29 / 29 / 0 / 0** |
| Toolkits *(sample 1)* | `dotnet test …/Fdp.Toolkits.Tests.csproj --no-build -v q --nologo` | 🔴 **1** — `GizmoRegistryTests.SC_GZ004_2` |
| Toolkits *(sample 2)* | *same* | 🔴 **1** — ⭐ **`StatelessGizmoRegistryTests.SC_GZ022_2`, a DIFFERENT test** |
| ⭐⭐ **Toolkits *(`Gizmos` namespace)*** | `… --filter "…Gizmos"` | ✅ **187 / 187** ⇒ **`DEBT-AIB-030`** |
| NodeEdit Core | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/NodeEditor.Core.Tests.csproj -v q --nologo` | ✅ **208 / 208** ⭐ **no `--no-build`** |
| NodeEdit UI | `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -v q --nologo` | ✅ **131 / 131** ⭐ **no `--no-build`** |
| tracker | `python3 scripts/tracker-counts.py --check` | ✅ **open 63 / done 176 (+1 refuted)** |

🔴🔴 **Goldens:** ⛔ **blueprint set unmoved in every commit.** ⛔ **HSM set unmoved too** — §3 explains
why, and the handoff expected otherwise.

### ⚠⚠ 1a. **The FastHSM gate must NOT take `--no-build`, and finding that out cost me a false regression**

📐 **My first full run reported FastHSM at 3 red — one more than baseline, and the extra one was MY OWN
new test.** ⛔ **It was a stale binary, not a regression.**

📐 **`Fhsm.Tests` is not in `IOS-IG-SimHost.sln`** *(`grep -c "Fhsm.Tests" IOS-IG-SimHost.sln` → **0**;
only `Fhsm.Compiler` and `Fhsm.Kernel` are)*. ⇒ ⭐⭐ **`dotnet build <sln> -t:Rebuild` never builds it,
so `--no-build` then runs whatever was in its `bin` from some earlier session** — in my case a
`Fhsm.Kernel.dll` from before the fix.

⇒ ⭐⭐⭐ **It belongs in the same category as the two NodeEdit gates**, and for the same reason: ⛔ **out
of solution ⇒ the gate must build.** ⚠ **This is also HOW the suite stayed outside the gate set** — a
`--no-build` run against a stale bin can report anything.

⭐ **Verified after building the test project explicitly: 298 / 300**, i.e. my two new tests pass and the
two pre-existing reds remain.

### 📐 The two pre-existing FastHSM reds — **measured at `6a6bdc6`, before any change of mine**

| test | note |
|---|---|
| `OrthogonalRegionTests.OutputLane_Conflict_Detected` | ⭐ **carries its own explanation in-file**: *"SetTraceBuffer removed in behav-diag-1; trace tests now need `HsmTraceContext` rewrite (DEBT)"* ⇒ it asserts on a trace buffer nothing writes to any more |
| `FailSafeTests.InfiniteLoop_Detected_And_Stops` | ⚠ **unexplained** |

⛔ **Not adopted** — filed as **`BP-304`**. ⭐ Same shape as `BP-288`'s scenario suite, same lesson.

---

## 2. ⭐⭐⭐ Item 1 — **the region-0 hard-code, and it had FOUR instances**

### 🔴 Did the rail fail first? **Yes — and here it is**

```
Assert.Equal() Failure: Values differ
Expected: 7   (R1_B — where region 2 should have moved)
Actual:   6   (R1_A — where it stayed)
```

⭐ **The slot layout is recorded in the test rather than assumed**: slot 0 is the **root region** holding
the `Parallel` composite, and the two orthogonal regions occupy **slots 1 and 2**. ⚠ My first draft
assumed regions were slots 0 and 1 and asserted a precondition that was simply false — ⭐ **the dump
that corrected it is what the comment now preserves.**

⭐⭐ **The rail discovers the owning slot** rather than hard-coding it, and asserts **both** halves — the
mover moved **and** every other slot is untouched. ⛔ **Either alone passes a wrong fix**: mover-only
passes a fix that writes every slot; bystander-only passes one that writes none.

### ⭐ How the region index travels out of `SelectTransition`

⭐⭐ **It was already known and simply dropped.** `SelectTransition`'s loop is `for (int r = 0; r <
regionCount; r++)`, and the winner is chosen inside it — so the index existed at the moment of choice.
🛠 It is now returned via `out int regionIndex`, assigned **at the same statement that assigns
`bestTransition`**, so the two cannot disagree.

⛔ **NOT re-derived at the call site**, per the STOP: re-deriving *"which region does this transition's
source belong to"* would be a second home for a fact `SelectTransition` already holds.

⭐ **Global transitions keep region 0**, matching their existing `SourceStateIndex = activeLeafIds[0]`
convention — ⚠ a global belongs to no region, and changing that would be a behaviour change nobody
asked for.

### ⭐⭐⭐ Whether the neighbours have the same shape — **THREE MORE DO**

> 🔴 **The handoff asked about history-restore and the terminal check. The answer is worse than either.**

| site | verdict |
|---|---|
| ⛔ **`SaveHistory(…, activeLeafIds[0])`** | **SAME SHAPE** — recorded a **bystander region's** leaf as the exiting state's history. ⇒ a later history restore would return the wrong region's state |
| ⛔ **`RestoreHistory` → `SetActiveLeafId(…, 0, …)`** | **SAME SHAPE, at ALL THREE of its exits** *(deep, shallow-found, shallow-fallback)* |
| ⛔ **`RestoreDeepHistory`** | propagated the same hard-coded `0` into every nested restore |
| ✅ **the terminal-state check** | ⭐ **does NOT have it** — it reads `finalLeafId`, a local, not a slot |

⇒ ⭐⭐ **One defect, four instances**, all now taking the transitioning region.

### ⭐ Shipped behaviour

⛔ **Nothing changes.** ⭐ At `regionCount == 1` the selecting region **is** slot 0, so the fix is a
literal no-op — pinned by `ASingleRegionMachine_IsUnaffected`. 📐 Confirmed by the population the STOP
named: **Examples.UrbanCombat 29/29 and Examples.Scenarios unchanged at 56/68**, plus Blueprints and
Hsm.Editor green.

⭐ `ExecuteTransition`, `SaveHistory`, `RestoreHistory` and `RestoreDeepHistory` are **`private static`
inside `Fhsm.Kernel`** ⇒ ⛔ **no ABI concern**, unlike `E3`.

---

## 3. ⭐⭐ Item 2 — `BP-299`, and **the golden does not move**

🛠 `RegionNodeDto.OwnerStableId`, written from the model — ⭐ **the state whose `RegionNodes` contains
the region**, asked of the MODEL rather than derived from the initial child, ⚠ **which is exactly what
may be missing.**

⭐⭐ **The `InitialChild.Parent` derivation stays as the FALLBACK, and it is not tidy-up dead code:**
without it **every asset saved before this field loses its region dividers on the first load.** The
field is nullable and omitted when null ⇒ old files unchanged, new files a superset.

⭐ **The back-compat rail STRIPS the field from serialised JSON** rather than hand-building a DTO the
current mapper would never emit — ⭐ the honest representation of an old file.

### ⚠ What moved in `hsm-persistence-shape`: **nothing, and the handoff expected movement**

📐 That golden hashes the **checked-in `.hsm.json` files**, and no code path in this batch rewrites
them. ⇒ ⭐ **the field appears the first time an asset is saved from the editor**, not at build time.
📌 **Same structural reason as Batch 75's** — the golden is over the corpus on disk, not over mapper
output. ⚠ **Third batch running where an expected-movement prediction did not hold for this reason**;
worth folding into how the expectation is written.

---

## 4. ⭐ Item 3 — the deep walk, and **the cycle question named**

🛠 Rules 8 **and** 8b now walk descendants. ⭐⭐⭐ **The REGION INDEX still comes from the DIRECT CHILD**
and is carried down: ⛔ **reading `RegionIndex` off a deep descendant is the wrong space** — inside a
**nested** parallel composite that field means the **INNER** composite's region, so a descendant could
report region 0 while living in this composite's region 1.

### ⭐⭐ The cycle question — **named, per the STOP, and it splits in two**

| | |
|---|---|
| ⭐ **the walk I built** | over the **STATE TREE**, which is a tree ⇒ ⛔ **cannot cycle by construction, and no depth cap is wanted** |
| ⚠ **the visited set is NOT a depth cap** | it guards a **malformed model** *(hand-edited or corrupted parent/child wiring)* — ⭐ **a validator that hangs on bad input is worse than one that misses a case** |
| ⛔⛔ **the REAL cycle question, unanswered** | **asset `A` hosts `B` hosts `A`.** That is a walk over **ASSETS**, needs a resolver this validator does not have, and belongs to whoever builds subtree hosting for real. ⭐ **Named rather than half-handled** |

⭐ Gap test **INVERTED**, plus a negative arm — **two nested hosts in the SAME region stay legal**.
⛔ Without it, a walk that merely reported every host would pass the inverted test **while erroring on
sequential use**.

---

## 5. 🔴 Revert probes

| probe | result |
|---|---|
| **item 1** — the fix itself | 🔴 the rail fails **before** it: `Expected 7, Actual 6` |
| **P** — stop writing `OwnerStableId` | 🔴 reddens the inverted `BP-299` rail **and** the back-compat rail |
| **R** — walk the root only *(the old direct-children behaviour)* | 🔴 reddens `ANestedSubtreeHost_IsCaughtByRuleEight` |
| ⚠ **Q** — walk to depth 1 | ✅ **green, correctly**: the nested host **is** a depth-1 descendant of the direct child, so this probe was too shallow to break anything. ⭐ Recorded because a green probe that proves nothing is worth saying out loud |

---

## 6. ⭐ IDs allocated *(rule 5)*

| kind | allocated |
|---|---|
| tracker rows | ⭐ **`BP-301`** · **`BP-302`** · **`BP-303`** · **`BP-304`** *(OPEN)* |
| closed | ⭐ **`BP-299`** *(by `BP-302`)* |
| diagnostics · architect questions | ⛔ none |

⭐ **Started-marker sha *(rule 1b)*: `60791f3`.**

---

## 7. ⭐⭐ Debt rows touched

| row | what happened |
|---|---|
| ✅ **`DEBT-AIB-029`** | **resolved** — rules 8/8b walk descendants (§4) |
| ⚠ **`DEBT-AIB-030`** | 🔴 one red per full sample, **and the identity rotated AGAIN** — `SC_GZ004_2` then `SC_GZ022_2`, **within this batch**. ✅ Green across all 187 `Gizmos` tests. ⛔ **Not signal** |
| ⚠ **new, unfiled before** | the two `Fhsm.Tests` reds ⇒ **`BP-304`** |

---

## 8. Not done

⛔⛔ **`E3`** *(latent; blocked on the decision the coordinator owes the user)* · **`E7b`'s bytes**
*(same blocker)* · **`E5`** · **`E7a`** · ⚠ **`BP-304`'s two FastHSM reds** *(not adopted)* ·
⛔ **the asset-level subtree cycle** *(§4 — named, unbuilt)* · blueprint multi-occurrence
*(user-deferred)* · wiring the producer picker *(parked)* · the 12 quarantined scenario tests · the
Track C **visual check**.
