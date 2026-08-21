# HANDOFF — Batch 60 (`W2` + `W4`): ⭐⭐⭐ **the runtime layout gate, and the layout it exists to guard**

> 📌 **Dispatched at `3fef04c22`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch. ⭐ **Rule 4:** pull it again before your final commit.
> ⛔ **Rule 3: the coordinator allocates no ids.** **You** allocate diagnostics and tracker rows.
> ✅ **Batches 56 · 58 · 57 · 59 are VERIFIED AND MERGED at `bc79be664`** — all eight gates
> coordinator-run, every claim checked. See §0.

---

## 0. ⛔⛔ TWO SEQUENCING ERRORS, BOTH THE COORDINATOR'S — read this before §1

⭐ **You followed the handoffs correctly. The plan they sat in was wrong twice, and both are mine.**

| | |
|---|---|
| 🔴 **`W2` never got dispatched** | [`PLAN_Cross_Host_Sequencing.md`](PLAN_Cross_Host_Sequencing.md) §6 put `W2` **before** Batch 57, ⛔ **but I only ever dispatched 58 and 57** ⇒ **57 shipped its AiPrimitive descriptors without the corpus-wide gate that was supposed to precede them.** ⭐ **No harm done — you found the 8-byte rebase yourselves and asserted it directly** (§0.1) — but the general gate is still owed, and that is this batch |
| 🔴🔴 **`W2` and `W4` were listed as SEPARATE batches. They cannot be** | ⛔ **`W2` adds an asset whose whole purpose is to make the gate RED, and `W4` is what makes it green.** ⇒ **splitting them means merging a knowingly-red suite.** ⭐ **They are one batch, red-first INSIDE it** |

### 0.1 ⭐⭐ What you caught that I did not — recorded because it is the sharper half of my own §2

I checked `FieldLayout`'s `0 / 8 / 16` starts and reported *"they do not collide — not a defect."* ✅ **True,
and it missed the real point.** ⭐⭐⭐ **You found that for an AiPrimitive the `8` is a DIFFERENT KIND OF
NUMBER** — it is where working state sits inside `Blackboard1024`, past the stored hash — **while an
Instance's `16` is a genuine struct offset** (`State` opens with a 16-byte cursor). ⇒ **a descriptor
carrying the raw `IrField.Offset` would be read at `8 + OffsetBytes` and land 8 bytes late: plausible
bytes from the wrong place.** ⭐ **That is exactly the class `W2` exists to catch, found by reading
before `W2` existed.**

---

## 1. ⭐⭐ `W2` — the gate

**Assert at RUNTIME, for every corpus asset and every emitted field:**
`Marshal.OffsetOf<TState>(name) == descriptor.OffsetBytes` *(and the size beside it)*.

| | |
|---|---|
| ⛔⛔ **why golden cannot do this** | ⭐⭐ **Golden Tier 1 records the COMPUTED offset** (`GoldenCorpus:268`) ⇒ **both sides of the comparison come from the same source.** 🔴 **Tier 1 stays byte-identical while the real field moves.** ⚠ **Tier 1 green is NOT evidence in this batch** |
| ⭐ **cover BOTH tiers** | `State` (Instance) **and** `WorkingState` (AiPrimitive) — ⚠ **and the AiPrimitive side must account for Batch 57's 8-byte rebase**, or the gate will "fail" on correct code |
| 🔴🔴 **the corpus CANNOT prove it** | ⭐⭐⭐ **Coordinator-measured: ZERO shipped `.bp.json` declares a `Vector3`/`Vector2`/`Vector4`/`Quaternion` variable.** ⇒ **every corpus asset passes this gate today, and would pass it if the arithmetic were wrong.** ⛔ **`BP-240`'s lesson a FIFTH time: the constructed asset is the only witness** |

### ⭐ The asset to construct

**A `Vector3` after a `byte`** — the case the existing escape hatch cannot see:
`FieldLayout.TypeAlignment` is `SizeBytes switch { 1 => 1, 2 => 2, <= 4 => 4, _ => 8 }` ⇒ **a 12-byte
`Vector3` gets align 8**; ⛔ **the CLR packs it at 4.** 📌 **`PA-14`.**

---

## 2. ⭐⭐ `W4` — the fix, and **the reason it is safe**

| | |
|---|---|
| ⭐ **separate ALIGNMENT-reliability from `SizeReliable`** | ⛔ **`CSharpEmitter:412`'s hatch cannot fire for this class** — a `Vector3` has a **reliable size (12)** and an **unreliable alignment**. ⚠ **Batch 57 extended that predicate to the working-state path; you are now changing WHAT IT TESTS. Re-read your own 57 diff first** |
| ⭐ **explicit `[FieldOffset]` on the emitted struct** | so the emitted layout **is** the computed layout, rather than agreeing with it by luck |
| ⭐⭐⭐ **blast radius: ZERO** | **Coordinator-measured: no shipped asset carries an affected type** ⇒ ⛔ **no field moves, no `StructureHash` moves, and there is NO blackboard re-initialisation hazard.** ⭐ **This is the cheapest moment this change will ever have** |
| ⚠ **but it is NOT theoretical** | `Vector2/3/4`/`Quaternion` **are in the 18-member offerable set** ⇒ **a designer can declare one today and get a silently wrong layout.** ⭐ **`U-8` promises every offered type compiles; this is what makes the promise true** |
| ⚠ **rail** | refuse a **managed-typed** blackboard variable — `LayoutKind.Explicit` forbids overlapping managed references |
| ⛔ **NOT in this batch** | **`BP-247`** *(your CS0664 float-literal finding — next batch)* · `W5`/`W6`/`W7` · `S2`–`S5` · any panel work |

---

## 3. Gates

**Baseline — coordinator-run at `bc79be664`:** build **0 errors / 69 warnings** · Blueprints
**3583 / 3573 / 0 / 10** · AiShared **1216** · BTree **612** · Breakpoints **130** · Generators **194** ·
Toolkits **1942 / 1942 / 0** · NodeEdit **208 / 131**.

| | |
|---|---|
| ⭐⭐⭐ **`W2` RED before `W4`** | 📐 **State the failure text.** ⛔ **A gate that was never red is a gate nobody has tested** |
| 🔴🔴 **`StructureHash` unchanged for the existing 42** | ⛔ **the new asset is the 43rd. If any of the 42 moves, `W4` changed a shipped layout and the blast-radius measurement was wrong — STOP and report** |
| ⭐ **golden 42 → 43, declared** | ⚠ **Tier 1 gains one asset and no existing line changes.** ⛔ **Do not read Tier 1 green as evidence `W4` worked — see §1** |
| **`persistence-shape.txt`** | ⛔ **UNCHANGED** — this batch does not touch persistence |
| ⭐ **revert-goes-red** | revert `W4` alone ⇒ `W2` must redden again |
| `tracker-counts.py --check` | clean |

---

## 4. ⚡ How to work

**Opus.** ⭐ **Both halves are layout arithmetic; a plausible wrong answer looks exactly like a right one.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

---

## 5. Reporting

⭐⭐ **The RED text from `W2` before `W4`, quoted** · 🔴 **`StructureHash` unchanged for the existing 42,
stated FIRST** · ⭐ **whether any EXISTING asset's descriptors disagreed with `Marshal.OffsetOf` once the
gate could see them** *(that would be a live defect the gate just found, and it outranks the batch)* ·
⭐ **what you did to the `SizeReliable` predicate and how it interacts with your Batch 57 change** ·
`persistence-shape.txt` unchanged · per-suite numbers **full and filtered** · `tracker-counts.py --check` ·
⭐ **every id you allocated** (rule 5).

⭐⭐⭐ **The question to carry, from your own Batch 59:** ⛔ **`ActionTable[id] = action` was
last-writer-wins with no guard, and `ComputeHash` is used in at least six places.** 📐 **How many other
content-addressed id spaces in this repo register last-writer-wins?** ⚠ **`UT0103`/`UT0102`/`UT0150`
show the pattern was recognised once, in one family, and never generalised** — ⭐ **and `BHU_020` has now
made it two. What is the third?**
