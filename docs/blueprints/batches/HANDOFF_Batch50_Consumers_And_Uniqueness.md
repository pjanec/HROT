# HANDOFF — Batch 50: ⭐⭐ **`U-11` + `U-14` — move the consumers, then make the names unique**

> 📌 **Dispatched at `94f01335`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1672+` is the next free diagnostic.
>
> 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md) §2 · `U-11`, `U-14` — **D3**.
> ⭐⭐ **Your own re-sequencing puts this batch on the critical path:** `U-11` → `U-12` → `U-10`'s wiring.
> ⇒ **`U-10` cannot be finished until this lands.**

---

## 0. ⭐ Why the pair, and why now

| | |
|---|---|
| **`U-11`** | ~34 semantic sites move off the three lists onto `Declarations`. ⭐ **The plan's own note: `U-4`/`U-5` already rewrote most of the scary file** |
| **`U-14`** | `BP-232` — `MakeUniqueName` checks `asset.Variables` **only**, so a `Parameter` and a `Variable` may both be `Health`. ⭐ *"Trivial AFTER `U-9`, awkward before"* — **one collection to check instead of three** |
| ⭐⭐ **`U-14` is the cheap TELL that `U-9` landed well** | ⚠ **if cross-kind uniqueness is still awkward here, the projections are hiding the model rather than presenting it.** ⛔ **That is a finding, not a workaround to route around** |

⚠ **`BP-232` is reachable, not theoretical:** `Stage5`'s **name fallback** searches
`Variables → WorkingState → Parameters`, so which `Health` a name-carrying node reaches is **list
order**. ⭐ **`U-3` fixed the resolution; `U-14` removes the ambiguity at its source.**

---

## 1. `U-11` — the consumers

### 1.1 Gates

| | |
|---|---|
| ⭐⭐ **Golden unchanged at EVERY sub-step** | ⛔ **not only at the end.** This is the gate that makes a 34-site sweep auditable |
| ⭐ **`persistence-shape.txt` unchanged** | ⚠ **this batch touches consumers, not storage** — ⛔ **if it moves, something wrote through that should not have** |
| 📐 **Shape** | ⭐ **one commit per bucket** — compiler stages · lowering · emit · editor — **so a regression bisects to a bucket.** The review ruled one batch, two sub-steps |

### 1.2 ⚠⚠ Two coordinator findings before you start — both from reading the tree today

**(1) `BlueprintVariablesWindow.cs` holds TWO things, and only one of them is slated for deletion.**

```
:14   BlueprintEditableAssetAdapter   — adapter
:45   BlueprintVariableSchemaSource   — ⭐ the source U-4/U-5 rebuilt. SURVIVES U-16
:377  BlueprintVariablesWindow        — ⛔ the standalone window U-16 RETIRES
```

⚠ **The plan says *"`BlueprintVariablesWindow` is a rewrite, not a line fix."*** ⭐ **That note predates
`U-4`/`U-5`, which already rewrote the source half — 458 lines total now.**
⇒ ⛔ **Do NOT rewrite the window.** `U-16` deletes it. 📐 **Do the minimum that keeps it correct and
compiling, and say what you left alone and why.** ⭐ **Rewriting code slated for deletion is the one
kind of waste this sequencing can still produce.**

**(2) The blast radius is wider than "~34 sites."** ⭐ **Coordinator-counted: 46 non-test files under
`Hrot.Blueprints.*` reference `.Parameters` / `.WorkingState` / `.Variables`.** ⚠ **Many are incidental
(`EventDispatcherDecl.Parameters` is a different `Parameters`), so 46 is an upper bound and ~34 may
well be the semantic count.** ⇒ 📐 **Report the real number once you have separated them** — ⛔ **and if
it is materially above 34, say so before sweeping, because the bucket split depends on it.**

---

## 2. `U-14` — `MakeUniqueName` across all kinds (`BP-232`)

| | |
|---|---|
| ✅ **Pass** | creating a `Variable` named `Health` when a `Parameter` `Health` exists is **refused** |
| ⭐⭐ **Graph locals stay OUT** | ⚠ **Batch 48 ruled this and it binds:** `Q27-C1` makes a local **legally shadow** an asset variable ⇒ ⛔ **folding locals in would point this rule at a space where duplicate names are the RULE** |
| 🔴 **Revert-goes-red** | restore the `Variables`-only check ⇒ the cross-kind test fails |

📌 **`U-5`'s refusal shape is the precedent** — the surface **says** it cannot, rather than accepting
and discarding. ⭐ **And Batch 43's duplicate-name guard is still sitting in the window's confirm path**
(recorded, not fixed). 📐 **If `U-14` makes the source the natural home, move it and say so** —
⛔ **but do not leave it in two places.**

---

## 3. Gates

**Baseline — coordinator-run on the merged Batch-49 tree (`3f8ad7b6`):**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3505 total / 3495 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐⭐ **Golden Tier 1 + Tier 2** | ⛔ **UNCHANGED — at every sub-step** |
| ⭐ **`persistence-shape.txt`** | ⛔ **UNCHANGED** |
| `tracker-counts.py --check` | clean **eighteen** batches running |

⭐ **Run the five `--no-build` suites in parallel; keep `\[FAIL\]` in the grep.**
⚠⚠ **The two NodeEdit gates take NO `--no-build`.**

---

## 4. ⚡ How to work

**You are on Opus.** ⭐ **Keep the bucket boundaries and every gate.** 🟢 **Sonnet fits a bucket's
mechanical sweep once you have fixed the pattern for that bucket** — ⛔ **never the bucket split, never
`U-14`'s rule, never a site where the move is not mechanical.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **`BP-232` closes with `U-14`** |
| ⚠ **Stop point** | ⭐ **at a bucket boundary, never mid-bucket.** `U-14` is small and independent — **if `U-11` runs long, ship the buckets you finished and say which** |

---

## 5. Reporting

Per-suite numbers · ⭐⭐ **golden unchanged at every sub-step, stated per bucket** ·
⭐ **`persistence-shape.txt` unchanged** · ⭐ **the REAL consumer count, semantic vs incidental** ·
⭐⭐ **what you left alone in `BlueprintVariablesWindow` and why** · **whether the duplicate-name guard
moved** · `tracker-counts.py --check` · ⭐ **every id you allocated** · ⭐ **where you stopped** ·
anything here **wrong against the code**.

⭐⭐ **Batch 49's best move was measuring the destructive change BEFORE making it** — canonicalisation
deletes anything the model does not carry, in 58 files at once, so every document was walked against
its canonical form **first**, and the two paths that vanish were **asserted** to be carried elsewhere
rather than assumed to be.

⚠ **`U-11` is the same shape at a different scale:** ⛔ **a consumer moved to a projection that does not
carry what it read is a silent behaviour change**, and ⭐ **the golden set only catches it if a shipped
asset happens to exercise that path.** ⇒ **Where a site reads something the projection might not carry,
say so — do not let the corpus's silence stand in for a check.**
