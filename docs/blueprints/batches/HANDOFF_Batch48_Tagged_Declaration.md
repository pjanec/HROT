# HANDOFF — Batch 48: ⭐⭐ **`U-9` — the tagged declaration. ONE task, and it is the model change**

> 📌 **Dispatched at `d003f673`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1672+` is the next free diagnostic
> *(coordinator-verified: `BP1671` was allocated last batch)*.
>
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-9` — **D1**.
> ⚠⚠ **Another order swap, same reason.** `U-6`/`U-13`/`U-16` still hard-require the visual check
> (**twelve** batches now). ⭐ **`U-9` depends only on `U-3`, which is done**, so it runs headless today.

---

## 0. ⛔⛔ The one rule that keeps this task from becoming two

⭐⭐ **The tag must NOT reach JSON.** The serializer keeps writing the **old three-list shape**, byte
for byte. ⛔ **If the tag reaches persisted files, `U-9` and `U-10` collapse into one task and the
migrator loses its own revert.**

⇒ ⭐ **`U-9` is an entirely INTERNAL change.** Its whole external signature is *"nothing moved."**

---

## 1. What the model looks like today — measured

```csharp
// BlueprintAsset.cs:14-21
public List<ParameterDecl> Parameters   { get; set; } = new();
public List<Guid>?         ParameterOrder;
public List<VariableDecl>  WorkingState { get; set; } = new();
public List<Guid>?         WorkingStateOrder;
public List<VariableDecl>  Variables    { get; set; } = new();
public List<Guid>?         VariableOrder;
```

### ⚠⚠ The trap this task will hit — `ParameterDecl` and `VariableDecl` are NOT the same shape

| member | `VariableDecl` | `ParameterDecl` |
|---|---|---|
| `Id` · `Name` · `Type` · `DefaultValueJson` · `Tooltip` · `Comment` | ✅ | ✅ |
| ⛔ **`IsEditable`** | ✅ | ❌ |
| ⛔ **`IsExposedOnSpawn`** | ✅ | ❌ |
| ⛔ **`Category`** | ✅ | ❌ |

⇒ ⭐⭐ **A single tagged declaration has to reconcile two shapes, and the down-projection to
`ParameterDecl` DROPS three members.** 📐 **Decide and say which:**

| | |
|---|---|
| **(a)** | the three members are **meaningless for a parameter** ⇒ the projection drops them deliberately, **and Pass 2's reflection test is written to expect exactly those three** |
| **(b)** | they are **meaningful and simply absent** ⇒ `ParameterDecl` gains them, ⚠ **which is a persisted-shape change and belongs in `U-10`, not here** |

⚖️ **(a) is the lean** — `U-9` must not touch persistence. ⛔ **But an undeclared silent drop is the
defect shape this programme keeps finding.** ⭐ **Whichever you pick, the drop must be ENUMERATED in
code, not implicit in a mapping that forgot a line.**

---

## 2. Gates

| | |
|---|---|
| ✅ **Pass 1** | ⭐ **golden 42/42, both tiers — nothing has moved yet.** This is the whole point of `U-9` |
| ⭐⭐ **Pass 2** | a **reflection** test asserts every member of the new decl type is carried by **BOTH** projections — ⭐ **the `Graph_CopyShape_PreservesEveryMember` pattern, which has already caught one real miss.** ⚠ **Plus the §1 exclusions, named explicitly** |
| ✅ **Pass 3** | round-trip: `Serialize(Deserialize(j)) == j` for **all 42** ⚠ *(this is the tag-must-not-reach-JSON gate in disguise — if the tag leaks, this fails)* |
| 🔴 **Revert** | cheap — **no persisted change yet.** ⭐ **If your revert is NOT cheap, the tag reached persistence and §0 was violated** |

⚠ **Pass 2 is the one that matters.** ⛔ **A projection that quietly forgets a member is invisible to
the golden corpus** — no shipped asset need exercise it — ⭐ **exactly like `BP-226`, which sat behind
`BP1024`/`BP1031` for the same reason.**

**Baseline — coordinator-run on the merged Batch-47 tree (`d98b98bf`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3474 total / 3464 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden 42/42, both tiers** | ⛔ **unchanged** |
| `tracker-counts.py --check` | clean **sixteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

---

## 3. 📌 What `U-9` sets up, so the shape is chosen with it in view

| next | needs from `U-9` |
|---|---|
| **`U-15`** canonicalise the corpus | a serializer that is **provably** still writing v1 |
| **`U-10`** migrator pair + envelope | ⛔ **the tag reaching JSON — which is why it must NOT here** |
| **`U-11`** ~34 consumers move off the views | the views must **exist and be complete**, or the move is not mechanical |
| **`U-14`** `MakeUniqueName` across kinds (`BP-232`) | ⭐ *"trivial after `U-9`, awkward before"* — one collection to check instead of three |

⭐ **`U-14` is the cheap tell that `U-9` landed well:** if a `Parameter` and a `Variable` sharing a
name is still awkward to detect afterwards, the projections are hiding the model rather than
presenting it.

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ this one stays there.** ⭐ **This is the model change the whole `D` programme
rests on** — `.claude/CLAUDE.md`'s *"do novel scheduler/IR/compiler work hands-on."*
🟢 **Sonnet fits nothing here except possibly the reflection test's boilerplate.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours |
| ⚠ **Stop and report** | ⭐ **if the projections cannot be made complete without touching persistence.** That is a real finding and it re-cuts `U-9`/`U-10` — ⛔ **do not smuggle a persisted change in under "internal only"** |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **golden 42/42 both tiers, stated explicitly** · ⭐⭐ **that Pass 3 (round-trip)
proves the tag did NOT reach JSON** · `tracker-counts.py --check` · ⭐ **every id you allocated** ·
**your §1 (a)/(b) ruling and the enumerated drops** · ⭐ **where you stopped** · anything here **wrong
against the code**.

⭐⭐ **Three batches running, the best work has been noticing a silence:** Batch 46 found a gate that
did not move when it should have; Batch 47 answered the oracle question by **counting call sites**
rather than arguing it, and its restored `BP-87` lock found `System.String` in the picker on its first
run.

⚠ **`U-9`'s silence is Pass 2.** ⛔ **A forgotten member in a projection reddens nothing** — not the
golden corpus, not the round-trip, not the build. ⭐ **The reflection test is the only thing that can
see it, so write it before the projections, not after.**

📌 **And §1's shape asymmetry is a coordinator finding from reading `Declarations.cs` — it is not in
the plan.** ⭐ **If it is wrong against the code, say so;** Batch 45 and Batch 47 both refused a
coordinator claim and were right to.
