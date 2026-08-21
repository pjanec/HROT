<!--STATUS
state: LIVE
updated: 2026-08-18
current-answer: §3 (the blocker) and §5 (the recommendation) — this is a STOP report
stale-below: nothing
known-rot: none
known-conflict: none
-->

# REPORT — Batch 85: ⛔⛔ **STOPPED. The tree is exactly as I found it.**

> 📌 **Started at `df0b6d4`** *(rule 1b marker)*, on dispatch `42b428d`, handoff ff-merged at `57f29fe`
> *(the rule-1a re-dispatch that moved the census to the coordinator)*.
> ⭐ **IDs allocated: `BP-331` `BP-332`.** ⛔ **No code shipped. `git status` is empty.**
> 📌 **The handoff's own instruction:** *"⭐ Stopping is a good outcome here"* · *"Landing NOTHING and
> reporting why beats landing it wrong"* · ⛔ *"Do not leave a half-collapsed enum — either the kind is
> merged and the hashes are identical, or the tree is exactly as you found it."*

---

## 1. ⭐⭐⭐ THE HEADLINE — **the collapse IS hash-neutral. It is PERSISTENCE that blocks it.**

| gate | result |
|---|---|
| ⭐⭐ **8 — `StructureHash` before/after** | ✅ **BYTE-IDENTICAL for all 43 compilable assets** |
| 🔴🔴 **round-trip** | ⛔ **15 `ByteStability_EveryFixture_SerializesToOriginalBytes` + `TheCanonicalJsonOfEveryCorpusAssetIsUnchanged` RED** |

⇒ ⭐⭐⭐ **The layout risk the whole batch was designed around — 📌 `R-24`'s hard reset — did not
materialise, and cannot.** ⛔ **What blocks the batch is a question the handoff did not ask: WHICH TAG
DOES A COLLAPSED DECLARATION SERIALIZE UNDER?**

---

## 2. ⭐⭐ WHAT I BUILT, AND WHY IT PROVES THE POINT

⭐ I completed the collapse far enough to **measure** it — 📐 **29 files modified, 1 added, the whole
solution building at 0 errors** — then reverted per §5. The work reached:

| ✅ done | |
|---|---|
| `DeclarationKind` → `{ Parameter, Variable }` | `Parameter` untouched *(📌 `R-08`)* |
| `VariableKind` → `{ Unresolved=0, Variable, Parameter }` | ⭐ `Unresolved = 0` kept, for its stated reason |
| `KindOrder` · `ResolutionOrder` · `Count` · the order-list drop | ⭐ **both order lists kept** — `ForgetOrder` drops an id from whichever holds it |
| all 22 production `Of(WorkingState)` sites | ⭐ **read one at a time**, ⛔ not sed'd; most were `.Concat` pairs that collapsed |
| `IrAsset.WorkingState` retired *(always empty)*, `Variables` carries the tier | ⇒ `StructureHashComputation`'s append sequence unchanged |
| `AiPrimitiveLowering`'s `__phase` / `__waitUntilTime` | ⭐ still APPEND, now to the one run — the same position they held |
| `FieldLayout` | ⭐ already kind-agnostic since Batch 56 — one layout run from one struct base |

### ⭐⭐ Two order questions, both solved — **worth keeping for the next attempt**

1. 🔴 **`GetOrdered` reorders by an order list and puts unlisted ids in a by-Id tail.** Feeding the
   merged run only `VariableOrder` would have moved every old-WorkingState field into that tail.
   ⇒ ⭐ **`ConcatOrder(WorkingStateOrder, VariableOrder)`** — the two lists concatenated in
   `KindOrder`'s old sequence. ⛔ Do not feed one; ⛔ do not sort.
2. 🔴 **Both property setters drive ONE run now**, and the deserializer sets both *(v2 is migrated
   **down** to the v1 three-list shape and bound to the properties)*. Plain `ReplaceWith` means the
   second setter **wipes** the first. ⇒ ⭐ **`DeclarationView.ReplaceSegment`**: `WorkingState` owns the
   leading segment, `Variables` the trailing one, order preserved for **any** setter order.

### ⚠⚠ And the gate caught a defect of MINE

📐 My first edit replaced `AppendFields(sb, asset.WorkingState)` with `StateDeclarations` **and left the
following `AppendFields(sb, asset.Variables)` line in place** ⇒ the state fields hashed **twice** ⇒
**24 of 43 hashes moved.** ⭐ **Gate 8 found it in one run.** ⛔ Had the batch relied on *"the goldens
did not move"*, the goldens would have moved too and the cause would have read as the collapse itself.
📌 **That is exactly why the handoff demanded computed hashes rather than golden stability.**

---

## 3. 🔴🔴 THE BLOCKER — **`BP-331`: a collapsed declaration has no tag to write**

📌 **The handoff's read-compat rule, §3:** *"Accept `"Kind": "WorkingState"` on READ, forever — it is
not a migration, it is an alias… do NOT rewrite the 37 affected assets in this batch."*

⭐⭐ **Reading is fine. WRITING is the problem.** 📐 With two kinds in the model there is **no
information left** to choose `"WorkingState"` when serializing ⇒ every previously-working-state
declaration re-serializes as `"Variable"`.

```
Serialization of 'InstanceCounter.bp.json' is not stable. Did pin hydration mutate the asset?
Serialization of 'MoveToAndFire.bp.json'  is not stable. …
```

⇒ ⛔ **the handoff's own sentence — *"assets adopt the new spelling naturally when next saved"* — IS a
rewrite**, deferred to whenever a designer next saves. ⚠ **And this repo gates byte-stability**, so the
deferral is not free: it is 15 red fixtures plus the canonical-JSON gate.

### ⭐ The three ways out, measured

| | option | cost |
|---|---|---|
| ⭐⭐ **a** | **Rewrite the 37 assets to `"Variable"` in the batch** | 📐 **37 SOURCE files, one word per declaration.** ⭐⭐ **ZERO hash movement and ZERO emit-golden movement — the tag is not in `StructureHash`.** ⚠ The handoff rejected this fearing *"458 files of golden movement"*; ⭐ **measured, it is 37, and none of them are goldens** |
| ⛔ **b** | Remember, per declaration, which tag it arrived under | ⛔ **that is keeping the kind under another name** — it defeats `R-01` |
| ⚠ **c** | Make byte-stability an EQUIVALENCE rather than byte equality | ⛔ weakens the gate that protects the persistence shape |

⇒ ⭐⭐⭐ **Recommendation: (a), and say so in the handoff.** It is the smallest change that keeps every
gate meaning what it means today.

---

## 4. ⚠⚠ `BP-332` — **the scope split in §3 is NOT ACHIEVABLE**

📌 **§3, out of scope:** *"the UI sections (My Blueprint still showing two) — that follows from the
kind, and it is a SEPARATE batch: this one must be provably behaviour-neutral."*

📐 **Measured: the two cannot be separated.** `BlueprintMyBlueprintModel:330` builds the Working State
section with `BuildDeclarationItems(DeclarationKind.WorkingState, …)`. Retire the member and the
section has **three** possible fates, and none is neutral:

| | |
|---|---|
| point it at `Variable` | ⛔ **the section shows the SAME rows as Variables** — duplicated rows in the outline |
| leave it sourceless | ⛔ permanently empty section with a `[+]` that adds to a *different* section |
| ⭐ **retire the section** | ⭐ the designed end state *(`R-01`)* — ⛔ **but visible, and it reddens ~37 model/section rails** |

⇒ ⭐ I took the third and it compiled cleanly — ⚠ **and that is precisely why the batch cannot be
"provably behaviour-neutral": the outline loses a section and its create command.**
⇒ ⭐⭐ **The next handoff must either take the UI change in scope, or say what the Working State section
becomes.**

---

## 5. ⭐ THE CENSUS — **conclusion CONFIRMED, denominator corrected**

📌 **`M-12` as dispatched:** *"541 .bp.json scanned… ASSETS DECLARING BOTH: 0."*
📐 **Re-run on SOURCE files only** *(the handoff invites re-running anything relied on)*:

```
100 SOURCE .bp.json scanned (0 unreadable)      ← 541 counts 441 obj/ and bin/ BUILD COPIES
    45  ()              30  ('Variable',)        9  ('Parameter',)
     9  ('Parameter','WorkingState')             7  ('WorkingState',)
ASSETS DECLARING BOTH WorkingState AND Variable: 0
```

⭐⭐ **The load-bearing conclusion holds — and holds *a fortiori*, since the duplicates are copies of
these same 100.** ⚠ **Only the counts were inflated**, and `"458 assets"` in gate 8 is not a real
population: ⭐ **43 assets actually compile** *(the generator's `AdditionalFiles` glob)*, and those are
the 43 gate 8 compared.

---

## 6. ⭐ THE GATE TABLE — **what I could measure before stopping**

| gate | `--no-build`? | result |
|---|---|---|
| `dotnet build IOS-IG-SimHost.sln` | — | ✅ **0 errors / 35 warnings** *(with the full collapse applied)* |
| ⭐⭐ **8 · `StructureHash` before/after, computed** | ✅ | ✅ **43/43 BYTE-IDENTICAL** |
| `Hrot.Editor.AiShared.Tests` | ✅ | ✅ **1397** — unchanged |
| `Hrot.AiEditor.Generators.Tests` | ✅ | ✅ **270** — unchanged |
| `Hrot.Blueprints.Tests` | ✅ | 🔴 **68 failed / 3692 passed / 10 skipped** |
| `Hrot.Blueprints.Compiler.Tests` | ✅ | 🔴 **1 failed / 2 passed** |
| ⭐⭐ **9 · the both-kinds rail** | — | ⛔ **NOT REACHED** — see §7 |
| **golden movement** | — | ⭐ **ZERO files written.** ⚠ The failures are **comparisons**, not rewrites: `Tier1_…MatchBaseline` ×12 and the byte-stability set report a difference; ⛔ nothing regenerated |
| **tree clean** | — | ✅ after every run, and ✅ **now — `git status` is empty** |
| **quarantine** | — | ✅ **12 scenario · 0 FastHSM**, unchanged |
| `tracker-counts.py --check` · `rulings-check.py` | — | ✅ **open 68 / done 197** · ✅ **57/57** |

### ⭐ The 68, grouped — ⛔ **none is pre-existing; all are the collapse**

| n | family | what it means |
|---|---|---|
| **15** | `ByteStability_EveryFixture_…` | 🔴 **the blocker** *(§3)* |
| **12** | `Tier1_StructureAndDiagnostics_MatchBaseline` | ⭐ **fields relabel `WorkingState:` → `Variables:`** — ⚠ **offsets and the hash line are IDENTICAL**; it is a report-section move, not a layout move |
| **3** | `Parse_AllDispatchKinds_RoundTrip` | same cause as the byte-stability set |
| **~37** | model / section / view rails | the three-kind model asserted directly *(`DeclarationSectionsTests`, `TaggedDeclarationTests`, `StoreFlipTests`, `MyBlueprintModel_Sections_FixedOrder`, …)* |
| **1** | `TheV2TagsAreExactlyDeclarationKindsMembersInOrder` | ⭐ **asserts the enum's names equal the on-disk tags IN ORDER** — ⛔ an equality the alias rule deliberately breaks. **It must become "every tag maps to a kind", and that is a decision, not a fix** |

---

## 7. ⛔ WHAT I DID NOT DO

| | |
|---|---|
| ⛔ **gate 9 — the both-kinds rail** | **not reached.** ⭐ The mechanism for it was built *(`ReplaceSegment` + `ConcatOrder`)* and reverted with everything else; ⚠ **an unrun rail is not evidence, so I claim nothing about the mixed-asset path** |
| ⛔ **rewrite the 37 assets** | the handoff forbade it — ⭐ **§3 recommends reversing that** |
| ⛔ **regenerate any golden** | ⚠ 12 Tier-1 baselines WOULD need it; ⛔ not mine to decide |
| ⛔ **`R-09`'s undeclared synthesized fields / shared state** | ⭐ **the merge makes neither worse** — `__phase` and `__waitUntilTime` keep their append position, and shared state is untouched |

---

## 8. ⭐ THE ONE-PARAGRAPH ANSWER

⭐⭐ **The collapse is safe where it was feared and blocked where it was not.** `R-24`'s wipe cannot
fire: layout has been kind-agnostic since Batch 56, and all 43 hashes are byte-identical. ⛔ **What
stops it is that two kinds cannot write three tags** — so every working-state declaration re-spells on
save, and this repo gates that. ⇒ ⭐ **Re-dispatch with (a) the 37-asset rewrite IN SCOPE** *(37 files,
one word each, zero hash and zero emit-golden movement)*, ⭐ **(b) the UI section retirement IN SCOPE**,
and ⭐ **(c) a ruling on `TheV2TagsAreExactlyDeclarationKindsMembersInOrder`**, whose equality the alias
rule is designed to break.
