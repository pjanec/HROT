# HANDOFF — Batch 62: 🔴🔴🔴 **`BP-251` — the parameter slot has no bound**, then the rest of 61

> 📌 **Dispatched at `<stamped below>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ✅ **Batch 60 + 61 items 1–2 VERIFIED AND MERGED at `f5c1dd7c5`** — all gates coordinator-run, every
> claim checked. ⭐ **Your `W5` STOP was correct and it found something bigger than `W5`.**
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⛔ **Rule 3: the coordinator allocates no ids.** **You** allocate diagnostics and tracker rows.
> ⭐ **One commit per item.** ⭐ **Per-item STOP conditions again — they worked.**

---

## 0. Order — ⚠ **not severity order, and here is why**

| # | item | note |
|---|---|---|
| **0** | 📐 **`BP-251` REACHABILITY — measure only, report before fixing** | cheap, depends on nothing, ⭐ **and it may change the whole batch** |
| **1** | **`S2`** — struct size resolution | ⭐⭐ **moved AHEAD of the fix: it builds the size oracle `BP-251`'s gate needs** |
| **2** | 🔴🔴 **`BP-251`** — the parameter-slot bound | consumes `S2`'s oracle |
| **3** | **`W6`** → **`W7`** | not reached last run |
| **4** | ⚠ **the `Fdp.Toolkits.Tests` RACE** | ⛔ **not yours — see §5** |

---

## 1. 🔴🔴🔴 `BP-251` — coordinator-verified, and the mechanism is worse than "unbounded"

⭐ **Your finding, confirmed at the emitter.** `AiPrimitiveEmitter:305/:344` emit:

```csharp
ref var p = ref Unsafe.As<byte, Params>(
    ref bb.BehaviorParameters[paramIndex * Unsafe.SizeOf<Params>()]);
```

| | coordinator-measured |
|---|---|
| **the buffer** | `BehaviorParameters` is `fixed byte[MaxBehaviorParamByteSize]` = **100 bytes** |
| ⭐⭐ **what `paramIndex` IS** | ⛔ **not an authored field** — it is the **BTree node payload index**, supplied by the kernel per node (`NodeLogicDelegate.cs:11` *"Index for looking up parameters"*, `NodeDeactivatorDelegate.cs:14` *"Payload index of the node being deactivated"*) |
| 🔴🔴 **therefore** | ⭐ **the multiplier is HOW MANY TIMES THE TREE BINDS THAT PRIMITIVE.** A 40-byte `Params` at the **third** node reads/writes bytes **80..120** — ⛔ **twenty bytes into `SoftAdvice` and `Interrupt`** |
| ⛔ **why `FDP_001` does not catch it** | it bounds **one DTO** at 100 bytes ⇒ **only the `paramIndex == 0` case.** ⭐ *"Exactly the corruption its own message says it prevents."* |

### 0️⃣ ⭐⭐ FIRST — measure reachability, and report it before writing any fix

📐 **Does any shipped BTree topology bind a blueprint AiPrimitive at two or more nodes, and what is the
largest `sizeof(Params)` among those?** ⛔ **The answer does not change whether we fix it — it changes
whether this is a LIVE corruption or a loaded gun.** ⭐ **Say which, with the asset and node count.**

🛑 **STOP and report if it is LIVE in shipped content** — ⚠ **that is a different conversation** (it
implies deployed blackboards are already being corrupted) and the user needs it immediately, ahead of
any fix.

### ⭐⭐⭐ Where the bound goes — the precedent is YOUR OWN, from Batch 58

⭐ **You wrote it:** *"the two mechanisms are two different source generators over the same compilation,
and a generator cannot see another generator's output"* ⇒ **`W1` became an ANALYZER.**
⭐⭐ **`BP-251` is that shape exactly** — the blueprint compiler knows `sizeof(Params)` but not the
topology; the BTree generator knows the topology but not `sizeof(Params)`. ⇒ ⚖️ **Coordinator lean: an
analyzer over the FINAL compilation, where both halves are visible** — ⭐ **and that is why `S2` comes
first: the size oracle it builds is what the analyzer needs to compute `sizeof(Params)`.**

⚠ **A runtime bounds check is NOT sufficient alone** — it turns silent corruption into a late crash.
⭐ **If you add one as defence in depth, say so; do not let it replace the build-time gate.**

---

## 2. `S2` — struct size resolution *(unchanged from Batch 61 §5, and now load-bearing twice)*

🔴 An unregistered struct resolves at a **guessed 4 bytes**; `StaticTypeRegistry:75-81` hardcodes three
with hand-computed sizes and names its own gap. ⭐⭐ **`StructSizeResolver` already exists and is fully
general** — and the blueprint compiler never calls it.

| | |
|---|---|
| ✅ **no project cycle** | `Hrot.AiEditor.Generators` references Schema + Persistence, ⛔ **not the compiler** |
| ⚖️ **lean** | ⭐ **the existing `IClrSignatureResolver` seam, or move `StructSizeResolver` where both can see it** — ⛔ **not a new Compiler→Generators reference** (it drags Roslyn into a deliberately reflection-less compiler) |
| ⭐ **new reason to get the placement right** | **`BP-251`'s analyzer needs the same oracle** ⇒ **place it so an analyzer can reach it too** |
| **gate** | an unregistered user struct gets its **real** size · ⭐ **reuse Batch 60's `EmittedStateLayoutTests`, do not write a second gate** |
| 🛑 **STOP if** | a new Compiler→Generators project reference is the only workable placement — ⛔ **architecture change, comes to the user** |
| 🛑 **STOP if** | ⭐ **a shipped asset uses an unregistered struct** ⇒ its real size changes that asset's layout ⇒ **live wrong-layout defect, report it** |

---

## 3. `W6` → `W7` *(not reached last run — carried verbatim)*

| | |
|---|---|
| **`W6`** | guard read-only projection — `GetComponent` not `GetComponentRW`; `in`/`ref readonly` at the thunk boundary. ⭐ Invariant: *"a speculative evaluation may not be observable."* 📐 **Re-measure the `[SharedAiCondition]` usage count and state it** |
| **`W7`** | concurrent-region rule — error on concurrent **writers**, permit concurrent **readers**. ⚠ needs `W6` |
| 🛑 **STOP if** | the `[SharedAiCondition]` count is not ~0 · or `W6` did not land cleanly |

---

## 4. ⭐ What your `W5` STOP established — keep it, it is now a rail

✅ **`W5`'s dispatched work was already built** (`BTreeJsonGenerator:186-206` → `WouldOverflow` +
`BTREE0002`) ⇒ ⛔ **the premise *"each binding is checked alone"* was wrong for the managed path.**
⭐⭐ **And the constant is written down FOUR times, not two** *(plus a bare `100` in
`BlueprintVariablesWindow:414` the test cannot reach)*. 📐 **Fold that fifth one in if it is cheap —
`BlueprintVariablesWindow` is editor code and CAN reference the runtime constant.**

---

## 5. ⚠ The `Fdp.Toolkits.Tests` race — ⛔ **NOT yours, and it needs a row**

⭐ **Coordinator-measured on YOUR tree, three consecutive runs of the identical binary:**
**1 failure · 1 failure · 2 failures.** ⇒ ⛔ **not order-dependence — a RACE.**
`StatelessGizmoRegistryTests.SC_GZ022_2_Register_UnregisteredType_Throws` **passes in isolation**, and
⭐ **your diff touches nothing in `Fdp.Toolkits/` or gizmos.**

⚠⚠ **Be careful how this is recorded — the coordinator was:** I measured this suite green at
`bc79be664` in **one** run, ⛔ **and with a race, one green is not evidence of "pre-existing."**
⭐ **The honest claim is: a race in an assembly your diff does not touch. Races do not respect commit
boundaries.**

| | |
|---|---|
| 📐 **do** | file it, and **fix it if the cause is a shared static registry** *(the obvious suspect — xUnit parallelises by collection)* |
| ⭐ **why it matters beyond itself** | ⛔ **it undermines every gate in the programme.** ⚠ **This is the FIFTH order-dependent/racy result here** — `PdbEmbeddedSourceTests`, `RecipeIntegrityTests`, Batch 58's, and now twice this |
| 🛑 **STOP if** | the cause is **not** local to the test assembly — a race in **production** static state is a different and much larger finding |

---

## 6. Gates

**Baseline — coordinator-run at `f5c1dd7c5`:** build **0 errors / 69 warnings** · Blueprints
**3615 / 3605 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **196** ·
NodeEdit **208 / 131** · ⚠ **Toolkits 1942 with the §5 race, 1–2 failures per run.**

| | |
|---|---|
| 🔴🔴 **`StructureHash` unchanged for all 43** | ⚠ **`S2` is the item that could move it — if it does, STOP** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| ⭐ **golden Tier 1 unchanged** | Tier 2 movement **declared per item** |
| ⭐ **per-item revert-goes-red** | |
| `tracker-counts.py --check` | clean |

---

## 7. Reporting

⭐⭐⭐ **`BP-251` reachability FIRST — live or loaded gun, with the asset and node count** ·
⭐⭐ **where the bound went and why** · ⭐ **`S2`'s placement decision** · ⭐ **the `[SharedAiCondition]`
count** · ⭐ **the race's cause, or that you could not localise it** · 🔴 **`StructureHash` unchanged** ·
per-suite numbers **full and filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **The question to carry:** ⛔ **`FDP_001` bounds one DTO and its message claims it prevents the
corruption that `BP-251` performs.** 📐 **How many other gates in this repo check the SINGLE case of a
quantity the code then MULTIPLIES?** ⚠ **`W5`'s summed-budget premise was wrong because someone had
already generalised that one — and `BP-251` is the same shape, ungeneralised, one layer down.**
